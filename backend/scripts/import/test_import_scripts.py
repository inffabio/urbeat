import os
import sqlite3
import tempfile
from pathlib import Path
import unittest
from unittest.mock import call, patch

import import_common
from import_common import candidate_key, candidate_path, load_local_environment, normalize_ibge_code
from brasil_aberto_import import (
    distinct_district_names,
    ibge_state_code,
    is_manual_record,
    is_official_record,
    normalize_neighborhood_name,
    replace_city_neighborhoods,
    snapshot_path,
    validate_uf,
)
from geocode_via_cep import (
    brasil_aberto_delay_seconds,
    cep_aberto_delay_seconds,
    cep_aberto_token,
    get_cep_from_dne,
    get_coordinates_from_cep,
    get_coordinates_from_cep_aberto,
    get_coordinates_from_mapbox,
    get_coordinates_from_nominatim,
    get_first_street_from_dne,
    mapbox_delay_seconds,
    mapbox_token,
)
from neighborhood_snapshot import (
    export_snapshot,
    read_snapshot,
    restore_snapshot,
    validate_snapshot_rows,
    write_snapshot,
)
from neighborhood_rebuild import rebuild_uf
from neighborhood_candidates import (
    build_candidates,
    export_candidates_by_uf,
    read_records,
    source_priority,
    write_candidates,
)
from neighborhood_source_import import (
    build_dne_street_index,
    geocode_candidates,
    get_first_street_from_dne_by_ibge,
    municipality_lookup,
    read_dne_neighborhoods,
)


IMPORT_DIR = Path(__file__).parent


class _ReplacementCursor:
    def __init__(self, rows=(), fail_on=None):
        self.rows = list(rows)
        self.statements = []
        self.fail_on = fail_on
        self.closed = False

    def execute(self, statement, params=None):
        self.statements.append((statement, params))
        if self.fail_on and self.fail_on in statement:
            raise RuntimeError("boom")

    def fetchall(self):
        return self.rows

    def close(self):
        self.closed = True

    def __enter__(self):
        return self

    def __exit__(self, *args):
        return False


class _ReplacementConnection:
    def __init__(self, rows=(), fail_on=None):
        self.rows = rows
        self.fail_on = fail_on
        self.commits = 0
        self.rollbacks = 0
        self.cursors = []

    def cursor(self):
        cursor = _ReplacementCursor(self.rows, self.fail_on)
        self.cursors.append(cursor)
        return cursor

    def commit(self):
        self.commits += 1

    def rollback(self):
        self.rollbacks += 1

    def close(self):
        self.closed = True


class _GeocodeCursor:
    def __init__(self, neighborhoods):
        self._neighborhoods = list(neighborhoods)
        self.updates = []
        self._fetch_count = 0
        self.last_statement = None
        self.last_params = None
        self.closed = False

    def execute(self, statement, params=None):
        self.last_statement = statement
        self.last_params = params
        if statement.lstrip().upper().startswith("UPDATE"):
            self.updates.append((statement, params))

    def fetchall(self):
        self._fetch_count += 1
        if self._fetch_count == 1:
            return self._neighborhoods
        return []

    def close(self):
        self.closed = True

    def __enter__(self):
        return self

    def __exit__(self, *args):
        return False


class _GeocodeConnection:
    def __init__(self, neighborhoods):
        self.cursor_instance = _GeocodeCursor(neighborhoods)
        self.commits = 0
        self.autocommit = False

    def cursor(self):
        return self.cursor_instance

    def commit(self):
        self.commits += 1

    def close(self):
        self.closed = True


class ImportScriptsTests(unittest.TestCase):
    def test_local_config_loads_missing_values_without_overriding_environment(self):
        config = IMPORT_DIR / "test-import.env"
        try:
            config.write_text("BRASIL_ABERTO_API_KEY=local-key\nURBEAT_DB_PASSWORD=local-password\n", encoding="utf-8")
            with patch.dict("os.environ", {"BRASIL_ABERTO_API_KEY": "existing-key"}, clear=False):
                load_local_environment(config)
                self.assertEqual(__import__("os").environ["BRASIL_ABERTO_API_KEY"], "existing-key")
                self.assertEqual(__import__("os").environ["URBEAT_DB_PASSWORD"], "local-password")
        finally:
            config.unlink(missing_ok=True)

    def test_validate_uf_accepts_all_brazilian_states_and_df(self):
        self.assertEqual(validate_uf("mg"), "MG")
        self.assertEqual(validate_uf("DF"), "DF")
        self.assertEqual(ibge_state_code("ES"), 32)

    def test_validate_uf_rejects_unknown_state(self):
        with self.assertRaises(ValueError):
            validate_uf("XX")

    def test_normalize_ibge_code_accepts_integers_floats_and_strings(self):
        self.assertEqual(normalize_ibge_code(3304557), "3304557")
        self.assertEqual(normalize_ibge_code(3304557.0), "3304557")
        self.assertEqual(normalize_ibge_code("3304557"), "3304557")
        self.assertEqual(normalize_ibge_code("3304557.0"), "3304557")
        self.assertEqual(normalize_ibge_code(" 3304557 "), "3304557")

    def test_normalize_ibge_code_rejects_invalid_or_partial_values(self):
        self.assertIsNone(normalize_ibge_code(None))
        self.assertIsNone(normalize_ibge_code(""))
        self.assertIsNone(normalize_ibge_code("   "))
        self.assertIsNone(normalize_ibge_code("abc"))
        self.assertIsNone(normalize_ibge_code(3304557.5))
        self.assertIsNone(normalize_ibge_code("3304557.5"))
        self.assertIsNone(normalize_ibge_code(True))

    def test_snapshot_path_uses_lowercase_uf(self):
        self.assertEqual(snapshot_path("MG").name, "bairros_mg.csv")

    def test_snapshot_round_trip_preserves_neighborhood_data(self):
        rows = [{
            "Uf": "SP",
            "CityIbgeCode": "3550308",
            "City": "Sao Paulo",
            "Neighborhood": "Centro",
            "NormalizedName": "centro",
            "Latitude": "-23.5505",
            "Longitude": "-46.6333",
            "Source": "brasil_aberto",
            "IsActive": "true",
        }]
        target = IMPORT_DIR / "test-snapshot.csv"
        try:
            write_snapshot(rows, target)
            self.assertEqual(read_snapshot(target), rows)
        finally:
            target.unlink(missing_ok=True)

    def test_snapshot_accepts_missing_coordinate_pair_and_preserves_empty_fields(self):
        rows = [{
            "Uf": "MG",
            "CityIbgeCode": "3106200",
            "City": "Belo Horizonte",
            "Neighborhood": "Bairro sem geolocalizacao",
            "NormalizedName": "bairro sem geolocalizacao",
            "Latitude": "",
            "Longitude": "",
            "Source": "brasil_aberto",
            "IsActive": "true",
        }]
        target = IMPORT_DIR / "pending-snapshot.csv"
        try:
            write_snapshot(rows, target)
            self.assertEqual(read_snapshot(target), rows)
        finally:
            target.unlink(missing_ok=True)

    def test_snapshot_rejects_missing_required_columns(self):
        target = IMPORT_DIR / "invalid-snapshot.csv"
        try:
            target.write_text("Uf,City\nSP,Sao Paulo\n", encoding="utf-8")
            with self.assertRaises(ValueError):
                read_snapshot(target)
        finally:
            target.unlink(missing_ok=True)

    def test_snapshot_rejects_incomplete_coordinates(self):
        target = IMPORT_DIR / "incomplete-snapshot.csv"
        rows = [{
            "Uf": "SP", "CityIbgeCode": "3550308", "City": "Sao Paulo",
            "Neighborhood": "Centro", "NormalizedName": "centro",
            "Latitude": "-23.55", "Longitude": "", "Source": "nominatim",
            "IsActive": "true",
        }]
        try:
            with self.assertRaisesRegex(ValueError, "Sao Paulo"):
                write_snapshot(rows, target)
        finally:
            target.unlink(missing_ok=True)

    def test_snapshot_rejects_invalid_coordinate_values(self):
        target = IMPORT_DIR / "invalid-coordinate-snapshot.csv"
        target.write_text(
            "Uf,CityIbgeCode,City,Neighborhood,NormalizedName,Latitude,Longitude,Source,IsActive\n"
            "SP,3550308,Sao Paulo,Centro,centro,91,-46.6,nominatim,true\n",
            encoding="utf-8",
        )
        try:
            with self.assertRaisesRegex(ValueError, "Sao Paulo"):
                read_snapshot(target)
        finally:
            target.unlink(missing_ok=True)

    def test_snapshot_rejects_empty_essential_fields(self):
        base = {
            "Uf": "SP", "CityIbgeCode": "3550308", "City": "Sao Paulo",
            "Neighborhood": "Centro", "NormalizedName": "centro",
            "Latitude": "-23.55", "Longitude": "-46.63", "Source": "nominatim",
            "IsActive": "true",
        }
        for field in ("Uf", "CityIbgeCode", "City", "Neighborhood", "NormalizedName"):
            for value in ("", "   ", None):
                with self.subTest(field=field, value=value):
                    rows = [{**base, field: value}]
                    with self.assertRaisesRegex(ValueError, "campos essenciais"):
                        validate_snapshot_rows(rows)

    def test_snapshot_rejects_mismatched_normalized_name(self):
        rows = [{
            "Uf": "SP", "CityIbgeCode": "3550308", "City": "Sao Paulo",
            "Neighborhood": "Sao Jose", "NormalizedName": "centro",
            "Latitude": "-23.55", "Longitude": "-46.63", "Source": "nominatim",
            "IsActive": "true",
        }]
        with self.assertRaisesRegex(ValueError, "NormalizedName inconsistente"):
            validate_snapshot_rows(rows)

    def test_snapshot_rejects_duplicate_uf_ibge_normalized_name(self):
        rows = [
            {
                "Uf": "SP", "CityIbgeCode": "3550308", "City": "Sao Paulo",
                "Neighborhood": "Centro", "NormalizedName": "centro",
                "Latitude": "-23.55", "Longitude": "-46.63", "Source": "nominatim",
                "IsActive": "true",
            },
            {
                "Uf": "SP", "CityIbgeCode": "3550308", "City": "Sao Paulo",
                "Neighborhood": "CENTRO", "NormalizedName": "centro",
                "Latitude": "", "Longitude": "", "Source": "brasil_aberto",
                "IsActive": "true",
            },
        ]
        with self.assertRaisesRegex(ValueError, "duplicidades"):
            validate_snapshot_rows(rows)

    def test_write_snapshot_rejects_duplicates_before_writing(self):
        rows = [
            {
                "Uf": "SP", "CityIbgeCode": "3550308", "City": "Sao Paulo",
                "Neighborhood": "Centro", "NormalizedName": "centro",
                "Latitude": "-23.55", "Longitude": "-46.63", "Source": "nominatim",
                "IsActive": "true",
            },
            {
                "Uf": "SP", "CityIbgeCode": "3550308", "City": "Sao Paulo",
                "Neighborhood": "CENTRO", "NormalizedName": "centro",
                "Latitude": "", "Longitude": "", "Source": "brasil_aberto",
                "IsActive": "true",
            },
        ]
        target = IMPORT_DIR / "duplicate-snapshot.csv"
        try:
            with self.assertRaisesRegex(ValueError, "duplicidades"):
                write_snapshot(rows, target)
            self.assertFalse(target.exists())
        finally:
            target.unlink(missing_ok=True)

    def test_scripts_require_runtime_credentials(self):
        for name in ("brasil_aberto_import.py", "geocode_via_cep.py", "import_common.py"):
            source = (IMPORT_DIR / name).read_text(encoding="utf-8")
            if name != "import_common.py":
                self.assertIn('os.environ.get("BRASIL_ABERTO_API_KEY")', source)
            if name == "import_common.py":
                self.assertIn('os.environ.get("URBEAT_DB_PASSWORD")', source)
            self.assertNotIn('os.environ.get("BRASIL_ABERTO_API_KEY",', source)
            self.assertNotIn('os.environ.get("URBEAT_DB_PASSWORD",', source)

    def test_database_connections_use_configured_port(self):
        for name in ("import_common.py", "neighborhood_snapshot.py"):
            source = (IMPORT_DIR / name).read_text(encoding="utf-8")
            self.assertIn('port=os.environ.get("URBEAT_DB_PORT",', source)


    def test_geocoders_preserve_existing_coordinates(self):
        for name in ("geocode_via_cep.py",):
            source = (IMPORT_DIR / name).read_text(encoding="utf-8")
            self.assertIn('d."Latitude" IS NULL OR d."Longitude" IS NULL', source)
            self.assertNotIn('COALESCE("Latitude", %s)', source)
            self.assertNotIn('COALESCE("Longitude", %s)', source)

    def test_nominatim_fallback_returns_real_coordinates_and_source(self):
        import geocode_via_cep

        original = geocode_via_cep.fetch_json
        try:
            geocode_via_cep.fetch_json = lambda url: [{"lat": "-22.9068", "lon": "-43.1729"}]
            self.assertEqual(
                get_coordinates_from_nominatim("Centro", "Rio de Janeiro", "RJ"),
                (-22.9068, -43.1729),
            )
        finally:
            geocode_via_cep.fetch_json = original

    def test_nominatim_fallback_reports_missing_coordinates(self):
        import geocode_via_cep

        original = geocode_via_cep.fetch_json
        try:
            geocode_via_cep.fetch_json = lambda url: []
            self.assertEqual(
                get_coordinates_from_nominatim("Bairro inexistente", "Rio", "RJ"),
                (None, None),
            )
        finally:
            geocode_via_cep.fetch_json = original

    def test_brasil_aberto_delay_seconds_defaults_and_parses(self):
        with patch.dict("os.environ", {}, clear=False):
            os.environ.pop("BRASIL_ABERTO_DELAY_SECONDS", None)
            self.assertEqual(brasil_aberto_delay_seconds(), 0.15)
        with patch.dict("os.environ", {"BRASIL_ABERTO_DELAY_SECONDS": "0.5"}):
            self.assertEqual(brasil_aberto_delay_seconds(), 0.5)
        with patch.dict("os.environ", {"BRASIL_ABERTO_DELAY_SECONDS": "abc"}):
            self.assertEqual(brasil_aberto_delay_seconds(), 0.15)
        with patch.dict("os.environ", {"BRASIL_ABERTO_DELAY_SECONDS": "-1"}):
            self.assertEqual(brasil_aberto_delay_seconds(), 0.0)

    def test_get_coordinates_from_cep_caches_repeated_lookups(self):
        import geocode_via_cep

        geocode_via_cep._cep_coordinate_cache.clear()
        calls = []
        original_fetch = geocode_via_cep.fetch_json
        try:
            geocode_via_cep.fetch_json = lambda url, headers=None: (
                calls.append(url)
                or {"result": {"coordinates": {"latitude": "-22.9", "longitude": "-43.1"}}}
            )
            with patch.object(geocode_via_cep.time, "sleep"):
                first = get_coordinates_from_cep("20010-000")
                second = get_coordinates_from_cep("20010-000")
                third = get_coordinates_from_cep("20010-000")
        finally:
            geocode_via_cep.fetch_json = original_fetch

        self.assertEqual(first, (-22.9, -43.1))
        self.assertEqual(second, first)
        self.assertEqual(third, first)
        self.assertEqual(len(calls), 1)

    def test_get_coordinates_from_cep_delays_configurably_before_call(self):
        import geocode_via_cep

        geocode_via_cep._cep_coordinate_cache.clear()
        sleeps = []
        original_fetch = geocode_via_cep.fetch_json
        try:
            geocode_via_cep.fetch_json = lambda url, headers=None: {
                "result": {"coordinates": {"latitude": "-22.9", "longitude": "-43.1"}}
            }
            with patch.dict("os.environ", {"BRASIL_ABERTO_DELAY_SECONDS": "0.25"}), \
                    patch.object(geocode_via_cep.time, "sleep", side_effect=sleeps.append):
                get_coordinates_from_cep("20010-000")
        finally:
            geocode_via_cep.fetch_json = original_fetch

        self.assertEqual(sleeps, [0.25])

    def test_cep_aberto_delay_seconds_defaults_and_parses(self):
        with patch.dict("os.environ", {}, clear=False):
            os.environ.pop("CEP_ABERTO_DELAY_SECONDS", None)
            self.assertEqual(cep_aberto_delay_seconds(), 0.15)
        with patch.dict("os.environ", {"CEP_ABERTO_DELAY_SECONDS": "0.5"}):
            self.assertEqual(cep_aberto_delay_seconds(), 0.5)
        with patch.dict("os.environ", {"CEP_ABERTO_DELAY_SECONDS": "abc"}):
            self.assertEqual(cep_aberto_delay_seconds(), 0.15)
        with patch.dict("os.environ", {"CEP_ABERTO_DELAY_SECONDS": "-1"}):
            self.assertEqual(cep_aberto_delay_seconds(), 0.0)

    def test_cep_aberto_token_never_uses_default(self):
        source = (IMPORT_DIR / "geocode_via_cep.py").read_text(encoding="utf-8")
        self.assertIn('os.environ.get("CEP_ABERTO_API_TOKEN")', source)
        self.assertNotIn('os.environ.get("CEP_ABERTO_API_TOKEN",', source)

    def test_cep_aberto_token_reads_environment_without_default(self):
        with patch.dict("os.environ", {}, clear=False):
            os.environ.pop("CEP_ABERTO_API_TOKEN", None)
            self.assertIsNone(cep_aberto_token())
        with patch.dict("os.environ", {"CEP_ABERTO_API_TOKEN": "secret-token"}):
            self.assertEqual(cep_aberto_token(), "secret-token")

    def test_cep_aberto_returns_none_without_token_and_does_not_fetch(self):
        import geocode_via_cep

        geocode_via_cep._cep_aberto_coordinate_cache.clear()
        original_fetch = geocode_via_cep.fetch_json
        try:
            geocode_via_cep.fetch_json = lambda url, headers=None: (_ for _ in ()).throw(AssertionError("fetch"))
            with patch.dict("os.environ", {}, clear=False):
                os.environ.pop("CEP_ABERTO_API_TOKEN", None)
                self.assertEqual(get_coordinates_from_cep_aberto("20010-000"), (None, None))
        finally:
            geocode_via_cep.fetch_json = original_fetch

    def test_cep_aberto_sends_token_header_and_parses_coordinates(self):
        import geocode_via_cep

        geocode_via_cep._cep_aberto_coordinate_cache.clear()
        calls = []
        original_fetch = geocode_via_cep.fetch_json
        try:
            def fake_fetch(url, headers=None):
                calls.append((url, headers))
                return {"latitude": "-22.9068", "longitude": "-43.1729"}

            geocode_via_cep.fetch_json = fake_fetch
            with patch.dict("os.environ", {"CEP_ABERTO_API_TOKEN": "secret-token"}), \
                    patch.object(geocode_via_cep.time, "sleep"):
                self.assertEqual(
                    get_coordinates_from_cep_aberto("20010-000"),
                    (-22.9068, -43.1729),
                )
        finally:
            geocode_via_cep.fetch_json = original_fetch

        self.assertEqual(len(calls), 1)
        url, headers = calls[0]
        self.assertEqual(url, "https://www.cepaberto.com/api/v3/cep?cep=20010000")
        self.assertEqual(headers, {"Authorization": "Token token=secret-token"})

    def test_cep_aberto_never_prints_token(self):
        import contextlib
        import io

        import geocode_via_cep

        geocode_via_cep._cep_aberto_coordinate_cache.clear()
        original_fetch = geocode_via_cep.fetch_json
        buffer = io.StringIO()
        try:
            geocode_via_cep.fetch_json = lambda url, headers=None: {"latitude": "-22.9", "longitude": "-43.1"}
            with patch.dict("os.environ", {"CEP_ABERTO_API_TOKEN": "secret-token"}), \
                    patch.object(geocode_via_cep.time, "sleep"), \
                    contextlib.redirect_stdout(buffer):
                get_coordinates_from_cep_aberto("20010-000")
        finally:
            geocode_via_cep.fetch_json = original_fetch

        self.assertNotIn("secret-token", buffer.getvalue())

    def test_cep_aberto_rejects_invalid_coordinates(self):
        import geocode_via_cep

        for payload in (
            {"latitude": "91.0", "longitude": "-43.1"},
            {"latitude": "-22.9", "longitude": "200.0"},
            {"latitude": "", "longitude": "-43.1"},
            {"latitude": "-22.9"},
            {},
        ):
            with self.subTest(payload=payload):
                geocode_via_cep._cep_aberto_coordinate_cache.clear()
                original_fetch = geocode_via_cep.fetch_json
                try:
                    geocode_via_cep.fetch_json = lambda url, headers=None: payload
                    with patch.dict("os.environ", {"CEP_ABERTO_API_TOKEN": "secret-token"}), \
                            patch.object(geocode_via_cep.time, "sleep"):
                        self.assertEqual(get_coordinates_from_cep_aberto("20010-000"), (None, None))
                finally:
                    geocode_via_cep.fetch_json = original_fetch

    def test_cep_aberto_caches_repeated_lookups_per_cep(self):
        import geocode_via_cep

        geocode_via_cep._cep_aberto_coordinate_cache.clear()
        calls = []
        original_fetch = geocode_via_cep.fetch_json
        try:
            geocode_via_cep.fetch_json = lambda url, headers=None: (
                calls.append(url)
                or {"latitude": "-22.9", "longitude": "-43.1"}
            )
            with patch.dict("os.environ", {"CEP_ABERTO_API_TOKEN": "secret-token"}), \
                    patch.object(geocode_via_cep.time, "sleep"):
                first = get_coordinates_from_cep_aberto("20010-000")
                second = get_coordinates_from_cep_aberto("20010-000")
                third = get_coordinates_from_cep_aberto("20010-000")
        finally:
            geocode_via_cep.fetch_json = original_fetch

        self.assertEqual(first, (-22.9, -43.1))
        self.assertEqual(second, first)
        self.assertEqual(third, first)
        self.assertEqual(len(calls), 1)

    def test_mapbox_token_reads_environment_without_default(self):
        with patch.dict("os.environ", {}, clear=False):
            os.environ.pop("MAPBOX_API_TOKEN", None)
            self.assertIsNone(mapbox_token())
        with patch.dict("os.environ", {"MAPBOX_API_TOKEN": "pk.secret"}):
            self.assertEqual(mapbox_token(), "pk.secret")

    def test_mapbox_token_never_uses_default(self):
        source = (IMPORT_DIR / "geocode_via_cep.py").read_text(encoding="utf-8")
        self.assertIn('os.environ.get("MAPBOX_API_TOKEN")', source)
        self.assertNotIn('os.environ.get("MAPBOX_API_TOKEN",', source)

    def test_mapbox_delay_seconds_defaults_and_parses(self):
        with patch.dict("os.environ", {}, clear=False):
            os.environ.pop("MAPBOX_DELAY_SECONDS", None)
            self.assertEqual(mapbox_delay_seconds(), 0.1)
        with patch.dict("os.environ", {"MAPBOX_DELAY_SECONDS": "0.5"}):
            self.assertEqual(mapbox_delay_seconds(), 0.5)
        with patch.dict("os.environ", {"MAPBOX_DELAY_SECONDS": "abc"}):
            self.assertEqual(mapbox_delay_seconds(), 0.1)
        with patch.dict("os.environ", {"MAPBOX_DELAY_SECONDS": "-1"}):
            self.assertEqual(mapbox_delay_seconds(), 0.0)

    def test_mapbox_returns_none_without_token_and_does_not_fetch(self):
        import geocode_via_cep

        geocode_via_cep._mapbox_coordinate_cache.clear()
        original_fetch = geocode_via_cep.fetch_json
        try:
            geocode_via_cep.fetch_json = lambda url: (_ for _ in ()).throw(AssertionError("fetch"))
            with patch.dict("os.environ", {}, clear=False):
                os.environ.pop("MAPBOX_API_TOKEN", None)
                self.assertEqual(
                    get_coordinates_from_mapbox("Avenida Brasil", "Rio de Janeiro", "RJ", "20000000"),
                    (None, None),
                )
        finally:
            geocode_via_cep.fetch_json = original_fetch

    def test_mapbox_builds_url_and_query_combining_street_cep_city_uf(self):
        from urllib.parse import unquote_plus

        import geocode_via_cep

        geocode_via_cep._mapbox_coordinate_cache.clear()
        calls = []
        original_fetch = geocode_via_cep.fetch_json
        try:
            def fake_fetch(url):
                calls.append(url)
                return {"features": [{"center": [-43.1729, -22.9068]}]}

            geocode_via_cep.fetch_json = fake_fetch
            with patch.dict("os.environ", {"MAPBOX_API_TOKEN": "pk.secret"}), \
                    patch.object(geocode_via_cep.time, "sleep"):
                result = get_coordinates_from_mapbox("Avenida Brasil", "Rio de Janeiro", "RJ", "20000000")
        finally:
            geocode_via_cep.fetch_json = original_fetch

        self.assertEqual(result, (-22.9068, -43.1729))
        self.assertEqual(len(calls), 1)
        url = calls[0]
        self.assertIn("https://api.mapbox.com/geocoding/v5/mapbox.places/", url)
        self.assertIn(".json?country=br&language=pt&limit=1&access_token=pk.secret", url)
        query = unquote_plus(url.split("/mapbox.places/")[1].split(".json")[0])
        self.assertEqual(query, "Avenida Brasil, 20000000, Rio de Janeiro, RJ, Brasil")

    def test_mapbox_query_omits_cep_when_absent_and_normalizes_uf(self):
        from urllib.parse import unquote_plus

        import geocode_via_cep

        geocode_via_cep._mapbox_coordinate_cache.clear()
        calls = []
        original_fetch = geocode_via_cep.fetch_json
        try:
            geocode_via_cep.fetch_json = lambda url: (calls.append(url) or {"features": []})
            with patch.dict("os.environ", {"MAPBOX_API_TOKEN": "pk.secret"}), \
                    patch.object(geocode_via_cep.time, "sleep"):
                get_coordinates_from_mapbox("Avenida Brasil", "Rio de Janeiro", "rj", None)
        finally:
            geocode_via_cep.fetch_json = original_fetch

        self.assertEqual(len(calls), 1)
        query = unquote_plus(calls[0].split("/mapbox.places/")[1].split(".json")[0])
        self.assertEqual(query, "Avenida Brasil, Rio de Janeiro, RJ, Brasil")

    def test_mapbox_parses_geometry_coordinates_when_center_absent(self):
        import geocode_via_cep

        geocode_via_cep._mapbox_coordinate_cache.clear()
        original_fetch = geocode_via_cep.fetch_json
        try:
            geocode_via_cep.fetch_json = lambda url: {"features": [
                {"geometry": {"coordinates": [-43.1729, -22.9068]}}
            ]}
            with patch.dict("os.environ", {"MAPBOX_API_TOKEN": "pk.secret"}), \
                    patch.object(geocode_via_cep.time, "sleep"):
                result = get_coordinates_from_mapbox("Rua A", "Rio de Janeiro", "RJ", "20000000")
        finally:
            geocode_via_cep.fetch_json = original_fetch

        self.assertEqual(result, (-22.9068, -43.1729))

    def test_mapbox_rejects_invalid_coordinates(self):
        import geocode_via_cep

        for payload in (
            {"features": [{"center": [1000.0, -43.1]}]},
            {"features": [{"center": [-43.1, -91.0]}]},
            {"features": [{"center": [None, None]}]},
            {"features": []},
            {},
        ):
            with self.subTest(payload=payload):
                geocode_via_cep._mapbox_coordinate_cache.clear()
                original_fetch = geocode_via_cep.fetch_json
                try:
                    geocode_via_cep.fetch_json = lambda url, _payload=payload: _payload
                    with patch.dict("os.environ", {"MAPBOX_API_TOKEN": "pk.secret"}), \
                            patch.object(geocode_via_cep.time, "sleep"):
                        self.assertEqual(
                            get_coordinates_from_mapbox("Rua A", "Rio de Janeiro", "RJ", "20000000"),
                            (None, None),
                        )
                finally:
                    geocode_via_cep.fetch_json = original_fetch

    def test_mapbox_caches_repeated_lookups_per_query(self):
        import geocode_via_cep

        geocode_via_cep._mapbox_coordinate_cache.clear()
        calls = []
        original_fetch = geocode_via_cep.fetch_json
        try:
            geocode_via_cep.fetch_json = lambda url: (
                calls.append(url) or {"features": [{"center": [-43.1, -22.9]}]}
            )
            with patch.dict("os.environ", {"MAPBOX_API_TOKEN": "pk.secret"}), \
                    patch.object(geocode_via_cep.time, "sleep"):
                first = get_coordinates_from_mapbox("Avenida Brasil", "Rio de Janeiro", "RJ", "20000000")
                second = get_coordinates_from_mapbox("Avenida Brasil", "Rio de Janeiro", "RJ", "20000000")
                third = get_coordinates_from_mapbox("Avenida Brasil", "Rio de Janeiro", "RJ", "20000000")
        finally:
            geocode_via_cep.fetch_json = original_fetch

        self.assertEqual(first, (-22.9, -43.1))
        self.assertEqual(second, first)
        self.assertEqual(third, first)
        self.assertEqual(len(calls), 1)

    def test_mapbox_never_prints_token(self):
        import contextlib
        import io

        import geocode_via_cep

        geocode_via_cep._mapbox_coordinate_cache.clear()
        original_fetch = geocode_via_cep.fetch_json
        buffer = io.StringIO()
        try:
            geocode_via_cep.fetch_json = lambda url: {"features": [{"center": [-43.1, -22.9]}]}
            with patch.dict("os.environ", {"MAPBOX_API_TOKEN": "pk.secret-token"}), \
                    patch.object(geocode_via_cep.time, "sleep"), \
                    contextlib.redirect_stdout(buffer):
                get_coordinates_from_mapbox("Rua A", "Rio de Janeiro", "RJ", "20000000")
        finally:
            geocode_via_cep.fetch_json = original_fetch

        self.assertNotIn("pk.secret-token", buffer.getvalue())

    def test_generalized_commands_are_available_and_legacy_wrappers_delegate(self):
        importer = (IMPORT_DIR / "brasil_aberto_import.py").read_text(encoding="utf-8")
        geocoder = (IMPORT_DIR / "geocode_via_cep.py").read_text(encoding="utf-8")
        self.assertIn("--uf", importer)
        self.assertIn("--uf", geocoder)
        self.assertIn("def fetch_municipalities", importer)
        self.assertIn("def geocode_uf", geocoder)
        self.assertIn("brasil_aberto_import", (IMPORT_DIR / "brasil_aberto_import_sp.py").read_text(encoding="utf-8"))
        self.assertIn("geocode_via_cep", (IMPORT_DIR / "geocode_via_cep_sp.py").read_text(encoding="utf-8"))

    def test_restore_snapshot_rejects_partial_coordinates_before_database_changes(self):
        class Cursor:
            rowcount = 1

            def __init__(self):
                self.statements = []

            def execute(self, statement, params):
                self.statements.append((statement, params))

            def __enter__(self):
                return self

            def __exit__(self, *args):
                return False

        class Connection:
            def __init__(self):
                self.cursor_instance = Cursor()
                self.commits = 0

            def cursor(self):
                return self.cursor_instance

            def commit(self):
                self.commits += 1

        target = IMPORT_DIR / "coordinate-snapshot.csv"
        rows = [{
            "Uf": "MG", "CityIbgeCode": "3106200", "City": "Belo Horizonte",
            "Neighborhood": "Centro", "NormalizedName": "centro", "Latitude": "",
            "Longitude": "-43.93", "Source": "snapshot", "IsActive": "true",
        }]
        try:
            target.write_text(
                "Uf,CityIbgeCode,City,Neighborhood,NormalizedName,Latitude,Longitude,Source,IsActive\n"
                "MG,3106200,Belo Horizonte,Centro,centro,,-43.93,snapshot,true\n",
                encoding="utf-8",
            )
            connection = Connection()
            with self.assertRaisesRegex(ValueError, "Belo Horizonte"):
                restore_snapshot(connection, target, "MG")
            self.assertEqual(connection.cursor_instance.statements, [])
            self.assertEqual(connection.commits, 0)
        finally:
            target.unlink(missing_ok=True)

    def test_restore_snapshot_preserves_existing_coordinates_and_is_idempotent(self):
        class Cursor:
            rowcount = 1

            def __init__(self):
                self.statements = []

            def execute(self, statement, params):
                self.statements.append((statement, params))

            def __enter__(self):
                return self

            def __exit__(self, *args):
                return False

        class Connection:
            def __init__(self):
                self.cursor_instance = Cursor()
                self.commits = 0

            def cursor(self):
                return self.cursor_instance

            def commit(self):
                self.commits += 1

        target = IMPORT_DIR / "coordinate-snapshot.csv"
        rows = [{
            "Uf": "MG", "CityIbgeCode": "3106200", "City": "Belo Horizonte",
            "Neighborhood": "Centro", "NormalizedName": "centro", "Latitude": "-19.92",
            "Longitude": "-43.93", "Source": "snapshot", "IsActive": "true",
        }]
        try:
            write_snapshot(rows, target)
            connection = Connection()
            self.assertEqual(restore_snapshot(connection, target, "MG"), 1)
            statement, params = connection.cursor_instance.statements[0]
            self.assertIn('COALESCE("DeliveryNeighborhoods"."Latitude", EXCLUDED."Latitude")', statement)
            self.assertIn('COALESCE("DeliveryNeighborhoods"."Longitude", EXCLUDED."Longitude")', statement)
            self.assertEqual(params[-2:], ("MG", "3106200"))
            self.assertEqual(connection.commits, 1)
        finally:
            target.unlink(missing_ok=True)

    def test_restore_snapshot_rolls_back_on_error(self):
        class Cursor:
            rowcount = 1

            def __init__(self):
                self.calls = 0

            def execute(self, statement, params):
                self.calls += 1
                if self.calls > 1:
                    raise RuntimeError("boom")

            def __enter__(self):
                return self

            def __exit__(self, *args):
                return False

        class Connection:
            def __init__(self):
                self.cursor_instance = Cursor()
                self.commits = 0
                self.rollbacks = 0

            def cursor(self):
                return self.cursor_instance

            def commit(self):
                self.commits += 1

            def rollback(self):
                self.rollbacks += 1

        target = IMPORT_DIR / "restore-rollback.csv"
        rows = [
            {
                "Uf": "MG", "CityIbgeCode": "3106200", "City": "Belo Horizonte",
                "Neighborhood": "Centro", "NormalizedName": "centro", "Latitude": "-19.92",
                "Longitude": "-43.93", "Source": "snapshot", "IsActive": "true",
            },
            {
                "Uf": "MG", "CityIbgeCode": "3106200", "City": "Belo Horizonte",
                "Neighborhood": "Savassi", "NormalizedName": "savassi", "Latitude": "",
                "Longitude": "", "Source": "snapshot", "IsActive": "true",
            },
        ]
        try:
            write_snapshot(rows, target)
            connection = Connection()
            with self.assertRaises(RuntimeError):
                restore_snapshot(connection, target, "MG")
            self.assertEqual(connection.rollbacks, 1)
            self.assertEqual(connection.commits, 0)
        finally:
            target.unlink(missing_ok=True)

    def test_restore_snapshot_requires_target_uf_and_rejects_other_uf(self):
        class Cursor:
            rowcount = 1

            def __init__(self):
                self.statements = []

            def execute(self, statement, params):
                self.statements.append((statement, params))

            def __enter__(self):
                return self

            def __exit__(self, *args):
                return False

        class Connection:
            def __init__(self):
                self.cursor_instance = Cursor()
                self.commits = 0
                self.rollbacks = 0

            def cursor(self):
                return self.cursor_instance

            def commit(self):
                self.commits += 1

            def rollback(self):
                self.rollbacks += 1

        target = IMPORT_DIR / "other-uf-snapshot.csv"
        rows = [{
            "Uf": "RJ", "CityIbgeCode": "3304557", "City": "Rio de Janeiro",
            "Neighborhood": "Centro", "NormalizedName": "centro", "Latitude": "-22.9",
            "Longitude": "-43.1", "Source": "snapshot", "IsActive": "true",
        }]
        try:
            write_snapshot(rows, target)
            connection = Connection()
            with self.assertRaisesRegex(ValueError, "UF inesperada"):
                restore_snapshot(connection, target, "MG")
            self.assertEqual(connection.cursor_instance.statements, [])
            self.assertEqual(connection.commits, 0)
            self.assertEqual(connection.rollbacks, 1)
        finally:
            target.unlink(missing_ok=True)

    def test_restore_snapshot_normalizes_uf_and_ibge_in_query(self):
        class Cursor:
            rowcount = 1

            def __init__(self):
                self.statements = []

            def execute(self, statement, params):
                self.statements.append((statement, params))

            def __enter__(self):
                return self

            def __exit__(self, *args):
                return False

        class Connection:
            def __init__(self):
                self.cursor_instance = Cursor()
                self.commits = 0

            def cursor(self):
                return self.cursor_instance

            def commit(self):
                self.commits += 1

        target = IMPORT_DIR / "normalized-restore-snapshot.csv"
        rows = [{
            "Uf": "mg", "CityIbgeCode": "3106200.0", "City": "Belo Horizonte",
            "Neighborhood": "Centro", "NormalizedName": "centro", "Latitude": "-19.92",
            "Longitude": "-43.93", "Source": "snapshot", "IsActive": "true",
        }]
        try:
            write_snapshot(rows, target)
            connection = Connection()
            self.assertEqual(restore_snapshot(connection, target, "MG"), 1)
            _, params = connection.cursor_instance.statements[0]
            self.assertEqual(params[-2:], ("MG", "3106200"))
        finally:
            target.unlink(missing_ok=True)

    def test_validate_snapshot_normalizes_uf_and_ibge_for_canonical_duplicates(self):
        rows = [
            {
                "Uf": "MG", "CityIbgeCode": "3106200", "City": "Belo Horizonte",
                "Neighborhood": "Centro", "NormalizedName": "centro",
                "Latitude": "-19.9", "Longitude": "-43.9", "Source": "snapshot", "IsActive": "true",
            },
            {
                "Uf": "mg", "CityIbgeCode": "3106200.0", "City": "Belo Horizonte",
                "Neighborhood": "CENTRO", "NormalizedName": "centro",
                "Latitude": "", "Longitude": "", "Source": "snapshot", "IsActive": "true",
            },
        ]
        with self.assertRaisesRegex(ValueError, "duplicidades"):
            validate_snapshot_rows(rows)

    def test_export_snapshot_publishes_pending_coordinates_with_empty_fields(self):
        class Cursor:
            def execute(self, statement, params):
                pass

            def fetchall(self):
                return [("RJ", "3304557", "Rio", "Centro", "centro", None, None, "brasil_aberto", True)]

            def __enter__(self):
                return self

            def __exit__(self, *args):
                return False

        class Connection:
            def cursor(self):
                return Cursor()

        target = IMPORT_DIR / "previous-snapshot.csv"
        try:
            self.assertEqual(export_snapshot(Connection(), "RJ", target), 1)
            self.assertIn(",centro,,,brasil_aberto,True", target.read_text(encoding="utf-8"))
        finally:
            target.unlink(missing_ok=True)

    def test_export_snapshot_without_validation_preserves_legacy_normalized_name(self):
        class Cursor:
            def execute(self, statement, params):
                pass

            def fetchall(self):
                return [("RJ", "3304557", "Rio de Janeiro", "São José", "são josé", "-22.9", "-43.1", "brasil_aberto", True)]

            def __enter__(self):
                return self

            def __exit__(self, *args):
                return False

        class Connection:
            def cursor(self):
                return Cursor()

        target = IMPORT_DIR / "legacy-backup.csv"
        try:
            self.assertEqual(export_snapshot(Connection(), "RJ", target, validate=False), 1)
            content = target.read_text(encoding="utf-8")
            self.assertIn("São José", content)
            self.assertIn("são josé", content)
        finally:
            target.unlink(missing_ok=True)

    def test_export_snapshot_default_rejects_inconsistent_normalized_name(self):
        class Cursor:
            def execute(self, statement, params):
                pass

            def fetchall(self):
                return [("RJ", "3304557", "Rio de Janeiro", "São José", "são josé", "-22.9", "-43.1", "brasil_aberto", True)]

            def __enter__(self):
                return self

            def __exit__(self, *args):
                return False

        class Connection:
            def cursor(self):
                return Cursor()

        target = IMPORT_DIR / "legacy-backup.csv"
        try:
            with self.assertRaisesRegex(ValueError, "NormalizedName inconsistente"):
                export_snapshot(Connection(), "RJ", target)
            self.assertFalse(target.exists())
        finally:
            target.unlink(missing_ok=True)

    def test_unvalidated_snapshot_is_not_importable_without_validation(self):
        rows = [{
            "Uf": "RJ", "CityIbgeCode": "3304557", "City": "Rio de Janeiro",
            "Neighborhood": "São José", "NormalizedName": "são josé",
            "Latitude": "-22.9", "Longitude": "-43.1", "Source": "brasil_aberto",
            "IsActive": "true",
        }]
        target = IMPORT_DIR / "legacy-backup.csv"
        try:
            write_snapshot(rows, target, validate=False)
            self.assertTrue(target.exists())
            with self.assertRaisesRegex(ValueError, "NormalizedName inconsistente"):
                read_snapshot(target)
        finally:
            target.unlink(missing_ok=True)

    def test_official_and_manual_classification_rules(self):
        self.assertTrue(is_manual_record(None, None))
        self.assertFalse(is_manual_record(None, "city-1"))
        self.assertFalse(is_manual_record("brasil_aberto", None))
        self.assertTrue(is_official_record(None, "city-1"))
        for source in ("brasil_aberto", "brasil_aberto_cep", "brasil_aberto_first_street", "osm_nominatim", "openstreetmap"):
            self.assertTrue(is_official_record(source, None))
        self.assertFalse(is_official_record(None, None))

    def test_distinct_district_names_deduplicates_and_ignores_short_names(self):
        districts = [{"name": "Centro"}, {"name": "CENTRO"}, {"name": "Ipanema"}, {"name": "X"}]
        self.assertEqual(distinct_district_names(districts), ["Centro", "Ipanema"])

    def test_distinct_district_names_ignores_punctuation_only_names(self):
        districts = [{"name": "Centro"}, {"name": "..."}, {"name": ", -"}, {"name": "Ipanema"}]
        self.assertEqual(distinct_district_names(districts), ["Centro", "Ipanema"])

    def test_replace_removes_obsolete_official_neighborhood(self):
        connection = _ReplacementConnection([("off-1", "city-1", "Centro", "centro", "brasil_aberto")])
        added, removed = replace_city_neighborhoods(
            connection, "city-1", "Rio de Janeiro", [{"name": "Ipanema"}]
        )
        self.assertEqual((added, removed), (1, 1))
        self.assertEqual(connection.commits, 1)
        self.assertEqual(connection.rollbacks, 0)
        statements = connection.cursors[0].statements
        kinds = [statement.strip().upper().split()[0] for statement, _ in statements]
        self.assertIn("INSERT", kinds)
        self.assertIn("DELETE", kinds)
        delete_params = [params for statement, params in statements if statement.strip().upper().startswith("DELETE")][0]
        self.assertEqual(delete_params, ("off-1",))

    def test_replace_updates_current_official_neighborhood_without_removing(self):
        connection = _ReplacementConnection([("off-1", "city-1", "Centro", "centro", "brasil_aberto")])
        added, removed = replace_city_neighborhoods(
            connection, "city-1", "Rio de Janeiro", [{"name": "Centro"}]
        )
        self.assertEqual((added, removed), (1, 0))
        statements = connection.cursors[0].statements
        kinds = [statement.strip().upper().split()[0] for statement, _ in statements]
        self.assertIn("UPDATE", kinds)
        self.assertNotIn("DELETE", kinds)
        self.assertNotIn("INSERT", kinds)

    def test_replace_preserves_manual_neighborhood_and_avoids_duplicate(self):
        connection = _ReplacementConnection([("man-1", None, "Centro", "centro", None)])
        added, removed = replace_city_neighborhoods(
            connection, "city-1", "Rio de Janeiro", [{"name": "Centro"}, {"name": "Ipanema"}]
        )
        self.assertEqual((added, removed), (1, 0))
        statements = connection.cursors[0].statements
        kinds = [statement.strip().upper().split()[0] for statement, _ in statements]
        self.assertIn("INSERT", kinds)
        self.assertNotIn("DELETE", kinds)
        self.assertNotIn("UPDATE", kinds)
        insert_params = [params for statement, params in statements if statement.strip().upper().startswith("INSERT")][0]
        self.assertIn("Ipanema", insert_params)

    def test_replace_empty_response_removes_nothing(self):
        connection = _ReplacementConnection([("off-1", "city-1", "Centro", "centro", "brasil_aberto")])
        added, removed = replace_city_neighborhoods(connection, "city-1", "Rio de Janeiro", [])
        self.assertEqual((added, removed), (0, 0))
        self.assertEqual(connection.cursors, [])
        self.assertEqual(connection.commits, 0)

    def test_replace_rolls_back_on_failure_without_partial_changes(self):
        connection = _ReplacementConnection(
            [("off-1", "city-1", "Centro", "centro", "brasil_aberto")], fail_on="DELETE"
        )
        with self.assertRaises(RuntimeError):
            replace_city_neighborhoods(
                connection, "city-1", "Rio de Janeiro", [{"name": "Ipanema"}]
            )
        self.assertEqual(connection.rollbacks, 1)
        self.assertEqual(connection.commits, 0)

    def test_import_uf_replaces_official_and_exports_all_snapshots_at_end(self):
        import brasil_aberto_import

        class Cursor:
            def __init__(self):
                self.statements = []
                self._rows = []

            def execute(self, statement, params=None):
                self.statements.append((statement, params))
                if '"Cities"' in statement and "SELECT" in statement.upper():
                    self._rows = [("city-id", "Rio de Janeiro", "3304557")]
                elif '"DeliveryNeighborhoods"' in statement and "SELECT" in statement.upper():
                    self._rows = []
                else:
                    self._rows = []

            def fetchall(self):
                return self._rows

            def close(self):
                pass

        class Connection:
            autocommit = False

            def __init__(self):
                self._cursor = Cursor()
                self.commits = 0
                self.rollbacks = 0

            def cursor(self):
                return self._cursor

            def commit(self):
                self.commits += 1

            def rollback(self):
                self.rollbacks += 1

            def close(self):
                pass

        connection = Connection()
        with patch.dict("os.environ", {"BRASIL_ABERTO_API_KEY": "test-only", "URBEAT_ALLOW_LEGACY_DB_IMPORT": "true"}), \
                patch.object(brasil_aberto_import, "fetch_municipalities", return_value=[{"nome": "Rio de Janeiro", "id": 3304557}]), \
                patch.object(brasil_aberto_import, "fetch_json", return_value={"result": [{"name": "Centro"}]}), \
                patch("geocode_via_cep.geocode_uf", return_value=(1, 1, 1)), \
                patch.object(brasil_aberto_import, "export_all_snapshots", return_value={"RJ": 1}) as exporter:
            brasil_aberto_import.import_uf("RJ", connection, confirmation="LEGACY IMPORT RJ")

        exporter.assert_called_once()
        insert_statements = [
            statement for statement, _ in connection._cursor.statements
            if statement.strip().upper().startswith('INSERT INTO "DELIVERYNEIGHBORHOODS"')
        ]
        self.assertEqual(len(insert_statements), 1)

    def test_import_uf_does_not_remove_when_city_query_fails(self):
        import brasil_aberto_import

        class Cursor:
            def __init__(self):
                self.statements = []
                self._rows = []

            def execute(self, statement, params=None):
                self.statements.append((statement, params))
                if '"Cities"' in statement and "SELECT" in statement.upper():
                    self._rows = [("city-id", "Rio de Janeiro", "3304557")]
                else:
                    self._rows = []

            def fetchall(self):
                return self._rows

            def close(self):
                pass

        class Connection:
            autocommit = False

            def __init__(self):
                self._cursor = Cursor()
                self.commits = 0
                self.rollbacks = 0

            def cursor(self):
                return self._cursor

            def commit(self):
                self.commits += 1

            def rollback(self):
                self.rollbacks += 1

            def close(self):
                pass

        connection = Connection()
        with patch.dict("os.environ", {"BRASIL_ABERTO_API_KEY": "test-only", "URBEAT_ALLOW_LEGACY_DB_IMPORT": "true"}), \
                patch.object(brasil_aberto_import, "fetch_municipalities", return_value=[{"nome": "Rio de Janeiro", "id": 3304557}]), \
                patch.object(brasil_aberto_import, "fetch_json", side_effect=RuntimeError("api down")), \
                patch("geocode_via_cep.geocode_uf", return_value=(0, 0, 0)), \
                patch.object(brasil_aberto_import, "export_all_snapshots", return_value={"RJ": 0}) as exporter:
            brasil_aberto_import.import_uf("RJ", connection, confirmation="LEGACY IMPORT RJ")

        exporter.assert_called_once()
        delivery_statements = [
            statement for statement, _ in connection._cursor.statements
            if '"DeliveryNeighborhoods"' in statement
        ]
        self.assertEqual(delivery_statements, [])

    def test_legacy_import_is_blocked_without_explicit_opt_in(self):
        import brasil_aberto_import

        with patch.dict("os.environ", {"BRASIL_ABERTO_API_KEY": "test-only"}, clear=False):
            os.environ.pop("URBEAT_ALLOW_LEGACY_DB_IMPORT", None)
            with self.assertRaisesRegex(RuntimeError, "neighborhood_source_import"):
                brasil_aberto_import.import_uf("RJ")

    def test_import_uf_requires_explicit_confirmation_when_called_directly(self):
        import brasil_aberto_import

        with patch.dict("os.environ", {"BRASIL_ABERTO_API_KEY": "test-only", "URBEAT_ALLOW_LEGACY_DB_IMPORT": "true"}):
            with self.assertRaisesRegex(RuntimeError, "LEGACY IMPORT RJ"):
                brasil_aberto_import.import_uf("RJ")
            with self.assertRaisesRegex(RuntimeError, "LEGACY IMPORT RJ"):
                brasil_aberto_import.import_uf("RJ", confirmation="LEGACY IMPORT SP")
            with self.assertRaisesRegex(RuntimeError, "LEGACY IMPORT RJ"):
                brasil_aberto_import.import_uf("RJ", confirmation="legacy import rj")

    def test_legacy_import_allowed_flag_is_strict_boolean(self):
        import brasil_aberto_import

        with patch.dict("os.environ", {}, clear=False):
            os.environ.pop("URBEAT_ALLOW_LEGACY_DB_IMPORT", None)
            self.assertFalse(brasil_aberto_import.legacy_import_allowed())
        with patch.dict("os.environ", {"URBEAT_ALLOW_LEGACY_DB_IMPORT": "true"}):
            self.assertTrue(brasil_aberto_import.legacy_import_allowed())
        with patch.dict("os.environ", {"URBEAT_ALLOW_LEGACY_DB_IMPORT": "TRUE"}):
            self.assertTrue(brasil_aberto_import.legacy_import_allowed())
        with patch.dict("os.environ", {"URBEAT_ALLOW_LEGACY_DB_IMPORT": "1"}):
            self.assertFalse(brasil_aberto_import.legacy_import_allowed())

    def test_legacy_cli_requires_exact_confirmation(self):
        import brasil_aberto_import

        with patch.dict("os.environ", {"URBEAT_ALLOW_LEGACY_DB_IMPORT": "true", "BRASIL_ABERTO_API_KEY": "test-only"}), \
                patch.object(brasil_aberto_import, "import_uf") as importer:
            with self.assertRaises(SystemExit):
                brasil_aberto_import.main(["--uf", "RJ", "--confirm", "LEGACY IMPORT SP"])
        importer.assert_not_called()

    def test_legacy_cli_delegates_with_exact_confirmation(self):
        import brasil_aberto_import

        with patch.dict("os.environ", {"URBEAT_ALLOW_LEGACY_DB_IMPORT": "true", "BRASIL_ABERTO_API_KEY": "test-only"}), \
                patch.object(brasil_aberto_import, "import_uf") as importer:
            brasil_aberto_import.main(["--uf", "RJ", "--confirm", "LEGACY IMPORT RJ"])
        importer.assert_called_once_with("RJ", confirmation="LEGACY IMPORT RJ")

    def test_legacy_cli_blocks_without_opt_in_even_with_confirmation(self):
        import brasil_aberto_import

        with patch.dict("os.environ", {"BRASIL_ABERTO_API_KEY": "test-only"}, clear=False):
            os.environ.pop("URBEAT_ALLOW_LEGACY_DB_IMPORT", None)
            with self.assertRaisesRegex(RuntimeError, "neighborhood_source_import"):
                brasil_aberto_import.main(["--uf", "RJ", "--confirm", "LEGACY IMPORT RJ"])

    def test_nominatim_is_default_fallback_with_explicit_opt_out(self):
        import geocode_via_cep

        with patch.dict("os.environ", {}, clear=False):
            os.environ.pop("URBEAT_DISABLE_NOMINATIM", None)
            self.assertTrue(geocode_via_cep.nominatim_enabled())
        with patch.dict("os.environ", {"URBEAT_DISABLE_NOMINATIM": "true"}):
            self.assertFalse(geocode_via_cep.nominatim_enabled())
        with patch.dict("os.environ", {"URBEAT_DISABLE_NOMINATIM": "TRUE"}):
            self.assertFalse(geocode_via_cep.nominatim_enabled())
        with patch.dict("os.environ", {"URBEAT_DISABLE_NOMINATIM": "1"}):
            self.assertTrue(geocode_via_cep.nominatim_enabled())

    def test_nominatim_delay_seconds_defaults_and_enforces_minimum(self):
        import geocode_via_cep

        with patch.dict("os.environ", {}, clear=False):
            os.environ.pop("NOMINATIM_DELAY_SECONDS", None)
            self.assertEqual(geocode_via_cep.nominatim_delay_seconds(), 1.0)
        with patch.dict("os.environ", {"NOMINATIM_DELAY_SECONDS": "2.5"}):
            self.assertEqual(geocode_via_cep.nominatim_delay_seconds(), 2.5)
        with patch.dict("os.environ", {"NOMINATIM_DELAY_SECONDS": "0.1"}):
            self.assertEqual(geocode_via_cep.nominatim_delay_seconds(), 1.0)
        with patch.dict("os.environ", {"NOMINATIM_DELAY_SECONDS": "abc"}):
            self.assertEqual(geocode_via_cep.nominatim_delay_seconds(), 1.0)

    def test_geocode_calls_nominatim_by_default_when_first_street_missing(self):
        import geocode_via_cep

        connection = _GeocodeConnection([("id-1", "Centro", "Rio de Janeiro", "RJ", None, None)])
        with patch.dict("os.environ", {"BRASIL_ABERTO_API_KEY": "test-only"}, clear=False), \
                patch.object(geocode_via_cep, "get_first_street_from_dne", return_value=(None, None)), \
                patch.object(geocode_via_cep, "get_coordinates_from_nominatim", return_value=(-22.9, -43.1)) as nominatim, \
                patch.object(geocode_via_cep.time, "sleep"):
            os.environ.pop("URBEAT_DISABLE_NOMINATIM", None)
            total, missing_cep, missing_coordinates = geocode_via_cep.geocode_uf("RJ", connection)
        self.assertEqual(nominatim.call_count, 1)
        statement, params = connection.cursor_instance.updates[0]
        self.assertEqual(params, (-22.9, -43.1, "osm_nominatim", "id-1"))
        self.assertEqual((total, missing_cep, missing_coordinates), (1, 1, 0))

    def test_geocode_skips_nominatim_when_explicitly_disabled(self):
        import geocode_via_cep

        connection = _GeocodeConnection([("id-1", "Centro", "Rio de Janeiro", "RJ", None, None)])
        with patch.dict("os.environ", {"BRASIL_ABERTO_API_KEY": "test-only", "URBEAT_DISABLE_NOMINATIM": "true"}), \
                patch.object(geocode_via_cep, "get_first_street_from_dne", return_value=(None, None)), \
                patch.object(geocode_via_cep, "get_coordinates_from_nominatim") as nominatim:
            total, missing_cep, missing_coordinates = geocode_via_cep.geocode_uf("RJ", connection)
        self.assertFalse(nominatim.called)
        self.assertEqual(connection.cursor_instance.updates, [])
        self.assertEqual((total, missing_cep, missing_coordinates), (1, 1, 1))

    def test_geocode_delays_before_nominatim_fallback(self):
        import geocode_via_cep

        connection = _GeocodeConnection([("id-1", "Centro", "Rio de Janeiro", "RJ", None, None)])
        sleeps = []
        with patch.dict("os.environ", {
                "BRASIL_ABERTO_API_KEY": "test-only",
                "NOMINATIM_DELAY_SECONDS": "2.0",
            }), \
                patch.object(geocode_via_cep, "get_first_street_from_dne", return_value=(None, None)), \
                patch.object(geocode_via_cep, "get_coordinates_from_nominatim", return_value=(-22.9, -43.1)), \
                patch.object(geocode_via_cep.time, "sleep", side_effect=sleeps.append):
            geocode_via_cep.geocode_uf("RJ", connection)
        self.assertIn(2.0, sleeps)

    def test_geocode_does_not_force_autocommit_or_commit_on_shared_connection(self):
        import geocode_via_cep

        connection = _GeocodeConnection([])
        with patch.dict("os.environ", {"BRASIL_ABERTO_API_KEY": "test-only"}):
            geocode_via_cep.geocode_uf("RJ", connection)
        self.assertEqual(connection.autocommit, False)
        self.assertEqual(connection.commits, 0)

    def test_neighborhood_documentation_describes_pending_coordinates(self):
        documentation = (Path(__file__).parents[3] / "Documentacao/Backend/PopulacaoBairrosRJ.md").read_text(
            encoding="utf-8"
        )
        self.assertIn("Latitude` e `Longitude` permanecem vazios", documentation)
        self.assertIn("rua/CEP encontrada", documentation)
        self.assertIn("nunca inventa coordenadas", documentation)
        self.assertIn("URBEAT_DISABLE_NOMINATIM", documentation)
        self.assertNotIn("URBEAT_ENABLE_NOMINATIM", documentation)

    def _make_dne_db(self, rows):
        handle = tempfile.NamedTemporaryFile(suffix=".db", delete=False)
        handle.close()
        db_path = handle.name
        connection = sqlite3.connect(db_path)
        try:
            connection.execute(
                "CREATE TABLE cep_unificado (bairro TEXT, municipio TEXT, logradouro TEXT, cep TEXT)"
            )
            connection.executemany(
                "INSERT INTO cep_unificado (bairro, municipio, logradouro, cep) VALUES (?, ?, ?, ?)",
                rows,
            )
            connection.commit()
        finally:
            connection.close()
        return db_path

    def test_first_dne_street_is_alphabetical(self):
        db_path = self._make_dne_db([
            ("Centro", "Niteroi", "Rua da Conceicao", "24020-000"),
            ("Centro", "Niteroi", "Avenida Brasil", "24000-000"),
            ("Icarai", "Niteroi", "Rua de Outro Bairro", "24200-000"),
        ])
        try:
            with patch.dict("os.environ", {"URBEAT_DNE_DB": db_path}):
                street, cep = get_first_street_from_dne("Centro", "Niteroi")
            self.assertEqual(street, "Avenida Brasil")
            self.assertEqual(cep, "24000000")
        finally:
            Path(db_path).unlink(missing_ok=True)

    def test_first_dne_street_ignores_empty_logradouro_and_cep(self):
        db_path = self._make_dne_db([
            ("Centro", "Niteroi", "", "24010-000"),
            ("Centro", "Niteroi", "Rua sem CEP", None),
            ("Centro", "Niteroi", "   ", "24020-000"),
        ])
        try:
            with patch.dict("os.environ", {"URBEAT_DNE_DB": db_path}):
                self.assertEqual(get_first_street_from_dne("Centro", "Niteroi"), (None, None))
        finally:
            Path(db_path).unlink(missing_ok=True)

    def test_get_cep_from_dne_wrapper_returns_normalized_cep(self):
        db_path = self._make_dne_db([
            ("Centro", "Niteroi", "Avenida Brasil", "24000-000"),
            ("Centro", "Niteroi", "Rua da Conceicao", "24020-000"),
        ])
        try:
            with patch.dict("os.environ", {"URBEAT_DNE_DB": db_path}):
                self.assertEqual(get_cep_from_dne("Centro", "Niteroi"), "24000000")
        finally:
            Path(db_path).unlink(missing_ok=True)

    def test_legacy_first_dne_street_matches_accented_variants_canonically(self):
        db_path = self._make_dne_db([
            ("Sao Jose", "Niteroi", "Rua da Conceicao", "24020-000"),
            ("São José", "Niteroi", "Avenida Brasil", "24000-000"),
        ])
        try:
            with patch.dict("os.environ", {"URBEAT_DNE_DB": db_path}):
                street, cep = get_first_street_from_dne("sao jose", "Niteroi")
            self.assertEqual(street, "Avenida Brasil")
            self.assertEqual(cep, "24000000")
        finally:
            Path(db_path).unlink(missing_ok=True)

    def test_geocode_prefers_first_alphabetical_street_cep(self):
        import geocode_via_cep

        connection = _GeocodeConnection([("id-1", "Centro", "Rio de Janeiro", "RJ", None, None)])
        with patch.dict("os.environ", {"BRASIL_ABERTO_API_KEY": "test-only"}), \
                patch.object(geocode_via_cep, "get_first_street_from_dne", return_value=("Avenida Brasil", "24000000")), \
                patch.object(geocode_via_cep, "get_coordinates_from_cep", return_value=(-22.9, -43.1)), \
                patch.object(geocode_via_cep, "get_coordinates_from_nominatim") as nominatim:
            geocode_via_cep.geocode_uf("RJ", connection)
        self.assertFalse(nominatim.called)
        statement, params = connection.cursor_instance.updates[0]
        self.assertIn('"Source"', statement)
        self.assertEqual(params, (-22.9, -43.1, "brasil_aberto_first_street", "id-1"))

    def test_geocode_uses_nominatim_when_first_street_has_no_coordinates(self):
        import geocode_via_cep

        connection = _GeocodeConnection([("id-1", "Centro", "Rio de Janeiro", "RJ", None, None)])
        with patch.dict("os.environ", {"BRASIL_ABERTO_API_KEY": "test-only"}), \
                patch.object(geocode_via_cep, "get_first_street_from_dne", return_value=("Rua A", "24000000")), \
                patch.object(geocode_via_cep, "get_coordinates_from_cep", return_value=(None, None)), \
                patch.object(geocode_via_cep, "get_coordinates_from_nominatim", return_value=(-22.9, -43.1)) as nominatim, \
                patch.object(geocode_via_cep.time, "sleep"):
            geocode_via_cep.geocode_uf("RJ", connection)
        self.assertEqual(nominatim.call_count, 1)
        statement, params = connection.cursor_instance.updates[0]
        self.assertEqual(params, (-22.9, -43.1, "osm_nominatim", "id-1"))

    def test_geocode_uses_cep_aberto_when_brasil_aberto_returns_no_pair(self):
        import geocode_via_cep

        connection = _GeocodeConnection([("id-1", "Centro", "Rio de Janeiro", "RJ", None, None)])
        with patch.dict("os.environ", {"BRASIL_ABERTO_API_KEY": "test-only", "CEP_ABERTO_API_TOKEN": "token"}), \
                patch.object(geocode_via_cep, "get_first_street_from_dne", return_value=("Rua A", "24000000")), \
                patch.object(geocode_via_cep, "get_coordinates_from_cep", return_value=(None, None)), \
                patch.object(geocode_via_cep, "get_coordinates_from_cep_aberto", return_value=(-22.9, -43.1)) as cep_aberto, \
                patch.object(geocode_via_cep, "get_coordinates_from_mapbox") as mapbox, \
                patch.object(geocode_via_cep, "get_coordinates_from_nominatim") as nominatim:
            geocode_via_cep.geocode_uf("RJ", connection)
        self.assertEqual(cep_aberto.call_count, 1)
        self.assertFalse(mapbox.called)
        self.assertFalse(nominatim.called)
        statement, params = connection.cursor_instance.updates[0]
        self.assertEqual(params, (-22.9, -43.1, "cep_aberto", "id-1"))

    def test_geocode_uses_mapbox_when_both_cep_providers_return_no_pair(self):
        import geocode_via_cep

        connection = _GeocodeConnection([("id-1", "Centro", "Rio de Janeiro", "RJ", None, None)])
        with patch.dict("os.environ", {"BRASIL_ABERTO_API_KEY": "test-only", "CEP_ABERTO_API_TOKEN": "token"}), \
                patch.object(geocode_via_cep, "get_first_street_from_dne", return_value=("Rua A", "24000000")), \
                patch.object(geocode_via_cep, "get_coordinates_from_cep", return_value=(None, None)), \
                patch.object(geocode_via_cep, "get_coordinates_from_cep_aberto", return_value=(None, None)), \
                patch.object(geocode_via_cep, "get_coordinates_from_mapbox", return_value=(-22.9, -43.1)) as mapbox, \
                patch.object(geocode_via_cep, "get_coordinates_from_nominatim") as nominatim:
            geocode_via_cep.geocode_uf("RJ", connection)
        self.assertEqual(mapbox.call_count, 1)
        self.assertFalse(nominatim.called)
        statement, params = connection.cursor_instance.updates[0]
        self.assertEqual(params, (-22.9, -43.1, "mapbox_geocoding", "id-1"))

    def test_geocode_uses_nominatim_when_all_structured_sources_fail(self):
        import geocode_via_cep

        connection = _GeocodeConnection([("id-1", "Centro", "Rio de Janeiro", "RJ", None, None)])
        with patch.dict("os.environ", {"BRASIL_ABERTO_API_KEY": "test-only", "CEP_ABERTO_API_TOKEN": "token"}), \
                patch.object(geocode_via_cep, "get_first_street_from_dne", return_value=("Rua A", "24000000")), \
                patch.object(geocode_via_cep, "get_coordinates_from_cep", return_value=(None, None)), \
                patch.object(geocode_via_cep, "get_coordinates_from_cep_aberto", return_value=(None, None)), \
                patch.object(geocode_via_cep, "get_coordinates_from_mapbox", return_value=(None, None)), \
                patch.object(geocode_via_cep, "get_coordinates_from_nominatim", return_value=(-22.9, -43.1)) as nominatim, \
                patch.object(geocode_via_cep.time, "sleep"):
            geocode_via_cep.geocode_uf("RJ", connection)
        self.assertEqual(nominatim.call_count, 1)
        statement, params = connection.cursor_instance.updates[0]
        self.assertEqual(params, (-22.9, -43.1, "osm_nominatim", "id-1"))

    def test_geocode_replaces_partial_coordinate_pair_with_new_pair(self):
        import geocode_via_cep

        connection = _GeocodeConnection([("id-1", "Centro", "Rio de Janeiro", "RJ", -22.9, None)])
        with patch.dict("os.environ", {"BRASIL_ABERTO_API_KEY": "test-only"}), \
                patch.object(geocode_via_cep, "get_first_street_from_dne", return_value=("Avenida Brasil", "24000000")), \
                patch.object(geocode_via_cep, "get_coordinates_from_cep", return_value=(-23.0, -43.2)):
            geocode_via_cep.geocode_uf("RJ", connection)
        statement, params = connection.cursor_instance.updates[0]
        self.assertNotIn('COALESCE("Latitude"', statement)
        self.assertNotIn('COALESCE("Longitude"', statement)
        self.assertEqual(params, (-23.0, -43.2, "brasil_aberto_first_street", "id-1"))

    def test_geocode_keeps_coordinates_empty_when_all_sources_fail(self):
        import geocode_via_cep

        connection = _GeocodeConnection([("id-1", "Centro", "Rio de Janeiro", "RJ", None, None)])
        with patch.dict("os.environ", {"BRASIL_ABERTO_API_KEY": "test-only"}), \
                patch.object(geocode_via_cep, "get_first_street_from_dne", return_value=(None, None)), \
                patch.object(geocode_via_cep, "get_coordinates_from_cep") as cep_adapter, \
                patch.object(geocode_via_cep, "get_coordinates_from_nominatim", return_value=(None, None)):
            total, missing_cep, missing_coordinates = geocode_via_cep.geocode_uf("RJ", connection)
        self.assertFalse(cep_adapter.called)
        self.assertEqual(connection.cursor_instance.updates, [])
        self.assertEqual((total, missing_cep, missing_coordinates), (1, 1, 1))

    def test_geocode_exports_snapshot_after_commit(self):
        import geocode_via_cep

        events = []

        class TrackedConnection(_GeocodeConnection):
            def commit(self):
                events.append("commit")
                super().commit()

        def record_export(connection, directory=None):
            events.append("export")

        with patch.dict("os.environ", {"BRASIL_ABERTO_API_KEY": "test-only"}), \
                patch("import_common.connect_database", return_value=TrackedConnection([])), \
                patch.object(geocode_via_cep, "export_all_snapshots", side_effect=record_export):
            geocode_via_cep.geocode_uf("RJ")

        self.assertEqual(events, ["commit", "export"])

    def test_geocode_uf_runs_without_brasil_aberto_when_cep_aberto_is_configured(self):
        import geocode_via_cep

        connection = _GeocodeConnection([("id-1", "Centro", "Rio de Janeiro", "RJ", None, None)])
        with patch.dict("os.environ", {"CEP_ABERTO_API_TOKEN": "token", "URBEAT_DISABLE_NOMINATIM": "true"}, clear=True), \
                patch.object(geocode_via_cep, "get_first_street_from_dne", return_value=("Rua A", "24000000")), \
                patch.object(geocode_via_cep, "get_coordinates_from_cep") as brasil_aberto, \
                patch.object(geocode_via_cep, "get_coordinates_from_cep_aberto", return_value=(-22.9, -43.1)) as cep_aberto, \
                patch.object(geocode_via_cep.time, "sleep"):
            total, missing_cep, missing_coordinates = geocode_via_cep.geocode_uf("RJ", connection)

        self.assertTrue(brasil_aberto.called)
        self.assertEqual(cep_aberto.call_count, 1)
        statement, params = connection.cursor_instance.updates[0]
        self.assertEqual(params, (-22.9, -43.1, "cep_aberto", "id-1"))
        self.assertEqual((total, missing_cep, missing_coordinates), (1, 0, 0))

    def test_geocode_uf_runs_without_brasil_aberto_when_mapbox_is_configured(self):
        import geocode_via_cep

        connection = _GeocodeConnection([("id-1", "Centro", "Rio de Janeiro", "RJ", None, None)])
        with patch.dict("os.environ", {"MAPBOX_API_TOKEN": "pk.secret", "URBEAT_DISABLE_NOMINATIM": "true"}, clear=True), \
                patch.object(geocode_via_cep, "get_first_street_from_dne", return_value=("Rua A", "24000000")), \
                patch.object(geocode_via_cep, "get_coordinates_from_cep", return_value=(None, None)), \
                patch.object(geocode_via_cep, "get_coordinates_from_cep_aberto", return_value=(None, None)), \
                patch.object(geocode_via_cep, "get_coordinates_from_mapbox", return_value=(-22.9, -43.1)) as mapbox, \
                patch.object(geocode_via_cep.time, "sleep"):
            total, missing_cep, missing_coordinates = geocode_via_cep.geocode_uf("RJ", connection)

        self.assertEqual(mapbox.call_count, 1)
        statement, params = connection.cursor_instance.updates[0]
        self.assertEqual(params, (-22.9, -43.1, "mapbox_geocoding", "id-1"))
        self.assertEqual((total, missing_cep, missing_coordinates), (1, 0, 0))

    def test_geocode_uf_runs_without_brasil_aberto_when_nominatim_is_enabled(self):
        import geocode_via_cep

        connection = _GeocodeConnection([("id-1", "Centro", "Rio de Janeiro", "RJ", None, None)])
        with patch.dict("os.environ", {}, clear=True), \
                patch.object(geocode_via_cep, "get_first_street_from_dne", return_value=(None, None)), \
                patch.object(geocode_via_cep, "get_coordinates_from_nominatim", return_value=(-22.9, -43.1)) as nominatim, \
                patch.object(geocode_via_cep.time, "sleep"):
            total, missing_cep, missing_coordinates = geocode_via_cep.geocode_uf("RJ", connection)

        self.assertEqual(nominatim.call_count, 1)
        statement, params = connection.cursor_instance.updates[0]
        self.assertEqual(params, (-22.9, -43.1, "osm_nominatim", "id-1"))
        self.assertEqual((total, missing_cep, missing_coordinates), (1, 1, 0))

    def test_geocode_uf_requires_a_provider_when_nominatim_is_disabled(self):
        import geocode_via_cep

        connection = _GeocodeConnection([])
        with patch.dict("os.environ", {"URBEAT_DISABLE_NOMINATIM": "true"}, clear=True):
            with self.assertRaises(RuntimeError):
                geocode_via_cep.geocode_uf("RJ", connection)

    def test_brasil_aberto_geocoder_skips_call_without_api_key(self):
        import geocode_via_cep

        geocode_via_cep._cep_coordinate_cache.clear()
        original = geocode_via_cep.fetch_json
        try:
            geocode_via_cep.fetch_json = lambda url, headers=None: (_ for _ in ()).throw(AssertionError("fetch"))
            with patch.dict("os.environ", {}, clear=True):
                self.assertEqual(get_coordinates_from_cep("24000000"), (None, None))
        finally:
            geocode_via_cep.fetch_json = original
            geocode_via_cep._cep_coordinate_cache.clear()

    def test_write_snapshot_is_atomic_and_leaves_no_temporary_file(self):
        rows = [{
            "Uf": "SP", "CityIbgeCode": "3550308", "City": "Sao Paulo",
            "Neighborhood": "Centro", "NormalizedName": "centro",
            "Latitude": "-23.55", "Longitude": "-46.63", "Source": "brasil_aberto",
            "IsActive": "true",
        }]
        target = IMPORT_DIR / "atomic-snapshot.csv"
        temporary = IMPORT_DIR / "atomic-snapshot.csv.tmp"
        try:
            write_snapshot(rows, target)
            self.assertTrue(target.exists())
            self.assertFalse(temporary.exists())
            self.assertEqual(read_snapshot(target), rows)
        finally:
            target.unlink(missing_ok=True)
            temporary.unlink(missing_ok=True)

    def test_write_snapshot_removes_temporary_file_on_failure(self):
        import neighborhood_snapshot

        rows = [{
            "Uf": "SP", "CityIbgeCode": "3550308", "City": "Sao Paulo",
            "Neighborhood": "Centro", "NormalizedName": "centro",
            "Latitude": "-23.55", "Longitude": "-46.63", "Source": "brasil_aberto",
            "IsActive": "true",
        }]
        target = IMPORT_DIR / "atomic-snapshot.csv"
        temporary = IMPORT_DIR / "atomic-snapshot.csv.tmp"
        try:
            with patch.object(neighborhood_snapshot, "read_snapshot", side_effect=ValueError("boom")):
                with self.assertRaises(ValueError):
                    write_snapshot(rows, target)
            self.assertFalse(target.exists())
            self.assertFalse(temporary.exists())
        finally:
            target.unlink(missing_ok=True)
            temporary.unlink(missing_ok=True)

    def test_write_snapshot_uses_unique_temporary_in_same_directory(self):
        import tempfile as tempfile_module

        rows = [{
            "Uf": "SP", "CityIbgeCode": "3550308", "City": "Sao Paulo",
            "Neighborhood": "Centro", "NormalizedName": "centro",
            "Latitude": "-23.55", "Longitude": "-46.63", "Source": "brasil_aberto",
            "IsActive": "true",
        }]
        captured = {}
        original = tempfile_module.NamedTemporaryFile

        def recording(*args, **kwargs):
            captured["dir"] = kwargs.get("dir")
            captured["prefix"] = kwargs.get("prefix")
            captured["suffix"] = kwargs.get("suffix")
            return original(*args, **kwargs)

        tempfile_module.NamedTemporaryFile = recording
        try:
            with tempfile.TemporaryDirectory() as tmp:
                directory = Path(tmp)
                target = directory / "bairros_mg.csv"
                write_snapshot(rows, target)
                self.assertTrue(target.exists())
                self.assertEqual(captured["dir"], str(directory))
                self.assertEqual(captured["suffix"], ".tmp")
                self.assertTrue(captured["prefix"].startswith("bairros_mg.csv."))
                self.assertEqual(list(directory.glob("*.tmp")), [])
        finally:
            tempfile_module.NamedTemporaryFile = original

    def test_export_all_snapshots_iters_all_ufs_in_defined_order(self):
        import import_common
        import neighborhood_snapshot

        calls = []

        def record_export(connection, uf, path):
            calls.append((uf, path))
            return 0

        with patch.object(neighborhood_snapshot, "export_snapshot", side_effect=record_export):
            totals = neighborhood_snapshot.export_all_snapshots(connection="fake")

        self.assertEqual([uf for uf, _ in calls], list(import_common.IBGE_STATE_CODES))
        self.assertEqual(len(totals), 27)
        for uf, path in calls:
            self.assertEqual(path.name, f"bairros_{uf.lower()}.csv")

    def test_export_all_snapshots_writes_27_files_and_replaces_empty_state(self):
        import import_common
        import neighborhood_snapshot

        class Cursor:
            def execute(self, statement, params):
                pass

            def fetchall(self):
                return []

            def __enter__(self):
                return self

            def __exit__(self, *args):
                return False

        class Connection:
            def cursor(self):
                return Cursor()

        with tempfile.TemporaryDirectory() as tmp:
            directory = Path(tmp)
            stale = directory / "bairros_ac.csv"
            stale.write_text("conteudo antigo\n", encoding="utf-8")
            totals = neighborhood_snapshot.export_all_snapshots(Connection(), directory=directory)
            self.assertEqual(len(totals), 27)
            for uf in import_common.IBGE_STATE_CODES:
                self.assertTrue((directory / f"bairros_{uf.lower()}.csv").exists())
            self.assertEqual(read_snapshot(stale), [])
            self.assertIn("Uf,CityIbgeCode", stale.read_text(encoding="utf-8"))
            self.assertNotIn("conteudo antigo", stale.read_text(encoding="utf-8"))

    def test_export_all_cli_exports_all_ufs_without_uf_or_file(self):
        import neighborhood_snapshot

        class FakeConnection:
            def close(self):
                pass

        fake_connection = FakeConnection()
        with patch.object(neighborhood_snapshot, "connect_from_environment", return_value=fake_connection), \
                patch.object(neighborhood_snapshot, "export_all_snapshots", return_value={"RJ": 3}) as exporter:
            neighborhood_snapshot.main(["export-all"])
        exporter.assert_called_once_with(fake_connection)


class NeighborhoodCandidateTests(unittest.TestCase):
    def test_shared_canonical_normalization_strips_accents_and_lowercases(self):
        self.assertEqual(import_common.normalize_neighborhood_name("  São José   "), "sao jose")
        self.assertEqual(import_common.normalize_neighborhood_name("VILA NOVA DE CAMPINAS"), "vila nova de campinas")

    def test_shared_canonical_normalization_replaces_punctuation_and_collapses_whitespace(self):
        self.assertEqual(import_common.normalize_neighborhood_name("São-José"), "sao jose")
        self.assertEqual(import_common.normalize_neighborhood_name("Vila   Nova  de Campinas"), "vila nova de campinas")
        self.assertEqual(import_common.normalize_neighborhood_name("Centro, 123"), "centro 123")
        self.assertEqual(import_common.normalize_neighborhood_name("Bairro! Novo?"), "bairro novo")

    def test_shared_canonical_normalization_rejects_only_empty_names(self):
        for value in (None, "", "   ", "---", " ! ", "."):
            with self.subTest(value=value):
                with self.assertRaises(ValueError):
                    import_common.normalize_neighborhood_name(value)

    def test_normalization_is_shared_by_importer(self):
        import brasil_aberto_import

        self.assertEqual(
            brasil_aberto_import.normalize_neighborhood_name("São José"),
            import_common.normalize_neighborhood_name("São José"),
        )
        self.assertIs(
            brasil_aberto_import.normalize_neighborhood_name,
            import_common.normalize_neighborhood_name,
        )

    def test_candidate_key_normalizes_uf_ibge_and_name(self):
        self.assertEqual(candidate_key("mg", "3106200", "São José"), ("MG", "3106200", "sao jose"))

    def test_candidate_key_rejects_invalid_uf(self):
        with self.assertRaises(ValueError):
            candidate_key("XX", "3106200", "Centro")

    def test_candidate_key_rejects_empty_ibge(self):
        with self.assertRaises(ValueError):
            candidate_key("MG", "   ", "Centro")

    def test_source_priority_prefers_dne_over_brasil_aberto(self):
        self.assertGreater(source_priority("dne"), source_priority("brasil_aberto"))
        self.assertEqual(source_priority(""), 0)
        self.assertEqual(source_priority(None), 0)
        self.assertEqual(source_priority("  DNE "), source_priority("dne"))

    def test_candidate_path_uses_lowercase_uf_in_candidates_dir(self):
        path = candidate_path("MG")
        self.assertEqual(path.name, "bairros_mg.csv")
        self.assertEqual(path.parent.name, "candidates")

    def test_candidate_path_accepts_custom_directory(self):
        self.assertEqual(
            candidate_path("RJ", Path("/tmp/candidatos")),
            Path("/tmp/candidatos") / "bairros_rj.csv",
        )

    def test_build_candidates_deduplicates_by_uf_ibge_and_normalized_name(self):
        records = [
            {"Uf": "MG", "CityIbgeCode": "3106200", "City": "Belo Horizonte", "Neighborhood": "São José"},
            {"Uf": "mg", "CityIbgeCode": "3106200", "City": "Belo Horizonte", "Neighborhood": "SAO JOSE"},
            {"Uf": "MG", "CityIbgeCode": "3106200", "City": "Belo Horizonte", "Neighborhood": "Centro"},
            {"Uf": "MG", "CityIbgeCode": "9999999", "City": "Outra", "Neighborhood": "São José"},
            {"Uf": "RJ", "CityIbgeCode": "3304557", "City": "Rio de Janeiro", "Neighborhood": "Centro"},
        ]
        candidates = build_candidates(records)
        self.assertEqual(len(candidates), 4)
        keys = [(c["Uf"], c["CityIbgeCode"], c["NormalizedName"]) for c in candidates]
        self.assertEqual(
            keys,
            [
                ("MG", "3106200", "centro"),
                ("MG", "3106200", "sao jose"),
                ("MG", "9999999", "sao jose"),
                ("RJ", "3304557", "centro"),
            ],
        )

    def test_build_candidates_prefers_record_with_coordinates(self):
        records = [
            {
                "Uf": "RJ", "CityIbgeCode": "3304557", "City": "Rio de Janeiro",
                "Neighborhood": "Centro", "Latitude": "", "Longitude": "",
                "Source": "brasil_aberto", "IsActive": "true",
            },
            {
                "Uf": "RJ", "CityIbgeCode": "3304557", "City": "Rio de Janeiro",
                "Neighborhood": "CENTRO", "Latitude": "-22.9068", "Longitude": "-43.1729",
                "Source": "osm_nominatim", "IsActive": "true",
            },
        ]
        candidates = build_candidates(records)
        self.assertEqual(len(candidates), 1)
        self.assertEqual(candidates[0]["Latitude"], -22.9068)
        self.assertEqual(candidates[0]["Longitude"], -43.1729)
        self.assertEqual(candidates[0]["Source"], "osm_nominatim")

    def test_build_candidates_prefers_dne_name_and_source_without_coordinate_conflict(self):
        records = [
            {
                "Uf": "RJ", "CityIbgeCode": "3304557", "City": "Rio de Janeiro",
                "Neighborhood": "São-José", "Latitude": "", "Longitude": "",
                "Source": "brasil_aberto", "IsActive": "true",
            },
            {
                "Uf": "RJ", "CityIbgeCode": "3304557", "City": "Rio de Janeiro",
                "Neighborhood": "São José", "Latitude": "", "Longitude": "",
                "Source": "dne", "IsActive": "true",
            },
        ]
        candidates = build_candidates(records)
        self.assertEqual(len(candidates), 1)
        self.assertEqual(candidates[0]["Neighborhood"], "São José")
        self.assertEqual(candidates[0]["Source"], "dne")

    def test_build_candidates_preserves_valid_coordinates_from_lower_priority_source(self):
        records = [
            {
                "Uf": "RJ", "CityIbgeCode": "3304557", "City": "Rio de Janeiro",
                "Neighborhood": "Centro", "Latitude": "", "Longitude": "",
                "Source": "dne", "IsActive": "true",
            },
            {
                "Uf": "RJ", "CityIbgeCode": "3304557", "City": "Rio de Janeiro",
                "Neighborhood": "CENTRO", "Latitude": "-22.9068", "Longitude": "-43.1729",
                "Source": "brasil_aberto", "IsActive": "true",
            },
        ]
        candidates = build_candidates(records)
        self.assertEqual(len(candidates), 1)
        self.assertEqual(candidates[0]["Source"], "brasil_aberto")
        self.assertEqual(candidates[0]["Latitude"], -22.9068)
        self.assertEqual(candidates[0]["Longitude"], -43.1729)

    def test_build_candidates_prefers_higher_priority_coordinates_when_both_have_coordinates(self):
        records = [
            {
                "Uf": "RJ", "CityIbgeCode": "3304557", "City": "Rio de Janeiro",
                "Neighborhood": "Centro", "Latitude": "-22.9068", "Longitude": "-43.1729",
                "Source": "brasil_aberto", "IsActive": "true",
            },
            {
                "Uf": "RJ", "CityIbgeCode": "3304557", "City": "Rio de Janeiro",
                "Neighborhood": "CENTRO", "Latitude": "-22.9000", "Longitude": "-43.1000",
                "Source": "dne", "IsActive": "true",
            },
        ]
        candidates = build_candidates(records)
        self.assertEqual(len(candidates), 1)
        self.assertEqual(candidates[0]["Source"], "dne")
        self.assertEqual(candidates[0]["Latitude"], -22.9)
        self.assertEqual(candidates[0]["Longitude"], -43.1)

    def test_build_candidates_source_reflects_coordinate_origin_over_name_source(self):
        records = [
            {
                "Uf": "RJ", "CityIbgeCode": "3304557", "City": "Rio de Janeiro",
                "Neighborhood": "Centro", "Latitude": "", "Longitude": "",
                "Source": "dne", "IsActive": "true",
            },
            {
                "Uf": "RJ", "CityIbgeCode": "3304557", "City": "Rio de Janeiro",
                "Neighborhood": "CENTRO", "Latitude": "-22.9068", "Longitude": "-43.1729",
                "Source": "osm_nominatim", "IsActive": "true",
            },
        ]
        candidates = build_candidates(records)
        self.assertEqual(len(candidates), 1)
        self.assertEqual(candidates[0]["Neighborhood"], "Centro")
        self.assertEqual(candidates[0]["Source"], "osm_nominatim")
        self.assertEqual(candidates[0]["Latitude"], -22.9068)

    def test_build_candidates_keeps_coordinate_source_when_name_priority_changes(self):
        records = [
            {
                "Uf": "RJ", "CityIbgeCode": "3304557", "City": "Rio de Janeiro",
                "Neighborhood": "Centro", "Latitude": "-22.9068", "Longitude": "-43.1729",
                "Source": "brasil_aberto", "IsActive": "true",
            },
            {
                "Uf": "RJ", "CityIbgeCode": "3304557", "City": "Rio de Janeiro",
                "Neighborhood": "CENTRO", "Latitude": "", "Longitude": "",
                "Source": "dne", "IsActive": "true",
            },
        ]
        candidates = build_candidates(records)
        self.assertEqual(len(candidates), 1)
        self.assertEqual(candidates[0]["Source"], "brasil_aberto")
        self.assertEqual(candidates[0]["Neighborhood"], "CENTRO")
        self.assertEqual(candidates[0]["Latitude"], -22.9068)
        self.assertEqual(candidates[0]["Longitude"], -43.1729)

    def test_build_candidates_rejects_partial_coordinates(self):
        records = [{
            "Uf": "RJ", "CityIbgeCode": "3304557", "City": "Rio de Janeiro",
            "Neighborhood": "Centro", "Latitude": "-22.9", "Longitude": "",
        }]
        with self.assertRaisesRegex(ValueError, "ambas"):
            build_candidates(records)

    def test_build_candidates_rejects_invalid_uf(self):
        with self.assertRaises(ValueError):
            build_candidates([{
                "Uf": "XX", "CityIbgeCode": "3106200", "City": "X", "Neighborhood": "Centro",
            }])

    def test_build_candidates_rejects_missing_essential_field(self):
        with self.assertRaisesRegex(ValueError, "Neighborhood"):
            build_candidates([{"Uf": "MG", "CityIbgeCode": "3106200", "City": "Belo Horizonte"}])

    def test_build_candidates_sorts_by_uf_ibge_and_normalized_name(self):
        records = [
            {"Uf": "RJ", "CityIbgeCode": "3304557", "City": "Rio de Janeiro", "Neighborhood": "Zumbi"},
            {"Uf": "AC", "CityIbgeCode": "1200401", "City": "Rio Branco", "Neighborhood": "Centro"},
            {"Uf": "RJ", "CityIbgeCode": "3304557", "City": "Rio de Janeiro", "Neighborhood": "Botafogo"},
        ]
        candidates = build_candidates(records)
        keys = [(c["Uf"], c["CityIbgeCode"], c["NormalizedName"]) for c in candidates]
        self.assertEqual(
            keys,
            [
                ("AC", "1200401", "centro"),
                ("RJ", "3304557", "botafogo"),
                ("RJ", "3304557", "zumbi"),
            ],
        )

    def test_export_candidates_writes_one_csv_per_uf(self):
        records = [
            {
                "Uf": "MG", "CityIbgeCode": "3106200", "City": "Belo Horizonte",
                "Neighborhood": "Centro", "Latitude": "-19.9167", "Longitude": "-43.9345",
                "Source": "brasil_aberto", "IsActive": "true",
            },
            {
                "Uf": "RJ", "CityIbgeCode": "3304557", "City": "Rio de Janeiro",
                "Neighborhood": "Ipanema", "Latitude": "", "Longitude": "",
                "Source": "brasil_aberto", "IsActive": "true",
            },
            {
                "Uf": "RJ", "CityIbgeCode": "3304557", "City": "Rio de Janeiro",
                "Neighborhood": "Ipanema",
            },
        ]
        candidates = build_candidates(records)
        with tempfile.TemporaryDirectory() as tmp:
            directory = Path(tmp)
            totals = export_candidates_by_uf(candidates, directory)
            self.assertEqual(sorted(totals), ["MG", "RJ"])
            self.assertEqual(totals["MG"], 1)
            self.assertEqual(totals["RJ"], 1)
            mg_file = directory / "bairros_mg.csv"
            rj_file = directory / "bairros_rj.csv"
            self.assertTrue(mg_file.exists())
            self.assertTrue(rj_file.exists())
            self.assertEqual([row["Uf"] for row in read_snapshot(mg_file)], ["MG"])
            self.assertEqual([row["Uf"] for row in read_snapshot(rj_file)], ["RJ"])

    def test_write_candidates_returns_count_and_writes_valid_csv(self):
        candidates = [{
            "Uf": "SP", "CityIbgeCode": "3550308", "City": "Sao Paulo",
            "Neighborhood": "Centro", "NormalizedName": "centro",
            "Latitude": "-23.55", "Longitude": "-46.63",
            "Source": "brasil_aberto", "IsActive": "true",
        }]
        target = IMPORT_DIR / "test-candidates.csv"
        try:
            self.assertEqual(write_candidates(candidates, target), 1)
            self.assertEqual(read_snapshot(target), candidates)
        finally:
            target.unlink(missing_ok=True)

    def test_read_records_rejects_missing_required_columns(self):
        target = IMPORT_DIR / "invalid-records.csv"
        try:
            target.write_text("Uf,City\nSP,Sao Paulo\n", encoding="utf-8")
            with self.assertRaises(ValueError):
                read_records(target)
        finally:
            target.unlink(missing_ok=True)

    def test_generate_cli_reads_records_and_writes_csv_per_uf(self):
        import neighborhood_candidates

        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            raw = root / "raw"
            raw.mkdir()
            (raw / "bairros_brutos.csv").write_text(
                "Uf,CityIbgeCode,City,Neighborhood,Latitude,Longitude,Source,IsActive\n"
                "MG,3106200,Belo Horizonte,Centro,-19.9,-43.9,brasil_aberto,true\n"
                "mg,3106200,Belo Horizonte,centro,,,brasil_aberto,true\n"
                "RJ,3304557,Rio de Janeiro,Ipanema,,,brasil_aberto,true\n",
                encoding="utf-8",
            )
            out = root / "out"
            neighborhood_candidates.main(["generate", "--input", str(raw), "--output", str(out)])
            self.assertTrue((out / "bairros_mg.csv").exists())
            self.assertTrue((out / "bairros_rj.csv").exists())
            self.assertEqual(len(read_snapshot(out / "bairros_mg.csv")), 1)
            self.assertEqual(len(read_snapshot(out / "bairros_rj.csv")), 1)


class NeighborhoodSourceImportTests(unittest.TestCase):
    def _make_dne_db(self, columns, rows):
        handle = tempfile.NamedTemporaryFile(suffix=".db", delete=False)
        handle.close()
        db_path = handle.name
        connection = sqlite3.connect(db_path)
        try:
            connection.execute(
                f"CREATE TABLE cep_unificado ({', '.join(c + ' TEXT' for c in columns)})"
            )
            connection.executemany(
                f"INSERT INTO cep_unificado ({', '.join(columns)}) VALUES ({', '.join('?' for _ in columns)})",
                rows,
            )
            connection.commit()
        finally:
            connection.close()
        return db_path

    def test_source_import_requires_runtime_api_key(self):
        source = (IMPORT_DIR / "neighborhood_source_import.py").read_text(encoding="utf-8")
        self.assertIn('os.environ.get("BRASIL_ABERTO_API_KEY")', source)
        self.assertNotIn('os.environ.get("BRASIL_ABERTO_API_KEY",', source)
        self.assertIn('os.environ.get("URBEAT_DNE_DB"', source)

    def test_municipality_lookup_indexes_by_name_and_code(self):
        by_name, by_code = municipality_lookup([
            {"nome": "Rio de Janeiro", "id": 3304557},
            {"nome": "Niterói", "id": 3303302},
        ])
        self.assertEqual(by_name["rio de janeiro"]["CityIbgeCode"], "3304557")
        self.assertEqual(by_name["niteroi"]["City"], "Niterói")
        self.assertEqual(by_code["3303302"]["City"], "Niterói")

    def test_municipality_lookup_removes_ambiguous_names_but_keeps_codes(self):
        by_name, by_code = municipality_lookup([
            {"nome": "Bom Jesus", "id": 3301000},
            {"nome": "Bom Jesus", "id": 3502000},
            {"nome": "Rio de Janeiro", "id": 3304557},
        ])
        self.assertNotIn("bom jesus", by_name)
        self.assertIn("rio de janeiro", by_name)
        self.assertEqual(by_code["3301000"]["City"], "Bom Jesus")
        self.assertEqual(by_code["3502000"]["City"], "Bom Jesus")

    def test_read_dne_rejects_unknown_or_other_uf_ibge_code(self):
        columns = ["uf", "municipio_cod_ibge", "municipio", "bairro", "cep", "logradouro"]
        rows = [
            ("RJ", "3304557", "Rio de Janeiro", "Centro", "20010-000", "Rua A"),
            ("RJ", "9999999", "Cidade Fantasma", "Lugar Nenhum", "00000-000", "Rua B"),
            ("RJ", "3550308", "São Paulo", "Centro SP", "01000-000", "Rua C"),
        ]
        db_path = self._make_dne_db(columns, rows)
        try:
            connection = sqlite3.connect(db_path)
            try:
                lookup, by_code = municipality_lookup([
                    {"nome": "Rio de Janeiro", "id": 3304557},
                ])
                records = read_dne_neighborhoods(connection, "RJ", lookup, by_code)
            finally:
                connection.close()
        finally:
            Path(db_path).unlink(missing_ok=True)

        self.assertEqual(len(records), 1)
        self.assertEqual(records[0]["Neighborhood"], "Centro")
        self.assertEqual(records[0]["CityIbgeCode"], "3304557")

    def test_read_dne_normalizes_float_ibge_code(self):
        handle = tempfile.NamedTemporaryFile(suffix=".db", delete=False)
        handle.close()
        db_path = handle.name
        connection = sqlite3.connect(db_path)
        try:
            connection.execute(
                "CREATE TABLE cep_unificado (uf TEXT, municipio_cod_ibge REAL, "
                "municipio TEXT, bairro TEXT, cep TEXT, logradouro TEXT)"
            )
            connection.execute(
                "INSERT INTO cep_unificado (uf, municipio_cod_ibge, municipio, bairro, cep, logradouro) "
                "VALUES (?, ?, ?, ?, ?, ?)",
                ("RJ", 3304557.0, "Rio de Janeiro", "Centro", "20010-000", "Rua A"),
            )
            connection.commit()
        finally:
            connection.close()
        try:
            connection = sqlite3.connect(db_path)
            try:
                lookup, by_code = municipality_lookup([{"nome": "Rio de Janeiro", "id": 3304557}])
                records = read_dne_neighborhoods(connection, "RJ", lookup, by_code)
            finally:
                connection.close()
        finally:
            Path(db_path).unlink(missing_ok=True)

        self.assertEqual(len(records), 1)
        self.assertEqual(records[0]["CityIbgeCode"], "3304557")

    def test_read_dne_full_schema_resolves_ibge_and_filters_uf(self):
        columns = ["uf", "municipio_cod_ibge", "municipio", "bairro", "cep", "logradouro"]
        rows = [
            ("RJ", "3304557", "Rio de Janeiro", "São José", "20000-000", "Rua A"),
            ("RJ", "3304557", "Rio de Janeiro", "SAO JOSE", "20000-001", "Rua B"),
            ("RJ", "3304557", "Rio de Janeiro", "Centro", "20010-000", "Rua C"),
            ("SP", "3550308", "São Paulo", "Centro", "01000-000", "Rua D"),
        ]
        db_path = self._make_dne_db(columns, rows)
        try:
            connection = sqlite3.connect(db_path)
            try:
                lookup, by_code = municipality_lookup([{"nome": "Rio de Janeiro", "id": 3304557}])
                records = read_dne_neighborhoods(connection, "RJ", lookup, by_code)
            finally:
                connection.close()
        finally:
            Path(db_path).unlink(missing_ok=True)

        self.assertEqual(len(records), 3)
        self.assertTrue(all(record["Uf"] == "RJ" for record in records))
        self.assertTrue(all(record["CityIbgeCode"] == "3304557" for record in records))
        self.assertTrue(all(record["Source"] == "dne" for record in records))
        self.assertEqual(
            sorted(record["Neighborhood"] for record in records),
            ["Centro", "SAO JOSE", "São José"],
        )

    def test_read_dne_minimal_schema_uses_lookup_by_name(self):
        columns = ["bairro", "municipio", "logradouro", "cep"]
        rows = [
            ("Centro", "Rio de Janeiro", "Rua A", "20010-000"),
            ("Ipanema", "Rio de Janeiro", "Rua B", "22000-000"),
            ("Centro", "São Paulo", "Rua C", "01000-000"),
            ("Centro", "Cidade Inexistente", "Rua D", "00000-000"),
        ]
        db_path = self._make_dne_db(columns, rows)
        try:
            connection = sqlite3.connect(db_path)
            try:
                lookup, _ = municipality_lookup([{"nome": "Rio de Janeiro", "id": 3304557}])
                records = read_dne_neighborhoods(connection, "RJ", lookup)
            finally:
                connection.close()
        finally:
            Path(db_path).unlink(missing_ok=True)

        self.assertEqual(len(records), 2)
        self.assertTrue(all(record["CityIbgeCode"] == "3304557" for record in records))
        self.assertEqual(
            sorted(record["Neighborhood"] for record in records), ["Centro", "Ipanema"]
        )

    def test_get_first_street_by_ibge_matches_normalized_name_variants(self):
        columns = ["uf", "municipio_cod_ibge", "municipio", "bairro", "cep", "logradouro"]
        rows = [
            ("RJ", "3304557", "Rio de Janeiro", "São José", "20000-010", "Rua da Conceicao"),
            ("RJ", "3304557", "Rio de Janeiro", "SAO JOSE", "20000-000", "Avenida Brasil"),
            ("RJ", "3304557", "Rio de Janeiro", "Centro", "20010-000", "Rua do Ouvidor"),
        ]
        db_path = self._make_dne_db(columns, rows)
        try:
            connection = sqlite3.connect(db_path)
            try:
                street, cep = get_first_street_from_dne_by_ibge(
                    connection, "3304557", "sao jose", "Rio de Janeiro"
                )
            finally:
                connection.close()
        finally:
            Path(db_path).unlink(missing_ok=True)
        self.assertEqual(street, "Avenida Brasil")
        self.assertEqual(cep, "20000000")

    def test_get_first_street_by_ibge_minimal_schema_uses_city_name(self):
        columns = ["bairro", "municipio", "logradouro", "cep"]
        rows = [
            ("Centro", "Rio de Janeiro", "Rua do Ouvidor", "20010-000"),
            ("Centro", "Rio de Janeiro", "Avenida Rio Branco", "20040-000"),
        ]
        db_path = self._make_dne_db(columns, rows)
        try:
            connection = sqlite3.connect(db_path)
            try:
                street, cep = get_first_street_from_dne_by_ibge(
                    connection, "3304557", "centro", "Rio de Janeiro"
                )
            finally:
                connection.close()
        finally:
            Path(db_path).unlink(missing_ok=True)
        self.assertEqual(street, "Avenida Rio Branco")
        self.assertEqual(cep, "20040000")

    def test_get_first_street_by_ibge_returns_none_when_no_match(self):
        columns = ["bairro", "municipio", "logradouro", "cep"]
        rows = [("Ipanema", "Rio de Janeiro", "Rua B", "22000-000")]
        db_path = self._make_dne_db(columns, rows)
        try:
            connection = sqlite3.connect(db_path)
            try:
                self.assertEqual(
                    get_first_street_from_dne_by_ibge(
                        connection, "3304557", "centro", "Rio de Janeiro"
                    ),
                    (None, None),
                )
            finally:
                connection.close()
        finally:
            Path(db_path).unlink(missing_ok=True)

    def test_get_first_street_by_ibge_does_not_fallback_by_name_for_homonymous_city(self):
        columns = ["uf", "municipio_cod_ibge", "municipio", "bairro", "cep", "logradouro"]
        rows = [
            ("RJ", "3502000", "Bom Jesus", "Centro", "20000-000", "Rua da Serra"),
        ]
        db_path = self._make_dne_db(columns, rows)
        try:
            connection = sqlite3.connect(db_path)
            try:
                self.assertEqual(
                    get_first_street_from_dne_by_ibge(
                        connection, "3301000", "centro", "Bom Jesus"
                    ),
                    (None, None),
                )
            finally:
                connection.close()
        finally:
            Path(db_path).unlink(missing_ok=True)

    def test_geocode_candidates_preserves_existing_coordinates(self):
        import neighborhood_source_import

        candidates = [{
            "Uf": "RJ", "CityIbgeCode": "3304557", "City": "Rio de Janeiro",
            "Neighborhood": "Centro", "NormalizedName": "centro",
            "Latitude": -22.9, "Longitude": -43.1, "Source": "brasil_aberto",
            "IsActive": True,
        }]
        with patch.object(neighborhood_source_import, "get_first_street_from_dne_by_ibge") as dne, \
                patch.object(neighborhood_source_import, "get_coordinates_from_cep") as cep:
            geocoded, missing_cep, pending = geocode_candidates(candidates, connection="fake")
        self.assertFalse(dne.called)
        self.assertFalse(cep.called)
        self.assertEqual((geocoded, missing_cep, pending), (0, 0, 0))

    def test_geocode_candidates_handles_api_errors_and_leaves_pending(self):
        import neighborhood_source_import

        candidates = [{
            "Uf": "RJ", "CityIbgeCode": "3304557", "City": "Rio de Janeiro",
            "Neighborhood": "Centro", "NormalizedName": "centro",
            "Latitude": None, "Longitude": None, "Source": "dne", "IsActive": True,
        }]
        with patch.object(
            neighborhood_source_import,
            "get_first_street_from_dne_by_ibge",
            return_value=("Rua A", "20000000"),
        ), patch.object(
            neighborhood_source_import,
            "get_coordinates_from_cep",
            side_effect=RuntimeError("api down"),
        ):
            geocoded, missing_cep, pending = geocode_candidates(candidates, connection="fake")
        self.assertEqual((geocoded, missing_cep, pending), (0, 0, 1))
        self.assertIsNone(candidates[0]["Latitude"])
        self.assertIsNone(candidates[0]["Longitude"])

    def test_geocode_candidates_isolates_dne_failure_per_candidate(self):
        import neighborhood_source_import

        candidates = [
            {
                "Uf": "RJ", "CityIbgeCode": "3304557", "City": "Rio de Janeiro",
                "Neighborhood": "Centro", "NormalizedName": "centro",
                "Latitude": None, "Longitude": None, "Source": "dne", "IsActive": True,
            },
            {
                "Uf": "RJ", "CityIbgeCode": "3304557", "City": "Rio de Janeiro",
                "Neighborhood": "Ipanema", "NormalizedName": "ipanema",
                "Latitude": None, "Longitude": None, "Source": "dne", "IsActive": True,
            },
        ]

        def dne_side_effect(connection, ibge_code, normalized_name, city_name=None):
            if normalized_name == "centro":
                raise RuntimeError("dne boom")
            return ("Rua B", "22000000")

        with patch.object(
            neighborhood_source_import,
            "get_first_street_from_dne_by_ibge",
            side_effect=dne_side_effect,
        ), patch.object(
            neighborhood_source_import,
            "get_coordinates_from_cep",
            return_value=(-22.9, -43.1),
        ):
            geocoded, missing_cep, pending = geocode_candidates(candidates, connection="fake")

        self.assertEqual((geocoded, missing_cep, pending), (1, 1, 1))
        self.assertIsNone(candidates[0]["Latitude"])
        self.assertIsNone(candidates[0]["Longitude"])
        self.assertEqual(candidates[1]["Latitude"], -22.9)
        self.assertEqual(candidates[1]["Longitude"], -43.1)

    def test_build_street_index_groups_by_city_and_normalized_name(self):
        columns = ["uf", "municipio_cod_ibge", "municipio", "bairro", "cep", "logradouro"]
        rows = [
            ("RJ", "3304557", "Rio de Janeiro", "São José", "20000-010", "Rua da Conceicao"),
            ("RJ", "3304557", "Rio de Janeiro", "SAO JOSE", "20000-000", "Avenida Brasil"),
            ("RJ", "3304557", "Rio de Janeiro", "Centro", "20010-000", "Rua do Ouvidor"),
            ("SP", "3550308", "São Paulo", "Centro", "01000-000", "Rua Augusta"),
        ]
        db_path = self._make_dne_db(columns, rows)
        try:
            connection = sqlite3.connect(db_path)
            try:
                lookup, by_code = municipality_lookup([
                    {"nome": "Rio de Janeiro", "id": 3304557},
                    {"nome": "São Paulo", "id": 3550308},
                ])
                index = build_dne_street_index(connection, "RJ", lookup, by_code)
            finally:
                connection.close()
        finally:
            Path(db_path).unlink(missing_ok=True)

        self.assertEqual(index[("3304557", "sao jose")], ("Avenida Brasil", "20000000"))
        self.assertEqual(index[("3304557", "centro")], ("Rua do Ouvidor", "20010000"))
        self.assertNotIn(("3550308", "centro"), index)

    def test_build_street_index_minimal_schema_resolves_by_name(self):
        columns = ["bairro", "municipio", "logradouro", "cep"]
        rows = [
            ("Centro", "Rio de Janeiro", "Rua do Ouvidor", "20010-000"),
            ("Centro", "Rio de Janeiro", "Avenida Rio Branco", "20040-000"),
            ("Centro", "Cidade Inexistente", "Rua D", "00000-000"),
        ]
        db_path = self._make_dne_db(columns, rows)
        try:
            connection = sqlite3.connect(db_path)
            try:
                lookup, _ = municipality_lookup([{"nome": "Rio de Janeiro", "id": 3304557}])
                index = build_dne_street_index(connection, "RJ", lookup)
            finally:
                connection.close()
        finally:
            Path(db_path).unlink(missing_ok=True)

        self.assertEqual(index[("3304557", "centro")], ("Avenida Rio Branco", "20040000"))

    def test_geocode_candidates_reuses_street_index_without_querying_dne(self):
        import neighborhood_source_import

        candidates = [{
            "Uf": "RJ", "CityIbgeCode": "3304557", "City": "Rio de Janeiro",
            "Neighborhood": "São José", "NormalizedName": "sao jose",
            "Latitude": None, "Longitude": None, "Source": "dne", "IsActive": True,
        }]
        street_index = {("3304557", "sao jose"): ("Avenida Brasil", "20000000")}
        with patch.object(neighborhood_source_import, "get_first_street_from_dne_by_ibge") as dne, \
                patch.object(neighborhood_source_import, "get_coordinates_from_cep", return_value=(-22.9, -43.1)) as cep:
            geocoded, missing_cep, pending = geocode_candidates(
                candidates, connection="fake", street_index=street_index
            )
        self.assertFalse(dne.called)
        cep.assert_called_once_with("20000000")
        self.assertEqual((geocoded, missing_cep, pending), (1, 0, 0))
        self.assertEqual(candidates[0]["Latitude"], -22.9)
        self.assertEqual(candidates[0]["Longitude"], -43.1)
        self.assertEqual(candidates[0]["Source"], "brasil_aberto_first_street")

    def test_geocode_candidates_falls_back_to_cep_aberto_with_source(self):
        import neighborhood_source_import

        candidates = [{
            "Uf": "RJ", "CityIbgeCode": "3304557", "City": "Rio de Janeiro",
            "Neighborhood": "São José", "NormalizedName": "sao jose",
            "Latitude": None, "Longitude": None, "Source": "dne", "IsActive": True,
        }]
        street_index = {("3304557", "sao jose"): ("Avenida Brasil", "20000000")}
        with patch.object(
            neighborhood_source_import, "get_coordinates_from_cep", return_value=(None, None)
        ), patch.object(
            neighborhood_source_import, "get_coordinates_from_cep_aberto", return_value=(-22.9, -43.1)
        ) as cep_aberto:
            geocoded, missing_cep, pending = geocode_candidates(
                candidates, connection="fake", street_index=street_index
            )
        cep_aberto.assert_called_once_with("20000000")
        self.assertEqual((geocoded, missing_cep, pending), (1, 0, 0))
        self.assertEqual(candidates[0]["Latitude"], -22.9)
        self.assertEqual(candidates[0]["Longitude"], -43.1)
        self.assertEqual(candidates[0]["Source"], "cep_aberto")

    def test_geocode_candidates_keeps_pending_when_cep_aberto_has_no_token(self):
        import neighborhood_source_import

        candidates = [{
            "Uf": "RJ", "CityIbgeCode": "3304557", "City": "Rio de Janeiro",
            "Neighborhood": "São José", "NormalizedName": "sao jose",
            "Latitude": None, "Longitude": None, "Source": "dne", "IsActive": True,
        }]
        street_index = {("3304557", "sao jose"): ("Avenida Brasil", "20000000")}
        with patch.object(
            neighborhood_source_import, "get_coordinates_from_cep", return_value=(None, None)
        ), patch.object(
            neighborhood_source_import, "get_coordinates_from_cep_aberto", return_value=(None, None)
        ):
            geocoded, missing_cep, pending = geocode_candidates(
                candidates, connection="fake", street_index=street_index
            )
        self.assertEqual((geocoded, missing_cep, pending), (0, 0, 1))
        self.assertIsNone(candidates[0]["Latitude"])
        self.assertIsNone(candidates[0]["Longitude"])
        self.assertEqual(candidates[0]["Source"], "dne")

    def test_geocode_candidates_preserves_brasil_aberto_when_valid(self):
        import neighborhood_source_import

        candidates = [{
            "Uf": "RJ", "CityIbgeCode": "3304557", "City": "Rio de Janeiro",
            "Neighborhood": "São José", "NormalizedName": "sao jose",
            "Latitude": None, "Longitude": None, "Source": "dne", "IsActive": True,
        }]
        street_index = {("3304557", "sao jose"): ("Avenida Brasil", "20000000")}
        with patch.object(
            neighborhood_source_import, "get_coordinates_from_cep", return_value=(-22.9, -43.1)
        ), patch.object(
            neighborhood_source_import, "get_coordinates_from_cep_aberto"
        ) as cep_aberto:
            geocoded, missing_cep, pending = geocode_candidates(
                candidates, connection="fake", street_index=street_index
            )
        self.assertFalse(cep_aberto.called)
        self.assertEqual((geocoded, missing_cep, pending), (1, 0, 0))
        self.assertEqual(candidates[0]["Source"], "brasil_aberto_first_street")

    def test_import_uf_builds_index_once_and_does_not_query_per_candidate(self):
        import neighborhood_source_import

        columns = ["uf", "municipio_cod_ibge", "municipio", "bairro", "cep", "logradouro"]
        rows = [
            ("RJ", "3304557", "Rio de Janeiro", "São José", "20000-010", "Rua da Conceicao"),
            ("RJ", "3304557", "Rio de Janeiro", "SAO JOSE", "20000-000", "Avenida Brasil"),
            ("RJ", "3304557", "Rio de Janeiro", "Centro", "20010-000", "Rua do Ouvidor"),
        ]
        db_path = self._make_dne_db(columns, rows)
        connection = sqlite3.connect(db_path)
        try:
            with tempfile.TemporaryDirectory() as tmp:
                directory = Path(tmp)
                with patch.dict("os.environ", {"BRASIL_ABERTO_API_KEY": "test-only"}), \
                        patch.object(neighborhood_source_import, "fetch_municipalities", return_value=[
                            {"nome": "Rio de Janeiro", "id": 3304557},
                        ]), \
                        patch.object(neighborhood_source_import, "fetch_json", return_value={
                            "result": [{"name": "São José"}, {"name": "Centro"}, {"name": "Ipanema"}]
                        }), \
                        patch.object(neighborhood_source_import, "get_coordinates_from_cep", return_value=(-22.9, -43.1)), \
                        patch.object(neighborhood_source_import, "build_dne_street_index", wraps=neighborhood_source_import.build_dne_street_index) as build_index, \
                        patch.object(neighborhood_source_import, "get_first_street_from_dne_by_ibge") as dne:
                    report = neighborhood_source_import.import_uf(
                        "RJ", connection=connection, directory=directory
                    )
                self.assertEqual(report["geocoded"], 2)
                self.assertEqual(build_index.call_count, 1)
                self.assertFalse(dne.called)
        finally:
            connection.close()
            Path(db_path).unlink(missing_ok=True)

    def test_import_uf_writes_deduplicated_csv_with_geocoded_coordinates(self):
        import neighborhood_source_import

        columns = ["uf", "municipio_cod_ibge", "municipio", "bairro", "cep", "logradouro"]
        rows = [
            ("RJ", "3304557", "Rio de Janeiro", "São José", "20000-010", "Rua da Conceicao"),
            ("RJ", "3304557", "Rio de Janeiro", "SAO JOSE", "20000-000", "Avenida Brasil"),
            ("RJ", "3304557", "Rio de Janeiro", "Centro", "20010-000", "Rua do Ouvidor"),
        ]
        db_path = self._make_dne_db(columns, rows)
        connection = sqlite3.connect(db_path)
        try:
            with tempfile.TemporaryDirectory() as tmp:
                directory = Path(tmp)
                with patch.dict("os.environ", {"BRASIL_ABERTO_API_KEY": "test-only"}), \
                        patch.object(neighborhood_source_import, "fetch_municipalities", return_value=[
                            {"nome": "Rio de Janeiro", "id": 3304557},
                        ]), \
                        patch.object(neighborhood_source_import, "fetch_json", return_value={
                            "result": [{"name": "São José"}, {"name": "Centro"}, {"name": "Ipanema"}]
                        }), \
                        patch.object(neighborhood_source_import, "get_coordinates_from_cep", return_value=(-22.9, -43.1)):
                    report = neighborhood_source_import.import_uf(
                        "RJ", connection=connection, directory=directory
                    )

                self.assertEqual(report["candidates"], 3)
                self.assertEqual(report["geocoded"], 2)
                self.assertEqual(report["missing_cep"], 1)
                self.assertEqual(report["pending"], 1)
                self.assertTrue((directory / "bairros_rj.csv").exists())
                rows_out = read_snapshot(directory / "bairros_rj.csv")
                self.assertEqual(
                    sorted(row["NormalizedName"] for row in rows_out),
                    ["centro", "ipanema", "sao jose"],
                )
                by_name = {row["NormalizedName"]: row for row in rows_out}
                self.assertEqual(by_name["sao jose"]["Latitude"], "-22.9")
                self.assertEqual(by_name["sao jose"]["Longitude"], "-43.1")
                self.assertEqual(by_name["sao jose"]["Source"], "brasil_aberto_first_street")
                self.assertEqual(by_name["ipanema"]["Latitude"], "")
                self.assertEqual(by_name["ipanema"]["Longitude"], "")
        finally:
            connection.close()
            Path(db_path).unlink(missing_ok=True)

    def test_import_uf_leaves_pending_when_all_api_calls_fail(self):
        import neighborhood_source_import

        columns = ["bairro", "municipio", "logradouro", "cep"]
        rows = [("Centro", "Rio de Janeiro", "Rua do Ouvidor", "20010-000")]
        db_path = self._make_dne_db(columns, rows)
        connection = sqlite3.connect(db_path)
        try:
            with tempfile.TemporaryDirectory() as tmp:
                directory = Path(tmp)
                with patch.dict("os.environ", {"BRASIL_ABERTO_API_KEY": "test-only"}), \
                        patch.object(neighborhood_source_import, "fetch_municipalities", return_value=[
                            {"nome": "Rio de Janeiro", "id": 3304557},
                        ]), \
                        patch.object(neighborhood_source_import, "fetch_json", side_effect=RuntimeError("api down")), \
                        patch.object(neighborhood_source_import, "get_coordinates_from_cep", return_value=(None, None)), \
                        patch.object(neighborhood_source_import, "get_coordinates_from_cep_aberto", return_value=(None, None)):
                    report = neighborhood_source_import.import_uf(
                        "RJ", connection=connection, directory=directory
                    )
                self.assertEqual(report["geocoded"], 0)
                self.assertEqual(report["pending"], report["candidates"])
                rows_out = read_snapshot(directory / "bairros_rj.csv")
                self.assertEqual(rows_out[0]["Latitude"], "")
        finally:
            connection.close()
            Path(db_path).unlink(missing_ok=True)

    def test_import_uf_requires_api_key(self):
        import neighborhood_source_import

        with patch.dict("os.environ", {}, clear=False):
            os.environ.pop("BRASIL_ABERTO_API_KEY", None)
            with self.assertRaises(RuntimeError):
                neighborhood_source_import.import_uf("RJ")

    def test_cli_delegates_to_import_uf_with_uf_and_output(self):
        import neighborhood_source_import

        with patch.object(neighborhood_source_import, "import_uf", return_value={}) as importer:
            neighborhood_source_import.main(["--uf", "SP", "--output", "/tmp/snapshots"])
        importer.assert_called_once_with("SP", directory=Path("/tmp/snapshots"))


class _RebuildCursor:
    def __init__(self, cities=(), fail_on=None, delete_rowcount=0):
        self.cities = list(cities)
        self.fail_on = fail_on
        self.delete_rowcount = delete_rowcount
        self.statements = []
        self.rowcount = 0

    def execute(self, statement, params=None):
        self.statements.append((statement, params))
        if self.fail_on and self.fail_on in statement:
            raise RuntimeError("boom")
        if statement.lstrip().upper().startswith("DELETE"):
            self.rowcount = self.delete_rowcount
        else:
            self.rowcount = 0

    def fetchall(self):
        return self.cities

    def close(self):
        pass

    def __enter__(self):
        return self

    def __exit__(self, *args):
        return False


class _RebuildConnection:
    def __init__(self, cities=(), fail_on=None, delete_rowcount=0):
        self.cursor_instance = _RebuildCursor(cities, fail_on, delete_rowcount)
        self.commits = 0
        self.rollbacks = 0

    def cursor(self):
        return self.cursor_instance

    def commit(self):
        self.commits += 1

    def rollback(self):
        self.rollbacks += 1

    def close(self):
        pass


class NeighborhoodRebuildTests(unittest.TestCase):
    BASE_ROW = {
        "Uf": "MG",
        "CityIbgeCode": "3106200",
        "City": "Belo Horizonte",
        "Neighborhood": "Centro",
        "NormalizedName": "centro",
        "Latitude": "-19.9167",
        "Longitude": "-43.9345",
        "Source": "brasil_aberto",
        "IsActive": "true",
    }

    def _rows(self, *overrides):
        rows = []
        for override in overrides:
            rows.append({**self.BASE_ROW, **override})
        return rows

    def test_rebuild_cli_requires_exact_confirmation(self):
        import neighborhood_rebuild

        with patch.object(neighborhood_rebuild, "connect_database") as connect:
            with self.assertRaises(SystemExit):
                neighborhood_rebuild.main(
                    ["--uf", "MG", "--file", "x.csv", "--confirm", "REBUILD SP"]
                )
        connect.assert_not_called()

    def test_rebuild_cli_runs_with_exact_confirmation(self):
        import neighborhood_rebuild

        connection = _RebuildConnection([("city-id", "3106200", "Belo Horizonte")])
        with patch.object(neighborhood_rebuild, "read_snapshot", return_value=[self.BASE_ROW]), \
                patch.object(neighborhood_rebuild, "connect_database", return_value=connection), \
                patch.object(neighborhood_rebuild, "rebuild_uf", return_value={
                    "backup": 1, "deleted": 1, "inserted": 1,
                }) as rebuild:
            neighborhood_rebuild.main(
                ["--uf", "MG", "--file", "x.csv", "--confirm", "REBUILD MG"]
            )
        rebuild.assert_called_once()

    def test_rebuild_rejects_rows_from_other_uf(self):
        import neighborhood_rebuild

        rows = self._rows({"Uf": "RJ"})
        connection = _RebuildConnection([("city-id", "3106200", "Belo Horizonte")])
        with patch.object(neighborhood_rebuild, "export_snapshot") as backup:
            with self.assertRaises(ValueError):
                rebuild_uf(connection, "MG", rows, backup_path=IMPORT_DIR / "backup.csv")
        backup.assert_not_called()
        self.assertEqual(connection.commits, 0)
        self.assertEqual(connection.rollbacks, 0)
        self.assertEqual(connection.cursor_instance.statements, [])

    def test_rebuild_rejects_missing_city_and_rolls_back(self):
        import neighborhood_rebuild

        rows = self._rows({"CityIbgeCode": "9999999"})
        connection = _RebuildConnection([("city-id", "3106200", "Belo Horizonte")])
        with patch.object(neighborhood_rebuild, "export_snapshot", return_value=1):
            with self.assertRaises(ValueError):
                rebuild_uf(connection, "MG", rows, backup_path=IMPORT_DIR / "backup.csv")
        self.assertEqual(connection.rollbacks, 1)
        self.assertEqual(connection.commits, 0)
        statements = [statement for statement, _ in connection.cursor_instance.statements]
        self.assertFalse(any(s.lstrip().upper().startswith("DELETE") for s in statements))
        self.assertFalse(any(s.lstrip().upper().startswith("INSERT") for s in statements))

    def test_rebuild_rejects_city_name_divergence_before_delete(self):
        import neighborhood_rebuild

        rows = self._rows({"City": "Belo Horizonte"})
        connection = _RebuildConnection([("city-id", "3106200", "Sao Paulo")])
        with patch.object(neighborhood_rebuild, "export_snapshot", return_value=1):
            with self.assertRaisesRegex(ValueError, "divergentes"):
                rebuild_uf(connection, "MG", rows, backup_path=IMPORT_DIR / "backup.csv")
        self.assertEqual(connection.rollbacks, 1)
        self.assertEqual(connection.commits, 0)
        statements = [statement for statement, _ in connection.cursor_instance.statements]
        self.assertFalse(any(s.lstrip().upper().startswith("DELETE") for s in statements))
        self.assertFalse(any(s.lstrip().upper().startswith("INSERT") for s in statements))

    def test_rebuild_accepts_canonically_equivalent_city_name(self):
        import neighborhood_rebuild

        rows = self._rows({"City": "Belo Horizonte"})
        connection = _RebuildConnection([("city-id", "3106200", "BELO HORIZONTE")], delete_rowcount=1)
        with patch.object(neighborhood_rebuild, "export_snapshot", return_value=1):
            report = rebuild_uf(connection, "MG", rows, backup_path=IMPORT_DIR / "backup.csv")
        self.assertEqual(report["deleted"], 1)
        self.assertEqual(connection.rollbacks, 0)
        self.assertEqual(connection.commits, 1)

    def test_rebuild_rolls_back_on_insert_failure(self):
        import neighborhood_rebuild

        rows = self._rows({}, {"Neighborhood": "Savassi", "NormalizedName": "savassi"})
        connection = _RebuildConnection(
            [("city-id", "3106200", "Belo Horizonte")], fail_on='INSERT INTO "DeliveryNeighborhoods"'
        )
        with patch.object(neighborhood_rebuild, "export_snapshot", return_value=2):
            with self.assertRaises(RuntimeError):
                rebuild_uf(connection, "MG", rows, backup_path=IMPORT_DIR / "backup.csv")
        self.assertEqual(connection.rollbacks, 1)
        self.assertEqual(connection.commits, 0)

    def test_rebuild_delete_is_restricted_to_uf(self):
        import neighborhood_rebuild

        rows = self._rows({})
        connection = _RebuildConnection([("city-id", "3106200", "Belo Horizonte")], delete_rowcount=5)
        with patch.object(neighborhood_rebuild, "export_snapshot", return_value=3):
            report = rebuild_uf(connection, "MG", rows, backup_path=IMPORT_DIR / "backup.csv")
        delete_statement, delete_params = next(
            (statement, params)
            for statement, params in connection.cursor_instance.statements
            if statement.lstrip().upper().startswith("DELETE")
        )
        self.assertIn('"Cities"', delete_statement)
        self.assertIn('d."CityId" = c."Id"', delete_statement)
        self.assertIn('c."Uf" = %s', delete_statement)
        self.assertEqual(delete_params, ("MG",))
        self.assertEqual(report["deleted"], 5)

    def test_rebuild_delete_removes_legacy_rows_without_city_id(self):
        import neighborhood_rebuild

        rows = self._rows({})
        connection = _RebuildConnection([("city-id", "3106200", "Belo Horizonte")], delete_rowcount=7)
        with patch.object(neighborhood_rebuild, "export_snapshot", return_value=4):
            report = rebuild_uf(connection, "MG", rows, backup_path=IMPORT_DIR / "backup.csv")

        delete_statement, delete_params = next(
            (statement, params)
            for statement, params in connection.cursor_instance.statements
            if statement.lstrip().upper().startswith("DELETE")
        )
        self.assertIn('d."CityId" = c."Id"', delete_statement)
        self.assertIn('d."CityId" IS NULL', delete_statement)
        self.assertIn('d."City" = c."Name"', delete_statement)
        self.assertIn('c."Uf" = %s', delete_statement)
        self.assertEqual(delete_params, ("MG",))
        self.assertEqual(report["deleted"], 7)

    def test_rebuild_insert_succeeds_without_unique_violation_after_legacy_delete(self):
        import neighborhood_rebuild

        rows = self._rows({})
        connection = _RebuildConnection([("city-id", "3106200", "Belo Horizonte")], delete_rowcount=1)
        with patch.object(neighborhood_rebuild, "export_snapshot", return_value=1):
            report = rebuild_uf(connection, "MG", rows, backup_path=IMPORT_DIR / "backup.csv")

        self.assertEqual(connection.commits, 1)
        self.assertEqual(connection.rollbacks, 0)
        self.assertEqual(report, {"backup": 1, "deleted": 1, "inserted": 1})

        statements = connection.cursor_instance.statements
        delete_index = next(
            i for i, (statement, _) in enumerate(statements)
            if statement.lstrip().upper().startswith("DELETE")
        )
        insert_indices = [
            i for i, (statement, _) in enumerate(statements)
            if statement.lstrip().upper().startswith("INSERT")
        ]
        self.assertTrue(insert_indices)
        self.assertLess(delete_index, min(insert_indices))
        delete_statement = statements[delete_index][0]
        self.assertIn('d."CityId" IS NULL', delete_statement)
        self.assertIn('d."City" = c."Name"', delete_statement)

    def test_rebuild_documented_as_replacing_manual_legacy_records(self):
        documentation = (Path(__file__).parents[3] / "Documentacao/Backend/PopulacaoBairrosRJ.md").read_text(
            encoding="utf-8"
        )
        self.assertIn("registros legados com `CityId IS NULL`", documentation)
        self.assertIn("substitui todos os", documentation)

    def test_rebuild_success_backs_up_deletes_and_inserts(self):
        import neighborhood_rebuild

        rows = self._rows(
            {},
            {
                "Neighborhood": "Savassi",
                "NormalizedName": "savassi",
                "Latitude": "",
                "Longitude": "",
                "Source": "",
            },
        )
        connection = _RebuildConnection([("city-id", "3106200", "Belo Horizonte")], delete_rowcount=2)
        backup_path = IMPORT_DIR / "backup.csv"
        with patch.object(neighborhood_rebuild, "export_snapshot", return_value=2) as backup:
            report = rebuild_uf(connection, "MG", rows, backup_path=backup_path)

        backup.assert_called_once_with(connection, "MG", backup_path, validate=False)
        self.assertEqual(report, {"backup": 2, "deleted": 2, "inserted": 2})
        self.assertEqual(connection.commits, 1)
        self.assertEqual(connection.rollbacks, 0)

        statements = connection.cursor_instance.statements
        kinds = [statement.lstrip().upper().split()[0] for statement, _ in statements]
        self.assertEqual(kinds, ["SELECT", "DELETE", "INSERT", "INSERT"])

        insert_statement, insert_params = statements[2]
        self.assertIn("gen_random_uuid()", insert_statement)
        self.assertIn("NULLIF(%s, '')", insert_statement)
        self.assertEqual(
            insert_params,
            ("city-id", "Belo Horizonte", "Centro", "centro", "-19.9167", "-43.9345", "brasil_aberto", True),
        )

        _, second_params = statements[3]
        self.assertEqual(
            second_params,
            ("city-id", "Belo Horizonte", "Savassi", "savassi", "", "", "", True),
        )

    def test_backup_uf_exports_legacy_normalized_name_without_validation(self):
        import neighborhood_rebuild

        class Cursor:
            def execute(self, statement, params):
                pass

            def fetchall(self):
                return [("RJ", "3304557", "Rio de Janeiro", "São José", "são josé", "-22.9", "-43.1", "brasil_aberto", True)]

            def __enter__(self):
                return self

            def __exit__(self, *args):
                return False

        class Connection:
            def cursor(self):
                return Cursor()

        target = IMPORT_DIR / "legacy-backup.csv"
        try:
            self.assertEqual(neighborhood_rebuild.backup_uf(Connection(), "RJ", target), 1)
            content = target.read_text(encoding="utf-8")
            self.assertIn("São José", content)
            self.assertIn("são josé", content)
        finally:
            target.unlink(missing_ok=True)


class NeighborhoodRegeocodeTests(unittest.TestCase):
    def _row(self, *overrides):
        row = {
            "Uf": "RJ",
            "CityIbgeCode": "3304557",
            "City": "Rio de Janeiro",
            "Neighborhood": "São José",
            "NormalizedName": "sao jose",
            "Latitude": "",
            "Longitude": "",
            "Source": "dne",
            "IsActive": "true",
        }
        for override in overrides:
            row.update(override)
        return row

    def _write_csv(self, rows):
        handle = tempfile.NamedTemporaryFile(suffix=".csv", delete=False)
        handle.close()
        write_snapshot(rows, handle.name)
        return Path(handle.name)

    def _make_dne_db(self, columns, rows):
        handle = tempfile.NamedTemporaryFile(suffix=".db", delete=False)
        handle.close()
        db_path = handle.name
        connection = sqlite3.connect(db_path)
        try:
            connection.execute(
                f"CREATE TABLE cep_unificado ({', '.join(c + ' TEXT' for c in columns)})"
            )
            connection.executemany(
                f"INSERT INTO cep_unificado ({', '.join(columns)}) VALUES ({', '.join('?' for _ in columns)})",
                rows,
            )
            connection.commit()
        finally:
            connection.close()
        return db_path

    def test_regeocode_rows_rejects_other_uf_before_changes(self):
        import neighborhood_regeocode

        rows = [
            self._row(),
            self._row({"Uf": "SP", "CityIbgeCode": "3550308", "City": "São Paulo",
                       "Neighborhood": "Centro", "NormalizedName": "centro"}),
        ]
        index = {("3304557", "sao jose"): ("Avenida Brasil", "20000000")}
        with self.assertRaises(ValueError):
            neighborhood_regeocode.regeocode_rows(rows, "RJ", index)
        self.assertEqual(rows[0]["Latitude"], "")
        self.assertEqual(rows[0]["Longitude"], "")
        self.assertEqual(rows[1]["Latitude"], "")
        self.assertEqual(rows[1]["Longitude"], "")

    def test_regeocode_rows_fills_pending_and_preserves_geocoded(self):
        import neighborhood_regeocode

        rows = [
            self._row(
                {"Latitude": "-22.9068", "Longitude": "-43.1729", "Source": "brasil_aberto"}
            ),
            self._row(),
        ]
        index = {("3304557", "sao jose"): ("Avenida Brasil", "20000000")}
        with patch.object(
            neighborhood_regeocode, "get_coordinates_from_cep_aberto", return_value=(-22.9, -43.1)
        ):
            result = neighborhood_regeocode.regeocode_rows(rows, "RJ", index)
        self.assertEqual(result, (2, 1, 0, 0, 0))
        self.assertEqual(rows[0]["Latitude"], "-22.9068")
        self.assertEqual(rows[0]["Longitude"], "-43.1729")
        self.assertEqual(rows[0]["Source"], "brasil_aberto")
        self.assertEqual(rows[1]["Latitude"], -22.9)
        self.assertEqual(rows[1]["Longitude"], -43.1)
        self.assertEqual(rows[1]["Source"], "cep_aberto")

    def test_regeocode_rows_keeps_pending_without_cep(self):
        import neighborhood_regeocode

        rows = [self._row()]
        index = {}
        with patch.object(neighborhood_regeocode, "get_coordinates_from_cep_aberto") as cep:
            result = neighborhood_regeocode.regeocode_rows(rows, "RJ", index)
        self.assertFalse(cep.called)
        self.assertEqual(result, (1, 0, 1, 1, 0))
        self.assertEqual(rows[0]["Latitude"], "")
        self.assertEqual(rows[0]["Longitude"], "")
        self.assertEqual(rows[0]["Source"], "dne")

    def test_regeocode_rows_keeps_pending_when_cep_aberto_returns_none(self):
        import neighborhood_regeocode

        rows = [self._row()]
        index = {("3304557", "sao jose"): ("Avenida Brasil", "20000000")}
        with patch.object(
            neighborhood_regeocode, "get_coordinates_from_cep_aberto", return_value=(None, None)
        ), patch.object(
            neighborhood_regeocode, "get_coordinates_from_mapbox", return_value=(None, None)
        ):
            result = neighborhood_regeocode.regeocode_rows(rows, "RJ", index)
        self.assertEqual(result, (1, 0, 0, 1, 0))
        self.assertEqual(rows[0]["Latitude"], "")
        self.assertEqual(rows[0]["Longitude"], "")
        self.assertEqual(rows[0]["Source"], "dne")

    def test_regeocode_rows_counts_provider_errors_and_continues_after_exception(self):
        import neighborhood_regeocode

        rows = [
            self._row(),
            self._row({"Neighborhood": "Centro", "NormalizedName": "centro"}),
        ]
        index = {
            ("3304557", "sao jose"): ("Avenida Brasil", "20000000"),
            ("3304557", "centro"): ("Rua do Ouvidor", "20010000"),
        }

        def cep_aberto_side_effect(cep):
            if cep == "20000000":
                raise RuntimeError("403 Forbidden")
            return (-22.9, -43.1)

        with patch.object(
            neighborhood_regeocode, "get_coordinates_from_cep_aberto", side_effect=cep_aberto_side_effect
        ), patch.object(
            neighborhood_regeocode, "get_coordinates_from_mapbox", return_value=(None, None)
        ):
            result = neighborhood_regeocode.regeocode_rows(rows, "RJ", index)

        self.assertEqual(result, (2, 1, 0, 1, 1))
        self.assertEqual(rows[0]["Latitude"], "")
        self.assertEqual(rows[0]["Longitude"], "")
        self.assertEqual(rows[0]["Source"], "dne")
        self.assertEqual(rows[1]["Latitude"], -22.9)
        self.assertEqual(rows[1]["Longitude"], -43.1)
        self.assertEqual(rows[1]["Source"], "cep_aberto")

    def test_regeocode_rows_caches_repeated_cep_lookups(self):
        import geocode_via_cep
        import neighborhood_regeocode

        geocode_via_cep._cep_aberto_coordinate_cache.clear()
        rows = [
            self._row(),
            self._row({"Neighborhood": "Centro", "NormalizedName": "centro"}),
        ]
        index = {
            ("3304557", "sao jose"): ("Avenida Brasil", "20000000"),
            ("3304557", "centro"): ("Rua do Ouvidor", "20000000"),
        }
        calls = []
        original_fetch = geocode_via_cep.fetch_json
        try:
            geocode_via_cep.fetch_json = lambda url, headers=None: (
                calls.append(url) or {"latitude": "-22.9", "longitude": "-43.1"}
            )
            with patch.dict("os.environ", {"CEP_ABERTO_API_TOKEN": "secret-token"}), \
                    patch.object(geocode_via_cep.time, "sleep"):
                result = neighborhood_regeocode.regeocode_rows(rows, "RJ", index)
        finally:
            geocode_via_cep.fetch_json = original_fetch
            geocode_via_cep._cep_aberto_coordinate_cache.clear()

        self.assertEqual(result, (2, 2, 0, 0, 0))
        self.assertEqual(len(calls), 1)
        self.assertEqual(rows[0]["Latitude"], -22.9)
        self.assertEqual(rows[1]["Latitude"], -22.9)
        self.assertEqual(rows[0]["Source"], "cep_aberto")
        self.assertEqual(rows[1]["Source"], "cep_aberto")

    def test_regeocode_rows_falls_back_to_mapbox_with_source(self):
        import neighborhood_regeocode

        rows = [self._row()]
        index = {("3304557", "sao jose"): ("Avenida Brasil", "20000000")}
        with patch.object(
            neighborhood_regeocode, "get_coordinates_from_cep_aberto", return_value=(None, None)
        ), patch.object(
            neighborhood_regeocode, "get_coordinates_from_mapbox", return_value=(-22.9, -43.1)
        ) as mapbox:
            result = neighborhood_regeocode.regeocode_rows(rows, "RJ", index)
        mapbox.assert_called_once_with("Avenida Brasil", "Rio de Janeiro", "RJ", "20000000")
        self.assertEqual(result, (1, 1, 0, 0, 0))
        self.assertEqual(rows[0]["Latitude"], -22.9)
        self.assertEqual(rows[0]["Longitude"], -43.1)
        self.assertEqual(rows[0]["Source"], "mapbox_geocoding")

    def test_regeocode_rows_does_not_call_mapbox_when_cep_aberto_succeeds(self):
        import neighborhood_regeocode

        rows = [self._row()]
        index = {("3304557", "sao jose"): ("Avenida Brasil", "20000000")}
        with patch.object(
            neighborhood_regeocode, "get_coordinates_from_cep_aberto", return_value=(-22.9, -43.1)
        ), patch.object(
            neighborhood_regeocode, "get_coordinates_from_mapbox"
        ) as mapbox:
            result = neighborhood_regeocode.regeocode_rows(rows, "RJ", index)
        self.assertFalse(mapbox.called)
        self.assertEqual(result, (1, 1, 0, 0, 0))
        self.assertEqual(rows[0]["Source"], "cep_aberto")

    def test_regeocode_rows_counts_mapbox_provider_errors_and_continues(self):
        import neighborhood_regeocode

        rows = [
            self._row(),
            self._row({"Neighborhood": "Centro", "NormalizedName": "centro"}),
        ]
        index = {
            ("3304557", "sao jose"): ("Avenida Brasil", "20000000"),
            ("3304557", "centro"): ("Rua do Ouvidor", "20010000"),
        }

        def mapbox_side_effect(street, city, uf, cep):
            if cep == "20000000":
                raise RuntimeError("mapbox down")
            return (-22.9, -43.1)

        with patch.object(
            neighborhood_regeocode, "get_coordinates_from_cep_aberto", return_value=(None, None)
        ), patch.object(
            neighborhood_regeocode, "get_coordinates_from_mapbox", side_effect=mapbox_side_effect
        ):
            result = neighborhood_regeocode.regeocode_rows(rows, "RJ", index)

        self.assertEqual(result, (2, 1, 0, 1, 1))
        self.assertEqual(rows[0]["Latitude"], "")
        self.assertEqual(rows[0]["Longitude"], "")
        self.assertEqual(rows[0]["Source"], "dne")
        self.assertEqual(rows[1]["Latitude"], -22.9)
        self.assertEqual(rows[1]["Longitude"], -43.1)
        self.assertEqual(rows[1]["Source"], "mapbox_geocoding")

    def test_regeocode_rows_keeps_pending_when_both_providers_return_none(self):
        import neighborhood_regeocode

        rows = [self._row()]
        index = {("3304557", "sao jose"): ("Avenida Brasil", "20000000")}
        with patch.object(
            neighborhood_regeocode, "get_coordinates_from_cep_aberto", return_value=(None, None)
        ), patch.object(
            neighborhood_regeocode, "get_coordinates_from_mapbox", return_value=(None, None)
        ):
            result = neighborhood_regeocode.regeocode_rows(rows, "RJ", index)
        self.assertEqual(result, (1, 0, 0, 1, 0))
        self.assertEqual(rows[0]["Latitude"], "")
        self.assertEqual(rows[0]["Longitude"], "")
        self.assertEqual(rows[0]["Source"], "dne")

    def test_regeocode_file_requires_token_before_touching_file(self):
        import neighborhood_regeocode

        rows = [self._row()]
        input_path = self._write_csv(rows)
        output_path = input_path.with_name("out.csv")
        try:
            with patch.object(neighborhood_regeocode, "cep_aberto_token", return_value=None), \
                    patch.object(neighborhood_regeocode, "connect_dne") as connect:
                with self.assertRaises(RuntimeError):
                    neighborhood_regeocode.regeocode_file(input_path, "RJ", output_path)
            connect.assert_not_called()
            self.assertFalse(output_path.exists())
        finally:
            input_path.unlink(missing_ok=True)
            output_path.unlink(missing_ok=True)

    def test_regeocode_file_uses_dne_index_and_writes_validated_csv(self):
        import neighborhood_regeocode

        columns = ["uf", "municipio_cod_ibge", "municipio", "bairro", "cep", "logradouro"]
        rows = [
            ("RJ", "3304557", "Rio de Janeiro", "São José", "20000-000", "Avenida Brasil"),
            ("RJ", "3304557", "Rio de Janeiro", "Centro", "20010-000", "Rua do Ouvidor"),
        ]
        db_path = self._make_dne_db(columns, rows)

        snapshot_rows = [
            self._row({
                "Neighborhood": "Centro",
                "NormalizedName": "centro",
                "Latitude": "-22.9068",
                "Longitude": "-43.1729",
                "Source": "brasil_aberto",
            }),
            self._row(),
            self._row({"Neighborhood": "Ipanema", "NormalizedName": "ipanema"}),
        ]
        input_path = self._write_csv(snapshot_rows)
        output_path = input_path.with_name("out.csv")
        try:
            connection = sqlite3.connect(db_path)
            with patch.object(neighborhood_regeocode, "cep_aberto_token", return_value="token"), \
                    patch.object(neighborhood_regeocode, "connect_dne", return_value=connection), \
                    patch.object(
                        neighborhood_regeocode,
                        "get_coordinates_from_cep_aberto",
                        return_value=(-22.9, -43.1),
                    ) as cep, \
                    patch.object(neighborhood_regeocode, "build_dne_street_index",
                                 wraps=neighborhood_regeocode.build_dne_street_index) as build_index:
                report = neighborhood_regeocode.regeocode_file(input_path, "RJ", output_path)

            self.assertEqual(build_index.call_count, 1)
            self.assertEqual(
                report,
                {"total": 3, "recuperados": 1, "pendentes": 1, "sem_cep": 1, "provider_errors": 0},
            )
            self.assertEqual(cep.call_count, 1)

            result = read_snapshot(output_path)
            by_name = {row["NormalizedName"]: row for row in result}
            self.assertEqual(by_name["sao jose"]["Latitude"], "-22.9")
            self.assertEqual(by_name["sao jose"]["Longitude"], "-43.1")
            self.assertEqual(by_name["sao jose"]["Source"], "cep_aberto")
            self.assertEqual(by_name["sao jose"]["Neighborhood"], "São José")
            self.assertEqual(by_name["ipanema"]["Latitude"], "")
            self.assertEqual(by_name["ipanema"]["Longitude"], "")
            self.assertEqual(by_name["ipanema"]["Source"], "dne")
        finally:
            connection.close()
            db_path and Path(db_path).unlink(missing_ok=True)
            input_path.unlink(missing_ok=True)
            output_path.unlink(missing_ok=True)

    def test_regeocode_file_defaults_output_to_input_file(self):
        import neighborhood_regeocode

        with patch.object(
            neighborhood_regeocode,
            "regeocode_file",
            return_value={"total": 0, "recuperados": 0, "pendentes": 0, "sem_cep": 0, "provider_errors": 0},
        ) as regeocode:
            neighborhood_regeocode.main(["--uf", "RJ", "--file", "in.csv"])
        regeocode.assert_called_once_with(Path("in.csv"), "RJ", Path("in.csv"))

    def test_regeocode_cli_delegates_with_explicit_output(self):
        import neighborhood_regeocode

        with patch.object(
            neighborhood_regeocode,
            "regeocode_file",
            return_value={"total": 0, "recuperados": 0, "pendentes": 0, "sem_cep": 0, "provider_errors": 0},
        ) as regeocode:
            neighborhood_regeocode.main(
                ["--uf", "SP", "--file", "in.csv", "--output", "out.csv"]
            )
        regeocode.assert_called_once_with(Path("in.csv"), "SP", Path("out.csv"))

    def test_regeocode_documented(self):
        documentation = (Path(__file__).parents[3] / "Documentacao/Backend/PopulacaoBairrosRJ.md").read_text(
            encoding="utf-8"
        )
        self.assertIn("neighborhood_regeocode.py", documentation)


if __name__ == "__main__":
    unittest.main()
