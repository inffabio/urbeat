import argparse
import os
import sqlite3
import time
from pathlib import Path

from brasil_aberto_import import (
    DISTRICTS_URL,
    distinct_district_names,
    districts_from_response,
    fetch_municipalities,
)
from geocode_via_cep import (
    get_coordinates_from_cep,
    get_coordinates_from_cep_aberto,
    valid_coordinates,
)
from import_common import (
    fetch_json,
    normalize_ibge_code,
    normalize_neighborhood_name,
    snapshot_path,
    validate_uf,
)
from neighborhood_candidates import build_candidates
from neighborhood_snapshot import write_snapshot


def _normalize_cep(cep):
    if cep is None:
        return None
    digits = "".join(char for char in str(cep) if char.isdigit())
    return digits or None


def connect_dne():
    return sqlite3.connect(os.environ.get("URBEAT_DNE_DB", "/home/dexter/dne.db"))


def dne_columns(connection):
    rows = connection.execute("PRAGMA table_info(cep_unificado)").fetchall()
    return {row[1] for row in rows}


def municipality_lookup(municipalities):
    """Indexa municipios IBGE por nome normalizado e por codigo IBGE.

    Nomes normalizados que colidem entre municipios distintos sao removidos do
    indice por nome para evitar resolucao ambigua; a resolucao por codigo
    permanece sempre disponivel e nao e afetada.
    """
    by_name = {}
    ambiguous_names = set()
    by_code = {}
    for city in municipalities:
        name = str(city.get("nome") or city.get("name") or "").strip()
        code = normalize_ibge_code(city.get("id") or city.get("ibge"))
        if not name or not code:
            continue
        try:
            key = normalize_neighborhood_name(name)
        except ValueError:
            continue
        info = {"City": name, "CityIbgeCode": code}
        by_code[code] = info
        existing = by_name.get(key)
        if existing is None:
            by_name[key] = info
        elif existing["CityIbgeCode"] != code:
            ambiguous_names.add(key)
    for key in ambiguous_names:
        by_name.pop(key, None)
    return by_name, by_code


def read_dne_neighborhoods(connection, uf, lookup_by_name, lookup_by_code=None):
    """Le bairros distintos do e-DNE para uma UF, adaptando-se ao schema.

    Prefere as colunas `uf` e `municipio_cod_ibge`; quando presentes, o codigo
    IBGE e validado contra os municipios IBGE da UF (`lookup_by_code`), de modo
    que codigos desconhecidos ou de outra UF sao ignorados. Quando essas colunas
    estao ausentes (schema minimo de testes com `municipio`, `bairro`,
    `logradouro`, `cep`), resolve o codigo IBGE pelo nome do municipio via
    `lookup_by_name`, que so contem nomes inequivocos. Municipios que nao
    pertencem a UF ou nao constam no IBGE sao ignorados.
    """
    uf = validate_uf(uf)
    columns = dne_columns(connection)
    has_uf = "uf" in columns
    has_ibge = "municipio_cod_ibge" in columns

    if has_uf:
        if has_ibge:
            rows = connection.execute(
                "SELECT DISTINCT municipio_cod_ibge, municipio, bairro "
                "FROM cep_unificado WHERE upper(trim(uf)) = ? "
                "ORDER BY municipio, bairro",
                (uf,),
            ).fetchall()
        else:
            rows = connection.execute(
                "SELECT DISTINCT municipio, bairro "
                "FROM cep_unificado WHERE upper(trim(uf)) = ? "
                "ORDER BY municipio, bairro",
                (uf,),
            ).fetchall()
    else:
        rows = connection.execute(
            "SELECT DISTINCT municipio, bairro FROM cep_unificado ORDER BY municipio, bairro"
        ).fetchall()

    records = []
    for row in rows:
        if has_uf and has_ibge:
            ibge_raw, municipio, bairro = row
        else:
            municipio, bairro = row
            ibge_raw = None

        bairro = str(bairro or "").strip()
        if len(bairro) < 2:
            continue
        municipio = str(municipio or "").strip()

        if ibge_raw is not None:
            ibge = normalize_ibge_code(ibge_raw)
            if ibge is None:
                continue
            info = (lookup_by_code or {}).get(ibge)
            if info is None:
                continue
        else:
            if not municipio:
                continue
            try:
                key = normalize_neighborhood_name(municipio)
            except ValueError:
                continue
            info = lookup_by_name.get(key)
            if info is None:
                continue

        records.append({
            "Uf": uf,
            "CityIbgeCode": info["CityIbgeCode"],
            "City": info["City"],
            "Neighborhood": bairro,
            "Source": "dne",
            "IsActive": "true",
        })
    return records


def build_dne_street_index(connection, uf, lookup_by_name, lookup_by_code=None):
    """Constroi um indice em memoria de logradouro/CEP por bairro para uma UF.

    Le logradouros e CEPs relevantes do e-DNE em uma unica consulta, filtrando
    pela coluna ``uf`` quando presente (evitando carregar a base inteira) e
    validando o codigo IBGE contra os municipios IBGE da UF. O nome do bairro
    e normalizado e os logradouros sao agrupados por ``(CityIbgeCode,
    NormalizedName)``, mantendo o logradouro alfabeticamente menor e seu CEP.
    Retorna um dicionario mapeando essa tupla para ``(logradouro, cep)``.
    """
    uf = validate_uf(uf)
    columns = dne_columns(connection)
    has_uf = "uf" in columns
    has_ibge = "municipio_cod_ibge" in columns

    if has_uf:
        if has_ibge:
            rows = connection.execute(
                "SELECT municipio_cod_ibge, bairro, logradouro, cep "
                "FROM cep_unificado WHERE upper(trim(uf)) = ?",
                (uf,),
            ).fetchall()
        else:
            rows = connection.execute(
                "SELECT municipio, bairro, logradouro, cep "
                "FROM cep_unificado WHERE upper(trim(uf)) = ?",
                (uf,),
            ).fetchall()
    else:
        rows = connection.execute(
            "SELECT municipio, bairro, logradouro, cep FROM cep_unificado"
        ).fetchall()

    index = {}
    for row in rows:
        if has_uf and has_ibge:
            ibge_raw, bairro, logradouro, cep = row
            ibge = normalize_ibge_code(ibge_raw)
            if ibge is None:
                continue
            info = (lookup_by_code or {}).get(ibge)
            if info is None:
                continue
            city_ibge_code = info["CityIbgeCode"]
        else:
            municipio, bairro, logradouro, cep = row
            if not municipio:
                continue
            try:
                key = normalize_neighborhood_name(municipio)
            except ValueError:
                continue
            info = lookup_by_name.get(key)
            if info is None:
                continue
            city_ibge_code = info["CityIbgeCode"]

        if logradouro is None or str(logradouro).strip() == "":
            continue
        cep_normalized = _normalize_cep(cep)
        if not cep_normalized:
            continue
        if bairro is None:
            continue
        try:
            normalized_name = normalize_neighborhood_name(bairro)
        except ValueError:
            continue

        entry_key = (city_ibge_code, normalized_name)
        street = str(logradouro).strip()
        existing = index.get(entry_key)
        if existing is None or street.lower() < existing[0].lower():
            index[entry_key] = (street, cep_normalized)
    return index


def _dne_streets_for_city(connection, ibge_code, city_name):
    columns = dne_columns(connection)
    if "municipio_cod_ibge" in columns:
        if not ibge_code:
            return []
        return connection.execute(
            "SELECT logradouro, cep, bairro FROM cep_unificado "
            "WHERE CAST(municipio_cod_ibge AS TEXT) = ?",
            (str(ibge_code),),
        ).fetchall()
    if "municipio" in columns and city_name:
        return connection.execute(
            "SELECT logradouro, cep, bairro FROM cep_unificado WHERE municipio = ?",
            (city_name,),
        ).fetchall()
    return []


def get_first_street_from_dne_by_ibge(connection, ibge_code, normalized_name, city_name=None):
    """Escolhe o logradouro/CEP alfabeticamente menor do e-DNE para um bairro.

    A busca prioriza `municipio_cod_ibge` e usa o nome normalizado do bairro
    para casar variantes (acentos/caixa). Retorna `(logradouro, cep)` ou
    `(None, None)`.
    """
    matches = []
    for logradouro, cep, bairro in _dne_streets_for_city(connection, ibge_code, city_name):
        if logradouro is None or str(logradouro).strip() == "":
            continue
        cep_normalized = _normalize_cep(cep)
        if not cep_normalized:
            continue
        if bairro is None:
            continue
        try:
            if normalize_neighborhood_name(bairro) != normalized_name:
                continue
        except ValueError:
            continue
        matches.append((str(logradouro).strip(), cep_normalized))
    if not matches:
        return None, None
    matches.sort(key=lambda item: item[0].lower())
    return matches[0]


def geocode_candidates(candidates, connection, street_index=None):
    """Preenche coordenadas pendentes via e-DNE + Brasil Aberto CEP.

    Preserva candidatos que ja possuem um par valido de coordenadas e nunca
    sobrescreve valores existentes. Falhas de API sao tratadas por candidato,
    deixando o par vazio para o proximo passo de fallback configuravel.

    Quando o Brasil Aberto nao retorna um par valido, tenta o Cep Aberto
    (``CEP_ABERTO_API_TOKEN``), registrando ``cep_aberto``; sem token, mantem
    o candidato pendente sem erro.

    Quando ``street_index`` (resultado de ``build_dne_street_index``) e
    fornecido, o logradouro/CEP e resolvido em memoria, sem consultar o DNE por
    candidato; caso contrario, mantem o caminho legado por candidato via
    ``get_first_street_from_dne_by_ibge``.
    """
    geocoded = 0
    missing_cep = 0
    for candidate in candidates:
        if candidate["Latitude"] is not None and candidate["Longitude"] is not None:
            continue
        try:
            if street_index is None:
                _, cep = get_first_street_from_dne_by_ibge(
                    connection, candidate["CityIbgeCode"], candidate["NormalizedName"], candidate["City"]
                )
            else:
                _, cep = street_index.get(
                    (candidate["CityIbgeCode"], candidate["NormalizedName"]),
                    (None, None),
                )
        except Exception:
            cep = None
        if not cep:
            missing_cep += 1
            continue
        try:
            latitude, longitude = get_coordinates_from_cep(cep)
        except Exception:
            latitude = longitude = None
        source = None
        if valid_coordinates(latitude, longitude):
            source = "brasil_aberto_first_street"
        else:
            try:
                latitude, longitude = get_coordinates_from_cep_aberto(cep)
            except Exception:
                latitude = longitude = None
            if valid_coordinates(latitude, longitude):
                source = "cep_aberto"
        if source is not None:
            candidate["Latitude"] = latitude
            candidate["Longitude"] = longitude
            candidate["Source"] = source
            geocoded += 1
    pending = sum(
        1 for candidate in candidates
        if candidate["Latitude"] is None or candidate["Longitude"] is None
    )
    return geocoded, missing_cep, pending


def pending_report(candidates):
    pending = {}
    for candidate in candidates:
        if candidate["Latitude"] is None or candidate["Longitude"] is None:
            pending.setdefault(candidate["City"], []).append(candidate["Neighborhood"])
    return pending


def import_uf(uf, connection=None, directory=None):
    """Gera o CSV final de bairros por UF sem escrever no banco.

    Le bairros do e-DNE, complementa com municipios IBGE e bairros do Brasil
    Aberto, deduplica com `build_candidates`, geocodifica via CEP e grava o
    snapshot validado em `snapshots/bairros_<uf>.csv` (ou diretorio configuravel).
    """
    api_key = os.environ.get("BRASIL_ABERTO_API_KEY")
    if not api_key:
        raise RuntimeError("Defina BRASIL_ABERTO_API_KEY antes de executar")
    uf = validate_uf(uf)

    municipalities = fetch_municipalities(uf)
    lookup_by_name, lookup_by_code = municipality_lookup(municipalities)

    owns_connection = connection is None
    if owns_connection:
        connection = connect_dne()

    try:
        districts_by_code = {}
        cities = list(lookup_by_code.values())
        for index, info in enumerate(cities):
            code = info["CityIbgeCode"]
            try:
                data = fetch_json(
                    DISTRICTS_URL.format(code), {"Authorization": f"Bearer {api_key}"}
                )
                districts_by_code[code] = distinct_district_names(districts_from_response(data))
            except Exception as error:
                print(f"  [{index + 1}/{len(cities)}] {info['City']}: ERRO - {error}")
            time.sleep(0.15)

        records = read_dne_neighborhoods(connection, uf, lookup_by_name, lookup_by_code)
        dne_total = len(records)
        for code, info in lookup_by_code.items():
            for name in districts_by_code.get(code, ()):
                records.append({
                    "Uf": uf,
                    "CityIbgeCode": info["CityIbgeCode"],
                    "City": info["City"],
                    "Neighborhood": name,
                    "Source": "brasil_aberto",
                    "IsActive": "true",
                })
        brasil_aberto_total = len(records) - dne_total

        candidates = build_candidates(records)
        street_index = build_dne_street_index(connection, uf, lookup_by_name, lookup_by_code)
        geocoded, missing_cep, pending = geocode_candidates(candidates, connection, street_index)

        path = snapshot_path(uf, directory)
        write_snapshot(candidates, path)

        print(
            f"UF {uf}: {len(cities)} municipios; {dne_total} bairros DNE; "
            f"{brasil_aberto_total} bairros Brasil Aberto"
        )
        print(
            f"Candidatos unicos: {len(candidates)}; geolocalizados: {geocoded}; "
            f"pendentes: {pending} ({missing_cep} sem CEP)"
        )
        pending_by_city = pending_report(candidates)
        if pending_by_city:
            report = "; ".join(
                f"{city} ({len(names)}): {', '.join(names)}"
                for city, names in sorted(pending_by_city.items())
            )
            print(f"Bairros pendentes por municipio: {report}")
        print(f"CSV: {path}")

        return {
            "uf": uf,
            "municipalities": len(cities),
            "dne_records": dne_total,
            "brasil_aberto_records": brasil_aberto_total,
            "candidates": len(candidates),
            "geocoded": geocoded,
            "missing_cep": missing_cep,
            "pending": pending,
            "path": str(path),
        }
    finally:
        if owns_connection:
            connection.close()


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="Gera CSV final de bairros por UF a partir de DNE, IBGE e Brasil Aberto"
    )
    parser.add_argument("--uf", default="RJ", type=validate_uf)
    parser.add_argument(
        "--output", type=Path, default=None, help="Diretorio de saida (padrao: snapshots/)"
    )
    args = parser.parse_args(argv)
    import_uf(args.uf, directory=args.output)


if __name__ == "__main__":
    main()
