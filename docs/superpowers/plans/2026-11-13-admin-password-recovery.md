# Plano — recuperação operacional da senha do administrador

**Data:** 2026-11-13
**Spec:** `docs/superpowers/specs/2026-11-13-admin-password-recovery-design.md`

## Entregas

### Backend

- [x] `AdminRecoveryOptions` desabilitado por padrão, sem chave padrão, faixa de tamanho 32–512.
- [x] `AdminRecoverPasswordRequestDto` + validador FluentValidation.
- [x] `IAdminPasswordRecoveryService` / `AdminPasswordRecoveryService`.
- [x] `POST /api/auth/admin/recover-password` com `X-Admin-Recovery-Key` como único transporte.
- [x] Restrição exclusiva ao e-mail configurado em `AdminSeed:Email` e à role `Admin`. Sem
  `AdminSeed:Email` (vazio/nulo) a chamada é rejeitada antes de consultar usuários; não há fallback
  para outra conta `Admin`.
- [x] Ledger `AdminRecoveryKeyUsage` com fingerprint SHA-256 e índice único.
- [x] Inserção atômica `INSERT ... ON CONFLICT` dentro da transação da troca de senha.
- [x] Revogação de todos os refresh tokens do administrador e rotação do security stamp.
- [x] Auditoria e logs sanitizados (sem senha/chave/token).
- [x] Rate limit dedicado `admin-recovery` (429).
- [x] Migração `AddAdminRecoveryKeyUsage`.
- [x] `AdminUserSeeder` inalterado; `AdminSeed:Password` continua sem atualizar admin existente.

### Postman

- [x] Request `POST /api/auth/admin/recover-password`, No Auth, header
  `X-Admin-Recovery-Key: {{adminRecoveryKey}}`, body com `{{adminEmail}}`,
  `{{newAdminPassword}}`, `{{newAdminPasswordConfirm}}`.
- [x] Variáveis vazias tipo `secret` nos ambientes local/OCI.
- [x] Endpoint classificado como `destructive` no guard da coleção e no mapa de features.
- [x] Descrição com instrução de habilitar temporariamente, executar uma vez e desabilitar/rotacionar.

### Testes

- [x] `AdminPasswordRecoveryServiceTests`: disabled, chave curta/ausente/inválida, alvo
  customer/desconhecido, `AdminSeed:Email` ausente/vazio sem alterar nenhuma conta, senha fraca,
  sucesso com revogação, reuso e concorrência.
- [x] `AdminPasswordRecoveryFlowTests`: 404 desabilitado, 401 sem/coma chave errada, chave em
  body/query ignorada, sucesso + sessão antiga inválida, 409 no reuso, Customer/Seller intactos.
- [x] `AdminPasswordRecoveryRelationalTests`: duas tentativas concorrentes no PostgreSQL
  (Testcontainers) com a mesma chave → exatamente um `Succeeded` e um `KeyAlreadyUsed`. Autoskip
  (log `SKIPPED (Docker unavailable)`) quando Docker não está disponível.

## Validação

```powershell
dotnet build backend/Urbeat.sln
dotnet test backend/tests/Urbeat.UnitTests
dotnet test backend/tests/Urbeat.IntegrationTests
git diff --check
pwsh -File scripts/postman/Test-PostmanCollection.ps1
pwsh -File scripts/postman/Test-PostmanCoverage.ps1
```

Resultado (2026-11-13): build sem erros; UnitTests 1403 aprovados; IntegrationTests 378 aprovados.
O teste relacional de concorrência registrou `SKIPPED (Docker unavailable)` neste ambiente (Docker
inativo), sem falha. A divergência preexistente de cobertura Postman não foi alterada nesta
correção.

## Operação

O procedimento completo está na spec. Resumo: gerar a chave fora do repositório, definir
`AdminRecovery__Enabled=true` e `AdminRecovery__Key` apenas no ambiente do servidor, reiniciar,
executar o request uma vez, confirmar a nova senha, remover a chave e desabilitar a feature.

Troca real de secret, reinício e deploy são manuais. Nenhum valor secreto é versionado.
