# Acesso administrativo ao dashboard de qualquer loja

**Data:** 2026-11-13
**Status:** especificação (não implementado)
**Escopo:** backend .NET 9 (`Domain -> Application -> Infrastructure -> WebApi`) e frontend Angular 20 + Ionic 8.
**Documentos relacionados:** `docs/superpowers/specs/2026-11-13-admin-management-design.md`,
`docs/superpowers/specs/2026-11-13-store-deletion-cloudinary-design.md`,
`docs/superpowers/specs/2026-08-15-wizard-dashboard-separation-design.md` (wizard versus dashboard),
`DESIGN.md`.

## 1. Objetivo

Permitir que um administrador autenticado encontre qualquer loja da plataforma e entre no painel
operacional dela para **ver e editar** cardápio, configurações, entregas, pedidos e publicação, sem
usar a senha do lojista, sem receber um token de lojista e sem impersonação silenciosa.

O acesso é um **contexto explícito de delegação**: o servidor registra `AdminUserId`, `StoreId`,
`ContextId`, expiração curta e origem. Toda requisição delega autorização ao mesmo conjunto de
serviços já usados pelo lojista; a diferença é apenas *quem* é o ator e *qual* loja está no escopo.
O comportamento do lojista e do cliente não muda.

Princípios não negociáveis:

1. O backend é autoritativo. A UI esconder um botão nunca autoriza nem desautoriza nada.
2. O contexto é identificado no servidor (`AdminUserId` + `StoreId` + `ContextId`), não inferido do
   frontend, do corpo da requisição ou de query string.
3. Nenhum token de lojista é emitido, trocado, armazenado ou reutilizado para o administrador.
4. Um contexto vale para **uma** loja. Não existe contexto "todas as lojas".
5. Ações fora do escopo aprovado continuam exigindo `SellerOnly` e falham fechado (`403`).
6. Toda concessão, uso relevante, encerramento e negativa gera auditoria sem segredos.

## 2. Escopo

### 2.1 Incluído

- Item lateral **Lojas** no shell `/admin`.
- Rota `/admin/lojas` com grid paginada de lojas, busca e filtros por publicação, abertura,
  assinatura e ciclo de vida.
- Colunas: logo (ou placeholder), nome, slug, proprietário, status, publicação, abertura e
  assinatura.
- Abertura de um contexto administrativo por loja e entrada no dashboard existente do lojista
  (`/app/*`) com o mesmo conjunto de telas.
- Acesso de visualização e edição, dentro do contexto, a: informações da loja, endereço, horários,
  bairros/taxas de entrega, categorias, produtos, adicionais, pedidos e publicação.
- Inclusão de lojas em onboarding (criadas pelo wizard, `IsPublished = false`) desde que exista
  linha persistida em `Stores`.
- Banner permanente `modo administrador — loja X` e ação `Voltar ao painel administrativo`.
- Expiração curta, revogação explícita, invalidação por remoção de papel/permissão, exclusão da
  loja e logout.
- Auditoria de ciclo de vida do contexto, de mutações delegadas e de negativas.
- Testes unitários, integração, relacionais e Jest.

### 2.2 Fora de escopo

- Implementar a gestão de administradores de
  `2026-11-13-admin-management-design.md`; este documento apenas considera que o ator é uma
  identidade com role `Admin`.
- Alterar `AdminUserSeeder`, break-glass (`POST /api/auth/admin/recover-password`), recuperação de
  senha, `forgot-password`, `change-password`, confirmação de e-mail ou promoção de função.
- Excluir loja, abrir/fechar loja, contratar/cancelar mensalidade, alterar meio de pagamento,
  cadastrar administradores ou alterar perfil/conta do lojista a partir do modo administrador.
- Impressão, "Instalar" (PWA/Capacitor), entregadores, rotas de entrega, avaliações, marketing,
  clientes da loja e gateway de pagamento.
- Emissão de token de lojista, troca de token, login como lojista ou qualquer forma de
  impersonação.
- Alterar a lógica da feature de horários; apenas a autorização dos endpoints existentes é
  adaptada.
- Deep link nativo, push, SignalR de pedidos no modo administrador (ver R6).
- DOCX, coleção Postman, `docker/`, Terraform, OCI Vault, deploy e `print-agent`/`oci-mcp-server`.

## 3. Modelo de contexto e sessão

### 3.1 Entidade `AdminStoreAccessContext`

Nova entidade em `Urbeat.Domain/Entities`, mapeada em
`Urbeat.Infrastructure/Persistence/Configurations` e registrada em `ApplicationDbContext`.

| Campo | Tipo | Regra |
| --- | --- | --- |
| `Id` | `Guid` | herdado de `BaseEntity` (`Guid.CreateVersion7()`) |
| `AdminUserId` | `Guid` | ator; vem exclusivamente do claim autenticado, nunca do corpo |
| `StoreId` | `Guid` | loja do contexto; FK para `Stores` com `OnDelete(Cascade)` |
| `TokenHash` | `string(128)` | `ITokenHasher.Hash(token)`; nunca o token |
| `CreatedAtUtc` | `DateTime` | UTC |
| `ExpiresAtUtc` | `DateTime` | UTC; `CreatedAtUtc + ContextLifetimeMinutes` (padrão 30) |
| `RevokedAtUtc` | `DateTime?` | revogação explícita, expiração detectada, logout ou invalidação |
| `RevokedReason` | `string(40)?` | `Closed`, `Limit`, `Logout`, `Expired`, `AdminRoleRemoved`, `StoreDeleted` |
| `LastSeenAtUtc` | `DateTime?` | última requisição aceita; atualizada no máximo 1x/60 s |
| `Reason` | `string(200)?` | motivo opcional informado na abertura, sanitizado |
| `IpAddress` | `string(64)?` | `HttpContext.Connection.RemoteIpAddress` na criação |
| `UserAgent` | `string(256)?` | truncado; nunca usado para autorização |

Índices:

- `TokenHash` único (busca e proteção contra duplicidade).
- `(AdminUserId, ExpiresAtUtc)` para validação e limpeza.
- `(StoreId)` para revogação em exclusão/bloqueio da loja.

Sem `ValueGenerated` adicional, timestamps UTC, nenhuma coluna de segredo em claro.

### 3.2 Token do contexto

- 32 bytes aleatórios (`RandomNumberGenerator.GetBytes(32)`), codificados em Base64Url.
- Persistência apenas de `ITokenHasher.Hash(token)` (SHA-256 versionado, comparação em tempo
  constante), exatamente como convites/refresh tokens existentes.
- Entregue **uma única vez**, na resposta de criação. `GET` de contexto nunca devolve o token.
- Transporte exclusivamente pelo header `X-Admin-Store-Context`.
- Nunca aparece em URL, query string, path, cookie, `localStorage`, `AuditLog`, Serilog, Swagger,
  corpo de erro, payload de job ou coleção Postman.
- Um header de contexto sem JWT válido não autoriza nada (`401`); com JWT autenticado que não
  tenha a role `Admin`, a resposta é `403`.
- Um JWT de `Seller`/`Customer` acompanhado do header é rejeitado com `403`
  (`ADMIN_STORE_CONTEXT_NOT_ALLOWED`), para impedir reutilização acidental do header por sessão de
  lojista.

### 3.3 Expiração e revogação

Decisão: **TTL absoluto de 30 minutos**, sem renovação deslizante e sem endpoint de extensão.
Motivo: o caso de uso é suporte/inspeção; ao terminar, o administrador volta à lista e reabre. Um
token vazado só é útil por 30 minutos, e somente junto de um JWT de administrador válido.

| Evento | Efeito |
| --- | --- |
| Fim do TTL | requisição seguinte marca `RevokedAtUtc`/`RevokedReason = Expired` e audita uma vez |
| `DELETE .../access-context` | `RevokedReason = Closed` |
| Logout de administrador | `RevokedReason = Logout`; `AuthService.LogoutAsync` (servidor) revoga todos os contextos ativos do usuário, mesmo que o cliente não consiga chamar `DELETE` |
| Remoção da role `Admin` | validação por request detecta ausência do papel e revoga (`AdminRoleRemoved`) |
| Exclusão da loja | FK `Cascade` remove contextos; validação também responde store inexistente |
| Fim do processo/servidor | a linha permanece até expirar; não há estado em memória obrigatório |

Configuração não secreta em `appsettings` (sem Vault):

```json
"AdminStoreAccess": {
  "ContextLifetimeMinutes": 30,
  "MaxActiveContextsPerAdmin": 5,
  "ExpiredRetentionHours": 24,
  "TouchIntervalSeconds": 60,
  "MaxPageSize": 100
}
```

Faixas validadas em `ValidateOnStart`: TTL 5–120 min; máximo de contextos 1–20; retenção 1–168 h;
intervalo de toque 10–300 s. Nenhum novo segredo é introduzido.

Limpeza: na criação de um contexto, o serviço remove contextos do mesmo administrador expirados há
mais de `ExpiredRetentionHours` (bounded delete). Não há job recorrente novo.

### 3.4 Armazenamento no cliente

- `sessionStorage`, chave `urbeat_admin_store_context`, valor JSON:
  `{ token, contextId, storeId, storeName, storeSlug, adminUserId, expiresAtUtc, returnUrl }`.
- Escopo por aba: duas abas podem inspecionar lojas diferentes ao mesmo tempo. Nunca
  `localStorage` (compartilharia o token entre abas e sobreviveria ao fechamento).
- `returnUrl` só aceita caminho iniciando com `/admin/lojas`; qualquer outro valor é descartado.
- Ao abrir o app com `Admin` no token e contexto salvo, o serviço valida o contexto via
  `GET /api/admin/stores/{storeId}/access-context` antes de qualquer página `/app` renderizar.
- Logout, expiração e revogação limpam a chave. Nenhum outro dado do lojista é persistido.

### 3.5 Identidade efetiva (sem impersonação)

O acesso usa uma **identidade efetiva de loja**, derivada no servidor:

```text
requisição autenticada
  -> JWT: sub = AdminUserId, role = Admin
  -> header: token opaco
  -> linha AdminStoreAccessContext (hash, admin, loja, validade)
  -> Store (Id = context.StoreId) -> OwnerUserId atual
  -> EffectiveSellerUserId = Store.OwnerUserId
  -> ActorUserId = AdminUserId
```

- `EffectiveSellerUserId` **não** é um token nem um login. É apenas o identificador que os serviços
  existentes já esperam para resolver a loja e validar posse.
- O `OwnerUserId` é lido da tabela `Stores` a cada requisição (sem cache no token). Se a loja for
  excluída ou transferida, o contexto deixa de resolver.
- Nenhum endpoint aceita `AdminUserId`, `SellerUserId` ou `OwnerUserId` do corpo/query para
  autorização. O valor vem do JWT + linha de contexto.
- O claim `NameIdentifier` original **não** é reescrito. Controladores passam a ler o identificador
  efetivo de um acessor escopado (5.3), preservando a identidade do administrador para auditoria e
  log.
- Não há `Authorization` de lojista, cookie de refresh de lojista, `securityStamp` de lojista nem
  qualquer credencial do lojista envolvida.

## 4. Contratos HTTP

### 4.1 Convenções

- Rotas administrativas: `/api/admin/stores*`, controller novo `AdminStoresController` com
  `[Authorize(Policy = AuthorizationPolicies.AdminOnly)]`.
- Abertura/consulta/encerramento de contexto: mesma controller, mesmas políticas.
- JSON `camelCase`; datas ISO-8601 UTC com `Z`; enums de resposta em `PascalCase` (padrão atual).
- Paginação 1-based (`page` padrão 1, `pageSize` padrão 20, máximo 100), como
  `StoreCustomersQueryDto`. Valores fora da faixa são normalizados, não geram `400`.
- Listagem e contexto respondem `Cache-Control: no-store`, `Pragma: no-cache` (dados de
  proprietário e credencial de sessão).
- Nenhuma resposta contém token de lojista, senha, hash, `securityStamp`, `Document`, `PixKey` ou
  telefone do lojista.
- Erros seguem ProblemDetails/`{ code, message }` com `code` estável.
- `GET`/`DELETE /api/admin/stores/{storeId}/access-context` leem o header **diretamente na ação**
  (via `IAdminStoreAccessService`), pois o middleware de 5.2 não processa `/api/admin/*`. Assim um
  contexto expirado nunca bloqueia a própria lista administrativa.

### 4.2 Listagem de lojas

`GET /api/admin/stores?search=&status=all&publication=all&openState=all&subscription=all&sort=name&page=1&pageSize=20`

| Parâmetro | Valores | Semântica |
| --- | --- | --- |
| `search` | até 120 caracteres | case-insensitive em `Name`, `Slug`, e-mail e nome do proprietário |
| `status` | `all` (padrão), `onboarding`, `active`, `suspended` | ciclo de vida derivado (4.2.2) |
| `publication` | `all`, `published`, `draft` | `IsPublished` |
| `openState` | `all`, `open`, `closed` | `IsOpen` persistido (não calcula horário) |
| `subscription` | `all`, `active`, `overdue`, `blocked`, `none` | `SellerSubscriptionStatus` do proprietário |
| `sort` | `name`, `name_desc`, `created`, `created_desc`, `updated`, `updated_desc` | padrão `name`; desempate por `Id` |
| `page` / `pageSize` | inteiros | 1-based; normalizados |

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
      "name": "Pizza Hunter",
      "slug": "pizza-hunter",
      "logoUrl": null,
      "owner": {
        "userId": "0192f0c4-1111-7c3d-8e4f-0a1b2c3d4e5f",
        "email": "lojista@exemplo.com",
        "name": "Maria Souza"
      },
      "status": "Onboarding",
      "isPublished": false,
      "isOpen": false,
      "isSubscriptionBlocked": false,
      "subscription": {
        "status": "Active",
        "planName": "Essencial",
        "nextDueDateUtc": "2026-12-05T00:00:00Z"
      },
      "createdAtUtc": "2026-11-10T13:00:00Z",
      "updatedAtUtc": "2026-11-12T18:40:00Z"
    },
    {
      "id": "0192f0c4-7b2c-7d4e-9f50-1b2c3d4e5f60",
      "name": "Cantina da Praça",
      "slug": "cantina-da-praca",
      "logoUrl": "https://res.cloudinary.com/urbeat/image/upload/v1/stores/cantina/logo.png",
      "owner": {
        "userId": "0192f0c4-2222-7d4e-9f50-1b2c3d4e5f61",
        "email": "cantina@exemplo.com",
        "name": "João Lima"
      },
      "status": "Active",
      "isPublished": true,
      "isOpen": true,
      "isSubscriptionBlocked": false,
      "subscription": {
        "status": "Active",
        "planName": "Pro",
        "nextDueDateUtc": "2026-12-01T00:00:00Z"
      },
      "createdAtUtc": "2026-09-01T11:00:00Z",
      "updatedAtUtc": "2026-11-13T09:15:00Z"
    }
  ]
}
```

#### 4.2.1 Campos e privacidade

- `owner.name` vem do claim `FullName` em `AspNetUserClaims`; se ausente, cai para o e-mail.
  `owner.email` é `null` somente quando a identidade do proprietário não existe mais (a abertura do
  contexto falha nesse caso).
- `logoUrl` pode ser `null`; a UI aplica placeholder (seção 7).
- A lista **não** devolve `Document`, `PixKey`, `PhoneNumber`, endereço completo, taxas, horários ou
  qualquer dado operacional. Esses dados só ficam visíveis depois de abrir o contexto, com as
  mesmas respostas que o lojista recebe.
- `subscription.status = None` quando não há `SellerSubscriptionStatus`; `planName` e
  `nextDueDateUtc` vêm da assinatura mais recente por `StartDateUtc` e podem ser `null`.
- `isSubscriptionBlocked` é exposto separado para a UI distinguir bloqueio efetivo de atraso.

#### 4.2.2 Ciclo de vida derivado

| `status` | Condição |
| --- | --- |
| `Suspended` | `IsSubscriptionBlocked = true` |
| `Onboarding` | `!IsSubscriptionBlocked && !IsPublished` |
| `Active` | `!IsSubscriptionBlocked && IsPublished` |

Publicação (`publication`) e abertura (`openState`) são filtros independentes. Loja em wizard é
`Onboarding` mesmo com horários/endereço incompletos — o requisito é apenas existir linha em
`Stores`.

#### 4.2.3 Implementação da leitura

`AdminStoreReadRepository` em `Urbeat.Infrastructure/Persistence/ReadRepositories` (interface em
`Urbeat.Application/Interfaces`), usando `ApplicationDbContext` com projeção direta para DTO:

- `join` com `AspNetUsers` para e-mail; subquery em `AspNetUserClaims` (`ClaimType = "FullName"`)
  para nome;
- `left join`/subqueries em `SellerSubscriptionStatuses` (owner) e `SellerSubscriptions`
  (loja mais recente);
- `ToLower().Contains(...)` para busca (funciona em Npgsql e InMemory, ao contrário de `ILike`);
- `CountAsync` + `Skip/Take` na mesma query base;
- nenhuma entidade de domínio é materializada na listagem.

Índices recomendados (migração): `Stores (Name)`, `Stores (Slug)` já único, e
`SellerSubscriptionStatuses (SellerUserId)`. Busca textual ampla fica sem índice trigram nesta
entrega (R3).

### 4.3 Abrir contexto

`POST /api/admin/stores/{storeId:guid}/access-context`

```json
{ "reason": "Chamado de suporte #4821" }
```

- `reason` é opcional (máx. 200), sanitizado, usado apenas em auditoria.
- `storeId` precisa existir em `Stores` (publicada ou não). Loja inexistente → `404
  STORE_NOT_FOUND`.
- O proprietário precisa existir em `AspNetUsers`; caso contrário `409
  STORE_OWNER_NOT_FOUND` (não cria contexto órfão).
- Revoga contextos expirados antigos do próprio administrador, mantém os ativos (até o limite).
- Se o administrador já tem `MaxActiveContextsPerAdmin` contextos ativos, revoga o mais antigo
  (`RevokedReason = Closed`, auditoria `AdminStoreAccessRevoked` com motivo `Limit`).
- Rate limit: nova política `admin-store-context` (padrão 30 req/min por `AdminUserId`, fallback
  IP), aplicada nos três endpoints de contexto, com limites em `RateLimitingOptions`
  (`AdminStoreContextPermitLimit`/`AdminStoreContextWindowSeconds`) para permitir override em
  testes, como as demais políticas. Token de 256 bits já torna adivinhação inviável; o limite
  protege contra abuso/erro do cliente.

`201 Created` — `Cache-Control: no-store`

```json
{
  "contextId": "0192f0c4-8c3d-7e5f-a061-2c3d4e5f6071",
  "adminUserId": "0192f0c4-3333-7e5f-a061-2c3d4e5f6072",
  "storeId": "0192f0c4-7b2c-7d4e-9f50-1b2c3d4e5f60",
  "storeName": "Cantina da Praça",
  "storeSlug": "cantina-da-praca",
  "expiresAtUtc": "2026-11-13T21:52:26Z",
  "token": "<token-opaco-base64url>"
}
```

O token aparece **somente** nesta resposta. Auditoria grava `Event = AdminStoreAccessGranted`,
`UserId = adminUserId`, `EntityId = storeId`, sem token.

### 4.4 Consultar contexto atual

`GET /api/admin/stores/{storeId:guid}/access-context` (com `X-Admin-Store-Context`)

`200 OK`

```json
{
  "contextId": "0192f0c4-8c3d-7e5f-a061-2c3d4e5f6071",
  "adminUserId": "0192f0c4-3333-7e5f-a061-2c3d4e5f6072",
  "storeId": "0192f0c4-7b2c-7d4e-9f50-1b2c3d4e5f60",
  "storeName": "Cantina da Praça",
  "storeSlug": "cantina-da-praca",
  "expiresAtUtc": "2026-11-13T21:52:26Z"
}
```

Usado no boot/restauração da aba e após `visibilitychange`. Nunca retorna o token.

### 4.5 Encerrar contexto

`DELETE /api/admin/stores/{storeId:guid}/access-context` (com header)

- `204 No Content`, idempotente para um contexto **conhecido** já revogado/expirado; nesse caso
  audita o encerramento explícito no máximo uma vez.
- Token desconhecido/adulterado → `403 ADMIN_STORE_CONTEXT_INVALID` (não confirma a existência de
  contextos alheios).
- Revoga apenas o contexto apresentado no header, nunca todos os contextos do administrador.

### 4.6 Códigos de erro

| `code` | HTTP | Significado |
| --- | --- | --- |
| `STORE_NOT_FOUND` | 404 | loja inexistente na criação de contexto |
| `STORE_OWNER_NOT_FOUND` | 409 | loja sem identidade de proprietário resolvível |
| `ADMIN_STORE_CONTEXT_NOT_ALLOWED` | 403 | header enviado por sessão que não é `Admin` |
| `ADMIN_STORE_CONTEXT_INVALID` | 403 | token desconhecido/revogado |
| `ADMIN_STORE_CONTEXT_EXPIRED` | 403 | TTL vencido |
| `ADMIN_STORE_CONTEXT_ADMIN_NOT_ALLOWED` | 403 | identidade perdeu a role `Admin` |
| `ADMIN_STORE_CONTEXT_STORE_MISMATCH` | 403 | `storeId` da rota/query difere do contexto |
| `ADMIN_STORE_CONTEXT_STORE_MISSING` | 403 | loja do contexto não existe mais |
| `ADMIN_STORE_ACTION_NOT_ALLOWED` | 403 | endpoint fora do escopo de delegação |

Mensagens são genéricas; nenhuma revela se outro administrador/loja existe. `403` de contexto não
faz logout do administrador: o JWT continua válido e a UI oferece retorno ao painel.

## 5. Autorização por camada

### 5.1 Política `StoreAccess`

Nova constante em `Urbeat.Application.Security.AuthorizationPolicies`:

```text
StoreAccess = nameof(StoreAccess)
```

Registro em `InfrastructureServiceCollectionExtensions` (junto das políticas existentes):

- exige usuário autenticado;
- `StoreAccessAuthorizationHandler`:
  - feature de contexto presente → exige role `Admin` e `storeId` de rota/query (quando existir)
    igual a `context.StoreId`; sucesso ou falha;
  - sem feature → role `Seller` → sucesso (comportamento atual preservado);
  - qualquer outro caso → falha.

`StoreAccessAuthorizationResultHandler` (implementação de
`IAuthorizationMiddlewareResultHandler`) traduz falhas de escopo em ProblemDetails com os `code`s
acima (inclusive `STORE_MISMATCH` e `ACTION_NOT_ALLOWED`) e grava
`AdminStoreActionDenied` quando havia JWT de `Admin`.

Fail-closed: controllers que não passarem para `StoreAccess` continuam `SellerOnly`; logo, um
administrador sem contexto recebe `403` neles. Nenhuma rota fica implicitamente liberada.

### 5.2 Middleware de contexto

`AdminStoreContextMiddleware`, registrado depois de `UseAuthentication()` e antes de
`UseAuthorization()`:

1. Ignora requisições sem header.
2. Só processa os prefixos delegáveis:
   `/api/stores/`, `/api/orders/`, `/api/neighborhoods/`, e ignora endpoints marcados com
   `[AllowAnonymous]` (ex.: `GET /api/stores/cuisine-types`), para que um contexto vencido não
   bloqueie dados públicos usados pela própria tela.
   Fora desses prefixos o header é ignorado (assim `/api/admin/stores` e `/api/auth/*` nunca
   dependem de um contexto expirado). `GET`/`DELETE .../access-context` validam o header dentro da
   própria ação (4.1), então continuam funcionando mesmo com o contexto vencido — é assim que a UI
   consegue encerrar/limpar uma sessão expirada.
3. Exige JWT autenticado: sem identidade válida → `401` (comportamento padrão do pipeline). Com
   identidade autenticada sem role `Admin` → `403 ADMIN_STORE_CONTEXT_NOT_ALLOWED`.
4. Carrega a linha por `TokenHash` em uma única query com:
   - `AdminUserId == sub`;
   - `RevokedAtUtc == null`;
   - `ExpiresAtUtc > now`;
   - existência da role `Admin` (`AspNetUserRoles`/`AspNetRoles`);
   - loja existente.
   Quando a gestão de administradores (`AdminAccountStates`) for implementada, a mesma query passa a
   exigir `IsBlocked = false`; hoje a fonte de verdade é a role.
5. Em falha, grava auditoria de negativa (uma vez por contexto/ motivo, usando
   `RevokedAtUtc/RevokedReason` para não multiplicar linhas) e responde o `code` correspondente.
6. Em sucesso, anexa `AdminStoreContextFeature` ao `HttpContext` com `ContextId`, `AdminUserId`,
   `StoreId`, `StoreOwnerUserId`, `ExpiresAtUtc` e atualiza `LastSeenAtUtc` no máximo a cada 60 s.
7. `storeId` de rota/query divergente é rejeitado no handler de autorização (5.1), não aqui, para
   que o log identifique a tentativa.

O middleware não lê corpo de requisição. Endpoints cujo `storeId` viaja no corpo
(`POST /api/stores/delivery-times`) continuam protegidos pela checagem de posse dentro do serviço,
que recebe o proprietário derivado do contexto (loja única por proprietário). Ver R5.

### 5.3 Acessor escopado

`IStoreRequestContext` (`Scoped`) expõe:

| Propriedade | Sessão `Seller` | Sessão `Admin` + contexto |
| --- | --- | --- |
| `ActorUserId` | `sub` | `adminUserId` |
| `EffectiveSellerUserId` | `sub` | `storeOwnerUserId` |
| `StoreId` | `null` (derivado pelo serviço) | `context.StoreId` |
| `IsAdminDelegated` | `false` | `true` |
| `ContextId` | `null` | `context.ContextId` |

Implementação:

- Em modo delegado, lê a feature do middleware.
- Em modo lojista, usa `ClaimTypes.NameIdentifier` (comportamento atual).
- Nunca aceita valores de corpo/query.

Os controladores trocam apenas a **origem do identificador**:

```text
antes:  var ownerUserId = GetCurrentUserId();
depois: var ownerUserId = _storeContext.EffectiveSellerUserId;
```

O helper privado `GetCurrentUserId()` de cada controlador pode delegar ao acessor, mantendo o resto
do método intacto. `PublishController`, que hoje lê o claim inline, passa a usar o acessor.

### 5.4 Autorização efetiva por camada

| Camada | Loja (seller) | Admin com contexto válido | Admin sem contexto |
| --- | --- | --- | --- |
| Guard Angular `/app` | `isSeller` | `isAdmin` + sessão ativa | redireciona `/admin/lojas` |
| Header HTTP | ausente | `X-Admin-Store-Context` | ausente |
| Política `StoreAccess` | papel `Seller` | papel `Admin` + contexto + loja | falha |
| Serviço | posse por `ownerUserId` | mesma checagem, com `ownerUserId` derivado | não executa |
| Banco | RLS inexistente; filtro por loja | mesmo filtro | — |
| Auditoria | `UserId = seller` | `UserId = admin`, `SubjectUserId = owner`, `ContextId` | negativa registrada |

Nada no frontend decide autorização. Guards e banners são UX; o servidor nega independentemente.

## 6. Adaptação dos endpoints existentes (sem duplicar lógica)

Regra: **nenhum endpoint novo de loja é criado**. Os mesmos controllers/services continuam
existindo; muda a política, a origem do identificador e, em um caso, a resolução da loja.

### 6.1 Controllers que passam o nível de classe para `StoreAccess`

| Controller | Ações delegáveis | Ações que permanecem `SellerOnly` |
| --- | --- | --- |
| `StoresController` | `GET my-store`, `PUT {id}`, `GET/PUT {id}/address`, `GET/PUT {id}/business-hours`, `PATCH {id}/delivery-config`, `{id}/cuisine-types` (GET/POST/DELETE), `delivery-times` (GET/POST), `delivery-neighborhoods` (GET), `delivery-neighborhoods-by-store` (GET), `upload-image` | `POST /api/stores` (criar), `DELETE {id}` (excluir loja), `PATCH {id}/status` (abrir/fechar), `POST delivery-neighborhoods` (criação global de bairro) |
| `StoreProductsController` | listar, criar, atualizar, disponibilidade, imagens, excluir | — |
| `StoreCategoriesController` | CRUD e reordenação | — |
| `StoreAdditionalsController` | CRUD e status | — |
| `PublishController` | `GET summary`, `POST publish` | — |
| `OrdersController` | `GET store`, `GET store/{id}`, `PATCH {orderId}/status`, `POST {orderId}/complete`, `PUT/DELETE store/{orderId}/delivery-driver`, `POST store/{orderId}/delivery-link`, `GET store/report`, `GET store/analytics`, `GET store/sales` | `GET store/customers`, `PUT/PATCH store/customers/*`, `GET store/deliveries`; ações `CustomerOnly` intactas |
| `SubscriptionsController` | `GET my`, `GET my/charges` (somente leitura) | `POST contract`, `GET plans` |
| `NeighborhoodsController` | `GET cities`, `GET cities/{id}/search`, `GET cities/{id}/map` | `POST import-by-city` |

`SubscriptionsController` deixa de ter política de classe `SellerOnly` e passa a declarar
`StoreAccess` nas duas leituras e `SellerOnly` no contrato. `ReviewsController`,
`SellerController`, `SellerPrintingController`, `DeliveryDriversController`,
`StorePaymentGatewayController` e `SellerNotificationsController` **não** mudam.

### 6.2 Resolução de loja no `my-store`

`GET /api/stores/my-store`:

```text
seller  -> store do claim (comportamento atual)
admin   -> store do contexto (context.StoreId)
```

Isso permite que todas as telas existentes de `/app` (que resolvem a loja por `getMyStore()`)
funcionem sem duplicação e sem parâmetro novo na URL. É a única ação com semântica ampliada; o
comportamento do lojista é idêntico e testado.

### 6.3 O que NÃO muda

- Serviços de aplicação (`IStoreService`, `IProductService`, `IOrderService`,
  `IStorePublishService`, `IStoreBusinessHoursService`, `IStoreAddressService`,
  `IProductCategoryService`, `IStoreAdditionalService`, `ISellerSubscriptionStatusService`):
  assinaturas e regras de negócio intactas.
- Recomputação de preços, taxas, totais, descontos e transições de pedido: intacta.
- Regra de publicação (`CanPublish`) e bloqueio por assinatura: intactos. O administrador não
  contorna requisito de publicação.
- Erros HTTP do lojista para as mesmas ações.
- Contrato de `StoreResponseDto` e demais DTOs.

### 6.4 Tabela de arquivos previstos

| Camada | Arquivo novo/alterado |
| --- | --- |
| Domain | `Entities/AdminStoreAccessContext.cs` |
| Application | `Interfaces/IAdminStoreAccessService.cs`, `Interfaces/IAdminStoreReadRepository.cs`, `DTOs/Admin/*`, `Security/AuthorizationPolicies.cs` |
| Infrastructure | `Persistence/Configurations/AdminStoreAccessContextConfiguration.cs`, `Persistence/ApplicationDbContext.cs`, `Persistence/ReadRepositories/AdminStoreReadRepository.cs`, `Services/AdminStoreAccessService.cs`, `Security/AdminStoreAuditStampInterceptor.cs`, `Identity/AuthService.cs` (revogação no logout), `DependencyInjection/InfrastructureServiceCollectionExtensions.cs` |
| WebApi | `Controllers/AdminStoresController.cs`, `Security/AdminStoreContextMiddleware.cs`, `Security/StoreAccessAuthorizationHandler.cs`, `Security/StoreAccessAuthorizationResultHandler.cs`, `Security/StoreRequestContext.cs`, `Controllers/StoresController.cs`, `Controllers/StoreProductsController.cs`, `Controllers/StoreCategoriesController.cs`, `Controllers/StoreAdditionalsController.cs`, `Controllers/PublishController.cs`, `Controllers/OrdersController.cs`, `Controllers/SubscriptionsController.cs`, `Controllers/NeighborhoodsController.cs` |
| Migration | `AddAdminStoreAccessContexts` |

Migração (a partir de `backend/`):

```powershell
dotnet ef migrations add AddAdminStoreAccessContexts --startup-project src/Urbeat.WebApi --project src/Urbeat.Infrastructure
```

Startup relacional já executa `Database.MigrateAsync()`; nunca usar `dotnet ef database update`
contra banco de implantação.

## 7. Lojas sem logo e lojas em onboarding

- **Sem logo:** a lista e o banner usam `StoreLogoAvatarComponent`
  (`frontend/src/app/shared/components/store-logo-avatar/`): círculo/quadrado arredondado de 40–44
  px, fundo `var(--app-brand-soft)`, inicial do nome em `var(--app-brand)`; se `logoUrl` falhar no
  carregamento (`error`), troca para o mesmo placeholder. Nunca renderiza `img` quebrada.
  - Na lista, o nome fica ao lado do avatar; a imagem recebe `alt=""` (decorativa) e o nome é o
    texto acessível. Quando usada isolada, `alt` = `Logo da {nome}`.
  - `loading="lazy"`, `decoding="async"`.
- **Onboarding:** qualquer linha de `Stores` aparece, independentemente de `IsPublished`,
  endereço, horários ou taxas. O contexto abre normalmente. As telas existentes já mostram
  pendências (publicação, endereço, horários); o administrador corrige o que falta e publica se os
  requisitos forem atendidos.
- **Loja sem proprietário resolvível:** aparece na lista (`owner.email` vazio) mas a abertura
  falha com `409 STORE_OWNER_NOT_FOUND`, com mensagem de ação (não cria contexto órfão).
- **Loja bloqueada por assinatura:** aparece como `Suspended`, abre para inspeção/edição, mas
  `PATCH status` continua `SellerOnly`, então o administrador não a abre nem fecha; qualquer
  tentativa de publicação segue as regras existentes do servidor.

## 8. Frontend, rotas, componentes e UX

### 8.1 Rotas

`frontend/src/app/app.routes.ts`:

```text
/admin
  visao-geral
  lojas            <-- novo (AdminStoresPageComponent)
  operacoes
  planos
  configuracoes
```

- `lojas` herda `canActivate/canActivateChild: [adminGuard]` do pai.
- `/app` **não** ganha uma árvore paralela: as mesmas rotas do lojista são reutilizadas.
- `authGuard`/`authChildGuard` passam a aceitar `Admin` com sessão ativa; sem sessão, redirecionam
  para `/admin/lojas` (sem `logout`). A checagem de `Admin` é assíncrona (`ensureRestored()`), mas o
  caminho do `Seller` continua síncrono. `Customer` mantém o comportamento atual. Isso mantém os
  arrays de guards intactos em `app.routes.spec.ts`.
- `app.routes.admin.spec.ts` precisa incluir `lojas` na lista exata de filhos: `['', 'visao-geral',
  'lojas', 'operacoes', 'planos', 'configuracoes']` (mudança esperada e documentada).

### 8.2 Serviços e modelos

- `core/services/admin-stores.service.ts` (`inject(HttpClient)` + `ApiService`):
  - `list(query): Observable<PagedAdminStoreSummary>`
  - `openContext(storeId, reason?): Observable<AdminStoreContext>`
  - `getContext(storeId): Observable<AdminStoreContextMetadata>`
  - `closeContext(storeId): Observable<void>`
- `shared/models/admin-store.model.ts`: `AdminStoreSummary`, `AdminStoreOwner`,
  `AdminStoreSubscription`, `AdminStoreStatus`, `PagedAdminStoreSummary`, `AdminStoresQuery`,
  `AdminStoreContext`, `AdminStoreContextMetadata`.
- `core/services/admin-store-session.service.ts` (Signals):
  - `session = signal<AdminStoreSession | null>()`, `isActive = computed(...)`,
    `isExpired = signal(false)`.
  - Persistência em `sessionStorage`. `ensureRestored()` é idempotente e compartilhado (uma única
    promise): lê a chave, valida com `GET /api/admin/stores/{storeId}/access-context` **enviando o
    header manualmente** e limpa se inválido. Guards e banner aguardam essa promise.
  - `start(response, returnUrl)` e `end()` (envia `DELETE` com o header manualmente, limpa sempre,
    mesmo se a rede falhar).
  - `markInvalid(code)` só para códigos de contexto (não para `ADMIN_STORE_ACTION_NOT_ALLOWED`).
  - Timer até `expiresAtUtc` + revalidação em `visibilitychange`.
  - `returnUrl` restrito a `/admin/lojas*`.
- `core/interceptors/admin-store-context.interceptor.ts`:
  - adiciona `X-Admin-Store-Context` apenas quando `isActive()` e a URL começa com
    `/api/stores/`, `/api/orders/` ou `/api/neighborhoods/`;
  - nunca adiciona em `/api/auth/` nem em `/api/admin/` (os endpoints de contexto montam o header
    explicitamente no serviço de sessão);
  - em `403` cujo `code` seja `ADMIN_STORE_CONTEXT_INVALID`, `ADMIN_STORE_CONTEXT_EXPIRED`,
    `ADMIN_STORE_CONTEXT_STORE_MISMATCH`, `ADMIN_STORE_CONTEXT_STORE_MISSING` ou
    `ADMIN_STORE_CONTEXT_ADMIN_NOT_ALLOWED`, chama `session.markInvalid(code)`, emite toast único e
    rethrowa (sem logout). `ADMIN_STORE_ACTION_NOT_ALLOWED` segue o tratamento genérico de `403`.
  - Registro em `app.config.ts`: `[serverAccessInterceptor, authInterceptor,
    adminStoreContextInterceptor, errorInterceptor]`.
  - O tratamento específico roda **antes** do toast genérico de `403` já existente em
    `error.interceptor.ts`.
- `core/interceptors/error.interceptor.ts`: para tokens `Admin`, um `401` sem recuperação
  redireciona para `/painel/login` (e limpa o contexto), não para `/login-vendedor`.

### 8.3 `SellerShellFacade` em modo administrador

`init()` deve:

- carregar `getMyStore()` (que resolve a loja do contexto);
- **não** chamar `SellerNotificationService.list()`;
- **não** iniciar `SignalRService.startSellerHub()`;
- **não** inicializar impressão/som;
- manter som/impressão desabilitados e expor `isAdminDelegated` para a shell.

Motivo: notificações e hub pertencem à identidade do lojista; chamá-los em modo delegado gera
`403` e derruba o carregamento. Atualizações de pedidos no modo administrador acontecem por
navegação/recarga explícita (R6).

### 8.4 `SellerAppShellComponent`

- Banner permanente (8.6) e CTA de retorno.
- Menu lateral em modo administrador:
  - manter: Dashboard, Pedidos, Cardápio (produtos/categorias/adicionais), Configurações
    (horários, informações, bio, bairros);
  - ocultar: Motoboys, Clientes, Mensalidade, Instalar;
  - `ConfigSubnavComponent`: ocultar a aba **Impressão**.
- Não exibir o link "ver loja" como edição; o link para a loja pública continua abrindo a
  storefront em outra aba.
- `logout()` no modo administrador: encerra o contexto e faz logout administrativo, indo para
  `/painel/login`.

### 8.5 Página `/admin/lojas`

`AdminStoresPageComponent` (standalone, `ChangeDetectionStrategy.OnPush`, Signals):

- Cabeçalho: título `Lojas`, contagem total e `role="status"` para anúncio de resultados.
- Busca com debounce de 300 ms, `switchMap` (cancela requisição anterior) e reset de página para 1.
- Filtros: selects/chips de status, publicação, abertura e assinatura; botão "Limpar filtros".
  Estado espelhado na URL (`?page=&search=&status=&publication=&openState=&subscription=&sort=`)
  para que o retorno do dashboard preserve a lista.
- Desktop (`>= 860px`): tabela com `<caption class="sr-only">`, cabeçalhos ordenáveis com
  `aria-sort`, colunas Logo/Nome+slug/Proprietário/Status/Publicação/Abertura/Assinatura/Ações.
- Mobile (`< 860px`): cards com pares rótulo/valor; alvo de toque mínimo 44 px.
- Linha/card: clique abre a loja; botão explícito `Abrir loja` para teclado; `Enter`/`Espaço`
  acionam a mesma ação. Enquanto abre, `openingStoreId` desabilita a ação e mostra spinner.
- Ação secundária `Ver loja pública` abre `/{slug}` em nova aba; desabilitada quando não publicada.
- Estados: loading (skeleton), vazio ("Nenhuma loja encontrada com esses filtros"), erro com
  "Tentar novamente", `403` com retorno a `/admin`.
- Paginação: anterior/próxima + números, 44 px, anúncio `aria-live="polite"`.
- Badges: `Onboarding` (neutro), `Active` (verde de status), `Suspended` (vermelho suave);
  `Publicada`/`Rascunho`; `Aberta`/`Fechada`; assinatura `Ativa`/`Vencida`/`Bloqueada`/`Sem
  assinatura`. Cores por tokens `--app-*`, nunca hex novos.
- Ao abrir com sucesso: `AdminStoreSessionService.start(response, returnUrl)` e
  `router.navigateByUrl('/app/dashboard')`.
- Erros de abertura: `404` → "Loja não encontrada. Atualize a lista."; `409` → "Loja sem
  proprietário resolvível."; `403` → "Seu acesso administrativo expirou."; `429` → aguarde.

### 8.6 Banner permanente

Componente `AdminStoreBannerComponent` (shared), renderizado no topo do conteúdo da shell do
lojista:

- Texto permanente: `modo administrador — loja {storeName}`.
- CTA: `Voltar ao painel administrativo` (mínimo 44 px, `type="button"`).
- `role="status"` com `aria-live="polite"` apenas para mudanças de estado (expiração), não para o
  texto estático.
- `position: sticky`, `top: 0`, `z-index` abaixo de modais Ionic, respeitando
  `env(safe-area-inset-top)` no mobile; nunca cobre o botão de menu.
- Em expiração: fundo de alerta, texto `Sessão administrativa expirada` e o mesmo CTA; o retorno
  encerra a sessão local sem tentar salvar.
- Em mobile, uma linha com truncamento (`text-overflow: ellipsis`, `title` com o nome completo) e
  CTA em altura de 44 px.

### 8.7 Ajustes em telas reutilizadas

| Tela | Ajuste em modo administrador |
| --- | --- |
| `StoreConfigPageComponent` (informações/bio) | não chama `getSellerProfile`/`updateSellerProfile`; campo "nome do contratante" fica oculto e `saveContractorName()` retorna `true`; o salvamento da loja não depende de perfil |
| `SellerStoreInfoPageComponent` | oculta a zona de exclusão de loja (backend também nega) |
| `SellerDashboard` / `seller-categories` / `seller-products` | leitura de `/api/subscriptions/my` permitida; nenhum botão de contratar/cancelar é exibido |
| `ConfigSubnavComponent` | oculta "Impressão" |
| Shell | oculta menus não delegáveis (8.4) |

Nenhuma tela ganha campo, botão ou fluxo destrutivo novo.

### 8.8 Acessibilidade e responsividade

- Alvos de toque ≥ 44×44 px; foco visível com `box-shadow: 0 0 0 3px var(--app-brand-shadow)`.
- Tabela semântica com `scope="col"`, `caption` para leitores de tela e `aria-sort`.
- Busca com `aria-label="Buscar lojas"`, filtros com `<label>` explícito.
- Banner nunca é a única indicação de modo: o título da página e o menu também refletem contexto.
- Contraste AA; badges com texto, não apenas cor.
- Layout mobile-first; nada de `position: fixed` que esconda a CTA atrás da tab bar.

## 9. Auditoria

### 9.1 `AuditLog` (tabela existente, colunas aditivas)

| Coluna nova | Tipo | Uso |
| --- | --- | --- |
| `ActorUserId` | `Guid?` | ator real; preenchido em linhas delegadas |
| `SubjectUserId` | `Guid?` | proprietário da loja em nome de quem se agiu |
| `AdminStoreContextId` | `Guid?` | correlação com `AdminStoreAccessContexts` |

`UserId` continua sendo o ator (`adminUserId`) para consultas existentes. Para linhas legadas,
as colunas novas ficam `null` e `UserId` mantém o significado atual.

Preenchimento centralizado por `AdminStoreAuditStampInterceptor`
(`SaveChangesInterceptor` singleton com `IHttpContextAccessor`, registrado via
`AddInterceptors` em `AddDbContextPool`):

- se existe `AdminStoreContextFeature` na requisição, toda linha `AuditLog` adicionada no
  `SaveChanges` recebe `ActorUserId = adminUserId`, `SubjectUserId = UserId` original,
  `AdminStoreContextId = contextId` e `UserId = adminUserId`;
- fora de uma requisição delegada (seller, cliente, jobs, seeders, testes) nada muda.

### 9.2 Eventos

| `Event` | `UserId` | `EntityId` | Descrição |
| --- | --- | --- | --- |
| `AdminStoreAccessGranted` | admin | loja | motivo opcional, expiração, IP (sem token) |
| `AdminStoreAccessRevoked` | admin | loja | `Closed`, `Logout`, `Limit` |
| `AdminStoreAccessExpired` | admin | loja | uma vez, ao detectar TTL vencido |
| `AdminStoreAccessRejected` | admin | loja | `Invalid`, `Mismatch`, `RoleRemoved`, `StoreMissing` |
| `AdminStoreActionExecuted` | admin | loja | `METHOD route-template status` para mutações 2xx |
| `AdminStoreActionDenied` | admin | loja | `ACTION_NOT_ALLOWED`, `STORE_MISMATCH` |

- `AdminStoreActionExecuted` é gravado por `IAsyncActionFilter` global (ou atributo nos controllers
  adaptados), somente para verbos `POST/PUT/PATCH/DELETE` e status 2xx. É defesa em profundidade:
  as linhas de negócio (`StoreUpdated`, `ProductCreated`, etc.) continuam sendo gravadas pelos
  serviços.
- Falha ao gravar auditoria **não** derruba a operação; loga `ADMIN_STORE_AUDIT_FAILED`. A auditoria
  de concessão/revogação é transacional com a mudança de estado do contexto.
- Nunca registrar: token, hash, header, senha, `Document`, `PixKey`, telefone, payload, query
  completa ou dados de cliente.
- Serilog equivalente com `AdminUserId`, `StoreId`, `ContextId`, `IP`, `Action`, `StatusCode`.
  `UseSerilogRequestLogging` continua logando apenas método/rota/status; o token viaja em header e
  nunca entra na URL.

## 10. Proteção contra vazamento de dados e ações destrutivas

### 10.1 Vazamento

- O contexto sozinho não autentica: token + JWT de `Admin` válido + role atual.
- O token só existe em memória e `sessionStorage`; respostas de contexto usam `no-store`.
- Listagem não expõe PII além de e-mail/nome do proprietário; documentos, chaves Pix, telefone e
  endereço só depois de abrir o contexto, com o mesmo payload do lojista.
- Nenhuma resposta de erro revela existência de loja de outro contexto, papéis ou e-mails.
- Contexto para loja A não acessa loja B: rota/query divergente → `403
  ADMIN_STORE_CONTEXT_STORE_MISMATCH`; serviços também rejeitam posse divergente.
- Endpoints públicos (`[AllowAnonymous]`, storefront, checkout, health) ignoram o header e não
  passam pelo acessor.
- `CustomerOnly` continua exigindo papel `Customer`; um administrador com contexto não compra,
  não acessa pedidos de cliente e não altera dados de cliente.
- Logs: sem header, sem token, sem corpo; variáveis sensíveis nunca são interpoladas.

### 10.2 Ações destrutivas

Bloqueadas para administrador (permanecem `SellerOnly`):

- excluir loja (`DELETE /api/stores/{id}`);
- abrir/fechar loja (`PATCH /api/stores/{id}/status`) — altera disponibilidade comercial imediata e
  não faz parte dos módulos aprovados; a coluna "abertura" é apenas informativa;
- contratar/cancelar mensalidade, alterar gateway de pagamento;
- perfil/conta/senha do lojista;
- entregadores, rotas, avaliações, marketing, clientes da loja;
- gestão de administradores, break-glass e recuperação de senha.

Permitidas, com o mesmo comportamento do lojista:

- excluir produto/categoria/adicional (com as confirmações existentes);
- publicar loja (somente se `CanPublish`);
- alterar taxas, horários, endereço e bairros.

Reforços:

- confirmações existentes são mantidas; em modo administrador, o texto do diálogo inclui
  `Esta ação será registrada como administrador.`;
- toda mutação delegada é auditada com ator, loja e contexto;
- nenhuma operação em lote nova (importação de bairros, migração de imagens) é exposta.

## 11. Concorrência e retorno ao painel

- **Duas abas, mesma loja:** cada aba tem seu token; ambas funcionam. Criar um novo contexto não
  revoga os anteriores (evita derrubar a outra aba). O limite de 5 por administrador e a
  expiração de 30 min mantêm a tabela pequena.
- **Duas abas, lojas diferentes:** isolamento por `sessionStorage` + header por requisição.
- **Dois administradores, mesma loja:** contextos independentes; auditoria distingue os atores.
- **Criação concorrente até o limite:** transação + índice; se dois pedidos ultrapassarem o limite,
  o último revoga o mais antigo; ambos os resultados são auditados.
- **Expiração durante edição:** o servidor passa a negar (`403 ADMIN_STORE_CONTEXT_EXPIRED`). Não
  há navegação automática (evita perder formulário). O banner entra em estado expirado, o
  `errorInterceptor` mostra uma única mensagem e qualquer salvamento é bloqueado. O retorno ao
  painel é explícito; se houver formulário sujo, a UI pede confirmação antes de descartar.
- **Retorno:** `Voltar ao painel administrativo` chama `DELETE` (melhor esforço), limpa a sessão e
  navega para `returnUrl` (lista com filtros/página preservados). Nunca navega para
  `/login-vendedor`.
- **Logout administrativo:** revoga o contexto ativo, limpa `sessionStorage` e encerra o refresh
  token no servidor; abas restantes recebem `401` e vão para `/painel/login`.
- **Loja excluída durante o contexto:** próxima requisição → `ADMIN_STORE_CONTEXT_STORE_MISSING`;
  a UI retorna à lista com aviso.
- **Papel `Admin` removido:** a validação por request (join de role) revoga o contexto; o access
  token residual continua válido para endpoints `AdminOnly` até expirar (mesma limitação aceita na
  especificação de administradores, R2 daquele documento).
- **InMemory:** a checagem de limite e a revogação por expiração usam `SemaphoreSlim` estático por
  processo nos testes, reproduzindo a exclusão mútua; em PostgreSQL usa transação + índice único
  de `TokenHash`.

## 12. Compatibilidade

- **Lojista:** mesmas rotas, mesmos corpos, mesmos códigos. `StoreAccess` aceita role `Seller`;
  `EffectiveSellerUserId = sub`; auditoria inalterada. `my-store` continua devolvendo a loja do
  próprio lojista.
- **Cliente:** nenhuma mudança. Checkout, pedidos, avaliações e rotas públicas intactos.
- **`authGuard`/`authChildGuard`:** o comportamento de `Seller` e `Customer` permanece; a única
  adição é o caso `Admin` com sessão. Os arrays de guards nas rotas não mudam, então
  `app.routes.spec.ts` continua passando.
- **`app.routes.admin.spec.ts`:** atualização esperada para incluir `lojas` na lista exata.
- **Admin shell:** adiciona um item; rotas existentes e `adminGuard` não mudam.
- **Logout:** `POST /api/auth/logout` mantém `204`, o clear do cookie e a revogação do refresh
  token; a única adição é revogar contextos administrativos do usuário. Lojista e cliente não têm
  contextos, então o comportamento deles é idêntico.
- **`AdminUserSeeder`, break-glass, recuperação de senha de admin, `forgot-password` de
  Customer/Seller:** intocados.
- **PWA/Capacitor:** nenhuma mudança de `webDir`, build, permissões ou deep link. O header extra é
  inofensivo no WebView; o modo administrador é web-first. `branding-consistency.spec.ts` exige
  `urbeat` minúsculo em qualquer cópia nova.
- **Infra:** CORS já usa `AllowAnyHeader`, então `X-Admin-Store-Context` funciona no dev server e
  no nginx. Nenhuma alteração em `docker/`, Terraform ou Vault.
- **Postman/DOCX/deploy/feature de horários:** fora do escopo; nenhum arquivo desses é alterado.

## 13. Testes

### 13.1 Unitários (backend, InMemory)

`backend/tests/Urbeat.UnitTests/Infrastructure/Services/AdminStoreAccessServiceTests.cs`

- criação: token de 32 bytes, apenas hash persistido, TTL de 30 min, `AdminUserId` do parâmetro,
  `StoreId` correto, auditoria sem token;
- loja inexistente → `STORE_NOT_FOUND`; proprietário inexistente → `STORE_OWNER_NOT_FOUND`, nenhuma
  escrita;
- validação: contexto expirado (marca `RevokedReason = Expired` uma vez), revogado, token
  desconhecido, admin divergente, role removida, loja removida;
- encerramento idempotente e restrito ao contexto apresentado;
- limite de contextos ativos revoga o mais antigo e audita;
- limpeza de expirados além da retenção;
- `LastSeenAtUtc` atualizado no máximo uma vez por intervalo;
- auditoria de concessão/revogação/negativa sem segredos.

`backend/tests/Urbeat.UnitTests/WebApi/StoreAccessAuthorizationHandlerTests.cs`

- `Seller` passa sem contexto;
- `Admin` sem contexto falha;
- `Admin` com contexto de outra loja falha com `STORE_MISMATCH`;
- `Admin` com contexto válido e rota correspondente passa;
- rota sem `storeId` usa a loja do contexto;
- `Customer` falha;
- handler de resultado produz o `code` esperado.

`backend/tests/Urbeat.UnitTests/WebApi/AdminStoreAuditStampTests.cs`

- `AuditLog` de mutação delegada: `UserId = admin`, `SubjectUserId = owner`,
  `AdminStoreContextId` preenchido;
- sessão de lojista: colunas novas `null` e `UserId` inalterado;
- operação fora de request (job/seeder) não estampa nada.

`backend/tests/Urbeat.UnitTests/Infrastructure/Persistence/AdminStoreReadRepositoryTests.cs`

- paginação, ordenação determinística e clamp de `pageSize`;
- busca por nome, slug, e-mail e nome do proprietário;
- filtros de status/publicação/abertura/assinatura;
- loja sem logo e loja em onboarding incluídas;
- item não contém `Document`, `PixKey`, `PhoneNumber` nem endereço;
- loja sem `SellerSubscriptionStatus` → `subscription.status = None`.

### 13.2 Integração (WebApi + InMemory)

`backend/tests/Urbeat.IntegrationTests/Api/AdminStoresFlowTests.cs`

- `GET /api/admin/stores` sem token → `401`; `Seller`/`Customer` → `403`; `Admin` → `200`;
- filtros e busca retornam o subconjunto esperado; paginação estável;
- loja criada pelo wizard (`IsPublished = false`) aparece e é filtrável como `onboarding`;
- `Cache-Control: no-store` na listagem;
- corpo não contém `document`, `pixKey`, `phoneNumber`, `token`, `password`, `secret`.

`backend/tests/Urbeat.IntegrationTests/Api/AdminStoreAccessFlowTests.cs`

- `POST .../access-context` sem token → `401`; com token de lojista → `403`; admin → `201`;
- token retornado uma única vez; `GET` não retorna token; `DELETE` → `204` e idempotente;
- reutilizar token revogado/expirado → `403` com o `code` correto;
- header de contexto com JWT de `Seller` → `403 ADMIN_STORE_CONTEXT_NOT_ALLOWED`;
- contexto da loja A em rota da loja B → `403 ADMIN_STORE_CONTEXT_STORE_MISMATCH`;
- admin com contexto: `GET /api/stores/my-store` devolve a loja do contexto;
- admin com contexto edita loja, endereço, horários, delivery-config, categoria, produto,
  adicional, consulta pedidos, altera status de pedido e publica quando elegível;
- admin com contexto **não** consegue: excluir loja, abrir/fechar loja, contratar mensalidade,
  alterar perfil do lojista, gerenciar entregadores, gateway de pagamento, avaliações ou clientes;
- admin sem contexto recebe `403` em todos os endpoints de loja;
- lojista continua funcionando exatamente como antes (regressão de `StoreFlowTests`,
  `StoreProductsFlowTests`, `StoreCategoriesFlowTests`, `StoreAdditionalsFlowTests`,
  `StoreOwnershipSecurityTests`);
- loja em onboarding: contexto abre e edição funciona; publicar sem requisitos → `400` (regra
  existente preservada);
- `AuditLogs` contém `AdminStoreAccessGranted`, `AdminStoreActionExecuted` e linhas de negócio com
  `UserId = admin`, `SubjectUserId = owner` e `AdminStoreContextId`;
- nenhuma resposta/log contém o token (asserção sobre corpo e sobre `AuditLog.Description`);
- rate limit de criação de contexto → `429` ao estourar o bucket;
- `GET /api/subscriptions/my` sob contexto retorna a assinatura do proprietário; `POST contract`
  → `403`.

### 13.3 Relacionais (Testcontainers PostgreSQL 16)

`backend/tests/Urbeat.IntegrationTests/Relational/AdminStoreAccessRelationalTests.cs`

- índice único de `TokenHash` impede duplicata;
- criação concorrente até `MaxActiveContextsPerAdmin` mantém no máximo o limite ativo;
- expiração gravada em UTC e consultada por índice;
- exclusão da loja remove contextos (`Cascade`) e requisições seguintes falham com
  `STORE_MISSING`;
- mutação que falha não deixa `AdminStoreActionExecuted` órfão na mesma transação;
- projeção da listagem funciona em Npgsql (busca `lower(...)`, joins de owner/subscription);
- auto-skip quando Docker não estiver disponível, como os demais testes `Relational`.

### 13.4 Frontend (Jest)

- `admin-stores.service.spec.ts`: verbos, URLs, query params, `no-store` não é responsabilidade do
  cliente, mapeamento de modelos.
- `admin-store-session.service.spec.ts`: grava apenas em `sessionStorage`, restaura e valida,
  expira pelo timer, limpa no logout, rejeita `returnUrl` externo.
- `admin-store-context.interceptor.spec.ts`: header só nos prefixos delegáveis; ausente em
  `/api/auth/` e `/api/admin/`; `403` com `code` marca sessão inválida e não faz logout.
- `auth.guard.spec.ts`: `Seller` inalterado; `Admin` com sessão passa; `Admin` sem sessão vai para
  `/admin/lojas` sem `logout`; `Customer` mantém o comportamento atual.
- `admin-stores-page.component.spec.ts`: loading/vazio/erro; debounce da busca; filtros resetam
  página; paginação; clique e teclado abrem a loja; placeholder quando `logoUrl` é `null`; badges;
  `429` e `409` exibidos; `returnUrl` preservado.
- `store-logo-avatar.component.spec.ts`: inicial do nome, troca para placeholder em `error`,
  `alt` correto.
- `seller-app-shell.component.spec.ts`: banner visível apenas com sessão; texto
  `modo administrador — loja ...`; CTA revoga e navega; menus não delegáveis ocultos; CTA com
  classe de alvo ≥ 44 px.
- `seller-shell.facade.spec.ts`: em modo administrador não chama notificações nem hub; carrega
  `my-store`.
- `store-config-page.component.spec.ts`: em modo administrador não chama perfil do lojista e
  salvar loja não depende de `updateSellerProfile`.
- `seller-store-info-page.component.spec.ts`: zona de exclusão oculta em modo administrador.
- `app.routes.admin.spec.ts`: `lojas` presente na ordem esperada.
- `branding-consistency.spec.ts`: cópias novas não introduzem `Urbeat`.

### 13.5 Validação prevista (executar na implementação)

```powershell
dotnet build backend/Urbeat.sln
dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~AdminStore"
dotnet test backend/tests/Urbeat.IntegrationTests --filter "FullyQualifiedName~AdminStore"
dotnet test backend/tests/Urbeat.IntegrationTests --filter "FullyQualifiedName~StoreOwnershipSecurity"
cd frontend
npx jest --no-coverage src/app/core/services/admin-store-session.service.spec.ts
npx jest --no-coverage src/app/core/interceptors/admin-store-context.interceptor.spec.ts
npx jest --no-coverage src/app/features/admin-stores/admin-stores-page.component.spec.ts
npx jest --no-coverage src/app/features/seller-shell/seller-app-shell.component.spec.ts
npx jest --no-coverage src/app/core/guards/auth.guard.spec.ts
npx jest --no-coverage src/app/app.routes.admin.spec.ts
npx ng build --configuration production
```

## 14. Critérios de aceite

1. `/admin` exibe o item lateral **Lojas**; `/admin/lojas` lista lojas paginadas com busca e
   filtros de status, publicação, abertura e assinatura.
2. Cada linha mostra logo (ou placeholder), nome, slug, proprietário, status, publicação,
   abertura e assinatura; lojas sem logo usam placeholder sem imagem quebrada.
3. Lojas criadas pelo wizard, com `IsPublished = false`, aparecem e podem ser abertas desde que
   exista registro persistido.
4. Abrir uma loja cria um contexto server-side que registra explicitamente `AdminUserId`, `StoreId`
   e `ContextId`, com validade de 30 minutos e token opaco entregue uma única vez.
5. Nenhuma senha, token ou refresh token de lojista é usado, emitido ou armazenado; não há
   impersonação silenciosa e o claim de identidade do administrador é preservado.
6. Um header de contexto sem JWT de `Admin` válido não autoriza nada; um JWT de `Seller` com o
   header é rejeitado.
7. Um contexto só acessa a loja à qual foi emitido; rota/query de outra loja retorna `403` com
   `ADMIN_STORE_CONTEXT_STORE_MISMATCH`.
8. Dentro do contexto, o administrador visualiza e edita produtos, categorias, adicionais,
   horários, endereço, bairros, taxas, configurações, pedidos e publicação usando as mesmas telas e
   contratos do lojista.
9. Excluir loja, abrir/fechar loja, mensalidade, perfil do lojista, entregadores, avaliações,
   clientes e gateway permanecem bloqueados para o administrador.
10. A UI exibe permanentemente `modo administrador — loja X` e `Voltar ao painel administrativo`
    em todas as telas do dashboard, com alvo de toque ≥ 44 px.
11. O retorno ao painel encerra o contexto, limpa a sessão da aba e volta à lista preservando
    filtros e página; nunca redireciona para o login de lojista.
12. Expiração, revogação, logout, remoção do papel `Admin` e exclusão da loja invalidam o acesso;
    a UI trata cada caso sem perder edição em andamento.
13. Toda concessão, revogação, expiração, negativa e mutação delegada gera auditoria com ator
    administrador, lojista em nome de quem se agiu, loja, contexto e IP — sem token, senha, hash,
    documento ou chave Pix.
14. Fluxos de lojista e de cliente não mudam de contrato nem de comportamento; guards e testes de
    rotas existentes continuam passando (exceto a lista exata de filhos de `/admin`, atualizada).
15. Build de produção e as suítes listadas em 13.5 passam.

## 15. Riscos

- **R1 — Token de contexto no navegador.** Qualquer XSS no domínio pode ler o `sessionStorage`.
  Mitigação: TTL de 30 min, vínculo ao JWT de administrador, revogação, `no-store`, `sessionStorage`
  em vez de `localStorage` e escopo de uma loja. A superfície é a mesma das demais sessões do app.
- **R2 — Volume de auditoria.** `AdminStoreActionExecuted` e negativas podem gerar muitas linhas em
  uso intenso. Mitigação: uma linha por mutação/negativa relevante, índices existentes de
  `AuditLog`, retenção operacional fora do produto. Observar crescimento.
- **R3 — Busca textual na lista.** `lower(...) LIKE '%termo%'` sem índice trigram pode ficar lenta
  com muitas lojas. Mitigação: índices em `Name`/`Slug`, paginação obrigatória; avaliar
  `pg_trgm` se o volume justificar (fora desta entrega).
- **R4 — Esquecimento de política em controller novo.** Um controller futuro que use `SellerOnly`
  falha fechado (admin recebe `403`), nunca abre demais. O risco é funcional, não de segurança.
  Mitigação: tabela 6.1 revisada em code review e teste de integração por área.
- **R5 — `storeId` no corpo.** `POST /api/stores/delivery-times` não pode ser validado no
  middleware. A proteção é a checagem de posse no serviço, com proprietário derivado do contexto e
  a invariante de uma loja por proprietário. Mitigação: teste de integração de loja cruzada.
- **R6 — Sem tempo real no modo administrador.** Notificações e hub do lojista não são iniciados;
  pedidos novos não aparecem sozinhos. Aceito para evitar emitir identidade de lojista; a página
  atualiza ao navegar/recarregar. Se necessário no futuro, criar hub administrativo próprio.
- **R7 — Reutilização de telas do lojista.** Telas assumem perfil, assinatura e impressão do
  lojista. Mitigação: ocultar/curto-circuitar chamadas não delegáveis (8.7) e cobrir com Jest e
  testes de integração. Regressão no fluxo do lojista é o principal risco de implementação.
- **R8 — Janela residual do JWT administrativo.** A remoção do papel invalida o contexto na
  próxima requisição, mas endpoints `AdminOnly` ainda aceitam o JWT até expirar (≤ 15 min). Mesmo
  comportamento documentado na especificação de administradores.
- **R9 — Múltiplas abas e limite de contextos.** Abrir a mesma loja em muitas abas pode revogar o
  contexto mais antigo ao atingir o limite. Mitigação: limite de 5, aviso na UI quando a sessão for
  invalidada e retorno à lista.
- **R10 — Rate limit por administrador atrás de NAT.** Vários administradores no mesmo IP podem
  compartilhar o fallback do bucket; 30/min por admin reduz falso positivo. Monitorar `429`.
- **R11 — Semântica ampliada de `my-store`.** A rota passa a resolver a loja do contexto. Um bug de
  header/sessão poderia exibir a loja errada; o servidor só anexa o contexto com role `Admin`,
  token válido e loja correspondente, e a UI limpa a sessão em qualquer `403` de contexto.
  Mitigação: testes de integração e Jest específicos.
- **R12 — Papel `Admin` acumulado com `Seller`.** Quando há feature de contexto válida, o contexto
  vence: a identidade efetiva é a loja do contexto, mesmo que o token também tenha role `Seller`.
  Sem contexto, o comportamento é o do lojista. Um token sem role `Admin` que envie o header é
  rejeitado no middleware, então não há como "virar" administrador pelo header.
