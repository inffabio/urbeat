import argparse
import csv
import math
import os
import tempfile
from pathlib import Path

from import_common import (
    IBGE_STATE_CODES,
    normalize_ibge_code,
    normalize_neighborhood_name,
    snapshot_path,
    validate_uf,
)


SNAPSHOT_COLUMNS = (
    "Uf",
    "CityIbgeCode",
    "City",
    "Neighborhood",
    "NormalizedName",
    "Latitude",
    "Longitude",
    "Source",
    "IsActive",
)

ESSENTIAL_FIELDS = ("Uf", "CityIbgeCode", "City", "Neighborhood", "NormalizedName")


def _is_empty(value):
    return value is None or str(value).strip() == ""


def _coordinate(value, field):
    if value is None or str(value).strip() == "":
        return None
    try:
        coordinate = float(value)
    except (TypeError, ValueError):
        raise ValueError(f"Coordenada {field} ausente ou invalida")
    if not math.isfinite(coordinate):
        raise ValueError(f"Coordenada {field} ausente ou invalida")
    limit = 90 if field == "Latitude" else 180
    if not -limit <= coordinate <= limit:
        raise ValueError(f"Coordenada {field} fora do intervalo valido")
    return coordinate


def normalize_coordinates(latitude, longitude):
    latitude = _coordinate(latitude, "Latitude")
    longitude = _coordinate(longitude, "Longitude")
    if (latitude is None) != (longitude is None):
        raise ValueError("Latitude/Longitude devem estar ambas preenchidas ou ambas vazias")
    return latitude, longitude


def validate_snapshot_rows(rows):
    coordinate_errors = {}
    essential_errors = {}
    normalized_errors = {}
    seen_keys = set()
    duplicates = []
    for row in rows:
        missing = [field for field in ESSENTIAL_FIELDS if _is_empty(row.get(field))]
        if missing:
            city = row.get("City") or "<municipio desconhecido>"
            neighborhood = row.get("Neighborhood") or "<bairro desconhecido>"
            essential_errors.setdefault(city, []).append(
                f"{neighborhood}: campos essenciais ausentes ({', '.join(missing)})"
            )
            continue

        city = row.get("City") or "<municipio desconhecido>"
        neighborhood = row.get("Neighborhood") or "<bairro desconhecido>"
        normalized = str(row.get("NormalizedName") or "").strip()

        try:
            uf = validate_uf(row.get("Uf"))
        except ValueError:
            essential_errors.setdefault(city, []).append(
                f"{neighborhood}: UF invalida '{row.get('Uf')}'"
            )
            continue
        ibge = normalize_ibge_code(row.get("CityIbgeCode"))
        if ibge is None:
            essential_errors.setdefault(city, []).append(
                f"{neighborhood}: codigo IBGE invalido '{row.get('CityIbgeCode')}'"
            )
            continue

        try:
            expected = normalize_neighborhood_name(neighborhood)
        except ValueError:
            expected = None
        if expected is not None and normalized != expected:
            normalized_errors.setdefault(city, []).append(
                f"{neighborhood}: NormalizedName '{normalized}' difere do esperado '{expected}'"
            )

        key = (
            uf,
            ibge,
            expected if expected is not None else normalized,
        )
        if key in seen_keys:
            duplicates.append(
                f"{neighborhood}: duplicado para ({key[0]}, {key[1]}, {key[2]})"
            )
        else:
            seen_keys.add(key)

        try:
            normalize_coordinates(row.get("Latitude"), row.get("Longitude"))
        except ValueError as error:
            coordinate_errors.setdefault(city, []).append(
                f'{row.get("Neighborhood") or "<bairro desconhecido>"}: {error}'
            )
    errors = []
    if coordinate_errors:
        report = "; ".join(
            f"{city} ({len(neighborhoods)}): {', '.join(neighborhoods)}"
            for city, neighborhoods in sorted(coordinate_errors.items())
        )
        errors.append(f"pares de coordenadas ausentes ou invalidos por municipio: {report}")
    if essential_errors:
        report = "; ".join(
            f"{city} ({len(neighborhoods)}): {', '.join(neighborhoods)}"
            for city, neighborhoods in sorted(essential_errors.items())
        )
        errors.append(f"campos essenciais ausentes por municipio: {report}")
    if normalized_errors:
        report = "; ".join(
            f"{city} ({len(neighborhoods)}): {', '.join(neighborhoods)}"
            for city, neighborhoods in sorted(normalized_errors.items())
        )
        errors.append(f"NormalizedName inconsistente por municipio: {report}")
    if duplicates:
        errors.append(
            f"duplicidades por (Uf, CityIbgeCode, NormalizedName): {', '.join(duplicates)}"
        )
    if errors:
        raise ValueError(f"Snapshot invalido: {'; '.join(errors)}")
    return rows


def write_snapshot(rows, path, validate=True):
    if validate:
        rows = validate_snapshot_rows(rows)
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = tempfile.NamedTemporaryFile(
        "w",
        encoding="utf-8",
        newline="",
        delete=False,
        dir=str(path.parent),
        prefix=path.name + ".",
        suffix=".tmp",
    )
    try:
        with temporary:
            writer = csv.DictWriter(temporary, fieldnames=SNAPSHOT_COLUMNS)
            writer.writeheader()
            writer.writerows({column: row.get(column, "") for column in SNAPSHOT_COLUMNS} for row in rows)
        if validate:
            read_snapshot(Path(temporary.name))
        Path(temporary.name).replace(path)
    except Exception:
        Path(temporary.name).unlink(missing_ok=True)
        raise


def read_snapshot(path):
    with Path(path).open("r", encoding="utf-8", newline="") as source:
        reader = csv.DictReader(source)
        missing = set(SNAPSHOT_COLUMNS) - set(reader.fieldnames or ())
        if missing:
            raise ValueError(f"CSV sem colunas obrigatorias: {', '.join(sorted(missing))}")
        return validate_snapshot_rows(list(reader))


def export_snapshot(connection, uf, path, validate=True):
    uf = validate_uf(uf)
    with connection.cursor() as cursor:
        cursor.execute(
            '''
            SELECT c."Uf", c."IbgeCode", c."Name", d."Neighborhood",
                   d."NormalizedName", d."Latitude", d."Longitude",
                   d."Source", d."IsActive"
            FROM "DeliveryNeighborhoods" d
            JOIN "Cities" c ON c."Id" = d."CityId"
            WHERE c."Uf" = %s
            ORDER BY c."Name", d."Neighborhood"
            ''',
            (uf,),
        )
        rows = [dict(zip(SNAPSHOT_COLUMNS, row)) for row in cursor.fetchall()]
    if validate:
        validate_snapshot_rows(rows)
    write_snapshot(rows, path, validate=validate)
    return len(rows)


def export_all_snapshots(connection, directory=None):
    totals = {}
    for uf in IBGE_STATE_CODES:
        totals[uf] = export_snapshot(connection, uf, snapshot_path(uf, directory))
    return totals


def restore_snapshot(connection, path, uf):
    uf = validate_uf(uf)
    rows = read_snapshot(path)
    restored = 0
    try:
        with connection.cursor() as cursor:
            for row in rows:
                row_uf = validate_uf(row["Uf"])
                if row_uf != uf:
                    raise ValueError(
                        f'Linha com UF inesperada: {row.get("City")}/{row.get("Neighborhood")} '
                        f'usa {row_uf}, esperado {uf}'
                    )
                ibge = normalize_ibge_code(row["CityIbgeCode"])
                cursor.execute(
                    '''
                    INSERT INTO "DeliveryNeighborhoods"
                        ("Id", "CityId", "City", "Neighborhood", "NormalizedName",
                         "Latitude", "Longitude", "Source", "IsActive", "CreatedAtUtc")
                    SELECT gen_random_uuid(), c."Id", %s, %s, %s,
                           NULLIF(%s, '')::double precision,
                           NULLIF(%s, '')::double precision,
                           NULLIF(%s, ''), %s::boolean, now()
                    FROM "Cities" c
                    WHERE c."Uf" = %s AND c."IbgeCode" = %s
                    ON CONFLICT ("CityId", "NormalizedName") DO UPDATE SET
                        "City" = EXCLUDED."City",
                        "Neighborhood" = EXCLUDED."Neighborhood",
                        "Latitude" = COALESCE("DeliveryNeighborhoods"."Latitude", EXCLUDED."Latitude"),
                        "Longitude" = COALESCE("DeliveryNeighborhoods"."Longitude", EXCLUDED."Longitude"),
                        "Source" = COALESCE(EXCLUDED."Source", "DeliveryNeighborhoods"."Source"),
                        "IsActive" = EXCLUDED."IsActive"
                    ''',
                    (
                        row["City"],
                        row["Neighborhood"],
                        row["NormalizedName"],
                        row["Latitude"],
                        row["Longitude"],
                        row["Source"],
                        row["IsActive"] or "true",
                        uf,
                        ibge,
                    ),
                )
                if cursor.rowcount == 0:
                    raise ValueError(
                        f'Municipio nao encontrado: {row["City"]} ({uf}/{ibge})'
                    )
                restored += 1
    except Exception:
        connection.rollback()
        raise
    connection.commit()
    return restored


def connect_from_environment():
    import psycopg2

    password = os.environ.get("URBEAT_DB_PASSWORD")
    if not password:
        raise RuntimeError("Defina URBEAT_DB_PASSWORD antes de executar")
    return psycopg2.connect(
        host=os.environ.get("URBEAT_DB_HOST", "localhost"),
        port=os.environ.get("URBEAT_DB_PORT", "5432"),
        database=os.environ.get("URBEAT_DB_NAME", "urbeatdb"),
        user=os.environ.get("URBEAT_DB_USER", "urbeatpostg"),
        password=password,
    )


def main(argv=None):
    parser = argparse.ArgumentParser(description="Exporta ou restaura snapshots CSV de bairros")
    parser.add_argument("action", choices=("export", "export-all", "restore"))
    parser.add_argument("--uf", type=validate_uf)
    parser.add_argument("--file", type=Path)
    args = parser.parse_args(argv)

    if args.action == "export" and not args.uf:
        parser.error("--uf e obrigatorio ao exportar")
    if args.action == "restore" and not args.uf:
        parser.error("--uf e obrigatorio ao restaurar")
    if args.action in ("export", "restore") and not args.file:
        parser.error("--file e obrigatorio")

    connection = connect_from_environment()
    try:
        if args.action == "export":
            total = export_snapshot(connection, args.uf, args.file)
            print(f"Exportados {total} bairros de {args.uf} para {args.file}")
        elif args.action == "export-all":
            totals = export_all_snapshots(connection)
            total = sum(totals.values())
            print(f"Exportados {total} bairros em {len(totals)} UFs")
        else:
            total = restore_snapshot(connection, args.file, args.uf)
            print(f"Restaurados {total} bairros de {args.file}")
    finally:
        connection.close()


if __name__ == "__main__":
    main()
