# Gestão de administradores no dashboard administrativo

**Data:** 2026-11-13
**Status:** especificação (não implementado)
**Escopo:** backend .NET 9 (`Domain -> Application -> Infrastructure -> WebApi`) e frontend Angular 20 + Ionic 8.
**Documentos relacionados:** `docs/superpowers/specs/2026-11-13-admin-password-recovery-design.md`,
`docs/superpowers/specs/2026-11-11-role-promotion-email-verification-design.md`.

## 1. Objetivo

Permitir que um administrador autenticado gerencie outros administradores sem nunca conhecer ou
transportar a senha deles. A criação e a redefinição de senha acontecem por **convite/link de
e-mail de uso único**: o convidado define a própria senha e ativa a conta. O backend permanece
autoritativo para autorização, validade do token, bloqueio, remoção e invariantes (incluindo
"nunca deixar a plataforma sem administrador ativo").

A seção **Administradores** vive dentro da tela existente `/admin/configuracoes`, ao lado dos
parâmetros de negócio, sem criar uma nova entrada de menu lateral e sem alterar o contrato de
`AdminUserSeeder`.

## 2. Escopo

### 2.1 Incluído

- Listagem paginada e filtrável de administradores (ativos, bloqueados e convites pendentes).
- Criação por convite de e-mail (sem senha inicial).
- Edição de e-mail.
- Bloqueio e desbloqueio.
- Reenvio e cancelamento de convite.
- Redefinição de senha por e-mail (link de uso único).
- Remoção de administrador.
- Revogação de sessões nas ações em que ela se aplica.
- Auditoria de todas as ações (inclusive as negadas).
- Páginas públicas de aceite de convite e de redefinição de senha.
- Testes unitários, de integração (incluindo um relacional) e de frontend.

### 2.2 Fora de escopo

- Alterar `backend/src/Urbeat.Infrastructure/Identity/AdminUserSeeder.cs` (semântica, e-mail,
  senha padrão e comportamento de idempotência permanecem iguais).
- Alterar o fluxo break-glass `POST /api/auth/admin/recover-password` e
  `IAdminPasswordRecoveryService`.
- Alterar `forgot-password` / `reset-password` / `change-password` de `Customer`/`Seller`.
- Criar tela de consulta de auditoria (o registro é gravado; a leitura operacional continua fora
  do produto).
- Convite de administrador que já é `Customer`/`Seller` (retorna conflito; promover uma conta
  existente exigiria decisão de produto própria).
- Deploy, OCI Vault, `docker/`, Terraform, DOCX, coleção Postman e feature de horários.
- Deep link nativo (Capacitor/Universal Links) para os links de e-mail; o fluxo é web-first.
- MFA, segundo fator, senhas temporárias e "troca obrigatória no próximo login".

## 3. Modelo de dados

Duas entidades novas em `Urbeat.Domain/Entities`, mapeadas em
`Urbeat.Infrastructure/Persistence/Configurations` e registradas em `ApplicationDbContext`.
Nenhuma coluna é adicionada a `AspNetUsers`, para não impactar o modelo do Identity nem o seeder.

### 3.1 `AdminInvitation`

Representa **um link de e-mail** (convite ou redefinição de senha). O valor bruto do token nunca é
persistido: apenas `TokenHash`. O token é derivado deterministicamente do `Id` + chave de assinatura
já provisionada (ver §6.3), então o job de e-mail consegue reconstruí-lo sem guardá-lo em payload.

| Campo | Tipo | Regra |
| --- | --- | --- |
| `Id` | `Guid` | herdado de `BaseEntity` (`Guid.CreateVersion7()`) |
| `UserId` | `Guid?` | alvo; `null` somente quando o convite foi cancelado e a identidade pendente excluída |
| `Email` | `string(254)` | e-mail normalizado (trim + lowercase) usado como destinatário e para exibição |
| `Purpose` | `AdminInvitationPurpose` | `AccountInvitation = 1`, `PasswordReset = 2` |
| `TokenHash` | `string(128)` | `ITokenHasher.Hash(token)` — nunca o token |
| `ExpiresAtUtc` | `DateTime` | UTC; convite 24 h, reset 60 min |
| `ConsumedAtUtc` | `DateTime?` | definido somente após a senha ser aplicada |
| `RevokedAtUtc` | `DateTime?` | cancelamento manual |
| `SupersededAtUtc` | `DateTime?` | substituído por um link mais novo |
| `CreatedByAdminId` | `Guid?` | ator que emitiu; `null` para fluxos públicos futuros |
| `EmailSentAtUtc` | `DateTime?` | preenchido pelo job após o aceite do provedor |
| `EmailSendingAtUtc` | `DateTime?` | lease de envio (5 min) |
| `EmailSendAttemptId` | `Guid?` | dono do lease de envio |
| `ClaimedAtUtc` | `DateTime?` | lease de consumo (2 min) |
| `ClaimedBy` | `Guid?` | dono do lease de consumo |
| `ConcurrencyVersion` | `int` | concorrência otimista, igual a `RolePromotionRequest` |
| `CreatedAtUtc` / `UpdatedAtUtc` | herdados | UTC |

`Email` é um snapshot: se o e-mail do alvo mudar, a linha antiga mantém o endereço usado para
auditoria. Não há FK com `ON DELETE CASCADE` para `AspNetUsers`; `UserId` usa `SetNull` para que o
cancelamento de um convite pendente não apague o rastro da emissão.

### 3.2 `AdminAccountState`

Estado operacional explícito do administrador. Existe para não confundir **bloqueio administrativo**
com o lockout automático do Identity (5 tentativas / 15 min), que continua funcionando
independentemente e não é contado como "bloqueado" na tela.

| Campo | Tipo | Regra |
| --- | --- | --- |
| `Id` | `Guid` | herdado de `BaseEntity` |
| `UserId` | `Guid` | único (1:1 com a identidade) |
| `IsBlocked` | `bool` | fonte de verdade do bloqueio administrativo |
| `BlockedAtUtc` | `DateTime?` | preenchido no bloqueio |
| `BlockedByAdminId` | `Guid?` | ator, para suporte |
| `CreatedAtUtc` / `UpdatedAtUtc` | herdados | UTC |

Uma linha só é criada no primeiro bloqueio; ausência de linha equivale a `IsBlocked = false`.

### 3.3 Índices e migration

- `AdminInvitations.TokenHash`: índice único.
- `AdminInvitations (UserId, Purpose)`: índice único **filtrado** para
  `"ConsumedAtUtc" IS NULL AND "RevokedAtUtc" IS NULL AND "SupersededAtUtc" IS NULL`, garantindo no
  máximo um link ativo por alvo/finalidade.
- `AdminInvitations (Email, Purpose)` e `AdminInvitations (ExpiresAtUtc)`: índices de apoio.
- `AdminAccountStates.UserId`: índice único.

Migration única `AddAdminAccountManagement`, gerada a partir de `backend/`:

```powershell
dotnet ef migrations add AddAdminAccountManagement --startup-project src/Urbeat.WebApi --project src/Urbeat.Infrastructure
```

O startup relacional já executa `Database.MigrateAsync()`. Nunca usar `dotnet ef database update`
contra banco de implantação.

## 4. Estados

### 4.1 Convite

Estado derivado (não existe coluna `State`):

| Estado | Condição | Efeito |
| --- | --- | --- |
| `Pending` | sem `Consumed/Revoked/Superseded` e `ExpiresAtUtc > now` | link válido; se `EmailSentAtUtc = null`, a UI mostra "convite em envio" |
| `Sending` | `Pending` + lease de envio válido (`EmailSendingAtUtc >= now - 5 min`) | link válido; job em andamento |
| `Expired` | sem consumo/revogação e `ExpiresAtUtc <= now` | link rejeitado; permite emitir novo |
| `Accepted` | `ConsumedAtUtc != null` | link consumido, conta ativa |
| `Revoked` | `RevokedAtUtc != null` | link rejeitado (cancelado) |
| `Superseded` | `SupersededAtUtc != null` | link rejeitado (substituído) |

### 4.2 Conta

| Estado | Definição | Login admin |
| --- | --- | --- |
| `Invited` | role `Admin`, `EmailConfirmed = false`, sem senha | negado (ver §6.5) |
| `Active` | role `Admin`, `EmailConfirmed = true`, sem bloqueio | permitido |
| `Blocked` | role `Admin`, `EmailConfirmed = true`, `AdminAccountStates.IsBlocked = true` | negado com `423` + `code: ADMIN_BLOCKED` |
| `Removed` | não possui mais role `Admin` (ou identidade pendente excluída) | não aparece na lista |

**Administrador ativo** (invariante do último admin) = role `Admin` ∧ `EmailConfirmed = true` ∧
`IsBlocked = false`. Convites pendentes e contas bloqueadas **não contam**.

## 5. Contratos HTTP

### 5.1 Convenções

- Rotas autenticadas: `/api/admin/administrators`, controller novo
  `AdminAccountsController` com `[Authorize(Policy = AuthorizationPolicies.AdminOnly)]`.
- Rotas públicas de link: `/api/auth/admin-account/*`, controller novo
  `AdminAccountSetupController`, todas com `[AllowAnonymous]` e
  `[EnableRateLimiting("password-recovery")]`.
- JSON em `camelCase` (padrão do WebApi). Datas em ISO-8601 UTC (`...Z`), nunca horário local.
- `404` para alvo inexistente ou que não possui role `Admin` (não vaza identidades de outros
  papéis). `409` para violação de invariante, com `code` estável.
- Paginação 1-based como em `StoreCustomersQueryDto` (`page` padrão 1, `pageSize` padrão 20, máximo
  100).
- Nenhuma resposta devolve token, hash, senha ou `securityStamp`.

### 5.2 Listagem

`GET /api/admin/administrators?status=all&search=&page=1&pageSize=20`

`status`: `all` (padrão) · `active` · `blocked` · `invited`. `search`: até 120 caracteres,
case-insensitive sobre `Email`. `page`/`pageSize` fora da faixa são normalizados (não geram 400).

`200 OK`

```json
{
  "page": 1,
  "pageSize": 20,
  "totalItems": 2,
  "totalPages": 1,
  "items": [
    {
      "id": "0192f0c4-6a1b-7c3d-8e4f-0a1b2c3d4e5f",
      "email": "admin@urbeat.local",
      "status": "Active",
      "isEmailConfirmed": true,
      "isBlocked": false,
      "isCurrentUser": true,
      "isLastActiveAdmin": true,
      "createdAtUtc": "2026-01-10T12:00:00Z",
      "invitation": null
    },
    {
      "id": "0192f0c4-7b2c-7d4e-9f50-1b2c3d4e5f60",
      "email": "novo.admin@exemplo.com",
      "status": "Invited",
      "isEmailConfirmed": false,
      "isBlocked": false,
      "isCurrentUser": false,
      "isLastActiveAdmin": false,
      "createdAtUtc": "2026-11-13T14:20:00Z",
      "invitation": {
        "id": "0192f0c4-8c3d-7e5f-a061-2c3d4e5f6071",
        "purpose": "AccountInvitation",
        "state": "Pending",
        "expiresAtUtc": "2026-11-14T14:20:00Z",
        "emailSentAtUtc": "2026-11-13T14:20:05Z",
        "createdAtUtc": "2026-11-13T14:20:00Z"
      }
    }
  ]
}
```

- Ordenação determinística: `email ASC` (a paginação precisa ser estável).
- `isCurrentUser` e `isLastActiveAdmin` são **dicas de UI**, nunca autorização.
- `invitation` reflete apenas a `AccountInvitation` ativa; um `PasswordReset` pendente não aparece na
  lista (a linha mostra apenas o status da conta).
- A consulta é feita com `join` em `UserRoles`/`Roles` (role `Admin`), `left join` em
  `AdminAccountStates` e no convite `AccountInvitation` ativo, projetando o DTO direto no SQL. Não
  usar `GetUsersInRoleAsync` no caminho paginado.

### 5.3 Criação (convite)

`POST /api/admin/administrators` · `[EnableRateLimiting("email-resend")]`

```json
{ "email": "novo.admin@exemplo.com" }
```

Ação, em uma única transação:

1. Normaliza o e-mail (trim + lowercase) e valida formato (`EmailAddress`, máx. 254).
2. Se já existe identidade com esse e-mail (qualquer papel) → `409 EMAIL_ALREADY_IN_USE`.
3. Cria `IdentityUser<Guid>` com `UserName = Email`, `EmailConfirmed = false`, **sem senha**
   (`CreateAsync(user)` sem senha) e id `Guid.CreateVersion7()`.
4. Adiciona a role `Admin`.
5. Emite `AdminInvitation` (`Purpose = AccountInvitation`, expira em 24 h) e enfileira o job de
   e-mail com o `Id` do convite.
6. Grava `AuditLog`.

`201 Created`

```json
{
  "administratorId": "0192f0c4-7b2c-7d4e-9f50-1b2c3d4e5f60",
  "email": "novo.admin@exemplo.com",
  "status": "Invited",
  "invitation": {
    "id": "0192f0c4-8c3d-7e5f-a061-2c3d4e5f6071",
    "purpose": "AccountInvitation",
    "state": "Pending",
    "expiresAtUtc": "2026-11-14T14:20:00Z",
    "emailSentAtUtc": null,
    "createdAtUtc": "2026-11-13T14:20:00Z"
  }
}
```

`emailSentAtUtc: null` significa **enfileirado**, não entregue. A UI mostra "convite em envio" até o
próximo carregamento.

Erros: `400` e-mail inválido · `409 EMAIL_ALREADY_IN_USE` · `429`.

### 5.4 Edição de e-mail

`PUT /api/admin/administrators/{id}/email`

```json
{ "email": "novo.endereco@exemplo.com" }
```

- Se o e-mail normalizado for igual ao atual: `200` com o item atual e nenhum efeito colateral
  (idempotente).
- `404 ADMIN_NOT_FOUND` se o alvo não tem role `Admin`.
- `409 EMAIL_ALREADY_IN_USE` se o endereço pertence a outra identidade (comparação normalizada).
- Se o alvo está `Invited`: supersede o convite ativo e emite um novo `AccountInvitation` para o
  novo endereço (sem isso o convidado não teria link válido). Resposta `200` com
  `invitation.state = "Pending"`.
- Se o alvo está `Active`/`Blocked`: aplica imediatamente em `Email` e `UserName`, mantém
  `EmailConfirmed = true`, revoga **todos** os refresh tokens do alvo e rotaciona o
  `securityStamp`. O bloqueio, se existir, é preservado. Qualquer `PasswordReset` pendente é
  marcado como `SupersededAtUtc`, para que um link já emitido não continue válido para um endereço
  que o administrador não controla mais.
- Envia dois avisos **somente nos casos `Active`/`Blocked`**: um para o endereço antigo ("seu e-mail
  de acesso foi alterado, se não foi você procure outro administrador") e outro para o novo. Falha
  de e-mail não desfaz a alteração (é assíncrona e não transacional); o resultado é auditado.

`200 OK` → mesmo shape de item da listagem (§5.2).

Erros: `400` e-mail inválido · `404` · `409 EMAIL_ALREADY_IN_USE` · `429`.

### 5.5 Reenvio e cancelamento de convite

`POST /api/admin/administrators/{id}/invitation/resend` · `[EnableRateLimiting("email-resend")]`

- Só vale para `Purpose = AccountInvitation`.
- Convite `Pending` (mesmo com e-mail ainda não enviado): **reutiliza** o mesmo convite e o mesmo
  link (não invalida um link já entregue), apenas reenfileira a entrega quando
  `EmailSentAtUtc = null` ou quando o último envio falhou (lease expirado). Se já foi entregue e
  ainda está pendente, reenvia com um novo `EmailSendAttemptId` (Message-Id distinto).
- Convite `Expired`: marca o antigo como `SupersededAtUtc` e cria um novo, com novo `Id` (logo,
  novo link) e nova expiração de 24 h.
- `409 INVITATION_NOT_PENDING` se o convite já foi aceito, revogado ou a conta já está ativa.

`202 Accepted`

```json
{
  "invitationId": "0192f0c4-8c3d-7e5f-a061-2c3d4e5f6071",
  "expiresAtUtc": "2026-11-14T14:20:00Z",
  "emailDeliveryPending": true
}
```

`DELETE /api/admin/administrators/{id}/invitation`

- `204 No Content`.
- Marca `RevokedAtUtc`, encerra leases e, se a conta **nunca foi ativada**
  (`EmailConfirmed = false` e sem senha), exclui a identidade pendente e atribui `UserId = null` ao
  convite (a auditoria guarda o e-mail e o id original).
- Se a conta já foi ativada → `409 ADMIN_NOT_ACTIVE` (use bloqueio/remoção).
- Repetir o cancelamento de um convite já revogado retorna `409 INVITATION_NOT_PENDING` (o cliente
  deve recarregar a lista; não é falha de servidor).

### 5.6 Redefinição de senha por e-mail

`POST /api/admin/administrators/{id}/password-reset` · `[EnableRateLimiting("email-resend")]`

- Emite `AdminInvitation` com `Purpose = PasswordReset`, expiração de **60 minutos**.
- Só é permitido para conta já ativada (`EmailConfirmed = true`). Para um convite pendente,
  `409 ADMIN_NOT_ACTIVE`: o caminho correto é reenviar o convite.
- Se já existe um reset `Pending` e não expirado, **reutiliza** (não gera link novo) e reenvia
  apenas se a entrega ainda não foi confirmada. Isso evita que dois cliques invalidem o link que o
  administrador já recebeu.
- Revoga **imediatamente** todos os refresh tokens do alvo (`RevokeAllForUserAsync`, pois o login
  admin é emitido com `Role = null`) e rotaciona o `securityStamp` na emissão. Assim uma sessão
  possivelmente comprometida para de renovar antes mesmo de o dono clicar.
- Não desbloqueia: se o alvo está `Blocked`, continua bloqueado após concluir a nova senha.
- Remover a senha antiga não acontece aqui; a troca ocorre no consumo do link (§5.9).

`202 Accepted`

```json
{
  "invitationId": "0192f0c4-9d4e-7f60-b172-3d4e5f607182",
  "expiresAtUtc": "2026-11-13T15:20:00Z",
  "emailDeliveryPending": true
}
```

Erros: `404 ADMIN_NOT_FOUND` · `409 ADMIN_NOT_ACTIVE` · `429`.

### 5.7 Bloqueio e desbloqueio

`POST /api/admin/administrators/{id}/block`

```json
{ "reason": "Desligamento do colaborador" }
```

`reason` é opcional (máx. 200 caracteres), sanitizado e usado **apenas** na descrição da auditoria.

- `400 SELF_ACTION_NOT_ALLOWED` para o próprio administrador autenticado.
- `404 ADMIN_NOT_FOUND` se o alvo não tem role `Admin`.
- `409 LAST_ACTIVE_ADMIN` quando o alvo é o único administrador ativo (§10).
- `409 ADMIN_ALREADY_BLOCKED` se já estiver bloqueado.
- `409 ADMIN_NOT_ACTIVE` se o alvo é um convite pendente (não há sessão nem senha para bloquear).
- Efeito: `AdminAccountStates.IsBlocked = true`, `BlockedAtUtc`, `BlockedByAdminId`; revoga todos os
  refresh tokens; rotaciona o `securityStamp`; audita.

`200 OK` → item da listagem com `isBlocked: true`.

`POST /api/admin/administrators/{id}/unblock`

- `409 ADMIN_NOT_BLOCKED` se não estiver bloqueado; `404` se não for admin.
- Efeito: `IsBlocked = false`, limpa `BlockedAtUtc/BlockedByAdminId`. Também limpa o lockout
  automático do Identity (`SetLockoutEndDateAsync(null)` + `ResetAccessFailedCountAsync`), pois o
  desbloqueio administrativo é a ação de suporte para um administrador travado.
- **Não** restaura sessões: o alvo precisa fazer login de novo.

### 5.8 Remoção

`DELETE /api/admin/administrators/{id}?reason=...`

- `400 SELF_ACTION_NOT_ALLOWED`, `409 LAST_ACTIVE_ADMIN`, `404 ADMIN_NOT_FOUND`.
- Se a conta **nunca foi ativada** (convite pendente): exclui a identidade pendente e revoga o
  convite (mesmo comportamento de §5.5).
- Se a conta está ativada: remove **somente** a role `Admin`, preservando a identidade e eventuais
  roles `Customer`/`Seller` (lojas, pedidos e dados de loja nunca são apagados). Revoga todos os
  refresh tokens e rotaciona o `securityStamp` antes de remover o papel, para que um access token
  ainda válido não consiga renovar nem voltar a operar como admin.
- Efeito colateral aceito: um access token JWT já emitido continua aceito até expirar (15 min),
  porque a política `AdminOnly` lê apenas o claim de role. Mesmo comportamento residual dos resets
  existentes (§14, risco R2).
- `204 No Content`.

### 5.9 Endpoints públicos do link

Todos `POST` (token no **corpo**, nunca em path/query, para não cair em logs de acesso), com
`[AllowAnonymous]` e `[EnableRateLimiting("password-recovery")]`.

`POST /api/auth/admin-account/invitation/validate`

```json
{ "token": "<token-opaco>" }
```

`200 OK` (sempre 200 para não transformar a validação em oráculo de existência):

```json
{
  "valid": true,
  "purpose": "AccountInvitation",
  "emailMasked": "n***@exemplo.com",
  "expiresAtUtc": "2026-11-14T14:20:00Z"
}
```

Inválido/expirado/consumido/revogado/substituído/finalidade errada:

```json
{ "valid": false, "message": "Este link é inválido ou expirou. Peça um novo convite a um administrador." }
```

`POST /api/auth/admin-account/invitation/accept`

```json
{ "token": "<token-opaco>", "newPassword": "...", "confirmPassword": "..." }
```

- Valida o token, toma o lease de consumo (§10), aplica a senha e marca o convite como consumido.
- Efeitos: `PasswordHash` definido pelo hasher configurado (Argon2id), `EmailConfirmed = true`,
  `AccessFailedCount = 0`, lockout automático limpo, `securityStamp` rotacionado (via
  `ResetPasswordAsync` com token do Identity gerado e consumido **em memória**, nunca enviado por
  e-mail). **Não** altera `AdminAccountStates.IsBlocked`.
- Revoga refresh tokens do alvo (defensivo; um convite recém-aceito não tem sessão).
- `200 OK` → `{ "succeeded": true, "purpose": "AccountInvitation", "message": "Conta ativada. Entre com sua nova senha." }`
- `400 WEAK_PASSWORD` (regras de §6.4) · `400 INVALID_OR_EXPIRED_TOKEN` (genérico) ·
  `409 TOKEN_ALREADY_USED` · `429`.

`POST /api/auth/admin-account/password-reset/validate` e
`POST /api/auth/admin-account/password-reset/confirm` têm o mesmo shape, com
`purpose = PasswordReset`. O `confirm`:
- aplica a nova senha, limpa lockout automático, **não** desbloqueia e exige que a conta já esteja
  com `EmailConfirmed = true` (caso contrário o token é tratado como `INVALID_OR_EXPIRED_TOKEN`);
- revoga todos os refresh tokens novamente (a revogação da emissão pode ter sido perdida por um
  restart);
- `200 OK` → `{ "succeeded": true, "purpose": "PasswordReset", "message": "Senha atualizada. Entre com a nova senha." }`.

Um token de `AccountInvitation` nunca é aceito em rota de `PasswordReset` e vice-versa: a finalidade
faz parte da derivação e é reconferida na linha persistida.

### 5.10 Códigos de erro

| `code` | HTTP | Significado |
| --- | --- | --- |
| `EMAIL_ALREADY_IN_USE` | 409 | endereço já pertence a uma identidade |
| `ADMIN_NOT_FOUND` | 404 | id inexistente ou sem role `Admin` |
| `ADMIN_NOT_ACTIVE` | 409 | convite pendente onde a ação exige conta ativa |
| `ADMIN_ALREADY_BLOCKED` | 409 | bloqueio repetido |
| `ADMIN_NOT_BLOCKED` | 409 | desbloqueio de conta não bloqueada |
| `LAST_ACTIVE_ADMIN` | 409 | a ação deixaria zero administradores ativos |
| `SELF_ACTION_NOT_ALLOWED` | 400 | bloquear/remover a própria conta |
| `INVITATION_NOT_PENDING` | 409 | convite já aceito, revogado ou substituído |
| `INVALID_OR_EXPIRED_TOKEN` | 400 | token inválido/expirado/revogado/substituído |
| `TOKEN_ALREADY_USED` | 409 | token já consumido |
| `WEAK_PASSWORD` | 400 | senha fora da política |
| `ADMIN_BLOCKED` | 423 | login de administrador bloqueado |

## 6. Segurança e autorização

### 6.1 Autoridade

- Toda rota de gestão exige `AuthorizationPolicies.AdminOnly`. O ato de o frontend esconder botões é
  apenas UX; nenhuma decisão de autorização depende dele.
- O ator é lido de `ClaimTypes.NameIdentifier`; se o claim faltar ou não existir identidade com role
  `Admin`, a requisição é `403`. Não existe "modo suporte" nem impersonação.
- O alvo é sempre resolvido por id **e** verificação de role `Admin`.

### 6.2 Token

- Composição: `16 bytes` derivados do `Id` do convite (Guid v7) + `32 bytes` de
  `HMACSHA256(chave, "admin-invitation:v1:" || idBytes)` (ou
  `"admin-password-reset:v1:"`), codificados em Base64Url (≈64 caracteres, 256 bits de saída HMAC).
- Persistência: somente `ITokenHasher.Hash(token)` (SHA-256 versionado, comparação em tempo
  constante). O valor bruto existe apenas em memória, no job de envio e no navegador do destinatário.
- Domínio separado por finalidade, para que um token de convite não valide em reset nem em
  promoção de função.
- Nunca aparece em: resposta HTTP, `AuditLog`, Serilog, argumento de job Hangfire, payload de outbox,
  query string ou path. O link usa **fragmento** (`#token=...`), que não é enviado ao servidor,
  `Referer` ou log de proxy; a página lê e remove o fragmento com `Location.replaceState`. O
  `UseSerilogRequestLogging` existente registra método/rota/status, não o corpo da requisição.

### 6.3 Chave de assinatura

Reutiliza `RolePromotionChallenge:SigningKey` (seção já validada com `ValidateOnStart`, sem default,
fornecida por ambiente/secret store como `RolePromotionChallenge__SigningKey`), por meio de
`IOptions<RolePromotionChallengeOptions>`. Justificativa: a especificação não pode introduzir um novo
segredo sem tocar deploy/Vault, e a separação de domínio garante independência criptográfica entre os
links. Rotação dessa chave invalida convites/resets de admin e promoções pendentes ao mesmo tempo
(ver R1).

### 6.4 Senha

- Política do Identity já configurada (mínimo 8, maiúscula, minúscula, dígito) e hash Argon2id.
- `newPassword` e `confirmPassword` devem ser iguais; máximo 128 caracteres; validador FluentValidation.
- A senha nunca é devolvida, logada, auditada ou enviada por e-mail. O e-mail apenas direciona para a
  página onde o próprio usuário a define.

### 6.5 Login de administrador

`LoginLegacyAsync` ganha uma verificação adicional depois de confirmar a role `Admin`: consulta
`AdminAccountStates`; se `IsBlocked`, retorna `IsBlocked = true` (novo campo em `LoginResultDto`) e
o `AuthController` responde `423 Locked` com `code = "ADMIN_BLOCKED"`. Sucesso, lockout e
credenciais inválidas mantêm exatamente o comportamento atual.

Um convite pendente não tem senha, portanto `CheckPasswordAsync` falha e o login continua
retornando `401` genérico — sem revelar que o endereço foi convidado.

### 6.6 Sessões

| Ação | Refresh tokens | `securityStamp` |
| --- | --- | --- |
| Bloqueio | revogados (todos) | rotacionado |
| Desbloqueio | não altera | não altera |
| Alteração de e-mail | revogados (todos) | rotacionado |
| Pedido de reset | revogados (todos) | rotacionado |
| Conclusão de reset/convite | revogados (todos, defensivo) | rotacionado |
| Remoção | revogados (todos) | rotacionado |

## 7. Fluxo de e-mail

### 7.1 Configuração

Nova seção **não secreta** `AdminAccount` (`AdminAccountOptions`, `ValidateOnStart`):

```json
"AdminAccount": {
  "InvitationLifetimeHours": 24,
  "PasswordResetLifetimeMinutes": 60,
  "InvitationPath": "/convite-admin",
  "PasswordResetPath": "/redefinir-senha-admin",
  "ClaimLeaseMinutes": 2,
  "EmailSendLeaseMinutes": 5,
  "MaxPageSize": 100
}
```

Faixas validadas: convite 1–168 h; reset 5–1440 min; leases 1–15 min; paths começando com `/` e sem
`?`/`#`.

### 7.2 Job

Novo `SendAdminAccountEmailJob` em `Urbeat.Infrastructure/Jobs`, `[Queue(HangfireQueues.Communications)]`,
payload = **somente** `Guid invitationId` (nunca token nem e-mail):

1. `TryClaimEmailDeliveryAsync(invitationId)` — retorna `null` se já foi enviado, consumido,
   revogado, expirou ou outro worker tem lease válido.
2. Reconstrói o token a partir do `Id` + chave e monta a URL com fragmento:
   - convite: `{FrontendBaseUrl}{InvitationPath}#token={token}`
   - reset: `{FrontendBaseUrl}{PasswordResetPath}#token={token}`
3. Renderiza `EmailTemplates.BuildAdminInvitation` / `BuildAdminPasswordReset` (mesmo `BaseLayout`,
   com prazo explícito e aviso "o urbeat nunca solicita sua senha por e-mail").
4. `IEmailService.SendAsync` com
   `idempotencyKey = "admin-{purpose-lower}-{invitationId:N}-{attemptId:N}"`.
5. `MarkEmailSentAsync` ou, em falha, libera o lease e relança para retry do Hangfire. O log registra
   apenas `InvitationId`/`Purpose`.

### 7.3 Recuperação

Não há sweep periódico novo. O caminho de recuperação é o **reenvio manual** (§5.5), que considera
`EmailSentAtUtc = null` e reenfileira. Perder o enqueue deixa o convite em `emailSentAtUtc: null`,
visível na lista como "convite em envio" — nunca como sucesso silencioso.

### 7.4 Templates

Novos métodos em `EmailTemplates`:

- `BuildAdminInvitation(acceptUrl, expiresHours)` — assunto "Você foi convidado para administrar o
  urbeat"; explica que **nenhuma senha foi criada**, que o link expira em 24 h e é de uso único.
- `BuildAdminPasswordReset(resetUrl, expiresMinutes)` — assunto "Redefinição de senha de
  administrador"; 60 min; aviso para ignorar se não solicitou.

URL sempre `WebUtility.HtmlEncode` no botão (padrão do arquivo). Nenhum token em texto alternativo,
preheader ou rodapé.

## 8. Frontend/UX

### 8.1 Seção dentro de Configurações

- `/admin/configuracoes` ganha um seletor de seção (`Parâmetros` | `Administradores`) no topo,
  mantendo o título e o cabeçalho atuais. Estado em `?secao=administradores` (deep link), padrão
  `parametros` para não quebrar o comportamento atual nem os testes existentes.
- `Administradores` só busca a lista quando a seção é ativada (lazy), para não adicionar requisições
  ao carregamento de parâmetros.
- Não há novo item no menu lateral (`AdminShellComponent.menuItems` permanece igual). A rota
  `app.routes.ts` de `/admin` continua com exatamente os mesmos filhos — `app.routes.admin.spec.ts`
  exige a lista atual.

### 8.2 Componente da seção

Novo `AdminAdministratorsSectionComponent` em
`frontend/src/app/features/admin-settings/administrators/` (standalone, `OnPush`, `inject()`,
Signals):

- Estado: `items`, `totalItems`, `page`, `loading`, `error`, `busyId`, `feedback`, `filters`
  (`status`, `search`).
- Busca com debounce de 300 ms; troca de filtro volta para `page = 1`.
- Lista em **cards**, não tabela: cada linha mostra e-mail, badge de status e ações. Em telas
  ≥ 900 px o card vira grade horizontal (e-mail | status | ações); abaixo disso empilha. Sem scroll
  horizontal.
- Ações por linha (todas com alvo de toque ≥ 44 px e rótulo acessível):
  `Editar e-mail`, `Reenviar convite` e `Cancelar convite` (somente `Invited`),
  `Redefinir senha`, `Bloquear`/`Desbloquear`, `Remover`.
- `Bloquear` e `Remover` ficam desabilitados quando `isCurrentUser` ou `isLastActiveAdmin`, com
  `title`/texto explicando o motivo. A validação real continua no servidor; se o servidor responder
  `409 LAST_ACTIVE_ADMIN`, a mensagem é exibida.
- Formulário "Convidar administrador" (e-mail + botão) no topo da seção. Sucesso → feedback
  `role="status"`, card `Invited` com "expira em 23h".
- Confirmações destrutivas em painel inline `role="alertdialog"` (mesmo padrão de
  `admin-settings-page.component.html`), com foco inicial no botão secundário, `Esc` para cancelar e
  texto que descreve o efeito (ex.: "as sessões deste administrador serão encerradas").
- Estados: `loading` com skeleton, `error` com "Tentar novamente", vazio com orientação para
  convidar, e `feedback` `aria-live="polite"`.
- Erros via `extractApiErrorMessage`; `409`/`400` mostram a mensagem do servidor.

### 8.3 Serviço e modelos

- `frontend/src/app/core/services/admin-account.service.ts` (`inject(ApiService)`), métodos:
  `list(query)`, `invite(email)`, `updateEmail(id, email)`, `resendInvitation(id)`,
  `cancelInvitation(id)`, `requestPasswordReset(id)`, `block(id, reason?)`, `unblock(id)`,
  `remove(id, reason?)`, `validateInvitationToken(token)`, `acceptInvitation(request)`,
  `validatePasswordResetToken(token)`, `confirmPasswordReset(request)`.
- Modelos em `frontend/src/app/shared/models/admin-account.model.ts`, espelhando §5 em camelCase e
  com unions literais (`AdminAccountStatus`, `AdminInvitationState`, `AdminInvitationPurpose`).

### 8.4 Páginas públicas

Duas rotas **top-level**, antes do catch-all `:storePath` em `app.routes.ts`, sem `adminGuard`:

| Rota | Componente | Uso |
| --- | --- | --- |
| `/convite-admin` | `features/admin-invite/admin-invite-page.component.ts` | primeiro acesso |
| `/redefinir-senha-admin` | `features/admin-password-reset/admin-password-reset-page.component.ts` | reset por e-mail |

Fluxo de ambas:

1. Lê `location.hash`, extrai `token` e chama `Location.replaceState` imediatamente (o token fica só
   em memória).
2. Sem token → estado `invalid` com link para `/painel/login`.
3. `POST .../validate`; `valid: false` → mensagem genérica e link para `/painel/login`.
4. Formulário de nova senha (reutiliza `shared/components/password-setup-form/`): campos
   `newPassword`/`confirmPassword`, `autocomplete="new-password"`, botão mostrar/ocultar, dica da
   política, validação local apenas como conveniência.
5. `accept`/`confirm`; `200` → tela de sucesso com botão "Entrar no painel" (`/painel/login`);
   `409 TOKEN_ALREADY_USED` → mensagem "este link já foi usado"; `400` → mensagem do servidor.

Visual: cartão centralizado com a mesma linguagem do login admin, reutilizando os valores das
variáveis `--admin-*` do `admin-shell.component.scss` escopados no próprio componente, sem
`.urbeat-onboarding`. Copy com marca minúscula
(`urbeat`), conforme `branding-consistency.spec.ts`. Alvos ≥ 44 px, foco visível, `aria-live` para
erros e `aria-busy` durante o envio.

### 8.5 Sessão do ator

Se o ator editar o **próprio** e-mail, após o `200` o componente chama `auth.logout()` e navega para
`/painel/login` (as sessões já foram revogadas no servidor). Para ações em terceiros, nada muda na
sessão do ator.

## 9. Auditoria

`AuditLog` (tabela existente) para toda ação **permitida e negada**:

| `Event` | `UserId` (ator) | `EntityId` (alvo) | Descrição |
| --- | --- | --- | --- |
| `AdminInvitationSent` | ator | novo admin | e-mail convidado, expiração, IP |
| `AdminInvitationResent` | ator | alvo | reenvio/reemissão e motivo |
| `AdminInvitationCancelled` | ator | alvo | convite revogado e identidade pendente excluída ou não |
| `AdminInvitationAccepted` | **próprio alvo** | alvo | ativação concluída |
| `AdminEmailChanged` | ator | alvo | e-mail antigo → novo (sem token) |
| `AdminBlocked` | ator | alvo | motivo informado, sessões revogadas |
| `AdminUnblocked` | ator | alvo | — |
| `AdminPasswordResetRequested` | ator | alvo | expiração, sessões revogadas |
| `AdminPasswordResetCompleted` | **próprio alvo** | alvo | — |
| `AdminRemoved` | ator | alvo | `RoleRemoved` ou `PendingInvitationDeleted`, motivo |
| `AdminActionDenied` | ator | alvo | `code` (`LAST_ACTIVE_ADMIN`, `SELF_ACTION_NOT_ALLOWED`, ...) |

- `Entity = nameof(IdentityUser<Guid>)`; `IpAddress` de `HttpContext.Connection.RemoteIpAddress`.
- **Nunca** registrar token, hash, senha, link completo, `securityStamp` ou corpo da requisição. A
  descrição usa apenas e-mail, motivo (sanitizado) e códigos.
- Serilog estruturado equivalente: `ADMIN_INVITATION_SENT`, `ADMIN_INVITATION_ACCEPTED`,
  `ADMIN_EMAIL_CHANGED`, `ADMIN_BLOCKED`, `ADMIN_UNBLOCKED`, `ADMIN_PASSWORD_RESET_REQUESTED`,
  `ADMIN_PASSWORD_RESET_COMPLETED`, `ADMIN_REMOVED`, `ADMIN_ACTION_DENIED`, com
  `UserId`/`TargetId`/`IP`.
- Ações bem-sucedidas gravam a auditoria na **mesma transação** da mutação (se a transação falhar,
  nada é gravado). Negativas (`AdminActionDenied`) são gravadas em `SaveChanges` próprio, depois do
  rollback da mutação, para não se perderem.

## 10. Concorrência e último administrador

### 10.1 Invariante

> Nenhuma operação pode reduzir a contagem de administradores ativos para zero.

Aplicada em bloqueio e remoção. Bloquear/remover quem **não** está ativo (convite pendente ou conta
já bloqueada) não viola a regra.

### 10.2 Verificação atômica

`AdminAccountService` executa, para bloqueio/remoção:

1. Abre transação (`BeginTransactionAsync`) se ainda não houver e o provider for relacional.
2. `pg_advisory_xact_lock(hashtextextended('admin-accounts:last-active', 0))` — mesmo padrão de
   `RolePromotionChallengeService.AcquireCreationLockAsync`, liberado automaticamente no
   commit/rollback.
3. Conta administradores ativos **excluindo o alvo** (role `Admin` + `EmailConfirmed` +
   `!(AdminAccountStates.IsBlocked)`).
4. Se `0` → `409 LAST_ACTIVE_ADMIN`, faz rollback da mutação e grava `AdminActionDenied` **fora** da
   transação (com `ChangeTracker.Clear()` e um `SaveChanges` próprio), para que a negativa sobreviva
   ao rollback.
5. Caso contrário: revoga tokens, rotaciona stamp, aplica mutação, grava auditoria, commit.

Identity store (`UserManager`) e `RevokeAllForUserAsync` usam o mesmo `ApplicationDbContext` escopado,
portanto participam da transação ambiente. Em `InMemory` não há transação: um `SemaphoreSlim` estático
serializa o trecho crítico no processo, reproduzindo a exclusão mútua (mesma limitação documentada em
`AdminPasswordRecoveryService`).

### 10.3 Consumo do token

Duas submissões simultâneas do mesmo link:

1. `TryClaim` condicional (`ClaimedAtUtc`/`ClaimedBy` + lease de 2 min) — apenas uma vence.
2. A vencedora aplica senha e marca `ConsumedAtUtc`; a perdedora recebe `409 TOKEN_ALREADY_USED` (se
   o consumo já foi persistido) ou `400` genérico.
3. Falha antes de aplicar → `ReleaseClaimAsync`, permitindo nova tentativa legítima.

### 10.4 Índice como rede de segurança

O índice único filtrado de §3.3 impede duas linhas ativas para o mesmo alvo/finalidade mesmo se a
lógica de aplicação regredir. Conflito de índice é traduzido para reutilização do convite existente,
nunca para `500`.

## 11. Compatibilidade

- `AdminUserSeeder`: intocado; a conta semeada é `Active` (e-mail confirmado, sem linha de bloqueio).
- `POST /api/auth/login/admin`: sucesso, `401`, `403` e `423` por lockout permanecem iguais. A única
  adição é o `423` com `code: ADMIN_BLOCKED` e o campo aditivo `IsBlocked` em `LoginResultDto`.
- `forgot-password`/`reset-password`/`change-password` de `Customer`/`Seller`,
  `role-promotion/confirm`, confirmação de e-mail e alteração de e-mail self-service não mudam.
- Break-glass `admin/recover-password`, `AdminRecoveryKeyUsages` e OCI Vault não são tocados.
- `/admin/configuracoes` mantém a URL e o carregamento de parâmetros; `?secao=...` é opcional.
- Rotas novas ficam antes de `:storePath`; nenhuma rota existente muda de posição.
- `app.routes.admin.spec.ts` (filhos exatos de `/admin`) e
  `admin-settings-page.component.spec.ts` (parâmetros no `ngOnInit`) continuam passando.
- PWA/Capacitor: nenhuma mudança de build, `webDir`, permissões ou deep link nativo. Os links abrem
  no navegador.
- Postman, DOCX, `docker/`, deploy e feature de horários: fora deste escopo.

## 12. Testes

### 12.1 Unitários (backend, InMemory)

`backend/tests/Urbeat.UnitTests/Infrastructure/Identity/AdminAccountServiceTests.cs`

- convite: normaliza e-mail, cria identidade sem senha e `EmailConfirmed = false`, atribui role
  `Admin`, uma única linha ativa, job enfileirado, auditoria sem token;
- convite de e-mail existente → `EMAIL_ALREADY_IN_USE` e nenhuma escrita;
- reenvio de convite `Pending` reutiliza o mesmo `Id` (link não muda);
- reenvio de convite expirado cria novo `Id` e supersede o anterior;
- cancelamento revoga e exclui identidade pendente (`UserId` vira `null`);
- cancelamento de conta ativa → `409 ADMIN_NOT_ACTIVE`;
- edição de e-mail: normaliza, revoga refresh tokens, rotaciona stamp, mantém bloqueio, reemite
  convite quando `Invited`, audita sem token;
- bloqueio: grava `AdminAccountStates`, revoga tokens, rotaciona o stamp, audita;
  `SELF_ACTION_NOT_ALLOWED`; `LAST_ACTIVE_ADMIN`; `ADMIN_ALREADY_BLOCKED`;
- desbloqueio: limpa estado e lockout automático, não restaura sessão;
- remoção: modo `RoleRemoved` preserva roles `Customer`/`Seller`; modo
  `PendingInvitationDeleted` exclui a identidade; `LAST_ACTIVE_ADMIN`; `SELF_ACTION_NOT_ALLOWED`;
- pedido de reset: TTL de 60 min, revoga tokens na emissão, reutiliza pedido ativo, não desbloqueia;
- conclusão de reset de conta bloqueada mantém `IsBlocked = true`;
- login de admin bloqueado retorna `IsBlocked` (e `AuthController` → `423`/`ADMIN_BLOCKED`);
- concorrência InMemory: dois bloqueios simultâneos dos dois últimos admins → exatamente um sucesso.

`backend/tests/Urbeat.UnitTests/Infrastructure/Identity/AdminInvitationChallengeServiceTests.cs`

- token derivado é determinístico para o mesmo `Id`, difere entre finalidades e nunca é persistido;
- `TokenHash` confere com `ITokenHasher.Verify`; hash não contém o token;
- `validate` rejeita expirado, consumido, revogado, superseded e finalidade trocada com a mesma
  mensagem genérica;
- leases de consumo e de envio: expiração, tomada por outro worker, `Release` idempotente.

### 12.2 Integração (WebApi + InMemory)

`backend/tests/Urbeat.IntegrationTests/Api/AdminAccountsFlowTests.cs`

- `GET/POST/PUT/DELETE` sem token → `401`; com token `Seller`/`Customer` → `403`;
- fluxo completo: convite → job (executado no teste) → validação → aceite → login admin → aparece
  `Active`;
- link de convite aceito não pode ser reutilizado (`409`);
- último admin ativo: bloqueio e remoção → `409 LAST_ACTIVE_ADMIN`;
- nenhuma resposta contém `token`, `tokenHash`, `password` ou `secret` (asserção sobre o corpo);
- `AuditLogs` contém o ator, o alvo e nenhum segredo;
- rate limit: estourar o bucket `password-recovery` nos endpoints públicos → `429`.

### 12.3 Relacional (Testcontainers PostgreSQL 16)

`backend/tests/Urbeat.IntegrationTests/Relational/AdminAccountsConcurrencyRelationalTests.cs`

- índice único filtrado impede dois convites ativos para o mesmo alvo/finalidade;
- duas remoções concorrentes dos dois últimos administradores → exatamente uma `204` e uma
  `409 LAST_ACTIVE_ADMIN`, com um admin ativo remanescente;
- dois aceites concorrentes do mesmo token → exatamente um `200` e um `409`, com uma senha aplicada.
- Auto-skip quando Docker não estiver disponível, seguindo o padrão dos testes `Relational`.

### 12.4 Frontend (Jest)

- `admin-account.service.spec.ts`: métodos/verbos/URLs corretos e mapeamento de modelos.
- `admin-administrators-section.component.spec.ts`: loading/vazio/erro, filtros com debounce,
  convite, badges, ações desabilitadas para `isCurrentUser`/`isLastActiveAdmin`, `409` exibido,
  confirmação com `role="alertdialog"` e foco.
- `admin-invite-page.component.spec.ts` e `admin-password-reset-page.component.spec.ts`: leitura e
  remoção do fragmento (`Location.replaceState` chamado), token ausente, `valid: false`, senha fraca,
  `TOKEN_ALREADY_USED`, sucesso leva a `/painel/login`.
- `password-setup-form.component.spec.ts`: validação, mostrar/ocultar, `autocomplete`, `aria`.
- `app.routes.admin.spec.ts`: filhos de `/admin` inalterados; `/convite-admin` e
  `/redefinir-senha-admin` existem antes de `:storePath` e sem `adminGuard`.
- `admin-settings-page.component.spec.ts`: seção padrão continua `Parametros` e não chama a API de
  administradores no `ngOnInit`.

### 12.5 Validação prevista (executar na implementação)

```powershell
dotnet build backend/Urbeat.sln
dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~AdminAccount|FullyQualifiedName~AdminInvitation"
dotnet test backend/tests/Urbeat.IntegrationTests --filter "FullyQualifiedName~AdminAccounts"
cd frontend
npx jest --no-coverage src/app/features/admin-settings/administrators/admin-administrators-section.component.spec.ts
npx jest --no-coverage src/app/features/admin-invite/admin-invite-page.component.spec.ts
npx jest --no-coverage src/app/features/admin-password-reset/admin-password-reset-page.component.spec.ts
npx jest --no-coverage src/app/app.routes.admin.spec.ts
npx ng build --configuration production
```

## 13. Critérios de aceite

1. `/admin/configuracoes` exibe a seção **Administradores**; `?secao=administradores` abre direto nela;
   o menu lateral e as rotas de `/admin` não mudam.
2. Administrador autenticado cria um novo administrador informando apenas o e-mail; nenhuma senha é
   recebida ou exibida em qualquer etapa.
3. O convite contém link para `/convite-admin#token=...`, expira em 24 h, é de uso único e o banco
   guarda apenas o hash do token.
4. O convidado define a própria senha, a conta passa a `EmailConfirmed = true` e o login admin passa a
   funcionar; o link não pode ser reutilizado.
5. A listagem mostra ativos, bloqueados e convites pendentes com estado, expiração e ações coerentes,
   paginada e com busca por e-mail.
6. É possível editar o e-mail; endereço em uso retorna `409` e a alteração revoga as sessões do alvo.
7. Bloqueio revoga sessões e impede login (`423`/`ADMIN_BLOCKED`); desbloqueio permite login novamente
   sem restaurar sessões antigas.
8. Reenviar convite não invalida um link pendente já entregue; cancelar invalida o link e remove a
   identidade pendente.
9. Redefinição de senha por e-mail expira em 60 min, é de uso único, revoga sessões na emissão e ao
   concluir, e não desbloqueia conta bloqueada.
10. É impossível bloquear, remover ou se auto-bloquear/auto-remover quando isso deixaria zero
    administradores ativos, inclusive sob requisições concorrentes.
11. Remover um administrador ativado preserva a identidade e eventuais roles `Customer`/`Seller`.
12. Toda ação (permitida ou negada) gera `AuditLog` com ator, alvo e IP, sem token, hash ou senha.
13. Todos os endpoints de gestão exigem `AdminOnly`; um token `Seller`/`Customer` recebe `403`; o
    frontend não é fonte de autorização.
14. `AdminUserSeeder`, break-glass, `forgot-password` de Customer/Seller, DOCX, Postman, deploy e
    horários permanecem inalterados.
15. Build de produção e as suítes de backend/frontend listadas em §12.5 passam.

## 14. Riscos

- **R1 — Chave compartilhada com promoção de função.** Reutilizar
  `RolePromotionChallenge:SigningKey` evita um novo segredo/deploy, mas rotacioná-la invalida links de
  admin e promoções pendentes ao mesmo tempo. Mitigação: finalidades separadas por domínio,
  reemissão disponível na UI; limpeza futura (fora deste escopo) seria renomear a seção para um nome
  neutro de "links de e-mail".
- **R2 — Janela residual do access token.** Bloqueio/remoção não invalidam JWT já emitido (≈15 min),
  pois a política lê apenas o claim de role. É o mesmo comportamento dos resets existentes; revogar
  por requisição exigiria checar estado em cada chamada. Documentado como aceito.
- **R3 — Edição de e-mail sem confirmação no novo endereço.** Um typo pode trancar o alvo fora.
  Mitigação: normalização, unicidade, aviso para os dois endereços, auditoria e outro administrador
  pode corrigir.
- **R4 — Entrega de e-mail at-least-once.** SMTP não deduplica; o `Message-Id` determinístico reduz
  duplicatas. Um reenvio manual pode gerar dois e-mails, mas apenas um link válido por vez.
- **R5 — Sem sweep de recuperação.** Perder o enqueue deixa o convite sem e-mail até um reenvio
  manual. Aceito para manter o escopo pequeno; a lista expõe `emailSentAtUtc: null`.
- **R6 — Diferença InMemory × PostgreSQL.** A garantia forte de concorrência só é exercitada no teste
  relacional; sem Docker ela fica coberta apenas pelo lock de processo.
- **R7 — Convite para e-mail de conta existente.** Retorna `409` por decisão de escopo. Se o produto
  quiser promover um `Customer`/`Seller` a admin, será necessário um fluxo de confirmação próprio.
- **R8 — Auditoria de ações negadas pode crescer.** Um atacante autenticado pode gerar muitas linhas
  `AdminActionDenied`; o rate limiter dos endpoints de e-mail mitiga abuso, mas o volume deve ser
  observado.
- **R9 — Remoção apenas da role `Admin`.** Uma identidade sem roles permanece em `AspNetUsers`
  (inerte para login). Apagar exigiria avaliar FKs de pedidos/lojas; não é feito aqui.
- **R10 — Rate limit por IP.** Escritórios atrás de NAT compartilham o bucket
  `password-recovery`/`email-resend`; um pico pode gerar `429` legítimo.
