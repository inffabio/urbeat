import argparse
import os
import sqlite3
import time
from urllib.parse import quote_plus

from import_common import fetch_json, validate_uf
from import_common import snapshot_path
from neighborhood_snapshot import export_snapshot


def valid_coordinates(latitude, longitude):
    try:
        latitude = float(latitude)
        longitude = float(longitude)
    except (TypeError, ValueError):
        return False
    return -90 <= latitude <= 90 and -180 <= longitude <= 180

def get_cep_from_dne(neighborhood, city):
    with sqlite3.connect(os.environ.get("URBEAT_DNE_DB", "/home/dexter/dne.db")) as connection:
        row = connection.execute(
            "SELECT cep FROM cep_unificado WHERE bairro = ? AND municipio = ? LIMIT 1",
            (neighborhood, city),
        ).fetchone()
    return row[0] if row else None

def get_coordinates_from_cep(cep):
    api_key = os.environ.get("BRASIL_ABERTO_API_KEY")
    data = fetch_json(
        f"https://api.brasilaberto.com/v2/zipcode/{cep}",
        {"Authorization": f"Bearer {api_key}"},
    )
    coordinates = data.get("result", {}).get("coordinates", {})
    latitude = coordinates.get("latitude")
    longitude = coordinates.get("longitude")
    if valid_coordinates(latitude, longitude):
        return float(latitude), float(longitude)
    return None, None


def get_coordinates_from_nominatim(neighborhood, city, uf):
    query = quote_plus(f"{neighborhood}, {city}, {uf}, Brasil")
    data = fetch_json(
        f"https://nominatim.openstreetmap.org/search?q={query}&format=jsonv2&limit=1"
    )
    if not data:
        return None, None
    try:
        latitude = float(data[0]["lat"])
        longitude = float(data[0]["lon"])
    except (KeyError, TypeError, ValueError):
        return None, None
    if not valid_coordinates(latitude, longitude):
        return None, None
    return latitude, longitude


def pending_coordinate_report(connection, uf):
    with connection.cursor() as cursor:
        cursor.execute(
            '''SELECT c."Name", d."Neighborhood"
               FROM "DeliveryNeighborhoods" d
               JOIN "Cities" c ON c."Id" = d."CityId"
               WHERE c."Uf" = %s AND d."IsActive" = true
                 AND (d."Latitude" IS NULL OR d."Longitude" IS NULL)
               ORDER BY c."Name", d."Neighborhood"''',
            (uf,),
        )
        pending = {}
        for city, neighborhood in cursor.fetchall():
            pending.setdefault(city, []).append(neighborhood)
    return pending

def geocode_uf(uf, connection=None):
    if not os.environ.get("BRASIL_ABERTO_API_KEY"):
        raise RuntimeError("Defina BRASIL_ABERTO_API_KEY antes de executar")
    uf = validate_uf(uf)
    owns_connection = connection is None
    if owns_connection:
        from import_common import connect_database
        connection = connect_database()
    connection.autocommit = True
    cursor = connection.cursor()
    cursor.execute('''SELECT d."Id", d."Neighborhood", d."City", c."Uf",
                             d."Latitude", d."Longitude"
        FROM "DeliveryNeighborhoods" d JOIN "Cities" c ON d."CityId" = c."Id"
        WHERE d."IsActive" = true
          AND (d."Latitude" IS NULL OR d."Longitude" IS NULL) AND c."Uf" = %s
        ORDER BY d."City", d."Neighborhood"''', (uf,))
    rows = cursor.fetchall()
    missing_cep = 0
    missing_coordinates = 0
    for index, (neighborhood_id, neighborhood, city, row_uf, existing_latitude, existing_longitude) in enumerate(rows, 1):
        source = None
        try:
            cep = get_cep_from_dne(neighborhood, city)
            if cep:
                latitude, longitude = get_coordinates_from_cep(str(cep))
                source = "brasil_aberto_cep"
            else:
                missing_cep += 1
                latitude, longitude = None, None
        except Exception:
            latitude, longitude = None, None
        if latitude is None or longitude is None:
            try:
                latitude, longitude = get_coordinates_from_nominatim(neighborhood, city, row_uf)
                source = "osm_nominatim"
            except Exception:
                latitude, longitude = None, None
        if latitude is None or longitude is None:
            missing_coordinates += 1
            continue
        cursor.execute(
            'UPDATE "DeliveryNeighborhoods" SET "Latitude" = COALESCE("Latitude", %s), "Longitude" = COALESCE("Longitude", %s), "Source" = COALESCE("Source", %s) WHERE "Id" = %s AND ("Latitude" IS NULL OR "Longitude" IS NULL)',
            (latitude, longitude, source, neighborhood_id),
        )
        if index % 100 == 0 or index == len(rows):
            print(f"  [{index}/{len(rows)}] {neighborhood}, {city}")
        time.sleep(0.05)
    connection.commit()
    pending = pending_coordinate_report(connection, uf)
    if pending:
        report = "; ".join(
            f"{city} ({len(neighborhoods)}): {', '.join(neighborhoods)}"
            for city, neighborhoods in pending.items()
        )
        print(f"Geocodificacao: {len(rows)} totais; {len(rows) - missing_coordinates} geolocalizados; {missing_coordinates} pendentes ({missing_cep} sem CEP)")
        print(f"Bairros pendentes por municipio: {report}")
    else:
        print(f"Geocodificacao: {len(rows)} totais; {len(rows)} geolocalizados; 0 pendentes")
    if owns_connection:
        export_snapshot(connection, uf, snapshot_path(uf))
    cursor.close()
    if owns_connection:
        connection.close()
    return len(rows), missing_cep, missing_coordinates

def main(argv=None):
    parser = argparse.ArgumentParser(description="Geocodifica bairros de uma UF pelo e-DNE")
    parser.add_argument("--uf", default="RJ", type=validate_uf)
    args = parser.parse_args(argv)
    total, missing_cep, missing_coordinates = geocode_uf(args.uf)
    print(f"Processados: {total}; sem CEP: {missing_cep}; sem coordenadas: {missing_coordinates}")

if __name__ == "__main__":
    main()
