"""Testes das funcoes puras e da descoberta do schema CNEFE.

Todos os testes usam bancos SQLite temporarios criados pelo proprio teste;
o artefato real ``imports/cnefe/delivery_brasil.db`` nunca e aberto aqui.

Execucao (a partir de ``backend/scripts/import``):
    python -m unittest test_cnefe_discover_schema -v
"""

import io
import json
import math
import sqlite3
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path

import cnefe_discover_schema as discovery
from cnefe_discover_schema import (
    BRAZIL_ENVELOPE,
    CEP_EMPTY,
    CEP_INVALID,
    CEP_MISSING,
    CEP_REPAIRABLE,
    CEP_VALID,
    COORD_MISSING_BOTH,
    COORD_MISSING_PARTIAL,
    COORD_NON_FINITE,
    COORD_NON_NUMERIC,
    COORD_OUT_OF_BRAZIL,
    COORD_OUT_OF_RANGE,
    COORD_VALID,
    COORD_ZERO,
    CentroidAccumulator,
    SchemaMappingError,
    classify_cep,
    classify_coordinates,
    classify_municipality,
    classify_uf,
    compare_centroids,
    discover,
    haversine_meters,
    kahan_add,
    open_readonly_connection,
    percentile,
    summarize_distribution,
    write_reports,
)


def build_fixture(path: Path, *, include_zip=True, include_ruas=True) -> None:
    """Cria um SQLite minimo com o mesmo layout do artefato real."""
    connection = sqlite3.connect(str(path))
    try:
        connection.execute(
            """
            CREATE TABLE bairros (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                uf_codigo TEXT,
                municipio_codigo TEXT,
                nome TEXT,
                latitude_central REAL,
                longitude_central REAL
            )
            """
        )
        connection.execute(
            """
            CREATE TABLE bairros_extra (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                uf_codigo TEXT,
                municipio_codigo TEXT,
                nome TEXT,
                latitude_central REAL,
                longitude_central REAL
            )
            """
        )
        if include_ruas:
            zip_column = "cep TEXT," if include_zip else ""
            connection.execute(
                f"""
                CREATE TABLE ruas (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    uf_codigo TEXT,
                    municipio_codigo TEXT,
                    bairro TEXT,
                    logradouro TEXT,
                    {zip_column}
                    latitude REAL,
                    longitude REAL
                )
                """
            )
        connection.execute(
            "CREATE INDEX idx_bairros_municipio ON bairros(municipio_codigo)"
        )

        neighborhoods = [
            ("11", "1100015", "CENTRO", -12.0, -62.0),
            ("11", "1100015", "ZONA RURAL", -12.1, -62.1),
            ("11", "1100015", "SEM PONTO", -12.2, -62.2),
            ("11", "1100015", "PONTO INVALIDO", -12.3, -62.3),
            ("11", "1100015", "NORA", -3.849624, -32.427444),
        ]
        connection.executemany(
            "INSERT INTO bairros (uf_codigo, municipio_codigo, nome, latitude_central, longitude_central)"
            " VALUES (?, ?, ?, ?, ?)",
            neighborhoods,
        )
        if include_ruas:
            streets = [
                # CENTRO: dois pontos iguais ao centroide -> erro 0
                ("11", "1100015", "CENTRO", "RUA A", "01310100", -12.0, -62.0),
                ("11", "1100015", "CENTRO", "RUA B", "01310-100", -12.0, -62.0),
                # ZONA RURAL: um ponto distante -> divergencia acima da tolerancia
                ("11", "1100015", "ZONA RURAL", "RUA C", "123", -12.2, -62.2),
                # PONTO INVALIDO: coordenada zerada -> ponto descartado
                ("11", "1100015", "PONTO INVALIDO", "RUA D", "", 0.0, 0.0),
                # NORA: ilha oceânica, dentro da envoltoria generosa
                ("11", "1100015", "NORA", "RUA E", "53990000", -3.849624, -32.427444),
                # Duplicata exata da chave natural (e do ponto)
                ("11", "1100015", "CENTRO", "RUA A", "01310100", -12.0, -62.0),
                # Coordenada parcialmente nula
                ("11", "1100015", "CENTRO", "RUA F", None, None, -62.0),
            ]
            if include_zip:
                connection.executemany(
                    "INSERT INTO ruas (uf_codigo, municipio_codigo, bairro, logradouro, cep, latitude, longitude)"
                    " VALUES (?, ?, ?, ?, ?, ?, ?)",
                    streets,
                )
            else:
                connection.executemany(
                    "INSERT INTO ruas (uf_codigo, municipio_codigo, bairro, logradouro, latitude, longitude)"
                    " VALUES (?, ?, ?, ?, ?, ?)",
                    [(row[0], row[1], row[2], row[3], row[5], row[6]) for row in streets],
                )
        connection.commit()
    finally:
        connection.close()


class PureHelpersTests(unittest.TestCase):
    def test_haversine_one_degree_of_latitude(self):
        self.assertAlmostEqual(
            haversine_meters(0.0, 0.0, 1.0, 0.0),
            discovery.EARTH_RADIUS_M * math.pi / 180.0,
            places=6,
        )

    def test_haversine_is_symmetric_and_zero_for_same_point(self):
        self.assertEqual(haversine_meters(-23.5, -46.6, -23.5, -46.6), 0.0)
        forward = haversine_meters(-23.5, -46.6, -22.9, -43.2)
        backward = haversine_meters(-22.9, -43.2, -23.5, -46.6)
        self.assertAlmostEqual(forward, backward, places=6)

    def test_percentile_interpolates_linearly(self):
        values = [1.0, 2.0, 3.0, 4.0]
        self.assertEqual(percentile(values, 0.0), 1.0)
        self.assertEqual(percentile(values, 0.5), 2.5)
        self.assertAlmostEqual(percentile(values, 0.9), 3.7, places=9)
        self.assertAlmostEqual(percentile(values, 0.99), 3.97, places=9)
        self.assertEqual(percentile(values, 1.0), 4.0)
        self.assertEqual(percentile([7.0], 0.5), 7.0)
        self.assertIsNone(percentile([], 0.5))

    def test_percentile_rejects_fraction_out_of_range(self):
        with self.assertRaises(ValueError):
            percentile([1.0], 1.5)

    def test_summarize_distribution(self):
        summary = summarize_distribution([0.0, 1.0, 2.0, 3.0])
        self.assertEqual(summary["count"], 4)
        self.assertEqual(summary["min"], 0.0)
        self.assertEqual(summary["median"], 1.5)
        self.assertEqual(summary["max"], 3.0)
        self.assertAlmostEqual(summary["mean"], 1.5)
        empty = summarize_distribution([])
        self.assertEqual(empty["count"], 0)
        self.assertIsNone(empty["median"])

    def test_kahan_add_is_at_least_as_accurate_as_naive_sum(self):
        values = [0.1, 1e16, -1e16, 0.1, 0.1]
        total = 0.0
        compensation = 0.0
        for value in values:
            total, compensation = kahan_add(total, compensation, value)
        naive = 0.0
        for value in values:
            naive += value
        exact = math.fsum(values)
        self.assertLessEqual(
            abs((total + compensation) - exact), abs(naive - exact)
        )


class ClassifierTests(unittest.TestCase):
    def test_classify_coordinates_codes(self):
        cases = (
            ((None, None), COORD_MISSING_BOTH),
            ((None, -46.6), COORD_MISSING_PARTIAL),
            ((-23.5, None), COORD_MISSING_PARTIAL),
            (("abc", -46.6), COORD_NON_NUMERIC),
            ((float("nan"), -46.6), COORD_NON_FINITE),
            ((float("inf"), -46.6), COORD_NON_FINITE),
            ((0.0, 0.0), COORD_ZERO),
            ((95.0, -46.6), COORD_OUT_OF_RANGE),
            ((-23.5, 200.0), COORD_OUT_OF_RANGE),
            ((10.0, -20.0), COORD_OUT_OF_BRAZIL),
            ((-23.5614, -46.6559), COORD_VALID),
            ((-3.8496, -32.4274), COORD_VALID),  # Fernando de Noronha
        )
        for (latitude, longitude), expected in cases:
            with self.subTest(latitude=latitude, longitude=longitude):
                self.assertEqual(classify_coordinates(latitude, longitude), expected)

    def test_brazil_envelope_covers_oceanic_islands(self):
        for latitude, longitude in ((-3.8496, -32.4274), (-20.5, -29.3), (-33.7, -53.4)):
            with self.subTest(latitude=latitude, longitude=longitude):
                self.assertEqual(
                    classify_coordinates(latitude, longitude), COORD_VALID
                )

    def test_classify_uf(self):
        self.assertEqual(classify_uf("35"), ("35", "ibge_state_code"))
        self.assertEqual(classify_uf(" 35 "), ("35", "ibge_state_code"))
        self.assertEqual(classify_uf("SP"), ("35", "uf_sigla"))
        self.assertEqual(classify_uf("sp"), ("35", "uf_sigla"))
        self.assertEqual(classify_uf("99"), (None, None))
        self.assertEqual(classify_uf(None), (None, None))
        self.assertEqual(classify_uf(""), (None, None))

    def test_classify_municipality(self):
        self.assertEqual(classify_municipality("3550308"), ("3550308", "digits7"))
        self.assertEqual(classify_municipality(3550308), ("3550308", "digits7"))
        self.assertEqual(classify_municipality("3550308.0"), ("3550308", "normalized"))
        self.assertEqual(classify_municipality(" 3550308 "), ("3550308", "normalized"))
        self.assertEqual(classify_municipality("355030"), (None, "invalid"))
        self.assertEqual(classify_municipality("35503081"), (None, "invalid"))
        self.assertEqual(classify_municipality("abc"), (None, "invalid"))
        self.assertEqual(classify_municipality(None), (None, "invalid"))

    def test_classify_cep(self):
        self.assertEqual(classify_cep("01310100"), CEP_VALID)
        self.assertEqual(classify_cep(" 01310100 "), CEP_VALID)
        self.assertEqual(classify_cep("01310-100"), CEP_REPAIRABLE)
        self.assertEqual(classify_cep("0131.0100"), CEP_REPAIRABLE)
        self.assertEqual(classify_cep("1310100"), CEP_INVALID)
        self.assertEqual(classify_cep("123"), CEP_INVALID)
        self.assertEqual(classify_cep(""), CEP_EMPTY)
        self.assertEqual(classify_cep(None), CEP_MISSING)


class AccumulatorTests(unittest.TestCase):
    def test_points_are_averaged_per_neighborhood(self):
        accumulator = CentroidAccumulator()
        accumulator.register_neighborhood("35", "3550308", "CENTRO", -23.5, -46.6)
        accumulator.add_point("35", "3550308", "CENTRO", -23.5, -46.6)
        accumulator.add_point("35", "3550308", "CENTRO", -23.6, -46.7)
        comparison = compare_centroids(accumulator, tolerance_m=1.0)
        self.assertEqual(comparison.compared, 1)
        self.assertEqual(len(comparison.errors), 1)
        self.assertGreater(comparison.errors[0], 1000.0)
        self.assertEqual(comparison.above_tolerance, 1)
        self.assertEqual(len(comparison.above_tolerance_samples), 1)
        sample = comparison.above_tolerance_samples[0]
        self.assertAlmostEqual(sample["recomputed"]["latitude"], -23.55, places=9)
        self.assertAlmostEqual(sample["recomputed"]["longitude"], -46.65, places=9)
        self.assertEqual(sample["point_count"], 2)

    def test_duplicate_neighborhood_key_is_counted_once(self):
        accumulator = CentroidAccumulator()
        self.assertTrue(
            accumulator.register_neighborhood("35", "3550308", "CENTRO", -23.5, -46.6)
        )
        self.assertFalse(
            accumulator.register_neighborhood("35", "3550308", "CENTRO", -23.5, -46.6)
        )
        self.assertEqual(len(accumulator), 1)
        self.assertEqual(accumulator.duplicate_neighborhood_rows, 1)
        self.assertEqual(len(accumulator.duplicate_neighborhood_samples), 1)

    def test_orphan_points_are_not_attributed(self):
        accumulator = CentroidAccumulator()
        accumulator.register_neighborhood("35", "3550308", "CENTRO", -23.5, -46.6)
        self.assertFalse(
            accumulator.add_point("35", "3550308", "OUTRO", -23.5, -46.6)
        )
        self.assertEqual(accumulator.orphan_rows, 1)
        self.assertEqual(len(accumulator.orphan_groups), 1)

    def test_without_points_and_low_sample(self):
        accumulator = CentroidAccumulator()
        accumulator.register_neighborhood("35", "3550308", "SEM PONTO", -23.5, -46.6)
        accumulator.register_neighborhood("35", "3550308", "UM PONTO", -23.5, -46.6)
        accumulator.add_point("35", "3550308", "UM PONTO", -23.5, -46.6)
        comparison = compare_centroids(
            accumulator, tolerance_m=1.0, low_sample_threshold=5
        )
        self.assertEqual(comparison.without_points, 1)
        self.assertEqual(comparison.low_sample, 1)
        self.assertEqual(len(comparison.low_sample_samples), 1)
        self.assertEqual(comparison.compared, 1)
        self.assertEqual(comparison.errors[0], 0.0)

    def test_divergence_samples_are_limited_and_sorted(self):
        accumulator = CentroidAccumulator()
        for index in range(30):
            name = f"BAIRRO {index:02d}"
            accumulator.register_neighborhood("35", "3550308", name, -23.5, -46.6)
            accumulator.add_point("35", "3550308", name, -23.5 - 0.001 * (index + 1), -46.6)
        comparison = compare_centroids(accumulator, tolerance_m=1.0, max_samples=5)
        self.assertEqual(comparison.above_tolerance, 30)
        self.assertEqual(len(comparison.above_tolerance_samples), 5)
        errors = [sample["error_m"] for sample in comparison.above_tolerance_samples]
        self.assertEqual(errors, sorted(errors, reverse=True))


class ReadOnlyAccessTests(unittest.TestCase):
    def test_open_readonly_blocks_writes(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "fixture.db"
            build_fixture(path)
            connection = open_readonly_connection(path)
            try:
                with self.assertRaises(sqlite3.OperationalError):
                    connection.execute("CREATE TABLE proibida (id INTEGER)")
                with self.assertRaises(sqlite3.OperationalError):
                    connection.execute("DELETE FROM bairros")
            finally:
                connection.close()

    def test_open_readonly_missing_file(self):
        with self.assertRaises(FileNotFoundError):
            open_readonly_connection(Path(tempfile.gettempdir()) / "nao-existe-cnefe.db")


class DiscoverFixtureTests(unittest.TestCase):
    def setUp(self):
        self._temporary = tempfile.TemporaryDirectory()
        self.directory = Path(self._temporary.name)
        self.sqlite_path = self.directory / "fixture.db"
        build_fixture(self.sqlite_path)
        self.output_dir = self.directory / "reports"

    def tearDown(self):
        self._temporary.cleanup()

    def _discover(self, **overrides):
        options = {
            "source_version": "CNEFE_TEST",
            "tolerance_m": 1.0,
            "chunk_size": 2,
            "low_sample_threshold": 5,
            "max_samples": 3,
            "generated_at_utc": "2026-01-01T00:00:00Z",
        }
        options.update(overrides)
        return discover(self.sqlite_path, **options)

    def test_inventory_and_counts(self):
        result = self._discover()
        schema = result.schema_report
        self.assertEqual(schema["source"]["file_name"], "fixture.db")
        self.assertTrue(schema["source"]["unchanged_after_run"])
        self.assertEqual(
            schema["source"]["sha256"], schema["source"]["sha256_before_run"]
        )
        tables = {table["name"]: table for table in schema["tables"]}
        self.assertEqual(tables["bairros"]["row_count"], 5)
        self.assertEqual(tables["ruas"]["row_count"], 7)
        self.assertEqual(tables["bairros"]["primary_key"], ["id"])
        self.assertEqual(
            [index["name"] for index in tables["bairros"]["indexes"]],
            ["idx_bairros_municipio"],
        )
        self.assertEqual(schema["format"]["encoding"], "UTF-8")
        self.assertEqual(len(schema["samples"]["bairros"]["rows"]), 5)

    def test_quality_checks(self):
        result = self._discover()
        quality = result.schema_report["quality"]
        centro = quality["ruas"]["columns"]["neighborhood_name"]
        self.assertEqual(centro["null"], 0)
        self.assertEqual(centro["empty"], 0)
        self.assertEqual(quality["ruas"]["coordinates"]["codes"][COORD_ZERO], 1)
        self.assertEqual(
            quality["ruas"]["coordinates"]["codes"][COORD_MISSING_PARTIAL], 1
        )
        self.assertEqual(quality["ruas"]["coordinates"]["valid"], 5)
        zip_codes = quality["ruas"]["zip_codes"]
        self.assertEqual(zip_codes["valid"], 3)
        self.assertEqual(zip_codes["repairable"], 1)
        self.assertEqual(zip_codes["invalid"], 1)
        self.assertEqual(zip_codes["empty"], 1)
        self.assertEqual(zip_codes["missing"], 1)
        self.assertEqual(
            quality["duplicates"]["streets_natural_key"]["duplicate_groups"], 1
        )
        self.assertEqual(quality["duplicates"]["streets_point_key"]["duplicate_groups"], 1)
        self.assertEqual(quality["duplicates"]["neighborhoods"]["duplicate_groups"], 0)
        self.assertEqual(quality["coverage"]["neighborhoods_without_points"], 2)

    def test_dimensions(self):
        result = self._discover()
        dimensions = result.schema_report["dimensions"]
        self.assertEqual(dimensions["uf"]["distinct"], 1)
        self.assertFalse(dimensions["uf"]["matches_expected"])
        self.assertEqual(dimensions["uf"]["format"], "ibge_state_code")
        self.assertEqual(dimensions["uf"]["values"][0]["uf_sigla"], "RO")
        self.assertEqual(dimensions["municipality"]["distinct_normalized"], 1)
        self.assertEqual(dimensions["municipality"]["prefix_mismatch_bairros"], 0)

    def test_centroid_validation(self):
        result = self._discover()
        centroid = result.centroid_report
        totals = centroid["totals"]
        self.assertEqual(totals["neighborhoods"], 5)
        self.assertEqual(totals["neighborhoods_compared"], 3)
        self.assertEqual(totals["neighborhoods_without_points"], 2)
        self.assertEqual(centroid["above_tolerance"]["count"], 1)
        self.assertEqual(len(centroid["above_tolerance"]["samples"]), 1)
        sample = centroid["above_tolerance"]["samples"][0]
        self.assertEqual(sample["bairro"], "ZONA RURAL")
        self.assertGreater(sample["error_m"], 1000.0)
        self.assertEqual(centroid["error_meters"]["min"], 0.0)
        self.assertEqual(centroid["method"]["provenance"], "materialized_with_divergences")
        self.assertEqual(centroid["method"]["tolerance_m"], 1.0)
        self.assertEqual(centroid["method"]["invalid_points_excluded"], 2)
        self.assertEqual(len(centroid["by_uf"]), 1)
        self.assertEqual(centroid["by_uf"][0]["above_tolerance"], 1)
        self.assertEqual(centroid["without_points"]["count"], 2)

        noronha = next(
            row for row in centroid["by_uf"] if row["uf_codigo"] == "11"
        )
        self.assertEqual(noronha["compared"], 3)

    def test_reports_are_written_and_deterministic(self):
        first = self._discover()
        written = write_reports(first, self.output_dir)
        self.assertEqual(
            sorted(written),
            ["centroid-validation.json", "schema-report.json", "schema-report.md"],
        )
        second = self._discover()
        self.assertEqual(
            first.schema_report["data_fingerprint"],
            second.schema_report["data_fingerprint"],
        )
        self.assertEqual(
            first.centroid_report["data_fingerprint"],
            second.centroid_report["data_fingerprint"],
        )
        self.assertEqual(
            first.schema_report["data_fingerprint"],
            first.centroid_report["data_fingerprint"],
        )

        payload = json.loads((self.output_dir / "schema-report.json").read_text("utf-8"))
        self.assertEqual(payload["data_fingerprint"], first.schema_report["data_fingerprint"])
        self.assertIn("performance", payload)
        centroid_payload = json.loads(
            (self.output_dir / "centroid-validation.json").read_text("utf-8")
        )
        self.assertEqual(centroid_payload["totals"]["neighborhoods"], 5)
        markdown = (self.output_dir / "schema-report.md").read_text("utf-8")
        self.assertTrue(markdown.startswith("# Descoberta e validacao do schema CNEFE"))
        self.assertIn("## 6. Validacao dos centroides materializados", markdown)

    def test_reports_use_lf_line_endings(self):
        result = self._discover()
        write_reports(result, self.output_dir)
        for name in ("schema-report.json", "centroid-validation.json", "schema-report.md"):
            raw = (self.output_dir / name).read_bytes()
            self.assertNotIn(b"\r\n", raw, name)

    def test_optional_zip_column_absent(self):
        path = self.directory / "sem_cep.db"
        build_fixture(path, include_zip=False)
        result = discover(
            path, chunk_size=2, generated_at_utc="2026-01-01T00:00:00Z"
        )
        self.assertIn(
            "zip_code", result.schema_report["mapping"]["missing_optional_streets"]
        )
        self.assertIsNone(result.schema_report["quality"]["ruas"]["zip_codes"])

    def test_missing_table_raises_mapping_error(self):
        path = self.directory / "sem_ruas.db"
        build_fixture(path, include_ruas=False)
        with self.assertRaises(SchemaMappingError):
            discover(path, chunk_size=2)

    def test_missing_required_column_raises_mapping_error(self):
        path = self.directory / "sem_coluna.db"
        build_fixture(path)
        connection = sqlite3.connect(str(path))
        try:
            connection.execute("ALTER TABLE ruas RENAME COLUMN latitude TO lat")
            connection.commit()
        finally:
            connection.close()
        with self.assertRaises(SchemaMappingError):
            discover(path, chunk_size=2)

    def test_cli_writes_reports(self):
        stdout = io.StringIO()
        stderr = io.StringIO()
        with redirect_stdout(stdout), redirect_stderr(stderr):
            exit_code = discovery.main(
                [
                    "--sqlite-path",
                    str(self.sqlite_path),
                    "--output-dir",
                    str(self.output_dir),
                    "--chunk-size",
                    "2",
                    "--source-version",
                    "CNEFE_TEST",
                ]
            )
        self.assertEqual(exit_code, 0)
        self.assertIn("Descoberta CNEFE concluida", stdout.getvalue())
        self.assertTrue((self.output_dir / "schema-report.json").is_file())
        self.assertTrue((self.output_dir / "schema-report.md").is_file())
        self.assertTrue((self.output_dir / "centroid-validation.json").is_file())

    def test_cli_dry_run_writes_nothing(self):
        stdout = io.StringIO()
        stderr = io.StringIO()
        with redirect_stdout(stdout), redirect_stderr(stderr):
            exit_code = discovery.main(
                [
                    "--sqlite-path",
                    str(self.sqlite_path),
                    "--output-dir",
                    str(self.output_dir),
                    "--chunk-size",
                    "2",
                    "--dry-run",
                ]
            )
        self.assertEqual(exit_code, 0)
        self.assertFalse(self.output_dir.exists())


if __name__ == "__main__":
    unittest.main()
