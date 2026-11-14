#!/usr/bin/env python3
"""Importacao do artefato SQLite do CNEFE para a base geografica do Urbeat.

Esta etapa implementa as Fases 2-4 de ``imports/cnefe/prompt.txt`` v2.1 de forma
incremental e auditavel:

* abre o SQLite em modo somente leitura (``mode=ro`` + ``PRAGMA query_only``);
* percorre ``bairros`` e ``ruas`` por chave (``id``), em lotes, sem ``OFFSET``;
* normaliza UF, codigo IBGE, CEP, nome de logradouro e coordenadas;
* valida o centroide materializado de cada bairro recomputando a media direta
  dos pontos validos de ``ruas`` (tolerancia configuravel, padrao 1 m);
* grava staging em CSV pronto para ``COPY`` e relatorios por UF;
* registra toda rejeicao com codigo estavel (``ImportErrors``);
* calcula o SHA-256 do artefato e grava o manifesto;
* nunca publica no PostgreSQL sem ``--publish`` e sem ``psycopg2`` configurado.

Nenhum dado e escrito no arquivo SQLite. O fator 1.3, a media de medias e a
coordenada de municipio/loja como coordenada de bairro nao existem neste codigo.

Uso tipico (uma UF por execucao):

    python backend/scripts/import/cnefe_import.py \\
        --sqlite-path imports/cnefe/delivery_brasil.db \\
        --uf SP --source-version CNEFE_2022 \\
        --output-dir imports/cnefe

Uso com publicacao (exige psycopg2 e variaveis URBEAT_DB_*):

    python backend/scripts/import/cnefe_import.py --uf SP --publish
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
import os
import re
import sqlite3
import unicodedata
from collections import Counter
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Iterable, Iterator, Sequence

from import_common import IBGE_STATE_CODES, load_local_environment, normalize_ibge_code
from cnefe_discover_schema import (
    BRAZIL_ENVELOPE,
    COORD_VALID,
    open_readonly_connection,
    classify_coordinates,
)

load_local_environment()

DEFAULT_SOURCE_VERSION = "CNEFE_2022"
DEFAULT_BATCH_SIZE = 50_000
DEFAULT_CENTROID_TOLERANCE_M = 1.0
DEFAULT_MAX_REJECT_RATE = 0.01
DEFAULT_LOW_SAMPLE_THRESHOLD = 5
EARTH_RADIUS_M = 6_371_000.0

# Baseline apenas informativo (secao 3.3/3.3.4 da especificacao). Comparado quando
# `--strict-baseline` e usado em uma passada nacional; qualquer divergencia e erro
# bloqueante, porque o artefato pode ter mudado de checksum.
BASELINE = {
    "reference": "imports/cnefe/prompt.txt v2.1 §3.3",
    "neighborhood_rows": 520_195,
    "street_rows": 4_266_401,
    "uf_count": 27,
    "municipality_count": 5_570,
}

NEIGHBORHOODS_TABLE = "bairros"
STREETS_TABLE = "ruas"

STATE_CODE_TO_SIGLA = {str(code): sigla for sigla, code in IBGE_STATE_CODES.items()}

# Codigos de erro da secao 6.4 da especificacao. Sao valores persistidos em
# ImportErrors.Code e nunca devem ser traduzidos.
INVALID_UF = "INVALID_UF"
INVALID_IBGE = "INVALID_IBGE"
UF_IBGE_MISMATCH = "UF_IBGE_MISMATCH"
MISSING_STREET = "MISSING_STREET"
MISSING_COORD = "MISSING_COORD"
INVALID_COORD = "INVALID_COORD"
COORD_OUT_OF_CITY = "COORD_OUT_OF_CITY"
INVALID_ZIP = "INVALID_ZIP"
PENDING_NEIGHBORHOOD = "PENDING_NEIGHBORHOOD"
UNKNOWN_CITY = "UNKNOWN_CITY"
DUPLICATE_ROW = "DUPLICATE_ROW"
LOW_SAMPLE = "LOW_SAMPLE"
COORD_CENTROID_MISMATCH = "COORD_CENTROID_MISMATCH"
COORD_CENTROID_MISSING = "COORD_CENTROID_MISSING"

STAGING_NEIGHBORHOODS = "neighborhoods.csv"
STAGING_STREETS = "streets.csv"
STAGING_ZIP_CODES = "street_zipcodes.csv"
STAGING_ERRORS = "errors.csv"

NEIGHBORHOOD_COLUMNS = (
    "uf",
    "municipio_ibge",
    "city_name",
    "neighborhood",
    "normalized_name",
    "kind",
    "latitude",
    "longitude",
    "coordinate_method",
    "coordinate_point_count",
    "coordinate_status",
    "coordinate_source_version",
    "coordinate_calculated_at_utc",
    "representative_latitude",
    "representative_longitude",
    "representative_method",
    "locality_type",
    "source",
)

STREET_COLUMNS = (
    "uf",
    "municipio_ibge",
    "neighborhood",
    "normalized_neighborhood",
    "street",
    "normalized_street",
    "latitude",
    "longitude",
    "coordinate_method",
    "address_point_count",
    "source",
    "source_version",
)

ZIP_CODE_COLUMNS = ("uf", "municipio_ibge", "neighborhood", "street", "zip_code", "is_primary")

ERROR_COLUMNS = (
    "uf",
    "source_table",
    "source_row_id",
    "field",
    "code",
    "message",
    "raw_value",
)

_WHITESPACE = re.compile(r"\s+")


class ImportAborted(RuntimeError):
    """A UF deve ser abortada (taxa de rejeicao ou divergencia bloqueante)."""


# ---------------------------------------------------------------------------
# Normalizacao canonica (identica a Urbeat.Domain.Geo.NeighborhoodName)
# ---------------------------------------------------------------------------


def normalize_name(value: object) -> str:
    """Forma canonica: trim, NFKD, sem marcas, alfanumerico ASCII, espaco unico, minusculo.

    Esta implementacao precisa produzir exatamente o mesmo resultado que
    ``Urbeat.Domain.Geo.NeighborhoodName.Normalize`` (teste de paridade nos dois
    lados). Caracteres nao alfanumericos viram um separador que so e emitido
    entre tokens.
    """
    if value is None:
        return ""
    text = str(value).strip().lower()
    if not text:
        return ""
    decomposed = unicodedata.normalize("NFKD", text)
    parts: list[str] = []
    pending_separator = False
    for character in decomposed:
        if unicodedata.combining(character):
            continue
        if character.isascii() and character.isalnum():
            if pending_separator and parts:
                parts.append(" ")
            pending_separator = False
            parts.append(character)
        else:
            pending_separator = True
    return "".join(parts)


def compose_display_name(street_type: object, street_title: object, street_name: object) -> str:
    """Nome de exibicao sem inventar tipo/titulo ausentes."""
    parts: list[str] = []
    for part in (street_type, street_title, street_name):
        if part is None:
            continue
        text = _WHITESPACE.sub(" ", str(part).strip())
        if text:
            parts.append(text)
    return " ".join(parts)


def normalize_zip_code(value: object) -> str | None:
    """CEP de 8 digitos ou None. Nunca inventa zeros e rejeita ``00000000``."""
    if value is None or isinstance(value, bool):
        return None
    if isinstance(value, float):
        if not value.is_integer():
            return None
        text = str(int(value))
    else:
        text = str(value)
    digits = "".join(character for character in text if character.isdigit() and character.isascii())
    if len(digits) != 8:
        return None
    if digits == "00000000":
        return None
    return digits


def format_coordinate(value: float) -> str:
    return f"{round(value, 7):.7f}"


def compute_source_row_hash(
    uf: str,
    municipality_ibge: str,
    neighborhood: object,
    street: object,
    zip_code: object,
    latitude: float,
    longitude: float,
) -> str:
    """SHA-256 deterministico da tupla natural do ponto (idempotencia).

    A concatenacao e identica a ``NeighborhoodName.ComputeSourceRowHash`` no .NET.
    """
    payload = "|".join(
        (
            str(uf).strip().upper(),
            str(municipality_ibge).strip(),
            normalize_name(neighborhood),
            normalize_name(street),
            normalize_zip_code(zip_code) or "",
            format_coordinate(latitude),
            format_coordinate(longitude),
        )
    )
    return hashlib.sha256(payload.encode("utf-8")).hexdigest()


def classify_uf(value: object) -> str | None:
    """Normaliza a UF para o codigo IBGE de 2 digitos usado no artefato."""
    if value is None:
        return None
    text = str(value).strip().upper()
    if not text:
        return None
    if text in STATE_CODE_TO_SIGLA:
        return text
    if text in IBGE_STATE_CODES:
        return str(IBGE_STATE_CODES[text])
    if text.isdigit() and len(text) < 2:
        padded = text.zfill(2)
        if padded in STATE_CODE_TO_SIGLA:
            return padded
    return None


def classify_municipality(value: object) -> str | None:
    normalized = normalize_ibge_code(value)
    if normalized is None or len(normalized) != 7:
        return None
    return normalized


def haversine_meters(latitude1: float, longitude1: float, latitude2: float, longitude2: float) -> float:
    phi1 = math.radians(latitude1)
    phi2 = math.radians(latitude2)
    delta_phi = math.radians(latitude2 - latitude1)
    delta_lambda = math.radians(longitude2 - longitude1)
    sin_phi = math.sin(delta_phi / 2.0)
    sin_lambda = math.sin(delta_lambda / 2.0)
    haversine = sin_phi * sin_phi + math.cos(phi1) * math.cos(phi2) * sin_lambda * sin_lambda
    haversine = min(1.0, max(0.0, haversine))
    return 2.0 * EARTH_RADIUS_M * math.asin(math.sqrt(haversine))


def classify_locality(name: str) -> str:
    """Classificacao auxiliar da localidade. Nunca altera elegibilidade por raio."""
    normalized = normalize_name(name)
    if normalized.startswith("aldeia indigena") or normalized.startswith("terra indigena"):
        return "indigenous_village"
    if any(token in normalized for token in ("fazenda", "sitio", "chacara", "assentamento", "rural")):
        return "rural_locality"
    if normalized in {"centro", "zona urbana", "area urbana", "sede"}:
        return "urban_neighborhood"
    if normalized.startswith(("bairro ", "vila ", "jardim ", "parque ", "lot ", "loteamento ")):
        return "urban_neighborhood"
    return "unknown"


def utc_now_iso() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z")


def sha256_file(path: Path, chunk_size: int = 1 << 20) -> str:
    digest = hashlib.sha256()
    with Path(path).open("rb") as handle:
        while True:
            chunk = handle.read(chunk_size)
            if not chunk:
                break
            digest.update(chunk)
    return digest.hexdigest()


# ---------------------------------------------------------------------------
# Agregacao
# ---------------------------------------------------------------------------


@dataclass
class NeighborhoodAggregate:
    uf: str
    municipality_ibge: str
    name: str
    normalized_name: str
    materialized_latitude: float | None
    materialized_longitude: float | None
    point_count: int = 0
    latitude_sum: float = 0.0
    longitude_sum: float = 0.0
    representative_latitude: float | None = None
    representative_longitude: float | None = None
    representative_distance_m: float | None = None
    seen: bool = False

    @property
    def mean_latitude(self) -> float | None:
        return self.latitude_sum / self.point_count if self.point_count else None

    @property
    def mean_longitude(self) -> float | None:
        return self.longitude_sum / self.point_count if self.point_count else None


@dataclass
class StreetAggregate:
    uf: str
    municipality_ibge: str
    neighborhood_name: str
    normalized_neighborhood: str
    display_name: str
    normalized_street: str
    point_count: int = 0
    latitude_sum: float = 0.0
    longitude_sum: float = 0.0

    @property
    def latitude(self) -> float | None:
        return self.latitude_sum / self.point_count if self.point_count else None

    @property
    def longitude(self) -> float | None:
        return self.longitude_sum / self.point_count if self.point_count else None


@dataclass
class ImportErrorRecord:
    uf: str
    source_table: str
    source_row_id: str
    field: str
    code: str
    message: str
    raw_value: str

    def as_row(self) -> tuple:
        return (
            self.uf,
            self.source_table,
            self.source_row_id,
            self.field,
            self.code,
            self.message,
            self.raw_value,
        )


@dataclass
class ExtractionResult:
    uf: str
    neighborhoods: dict[tuple[str, str], NeighborhoodAggregate] = field(default_factory=dict)
    streets: dict[tuple[str, str, str], StreetAggregate] = field(default_factory=dict)
    zip_codes: set[tuple[str, str, str, str]] = field(default_factory=set)
    errors: list[ImportErrorRecord] = field(default_factory=list)
    counts: Counter = field(default_factory=Counter)
    duplicate_hashes: int = 0
    locality_types: Counter = field(default_factory=Counter)

    @property
    def rows_read(self) -> int:
        return self.counts["street_rows_read"]

    @property
    def rows_rejected(self) -> int:
        return self.counts["street_rows_rejected"]

    @property
    def rows_published(self) -> int:
        return self.counts["street_rows_published"]

    @property
    def rows_skipped(self) -> int:
        return self.counts["street_rows_skipped"]


class CnefeAggregator:
    """Agrega bairros e ruas de UMA UF, em streaming, sem carregar linhas individuais.

    A media do bairro e a media direta dos pontos de ``ruas`` (peso por ponto; no
    artefato atual, um ponto por logradouro). Nunca e media de medias.
    """

    def __init__(self, uf: str, source_version: str, tolerance_m: float, low_sample_threshold: int) -> None:
        self.result = ExtractionResult(uf=uf)
        self.uf = uf
        self.source_version = source_version
        self.tolerance_m = tolerance_m
        self.low_sample_threshold = low_sample_threshold
        self._seen_hashes: set[str] = set()

    def _error(
        self,
        source_table: str,
        source_row_id: object,
        field: str,
        code: str,
        message: str,
        raw_value: object,
    ) -> None:
        self.result.errors.append(
            ImportErrorRecord(
                uf=self.uf,
                source_table=source_table,
                source_row_id=str(source_row_id),
                field=field,
                code=code,
                message=message,
                raw_value="" if raw_value is None else str(raw_value)[:200],
            )
        )

    def add_neighborhood_row(
        self,
        row_id: object,
        uf_value: object,
        municipality_value: object,
        name: object,
        latitude_central: object,
        longitude_central: object,
    ) -> None:
        self.result.counts["neighborhood_rows_read"] += 1
        uf = classify_uf(uf_value)
        municipality = classify_municipality(municipality_value)
        display_name = _WHITESPACE.sub(" ", str(name or "").strip())

        if uf is None:
            self._error(NEIGHBORHOODS_TABLE, row_id, "uf_codigo", INVALID_UF, "UF invalida", uf_value)
            self.result.counts["neighborhood_rows_rejected"] += 1
            return
        if municipality is None:
            self._error(NEIGHBORHOODS_TABLE, row_id, "municipio_codigo", INVALID_IBGE, "Codigo IBGE invalido", municipality_value)
            self.result.counts["neighborhood_rows_rejected"] += 1
            return
        if municipality[:2] != uf:
            self._error(NEIGHBORHOODS_TABLE, row_id, "municipio_codigo", UF_IBGE_MISMATCH, "Prefixo do IBGE difere da UF", municipality_value)
            self.result.counts["neighborhood_rows_rejected"] += 1
            return
        if not display_name:
            self._error(NEIGHBORHOODS_TABLE, row_id, "nome", PENDING_NEIGHBORHOOD, "Localidade vazia", name)
            self.result.counts["neighborhood_rows_rejected"] += 1
            return

        normalized_name = normalize_name(display_name)
        key = (municipality, normalized_name)
        latitude = _as_float(latitude_central)
        longitude = _as_float(longitude_central)
        if not _is_valid_coordinate(latitude, longitude):
            latitude = None
            longitude = None

        existing = self.result.neighborhoods.get(key)
        if existing is not None:
            # Duas grafias que normalizam para o mesmo valor: a grafia de exibicao
            # escolhida e deterministica (lexicografica) e o registro e unico.
            self.result.counts["neighborhood_duplicate_names"] += 1
            if display_name < existing.name:
                existing.name = display_name
            if existing.materialized_latitude is None and latitude is not None:
                existing.materialized_latitude = latitude
                existing.materialized_longitude = longitude
            return

        self.result.neighborhoods[key] = NeighborhoodAggregate(
            uf=uf,
            municipality_ibge=municipality,
            name=display_name,
            normalized_name=normalized_name,
            materialized_latitude=latitude,
            materialized_longitude=longitude,
        )

    def add_street_row(
        self,
        row_id: object,
        uf_value: object,
        municipality_value: object,
        neighborhood_value: object,
        street_value: object,
        zip_value: object,
        latitude: object,
        longitude: object,
    ) -> None:
        self.result.counts["street_rows_read"] += 1
        uf = classify_uf(uf_value)
        municipality = classify_municipality(municipality_value)

        if uf is None or uf != self.uf:
            self._error(STREETS_TABLE, row_id, "uf_codigo", INVALID_UF, "UF invalida", uf_value)
            self.result.counts["street_rows_rejected"] += 1
            return
        if municipality is None:
            self._error(STREETS_TABLE, row_id, "municipio_codigo", INVALID_IBGE, "Codigo IBGE invalido", municipality_value)
            self.result.counts["street_rows_rejected"] += 1
            return
        if municipality[:2] != uf:
            self._error(STREETS_TABLE, row_id, "municipio_codigo", UF_IBGE_MISMATCH, "Prefixo do IBGE difere da UF", municipality_value)
            self.result.counts["street_rows_rejected"] += 1
            return

        display_street = _WHITESPACE.sub(" ", str(street_value or "").strip())
        if not display_street:
            self._error(STREETS_TABLE, row_id, "logradouro", MISSING_STREET, "Logradouro vazio", street_value)
            self.result.counts["street_rows_rejected"] += 1
            return

        zip_code = normalize_zip_code(zip_value)
        if zip_value not in (None, "") and zip_code is None:
            self._error(STREETS_TABLE, row_id, "cep", INVALID_ZIP, "CEP invalido; gravado como nulo", zip_value)
            self.result.counts["invalid_zip"] += 1

        neighborhood_display = _WHITESPACE.sub(" ", str(neighborhood_value or "").strip())
        normalized_neighborhood = normalize_name(neighborhood_display)
        if not normalized_neighborhood:
            self._error(
                STREETS_TABLE,
                row_id,
                "bairro",
                PENDING_NEIGHBORHOOD,
                "Localidade ausente; logradouro publicado sem bairro",
                neighborhood_value,
            )
            self.result.counts["pending_neighborhood_rows"] += 1

        latitude_value = _as_float(latitude)
        longitude_value = _as_float(longitude)
        coordinate_code = classify_coordinates(latitude_value, longitude_value)

        street_key = (municipality, normalized_neighborhood, normalize_name(display_street))
        street = self.result.streets.get(street_key)
        if street is None:
            street = StreetAggregate(
                uf=uf,
                municipality_ibge=municipality,
                neighborhood_name=neighborhood_display,
                normalized_neighborhood=normalized_neighborhood,
                display_name=compose_display_name(None, None, display_street),
                normalized_street=normalize_name(display_street),
            )
            self.result.streets[street_key] = street

        if zip_code is not None:
            # Um logradouro pode ter varios CEPs; o primeiro (ordem de leitura) e o primario.
            key = (municipality, normalized_neighborhood, street.normalized_street, zip_code)
            if key not in self.result.zip_codes:
                self.result.zip_codes.add(key)
                self.result.counts["zip_codes"] += 1

        if coordinate_code != COORD_VALID:
            code = MISSING_COORD if coordinate_code in ("missing_both", "missing_partial") else INVALID_COORD
            self._error(STREETS_TABLE, row_id, "latitude/longitude", code, f"Coordenada descartada ({coordinate_code})", f"{latitude}:{longitude}")
            self.result.counts["points_rejected"] += 1
            # O logradouro/CEP ainda e publicado; apenas o ponto nao entra em centroide.
            self.result.counts["street_rows_published"] += 1
            return

        assert latitude_value is not None and longitude_value is not None
        source_hash = compute_source_row_hash(
            uf,
            municipality,
            neighborhood_display,
            display_street,
            zip_code,
            latitude_value,
            longitude_value,
        )
        if source_hash in self._seen_hashes:
            self._error(STREETS_TABLE, row_id, "row", DUPLICATE_ROW, "Ponto duplicado na mesma versao", source_hash)
            self.result.counts["street_rows_skipped"] += 1
            return
        self._seen_hashes.add(source_hash)

        # Bounding box por municipio ainda nao e conhecido na primeira passada: a
        # envoltoria do Brasil e o filtro disponivel e suficiente para o artefato.
        if not (
            BRAZIL_ENVELOPE["min_latitude"] <= latitude_value <= BRAZIL_ENVELOPE["max_latitude"]
            and BRAZIL_ENVELOPE["min_longitude"] <= longitude_value <= BRAZIL_ENVELOPE["max_longitude"]
        ):
            self._error(STREETS_TABLE, row_id, "latitude/longitude", COORD_OUT_OF_CITY, "Ponto fora da envoltoria do Brasil", f"{latitude}:{longitude}")
            self.result.counts["points_rejected"] += 1
            return

        street.point_count += 1
        street.latitude_sum += latitude_value
        street.longitude_sum += longitude_value
        self.result.counts["street_rows_published"] += 1

        neighborhood = self.result.neighborhoods.get((municipality, normalized_neighborhood))
        if neighborhood is None:
            # Ponto auditavel, mas sem bairro oficial correspondente: nao entra em centroide.
            self.result.counts["orphan_point_rows"] += 1
            return

        neighborhood.seen = True
        neighborhood.point_count += 1
        neighborhood.latitude_sum += latitude_value
        neighborhood.longitude_sum += longitude_value

        # Anexo de auditoria: ponto valido mais proximo do centroide materializado.
        # Auxiliar apenas; a elegibilidade usa o centroide (Latitude/Longitude).
        if neighborhood.materialized_latitude is not None and neighborhood.materialized_longitude is not None:
            distance = haversine_meters(
                neighborhood.materialized_latitude,
                neighborhood.materialized_longitude,
                latitude_value,
                longitude_value,
            )
            if neighborhood.representative_distance_m is None or distance < neighborhood.representative_distance_m:
                neighborhood.representative_distance_m = distance
                neighborhood.representative_latitude = latitude_value
                neighborhood.representative_longitude = longitude_value

    def finalize(self, calculated_at_utc: str) -> dict:
        """Aplica a validacao de centroide e devolve as linhas prontas para staging."""
        mismatches = 0
        pending = 0
        low_sample = 0
        recomputed = 0
        validated_materialized = 0
        neighborhoods: list[dict] = []

        for neighborhood in self.result.neighborhoods.values():
            if neighborhood.point_count == 0:
                status = "pending"
                latitude = None
                longitude = None
                pending += 1
            else:
                mean_latitude = neighborhood.mean_latitude
                mean_longitude = neighborhood.mean_longitude
                assert mean_latitude is not None and mean_longitude is not None

                if neighborhood.materialized_latitude is not None and neighborhood.materialized_longitude is not None:
                    deviation = haversine_meters(
                        neighborhood.materialized_latitude,
                        neighborhood.materialized_longitude,
                        mean_latitude,
                        mean_longitude,
                    )
                    if deviation > self.tolerance_m:
                        mismatches += 1
                        self._error(
                            NEIGHBORHOODS_TABLE,
                            f"{neighborhood.municipality_ibge}:{neighborhood.name}",
                            "latitude_central/longitude_central",
                            COORD_CENTROID_MISMATCH,
                            f"Centroide materializado diverge da media dos pontos em {deviation:.3f} m "
                            f"(tolerancia {self.tolerance_m} m)",
                            f"{neighborhood.materialized_latitude}:{neighborhood.materialized_longitude}",
                        )
                        status = "pending"
                        latitude = None
                        longitude = None
                    else:
                        status = "computed"
                        latitude = neighborhood.materialized_latitude
                        longitude = neighborhood.materialized_longitude
                        validated_materialized += 1
                else:
                    # Artefato sem centroide materializado: recalcula a media direta.
                    status = "computed"
                    latitude = mean_latitude
                    longitude = mean_longitude
                    recomputed += 1

                if 0 < neighborhood.point_count < self.low_sample_threshold:
                    low_sample += 1
                    self._error(
                        NEIGHBORHOODS_TABLE,
                        f"{neighborhood.municipality_ibge}:{neighborhood.name}",
                        "coordinate_point_count",
                        LOW_SAMPLE,
                        f"Bairro com apenas {neighborhood.point_count} ponto(s) valido(s)",
                        neighborhood.point_count,
                    )

            locality_type = classify_locality(neighborhood.name)
            self.result.locality_types[locality_type] += 1

            neighborhoods.append(
                {
                    "uf": neighborhood.uf,
                    "municipio_ibge": neighborhood.municipality_ibge,
                    "city_name": "",
                    "neighborhood": neighborhood.name,
                    "normalized_name": neighborhood.normalized_name,
                    "kind": "official_cnefe",
                    "latitude": "" if latitude is None else format_coordinate(latitude),
                    "longitude": "" if longitude is None else format_coordinate(longitude),
                    "coordinate_method": "ADDRESS_POINT_MEAN" if latitude is not None else "",
                    "coordinate_point_count": neighborhood.point_count,
                    "coordinate_status": status,
                    "coordinate_source_version": self.source_version,
                    "coordinate_calculated_at_utc": calculated_at_utc,
                    "representative_latitude": ""
                    if neighborhood.representative_latitude is None
                    else format_coordinate(neighborhood.representative_latitude),
                    "representative_longitude": ""
                    if neighborhood.representative_longitude is None
                    else format_coordinate(neighborhood.representative_longitude),
                    "representative_method": ""
                    if neighborhood.representative_latitude is None
                    else "nearest_to_centroid_v1",
                    "locality_type": locality_type,
                    "source": "cnefe",
                }
            )

        streets: list[dict] = []
        for street in self.result.streets.values():
            streets.append(
                {
                    "uf": street.uf,
                    "municipio_ibge": street.municipality_ibge,
                    "neighborhood": street.neighborhood_name,
                    "normalized_neighborhood": street.normalized_neighborhood,
                    "street": street.display_name,
                    "normalized_street": street.normalized_street,
                    "latitude": "" if street.latitude is None else format_coordinate(street.latitude),
                    "longitude": "" if street.longitude is None else format_coordinate(street.longitude),
                    "coordinate_method": "ADDRESS_POINT_MEAN" if street.point_count else "",
                    "address_point_count": street.point_count,
                    "source": "cnefe",
                    "source_version": self.source_version,
                }
            )

        zip_rows: list[dict] = []
        previous_street_key: tuple[str, str, str] | None = None
        for municipality, normalized_neighborhood, normalized_street, zip_code in sorted(self.result.zip_codes):
            street_key = (municipality, normalized_neighborhood, normalized_street)
            is_primary = street_key != previous_street_key
            previous_street_key = street_key
            zip_rows.append(
                {
                    "uf": self.uf,
                    "municipio_ibge": municipality,
                    "neighborhood": normalized_neighborhood,
                    "street": normalized_street,
                    "zip_code": zip_code,
                    "is_primary": is_primary,
                }
            )

        published = len(streets)
        rejected = self.result.counts["street_rows_rejected"]
        skipped = self.result.counts["street_rows_skipped"]
        self.result.counts["streets_published"] = published
        self.result.counts["centroid_mismatch"] = mismatches
        self.result.counts["centroid_pending"] = pending
        self.result.counts["centroid_low_sample"] = low_sample
        self.result.counts["centroid_recomputed"] = recomputed
        self.result.counts["centroid_materialized_validated"] = validated_materialized

        rows_published = self.result.rows_published

        return {
            "neighborhoods": neighborhoods,
            "streets": streets,
            "zip_codes": zip_rows,
            "rows_read": self.result.rows_read,
            "rows_published": rows_published,
            "rows_rejected": rejected,
            "rows_skipped": skipped,
            "reconciles": self.result.rows_read == rows_published + rejected + skipped,
            "errors": list(self.result.errors),
            "counts": self.result.counts,
            "locality_types": dict(self.result.locality_types),
        }


def _as_float(value: object) -> float | None:
    if value is None or isinstance(value, bool):
        return None
    if isinstance(value, (int, float)):
        number = float(value)
        return number if math.isfinite(number) else None
    text = str(value).strip()
    if not text:
        return None
    try:
        number = float(text)
    except ValueError:
        return None
    return number if math.isfinite(number) else None


def _is_valid_coordinate(latitude: float | None, longitude: float | None) -> bool:
    if latitude is None or longitude is None:
        return False
    return classify_coordinates(latitude, longitude) == COORD_VALID


# ---------------------------------------------------------------------------
# Extracao (SQLite somente leitura)
# ---------------------------------------------------------------------------


def _iter_table(
    connection: sqlite3.Connection,
    table: str,
    columns: Sequence[str],
    batch_size: int,
) -> Iterator[tuple]:
    """Percorre a tabela pela chave primaria, em lotes (keyset, sem OFFSET)."""
    if batch_size <= 0:
        raise ValueError("batch_size deve ser positivo")
    select = ", ".join(f'"{column}"' for column in columns)
    statement = f'SELECT {select} FROM "{table}" ORDER BY id'
    cursor = connection.execute(statement)
    try:
        while True:
            batch = cursor.fetchmany(batch_size)
            if not batch:
                break
            for row in batch:
                yield row
    finally:
        cursor.close()


def extract(
    sqlite_path: str | Path,
    ufs: Sequence[str] | None,
    source_version: str,
    batch_size: int = DEFAULT_BATCH_SIZE,
    tolerance_m: float = DEFAULT_CENTROID_TOLERANCE_M,
    low_sample_threshold: int = DEFAULT_LOW_SAMPLE_THRESHOLD,
) -> dict[str, dict]:
    """Executa a extracao e devolve um resultado por UF."""
    requested = {classify_uf(uf) for uf in (ufs or [])}
    requested.discard(None)
    if ufs and not requested:
        raise ValueError("Nenhuma UF valida informada")

    aggregators: dict[str, CnefeAggregator] = {
        uf: CnefeAggregator(uf, source_version, tolerance_m, low_sample_threshold)
        for uf in (requested or [])
    }
    calculated_at_utc = utc_now_iso()
    connection = open_readonly_connection(sqlite_path)
    try:
        # Uma unica passada por ``bairros`` e por ``ruas``, separando as UFs em
        # streaming. Sem OFFSET e sem indice por UF (que nao existe na origem).
        for row in _iter_table(
            connection,
            NEIGHBORHOODS_TABLE,
            ("id", "uf_codigo", "municipio_codigo", "nome", "latitude_central", "longitude_central"),
            batch_size,
        ):
            uf = classify_uf(row[1])
            if uf is None or (requested and uf not in requested):
                continue
            aggregator = aggregators.get(uf)
            if aggregator is None:
                aggregator = CnefeAggregator(uf, source_version, tolerance_m, low_sample_threshold)
                aggregators[uf] = aggregator
            aggregator.add_neighborhood_row(*row)

        for row in _iter_table(
            connection,
            STREETS_TABLE,
            ("id", "uf_codigo", "municipio_codigo", "bairro", "logradouro", "cep", "latitude", "longitude"),
            batch_size,
        ):
            uf = classify_uf(row[1])
            if uf is None or (requested and uf not in requested):
                continue
            aggregator = aggregators.get(uf)
            if aggregator is None:
                aggregator = CnefeAggregator(uf, source_version, tolerance_m, low_sample_threshold)
                aggregators[uf] = aggregator
            aggregator.add_street_row(*row)
    finally:
        connection.close()

    results: dict[str, dict] = {}
    for uf, aggregator in aggregators.items():
        results[uf] = aggregator.finalize(calculated_at_utc)
    return results


# ---------------------------------------------------------------------------
# Staging, relatorios e manifesto
# ---------------------------------------------------------------------------


def write_csv(path: Path, columns: Sequence[str], rows: Iterable[dict]) -> int:
    path.parent.mkdir(parents=True, exist_ok=True)
    written = 0
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(columns), lineterminator="\n")
        writer.writeheader()
        for row in rows:
            writer.writerow({column: row.get(column, "") for column in columns})
            written += 1
    return written


def write_staging(output_dir: Path, uf: str, result: dict, errors: Sequence[ImportErrorRecord]) -> dict[str, int]:
    uf_dir = output_dir / "staging" / uf.lower()
    counts = {
        "neighborhoods": write_csv(uf_dir / STAGING_NEIGHBORHOODS, NEIGHBORHOOD_COLUMNS, result["neighborhoods"]),
        "streets": write_csv(uf_dir / STAGING_STREETS, STREET_COLUMNS, result["streets"]),
        "zip_codes": write_csv(uf_dir / STAGING_ZIP_CODES, ZIP_CODE_COLUMNS, result["zip_codes"]),
        "errors": write_csv(
            uf_dir / STAGING_ERRORS,
            ERROR_COLUMNS,
            [dict(zip(ERROR_COLUMNS, error.as_row())) for error in errors],
        ),
    }
    return counts


def write_uf_report(
    reports_dir: Path,
    uf: str,
    source_version: str,
    result: dict,
    counts: Counter,
    errors: Sequence[ImportErrorRecord],
    reject_rate: float,
    tolerance_m: float,
) -> Path:
    reports_dir.mkdir(parents=True, exist_ok=True)
    path = reports_dir / f"{uf.lower()}-report.md"
    error_counts = Counter(error.code for error in errors)

    lines = [
        f"# Importacao CNEFE - UF {uf}",
        "",
        f"- Versao da fonte: `{source_version}`",
        f"- Bairros publicados: {len(result['neighborhoods'])}",
        f"- Logradouros publicados: {counts.get('streets', 0)}",
        f"- CEPs publicados: {counts.get('zip_codes', 0)}",
        f"- Linhas de `ruas` lidas: {result['rows_read']}",
        f"- Publicadas: {result['rows_published']}",
        f"- Rejeitadas: {result['rows_rejected']}",
        f"- Ignoradas por duplicidade: {result['rows_skipped']}",
        f"- Reconciliacao fecha: {'sim' if result['reconciles'] else 'NAO'}",
        f"- Taxa de rejeicao: {reject_rate:.4%}",
        f"- Tolerancia de centroide: {tolerance_m} m",
        "",
        "## Centroides",
        "",
        f"- Materializado validado: {counts.get('centroid_materialized_validated', 0)}",
        f"- Recomputado: {counts.get('centroid_recomputed', 0)}",
        f"- Pendentes (sem pontos): {counts.get('centroid_pending', 0)}",
        f"- Divergencia acima da tolerancia: {counts.get('centroid_mismatch', 0)}",
        f"- Amostra baixa (< 5 pontos): {counts.get('centroid_low_sample', 0)}",
        "",
        "## Rejeicoes por codigo",
        "",
    ]

    if error_counts:
        lines.extend(f"- `{code}`: {total}" for code, total in sorted(error_counts.items()))
    else:
        lines.append("- Nenhuma rejeicao registrada.")

    lines.extend(
        [
            "",
            "## Classificacao auxiliar de localidades",
            "",
        ]
    )
    locality_types = result.get("locality_types") or {}
    if locality_types:
        lines.extend(f"- `{kind}`: {total}" for kind, total in sorted(locality_types.items()))
    else:
        lines.append("- Sem dados.")

    path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return path


def write_centroid_report(reports_dir: Path, results: dict[str, dict]) -> Path:
    reports_dir.mkdir(parents=True, exist_ok=True)
    path = reports_dir / "centroid-validation.md"
    lines = [
        "# Validacao dos centroides de bairro",
        "",
        "Metodo: `ADDRESS_POINT_MEAN`. O valor materializado em `bairros.latitude_central`/`longitude_central`",
        "e copiado e validado contra a media direta dos pontos validos de `ruas` do bairro.",
        "Uma divergencia acima da tolerancia bloqueia o bairro (`COORD_CENTROID_MISMATCH`) e nao e corrigida",
        "silenciosamente. A media de medias nunca e utilizada.",
        "",
        "| UF | Bairros | Materializado validado | Recomputado | Pendentes | Divergentes | Amostra baixa |",
        "|---|---|---|---|---|---|---|",
    ]
    for uf in sorted(results):
        result = results[uf]
        lines.append(
            "| {uf} | {nb} | {validated} | {recomputed} | {pending} | {mismatch} | {low} |".format(
                uf=uf,
                nb=len(result["neighborhoods"]),
                validated=result["counts"].get("centroid_materialized_validated", 0),
                recomputed=result["counts"].get("centroid_recomputed", 0),
                pending=result["counts"].get("centroid_pending", 0),
                mismatch=result["counts"].get("centroid_mismatch", 0),
                low=result["counts"].get("centroid_low_sample", 0),
            )
        )
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return path


def write_curation_report(reports_dir: Path, results: dict[str, dict]) -> Path:
    reports_dir.mkdir(parents=True, exist_ok=True)
    path = reports_dir / "curation-localities.csv"
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.writer(handle, lineterminator="\n")
        writer.writerow(("uf", "municipio_ibge", "locality", "locality_type", "coordinate_status"))
        for uf in sorted(results):
            for row in results[uf]["neighborhoods"]:
                if row["locality_type"] in ("indigenous_village", "rural_locality", "other", "unknown"):
                    writer.writerow(
                        (
                            row["uf"],
                            row["municipio_ibge"],
                            row["neighborhood"],
                            row["locality_type"],
                            row["coordinate_status"],
                        )
                    )
    return path


def write_manifest(
    manifests_dir: Path,
    source_version: str,
    sqlite_path: Path,
    checksum: str,
    ufs: Sequence[str],
    results: dict[str, dict],
    tolerance_m: float,
    mapping_version: str,
) -> Path:
    manifests_dir.mkdir(parents=True, exist_ok=True)
    path = manifests_dir / f"{source_version}.json"
    payload = {
        "source_name": "CNEFE",
        "source_version": source_version,
        "logical_path": str(sqlite_path),
        "size_bytes": sqlite_path.stat().st_size,
        "modified_at_utc": datetime.fromtimestamp(
            sqlite_path.stat().st_mtime, tz=timezone.utc
        ).isoformat(timespec="seconds"),
        "sha256": checksum,
        "extracted_at_utc": utc_now_iso(),
        "mapping_version": mapping_version,
        "centroid_tolerance_m": tolerance_m,
        "ufs": list(ufs),
        "totals": {
            uf: {
                "neighborhoods": len(results[uf]["neighborhoods"]),
                "streets": results[uf]["counts"].get("streets_published", 0),
                "zip_codes": results[uf]["counts"].get("zip_codes", 0),
                "rows_read": results[uf]["rows_read"],
                "rows_rejected": results[uf]["rows_rejected"],
                "rows_skipped": results[uf]["rows_skipped"],
            }
            for uf in sorted(results)
        },
    }
    path.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return path


# ---------------------------------------------------------------------------
# Publicacao (opcional, PostgreSQL)
# ---------------------------------------------------------------------------



def _safe_identifier(value: str, field: str) -> str:
    """Valida valores interpolados no script SQL de publicacao."""
    if not re.fullmatch(r"[A-Za-z0-9_.:-]{1,64}", value):
        raise ValueError(f"{field} contem caracteres nao permitidos: {value!r}")
    return value


STAGING_DDL = (
    """
    CREATE TEMP TABLE stg_neighborhoods (
        uf char(2), municipio_ibge varchar(7), city_name varchar(255), neighborhood varchar(150),
        normalized_name varchar(255), kind varchar(20), latitude varchar(32), longitude varchar(32),
        coordinate_method varchar(40), coordinate_point_count integer, coordinate_status varchar(20),
        coordinate_source_version varchar(64), coordinate_calculated_at_utc varchar(40),
        representative_latitude varchar(32), representative_longitude varchar(32),
        representative_method varchar(40), locality_type varchar(30), source varchar(30)
    ) ON COMMIT DROP;
    """.strip(),
    """
    CREATE TEMP TABLE stg_streets (
        uf char(2), municipio_ibge varchar(7), neighborhood varchar(150),
        normalized_neighborhood varchar(255), street varchar(255),
        normalized_street varchar(255), latitude varchar(32), longitude varchar(32),
        coordinate_method varchar(40), address_point_count integer, source varchar(30),
        source_version varchar(64)
    ) ON COMMIT DROP;
    """.strip(),
    """
    CREATE TEMP TABLE stg_zip_codes (
        uf char(2), municipio_ibge varchar(7), neighborhood varchar(255), street varchar(255),
        zip_code char(8), is_primary boolean
    ) ON COMMIT DROP;
    """.strip(),
)

UNKNOWN_CITY_GUARD = """
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM stg_neighborhoods s
        LEFT JOIN "Cities" c ON c."IbgeCode" = s.municipio_ibge
        WHERE c."Id" IS NULL
    ) THEN
        RAISE EXCEPTION 'UNKNOWN_CITY: carregue o snapshot do IBGE Localidades antes da publicacao';
    END IF;
END $$;
""".strip()

UPSERT_NEIGHBORHOODS = """
INSERT INTO "DeliveryNeighborhoods" AS target (
    "Id", "CityId", "City", "Neighborhood", "NormalizedName", "Kind", "Latitude", "Longitude",
    "CoordinateMethod", "CoordinatePointCount", "CoordinateStatus", "CoordinateSourceVersion",
    "CoordinateCalculatedAtUtc", "RepresentativeLatitude", "RepresentativeLongitude",
    "RepresentativeMethod", "LocalityType", "Source", "IsActive", "CreatedAtUtc")
SELECT gen_random_uuid(), c."Id", c."Name", s.neighborhood, s.normalized_name, s.kind,
       NULLIF(s.latitude, '')::double precision, NULLIF(s.longitude, '')::double precision,
       NULLIF(s.coordinate_method, ''), s.coordinate_point_count, s.coordinate_status,
       s.coordinate_source_version, NULLIF(s.coordinate_calculated_at_utc, '')::timestamptz,
       NULLIF(s.representative_latitude, '')::double precision,
       NULLIF(s.representative_longitude, '')::double precision,
       NULLIF(s.representative_method, ''), NULLIF(s.locality_type, ''), s.source, true, now()
FROM stg_neighborhoods s
JOIN "Cities" c ON c."IbgeCode" = s.municipio_ibge
ON CONFLICT ("CityId", "NormalizedName") WHERE "CityId" IS NOT NULL DO UPDATE SET
    "Neighborhood" = EXCLUDED."Neighborhood",
    "Latitude" = EXCLUDED."Latitude",
    "Longitude" = EXCLUDED."Longitude",
    "CoordinateMethod" = EXCLUDED."CoordinateMethod",
    "CoordinatePointCount" = EXCLUDED."CoordinatePointCount",
    "CoordinateStatus" = EXCLUDED."CoordinateStatus",
    "CoordinateSourceVersion" = EXCLUDED."CoordinateSourceVersion",
    "CoordinateCalculatedAtUtc" = EXCLUDED."CoordinateCalculatedAtUtc",
    "RepresentativeLatitude" = EXCLUDED."RepresentativeLatitude",
    "RepresentativeLongitude" = EXCLUDED."RepresentativeLongitude",
    "RepresentativeMethod" = EXCLUDED."RepresentativeMethod",
    "LocalityType" = EXCLUDED."LocalityType",
    "UpdatedAtUtc" = now()
WHERE target."Kind" <> 'manual';
""".strip()

UPSERT_STREETS_WITH_NEIGHBORHOOD = """
INSERT INTO "DeliveryStreets" AS target (
    "Id", "CityId", "NeighborhoodId", "Name", "NormalizedName", "Latitude", "Longitude",
    "CoordinateMethod", "AddressPointCount", "Source", "SourceVersion", "IsActive", "CreatedAtUtc")
SELECT gen_random_uuid(), c."Id", n."Id", s.street, s.normalized_street,
       NULLIF(s.latitude, '')::double precision, NULLIF(s.longitude, '')::double precision,
       NULLIF(s.coordinate_method, ''), s.address_point_count, s.source, s.source_version, true, now()
FROM stg_streets s
JOIN "Cities" c ON c."IbgeCode" = s.municipio_ibge
JOIN "DeliveryNeighborhoods" n
  ON n."CityId" = c."Id" AND n."NormalizedName" = s.normalized_neighborhood
ON CONFLICT ("CityId", "NeighborhoodId", "NormalizedName") WHERE "NeighborhoodId" IS NOT NULL DO UPDATE SET
    "Name" = EXCLUDED."Name",
    "Latitude" = EXCLUDED."Latitude",
    "Longitude" = EXCLUDED."Longitude",
    "CoordinateMethod" = EXCLUDED."CoordinateMethod",
    "AddressPointCount" = EXCLUDED."AddressPointCount",
    "SourceVersion" = EXCLUDED."SourceVersion",
    "UpdatedAtUtc" = now();
""".strip()

UPSERT_STREETS_WITHOUT_NEIGHBORHOOD = """
INSERT INTO "DeliveryStreets" (
    "Id", "CityId", "NeighborhoodId", "Name", "NormalizedName", "Latitude", "Longitude",
    "CoordinateMethod", "AddressPointCount", "Source", "SourceVersion", "IsActive", "CreatedAtUtc")
SELECT gen_random_uuid(), c."Id", NULL, s.street, s.normalized_street,
       NULLIF(s.latitude, '')::double precision, NULLIF(s.longitude, '')::double precision,
       NULLIF(s.coordinate_method, ''), s.address_point_count, s.source, s.source_version, true, now()
FROM stg_streets s
JOIN "Cities" c ON c."IbgeCode" = s.municipio_ibge
LEFT JOIN "DeliveryNeighborhoods" n
  ON n."CityId" = c."Id" AND n."NormalizedName" = s.normalized_neighborhood
WHERE n."Id" IS NULL
ON CONFLICT ("CityId", "NormalizedName") WHERE "NeighborhoodId" IS NULL DO NOTHING;
""".strip()

UPSERT_ZIP_CODES = """
INSERT INTO "DeliveryStreetZipCodes" ("StreetId", "ZipCode", "IsPrimary", "Source", "SourceVersion", "CreatedAtUtc")
SELECT st."Id", z.zip_code, z.is_primary, 'cnefe', '{version}', now()
FROM stg_zip_codes z
JOIN "Cities" c ON c."IbgeCode" = z.municipio_ibge
JOIN "DeliveryNeighborhoods" n
  ON n."CityId" = c."Id" AND n."NormalizedName" = z.neighborhood
JOIN "DeliveryStreets" st
  ON st."CityId" = c."Id" AND st."NeighborhoodId" = n."Id" AND st."NormalizedName" = z.street
WHERE z.zip_code ~ '^[0-9]{{8}}$'
ON CONFLICT ("StreetId", "ZipCode") DO NOTHING;
""".strip()

UPSERT_ZIP_CODES_WITHOUT_NEIGHBORHOOD = """
INSERT INTO "DeliveryStreetZipCodes" ("StreetId", "ZipCode", "IsPrimary", "Source", "SourceVersion", "CreatedAtUtc")
SELECT st."Id", z.zip_code, z.is_primary, 'cnefe', '{version}', now()
FROM stg_zip_codes z
JOIN "Cities" c ON c."IbgeCode" = z.municipio_ibge
JOIN "DeliveryStreets" st
  ON st."CityId" = c."Id" AND st."NeighborhoodId" IS NULL AND st."NormalizedName" = z.street
WHERE z.zip_code ~ '^[0-9]{{8}}$' AND z.neighborhood = ''
ON CONFLICT ("StreetId", "ZipCode") DO NOTHING;
""".strip()


def build_publish_statements(source_version: str, uf: str, run_id: str) -> list[str]:
    """Script de publicacao versionado, executado por UF dentro de uma transacao."""
    version = _safe_identifier(source_version, "source_version")
    normalized_uf = _safe_identifier(uf, "uf")
    run = _safe_identifier(run_id, "run_id")

    return [
        *STAGING_DDL,
        UNKNOWN_CITY_GUARD,
        UPSERT_NEIGHBORHOODS,
        UPSERT_STREETS_WITH_NEIGHBORHOOD,
        UPSERT_STREETS_WITHOUT_NEIGHBORHOOD,
        UPSERT_ZIP_CODES.format(version=version),
        UPSERT_ZIP_CODES_WITHOUT_NEIGHBORHOOD.format(version=version),
        f"""
INSERT INTO "ImportDatasets" ("Id", "SourceVersion", "Uf", "RunId", "IsActive", "ActivatedAtUtc", "CreatedAtUtc")
VALUES (gen_random_uuid(), '{version}', '{normalized_uf}', '{run}', false, NULL, now())
ON CONFLICT ("SourceVersion", "Uf") DO NOTHING;
""".strip(),
        f"""
UPDATE "ImportDatasets" SET "IsActive" = false, "DeactivatedAtUtc" = now()
WHERE "Uf" = '{normalized_uf}' AND "IsActive" AND "SourceVersion" <> '{version}';
""".strip(),
        f"""
UPDATE "ImportDatasets" SET "IsActive" = true, "ActivatedAtUtc" = now(), "DeactivatedAtUtc" = NULL
WHERE "Uf" = '{normalized_uf}' AND "SourceVersion" = '{version}';
""".strip(),
    ]


def publish_uf(
    output_dir: Path,
    uf: str,
    source_version: str,
    result: dict,
    checksum: str,
    metadata: dict,
) -> str:
    """Publica uma UF no PostgreSQL (transacional, por UF, sem TRUNCATE global).

    Exige ``psycopg2`` e as variaveis ``URBEAT_DB_*``. O script SQL equivalente e
    gravado em ``staging/<uf>/publish.sql`` para revisao e execucao manual em
    homologacao. Este comando nunca e executado automaticamente.
    """
    try:
        import psycopg2  # type: ignore
    except ImportError as error:  # pragma: no cover - depende do ambiente
        raise ImportAborted(
            "psycopg2 nao esta instalado. O staging e os relatorios ja foram gravados; "
            "instale psycopg2 ou revise staging/<uf>/publish.sql manualmente."
        ) from error

    connection = psycopg2.connect(
        host=os.environ.get("URBEAT_DB_HOST", "localhost"),
        port=os.environ.get("URBEAT_DB_PORT", "5432"),
        dbname=os.environ.get("URBEAT_DB_NAME", "urbeatdb"),
        user=os.environ.get("URBEAT_DB_USER", "urbeatpostg"),
        password=os.environ.get("URBEAT_DB_PASSWORD"),
    )

    try:
        with connection, connection.cursor() as cursor:
            # Advisory lock por UF: duas cargas concorrentes da mesma UF nao rodam juntas.
            cursor.execute("SELECT pg_advisory_xact_lock(hashtext(%s))", (f"cnefe:{uf}",))
            cursor.execute(
                """
                INSERT INTO "ImportRuns" ("Id","SourceName","SourceVersion","SourceChecksum","Uf","Status",
                    "RowsRead","RowsStaged","RowsPublished","RowsRejected","RowsSkipped","StartedAtUtc","Metadata","CreatedAtUtc")
                VALUES (gen_random_uuid(),'CNEFE',%s,%s,%s,'publishing',%s,%s,%s,%s,%s,now(),%s::jsonb,now())
                RETURNING "Id"::text
                """,
                (
                    source_version,
                    checksum,
                    uf,
                    result["rows_read"],
                    len(result["streets"]),
                    result["rows_published"],
                    result["rows_rejected"],
                    result["rows_skipped"],
                    json.dumps(metadata, ensure_ascii=False),
                ),
            )
            run_id = cursor.fetchone()[0]

            statements = build_publish_statements(source_version, uf, run_id)
            ddl_count = len(STAGING_DDL)
            for statement in statements[:ddl_count]:
                cursor.execute(statement)

            staging_dir = output_dir / "staging" / uf.lower()
            for filename, table in (
                (STAGING_NEIGHBORHOODS, "stg_neighborhoods"),
                (STAGING_STREETS, "stg_streets"),
                (STAGING_ZIP_CODES, "stg_zip_codes"),
            ):
                with (staging_dir / filename).open("r", encoding="utf-8", newline="") as handle:
                    cursor.copy_expert(
                        f"COPY {table} FROM STDIN WITH (FORMAT csv, HEADER true, NULL '')",
                        handle,
                    )

            # Depois do COPY: guarda UNKNOWN_CITY, upserts e troca do ponteiro de versao.
            for statement in statements[ddl_count:]:
                cursor.execute(statement)

            cursor.execute(
                """
                UPDATE "ImportRuns" SET "Status" = 'published', "FinishedAtUtc" = now(),
                    "RowsRead" = %s, "RowsPublished" = %s, "RowsRejected" = %s, "RowsSkipped" = %s
                WHERE "Id"::text = %s
                """,
                (
                    result["rows_read"],
                    result["rows_published"],
                    result["rows_rejected"],
                    result["rows_skipped"],
                    run_id,
                ),
            )

            (staging_dir / "publish.sql").write_text(
                "\n\n".join(build_publish_statements(source_version, uf, run_id)) + "\n",
                encoding="utf-8",
            )

        return run_id
    finally:
        connection.close()


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Importacao CNEFE (SQLite -> staging PostgreSQL)")
    parser.add_argument("--sqlite-path", default=os.environ.get("URBEAT_CNEFE_SQLITE_PATH"))
    parser.add_argument("--source-version", default=os.environ.get("URBEAT_CNEFE_SOURCE_VERSION", DEFAULT_SOURCE_VERSION))
    parser.add_argument("--uf", action="append", default=None, help="UF (sigla ou codigo IBGE). Repita para varias.")
    parser.add_argument("--output-dir", default="imports/cnefe")
    parser.add_argument("--batch-size", type=int, default=int(os.environ.get("URBEAT_IMPORT_BATCH_SIZE", DEFAULT_BATCH_SIZE)))
    parser.add_argument(
        "--tolerance-m",
        type=float,
        default=float(os.environ.get("URBEAT_CNEFE_CENTROID_TOLERANCE_M", DEFAULT_CENTROID_TOLERANCE_M)),
    )
    parser.add_argument(
        "--max-reject-rate",
        type=float,
        default=float(os.environ.get("URBEAT_IMPORT_MAX_REJECT_RATE", DEFAULT_MAX_REJECT_RATE)),
    )
    parser.add_argument("--dry-run", action="store_true", help="Valida e imprime; nao grava staging.")
    parser.add_argument("--publish", action="store_true", help="Publica no PostgreSQL (exige psycopg2).")
    parser.add_argument(
        "--strict-baseline",
        action="store_true",
        help="Falha se a passada nacional divergir do baseline documentado (contagens/UF/municipios).",
    )
    parser.add_argument("--mapping-version", default="cnefe-source-mapping-v1")
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)

    if not args.sqlite_path:
        parser.error("--sqlite-path ou URBEAT_CNEFE_SQLITE_PATH e obrigatorio")

    sqlite_path = Path(args.sqlite_path).expanduser().resolve()
    output_dir = Path(args.output_dir)

    checksum_before = sha256_file(sqlite_path)
    results = extract(
        sqlite_path,
        args.uf,
        args.source_version,
        batch_size=args.batch_size,
        tolerance_m=args.tolerance_m,
    )
    checksum_after = sha256_file(sqlite_path)
    if checksum_before != checksum_after:
        raise ImportAborted("O arquivo SQLite mudou durante a importacao; operacao abortada.")

    if args.strict_baseline and not args.uf:
        neighborhood_rows = sum(len(result["neighborhoods"]) for result in results.values())
        municipality_codes = {
            row["municipio_ibge"]
            for result in results.values()
            for row in result["neighborhoods"]
        }
        actual = {
            "neighborhood_rows": neighborhood_rows,
            "street_rows": sum(result["rows_read"] for result in results.values()),
            "uf_count": len(results),
            "municipality_count": len(municipality_codes),
        }
        divergences = {
            key: (expected, actual[key])
            for key, expected in BASELINE.items()
            if key in actual and actual[key] != expected
        }
        if divergences:
            detail = ", ".join(
                f"{key}: esperado {expected}, lido {value}"
                for key, (expected, value) in sorted(divergences.items())
            )
            raise ImportAborted(f"Divergencia de baseline ({BASELINE['reference']}): {detail}")

    if args.dry_run:
        for uf in sorted(results):
            result = results[uf]
            print(
                f"[dry-run] UF {uf}: bairros={len(result['neighborhoods'])} "
                f"logradouros={len(result['streets'])} lidas={result['rows_read']} "
                f"rejeitadas={result['rows_rejected']} duplicadas={result['rows_skipped']} "
                f"reconcilia={result['reconciles']}"
            )
        return 0

    all_rejections = 0
    reports_dir = output_dir / "reports"
    for uf in sorted(results):
        result = results[uf]
        uf_aggregator_errors = result.get("errors", [])
        reject_rate = result["rows_rejected"] / result["rows_read"] if result["rows_read"] else 0.0
        if reject_rate > args.max_reject_rate:
            raise ImportAborted(
                f"UF {uf}: taxa de rejeicao {reject_rate:.4%} acima do limite {args.max_reject_rate:.4%}"
            )
        if not result["reconciles"]:
            raise ImportAborted(f"UF {uf}: reconciliacao nao fecha ({result['rows_read']} != "
                                f"{result['rows_published']} + {result['rows_rejected']} + {result['rows_skipped']})")
        all_rejections += result["rows_rejected"]

    for uf in sorted(results):
        result = results[uf]
        errors = result.get("errors", [])
        counts = write_staging(output_dir, uf, result, errors)
        write_uf_report(
            reports_dir,
            uf,
            args.source_version,
            result,
            result["counts"],
            errors,
            result["rows_rejected"] / result["rows_read"] if result["rows_read"] else 0.0,
            args.tolerance_m,
        )
        if args.publish:
            publish_uf(
                output_dir,
                uf,
                args.source_version,
                result,
                checksum_before,
                {
                    "mapping_version": args.mapping_version,
                    "tolerance_m": args.tolerance_m,
                    "reject_rate": result["rows_rejected"] / result["rows_read"] if result["rows_read"] else 0.0,
                    "locality_types": dict(result["counts"].get("locality_types", {})),
                    "neighborhoods": len(result["neighborhoods"]),
                    "streets": counts["streets"],
                    "zip_codes": counts["zip_codes"],
                },
            )
        print(
            f"UF {uf}: bairros={len(result['neighborhoods'])} logradouros={counts['streets']} "
            f"ceps={counts['zip_codes']} lidas={result['rows_read']} "
            f"rejeitadas={result['rows_rejected']} duplicadas={result['rows_skipped']}"
        )

    write_centroid_report(reports_dir, results)
    write_curation_report(reports_dir, results)
    payload = write_manifest(
        output_dir / "manifests",
        args.source_version,
        sqlite_path,
        checksum_before,
        sorted(results),
        results,
        args.tolerance_m,
        args.mapping_version,
    )

    print(f"Manifesto: {payload}")
    print(f"Rejeicoes registradas: {all_rejections}")
    return 0


if __name__ == "__main__":  # pragma: no cover
    raise SystemExit(main())
