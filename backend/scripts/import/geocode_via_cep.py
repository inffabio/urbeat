import argparse
import os
import sqlite3
import time
from urllib.parse import quote_plus

from import_common import fetch_json, normalize_neighborhood_name, validate_uf
from neighborhood_snapshot import export_all_snapshots


def valid_coordinates(latitude, longitude):
    try:
        latitude = float(latitude)
        longitude = float(longitude)
    except (TypeError, ValueError):
        return False
    return -90 <= latitude <= 90 and -180 <= longitude <= 180

def _normalize_cep(cep):
    if cep is None:
        return None
    digits = "".join(char for char in str(cep) if char.isdigit())
    return digits or None


def get_first_street_from_dne(neighborhood, city):
    """Retorna ``(logradouro, cep)`` do e-DNE para um bairro, no fluxo legado.

    Usa a normalizacao canonica compartilhada para casar variantes de
    acento/caixa do nome do bairro. Fluxo recomendado: gere o CSV final sem
    escrever no banco com ``neighborhood_source_import.py`` e aplique no banco
    com ``neighborhood_rebuild.py``; este helper e usado apenas pelo importador
    legado, que exige opt-in explicito (``URBEAT_ALLOW_LEGACY_DB_IMPORT=true``).
    """
    connection = sqlite3.connect(os.environ.get("URBEAT_DNE_DB", "/home/dexter/dne.db"))
    try:
        target = normalize_neighborhood_name(neighborhood)
        rows = connection.execute(
            "SELECT logradouro, cep, bairro FROM cep_unificado "
            "WHERE municipio = ? "
            "AND logradouro IS NOT NULL AND trim(logradouro) <> '' "
            "AND cep IS NOT NULL AND trim(cep) <> ''",
            (city,),
        ).fetchall()
    finally:
        connection.close()
    best = None
    for logradouro, cep, bairro in rows:
        try:
            if normalize_neighborhood_name(bairro) != target:
                continue
        except ValueError:
            continue
        cep_normalized = _normalize_cep(cep)
        if not cep_normalized:
            continue
        candidate = (str(logradouro).strip(), cep_normalized)
        if best is None or candidate[0].lower() < best[0].lower():
            best = candidate
    return best if best is not None else (None, None)


def get_cep_from_dne(neighborhood, city):
    _, cep = get_first_street_from_dne(neighborhood, city)
    return cep

_cep_coordinate_cache = {}


def brasil_aberto_delay_seconds():
    """Retorna o atraso configurável entre chamadas ao Brasil Aberto.

    Configurável via ``BRASIL_ABERTO_DELAY_SECONDS`` (valor numerico em
    segundos); valores invalidos ou ausentes usam o padrao seguro de 0.15s.
    """
    raw = os.environ.get("BRASIL_ABERTO_DELAY_SECONDS")
    if raw is None:
        return 0.15
    try:
        delay = float(raw)
    except (TypeError, ValueError):
        return 0.15
    return max(0.0, delay)


def nominatim_enabled():
    """Indica se o fallback Nominatim esta habilitado por opt-in explicito.

    O Nominatim nao e chamado por padrao; exige ``URBEAT_ENABLE_NOMINATIM=true``.
    """
    return os.environ.get("URBEAT_ENABLE_NOMINATIM", "").strip().lower() == "true"


def nominatim_delay_seconds():
    """Retorna o atraso minimo entre chamadas ao Nominatim.

    Configuravel via ``NOMINATIM_DELAY_SECONDS``; valores ausentes ou invalidos
    usam o padrao de 1s, e valores menores que 1s sao elevados para 1s para
    respeitar a politica de uso do serviço.
    """
    raw = os.environ.get("NOMINATIM_DELAY_SECONDS")
    if raw is None:
        return 1.0
    try:
        delay = float(raw)
    except (TypeError, ValueError):
        return 1.0
    return max(1.0, delay)


def cep_aberto_token():
    """Retorna o token do Cep Aberto, sem valor padrao.

    O token vem de ``CEP_ABERTO_API_TOKEN`` e nunca e impresso nem usado com
    valor padrao: quando ausente, retorna ``None`` e o fallback nao e chamado.
    """
    return os.environ.get("CEP_ABERTO_API_TOKEN")


def cep_aberto_delay_seconds():
    """Retorna o atraso configurável entre chamadas ao Cep Aberto.

    Configurável via ``CEP_ABERTO_DELAY_SECONDS`` (valor numerico em segundos);
    valores invalidos ou ausentes usam o padrao seguro de 0.15s.
    """
    raw = os.environ.get("CEP_ABERTO_DELAY_SECONDS")
    if raw is None:
        return 0.15
    try:
        delay = float(raw)
    except (TypeError, ValueError):
        return 0.15
    return max(0.0, delay)


_cep_aberto_coordinate_cache = {}


def get_coordinates_from_cep_aberto(cep):
    """Converte um CEP em coordenadas usando o Cep Aberto.

    Consulta ``https://www.cepaberto.com/api/v3/cep?cep={cep}`` com o header
    ``Authorization: Token token=<CEP_ABERTO_API_TOKEN>`` e le latitude/longitude
    no topo da resposta. Sem token configurado, nao faz chamada e retorna
    ``(None, None)``. O resultado (inclusive ausente/invalido) e cacheado por CEP.
    """
    normalized = _normalize_cep(cep)
    if normalized is None:
        return None, None
    if normalized in _cep_aberto_coordinate_cache:
        return _cep_aberto_coordinate_cache[normalized]
    token = cep_aberto_token()
    if not token:
        return None, None
    time.sleep(cep_aberto_delay_seconds())
    data = fetch_json(
        f"https://www.cepaberto.com/api/v3/cep?cep={normalized}",
        {"Authorization": f"Token token={token}"},
    )
    latitude = data.get("latitude")
    longitude = data.get("longitude")
    result = (None, None)
    if valid_coordinates(latitude, longitude):
        result = (float(latitude), float(longitude))
    _cep_aberto_coordinate_cache[normalized] = result
    return result


def get_coordinates_from_cep(cep):
    normalized = _normalize_cep(cep)
    if normalized is None:
        return None, None
    if normalized in _cep_coordinate_cache:
        return _cep_coordinate_cache[normalized]
    api_key = os.environ.get("BRASIL_ABERTO_API_KEY")
    time.sleep(brasil_aberto_delay_seconds())
    data = fetch_json(
        f"https://api.brasilaberto.com/v2/zipcode/{normalized}",
        {"Authorization": f"Bearer {api_key}"},
    )
    coordinates = data.get("result", {}).get("coordinates", {})
    latitude = coordinates.get("latitude")
    longitude = coordinates.get("longitude")
    result = (None, None)
    if valid_coordinates(latitude, longitude):
        result = (float(latitude), float(longitude))
    _cep_coordinate_cache[normalized] = result
    return result


def mapbox_token():
    """Retorna o token do Mapbox, sem valor padrao.

    O token vem de ``MAPBOX_API_TOKEN`` e nunca e impresso nem usado com valor
    padrao: quando ausente, retorna ``None`` e o fallback nao e chamado.
    """
    return os.environ.get("MAPBOX_API_TOKEN")


def mapbox_delay_seconds():
    """Retorna o atraso configuravel entre chamadas ao Mapbox.

    Configuravel via ``MAPBOX_DELAY_SECONDS`` (valor numerico em segundos);
    valores invalidos ou ausentes usam o padrao seguro de 0.1s.
    """
    raw = os.environ.get("MAPBOX_DELAY_SECONDS")
    if raw is None:
        return 0.1
    try:
        delay = float(raw)
    except (TypeError, ValueError):
        return 0.1
    return max(0.0, delay)


_mapbox_coordinate_cache = {}


def _mapbox_query(street, city, uf, cep=None):
    parts = [str(street).strip()]
    normalized_cep = _normalize_cep(cep)
    if normalized_cep:
        parts.append(normalized_cep)
    parts.extend([str(city).strip(), str(uf).strip().upper(), "Brasil"])
    return ", ".join(part for part in parts if part)


def get_coordinates_from_mapbox(street, city, uf, cep=None):
    """Converte um logradouro em coordenadas usando o Mapbox Geocoding.

    Consulta ``https://api.mapbox.com/geocoding/v5/mapbox.places/{query}.json``
    com ``country=br``, ``language=pt`` e ``limit=1``, combinando logradouro,
    CEP (quando houver), municipio, UF e "Brasil" na query. Le as coordenadas em
    ``features[0].center`` (ou ``features[0].geometry.coordinates``), validando
    lat/lon. Sem token configurado, nao faz chamada e retorna ``(None, None)``;
    excecoes de rede/API ficam para o chamador. O resultado (inclusive
    ausente/invalido) e cacheado por query.
    """
    if street is None or str(street).strip() == "":
        return None, None
    token = mapbox_token()
    if not token:
        return None, None
    query = _mapbox_query(street, city, uf, cep)
    if query in _mapbox_coordinate_cache:
        return _mapbox_coordinate_cache[query]
    time.sleep(mapbox_delay_seconds())
    data = fetch_json(
        f"https://api.mapbox.com/geocoding/v5/mapbox.places/{quote_plus(query)}.json"
        f"?country=br&language=pt&limit=1&access_token={token}"
    )
    latitude = longitude = None
    features = data.get("features") if isinstance(data, dict) else None
    if isinstance(features, list) and features:
        feature = features[0]
        if isinstance(feature, dict):
            coordinates = feature.get("center")
            if not isinstance(coordinates, (list, tuple)) or len(coordinates) < 2:
                geometry = feature.get("geometry")
                coordinates = geometry.get("coordinates") if isinstance(geometry, dict) else None
            if isinstance(coordinates, (list, tuple)) and len(coordinates) >= 2:
                longitude, latitude = coordinates[0], coordinates[1]
    result = (None, None)
    if valid_coordinates(latitude, longitude):
        result = (float(latitude), float(longitude))
    _mapbox_coordinate_cache[query] = result
    return result


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
        latitude = longitude = None
        try:
            _, cep = get_first_street_from_dne(neighborhood, city)
            if cep:
                latitude, longitude = get_coordinates_from_cep(cep)
                if valid_coordinates(latitude, longitude):
                    source = "brasil_aberto_first_street"
                else:
                    latitude = longitude = None
            else:
                missing_cep += 1
        except Exception:
            latitude = longitude = None
        if source is None and nominatim_enabled():
            time.sleep(nominatim_delay_seconds())
            try:
                latitude, longitude = get_coordinates_from_nominatim(neighborhood, city, row_uf)
                if valid_coordinates(latitude, longitude):
                    source = "osm_nominatim"
                else:
                    latitude = longitude = None
            except Exception:
                latitude = longitude = None
        if source is None:
            missing_coordinates += 1
            continue
        cursor.execute(
            'UPDATE "DeliveryNeighborhoods" SET "Latitude" = %s, "Longitude" = %s, "Source" = COALESCE("Source", %s) WHERE "Id" = %s AND ("Latitude" IS NULL OR "Longitude" IS NULL)',
            (latitude, longitude, source, neighborhood_id),
        )
        if index % 100 == 0 or index == len(rows):
            print(f"  [{index}/{len(rows)}] {neighborhood}, {city}")
        time.sleep(0.05)
    if owns_connection:
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
        export_all_snapshots(connection)
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
