#!/usr/bin/env python3
"""Descoberta e validacao somente leitura do SQLite do CNEFE.

Etapa nao destrutiva da Fase 0 de ``imports/cnefe/prompt.txt`` v2.1
(secoes 3.2, 3.3 e 10): reproduz o inventario de schema, a qualidade do
artefato e a validacao dos centroides materializados em ``bairros``.

Garantias:

* o arquivo SQLite e aberto com ``mode=ro`` e ``PRAGMA query_only=1``;
* nenhuma tabela, indice ou linha e criada, alterada ou removida;
* ``VACUUM`` nunca e executado;
* o SHA-256 e recalculado ao final para provar que o arquivo nao mudou.

Uso:
    python backend/scripts/import/cnefe_discover_schema.py \\
        --sqlite-path imports/cnefe/delivery_brasil.db \\
        --output-dir imports/cnefe/reports

Saidas (em ``--output-dir``):
    schema-report.md
    schema-report.json
    centroid-validation.json

Somente biblioteca padrao do Python: nenhuma dependencia externa.
"""

from __future__ import annotations

import argparse
import array
import hashlib
import json
import math
import os
import sqlite3
import statistics
import sys
import tempfile
import time
from collections import Counter
from dataclasses import dataclass, field
from datetime import datetime, timezone
from functools import lru_cache
from pathlib import Path
from typing import Callable, Iterator, Sequence

from import_common import IBGE_STATE_CODES, normalize_ibge_code

DEFAULT_SOURCE_VERSION = "CNEFE_2022"
DEFAULT_CHUNK_SIZE = 50_000
DEFAULT_CENTROID_TOLERANCE_M = 1.0
DEFAULT_LOW_SAMPLE_THRESHOLD = 5
DEFAULT_MAX_SAMPLES = 10
SAMPLE_ROWS = 20

# Raio medio da Terra em metros; equivalente ao ``R = 6371 km`` usado pelo
# backend (``StoreService.HaversineKm``) para nao divergir na unidade.
EARTH_RADIUS_M = 6_371_000.0

# Envoltoria generosa do Brasil. Pontos fora dela sao claramente invalidos
# mesmo que estejam dentro de +-90/+-180. O limite leste (-28.0) cobre os
# territorios insulares (Fernando de Noronha -32.4, Atol das Rocas,
# Trindade -29.3 e Martin Vaz -28.9), que sao pontos validos do CNEFE.
BRAZIL_ENVELOPE = {
    "min_latitude": -34.5,
    "max_latitude": 6.0,
    "min_longitude": -74.5,
    "max_longitude": -28.0,
}

# Baseline documentado na secao 3.3/3.3.4 da especificacao v2.1. Serve apenas
# para comparacao informativa: um artefato novo pode divergir legitimamente.
BASELINE = {
    "reference": "imports/cnefe/prompt.txt v2.1 §3.3 e §3.3.4",
    "neighborhood_rows": 520_195,
    "street_rows": 4_266_401,
    "uf_count": 27,
    "municipality_count": 5_570,
    "neighborhoods_without_streets": 0,
    "duplicate_neighborhood_keys": 0,
}

UF_FORMAT_STATE_CODE = "ibge_state_code"
UF_FORMAT_SIGLA = "uf_sigla"
UF_FORMAT_ZERO_PADDED = "zero_padded_state_code"

CEP_MISSING = "missing"
CEP_EMPTY = "empty"
CEP_VALID = "valid"
CEP_REPAIRABLE = "repairable"
CEP_INVALID = "invalid"

COORD_VALID = "valid"
COORD_MISSING_BOTH = "missing_both"
COORD_MISSING_PARTIAL = "missing_partial"
COORD_NON_NUMERIC = "non_numeric"
COORD_NON_FINITE = "non_finite"
COORD_ZERO = "zero"
COORD_OUT_OF_RANGE = "out_of_range"
COORD_OUT_OF_BRAZIL = "out_of_brazil_envelope"

COORD_ISSUE_CODES = (
    COORD_MISSING_BOTH,
    COORD_MISSING_PARTIAL,
    COORD_NON_NUMERIC,
    COORD_NON_FINITE,
    COORD_ZERO,
    COORD_OUT_OF_RANGE,
    COORD_OUT_OF_BRAZIL,
)

# Mapeamento confirmado (secao 3.3.3). Campos opcionais ausentes sao
# registrados no relatorio e nunca inventados.
NEIGHBORHOOD_FIELDS = {
    "primary_key": "id",
    "uf": "uf_codigo",
    "municipality_ibge_code": "municipio_codigo",
    "locality": "nome",
    "neighborhood_centroid_latitude": "latitude_central",
    "neighborhood_centroid_longitude": "longitude_central",
}
NEIGHBORHOOD_REQUIRED = ("primary_key", "uf", "municipality_ibge_code", "locality")
NEIGHBORHOOD_OPTIONAL = (
    "neighborhood_centroid_latitude",
    "neighborhood_centroid_longitude",
)

STREET_FIELDS = {
    "primary_key": "id",
    "uf": "uf_codigo",
    "municipality_ibge_code": "municipio_codigo",
    "neighborhood_name": "bairro",
    "street_name": "logradouro",
    "zip_code": "cep",
    "latitude": "latitude",
    "longitude": "longitude",
}
STREET_REQUIRED = (
    "primary_key",
    "uf",
    "municipality_ibge_code",
    "neighborhood_name",
    "street_name",
    "latitude",
    "longitude",
)
STREET_OPTIONAL = ("zip_code",)

NEIGHBORHOODS_TABLE = "bairros"
STREETS_TABLE = "ruas"

STATE_CODE_TO_SIGLA = {str(code): sigla for sigla, code in IBGE_STATE_CODES.items()}

FINGERPRINT_EXCLUDED_KEYS = ("report", "performance", "data_fingerprint")


class SchemaMappingError(RuntimeError):
    """O artefato nao possui tabela/coluna obrigatoria do mapeamento."""


# ---------------------------------------------------------------------------
# Utilitarios puros
# ---------------------------------------------------------------------------


def haversine_meters(
    latitude1: float, longitude1: float, latitude2: float, longitude2: float
) -> float:
    """Distancia em linha reta (circulo completo) entre dois pontos, em metros."""
    phi1 = math.radians(latitude1)
    phi2 = math.radians(latitude2)
    delta_phi = math.radians(latitude2 - latitude1)
    delta_lambda = math.radians(longitude2 - longitude1)
    sin_phi = math.sin(delta_phi / 2.0)
    sin_lambda = math.sin(delta_lambda / 2.0)
    haversine = sin_phi * sin_phi + math.cos(phi1) * math.cos(phi2) * sin_lambda * sin_lambda
    haversine = min(1.0, max(0.0, haversine))
    return 2.0 * EARTH_RADIUS_M * math.asin(math.sqrt(haversine))


def percentile(ordered_values: Sequence[float], fraction: float) -> float | None:
    """Percentil com interpolacao linear sobre uma sequencia ja ordenada."""
    if not 0.0 <= fraction <= 1.0:
        raise ValueError("fraction deve estar entre 0 e 1")
    if not ordered_values:
        return None
    if len(ordered_values) == 1:
        return float(ordered_values[0])
    position = (len(ordered_values) - 1) * fraction
    lower = math.floor(position)
    upper = math.ceil(position)
    if lower == upper:
        return float(ordered_values[lower])
    weight = position - lower
    return (
        float(ordered_values[lower]) * (1.0 - weight)
        + float(ordered_values[upper]) * weight
    )


def summarize_distribution(values) -> dict:
    """Minimo, mediana, p90, p99, maximo e media de uma sequencia de floats."""
    ordered = sorted(values)
    if not ordered:
        return {
            "count": 0,
            "min": None,
            "median": None,
            "p90": None,
            "p99": None,
            "max": None,
            "mean": None,
        }
    return {
        "count": len(ordered),
        "min": float(ordered[0]),
        "median": percentile(ordered, 0.5),
        "p90": percentile(ordered, 0.90),
        "p99": percentile(ordered, 0.99),
        "max": float(ordered[-1]),
        "mean": statistics.fmean(ordered),
    }


def kahan_add(total: float, compensation: float, value: float) -> tuple[float, float]:
    """Soma compensada (Kahan) para reduzir erro de acumulacao em floats."""
    adjusted = value - compensation
    updated = total + adjusted
    compensation = (updated - total) - adjusted
    return updated, compensation


def sha256_file(path: Path, chunk_size: int = 1 << 20) -> str:
    digest = hashlib.sha256()
    with Path(path).open("rb") as handle:
        while True:
            chunk = handle.read(chunk_size)
            if not chunk:
                break
            digest.update(chunk)
    return digest.hexdigest()


def human_size(size_bytes: int) -> str:
    value = float(size_bytes)
    for unit in ("B", "KiB", "MiB", "GiB"):
        if value < 1024.0 or unit == "GiB":
            return f"{int(value)} B" if unit == "B" else f"{value:.1f} {unit}"
        value /= 1024.0
    return f"{value:.1f} GiB"


def utc_now_iso() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z")


def timestamp_to_utc_iso(timestamp: float) -> str:
    return (
        datetime.fromtimestamp(timestamp, tz=timezone.utc)
        .isoformat(timespec="seconds")
        .replace("+00:00", "Z")
    )


def parse_coordinate(value: object) -> tuple[float | None, str]:
    """Converte um valor de coordenada em float, informando o estado do dado."""
    if value is None:
        return None, "missing"
    if isinstance(value, bool):
        return None, "invalid"
    if isinstance(value, (int, float)):
        return float(value), "ok"
    text = str(value).strip()
    if not text:
        return None, "missing"
    try:
        return float(text), "ok"
    except ValueError:
        return None, "invalid"


def classify_coordinates(latitude: object, longitude: object) -> str:
    """Classifica um par de coordenadas em um unico codigo de qualidade."""
    lat_value, lat_state = parse_coordinate(latitude)
    lon_value, lon_state = parse_coordinate(longitude)
    return classify_parsed_coordinates(lat_value, lat_state, lon_value, lon_state)


def classify_parsed_coordinates(
    lat_value: float | None,
    lat_state: str,
    lon_value: float | None,
    lon_state: str,
) -> str:
    """Variante de ``classify_coordinates`` para valores ja parseados."""
    if lat_state == "invalid" or lon_state == "invalid":
        return COORD_NON_NUMERIC
    if lat_state == "missing" and lon_state == "missing":
        return COORD_MISSING_BOTH
    if lat_state == "missing" or lon_state == "missing":
        return COORD_MISSING_PARTIAL
    assert lat_value is not None and lon_value is not None
    if not (math.isfinite(lat_value) and math.isfinite(lon_value)):
        return COORD_NON_FINITE
    if lat_value == 0.0 and lon_value == 0.0:
        return COORD_ZERO
    if not (-90.0 <= lat_value <= 90.0) or not (-180.0 <= lon_value <= 180.0):
        return COORD_OUT_OF_RANGE
    if not (
        BRAZIL_ENVELOPE["min_latitude"] <= lat_value <= BRAZIL_ENVELOPE["max_latitude"]
        and BRAZIL_ENVELOPE["min_longitude"]
        <= lon_value
        <= BRAZIL_ENVELOPE["max_longitude"]
    ):
        return COORD_OUT_OF_BRAZIL
    return COORD_VALID


def classify_uf(value: object) -> tuple[str | None, str | None]:
    """Normaliza o codigo de UF.

    Retorna ``(codigo_ibge_uf, formato)`` ou ``(None, None)`` quando o valor nao
    e reconhecido. O artefato real usa o codigo IBGE de 2 digitos; siglas sao
    aceitas para artefatos futuros e registradas como formato distinto.
    """
    if value is None:
        return None, None
    text = str(value).strip().upper()
    if not text:
        return None, None
    if text in STATE_CODE_TO_SIGLA:
        return text, UF_FORMAT_STATE_CODE
    if text in IBGE_STATE_CODES:
        return str(IBGE_STATE_CODES[text]), UF_FORMAT_SIGLA
    if text.isdigit() and len(text) < 2:
        padded = text.zfill(2)
        if padded in STATE_CODE_TO_SIGLA:
            return padded, UF_FORMAT_ZERO_PADDED
    return None, None


def classify_municipality(value: object) -> tuple[str | None, str]:
    """Normaliza o codigo IBGE do municipio para 7 digitos.

    Retorna ``(codigo, formato)`` com formato ``digits7`` (valor ja canonico),
    ``normalized`` (precisou de normalizacao) ou ``invalid``.
    """
    normalized = normalize_ibge_code(value)
    if normalized is None or len(normalized) != 7:
        return None, "invalid"
    raw = "" if value is None else str(value)
    return normalized, "digits7" if raw == normalized else "normalized"


def classify_cep(value: object) -> str:
    """Classifica um CEP: ausente, vazio, valido, reparavel ou invalido."""
    if value is None:
        return CEP_MISSING
    if isinstance(value, bool):
        return CEP_INVALID
    if isinstance(value, int):
        text = str(value)
    elif isinstance(value, float):
        text = str(int(value)) if value.is_integer() else str(value)
    else:
        text = str(value)
    stripped = text.strip()
    if not stripped:
        return CEP_EMPTY
    digits = "".join(char for char in stripped if char in "0123456789")
    if len(stripped) == 8 and all(char in "0123456789" for char in stripped):
        return CEP_VALID
    separators = {" ", "-", ".", "/"}
    if len(digits) == 8 and all(
        char in "0123456789" or char in separators for char in stripped
    ):
        return CEP_REPAIRABLE
    return CEP_INVALID


# Variantes memoizadas: usadas nas passadas de streaming, onde os valores de
# UF/municipio/CEP se repetem milhoes de vezes.
classify_uf_cached = lru_cache(maxsize=4096)(classify_uf)
classify_municipality_cached = lru_cache(maxsize=8192)(classify_municipality)
classify_cep_cached = lru_cache(maxsize=32768)(classify_cep)


def quote_identifier(name: str) -> str:
    return '"' + str(name).replace('"', '""') + '"'


def truncate_value(value: object, limit: int = 60) -> object:
    """Trunca textos longos preservando o tipo dos demais valores."""
    if isinstance(value, str) and len(value) > limit:
        return value[: limit - 3] + "..."
    return value


def round_float(value: float | None, digits: int = 6) -> float | None:
    if value is None:
        return None
    return round(float(value), digits)


def peak_memory_bytes() -> int | None:
    """Pico de memoria residente do processo, quando o SO permite medir."""
    try:
        if sys.platform == "win32":
            import ctypes
            from ctypes import wintypes

            class _ProcessMemoryCounters(ctypes.Structure):
                _fields_ = [
                    ("cb", wintypes.DWORD),
                    ("PageFaultCount", wintypes.DWORD),
                    ("PeakWorkingSetSize", ctypes.c_size_t),
                    ("WorkingSetSize", ctypes.c_size_t),
                    ("QuotaPeakPagedPoolUsage", ctypes.c_size_t),
                    ("QuotaPagedPoolUsage", ctypes.c_size_t),
                    ("QuotaPeakNonPagedPoolUsage", ctypes.c_size_t),
                    ("QuotaNonPagedPoolUsage", ctypes.c_size_t),
                    ("PagefileUsage", ctypes.c_size_t),
                    ("PeakPagefileUsage", ctypes.c_size_t),
                ]

            kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
            psapi = ctypes.WinDLL("psapi", use_last_error=True)
            kernel32.GetCurrentProcess.restype = wintypes.HANDLE
            psapi.GetProcessMemoryInfo.argtypes = [
                wintypes.HANDLE,
                ctypes.POINTER(_ProcessMemoryCounters),
                wintypes.DWORD,
            ]
            psapi.GetProcessMemoryInfo.restype = wintypes.BOOL

            counters = _ProcessMemoryCounters()
            counters.cb = ctypes.sizeof(_ProcessMemoryCounters)
            if psapi.GetProcessMemoryInfo(
                kernel32.GetCurrentProcess(), ctypes.byref(counters), counters.cb
            ):
                return int(counters.PeakWorkingSetSize)
            return None
        import resource

        usage = resource.getrusage(resource.RUSAGE_SELF).ru_maxrss
        return int(usage) if sys.platform == "darwin" else int(usage) * 1024
    except Exception:  # pragma: no cover - depende do SO
        return None


# ---------------------------------------------------------------------------
# Estatisticas de qualidade
# ---------------------------------------------------------------------------


NEIGHBORHOOD_CONTEXT_FIELDS = ("uf_codigo", "municipio_codigo", "bairro")
STREET_CONTEXT_FIELDS = ("uf_codigo", "municipio_codigo", "bairro", "logradouro")


def context_to_sample(context_fields: Sequence[str], context: tuple) -> dict:
    """Monta a amostra somente quando ela e de fato registrada."""
    return {
        field_name: truncate_value(value)
        for field_name, value in zip(context_fields, context)
    }


@dataclass
class TextFieldStats:
    """Nulos, vazios, caracteres de substituicao e maior comprimento."""

    field: str
    column: str
    context_fields: tuple = NEIGHBORHOOD_CONTEXT_FIELDS
    rows: int = 0
    null: int = 0
    empty: int = 0
    replacement_char: int = 0
    max_length: int = 0
    samples: list = field(default_factory=list)

    def observe(self, value: object, context: tuple, max_samples: int) -> None:
        self.rows += 1
        if value is None:
            self.null += 1
            return
        text = value if isinstance(value, str) else str(value)
        length = len(text)
        if length > self.max_length:
            self.max_length = length
        if not text.strip():
            self.empty += 1
        if "\ufffd" in text:
            self.replacement_char += 1
            if len(self.samples) < max_samples:
                self.samples.append(
                    {
                        "value": truncate_value(text),
                        **context_to_sample(self.context_fields, context),
                    }
                )

    def as_dict(self) -> dict:
        return {
            "column": self.column,
            "rows": self.rows,
            "null": self.null,
            "empty": self.empty,
            "null_or_empty": self.null + self.empty,
            "replacement_char": self.replacement_char,
            "max_length": self.max_length,
            "samples": self.samples,
        }


@dataclass
class CoordinateStats:
    """Distribuicao dos codigos de qualidade de coordenadas."""

    scope: str
    context_fields: tuple = NEIGHBORHOOD_CONTEXT_FIELDS
    rows: int = 0
    counts: Counter = field(default_factory=Counter)
    min_latitude: float | None = None
    max_latitude: float | None = None
    min_longitude: float | None = None
    max_longitude: float | None = None
    axis_zero: int = 0
    samples: dict = field(default_factory=dict)

    def observe(
        self,
        latitude: object,
        longitude: object,
        context: tuple,
        max_samples: int,
    ) -> tuple[str, float | None, float | None]:
        """Classifica o par e devolve o codigo junto dos valores parseados."""
        lat_value, lat_state = parse_coordinate(latitude)
        lon_value, lon_state = parse_coordinate(longitude)
        code = classify_parsed_coordinates(lat_value, lat_state, lon_value, lon_state)
        self.rows += 1
        self.counts[code] += 1
        if code != COORD_VALID:
            bucket = self.samples.setdefault(code, [])
            if len(bucket) < max_samples:
                bucket.append(
                    {
                        "latitude": latitude,
                        "longitude": longitude,
                        **context_to_sample(self.context_fields, context),
                    }
                )
            return code, lat_value, lon_value
        assert lat_value is not None and lon_value is not None
        if lat_value == 0.0 or lon_value == 0.0:
            self.axis_zero += 1
        if self.min_latitude is None or lat_value < self.min_latitude:
            self.min_latitude = lat_value
        if self.max_latitude is None or lat_value > self.max_latitude:
            self.max_latitude = lat_value
        if self.min_longitude is None or lon_value < self.min_longitude:
            self.min_longitude = lon_value
        if self.max_longitude is None or lon_value > self.max_longitude:
            self.max_longitude = lon_value
        return code, lat_value, lon_value

    @property
    def invalid(self) -> int:
        return self.rows - self.counts[COORD_VALID]

    def as_dict(self) -> dict:
        return {
            "scope": self.scope,
            "rows": self.rows,
            "valid": self.counts[COORD_VALID],
            "invalid": self.invalid,
            "codes": {code: self.counts[code] for code in (COORD_VALID,) + COORD_ISSUE_CODES},
            "axis_zero": self.axis_zero,
            "valid_range": {
                "min_latitude": round_float(self.min_latitude, 6),
                "max_latitude": round_float(self.max_latitude, 6),
                "min_longitude": round_float(self.min_longitude, 6),
                "max_longitude": round_float(self.max_longitude, 6),
            },
            "samples": self.samples,
        }


@dataclass
class ZipStats:
    """Classificacao dos CEPs encontrados."""

    column: str
    context_fields: tuple = STREET_CONTEXT_FIELDS
    rows: int = 0
    counts: Counter = field(default_factory=Counter)
    samples: list = field(default_factory=list)

    def observe(self, value: object, context: tuple, max_samples: int) -> str:
        code = classify_cep_cached(value)
        self.rows += 1
        self.counts[code] += 1
        if code in (CEP_INVALID, CEP_REPAIRABLE) and len(self.samples) < max_samples:
            self.samples.append(
                {
                    "value": truncate_value(value),
                    "code": code,
                    **context_to_sample(self.context_fields, context),
                }
            )
        return code

    def as_dict(self) -> dict:
        return {
            "column": self.column,
            "rows": self.rows,
            "missing": self.counts[CEP_MISSING],
            "empty": self.counts[CEP_EMPTY],
            "valid": self.counts[CEP_VALID],
            "repairable": self.counts[CEP_REPAIRABLE],
            "invalid": self.counts[CEP_INVALID],
            "samples": self.samples,
        }


@dataclass
class DimensionStats:
    """UF e municipio: formato, consistencia e cardinalidade."""

    context_fields: tuple = NEIGHBORHOOD_CONTEXT_FIELDS
    uf_formats: Counter = field(default_factory=Counter)
    uf_invalid: list = field(default_factory=list)
    uf_invalid_count: int = 0
    municipality_invalid: list = field(default_factory=list)
    municipality_invalid_count: int = 0
    municipality_normalized: int = 0
    municipality_prefix_mismatch: int = 0
    municipality_prefix_samples: list = field(default_factory=list)
    uf_codes: set = field(default_factory=set)
    municipality_codes: set = field(default_factory=set)
    rows: int = 0

    def observe(
        self, uf: object, municipality: object, context: tuple, max_samples: int
    ) -> None:
        self.rows += 1
        uf_code, uf_format = classify_uf_cached(uf)
        if uf_code is None:
            self.uf_invalid_count += 1
            if len(self.uf_invalid) < max_samples:
                self.uf_invalid.append(
                    {
                        "value": truncate_value(uf),
                        **context_to_sample(self.context_fields, context),
                    }
                )
        else:
            self.uf_codes.add(uf_code)
            self.uf_formats[uf_format] += 1
        municipality_code, municipality_format = classify_municipality_cached(municipality)
        if municipality_code is None:
            self.municipality_invalid_count += 1
            if len(self.municipality_invalid) < max_samples:
                self.municipality_invalid.append(
                    {
                        "value": truncate_value(municipality),
                        **context_to_sample(self.context_fields, context),
                    }
                )
            return
        if municipality_format == "normalized":
            self.municipality_normalized += 1
        self.municipality_codes.add(municipality_code)
        if uf_code is not None and municipality_code[:2] != uf_code:
            self.municipality_prefix_mismatch += 1
            if len(self.municipality_prefix_samples) < max_samples:
                self.municipality_prefix_samples.append(
                    {
                        "uf_codigo": uf_code,
                        "municipio_codigo": municipality_code,
                        **context_to_sample(self.context_fields, context),
                    }
                )


def _dominant_format(formats: Counter) -> str | None:
    if not formats:
        return None
    ordered = sorted(formats.items(), key=lambda item: (-item[1], item[0]))
    return ordered[0][0]


# ---------------------------------------------------------------------------
# Agregacao de centroides
# ---------------------------------------------------------------------------

_RECORD_COUNT = 0
_RECORD_LAT_SUM = 1
_RECORD_LAT_COMP = 2
_RECORD_LON_SUM = 3
_RECORD_LON_COMP = 4
_RECORD_LAT_CENTRAL = 5
_RECORD_LON_CENTRAL = 6


class CentroidAccumulator:
    """Agrega pontos de ruas por ``(uf, municipio, bairro)``.

    Memoria O(numero de bairros): um registro por bairro, nunca uma linha por
    ponto de rua. As somas usam compensacao de Kahan para que o resultado seja
    estavel. A media e calculada sobre os pontos validos de ``ruas``, com peso
    por ponto (neste artefato, um ponto por logradouro); nao existe media de
    medias, pois cada bairro tem uma unica soma consolidada.
    """

    def __init__(self, max_samples: int = DEFAULT_MAX_SAMPLES) -> None:
        self._records: dict[tuple[str, str, str], list] = {}
        self._dim_cache: dict[str, str] = {}
        self.max_samples = max_samples
        self.max_orphan_groups = 200_000
        self.neighborhood_rows = 0
        self.duplicate_neighborhood_rows = 0
        self.duplicate_neighborhood_samples: list[dict] = []
        self.points_added = 0
        self.orphan_rows = 0
        self.orphan_groups: set[tuple[str, str, str]] = set()
        self.orphan_samples: list[dict] = []
        self.orphan_groups_truncated = False

    def _intern(self, value: str) -> str:
        cached = self._dim_cache.get(value)
        if cached is not None:
            return cached
        self._dim_cache[value] = value
        return value

    def register_neighborhood(
        self,
        uf: str,
        municipality_code: str,
        name: str,
        latitude_central: float | None,
        longitude_central: float | None,
        context: tuple = (),
    ) -> bool:
        """Registra um bairro. Retorna ``False`` quando a chave ja existia."""
        self.neighborhood_rows += 1
        composite = (self._intern(uf), self._intern(municipality_code), name)
        if composite in self._records:
            self.duplicate_neighborhood_rows += 1
            if len(self.duplicate_neighborhood_samples) < self.max_samples:
                self.duplicate_neighborhood_samples.append(
                    {
                        "uf_codigo": uf,
                        "municipio_codigo": municipality_code,
                        "bairro": name,
                        **context_to_sample(NEIGHBORHOOD_CONTEXT_FIELDS, context),
                    }
                )
            return False
        self._records[composite] = [
            0,
            0.0,
            0.0,
            0.0,
            0.0,
            latitude_central,
            longitude_central,
        ]
        return True

    def add_point(
        self,
        uf: str,
        municipality_code: str,
        name: str,
        latitude: float,
        longitude: float,
    ) -> bool:
        """Acumula um ponto valido. Retorna ``False`` se o grupo nao tem bairro."""
        record = self._records.get(
            (self._intern(uf), self._intern(municipality_code), name)
        )
        if record is None:
            self.orphan_rows += 1
            if not self.orphan_groups_truncated:
                orphan_key = (uf, municipality_code, name)
                if orphan_key not in self.orphan_groups:
                    if len(self.orphan_groups) >= self.max_orphan_groups:
                        self.orphan_groups_truncated = True
                    else:
                        self.orphan_groups.add(orphan_key)
                        if len(self.orphan_samples) < self.max_samples:
                            self.orphan_samples.append(
                                {
                                    "uf_codigo": uf,
                                    "municipio_codigo": municipality_code,
                                    "bairro": name,
                                }
                            )
            return False
        record[_RECORD_COUNT] += 1
        record[_RECORD_LAT_SUM], record[_RECORD_LAT_COMP] = kahan_add(
            record[_RECORD_LAT_SUM], record[_RECORD_LAT_COMP], latitude
        )
        record[_RECORD_LON_SUM], record[_RECORD_LON_COMP] = kahan_add(
            record[_RECORD_LON_SUM], record[_RECORD_LON_COMP], longitude
        )
        self.points_added += 1
        return True

    def __len__(self) -> int:
        return len(self._records)

    def items(self) -> Iterator[tuple[tuple[str, str, str], list]]:
        return iter(self._records.items())


@dataclass
class CentroidComparison:
    """Resultado da comparacao centroide materializado x media dos pontos."""

    compared: int
    errors: array.array
    errors_by_uf: dict[str, array.array]
    neighborhoods_by_uf: Counter
    above_tolerance: int
    above_tolerance_samples: list
    missing_centroid: int
    missing_centroid_samples: list
    without_points: int
    without_points_samples: list
    low_sample: int
    low_sample_samples: list
    points_invalid_excluded: int


def _sort_key_for_divergence(item: dict) -> tuple:
    return (
        -item["error_m"],
        item["uf_codigo"],
        item["municipio_codigo"],
        item["bairro"],
    )


def _keep_top(items: list[dict], item: dict, limit: int) -> None:
    """Mantem no maximo ~``limit`` itens, com corte deterministico."""
    items.append(item)
    if len(items) > max(limit, 1) * 8:
        items.sort(key=_sort_key_for_divergence)
        del items[limit:]


def compare_centroids(
    accumulator: CentroidAccumulator,
    *,
    tolerance_m: float,
    max_samples: int = DEFAULT_MAX_SAMPLES,
    low_sample_threshold: int = DEFAULT_LOW_SAMPLE_THRESHOLD,
    points_invalid_excluded: int = 0,
) -> CentroidComparison:
    """Compara ``*_central`` com a media direta dos pontos de cada bairro."""
    errors = array.array("d")
    errors_by_uf: dict[str, array.array] = {}
    neighborhoods_by_uf: Counter = Counter()
    above_tolerance = 0
    above_samples: list[dict] = []
    missing_centroid = 0
    missing_centroid_samples: list[dict] = []
    without_points = 0
    without_points_samples: list[dict] = []
    low_sample = 0
    low_sample_samples: list[dict] = []
    compared = 0

    for (uf, municipality_code, name), record in accumulator.items():
        neighborhoods_by_uf[uf] += 1
        point_count = record[_RECORD_COUNT]
        if point_count == 0:
            without_points += 1
            if len(without_points_samples) < max_samples:
                without_points_samples.append(
                    {
                        "uf_codigo": uf,
                        "municipio_codigo": municipality_code,
                        "bairro": name,
                    }
                )
            continue
        latitude_central = record[_RECORD_LAT_CENTRAL]
        longitude_central = record[_RECORD_LON_CENTRAL]
        if latitude_central is None or longitude_central is None:
            missing_centroid += 1
            if len(missing_centroid_samples) < max_samples:
                missing_centroid_samples.append(
                    {
                        "uf_codigo": uf,
                        "municipio_codigo": municipality_code,
                        "bairro": name,
                        "point_count": point_count,
                    }
                )
            continue
        mean_latitude = record[_RECORD_LAT_SUM] / point_count
        mean_longitude = record[_RECORD_LON_SUM] / point_count
        error = haversine_meters(
            float(latitude_central),
            float(longitude_central),
            mean_latitude,
            mean_longitude,
        )
        compared += 1
        errors.append(error)
        errors_by_uf.setdefault(uf, array.array("d")).append(error)

        if 0 < point_count < low_sample_threshold:
            low_sample += 1
            if len(low_sample_samples) < max_samples:
                low_sample_samples.append(
                    {
                        "uf_codigo": uf,
                        "municipio_codigo": municipality_code,
                        "bairro": name,
                        "point_count": point_count,
                        "error_m": round_float(error),
                    }
                )

        if error > tolerance_m:
            above_tolerance += 1
            _keep_top(
                above_samples,
                {
                    "uf_codigo": uf,
                    "municipio_codigo": municipality_code,
                    "bairro": name,
                    "point_count": point_count,
                    "centroid": {
                        "latitude": round_float(float(latitude_central), 7),
                        "longitude": round_float(float(longitude_central), 7),
                    },
                    "recomputed": {
                        "latitude": round_float(mean_latitude, 7),
                        "longitude": round_float(mean_longitude, 7),
                    },
                    "error_m": round_float(error),
                },
                max_samples,
            )

    above_samples.sort(key=_sort_key_for_divergence)
    del above_samples[max_samples:]

    return CentroidComparison(
        compared=compared,
        errors=errors,
        errors_by_uf=errors_by_uf,
        neighborhoods_by_uf=neighborhoods_by_uf,
        above_tolerance=above_tolerance,
        above_tolerance_samples=above_samples,
        missing_centroid=missing_centroid,
        missing_centroid_samples=missing_centroid_samples,
        without_points=without_points,
        without_points_samples=without_points_samples,
        low_sample=low_sample,
        low_sample_samples=low_sample_samples,
        points_invalid_excluded=points_invalid_excluded,
    )


# ---------------------------------------------------------------------------
# Acesso ao SQLite (somente leitura)
# ---------------------------------------------------------------------------


def open_readonly_connection(sqlite_path: str | Path) -> sqlite3.Connection:
    """Abre o SQLite em modo somente leitura e bloqueia escrita por PRAGMA."""
    resolved = Path(sqlite_path).expanduser().resolve()
    if not resolved.is_file():
        raise FileNotFoundError(f"SQLite nao encontrado: {resolved}")
    connection = sqlite3.connect(resolved.as_uri() + "?mode=ro", uri=True)
    try:
        connection.execute("PRAGMA query_only = 1")
        query_only = connection.execute("PRAGMA query_only").fetchone()
        if not query_only or int(query_only[0]) != 1:
            raise RuntimeError("Nao foi possivel habilitar PRAGMA query_only no SQLite")
    except Exception:
        connection.close()
        raise
    return connection


@dataclass(frozen=True)
class TableMapping:
    role: str
    table: str
    columns: dict  # campo canonico -> coluna real
    select_fields: tuple  # campos presentes, na ordem do SELECT (pk primeiro)
    missing_optional: tuple

    def has(self, field_name: str) -> bool:
        return field_name in self.columns


def table_names(connection: sqlite3.Connection) -> list[str]:
    rows = connection.execute(
        "SELECT name FROM sqlite_master WHERE type IN ('table', 'view') ORDER BY name"
    ).fetchall()
    return [row[0] for row in rows]


def resolve_table_mapping(
    connection: sqlite3.Connection,
    *,
    role: str,
    table: str,
    fields: dict,
    required: Sequence[str],
    optional: Sequence[str],
) -> TableMapping:
    available = set(table_names(connection))
    if table not in available:
        raise SchemaMappingError(
            f'Tabela obrigatoria "{table}" ausente no SQLite. '
            f"Tabelas encontradas: {', '.join(sorted(available)) or '(nenhuma)'}"
        )
    columns = {
        row[1]
        for row in connection.execute(f"PRAGMA table_info({quote_identifier(table)})")
    }
    missing = [field_name for field_name in required if fields.get(field_name) not in columns]
    if missing:
        detail = ", ".join(
            f"{field_name} -> {fields.get(field_name)}" for field_name in missing
        )
        raise SchemaMappingError(
            f'Tabela "{table}" sem colunas obrigatorias do mapeamento: {detail}. '
            f"Colunas encontradas: {', '.join(sorted(columns))}"
        )
    resolved = {field_name: field for field_name, field in fields.items() if field in columns}
    missing_optional = tuple(
        field_name for field_name in optional if fields.get(field_name) not in columns
    )
    return TableMapping(
        role=role,
        table=table,
        columns=resolved,
        select_fields=tuple(resolved.keys()),
        missing_optional=missing_optional,
    )


def stream_rows(
    connection: sqlite3.Connection,
    mapping: TableMapping,
    chunk_size: int,
) -> Iterator[list]:
    """Percorre a tabela pela chave primaria em chunks (sem ``OFFSET``)."""
    if chunk_size <= 0:
        raise ValueError("chunk_size deve ser positivo")
    columns = ", ".join(
        quote_identifier(mapping.columns[name]) for name in mapping.select_fields
    )
    primary_key = quote_identifier(mapping.columns["primary_key"])
    statement = (
        f"SELECT {columns} FROM {quote_identifier(mapping.table)} ORDER BY {primary_key}"
    )
    cursor = connection.execute(statement)
    try:
        while True:
            chunk = cursor.fetchmany(chunk_size)
            if not chunk:
                break
            yield chunk
    finally:
        cursor.close()


# ---------------------------------------------------------------------------
# Inventario de schema
# ---------------------------------------------------------------------------


def collect_schema_inventory(connection: sqlite3.Connection) -> dict:
    master = connection.execute(
        """
        SELECT type, name, tbl_name, sql
        FROM sqlite_master
        WHERE type IN ('table', 'view')
        ORDER BY type, name
        """
    ).fetchall()
    tables: list[dict] = []
    views: list[dict] = []

    for table_type, name, _tbl_name, sql in master:
        entry = {
            "name": name,
            "type": table_type,
            "sql": sql,
            "row_count": None,
            "columns": [],
            "primary_key": [],
            "indexes": [],
            "sample_rows": [],
            "notes": [],
        }
        for cid, column_name, column_type, not_null, default_value, primary_key in (
            connection.execute(f"PRAGMA table_info({quote_identifier(name)})")
        ):
            entry["columns"].append(
                {
                    "position": int(cid),
                    "name": column_name,
                    "type": column_type,
                    "not_null": bool(not_null),
                    "default": default_value,
                    "primary_key_position": int(primary_key),
                }
            )
            if int(primary_key) > 0:
                entry["primary_key"].append(column_name)

        if table_type == "table":
            entry["row_count"] = int(
                connection.execute(
                    f"SELECT COUNT(*) FROM {quote_identifier(name)}"
                ).fetchone()[0]
            )
            entry["sample_rows"] = _sample_rows(connection, name, SAMPLE_ROWS)

        for _seq, index_name, unique, origin, partial in connection.execute(
            f"PRAGMA index_list({quote_identifier(name)})"
        ):
            index_columns = [
                row[2]
                for row in connection.execute(
                    f"PRAGMA index_info({quote_identifier(index_name)})"
                )
            ]
            index_sql = connection.execute(
                "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = ?",
                (index_name,),
            ).fetchone()
            entry["indexes"].append(
                {
                    "name": index_name,
                    "unique": bool(unique),
                    "origin": origin,
                    "partial": bool(partial),
                    "columns": index_columns,
                    "sql": index_sql[0] if index_sql else None,
                    "distinct_count": _index_distinct_count(connection, name, index_columns),
                }
            )

        if name == "sqlite_sequence":
            entry["notes"].append(
                "Tabela interna do SQLite (AUTOINCREMENT). Nao representa identidade de "
                "negocio e nao deve ser importada nem reaproveitada."
            )
        if table_type == "table":
            tables.append(entry)
        else:
            views.append(entry)

    encoding = connection.execute("PRAGMA encoding").fetchone()
    page_size = connection.execute("PRAGMA page_size").fetchone()
    page_count = connection.execute("PRAGMA page_count").fetchone()
    journal_mode = connection.execute("PRAGMA journal_mode").fetchone()
    return {
        "tables": tables,
        "views": views,
        "pragmas": {
            "encoding": encoding[0] if encoding else None,
            "page_size": int(page_size[0]) if page_size else None,
            "page_count": int(page_count[0]) if page_count else None,
            "journal_mode": journal_mode[0] if journal_mode else None,
        },
    }


def _index_distinct_count(
    connection: sqlite3.Connection, table: str, columns: Sequence[str]
) -> int | None:
    if not columns:
        return None
    group_by = ", ".join(quote_identifier(column) for column in columns)
    statement = (
        f"SELECT COUNT(*) FROM (SELECT 1 FROM {quote_identifier(table)} GROUP BY {group_by})"
    )
    try:
        return int(connection.execute(statement).fetchone()[0])
    except sqlite3.DatabaseError:
        return None


def _sample_rows(connection: sqlite3.Connection, table: str, limit: int) -> list[dict]:
    cursor = connection.execute(
        f"SELECT * FROM {quote_identifier(table)} LIMIT ?", (int(limit),)
    )
    column_names = [description[0] for description in cursor.description]
    rows = []
    for row in cursor.fetchall()[: int(limit)]:
        rows.append(
            {column: truncate_value(value) for column, value in zip(column_names, row)}
        )
    return rows


def collect_duplicates(
    connection: sqlite3.Connection,
    table: str,
    columns: Sequence[str],
    max_samples: int,
) -> dict:
    group_by = ", ".join(quote_identifier(column) for column in columns)
    select_columns = ", ".join(quote_identifier(column) for column in columns)
    total = int(
        connection.execute(
            "SELECT COUNT(*) FROM ("
            f"SELECT 1 FROM {quote_identifier(table)} GROUP BY {group_by} "
            "HAVING COUNT(*) > 1)"
        ).fetchone()[0]
    )
    samples: list[dict] = []
    if total:
        order = ", ".join(str(position + 1) for position in range(len(columns)))
        rows = connection.execute(
            f"SELECT {select_columns}, COUNT(*) AS duplicatas "
            f"FROM {quote_identifier(table)} GROUP BY {group_by} "
            f"HAVING COUNT(*) > 1 ORDER BY {order} LIMIT ?",
            (int(max_samples),),
        ).fetchall()
        for row in rows:
            sample = {
                column: truncate_value(value)
                for column, value in zip(columns, row[:-1])
            }
            sample["rows"] = int(row[-1])
            samples.append(sample)
    return {
        "table": table,
        "key": list(columns),
        "duplicate_groups": total,
        "samples": samples,
    }


def collect_distribution(
    connection: sqlite3.Connection, table: str, columns: Sequence[str]
) -> dict[str, int]:
    """Frequencia das colunas informadas, limitada a ordenacao deterministica."""
    select_columns = ", ".join(quote_identifier(column) for column in columns)
    order = ", ".join(str(position + 1) for position in range(len(columns)))
    rows = connection.execute(
        f"SELECT {select_columns}, COUNT(*) FROM {quote_identifier(table)} "
        f"GROUP BY {select_columns} ORDER BY {order}"
    ).fetchall()
    distribution: dict[str, int] = {}
    for row in rows:
        key = "|".join("(null)" if value is None else str(value) for value in row[:-1])
        distribution[key] = int(row[-1])
    return distribution


# ---------------------------------------------------------------------------
# Passadas de qualidade
# ---------------------------------------------------------------------------


@dataclass
class NeighborhoodPass:
    quality: DimensionStats
    coordinate_stats: CoordinateStats
    text_stats: dict


@dataclass
class StreetPass:
    quality: DimensionStats
    coordinate_stats: CoordinateStats
    text_stats: dict
    zip_stats: ZipStats | None


def _text_stats_for(
    mapping: TableMapping,
    field_names: Sequence[str],
    accumulator: dict,
    context_fields: tuple,
) -> None:
    for field_name in field_names:
        if mapping.has(field_name):
            accumulator[field_name] = TextFieldStats(
                field=field_name,
                column=mapping.columns[field_name],
                context_fields=context_fields,
            )


def consume_neighborhoods(
    connection: sqlite3.Connection,
    mapping: TableMapping,
    accumulator: CentroidAccumulator,
    *,
    chunk_size: int,
    max_samples: int,
) -> NeighborhoodPass:
    text_stats: dict[str, TextFieldStats] = {}
    _text_stats_for(
        mapping,
        ("uf", "municipality_ibge_code", "locality"),
        text_stats,
        NEIGHBORHOOD_CONTEXT_FIELDS,
    )
    quality = DimensionStats(context_fields=NEIGHBORHOOD_CONTEXT_FIELDS)
    coordinate_stats = CoordinateStats(
        scope="bairros.latitude_central/longitude_central",
        context_fields=NEIGHBORHOOD_CONTEXT_FIELDS,
    )
    index = {name: position for position, name in enumerate(mapping.select_fields)}
    observers = [(index[field_name], stats) for field_name, stats in text_stats.items()]
    has_centroid = mapping.has("neighborhood_centroid_latitude") and mapping.has(
        "neighborhood_centroid_longitude"
    )
    uf_position = index["uf"]
    municipality_position = index["municipality_ibge_code"]
    name_position = index["locality"]
    centroid_latitude_position = (
        index["neighborhood_centroid_latitude"] if has_centroid else None
    )
    centroid_longitude_position = (
        index["neighborhood_centroid_longitude"] if has_centroid else None
    )

    for chunk in stream_rows(connection, mapping, chunk_size):
        for row in chunk:
            uf = row[uf_position]
            municipality = row[municipality_position]
            name = row[name_position]
            latitude = (
                row[centroid_latitude_position]
                if centroid_latitude_position is not None
                else None
            )
            longitude = (
                row[centroid_longitude_position]
                if centroid_longitude_position is not None
                else None
            )
            context = (uf, municipality, name)
            for position, stats in observers:
                stats.observe(row[position], context, max_samples)
            quality.observe(uf, municipality, context, max_samples)
            _, lat_value, lon_value = coordinate_stats.observe(
                latitude, longitude, context, max_samples
            )

            uf_code, _ = classify_uf_cached(uf)
            municipality_code, _ = classify_municipality_cached(municipality)
            if (
                uf_code is None
                or municipality_code is None
                or not isinstance(name, str)
                or not name.strip()
            ):
                continue
            accumulator.register_neighborhood(
                uf_code,
                municipality_code,
                name,
                lat_value,
                lon_value,
                context=context,
            )
    return NeighborhoodPass(
        quality=quality, coordinate_stats=coordinate_stats, text_stats=text_stats
    )


def consume_streets(
    connection: sqlite3.Connection,
    mapping: TableMapping,
    accumulator: CentroidAccumulator,
    *,
    chunk_size: int,
    max_samples: int,
) -> StreetPass:
    text_stats: dict[str, TextFieldStats] = {}
    _text_stats_for(
        mapping,
        ("uf", "municipality_ibge_code", "neighborhood_name", "street_name", "zip_code"),
        text_stats,
        STREET_CONTEXT_FIELDS,
    )
    quality = DimensionStats(context_fields=STREET_CONTEXT_FIELDS)
    coordinate_stats = CoordinateStats(
        scope="ruas.latitude/longitude", context_fields=STREET_CONTEXT_FIELDS
    )
    zip_stats = (
        ZipStats(column=mapping.columns["zip_code"])
        if mapping.has("zip_code")
        else None
    )
    index = {name: position for position, name in enumerate(mapping.select_fields)}
    observers = [(index[field_name], stats) for field_name, stats in text_stats.items()]
    uf_position = index["uf"]
    municipality_position = index["municipality_ibge_code"]
    name_position = index["neighborhood_name"]
    street_position = index["street_name"]
    latitude_position = index["latitude"]
    longitude_position = index["longitude"]
    zip_position = index["zip_code"] if mapping.has("zip_code") else None

    for chunk in stream_rows(connection, mapping, chunk_size):
        for row in chunk:
            uf = row[uf_position]
            municipality = row[municipality_position]
            neighborhood_name = row[name_position]
            street_name = row[street_position]
            latitude = row[latitude_position]
            longitude = row[longitude_position]
            context = (uf, municipality, neighborhood_name, street_name)
            for position, stats in observers:
                stats.observe(row[position], context, max_samples)
            quality.observe(uf, municipality, context, max_samples)
            if zip_stats is not None:
                zip_stats.observe(row[zip_position], context, max_samples)
            (
                coordinate_code,
                lat_value,
                lon_value,
            ) = coordinate_stats.observe(latitude, longitude, context, max_samples)
            if coordinate_code != COORD_VALID:
                continue
            uf_code, _ = classify_uf_cached(uf)
            municipality_code, _ = classify_municipality_cached(municipality)
            if (
                uf_code is None
                or municipality_code is None
                or not isinstance(neighborhood_name, str)
                or not neighborhood_name.strip()
            ):
                continue
            assert lat_value is not None and lon_value is not None
            accumulator.add_point(
                uf_code, municipality_code, neighborhood_name, lat_value, lon_value
            )
    return StreetPass(
        quality=quality,
        coordinate_stats=coordinate_stats,
        text_stats=text_stats,
        zip_stats=zip_stats,
    )


# ---------------------------------------------------------------------------
# Montagem dos relatorios
# ---------------------------------------------------------------------------


@dataclass
class DiscoveryResult:
    schema_report: dict
    centroid_report: dict
    duration_seconds: float


def _canonical_fingerprint(schema_report: dict, centroid_report: dict) -> str:
    payload = {
        "schema": {
            key: value
            for key, value in schema_report.items()
            if key not in FINGERPRINT_EXCLUDED_KEYS
        },
        "centroid": {
            key: value
            for key, value in centroid_report.items()
            if key not in FINGERPRINT_EXCLUDED_KEYS
        },
    }
    canonical = json.dumps(
        payload, ensure_ascii=False, sort_keys=True, separators=(",", ":")
    )
    return hashlib.sha256(canonical.encode("utf-8")).hexdigest()


def _resolve_logical_path(resolved: Path, override: str | None) -> str:
    if override:
        return override.replace("\\", "/")
    try:
        return resolved.relative_to(Path.cwd().resolve()).as_posix()
    except ValueError:
        return resolved.name


def discover(
    sqlite_path: str | Path,
    *,
    source_version: str = DEFAULT_SOURCE_VERSION,
    tolerance_m: float = DEFAULT_CENTROID_TOLERANCE_M,
    chunk_size: int = DEFAULT_CHUNK_SIZE,
    low_sample_threshold: int = DEFAULT_LOW_SAMPLE_THRESHOLD,
    max_samples: int = DEFAULT_MAX_SAMPLES,
    logical_path: str | None = None,
    generated_at_utc: str | None = None,
    progress: Callable[[str], None] | None = None,
) -> DiscoveryResult:
    """Executa a descoberta e devolve os relatorios em memoria."""
    if not math.isfinite(tolerance_m) or tolerance_m < 0:
        raise ValueError("A tolerancia de centroide deve ser finita e >= 0")
    if low_sample_threshold < 1:
        raise ValueError("low_sample_threshold deve ser >= 1")
    if max_samples < 0:
        raise ValueError("max_samples deve ser >= 0")
    if chunk_size <= 0:
        raise ValueError("chunk_size deve ser positivo")

    started = time.perf_counter()
    resolved = Path(sqlite_path).expanduser().resolve()
    if not resolved.is_file():
        raise FileNotFoundError(f"SQLite nao encontrado: {resolved}")
    say = progress or (lambda _message: None)
    timings: dict[str, float] = {}
    mark = time.perf_counter()

    def lap(name: str) -> None:
        nonlocal mark
        now = time.perf_counter()
        timings[name] = round(now - mark, 3)
        mark = now

    say("calculando checksum inicial")
    stat_before = resolved.stat()
    sha256_before = sha256_file(resolved)
    lap("checksum_initial")

    connection = open_readonly_connection(resolved)
    try:
        say("lendo inventario de schema")
        inventory = collect_schema_inventory(connection)
        lap("schema_inventory")

        say("resolvendo mapeamento confirmado")
        neighborhood_mapping = resolve_table_mapping(
            connection,
            role="neighborhoods",
            table=NEIGHBORHOODS_TABLE,
            fields=NEIGHBORHOOD_FIELDS,
            required=NEIGHBORHOOD_REQUIRED,
            optional=NEIGHBORHOOD_OPTIONAL,
        )
        street_mapping = resolve_table_mapping(
            connection,
            role="streets",
            table=STREETS_TABLE,
            fields=STREET_FIELDS,
            required=STREET_REQUIRED,
            optional=STREET_OPTIONAL,
        )
        lap("mapping_resolution")

        say("coletando distribuicoes de UF e municipio")
        distributions = {
            "uf_bairros": collect_distribution(
                connection,
                neighborhood_mapping.table,
                [neighborhood_mapping.columns["uf"]],
            ),
            "uf_ruas": collect_distribution(
                connection, street_mapping.table, [street_mapping.columns["uf"]]
            ),
            "municipality_bairros": collect_distribution(
                connection,
                neighborhood_mapping.table,
                [neighborhood_mapping.columns["municipality_ibge_code"]],
            ),
            "municipality_ruas": collect_distribution(
                connection,
                street_mapping.table,
                [street_mapping.columns["municipality_ibge_code"]],
            ),
        }
        lap("distributions")

        say("verificando duplicidades por chave candidata")
        street_key_fields = (
            "uf",
            "municipality_ibge_code",
            "neighborhood_name",
            "street_name",
        )
        street_key_columns = [
            street_mapping.columns[field_name]
            for field_name in street_key_fields
            if street_mapping.has(field_name)
        ]
        if street_mapping.has("zip_code"):
            street_key_columns.append(street_mapping.columns["zip_code"])
        duplicates = {
            "neighborhoods": collect_duplicates(
                connection,
                neighborhood_mapping.table,
                [
                    neighborhood_mapping.columns["uf"],
                    neighborhood_mapping.columns["municipality_ibge_code"],
                    neighborhood_mapping.columns["locality"],
                ],
                max_samples,
            ),
            "streets_natural_key": collect_duplicates(
                connection, street_mapping.table, street_key_columns, max_samples
            ),
            "streets_point_key": collect_duplicates(
                connection,
                street_mapping.table,
                street_key_columns
                + [
                    street_mapping.columns["latitude"],
                    street_mapping.columns["longitude"],
                ],
                max_samples,
            ),
        }
        lap("duplicates")

        say("agregando bairros")
        accumulator = CentroidAccumulator(max_samples=max_samples)
        neighborhoods_pass = consume_neighborhoods(
            connection,
            neighborhood_mapping,
            accumulator,
            chunk_size=chunk_size,
            max_samples=max_samples,
        )
        lap("neighborhoods_pass")
        say(
            f"{accumulator.neighborhood_rows} linhas de bairros, "
            f"{len(accumulator)} chaves distintas"
        )

        say("percorrendo ruas e agregando pontos por bairro")
        streets_pass = consume_streets(
            connection,
            street_mapping,
            accumulator,
            chunk_size=chunk_size,
            max_samples=max_samples,
        )
        lap("streets_pass")
        say(f"{streets_pass.quality.rows} linhas de ruas processadas")
    finally:
        connection.close()

    say("comparando centroides materializados")
    comparison = compare_centroids(
        accumulator,
        tolerance_m=tolerance_m,
        max_samples=max_samples,
        low_sample_threshold=low_sample_threshold,
        points_invalid_excluded=streets_pass.coordinate_stats.invalid,
    )
    lap("centroid_comparison")

    say("recalculando checksum para provar que o arquivo nao mudou")
    stat_after = resolved.stat()
    sha256_after = sha256_file(resolved)
    unchanged = (
        sha256_before == sha256_after
        and stat_before.st_size == stat_after.st_size
        and stat_before.st_mtime_ns == stat_after.st_mtime_ns
    )
    if not unchanged:
        raise RuntimeError(
            "O arquivo SQLite mudou durante a execucao; a analise nao e confiavel."
        )
    lap("checksum_final")

    generated_at = generated_at_utc or utc_now_iso()
    logical = _resolve_logical_path(resolved, logical_path)

    schema_report = _build_schema_report(
        inventory=inventory,
        resolved=resolved,
        logical_path=logical,
        stat=stat_after,
        sha256=sha256_after,
        sha256_before=sha256_before,
        unchanged=unchanged,
        source_version=source_version,
        generated_at=generated_at,
        chunk_size=chunk_size,
        max_samples=max_samples,
        tolerance_m=tolerance_m,
        low_sample_threshold=low_sample_threshold,
        neighborhood_mapping=neighborhood_mapping,
        street_mapping=street_mapping,
        neighborhoods_pass=neighborhoods_pass,
        streets_pass=streets_pass,
        accumulator=accumulator,
        duplicates=duplicates,
        distributions=distributions,
        comparison=comparison,
    )
    centroid_report = _build_centroid_report(
        resolved=resolved,
        logical_path=logical,
        stat=stat_after,
        sha256=sha256_after,
        source_version=source_version,
        generated_at=generated_at,
        tolerance_m=tolerance_m,
        low_sample_threshold=low_sample_threshold,
        chunk_size=chunk_size,
        accumulator=accumulator,
        streets_pass=streets_pass,
        comparison=comparison,
        neighborhood_mapping=neighborhood_mapping,
        street_mapping=street_mapping,
    )

    fingerprint = _canonical_fingerprint(schema_report, centroid_report)
    schema_report["data_fingerprint"] = fingerprint
    centroid_report["data_fingerprint"] = fingerprint

    duration = time.perf_counter() - started
    performance = {
        "duration_seconds": round(duration, 3),
        "peak_memory_bytes": peak_memory_bytes(),
        "chunk_size": chunk_size,
        "phase_seconds": timings,
        "streaming_passes": {
            "bairros": 1,
            "ruas": 1,
        },
        "fingerprint_excludes": ["report.generated_at_utc", "performance"],
    }
    schema_report["performance"] = performance
    centroid_report["performance"] = performance

    return DiscoveryResult(
        schema_report=schema_report,
        centroid_report=centroid_report,
        duration_seconds=duration,
    )


def _build_schema_report(
    *,
    inventory: dict,
    resolved: Path,
    logical_path: str,
    stat,
    sha256: str,
    sha256_before: str,
    unchanged: bool,
    source_version: str,
    generated_at: str,
    chunk_size: int,
    max_samples: int,
    tolerance_m: float,
    low_sample_threshold: int,
    neighborhood_mapping: TableMapping,
    street_mapping: TableMapping,
    neighborhoods_pass: NeighborhoodPass,
    streets_pass: StreetPass,
    accumulator: CentroidAccumulator,
    duplicates: dict,
    distributions: dict,
    comparison: CentroidComparison,
) -> dict:
    tables_by_name = {table["name"]: table for table in inventory["tables"]}
    neighborhood_rows = tables_by_name[neighborhood_mapping.table]["row_count"]
    street_rows = tables_by_name[street_mapping.table]["row_count"]

    uf_bairros = distributions["uf_bairros"]
    uf_ruas = distributions["uf_ruas"]
    uf_codes = sorted(code for code in uf_bairros if code != "(null)")
    municipalities_by_uf: Counter = Counter()
    for code in distributions["municipality_bairros"]:
        if code != "(null)":
            municipalities_by_uf[code[:2]] += 1

    uf_table = [
        {
            "uf_codigo": code,
            "uf_sigla": STATE_CODE_TO_SIGLA.get(code),
            "neighborhoods": uf_bairros.get(code, 0),
            "streets": uf_ruas.get(code, 0),
            "municipalities": municipalities_by_uf.get(code, 0),
        }
        for code in uf_codes
    ]

    municipality_keys = [
        key for key in distributions["municipality_bairros"] if key != "(null)"
    ]
    municipality_count = len(municipality_keys)
    municipality_normalized_distinct = len(neighborhoods_pass.quality.municipality_codes)

    measured = {
        "neighborhood_rows": neighborhood_rows,
        "street_rows": street_rows,
        "uf_count": len(uf_codes),
        "municipality_count": municipality_count,
        "neighborhoods_without_streets": comparison.without_points,
        "duplicate_neighborhood_keys": duplicates["neighborhoods"]["duplicate_groups"],
    }
    baseline = {
        "reference": BASELINE["reference"],
        "measured": measured,
        "expected": {
            "neighborhood_rows": BASELINE["neighborhood_rows"],
            "street_rows": BASELINE["street_rows"],
            "uf_count": BASELINE["uf_count"],
            "municipality_count": BASELINE["municipality_count"],
            "neighborhoods_without_streets": BASELINE["neighborhoods_without_streets"],
            "duplicate_neighborhood_keys": BASELINE["duplicate_neighborhood_keys"],
        },
    }
    baseline["checks"] = {
        key: baseline["measured"][key] == baseline["expected"][key]
        for key in baseline["expected"]
    }
    baseline["matches"] = all(baseline["checks"].values())

    coverage = {
        "neighborhoods": sum(comparison.neighborhoods_by_uf.values()),
        "neighborhoods_with_valid_points": len(accumulator) - comparison.without_points,
        "neighborhoods_without_points": comparison.without_points,
        "neighborhoods_without_points_samples": comparison.without_points_samples,
        "street_groups": len(accumulator) + len(accumulator.orphan_groups),
        "street_groups_without_neighborhood": len(accumulator.orphan_groups),
        "street_groups_without_neighborhood_truncated": accumulator.orphan_groups_truncated,
        "street_groups_without_neighborhood_samples": accumulator.orphan_samples,
        "street_rows_without_neighborhood": accumulator.orphan_rows,
        "municipalities_in_bairros": len(neighborhoods_pass.quality.municipality_codes),
        "municipalities_in_ruas": len(streets_pass.quality.municipality_codes),
        "municipalities_only_in_bairros": sorted(
            neighborhoods_pass.quality.municipality_codes
            - streets_pass.quality.municipality_codes
        )[:max_samples],
        "municipalities_only_in_ruas": sorted(
            streets_pass.quality.municipality_codes
            - neighborhoods_pass.quality.municipality_codes
        )[:max_samples],
    }

    return {
        "report": {
            "kind": "cnefe-schema-discovery",
            "specification": "imports/cnefe/prompt.txt v2.1 (secoes 3.2, 3.3 e 10)",
            "generated_at_utc": generated_at,
            "tool": "backend/scripts/import/cnefe_discover_schema.py",
            "python_version": sys.version.split()[0],
            "sqlite_version": sqlite3.sqlite_version,
            "access_mode": "read-only (mode=ro, PRAGMA query_only=1)",
            "chunk_size": chunk_size,
            "max_samples": max_samples,
        },
        "source": {
            "file_name": resolved.name,
            "logical_path": logical_path,
            "source_version": source_version,
            "size_bytes": int(stat.st_size),
            "size_human": human_size(stat.st_size),
            "modified_at_utc": timestamp_to_utc_iso(stat.st_mtime),
            "sha256": sha256,
            "sha256_before_run": sha256_before,
            "sha256_after_run": sha256,
            "unchanged_after_run": unchanged,
        },
        "format": inventory["pragmas"],
        "tables": inventory["tables"],
        "views": inventory["views"],
        "mapping": {
            "neighborhoods_table": neighborhood_mapping.table,
            "streets_table": street_mapping.table,
            "neighborhoods_columns": dict(neighborhood_mapping.columns),
            "streets_columns": dict(street_mapping.columns),
            "missing_optional_neighborhoods": list(neighborhood_mapping.missing_optional),
            "missing_optional_streets": list(street_mapping.missing_optional),
        },
        "dimensions": {
            "uf": {
                "distinct": len(uf_codes),
                "expected": BASELINE["uf_count"],
                "matches_expected": len(uf_codes) == BASELINE["uf_count"],
                "format": _dominant_format(neighborhoods_pass.quality.uf_formats),
                "formats_bairros": dict(sorted(neighborhoods_pass.quality.uf_formats.items())),
                "formats_ruas": dict(sorted(streets_pass.quality.uf_formats.items())),
                "values": uf_table,
                "invalid_bairros": neighborhoods_pass.quality.uf_invalid_count,
                "invalid_ruas": streets_pass.quality.uf_invalid_count,
                "invalid_samples_bairros": neighborhoods_pass.quality.uf_invalid,
                "invalid_samples_ruas": streets_pass.quality.uf_invalid,
            },
            "municipality": {
                "distinct_raw": municipality_count,
                "distinct_normalized": municipality_normalized_distinct,
                "expected": BASELINE["municipality_count"],
                "matches_expected": (
                    municipality_count == BASELINE["municipality_count"]
                    and municipality_normalized_distinct
                    == BASELINE["municipality_count"]
                ),
                "normalized_values_bairros": neighborhoods_pass.quality.municipality_normalized,
                "normalized_values_ruas": streets_pass.quality.municipality_normalized,
                "invalid_bairros": neighborhoods_pass.quality.municipality_invalid_count,
                "invalid_ruas": streets_pass.quality.municipality_invalid_count,
                "invalid_samples_bairros": neighborhoods_pass.quality.municipality_invalid,
                "invalid_samples_ruas": streets_pass.quality.municipality_invalid,
                "prefix_mismatch_bairros": (
                    neighborhoods_pass.quality.municipality_prefix_mismatch
                ),
                "prefix_mismatch_ruas": streets_pass.quality.municipality_prefix_mismatch,
                "prefix_mismatch_samples": (
                    neighborhoods_pass.quality.municipality_prefix_samples
                    + streets_pass.quality.municipality_prefix_samples
                )[:max_samples],
            },
        },
        "quality": {
            "bairros": {
                "table": neighborhood_mapping.table,
                "rows": neighborhood_rows,
                "columns": {
                    field_name: stats.as_dict()
                    for field_name, stats in neighborhoods_pass.text_stats.items()
                },
                "coordinates": neighborhoods_pass.coordinate_stats.as_dict(),
            },
            "ruas": {
                "table": street_mapping.table,
                "rows": street_rows,
                "columns": {
                    field_name: stats.as_dict()
                    for field_name, stats in streets_pass.text_stats.items()
                },
                "coordinates": streets_pass.coordinate_stats.as_dict(),
                "zip_codes": (
                    streets_pass.zip_stats.as_dict() if streets_pass.zip_stats else None
                ),
            },
            "duplicates": duplicates,
            "coverage": coverage,
        },
        "centroid_validation": {
            "report_file": "centroid-validation.json",
            "method": "ADDRESS_POINT_MEAN",
            "tolerance_m": tolerance_m,
            "low_sample_threshold": low_sample_threshold,
            "compared_neighborhoods": comparison.compared,
            "max_error_m": round_float(
                max(comparison.errors) if len(comparison.errors) else None
            ),
            "above_tolerance": comparison.above_tolerance,
            "neighborhoods_without_points": comparison.without_points,
        },
        "baseline": baseline,
        "samples": {
            table["name"]: {
                "limit": len(table["sample_rows"]),
                "rows": table["sample_rows"],
            }
            for table in inventory["tables"]
        },
        "notes": [
            "Nenhuma escrita foi executada no SQLite: mode=ro + PRAGMA query_only=1 e "
            "checksum SHA-256 identico antes e depois da execucao.",
            "Cada linha de ruas e um registro por logradouro; medias de coordenadas tem "
            "peso por logradouro, nunca por domicilio.",
            "A comparacao de centroides usa uma unica soma consolidada por "
            "(uf_codigo, municipio_codigo, bairro); nao existe media de medias.",
            "Valores de uf_codigo no artefato atual sao codigos IBGE de 2 digitos; a "
            "conversao para sigla e apenas de exibicao.",
            "Amostras sao limitadas e contem apenas identificadores tecnicos e nomes "
            "geograficos; nenhum dado pessoal e coletado.",
        ],
    }


def _build_centroid_report(
    *,
    resolved: Path,
    logical_path: str,
    stat,
    sha256: str,
    source_version: str,
    generated_at: str,
    tolerance_m: float,
    low_sample_threshold: int,
    chunk_size: int,
    accumulator: CentroidAccumulator,
    streets_pass: StreetPass,
    comparison: CentroidComparison,
    neighborhood_mapping: TableMapping,
    street_mapping: TableMapping,
) -> dict:
    overall = summarize_distribution(comparison.errors)
    by_uf = []
    for uf in sorted(comparison.neighborhoods_by_uf):
        uf_errors = comparison.errors_by_uf.get(uf, array.array("d"))
        stats = summarize_distribution(uf_errors)
        above = sum(1 for error in uf_errors if error > tolerance_m)
        by_uf.append(
            {
                "uf_codigo": uf,
                "uf_sigla": STATE_CODE_TO_SIGLA.get(uf),
                "neighborhoods": comparison.neighborhoods_by_uf[uf],
                "compared": stats["count"],
                "above_tolerance": above,
                "min_m": round_float(stats["min"]),
                "median_m": round_float(stats["median"]),
                "p90_m": round_float(stats["p90"]),
                "p99_m": round_float(stats["p99"]),
                "max_m": round_float(stats["max"]),
                "mean_m": round_float(stats["mean"]),
            }
        )

    provenance = (
        "materialized_and_validated"
        if comparison.above_tolerance == 0 and comparison.compared > 0
        else "materialized_with_divergences"
    )

    return {
        "report": {
            "kind": "cnefe-centroid-validation",
            "specification": "imports/cnefe/prompt.txt v2.1 (secoes 3.3.5, 5.1 e 10)",
            "generated_at_utc": generated_at,
            "tool": "backend/scripts/import/cnefe_discover_schema.py",
            "access_mode": "read-only (mode=ro, PRAGMA query_only=1)",
            "chunk_size": chunk_size,
        },
        "source": {
            "file_name": resolved.name,
            "logical_path": logical_path,
            "source_version": source_version,
            "size_bytes": int(stat.st_size),
            "sha256": sha256,
            "neighborhoods_table": neighborhood_mapping.table,
            "streets_table": street_mapping.table,
        },
        "method": {
            "id": "ADDRESS_POINT_MEAN",
            "description": (
                "Media aritmetica direta de latitude e longitude dos pontos de ruas "
                "agrupados por (uf_codigo, municipio_codigo, bairro), comparada com "
                "latitude_central/longitude_central de bairros por distancia Haversine "
                "em linha reta."
            ),
            "aggregation_key": [
                neighborhood_mapping.columns["uf"],
                neighborhood_mapping.columns["municipality_ibge_code"],
                neighborhood_mapping.columns["locality"],
            ],
            "no_average_of_averages": True,
            "summation": "kahan_compensated",
            "distance": "haversine",
            "distance_radius_m": EARTH_RADIUS_M,
            "tolerance_m": tolerance_m,
            "valid_point_rules": [
                "latitude e longitude nao nulas",
                "valores finitos",
                "diferentes de (0,0)",
                "latitude em [-90, 90] e longitude em [-180, 180]",
                "dentro da envoltoria generosa do Brasil "
                f"(lat {BRAZIL_ENVELOPE['min_latitude']}..{BRAZIL_ENVELOPE['max_latitude']}, "
                f"lon {BRAZIL_ENVELOPE['min_longitude']}..{BRAZIL_ENVELOPE['max_longitude']})",
            ],
            "invalid_points_excluded": comparison.points_invalid_excluded,
            "granularity_note": (
                "Cada linha de ruas e um registro por logradouro, nao por domicilio. "
                "A media tem peso por logradouro e nao deve ser descrita como media "
                "por domicilio."
            ),
            "provenance": provenance,
        },
        "totals": {
            "neighborhoods": sum(comparison.neighborhoods_by_uf.values()),
            "neighborhoods_with_valid_points": len(accumulator) - comparison.without_points,
            "neighborhoods_without_points": comparison.without_points,
            "neighborhoods_with_materialized_centroid": (
                len(accumulator) - comparison.missing_centroid - comparison.without_points
            ),
            "neighborhoods_missing_centroid": comparison.missing_centroid,
            "neighborhoods_compared": comparison.compared,
            "street_groups": len(accumulator) + len(accumulator.orphan_groups),
            "street_groups_without_neighborhood": len(accumulator.orphan_groups),
            "street_groups_without_neighborhood_truncated": (
                accumulator.orphan_groups_truncated
            ),
            "points_valid": streets_pass.coordinate_stats.counts[COORD_VALID],
            "points_invalid": streets_pass.coordinate_stats.invalid,
            "low_sample_threshold": low_sample_threshold,
            "neighborhoods_below_low_sample_threshold": comparison.low_sample,
            "duplicate_neighborhood_keys": accumulator.duplicate_neighborhood_rows,
        },
        "error_meters": {
            "count": overall["count"],
            "min": round_float(overall["min"]),
            "median": round_float(overall["median"]),
            "p90": round_float(overall["p90"]),
            "p99": round_float(overall["p99"]),
            "max": round_float(overall["max"]),
            "mean": round_float(overall["mean"]),
        },
        "above_tolerance": {
            "tolerance_m": tolerance_m,
            "count": comparison.above_tolerance,
            "samples": comparison.above_tolerance_samples,
        },
        "by_uf": by_uf,
        "without_points": {
            "count": comparison.without_points,
            "samples": comparison.without_points_samples,
        },
        "missing_centroid": {
            "count": comparison.missing_centroid,
            "samples": comparison.missing_centroid_samples,
        },
        "low_sample": {
            "threshold": low_sample_threshold,
            "count": comparison.low_sample,
            "samples": comparison.low_sample_samples,
        },
        "orphan_street_groups": {
            "count": len(accumulator.orphan_groups),
            "truncated": accumulator.orphan_groups_truncated,
            "rows": accumulator.orphan_rows,
            "samples": accumulator.orphan_samples,
        },
        "duplicate_neighborhood_key_samples": accumulator.duplicate_neighborhood_samples,
        "conclusion": (
            "Todos os bairros comparados estao dentro da tolerancia configurada."
            if comparison.above_tolerance == 0
            else "Existem bairros com divergencia acima da tolerancia; a publicacao "
            "desses bairros deve ser bloqueada (COORD_CENTROID_MISMATCH)."
        ),
    }


# ---------------------------------------------------------------------------
# Renderizacao markdown e escrita
# ---------------------------------------------------------------------------


def _cell(value: object) -> str:
    if value is None:
        return "n/d"
    text = str(value).replace("|", "\\|").replace("\n", " ").replace("\r", " ")
    return text or "n/d"


def _md_table(headers: Sequence[str], rows: Sequence[Sequence[object]]) -> str:
    lines = ["| " + " | ".join(_cell(header) for header in headers) + " |"]
    lines.append("|" + "|".join("---" for _ in headers) + "|")
    for row in rows:
        lines.append("| " + " | ".join(_cell(value) for value in row) + " |")
    return "\n".join(lines)


def _fmt_int(value: int | None) -> str:
    if value is None:
        return "n/d"
    return f"{int(value):,}".replace(",", ".")


def _fmt_float(value: float | None, digits: int = 4) -> str:
    if value is None:
        return "n/d"
    return f"{float(value):.{digits}f}"


def _status(flag: bool) -> str:
    return "OK" if flag else "DIVERGENTE"


def render_schema_markdown(schema_report: dict, centroid_report: dict) -> str:
    """Renderiza o relatorio Markdown a partir dos dois relatorios JSON.

    O documento e gerado por secoes numeradas na mesma ordem em que aparecem
    no arquivo final; a saida e deterministica para o mesmo par de relatorios.
    """
    report = schema_report["report"]
    source = schema_report["source"]
    quality = schema_report["quality"]
    dimensions = schema_report["dimensions"]
    baseline = schema_report["baseline"]
    coverage = quality["coverage"]
    centroid = schema_report["centroid_validation"]
    performance = schema_report.get("performance", {})

    lines: list[str] = []
    lines.append("# Descoberta e validacao do schema CNEFE (somente leitura)")
    lines.append("")
    lines.append(
        f"- **Especificacao:** `{report['specification']}`"
    )
    lines.append(f"- **Gerado em (UTC):** `{report['generated_at_utc']}`")
    lines.append(f"- **Script:** `{report['tool']}`")
    lines.append(f"- **Modo de acesso:** {report['access_mode']}")
    lines.append(f"- **Versao da fonte:** `{source['source_version']}`")
    lines.append(f"- **Fingerprint dos dados:** `{schema_report['data_fingerprint']}`")
    lines.append(
        "- **Fingerprint exclui:** timestamps de execucao e metricas de performance; "
        "os demais valores sao deterministicos."
    )
    lines.append("")

    lines.append("## 1. Arquivo de origem")
    lines.append("")
    lines.append(
        _md_table(
            ("Propriedade", "Valor"),
            (
                ("Arquivo", f"`{source['file_name']}`"),
                ("Caminho logico", f"`{source['logical_path']}`"),
                ("Tamanho", f"{_fmt_int(source['size_bytes'])} bytes ({source['size_human']})"),
                ("Modificado em (UTC)", source["modified_at_utc"]),
                ("Encoding declarado", schema_report["format"].get("encoding")),
                ("Page size", schema_report["format"].get("page_size")),
                ("Page count", schema_report["format"].get("page_count")),
                ("Journal mode", schema_report["format"].get("journal_mode")),
                ("SHA-256 (antes)", f"`{source['sha256_before_run']}`"),
                ("SHA-256 (depois)", f"`{source['sha256_after_run']}`"),
                (
                    "Arquivo inalterado",
                    f"**{_status(bool(source['unchanged_after_run']))}**",
                ),
            ),
        )
    )
    lines.append("")
    lines.append(
        "Nenhum `INSERT`, `UPDATE`, `DELETE`, `VACUUM`, `CREATE` ou `DROP` foi "
        "executado. A conexao usa `mode=ro` e `PRAGMA query_only=1`, e o checksum "
        "foi confirmado identico antes e depois da execucao."
    )
    lines.append("")

    lines.append("## 2. Inventario de schema")
    lines.append("")
    lines.append("### 2.1 Tabelas")
    lines.append("")
    lines.append(
        _md_table(
            ("Tabela", "Linhas", "Colunas", "Chave primaria", "Indices"),
            (
                (
                    table["name"],
                    _fmt_int(table["row_count"]),
                    len(table["columns"]),
                    ", ".join(table["primary_key"]) or "n/d",
                    ", ".join(index["name"] for index in table["indexes"]) or "n/d",
                )
                for table in schema_report["tables"]
            ),
        )
    )
    lines.append("")
    if schema_report["views"]:
        lines.append(
            "Views encontradas: "
            + ", ".join(view["name"] for view in schema_report["views"])
        )
        lines.append("")
    else:
        lines.append("Nenhuma view encontrada em `sqlite_master`.")
        lines.append("")

    lines.append("### 2.2 Colunas")
    lines.append("")
    for table in schema_report["tables"]:
        lines.append(f"**`{table['name']}`**")
        lines.append("")
        lines.append(
            _md_table(
                ("#", "Coluna", "Tipo", "NOT NULL", "Default", "PK"),
                (
                    (
                        column["position"],
                        column["name"],
                        column["type"] or "(sem tipo)",
                        "sim" if column["not_null"] else "nao",
                        column["default"],
                        column["primary_key_position"] or "n/d",
                    )
                    for column in table["columns"]
                ),
            )
        )
        lines.append("")
        for note in table.get("notes", []):
            lines.append(f"> {note}")
            lines.append("")

    lines.append("### 2.3 Indices e seletividade")
    lines.append("")
    index_rows = []
    for table in schema_report["tables"]:
        for index in table["indexes"]:
            index_rows.append(
                (
                    index["name"],
                    table["name"],
                    ", ".join(index["columns"]),
                    "sim" if index["unique"] else "nao",
                    _fmt_int(index["distinct_count"]),
                )
            )
    lines.append(
        _md_table(("# Indice", "Tabela", "Colunas", "Unico", "Valores distintos"), index_rows)
        if index_rows
        else "Nenhum indice declarado."
    )
    lines.append("")
    for table in schema_report["tables"]:
        for index in table["indexes"]:
            if index.get("sql"):
                lines.append(f"- `{index['name']}`: `{index['sql']}`")
    lines.append("")

    lines.append("### 2.4 Mapeamento confirmado")
    lines.append("")
    lines.append(
        _md_table(
            ("Campo canonico", "Tabela bairros", "Tabela ruas"),
            (
                (
                    field_name,
                    schema_report["mapping"]["neighborhoods_columns"].get(field_name),
                    schema_report["mapping"]["streets_columns"].get(field_name),
                )
                for field_name in sorted(
                    set(schema_report["mapping"]["neighborhoods_columns"])
                    | set(schema_report["mapping"]["streets_columns"])
                )
            ),
        )
    )
    lines.append("")
    optional_neighborhoods = schema_report["mapping"]["missing_optional_neighborhoods"]
    optional_streets = schema_report["mapping"]["missing_optional_streets"]
    if optional_neighborhoods or optional_streets:
        lines.append(
            "Campos opcionais ausentes: "
            + ", ".join(sorted(set(optional_neighborhoods) | set(optional_streets)))
        )
        lines.append("")
    else:
        lines.append(
            "Todos os campos opcionais do mapeamento estao presentes no artefato."
        )
        lines.append("")

    lines.append("## 3. Amostras (limitadas a 20 linhas por tabela)")
    lines.append("")
    for table in schema_report["tables"]:
        sample = schema_report["samples"].get(table["name"], {"rows": []})
        if not sample["rows"]:
            continue
        lines.append(f"**`{table['name']}`**")
        lines.append("")
        headers = [column["name"] for column in table["columns"]]
        lines.append(
            _md_table(
                headers,
                ([row.get(header) for header in headers] for row in sample["rows"]),
            )
        )
        lines.append("")

    lines.append("## 4. Dimensoes")
    lines.append("")
    uf = dimensions["uf"]
    municipality = dimensions["municipality"]
    lines.append(
        _md_table(
            ("Dimensao", "Distintos", "Esperado (v2.1)", "Situacao", "Formato"),
            (
                (
                    "UF (`uf_codigo`)",
                    _fmt_int(uf["distinct"]),
                    _fmt_int(uf["expected"]),
                    _status(bool(uf["matches_expected"])),
                    uf["format"],
                ),
                (
                    "Municipio (`municipio_codigo`)",
                    _fmt_int(municipality["distinct_raw"]),
                    _fmt_int(municipality["expected"]),
                    _status(bool(municipality["matches_expected"])),
                    "7 digitos IBGE",
                ),
            ),
        )
    )
    lines.append("")
    lines.append(
        _md_table(
            (
                "UF",
                "Sigla",
                "Municipios",
                "Bairros",
                "Ruas",
            ),
            (
                (
                    row["uf_codigo"],
                    row["uf_sigla"],
                    _fmt_int(row["municipalities"]),
                    _fmt_int(row["neighborhoods"]),
                    _fmt_int(row["streets"]),
                )
                for row in uf["values"]
            ),
        )
    )
    lines.append("")
    lines.append(
        f"- Municipios distintos apos normalizacao (bairros): "
        f"{_fmt_int(municipality['distinct_normalized'])}"
    )
    lines.append(
        f"- Valores de municipio que precisaram de normalizacao: bairros="
        f"{_fmt_int(municipality['normalized_values_bairros'])}, ruas="
        f"{_fmt_int(municipality['normalized_values_ruas'])}"
    )
    lines.append(
        f"- Divergencias de prefixo UF x IBGE: bairros="
        f"{_fmt_int(municipality['prefix_mismatch_bairros'])}, ruas="
        f"{_fmt_int(municipality['prefix_mismatch_ruas'])}"
    )
    lines.append(
        f"- Valores de UF invalidos: bairros={_fmt_int(uf['invalid_bairros'])}, "
        f"ruas={_fmt_int(uf['invalid_ruas'])}"
    )
    lines.append("")

    lines.append("## 5. Qualidade")
    lines.append("")
    lines.append("### 5.1 Nulos, vazios e caracteres invalidos")
    lines.append("")
    for table_key in ("bairros", "ruas"):
        block = quality[table_key]
        lines.append(f"**`{block['table']}` ({_fmt_int(block['rows'])} linhas)**")
        lines.append("")
        lines.append(
            _md_table(
                ("Campo", "Coluna", "Nulos", "Vazios", "Nulo ou vazio", "U+FFFD", "Maior comprimento"),
                (
                    (
                        field_name,
                        stats["column"],
                        _fmt_int(stats["null"]),
                        _fmt_int(stats["empty"]),
                        _fmt_int(stats["null_or_empty"]),
                        _fmt_int(stats["replacement_char"]),
                        _fmt_int(stats["max_length"]),
                    )
                    for field_name, stats in block["columns"].items()
                ),
            )
        )
        lines.append("")

    zip_block = quality["ruas"]["zip_codes"]
    lines.append("### 5.2 CEPs")
    lines.append("")
    if zip_block:
        lines.append(
            _md_table(
                ("Situacao", "Linhas"),
                (
                    ("Valido (8 digitos)", _fmt_int(zip_block["valid"])),
                    ("Reparavel (separadores)", _fmt_int(zip_block["repairable"])),
                    ("Invalido", _fmt_int(zip_block["invalid"])),
                    ("Vazio", _fmt_int(zip_block["empty"])),
                    ("Nulo", _fmt_int(zip_block["missing"])),
                    ("Total", _fmt_int(zip_block["rows"])),
                ),
            )
        )
        lines.append("")
        lines.append(
            "CEP valido no formato nao implica CEP existente; a validacao final "
            "continua ocorrendo no importador (8 digitos apos remocao de "
            "separadores, sem preencher zeros)."
        )
    else:
        lines.append("Coluna de CEP ausente no artefato; verificacao nao aplicavel.")
    lines.append("")

    lines.append("### 5.3 Coordenadas")
    lines.append("")
    for table_key, label in (("bairros", "bairros"), ("ruas", "ruas")):
        coordinates = quality[table_key]["coordinates"]
        lines.append(f"**`{coordinates['scope']}`**")
        lines.append("")
        rows = [("Validas", _fmt_int(coordinates["valid"]))]
        for code in (
            COORD_MISSING_BOTH,
            COORD_MISSING_PARTIAL,
            COORD_NON_NUMERIC,
            COORD_NON_FINITE,
            COORD_ZERO,
            COORD_OUT_OF_RANGE,
            COORD_OUT_OF_BRAZIL,
        ):
            rows.append((code, _fmt_int(coordinates["codes"][code])))
        rows.append(("Total", _fmt_int(coordinates["rows"])))
        lines.append(_md_table(("Situacao", "Linhas"), rows))
        lines.append("")
        valid_range = coordinates["valid_range"]
        lines.append(
            f"Faixa valida: latitude {_fmt_float(valid_range['min_latitude'], 6)} a "
            f"{_fmt_float(valid_range['max_latitude'], 6)}; longitude "
            f"{_fmt_float(valid_range['min_longitude'], 6)} a "
            f"{_fmt_float(valid_range['max_longitude'], 6)}. "
            f"Eixos zerados (lat=0 ou lon=0): {_fmt_int(coordinates['axis_zero'])}."
        )
        lines.append("")

    lines.append("### 5.4 Duplicidades")
    lines.append("")
    duplicates = quality["duplicates"]
    duplicate_labels = {
        "neighborhoods": "Bairro (bruto)",
        "streets_natural_key": "Logradouro (chave natural)",
        "streets_point_key": "Ponto de logradouro (com coordenadas)",
    }
    lines.append(
        _md_table(
            ("Chave", "Tabela", "Colunas", "Grupos duplicados"),
            (
                (
                    duplicate_labels.get(label, label),
                    block["table"],
                    ", ".join(block["key"]),
                    _fmt_int(block["duplicate_groups"]),
                )
                for label, block in duplicates.items()
            ),
        )
    )
    lines.append("")

    lines.append("### 5.5 Cobertura bairro x ruas")
    lines.append("")
    lines.append(
        _md_table(
            ("Metrica", "Valor"),
            (
                ("Bairros distintos", _fmt_int(coverage["neighborhoods"])),
                (
                    "Bairros com pontos de ruas",
                    _fmt_int(coverage["neighborhoods_with_valid_points"]),
                ),
                ("Bairros sem pontos", _fmt_int(coverage["neighborhoods_without_points"])),
                ("Grupos de ruas distintos", _fmt_int(coverage["street_groups"])),
                (
                    "Grupos de ruas sem bairro",
                    _fmt_int(coverage["street_groups_without_neighborhood"]),
                ),
                (
                    "Linhas de ruas sem bairro",
                    _fmt_int(coverage["street_rows_without_neighborhood"]),
                ),
                (
                    "Municipios em bairros",
                    _fmt_int(coverage["municipalities_in_bairros"]),
                ),
                ("Municipios em ruas", _fmt_int(coverage["municipalities_in_ruas"])),
            ),
        )
    )
    lines.append("")

    lines.append("## 6. Validacao dos centroides materializados")
    lines.append("")
    error_meters = centroid_report["error_meters"]
    totals = centroid_report["totals"]
    lines.append(
        _md_table(
            ("Metrica", "Valor"),
            (
                ("Metodo", centroid_report["method"]["id"]),
                ("Proveniencia", centroid_report["method"]["provenance"]),
                ("Tolerancia (m)", _fmt_float(centroid["tolerance_m"], 2)),
                ("Bairros comparados", _fmt_int(totals["neighborhoods_compared"])),
                ("Bairros sem pontos", _fmt_int(totals["neighborhoods_without_points"])),
                ("Mediana do erro (m)", _fmt_float(error_meters["median"])),
                ("p90 do erro (m)", _fmt_float(error_meters["p90"])),
                ("p99 do erro (m)", _fmt_float(error_meters["p99"])),
                ("Maximo do erro (m)", _fmt_float(error_meters["max"])),
                ("Acima da tolerancia", _fmt_int(centroid["above_tolerance"])),
                (
                    "Bairros com menos de "
                    f"{_fmt_int(centroid['low_sample_threshold'])} pontos (low_sample)",
                    _fmt_int(totals["neighborhoods_below_low_sample_threshold"]),
                ),
            ),
        )
    )
    lines.append("")
    lines.append(
        "A media e calculada diretamente sobre os pontos de `ruas` de cada bairro "
        "(uma unica soma consolidada, sem media de medias) e comparada com "
        "`latitude_central`/`longitude_central`. Detalhes e amostras em "
        "`centroid-validation.json`."
    )
    lines.append("")
    by_uf = centroid_report["by_uf"]
    if by_uf:
        lines.append(f"**Desvio por UF ({len(by_uf)} UFs)**")
        lines.append("")
        lines.append(
            _md_table(
                ("UF", "Sigla", "Comparados", "Mediana (m)", "p90 (m)", "p99 (m)", "Maximo (m)", "Acima da tolerancia"),
                (
                    (
                        row["uf_codigo"],
                        row["uf_sigla"],
                        _fmt_int(row["compared"]),
                        _fmt_float(row["median_m"]),
                        _fmt_float(row["p90_m"]),
                        _fmt_float(row["p99_m"]),
                        _fmt_float(row["max_m"]),
                        _fmt_int(row["above_tolerance"]),
                    )
                    for row in by_uf
                ),
            )
        )
        lines.append("")

    lines.append("## 7. Baseline v2.1")
    lines.append("")
    lines.append(f"Referencia: {baseline['reference']}")
    lines.append("")
    lines.append(
        _md_table(
            ("Metrica", "Esperado", "Medido", "Situacao"),
            (
                (
                    key,
                    _fmt_int(baseline["expected"][key]),
                    _fmt_int(baseline["measured"][key]),
                    _status(bool(baseline["checks"][key])),
                )
                for key in baseline["expected"]
            ),
        )
    )
    lines.append("")
    lines.append(
        "A comparacao com o baseline e informativa: um artefato novo pode divergir "
        "legitimamente. O importador deve reexecutar estas verificacoes e falhar "
        "somente contra o baseline do proprio artefato aprovado."
    )
    lines.append("")

    lines.append("## 8. Execucao, memoria e determinismo")
    lines.append("")
    peak_memory = performance.get("peak_memory_bytes")
    lines.append(
        _md_table(
            ("Item", "Valor"),
            (
                ("Chunk de leitura", _fmt_int(report["chunk_size"])),
                ("Amostras por categoria", _fmt_int(report["max_samples"])),
                ("Duracao (s)", _fmt_float(performance.get("duration_seconds"), 2)),
                (
                    "Pico de memoria residente (MiB)",
                    _fmt_float(peak_memory / (1024 * 1024), 1)
                    if peak_memory
                    else "n/d",
                ),
            ),
        )
    )
    lines.append("")
    phase_seconds = performance.get("phase_seconds") or {}
    if phase_seconds:
        lines.append("**Tempo por fase**")
        lines.append("")
        lines.append(
            _md_table(
                ("Fase", "Segundos"),
                ((name, _fmt_float(seconds, 3)) for name, seconds in phase_seconds.items()),
            )
        )
        lines.append("")
    lines.append(
        "- Duas passadas streaming, uma por tabela, na ordem da chave primaria "
        "(`ORDER BY id`, sem `OFFSET`), em chunks; nenhuma lista de 4,2 milhoes de "
        "linhas e mantida em memoria."
    )
    lines.append(
        "- A agregacao usa um dicionario com um registro por bairro "
        "(contagem + somas compensadas de latitude/longitude), memoria O(bairros)."
    )
    lines.append(
        "- As somas usam compensacao de Kahan e a leitura tem ordem fixa, portanto "
        "o mesmo arquivo produz os mesmos numeros (fingerprint estavel)."
    )
    lines.append("")

    lines.append("## 9. Limitacoes e proximos passos")
    lines.append("")
    lines.append(
        "- A comparacao usa o texto bruto de `bairro`/`nome`; nenhuma normalizacao "
        "de nome e aplicada nesta fase, portanto grafias divergentes aparecem como "
        "grupos distintos e sao contabilizadas em `street_groups_without_neighborhood`."
    )
    lines.append(
        "- O artefato e pre-agregado por logradouro: a media tem peso por logradouro, "
        "nao por domicilio."
    )
    lines.append(
        "- Esta etapa nao cria migracao PostgreSQL, nao altera entidades, APIs, Redis "
        "ou `StoreDeliveryAreas`."
    )
    lines.append(
        "- Proximos passos da Fase 0: versionar `config/source-mapping.yaml` com o "
        "mapeamento confirmado e reexecutar esta descoberta sempre que o checksum do "
        "artefato mudar."
    )
    lines.append("")
    return "\n".join(lines)


def _atomic_write_text(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(
        "w",
        encoding="utf-8",
        newline="\n",
        delete=False,
        dir=str(path.parent),
        prefix=path.name + ".",
        suffix=".tmp",
    ) as handle:
        temporary = Path(handle.name)
        try:
            handle.write(content)
        except Exception:
            temporary.unlink(missing_ok=True)
            raise
    os.replace(temporary, path)


def write_reports(result: DiscoveryResult, output_dir: str | Path) -> dict[str, Path]:
    """Grava os tres relatorios de forma atomica e deterministica."""
    output_path = Path(output_dir)
    markdown = render_schema_markdown(result.schema_report, result.centroid_report)
    payloads = (
        (
            "schema-report.json",
            json.dumps(result.schema_report, ensure_ascii=False, indent=2) + "\n",
        ),
        (
            "centroid-validation.json",
            json.dumps(result.centroid_report, ensure_ascii=False, indent=2) + "\n",
        ),
        ("schema-report.md", markdown),
    )
    written: dict[str, Path] = {}
    for name, content in payloads:
        path = output_path / name
        _atomic_write_text(path, content)
        written[name] = path
    return written


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------


def default_output_dir() -> Path:
    repository_root = Path(__file__).resolve().parents[3]
    return repository_root / "imports" / "cnefe" / "reports"


def _env_default(name: str, fallback: str) -> str:
    value = os.environ.get(name)
    return value if value else fallback


def _env_float(name: str, fallback: float) -> float:
    try:
        return float(_env_default(name, str(fallback)))
    except ValueError:
        return fallback


def _env_int(name: str, fallback: int) -> int:
    try:
        return int(_env_default(name, str(fallback)))
    except ValueError:
        return fallback


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description=(
            "Descoberta e validacao somente leitura do SQLite do CNEFE "
            "(Fase 0 da especificacao v2.1)."
        )
    )
    parser.add_argument(
        "--sqlite-path",
        default=_env_default("URBEAT_CNEFE_SQLITE_PATH", ""),
        help=(
            "Caminho do arquivo SQLite do CNEFE. Obrigatorio (ou defina "
            "URBEAT_CNEFE_SQLITE_PATH). O arquivo e aberto em modo somente leitura."
        ),
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=default_output_dir(),
        help="Diretorio dos relatorios (padrao: imports/cnefe/reports).",
    )
    parser.add_argument(
        "--source-version",
        default=_env_default("URBEAT_CNEFE_SOURCE_VERSION", DEFAULT_SOURCE_VERSION),
        help=f"Versao declarada da fonte (padrao: {DEFAULT_SOURCE_VERSION}).",
    )
    parser.add_argument(
        "--centroid-tolerance-m",
        type=float,
        default=_env_float(
            "URBEAT_CNEFE_CENTROID_TOLERANCE_M", DEFAULT_CENTROID_TOLERANCE_M
        ),
        help="Tolerancia em metros para divergencia de centroide (padrao: 1.0).",
    )
    parser.add_argument(
        "--low-sample-threshold",
        type=int,
        default=DEFAULT_LOW_SAMPLE_THRESHOLD,
        help="Sinaliza bairros com menos pontos que este limiar (padrao: 5).",
    )
    parser.add_argument(
        "--chunk-size",
        type=int,
        default=_env_int("URBEAT_IMPORT_BATCH_SIZE", DEFAULT_CHUNK_SIZE),
        help=f"Linhas por chunk de leitura (padrao: {DEFAULT_CHUNK_SIZE}).",
    )
    parser.add_argument(
        "--max-samples",
        type=int,
        default=DEFAULT_MAX_SAMPLES,
        help=f"Maximo de amostras por categoria (padrao: {DEFAULT_MAX_SAMPLES}).",
    )
    parser.add_argument(
        "--logical-path",
        default=None,
        help="Caminho logico registrado nos relatorios (padrao: relativo ao repo).",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Executa a analise sem gravar relatorios.",
    )
    parser.add_argument(
        "--quiet",
        action="store_true",
        help="Nao imprime o progresso das fases.",
    )
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    if not args.sqlite_path:
        parser.error(
            "--sqlite-path e obrigatorio (ou defina URBEAT_CNEFE_SQLITE_PATH)"
        )

    def progress(message: str) -> None:
        if not args.quiet:
            print(f"[cnefe-discovery] {message}", file=sys.stderr, flush=True)

    try:
        result = discover(
            args.sqlite_path,
            source_version=args.source_version,
            tolerance_m=args.centroid_tolerance_m,
            chunk_size=args.chunk_size,
            low_sample_threshold=args.low_sample_threshold,
            max_samples=args.max_samples,
            logical_path=args.logical_path,
            progress=progress,
        )
    except (FileNotFoundError, SchemaMappingError, ValueError, RuntimeError) as error:
        print(f"[cnefe-discovery] erro: {error}", file=sys.stderr)
        return 2

    schema_report = result.schema_report
    centroid_report = result.centroid_report
    totals = centroid_report["totals"]
    errors = centroid_report["error_meters"]

    print(
        "Descoberta CNEFE concluida em "
        f"{result.duration_seconds:.1f}s (somente leitura)."
    )
    print(
        f"  bairros: {schema_report['baseline']['measured']['neighborhood_rows']:,}"
        f" | ruas: {schema_report['baseline']['measured']['street_rows']:,}"
        f" | UFs: {schema_report['dimensions']['uf']['distinct']}"
        f" | municipios: {schema_report['dimensions']['municipality']['distinct_normalized']}"
    )
    print(
        f"  centroides comparados: {totals['neighborhoods_compared']:,}"
        f" | mediana {_fmt_float(errors['median'])} m"
        f" | p90 {_fmt_float(errors['p90'])} m"
        f" | p99 {_fmt_float(errors['p99'])} m"
        f" | max {_fmt_float(errors['max'])} m"
    )
    print(
        f"  acima da tolerancia: {centroid_report['above_tolerance']['count']:,}"
        f" | bairros sem pontos: {totals['neighborhoods_without_points']:,}"
    )
    print(
        "  baseline v2.1: "
        + ("compativel" if schema_report["baseline"]["matches"] else "divergente")
    )
    if args.dry_run:
        print("  --dry-run: nenhum relatorio foi gravado.")
        return 0

    written = write_reports(result, args.output_dir)
    for name, path in written.items():
        print(f"  {name}: {path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
