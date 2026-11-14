# Base geografica CNEFE - operacao

Runbook curto para importar, validar e reverter a base geografica derivada do CNEFE.
Nenhum comando aqui executa commit, push ou deploy, e nenhum deles escreve no arquivo
SQLite original.

## 1. Pre-requisitos

- Arquivo SQLite do CNEFE fora do Git (o artefato atual tem ~713 MB).
- Python 3.11+ (somente biblioteca padrao para a extracao).
- `psycopg2` apenas se for publicar no PostgreSQL.
- Snapshot oficial do IBGE Localidades carregado em `Cities` (codigo IBGE + UF + nome).
  Municipios ausentes abortam a publicacao com `UNKNOWN_CITY`; nada e inventado.
- Variaveis de ambiente (nomes; nunca versionar valores):

```
URBEAT_CNEFE_SQLITE_PATH
URBEAT_CNEFE_SOURCE_VERSION        # ex.: CNEFE_2022
URBEAT_CNEFE_CENTROID_TOLERANCE_M  # padrao 1
URBEAT_IMPORT_BATCH_SIZE           # padrao 50000
URBEAT_IMPORT_MAX_REJECT_RATE      # padrao 0.01
URBEAT_DB_HOST / URBEAT_DB_PORT / URBEAT_DB_NAME / URBEAT_DB_USER / URBEAT_DB_PASSWORD
```

## 2. Descoberta (obrigatoria antes da carga)

```powershell
python backend/scripts/import/cnefe_discover_schema.py `
  --sqlite-path $env:URBEAT_CNEFE_SQLITE_PATH `
  --output-dir imports/cnefe/reports
```

Gera `schema-report.md`, `schema-report.json` e `centroid-validation.json`. O script
abre o SQLite em `mode=ro` com `PRAGMA query_only=1` e recalcula o SHA-256 ao final para
provar que o arquivo nao mudou.

## 3. Numeros e invariantes esperados

| Item | Valor |
|---|---|
| `bairros` | 520.195 |
| `ruas` | 4.266.401 |
| UFs | 27 |
| Municipios (IBGE) | 5.570 |
| Bairros sem ponto | 0 |
| Desvio maximo centroide materializado x media dos pontos | ~0,08 m |

O importador falha em desvio de baseline (coordenada invalida, UF invalida, contagem que
nao reconcilia). Ele nunca preenche bairro com "Centro", nunca usa a primeira rua e nunca
aplica fator 1.3.

## 4. Importacao de uma UF (staging, sem publicar)

```powershell
python backend/scripts/import/cnefe_import.py `
  --sqlite-path $env:URBEAT_CNEFE_SQLITE_PATH `
  --source-version $env:URBEAT_CNEFE_SOURCE_VERSION `
  --uf SP `
  --output-dir imports/cnefe
```

Saidas:

```
imports/cnefe/staging/sp/neighborhoods.csv
imports/cnefe/staging/sp/streets.csv
imports/cnefe/staging/sp/street_zipcodes.csv
imports/cnefe/staging/sp/errors.csv
imports/cnefe/reports/sp-report.md
imports/cnefe/reports/centroid-validation.md
imports/cnefe/reports/curation-localities.csv
imports/cnefe/manifests/<source-version>.json
```

`--dry-run` valida e imprime contagens sem gravar nada. `--max-reject-rate` aborta a UF
quando a taxa de rejeicao excede o limite.

## 5. Publicacao

A publicacao e opcional e explicita. Requer `psycopg2` e as variaveis `URBEAT_DB_*`
(usuario com escrita apenas nas tabelas de importacao/geograficas):

```powershell
python backend/scripts/import/cnefe_import.py --uf SP --publish
```

Comportamento:

- transacao por UF com `pg_advisory_xact_lock`;
- `COPY` para staging temporario e upsert por chave natural;
- `DeliveryNeighborhoods` oficial e atualizado somente quando `Kind <> 'manual'`;
- `ImportDatasets` troca o ponteiro ativo da UF de forma atomica;
- `ImportRuns` registra contagens, checksum e metadados;
- o script SQL equivalente e gravado em `staging/sp/publish.sql` para revisao.

> Pendencia operacional: validar a publicacao (`--publish`) em homologacao com
> PostgreSQL/Testcontainers antes da primeira carga real. Neste ambiente nao havia
> `psycopg2` nem Docker disponivel, portanto o caminho foi implementado e testado
> apenas na geracao de SQL e no staging.

## 6. Reconciliacao e rollback

- `ImportDatasets` guarda a versao ativa por UF.
- Rollback = desativar a versao atual, reativar a anterior e registrar o motivo em
  `ImportRuns.RollbackReason`. Nenhuma linha e apagada.
- `ImportErrors` mantem toda linha rejeitada com codigo estavel (`INVALID_ZIP`,
  `COORD_CENTROID_MISMATCH`, `PENDING_NEIGHBORHOOD`, ...).
- Antes de remover uma versao antiga, verificar `StoreDeliveryAreas` e caches Redis.

## 7. Redis (indice derivado)

O indice geoespacial e opcional e sempre derivado do PostgreSQL:

- chaves: `urbeat:geo:{datasetVersion}:neighborhoods[:uf]`, meta em `...:meta`;
- cache de CEP: `urbeat:addr:{datasetVersion}:cep:{cep}` (TTL 30 dias; negativo 5 min);
- reconstrucao: `INeighborhoodGeoIndex.RebuildAsync(datasetVersion, uf)`;
- reconciliacao: `ReconcileAsync` compara `ZCARD` com a contagem do PostgreSQL;
- sem Redis, `NullNeighborhoodGeoIndex` e usado e toda consulta cai no PostgreSQL.

O caminho de requisicao (raio, CEP) continua autoritativo no PostgreSQL nesta fase; o
indice e acelerador e pode ser apagado/reconstruido sem perda de verdade.

## 8. Migracao de `StoreDeliveryAreas`

- Fase A (aplicada): colunas `NeighborhoodId`, `DistanceKm`, `DistanceMethod`; a coluna
  textual `Neighborhood` permanece.
- Backfill automatico na migracao: cidade do endereco da loja + nome normalizado.
  Vinculos sem correspondencia vao para `StoreDeliveryAreaMigrationIssues` com motivo;
  nada e apagado.
- Escrita nova grava nome e identifier; leitura prefere o identifier e usa o nome apenas
  como fallback.
- Fase B (pendente): exigir 100% resolvido/quarentenado e remover a coluna textual em
  migracao dedicada (breaking change anunciado).

## 9. Testes

```powershell
python backend/scripts/import/test_cnefe_discover_schema.py
python -m unittest discover -s backend/scripts/import -p "test_*.py"
dotnet build backend/Urbeat.sln
dotnet test backend/tests/Urbeat.UnitTests
dotnet test backend/tests/Urbeat.IntegrationTests
```

Os testes relacionais usam Testcontainers PostgreSQL e se auto-ignoram quando Docker nao
esta disponivel.
