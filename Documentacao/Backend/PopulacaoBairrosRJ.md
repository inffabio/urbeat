# Fluxo de Bairros por UF

O fluxo vale para todas as 27 unidades federativas do Brasil, incluindo o
Distrito Federal. A UF e validada contra a tabela oficial de codigos IBGE;
nenhuma lista de municipios e mantida no codigo.

## Regra operacional

1. O importador consulta os municipios oficiais na API IBGE.
2. Para cada codigo IBGE de municipio, consulta os bairros na API Brasil Aberto.
3. O importador tenta geocodificar os bairros ativos, preservando coordenadas
   existentes, antes de gerar `snapshots/bairros_<uf>.csv`.
4. Quando encontrada, a coordenada representa aproximadamente a primeira
   rua/CEP encontrada no bairro: o e-DNE e consultado primeiro, escolhendo a
   rua alfabeticamente menor dentro do bairro e municipio (ignorando
   logradouros e CEPs vazios), e o CEP e convertido em coordenadas pela API
   Brasil Aberto com origem `brasil_aberto_first_street`. Quando o Brasil
   Aberto nao retorna um par valido, o Cep Aberto e tentado como fallback
   (somente se `CEP_ABERTO_API_TOKEN` estiver configurado), com origem
   `cep_aberto`; sem token, o bairro permanece pendente sem erro. OSM/Nominatim
   e usado como ultimo fallback real, com origem `osm_nominatim`, habilitado
   por padrao, com atraso minimo configuravel (padrao 1s). Para ambientes que
   nao devem fazer chamadas externas, pode ser desativado com
   `URBEAT_DISABLE_NOMINATIM=true`; nesse caso o bairro permanece pendente e o
   Nominatim nao e chamado. A origem da coordenada e registrada em `Source`;
   nunca e usado centroide do municipio.
5. Restauracoes usam somente CSV. Restaurar nunca consulta API, e a operacao
   e idempotente.

Um snapshot so e criado depois que os bairros foram importados no banco e a tentativa de geolocalizacao terminou. Ele aceita bairros pendentes sem geolocalizacao: nesses casos, os campos
`Latitude` e `Longitude` permanecem vazios no CSV. Pares parciais (uma coordenada
preenchida e a outra vazia) e coordenadas invalidas sao rejeitados. Bairros
pendentes nao bloqueiam a publicacao do CSV; as mensagens distinguem bairros
totais, geolocalizados e pendentes. Nao execute importacao real sem
banco/configuracao; testes nao fazem chamadas reais nem devem expor segredos.

## Substituicao controlada de bairros oficiais

O importador substitui os bairros oficiais de cada municipio pela lista atual
da API Brasil Aberto, preservando os bairros manuais/customizados.

- Um registro e considerado **oficial** quando tem `Source` preenchido com uma
  origem conhecida (`brasil_aberto`, `brasil_aberto_cep`,
  `brasil_aberto_first_street`, `osm_nominatim` ou `openstreetmap`) ou quando
  tem `CityId` preenchido. Esses registros podem ser atualizados ou removidos
  conforme a resposta atual da API.
- Um registro e considerado **manual** quando `Source` e `CityId` estao nulos
  (bairros criados pelo vendedor via `CreateDeliveryNeighborhoodAsync`). Esses
  registros nunca sao alterados nem removidos pelo importador.
- Para cada municipio, em uma transacao unica: os nomes novos sao inseridos
  (ou atualizados, quando o oficial ja existe), e os oficiais obsoletos que
  nao estao na resposta atual sao removidos. Em caso de erro, a transacao e
  revertida e aquele municipio nao fica parcialmente substituido; os demais
  municipios nao sao afetados.
- Se a API retornar lista vazia ou a consulta de um municipio falhar, nenhum
  bairro e removido naquele municipio.
- Se um nome oficial conflitar com um bairro manual de mesmo nome, o manual e
  preservado e nenhuma duplicata invalida e criada.
- Apos processar os municipios, o fluxo mantem a geocodificacao e gera os
  snapshots das 27 UFs (`export-all`).

## Candidatos por UF e normalizacao canonica

A primeira etapa do fluxo normaliza os nomes de bairro de forma canonica e
gera candidatos unicos, antes de qualquer importacao ou geocodificacao.

- A normalizacao canonica compartilhada vive em `import_common.py` como
  `normalize_neighborhood_name(name)`: remove acentos e diacriticos (NFKD),
  aplica caixa baixa e remove espacos das bordas. Todos os scripts do fluxo
  usam a mesma funcao, inclusive o importador `brasil_aberto_import.py`.
- A chave de deduplicacao e `candidate_key(uf, codigo IBGE, nome normalizado)`,
  ou seja, `Uf + CityIbgeCode + NormalizedName`. Registros que so diferem por
  caixa ou acento colapsam em um unico candidato.
- O modulo `neighborhood_candidates.py` expoe `build_candidates(records)` para
  normalizar e deduplicar registros brutos, e `export_candidates_by_uf(...)`
  para escrever um CSV final por UF em `candidates/bairros_<uf>.csv`.
- Cada registro bruto precisa de `Uf`, `CityIbgeCode`, `City` e `Neighborhood`.
  Latitude e Longitude, quando presentes, devem formar um par completo (ou
  ambos vazios); pares parciais ou invalidos sao rejeitados. O candidato
  aproveita o primeiro registro com coordenadas validas.
- A saida usa as mesmas colunas do snapshot (`Uf`, `CityIbgeCode`, `City`,
  `Neighborhood`, `NormalizedName`, `Latitude`, `Longitude`, `Source` e
  `IsActive`) para que a proxima etapa possa importar diretamente.

A etapa `neighborhood_source_import.py` (descrita abaixo) consome esses
candidatos para gerar o CSV final por UF sem escrever no banco. A importacao
efetiva no banco permanece em `brasil_aberto_import.py` e nao e executada por
essa etapa.

## Geração do CSV final por UF (sem banco)

A etapa `neighborhood_source_import.py` gera o CSV final de bairros por UF
diretamente das fontes reais, sem escrever no banco e sem apagar dados:

1. Municipios e codigos vem da API IBGE (`localidades/estados/<uf>/municipios`).
2. Bairros vem preferencialmente do e-DNE SQLite (`URBEAT_DNE_DB`, tabela
   `cep_unificado`), usando `uf`, `municipio_cod_ibge`, `municipio`, `bairro` e
   `cep` quando disponiveis; o schema minimo (`municipio`, `bairro`,
   `logradouro`, `cep`) tambem e aceito, resolvendo o codigo IBGE pelo nome do
   municipio.
3. A API Brasil Aberto `districts-by-ibge-code` complementa os candidatos.
4. Os registros sao unificados e deduplicados por
   `neighborhood_candidates.build_candidates` antes de qualquer geocodificacao.
5. Para cada candidato, escolhe o logradouro/CEP alfabeticamente menor do e-DNE
   por codigo IBGE + nome normalizado e converte o CEP em coordenadas pela API
   Brasil Aberto (`v2/zipcode/{cep}`). Quando o Brasil Aberto nao retorna um
   par valido, tenta o Cep Aberto (`v3/cep?cep={cep}`) apenas se
   `CEP_ABERTO_API_TOKEN` estiver configurado, registrando `cep_aberto`.
   Coordenadas existentes sao preservadas; bairros sem par valido permanecem
   com `Latitude`/`Longitude` vazios.
6. O CSV final validado e gravado em `snapshots/bairros_<uf>.csv` (ou diretorio
   configuravel via `--output`) com as colunas `SNAPSHOT_COLUMNS`, pronto para
   restauracao futura.

Nenhum Nominatim e usado em massa nesta etapa; o fallback fica separado e
configuravel (`geocode_via_cep.py`). Falhas de API e de consulta ao e-DNE sao
tratadas por candidato, deixando o par de coordenadas vazio e reportando a
pendencia, em vez de abortar a UF inteira.

## Reparação de coordenadas pendentes de um snapshot (sem banco)

A etapa `neighborhood_regeocode.py` repara somente as coordenadas pendentes de
um snapshot CSV ja validado, sem escrever no banco e sem tocar em linhas ja
geolocalizadas:

1. Exige `CEP_ABERTO_API_TOKEN` antes de ler ou alterar qualquer arquivo; sem
   token, o comando falha sem modificar o snapshot de entrada.
2. Lê e valida o snapshot com `read_snapshot` e garante que todas as linhas
   pertencem a UF informada (`--uf`), rejeitando linhas de outra UF.
3. Monta o lookup de municipios a partir das proprias linhas do snapshot
   (`municipality_lookup`) e carrega o indice de logradouro/CEP do e-DNE uma
   unica vez via `build_dne_street_index`, usando a conexao SQLite
   `connect_dne`.
4. Para cada linha sem par de coordenadas, localiza o logradouro/CEP em memoria
   e tenta primeiro o Cep Aberto (`get_coordinates_from_cep_aberto`); nunca chama
   Brasil Aberto nem Nominatim. Quando o Cep Aberto nao retorna um par valido,
   tenta o Mapbox Geocoding (`get_coordinates_from_mapbox`), que combina
   logradouro, CEP, municipio, UF e "Brasil" na query e, quando retorna um par
   valido, define `Source` como `mapbox_geocoding`. Erros individuais do
   provedor (403/429/5xx ou timeout) sao capturados por linha: a linha
   permanece pendente e o processamento continua, sem abortar a UF inteira.
5. Linhas ja geolocalizadas permanecem identicas; linhas sem CEP no indice
   permanecem pendentes, assim como linhas cujo CEP nao retorna par valido.
6. Grava o CSV de saida de forma atomica e validada via `write_snapshot` e
   reporta total, recuperados, pendentes, sem CEP e erros do provedor
   (`provider_errors`). Erros estruturais de leitura/validacao impedem a escrita
   da saida; erros individuais do provedor permitem gerar o CSV completo.

## Importador legado bloqueado por padrao

O importador legado `brasil_aberto_import.py` **nao escreve mais no banco por
padrao**. Qualquer execucao sem opt-in explicito falha com uma mensagem clara
orientando o novo fluxo (`neighborhood_source_import.py` para gerar o CSV final
e `neighborhood_rebuild.py` para aplicar no banco).

Para manter a compatibilidade legada, e preciso atender as duas condicoes,
nunca assumidas por padrao:

1. Definir a variavel `URBEAT_ALLOW_LEGACY_DB_IMPORT=true`.
2. Confirmar explicitamente com o texto exato `LEGACY IMPORT <UF>`.

Pela CLI, use `--confirm "LEGACY IMPORT <UF>"`. Quando `import_uf()` e chamada
diretamente, a confirmação e obrigatoria via parametro
`confirmation="LEGACY IMPORT <UF>"` (a função exige
`confirmation == f"LEGACY IMPORT {UF}"`); sem ela, mesmo com o opt-in ativo, a
funcao recusa. `replace_city_neighborhoods` nao exige essa confirmação por ser
helper de teste/baixo nivel.

Sem essas duas condicoes, o importador legado recusa a escrita no banco. O
fluxo legado permanece disponivel apenas para compatibilidade; o novo fluxo e
o recomendado.

## Cadeia de fallback de coordenadas

`geocode_via_cep.py` resolve cada bairro pendente na seguinte ordem, sempre
tratando latitude e longitude como um par e nunca inventando centroides:

1. Primeira rua/CEP do e-DNE (ordem alfabetica) convertida pelo Brasil Aberto,
   com origem `brasil_aberto_first_street`.
2. Cep Aberto para o mesmo CEP (`CEP_ABERTO_API_TOKEN`), com origem
   `cep_aberto`.
3. Mapbox para o logradouro/CEP (`MAPBOX_API_TOKEN`), com origem
   `mapbox_geocoding`.
4. Nominatim pelo bairro, cidade e UF, com origem `osm_nominatim`.

Cada etapa so e tentada quando a anterior nao devolveu um par valido; falhas de
rede/API nao interrompem a importacao. Quando nenhuma fonte devolve um par
valido, os dois campos permanecem vazios.

O fallback Nominatim e chamado por padrao, usando bairro, cidade e UF como
contexto. Um atraso minimo configuravel via `NOMINATIM_DELAY_SECONDS` (padrao
1s, valor minimo 1s) e aplicado entre as chamadas, e o `User-Agent` existente e
o timeout de 30s sao respeitados. Para ambientes que nao devem fazer chamadas
externas, o fallback pode ser desativado explicitamente com
`URBEAT_DISABLE_NOMINATIM=true`; nesse caso os bairros permanecem pendentes.

## Reconstrução de bairros por UF no banco (com backup e confirmação)

A etapa `neighborhood_rebuild.py` aplica um snapshot CSV diretamente no
PostgreSQL, substituindo os bairros de uma única UF de forma transacional:

1. Lê e valida o snapshot com `read_snapshot` (mesmas regras de colunas,
   coordenadas e duplicidades).
2. Exige `--uf`, `--file` e `--confirm` com o valor exato `REBUILD <UF>`; sem a
   confirmação correta o comando não executa nada.
3. Faz um backup CSV do estado atual da UF (`<arquivo>.backup-<uf>.csv`) antes
   de alterar qualquer dado, via `export_snapshot(..., validate=False)`. Esse
   modo copia os registros existentes exatamente como estão (inclusive
   `NormalizedName` antigos que já não seguem a normalização canônica), sem
   validar coordenadas ou duplicidades, e continua atômico. O backup resultante
   não é importável sem validação: `read_snapshot`/`restore_snapshot` continuam
   exigindo a validação canônica.
4. Conecta usando `import_common.connect_database`.
5. Em uma única transação: valida que todas as linhas pertencem à UF informada
   e que cada `CityIbgeCode` existe na tabela `Cities`; apaga os
   `DeliveryNeighborhoods` dos municípios da UF selecionada (sem tocar outras
   UFs e sem apagar `Cities`); insere os registros do CSV com `Id`
   `gen_random_uuid()`, `CityId` resolvido, `City`, `Neighborhood`,
   `NormalizedName`, `Latitude`/`Longitude` via `NULLIF`, `Source` via
   `NULLIF`, `IsActive` e `CreatedAtUtc`.
   A exclusão é restrita à UF informada e cobre duas situações, via SQL
   parametrizado: registros ligados por `CityId` a uma `City` da UF e
   registros legados com `CityId IS NULL` cujo `City` coincide com o `Name`
   de uma `City` da UF. Sem remover esses legados, a inserção falharia com
   `UniqueViolation` no índice único `(Neighborhood, City)`.
6. Confirma (`commit`) somente ao final; em caso de erro executa `rollback`
   explícito, deixando o banco sem alterações parciais.
7. Reporta as contagens de backup, apagados e inseridos.

> **Atenção (ambiente de teste/manuais):** o `rebuild` substitui todos os
> registros da UF, incluindo bairros manuais e legados (com ou sem `CityId`)
> cujo `City` corresponda a um município da UF. Antes de executar em um
> ambiente com cadastros manuais, confirme que eles podem ser substituídos
> pelo conteúdo do snapshot; o backup CSV gerado antes da alteração
> (`<arquivo>.backup-<uf>.csv`) preserva o estado anterior, mas não restaura
> automaticamente registros legados com `CityId IS NULL`.

## Comandos

Executar a partir de `backend/scripts/import/`:

```bash
# CSV final por UF a partir das fontes reais, sem escrever no banco (fluxo recomendado)
python neighborhood_source_import.py --uf RJ
python neighborhood_source_import.py --uf SP
python neighborhood_source_import.py --uf MG
python neighborhood_source_import.py --uf MG --output /tmp/snapshots

# Reconstruir os bairros de uma UF no banco (destrutivo, exige confirmacao)
python neighborhood_rebuild.py --uf MG --file snapshots/bairros_mg.csv --confirm "REBUILD MG"

# Restauracao sem API (exige UF-alvo; rejeita linhas de outra UF)
python restore_neighborhoods.py restore --uf MG --file snapshots/bairros_mg.csv

# Reparar coordenadas pendentes de um snapshot CSV (Cep Aberto e Mapbox; exige CEP_ABERTO_API_TOKEN)
python neighborhood_regeocode.py --uf RJ --file snapshots/bairros_rj.csv --output snapshots/bairros_rj.csv

# Geocodificacao por UF (e-DNE/Brasil Aberto -> Cep Aberto -> Mapbox -> Nominatim)
python geocode_via_cep.py --uf ES
python geocode_via_cep_sp.py            # SP

# Importador LEGADO: bloqueado por padrao, exige opt-in + confirmacao
URBEAT_ALLOW_LEGACY_DB_IMPORT=true python brasil_aberto_import.py --uf MG --confirm "LEGACY IMPORT MG"
URBEAT_ALLOW_LEGACY_DB_IMPORT=true python brasil_aberto_import_sp.py    # SP

# Exportar TODAS as 27 UFs (substitui todos os snapshots com o estado atual do banco)
python neighborhood_snapshot.py export-all

# Gerar candidatos por UF a partir de registros brutos (CSVs ou diretorio)
python neighborhood_candidates.py generate --input raw/ --output candidates/

# Restringir a geracao a uma UF
python neighborhood_candidates.py generate --input raw/ --output candidates/ --uf MG
```

Variaveis necessarias para importacao/geocodificacao:

- `BRASIL_ABERTO_API_KEY`
- `CEP_ABERTO_API_TOKEN` (opcional no importador; habilita o fallback Cep Aberto em `geocode_via_cep.py` e e obrigatorio em `neighborhood_regeocode.py`)
- `CEP_ABERTO_DELAY_SECONDS` (opcional; atraso entre chamadas ao Cep Aberto; padrao 0.15s)
- `MAPBOX_API_TOKEN` (opcional; habilita o fallback Mapbox em `geocode_via_cep.py` e `neighborhood_regeocode.py`)
- `MAPBOX_DELAY_SECONDS` (opcional; atraso entre chamadas ao Mapbox; padrao 0.1s)
- `URBEAT_DB_PASSWORD`
- `URBEAT_DB_HOST`, `URBEAT_DB_NAME` e `URBEAT_DB_USER` (opcionais)
- `URBEAT_DNE_DB` (opcional; padrao `/home/dexter/dne.db`)
- `URBEAT_ALLOW_LEGACY_DB_IMPORT` (`true` para habilitar o importador legado; padrao: bloqueado)
- `URBEAT_DISABLE_NOMINATIM` (`true` para desativar o fallback Nominatim, que e padrao)
- `NOMINATIM_DELAY_SECONDS` (atraso minimo entre chamadas ao Nominatim; padrao 1s)

Nao registre chaves reais na documentacao ou no repositorio.

## Snapshots

Cada arquivo possui as colunas `Uf`, `CityIbgeCode`, `City`, `Neighborhood`,
`NormalizedName`, `Latitude`, `Longitude`, `Source` e `IsActive`. O municipio
na restauracao e resolvido exclusivamente por `Uf + CityIbgeCode`.

A restauracao exige uma UF-alvo (`--uf`) e rejeita qualquer linha cuja UF difira
da UF informada, sem alterar o banco. A validacao normaliza a UF e o codigo
IBGE (ex.: `mg`/`MG` e `3106200.0`/`3106200` sao tratados como equivalentes),
rejeita duplicidades canonicas por `(Uf, CityIbgeCode, nome canonico)` e rejeita
`NormalizedName` divergente do nome canonico do bairro.

O `UPSERT` por `CityId + NormalizedName` garante idempotencia. Latitude e
longitude existentes no banco sao preservadas. A restauracao aceita e preserva
campos vazios, rejeita pares parciais ou fora da faixa e valida todas as linhas
antes de alterar o banco. Em caso de erro a transacao e revertida (rollback),
deixando o banco sem alteracoes parciais. Restaurar via CSV nunca inventa coordenadas.

O snapshot e gerado ao final de cada importacao ou reparo concluido, depois do
commit das alteracoes, para `backend/scripts/import/snapshots/bairros_<uf>.csv`.
A escrita e atomica: um arquivo temporario e validado antes de substituir o
destino, e o temporario e removido em caso de falha.

Ao final de cada importacao ou reparo, o fluxo regenera os snapshots de TODAS
as 27 UFs (nao apenas a UF processada), lendo o banco e substituindo os arquivos
existentes — sem append ou merge. O comando `python neighborhood_snapshot.py
export-all` faz o mesmo de forma isolada, gerando `bairros_<uf>.csv` para todas
as UFs com o estado atual do banco. Estados sem registros produzem um CSV valido
contendo apenas o cabecalho (o arquivo e substituido, nunca apagado).

## Fontes

- Municipios e codigos: [API IBGE](https://servicodados.ibge.gov.br/api/v1/localidades/estados/33/municipios)
- Bairros: API Brasil Aberto `districts-by-ibge-code`
- CEPs: [Correios e-DNE](https://github.com/cauethenorio/edne-correios-loader)
- Coordenadas: primeira rua/CEP do e-DNE convertida pela API Brasil Aberto
  `v2/zipcode/{cep}` com origem `brasil_aberto_first_street`
- Fallback de coordenadas: [Cep Aberto](https://www.cepaberto.com/) `v3/cep?cep={cep}`
  com origem `cep_aberto` (exige `CEP_ABERTO_API_TOKEN`)
- Fallback de coordenadas: [Mapbox Geocoding](https://docs.mapbox.com/api/search/geocoding/)
  `v5/mapbox.places/{query}.json` com origem `mapbox_geocoding` (exige `MAPBOX_API_TOKEN`)
- Fallback de coordenadas: [OpenStreetMap Nominatim](https://nominatim.openstreetmap.org/) com origem `osm_nominatim`
