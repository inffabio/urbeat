# Coleção Postman do Urbeat

**Data:** 2026-10-28
**Status:** especificação aprovada para implementação
**Organização:** 14 features funcionais e 44 subpastas de fluxo; controllers são apenas a origem técnica da extração.
**Escopo desta entrega:** somente este documento. A coleção, os ambientes, os scripts de verificação e o agente OpenCode descritos aqui **não** foram implementados nesta tarefa e nada foi commitado.

## 1. Objetivo

Definir uma coleção Postman importável que:

1. cubra **todos os endpoints REST e webhooks** expostos pelos controllers do `Urbeat.WebApi`;
2. seja organizada por features funcionais e fluxos de negócio, com nomes e URLs estáveis;
3. use ambientes **Local** (padrão) e **OCI** (configurável), sem credenciais versionadas;
4. capture tokens e identificadores apenas quando o contrato devolver esses valores;
5. traga exemplos de payload derivados dos DTOs reais e fixtures de imagem existentes no repositório;
6. impeça, por padrão, a execução automática de operações de escrita, destrutivas e de webhooks;
7. inclua verificação repetível de cobertura e de importabilidade;
8. tenha um agente OpenCode que mantenha a coleção sincronizada quando os contratos mudarem.

O resultado esperado é uma ferramenta de exploração e homologação manual, não um cliente de produção nem uma suíte de carga.

## 2. Fora de escopo

- Implementar a coleção, os ambientes, os scripts ou o agente nesta tarefa.
- Alterar backend, frontend, Docker, scripts de deploy ou `.opencode/opencode.json`.
- SignalR (`/hubs/seller-notifications`, `/hubs/customer-notifications`), `/health`, `/swagger`, `/hangfire` e `/metrics`: não são controllers REST e não entram nas 179 ações.
- Automação de deploy, testes de carga, testes E2E de navegador ou testes de segurança ofensiva.
- Criar, obter ou versionar credenciais, tokens, documentos pessoais ou dados de produção.
- Executar qualquer chamada contra a OCI durante a implementação ou validação.

## 3. Fontes de verdade e método

Precedência adotada:

1. **Código dos controllers e DTOs** é a fonte autoritativa de rotas, verbos, corpos, autorização e códigos de resposta.
2. **Configuração executável** (`Program.cs`, `appsettings*.json`, `nginx.conf`, `DEPLOY.md`) define URLs, cookies, limites e exposição por ambiente.
3. **Swagger** (`/swagger/v1/swagger.json`) é apenas conferência secundária de paths e schemas. Ele **não** possui definição de segurança (não há `Bearer`/`Authorize`), não traz exemplos e não deve ser usado para gerar autenticação.
4. **Frontend** foi consultado apenas para confirmar que as URLs de API são relativas à origem; não é fonte de contrato.

Levantamento executado para esta especificação:

- `backend/src/Urbeat.WebApi/Controllers/*.cs`: 33 controllers.
- Extração de `[Route]`, `[HttpX]`, `[Authorize]`, `[AllowAnonymous]`, `[FromBody]`, `IFormFile`.
- Contagem: **179 ações únicas** (`MÉTODO + rota`), sem duplicidade.
- Matriz de autorização: 42 anônimas, 85 `SellerOnly`, 18 `CustomerOnly`, 23 `AdminOnly`, 5 `Role:Seller`, 5 `DeliveryDriverOnly` e 1 `[Authorize]` genérica.
- Contratos auxiliares lidos: `AuthController`, `CheckoutController`, `OrdersController`, `PaymentsController`, `StoresController`, `StoreProductsController`, `PublicStoresController`, `DeliveryDriversController`, `DeliveryRoutesController`, `WebhooksController`, `AdminController`, `LandingPageContentController`, `ReviewsController`.
- DTOs e validadores correspondentes; enums de domínio; `StoreMediaUploadValidator`; adaptadores Mercado Pago/Asaas; seeders; `.opencode/agents/`.
- `DEPLOY.md` para a URL pública da API e `docker-compose.yml` para a URL local.

Regra de ouro: **não inventar rota, campo, enum, header ou credencial**. Quando o código não permitir determinar um valor, a coleção usa variável vazia e o `README` explica como preenchê-la.

## 4. Inventário e organização por features

Controllers são a **fonte de extração** do contrato, não o critério de organização da coleção. Cada requisição pertence a exatamente uma feature funcional e, dentro dela, a uma subpasta de fluxo de negócio.

### 4.1 Números de referência

| Métrica | Valor |
| --- | ---: |
| Controllers de origem | 33 |
| Ações REST | 179 |
| Método + rota duplicados | 0 |
| Features funcionais | 14 |
| Subpastas de fluxo | 44 |
| Ações anônimas | 42 |
| Ações `SellerOnly` | 85 |
| Ações `CustomerOnly` | 18 |
| Ações `AdminOnly` | 23 |
| Ações `Role:Seller` | 5 |
| Ações `DeliveryDriverOnly` | 5 |
| Ações `[Authorize]` genérica | 1 |
| Endpoints multipart | 2 |
| Webhooks | 2 (feature `13`) |

O Anexo A lista as 179 ações por feature e subpasta.

### 4.2 Features funcionais

A coleção terá exatamente estas 14 features de primeiro nível:

| Feature | Escopo funcional | Subpastas de fluxo | Ações |
| --- | --- | --- | ---: |
| `01 · Acesso e identidade` | Cadastro, login, sessão, confirmação e recuperação de acesso | `Cadastro`, `Login`, `Sessão`, `Confirmação e recuperação` | 16 |
| `02 · Conta do cliente` | Perfil, endereços e notificações do cliente | `Perfil`, `Endereços`, `Notificações` | 9 |
| `03 · Descoberta e vitrine` | Lojas públicas, cardápio público, tipos de culinária e avaliações públicas | `Lojas`, `Cardápio`, `Tipos de culinária`, `Avaliações` | 10 |
| `04 · Loja e operação` | Onboarding, perfil, horários, cobertura de entrega e tipos de culinária da loja | `Onboarding e perfil`, `Horários e abertura`, `Cobertura de entrega`, `Tipos de culinária` | 25 |
| `05 · Publicação e assinatura` | Publicação da loja e ciclo da assinatura | `Publicação`, `Assinatura` | 6 |
| `06 · Impressão e notificações` | Configuração de impressão, fila de impressão e notificações do vendedor | `Configuração de impressão`, `Fila de impressão`, `Notificações do vendedor` | 8 |
| `07 · Cardápio e oferta` | Categorias, produtos, adicionais e grupos de opções | `Categorias`, `Produtos`, `Adicionais e grupos` | 19 |
| `08 · Checkout e pedidos` | Checkout, pedidos do cliente, pedidos da loja e clientes da loja | `Checkout`, `Pedidos do cliente`, `Pedidos da loja`, `Clientes da loja` | 22 |
| `09 · Pagamentos` | Gateway da loja e pagamentos do pedido | `Gateway da loja`, `Pagamentos do pedido` | 6 |
| `10 · Avaliações` | Avaliações do pedido e avaliações recebidas pela loja | `Avaliações do pedido`, `Avaliações recebidas` | 3 |
| `11 · Entregas` | Entregadores, rotas, app do entregador e links públicos | `Entregadores`, `Rotas de entrega`, `App do entregador`, `Links públicos de entrega` | 27 |
| `12 · Administração` | Planos, cobranças, parâmetros, manutenção, landing page e outbox | `Painel e planos`, `Assinaturas e cobranças`, `Parâmetros do sistema`, `Manutenção e importações`, `Conteúdo da landing page`, `Outbox` | 25 |
| `13 · Webhooks` | Callbacks de provedores de pagamento/assinatura | `Mercado Pago`, `Asaas` | 2 |
| `14 · Utilidades` | Consultas auxiliares sem domínio de negócio | `Consulta de CEP` | 1 |
| **Total** | **14 features** | **44 subpastas** | **179** |

Regras de composição:

- As subpastas são **fluxos de negócio**, nunca nomes de controllers, camadas ou projetos.
- `POST /api/orders` fica em `08 · Checkout e pedidos / Checkout`; não é duplicado em Pagamentos.
- Os webhooks ficam exclusivamente em `13 · Webhooks`, mesmo gerando efeitos em pagamentos, pedidos ou assinaturas.
- Consultas públicas usadas pela vitrine ficam em `03`, mesmo que o código-fonte esteja em um controller de loja (`GET /api/stores/cuisine-types`).
- Ações administrativas ficam em `12`, ainda que operem recursos de loja, pedido ou assinatura.
- Atribuir/remover entregador e gerar link individual são transições do pedido e ficam em `08 / Pedidos da loja`; criar rota, operar PIN e consumir link público ficam em `11`.

A reorganização por feature **não altera** a autenticação de cada ação, o catálogo de variáveis, a política de segredos, a classificação `read`/`write`/`destructive`/`webhook` nem os guardas; muda apenas o agrupamento e a ordem de apresentação.

### 4.3 Critérios para classificar e manter por feature

1. **Uma ação, uma feature, uma subpasta.** O mapeamento é uma bijeção entre as 179 ações e `feature-map.json`; não há duplicação para "facilitar" outro fluxo.
2. **A feature é a capacidade de negócio**, não o controller, o verbo HTTP, a camada ou o prefixo da URL. O critério é: qual resultado de negócio o endpoint entrega e qual estado ele governa?
3. **O dono do estado prevalece.** Se um endpoint cria pedido ou muda seu ciclo (inclusive confirmação de recebimento pelo cliente e conclusão pela loja), pertence a Checkout e pedidos; se opera entrega/rota/PIN do entregador, pertence a Entregas; se move dinheiro, pertence a Pagamentos; se processa callback de provedor, pertence a Webhooks.
4. **O consumidor dominante prevalece.** Um endpoint usado principalmente pelo cliente fica na feature do cliente; um endpoint operado pela loja fica na feature da loja, mesmo que compartilhe DTO ou serviço.
5. **Fluxos atravessam features sem duplicar requisições.** A requisição fica na feature dona do estado; as demais features documentam a dependência em descrição/README e referenciam o nome estável. Exemplo: criar pedido em `08` e depois pagar em `09`; criar rota em `11` e atualizar status do pedido em `08`.
6. **Webhooks sempre em `13`.** Nenhum webhook é movido para Pagamentos, Pedidos ou Assinatura.
7. **Administração em `12`.** Ações `AdminOnly` de plataforma ficam em Administração, salvo decisão explícita documentada; endpoints públicos da landing page também ficam em `12 / Conteúdo da landing page`.
8. **Crescimento não volta a virar controller.** Se uma feature passar de ~30 ações, criar/renomear subpasta por fluxo de negócio; não criar subpasta com nome de controller.
9. **IDs e nomes são estáveis.** As features `01`–`14` não são renumeradas. Uma feature nova recebe o próximo número e exige atualização explícita do mapa, dos critérios e da documentação.
10. **Ambiguidade não é resolvida por palpite.** O agente deve parar e reportar se um endpoint puder pertencer a duas features com justificativa equivalente.

Árvore de decisão para endpoint novo:

1. É webhook/callback de provedor? → `13`.
2. É autenticação, sessão ou recuperação de acesso? → `01`.
3. É autoatendimento do cliente (perfil, endereço, notificação)? → `02`.
4. É descoberta/vitrine pública? → `03`.
5. É configuração/operação da loja (onboarding, horário, cobertura, culinária)? → `04`.
6. É publicação ou assinatura? → `05`.
7. É impressão ou notificação do vendedor? → `06`.
8. É cardápio/oferta da loja? → `07`.
9. É checkout, ciclo do pedido ou clientes da loja? → `08`.
10. É pagamento ou gateway? → `09`.
11. É avaliação? → `10`.
12. É operação de entrega (entregador, rota, PIN) ou link público de entrega? → `11`. Se for apenas uma transição do pedido, permanece em `08`.
13. É administração/manutenção da plataforma? → `12`.
14. É utilidade sem domínio de negócio? → `14`.
15. Nenhuma das anteriores: não classificar automaticamente; reportar.

### 4.4 Regras de mapeamento rota → Postman

1. O nome da requisição é exatamente `MÉTODO + espaço + rota de contrato`, sem a constraint de rota:
   - `GET /api/stores/{storeId}/products`
   - `POST /api/orders/{orderId}/review`
   - `GET /api/LandingPageContent/section/{section}`
2. A URL usa `{{baseUrl}}` + a rota, substituindo cada parâmetro por variável de ambiente:
   - `{storeId:guid}` → `{{storeId}}`
   - `{sequence:int}` → `{{stopSequence}}`
   - `{token}` → variável contextual (`{{deliveryRouteToken}}`, `{{individualDeliveryToken}}` ou `{{driverInviteToken}}`)
   - `{key}` → `{{systemParameterKey}}`
   - demais parâmetros → `{{nomeDoParametro}}` em camelCase.
3. Constraints (`:guid`, `:int`) não aparecem na URL do Postman; são validações do servidor.
4. A rota `api/LandingPageContent` preserva a caixa do código-fonte. O roteamento ASP.NET é case-insensitive, mas o nome e o path da coleção não serão normalizados para minúsculas.
5. Rotas definidas com barra inicial em actions (`api/orders/...`, `api/public/...`) são gravadas sem barra duplicada.
6. Query strings opcionais entram como parâmetros **desabilitados** por padrão, com descrição e exemplo. Path params são sempre habilitados.
7. Nenhuma URL contém host fixo; apenas `{{baseUrl}}/api/...`.
8. Parâmetros ambíguos usam nomes contextuais:

| Parâmetro do contrato | Contexto | Variável Postman |
| --- | --- | --- |
| `{id:guid}` | `LandingPageContent` | `{{landingContentId}}` |
| `{id}` | `OutboxOperations` | `{{outboxEventId}}` |
| `{notificationId}` | `CustomerNotifications` | `{{customerNotificationId}}` |
| `{notificationId}` | `SellerNotifications` | `{{sellerNotificationId}}` |
| `{token}` | `DeliveryDrivers/invites` | `{{driverInviteToken}}` |
| `{token}` | `PublicDeliveryRoutes` | `{{deliveryRouteToken}}` |
| `{token}` | `PublicIndividualDeliveries` | `{{individualDeliveryToken}}` |
| `{sequence:int}` | `PublicDeliveryRoutes` | `{{stopSequence}}` |
| `{key}` | `Admin/system-parameters` | `{{systemParameterKey}}` |
| `{code}` | `Auth/email/confirm` | `{{emailConfirmationCode}}` |
| `{slug}` | `PublicStores/by-slug` | `{{storeSlug}}` |
| `{storePath}` | `PublicStores/by-path` | `{{storePath}}` |
| `{cep}` | `AddressLookup` | `{{cep}}` |
| `{section}` | `LandingPageContent` | `{{landingSection}}` |
| `{cityId:guid}` | `Neighborhoods` | `{{cityId}}` |
| `{cuisineTypeId:guid}` | `Stores/cuisine-types` | `{{cuisineTypeId}}` |

## 5. Arquivos que a implementação deverá criar

| Caminho | Finalidade |
| --- | --- |
| `postman/urbeat.postman_collection.json` | Coleção v2.1 com as 179 requisições, guardas e scripts de captura. |
| `postman/urbeat.local.postman_environment.json` | Ambiente padrão, sem credenciais. |
| `postman/urbeat.oci.postman_environment.json` | Ambiente OCI, somente URLs e flags seguras, sem credenciais. |
| `postman/feature-map.json` | Mapa autoritativo `endpoint → feature/subpasta`, com as 14 features e as 179 ações. |
| `postman/README.md` | Como importar, preparar contas locais, alternar `runMode`, lidar com arquivos, riscos da OCI e comandos de verificação. |
| `scripts/postman/Test-PostmanCoverage.ps1` | Compara controllers, `feature-map.json` e coleção; falha em divergência. |
| `scripts/postman/Test-PostmanCollection.ps1` | Valida estrutura v2.1, ausência de segredos, guardas, multipart e ambientes. |
| `.opencode/agents/postman-sync.md` | Agente de manutenção descrito na seção 15. |

Nenhum arquivo fora desses caminhos deve ser alterado pela implementação, exceto a permissão de delegação mencionada na seção 15.4.

## 6. Estrutura da coleção

### 6.1 Identidade e ordenação

- `info.name`: `urbeat · API REST`
- `info.schema`: `https://schema.getpostman.com/json/collection/v2.1.0/collection.json`
- `info._postman_id`: UUID fixo, gerado uma vez.
- IDs de requisição são UUIDs estáveis. A manutenção **nunca** regenera a coleção inteira: preserva IDs existentes e cria IDs apenas para requisições novas.
- Pastas usam os prefixos numéricos da seção 4.2 e representam features funcionais.
- Subpastas representam fluxos de negócio dentro da feature; a ordem segue o fluxo (por exemplo, onboarding antes de horários/cobertura), não a ordem de declaração no controller.
- Cada requisição pertence a exatamente uma feature/subpasta, conforme `postman/feature-map.json`. Não há requisição duplicada para outro fluxo.
- Endpoints novos são classificados pela árvore da seção 4.3 e acrescentados à subpasta de negócio correspondente; nunca são colocados em pasta com nome de controller.
- Renomear uma requisição só é permitido quando o contrato muda; o nome antigo deve ser reportado como removido.

O contrato mínimo de `postman/feature-map.json` é:

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
    "POST /api/auth/register/customer": { "feature": "01", "subfolder": "Cadastro" },
    "GET /api/stores/cuisine-types": { "feature": "03", "subfolder": "Tipos de culinária" }
  }
}
```

O arquivo real contém as 14 features e as 179 entradas de endpoint. `Test-PostmanCoverage.ps1` exige correspondência 1:1 entre a extração dos controllers, as entradas do mapa e as requisições da coleção.

### 6.2 Autorização

A autorização é declarada **em cada requisição**, não herdada de pasta. Isso evita que uma requisição pública dentro de uma feature majoritariamente autenticada herde um token por engano.

| Anotação no controller | Configuração Postman |
| --- | --- |
| `[AllowAnonymous]` ou controller sem `[Authorize]` | `No Auth` |
| `AdminOnly` | Bearer `{{adminToken}}` |
| `SellerOnly` ou `Roles = "Seller"` | Bearer `{{sellerToken}}` |
| `CustomerOnly` | Bearer `{{customerToken}}` |
| `DeliveryDriverOnly` | Bearer `{{driverToken}}` |
| `[Authorize]` genérica | Bearer `{{customerToken}}` (único caso: `GET /api/orders/{orderId}/review`) |

O header `Authorization` não é adicionado manualmente; usa-se o tipo `bearer` do Postman para evitar header duplicado.

### 6.3 Descrição obrigatória de cada requisição

Toda requisição terá uma descrição com este template:

```
Feature: <id> · <nome> / <subpasta>
Controller: <Nome>.<Action>  (rastreabilidade técnica, não organização)
Auth: <No Auth | adminToken | sellerToken | customerToken | driverToken>
Classificação: <read | write | destructive | webhook>
Pré-requisitos: <variáveis e estado necessários>
Efeitos: <o que cria/altera; "nenhum" para GET>
Captura: <variáveis gravadas pela resposta ou "nenhuma">
Manual: <sim para destrutivas/webhooks; omitido nas demais>
```

Isso permite auditoria sem abrir o código e serve de contrato para o agente.

### 6.4 Headers e corpos

- `Accept: application/json` em todas as requisições.
- Não declarar `Content-Type` manualmente para JSON ou multipart; o Postman define o valor e o boundary.
- Corpos JSON usam `body.mode = "raw"`, `options.raw.language = "json"`.
- Webhooks usam corpo JSON bruto; o backend lê o stream independentemente do modelo.
- Respostas de exemplo salvas são opcionais. Quando existirem, não podem conter tokens, cookies, dados pessoais, IDs de produção nem payload de OCI.

## 7. Ambientes e variáveis

### 7.1 `urbeat.local.postman_environment.json`

Valores padrão:

| Variável | Valor |
| --- | --- |
| `baseUrl` | `http://localhost:5000` |
| `webBaseUrl` | `http://localhost:4200` |
| `runMode` | `read-only` |
| `allowWebhooks` | `false` |

Todas as demais variáveis existem com `value` vazio, incluindo tokens, IDs, e-mails, senhas, documentos, CEP, bairro, PIN e tokens de webhook.

Variantes documentadas no `README`:

- API via `dotnet run` (perfil HTTP): `http://localhost:5281`.
- API via `dotnet run` (perfil HTTPS): `https://localhost:7274`; é a variante recomendada para testar `POST /api/auth/refresh`, porque o cookie é `Secure`.
- Frontend local: `http://localhost:4200`.

### 7.2 `urbeat.oci.postman_environment.json`

| Variável | Valor |
| --- | --- |
| `baseUrl` | `https://api.urbeat.com.br` |
| `webBaseUrl` | `https://urbeat.com.br` |
| `runMode` | `read-only` |
| `allowWebhooks` | `false` |

Todas as demais variáveis ficam vazias. O ambiente OCI **não** contém usuário, senha, token, cookie, CPF/CNPJ, ID de loja, ID de pedido nem token de webhook.

O ambiente OCI serve para conferir URLs, importação e leitura pública. Leituras autenticadas só podem ocorrer com conta de teste dedicada e aprovação explícita; ainda assim podem expor dados pessoais.

### 7.3 Catálogo de variáveis

Todas as variáveis são de ambiente (`environment`), não de coleção, para permitir valores diferentes por máquina e impedir que um valor local vaze para a OCI.

**Controle de execução**

| Variável | Uso | Tipo |
| --- | --- | --- |
| `runMode` | `read-only`, `write` ou `destructive` | padrão |
| `allowWebhooks` | `true` somente para teste local de webhook | padrão |

**URLs e dados públicos**

`baseUrl`, `webBaseUrl`, `webhookPublicUrl`, `cep`, `neighborhood`, `cuisineType`, `storePath`, `storeSlug`, `landingSection`, `cityId`, `printerPresetId`.

**Credenciais de teste (vazias; tipo `secret` quando suportado)**

`customerEmail`, `newCustomerEmail`, `customerPassword`, `sellerEmail`, `sellerPassword`, `adminEmail`, `adminPassword`, `driverEmail`, `driverPassword`.

**Tokens de acesso (vazios; tipo `secret`)**

`customerToken`, `sellerToken`, `adminToken`, `driverToken`.

**Identificadores e dados gerados (vazios)**

`storeId`, `categoryId`, `productId`, `productImageUrl`, `uploadedImageUrl`, `additionalId`, `additionalGroupId`, `addressId`, `orderId`, `orderCode`, `paymentId`, `gatewayTransactionId`, `storeDriverId`, `deliveryDriverId`, `routeId`, `deliveryRouteToken`, `individualDeliveryToken`, `driverInviteToken`, `customerUserId`, `sellerUserId`, `planId`, `systemParameterKey`, `landingContentId`, `outboxEventId`, `customerNotificationId`, `sellerNotificationId`, `verificationId`, `verificationCode`, `stopSequence`, `deliveryPin`, `emailConfirmationCode`, `passwordResetToken`, `emailChangeChallenge`, `cuisineTypeId`.

**Dados sensíveis de teste (vazios; tipo `secret`)**

`customerDocument`, `sellerDocument`, `storeDocument`, `storePixKey`, `asaasWebhookToken`, `paymentGatewayAccessToken`, `printerBase64Data`.

`printerBase64Data` não é segredo, mas fica vazio porque o conteúdo depende do modelo físico da impressora.

### 7.4 Política de segredos

1. Nenhum valor secreto é gravado em JSON versionado, exemplo, descrição, README ou script.
2. Senhas e tokens usam variáveis `secret` quando a versão do Postman suportar. Caso contrário, usar **Postman Vault** (`{{vault:chave}}`) ou preencher manualmente na sessão.
3. O `README` recomenda limpar os valores e reimportar o ambiente antes de exportar ou compartilhar. Exportar um ambiente preenchido grava os valores em texto claro.
4. Tokens JWT, cookies, `asaas-access-token` e access tokens de gateway nunca são impressos no Console do Postman nem em relatórios do Newman.
5. O ambiente OCI nunca recebe segredo de produção. Testes de webhook em OCI são proibidos.
6. Nenhum documento pessoal real é usado. `customerDocument`, `sellerDocument` e `storeDocument` devem ser preenchidos com CPF/CNPJ sintéticos válidos para teste, gerados pelo usuário; o exemplo não fixa um número.

### 7.5 Captura de valores

Scripts de teste só gravam valores quando a resposta é `2xx`, o corpo é JSON e o campo existe. Falha de captura não deve gerar exceção.

| Requisição bem-sucedida | Variável | Campo |
| --- | --- | --- |
| `POST /api/auth/login/customer`, `POST /api/auth/token` | `customerToken` | `accessToken` |
| `POST /api/auth/login/seller` | `sellerToken` | `accessToken` |
| `POST /api/auth/login/admin` | `adminToken` | `accessToken` |
| `POST /api/auth/login/driver` | `driverToken` | `accessToken` |
| `POST /api/auth/refresh` | token do último login | `accessToken` |
| `POST /api/checkout/customer-session` e `POST /api/checkout/customer-verification/confirm` | `customerToken` | `accessToken` |
| `POST /api/checkout/customer-session` e `POST /api/checkout/customer-verification/confirm` | `addressId` (se presente) | `customerAddressId` |
| `POST /api/auth/register/customer` | `customerUserId`, `emailChangeChallenge` (se presente) | `userId`, `emailChangeChallenge` |
| `POST /api/auth/register/seller` | `sellerUserId`, `emailChangeChallenge` (se presente) | `userId`, `emailChangeChallenge` |
| `POST /api/checkout/customer-verification/start` | `verificationId` | `verificationId` |
| `POST /api/stores` | `storeId`, `storeSlug` | `id`, `slug` |
| `GET /api/stores/my-store` | `storeId` (somente se vazio) | `id` |
| `POST /api/stores/upload-image` | `uploadedImageUrl` | `url` |
| `POST /api/stores/{storeId}/categories` | `categoryId` | `id` |
| `POST /api/stores/{storeId}/cuisine-types` | `cuisineTypeId` | `id` |
| `POST /api/stores/{storeId}/products` | `productId` | `id` |
| `POST /api/stores/{storeId}/products/{productId}/images` | `productImageUrl` | `imageUrl` |
| `POST /api/stores/{storeId}/additionals` | `additionalId` | `id` |
| `GET /api/stores/{storeId}/additionals/groups` | `additionalGroupId` (primeiro item, se vazio) | `[0].id` |
| `POST /api/customer/addresses` | `addressId` | `id` |
| `POST /api/checkout/confirm` e `POST /api/orders` | `orderId`, `orderCode` | `orderId`, `code` |
| `POST /api/payments/order` | `paymentId`, `gatewayTransactionId` | `paymentId`, `gatewayTransactionId` |
| `POST /api/delivery-drivers` | `storeDriverId`, `deliveryDriverId` | `id`, `deliveryDriverId` |
| `POST /api/delivery-drivers/store` | `storeDriverId`, `deliveryDriverId` | `id`, `deliveryDriverId` |
| `POST /api/delivery-routes/store` | `routeId`, `deliveryRouteToken` (se houver `shareUrl`) | `route.id`, último segmento de `shareUrl` |
| `POST /api/delivery-routes/store/{routeId}/share` | `deliveryRouteToken` | último segmento de `shareUrl` |
| `POST /api/delivery-routes/store/send` | `deliveryRouteToken` | último segmento de `groups[0].shareUrl` |
| `POST /api/orders/store/{orderId}/delivery-link` | `individualDeliveryToken` | último segmento de `shareUrl` |
| `POST /api/admin/plans` | `planId` | `id` |
| `GET /api/admin/system-parameters` | `systemParameterKey` (primeiro item, se vazio) | `[0].key` |
| `POST /api/LandingPageContent` | `landingContentId` | `id` |
| `GET /api/outbox/failed` | `outboxEventId` (primeiro item, se vazio) | `[0].id` |
| `GET /api/customer/notifications` | `customerNotificationId` (primeiro item, se vazio) | `[0].id` |
| `GET /api/seller/notifications` | `sellerNotificationId` (primeiro item, se vazio) | `[0].id` |

Não há captura de:

- token de convite de entregador (`/invites/{token}`), pois ele só é enviado por e-mail;
- código OTP, pois não é devolvido pela API;
- short code de confirmação de e-mail, pois só existe no e-mail/Redis;
- refresh token, pois é cookie `HttpOnly`;
- senha de conta criada por `customer-session`;
- IDs de recursos de produção a partir de listagens públicas.

## 8. Autenticação e sessão

- Esquema: JWT Bearer (`Authorization: Bearer <token>`), validado por issuer, audience, assinatura e lifetime; `ClockSkew` de 1 minuto.
- Papéis: `Admin`, `Seller`, `Customer`, `DeliveryDriver`. As policies estão em `AuthorizationPolicies`.
- `401` = token ausente/inválido/expirado. `403` = token válido com papel errado, e-mail não confirmado (código `EMAIL_NOT_CONFIRMED`) ou regra de negócio. `423` = conta bloqueada.
- O login devolve `{ accessToken, expiresAtUtc }`. O refresh token vai apenas no cookie `urbeat.refresh_token` (`HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/`).
- `POST /api/auth/refresh` depende exclusivamente desse cookie; não aceita refresh token no corpo.
- Como o cookie tem nome único e domínio único, login sucessivo em papéis diferentes sobrescreve o cookie de refresh. Os access tokens por papel continuam válidos até expirarem. Para renovar, faça login novamente no papel desejado.
- Em `http://localhost:5000` (Docker Compose), o cookie `Secure` pode não ser enviado pelo cookie jar. Para testar refresh, use a variante HTTPS `https://localhost:7274` ou refaça o login.
- O cookie não isola portas: `localhost:5000` e `localhost:5281` compartilham o mesmo domínio de cookie.
- Recomenda-se `POST /api/auth/logout` antes de trocar de conta no mesmo host, para limpar o cookie.

### 8.1 Criação de contas e confirmação local

- `POST /api/auth/register/*` devolve `userId`, `emailConfirmationPending` e, quando aplicável, `emailChangeChallenge`. **Não** devolve token de acesso.
- A conta precisa de e-mail confirmado para login (`403 EMAIL_NOT_CONFIRMED`).
- Com `Email:LogOnly=true` ou SMTP vazio, o envio é apenas registrado como ignorado; o código/link não aparece no corpo da resposta. O OTP do checkout usa provedor fake em Development e registra o código no log da API.
- Portanto, a confirmação de e-mail e o OTP **não são automatizáveis pela coleção**. O `README` deve orientar: configurar SMTP local, consultar o log da API no caso do OTP, ou usar contas já confirmadas.
- O admin local é criado pelo `AdminUserSeeder` a partir da configuração `AdminSeed`. A coleção não grava esse e-mail nem essa senha; o usuário preenche `adminEmail`/`adminPassword` na sessão.
- `POST /api/checkout/customer-session` cria/reutiliza uma conta de cliente e devolve access token sem OTP. É classificado como **destrutivo** e só pode ser executado localmente, com opt-in explícito. Nunca na OCI.

## 9. Exemplos de payload

### 9.1 Regras

- Todo endpoint com `[FromBody]` tem exemplo JSON com **todas as chaves do DTO de entrada**, usando os nomes em camelCase.
- Enums são numéricos, pois o backend não configura `JsonStringEnumConverter` para a API.
- Datas são ISO 8601 em UTC (`Z`); `TimeOnly` usa `HH:mm:ss`.
- GUIDs são sempre variáveis de ambiente, nunca valores fixos.
- Campos opcionais aparecem no exemplo com valor realista, `null` ou omitidos quando o próprio DTO/validator indicar que a omissão é válida.
- Exemplos não contêm PII real, credenciais, tokens ou IDs de produção.
- GETs com filtros documentam query params na descrição e em parâmetros desabilitados, com exemplo.

### 9.2 Autenticação

`POST /api/auth/login/*` e `POST /api/auth/token`:

```json
{
  "email": "{{customerEmail}}",
  "password": "{{customerPassword}}"
}
```

`POST /api/auth/register/customer` e `/register/seller`:

```json
{
  "fullName": "Cliente Teste Urbeat",
  "email": "{{customerEmail}}",
  "password": "{{customerPassword}}",
  "phoneNumber": "+5521999990001",
  "document": "{{customerDocument}}"
}
```

`POST /api/auth/email/confirm`:

```json
{
  "userId": "{{customerUserId}}",
  "token": "{{emailConfirmationCode}}"
}
```

`POST /api/auth/forgot-password`:

```json
{
  "email": "{{customerEmail}}"
}
```

`POST /api/auth/reset-password` (o `token` vem do e-mail; não é capturável):

```json
{
  "token": "{{passwordResetToken}}",
  "newPassword": "{{customerPassword}}",
  "confirmPassword": "{{customerPassword}}"
}
```

`POST /api/auth/update-email`:

```json
{
  "userId": "{{customerUserId}}",
  "currentEmail": "{{customerEmail}}",
  "newEmail": "{{newCustomerEmail}}",
  "emailChangeChallenge": "{{emailChangeChallenge}}"
}
```

`POST /api/auth/email/resend-confirmation`:

```json
{
  "email": "{{customerEmail}}"
}
```

`POST /api/auth/refresh` e `POST /api/auth/logout` não têm corpo.

### 9.3 Loja

`POST /api/stores`:

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

`slug` deve casar `^[a-z0-9]+(?:-[a-z0-9]+)*$`, ter 3–120 caracteres e ser único. `cuisineType` deve ser um tipo de culinária ativo; `Hamburgueria` é um dos padrões semeados.

`PUT /api/stores/{storeId}/address`:

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

`PUT /api/stores/{storeId}/business-hours`:

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

`dayOfWeek` é `System.DayOfWeek` numérico (domingo = 0, segunda = 1, …, sábado = 6).

`PATCH /api/stores/{storeId}/delivery-config`:

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

`PATCH /api/stores/{storeId}/status`: `{ "isOpen": true }`.

`POST /api/stores/{storeId}/cuisine-types`: `{ "name": "Comida Japonesa" }`.

`POST /api/stores/delivery-times`: `{ "storeId": "{{storeId}}", "minTimeMinutes": 30, "maxTimeMinutes": 60 }`.

`POST /api/stores/delivery-neighborhoods`: `{ "neighborhood": "Bela Vista", "city": "São Paulo" }`.

`POST /api/neighborhoods/import-by-city` (seller):

```json
{
  "city": "Rio de Janeiro",
  "uf": "RJ",
  "storeId": "{{storeId}}"
}
```

`POST /api/admin/delivery-neighborhoods`: `{ "neighborhood": "Bela Vista", "city": "São Paulo" }`.

`GET /api/stores/delivery-neighborhoods` exige `city` (texto). `GET /api/stores/delivery-neighborhoods-by-store` exige `storeId` (GUID) e aceita `radiusKm` (double opcional, maior que zero).

### 9.4 Catálogo da loja

`POST /api/stores/{storeId}/categories`:

```json
{
  "name": "Destaques",
  "description": "Categoria de teste",
  "displayOrder": 1,
  "isActive": true,
  "isFeatured": true
}
```

`PUT /api/stores/{storeId}/categories/reorder` usa um array puro:

```json
[
  { "id": "{{categoryId}}", "displayOrder": 1 }
]
```

`POST /api/stores/{storeId}/products`:

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
  "additionals": [],
  "additionalIds": [],
  "choiceOptions": [],
  "variations": [],
  "optionGroups": []
}
```

`imageUrl` é obrigatório e não pode ser vazio. `saleMode` aceita `single`, `size`, `fixed_weight` ou `variable_weight`. Para `size`/`fixed_weight`, enviar ao menos uma variação ativa com preço; para `variable_weight`, enviar `weightConfig`.

`PATCH /api/stores/{storeId}/products/{productId}/availability`: `{ "isAvailable": false }`.

`POST /api/stores/{storeId}/products/batch`:

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

`POST /api/stores/{storeId}/additionals`:

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

`PATCH /api/stores/{storeId}/additionals/{additionalId}/status`: `{ "isActive": false }`.

`PUT /api/stores/{storeId}/payment-gateway`:

```json
{
  "gateway": 1,
  "accessToken": "{{paymentGatewayAccessToken}}",
  "notificationUrl": null,
  "environment": "Sandbox",
  "isActive": true
}
```

`gateway`: `1` = MercadoPago, `2` = Mock. O access token é segredo e fica vazio por padrão. `notificationUrl` pode receber `{{webhookPublicUrl}}` quando houver um túnel público local apontando para `/api/webhooks/mercadopago`; não preencher com URL de produção no ambiente OCI. `POST /api/stores/upload-image?type=products` é multipart (seção 10).

### 9.5 Checkout e pedidos

`POST /api/checkout/customer-verification/start`:

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

`POST /api/checkout/customer-verification/confirm`:

```json
{
  "verificationId": "{{verificationId}}",
  "code": "{{verificationCode}}"
}
```

`verificationCode` é o OTP de 4 dígitos; não é devolvido pela API e não é capturável. `POST /api/checkout/customer-verification/resend` usa `{ "verificationId": "{{verificationId}}" }`.

`POST /api/checkout/preview` (não exige pagamento nem endereço):

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

`POST /api/checkout/confirm` exige cliente autenticado e, para `fulfillmentType = 1` (Delivery), `customerAddressId`; para `2` (PickUp), o endereço deve ser omitido/nulo. `paymentMethod` obrigatório: `1` Pix online, `2` cartão online, `3` dinheiro na entrega, `4` cartão na entrega. `POST /api/orders` usa exatamente o mesmo corpo de confirmação.

`PATCH /api/orders/{orderId}/status`:

```json
{
  "newStatus": 4,
  "notes": "Pedido em preparo",
  "deliveryDriverId": null
}
```

`newStatus`: `1` Created, `2` PendingPayment, `3` Received, `4` Preparing, `5` Ready, `6` OnDelivery, `7` Delivered, `8` Cancelled. A transição é validada no servidor; `deliveryDriverId` (ID de vínculo da loja, não o ID global) é obrigatório para `OnDelivery`.

`POST /api/payments/order`:

```json
{
  "orderId": "{{orderId}}"
}
```

`POST /api/orders/{orderId}/review`:

```json
{
  "rating": 5,
  "comment": "Pedido de teste aprovado."
}
```

`rating` entre 1 e 5.

### 9.6 Entregas

`POST /api/delivery-drivers/store`:

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

Quando `email` é preenchido, o backend encaminha para o fluxo de convite; sem e-mail, cria o vínculo simples de loja. `POST /api/delivery-drivers` é a variante global.

`POST /api/delivery-drivers/store/invite`:

```json
{
  "fullName": "Entregador Convidado",
  "email": "{{driverEmail}}",
  "phoneNumber": "+5521999990004"
}
```

`POST /api/delivery-drivers/invites/{token}/accept`:

```json
{
  "password": "{{driverPassword}}",
  "fullName": "Entregador Convidado"
}
```

`POST /api/delivery-routes/store`:

```json
{
  "deliveryDriverId": "{{storeDriverId}}",
  "orderIds": ["{{orderId}}"]
}
```

`POST /api/delivery-routes/store/send`:

```json
{
  "orderIds": ["{{orderId}}"],
  "physicalConfirmation": true
}
```

`POST /api/driver/orders/{orderId}/confirm-pin` e as rotas públicas de entrega:

```json
{
  "pin": "{{deliveryPin}}"
}
```

O PIN é derivado do telefone do cliente e não é devolvido por nenhum endpoint; deve ser conhecido pelo testador.

`PUT /api/orders/store/{orderId}/delivery-driver`: `{ "deliveryDriverId": "{{storeDriverId}}" }`.

### 9.7 Administração

`POST /api/admin/plans`:

```json
{
  "name": "Plano Teste Urbeat",
  "amount": 99.90,
  "description": "Plano criado em ambiente local.",
  "isActive": true
}
```

`PATCH /api/admin/plans/{planId}/status`: `{ "isActive": false }`.

`POST /api/admin/subscriptions/status`:

```json
{
  "sellerUserId": "{{sellerUserId}}",
  "nextDueDateUtc": "2026-11-10T00:00:00Z",
  "billingStatus": 1
}
```

`billingStatus`: `1` Active, `2` Overdue, `3` Blocked.

`PUT /api/admin/system-parameters/{key}`:

```json
{
  "value": "true"
}
```

`POST /api/LandingPageContent`:

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

`POST /api/subscriptions/contract`:

```json
{
  "storeId": "{{storeId}}",
  "planId": "{{planId}}",
  "firstDueDateUtc": "2026-11-10T00:00:00Z"
}
```

### 9.8 Impressão

`PUT /api/printer-config/store`:

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

`printerPresetId` deve vir de `GET /api/printer-config/presets`; não é um valor inventado. `POST /api/printer-config/wifi-print` recebe `{ ipAddress, port, base64Data }`; a coleção deixa `base64Data` em variável vazia e não é executado automaticamente, pois envia bytes a um equipamento da rede local.

`POST /api/seller/printing/jobs/{orderId}/result`:

```json
{
  "success": true,
  "ambiguous": false,
  "operationKey": "{{$guid}}",
  "message": "Impressão confirmada no teste local."
}
```

### 9.9 Consultas e filtros

| Endpoint | Query params |
| --- | --- |
| `GET /api/public/stores` | `cuisineType` (opcional) |
| `GET /api/public/stores/{storeId}/delivery-check` | `neighborhood` (obrigatório) |
| `GET /api/orders/store` | `status`, `startDateUtc`, `endDateUtc`, `page`, `pageSize` (padrão 1/20) |
| `GET /api/orders/store/customers` | `search`, `status` (padrão `all`), `sort` (padrão `lastOrderDesc`), `page`, `pageSize` |
| `GET /api/orders/store/report` | `startDateUtc`, `endDateUtc` |
| `GET /api/stores/delivery-neighborhoods` | `city` (obrigatório) |
| `GET /api/stores/delivery-neighborhoods-by-store` | `storeId` (obrigatório), `radiusKm` (opcional) |
| `GET /api/neighborhoods/cities/{cityId}/search` | `storeId` (opcional), `search` (opcional), `activeOnly` (padrão `true`) |
| `GET /api/neighborhoods/cities/{cityId}/map` | `storeId` (opcional) |
| `GET /api/auth/validate-reset-token` | `token` (obrigatório) |
| `GET /api/address-lookup/cep/{cep}` | CEP com 8 dígitos, sem máscara |

## 10. Uploads e fixtures de imagem

### 10.1 Endpoints multipart

| Requisição | Campo | Query | Limite | Retorno |
| --- | --- | --- | --- | --- |
| `POST /api/stores/upload-image` | `file` | `type` = `logo`, `banner`, `products` ou `store-media` (omitido = `store-media`) | 2 MB logo, 5 MB banner, 6 MB demais; request de 50 MB | `{ "url": "..." }` |
| `POST /api/stores/{storeId}/products/{productId}/images` | `file` | — | 6 MB; request de 50 MB | `ProductResponseDto` com `imageUrl` |

Formatos aceitos: `.avif`, `.jpg`, `.jpeg`, `.png`, `.svg`, `.webp`. O backend valida extensão, MIME declarado (quando presente), tamanho e conteúdo real (ImageSharp/estrutura AVIF/XML seguro para SVG). `application/octet-stream` e MIME ausente são aceitos; prevalece o sniffing.

### 10.2 Fixtures reais do repositório

Usar arquivos versionados, nunca URLs de imagem aleatórias:

| Uso | Arquivo | Tamanho | Formato |
| --- | --- | ---: | --- |
| `type=logo` | `frontend/src/assets/images/urbeat-logo.jpg` | 42.769 bytes | JPEG válido (termina em `FF D9`) |
| `type=banner` | `frontend/src/assets/images/logo_urbeat.png` | 79.958 bytes | PNG válido |
| `type=products` | `frontend/src/assets/images/favicon.png` | 1.522 bytes | PNG válido |
| `type=store-media` / imagem de produto | `docs/BoxSuaSacolaCarrinho.jpeg` | 82.812 bytes | JPEG válido |

Todos estão abaixo dos limites por tipo. O `README` deve trazer a tabela e a instrução de selecionar o arquivo no campo `file`.

**SVG:** os SVGs do repositório (`frontend/public/images/loja.svg`, `moto.svg`) contêm `DOCTYPE` com DTD externo. O validador usa `DtdProcessing.Prohibit` e os rejeita. Não usar esses arquivos como fixture de SVG; se for necessário testar SVG, fornecer um SVG sem DTD e sem referências externas, fora do repositório.

### 10.3 Limitação de portabilidade

O Postman não incorpora os bytes do arquivo no JSON da coleção; ele guarda o caminho/metadados. Consequências:

- Ao importar em outra máquina, o campo `file` precisa ser selecionado novamente.
- Caminhos absolutos do Windows não são portáveis.
- No Newman, usar `--working-dir` apontando para a raiz do repositório e `src` relativo para os fixtures; no Postman desktop, selecionar manualmente.
- A coleção não deve falhar na importação por causa do arquivo ausente; o campo fica sem `src` e a descrição indica o fixture.

## 11. Webhooks

### 11.1 Mercado Pago

`POST /api/webhooks/mercadopago`

- Sem autenticação no controller; o handler extrai `data.id` (ou `id`) e consulta o gateway.
- Payload de exemplo (valores fictícios):

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

- Em ambiente local com `MercadoPago:AllowSimulation=true`, o adaptador devolve `approved` para qualquer ID. Para que o pagamento seja encontrado, `data.id` deve ser igual ao `gatewayTransactionId` capturado em `POST /api/payments/order`.
- Fora da simulação, o handler consulta a API real do Mercado Pago e pode alterar o estado de um pagamento existente. **Nunca executar na OCI.**
- Não há validação de assinatura no controller; a proteção é a consulta ao gateway e a máquina de estados. A coleção não deve sugerir que o endpoint é seguro para replay público.

### 11.2 Asaas

`POST /api/webhooks/asaas`

- Exige o header `asaas-access-token` com valor idêntico à configuração `AsaasWebhook:Token` (comparação ordinal). Sem token configurado ou com valor divergente: `401`.
- O valor nunca é versionado. Usar `{{asaasWebhookToken}}` (secret) ou Vault.
- Payload de exemplo (valores fictícios):

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

- O parser usa `id` como chave idempotente, `event`, `payment.status`/`status`, `payment.value`, `payment.dueDate`, `payment.clientPaymentDate`/`paymentDate` e `externalReference` (ou `sellerUserId`) para localizar o vendedor.
- Status mapeados: `RECEIVED`, `CONFIRMED`, `RECEIVED_IN_CASH` → Active; `OVERDUE`, `PENDING` → Overdue; `REFUNDED` → Blocked; desconhecido → Overdue.
- Payload de exemplo é apenas forma; não reproduzir um evento real em produção.

### 11.3 Regras de execução

- Feature `13 · Webhooks` tem guarda própria: só executa com `runMode=destructive` **e** `allowWebhooks=true`.
- Nunca participa de "Run folder"/"Run collection" por padrão.
- A descrição de cada webhook contém aviso em maiúsculas sobre o efeito e o ambiente permitido.
- Não salvar respostas de webhook da OCI.

## 12. Classificação e guardas de execução

### 12.1 Modos

| `runMode` | Permitido |
| --- | --- |
| `read-only` (padrão) | `GET`/`HEAD` + allowlist de autenticação e prévia |
| `write` | `read-only` + criações/edições não destrutivas |
| `destructive` | tudo, exceto webhooks (que exigem `allowWebhooks=true`) |

Allowlist de `read-only` (além de GET/HEAD):

- `POST /api/auth/login/customer`
- `POST /api/auth/login/seller`
- `POST /api/auth/login/admin`
- `POST /api/auth/login/driver`
- `POST /api/auth/token`
- `POST /api/auth/refresh`
- `POST /api/checkout/preview`

Qualquer outro método é bloqueado em `read-only`.

### 12.2 Classificação

- `read`: GET/HEAD e os POSTs da allowlist.
- `write`: cria/edita recursos sem efeito financeiro, fiscal, de conta ou de entrega. Exemplos: criar loja/categoria/produto/adicional/endereço, atualizar perfil, horários, configuração de entrega, upload, marcar notificação como lida, criar avaliação.
- `destructive`: apaga, publica, fecha loja, muda status de pedido, cria pagamento, contrata assinatura, cria/concede conta, envia convite/e-mail/SMS, compartilha link bearer, dispara importação/migração/retry, altera parâmetros críticos, imprime em rede ou processa webhook.
- `webhook`: os dois endpoints da seção 11, sempre `destructive` + `allowWebhooks`.

### 12.3 Implementação do guard

O guard fica no evento `prerequest` de **coleção**, para rodar antes de qualquer requisição e de qualquer script de pasta. A identidade usada é o nome estável da requisição (`pm.info.requestName`), que não depende de variáveis vazias:

```js
const name = pm.info.requestName; // "MÉTODO /api/..."
const method = name.split(' ')[0];
const mode = String(pm.environment.get('runMode') || 'read-only').toLowerCase();

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
  // lista completa da seção 12.4
]);

const isRead = method === 'GET' || method === 'HEAD';
const isAllowedPost = readOnlyPosts.has(name);
const isWebhook = name.startsWith('POST /api/webhooks/');

let skip = false;
if (!isRead && !isAllowedPost && mode === 'read-only') skip = true;
if (destructive.has(name) && mode !== 'destructive') skip = true;
if (isWebhook && pm.environment.get('allowWebhooks') !== 'true') skip = true;

if (skip) {
  if (pm.execution && typeof pm.execution.skipRequest === 'function') {
    pm.execution.skipRequest();
  } else {
    throw new Error('Execução bloqueada pelo guard do Urbeat: ' + name);
  }
}
```

Regras:

- `runMode` inválido é tratado como `read-only` (fail closed).
- `pm.execution.skipRequest()` é o mecanismo preferido. Em versões que não o exponham, o `throw` interrompe a requisição, mas o Runner marca erro; o `README` deve exigir uma versão atual do Postman.
- O guard não depende de token, ID ou resposta anterior.
- O guard não usa `pm.sendRequest` e não faz chamadas ocultas.

### 12.4 Lista de operações destrutivas

Além de **todos os `DELETE`**, entram na lista:

- `POST /api/auth/register/customer`, `POST /api/auth/register/seller`, `POST /api/auth/email/confirm/{code}`, `POST /api/auth/email/confirm`, `POST /api/auth/email/resend-confirmation`, `POST /api/auth/forgot-password`, `POST /api/auth/reset-password`, `POST /api/auth/update-email`
- `POST /api/checkout/customer-verification/start`, `POST /api/checkout/customer-session`, `POST /api/checkout/customer-verification/confirm`, `POST /api/checkout/customer-verification/resend`, `POST /api/checkout/confirm`
- `POST /api/orders`, `PATCH /api/orders/{orderId}/status`, `POST /api/orders/{orderId}/delivery-confirmation`, `POST /api/orders/{orderId}/complete`
- `POST /api/payments/order`
- `POST /api/stores/{storeId}/publish`
- `POST /api/delivery-drivers/store` (pode disparar convite quando `email` está preenchido; por segurança, a classificação é sempre destrutiva)
- `PATCH /api/stores/{storeId}/status`
- `PUT /api/stores/{storeId}/payment-gateway`
- `PATCH /api/orders/store/customers/{customerUserId}/status`
- `POST /api/delivery-drivers/store/invite`, `POST /api/delivery-drivers/store/{storeDriverId}/resend-invite`, `POST /api/delivery-drivers/store/{storeDriverId}/revoke`, `POST /api/delivery-drivers/invites/{token}/accept`, `PATCH /api/delivery-drivers/{storeDriverId}/status`
- `POST /api/delivery-routes/store/send`, `POST /api/delivery-routes/store/{routeId}/share`, `POST /api/delivery-routes/store/{routeId}/revoke`
- `POST /api/orders/store/{orderId}/delivery-link`
- `POST /api/driver/orders/{orderId}/start`, `POST /api/driver/orders/{orderId}/confirm-pin`
- `POST /api/public/delivery-routes/{token}/stops/{sequence}/deliver`, `POST /api/public/deliveries/{token}/deliver`
- `POST /api/admin/plans`, `PUT /api/admin/plans/{planId}`, `PATCH /api/admin/plans/{planId}/status`
- `POST /api/admin/subscriptions/status`, `POST /api/admin/subscriptions/notifications/process`
- `PUT /api/admin/system-parameters/{key}`, `DELETE /api/admin/system-parameters/{key}`, `POST /api/admin/system-parameters/reload`
- `POST /api/admin/migrate-images`, `POST /api/admin/import-neighborhoods-rj`, `POST /api/admin/import-neighborhoods-google`, `POST /api/admin/delivery-neighborhoods`
- `POST /api/neighborhoods/import-by-city`
- `POST /api/outbox/{id}/retry`
- `POST /api/subscriptions/contract`
- `POST /api/printer-config/wifi-print`
- `POST /api/webhooks/mercadopago`, `POST /api/webhooks/asaas`

Essa lista deve ser conferida pelo script de cobertura contra todos os `DELETE` e contra a lista de nomes marcados como `destructive` na coleção. Divergência falha a validação.

## 13. Segurança

1. **Sem credenciais versionadas.** Nem senha local, nem token, nem cookie, nem access token de gateway, nem token Asaas.
2. **Sem PII real.** Nomes, e-mails, telefones, documentos e endereços são fictícios e claramente identificados como teste.
3. **Tokens são bearer.** Qualquer pessoa com o token acessa o recurso até a expiração. Não colar tokens em issues, chats, logs ou capturas de tela.
4. **Links públicos de entrega são bearer.** `deliveryRouteToken` e `individualDeliveryToken` dão acesso a nome, telefone e endereço do cliente. Limpar histórico do Postman e variáveis após o teste.
5. **OCI é produção.** O guard é uma proteção operacional, não uma fronteira de segurança: qualquer pessoa pode editar a coleção ou o ambiente. A regra é não executar escrita/destrutiva/webhook na OCI, independentemente do guard.
6. **Leituras autenticadas na OCI também expõem PII.** Só usar conta de teste dedicada e com aprovação; preferir endpoints públicos.
7. **Rate limiting.** Produção: login 30/min por IP, recuperação de senha 5/5 min, rotas públicas 120/min. O Runner não deve repetir login ou OTP em loop; 5 falhas bloqueiam a conta por 15 min.
8. **Webhook Mercado Pago** não valida assinatura no controller; um replay local com simulação pode marcar pagamento como pago. Executar apenas em banco local descartável.
9. **`customer-session`** cria conta sem OTP; é a operação mais sensível da coleção e só pode rodar localmente com `runMode=destructive`.
10. **Uploads** aceitam até 50 MB na borda, mas cada tipo tem limite menor e validação de conteúdo. Não desativar a validação para "facilitar" o teste.
11. **CORS não se aplica ao Postman.** Não inferir que uma chamada funciona no navegador porque funcionou na coleção; as origens permitidas são configuradas no backend.
12. **Não usar dados de produção para preencher IDs.** Loja, pedido, pagamento e tokens de entrega devem ser de ambiente local ou de conta de teste explicitamente autorizada.

## 14. Limitações reais do Postman

1. **Arquivo multipart não é embutido.** O JSON guarda caminho/metadados; a importação em outra máquina exige selecionar o arquivo de novo. No Newman, usar `--working-dir`.
2. **Ambientes exportados são texto claro.** Exportar depois de preencher tokens/senhas vaza segredos. Limpar antes de compartilhar; preferir Vault.
3. **`pm.execution.skipRequest()` depende da versão.** Sem essa API, o fallback por `throw` interrompe a requisição mas aparece como erro no Runner.
4. **Cookie de refresh é `HttpOnly` e `Secure`.** O Postman pode receber o `Set-Cookie`, mas o envio em HTTP local não é garantido; refresh pode retornar `401`. Alternativa: re-login ou HTTPS local.
5. **Cookie não isola porta.** `localhost:5000` e `localhost:5281` compartilham cookie; trocar de papel exige logout/login.
6. **Swagger não tem segurança nem exemplos.** Não é possível usar "Authorize" do Swagger UI nem gerar a coleção autenticada a partir dele. A coleção é mantida a partir do código.
7. **Swagger pode ficar desatualizado** se a API não estiver rodando. A verificação principal usa os controllers, não o Swagger.
8. **Enums são numéricos.** Enviar `"PixOnline"` ou `"Delivery"` falha; usar `1`, `2`, etc.
9. **Não existe flag nativa "manual only".** O bloqueio é implementado por script; não é uma permissão imposta pelo Postman.
10. **Runner marca requisições puladas**, e isso não é falha. O relatório deve ser lido com essa distinção.
11. **Arquivos de imagem com caminho absoluto do Windows não são portáveis.** Usar seleção manual ou `src` relativo com Newman.
12. **SVG com `DOCTYPE` é rejeitado pelo backend**, mesmo sendo um SVG válido. Não é limitação do Postman, mas afeta os fixtures.
13. **Não há idempotência de criação.** Reexecutar criações gera duplicatas/409. Para recomeçar, recriar o banco local (`docker compose down -v`) em vez de "limpar" manualmente.
14. **A coleção não substitui testes automatizados.** Ela valida contratos e fluxos manuais; regressões de negócio continuam cobertas por testes do backend/frontend.
15. **Respostas variam.** Alguns endpoints retornam `200` com objeto de resultado contendo flags (`notFound`, `forbidden`), outros retornam `404/403`. Testes de status devem seguir o controller, não um padrão REST assumido.
16. **Listas vazias são respostas válidas.** Não falhar um GET só porque o banco local não tem lojas/pedidos; validar forma e status.

## 15. Agente OpenCode de sincronização

### 15.1 Arquivo e modo

Criar `.opencode/agents/postman-sync.md` com frontmatter no padrão do repositório:

```yaml
---
description: Mantém a coleção Postman do Urbeat sincronizada com contratos, DTOs e features funcionais, sem executar operações de escrita
mode: subagent
---
```

O agente é um subagente; não substitui o `arquiteto` nem o `programador`. O `arquiteto` o aciona quando um contrato REST muda. Não fixar modelo no frontmatter; herdar o modelo do ambiente.

### 15.2 Responsabilidades

1. Ler `AGENTS.md`, esta especificação, `postman/feature-map.json` e `postman/README.md`.
2. Executar `Test-PostmanCoverage.ps1` e comparar a lista de contratos com o mapa e a coleção.
3. Para ação nova, classificá-la pela árvore da seção 4.3 **antes** de criar a requisição; registrar a entrada em `feature-map.json` e depois criar a requisição na feature/subpasta indicada.
4. Atualizar requisições existentes quando método, rota, auth, corpo, query ou DTO mudarem.
5. Se a mudança de negócio alterar a feature/subpasta de um endpoint existente, atualizar o mapa com justificativa explícita e reportar como movimentação.
6. Remover requisições de ações removidas e remover a entrada correspondente do mapa; reportar a remoção.
7. Atualizar o catálogo de variáveis dos dois ambientes quando uma variável nova for necessária, sempre com valor vazio se for segredo/ID.
8. Atualizar o `README` quando o fluxo de uso ou as fixtures mudarem.
9. Executar `Test-PostmanCollection.ps1` e, se a API local estiver disponível, o smoke público com Newman.
10. Reportar: ações adicionadas, alteradas e removidas; features/subpastas afetadas; entradas do mapa criadas/movidas/removidas; variáveis novas; comandos executados; resultado das verificações; riscos ou ambiguidades.

### 15.3 Regras de decisão

- Fonte de verdade do contrato: controllers/DTOs. Organização: features da seção 4.2 e critérios da seção 4.3. Swagger só confirma schema/paths.
- Se uma rota, auth, corpo ou **feature** não puder ser determinada sem ambiguidade, **parar e reportar**; não inventar e não criar pasta provisória.
- Nenhuma pasta ou subpasta recebe nome de controller, camada ou projeto. Se a classificação parecer "naturalmente do controller", usar o resultado de negócio e o dono do estado.
- Cada endpoint tem exatamente uma entrada no `feature-map.json`; não criar duplicata para outro fluxo.
- Webhooks ficam exclusivamente em `13 · Webhooks`; não mover para Pagamentos, Pedidos ou Assinatura.
- Manter os IDs `01`–`14`; não renumerar features. Uma feature nova exige decisão explícita, próximo número livre e atualização da tabela 4.2.
- Se uma feature crescer além de ~30 ações, dividir em subpastas de fluxo; não recriar subpastas por controller.
- Se houver colisão de nome de variável (`{token}` em contextos diferentes), usar o nome contextual definido na seção 4.4.
- Se um DTO mudar e o exemplo ficar inválido, atualizar o exemplo; se o DTO não for parseável com segurança, reportar em vez de "aproximar".
- Preservar IDs de requisição e a ordem de fluxo dentro da feature; não regenerar a coleção inteira.
- Não alterar `backend/`, `frontend/`, `docker/`, `scripts/criarDeployOracleCloud/` nem configurações de deploy.
- Não executar a coleção contra a OCI, não executar requisições `destructive`/`webhook`, não criar ou imprimir segredos.
- Não commitar automaticamente; o diff fica para revisão humana.
- Não adicionar endpoints fora dos controllers (health, Swagger, hubs, Hangfire, métricas).

### 15.4 Integração com o arquiteto

O `arquiteto` só delega ao `postman-sync` se a configuração de permissões dele permitir esse subagente. A implementação futura deve adicionar a permissão de delegação em `.opencode/agents/arquiteto.md` (ou confirmar que o schema local já permite), sem alterar `default_agent`.

Fluxo esperado:

1. Mudança de contrato é implementada e revisada.
2. `arquiteto` aciona `postman-sync` com o escopo da mudança.
3. `postman-sync` atualiza somente `postman/**`, `scripts/postman/**` e, se necessário, esta especificação/README.
4. `arquiteto` revisa o diff e o relatório de cobertura.
5. Nenhum commit automático.

### 15.5 Critérios de aceite do agente

- Uma ação nova em um controller gera exatamente uma entrada no `feature-map.json` e uma requisição na feature/subpasta correta.
- Uma ação removida remove exatamente a requisição e a entrada do mapa, sem afetar as demais.
- Uma mudança de DTO atualiza o exemplo ou é reportada como ambígua.
- A cobertura continua em 33 controllers / 179 ações / 14 features / 44 subpastas (ou no novo total justificado) e o script passa.
- Nenhum segredo é introduzido.
- Nenhuma chamada é feita à OCI.
- O relatório final lista arquivos alterados e comandos executados.

## 16. Critérios de aceite da implementação

1. A coleção importa no Postman sem erro de schema e mostra **14 features**, **44 subpastas de fluxo** e **179 requisições**.
2. Existe exatamente uma requisição por `MÉTODO + rota`, sem duplicatas nem ações faltantes; cada uma tem exatamente uma entrada em `feature-map.json` e uma única feature/subpasta; o Anexo A é a referência.
3. A matriz de autorização confere: 42 No Auth, 85 `sellerToken`, 18 `customerToken`, 23 `adminToken`, 5 `sellerToken` (`Role:Seller`), 5 `driverToken` e 1 `customerToken` (`[Authorize]`).
4. Toda URL começa com `{{baseUrl}}/api/` e não contém host absoluto.
5. Os dois ambientes existem, são importáveis e não contêm credenciais, tokens, cookies, IDs de produção nem PII.
6. `runMode` é `read-only` e `allowWebhooks` é `false` nos dois ambientes.
7. O guard cobre todas as requisições não read-only e toda a lista da seção 12.4; executar a coleção no modo padrão não envia nenhuma escrita, nenhuma operação destrutiva e nenhum webhook.
8. Cada endpoint com `[FromBody]` tem exemplo JSON com as chaves do DTO; cada enum usa valor numérico; cada data usa UTC.
9. Os dois endpoints multipart têm campo `file`, limites e fixtures documentados; nenhum usa URL de imagem externa.
10. Login captura o token do papel correto; refresh é documentado como dependente de cookie; nenhuma captura ocorre em resposta de erro.
11. Webhooks têm payload, header (Asaas), efeitos e aviso de execução manual; não são executados por Runner padrão.
12. `feature-map.json` tem 14 features, 44 subpastas e 179 entradas; `Test-PostmanCoverage.ps1` e `Test-PostmanCollection.ps1` encerram com código 0 e falham quando há divergência simulada.
13. O smoke público com Newman não retorna 5xx e não requer credenciais.
14. O `README` explica importação, contas locais, confirmação de e-mail/OTP, modos, limpeza de variáveis, arquivos e proibição de uso na OCI.
15. O agente `postman-sync` existe, é subagente, tem as guardas da seção 15 e não altera código de aplicação.
16. Nenhum segredo ou dado pessoal real aparece no diff.

## 17. Plano de validação

Executar na fase de implementação, a partir da raiz do repositório.

### 17.1 Conferência da fonte

```powershell
$controllers = Get-ChildItem backend/src/Urbeat.WebApi/Controllers -Filter *Controller.cs
$controllers.Count  # 33

# extração usada nesta especificação: contar [HttpX] por arquivo
$total = 0
$controllers | ForEach-Object {
  $total += (Select-String -Path $_.FullName -Pattern '^\s*\[Http(Get|Post|Put|Patch|Delete)').Count
}
$total  # 179
```

### 17.2 Cobertura

```powershell
powershell -NoProfile -File scripts/postman/Test-PostmanCoverage.ps1 `
  -CollectionPath postman/urbeat.postman_collection.json `
  -FeatureMapPath postman/feature-map.json `
  -ControllersPath backend/src/Urbeat.WebApi/Controllers
```

Critérios:

- 33 controllers e 179 ações na extração.
- 14 features, 44 subpastas e 179 entradas no `feature-map.json`.
- Nenhuma ação ausente/extra/duplicada.
- Cada endpoint aparece em exatamente uma feature/subpasta; nenhuma pasta usa nome de controller.
- Webhooks somente na feature `13`.
- Auth de cada requisição igual à do controller.
- `[FromBody]` presente nos exemplos; `IFormFile` somente nos dois multipart.
- Casos de fronteira alocados corretamente: `GET /api/stores/cuisine-types` e `GET /api/public/stores/{storeId}/reviews` em `03`; `POST/PUT/DELETE /api/stores/{storeId}/cuisine-types` em `04`; gateway da loja em `09`; webhooks em `13`; `POST /api/orders` em `08`, nunca em `09`.
- Lista de destrutivas igual à seção 12.4.

### 17.3 Estrutura, schema e segredos

```powershell
powershell -NoProfile -File scripts/postman/Test-PostmanCollection.ps1 `
  -CollectionPath postman/urbeat.postman_collection.json `
  -LocalEnvironment postman/urbeat.local.postman_environment.json `
  -OciEnvironment postman/urbeat.oci.postman_environment.json
```

Verificações mínimas:

- JSON válido; `info.schema` v2.1; `_postman_id` UUID; IDs únicos.
- URLs com `{{baseUrl}}`; nenhum `http://localhost` ou domínio OCI dentro da coleção.
- Nenhum padrão de JWT (`eyJ...`), `Bearer ` literal, `CHANGE_ME`, senha preenchida, `asaas-access-token` com valor, CPF/CNPJ fixo.
- Toda variável de token/senha/ID vazia nos dois ambientes.
- `feature-map.json` presente, com 14 features, 44 subpastas e 179 endpoints; toda requisição tem destino único.
- Nenhuma pasta/subpasta com nome de controller; features e subpastas seguem a seção 4.2.
- Guard presente; webhooks somente em `13 · Webhooks` e cobertos.
- Exatamente 2 entradas `formdata` do tipo arquivo, com descrição do fixture.
- Nenhum `pm.sendRequest` nem `setNextRequest` em loop.

### 17.4 Importação manual

1. Abrir Postman desktop atual.
2. Importar `urbeat.postman_collection.json` e os dois environments.
3. Confirmar 14 features, 44 subpastas e 179 requisições.
4. Selecionar o ambiente Local.
5. Executar `GET /api/public/stores` e `GET /api/address-lookup/cep/01310100`; aceitar `200` ou `404` (sem dados) e exigir ausência de 5xx.
6. Abrir uma requisição multipart e confirmar que o campo `file` está sem arquivo e a descrição aponta o fixture.

### 17.5 Guardas

1. Com `runMode=read-only`, usar "Run collection" e confirmar que apenas GET/HEAD e a allowlist de login/prévia executam; o restante é pulado.
2. Confirmar que `13 · Webhooks` não executou nenhuma requisição.
3. Mudar para `write`; confirmar que criações não destrutivas executam e que destrutivas continuam bloqueadas.
4. Mudar para `destructive` e confirmar que webhooks continuam bloqueados até `allowWebhooks=true`.
5. Restaurar `read-only` e `false` ao final; limpar tokens/IDs do ambiente.

### 17.6 Fluxo local mínimo (manual)

Pré-requisitos: `docker compose up --build`, banco local descartável, contas confirmadas e SMTP/log disponíveis para confirmação.

1. Login seller → captura `sellerToken`.
2. Criar loja → captura `storeId`/`storeSlug`.
3. Upload de logo com o fixture → captura `uploadedImageUrl`.
4. Criar categoria e produto → captura `categoryId`/`productId`.
5. Login customer por `customer-session` (somente local e `destructive`) ou conta confirmada → captura `customerToken`.
6. Criar endereço → captura `addressId`.
7. `POST /api/checkout/preview` → conferir totais recalculados.
8. `POST /api/checkout/confirm` com Pix online → captura `orderId`.
9. `POST /api/payments/order` → captura `paymentId`/`gatewayTransactionId`.
10. Webhook Mercado Pago local com `gatewayTransactionId` → conferir transição (somente `allowWebhooks=true`).
11. Criar entregador/rota e confirmar entrega com PIN conhecido → conferir estados.
12. Limpar o ambiente/recrear o banco ao final.

### 17.7 Smoke público com Newman

```powershell
npx --yes newman run postman/urbeat.postman_collection.json `
  -e postman/urbeat.local.postman_environment.json `
  --folder "03 · Descoberta e vitrine" `
  --reporters cli
```

- Não requer credenciais.
- Pode retornar `404` em banco vazio; o teste deve aceitar `404` como resposta válida para recurso inexistente, mas nunca `5xx`.
- Repetir para `14 · Utilidades`; a consulta de CEP depende do ViaCep e pode falhar por rede — nesse caso o teste deve reportar a dependência externa, não mascarar como sucesso.

### 17.8 OCI

- Apenas importar e conferir URLs; não executar.
- Se for indispensável validar leitura pública, usar `runMode=read-only`, `allowWebhooks=false`, sem token e apenas `GET /api/public/stores` com `storeId` público informado manualmente.
- Nenhum relatório, print ou histórico com dados da OCI deve ser versionado.

## 18. Riscos e decisões

| Risco / decisão | Mitigação |
| --- | --- |
| Coleção divergir dos controllers | Script de cobertura obrigatório e agente de sincronização. |
| Feature errada ou endpoint duplicado em duas features | `feature-map.json` 1:1, script de cobertura e revisão humana; agente para em ambiguidade. |
| Swagger sem segurança induzir a erro | Auth derivada dos controllers; Swagger só para schema. |
| Execução acidental de escrita | `runMode=read-only` + guard no prerequest + revisão da lista destrutiva. |
| Vazamento de token em ambiente exportado | Variáveis vazias, tipo secret/Vault, README e scan de segredos. |
| Refresh instável em HTTP local | Documentar HTTPS local ou re-login; não falhar a suíte por isso. |
| Confirmação de e-mail/OTP não automatizável | Documentar SMTP/log/contas confirmadas; não simular código. |
| Webhook alterar pagamento real | Manual, `allowWebhooks`, somente local; proibido na OCI. |
| Dados de produção em exemplos | Placeholders, documentos sintéticos, IDs vazios. |
| Reexecução criar duplicatas | Banco local descartável; sem cleanup automático inventado. |
| `pm.execution.skipRequest` indisponível | Fallback com erro explícito; exigir versão atual do Postman. |
| Fixtures de imagem não portáveis | Seleção manual/Newman `--working-dir`; descrição em cada multipart. |
| SVG do repositório rejeitado | Não usar; documentar SVG sem DTD como fixture externo. |
| Postman não ser um teste de regressão | Manter testes backend/frontend como autoridade de comportamento. |

## Anexo A — inventário por feature

Controllers são a origem técnica da extração, não o critério de agrupamento. Cada uma das 179 ações aparece uma única vez abaixo, na feature e subpasta de negócio que possui o estado/resultado. Auth segue a mesma legenda da seção 4.1: `anonymous`, `SellerOnly`, `CustomerOnly`, `AdminOnly`, `Role:Seller`, `DeliveryDriverOnly` e `authenticated`. As constraints de rota (`:guid`, `:int`) foram omitidas nesta listagem; na coleção elas viram variáveis de ambiente.

**Total: 14 features, 44 subpastas e 179 ações.**

### 01 · Acesso e identidade — 16 endpoints

| Subpasta | Ações | Endpoints (`MÉTODO /rota [auth]`) |
| --- | ---: | --- |
| Cadastro | 2 | POST /api/auth/register/customer [anonymous]<br>POST /api/auth/register/seller [anonymous] |
| Confirmação e recuperação | 7 | POST /api/auth/email/confirm [anonymous]<br>POST /api/auth/email/confirm/{code} [anonymous]<br>POST /api/auth/email/resend-confirmation [anonymous]<br>POST /api/auth/forgot-password [anonymous]<br>POST /api/auth/reset-password [anonymous]<br>POST /api/auth/update-email [anonymous]<br>GET /api/auth/validate-reset-token [anonymous] |
| Login | 5 | POST /api/auth/login/admin [anonymous]<br>POST /api/auth/login/customer [anonymous]<br>POST /api/auth/login/driver [anonymous]<br>POST /api/auth/login/seller [anonymous]<br>POST /api/auth/token [anonymous] |
| Sessão | 2 | POST /api/auth/logout [anonymous]<br>POST /api/auth/refresh [anonymous] |

### 02 · Conta do cliente — 9 endpoints

| Subpasta | Ações | Endpoints (`MÉTODO /rota [auth]`) |
| --- | ---: | --- |
| Endereços | 4 | POST /api/customer/addresses [CustomerOnly]<br>GET /api/customer/addresses [CustomerOnly]<br>DELETE /api/customer/addresses/{addressId} [CustomerOnly]<br>PUT /api/customer/addresses/{addressId} [CustomerOnly] |
| Notificações | 2 | GET /api/customer/notifications [CustomerOnly]<br>PATCH /api/customer/notifications/{notificationId}/read [CustomerOnly] |
| Perfil | 3 | GET /api/customer/home [CustomerOnly]<br>PUT /api/customer/me [CustomerOnly]<br>GET /api/customer/me [CustomerOnly] |

### 03 · Descoberta e vitrine — 10 endpoints

| Subpasta | Ações | Endpoints (`MÉTODO /rota [auth]`) |
| --- | ---: | --- |
| Avaliações | 1 | GET /api/public/stores/{storeId}/reviews [anonymous] |
| Cardápio | 3 | GET /api/public/stores/{storeId}/catalog/categories [anonymous]<br>GET /api/public/stores/{storeId}/catalog/products [anonymous]<br>GET /api/public/stores/{storeId}/catalog/products/featured [anonymous] |
| Lojas | 5 | GET /api/public/stores [anonymous]<br>GET /api/public/stores/{storeId} [anonymous]<br>GET /api/public/stores/{storeId}/delivery-check [anonymous]<br>GET /api/public/stores/by-path/{storePath} [anonymous]<br>GET /api/public/stores/by-slug/{slug} [anonymous] |
| Tipos de culinária | 1 | GET /api/stores/cuisine-types [anonymous] |

### 04 · Loja e operação — 25 endpoints

| Subpasta | Ações | Endpoints (`MÉTODO /rota [auth]`) |
| --- | ---: | --- |
| Cobertura de entrega | 10 | GET /api/neighborhoods/cities [Role:Seller]<br>GET /api/neighborhoods/cities/{cityId}/map [Role:Seller]<br>GET /api/neighborhoods/cities/{cityId}/search [Role:Seller]<br>POST /api/neighborhoods/import-by-city [Role:Seller]<br>PATCH /api/stores/{storeId}/delivery-config [SellerOnly]<br>POST /api/stores/delivery-neighborhoods [SellerOnly]<br>GET /api/stores/delivery-neighborhoods [SellerOnly]<br>GET /api/stores/delivery-neighborhoods-by-store [Role:Seller]<br>GET /api/stores/delivery-times [SellerOnly]<br>POST /api/stores/delivery-times [SellerOnly] |
| Horários e abertura | 3 | PUT /api/stores/{storeId}/business-hours [SellerOnly]<br>GET /api/stores/{storeId}/business-hours [SellerOnly]<br>PATCH /api/stores/{storeId}/status [SellerOnly] |
| Onboarding e perfil | 9 | GET /api/seller/panel [SellerOnly]<br>GET /api/seller/profile [SellerOnly]<br>PUT /api/seller/profile [SellerOnly]<br>POST /api/stores [SellerOnly]<br>PUT /api/stores/{storeId} [SellerOnly]<br>GET /api/stores/{storeId}/address [SellerOnly]<br>PUT /api/stores/{storeId}/address [SellerOnly]<br>GET /api/stores/my-store [SellerOnly]<br>POST /api/stores/upload-image [SellerOnly] |
| Tipos de culinária | 3 | POST /api/stores/{storeId}/cuisine-types [SellerOnly]<br>GET /api/stores/{storeId}/cuisine-types [SellerOnly]<br>DELETE /api/stores/{storeId}/cuisine-types/{cuisineTypeId} [SellerOnly] |

### 05 · Publicação e assinatura — 6 endpoints

| Subpasta | Ações | Endpoints (`MÉTODO /rota [auth]`) |
| --- | ---: | --- |
| Assinatura | 4 | POST /api/subscriptions/contract [SellerOnly]<br>GET /api/subscriptions/my [SellerOnly]<br>GET /api/subscriptions/my/charges [SellerOnly]<br>GET /api/subscriptions/plans [SellerOnly] |
| Publicação | 2 | POST /api/stores/{storeId}/publish [SellerOnly]<br>GET /api/stores/{storeId}/publish/summary [SellerOnly] |

### 06 · Impressão e notificações — 8 endpoints

| Subpasta | Ações | Endpoints (`MÉTODO /rota [auth]`) |
| --- | ---: | --- |
| Configuração de impressão | 4 | GET /api/printer-config/presets [SellerOnly]<br>PUT /api/printer-config/store [SellerOnly]<br>GET /api/printer-config/store [SellerOnly]<br>POST /api/printer-config/wifi-print [SellerOnly] |
| Fila de impressão | 2 | GET /api/seller/printing/jobs [SellerOnly]<br>POST /api/seller/printing/jobs/{orderId}/result [SellerOnly] |
| Notificações do vendedor | 2 | GET /api/seller/notifications [SellerOnly]<br>PATCH /api/seller/notifications/{notificationId}/read [SellerOnly] |

### 07 · Cardápio e oferta — 19 endpoints

| Subpasta | Ações | Endpoints (`MÉTODO /rota [auth]`) |
| --- | ---: | --- |
| Adicionais e grupos | 6 | POST /api/stores/{storeId}/additionals [SellerOnly]<br>GET /api/stores/{storeId}/additionals [SellerOnly]<br>DELETE /api/stores/{storeId}/additionals/{additionalId} [SellerOnly]<br>PUT /api/stores/{storeId}/additionals/{additionalId} [SellerOnly]<br>PATCH /api/stores/{storeId}/additionals/{additionalId}/status [SellerOnly]<br>GET /api/stores/{storeId}/additionals/groups [SellerOnly] |
| Categorias | 5 | POST /api/stores/{storeId}/categories [SellerOnly]<br>GET /api/stores/{storeId}/categories [SellerOnly]<br>DELETE /api/stores/{storeId}/categories/{categoryId} [SellerOnly]<br>PUT /api/stores/{storeId}/categories/{categoryId} [SellerOnly]<br>PUT /api/stores/{storeId}/categories/reorder [SellerOnly] |
| Produtos | 8 | POST /api/stores/{storeId}/products [SellerOnly]<br>GET /api/stores/{storeId}/products [SellerOnly]<br>DELETE /api/stores/{storeId}/products/{productId} [SellerOnly]<br>PUT /api/stores/{storeId}/products/{productId} [SellerOnly]<br>PATCH /api/stores/{storeId}/products/{productId}/availability [SellerOnly]<br>POST /api/stores/{storeId}/products/{productId}/images [SellerOnly]<br>POST /api/stores/{storeId}/products/batch [SellerOnly]<br>GET /api/stores/{storeId}/products/option-groups [SellerOnly] |

### 08 · Checkout e pedidos — 22 endpoints

| Subpasta | Ações | Endpoints (`MÉTODO /rota [auth]`) |
| --- | ---: | --- |
| Checkout | 7 | POST /api/checkout/confirm [CustomerOnly]<br>POST /api/checkout/customer-session [anonymous]<br>POST /api/checkout/customer-verification/confirm [anonymous]<br>POST /api/checkout/customer-verification/resend [anonymous]<br>POST /api/checkout/customer-verification/start [anonymous]<br>POST /api/checkout/preview [anonymous]<br>POST /api/orders [CustomerOnly] |
| Clientes da loja | 3 | GET /api/orders/store/customers [SellerOnly]<br>PUT /api/orders/store/customers/{customerUserId} [SellerOnly]<br>PATCH /api/orders/store/customers/{customerUserId}/status [SellerOnly] |
| Pedidos da loja | 9 | POST /api/orders/{orderId}/complete [SellerOnly]<br>PATCH /api/orders/{orderId}/status [SellerOnly]<br>GET /api/orders/store [SellerOnly]<br>GET /api/orders/store/{orderId} [SellerOnly]<br>DELETE /api/orders/store/{orderId}/delivery-driver [SellerOnly]<br>PUT /api/orders/store/{orderId}/delivery-driver [SellerOnly]<br>POST /api/orders/store/{orderId}/delivery-link [SellerOnly]<br>GET /api/orders/store/deliveries [SellerOnly]<br>GET /api/orders/store/report [SellerOnly] |
| Pedidos do cliente | 3 | GET /api/orders/{orderId} [CustomerOnly]<br>POST /api/orders/{orderId}/delivery-confirmation [CustomerOnly]<br>GET /api/orders/my [CustomerOnly] |

### 09 · Pagamentos — 6 endpoints

| Subpasta | Ações | Endpoints (`MÉTODO /rota [auth]`) |
| --- | ---: | --- |
| Gateway da loja | 3 | DELETE /api/stores/{storeId}/payment-gateway [SellerOnly]<br>PUT /api/stores/{storeId}/payment-gateway [SellerOnly]<br>GET /api/stores/{storeId}/payment-gateway [SellerOnly] |
| Pagamentos do pedido | 3 | POST /api/payments/order [CustomerOnly]<br>GET /api/payments/order/{orderId} [CustomerOnly]<br>GET /api/payments/order/{orderId}/history [CustomerOnly] |

### 10 · Avaliações — 3 endpoints

| Subpasta | Ações | Endpoints (`MÉTODO /rota [auth]`) |
| --- | ---: | --- |
| Avaliações do pedido | 2 | GET /api/orders/{orderId}/review [authenticated]<br>POST /api/orders/{orderId}/review [CustomerOnly] |
| Avaliações recebidas | 1 | GET /api/reviews/store [SellerOnly] |

### 11 · Entregas — 27 endpoints

| Subpasta | Ações | Endpoints (`MÉTODO /rota [auth]`) |
| --- | ---: | --- |
| App do entregador | 5 | GET /api/driver/orders [DeliveryDriverOnly]<br>GET /api/driver/orders/{orderId} [DeliveryDriverOnly]<br>POST /api/driver/orders/{orderId}/confirm-pin [DeliveryDriverOnly]<br>POST /api/driver/orders/{orderId}/start [DeliveryDriverOnly]<br>GET /api/driver/stores [DeliveryDriverOnly] |
| Entregadores | 11 | POST /api/delivery-drivers [SellerOnly]<br>PUT /api/delivery-drivers/{storeDriverId} [SellerOnly]<br>DELETE /api/delivery-drivers/{storeDriverId} [SellerOnly]<br>PATCH /api/delivery-drivers/{storeDriverId}/status [SellerOnly]<br>GET /api/delivery-drivers/invites/{token} [anonymous]<br>POST /api/delivery-drivers/invites/{token}/accept [anonymous]<br>POST /api/delivery-drivers/store [SellerOnly]<br>GET /api/delivery-drivers/store [SellerOnly]<br>POST /api/delivery-drivers/store/{storeDriverId}/resend-invite [SellerOnly]<br>POST /api/delivery-drivers/store/{storeDriverId}/revoke [SellerOnly]<br>POST /api/delivery-drivers/store/invite [SellerOnly] |
| Links públicos de entrega | 4 | GET /api/public/deliveries/{token} [anonymous]<br>POST /api/public/deliveries/{token}/deliver [anonymous]<br>GET /api/public/delivery-routes/{token} [anonymous]<br>POST /api/public/delivery-routes/{token}/stops/{sequence}/deliver [anonymous] |
| Rotas de entrega | 7 | POST /api/delivery-routes/store [SellerOnly]<br>GET /api/delivery-routes/store [SellerOnly]<br>PUT /api/delivery-routes/store/{routeId} [SellerOnly]<br>GET /api/delivery-routes/store/{routeId} [SellerOnly]<br>POST /api/delivery-routes/store/{routeId}/revoke [SellerOnly]<br>POST /api/delivery-routes/store/{routeId}/share [SellerOnly]<br>POST /api/delivery-routes/store/send [SellerOnly] |

### 12 · Administração — 25 endpoints

| Subpasta | Ações | Endpoints (`MÉTODO /rota [auth]`) |
| --- | ---: | --- |
| Assinaturas e cobranças | 2 | POST /api/admin/subscriptions/notifications/process [AdminOnly]<br>POST /api/admin/subscriptions/status [AdminOnly] |
| Conteúdo da landing page | 6 | POST /api/LandingPageContent [AdminOnly]<br>GET /api/LandingPageContent [anonymous]<br>DELETE /api/LandingPageContent/{id} [AdminOnly]<br>PUT /api/LandingPageContent/{id} [AdminOnly]<br>GET /api/LandingPageContent/{id} [AdminOnly]<br>GET /api/LandingPageContent/section/{section} [anonymous] |
| Manutenção e importações | 4 | POST /api/admin/delivery-neighborhoods [AdminOnly]<br>POST /api/admin/import-neighborhoods-google [AdminOnly]<br>POST /api/admin/import-neighborhoods-rj [AdminOnly]<br>POST /api/admin/migrate-images [AdminOnly] |
| Outbox | 3 | POST /api/outbox/{id}/retry [AdminOnly]<br>GET /api/outbox/failed [AdminOnly]<br>GET /api/outbox/summary [AdminOnly] |
| Painel e planos | 5 | GET /api/admin/dashboard [AdminOnly]<br>POST /api/admin/plans [AdminOnly]<br>GET /api/admin/plans [AdminOnly]<br>PUT /api/admin/plans/{planId} [AdminOnly]<br>PATCH /api/admin/plans/{planId}/status [AdminOnly] |
| Parâmetros do sistema | 5 | GET /api/admin/system-parameters [AdminOnly]<br>DELETE /api/admin/system-parameters/{key} [AdminOnly]<br>PUT /api/admin/system-parameters/{key} [AdminOnly]<br>GET /api/admin/system-parameters/{key} [AdminOnly]<br>POST /api/admin/system-parameters/reload [AdminOnly] |

### 13 · Webhooks — 2 endpoints

| Subpasta | Ações | Endpoints (`MÉTODO /rota [auth]`) |
| --- | ---: | --- |
| Asaas | 1 | POST /api/webhooks/asaas [anonymous] |
| Mercado Pago | 1 | POST /api/webhooks/mercadopago [anonymous] |

### 14 · Utilidades — 1 endpoint

| Subpasta | Ações | Endpoints (`MÉTODO /rota [auth]`) |
| --- | ---: | --- |
| Consulta de CEP | 1 | GET /api/address-lookup/cep/{cep} [anonymous] |

## Anexo B — checklist de importação

- [ ] Coleção v2.1 importada sem erro.
- [ ] Ambientes Local e OCI importados separadamente.
- [ ] 14 features e 44 subpastas visíveis; nenhuma pasta nomeada por controller.
- [ ] 179 requisições; contagem por feature confere com a seção 4.2 e com `feature-map.json`.
- [ ] Nenhum token/senha/ID preenchido nos JSONs.
- [ ] `runMode=read-only` e `allowWebhooks=false`.
- [ ] GET público responde sem credencial.
- [ ] Multipart mostra campo `file` vazio e fixture na descrição.
- [ ] Runner no modo padrão pula escritas/destrutivas/webhooks.
- [ ] Webhooks e OCI não foram executados.
- [ ] Histórico/variáveis limpos ao final.
