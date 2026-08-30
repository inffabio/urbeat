import argparse
from pathlib import Path

from import_common import (
    connect_database,
    normalize_ibge_code,
    normalize_neighborhood_name,
    validate_uf,
)
from neighborhood_snapshot import export_snapshot, read_snapshot


DELETE_BY_UF_SQL = '''
DELETE FROM "DeliveryNeighborhoods" AS d
USING "Cities" AS c
WHERE c."Uf" = %s
  AND (
    d."CityId" = c."Id"
    OR (d."CityId" IS NULL AND d."City" = c."Name")
  )
'''

INSERT_NEIGHBORHOOD_SQL = '''
INSERT INTO "DeliveryNeighborhoods"
    ("Id", "CityId", "City", "Neighborhood", "NormalizedName",
     "Latitude", "Longitude", "Source", "IsActive", "CreatedAtUtc")
VALUES (gen_random_uuid(), %s, %s, %s, %s,
        NULLIF(%s, '')::double precision,
        NULLIF(%s, '')::double precision,
        NULLIF(%s, ''), %s::boolean, now())
'''


def _parse_bool(value):
    if isinstance(value, bool):
        return value
    text = str(value or "").strip().lower()
    if text in ("false", "0", "no", "f"):
        return False
    return True


def _validate_rows_belong_to_uf(rows, uf):
    for row in rows:
        row_uf = (row.get("Uf") or "").strip().upper()
        if row_uf != uf:
            raise ValueError(
                f'Linha com UF inesperada: {row.get("City")}/{row.get("Neighborhood")} '
                f'usa {row_uf or "<vazia>"}, esperado {uf}'
            )


def _load_cities(cursor, uf):
    cursor.execute('SELECT "Id", "IbgeCode", "Name" FROM "Cities" WHERE "Uf" = %s', (uf,))
    return {
        normalize_ibge_code(ibge): {"id": city_id, "name": name}
        for city_id, ibge, name in cursor.fetchall()
    }


def _missing_cities(rows, cities):
    return sorted({
        normalize_ibge_code(row["CityIbgeCode"])
        for row in rows
        if normalize_ibge_code(row["CityIbgeCode"]) not in cities
    })


def _canonical_city_name(name):
    try:
        return normalize_neighborhood_name(name)
    except ValueError:
        return None


def _validate_city_names(rows, cities):
    mismatches = []
    for row in rows:
        ibge = normalize_ibge_code(row["CityIbgeCode"])
        registered = cities.get(ibge)
        if registered is None:
            continue
        if _canonical_city_name(registered["name"]) != _canonical_city_name(row["City"]):
            mismatches.append(
                f'{row["City"]} ({ibge}) difere de {registered["name"]}'
            )
    if mismatches:
        raise ValueError(
            "Municipios divergentes entre snapshot e Cities: "
            + "; ".join(sorted(set(mismatches)))
        )


def backup_uf(connection, uf, backup_path):
    return export_snapshot(connection, validate_uf(uf), backup_path, validate=False)


def rebuild_uf(connection, uf, rows, backup_path=None):
    uf = validate_uf(uf)
    _validate_rows_belong_to_uf(rows, uf)

    backup_count = None
    if backup_path is not None:
        backup_count = backup_uf(connection, uf, backup_path)

    inserted = 0
    deleted = 0
    try:
        with connection.cursor() as cursor:
            cities = _load_cities(cursor, uf)
            missing = _missing_cities(rows, cities)
            if missing:
                raise ValueError(
                    f"Municipios nao encontrados em Cities ({uf}): {', '.join(missing)}"
                )
            _validate_city_names(rows, cities)
            cursor.execute(DELETE_BY_UF_SQL, (uf,))
            deleted = cursor.rowcount
            for row in rows:
                city_id = cities[normalize_ibge_code(row["CityIbgeCode"])]["id"]
                cursor.execute(
                    INSERT_NEIGHBORHOOD_SQL,
                    (
                        city_id,
                        row["City"],
                        row["Neighborhood"],
                        row["NormalizedName"],
                        row["Latitude"],
                        row["Longitude"],
                        row["Source"],
                        _parse_bool(row.get("IsActive")),
                    ),
                )
                inserted += 1
    except Exception:
        connection.rollback()
        raise
    connection.commit()
    return {"backup": backup_count, "deleted": deleted, "inserted": inserted}


def _default_backup_path(file_path, uf):
    return file_path.with_name(f"{file_path.stem}.backup-{uf.lower()}.csv")


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="Reconstroi os bairros de uma UF a partir de um snapshot CSV"
    )
    parser.add_argument("--uf", type=validate_uf, required=True)
    parser.add_argument("--file", type=Path, required=True)
    parser.add_argument(
        "--confirm",
        required=True,
        help='Digite exatamente "REBUILD <UF>" para confirmar a operacao',
    )
    args = parser.parse_args(argv)

    expected = f"REBUILD {args.uf}"
    if args.confirm != expected:
        parser.error(f"--confirm deve ser exatamente '{expected}'")

    rows = read_snapshot(args.file)
    connection = connect_database()
    try:
        backup_path = _default_backup_path(args.file, args.uf)
        report = rebuild_uf(connection, args.uf, rows, backup_path)
    finally:
        connection.close()

    print(
        f"UF {args.uf}: backup={report['backup']} bairros, "
        f"apagados={report['deleted']}, inseridos={report['inserted']}. "
        f"Backup salvo em {backup_path}"
    )


if __name__ == "__main__":
    main()
