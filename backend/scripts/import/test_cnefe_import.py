"""Testes da importacao CNEFE (funcoes puras, fixtures e staging).

Os testes usam bancos SQLite temporarios; o artefato real
``imports/cnefe/delivery_brasil.db`` nunca e aberto e nunca e escrito.

Execucao (a partir de ``backend/scripts/import``):
    python -m unittest test_cnefe_import -v
"""

import csv
import json
import sqlite3
import tempfile
import unittest
from pathlib import Path

import cnefe_import as importer
from cnefe_import import (
    COORD_CENTROID_MISMATCH,
    DUPLICATE_ROW,
    INVALID_IBGE,
    INVALID_ZIP,
    LOW_SAMPLE,
    MISSING_COORD,
    PENDING_NEIGHBORHOOD,
    build_publish_statements,
    classify_locality,
    compose_display_name,
    compute_source_row_hash,
    extract,
    main,
    normalize_name,
    normalize_zip_code,
    write_centroid_report,
    write_curation_report,
    write_manifest,
    write_staging,
    write_uf_report,
)


def build_fixture(path: Path) -> None:
    """Artefato minimo com o mesmo layout confirmado de ``bairros`` e ``ruas``."""
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
            CREATE TABLE ruas (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                uf_codigo TEXT,
                municipio_codigo TEXT,
                bairro TEXT,
                logradouro TEXT,
                cep TEXT,
                latitude REAL,
                longitude REAL
            )
            """
        )
        connection.executemany(
            "INSERT INTO bairros (uf_codigo, municipio_codigo, nome, latitude_central, longitude_central)"
            " VALUES (?, ?, ?, ?, ?)",
            [
                ("35", "3550308", "BELA VISTA", -23.5, -46.6),
                ("35", "3550308", "CENTRO", -23.6, -46.7),
                ("35", "3550308", "SEM PONTO", None, None),
                ("35", "3550308", "DIVERGENTE", -23.0, -46.0),
                ("35", "3550308", "ALDEIA INDIGENA X", -23.7, -46.8),
            ],
        )
        connection.executemany(
            "INSERT INTO ruas (uf_codigo, municipio_codigo, bairro, logradouro, cep, latitude, longitude)"
            " VALUES (?, ?, ?, ?, ?, ?, ?)",
            [
                ("35", "3550308", "BELA VISTA", "AVENIDA PAULISTA", "01310100", -23.5, -46.6),
                ("35", "3550308", "BELA VISTA", "AVENIDA PAULISTA", "01310101", -23.5, -46.6),
                ("35", "3550308", "BELA VISTA", "AVENIDA PAULISTA", "01310100", -23.5, -46.6),
                ("35", "3550308", "CENTRO", "RUA A", "123", -23.6, -46.7),
                ("35", "3550308", "DIVERGENTE", "RUA B", "01001000", -23.2, -46.2),
                ("35", "3550308", "SEM PONTO", "RUA C", "", None, None),
                ("35", "3550308", "ALDEIA INDIGENA X", "RUA D", "01001000", -23.7, -46.8),
                ("35", "35503", "OUTRO", "RUA E", "01001000", -23.7, -46.8),
                ("35", "9999999", "OUTRO", "RUA F", "01001000", -23.7, -46.8),
            ],
        )
        connection.commit()
    finally:
        connection.close()


class PureFunctionTests(unittest.TestCase):
    def test_normalize_name_matches_the_dotnet_contract(self):
        cases = {
            "  Bela Vista ": "bela vista",
            "BELA VISTA": "bela vista",
            "Bela  Vista": "bela vista",
            "Bela-Vista": "bela vista",
            "Bela/Vista": "bela vista",
            "José Bonifácio": "jose bonifacio",
            "Açaí": "acai",
            "D'Ávila": "d avila",
            "Vila Sésamo (Setor 2)": "vila sesamo setor 2",
            None: "",
            "---": "",
        }
        for value, expected in cases.items():
            with self.subTest(value=value):
                self.assertEqual(normalize_name(value), expected)

    def test_normalize_zip_code(self):
        self.assertEqual(normalize_zip_code("01310-100"), "01310100")
        self.assertEqual(normalize_zip_code(" 01310 100 "), "01310100")
        self.assertIsNone(normalize_zip_code("123"))
        self.assertIsNone(normalize_zip_code("00000000"))
        self.assertIsNone(normalize_zip_code(""))
        self.assertIsNone(normalize_zip_code(None))

    def test_compose_display_name_never_invents_parts(self):
        self.assertEqual(compose_display_name(None, None, "  Rua   Sem  Tipo "), "Rua Sem Tipo")
        self.assertEqual(compose_display_name("Avenida", None, "Paulista"), "Avenida Paulista")

    def test_source_row_hash_is_stable_and_matches_dotnet(self):
        first = compute_source_row_hash(
            "35", "3550308", "Bela Vista", "Avenida Paulista", "01310100", -23.5613991, -46.6558819
        )
        second = compute_source_row_hash(
            "35", "3550308", "Bela Vista", "Avenida Paulista", "01310100", -23.5613991, -46.6558819
        )
        different = compute_source_row_hash(
            "35", "3550308", "Bela Vista", "Avenida Paulista", "01310100", -23.5613992, -46.6558819
        )

        self.assertEqual(first, second)
        self.assertNotEqual(first, different)
        # Valor compartilhado com DeliveryGeoMathTests/NeighborhoodNameTests no .NET.
        self.assertEqual(first, "3fb876390ab24bb96bde463f583cbc4d00c68cc6c6d6c376bb3dd8f9be4662c5")

    def test_classify_locality_is_auxiliary_only(self):
        self.assertEqual(classify_locality("ALDEIA INDIGENA X"), "indigenous_village")
        self.assertEqual(classify_locality("FAZENDA SANTA RITA"), "rural_locality")
        self.assertEqual(classify_locality("Centro"), "urban_neighborhood")
        self.assertEqual(classify_locality("Jardim das Flores"), "urban_neighborhood")
        self.assertEqual(classify_locality("Zona 7"), "unknown")


class ExtractionTests(unittest.TestCase):
    def setUp(self):
        self._temporary = tempfile.TemporaryDirectory()
        self.directory = Path(self._temporary.name)
        self.sqlite_path = self.directory / "fixture.db"
        build_fixture(self.sqlite_path)

    def tearDown(self):
        self._temporary.cleanup()

    def _extract(self, **overrides):
        options = {
            "ufs": ["SP"],
            "source_version": "CNEFE_TEST",
            "batch_size": 2,
        }
        options.update(overrides)
        return extract(self.sqlite_path, **options)

    def test_counts_and_reconciliation(self):
        result = self._extract()["35"]

        self.assertEqual(len(result["neighborhoods"]), 5)
        self.assertEqual(result["rows_read"], 9)
        self.assertEqual(result["rows_rejected"], 2)  # IBGE invalido + prefixo divergente
        self.assertEqual(result["rows_skipped"], 1)  # ponto duplicado (mesmo hash)
        self.assertEqual(result["rows_published"], 6)
        self.assertTrue(result["reconciles"])

        codes = [error.code for error in result["errors"]]
        self.assertIn(INVALID_IBGE, codes)
        self.assertIn(importer.UF_IBGE_MISMATCH, codes)
        self.assertIn(DUPLICATE_ROW, codes)
        self.assertIn(INVALID_ZIP, codes)
        self.assertIn(MISSING_COORD, codes)

    def test_centroid_validation(self):
        result = self._extract()["35"]
        statuses = {
            row["neighborhood"]: (row["coordinate_status"], row["coordinate_point_count"])
            for row in result["neighborhoods"]
        }

        self.assertEqual(statuses["BELA VISTA"], ("computed", 2))
        self.assertEqual(statuses["CENTRO"], ("computed", 1))
        self.assertEqual(statuses["SEM PONTO"], ("pending", 0))
        self.assertEqual(statuses["DIVERGENTE"], ("pending", 1))

        mismatch = [error for error in result["errors"] if error.code == COORD_CENTROID_MISMATCH]
        self.assertEqual(len(mismatch), 1)
        self.assertEqual(mismatch[0].source_row_id, "3550308:DIVERGENTE")

        low_sample = [error for error in result["errors"] if error.code == LOW_SAMPLE]
        self.assertEqual(len(low_sample), 4)  # Bela Vista, Centro, Divergente e Aldeia (menos de 5 pontos)

    def test_centroid_uses_materialized_value_and_not_first_street(self):
        result = self._extract()["35"]
        centro = next(row for row in result["neighborhoods"] if row["neighborhood"] == "CENTRO")

        self.assertEqual(centro["latitude"], "-23.6000000")
        self.assertEqual(centro["longitude"], "-46.7000000")
        self.assertEqual(centro["coordinate_method"], "ADDRESS_POINT_MEAN")
        self.assertEqual(centro["coordinate_source_version"], "CNEFE_TEST")
        self.assertEqual(centro["kind"], "official_cnefe")
        self.assertEqual(centro["representative_method"], "nearest_to_centroid_v1")

    def test_pending_neighborhood_and_zip_staging(self):
        result = self._extract()["35"]
        zip_codes = {(row["zip_code"], row["is_primary"]) for row in result["zip_codes"]}
        self.assertIn(("01310100", True), zip_codes)

        paulista = [row for row in result["streets"] if row["street"] == "AVENIDA PAULISTA"]
        self.assertEqual(len(paulista), 1)
        self.assertEqual(paulista[0]["address_point_count"], 2)
        self.assertEqual(paulista[0]["latitude"], "-23.5000000")

    def test_uf_filter_ignores_other_states(self):
        results = self._extract(ufs=[" 35 "])
        self.assertIn("35", results)
        self.assertNotIn("99", results)

    def test_invalid_uf_filter(self):
        with self.assertRaises(ValueError):
            self._extract(ufs=["XX"])


class OutputTests(unittest.TestCase):
    def setUp(self):
        self._temporary = tempfile.TemporaryDirectory()
        self.directory = Path(self._temporary.name)
        self.sqlite_path = self.directory / "fixture.db"
        build_fixture(self.sqlite_path)
        self.output_dir = self.directory / "out"

    def tearDown(self):
        self._temporary.cleanup()

    def test_staging_reports_and_manifest(self):
        results = extract(self.sqlite_path, ["SP"], "CNEFE_TEST", batch_size=2)
        result = results["35"]
        counts = write_staging(self.output_dir, "35", result, result["errors"])

        self.assertEqual(counts["neighborhoods"], 5)
        self.assertEqual(counts["streets"], 5)
        self.assertGreaterEqual(counts["zip_codes"], 2)

        with (self.output_dir / "staging" / "35" / "neighborhoods.csv").open(
            encoding="utf-8", newline=""
        ) as handle:
            reader = csv.DictReader(handle)
            self.assertEqual(reader.fieldnames, list(importer.NEIGHBORHOOD_COLUMNS))
            rows = list(reader)
        self.assertEqual(len(rows), 5)
        self.assertEqual(rows[0]["kind"], "official_cnefe")

        for name in ("neighborhoods.csv", "streets.csv", "street_zipcodes.csv", "errors.csv"):
            raw = (self.output_dir / "staging" / "35" / name).read_bytes()
            self.assertNotIn(b"\r\n", raw, name)

        write_uf_report(
            self.output_dir / "reports", "35", "CNEFE_TEST", result, result["counts"], result["errors"], 0.1, 1.0
        )
        write_centroid_report(self.output_dir / "reports", results)
        write_curation_report(self.output_dir / "reports", results)
        manifest = write_manifest(
            self.output_dir / "manifests",
            "CNEFE_TEST",
            self.sqlite_path,
            "0" * 64,
            ["35"],
            results,
            1.0,
            "mapping-v1",
        )

        payload = json.loads(manifest.read_text("utf-8"))
        self.assertEqual(payload["sha256"], "0" * 64)
        self.assertEqual(payload["totals"]["35"]["neighborhoods"], 5)
        self.assertTrue((self.output_dir / "reports" / "centroid-validation.md").is_file())
        curation = (self.output_dir / "reports" / "curation-localities.csv").read_text("utf-8")
        self.assertIn("ALDEIA INDIGENA X", curation)

    def test_cli_dry_run_writes_nothing(self):
        exit_code = main(
            [
                "--sqlite-path",
                str(self.sqlite_path),
                "--output-dir",
                str(self.output_dir),
                "--uf",
                "SP",
                "--batch-size",
                "2",
                "--dry-run",
            ]
        )
        self.assertEqual(exit_code, 0)
        self.assertFalse(self.output_dir.exists())

    def test_cli_writes_outputs(self):
        exit_code = main(
            [
                "--sqlite-path",
                str(self.sqlite_path),
                "--output-dir",
                str(self.output_dir),
                "--uf",
                "SP",
                "--source-version",
                "CNEFE_TEST",
                "--batch-size",
                "2",
                "--max-reject-rate",
                "0.5",
            ]
        )
        self.assertEqual(exit_code, 0)
        self.assertTrue((self.output_dir / "manifests" / "CNEFE_TEST.json").is_file())
        self.assertTrue((self.output_dir / "reports" / "centroid-validation.md").is_file())
        self.assertTrue((self.output_dir / "reports" / "35-report.md").is_file())

    def test_cli_aborts_when_reject_rate_is_exceeded(self):
        with self.assertRaises(importer.ImportAborted):
            main(
                [
                    "--sqlite-path",
                    str(self.sqlite_path),
                    "--output-dir",
                    str(self.output_dir),
                    "--uf",
                    "SP",
                    "--batch-size",
                    "2",
                    "--max-reject-rate",
                    "0",
                ]
            )


    def test_cli_aborts_on_baseline_divergence_when_strict(self):
        with self.assertRaises(importer.ImportAborted) as error:
            main(
                [
                    "--sqlite-path",
                    str(self.sqlite_path),
                    "--output-dir",
                    str(self.output_dir),
                    "--batch-size",
                    "2",
                    "--strict-baseline",
                ]
            )

        str(error.exception).Should if False else None
        self.assertIn("baseline", str(error.exception).lower())
        self.assertFalse(self.output_dir.exists())


class PublishScriptTests(unittest.TestCase):
    def test_publish_statements_are_idempotent_and_scoped(self):
        statements = build_publish_statements("CNEFE_2022", "SP", "018f0000-0000-7000-8000-000000000000")
        joined = "\n".join(statements)

        self.assertIn('CREATE TEMP TABLE stg_neighborhoods', joined)
        self.assertIn('ON CONFLICT ("CityId", "NormalizedName")', joined)
        self.assertIn('ON CONFLICT ("SourceVersion", "Uf")', joined)
        self.assertNotIn("TRUNCATE", joined.upper())
        self.assertNotIn("DELETE FROM", joined.upper())
        # A ativacao e sempre por UF; nao existe publicacao global implicita.
        self.assertIn("WHERE \"Uf\" = 'SP'", joined)

    def test_publish_statements_reject_unsafe_values(self):
        with self.assertRaises(ValueError):
            build_publish_statements("CNEFE_2022'; DROP TABLE x; --", "SP", "run")


if __name__ == "__main__":
    unittest.main()
