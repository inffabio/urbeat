# Curadoria de bairros

Esta fundação mantém os registros CNEFE e os IDs de bairros intactos. A migration
`AddNeighborhoodCurationFoundation` cria apenas a coluna opcional `DisplayName` e a
tabela de aliases; ela não insere, renomeia, desativa ou funde nenhum bairro.

## Regras de produto

- A apresentação é sempre em **LETRAS MAIÚSCULAS** (`DisplayName`); acentos são
  preservados. O texto canônico armazenado mantém a grafia aprovada.
- O canônico é preferencialmente o registro `official_cnefe`, com correção apenas
  de acentos/caixa quando há evidência no próprio grupo. Nenhum nome é inventado.
- Registros fonte nunca são apagados. Um alias registra a grafia alternativa e
  aponta para o canônico; a linha de origem continua no banco para auditoria.
- `StoreDeliveryAreas` vinculadas a um alias são remapeadas para o canônico
  preservando loja, taxa, notas, status e campos de auditoria. Duplicatas por
  loja/canônico são resolvidas de forma determinística e auditadas.

## Fluxo operacional

1. Exportar candidatos por cidade, preservando nome original, `CityId` e `Id`.
2. Revisar cada grupo e registrar a decisão em um arquivo de decisões
   (`urbeat.neighborhood.alias-decisions/1`). Grupos sem evidência suficiente usam
   `keep_separate`; grupos com cidade/UF ausente usam o arquivo de reparos.
3. Gerar o manifesto (`build-manifest`) e validar o dry-run contra o banco.
4. Fazer backup lógico/CSV apenas das linhas afetadas e registrar caminho + hash.
5. Aplicar com `apply --confirm-hash <hash>`; a operação é transacional e
   idempotente.
6. Validar contagens, `__EFMigrationsHistory`, health HTTP e logs.

### Comandos

```powershell
# 1. manifesto reproduzível (sem banco)
dotnet Urbeat.WebApi.dll --curation build-manifest `
  --queue queue.json --decisions decisions.json --repairs repairs.json `
  --manifest-out manifest.json

# 2. dry-run contra o banco (somente leitura)
dotnet Urbeat.WebApi.dll --curation dry-run `
  --queue queue.json --decisions decisions.json --repairs repairs.json `
  --manifest-out manifest.json --report-out dry-run.json

# 3. backup lógico (gera um script \copy para psql)
dotnet Urbeat.WebApi.dll --curation backup-sql `
  --manifest manifest.json --out backup.sql

# 4. aplicação explícita (o hash do manifesto é obrigatório)
dotnet Urbeat.WebApi.dll --curation apply `
  --manifest manifest.json --confirm-hash <manifest_hash>

# 5. verificação
dotnet Urbeat.WebApi.dll --curation verify --manifest manifest.json

# 6. reversão (também exige o hash do manifesto)
dotnet Urbeat.WebApi.dll --curation rollback `
  --manifest manifest.json --confirm-hash <manifest_hash>
```

`apply` e `rollback` recusam a execução sem `--confirm-hash` igual ao `manifest_hash`.
O `build-manifest` não acessa o banco; `dry-run`, `verify`, `apply` e `rollback` usam a
mesma conexão da API. `dry-run` monta o plano sem `SaveChanges`, portanto não escreve.

## Decisões desta execução nacional

- Fila estendida: 1.557 grupos nacionais + 4 grupos dedicados de Maricá/Macaé.
- 1.559 grupos unificados, 2 mantidos separados (nomes sem membro em maiúsculas).
- 1.567 aliases; 93 correções canônicas apenas de acentos/caixa.
- 25 UFs com aliases aplicáveis. AP e RR não tinham grupos candidatos; DF foi
  incluído pelos grupos de variação textual.
- Maricá: `G004418` unificado em `Chácara de Inoã` (evidência local de Inoã) e
  `G004419` corrigido para `São José do Imbassaí`.
- Macaé: `G004411`–`G004414` unificados; as 3 áreas de loja foram remapeadas.
- `teste` (Maricá, sem UF/CityId) foi desativado com auditoria; `Bairro Novo`
  recebeu o `CityId` de Rio das Ostras/RJ.

O manifesto completo, o relatório de dry-run e o arquivo de backup ficam fora do
repositório; caminho e SHA-256 são registrados no relatório de operação.

## Registro da execução nacional (2026-11-15)

- Commit/deploy: `a8ac2752a921505365e95b1dc85352d0e7b680ce`.
- Migration aplicada: `20261115034808_AddNeighborhoodCurationFoundation`.
- Manifesto: `d961358f271151f9c0185ca7a73f0f08c6ee75fbb57a7c0b4bb790276cd90ba1`.
- Fila: SHA-256 `5e5fd1b9823dfcaf13dcd0767296f817dbc6df78df9df403cd24e1c52f7c7af2`.
- Decisões: SHA-256 `a38d87b103b7d50d728f10fdba5e76cd35b8b28760618cd980bab49c6a656065`.
- Backup no servidor: `/home/dexter/urbeat-backups/neighborhood-20261115/`
  - `urbeat-neighborhood-backup.csv` (3.128 linhas) —
    `6f654d907ade645bf88177d76fdb5478e8f3c97999e1bee190e4dc8a64fc6cf0`
  - `urbeat-store-area-backup.csv` (3 linhas) —
    `674abae5c5506eb2b9f97bf17726b29953d651cd169c4d6ee7d59ea5afd3afc0`
  - `urbeat-store-area-by-store-backup.csv` (55 linhas) —
    `e2251a2523efed947f41aaf6915e7ef3e5a6761ddeaf9857e2e1c84a22ac1d61`
- Dry-run: 1.567 aliases, 1.559 display names, 93 correções canônicas, 3 áreas
  remapeadas, 0 duplicatas, 2 reparos, 0 avisos.
- Apply (`2026-11-15T06:57:54Z`): exatamente as mesmas contagens; 1.567 aliases
  ativos/aprovados; 1.559 display names; 3 áreas de Macaé remapeadas preservando
  taxa, notas e status; 0 conflitos de alias; 0 aliases fora da cidade.
- Reparos: `teste` desativado; `Bairro Novo` vinculado a Rio das Ostras/RJ.
- Grupos mantidos separados (2): `G001419` (Ceres/GO) e `G004918`
  (Caraguatatuba/SP), ambos sem membro em maiúsculas.
- Cobertura: 25 UFs. AP e RR não possuem grupos candidatos na base; DF foi
  incluído pelas variações textuais de Brasília.
- Health HTTP: frontend 200, API 200 (`Healthy`); nenhum 500 ou exceção nos logs
  das duas réplicas da API e dos workers após a aplicação.


## Reversão

`rollback` desativa os aliases criados pelo manifesto, limpa `DisplayName` quando
ainda corresponde ao valor aplicado e restaura o nome canônico quando ele ainda é
a correção de acentos. O remapeamento de `StoreDeliveryAreas` é revertido a partir
do backup CSV registrado antes da aplicação; o apply nunca apaga bairros.

Aliases atravessando `CityId` são rejeitados pelo trigger PostgreSQL. A unicidade
de `(CityId, NormalizedAlias)` impede que o mesmo texto ativo seja ambíguo dentro
da cidade.
