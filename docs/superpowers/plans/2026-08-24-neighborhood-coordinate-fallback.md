# Fallback de Coordenadas por Primeira Rua Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Preencher bairros sem coordenadas usando a primeira rua alfabeticamente encontrada no e-DNE/CEP, aplicar fallbacks seguros e regenerar o snapshot CSV da UF apos o commit.

**Architecture:** O reparo permanece no fluxo Python de importacao existente. O e-DNE local fornece a primeira rua e seu CEP em ordem alfabetica; Brasil Aberto converte o CEP em coordenadas; Nominatim continua como fallback textual do bairro. A rotina exporta o estado final do banco para `backend/scripts/import/snapshots/bairros_<uf>.csv` somente depois das atualizacoes.

**Tech Stack:** Python 3, PostgreSQL/psycopg2, e-DNE SQLite, Brasil Aberto API, Nominatim, unittest.

## Global Constraints

- Nunca substituir coordenadas existentes.
- Latitude e longitude devem ser gravadas como par; pares parciais ou fora dos limites sao invalidos.
- Nunca usar centroide municipal ou coordenadas inventadas.
- Respeitar os clientes externos e o `User-Agent` existente.
- Restauracao de CSV continua sem consultas externas e preserva campos vazios.
- O arquivo final deve ser `backend/scripts/import/snapshots/bairros_<uf>.csv`.

---

### Task 1: Adicionar selecao deterministica da primeira rua no e-DNE

**Files:**
- Modify: `backend/scripts/import/geocode_via_cep.py:20-39`
- Test: `backend/scripts/import/test_import_scripts.py`

**Interfaces:**
- Produces `get_first_street_from_dne(neighborhood, city) -> tuple[str | None, str | None]`, retornando `(logradouro, cep)` ou `(None, None)`.
- Mantem `get_cep_from_dne` como wrapper de compatibilidade, retornando somente o CEP.

- [ ] **Step 1: Write the failing tests**

Adicionar testes que configurem uma conexao SQLite temporaria com `cep_unificado`, insiram duas ruas do mesmo bairro fora de ordem e verifiquem que a rua alfabeticamente menor e seu CEP sao retornados. Adicionar casos sem linha e com logradouro vazio.

```python
def test_first_dne_street_is_alphabetical(self):
    street, cep = get_first_street_from_dne("Centro", "Niteroi")
    self.assertEqual(street, "Avenida Brasil")
    self.assertEqual(cep, "24000000")
```

- [ ] **Step 2: Run test to verify it fails**

Run: `python -m unittest backend/scripts/import/test_import_scripts.py -v`
Expected: FAIL because `get_first_street_from_dne` does not exist.

- [ ] **Step 3: Implement the minimal query**

Implement a parameterized SQLite query:

```python
SELECT logradouro, cep
FROM cep_unificado
WHERE bairro = ? AND municipio = ?
  AND logradouro IS NOT NULL AND trim(logradouro) <> ''
  AND cep IS NOT NULL AND trim(cep) <> ''
ORDER BY lower(logradouro), logradouro
LIMIT 1
```

Normalize the returned CEP to digits before returning it. Make `get_cep_from_dne` call this helper and return only its second value.

- [ ] **Step 4: Run focused tests**

Run: `python -m unittest backend/scripts/import/test_import_scripts.py -v`
Expected: PASS, including existing snapshot tests.

- [ ] **Step 5: Review the query**

Confirm that the query is parameterized, ignores incomplete rows, and does not modify the DNE database.

### Task 2: Integrar primeira rua, CEP e fallback Nominatim

**Files:**
- Modify: `backend/scripts/import/geocode_via_cep.py:42-118`
- Test: `backend/scripts/import/test_import_scripts.py`

**Interfaces:**
- Consumes `get_first_street_from_dne` from Task 1.
- Keeps `get_coordinates_from_cep(cep)` and `get_coordinates_from_nominatim(neighborhood, city, uf)` as source adapters.
- Produces the same `(latitude, longitude, source)` update contract used by `geocode_uf`.

- [ ] **Step 1: Write failing source-priority tests**

Mock the DNE and HTTP adapters and cover this order:

```python
def test_geocode_prefers_first_alphabetical_street_cep(self):
    # DNE returns the alphabetically first street and Brasil Aberto returns coordinates.
    # Assert that the database receives those coordinates and source brasil_aberto_first_street.
    self.assertEqual(updated_row["Latitude"], -22.9)
    self.assertEqual(updated_row["Longitude"], -43.1)
    self.assertEqual(updated_row["Source"], "brasil_aberto_first_street")

def test_geocode_uses_nominatim_when_first_street_has_no_coordinates(self):
    # Assert Nominatim is called only after the street CEP path returns no valid pair.
    self.assertEqual(updated_row["Source"], "osm_nominatim")
    self.assertEqual(nominatim_mock.call_count, 1)
```

The test implementation must use the existing mocks/fixtures in `test_import_scripts.py`, not real network calls.

- [ ] **Step 2: Run focused tests to verify failure**

Run: `python -m unittest backend/scripts/import/test_import_scripts.py -v`
Expected: FAIL on the new source-priority assertions.

- [ ] **Step 3: Implement the source order**

For each pending neighborhood:

1. Call `get_first_street_from_dne(neighborhood, city)`.
2. If a CEP exists, call `get_coordinates_from_cep`.
3. Accept it only when `valid_coordinates` returns true for both values.
4. Set source to `brasil_aberto_first_street`.
5. If that path fails or returns no valid pair, call Nominatim and set source to `osm_nominatim` only for a valid pair.
6. If both fail, leave the row unchanged and count it as pending.

Use the existing `COALESCE` update guard so a late fallback cannot overwrite an existing coordinate pair.

- [ ] **Step 4: Run focused tests**

Run: `python -m unittest backend/scripts/import/test_import_scripts.py -v`
Expected: PASS.

- [ ] **Step 5: Run static checks**

Run: `python -m py_compile backend/scripts/import/geocode_via_cep.py backend/scripts/import/neighborhood_snapshot.py`
Expected: no output and exit code 0.

### Task 3: Garantir export seguro do snapshot apos o commit

**Files:**
- Modify: `backend/scripts/import/geocode_via_cep.py:115-138`
- Modify: `backend/scripts/import/brasil_aberto_import.py:66-79`
- Modify: `backend/scripts/import/neighborhood_snapshot.py:60-67` only if atomic replacement is required
- Test: `backend/scripts/import/test_import_scripts.py`

**Interfaces:**
- Consumes `snapshot_path(uf)` from `import_common.py`.
- Produces `snapshots/bairros_<uf>.csv` containing the final database state.

- [ ] **Step 1: Write failing export timing test**

Add a test that runs the geocoding flow with a mocked database, verifies the update is committed before `export_snapshot` is called, and verifies the target is `snapshot_path("RJ")`.

```python
def test_geocode_exports_snapshot_after_commit(self):
    # Assert call order: commit -> export_snapshot(connection, "RJ", snapshot_path("RJ"))
    self.assertEqual(call_order, ["commit", "export"])
    self.assertEqual(exported_path.name, "bairros_rj.csv")
```

- [ ] **Step 2: Run the test to verify failure**

Run: `python -m unittest backend/scripts/import/test_import_scripts.py -v`
Expected: FAIL if export is observed before commit or with a different path.

- [ ] **Step 3: Implement final-state export**

Keep the existing export behavior for owned connections and make the shared-connection import path export exactly once after `geocode_uf` has committed its updates. Do not export before the database transaction is complete. Update `write_snapshot` to write to `path.with_suffix(path.suffix + ".tmp")`, flush and close the temporary file, validate its contents with `read_snapshot`, and replace the destination with `Path.replace`; remove the temporary file on failure.

- [ ] **Step 4: Run snapshot tests**

Run: `python -m unittest backend/scripts/import/test_import_scripts.py -v`
Expected: PASS, including empty coordinate preservation, partial-coordinate rejection, idempotent restore, and the new export-order test.

- [ ] **Step 5: Validate the RJ snapshot path**

Run from `backend/scripts/import/`:

```bash
python -c "from import_common import snapshot_path; print(snapshot_path('RJ'))"
```

Expected: `.../backend/scripts/import/snapshots/bairros_rj.csv`.

### Task 4: Atualizar documentacao operacional

**Files:**
- Modify: `Documentacao/Backend/PopulacaoBairrosRJ.md:7-24,56-73`
- Modify: `DEPLOY.md:234-253`
- Modify: `docs/superpowers/specs/2026-08-24-neighborhood-coordinate-fallback-design.md` only if implementation details differ from the approved design

- [ ] **Step 1: Document the source priority**

State that the DNE first street is selected alphabetically, its CEP is converted by Brasil Aberto, Nominatim is fallback, and unresolved pairs remain empty.

- [ ] **Step 2: Document snapshot regeneration**

State that every completed import or repair exports to `backend/scripts/import/snapshots/bairros_<uf>.csv` after the database commit, and that restore never calls external APIs.

- [ ] **Step 3: Run documentation consistency checks**

Search for stale statements that claim only Nominatim is used or omit the snapshot regeneration rule:

```bash
rg "Nominatim|primeira rua|snapshot|bairros_<uf>" Documentacao/Backend/PopulacaoBairrosRJ.md DEPLOY.md docs/superpowers/specs/2026-08-24-neighborhood-coordinate-fallback-design.md
```

Expected: all three documents describe the same source order and output path.

### Task 5: Validacao final

**Files:**
- Test: `backend/scripts/import/test_import_scripts.py`

- [ ] **Step 1: Run all import-script tests**

Run: `python -m unittest discover -s backend/scripts/import -p "test_*.py" -v`
Expected: all tests pass.

- [ ] **Step 2: Verify no partial coordinates in the RJ snapshot**

Run a Python validation using `read_snapshot` against `backend/scripts/import/snapshots/bairros_rj.csv`.
Expected: no exception; pending rows have both coordinate fields empty.

- [ ] **Step 3: Review the final diff**

Run: `git diff -- backend/scripts/import Documentacao/Backend/PopulacaoBairrosRJ.md DEPLOY.md docs/superpowers/specs/2026-08-24-neighborhood-coordinate-fallback-design.md docs/superpowers/plans/2026-08-24-neighborhood-coordinate-fallback.md`
Expected: only the coordinate fallback, snapshot export, tests, and related documentation are changed.
