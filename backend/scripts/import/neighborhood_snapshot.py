import argparse
import csv
import math
import os
from pathlib import Path

from import_common import validate_uf


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


def validate_snapshot_rows(rows):
    pending = {}
    for row in rows:
        try:
            latitude = _coordinate(row.get("Latitude"), "Latitude")
            longitude = _coordinate(row.get("Longitude"), "Longitude")
            if (latitude is None) != (longitude is None):
                raise ValueError("Latitude/Longitude devem estar ambas preenchidas ou ambas vazias")
        except ValueError as error:
            city = row.get("City") or "<municipio desconhecido>"
            pending.setdefault(city, []).append(
                f'{row.get("Neighborhood") or "<bairro desconhecido>"}: {error}'
            )
    if pending:
        report = "; ".join(
            f"{city} ({len(neighborhoods)}): {', '.join(neighborhoods)}"
            for city, neighborhoods in sorted(pending.items())
        )
        raise ValueError(f"Snapshot invalido: pares de coordenadas ausentes ou invalidos por municipio: {report}")
    return rows


def write_snapshot(rows, path):
    rows = validate_snapshot_rows(rows)
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="") as output:
        writer = csv.DictWriter(output, fieldnames=SNAPSHOT_COLUMNS)
        writer.writeheader()
        writer.writerows({column: row.get(column, "") for column in SNAPSHOT_COLUMNS} for row in rows)


def read_snapshot(path):
    with Path(path).open("r", encoding="utf-8", newline="") as source:
        reader = csv.DictReader(source)
        missing = set(SNAPSHOT_COLUMNS) - set(reader.fieldnames or ())
        if missing:
            raise ValueError(f"CSV sem colunas obrigatorias: {', '.join(sorted(missing))}")
        return validate_snapshot_rows(list(reader))


def export_snapshot(connection, uf, path):
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
    validate_snapshot_rows(rows)
    write_snapshot(rows, path)
    return len(rows)


def restore_snapshot(connection, path):
    rows = read_snapshot(path)
    restored = 0
    with connection.cursor() as cursor:
        for row in rows:
            uf = validate_uf(row["Uf"])
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
                    row["CityIbgeCode"],
                ),
            )
            if cursor.rowcount == 0:
                raise ValueError(
                    f'Municipio nao encontrado: {row["City"]} ({row["Uf"]}/{row["CityIbgeCode"]})'
                )
            restored += 1
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


def main():
    parser = argparse.ArgumentParser(description="Exporta ou restaura snapshots CSV de bairros")
    parser.add_argument("action", choices=("export", "restore"))
    parser.add_argument("--uf", type=validate_uf)
    parser.add_argument("--file", required=True, type=Path)
    args = parser.parse_args()

    if args.action == "export" and not args.uf:
        parser.error("--uf e obrigatorio ao exportar")

    connection = connect_from_environment()
    try:
        if args.action == "export":
            total = export_snapshot(connection, args.uf, args.file)
            print(f"Exportados {total} bairros de {args.uf} para {args.file}")
        else:
            total = restore_snapshot(connection, args.file)
            print(f"Restaurados {total} bairros de {args.file}")
    finally:
        connection.close()


if __name__ == "__main__":
    main()
