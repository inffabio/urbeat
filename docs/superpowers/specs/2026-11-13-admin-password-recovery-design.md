# Recuperação operacional da senha do administrador

**Data:** 2026-11-13
**Status:** implementado
**Escopo:** backend .NET 9 (`Domain -> Application -> Infrastructure -> WebApi`), coleção Postman e documentação operacional.

## Contexto

O `change-password` exige a senha atual e o `forgot-password`/`reset-password` só operam
credenciais de `Customer`/`Seller` (`UserRoleCredentials`). O administrador usa o hash legado de
`AspNetUsers` e não tinha caminho de recuperação quando a senha era desconhecida.

Este fluxo é um **break-glass operacional**: não substitui o login normal, não cria uma senha
padrão e não altera o `AdminUserSeeder`.

## Não objetivos

- Não executar deploy, não alterar OCI Vault/secrets e não versionar chaves.
- Não permitir redefinir `Customer`/`Seller` por este endpoint.
- Não fazer o `AdminSeed:Password` sobrescrever a senha de um administrador existente.
- Não expor a chave em body, query, logs ou respostas.

## Contrato

`POST /api/auth/admin/recover-password`

- **Auth:** No Auth (o administrador está bloqueado), autorizado exclusivamente pelo header
  `X-Admin-Recovery-Key`.
- **Body:**

```json
{
  "email": "{{adminEmail}}",
  "newPassword": "{{newAdminPassword}}",
  "confirmPassword": "{{newAdminPasswordConfirm}}"
}
```

- **Chave:** somente no header, um único valor. Chave em body/query é ignorada e a requisição é
  tratada como sem chave.
- **Alvo:** exclusivamente o e-mail configurado em `AdminSeed:Email`, que é **obrigatório** para
  esta feature, e somente conta com role `Admin`. Sem `AdminSeed:Email` (vazio ou nulo) toda
  chamada com chave válida recebe `403`, antes de qualquer consulta a usuários; não existe fallback
  para "qualquer conta Admin". E-mail desconhecido, conta sem role `Admin` e endereço diferente do
  configurado recebem a mesma resposta genérica.
- **Senha:** validada pelas regras de senha do Identity e pelo validador do endpoint (mínimo 8,
  maiúscula, minúscula, número e caractere especial).

### Respostas

| Situação | HTTP |
| --- | --- |
| Sucesso (sem credenciais no corpo) | `204 No Content` |
| Feature desabilitada ou chave não configurada | `404 Not Found` |
| Header ausente, duplicado ou chave inválida | `401 Unauthorized` |
| Alvo não é o administrador permitido | `403 Forbidden` |
| Chave já consumida por recuperação bem-sucedida | `409 Conflict` |
| Payload ou senha inválidos | `400 Bad Request` |
| Limite de tentativas excedido | `429 Too Many Requests` |

O `404` é intencional: uma implantação sem a feature não revela que a rota existe.

## Configuração

```json
"AdminRecovery": {
  "Enabled": false
}
```

- `AdminRecovery:Enabled` é `false` por padrão no `appsettings.json`.
- `AdminSeed:Email` é obrigatório: é o único alvo aceito pela recuperação. Se estiver vazio/nulo
  (por exemplo, uma sobrescrita de ambiente apagando o valor), a feature continua respondendo
  `404` quando desabilitada e `403` para qualquer e-mail quando habilitada — nunca há fallback para
  outra conta `Admin`. O `AdminUserSeeder` mantém o próprio default de desenvolvimento; a
  obrigatoriedade vale para o alvo da recuperação.
- `AdminRecovery:Key` **não tem valor padrão** e não é versionada. Deve ser fornecida apenas pelo
  ambiente/secret store no momento da operação (`AdminRecovery__Key`).
- A feature só fica disponível quando `Enabled=true` **e** a chave tem entre 32 e 512 caracteres.
  Configuração incompleta é tratada como indisponível (`404`), sem detalhes.
- A comparação da chave é feita sobre SHA-256 dos dois valores com
  `CryptographicOperations.FixedTimeEquals`, normalizando o tamanho e evitando comparação sensível
  ao tempo.
- O limite dedicado é `RateLimiting:AdminRecoveryPermitLimit` /
  `RateLimiting:AdminRecoveryWindowSeconds` (padrão 5 por 900 s), particionado por IP.

## Uso único autoritativo

A tabela `AdminRecoveryKeyUsages` guarda somente:

- `KeyFingerprint`: SHA-256 hexadecimal da chave configurada (nunca a chave);
- `AdminUserId`, `UsedAtUtc`, `IpAddress`;
- `Id`/`CreatedAtUtc` herdados de `BaseEntity`.

O índice único em `KeyFingerprint` é a fonte de verdade entre réplicas e chamadas concorrentes. A
inserção é feita com `INSERT ... ON CONFLICT ("KeyFingerprint") DO NOTHING` dentro da mesma
transação que altera a senha. Assim:

- a primeira recuperação bem-sucedida consome a chave;
- uma segunda chamada concorrente recebe `409`;
- se a alteração falhar, a transação relacional é revertida e a chave continua utilizável;
- no provider InMemory (testes), um lock de processo reproduz a exclusão mútua.

Para rotacionar, basta gerar uma nova chave: o fingerprint muda e uma nova recuperação é possível.
A chave anterior permanece consumida.

## Invalidação de sessão

Após aplicar a senha:

- `RevokeAllForUserAsync` revoga todos os refresh tokens do administrador (o login admin é emitido
  sem role, então a revogação por role não seria suficiente);
- `UserManager.ResetPasswordAsync` rotaciona o security stamp do Identity;
- access tokens já emitidos continuam válidos até o vencimento curto do JWT (15 min por padrão),
  mas não podem ser renovados. Esse é o mesmo comportamento dos resets existentes.

## Auditoria e logs

Eventos estruturados sem senha, chave ou token:

- `ADMIN_RECOVERY_INVALID_KEY`
- `ADMIN_RECOVERY_TARGET_REJECTED`
- `ADMIN_RECOVERY_PASSWORD_REJECTED`
- `ADMIN_RECOVERY_KEY_REUSED`
- `ADMIN_RECOVERY_SUCCEEDED`
- `ADMIN_RECOVERY_FAILED`

Também são gravados `AuditLog` com `Event`, `UserId`, `EntityId`, descrição sanitizada e IP. A senha,
a chave e tokens nunca entram na descrição.

## Operação manual (sem valores neste repositório)

1. Gerar uma chave aleatória forte (por exemplo, 32+ bytes de um CSPRNG) fora do repositório.
2. No servidor, definir temporariamente `AdminRecovery__Enabled=true` e
   `AdminRecovery__Key=<chave-gerada>` no ambiente/secret store. Não versionar.
3. Reiniciar a aplicação para carregar a configuração.
4. Executar **uma vez** `POST /api/auth/admin/recover-password` pelo Postman, com
   `X-Admin-Recovery-Key: {{adminRecoveryKey}}` e uma nova senha conhecida.
5. Confirmar `204`, entrar com a nova senha e validar que as sessões antigas não renovam.
6. Desabilitar `AdminRecovery__Enabled` e remover `AdminRecovery__Key` do ambiente/secret store.
7. Se a chave tiver sido exposta, rotacioná-la; ela já não pode ser reutilizada, mas a rotação
   reduz a superfície.
8. Troca de secret, reinício e deploy são **manuais** e não fazem parte deste código.

## Testes

- Unitários (`AdminPasswordRecoveryServiceTests`, provider InMemory): feature desabilitada, chave
  curta/ausente/inválida, alvo `Customer`/desconhecido, **`AdminSeed:Email` ausente/vazio** (nenhuma
  conta é alterada e nenhuma chave é consumida), senha fraca, sucesso com revogação de sessões,
  reuso e concorrência (lock de processo).
- Relacionais (`AdminPasswordRecoveryRelationalTests`, Testcontainers PostgreSQL 16): duas
  tentativas concorrentes com a mesma chave exigem exatamente um `Succeeded` e um
  `KeyAlreadyUsed`, com uma única linha em `AdminRecoveryKeyUsages` e a senha vencedora aplicada.
  É o único teste que exercita o índice único real e o `INSERT ... ON CONFLICT` concorrente.
- **Limitação:** o teste relacional depende de Docker/Testcontainers. Quando o runtime não está
  disponível, o teste registra `SKIPPED (Docker unavailable)` no output e retorna sem falhar,
  seguindo o padrão dos demais testes `Relational` (o xUnit 2 não possui skip dinâmico). Sem Docker
  a garantia de concorrência fica coberta apenas pelo teste InMemory com lock de processo.

### Resultado da execução local (2026-11-13)

- `dotnet build backend/Urbeat.sln`: sucesso, 0 erros.
- `dotnet test backend/tests/Urbeat.UnitTests`: 1403 aprovados, 0 falhas — inclui o novo caso de
  `AdminSeed:Email` ausente/vazio sem alteração de conta.
- `dotnet test backend/tests/Urbeat.IntegrationTests`: 378 aprovados, 0 falhas. O teste relacional
  registrou `SKIPPED (Docker unavailable)` porque o Docker não estava ativo no ambiente; as
  asserções de concorrência no PostgreSQL não foram executadas nesta rodada.

## Migração

A migração `AddAdminRecoveryKeyUsage` cria a tabela e o índice único. O startup relacional já executa
`Database.MigrateAsync()`. Para gerar/recriar em desenvolvimento:

```powershell
cd backend
dotnet ef migrations add AddAdminRecoveryKeyUsage --startup-project src/Urbeat.WebApi --project src/Urbeat.Infrastructure
```

Nunca usar `dotnet ef database update` contra banco de implantação.
