import gzip
import json
import os
import unicodedata
import urllib.request
from pathlib import Path

IBGE_STATE_CODES = {
    "AC": 12, "AL": 27, "AP": 16, "AM": 13, "BA": 29, "CE": 23,
    "DF": 53, "ES": 32, "GO": 52, "MA": 21, "MT": 51, "MS": 50,
    "MG": 31, "PA": 15, "PB": 25, "PR": 41, "PE": 26, "PI": 22,
    "RJ": 33, "RN": 24, "RS": 43, "RO": 11, "RR": 14, "SC": 42,
    "SP": 35, "SE": 28, "TO": 17,
}


def load_local_environment(path=None):
    config_path = Path(path) if path else Path(__file__).with_name(".env.local")
    if not config_path.is_file():
        return
    for raw_line in config_path.read_text(encoding="utf-8").splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        name, value = line.split("=", 1)
        name = name.strip()
        value = value.strip().strip('"').strip("'")
        if name and value and name not in os.environ:
            os.environ[name] = value


load_local_environment()

def validate_uf(uf):
    normalized = (uf or "").strip().upper()
    if normalized not in IBGE_STATE_CODES:
        raise ValueError(f"UF invalida: {uf}. Use uma UF brasileira de dois caracteres.")
    return normalized

def ibge_state_code(uf):
    return IBGE_STATE_CODES[validate_uf(uf)]

def snapshot_path(uf, directory=None):
    base = Path(directory) if directory else Path(__file__).parent / "snapshots"
    return base / f"bairros_{validate_uf(uf).lower()}.csv"

def normalize_neighborhood_name(name):
    value = (name or "").strip()
    if not value:
        raise ValueError("Nome de bairro vazio")
    normalized = unicodedata.normalize("NFKD", value)
    normalized = "".join(char for char in normalized if not unicodedata.combining(char))
    normalized = "".join(char if char.isalnum() else " " for char in normalized)
    normalized = " ".join(normalized.split()).lower()
    if not normalized:
        raise ValueError("Nome de bairro vazio")
    return normalized

def normalize_ibge_code(value):
    """Normaliza um codigo IBGE de municipio para sua forma canonica de string.

    Aceita inteiros, floats integrais e strings, inclusive valores numericos
    com sufixo ".0" (ex.: ``3304557.0``), retornando ``"3304557"``. Retorna
    ``None`` para valores ausentes, nao numericos ou nao inteiros.
    """
    if value is None or isinstance(value, bool):
        return None
    if isinstance(value, int):
        text = str(value)
    elif isinstance(value, float):
        if not value.is_integer():
            return None
        text = str(int(value))
    else:
        text = str(value).strip()
        if not text:
            return None
        if text.endswith(".0") and text[:-2].isdigit():
            text = text[:-2]
    if not text.isdigit():
        return None
    return text


def candidate_key(uf, city_ibge_code, neighborhood_name):
    ibge = normalize_ibge_code(city_ibge_code)
    if not ibge:
        raise ValueError("CityIbgeCode ausente ou vazio")
    return (
        validate_uf(uf),
        ibge,
        normalize_neighborhood_name(neighborhood_name),
    )

def candidate_path(uf, directory=None):
    base = Path(directory) if directory else Path(__file__).parent / "candidates"
    return base / f"bairros_{validate_uf(uf).lower()}.csv"

def fetch_json(url, headers=None):
    request = urllib.request.Request(url)
    for key, value in (headers or {}).items():
        request.add_header(key, value)
    request.add_header("User-Agent", "Urbeat/1.0")
    request.add_header("Accept-Encoding", "gzip")
    with urllib.request.urlopen(request, timeout=30) as response:
        payload = response.read()
        if response.headers.get("Content-Encoding") == "gzip":
            payload = gzip.decompress(payload)
        return json.loads(payload.decode("utf-8"))

def connect_database():
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
