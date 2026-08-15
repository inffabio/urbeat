import argparse
import os
import time
import uuid

from import_common import fetch_json, ibge_state_code, snapshot_path, validate_uf
from neighborhood_snapshot import export_snapshot

IBGE_URL = "https://servicodados.ibge.gov.br/api/v1/localidades/estados/{}/municipios"
DISTRICTS_URL = "https://api.brasilaberto.com/v1/districts-by-ibge-code/{}"

def fetch_municipalities(uf):
    return fetch_json(IBGE_URL.format(ibge_state_code(uf)))

def districts_from_response(data):
    districts = data.get("result", data) if isinstance(data, dict) else data
    if isinstance(districts, dict):
        districts = districts.get("districts", [])
    return districts

def import_uf(uf, connection=None):
    api_key = os.environ.get("BRASIL_ABERTO_API_KEY")
    if not api_key:
        raise RuntimeError("Defina BRASIL_ABERTO_API_KEY antes de executar")
    uf = validate_uf(uf)
    cities_data = fetch_municipalities(uf)
    owns_connection = connection is None
    if owns_connection:
        from import_common import connect_database
        connection = connect_database()
    connection.autocommit = True
    cursor = connection.cursor()
    for city in cities_data:
        cursor.execute(
            'INSERT INTO "Cities" ("Id", "Name", "Uf", "IbgeCode", "CreatedAtUtc") VALUES (%s, %s, %s, %s, now()) ON CONFLICT DO NOTHING',
            (str(uuid.uuid4()), city["nome"], uf, str(city["id"])),
        )
    cursor.execute(
        'SELECT "Id", "Name", "IbgeCode" FROM "Cities" WHERE "Uf" = %s AND "IbgeCode" IS NOT NULL ORDER BY "Name"',
        (uf,),
    )
    cities = cursor.fetchall()
    total = 0
    for index, (city_id, city_name, ibge_code) in enumerate(cities):
        try:
            data = fetch_json(DISTRICTS_URL.format(ibge_code), {"Authorization": f"Bearer {api_key}"})
            count = 0
            for district in districts_from_response(data):
                name = district.get("name", district) if isinstance(district, dict) else str(district)
                if not name or len(name.strip()) < 2:
                    continue
                cursor.execute(
                    '''INSERT INTO "DeliveryNeighborhoods" ("Id", "CityId", "City", "Neighborhood", "NormalizedName",
                       "Latitude", "Longitude", "Source", "IsActive", "CreatedAtUtc")
                       VALUES (%s, %s, %s, %s, lower(unaccent(%s)), NULL, NULL, 'brasil_aberto', true, now())
                       ON CONFLICT DO NOTHING''',
                    (str(uuid.uuid4()), city_id, city_name, name.strip(), name.strip()),
                )
                count += 1
            total += count
            if (index + 1) % 20 == 0 or index == len(cities) - 1:
                print(f"  [{index + 1}/{len(cities)}] {city_name}: {count} bairros (total: {total})")
        except Exception as error:
            print(f"  [{index + 1}/{len(cities)}] {city_name}: ERRO - {error}")
        time.sleep(0.15)
    connection.commit()
    try:
        from geocode_via_cep import geocode_uf
        geocoded_total, missing_cep, missing_coordinates = geocode_uf(uf, connection=connection)
        snapshot = snapshot_path(uf)
        exported = export_snapshot(connection, uf, snapshot)
    finally:
        cursor.close()
        if owns_connection:
            connection.close()
    print(f"\n=== TOTAL: {total} bairros importados para {uf} ===")
    print(f"=== GEOLOCALIZACAO: {geocoded_total - missing_coordinates} geolocalizados; {missing_coordinates} pendentes ({missing_cep} sem CEP) ===")
    print(f"=== SNAPSHOT: {exported} bairros totais exportados para {snapshot} ===")
    return exported

def main(argv=None):
    parser = argparse.ArgumentParser(description="Importa bairros por UF via IBGE e Brasil Aberto")
    parser.add_argument("--uf", default="RJ", type=validate_uf)
    args = parser.parse_args(argv)
    import_uf(args.uf)

if __name__ == "__main__":
    main()
