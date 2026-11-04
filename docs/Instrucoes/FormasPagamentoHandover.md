# Formas de pagamento do storefront e confirmação de recebimento

Status: implementado em 2026-11-03.

## Decisão de produto

O storefront deixou de oferecer **Pix online** para novos pedidos. O checkout exige uma escolha
explícita entre quatro formas pagas no recebimento/retirada:

| Escolha | Enum | Valor persistido |
| --- | --- | --- |
| Pix na maquininha | `PixOnDelivery` | 5 |
| Dinheiro | `CashOnDelivery` | 3 |
| Débito | `DebitOnDelivery` | 6 |
| Crédito | `CreditOnDelivery` | 7 |

Os valores antigos **não foram renumerados**. `PixOnline = 1`, `CardOnline = 2` e
`CardOnDelivery = 4` continuam legíveis para pedidos históricos e transações Pix em andamento.
Novos pedidos com qualquer um desses três métodos são rejeitados pelo backend
(`CheckoutService` + `OrderPaymentPolicy`), não apenas escondidos na interface.

A maquininha é tratada como pagamento presencial. Urbeat **não** coleta identificador de terminal,
comprovante de adquirente, dados bancários, nem afirma verificação bancária. O lojista e o motoboy
apenas registram a forma escolhida e, no dinheiro, os valores efetivamente recebidos.

## Dinheiro: troco no checkout e no recebimento

Campos aditivos em `Orders`:

- `CashWithoutChange` (`bool?`): `true` = valor exato, `false` = precisa de troco.
- `CashChangeForAmount` (`numeric(10,2)?`): valor para o qual o cliente quer troco.
- `CashReceivedAmount` (`numeric(10,2)?`): valor efetivamente recebido pelo motoboy.
- `CashChangeAmount` (`numeric(10,2)?`): troco calculado pelo servidor.
- `PaymentConfirmedAtUtc` / `PaymentConfirmedByDriverId`: confirmação manual e auditoria.

Regras autoritativas (`OrderPaymentPolicy`, espelhadas no FluentValidation):

1. Pedido em dinheiro exige `CashWithoutChange` informado.
2. `CashWithoutChange = true` não pode enviar `CashChangeForAmount`.
3. `CashWithoutChange = false` exige `CashChangeForAmount >= total autoritativo`.
4. Valores usam no máximo duas casas decimais e limite de segurança de R$ 100.000,00.
5. Métodos não-dinheiro não podem enviar campos de troco.
6. No recebimento, o motoboy informa `CashReceivedAmount`:
   - precisa ser `>= total`;
   - o valor previsto no checkout (`CashChangeForAmount`) é apenas uma previsão do cliente e **não**
     um teto: qualquer valor efetivo válido acima do total é aceito, inclusive quando o cliente
     escolheu “sem troco”;
   - o troco é sempre `recebido - total`, calculado no servidor.

O frontend apenas antecipa a validação para UX. O servidor revalida com o total recalculado por
preços, frete e descontos autoritativos.

## Total aceito no preview (`ExpectedTotal`)

O request de checkout ganhou o campo aditivo `ExpectedTotal` (nullable). O frontend sempre envia o
total autoritativo exibido no preview. No `confirm`, o servidor compara esse valor com o total
recalculado **antes de qualquer persistência, outbox, impressão ou notificação**:

- divergência → `409` com `error`, `total` e `summary` autoritativos; nenhum pedido é criado;
- o frontend atualiza o total exibido, mantém o carrinho e exige um novo clique de confirmação,
  sem criar duplicata;
- igualdade → segue o fluxo normal.

Compatibilidade explícita: clientes antigos que não enviam `ExpectedTotal` continuam aceitos e o
servidor persiste o total autoritativo recalculado. Eles não têm a proteção de “aceite do valor
exibido”, mas também não são quebrados; o frontend atualizado sempre envia o campo.

## Confirmação manual antes do PIN

O motoboy **não possui conta, senha, sessão ou token de entregador**. A operação acontece
exclusivamente pelos links públicos bearer de rota/entrega. Pedidos criados sob o contrato atual
exigem que o motoboy confirme manualmente o recebimento e a forma de pagamento **antes** do PIN de
4 dígitos:

- Link público de rota/entrega individual: o pedido de PIN envia `handoverConfirmed: true` (e
  `cashReceivedAmount` no dinheiro). O servidor persiste a confirmação antes de verificar o PIN. A
  interface exige checkbox explícito e não envia o campo sem ação do motoboy. Não existe endpoint
  autenticado de handover nem fluxo alternativo.

Invariantes preservados:

- A confirmação só é aceita pelo token público válido da rota/entrega, com vínculo ativo entre a
  loja, o motoboy operacional e o pedido.
- `ConfirmDeliveryPinAsync` retorna `HandoverRequired` enquanto `PaymentConfirmedAtUtc` estiver
  nulo, **sem consumir tentativa de PIN**. O contador e o bloqueio de 5 tentativas continuam iguais.
- Repetir a confirmação é idempotente: não reescreve valores, horário nem auditoria.
- Timestamps são UTC.
- Auditoria: `OrderPaymentHandoverConfirmed` (e os eventos já existentes de PIN/entrega).
- O PIN continua armazenado como hash e nunca é retornado ao lojista/motoboy.
- Em links públicos, a confirmação de recebimento e o PIN válido são persistidos **atomicamente**:
  PIN incorreto ou bloqueado atualiza apenas o contador de tentativas e nunca marca o pagamento,
  não grava valores de dinheiro e não cria auditoria de handover.
- Pedidos novos com entrega não podem ser concluídos por caminho alternativo: a confirmação do
  cliente (`/delivery-confirmation`) e a conclusão do lojista exigem handover/pagamento **e** PIN.
  Se o lojista marcar `Delivered` antes, o pedido continua pendente até o motoboy concluir pelo link
  público. Pedidos legados e retiradas mantêm o fluxo anterior.

## Compatibilidade explícita

`OrderPaymentPolicy.RequiresHandoverConfirmation` só exige a nova confirmação quando:

- o método é `PixOnDelivery`, `DebitOnDelivery` ou `CreditOnDelivery`; ou
- é `CashOnDelivery` **e** o pedido tem a escolha de troco (`CashWithoutChange` não nulo).

Consequências deliberadas:

- Pedidos antigos em `CashOnDelivery` sem escolha de troco, `CardOnDelivery`, `PixOnline` e
  `CardOnline` mantêm o fluxo anterior de PIN. Entregas em andamento não ficam presas.
- Pix já criado continua funcionando: links, `PaymentService`, webhooks, worker Mock Pix, retries e
  histórico de pagamento não foram alterados. O bloqueio é somente na criação de novos pedidos.
- Assinaturas, Asaas, terceiros e rotas de entrega existentes não foram alterados fora dos campos
  aditivos de confirmação.

### Clientes em cache (PWA e Capacitor)

O storefront entregue antes desta mudança não conhece `ExpectedTotal` nem `CashWithoutChange`. Como
o service worker (`ngsw-config.json`, `installMode: prefetch`) e o webview do Capacitor podem manter
o bundle antigo por um tempo, dois caminhos foram tratados explicitamente:

- **Dinheiro sem escolha de troco** (payload legado): `OrderPaymentPolicy.IsLegacyCashCheckout`
  reconhece a ausência simultânea de `ExpectedTotal` e `CashWithoutChange` e o checkout continua
  aceito. Nenhuma decisão é inventada: `Orders.CashWithoutChange` e `CashChangeForAmount` ficam
  nulos, `RequiresHandoverConfirmation` continua falso e o pedido segue o fluxo anterior de PIN.
  Um cliente novo que já envia `ExpectedTotal` mas omite a escolha continua recebendo erro, porque
  esse payload não é legado — é inconsistente.
- **Pix online** (bundle antigo): continua **rejeitado** para novos pedidos e nenhum pedido é
  persistido. A mensagem é acionável e não expõe código interno: "Esta forma de pagamento não está
  mais disponível. Atualize o aplicativo ou recarregue a página e escolha Pix na maquininha,
  dinheiro, débito ou crédito."

O log `CHECKOUT_LEGACY_CASH` registra cada checkout aceito pelo caminho legado; use-o para medir
quando a base em cache terminou de atualizar e o caminho pode ser removido.

## Retirada (pickup)

Retirada continua sem PIN e sem motoboy:

1. lojista avança `Received -> Preparing -> Ready`;
2. lojista marca `Delivered` no balcão;
3. lojista conclui o pedido (`/complete`).

Não foi criado nenhum PIN de retirada nem atribuição de entregador. Limitação documentada: a
retirada continua baseada na operação declarada pelo lojista; não há comprovante físico ou PIN de
retirada neste escopo.

## Migration

`20261103152905_AddOrderPaymentHandoverAndCashChange` é puramente aditiva:

- `Orders.CashWithoutChange` (`boolean null`)
- `Orders.CashChangeForAmount` (`numeric(10,2) null`)
- `Orders.CashReceivedAmount` (`numeric(10,2) null`)
- `Orders.CashChangeAmount` (`numeric(10,2) null`)
- `Orders.PaymentConfirmedAtUtc` (`timestamptz null`)
- `Orders.PaymentConfirmedByDriverId` (`uuid null`)
- `OrderDeliveryAssignments.HandoverConfirmedAtUtc` (`timestamptz null`)

Não há backfill nem alteração destrutiva. Pedidos existentes permanecem com campos nulos e fluxo
legado. O startup aplica a migration; não usar `dotnet ef database update` em banco de deploy.

## Contratos e superfícies atualizadas

Backend:

- `PaymentMethod` (novos valores 5/6/7 sem renumerar).
- `CheckoutRequestDto`, `CheckoutSummaryResponseDto`, `CheckoutResultDto`.
- `OrderDetailsResponseDto`, `OrderSummaryResponseDto`, `SellerDeliverySummaryResponseDto`.
- `ConfirmDeliveryPinRequestDto` (`handoverConfirmed`, `cashReceivedAmount`).
- `PublicDeliveryRouteStopDto` e `PublicDeliveryRoutePinResultDto`.

Frontend:

- checkout `/checkout/pagamento`: quatro opções, escolha obrigatória e troco em dinheiro; o total
  exibido e o limite de troco vêm do preview autoritativo, e o checkout fica bloqueado enquanto o
  preview estiver pendente ou falhar; se a confirmação devolver outro total, o cliente é avisado;
- `/checkout/entrega` (legado) redireciona para `/checkout/pagamento`;
- card de pedido do lojista: forma, troco, valores recebidos e confirmação;
- link público de rota/entrega em `/rota/:token` e `/entrega/:token`: forma, troco e confirmação
  explícita no diálogo do PIN. É o único fluxo operacional do motoboy;
- lojista em `/app/entregadores`: cadastro operacional do motoboy por nome/telefone e atribuição de
  pedidos/rotas, sem convite, senha ou conta de entregador;
- rastreio, dashboard, listas e impressão: rótulos dos novos métodos (compatíveis com os antigos).

## Ausência de autenticação do motoboy

- Não existe `POST /api/auth/login/driver`, papel/policy `DeliveryDriverOnly`, `DriverController`,
  rotas `/api/driver/*` nem telas `/entregador/login`, `/entregador` ou `/entregador/convite/:token`.
- O lojista continua cadastrando e atribuindo o motoboy operacional (nome/telefone) e criando
  rotas/links. O motoboy acessa a entrega somente com o link público válido, mais PIN/handover
  quando aplicável.
- Links inválidos, expirados ou revogados continuam retornando os status existentes (`404`/`410`) e
  nunca expõem dados do pedido.
- Nenhum bearer de entregador é necessário, emitido ou documentado; o token do link é o único
  segredo e não deve ser impresso em logs ou documentação.

## Limitações conhecidas

- O link público confirma recebimento e PIN na mesma requisição (com `handoverConfirmed`
  obrigatório). Não existe um endpoint público separado de handover; isso evita bypass silencioso
  sem ampliar a superfície anônima.
- A confirmação de recebimento é operacional. Ela não prova liquidação financeira, nem substitui
  comprovante da maquininha.
- Pedidos legados não recebem retroativamente a confirmação; o fluxo antigo é preservado.
- A retirada não possui PIN nem comprovação além do fluxo já existente.

## Validação executada

- `dotnet build backend/Urbeat.sln`
- `dotnet test backend/tests/Urbeat.UnitTests` (931 testes)
- `dotnet test backend/tests/Urbeat.IntegrationTests` (261 testes)
- `npx jest --no-coverage` no `frontend/` (109 suítes / 1884 testes)
- `npx ng build --configuration production`
- `powershell -NoProfile -File scripts/postman/Test-PostmanCollection.ps1`
- `powershell -NoProfile -File scripts/postman/Test-PostmanCoverage.ps1`
