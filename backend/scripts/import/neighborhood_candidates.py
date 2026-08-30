import argparse
import csv
from pathlib import Path

from import_common import (
    candidate_key,
    candidate_path,
    normalize_ibge_code,
    normalize_neighborhood_name,
    validate_uf,
)
from neighborhood_snapshot import SNAPSHOT_COLUMNS, normalize_coordinates, write_snapshot

CANDIDATE_COLUMNS = SNAPSHOT_COLUMNS

ESSENTIAL_FIELDS = ("Uf", "CityIbgeCode", "City", "Neighborhood")


def _is_empty(value):
    return value is None or str(value).strip() == ""


def _as_bool(value, default=True):
    if value is None or str(value).strip() == "":
        return default
    return str(value).strip().lower() in ("true", "1", "sim", "yes", "t", "y")


SOURCE_PRIORITY = {
    "dne": 40,
    "brasil_aberto": 30,
    "brasil_aberto_cep": 30,
    "brasil_aberto_first_street": 30,
    "openstreetmap": 20,
    "osm_nominatim": 20,
    "nominatim": 20,
}


def source_priority(source):
    """Retorna a prioridade deterministica de uma origem; maior vence em duplicatas.

    A e-DNE (`dne`) e a origem mais confiavel para o nome exibido e por isso
    vence o Brasil Aberto; as origens geograficas de fallback tem prioridade
    menor. Origens desconhecidas ou vazias recebem a prioridade minima.
    """
    normalized = (source or "").strip().lower()
    return SOURCE_PRIORITY.get(normalized, 0)


def _merge_candidate(existing, record):
    """Funde um registro duplicado no candidato existente de forma deterministica.

    Nome exibido e municipio seguem a origem de maior prioridade (DNE vence
    Brasil Aberto). Coordenadas validas sao preservadas e, quando ambas as
    origens tem coordenadas, prevalece a de maior prioridade. O campo `Source`
    reflete a origem da coordenada quando o candidato possui coordenadas; sem
    coordenadas, reflete a origem da lista/nome. O candidato fica ativo se
    qualquer origem for ativa.
    """
    merged = dict(existing)

    existing_has_coordinates = existing["Latitude"] is not None and existing["Longitude"] is not None
    record_has_coordinates = record["Latitude"] is not None and record["Longitude"] is not None

    record_wins_name = source_priority(record["Source"]) > source_priority(existing["Source"])
    if record_wins_name:
        merged["Neighborhood"] = record["Neighborhood"]
        merged["City"] = record["City"]

    record_wins_coordinates = record_has_coordinates and (
        not existing_has_coordinates
        or source_priority(record["Source"]) > source_priority(existing["Source"])
    )
    if record_wins_coordinates:
        merged["Latitude"] = record["Latitude"]
        merged["Longitude"] = record["Longitude"]
        merged["Source"] = record["Source"]
    elif existing_has_coordinates:
        merged["Source"] = existing["Source"]
    elif record_wins_name:
        merged["Source"] = record["Source"]

    if record["IsActive"]:
        merged["IsActive"] = True
    return merged


def build_candidates(records):
    """Normaliza e deduplica registros de bairros por (UF, codigo IBGE, nome normalizado).

    Cada registro de entrada precisa de `Uf`, `CityIbgeCode`, `City` e
    `Neighborhood`. Latitude e Longitude, quando presentes, devem formar um par
    completo (ou ambos vazios); pares parciais ou invalidos sao rejeitados.
    Duplicatas sao resolvidas por prioridade deterministica de origem (ver
    `source_priority`): a origem de maior prioridade define nome exibido e
    Source, e as coordenadas validas sao preservadas, preferindo a origem de
    maior prioridade quando ambas as tem.
    Retorna a lista ordenada por (Uf, CityIbgeCode, NormalizedName).
    """
    candidates = {}
    for record in records:
        uf = validate_uf(record.get("Uf"))
        ibge = normalize_ibge_code(record.get("CityIbgeCode"))
        city = str(record.get("City") or "").strip()
        name = str(record.get("Neighborhood") or "").strip()
        missing = [
            field for field, value in (
                ("Uf", uf), ("CityIbgeCode", ibge), ("City", city), ("Neighborhood", name)
            ) if _is_empty(value)
        ]
        if missing:
            raise ValueError(f"Campos essenciais ausentes: {', '.join(missing)}")
        key = candidate_key(uf, ibge, name)
        latitude, longitude = normalize_coordinates(record.get("Latitude"), record.get("Longitude"))
        source = str(record.get("Source") or "").strip()
        active = _as_bool(record.get("IsActive"))

        candidate = {
            "Uf": uf,
            "CityIbgeCode": ibge,
            "City": city,
            "Neighborhood": name,
            "NormalizedName": key[2],
            "Latitude": latitude,
            "Longitude": longitude,
            "Source": source,
            "IsActive": active,
        }

        existing = candidates.get(key)
        candidates[key] = candidate if existing is None else _merge_candidate(existing, candidate)
    return [candidates[key] for key in sorted(candidates)]


def read_records(path):
    with Path(path).open("r", encoding="utf-8", newline="") as source:
        reader = csv.DictReader(source)
        missing = set(ESSENTIAL_FIELDS) - set(reader.fieldnames or ())
        if missing:
            raise ValueError(f"CSV sem colunas obrigatorias: {', '.join(sorted(missing))}")
        return list(reader)


def write_candidates(rows, path):
    write_snapshot(rows, path)
    return len(rows)


def group_candidates_by_uf(candidates):
    grouped = {}
    for candidate in candidates:
        grouped.setdefault(validate_uf(candidate["Uf"]), []).append(candidate)
    return grouped


def export_candidates_by_uf(candidates, directory=None):
    totals = {}
    for uf, rows in group_candidates_by_uf(candidates).items():
        totals[uf] = write_candidates(rows, candidate_path(uf, directory))
    return totals


def _collect_input_files(path):
    path = Path(path)
    if path.is_file():
        return [path]
    if path.is_dir():
        files = sorted(path.glob("*.csv"))
        if not files:
            raise ValueError(f"Nenhum CSV encontrado em {path}")
        return files
    raise ValueError(f"Entrada nao encontrada: {path}")


def main(argv=None):
    parser = argparse.ArgumentParser(description="Gera candidatos de bairros por UF a partir de registros brutos")
    parser.add_argument("action", choices=("generate",))
    parser.add_argument("--input", type=Path, required=True, help="CSV ou diretorio com CSVs de registros brutos")
    parser.add_argument("--output", type=Path, default=None, help="Diretorio de saida (padrao: candidates/)")
    parser.add_argument("--uf", type=validate_uf, default=None, help="Restringe candidatos a uma UF")
    args = parser.parse_args(argv)

    records = []
    for path in _collect_input_files(args.input):
        records.extend(read_records(path))
    candidates = build_candidates(records)
    if args.uf:
        candidates = [candidate for candidate in candidates if candidate["Uf"] == args.uf]
    totals = export_candidates_by_uf(candidates, args.output)
    for uf in sorted(totals):
        print(f"{uf}: {totals[uf]} candidatos -> {candidate_path(uf, args.output)}")
    print(f"Total: {len(candidates)} candidatos em {len(totals)} UFs")


if __name__ == "__main__":
    main()
