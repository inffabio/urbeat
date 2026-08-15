from pathlib import Path
import unittest
from unittest.mock import patch

from import_common import load_local_environment
from brasil_aberto_import import ibge_state_code, snapshot_path, validate_uf
from geocode_via_cep import get_coordinates_from_nominatim
from neighborhood_snapshot import (
    export_snapshot,
    read_snapshot,
    restore_snapshot,
    write_snapshot,
)


IMPORT_DIR = Path(__file__).parent


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
            self.assertIn('COALESCE("Latitude", %s)', source)
            self.assertIn('COALESCE("Longitude", %s)', source)

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
                restore_snapshot(connection, target)
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
            self.assertEqual(restore_snapshot(connection, target), 1)
            statement, params = connection.cursor_instance.statements[0]
            self.assertIn('COALESCE("DeliveryNeighborhoods"."Latitude", EXCLUDED."Latitude")', statement)
            self.assertIn('COALESCE("DeliveryNeighborhoods"."Longitude", EXCLUDED."Longitude")', statement)
            self.assertEqual(params[-2:], ("MG", "3106200"))
            self.assertEqual(connection.commits, 1)
        finally:
            target.unlink(missing_ok=True)

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

    def test_import_publishes_snapshot_when_geocoding_is_pending(self):
        import brasil_aberto_import

        class Cursor:
            rowcount = 1

            def execute(self, statement, params):
                self.last_statement = statement

            def fetchall(self):
                return [("city-id", "Rio", "3304557")]

            def close(self):
                pass

        class Connection:
            autocommit = False

            def __init__(self):
                self.cursor_instance = Cursor()

            def cursor(self):
                return self.cursor_instance

            def commit(self):
                pass

        with patch.dict("os.environ", {"BRASIL_ABERTO_API_KEY": "test-only"}), \
                patch.object(brasil_aberto_import, "fetch_municipalities", return_value=[{"nome": "Rio", "id": 3304557}]), \
                patch.object(brasil_aberto_import, "fetch_json", return_value={"result": [{"name": "Centro"}]}), \
                 patch("geocode_via_cep.geocode_uf", return_value=(1, 1, 1)), \
                patch.object(brasil_aberto_import, "export_snapshot") as exporter:
            brasil_aberto_import.import_uf("RJ", Connection())
            exporter.assert_called_once()

    def test_neighborhood_documentation_describes_pending_coordinates(self):
        documentation = (Path(__file__).parents[3] / "Documentacao/Backend/PopulacaoBairrosRJ.md").read_text(
            encoding="utf-8"
        )
        self.assertIn("Latitude` e `Longitude` permanecem vazios", documentation)
        self.assertIn("rua/CEP encontrada", documentation)
        self.assertIn("nunca inventa coordenadas", documentation)


if __name__ == "__main__":
    unittest.main()
