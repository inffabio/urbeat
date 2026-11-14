# Descoberta e validacao do schema CNEFE (somente leitura)

- **Especificacao:** `imports/cnefe/prompt.txt v2.1 (secoes 3.2, 3.3 e 10)`
- **Gerado em (UTC):** `2026-11-14T02:34:26Z`
- **Script:** `backend/scripts/import/cnefe_discover_schema.py`
- **Modo de acesso:** read-only (mode=ro, PRAGMA query_only=1)
- **Versao da fonte:** `CNEFE_2022`
- **Fingerprint dos dados:** `f2738fcf0643742c73736d0488fd873fdc8e5f20ee3922a47fa0879779eca1c1`
- **Fingerprint exclui:** timestamps de execucao e metricas de performance; os demais valores sao deterministicos.

## 1. Arquivo de origem

| Propriedade | Valor |
|---|---|
| Arquivo | `delivery_brasil.db` |
| Caminho logico | `imports/cnefe/delivery_brasil.db` |
| Tamanho | 713.089.024 bytes (680.1 MiB) |
| Modificado em (UTC) | 2026-11-14T01:33:04Z |
| Encoding declarado | UTF-8 |
| Page size | 4096 |
| Page count | 174094 |
| Journal mode | delete |
| SHA-256 (antes) | `b935dfaaaa3bed2759f77961fce9283fabffb32da9cd9537a8c444410a121f8a` |
| SHA-256 (depois) | `b935dfaaaa3bed2759f77961fce9283fabffb32da9cd9537a8c444410a121f8a` |
| Arquivo inalterado | **OK** |

Nenhum `INSERT`, `UPDATE`, `DELETE`, `VACUUM`, `CREATE` ou `DROP` foi executado. A conexao usa `mode=ro` e `PRAGMA query_only=1`, e o checksum foi confirmado identico antes e depois da execucao.

## 2. Inventario de schema

### 2.1 Tabelas

| Tabela | Linhas | Colunas | Chave primaria | Indices |
|---|---|---|---|---|
| bairros | 520.195 | 6 | id | idx_bairros_municipio |
| ruas | 4.266.401 | 8 | id | idx_ruas_logradouro, idx_ruas_municipio_bairro, idx_ruas_cep |
| sqlite_sequence | 2 | 2 | n/d | n/d |

Nenhuma view encontrada em `sqlite_master`.

### 2.2 Colunas

**`bairros`**

| # | Coluna | Tipo | NOT NULL | Default | PK |
|---|---|---|---|---|---|
| 0 | id | INTEGER | nao | n/d | 1 |
| 1 | uf_codigo | TEXT | nao | n/d | n/d |
| 2 | municipio_codigo | TEXT | nao | n/d | n/d |
| 3 | nome | TEXT | nao | n/d | n/d |
| 4 | latitude_central | REAL | nao | n/d | n/d |
| 5 | longitude_central | REAL | nao | n/d | n/d |

**`ruas`**

| # | Coluna | Tipo | NOT NULL | Default | PK |
|---|---|---|---|---|---|
| 0 | id | INTEGER | nao | n/d | 1 |
| 1 | uf_codigo | TEXT | nao | n/d | n/d |
| 2 | municipio_codigo | TEXT | nao | n/d | n/d |
| 3 | bairro | TEXT | nao | n/d | n/d |
| 4 | logradouro | TEXT | nao | n/d | n/d |
| 5 | cep | TEXT | nao | n/d | n/d |
| 6 | latitude | REAL | nao | n/d | n/d |
| 7 | longitude | REAL | nao | n/d | n/d |

**`sqlite_sequence`**

| # | Coluna | Tipo | NOT NULL | Default | PK |
|---|---|---|---|---|---|
| 0 | name | (sem tipo) | nao | n/d | n/d |
| 1 | seq | (sem tipo) | nao | n/d | n/d |

> Tabela interna do SQLite (AUTOINCREMENT). Nao representa identidade de negocio e nao deve ser importada nem reaproveitada.

### 2.3 Indices e seletividade

| # Indice | Tabela | Colunas | Unico | Valores distintos |
|---|---|---|---|---|
| idx_bairros_municipio | bairros | municipio_codigo | nao | 5.570 |
| idx_ruas_logradouro | ruas | logradouro | nao | 1.602.298 |
| idx_ruas_municipio_bairro | ruas | municipio_codigo, bairro | nao | 520.195 |
| idx_ruas_cep | ruas | cep | nao | 931.499 |

- `idx_bairros_municipio`: `CREATE INDEX idx_bairros_municipio ON bairros(municipio_codigo)`
- `idx_ruas_logradouro`: `CREATE INDEX idx_ruas_logradouro ON ruas(logradouro)`
- `idx_ruas_municipio_bairro`: `CREATE INDEX idx_ruas_municipio_bairro ON ruas(municipio_codigo, bairro)`
- `idx_ruas_cep`: `CREATE INDEX idx_ruas_cep ON ruas(cep)`

### 2.4 Mapeamento confirmado

| Campo canonico | Tabela bairros | Tabela ruas |
|---|---|---|
| latitude | n/d | latitude |
| locality | nome | n/d |
| longitude | n/d | longitude |
| municipality_ibge_code | municipio_codigo | municipio_codigo |
| neighborhood_centroid_latitude | latitude_central | n/d |
| neighborhood_centroid_longitude | longitude_central | n/d |
| neighborhood_name | n/d | bairro |
| primary_key | id | id |
| street_name | n/d | logradouro |
| uf | uf_codigo | uf_codigo |
| zip_code | n/d | cep |

Todos os campos opcionais do mapeamento estao presentes no artefato.

## 3. Amostras (limitadas a 20 linhas por tabela)

**`bairros`**

| id | uf_codigo | municipio_codigo | nome | latitude_central | longitude_central |
|---|---|---|---|---|---|
| 1 | 11 | 1100015 | ALDEIA INDIGENA AEKA | -12.075615 | -62.580233 |
| 2 | 11 | 1100015 | ALDEIA INDIGENA BANANEIRA | -12.179382 | -62.588131 |
| 3 | 11 | 1100015 | ALDEIA INDIGENA BARRANCO ALTO | -12.047012 | -62.543873 |
| 4 | 11 | 1100015 | ALDEIA INDIGENA BARREIRA DE ARARA | -12.05792 | -62.544296 |
| 5 | 11 | 1100015 | ALDEIA INDIGENA BOA ESPERANCA | -12.066348 | -62.569913 |
| 6 | 11 | 1100015 | ALDEIA INDIGENA BOM JESUS 1 | -11.996169 | -62.440914 |
| 7 | 11 | 1100015 | ALDEIA INDIGENA BOM JESUS 2 | -12.002891 | -62.462329 |
| 8 | 11 | 1100015 | ALDEIA INDIGENA BOM SOSSEGO | -12.170102 | -62.589543 |
| 9 | 11 | 1100015 | ALDEIA INDIGENA CASTANHEIRA | -12.138213 | -62.582945 |
| 10 | 11 | 1100015 | ALDEIA INDIGENA JATOBA | -12.199235 | -62.603408 |
| 11 | 11 | 1100015 | ALDEIA INDIGENA MARCOS | -12.034057 | -62.529527 |
| 12 | 11 | 1100015 | ALDEIA INDIGENA MATO GROSSO | -12.003056 | -62.476775 |
| 13 | 11 | 1100015 | ALDEIA INDIGENA MOAM | -12.117642 | -62.586342 |
| 14 | 11 | 1100015 | ALDEIA INDIGENA MONTE CRISTO | -12.123225 | -62.583785 |
| 15 | 11 | 1100015 | ALDEIA INDIGENA MORADA NOVA | -12.068917 | -62.574492 |
| 16 | 11 | 1100015 | ALDEIA INDIGENA NOVA ESPERANCA | -12.080518 | -62.58894 |
| 17 | 11 | 1100015 | ALDEIA INDIGENA PARAISO | -12.017131 | -62.491814 |
| 18 | 11 | 1100015 | ALDEIA INDIGENA PE DE SERRA | -12.036637 | -62.52028 |
| 19 | 11 | 1100015 | ALDEIA INDIGENA SAMBA | -12.025719 | -62.508895 |
| 20 | 11 | 1100015 | ALDEIA INDIGENA SAO LUIZ | -12.004845 | -62.487825 |

**`ruas`**

| id | uf_codigo | municipio_codigo | bairro | logradouro | cep | latitude | longitude |
|---|---|---|---|---|---|---|---|
| 1 | 11 | 1100015 | ALDEIA INDIGENA AEKA | LINHA QUARENTA E SETE E MEIO | 76954000 | -12.075615 | -62.580233 |
| 2 | 11 | 1100015 | ALDEIA INDIGENA BANANEIRA | LINHA QUARENTA E SETE E MEIO | 76954000 | -12.179382 | -62.588131 |
| 3 | 11 | 1100015 | ALDEIA INDIGENA BARRANCO ALTO | LINHA 47 | 76954000 | -12.047012 | -62.543873 |
| 4 | 11 | 1100015 | ALDEIA INDIGENA BARREIRA DE ARARA | LINHA 47 | 76954000 | -12.05792 | -62.544296 |
| 5 | 11 | 1100015 | ALDEIA INDIGENA BOA ESPERANCA | LINHA QUARENTA E SETE E MEIO | 76954000 | -12.066348 | -62.569913 |
| 6 | 11 | 1100015 | ALDEIA INDIGENA BOM JESUS 1 | LINHA QUARENTA E SETE E MEIO | 76954000 | -11.996169 | -62.440914 |
| 7 | 11 | 1100015 | ALDEIA INDIGENA BOM JESUS 2 | LINHA QUARENTA E SETE E MEIO | 76954000 | -12.002891 | -62.462329 |
| 8 | 11 | 1100015 | ALDEIA INDIGENA BOM SOSSEGO | LINHA QUARENTA E SETE E MEIO | 76954000 | -12.170102 | -62.589543 |
| 9 | 11 | 1100015 | ALDEIA INDIGENA CASTANHEIRA | LINHA QUARENTA E SETE E MEIO | 76954000 | -12.138213 | -62.582945 |
| 10 | 11 | 1100015 | ALDEIA INDIGENA JATOBA | LINHA QUARENTA E SETE E MEIO | 76954000 | -12.199235 | -62.603408 |
| 11 | 11 | 1100015 | ALDEIA INDIGENA MARCOS | LINHA QUARENTA E SETE E MEIO | 76954000 | -12.034057 | -62.529527 |
| 12 | 11 | 1100015 | ALDEIA INDIGENA MATO GROSSO | LINHA QUARENTA E SETE E MEIO | 76954000 | -12.003056 | -62.476775 |
| 13 | 11 | 1100015 | ALDEIA INDIGENA MOAM | LINHA QUARENTA E SETE E MEIO | 76954000 | -12.117642 | -62.586342 |
| 14 | 11 | 1100015 | ALDEIA INDIGENA MONTE CRISTO | LINHA QUARENTA E SETE E MEIO | 76954000 | -12.123225 | -62.583785 |
| 15 | 11 | 1100015 | ALDEIA INDIGENA MORADA NOVA | LINHA QUARENTA E SETE E MEIO | 76954000 | -12.068917 | -62.574492 |
| 16 | 11 | 1100015 | ALDEIA INDIGENA NOVA ESPERANCA | LINHA QUARENTA E SETE E MEIO | 76954000 | -12.080518 | -62.58894 |
| 17 | 11 | 1100015 | ALDEIA INDIGENA PARAISO | LINHA QUARENTA E SETE E MEIO | 76954000 | -12.017131 | -62.491814 |
| 18 | 11 | 1100015 | ALDEIA INDIGENA PE DE SERRA | LINHA QUARENTA E SETE E MEIO | 76954000 | -12.036637 | -62.52028 |
| 19 | 11 | 1100015 | ALDEIA INDIGENA SAMBA | LINHA QUARENTA E SETE E MEIO | 76954000 | -12.025719 | -62.508895 |
| 20 | 11 | 1100015 | ALDEIA INDIGENA SAO LUIZ | LINHA QUARENTA E SETE E MEIO | 76954000 | -12.004845 | -62.487825 |

**`sqlite_sequence`**

| name | seq |
|---|---|
| ruas | 4266401 |
| bairros | 520195 |

## 4. Dimensoes

| Dimensao | Distintos | Esperado (v2.1) | Situacao | Formato |
|---|---|---|---|---|
| UF (`uf_codigo`) | 27 | 27 | OK | ibge_state_code |
| Municipio (`municipio_codigo`) | 5.570 | 5.570 | OK | 7 digitos IBGE |

| UF | Sigla | Municipios | Bairros | Ruas |
|---|---|---|---|---|
| 11 | RO | 52 | 3.322 | 33.538 |
| 12 | AC | 22 | 3.440 | 18.644 |
| 13 | AM | 62 | 11.874 | 57.976 |
| 14 | RR | 15 | 1.589 | 9.067 |
| 15 | PA | 144 | 22.153 | 146.961 |
| 16 | AP | 16 | 1.267 | 10.157 |
| 17 | TO | 139 | 12.160 | 80.374 |
| 21 | MA | 217 | 34.509 | 154.670 |
| 22 | PI | 224 | 26.564 | 114.670 |
| 23 | CE | 184 | 25.251 | 163.955 |
| 24 | RN | 167 | 8.786 | 64.234 |
| 25 | PB | 223 | 17.181 | 92.940 |
| 26 | PE | 185 | 26.928 | 172.275 |
| 27 | AL | 102 | 7.392 | 55.030 |
| 28 | SE | 75 | 4.712 | 48.970 |
| 29 | BA | 417 | 64.529 | 430.098 |
| 31 | MG | 853 | 71.048 | 582.630 |
| 32 | ES | 78 | 7.644 | 83.688 |
| 33 | RJ | 92 | 7.688 | 202.071 |
| 35 | SP | 645 | 59.014 | 684.905 |
| 41 | PR | 399 | 29.891 | 251.536 |
| 42 | SC | 295 | 10.036 | 151.114 |
| 43 | RS | 497 | 20.470 | 239.848 |
| 50 | MS | 79 | 9.191 | 68.345 |
| 51 | MT | 141 | 16.348 | 103.542 |
| 52 | GO | 246 | 16.545 | 207.450 |
| 53 | DF | 1 | 663 | 37.713 |

- Municipios distintos apos normalizacao (bairros): 5.570
- Valores de municipio que precisaram de normalizacao: bairros=0, ruas=0
- Divergencias de prefixo UF x IBGE: bairros=0, ruas=0
- Valores de UF invalidos: bairros=0, ruas=0

## 5. Qualidade

### 5.1 Nulos, vazios e caracteres invalidos

**`bairros` (520.195 linhas)**

| Campo | Coluna | Nulos | Vazios | Nulo ou vazio | U+FFFD | Maior comprimento |
|---|---|---|---|---|---|---|
| uf | uf_codigo | 0 | 0 | 0 | 0 | 2 |
| municipality_ibge_code | municipio_codigo | 0 | 0 | 0 | 0 | 7 |
| locality | nome | 0 | 0 | 0 | 0 | 60 |

**`ruas` (4.266.401 linhas)**

| Campo | Coluna | Nulos | Vazios | Nulo ou vazio | U+FFFD | Maior comprimento |
|---|---|---|---|---|---|---|
| uf | uf_codigo | 0 | 0 | 0 | 0 | 2 |
| municipality_ibge_code | municipio_codigo | 0 | 0 | 0 | 0 | 7 |
| neighborhood_name | bairro | 0 | 0 | 0 | 0 | 60 |
| street_name | logradouro | 0 | 0 | 0 | 0 | 84 |
| zip_code | cep | 0 | 0 | 0 | 0 | 8 |

### 5.2 CEPs

| Situacao | Linhas |
|---|---|
| Valido (8 digitos) | 4.266.401 |
| Reparavel (separadores) | 0 |
| Invalido | 0 |
| Vazio | 0 |
| Nulo | 0 |
| Total | 4.266.401 |

CEP valido no formato nao implica CEP existente; a validacao final continua ocorrendo no importador (8 digitos apos remocao de separadores, sem preencher zeros).

### 5.3 Coordenadas

**`bairros.latitude_central/longitude_central`**

| Situacao | Linhas |
|---|---|
| Validas | 520.195 |
| missing_both | 0 |
| missing_partial | 0 |
| non_numeric | 0 |
| non_finite | 0 |
| zero | 0 |
| out_of_range | 0 |
| out_of_brazil_envelope | 0 |
| Total | 520.195 |

Faixa valida: latitude -33.733684 a 5.152899; longitude -73.649865 a -32.399613. Eixos zerados (lat=0 ou lon=0): 0.

**`ruas.latitude/longitude`**

| Situacao | Linhas |
|---|---|
| Validas | 4.266.401 |
| missing_both | 0 |
| missing_partial | 0 |
| non_numeric | 0 |
| non_finite | 0 |
| zero | 0 |
| out_of_range | 0 |
| out_of_brazil_envelope | 0 |
| Total | 4.266.401 |

Faixa valida: latitude -33.742434 a 5.152899; longitude -73.649865 a -32.399028. Eixos zerados (lat=0 ou lon=0): 0.

### 5.4 Duplicidades

| Chave | Tabela | Colunas | Grupos duplicados |
|---|---|---|---|
| Bairro (bruto) | bairros | uf_codigo, municipio_codigo, nome | 0 |
| Logradouro (chave natural) | ruas | uf_codigo, municipio_codigo, bairro, logradouro, cep | 0 |
| Ponto de logradouro (com coordenadas) | ruas | uf_codigo, municipio_codigo, bairro, logradouro, cep, latitude, longitude | 0 |

### 5.5 Cobertura bairro x ruas

| Metrica | Valor |
|---|---|
| Bairros distintos | 520.195 |
| Bairros com pontos de ruas | 520.195 |
| Bairros sem pontos | 0 |
| Grupos de ruas distintos | 520.195 |
| Grupos de ruas sem bairro | 0 |
| Linhas de ruas sem bairro | 0 |
| Municipios em bairros | 5.570 |
| Municipios em ruas | 5.570 |

## 6. Validacao dos centroides materializados

| Metrica | Valor |
|---|---|
| Metodo | ADDRESS_POINT_MEAN |
| Proveniencia | materialized_and_validated |
| Tolerancia (m) | 1.00 |
| Bairros comparados | 520.195 |
| Bairros sem pontos | 0 |
| Mediana do erro (m) | 0.0304 |
| p90 do erro (m) | 0.0589 |
| p99 do erro (m) | 0.0784 |
| Maximo do erro (m) | 0.0786 |
| Acima da tolerancia | 0 |
| Bairros com menos de 5 pontos (low_sample) | 344.434 |

A media e calculada diretamente sobre os pontos de `ruas` de cada bairro (uma unica soma consolidada, sem media de medias) e comparada com `latitude_central`/`longitude_central`. Detalhes e amostras em `centroid-validation.json`.

**Desvio por UF (27 UFs)**

| UF | Sigla | Comparados | Mediana (m) | p90 (m) | p99 (m) | Maximo (m) | Acima da tolerancia |
|---|---|---|---|---|---|---|---|
| 11 | RO | 3.322 | 0.0278 | 0.0592 | 0.0780 | 0.0782 | 0 |
| 12 | AC | 3.440 | 0.0000 | 0.0556 | 0.0782 | 0.0783 | 0 |
| 13 | AM | 11.874 | 0.0000 | 0.0556 | 0.0786 | 0.0786 | 0 |
| 14 | RR | 1.589 | 0.0000 | 0.0556 | 0.0786 | 0.0786 | 0 |
| 15 | PA | 22.153 | 0.0247 | 0.0592 | 0.0786 | 0.0786 | 0 |
| 16 | AP | 1.267 | 0.0197 | 0.0574 | 0.0786 | 0.0786 | 0 |
| 17 | TO | 12.160 | 0.0000 | 0.0556 | 0.0782 | 0.0785 | 0 |
| 21 | MA | 34.509 | 0.0000 | 0.0586 | 0.0786 | 0.0786 | 0 |
| 22 | PI | 26.564 | 0.0278 | 0.0619 | 0.0785 | 0.0786 | 0 |
| 23 | CE | 25.251 | 0.0354 | 0.0620 | 0.0785 | 0.0786 | 0 |
| 24 | RN | 8.786 | 0.0000 | 0.0562 | 0.0784 | 0.0785 | 0 |
| 25 | PB | 17.181 | 0.0000 | 0.0587 | 0.0784 | 0.0784 | 0 |
| 26 | PE | 26.928 | 0.0000 | 0.0579 | 0.0783 | 0.0783 | 0 |
| 27 | AL | 7.392 | 0.0274 | 0.0590 | 0.0781 | 0.0782 | 0 |
| 28 | SE | 4.712 | 0.0372 | 0.0619 | 0.0780 | 0.0781 | 0 |
| 29 | BA | 64.529 | 0.0302 | 0.0606 | 0.0779 | 0.0782 | 0 |
| 31 | MG | 71.048 | 0.0349 | 0.0594 | 0.0769 | 0.0774 | 0 |
| 32 | ES | 7.644 | 0.0369 | 0.0595 | 0.0765 | 0.0767 | 0 |
| 33 | RJ | 7.688 | 0.0378 | 0.0585 | 0.0758 | 0.0761 | 0 |
| 35 | SP | 59.014 | 0.0344 | 0.0587 | 0.0759 | 0.0763 | 0 |
| 41 | PR | 29.891 | 0.0339 | 0.0582 | 0.0754 | 0.0757 | 0 |
| 42 | SC | 10.036 | 0.0373 | 0.0594 | 0.0745 | 0.0747 | 0 |
| 43 | RS | 20.470 | 0.0371 | 0.0589 | 0.0741 | 0.0744 | 0 |
| 50 | MS | 9.191 | 0.0117 | 0.0568 | 0.0762 | 0.0768 | 0 |
| 51 | MT | 16.348 | 0.0000 | 0.0559 | 0.0778 | 0.0781 | 0 |
| 52 | GO | 16.545 | 0.0371 | 0.0605 | 0.0772 | 0.0776 | 0 |
| 53 | DF | 663 | 0.0222 | 0.0558 | 0.0771 | 0.0772 | 0 |

## 7. Baseline v2.1

Referencia: imports/cnefe/prompt.txt v2.1 §3.3 e §3.3.4

| Metrica | Esperado | Medido | Situacao |
|---|---|---|---|
| neighborhood_rows | 520.195 | 520.195 | OK |
| street_rows | 4.266.401 | 4.266.401 | OK |
| uf_count | 27 | 27 | OK |
| municipality_count | 5.570 | 5.570 | OK |
| neighborhoods_without_streets | 0 | 0 | OK |
| duplicate_neighborhood_keys | 0 | 0 | OK |

A comparacao com o baseline e informativa: um artefato novo pode divergir legitimamente. O importador deve reexecutar estas verificacoes e falhar somente contra o baseline do proprio artefato aprovado.

## 8. Execucao, memoria e determinismo

| Item | Valor |
|---|---|
| Chunk de leitura | 50.000 |
| Amostras por categoria | 10 |
| Duracao (s) | 189.10 |
| Pico de memoria residente (MiB) | 361.2 |

**Tempo por fase**

| Fase | Segundos |
|---|---|
| checksum_initial | 0.781 |
| schema_inventory | 1.213 |
| mapping_resolution | 0.000 |
| distributions | 4.476 |
| duplicates | 52.794 |
| neighborhoods_pass | 9.843 |
| streets_pass | 114.392 |
| centroid_comparison | 2.979 |
| checksum_final | 1.906 |

- Duas passadas streaming, uma por tabela, na ordem da chave primaria (`ORDER BY id`, sem `OFFSET`), em chunks; nenhuma lista de 4,2 milhoes de linhas e mantida em memoria.
- A agregacao usa um dicionario com um registro por bairro (contagem + somas compensadas de latitude/longitude), memoria O(bairros).
- As somas usam compensacao de Kahan e a leitura tem ordem fixa, portanto o mesmo arquivo produz os mesmos numeros (fingerprint estavel).

## 9. Limitacoes e proximos passos

- A comparacao usa o texto bruto de `bairro`/`nome`; nenhuma normalizacao de nome e aplicada nesta fase, portanto grafias divergentes aparecem como grupos distintos e sao contabilizadas em `street_groups_without_neighborhood`.
- O artefato e pre-agregado por logradouro: a media tem peso por logradouro, nao por domicilio.
- Esta etapa nao cria migracao PostgreSQL, nao altera entidades, APIs, Redis ou `StoreDeliveryAreas`.
- Proximos passos da Fase 0: versionar `config/source-mapping.yaml` com o mapeamento confirmado e reexecutar esta descoberta sempre que o checksum do artefato mudar.
