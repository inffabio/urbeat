import argparse
import os
import time
import uuid

from import_common import fetch_json, ibge_state_code, normalize_neighborhood_name, snapshot_path, validate_uf
from neighborhood_snapshot import export_all_snapshots

IBGE_URL = "https://servicodados.ibge.gov.br/api/v1/localidades/estados/{}/municipios"
DISTRICTS_URL = "https://api.brasilaberto.com/v1/districts-by-ibge-code/{}"

OFFICIAL_SOURCES = frozenset(
    (
        "brasil_aberto",
        "brasil_aberto_cep",
        "brasil_aberto_first_street",
        "osm_nominatim",
        "openstreetmap",
    )
)

LEGACY_IMPORT_ENV = "URBEAT_ALLOW_LEGACY_DB_IMPORT"


def legacy_import_allowed():
    return os.environ.get(LEGACY_IMPORT_ENV, "").strip().lower() == "true"


def ensure_legacy_import_allowed():
    """Bloqueia o importador legado por padrao e orienta o novo fluxo.

    O importador legado so escreve no banco com opt-in explicito
    (``URBEAT_ALLOW_LEGACY_DB_IMPORT=true``) e confirmacao inequivoca; nunca por
    padrao. O fluxo recomendado e gerar o CSV final com
    ``neighborhood_source_import.py`` e aplica-lo com ``neighborhood_rebuild.py``.
    """
    if legacy_import_allowed():
        return
    raise RuntimeError(
        "O importador legado (brasil_aberto_import.py) nao escreve mais no banco "
        "por padrao. Use o novo fluxo: gere o CSV final com "
        "`neighborhood_source_import.py --uf <UF>` e aplique no banco com "
        "`neighborhood_rebuild.py --uf <UF> --file snapshots/bairros_<uf>.csv "
        "--confirm \"REBUILD <UF>\"`. Para manter a compatibilidade legada, defina "
        "URBEAT_ALLOW_LEGACY_DB_IMPORT=true e confirme explicitamente com "
        "--confirm \"LEGACY IMPORT <UF>\"."
    )


def fetch_municipalities(uf):
    return fetch_json(IBGE_URL.format(ibge_state_code(uf)))

def districts_from_response(data):
    districts = data.get("result", data) if isinstance(data, dict) else data
    if isinstance(districts, dict):
        districts = districts.get("districts", [])
    return districts


def is_manual_record(source, city_id):
    return source is None and city_id is None


def is_official_record(source, city_id):
    return city_id is not None or source in OFFICIAL_SOURCES


def distinct_district_names(districts):
    names = []
    seen = set()
    for district in districts:
        name = district.get("name", district) if isinstance(district, dict) else str(district)
        name = (name or "").strip()
        if len(name) < 2:
            continue
        try:
            key = normalize_neighborhood_name(name)
        except ValueError:
            continue
        if not key or key in seen:
            continue
        seen.add(key)
        names.append(name)
    return names


def replace_city_neighborhoods(connection, city_id, city_name, district_names):
    """Substitui os bairros oficiais de um municipio pela lista atual da API.

    Preserva bairros manuais/customizados (Source nulo e CityId nulo) e nunca
    remove nada quando a resposta da API esta vazia. A operacao roda dentro de
    uma transacao por municipio: em caso de erro nada e alterado. Retorna
    (adicionados, removidos).
    """
    names = distinct_district_names(district_names)
    if not names:
        return 0, 0

    new_keys = {normalize_neighborhood_name(name) for name in names}
    protected_keys = set()
    official_by_key = {}

    cursor = connection.cursor()
    added = 0
    removed = 0
    try:
        cursor.execute(
            '''SELECT "Id", "CityId", "Neighborhood", "NormalizedName", "Source"
               FROM "DeliveryNeighborhoods"
               WHERE "CityId" = %s OR ("CityId" IS NULL AND "City" = %s)''',
            (city_id, city_name),
        )
        for row_id, row_city_id, neighborhood, normalized, source in cursor.fetchall():
            key = normalize_neighborhood_name(normalized)
            if is_official_record(source, row_city_id):
                official_by_key[key] = row_id
            else:
                protected_keys.add(key)

        for name in names:
            key = normalize_neighborhood_name(name)
            if key in protected_keys:
                continue
            if key in official_by_key:
                cursor.execute(
                    '''UPDATE "DeliveryNeighborhoods"
                       SET "CityId" = %s, "City" = %s, "Neighborhood" = %s,
                           "Source" = 'brasil_aberto', "IsActive" = true
                       WHERE "Id" = %s''',
                    (city_id, city_name, name, official_by_key[key]),
                )
            else:
                cursor.execute(
                    '''INSERT INTO "DeliveryNeighborhoods"
                       ("Id", "CityId", "City", "Neighborhood", "NormalizedName",
                        "Latitude", "Longitude", "Source", "IsActive", "CreatedAtUtc")
                       VALUES (%s, %s, %s, %s, lower(unaccent(%s)), NULL, NULL, 'brasil_aberto', true, now())
                       ON CONFLICT ("CityId", "NormalizedName") DO NOTHING''',
                    (str(uuid.uuid4()), city_id, city_name, name, name),
                )
            added += 1

        for key, row_id in official_by_key.items():
            if key not in new_keys:
                cursor.execute(
                    'DELETE FROM "DeliveryNeighborhoods" WHERE "Id" = %s',
                    (row_id,),
                )
                removed += 1

        connection.commit()
    except Exception:
        connection.rollback()
        raise
    finally:
        cursor.close()
    return added, removed


def import_uf(uf, connection=None, confirmation=None):
    ensure_legacy_import_allowed()
    api_key = os.environ.get("BRASIL_ABERTO_API_KEY")
    if not api_key:
        raise RuntimeError("Defina BRASIL_ABERTO_API_KEY antes de executar")
    uf = validate_uf(uf)
    expected = f"LEGACY IMPORT {uf}"
    if confirmation != expected:
        raise RuntimeError(
            f'Confirme o importador legado explicitamente: confirmation deve ser '
            f'exatamente "{expected}" (recebido: {confirmation!r}).'
        )
    cities_data = fetch_municipalities(uf)
    owns_connection = connection is None
    if owns_connection:
        from import_common import connect_database
        connection = connect_database()
    connection.autocommit = False
    cursor = connection.cursor()
    try:
        for city in cities_data:
            cursor.execute(
                'INSERT INTO "Cities" ("Id", "Name", "Uf", "IbgeCode", "CreatedAtUtc") VALUES (%s, %s, %s, %s, now()) ON CONFLICT DO NOTHING',
                (str(uuid.uuid4()), city["nome"], uf, str(city["id"])),
            )
        connection.commit()
    finally:
        cursor.close()
    cursor = connection.cursor()
    try:
        cursor.execute(
            'SELECT "Id", "Name", "IbgeCode" FROM "Cities" WHERE "Uf" = %s AND "IbgeCode" IS NOT NULL ORDER BY "Name"',
            (uf,),
        )
        cities = cursor.fetchall()
    finally:
        cursor.close()
    total = 0
    removed_total = 0
    for index, (city_id, city_name, ibge_code) in enumerate(cities):
        try:
            data = fetch_json(DISTRICTS_URL.format(ibge_code), {"Authorization": f"Bearer {api_key}"})
            added, removed = replace_city_neighborhoods(
                connection, city_id, city_name, districts_from_response(data)
            )
            total += added
            removed_total += removed
            if (index + 1) % 20 == 0 or index == len(cities) - 1:
                print(f"  [{index + 1}/{len(cities)}] {city_name}: {added} bairros (total: {total})")
        except Exception as error:
            print(f"  [{index + 1}/{len(cities)}] {city_name}: ERRO - {error}")
        time.sleep(0.15)
    try:
        from geocode_via_cep import geocode_uf
        geocoded_total, missing_cep, missing_coordinates = geocode_uf(uf, connection=connection)
        connection.commit()
        totals = export_all_snapshots(connection)
    finally:
        if owns_connection:
            connection.close()
    exported = totals[uf]
    print(f"\n=== TOTAL: {total} bairros importados para {uf} ===")
    print(f"=== REMOVIDOS: {removed_total} bairros oficiais obsoletos ===")
    print(f"=== GEOLOCALIZACAO: {geocoded_total - missing_coordinates} geolocalizados; {missing_coordinates} pendentes ({missing_cep} sem CEP) ===")
    print(f"=== SNAPSHOT: {exported} bairros em {uf}; snapshots gerados para {len(totals)} UFs ===")
    return exported

def main(argv=None):
    parser = argparse.ArgumentParser(description="Importa bairros por UF via IBGE e Brasil Aberto (legado)")
    parser.add_argument("--uf", default="RJ", type=validate_uf)
    parser.add_argument("--confirm", help='Digite exatamente "LEGACY IMPORT <UF>" para confirmar o fluxo legado')
    args = parser.parse_args(argv)
    ensure_legacy_import_allowed()
    expected = f"LEGACY IMPORT {args.uf}"
    if args.confirm != expected:
        parser.error(f'--confirm deve ser exatamente "{expected}"')
    import_uf(args.uf, confirmation=args.confirm)

if __name__ == "__main__":
    main()
