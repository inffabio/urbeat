# Coleção Postman do Urbeat — Plano de Implementação

> **For agentic workers:** REQUIRED SUB-SKILL: use `superpowers:subagent-driven-development` (recomendado) ou `superpowers:executing-plans` para executar este plano tarefa por tarefa. Cada passo tem checkbox (`- [ ]`). Não execute chamadas contra a OCI e não faça commits: o diff fica para revisão humana (especificação §15.3 e §15.4).

**Goal:** criar a coleção Postman v2.1 importável que cobre as 179 ações REST/webhook do `Urbeat.WebApi`, organizada em 14 features e 44 subpastas de negócio, com ambientes Local/OCI sem credenciais, guardas de execução, exemplos derivados dos DTOs, fixtures reais, scripts de verificação de cobertura/estrutura e um subagente OpenCode de manutenção.

**Architecture:** `postman/feature-map.json` é o mapa autoritativo 1:1 entre as 179 ações e feature/subpasta. A coleção é o artefato principal, mantido como fonte versionada (não gerado em build). Dois scripts PowerShell validam, respectivamente, contrato↔mapa↔coleção e estrutura/segredos/guardas. Um guard no `prerequest` de coleção bloqueia escrita/destrutivas/webhooks por padrão (`runMode=read-only`, `allowWebhooks=false`). Ambientes guardam todos os valores mutáveis; nada sensível é versionado.

**Tech Stack:** Postman Collection v2.1 (JSON), PowerShell 5.1/7, Newman via `npx`, ASP.NET Core 9 (somente leitura de controllers/DTOs), .NET minimal APIs não são tocadas.

---

## 0. Correções e lacunas da especificação detectadas na verificação do código

Estas divergências foram confirmadas contra os controllers/DTOs atuais e **devem** ser resolvidas como abaixo. Todas são reportadas no aceite; nenhuma altera a arquitetura aprovada.

| # | Especificação diz | Código real (fonte) | Resolução no plano |
| --- | --- | --- | --- |
| D1 | §7.5: `POST /api/auth/refresh` grava "token do último login" | O refresh devolve só `accessToken`; não há estado de qual papel fez o último login | Adicionar variável de ambiente não secreta `lastLoginRole` (`customer`/`seller`/`admin`/`driver`), escrita pelos logins; o refresh grava no token correspondente. Documentar em `postman/README.md`. |
| D2 | §7.5: `GET /api/outbox/failed` captura `[0].id` | `OutboxOperations.ListFailed` devolve `{ page, pageSize, totalItems, totalPages, items: [...] }` | Capturar `items[0].id`. |
| D3 | §7.5: `GET /api/customer/notifications` e `GET /api/seller/notifications` capturam `[0].id` | Ambos devolvem `{ unreadCount, items: [...] }` | Capturar `items[0].id`. |
| D4 | §9.1: todo corpo com todas as chaves do DTO | `CreateProductRequestDto`/`UpdateProductRequestDto` possuem `weightConfig`; o exemplo §9.4 o omite | Incluir `"weightConfig": null` no exemplo de produto (B28). |
| D5 | §9.5: `POST /api/checkout/confirm` usa "exatamente o mesmo corpo" de `preview` e `paymentMethod` é obrigatório (1–4) | `preview` aceita `paymentMethod: null`; `confirm`/`POST /api/orders` não o exigem — o serviço assume `CashOnDelivery` (3) quando ausente (`CheckoutService.cs:237`) | Dois exemplos: B13a (preview, `paymentMethod: null`) e B13b (confirm/orders, `paymentMethod: 1` para o fluxo Pix). Documentar que `null` vira dinheiro na entrega, para não gerar pedido com método inesperado. |
| D6 | §9.2: corpo de `/api/auth/email/confirm` usa `{{emailConfirmationCode}}`; §4.4 usa a mesma variável para `{code}` | O corpo espera o token longo do link; a rota `{code}` espera o short code | Adicionar `emailConfirmationToken` (secret, vazio) para o corpo; manter `emailConfirmationCode` para a rota. Documentar a diferença. |
| D7 | §12.3: "runMode inválido é tratado como read-only (fail closed)" | O código de exemplo só compara `mode === 'read-only'`; um valor inválido libera POSTs não destrutivos | Normalizar `mode` para `read-only` quando não for `write`/`destructive` (código da seção 3.4). |
| D8 | §11.2: Asaas "sem token configurado → 401" | `appsettings.json` versiona `AsaasWebhook:Token = "CHANGE_ME_ASAAS_WEBHOOK_TOKEN"` | Não copiar esse valor. O README instrui configurar o token local e preencher `{{asaasWebhookToken}}`; o teste de 401 usa valor divergente. |
| D9 | §17.5: executar a coleção inteira em `read-only` e "confirmar que só GET/allowlist executam" | GETs autenticados retornam 401 sem tokens; Newman não reporta requisições puladas de forma uniforme; fallback `throw` marca erro | Verificação determinística do guard em `Test-PostmanCollection.ps1 -VerifyGuard` + execução local apenas por pasta pública. O run completo manual é aceito com 401, desde que nenhuma escrita seja enviada. |
| D10 | §5 lista 8 arquivos; §15.4 exige permissão em `arquiteto.md` | A permissão atual só delega a `programador` | Alterar **somente** o bloco `permissions` de `.opencode/agents/arquiteto.md`, como exceção já prevista na §5. |
| D11 | §17.2 cita `POST/PUT/DELETE /api/stores/{storeId}/cuisine-types` em `04` | `StoresController` expõe `POST`, `GET` e `DELETE` desse recurso; **não existe PUT** | Validar `POST/GET/DELETE` em `04`, como no Anexo A. |

> Itens apenas registrados, sem mudança de conteúdo: `POST /api/auth/logout` não está na allowlist (fica `write`); `StoreDeliveryAreaDto.Id` e `StoreBusinessHourShiftDto.Id` são omitidos nos exemplos por serem opcionais para criação (não há GUID fixo permitido); `printerBase64Data` permanece vazio e `secret`; `emailChangeChallenge`, `deliveryRouteToken`, `individualDeliveryToken` e `driverInviteToken` são gravados como `secret` (a §7.3 os lista entre IDs, mas são credenciais bearer).

---

## 1. Restrições globais

- **Contagens invariantes:** 33 controllers, 179 ações, 14 features, 44 subpastas. Matriz de auth: 42 anônimas, 85 `SellerOnly`, 18 `CustomerOnly`, 23 `AdminOnly`, 5 `Role:Seller`, 5 `DeliveryDriverOnly`, 1 `[Authorize]` genérica.
- **Classificação esperada:** 78 `read` (71 GET + 7 POST da allowlist), 63 `destructive` (61 + 2 webhooks), 38 `write`. Total 179.
- **Sem credenciais/PII:** nenhum token, senha, cookie, CPF/CNPJ, e-mail real, ID de produção ou resposta de OCI em JSON, descrição, exemplos, README ou script.
- **Sem host fixo na coleção:** toda URL começa com `{{baseUrl}}/api/`; hosts existem somente nos dois ambientes.
- **Guardas por padrão:** `runMode=read-only`, `allowWebhooks=false` nos dois ambientes. Webhooks exigem `runMode=destructive` **e** `allowWebhooks=true`, apenas local.
- **OCI é proibida para escrita/destrutiva/webhook**; leitura autenticada só com conta de teste dedicada e aprovação explícita. Nenhuma chamada OCI é feita por este plano.
- **Sem commits** e sem alterar `backend/`, `frontend/`, `docker/`, `print-agent/`, `oci-mcp-server/`, `scripts/criarDeployOracleCloud/`, `scripts/deploy-internal.ps1` ou `.opencode/opencode.json`.
- **Datas** em ISO 8601 UTC (`Z`); `TimeOnly` em `HH:mm:ss`; enums **numéricos**; JSON em camelCase.
- **Postman ≥ 10.15** (para `pm.execution.skipRequest()`); Newman ≥ 6 via `npx --yes newman`.
- **Fixtures:** somente arquivos versionados (`frontend/src/assets/images/urbeat-logo.jpg` 42.769 B; `logo_urbeat.png` 79.958 B; `favicon.png` 1.522 B; `docs/BoxSuaSacolaCarrinho.jpeg` 82.812 B). Não usar `frontend/public/images/loja.svg` nem `moto.svg` (DOCTYPE/DTD são rejeitados por `DtdProcessing.Prohibit`).
- **Sem novas dependências** de backend/frontend; Newman é executado via `npx`, não é adicionado ao `package.json`.

---

## 2. Estrutura de arquivos

| Caminho | Responsabilidade |
| --- | --- |
| `postman/urbeat.postman_collection.json` | Coleção v2.1: 14 features, 44 subpastas, 179 requisições, guard de coleção, scripts de captura/teste. |
| `postman/urbeat.local.postman_environment.json` | `baseUrl=http://localhost:5000`, `webBaseUrl=http://localhost:4200`, `runMode=read-only`, `allowWebhooks=false`; demais variáveis vazias. |
| `postman/urbeat.oci.postman_environment.json` | `baseUrl=https://api.urbeat.com.br`, `webBaseUrl=https://urbeat.com.br`, mesmas flags; demais vazias. |
| `postman/feature-map.json` | Mapa autoritativo `endpoint → {feature, subfolder}` com as 14 features e 179 entradas. |
| `postman/README.md` | Importação, contas locais, confirmação/OTP, modos, fixtures, riscos da OCI, limpeza, comandos. |
| `scripts/postman/Test-PostmanCoverage.ps1` | Extrai controllers, valida mapa e coleção (cobertura, auth, classe, DTOs, destrutivas). |
| `scripts/postman/Test-PostmanCollection.ps1` | Valida schema v2.1, IDs, URLs, segredos, ambientes, multipart, guard e scripts. |
| `.opencode/agents/postman-sync.md` | Subagente de manutenção (frontmatter sem `model`). |
| `.opencode/agents/arquiteto.md` | **Somente** adicionar a permissão de delegação ao `postman-sync`. |

Nenhum outro arquivo pode ser criado ou alterado. Arquivos temporários de teste ficam em `$env:TEMP` e são removidos ao final de cada tarefa.

---

## 3. Referência compartilhada (usada por todas as tarefas)

### 3.1 Nome, URL e parâmetros

- Nome da requisição = `MÉTODO + espaço + rota`, sem constraints: `GET /api/stores/{storeId}/products`.
- URL = `{{baseUrl}}` + rota; cada `{param}` vira `{{variavel}}`:
  - `{storeId:guid}` → `{{storeId}}`; `{sequence:int}` → `{{stopSequence}}`;
  - `{token}` contextual → `{{deliveryRouteToken}}`, `{{individualDeliveryToken}}` ou `{{driverInviteToken}}` (tabela §4.4);
  - `{id}` do Outbox → `{{outboxEventId}}`; `{notificationId}` → `{{customerNotificationId}}`/`{{sellerNotificationId}}`; `{id:guid}` da landing → `{{landingContentId}}`; `{key}` → `{{systemParameterKey}}`; `{code}` → `{{emailConfirmationCode}}`; `{slug}` → `{{storeSlug}}`; `{storePath}` → `{{storePath}}`; `{cep}` → `{{cep}}`; `{section}` → `{{landingSection}}`; `{cityId:guid}` → `{{cityId}}`; `{cuisineTypeId:guid}` → `{{cuisineTypeId}}`.
- Preservar `api/LandingPageContent` com maiúsculas. Rotas de action com barra inicial (`api/orders/...`) não geram `//`.
- Query opcional entra **desabilitada** (`"disabled": true`) com descrição e exemplo; query obrigatória entra habilitada e vazia.
- `url` usa `raw`, `host: ["{{baseUrl}}"]` e `path` sem o host; nunca `http://`/`https://` na coleção.
- `header`: somente `Accept: application/json`. Não declarar `Content-Type` (Postman define JSON/boundary) nem `Authorization` (usar `request.auth`).

### 3.2 Autorização por requisição

| Origem no controller | `request.auth` |
| --- | --- |
| `[AllowAnonymous]` ou controller sem `[Authorize]` | `{ "type": "noauth" }` |
| `AdminOnly` | `{ "type": "bearer", "bearer": [{ "key": "token", "value": "{{adminToken}}", "type": "string" }] }` |
| `SellerOnly` ou `Roles = "Seller"` | bearer `{{sellerToken}}` |
| `CustomerOnly` | bearer `{{customerToken}}` |
| `DeliveryDriverOnly` | bearer `{{driverToken}}` |
| `[Authorize]` genérica (`GET /api/orders/{orderId}/review`) | bearer `{{customerToken}}` |

### 3.3 Descrição obrigatória

```
Feature: <NN> · <nome> / <subpasta>
Controller: <Arquivo>.<Action>
Auth: <No Auth | adminToken | sellerToken | customerToken | driverToken>
Classificação: <read | write | destructive | webhook>
Pré-requisitos: <variáveis/estado; "nenhum" quando não houver>
Efeitos: <o que cria/altera; "nenhum" para GET>
Captura: <variáveis gravadas ou "nenhuma">
Manual: sim        # somente destructive e webhook
```

`Controller` é rastreabilidade, não organização. A linha `Feature` deve ser idêntica ao caminho de pastas.

Cada requisição recebe um `id` UUID gerado **uma única vez** (ex.: `[guid]::NewGuid().ToString()`); a manutenção preserva IDs existentes e só cria IDs para requisições novas. Nunca regenerar a coleção inteira.

### 3.4 Guard de coleção (código completo)

Inserir em `event` de nível de coleção, `listen: "prerequest"`, `script.type: "text/javascript"`. Não usar `pm.sendRequest`.

```js
// Guard de execução do Urbeat — roda antes de qualquer requisição/pasta.
// Fail closed: runMode ausente ou inválido equivale a "read-only".
(() => {
  const name = pm.info.requestName; // "MÉTODO /api/..."
  const method = name.split(' ')[0];
  const rawMode = String(pm.environment.get('runMode') || 'read-only').toLowerCase();
  const mode = (rawMode === 'write' || rawMode === 'destructive') ? rawMode : 'read-only';
  const allowWebhooks = String(pm.environment.get('allowWebhooks') || 'false') === 'true';

  const readOnlyPosts = new Set([
    'POST /api/auth/login/customer',
    'POST /api/auth/login/seller',
    'POST /api/auth/login/admin',
    'POST /api/auth/login/driver',
    'POST /api/auth/token',
    'POST /api/auth/refresh',
    'POST /api/checkout/preview'
  ]);

  const destructive = new Set([
    'POST /api/auth/register/customer',
    'POST /api/auth/register/seller',
    'POST /api/auth/email/confirm/{code}',
    'POST /api/auth/email/confirm',
    'POST /api/auth/email/resend-confirmation',
    'POST /api/auth/forgot-password',
    'POST /api/auth/reset-password',
    'POST /api/auth/update-email',
    'DELETE /api/customer/addresses/{addressId}',
    'POST /api/neighborhoods/import-by-city',
    'PATCH /api/stores/{storeId}/status',
    'DELETE /api/stores/{storeId}/cuisine-types/{cuisineTypeId}',
    'POST /api/subscriptions/contract',
    'POST /api/stores/{storeId}/publish',
    'POST /api/printer-config/wifi-print',
    'DELETE /api/stores/{storeId}/additionals/{additionalId}',
    'DELETE /api/stores/{storeId}/categories/{categoryId}',
    'DELETE /api/stores/{storeId}/products/{productId}',
    'POST /api/checkout/confirm',
    'POST /api/checkout/customer-session',
    'POST /api/checkout/customer-verification/start',
    'POST /api/checkout/customer-verification/confirm',
    'POST /api/checkout/customer-verification/resend',
    'POST /api/orders',
    'PATCH /api/orders/store/customers/{customerUserId}/status',
    'POST /api/orders/{orderId}/complete',
    'PATCH /api/orders/{orderId}/status',
    'DELETE /api/orders/store/{orderId}/delivery-driver',
    'POST /api/orders/store/{orderId}/delivery-link',
    'POST /api/orders/{orderId}/delivery-confirmation',
    'DELETE /api/stores/{storeId}/payment-gateway',
    'PUT /api/stores/{storeId}/payment-gateway',
    'POST /api/payments/order',
    'POST /api/driver/orders/{orderId}/start',
    'POST /api/driver/orders/{orderId}/confirm-pin',
    'DELETE /api/delivery-drivers/{storeDriverId}',
    'PATCH /api/delivery-drivers/{storeDriverId}/status',
    'POST /api/delivery-drivers/invites/{token}/accept',
    'POST /api/delivery-drivers/store',
    'POST /api/delivery-drivers/store/{storeDriverId}/resend-invite',
    'POST /api/delivery-drivers/store/{storeDriverId}/revoke',
    'POST /api/delivery-drivers/store/invite',
    'POST /api/delivery-routes/store/{routeId}/revoke',
    'POST /api/delivery-routes/store/{routeId}/share',
    'POST /api/delivery-routes/store/send',
    'POST /api/public/deliveries/{token}/deliver',
    'POST /api/public/delivery-routes/{token}/stops/{sequence}/deliver',
    'POST /api/admin/subscriptions/notifications/process',
    'POST /api/admin/subscriptions/status',
    'POST /api/admin/delivery-neighborhoods',
    'POST /api/admin/import-neighborhoods-google',
    'POST /api/admin/import-neighborhoods-rj',
    'POST /api/admin/migrate-images',
    'POST /api/outbox/{id}/retry',
    'POST /api/admin/plans',
    'PUT /api/admin/plans/{planId}',
    'PATCH /api/admin/plans/{planId}/status',
    'DELETE /api/admin/system-parameters/{key}',
    'PUT /api/admin/system-parameters/{key}',
    'POST /api/admin/system-parameters/reload',
    'DELETE /api/LandingPageContent/{id}',
    'POST /api/webhooks/mercadopago',
    'POST /api/webhooks/asaas'
  ]);

  const isRead = method === 'GET' || method === 'HEAD';
  const isAllowedPost = readOnlyPosts.has(name);
  const isWebhook = name.startsWith('POST /api/webhooks/');

  let skip = false;
  if (!isRead && !isAllowedPost && mode === 'read-only') skip = true;
  if (destructive.has(name) && mode !== 'destructive') skip = true;
  if (isWebhook && !allowWebhooks) skip = true;

  if (!skip) return;
  if (pm.execution && typeof pm.execution.skipRequest === 'function') {
    pm.execution.skipRequest();
    return;
  }
  throw new Error('Execução bloqueada pelo guard do Urbeat: ' + name);
})();
```

### 3.5 Scripts de captura (templates)

Todos os scripts de captura devem ser tolerantes a erro (nunca lançar exceção) e só gravar em `2xx` com JSON válido. Sem `pm.sendRequest`.

**Login (por papel)** — `event[listen=test]`:

```js
(() => {
  try {
    if (!pm.response || pm.response.code < 200 || pm.response.code >= 300) return;
    const body = pm.response.json();
    if (!body || typeof body.accessToken !== 'string' || body.accessToken.length === 0) return;
    pm.environment.set('adminToken', body.accessToken); // trocar por customerToken/sellerToken/driverToken
    pm.environment.set('lastLoginRole', 'admin');       // trocar por customer/seller/driver
  } catch (e) { /* captura best-effort */ }
})();
```

**Refresh** — grava no token do último login:

```js
(() => {
  try {
    if (!pm.response || pm.response.code < 200 || pm.response.code >= 300) return;
    const body = pm.response.json();
    if (!body || typeof body.accessToken !== 'string' || !body.accessToken) return;
    const target = {
      customer: 'customerToken',
      seller: 'sellerToken',
      admin: 'adminToken',
      driver: 'driverToken'
    }[String(pm.environment.get('lastLoginRole') || '')];
    if (target) pm.environment.set(target, body.accessToken);
  } catch (e) { /* captura best-effort */ }
})();
```

**Campo simples / condicional** (ex.: `storeId` só se vazio):

```js
(() => {
  try {
    if (!pm.response || pm.response.code < 200 || pm.response.code >= 300) return;
    const body = pm.response.json();
    if (!body || typeof body !== 'object') return;
    const value = body.id;
    if (typeof value === 'string' && value.length > 0 && !pm.environment.get('storeId')) {
      pm.environment.set('storeId', value);
    }
  } catch (e) { /* captura best-effort */ }
})();
```

**Primeiro item de lista ou de `items`**:

```js
(() => {
  try {
    if (!pm.response || pm.response.code < 200 || pm.response.code >= 300) return;
    const body = pm.response.json();
    if (!body) return;
    const first = Array.isArray(body) ? body[0] : (Array.isArray(body.items) ? body.items[0] : null);
    if (!first || typeof first.id !== 'string' || !first.id) return;
    if (!pm.environment.get('customerNotificationId')) {
      pm.environment.set('customerNotificationId', first.id);
    }
  } catch (e) { /* captura best-effort */ }
})();
```

**Último segmento de `shareUrl`** (token de link público):

```js
(() => {
  try {
    if (!pm.response || pm.response.code < 200 || pm.response.code >= 300) return;
    const body = pm.response.json();
    const url = body && body.shareUrl;
    if (typeof url !== 'string' || !url) return;
    const clean = url.split('?')[0].replace(/\/+$/, '');
    const token = clean.split('/').pop();
    if (token) pm.environment.set('deliveryRouteToken', token); // ou individualDeliveryToken
  } catch (e) { /* captura best-effort */ }
})();
```

**Registro/rota composta**: para `POST /api/delivery-routes/store`, gravar `body.route.id` em `routeId` e, se existir `shareUrl`, o último segmento em `deliveryRouteToken`. Para `POST /api/delivery-routes/store/send`, usar `body.groups[0].shareUrl`.

**Teste público (somente GET anônimo)**:

```js
pm.test('sem 5xx', function () {
  pm.expect(pm.response.code).to.be.below(500);
});
```

Não adicionar assertivas de status a requisições autenticadas, writes ou destrutivas: o guard pode pulá-las e o estado local varia.

### 3.6 Ambientes

Formato:

```json
{
  "id": "<uuid gerado uma vez>",
  "name": "urbeat · local",
  "values": [
    { "key": "baseUrl", "value": "http://localhost:5000", "type": "default", "enabled": true }
  ],
  "_postman_variable_scope": "environment"
}
```

- `runMode` e `allowWebhooks` são `default` (texto), não secret.
- `lastLoginRole` é `default`, vazio.
- Tokens, senhas, documentos, chaves, challenges, códigos e PIN são `secret` (inclui `deliveryRouteToken`, `individualDeliveryToken`, `driverInviteToken`, `emailConfirmationCode`, `emailConfirmationToken`, `passwordResetToken`, `verificationCode`, `deliveryPin`).
- IDs e URLs são `default` e vazios.
- Conjunto de chaves idêntico nos dois ambientes; somente `baseUrl`/`webBaseUrl` diferem.

Catálogo completo (ordem alfabética dentro de cada grupo):

| Grupo | Variáveis |
| --- | --- |
| Controle | `runMode`, `allowWebhooks`, `lastLoginRole` |
| URLs/dados públicos | `baseUrl`, `webBaseUrl`, `webhookPublicUrl`, `cep`, `neighborhood`, `cuisineType`, `storePath`, `storeSlug`, `landingSection`, `cityId`, `printerPresetId` |
| Credenciais (secret) | `customerEmail`, `newCustomerEmail`, `customerPassword`, `sellerEmail`, `sellerPassword`, `adminEmail`, `adminPassword`, `driverEmail`, `driverPassword` |
| Tokens (secret) | `customerToken`, `sellerToken`, `adminToken`, `driverToken` |
| IDs/valores (default) | `storeId`, `categoryId`, `productId`, `productImageUrl`, `uploadedImageUrl`, `additionalId`, `additionalGroupId`, `addressId`, `orderId`, `orderCode`, `paymentId`, `gatewayTransactionId`, `storeDriverId`, `deliveryDriverId`, `routeId`, `customerUserId`, `sellerUserId`, `planId`, `systemParameterKey`, `landingContentId`, `outboxEventId`, `customerNotificationId`, `sellerNotificationId`, `verificationId`, `stopSequence` |
| Segredos/links/códigos (secret) | `deliveryRouteToken`, `individualDeliveryToken`, `driverInviteToken`, `verificationCode`, `deliveryPin`, `emailConfirmationCode`, `emailConfirmationToken`, `passwordResetToken`, `emailChangeChallenge`, `customerDocument`, `sellerDocument`, `storeDocument`, `storePixKey`, `asaasWebhookToken`, `paymentGatewayAccessToken`, `printerBase64Data` |

### 3.7 Multipart e fixtures

| Requisição | Campo | Query | Fixture |
| --- | --- | --- | --- |
| `POST /api/stores/upload-image` | `file` (`type: "file"`, sem `src`) | `type` habilitada; exemplo `logo`; aceita `logo`/`banner`/`products`/`store-media` | `frontend/src/assets/images/urbeat-logo.jpg` (logo), `frontend/src/assets/images/logo_urbeat.png` (banner), `frontend/src/assets/images/favicon.png` (products), `docs/BoxSuaSacolaCarrinho.jpeg` (store-media) |
| `POST /api/stores/{storeId}/products/{productId}/images` | `file` (`type: "file"`, sem `src`) | — | `docs/BoxSuaSacolaCarrinho.jpeg` |

Nenhuma entrada `formdata` pode ter `src` absoluto. A descrição de cada multipart lista o fixture e diz "selecione o arquivo manualmente; no Newman use `--working-dir` na raiz".

### 3.8 Contadores para verificação

| Momento | Requisições |
| --- | ---: |
| Após Task 2 | 0 |
| Após feature 01 | 16 |
| 02 | 25 |
| 03 | 35 |
| 04 | 60 |
| 05 | 66 |
| 06 | 74 |
| 07 | 93 |
| 08 | 115 |
| 09 | 121 |
| 10 | 124 |
| 11 | 151 |
| 12 | 176 |
| 13 | 178 |
| 14 | 179 |

---

## 4. Tarefas

### Task 1: Mapa autoritativo e verificador de cobertura

**Files:**
- Create: `postman/feature-map.json`
- Create: `scripts/postman/Test-PostmanCoverage.ps1`

**Interfaces:**
- Produces: mapa com `version`, `features[14]` (`id`, `name`, `subfolders[]`) e `endpoints{name → {feature, subfolder}}`.
- Produces: script com parâmetros `-CollectionPath`, `-FeatureMapPath`, `-ControllersPath`, `-Feature`, `-MapOnly`; sai `0` em sucesso e `1` em divergência.

- [ ] **Step 1: Criar `postman/feature-map.json` a partir do Anexo A**

  Estrutura exata:

  ```json
  {
    "version": 1,
    "features": [
      {
        "id": "01",
        "name": "Acesso e identidade",
        "subfolders": ["Cadastro", "Login", "Sessão", "Confirmação e recuperação"]
      }
    ],
    "endpoints": {
      "POST /api/auth/register/customer": { "feature": "01", "subfolder": "Cadastro" }
    }
  }
  ```

  Transcrever as 14 features (nomes sem prefixo numérico) e as 44 subpastas na ordem do fluxo do Anexo A. Transcrever as 179 chaves exatamente como na coluna "Requisição" do Anexo A. Não incluir auth/classe no mapa.

- [ ] **Step 2: Implementar a extração de controllers**

  Núcleo obrigatório (o restante é validação/report):

  ```powershell
  [CmdletBinding()]
  param(
      [string]$CollectionPath = 'postman/urbeat.postman_collection.json',
      [string]$FeatureMapPath = 'postman/feature-map.json',
      [string]$ControllersPath = 'backend/src/Urbeat.WebApi/Controllers',
      [string]$Feature,
      [switch]$MapOnly
  )
  $ErrorActionPreference = 'Stop'
  $script:failures = [System.Collections.Generic.List[string]]::new()
  function Fail([string]$m) { $script:failures.Add($m) }

  function Get-AuthKind {
      param([string]$Block, [string]$Fallback)
      if ($Block -match '\[AllowAnonymous\]') { return 'anonymous' }
      if ($Block -match 'AdminOnly') { return 'AdminOnly' }
      if ($Block -match 'SellerOnly') { return 'SellerOnly' }
      if ($Block -match 'CustomerOnly') { return 'CustomerOnly' }
      if ($Block -match 'DeliveryDriverOnly') { return 'DeliveryDriverOnly' }
      if ($Block -match 'Roles\s*=\s*"Seller"') { return 'Role:Seller' }
      if ($Block -match '\[Authorize') { return 'authenticated' }
      return $Fallback
  }

  function Get-ControllerActions {
      param([string]$Path)
      $result = [System.Collections.Generic.List[object]]::new()
      foreach ($file in (Get-ChildItem -LiteralPath $Path -Filter *Controller.cs -File | Sort-Object Name)) {
          $lines = Get-Content -LiteralPath $file.FullName
          $classLine = ($lines | Select-String -Pattern '^\s*public\s+(sealed\s+)?class\s+\w+Controller' | Select-Object -First 1)
          if (-not $classLine) { throw "Controller não encontrado em $($file.Name)" }
          $classIndex = $classLine.LineNumber - 1
          $header = ($lines[0..($classIndex - 1)]) -join "`n"
          $controllerRoute = ''
          if ($header -match '\[Route\("([^"]+)"\)\]') { $controllerRoute = $Matches[1] }
          $controllerAuth = Get-AuthKind -Block $header -Fallback 'anonymous'

          for ($i = 0; $i -lt $lines.Count; $i++) {
              if ($lines[$i] -notmatch '^\s*\[Http(Get|Post|Put|Patch|Delete)(\("([^"]*)"\))?') { continue }
              $method = $Matches[1].ToUpperInvariant()
              $template = $Matches[3]

              # percorre atributos contíguos antes e depois de [HttpX] até a declaração
              $start = $i
              while ($start -gt $classIndex -and $lines[$start - 1] -match '^\s*\[') { $start-- }
              $j = $i + 1
              while ($j -lt $lines.Count -and $lines[$j] -notmatch '\bpublic\b') { $j++ }
              $block = ($lines[$start..$j] -join "`n")

              $declaration = ($lines[$j..([Math]::Min($j + 2, $lines.Count - 1))] -join ' ')
              $action = if ($declaration -match '\b([A-Za-z_]\w*)\s*\(') { $Matches[1] } else { 'Unknown' }

              $path = if ([string]::IsNullOrEmpty($template)) { $controllerRoute } else { "$controllerRoute/$template" }
              $path = ($path -replace '^/+', '') -replace '\[controller\]', ($file.BaseName -replace 'Controller$', '')
              $path = $path -replace '\{([^}:]+):[^}]+\}', '{$1}'

              $result.Add([pscustomobject]@{
                  Name = "$method /$path"
                  Method = $method
                  Controller = $file.BaseName
                  Action = $action
                  Auth = (Get-AuthKind -Block $block -Fallback $controllerAuth)
              })
          }
      }
      return $result
  }
  ```

- [ ] **Step 3: Validar fonte, mapa e (quando houver) coleção**

  Checks mínimos, todos com `Fail`:
  1. 33 arquivos; 179 ações; zero nomes duplicados.
  2. `version=1`; 14 features com ids `01`–`14`; 44 subpastas no total; 179 entradas.
  3. Conjunto de chaves do mapa = conjunto extraído. Reportar faltantes/extras.
  4. Toda entrada aponta feature existente e subpasta declarada.
  5. Contagem por feature = `16,9,10,25,6,8,19,22,6,3,27,25,2,1`.
  6. Fronteiras explícitas: `GET /api/stores/cuisine-types` e `GET /api/public/stores/{storeId}/reviews` em `03`; `POST/GET/DELETE /api/stores/{storeId}/cuisine-types` em `04`; gateway em `09`; webhooks em `13`; `POST /api/orders` em `08`.
  7. Nenhuma feature/subpasta com nome de controller (`Auth`, `Stores`, `Orders`, ...).
  8. Com coleção: cada requisição aparece uma vez; destino igual ao mapa; `Auth:` da descrição igual à extração; `Classificação:` derivada deterministicamente (webhook se `POST /api/webhooks/*`; destructive se estiver na lista de 63; read se GET/HEAD ou na allowlist de 7 POSTs; caso contrário write).
  9. Nomes com classe `destructive` ∪ `webhook` = lista hardcoded de 63 nomes do guard (que já inclui os 2 webhooks); todo `DELETE` é destrutivo.
  10. Para cada ação com `[FromBody]` (69), a requisição correspondente tem `body.mode="raw"`, `options.raw.language="json"` e o JSON contém todas as chaves de nível superior do DTO.
  11. Exatamente 2 requisições com `IFormFile`/`formdata`.

  Implementar `Get-DtoPropertyMap` varrendo `backend/src/Urbeat.Application/**/*.cs` e `backend/src/Urbeat.WebApi/Controllers/*.cs`: ao encontrar `class <Tipo>` na profundidade 0 do arquivo, coletar propriedades `public <tipo> <Nome> { get; ... }` na profundidade 1; converter para camelCase (`[char]::ToLowerInvariant($Nome[0]) + $Nome.Substring(1)`). Caso especial: `ReorderStoreCategoriesRequestDto` (array) usa `ReorderStoreCategoriesItemDto`.

  ```powershell
  function Get-DtoPropertyMap {
      param([string[]]$Roots)
      $map = @{}
      foreach ($root in $Roots) {
          foreach ($file in (Get-ChildItem -LiteralPath $root -Recurse -Filter *.cs -File)) {
              $depth = 0
              $current = $null
              foreach ($line in (Get-Content -LiteralPath $file.FullName)) {
                  if ($depth -eq 0 -and $line -match '^\s*(?:public|internal)?\s*(?:sealed\s+)?class\s+(\w+)') {
                      $current = $Matches[1]
                      $map[$current] = [System.Collections.Generic.List[string]]::new()
                  }
                  if ($depth -eq 1 -and $current -and
                      $line -match '^\s*public\s+[\w\?<>,\[\]\. ]+\s+(\w+)\s*\{\s*get;') {
                      $map[$current].Add($Matches[1])
                  }
                  $depth += ([regex]::Matches($line, '\{')).Count
                  $depth -= ([regex]::Matches($line, '\}')).Count
                  if ($depth -le 0) { $current = $null }
              }
          }
      }
      return $map
  }
  ```

  **Encoding:** no Windows PowerShell 5.1, `Get-Content` sem `-Encoding` lê UTF-8 como ANSI e corrompe `·`, `ç`, `ã`. Ler mapa, coleção e ambientes com `[System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)` (ou `Get-Content -Encoding UTF8`) e gravar sempre em UTF-8 sem BOM quando possível.

  Saída esperada:

  ```
  OK controllers=33 actions=179 features=14 subfolders=44 endpoints=179
  OK auth anonymous=42 SellerOnly=85 CustomerOnly=18 AdminOnly=23 Role:Seller=5 DeliveryDriverOnly=5 authenticated=1
  OK class read=78 write=38 destructive=61 webhook=2 guard=63
  ```

- [ ] **Step 4: Rodar a validação do mapa (antes da coleção existir)**

  ```powershell
  powershell -NoProfile -File scripts/postman/Test-PostmanCoverage.ps1 -MapOnly
  ```

  Esperado: exit `0` e as três linhas `OK` acima (sem a parte de coleção). Se falhar, corrigir o mapa, não o script.

- [ ] **Step 5: Teste negativo do mapa**

  ```powershell
  $tmp = Join-Path $env:TEMP 'urbeat-feature-map-neg.json'
  (Get-Content postman/feature-map.json -Raw | ConvertFrom-Json) |
    ForEach-Object { $_.endpoints.PSObject.Properties.Remove('POST /api/auth/register/customer'); $_ } |
    ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $tmp -Encoding UTF8
  powershell -NoProfile -File scripts/postman/Test-PostmanCoverage.ps1 -MapOnly -FeatureMapPath $tmp
  Remove-Item -LiteralPath $tmp -Force
  ```

  Esperado: exit `1` com `missing endpoint: POST /api/auth/register/customer`.

- [ ] **Step 6: Nenhum commit**

  Não executar `git add`/`git commit`; conferir `git status --porcelain` e deixar o diff para revisão.

---

### Task 2: Esqueleto da coleção, ambientes, guard e verificador de estrutura

**Files:**
- Create: `postman/urbeat.postman_collection.json`
- Create: `postman/urbeat.local.postman_environment.json`
- Create: `postman/urbeat.oci.postman_environment.json`
- Create: `scripts/postman/Test-PostmanCollection.ps1`

**Interfaces:**
- Consumes: nomes de features/subpastas do mapa (Task 1).
- Produces: coleção com `info`, 14 pastas e 44 subpastas vazias, `event` de guard; ambientes completos; script com `-CollectionPath`, `-LocalEnvironment`, `-OciEnvironment`, `-VerifyGuard`; exit `0/1`.

- [ ] **Step 1: Criar o esqueleto da coleção**

  ```json
  {
    "info": {
      "_postman_id": "<uuid gerado uma vez>",
      "name": "urbeat · API REST",
      "description": "Coleção de homologação da API do urbeat. runMode=read-only por padrão; webhooks exigem allowWebhooks=true. Ver postman/README.md.",
      "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json"
    },
    "item": [
      {
        "name": "01 · Acesso e identidade",
        "description": "Cadastro, login, sessão, confirmação e recuperação de acesso.",
        "item": [
          { "name": "Cadastro", "item": [] }
        ]
      }
    ],
    "event": [
      {
        "listen": "prerequest",
        "script": { "type": "text/javascript", "exec": ["<linhas do guard da seção 3.4>"] }
      }
    ]
  }
  ```

  Repetir as 14 features/44 subpastas exatamente como no mapa e na ordem do fluxo. Não criar `variable` de coleção (tudo é ambiente).

- [ ] **Step 2: Criar os dois ambientes com o catálogo completo**

  Usar a seção 3.6. Local: `baseUrl=http://localhost:5000`, `webBaseUrl=http://localhost:4200`. OCI: `baseUrl=https://api.urbeat.com.br`, `webBaseUrl=https://urbeat.com.br`. Nos dois: `runMode=read-only`, `allowWebhooks=false`, todas as demais `value=""`. `lastLoginRole`, `emailConfirmationToken` e todos os tokens/IDs vazios.

- [ ] **Step 3: Implementar `Test-PostmanCollection.ps1`**

  Checks mínimos:
  1. JSON válido; `info.schema` v2.1; `info.name` exato; `_postman_id` GUID; `item.id` presente em toda requisição e GUID único.
  2. 14 pastas e 44 subpastas com os nomes do mapa; nenhuma pasta/subpasta com nome de controller.
  3. Toda `request.url.raw` começa com `{{baseUrl}}/api/`; sem `http://`, `https://`, `//` após `{{baseUrl}}`, `:guid` ou `:int`.
  4. `Accept: application/json` presente; nenhum header `Content-Type`/`Authorization`; `auth.type` em `noauth|bearer`; bearer só com `{{customerToken}}`, `{{sellerToken}}`, `{{adminToken}}`, `{{driverToken}}`.
  5. Corpo `raw`/`json` parseável; nenhum GUID literal no corpo (permitir UUIDs apenas em `id`, `_postman_id` e `info`); nenhuma URL de imagem externa (o `websiteUrl` público de B14/B15 é permitido).
  6. Exatamente 2 `formdata` com campo `file` tipo `file`, sem `src`; descrição cita o fixture.
  7. Descrição com as linhas `Feature:`, `Controller:`, `Auth:`, `Classificação:`, `Pré-requisitos:`, `Efeitos:`, `Captura:`; `Manual: sim` em destructive/webhook.
  8. Nenhum `pm.sendRequest`/`setNextRequest` em scripts; capturas contêm `try`/`catch`.
  9. Guard: existe `event` prerequest de coleção; extrair `readOnlyPosts` (7 nomes) e `destructive` (63 nomes) e comparar com as listas hardcoded no script; `mode` normalizado; `isWebhook` presente.
  10. Ambientes: nomes/URLs exatos; `runMode=read-only`; `allowWebhooks=false`; mesmas chaves; todos os valores vazios exceto `baseUrl`/`webBaseUrl`/`runMode`/`allowWebhooks`; sem chave duplicada.
  11. Scan de segredos nos 3 JSONs: `eyJ[A-Za-z0-9_-]{10,}`, `(?i)bearer\s+[A-Za-z0-9._-]{10,}`, `CHANGE_ME`, `-----BEGIN`, `asaas-access-token` com valor literal (permitir somente `{{asaasWebhookToken}}`), `(?<![+\d])\d{11}(?!\d)`, `\d{14}`, `@urbeat.com.br` (exceto README).
  12. Saída `OK structure folders=14 subfolders=44 requests=N` e `OK guard ...`; exit `1` com a lista de falhas.

- [ ] **Step 4: Rodar o verificador de estrutura no esqueleto**

  ```powershell
  powershell -NoProfile -File scripts/postman/Test-PostmanCollection.ps1 `
    -CollectionPath postman/urbeat.postman_collection.json `
    -LocalEnvironment postman/urbeat.local.postman_environment.json `
    -OciEnvironment postman/urbeat.oci.postman_environment.json `
    -VerifyGuard
  ```

  Esperado: `OK structure folders=14 subfolders=44 requests=0` e `OK guard read-only=7 destructive=63`.

- [ ] **Step 5: Teste negativo do guard e dos segredos**

  Em uma cópia temporária da coleção, trocar `runMode` para `"lixo"` em uma cópia do ambiente e validar que `POST /api/checkout/preview` continua executável e que um POST fora da allowlist é bloqueado (simulação do script). Em outra cópia, inserir `"eyJhbGciOiJIUzI1NiJ9.fake.fake"` em uma descrição e confirmar exit `1` (`secret pattern`). Remover as cópias de `$env:TEMP`.

- [ ] **Step 6: Nenhum commit**

---

### Task 3: Feature `01 · Acesso e identidade` (16 requisições)

**Files:**
- Modify: `postman/urbeat.postman_collection.json`

**Interfaces:**
- Consumes: templates 3.3/3.5, exemplos B01–B07.
- Produces: subpastas `Cadastro` (2), `Login` (5), `Sessão` (2), `Confirmação e recuperação` (7).

- [ ] **Step 1: Inserir as 16 requisições**

  Seguir a tabela do Anexo A (feature 01). Ordem: `Cadastro` → `Login` → `Sessão` → `Confirmação e recuperação`.
  - `POST /api/auth/register/customer` e `/register/seller`: corpo B01; captura `userId`→`customerUserId`/`sellerUserId` e `emailChangeChallenge` (se string não vazia).
  - Logins: corpo B02; captura `accessToken` na variável do papel e `lastLoginRole`.
  - `POST /api/auth/token`: mesmo corpo B02; grava `customerToken` e `lastLoginRole=customer`.
  - `POST /api/auth/logout`: sem corpo; script de teste limpa `lastLoginRole` em `2xx`.
  - `POST /api/auth/refresh`: sem corpo; script da seção 3.5 (refresh).
  - `POST /api/auth/email/confirm`: B03 (`token: "{{emailConfirmationToken}}"`).
  - `POST /api/auth/email/confirm/{code}`: sem corpo; path `{{emailConfirmationCode}}`.
  - `POST /api/auth/email/resend-confirmation`: B04; `forgot-password`: B05; `reset-password`: B06; `update-email`: B07.
  - `GET /api/auth/validate-reset-token`: query `token` habilitada, valor `{{passwordResetToken}}`.

- [ ] **Step 2: Conferir contagens da feature**

  ```powershell
  powershell -NoProfile -File scripts/postman/Test-PostmanCoverage.ps1 -Feature 01
  ```

  Esperado: `OK feature=01 requests=16 missing=0 duplicate=0`.

- [ ] **Step 3: Conferir estrutura acumulada**

  ```powershell
  powershell -NoProfile -File scripts/postman/Test-PostmanCollection.ps1 `
    -CollectionPath postman/urbeat.postman_collection.json `
    -LocalEnvironment postman/urbeat.local.postman_environment.json `
    -OciEnvironment postman/urbeat.oci.postman_environment.json
  ```

  Esperado: `OK structure ... requests=16` e nenhum `secret pattern`.

- [ ] **Step 4: Validar JSON e descrições**

  ```powershell
  $c = Get-Content postman/urbeat.postman_collection.json -Raw | ConvertFrom-Json
  ($c.item | Where-Object name -like '01 *').item | ForEach-Object { "{0}={1}" -f $_.name, $_.item.Count }
  ```

  Esperado: `Cadastro=2`, `Login=5`, `Sessão=2`, `Confirmação e recuperação=7`.

---

### Task 4: Feature `02 · Conta do cliente` (9 requisições)

**Files:**
- Modify: `postman/urbeat.postman_collection.json`

- [ ] **Step 1: Inserir as 9 requisições** conforme Anexo A (feature 02):
  - `Perfil`: `GET home`; `PUT me` B09; `GET me`.
  - `Endereços`: POST/PUT com B08; GET; DELETE.
  - `Notificações`: GET captura `items[0].id`→`customerNotificationId` (só se vazio); PATCH `{notificationId}/read` com `{{customerNotificationId}}`.

- [ ] **Step 2: Rodar cobertura e estrutura**

  ```powershell
  powershell -NoProfile -File scripts/postman/Test-PostmanCoverage.ps1 -Feature 02
  powershell -NoProfile -File scripts/postman/Test-PostmanCollection.ps1 `
    -CollectionPath postman/urbeat.postman_collection.json `
    -LocalEnvironment postman/urbeat.local.postman_environment.json `
    -OciEnvironment postman/urbeat.oci.postman_environment.json
  ```

  Esperado: `OK feature=02 requests=9`; `requests=25`; exit `0`.

- [ ] **Step 3: Conferir** `Perfil=3`, `Endereços=4`, `Notificações=2`.

---

### Task 5: Feature `03 · Descoberta e vitrine` (10 requisições)

**Files:**
- Modify: `postman/urbeat.postman_collection.json`

- [ ] **Step 1: Inserir as 10 requisições** conforme Anexo A:
  - `Lojas`: `GET /api/public/stores` (query `cuisineType` desabilitada com exemplo "Hamburgueria"); `{storeId}`; `delivery-check` (query `neighborhood` **habilitada** com `{{neighborhood}}`); `by-path/{storePath}`; `by-slug/{slug}`.
  - `Cardápio`: 3 GETs de catálogo.
  - `Tipos de culinária`: `GET /api/stores/cuisine-types` (auth No Auth — não herdar SellerOnly).
  - `Avaliações`: `GET /api/public/stores/{storeId}/reviews`.
  - Adicionar o teste `sem 5xx` (3.5) em todas (são anônimas).

- [ ] **Step 2: Rodar cobertura e estrutura**

  ```powershell
  powershell -NoProfile -File scripts/postman/Test-PostmanCoverage.ps1 -Feature 03
  ```

  Esperado: `OK feature=03 requests=10`; total `35`; `Lojas=5`, `Cardápio=3`, `Tipos de culinária=1`, `Avaliações=1`.

- [ ] **Step 3: Smoke opcional local** (só se a API estiver de pé; não executar na OCI)

  ```powershell
  npx --yes newman run postman/urbeat.postman_collection.json `
    -e postman/urbeat.local.postman_environment.json `
    --folder "03 · Descoberta e vitrine" --reporters cli
  ```

  Esperado: nenhum 5xx; `404`/lista vazia são aceitos.

---

### Task 6: Feature `04 · Loja e operação` (25 requisições)

**Files:**
- Modify: `postman/urbeat.postman_collection.json`

- [ ] **Step 1: Inserir `Onboarding e perfil` (9)**: `GET seller/panel`, `GET seller/profile`, `PUT seller/profile` (B54), `POST stores` (B14; captura `storeId`/`storeSlug`), `PUT stores/{storeId}` (B15), `GET/PUT stores/{storeId}/address` (B16), `GET stores/my-store` (captura `storeId` só se vazio), `POST stores/upload-image` (multipart `logo`, captura `url`→`uploadedImageUrl`; descrição com fixture e limites 2/5/6 MB).
- [ ] **Step 2: Inserir `Horários e abertura` (3)**: `PUT business-hours` (B17), `GET business-hours`, `PATCH status` (B18).
- [ ] **Step 3: Inserir `Cobertura de entrega` (10)**: `GET neighborhoods/cities` (Role:Seller→sellerToken); `GET .../map` (query `storeId` desabilitada); `GET .../search` (queries `storeId`/`search`/`activeOnly` desabilitadas, exemplo `true`); `POST neighborhoods/import-by-city` (B23); `PATCH stores/{storeId}/delivery-config` (B19); `POST stores/delivery-neighborhoods` (B22); `GET stores/delivery-neighborhoods` (query `city` habilitada, exemplo "São Paulo"); `GET stores/delivery-neighborhoods-by-store` (query `storeId` habilitada com `{{storeId}}`, `radiusKm` desabilitada); `GET delivery-times`; `POST delivery-times` (B21).
- [ ] **Step 4: Inserir `Tipos de culinária` (3)**: `POST` (B20; captura `cuisineTypeId`), `GET`, `DELETE`.
- [ ] **Step 5: Rodar**

  ```powershell
  powershell -NoProfile -File scripts/postman/Test-PostmanCoverage.ps1 -Feature 04
  ```

  Esperado: `OK feature=04 requests=25`; total `60`; subpastas `9/3/10/3`; nenhuma query opcional habilitada indevidamente.

---

### Task 7: Feature `05 · Publicação e assinatura` (6 requisições)

**Files:**
- Modify: `postman/urbeat.postman_collection.json`

- [ ] **Step 1:** `Publicação`: `GET /api/stores/{storeId}/publish/summary` (read) e `POST /api/stores/{storeId}/publish` (destructive, sem corpo).
- [ ] **Step 2:** `Assinatura`: `GET plans`, `POST contract` (B32), `GET my`, `GET my/charges`.
- [ ] **Step 3:** Rodar `Test-PostmanCoverage.ps1 -Feature 05`; esperado `requests=6`, total `66`, `Publicação=2`, `Assinatura=4`.

---

### Task 8: Feature `06 · Impressão e notificações` (8 requisições)

**Files:**
- Modify: `postman/urbeat.postman_collection.json`

- [ ] **Step 1:** `Configuração de impressão`: `GET presets`, `GET store`, `PUT store` (B39; `printerPresetId` vem de `GET presets`), `POST wifi-print` (B40; destructive; `base64Data` vazio; descrição avisa que envia bytes na rede local).
- [ ] **Step 2:** `Fila de impressão`: `GET jobs` (queries `afterCursor`/`limit` desabilitadas), `POST jobs/{orderId}/result` (B41).
- [ ] **Step 3:** `Notificações do vendedor`: `GET` (captura `items[0].id`→`sellerNotificationId`), `PATCH {notificationId}/read`.
- [ ] **Step 4:** Rodar `-Feature 06`; esperado `requests=8`, total `74`, `4/2/2`.

---

### Task 9: Feature `07 · Cardápio e oferta` (19 requisições)

**Files:**
- Modify: `postman/urbeat.postman_collection.json`

- [ ] **Step 1:** `Categorias` (5): POST (B26; captura `categoryId`), GET, PUT `{categoryId}` (B26), PUT `reorder` (B27, array puro), DELETE.
- [ ] **Step 2:** `Produtos` (8): POST (B28; captura `productId`), GET, PUT `{productId}` (B28), PATCH `availability` (B29), POST `{productId}/images` (multipart, captura `imageUrl`→`productImageUrl`), POST `batch` (B30), DELETE, GET `option-groups`.
- [ ] **Step 3:** `Adicionais e grupos` (6): POST (B24; captura `additionalId`), GET, PUT `{additionalId}` (B24), PATCH `status` (B25), DELETE, GET `groups` (captura `[0].id`→`additionalGroupId` só se vazio).
- [ ] **Step 4:** Rodar `-Feature 07`; esperado `requests=19`, total `93`, `5/8/6`.

---

### Task 10: Feature `08 · Checkout e pedidos` (22 requisições)

**Files:**
- Modify: `postman/urbeat.postman_collection.json`

- [ ] **Step 1:** `Checkout` (7), nesta ordem de fluxo:
  1. `POST /api/checkout/preview` (read; B13a com `paymentMethod: null`).
  2. `POST /api/checkout/customer-verification/start` (destructive; B10; captura `verificationId`).
  3. `POST /api/checkout/customer-verification/confirm` (destructive; B11; captura `accessToken`→`customerToken` e `customerAddressId`→`addressId` se presente e vazio).
  4. `POST /api/checkout/customer-verification/resend` (destructive; B12).
  5. `POST /api/checkout/customer-session` (destructive; B10; mesmas capturas de 3).
  6. `POST /api/checkout/confirm` (destructive; B13b; captura `orderId`/`code`→`orderId`/`orderCode`).
  7. `POST /api/orders` (destructive; B13b; mesmas capturas).
- [ ] **Step 2:** `Pedidos do cliente` (3): `GET my`, `GET {orderId}`, `POST {orderId}/delivery-confirmation` (destructive, sem corpo).
- [ ] **Step 3:** `Pedidos da loja` (9): `GET store` (queries `status`,`startDateUtc`,`endDateUtc`,`page`,`pageSize` desabilitadas, exemplos 1/20), `GET store/{orderId}`, `PATCH {orderId}/status` (B52; destructive), `POST {orderId}/complete` (destructive), `PUT store/{orderId}/delivery-driver` (B51), `DELETE store/{orderId}/delivery-driver` (destructive), `POST store/{orderId}/delivery-link` (destructive; captura último segmento de `shareUrl`→`individualDeliveryToken`), `GET store/deliveries`, `GET store/report` (queries desabilitadas).
- [ ] **Step 4:** `Clientes da loja` (3): `GET store/customers` (queries `search`/`status`/`sort`/`page`/`pageSize` desabilitadas, exemplos `all`/`lastOrderDesc`/1/20), `PUT store/customers/{customerUserId}` (B55), `PATCH status` (B56; destructive).
- [ ] **Step 5:** Rodar `-Feature 08`; esperado `requests=22`, total `115`, `7/3/9/3`; confirmar que `POST /api/orders` está em `08` e não em `09`.

---

### Task 11: Feature `09 · Pagamentos` (6 requisições)

**Files:**
- Modify: `postman/urbeat.postman_collection.json`

- [ ] **Step 1:** `Gateway da loja`: `GET`, `PUT` (B31; destructive; `notificationUrl` vazio ou `{{webhookPublicUrl}}`, nunca produção), `DELETE` (destructive).
- [ ] **Step 2:** `Pagamentos do pedido`: `POST /api/payments/order` (destructive; B57; captura `paymentId`/`gatewayTransactionId`), `GET /{orderId}`, `GET /{orderId}/history`.
- [ ] **Step 3:** Rodar `-Feature 09`; esperado `requests=6`, total `121`, `3/3`.

---

### Task 12: Feature `10 · Avaliações` (3 requisições)

**Files:**
- Modify: `postman/urbeat.postman_collection.json`

- [ ] **Step 1:** `Avaliações do pedido`: `GET /api/orders/{orderId}/review` (auth `customerToken`, pois é `[Authorize]` genérica) e `POST /api/orders/{orderId}/review` (CustomerOnly, B53, write).
- [ ] **Step 2:** `Avaliações recebidas`: `GET /api/reviews/store` (SellerOnly).
- [ ] **Step 3:** Rodar `-Feature 10`; esperado `requests=3`, total `124`, `2/1`.

---

### Task 13: Feature `11 · Entregas` (27 requisições)

**Files:**
- Modify: `postman/urbeat.postman_collection.json`

- [ ] **Step 1:** `Entregadores` (11): `GET /api/delivery-drivers/store`; `POST /api/delivery-drivers` (B42; captura `id`→`storeDriverId`, `deliveryDriverId`); `POST /api/delivery-drivers/store` (destructive; B43; mesmas capturas); `PUT /{storeDriverId}` (B42); `PATCH /{storeDriverId}/status` (B44; destructive); `POST store/invite` (destructive; B45); `POST store/{storeDriverId}/resend-invite` (destructive, sem corpo); `POST store/{storeDriverId}/revoke` (destructive, sem corpo); `DELETE /{storeDriverId}` (destructive); `GET invites/{token}` (No Auth; `driverInviteToken`; captura nenhuma); `POST invites/{token}/accept` (No Auth; destructive; B46).
- [ ] **Step 2:** `Rotas de entrega` (7): `POST store` (write; B47; captura `route.id`→`routeId` e, se houver, último segmento de `shareUrl`→`deliveryRouteToken`); `GET store`; `GET store/{routeId}`; `PUT store/{routeId}` (B48); `POST store/{routeId}/share` (destructive; captura token); `POST store/{routeId}/revoke` (destructive, sem corpo); `POST store/send` (destructive; B49; captura `groups[0].shareUrl`).
- [ ] **Step 3:** `App do entregador` (5): `GET stores`, `GET orders`, `GET orders/{orderId}`, `POST orders/{orderId}/start` (destructive, sem corpo), `POST orders/{orderId}/confirm-pin` (destructive; B50).
- [ ] **Step 4:** `Links públicos de entrega` (4): `GET /api/public/deliveries/{token}` (`individualDeliveryToken`, No Auth), `POST .../deliver` (destructive; B50), `GET /api/public/delivery-routes/{token}` (`deliveryRouteToken`, No Auth), `POST .../stops/{sequence}/deliver` (destructive; `{{stopSequence}}`; B50).
- [ ] **Step 5:** Rodar `-Feature 11`; esperado `requests=27`, total `151`, `11/7/5/4`; nenhum link público capturado de listagem pública.

---

### Task 14: Feature `12 · Administração` (25 requisições)

**Files:**
- Modify: `postman/urbeat.postman_collection.json`

- [ ] **Step 1:** `Painel e planos` (5): `GET dashboard`; `GET plans`; `POST plans` (destructive; B33; captura `id`→`planId`); `PUT plans/{planId}` (destructive; B34); `PATCH plans/{planId}/status` (destructive; B35).
- [ ] **Step 2:** `Assinaturas e cobranças` (2): `POST subscriptions/status` (destructive; B36); `POST subscriptions/notifications/process` (destructive, sem corpo).
- [ ] **Step 3:** `Parâmetros do sistema` (5): `GET system-parameters` (captura `[0].key`→`systemParameterKey` só se vazio); `GET {key}`; `PUT {key}` (destructive; B37); `DELETE {key}` (destructive); `POST reload` (destructive, sem corpo).
- [ ] **Step 4:** `Manutenção e importações` (4): `POST admin/delivery-neighborhoods` (destructive; B22); `POST import-neighborhoods-rj`, `POST import-neighborhoods-google`, `POST migrate-images` (destructive, sem corpo).
- [ ] **Step 5:** `Conteúdo da landing page` (6): `GET /api/LandingPageContent` (No Auth), `GET section/{section}` (No Auth; `landingSection`), `POST` (write; B38; captura `id`→`landingContentId`), `GET {id}` (adminToken), `PUT {id}` (write; B38), `DELETE {id}` (destructive). Preservar maiúsculas na rota. A §12.4 não classifica POST/PUT da landing como destrutivos.
- [ ] **Step 6:** `Outbox` (3): `GET summary`; `GET failed` (queries `page`/`pageSize` desabilitadas; captura `items[0].id`→`outboxEventId` só se vazio); `POST {id}/retry` (destructive; `outboxEventId`).
- [ ] **Step 7:** Rodar `-Feature 12`; esperado `requests=25`, total `176`, `5/2/5/4/6/3`.

---

### Task 15: Feature `13 · Webhooks` (2 requisições)

**Files:**
- Modify: `postman/urbeat.postman_collection.json`

- [ ] **Step 1:** `Mercado Pago`: `POST /api/webhooks/mercadopago` (No Auth, classe `webhook`, B58). Descrição em maiúsculas com aviso: só local, `runMode=destructive` **e** `allowWebhooks=true`, nunca OCI; replay com simulação pode marcar pagamento como pago.
- [ ] **Step 2:** `Asaas`: `POST /api/webhooks/asaas` (No Auth, classe `webhook`, B59) com header `asaas-access-token: {{asaasWebhookToken}}` (o único header além de `Accept`; é header de contrato, não `Authorization`). Descrição idem.
- [ ] **Step 3:** Nenhum script de captura; nenhum teste de status.
- [ ] **Step 4:** Rodar `-Feature 13`; esperado `requests=2`, total `178`; confirmar no `Test-PostmanCollection.ps1` que webhooks só existem em `13 · Webhooks` e estão na lista destrutiva.

---

### Task 16: Feature `14 · Utilidades` (1 requisição)

**Files:**
- Modify: `postman/urbeat.postman_collection.json`

- [ ] **Step 1:** `GET /api/address-lookup/cep/{cep}` (No Auth, read, `{{cep}}`, teste `sem 5xx`).
- [ ] **Step 2:** Rodar `-Feature 14`; esperado `requests=1`, total `179`. A consulta depende do ViaCep: falha de rede deve ser reportada, não mascarada.

---

### Task 17: README operacional

**Files:**
- Create: `postman/README.md`

- [ ] **Step 1: Escrever as seções obrigatórias**

  ```
  # Coleção Postman do urbeat
  1. Escopo e o que a coleção não é (não é suíte de carga/regressão/produção)
  2. Requisitos (Postman ≥ 10.15; Newman ≥ 6; API local)
  3. Importação (coleção + environments; 14 features/44 subpastas/179 requisições)
  4. Ambientes (Local padrão; variantes 5281 HTTP e 7274 HTTPS; OCI só leitura)
  5. Contas locais (AdminSeed; preencher adminEmail/adminPassword manualmente; nunca versionar)
  6. Confirmação de e-mail e OTP (SMTP local, Email:LogOnly, log da API, contas confirmadas; não automatizável)
  7. runMode e allowWebhooks (tabela de modos, allowlist, fail closed, webhooks)
  8. Variáveis (catálogo, lastLoginRole, emailConfirmationToken, limpeza antes de exportar, Vault); registrar as correções: refresh grava no papel do último login, notificações/outbox capturam items[0].id e o token do link de e-mail é separado do short code.
  9. Fluxo local mínimo (17.6 da especificação, em passos numerados)
  10. Uploads e fixtures (tabela 10.2, limites, seleção manual, Newman --working-dir, SVG com DTD rejeitado)
  11. Webhooks (Mercado Pago AllowSimulation; Asaas token; proibição na OCI)
  12. Refresh (cookie HttpOnly/Secure/SameSite=Strict/Path=/; porta compartilhada; logout antes de trocar papel)
  13. OCI (produção; guard não é fronteira de segurança; PII; nenhuma escrita/webhook)
  14. Verificação (comandos da seção 5 deste plano)
  15. Limitações (14 da especificação, incluindo multipart não embutido, skipRequest dependente de versão, enums numéricos)
  16. Manutenção (postman-sync; não regenerar IDs; não commitar automaticamente)
  ```

- [ ] **Step 2: Incluir os avisos de risco**

  - Exportar ambiente preenchido grava segredos em texto claro.
  - `deliveryRouteToken`/`individualDeliveryToken` dão acesso a nome, telefone e endereço.
  - Reexecutar criações gera duplicatas; recriar o banco local (`docker compose down -v`) em vez de "limpar".
  - Tokens JWT nunca vão para issue/chat/log/print.

- [ ] **Step 3: Conferir marca e links**

  ```powershell
  Select-String -Path postman/README.md -Pattern 'Urbeat|URBEAT' -CaseSensitive
  ```

  Esperado: nenhuma ocorrência (marca é `urbeat` minúsculo). Links de arquivos devem existir no repositório.

---

### Task 18: Agente `postman-sync` e permissão de delegação

**Files:**
- Create: `.opencode/agents/postman-sync.md`
- Modify: `.opencode/agents/arquiteto.md` (somente `permissions`)

- [ ] **Step 1: Criar o agente**

  ```markdown
  ---
  description: Mantém a coleção Postman do Urbeat sincronizada com contratos, DTOs e features funcionais, sem executar operações de escrita
  mode: subagent
  ---

  Você mantém `postman/`, `scripts/postman/` e a documentação da coleção do Urbeat.

  ## Entradas obrigatórias
  1. `AGENTS.md`
  2. `docs/superpowers/specs/2026-10-28-urbeat-postman-design.md`
  3. `postman/feature-map.json` e `postman/README.md`

  ## Procedimento
  1. Rode `scripts/postman/Test-PostmanCoverage.ps1` e compare controllers/DTOs com o mapa e a coleção.
  2. Para ação nova, classifique pela árvore da §4.3 **antes** de criar a requisição; registre no mapa e depois crie na feature/subpasta.
  3. Atualize requisições existentes quando método, rota, auth, corpo, query ou DTO mudarem.
  4. Movimente endpoint entre features só com justificativa explícita; reporte como movimentação.
  5. Remova requisição e entrada do mapa quando a ação desaparecer; reporte.
  6. Atualize os dois ambientes quando surgir variável nova (segredo/ID sempre vazio).
  7. Atualize o README quando fluxo/fixtures mudarem.
  8. Rode `Test-PostmanCollection.ps1`; se a API local estiver disponível, rode o smoke público com Newman.

  ## Regras
  - Fonte de verdade: controllers/DTOs. Swagger só confirma paths/schemas.
  - Em ambiguidade de rota/auth/corpo/feature: pare e reporte; não invente pasta nem valor.
  - Webhooks somente em `13 · Webhooks`; `POST /api/orders` em `08`.
  - Preserve IDs de requisição e a ordem de fluxo; nunca regenere a coleção.
  - Não altere `backend/`, `frontend/`, `docker/`, `scripts/criarDeployOracleCloud/` nem deploy.
  - Nunca chame a OCI; nunca execute destructive/webhook; nunca crie/imprima segredos.
  - Não commite. Entregue o diff para revisão humana.

  ## Relatório final
  Liste: ações adicionadas/alteradas/removidas; features/subpastas afetadas; entradas do mapa criadas/movidas/removidas; variáveis novas; comandos executados; resultado das verificações; riscos/ambiguidades.
  ```

- [ ] **Step 2: Adicionar a permissão no `arquiteto.md`**

  Inserir no bloco `permissions` existente, sem alterar `default_agent` e sem reescrever o arquivo com `Set-Content`:

  ```yaml
  - action: subagent
    resource: postman-sync
    effect: allow
  ```

  O bloco atual contém `resource: programador`; adicionar o novo item logo após ele.

- [ ] **Step 3: Validar diff e frontmatter**

  ```powershell
  git diff -- .opencode/agents/arquiteto.md
  Get-Content .opencode/agents/postman-sync.md -TotalCount 4
  ```

  Esperado: `arquiteto.md` com apenas 3 linhas adicionadas (nenhuma linha existente reescrita); `postman-sync.md` com `description` e `mode: subagent`, sem `model`.

---

### Task 19: Verificação final, segurança e aceite

**Files:**
- Read: todos os artefatos.

- [ ] **Step 1: Cobertura completa**

  ```powershell
  powershell -NoProfile -File scripts/postman/Test-PostmanCoverage.ps1 `
    -CollectionPath postman/urbeat.postman_collection.json `
    -FeatureMapPath postman/feature-map.json `
    -ControllersPath backend/src/Urbeat.WebApi/Controllers
  ```

  Esperado: exit `0`; 33/179/14/44; auth 42/85/18/23/5/5/1; classe 78 read / 38 write / 61 destructive / 2 webhook (63 nomes no guard); `missing=0`, `extra=0`, `duplicate=0`.

- [ ] **Step 2: Estrutura, segredos e guard**

  ```powershell
  powershell -NoProfile -File scripts/postman/Test-PostmanCollection.ps1 `
    -CollectionPath postman/urbeat.postman_collection.json `
    -LocalEnvironment postman/urbeat.local.postman_environment.json `
    -OciEnvironment postman/urbeat.oci.postman_environment.json `
    -VerifyGuard
  ```

  Esperado: exit `0`; `requests=179`; `OK guard read-only=7 destructive=63`; `OK secrets=0`.

- [ ] **Step 3: Simulação determinística do guard**

  O script, com `-VerifyGuard`, simula para cada uma das 179 requisições, em `read-only`, `write`, `destructive` e `destructive+allowWebhooks`, a decisão do guard e compara com:
  - `read-only`: executa somente 71 GET + 7 allowlist; nunca executa destructive/webhook.
  - `write`: executa read + 38 writes; nunca destructive/webhook.
  - `destructive` sem `allowWebhooks`: executa tudo menos os 2 webhooks.
  - `destructive` com `allowWebhooks=true`: executa tudo.
  - `runMode=lixo`: comporta-se como `read-only`.

  Esperado: `OK guard matrix 179x4`.

- [ ] **Step 4: Testes negativos obrigatórios**

  Em cópias temporárias, provar exit `1` para: (a) endpoint removido do mapa; (b) endpoint duplicado em outra feature/subpasta; (c) auth trocada em uma requisição; (d) corpo sem uma chave de DTO; (e) `runMode` inválido liberando POST; (f) segredo `eyJ...` em descrição. Remover as cópias.

- [ ] **Step 5: Auditoria de segurança do diff**

  ```powershell
  git status --porcelain
  git diff -- postman/ .opencode/agents/ | Select-String -Pattern 'eyJ[A-Za-z0-9_-]{10,}|-----BEGIN'
  Select-String -Path postman/*.json,postman/README.md -Pattern 'eyJ[A-Za-z0-9_-]{10,}|-----BEGIN|CHANGE_ME'
  Select-String -Path postman/*.json -Pattern 'https?://' | Select-String -NotMatch 'schema.getpostman.com|api.urbeat.com.br|urbeat.com.br|localhost'
  ```

  Esperado: `git status` só com os 8 arquivos do escopo (+ permissão em `arquiteto.md` + este plano, se ainda não estiver versionado); nenhum segredo; nenhum host fora dos ambientes. Não incluir `scripts/postman/*.ps1` no scan de `CHANGE_ME`, pois os próprios verificadores contêm o padrão como regex.

- [ ] **Step 6: Smoke público local (opcional; nunca OCI)**

  ```powershell
  npx --yes newman run postman/urbeat.postman_collection.json `
    -e postman/urbeat.local.postman_environment.json `
    --folder "03 · Descoberta e vitrine" --reporters cli

  npx --yes newman run postman/urbeat.postman_collection.json `
    -e postman/urbeat.local.postman_environment.json `
    --folder "14 · Utilidades" --reporters cli
  ```

  Para um teste real do CEP, preencher `cep=01310100` apenas na sessão. Aceitar `200/404` em banco vazio; exigir ausência de 5xx. Falha do ViaCep deve ser reportada como dependência externa.

- [ ] **Step 7: Guardas manuais (Postman desktop)**

  1. `runMode=read-only`: "Run collection" no ambiente Local; confirmar que nenhuma escrita/destrutiva/webhook é enviada (401 em GETs autenticados é esperado).
  2. `write`: confirmar que criações não destrutivas executam e que destrutivas continuam bloqueadas.
  3. `destructive`: confirmar que webhooks continuam bloqueados até `allowWebhooks=true`.
  4. Restaurar `read-only`/`false`; limpar tokens/IDs (`Environment → Clear values`) e fechar sem exportar.

- [ ] **Step 8: Aceite manual de importação**

  - Importar coleção + 2 ambientes no Postman desktop atual; confirmar 14 features, 44 subpastas, 179 requisições.
  - Abrir um multipart e confirmar `file` vazio + fixture na descrição.
  - Conferir que o ambiente OCI importa e que `baseUrl` está correto; **não executar**.
  - Conferir que nenhum response de exemplo foi salvo.
  - Limpar histórico/variáveis ao final.

- [ ] **Step 9: Encerrar sem commit e sem OCI**

  Reportar arquivos alterados, comandos executados, resultados, desvios D1–D11 e riscos remanescentes.

---

## 5. Anexo A — matriz das 179 requisições

Legenda: Auth = `No Auth` | `sellerToken` | `customerToken` | `adminToken` | `driverToken`. Classe = `read` | `write` | `destructive` | `webhook`. `Bnn` refere-se ao Anexo B.

### 01 · Acesso e identidade — 16

| Subpasta | Requisição | Controller.Action | Auth | Classe | Corpo/Query | Captura |
| --- | --- | --- | --- | --- | --- | --- |
| Cadastro | `POST /api/auth/register/customer` | Auth.RegisterCustomer | No Auth | destructive | B01 | `customerUserId`←`userId`; `emailChangeChallenge` |
| Cadastro | `POST /api/auth/register/seller` | Auth.RegisterSeller | No Auth | destructive | B01 | `sellerUserId`←`userId`; `emailChangeChallenge` |
| Login | `POST /api/auth/login/customer` | Auth.LoginCustomer | No Auth | read | B02 | `customerToken`; `lastLoginRole=customer` |
| Login | `POST /api/auth/login/seller` | Auth.LoginSeller | No Auth | read | B02 | `sellerToken`; `lastLoginRole=seller` |
| Login | `POST /api/auth/login/admin` | Auth.LoginAdmin | No Auth | read | B02 | `adminToken`; `lastLoginRole=admin` |
| Login | `POST /api/auth/login/driver` | Auth.LoginDriver | No Auth | read | B02 | `driverToken`; `lastLoginRole=driver` |
| Login | `POST /api/auth/token` | Auth.GenerateToken | No Auth | read | B02 | `customerToken`; `lastLoginRole=customer` |
| Sessão | `POST /api/auth/logout` | Auth.Logout | No Auth | write | — | limpa `lastLoginRole` |
| Sessão | `POST /api/auth/refresh` | Auth.Refresh | No Auth | read | — | token do `lastLoginRole` |
| Confirmação e recuperação | `POST /api/auth/email/confirm` | Auth.ConfirmEmail | No Auth | destructive | B03 | — |
| Confirmação e recuperação | `POST /api/auth/email/confirm/{code}` | Auth.ConfirmEmailByCode | No Auth | destructive | — (`{{emailConfirmationCode}}`) | — |
| Confirmação e recuperação | `POST /api/auth/email/resend-confirmation` | Auth.ResendEmailConfirmation | No Auth | destructive | B04 | — |
| Confirmação e recuperação | `POST /api/auth/forgot-password` | Auth.ForgotPassword | No Auth | destructive | B05 | — |
| Confirmação e recuperação | `POST /api/auth/reset-password` | Auth.ResetPassword | No Auth | destructive | B06 | — |
| Confirmação e recuperação | `POST /api/auth/update-email` | Auth.UpdateEmail | No Auth | destructive | B07 | — |
| Confirmação e recuperação | `GET /api/auth/validate-reset-token` | Auth.ValidateResetToken | No Auth | read | query `token` (on, `{{passwordResetToken}}`) | — |

### 02 · Conta do cliente — 9

| Subpasta | Requisição | Controller.Action | Auth | Classe | Corpo/Query | Captura |
| --- | --- | --- | --- | --- | --- | --- |
| Perfil | `GET /api/customer/home` | Customer.GetHome | customerToken | read | — | — |
| Perfil | `PUT /api/customer/me` | Customer.UpdateCurrentCustomer | customerToken | write | B09 | — |
| Perfil | `GET /api/customer/me` | Customer.GetCurrentCustomer | customerToken | read | — | — |
| Endereços | `POST /api/customer/addresses` | CustomerAddresses.Create | customerToken | write | B08 | `addressId`←`id` |
| Endereços | `GET /api/customer/addresses` | CustomerAddresses.List | customerToken | read | — | — |
| Endereços | `DELETE /api/customer/addresses/{addressId}` | CustomerAddresses.Delete | customerToken | destructive | — | — |
| Endereços | `PUT /api/customer/addresses/{addressId}` | CustomerAddresses.Update | customerToken | write | B08 | — |
| Notificações | `GET /api/customer/notifications` | CustomerNotifications.List | customerToken | read | — | `customerNotificationId`←`items[0].id` (se vazio) |
| Notificações | `PATCH /api/customer/notifications/{notificationId}/read` | CustomerNotifications.MarkAsRead | customerToken | write | — | — |

### 03 · Descoberta e vitrine — 10

| Subpasta | Requisição | Controller.Action | Auth | Classe | Corpo/Query | Captura |
| --- | --- | --- | --- | --- | --- | --- |
| Lojas | `GET /api/public/stores` | PublicStores.List | No Auth | read | query `cuisineType` (off, ex. Hamburgeria) | — |
| Lojas | `GET /api/public/stores/{storeId}` | PublicStores.GetById | No Auth | read | — | — |
| Lojas | `GET /api/public/stores/{storeId}/delivery-check` | PublicStores.DeliveryCheck | No Auth | read | query `neighborhood` (on) | — |
| Lojas | `GET /api/public/stores/by-path/{storePath}` | PublicStores.GetByPath | No Auth | read | `{{storePath}}` | — |
| Lojas | `GET /api/public/stores/by-slug/{slug}` | PublicStores.GetBySlug | No Auth | read | `{{storeSlug}}` | — |
| Cardápio | `GET /api/public/stores/{storeId}/catalog/categories` | PublicCatalog.ListCategories | No Auth | read | — | — |
| Cardápio | `GET /api/public/stores/{storeId}/catalog/products` | PublicCatalog.ListProducts | No Auth | read | — | — |
| Cardápio | `GET /api/public/stores/{storeId}/catalog/products/featured` | PublicCatalog.ListFeaturedProducts | No Auth | read | — | — |
| Tipos de culinária | `GET /api/stores/cuisine-types` | Stores.GetCuisineTypes | No Auth | read | — | — |
| Avaliações | `GET /api/public/stores/{storeId}/reviews` | Reviews.ListByStore | No Auth | read | — | — |

### 04 · Loja e operação — 25

| Subpasta | Requisição | Controller.Action | Auth | Classe | Corpo/Query | Captura |
| --- | --- | --- | --- | --- | --- | --- |
| Onboarding e perfil | `GET /api/seller/panel` | Seller.GetPanel | sellerToken | read | — | — |
| Onboarding e perfil | `GET /api/seller/profile` | Seller.GetProfile | sellerToken | read | — | — |
| Onboarding e perfil | `PUT /api/seller/profile` | Seller.UpdateProfile | sellerToken | write | B54 | — |
| Onboarding e perfil | `POST /api/stores` | Stores.Create | sellerToken | write | B14 | `storeId`←`id`; `storeSlug`←`slug` |
| Onboarding e perfil | `PUT /api/stores/{storeId}` | Stores.Update | sellerToken | write | B15 | — |
| Onboarding e perfil | `GET /api/stores/{storeId}/address` | Stores.GetAddress | sellerToken | read | — | — |
| Onboarding e perfil | `PUT /api/stores/{storeId}/address` | Stores.UpsertAddress | sellerToken | write | B16 | — |
| Onboarding e perfil | `GET /api/stores/my-store` | Stores.GetMyStore | sellerToken | read | — | `storeId`←`id` (se vazio) |
| Onboarding e perfil | `POST /api/stores/upload-image` | Stores.UploadImage | sellerToken | write | multipart `file` + query `type` | `uploadedImageUrl`←`url` |
| Horários e abertura | `GET /api/stores/{storeId}/business-hours` | Stores.GetBusinessHours | sellerToken | read | — | — |
| Horários e abertura | `PUT /api/stores/{storeId}/business-hours` | Stores.UpsertBusinessHours | sellerToken | write | B17 | — |
| Horários e abertura | `PATCH /api/stores/{storeId}/status` | Stores.UpdateStatus | sellerToken | destructive | B18 | — |
| Cobertura de entrega | `GET /api/neighborhoods/cities` | Neighborhoods.GetCities | sellerToken | read | — | — |
| Cobertura de entrega | `GET /api/neighborhoods/cities/{cityId}/map` | Neighborhoods.GetMapData | sellerToken | read | query `storeId` (off) | — |
| Cobertura de entrega | `GET /api/neighborhoods/cities/{cityId}/search` | Neighborhoods.SearchNeighborhoods | sellerToken | read | queries `storeId`/`search`/`activeOnly` (off; ex. true) | — |
| Cobertura de entrega | `POST /api/neighborhoods/import-by-city` | Neighborhoods.ImportByCity | sellerToken | destructive | B23 | — |
| Cobertura de entrega | `PATCH /api/stores/{storeId}/delivery-config` | Stores.UpdateDeliveryConfig | sellerToken | write | B19 | — |
| Cobertura de entrega | `POST /api/stores/delivery-neighborhoods` | Stores.CreateDeliveryNeighborhood | sellerToken | write | B22 | — |
| Cobertura de entrega | `GET /api/stores/delivery-neighborhoods` | Stores.GetDeliveryNeighborhoods | sellerToken | read | query `city` (on, ex. São Paulo) | — |
| Cobertura de entrega | `GET /api/stores/delivery-neighborhoods-by-store` | Stores.GetDeliveryNeighborhoodsByStore | sellerToken | read | queries `storeId` (on), `radiusKm` (off) | — |
| Cobertura de entrega | `GET /api/stores/delivery-times` | Stores.GetDeliveryTimes | sellerToken | read | — | — |
| Cobertura de entrega | `POST /api/stores/delivery-times` | Stores.CreateDeliveryTime | sellerToken | write | B21 | — |
| Tipos de culinária | `POST /api/stores/{storeId}/cuisine-types` | Stores.CreateStoreCuisineType | sellerToken | write | B20 | `cuisineTypeId`←`id` |
| Tipos de culinária | `GET /api/stores/{storeId}/cuisine-types` | Stores.GetStoreCuisineTypes | sellerToken | read | — | — |
| Tipos de culinária | `DELETE /api/stores/{storeId}/cuisine-types/{cuisineTypeId}` | Stores.DeleteStoreCuisineType | sellerToken | destructive | — | — |

### 05 · Publicação e assinatura — 6

| Subpasta | Requisição | Controller.Action | Auth | Classe | Corpo/Query | Captura |
| --- | --- | --- | --- | --- | --- | --- |
| Publicação | `GET /api/stores/{storeId}/publish/summary` | Publish.GetPublishSummary | sellerToken | read | — | — |
| Publicação | `POST /api/stores/{storeId}/publish` | Publish.PublishStore | sellerToken | destructive | — | — |
| Assinatura | `GET /api/subscriptions/plans` | Subscriptions.ListActivePlans | sellerToken | read | — | — |
| Assinatura | `POST /api/subscriptions/contract` | Subscriptions.Contract | sellerToken | destructive | B32 | — |
| Assinatura | `GET /api/subscriptions/my` | Subscriptions.GetMySubscription | sellerToken | read | — | — |
| Assinatura | `GET /api/subscriptions/my/charges` | Subscriptions.GetMyChargeHistory | sellerToken | read | — | — |

### 06 · Impressão e notificações — 8

| Subpasta | Requisição | Controller.Action | Auth | Classe | Corpo/Query | Captura |
| --- | --- | --- | --- | --- | --- | --- |
| Configuração de impressão | `GET /api/printer-config/presets` | PrinterConfig.GetPresets | sellerToken | read | — | — |
| Configuração de impressão | `GET /api/printer-config/store` | PrinterConfig.GetStoreConfig | sellerToken | read | — | — |
| Configuração de impressão | `PUT /api/printer-config/store` | PrinterConfig.SaveStoreConfig | sellerToken | write | B39 | — |
| Configuração de impressão | `POST /api/printer-config/wifi-print` | PrinterConfig.WifiPrint | sellerToken | destructive | B40 | — |
| Fila de impressão | `GET /api/seller/printing/jobs` | SellerPrinting.ListPending | sellerToken | read | queries `afterCursor`/`limit` (off) | — |
| Fila de impressão | `POST /api/seller/printing/jobs/{orderId}/result` | SellerPrinting.RecordResult | sellerToken | write | B41 | — |
| Notificações do vendedor | `GET /api/seller/notifications` | SellerNotifications.List | sellerToken | read | — | `sellerNotificationId`←`items[0].id` (se vazio) |
| Notificações do vendedor | `PATCH /api/seller/notifications/{notificationId}/read` | SellerNotifications.MarkAsRead | sellerToken | write | — | — |

### 07 · Cardápio e oferta — 19

| Subpasta | Requisição | Controller.Action | Auth | Classe | Corpo/Query | Captura |
| --- | --- | --- | --- | --- | --- | --- |
| Categorias | `POST /api/stores/{storeId}/categories` | StoreCategories.Create | sellerToken | write | B26 | `categoryId`←`id` |
| Categorias | `GET /api/stores/{storeId}/categories` | StoreCategories.List | sellerToken | read | — | — |
| Categorias | `PUT /api/stores/{storeId}/categories/{categoryId}` | StoreCategories.Update | sellerToken | write | B26 | — |
| Categorias | `PUT /api/stores/{storeId}/categories/reorder` | StoreCategories.Reorder | sellerToken | write | B27 (array puro) | — |
| Categorias | `DELETE /api/stores/{storeId}/categories/{categoryId}` | StoreCategories.Delete | sellerToken | destructive | — | — |
| Produtos | `POST /api/stores/{storeId}/products` | StoreProducts.Create | sellerToken | write | B28 | `productId`←`id` |
| Produtos | `GET /api/stores/{storeId}/products` | StoreProducts.List | sellerToken | read | — | — |
| Produtos | `PUT /api/stores/{storeId}/products/{productId}` | StoreProducts.Update | sellerToken | write | B28 | — |
| Produtos | `PATCH /api/stores/{storeId}/products/{productId}/availability` | StoreProducts.UpdateAvailability | sellerToken | write | B29 | — |
| Produtos | `POST /api/stores/{storeId}/products/{productId}/images` | StoreProducts.UploadImage | sellerToken | write | multipart `file` | `productImageUrl`←`imageUrl` |
| Produtos | `POST /api/stores/{storeId}/products/batch` | StoreProducts.BatchUpsert | sellerToken | write | B30 | — |
| Produtos | `DELETE /api/stores/{storeId}/products/{productId}` | StoreProducts.Delete | sellerToken | destructive | — | — |
| Produtos | `GET /api/stores/{storeId}/products/option-groups` | StoreProducts.ListOptionGroupTemplates | sellerToken | read | — | — |
| Adicionais e grupos | `POST /api/stores/{storeId}/additionals` | StoreAdditionals.Create | sellerToken | write | B24 | `additionalId`←`id` |
| Adicionais e grupos | `GET /api/stores/{storeId}/additionals` | StoreAdditionals.List | sellerToken | read | — | — |
| Adicionais e grupos | `PUT /api/stores/{storeId}/additionals/{additionalId}` | StoreAdditionals.Update | sellerToken | write | B24 | — |
| Adicionais e grupos | `PATCH /api/stores/{storeId}/additionals/{additionalId}/status` | StoreAdditionals.UpdateStatus | sellerToken | write | B25 | — |
| Adicionais e grupos | `DELETE /api/stores/{storeId}/additionals/{additionalId}` | StoreAdditionals.Delete | sellerToken | destructive | — | — |
| Adicionais e grupos | `GET /api/stores/{storeId}/additionals/groups` | StoreAdditionals.ListGroups | sellerToken | read | — | `additionalGroupId`←`[0].id` (se vazio) |

### 08 · Checkout e pedidos — 22

| Subpasta | Requisição | Controller.Action | Auth | Classe | Corpo/Query | Captura |
| --- | --- | --- | --- | --- | --- | --- |
| Checkout | `POST /api/checkout/preview` | Checkout.Preview | No Auth | read | B13a | — |
| Checkout | `POST /api/checkout/customer-verification/start` | Checkout.StartCustomerVerification | No Auth | destructive | B10 | `verificationId`←`verificationId` |
| Checkout | `POST /api/checkout/customer-verification/confirm` | Checkout.ConfirmCustomerVerification | No Auth | destructive | B11 | `customerToken`←`accessToken`; `addressId`←`customerAddressId` |
| Checkout | `POST /api/checkout/customer-verification/resend` | Checkout.ResendCustomerVerification | No Auth | destructive | B12 | — |
| Checkout | `POST /api/checkout/customer-session` | Checkout.CreateCustomerSession | No Auth | destructive | B10 | `customerToken`←`accessToken`; `addressId`←`customerAddressId` |
| Checkout | `POST /api/checkout/confirm` | Checkout.Confirm | customerToken | destructive | B13b | `orderId`←`orderId`; `orderCode`←`code` |
| Checkout | `POST /api/orders` | Orders.Create | customerToken | destructive | B13b | `orderId`←`orderId`; `orderCode`←`code` |
| Pedidos do cliente | `GET /api/orders/my` | Orders.MyOrders | customerToken | read | — | — |
| Pedidos do cliente | `GET /api/orders/{orderId}` | Orders.GetById | customerToken | read | — | — |
| Pedidos do cliente | `POST /api/orders/{orderId}/delivery-confirmation` | Orders.ConfirmDelivery | customerToken | destructive | — | — |
| Pedidos da loja | `GET /api/orders/store` | Orders.StoreOrders | sellerToken | read | queries `status`/`startDateUtc`/`endDateUtc`/`page`/`pageSize` (off; 1/20) | — |
| Pedidos da loja | `GET /api/orders/store/{orderId}` | Orders.GetStoreOrder | sellerToken | read | — | — |
| Pedidos da loja | `PATCH /api/orders/{orderId}/status` | Orders.UpdateStatus | sellerToken | destructive | B52 | — |
| Pedidos da loja | `POST /api/orders/{orderId}/complete` | Orders.CompleteForSellerBoard | sellerToken | destructive | — | — |
| Pedidos da loja | `PUT /api/orders/store/{orderId}/delivery-driver` | Orders.AssignDeliveryDriver | sellerToken | write | B51 | — |
| Pedidos da loja | `DELETE /api/orders/store/{orderId}/delivery-driver` | Orders.RemoveDeliveryDriverAssignment | sellerToken | destructive | — | — |
| Pedidos da loja | `POST /api/orders/store/{orderId}/delivery-link` | Orders.ShareIndividualDelivery | sellerToken | destructive | — | `individualDeliveryToken`←último segmento de `shareUrl` |
| Pedidos da loja | `GET /api/orders/store/deliveries` | Orders.StoreDeliveries | sellerToken | read | — | — |
| Pedidos da loja | `GET /api/orders/store/report` | Orders.StoreSimpleReport | sellerToken | read | queries `startDateUtc`/`endDateUtc` (off) | — |
| Clientes da loja | `GET /api/orders/store/customers` | Orders.StoreCustomers | sellerToken | read | queries `search`/`status`/`sort`/`page`/`pageSize` (off) | — |
| Clientes da loja | `PUT /api/orders/store/customers/{customerUserId}` | Orders.UpdateStoreCustomer | sellerToken | write | B55 | — |
| Clientes da loja | `PATCH /api/orders/store/customers/{customerUserId}/status` | Orders.UpdateStoreCustomerStatus | sellerToken | destructive | B56 | — |

### 09 · Pagamentos — 6

| Subpasta | Requisição | Controller.Action | Auth | Classe | Corpo/Query | Captura |
| --- | --- | --- | --- | --- | --- | --- |
| Gateway da loja | `GET /api/stores/{storeId}/payment-gateway` | StorePaymentGateway.Get | sellerToken | read | — | — |
| Gateway da loja | `PUT /api/stores/{storeId}/payment-gateway` | StorePaymentGateway.Upsert | sellerToken | destructive | B31 | — |
| Gateway da loja | `DELETE /api/stores/{storeId}/payment-gateway` | StorePaymentGateway.Delete | sellerToken | destructive | — | — |
| Pagamentos do pedido | `POST /api/payments/order` | Payments.CreateOrderPayment | customerToken | destructive | B57 | `paymentId`←`paymentId`; `gatewayTransactionId`←`gatewayTransactionId` |
| Pagamentos do pedido | `GET /api/payments/order/{orderId}` | Payments.GetOrderPayment | customerToken | read | — | — |
| Pagamentos do pedido | `GET /api/payments/order/{orderId}/history` | Payments.GetOrderPaymentHistory | customerToken | read | — | — |

### 10 · Avaliações — 3

| Subpasta | Requisição | Controller.Action | Auth | Classe | Corpo/Query | Captura |
| --- | --- | --- | --- | --- | --- | --- |
| Avaliações do pedido | `GET /api/orders/{orderId}/review` | Reviews.GetByOrder | customerToken | read | — | — |
| Avaliações do pedido | `POST /api/orders/{orderId}/review` | Reviews.CreateOrUpdate | customerToken | write | B53 | — |
| Avaliações recebidas | `GET /api/reviews/store` | Reviews.ListBySeller | sellerToken | read | — | — |

### 11 · Entregas — 27

| Subpasta | Requisição | Controller.Action | Auth | Classe | Corpo/Query | Captura |
| --- | --- | --- | --- | --- | --- | --- |
| Entregadores | `POST /api/delivery-drivers` | DeliveryDrivers.Create | sellerToken | write | B42 | `storeDriverId`←`id`; `deliveryDriverId`←`deliveryDriverId` |
| Entregadores | `GET /api/delivery-drivers/store` | DeliveryDrivers.ListStoreDrivers | sellerToken | read | — | — |
| Entregadores | `POST /api/delivery-drivers/store` | DeliveryDrivers.CreateForStore | sellerToken | destructive | B43 | `storeDriverId`←`id`; `deliveryDriverId`←`deliveryDriverId` |
| Entregadores | `PUT /api/delivery-drivers/{storeDriverId}` | DeliveryDrivers.Update | sellerToken | write | B42 | — |
| Entregadores | `PATCH /api/delivery-drivers/{storeDriverId}/status` | DeliveryDrivers.SetStatus | sellerToken | destructive | B44 | — |
| Entregadores | `POST /api/delivery-drivers/store/invite` | DeliveryDrivers.Invite | sellerToken | destructive | B45 | — |
| Entregadores | `POST /api/delivery-drivers/store/{storeDriverId}/resend-invite` | DeliveryDrivers.ResendInvite | sellerToken | destructive | — | — |
| Entregadores | `POST /api/delivery-drivers/store/{storeDriverId}/revoke` | DeliveryDrivers.Revoke | sellerToken | destructive | — | — |
| Entregadores | `DELETE /api/delivery-drivers/{storeDriverId}` | DeliveryDrivers.Delete | sellerToken | destructive | — | — |
| Entregadores | `GET /api/delivery-drivers/invites/{token}` | DeliveryDrivers.PreviewInvite | No Auth | read | `{{driverInviteToken}}` | — |
| Entregadores | `POST /api/delivery-drivers/invites/{token}/accept` | DeliveryDrivers.AcceptInvite | No Auth | destructive | B46 | — |
| Rotas de entrega | `POST /api/delivery-routes/store` | DeliveryRoutes.Create | sellerToken | write | B47 | `routeId`←`route.id`; `deliveryRouteToken` (se houver `shareUrl`) |
| Rotas de entrega | `GET /api/delivery-routes/store` | DeliveryRoutes.ListStoreRoutes | sellerToken | read | — | — |
| Rotas de entrega | `GET /api/delivery-routes/store/{routeId}` | DeliveryRoutes.GetStoreRoute | sellerToken | read | — | — |
| Rotas de entrega | `PUT /api/delivery-routes/store/{routeId}` | DeliveryRoutes.Update | sellerToken | write | B48 | — |
| Rotas de entrega | `POST /api/delivery-routes/store/{routeId}/share` | DeliveryRoutes.Share | sellerToken | destructive | — | `deliveryRouteToken`←último segmento de `shareUrl` |
| Rotas de entrega | `POST /api/delivery-routes/store/{routeId}/revoke` | DeliveryRoutes.Revoke | sellerToken | destructive | — | — |
| Rotas de entrega | `POST /api/delivery-routes/store/send` | DeliveryRoutes.SendBatches | sellerToken | destructive | B49 | `deliveryRouteToken`←`groups[0].shareUrl` |
| App do entregador | `GET /api/driver/stores` | Driver.Stores | driverToken | read | — | — |
| App do entregador | `GET /api/driver/orders` | Driver.Orders | driverToken | read | — | — |
| App do entregador | `GET /api/driver/orders/{orderId}` | Driver.Order | driverToken | read | — | — |
| App do entregador | `POST /api/driver/orders/{orderId}/start` | Driver.StartDelivery | driverToken | destructive | — | — |
| App do entregador | `POST /api/driver/orders/{orderId}/confirm-pin` | Driver.ConfirmPin | driverToken | destructive | B50 | — |
| Links públicos de entrega | `GET /api/public/deliveries/{token}` | PublicIndividualDeliveries.GetDelivery | No Auth | read | `{{individualDeliveryToken}}` | — |
| Links públicos de entrega | `POST /api/public/deliveries/{token}/deliver` | PublicIndividualDeliveries.ConfirmDelivery | No Auth | destructive | B50 | — |
| Links públicos de entrega | `GET /api/public/delivery-routes/{token}` | PublicDeliveryRoutes.GetRoute | No Auth | read | `{{deliveryRouteToken}}` | — |
| Links públicos de entrega | `POST /api/public/delivery-routes/{token}/stops/{sequence}/deliver` | PublicDeliveryRoutes.ConfirmDelivery | No Auth | destructive | B50; `{{stopSequence}}` | — |

### 12 · Administração — 25

| Subpasta | Requisição | Controller.Action | Auth | Classe | Corpo/Query | Captura |
| --- | --- | --- | --- | --- | --- | --- |
| Painel e planos | `GET /api/admin/dashboard` | Admin.GetDashboard | adminToken | read | — | — |
| Painel e planos | `GET /api/admin/plans` | Admin.ListPlans | adminToken | read | — | — |
| Painel e planos | `POST /api/admin/plans` | Admin.CreatePlan | adminToken | destructive | B33 | `planId`←`id` |
| Painel e planos | `PUT /api/admin/plans/{planId}` | Admin.UpdatePlan | adminToken | destructive | B34 | — |
| Painel e planos | `PATCH /api/admin/plans/{planId}/status` | Admin.UpdatePlanStatus | adminToken | destructive | B35 | — |
| Assinaturas e cobranças | `POST /api/admin/subscriptions/status` | Admin.UpsertSubscriptionStatus | adminToken | destructive | B36 | — |
| Assinaturas e cobranças | `POST /api/admin/subscriptions/notifications/process` | Admin.ProcessSubscriptionNotifications | adminToken | destructive | — | — |
| Parâmetros do sistema | `GET /api/admin/system-parameters` | Admin.ListSystemParameters | adminToken | read | — | `systemParameterKey`←`[0].key` (se vazio) |
| Parâmetros do sistema | `GET /api/admin/system-parameters/{key}` | Admin.GetSystemParameter | adminToken | read | — | — |
| Parâmetros do sistema | `PUT /api/admin/system-parameters/{key}` | Admin.UpdateSystemParameter | adminToken | destructive | B37 | — |
| Parâmetros do sistema | `DELETE /api/admin/system-parameters/{key}` | Admin.DeleteSystemParameter | adminToken | destructive | — | — |
| Parâmetros do sistema | `POST /api/admin/system-parameters/reload` | Admin.ReloadSystemParameters | adminToken | destructive | — | — |
| Manutenção e importações | `POST /api/admin/delivery-neighborhoods` | AdminDeliveryNeighborhoods.CreateDeliveryNeighborhood | adminToken | destructive | B22 | — |
| Manutenção e importações | `POST /api/admin/import-neighborhoods-rj` | Admin.ImportNeighborhoodsRj | adminToken | destructive | — | — |
| Manutenção e importações | `POST /api/admin/import-neighborhoods-google` | Admin.ImportNeighborhoodsGoogle | adminToken | destructive | — | — |
| Manutenção e importações | `POST /api/admin/migrate-images` | Admin.MigrateImages | adminToken | destructive | — | — |
| Conteúdo da landing page | `GET /api/LandingPageContent` | LandingPageContent.GetAll | No Auth | read | — | — |
| Conteúdo da landing page | `GET /api/LandingPageContent/section/{section}` | LandingPageContent.GetBySection | No Auth | read | `{{landingSection}}` | — |
| Conteúdo da landing page | `POST /api/LandingPageContent` | LandingPageContent.Create | adminToken | write | B38 | `landingContentId`←`id` |
| Conteúdo da landing page | `GET /api/LandingPageContent/{id}` | LandingPageContent.GetById | adminToken | read | `{{landingContentId}}` | — |
| Conteúdo da landing page | `PUT /api/LandingPageContent/{id}` | LandingPageContent.Update | adminToken | write | B38 | — |
| Conteúdo da landing page | `DELETE /api/LandingPageContent/{id}` | LandingPageContent.Delete | adminToken | destructive | — | — |
| Outbox | `GET /api/outbox/summary` | OutboxOperations.GetSummary | adminToken | read | — | — |
| Outbox | `GET /api/outbox/failed` | OutboxOperations.ListFailed | adminToken | read | queries `page`/`pageSize` (off) | `outboxEventId`←`items[0].id` (se vazio) |
| Outbox | `POST /api/outbox/{id}/retry` | OutboxOperations.Retry | adminToken | destructive | — | — |

### 13 · Webhooks — 2

| Subpasta | Requisição | Controller.Action | Auth | Classe | Corpo/Query | Captura |
| --- | --- | --- | --- | --- | --- | --- |
| Mercado Pago | `POST /api/webhooks/mercadopago` | Webhooks.MercadoPago | No Auth | webhook | B58 | — |
| Asaas | `POST /api/webhooks/asaas` | Webhooks.Asaas | No Auth | webhook | B59 + header `asaas-access-token` | — |

### 14 · Utilidades — 1

| Subpasta | Requisição | Controller.Action | Auth | Classe | Corpo/Query | Captura |
| --- | --- | --- | --- | --- | --- | --- |
| Consulta de CEP | `GET /api/address-lookup/cep/{cep}` | AddressLookup.LookupByCep | No Auth | read | `{{cep}}` | — |

---

## 6. Anexo B — exemplos de corpo

Convenções: camelCase, enums numéricos, datas UTC, GUIDs sempre por variável. Campos opcionais aparecem com `null` quando o DTO aceita. `imageUrl` é obrigatório no validator de produto.

### B01 — `RegisterUserRequestDto` (`register/customer`, `register/seller`)

```json
{
  "fullName": "Cliente Teste Urbeat",
  "email": "{{customerEmail}}",
  "password": "{{customerPassword}}",
  "phoneNumber": "+5521999990001",
  "document": "{{customerDocument}}"
}
```

> Em `register/seller`, trocar para `"fullName": "Vendedor Teste Urbeat"`, `{{sellerEmail}}`, `{{sellerPassword}}` e `{{sellerDocument}}`; o endpoint define o papel.

### B02 — `LoginRequestDto` (5 logins + `token`)

```json
{
  "email": "{{customerEmail}}",
  "password": "{{customerPassword}}"
}
```

> Em `login/seller` usar `{{sellerEmail}}`/`{{sellerPassword}}`; em `login/admin`, `{{adminEmail}}`/`{{adminPassword}}`; em `login/driver`, `{{driverEmail}}`/`{{driverPassword}}`. `POST /api/auth/token` é o login de cliente.

### B03 — `ConfirmEmailRequestDto`

```json
{
  "userId": "{{customerUserId}}",
  "token": "{{emailConfirmationToken}}"
}
```

### B04 — `ResendEmailConfirmationRequestDto`

```json
{
  "email": "{{customerEmail}}"
}
```

### B05 — `ForgotPasswordRequestDto`

```json
{
  "email": "{{customerEmail}}"
}
```

### B06 — `ResetPasswordRequestDto`

```json
{
  "token": "{{passwordResetToken}}",
  "newPassword": "{{customerPassword}}",
  "confirmPassword": "{{customerPassword}}"
}
```

### B07 — `UpdateEmailRequestDto`

```json
{
  "userId": "{{customerUserId}}",
  "currentEmail": "{{customerEmail}}",
  "newEmail": "{{newCustomerEmail}}",
  "emailChangeChallenge": "{{emailChangeChallenge}}"
}
```

### B08 — `UpsertCustomerAddressRequestDto` (POST/PUT)

```json
{
  "cep": "{{cep}}",
  "number": "1000",
  "street": "Avenida Paulista",
  "neighborhood": "Bela Vista",
  "city": "São Paulo",
  "state": "SP",
  "complement": "Apto 10",
  "reference": "Próximo ao metrô",
  "isPrimary": true,
  "latitude": -23.561414,
  "longitude": -46.655881
}
```

### B09 — `UpdateCustomerProfileRequestDto`

```json
{
  "fullName": "Cliente Teste Urbeat",
  "email": "{{customerEmail}}",
  "phoneNumber": "+5521999990001"
}
```

### B10 — `StartCustomerVerificationRequestDto` (start e customer-session)

```json
{
  "storeId": "{{storeId}}",
  "customer": {
    "fullName": "Cliente Teste Urbeat",
    "email": "{{customerEmail}}",
    "phoneNumber": "+5521999990001"
  },
  "address": {
    "cep": "01310100",
    "street": "Avenida Paulista",
    "number": "1000",
    "complement": "Apto 10",
    "neighborhood": "Bela Vista",
    "city": "São Paulo",
    "state": "SP"
  }
}
```

### B11 — `ConfirmCustomerVerificationRequestDto`

```json
{
  "verificationId": "{{verificationId}}",
  "code": "{{verificationCode}}"
}
```

### B12 — `ResendCustomerVerificationRequestDto`

```json
{
  "verificationId": "{{verificationId}}"
}
```

### B13a — `CheckoutRequestDto` (preview)

```json
{
  "storeId": "{{storeId}}",
  "fulfillmentType": 1,
  "customerAddressId": "{{addressId}}",
  "paymentMethod": null,
  "notes": "Prévia de teste",
  "items": [
    {
      "productId": "{{productId}}",
      "quantity": 1,
      "notes": "Sem cebola",
      "variationId": null,
      "choiceOptionId": null,
      "weightGrams": null,
      "additionalIds": [],
      "optionGroups": []
    }
  ]
}
```

### B13b — `CheckoutRequestDto` (confirm e `POST /api/orders`)

```json
{
  "storeId": "{{storeId}}",
  "fulfillmentType": 1,
  "customerAddressId": "{{addressId}}",
  "paymentMethod": 1,
  "notes": "Pedido de teste",
  "items": [
    {
      "productId": "{{productId}}",
      "quantity": 1,
      "notes": "Sem cebola",
      "variationId": null,
      "choiceOptionId": null,
      "weightGrams": null,
      "additionalIds": [],
      "optionGroups": []
    }
  ]
}
```

### B14 — `CreateStoreRequestDto`

```json
{
  "name": "Loja Teste Urbeat",
  "slug": "{{storeSlug}}",
  "phoneNumber": "+5521999990002",
  "document": "{{storeDocument}}",
  "pixKey": "{{storePixKey}}",
  "websiteUrl": "https://www.urbeat.com.br",
  "cuisineType": "Hamburgueria",
  "bannerUrl": "{{uploadedImageUrl}}",
  "logoUrl": "{{uploadedImageUrl}}",
  "supportsDelivery": true,
  "supportsPickup": true,
  "initialMinute": 30,
  "finalMinute": 60,
  "maxDeliveryRadiusKm": 5
}
```

### B15 — `UpdateStoreRequestDto` (mesmas chaves de B14)

```json
{
  "name": "Loja Teste Urbeat",
  "slug": "{{storeSlug}}",
  "phoneNumber": "+5521999990002",
  "document": "{{storeDocument}}",
  "pixKey": "{{storePixKey}}",
  "websiteUrl": "https://www.urbeat.com.br",
  "cuisineType": "Hamburgueria",
  "bannerUrl": "{{uploadedImageUrl}}",
  "logoUrl": "{{uploadedImageUrl}}",
  "supportsDelivery": true,
  "supportsPickup": true,
  "initialMinute": 30,
  "finalMinute": 60,
  "maxDeliveryRadiusKm": 5
}
```

### B16 — `UpdateStoreAddressRequestDto`

```json
{
  "street": "Avenida Paulista",
  "number": "1000",
  "neighborhood": "Bela Vista",
  "city": "São Paulo",
  "state": "SP",
  "zipCode": "01310100",
  "complement": "Loja 1",
  "reference": "Próximo ao metrô",
  "latitude": -23.561414,
  "longitude": -46.655881
}
```

### B17 — `UpsertStoreBusinessHoursRequestDto`

```json
{
  "items": [
    {
      "dayOfWeek": 1,
      "isOpen": true,
      "shifts": [
        { "startTime": "11:00:00", "endTime": "15:00:00" },
        { "startTime": "18:00:00", "endTime": "23:00:00" }
      ]
    }
  ]
}
```

### B18 — `UpdateStoreStatusRequestDto`

```json
{
  "isOpen": true
}
```

### B19 — `UpdateStoreDeliveryConfigRequestDto`

```json
{
  "deliveryFee": 7.90,
  "minimumOrderValue": 20.00,
  "freeShippingThreshold": 80.00,
  "freeShippingToday": false,
  "maxDeliveryRadiusKm": 6.5,
  "deliveryAreas": [
    {
      "neighborhood": "Bela Vista",
      "deliveryFee": 7.90,
      "isActive": true,
      "notes": "Área de teste"
    }
  ]
}
```

> `id` de `StoreDeliveryAreaDto` é omitido de propósito: `Guid.Empty` significa "nova área" e não é permitido fixar GUID. Só enviar `id` ao editar uma área existente.

### B20 — `CreateCuisineTypeRequestDto`

```json
{
  "name": "Comida Japonesa"
}
```

### B21 — `CreateDeliveryTimeRequestDto`

```json
{
  "storeId": "{{storeId}}",
  "minTimeMinutes": 30,
  "maxTimeMinutes": 60
}
```

### B22 — `CreateDeliveryNeighborhoodRequestDto` (loja e admin)

```json
{
  "neighborhood": "Bela Vista",
  "city": "São Paulo"
}
```

### B23 — `ImportNeighborhoodsByCityRequestDto`

```json
{
  "city": "Rio de Janeiro",
  "uf": "RJ",
  "storeId": "{{storeId}}"
}
```

### B24 — `StoreAdditionalRequestDto`

```json
{
  "name": "Bacon extra",
  "description": "Porção adicional",
  "groupId": "{{additionalGroupId}}",
  "price": 4.50,
  "isActive": true,
  "displayOrder": 1
}
```

### B25 — `UpdateStoreAdditionalStatusRequestDto`

```json
{
  "isActive": false
}
```

### B26 — `CreateProductCategoryRequestDto` / `UpdateProductCategoryRequestDto`

```json
{
  "name": "Destaques",
  "description": "Categoria de teste",
  "displayOrder": 1,
  "isActive": true,
  "isFeatured": true
}
```

### B27 — `ReorderStoreCategoriesRequestDto` (array puro)

```json
[
  { "id": "{{categoryId}}", "displayOrder": 1 }
]
```

### B28 — `CreateProductRequestDto` / `UpdateProductRequestDto`

```json
{
  "categoryId": "{{categoryId}}",
  "name": "Produto Teste Urbeat",
  "description": "Produto criado pela coleção de homologação.",
  "price": 29.90,
  "promotionalPrice": null,
  "imageUrl": "{{uploadedImageUrl}}",
  "isAvailable": true,
  "isFeatured": false,
  "displayOrder": 1,
  "stockEnabled": false,
  "stockQuantity": 0,
  "isBestSeller": false,
  "isNew": true,
  "tagPriority": "",
  "saleMode": "single",
  "weightConfig": null,
  "additionals": [],
  "additionalIds": [],
  "choiceOptions": [],
  "variations": [],
  "optionGroups": []
}
```

### B29 — `UpdateProductAvailabilityRequestDto`

```json
{
  "isAvailable": false
}
```

### B30 — `BatchUpsertProductsRequestDto`

```json
{
  "items": [
    {
      "id": null,
      "categoryId": "{{categoryId}}",
      "name": "Produto em lote",
      "description": "Item de teste em lote",
      "price": 19.90,
      "imageUrl": "{{uploadedImageUrl}}",
      "isAvailable": true,
      "displayOrder": 2
    }
  ]
}
```

### B31 — `UpsertPaymentGatewayConfigRequestDto`

```json
{
  "gateway": 1,
  "accessToken": "{{paymentGatewayAccessToken}}",
  "notificationUrl": null,
  "environment": "Sandbox",
  "isActive": true
}
```

### B32 — `ContractSellerSubscriptionRequestDto`

```json
{
  "storeId": "{{storeId}}",
  "planId": "{{planId}}",
  "firstDueDateUtc": "2026-11-10T00:00:00Z"
}
```

### B33 — `CreatePlanRequestDto`

```json
{
  "name": "Plano Teste Urbeat",
  "amount": 99.90,
  "description": "Plano criado em ambiente local.",
  "isActive": true
}
```

### B34 — `UpdatePlanRequestDto`

```json
{
  "name": "Plano Teste Urbeat",
  "amount": 99.90,
  "description": "Plano atualizado em ambiente local."
}
```

### B35 — `UpdatePlanStatusRequestDto`

```json
{
  "isActive": false
}
```

### B36 — `UpsertSellerSubscriptionStatusRequestDto`

```json
{
  "sellerUserId": "{{sellerUserId}}",
  "nextDueDateUtc": "2026-11-10T00:00:00Z",
  "billingStatus": 1
}
```

### B37 — `UpdateSystemParameterRequest`

```json
{
  "value": "true"
}
```

### B38 — `LandingPageContentRequestDto`

```json
{
  "section": "hero",
  "key": "titulo",
  "value": "Conteúdo de teste",
  "displayOrder": 1,
  "isActive": true,
  "description": "Conteúdo criado pela coleção local."
}
```

### B39 — `StorePrinterConfigRequestDto`

```json
{
  "printerPresetId": "{{printerPresetId}}",
  "printerName": "Impressora Teste",
  "macAddress": "00:11:22:33:44:55",
  "copies": 1,
  "autoPrint": false,
  "autoCut": true,
  "printKitchenCopy": true,
  "printCounterCopy": true,
  "printCustomerReceipt": false,
  "printLogo": false,
  "highlightOrderNumber": true,
  "footerText": "Teste Urbeat"
}
```

### B40 — `WifiPrintRequest`

```json
{
  "ipAddress": "192.168.0.10",
  "port": 9100,
  "base64Data": "{{printerBase64Data}}"
}
```

### B41 — `RecordPrintJobResultRequestDto`

```json
{
  "success": true,
  "ambiguous": false,
  "operationKey": "{{$guid}}",
  "message": "Impressão confirmada no teste local."
}
```

### B42 — `UpsertStoreDeliveryDriverRequestDto` (`POST /api/delivery-drivers`, `PUT /{storeDriverId}`)

```json
{
  "name": "Entregador Teste",
  "phoneNumber": "+5521999990003",
  "whatsAppPhoneNumber": "+5521999990003",
  "isActive": true
}
```

### B43 — `CreateStoreDeliveryDriverRequestDto` (`POST /api/delivery-drivers/store`)

```json
{
  "name": "Entregador Teste",
  "fullName": null,
  "email": null,
  "phoneNumber": "+5521999990003",
  "whatsAppPhoneNumber": "+5521999990003",
  "isActive": true
}
```

> Com `email` preenchido, o backend encaminha para o fluxo de convite. O exemplo mantém `null` para criar o vínculo simples.

### B44 — `SetStoreDeliveryDriverStatusRequestDto`

```json
{
  "isActive": false
}
```

### B45 — `InviteDeliveryDriverRequestDto`

```json
{
  "fullName": "Entregador Convidado",
  "email": "{{driverEmail}}",
  "phoneNumber": "+5521999990004"
}
```

### B46 — `AcceptDeliveryDriverInviteRequestDto`

```json
{
  "password": "{{driverPassword}}",
  "fullName": "Entregador Convidado"
}
```

### B47 — `CreateDeliveryRouteRequestDto`

```json
{
  "deliveryDriverId": "{{storeDriverId}}",
  "orderIds": ["{{orderId}}"]
}
```

### B48 — `UpdateDeliveryRouteRequestDto`

```json
{
  "deliveryDriverId": "{{storeDriverId}}",
  "orderIds": ["{{orderId}}"]
}
```

### B49 — `SendDeliveryRoutesRequestDto`

```json
{
  "orderIds": ["{{orderId}}"],
  "physicalConfirmation": true
}
```

### B50 — `ConfirmDeliveryPinRequestDto` (3 endpoints)

```json
{
  "pin": "{{deliveryPin}}"
}
```

### B51 — `AssignDeliveryDriverRequestDto`

```json
{
  "deliveryDriverId": "{{storeDriverId}}"
}
```

### B52 — `UpdateOrderStatusRequestDto`

```json
{
  "newStatus": 4,
  "notes": "Pedido em preparo",
  "deliveryDriverId": null
}
```

### B53 — `CreateReviewRequestDto`

```json
{
  "rating": 5,
  "comment": "Pedido de teste aprovado."
}
```

### B54 — `UpdateSellerProfileRequestDto`

```json
{
  "fullName": "Vendedor Teste Urbeat"
}
```

### B55 — `UpdateStoreCustomerRequestDto`

```json
{
  "name": "Cliente Teste Urbeat",
  "email": "{{customerEmail}}",
  "phone": "+5521999990001",
  "cep": "{{cep}}",
  "street": "Avenida Paulista",
  "number": "1000",
  "complement": "Apto 10",
  "neighborhood": "Bela Vista",
  "city": "São Paulo",
  "state": "SP"
}
```

### B56 — `UpdateStoreCustomerStatusRequestDto`

```json
{
  "isActive": false
}
```

### B57 — `CreateOrderPaymentRequestDto`

```json
{
  "orderId": "{{orderId}}"
}
```

### B58 — Webhook Mercado Pago

```json
{
  "id": 123456789,
  "live_mode": false,
  "type": "payment",
  "action": "payment.updated",
  "data": {
    "id": "{{gatewayTransactionId}}"
  }
}
```

### B59 — Webhook Asaas (header `asaas-access-token: {{asaasWebhookToken}}`)

```json
{
  "id": "evt_000000000000000000000000",
  "event": "PAYMENT_CONFIRMED",
  "payment": {
    "id": "pay_000000000000000000000000",
    "status": "CONFIRMED",
    "value": 49.90,
    "dueDate": "2026-11-10",
    "paymentDate": "2026-11-10",
    "externalReference": "{{sellerUserId}}"
  }
}
```

---

## 7. Anexo C — comandos e resultados esperados

Executar da raiz do repositório.

```powershell
# 1. Conferência da fonte (informativa)
(Get-ChildItem backend/src/Urbeat.WebApi/Controllers -Filter *Controller.cs).Count   # 33
$t = 0; Get-ChildItem backend/src/Urbeat.WebApi/Controllers -Filter *Controller.cs |
  ForEach-Object { $t += (Select-String -Path $_.FullName -Pattern '^\s*\[Http(Get|Post|Put|Patch|Delete)').Count }; $t  # 179

# 2. Cobertura
powershell -NoProfile -File scripts/postman/Test-PostmanCoverage.ps1 `
  -CollectionPath postman/urbeat.postman_collection.json `
  -FeatureMapPath postman/feature-map.json `
  -ControllersPath backend/src/Urbeat.WebApi/Controllers

# 3. Estrutura, segredos e guard
powershell -NoProfile -File scripts/postman/Test-PostmanCollection.ps1 `
  -CollectionPath postman/urbeat.postman_collection.json `
  -LocalEnvironment postman/urbeat.local.postman_environment.json `
  -OciEnvironment postman/urbeat.oci.postman_environment.json `
  -VerifyGuard

# 4. JSON válido
Get-Content postman/urbeat.postman_collection.json -Raw | ConvertFrom-Json | Out-Null
Get-Content postman/urbeat.local.postman_environment.json -Raw | ConvertFrom-Json | Out-Null
Get-Content postman/urbeat.oci.postman_environment.json -Raw | ConvertFrom-Json | Out-Null

# 5. Contagem por feature
$c = Get-Content postman/urbeat.postman_collection.json -Raw | ConvertFrom-Json
$c.item | ForEach-Object {
  $n = ($_.item | ForEach-Object { $_.item.Count } | Measure-Object -Sum).Sum
  "{0}: {1} requisições em {2} subpastas" -f $_.name, $n, $_.item.Count
}
```

Saída esperada do item 5: 16, 9, 10, 25, 6, 8, 19, 22, 6, 3, 27, 25, 2, 1 (total 179; 44 subpastas).

Smoke público (somente com API local; nunca OCI):

```powershell
npx --yes newman run postman/urbeat.postman_collection.json `
  -e postman/urbeat.local.postman_environment.json `
  --folder "03 · Descoberta e vitrine" --reporters cli

npx --yes newman run postman/urbeat.postman_collection.json `
  -e postman/urbeat.local.postman_environment.json `
  --folder "14 · Utilidades" --reporters cli
```

Pré-requisitos do fluxo manual (não executar na implementação): `docker compose up --build`, banco local descartável, contas confirmadas, SMTP/log disponível. Ao final, `docker compose down -v` para recomeçar do zero.

---

## 8. Anexo D — checklist de segurança e mitigação de side effects

**Segurança**
- [ ] Nenhum valor secreto nos 3 JSONs, README ou scripts.
- [ ] Ambientes OCI/Local com todas as credenciais/IDs vazios.
- [ ] `runMode=read-only`, `allowWebhooks=false` nos dois ambientes.
- [ ] Nenhum `Authorization` manual; bearer só via `request.auth`.
- [ ] Nenhum `pm.sendRequest`; nenhum token impresso em `console.log`.
- [ ] Tokens de link público tratados como bearer e limpos ao final.
- [ ] Nenhuma leitura autenticada na OCI; nenhuma escrita/webhook na OCI.
- [ ] Nenhum CPF/CNPJ/e-mail real; documentos sintéticos preenchidos pelo testador.
- [ ] Nenhum ID de produção em exemplos/capturas.
- [ ] `postman-sync` proibido de commitar e de tocar código de aplicação.

**Side effects**
- [ ] Tasks 1–18 não fazem chamadas HTTP, exceto o smoke local opcional da Task 5 (somente GET anônimo em `03`); nenhuma chamada é feita à OCI.
- [ ] Nenhuma execução de `destructive`/webhook durante a implementação.
- [ ] Newman só em pastas públicas e com API local.
- [ ] `docker compose down -v` é decisão do testador; o plano não apaga volumes.
- [ ] Arquivos temporários em `$env:TEMP` são removidos.
- [ ] Nenhum commit; diff íntegro para revisão.
- [ ] Nenhuma chamada SSH/SCP/OCI ou execução de `scripts/criarDeployOracleCloud/*`.

---

## 9. Anexo E — aceite manual (Postman desktop)

- [ ] Importar os 3 arquivos sem erro de schema.
- [ ] 14 features, 44 subpastas e 179 requisições visíveis.
- [ ] Nenhuma pasta/subpasta com nome de controller.
- [ ] `GET /api/public/stores` responde sem credencial.
- [ ] Multipart mostra `file` vazio e o fixture na descrição.
- [ ] Runner em `read-only` não envia escritas/destrutivas/webhooks (401 em GETs autenticados é esperado).
- [ ] `write` libera apenas writes; `destructive` libera destructive; webhooks só com `allowWebhooks=true`.
- [ ] Webhooks e OCI não foram executados.
- [ ] Variáveis/histórico limpos ao final.
- [ ] Nenhum response de exemplo com dados pessoais foi salvo.
