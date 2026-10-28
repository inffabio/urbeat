# Coleção Postman do urbeat

Coleção de homologação manual da API REST do urbeat. Cobre as 179 ações expostas pelos controllers da WebApi do urbeat, incluindo os 2
webhooks, organizadas em 14 features funcionais e 44 subpastas de fluxo.

## 1. Escopo e o que a coleção não é

- **É**: ferramenta de exploração, conferência de contrato e homologação manual
  em ambiente local.
- **Não é**: cliente de produção, suíte de carga, teste E2E de navegador, teste de
  segurança ofensiva ou substituto dos testes automatizados de backend/frontend.
- Não cobre SignalR (`/hubs/...`), `/health`, `/swagger`, `/hangfire` nem `/metrics`.
- Nenhuma credencial, token, cookie, documento pessoal ou ID de produção é
  versionado. Os ambientes trazem todas as variáveis vazias.

## 2. Requisitos

- Postman desktop **10.15 ou superior** (necessário para
  `pm.execution.skipRequest()`).
- Opcional: Newman 6+ via `npx --yes newman` para smoke local.
- API local disponível para executar requisições (a importação não exige API).

## 3. Importação

1. Abra o Postman desktop.
2. Importe `postman/urbeat.postman_collection.json`.
3. Importe `postman/urbeat.local.postman_environment.json` e
   `postman/urbeat.oci.postman_environment.json` separadamente.
4. Selecione o ambiente **urbeat · local**.

Resultado esperado: 14 features, 44 subpastas e 179 requisições. Nenhuma pasta
recebe nome de controller; a origem técnica aparece na linha `Controller:` de cada
descrição.

Contagens de referência:

| Métrica | Valor |
| --- | ---: |
| Ações REST + webhooks | 179 |
| Anônimas (`No Auth`) | 42 |
| `sellerToken` (`SellerOnly` + `Role:Seller`) | 90 |
| `customerToken` (`CustomerOnly` + `[Authorize]`) | 19 |
| `adminToken` | 23 |
| `driverToken` | 5 |
| `read` | 78 |
| `write` | 38 |
| `destructive` | 61 |
| `webhook` | 2 |

## 4. Ambientes

| Ambiente | `baseUrl` | `webBaseUrl` | Uso |
| --- | --- | --- | --- |
| `urbeat · local` | `http://localhost:5000` | `http://localhost:4200` | Padrão, Docker Compose. |
| `urbeat · oci` | `https://urbeat.com.br` | `https://urbeat.com.br` | Somente conferência de URL e leitura pública autorizada. |

Variantes locais (edite apenas na sessão):

- `dotnet run` (perfil HTTP): `baseUrl=http://localhost:5281`.
- `dotnet run` (perfil HTTPS): `baseUrl=https://localhost:7274`. Recomendado para
  testar `POST /api/auth/refresh`, porque o cookie é `Secure`.
- Frontend local: `webBaseUrl=http://localhost:4200`.

O mesmo conjunto de variáveis existe nos dois ambientes; somente `baseUrl` e
`webBaseUrl` diferem. Nenhum valor sensível é preenchido.

A URL OCI padrão é `https://urbeat.com.br` porque é o host explicitamente servido
pelo nginx versionado: `docker/nginx/conf.d/20-https.conf:17` declara
`server_name urbeat.com.br www.urbeat.com.br` e `:66` expõe `location /api/`.
`api.urbeat.com.br` só tem vhost explícito em HTTP (`10-http.conf:24-47`) e é
citado em `DEPLOY.md:97`; não há `server_name` HTTPS dedicado para o subdomínio no
arquivo versionado. Se a infraestrutura confirmar HTTPS no subdomínio, altere
`baseUrl` apenas na sessão, sem versionar a mudança.

## 5. Contas locais

- O admin local é criado pelo `AdminUserSeeder` a partir da configuração
  `AdminSeed`. A coleção não conhece nem grava esse e-mail/senha.
- Preencha `adminEmail`/`adminPassword` manualmente na sessão.
- Cliente e vendedor podem vir de `POST /api/auth/register/*` (exigem confirmação
  de e-mail) ou de contas previamente confirmadas.
- Entregador pode vir de convite aceito ou de conta de teste já existente.

## 6. Confirmação de e-mail e OTP

A confirmação de e-mail e o OTP do checkout **não são automatizáveis** pela
coleção:

- `POST /api/auth/register/*` retorna `userId`, `emailConfirmationPending` e, às
  vezes, `emailChangeChallenge`; não retorna token de acesso.
- Com `Email:LogOnly=true` ou SMTP vazio, o envio é apenas registrado como
  ignorado e o link não aparece na resposta.
- O OTP do checkout usa provedor fake em Development e registra o código no log da
  API.

Alternativas: configurar SMTP local, consultar o log da API ou usar contas já
confirmadas. A coleção não inventa códigos.

Diferença importante:

- `emailConfirmationCode` é o **short code** da rota
  `POST /api/auth/email/confirm/{code}`.
- `emailConfirmationToken` é o **token longo** do corpo de
  `POST /api/auth/email/confirm`.

## 7. `runMode` e `allowWebhooks`

O guard fica no evento `prerequest` de **coleção** e roda antes de qualquer
requisição — inclusive em um **Send manual**, não apenas no Runner.

Antes de decidir pelo `runMode`, o guard confere se o nome estável
(`MÉTODO /rota`) corresponde ao método e ao path efetivos de `pm.request`. Nome
malformado, método divergente, quantidade de segmentos diferente, literal de rota
diferente, `baseUrl` ausente ou host absoluto fora do ambiente bloqueiam a
requisição mesmo em `destructive`. Segmentos variáveis podem chegar resolvidos
(`/api/stores/<guid>/products`) ou como `{{storeId}}`; ambos são aceitos para não
quebrar rotas com parâmetros.

Limitações dessa validação:

- a query string não é comparada, apenas método e path;
- quando o shape literal é o mesmo, o guard não distingue nomes contextuais de
  variáveis (`{token}` de rota pública vs. convite), apenas a posição/literal;
- a comparação depende de `pm.request.url.getPath()`/`pm.request.url.path` do SDK
  do Postman; se a URL não expuser path verificável, o guard bloqueia;
- `baseUrl` é obrigatório no ambiente selecionado; sem ele, o guard bloqueia.

| `runMode` | Executa |
| --- | --- |
| `read-only` (padrão) | GET/HEAD + allowlist de login/prévia. Bloqueia o restante. |
| `write` | `read-only` + criações/edições não destrutivas. Bloqueia destrutivas e webhooks. |
| `destructive` | Tudo, exceto webhooks. |
| valor ausente ou inválido | Tratado como `read-only` (fail closed). |

Allowlist de `read-only` (além de GET/HEAD):

- `POST /api/auth/login/customer`
- `POST /api/auth/login/seller`
- `POST /api/auth/login/admin`
- `POST /api/auth/login/driver`
- `POST /api/auth/token`
- `POST /api/auth/refresh`
- `POST /api/checkout/preview`

Webhooks exigem `runMode=destructive` **e** `allowWebhooks=true`, apenas local.

Como o guard se comporta:

- `pm.execution.skipRequest()` pula a requisição. No Runner ela aparece como
  **skipped**, o que não é falha.
- Em versões sem essa API, o fallback lança erro e interrompe a requisição; o
  Runner marca erro. Use Postman atualizado.
- O guard é **proteção operacional contra acidente**, não fronteira de segurança.
  Qualquer pessoa pode editar a coleção ou o ambiente. A regra de não escrever na
  OCI é humana.
- Para executar manualmente uma escrita bloqueada, eleve `runMode` de forma
  consciente. Não existe "modo manual" separado que ignore o guard.

## 8. Variáveis

Todas as variáveis são de **ambiente**, nunca de coleção. Catálogo:

| Grupo | Variáveis |
| --- | --- |
| Controle | `runMode`, `allowWebhooks`, `lastLoginRole` |
| URLs/dados públicos | `baseUrl`, `webBaseUrl`, `webhookPublicUrl`, `cep`, `neighborhood`, `cuisineType`, `storePath`, `storeSlug`, `landingSection`, `cityId`, `printerPresetId` |
| Credenciais (secret) | `customerEmail`, `newCustomerEmail`, `customerPassword`, `sellerEmail`, `sellerPassword`, `adminEmail`, `adminPassword`, `driverEmail`, `driverPassword` |
| Tokens (secret) | `customerToken`, `sellerToken`, `adminToken`, `driverToken` |
| IDs/valores | `storeId`, `cuisineTypeId`, `categoryId`, `productId`, `productImageUrl`, `uploadedImageUrl`, `additionalId`, `additionalGroupId`, `addressId`, `orderId`, `orderCode`, `paymentId`, `gatewayTransactionId`, `storeDriverId`, `deliveryDriverId`, `routeId`, `customerUserId`, `sellerUserId`, `planId`, `systemParameterKey`, `landingContentId`, `outboxEventId`, `customerNotificationId`, `sellerNotificationId`, `verificationId`, `stopSequence` |
| Segredos/links/códigos (secret) | `deliveryRouteToken`, `individualDeliveryToken`, `driverInviteToken`, `verificationCode`, `deliveryPin`, `emailConfirmationCode`, `emailConfirmationToken`, `passwordResetToken`, `emailChangeChallenge`, `customerDocument`, `sellerDocument`, `storeDocument`, `storePixKey`, `asaasWebhookToken`, `paymentGatewayAccessToken`, `printerBase64Data` |

Notas de captura:

- `lastLoginRole` registra o último login (`customer`, `seller`, `admin`,
  `driver`). `POST /api/auth/refresh` grava o novo access token na variável desse
  papel, pois o refresh não informa qual conta renovou.
- `POST /api/auth/logout` limpa `lastLoginRole`; faça login novamente para renovar
  um papel específico.
- Notificações e outbox capturam `items[0].id` (não `[0].id`).
- `deliveryRouteToken`, `individualDeliveryToken` e `driverInviteToken` são
  credenciais bearer, embora a especificação original os listasse entre IDs.
- `printerBase64Data` fica vazio e marcado como secret: o conteúdo depende do
  modelo físico da impressora.
- **Limpeza**: exportar um ambiente preenchido grava tokens/senhas em texto claro.
  Antes de exportar ou compartilhar, use `Environment → Clear values` e reimporte o
  arquivo original. Prefira Postman Vault (`{{vault:chave}}`) quando disponível.
- Tokens JWT, cookies, `asaas-access-token` e access tokens de gateway nunca devem
  ser colados em issues, chats, logs ou capturas de tela.

## 9. Fluxo local mínimo

Pré-requisitos: `docker compose up --build`, banco local descartável e contas
confirmadas/SMTP/log disponíveis.

1. Login seller → captura `sellerToken`.
2. Upload do logo com o fixture (`type=logo`) → captura `uploadedImageUrl`. Esse
   endpoint aceita upload antes de existir loja e grava em
   `stores/_pending/{userId}/logo`.
3. Criar loja usando `{{uploadedImageUrl}}` em logo/banner → captura `storeId` e
   `storeSlug`.
4. Opcional: subir banner/produto com `storeId` já preenchido.
5. Criar categoria e produto → captura `categoryId` e `productId`.
6. Login customer (conta confirmada) ou `customer-session` local e destrutivo →
   captura `customerToken`.
7. Criar endereço → captura `addressId`.
8. `POST /api/checkout/preview` → conferir totais recalculados pelo servidor.
9. `POST /api/checkout/confirm` com Pix online → captura `orderId`/`orderCode`.
10. `POST /api/payments/order` → captura `paymentId`/`gatewayTransactionId`.
11. Webhook Mercado Pago local com o `gatewayTransactionId` → conferir transição
    (somente com `allowWebhooks=true`).
12. Criar entregador/rota e confirmar entrega com PIN conhecido.
13. Ao final, limpe as variáveis e recrie o banco local.

Reexecutar criações gera duplicatas ou `409`. Para recomeçar do zero, recrie o
banco local com `docker compose down -v` em vez de "limpar" registros à mão.

## 10. Uploads e fixtures

| Requisição | Campo | Query | Fixture |
| --- | --- | --- | --- |
| `POST /api/stores/upload-image` | `file` | `type=logo` (padrão do exemplo) | `frontend/src/assets/images/urbeat-logo.jpg` (logo, 42.769 B) |
| `POST /api/stores/upload-image` | `file` | `type=banner` | `frontend/src/assets/images/logo_urbeat.png` (79.958 B) |
| `POST /api/stores/upload-image` | `file` | `type=products` | `frontend/src/assets/images/favicon.png` (1.522 B) |
| `POST /api/stores/upload-image` | `file` | `type=store-media` | `docs/BoxSuaSacolaCarrinho.jpeg` (82.812 B) |
| `POST /api/stores/{storeId}/products/{productId}/images` | `file` | — | `docs/BoxSuaSacolaCarrinho.jpeg` (82.812 B) |

Limites: 2 MB logo, 5 MB banner, 6 MB demais; request de 50 MB. Formatos:
`.avif`, `.jpg`, `.jpeg`, `.png`, `.svg`, `.webp`. O backend valida extensão, MIME
declarado, tamanho e conteúdo real.

O Postman **não incorpora os bytes** no JSON da coleção. Ao importar em outra
máquina, selecione o arquivo novamente no campo `file`. No Newman, use
`--working-dir` na raiz do repositório e um `src` relativo. Caminhos absolutos do
Windows não são portáveis.

Não use `frontend/public/images/loja.svg` nem `moto.svg`: eles contêm `DOCTYPE`
com DTD externo e são rejeitados pelo validador (`DtdProcessing.Prohibit`). Para
testar SVG, forneça um arquivo sem DTD e sem referências externas, fora do
repositório.

## 11. Webhooks

### Mercado Pago

- `POST /api/webhooks/mercadopago` não valida assinatura no controller; consulta o
  gateway e aplica a máquina de estados.
- Em local com `MercadoPago:AllowSimulation=true`, o adaptador devolve `approved`
  para qualquer ID. Para o pagamento ser encontrado, `data.id` deve ser igual ao
  `gatewayTransactionId` capturado em `POST /api/payments/order`.
- Fora da simulação, a chamada consulta a API real e pode alterar pagamento.
- Replay público não é seguro; execute apenas em banco local descartável.

### Asaas

- Exige o header `asaas-access-token` idêntico a `AsaasWebhook:Token` (comparação
  ordinal). Sem token configurado ou divergente: `401`.
- O `appsettings.json` versiona um valor placeholder de token; **não copie** esse
  valor. Configure um token local e preencha `asaasWebhookToken` na sessão.
- Para testar o `401`, use um valor divergente.

Webhooks nunca participam de Runner padrão. Só executam com
`runMode=destructive` **e** `allowWebhooks=true`, e nunca na OCI.

`webhookPublicUrl` existe para o campo `notificationUrl` do gateway da loja quando
houver um túnel público local apontando para `/api/webhooks/mercadopago`. Nunca
preencha esse campo com uma URL de produção no ambiente OCI.

## 12. Refresh e sessão

- O login devolve `{ accessToken, expiresAtUtc }`. O refresh token vai apenas no
  cookie `urbeat.refresh_token` (`HttpOnly`, `Secure`, `SameSite=Strict`,
  `Path=/`).
- `POST /api/auth/refresh` depende exclusivamente desse cookie.
- Em `http://localhost:5000` o cookie `Secure` pode não ser enviado; para testar
  refresh use `https://localhost:7274` ou refaça o login.
- O cookie não isola portas: `localhost:5000` e `localhost:5281` compartilham o
  mesmo domínio. Faça logout antes de trocar de conta/papel.
- Login sucessivo em papéis diferentes sobrescreve o cookie de refresh; os access
  tokens anteriores continuam válidos até expirarem.

## 13. OCI (produção)

- O ambiente OCI serve para conferir URLs, importação e leitura pública.
- O `baseUrl` OCI padrão é `https://urbeat.com.br`, compatível com
  `docker/nginx/conf.d/20-https.conf:17` (`server_name urbeat.com.br`) e `:66`
  (`location /api/`). O subdomínio `api.urbeat.com.br` só aparece explicitamente
  em HTTP em `10-http.conf:24-47`; se a infraestrutura confirmar HTTPS nele,
  altere `baseUrl` apenas na sessão.
- **Nunca** execute escrita, operação destrutiva ou webhook na OCI.
- Leitura autenticada também expõe PII e só pode ocorrer com conta de teste
  dedicada e aprovação explícita.
- O guard não é fronteira de segurança: qualquer pessoa pode editar a coleção ou o
  ambiente. A proibição é operacional e humana.
- Nenhum relatório, print ou histórico com dados da OCI deve ser versionado.
- `customer-session` cria conta sem OTP e só pode rodar localmente.

## 14. Verificação

Da raiz do repositório:

```powershell
# Cobertura: controllers -> mapa -> coleção (179 ações, 14 features, 44 subpastas)
powershell -NoProfile -File scripts/postman/Test-PostmanCoverage.ps1

# Estrutura, segredos, ambientes e matriz determinística do guard
powershell -NoProfile -File scripts/postman/Test-PostmanCollection.ps1 -VerifyGuard

# JSON válido
Get-Content postman/urbeat.postman_collection.json -Raw | ConvertFrom-Json | Out-Null
Get-Content postman/urbeat.local.postman_environment.json -Raw | ConvertFrom-Json | Out-Null
Get-Content postman/urbeat.oci.postman_environment.json -Raw | ConvertFrom-Json | Out-Null
```

Os scripts retornam `0` em sucesso e `1` em divergência. `Test-PostmanCoverage.ps1`
compara método e cada segmento da rota com o controller, incluindo a variável
esperada em cada parâmetro de path, além de auth, classe, query obrigatória e
chaves de DTO. Ele aceita `-MapOnly` (antes da coleção existir) e `-Feature 08`
(recorte por feature).
`Test-PostmanCollection.ps1 -VerifyGuard` executa a matriz de decisão do guard para
as 179 requisições em `read-only`, `write`, `destructive`,
`destructive+allowWebhooks` e `runMode` inválido, além de casos sintéticos de
divergência entre nome, método, path, host e `baseUrl`.

Smoke público local (opcional; nunca OCI):

```powershell
npx --yes newman run postman/urbeat.postman_collection.json `
  -e postman/urbeat.local.postman_environment.json `
  --folder "03 · Descoberta e vitrine" --reporters cli

npx --yes newman run postman/urbeat.postman_collection.json `
  -e postman/urbeat.local.postman_environment.json `
  --folder "14 · Utilidades" --reporters cli
```

Asserts dos GETs públicos:

- Listas sem parâmetro (`GET /api/public/stores`, `GET /api/stores/cuisine-types`
  e `GET /api/LandingPageContent`) exigem `200` e corpo array.
- `GET /api/LandingPageContent/section/{section}` só exige `200` e array quando
  `landingSection` está preenchido; sem a variável, valida apenas ausência de
  `401/403/5xx`.
- Endpoints com variável de path/query só exigem status de contrato quando a
  variável está preenchida (`200/404`, `200/404/410`). Com variável vazia, o teste
  verifica apenas ausência de `401/403/5xx`; não trata `400/404` como sucesso.
- `validate-reset-token` sem token exige `400`; com token exige `200` e objeto com
  `valid`.
- CEP com 8 dígitos aceita `200/404`; CEP inválido aceita `400/404` como falha de
  validação, nunca como sucesso. A consulta depende do ViaCep: falha de rede deve
  ser reportada, não mascarada.
- Nenhum smoke alega sucesso em `401`; endpoints públicos não devem retornar
  `401/403`, e isso é verificado explicitamente.
- O smoke cobre contrato público e ausência de autenticação/5xx; ele não prova
  regras de negócio nem substitui os testes de backend/frontend.

## 15. Limitações reais

1. Multipart não embute o arquivo; selecione manualmente ou use `--working-dir`.
2. Ambientes exportados são texto claro.
3. `pm.execution.skipRequest()` depende da versão; o fallback por `throw` aparece
   como erro no Runner.
4. O cookie de refresh pode não funcionar em HTTP local.
5. O cookie não isola portas.
6. O Swagger não tem definição de segurança nem exemplos; a fonte de verdade é o
   código.
7. O Swagger pode ficar desatualizado se a API não estiver rodando.
8. Enums são numéricos; enviar `"PixOnline"` ou `"Delivery"` falha.
9. Não existe flag nativa "manual only"; o bloqueio é script.
10. Requisições puladas no Runner não são falhas.
11. Caminhos absolutos de imagem não são portáveis.
12. SVG com `DOCTYPE` é rejeitado pelo backend.
13. Não há idempotência de criação; reexecutar gera duplicatas/`409`.
14. A coleção não substitui testes automatizados.
15. Alguns endpoints retornam `200` com flags (`notFound`, `forbidden`) em vez de
    `404/403`; siga o controller.
16. Listas vazias são respostas válidas.
17. O guard valida método e path do nome contra `pm.request`, mas não valida query
    string nem distingue nomes contextuais de variáveis no mesmo shape literal; a
    comparação depende de `pm.request.url.getPath()`/`path` do SDK do Postman.
18. `baseUrl` é obrigatório no ambiente; sem ele o guard bloqueia por fail closed.
19. Variável de path vazia pode reduzir o path efetivo e fazer o guard bloquear por
    divergência de segmentos; preencha a variável antes de executar.

## 16. Manutenção

- O mapa autoritativo é `postman/feature-map.json` (1:1 entre as 179 ações e
  feature/subpasta).
- IDs de requisição são estáveis; nunca regenere a coleção inteira. Crie IDs apenas
  para requisições novas.
- Novas ações são classificadas pela árvore de decisão da especificação antes de
  entrar na coleção; webhooks ficam exclusivamente em `13 · Webhooks` e
  `POST /api/orders` em `08 · Checkout e pedidos`.
- O agente `.opencode/agents/postman-sync.md` mantém a coleção, roda os dois
  verificadores e reporta adições/remoções/movimentações. Ele não executa OCI,
  não executa destrutivas/webhooks e não commita.
- Divergências encontradas na implementação, conferidas contra o código:
  - `GET /api/driver/orders` exige o query `storeId`; `GET
    /api/stores/delivery-times` também. O plano não os listava.
  - `GET`/`DELETE /api/stores/{storeId}/payment-gateway` aceitam `gateway`
    opcional; `DELETE /api/stores/{storeId}/categories/{categoryId}` aceita
    `reassignCategoryId`; `POST /api/admin/import-neighborhoods-google` aceita
    `uf`. Todos entram desabilitados quando opcionais.
  - `POST /api/stores/{storeId}/additionals`, `PUT .../additionals/{additionalId}`
    e `PATCH .../status`, além de `PUT/PATCH /api/orders/store/customers/...`, usam
    corpo implícito (`ApiController`), não `[FromBody]` explícito.
  - Correções D1–D11 do plano aplicadas: `lastLoginRole` para o refresh;
    `items[0].id` em notificações/outbox; `weightConfig` no produto; B13a/B13b
    separados; `emailConfirmationToken` separado do short code; normalização
    fail-closed de `runMode`; token Asaas não versionado; verificação determinística
    do guard; permissão de delegação do `arquiteto`; `cuisine-types` sem `PUT`.

## 17. Segurança

- A marca é `urbeat`, minúscula.
- Nenhum segredo, cookie, JWT, CPF/CNPJ real ou e-mail real é versionado.
- Documentos de teste devem ser CPF/CNPJ sintéticos válidos, gerados pelo testador;
  a coleção não fixa números.
- Links públicos de entrega são bearer e dão acesso a nome, telefone e endereço do
  cliente. Limpe o histórico e as variáveis depois do teste.
- Não desative a validação de upload para "facilitar" o teste.
- CORS não se aplica ao Postman; uma chamada bem-sucedida aqui não prova que o
  navegador funciona.
