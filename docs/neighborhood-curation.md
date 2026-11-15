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

## Reversão

`rollback` desativa os aliases criados pelo manifesto, limpa `DisplayName` quando
ainda corresponde ao valor aplicado e restaura o nome canônico quando ele ainda é
a correção de acentos. O remapeamento de `StoreDeliveryAreas` é revertido a partir
do backup CSV registrado antes da aplicação; o apply nunca apaga bairros.

Aliases atravessando `CityId` são rejeitados pelo trigger PostgreSQL. A unicidade
de `(CityId, NormalizedAlias)` impede que o mesmo texto ativo seja ambíguo dentro
da cidade.
