# Cancelamento de pedido pelo lojista

**Data:** 2026-11-17
**Status:** implementado (sem integração financeira de reembolso)
**Escopo:** backend .NET 9 (`Domain -> Application -> Infrastructure -> WebApi`) e frontend
Angular 20 (tela `/app/pedidos`).
**Documentos relacionados:** `docs/superpowers/specs/2026-08-18-order-status-sync-design.md`,
`docs/superpowers/specs/2026-08-29-transactional-outbox-design.md`,
`docs/superpowers/specs/2026-10-24-real-time-seller-orders-design.md`,
`docs/superpowers/specs/2026-08-28-customer-order-tracking-design.md`, `AGENTS.md`.

## 1. Objetivo

Permitir que o lojista cancele um pedido **antes do preparo terminar**, com motivo obrigatório,
registro de responsável e timestamp UTC, histórico/auditoria e aviso ao cliente por notificação
persistida + SignalR — sem que o cliente ou o frontend decidam status, autor ou totais.

O backend é a autoridade: a tela apenas oferece a ação e envia o motivo. Status, autor
(`CancelledBy = Store`) e horário são derivados no servidor.

## 2. Atores e autorização

| Ator | Pode cancelar? |
| --- | --- |
| Lojista proprietário da loja do pedido (`Stores.OwnerUserId`) | Sim, em `Received`/`Preparing` |
| Outro lojista | Não (`403`) |
| Cliente | Não (endpoint exige policy de loja) |
| Administrador em contexto delegado (`AdminStoreAccessContext`) | Não nesta entrega — o serviço exige o proprietário, mesma regra de `PATCH /status` (ver §11) |

Endpoint autenticado com `[Authorize(Policy = AuthorizationPolicies.StoreAccess)]`. O vínculo com a
loja é verificado no serviço (`Order.StoreId` + `Store.OwnerUserId == sellerUserId`); o corpo da
requisição nunca informa loja, autor ou status.

## 3. Estados permitidos

| Status atual | Cancelamento pela loja |
| --- | --- |
| `Received` | Sim |
| `Preparing` | Sim |
| `PendingPayment`/`Created` | Não (`409`) |
| `Ready` | Não (`409`) |
| `OnDelivery` | Não (`409`) |
| `Delivered` | Não (`409`) |
| `Cancelled` | Replay idempotente (`200`, sem novos efeitos) |
| Pickup já retirado (`SellerCompletedAtUtc != null`) | Não (`409`) |

O `OrderStatusStateMachine` continua modelando `... -> Cancelled` para o ciclo de vida, mas o
cancelamento **pela loja** usa uma allowlist própria e mais restrita. O `PATCH /status` genérico
**não** aceita `Cancelled` (ver §4): o único caminho de cancelamento é `POST /cancel`.

## 4. Contrato HTTP

```
POST /api/orders/{orderId}/cancel
Authorization: Bearer <jwt de lojista>
Content-Type: application/json

{ "reason": "Cliente pediu para cancelar" }
```

Regras do motivo (aplicadas no validator FluentValidation **e** no serviço):

- obrigatório;
- mínimo 5 caracteres após `Trim()`;
- máximo 500 caracteres (largura da coluna);
- o motivo é persistido apenas para auditoria/suporte e **não** é ecoado ao cliente.

Respostas:

| Código | Situação | Corpo |
| --- | --- | --- |
| `200` | Cancelado agora ou replay de pedido já cancelado | `OrderDetailsResponseDto` com `status = Cancelled` |
| `400` | Motivo ausente/curto (validator) ou reprovado no serviço | `ValidationProblemDetails` / `{ "error": ... }` |
| `403` | Pedido de outra loja | vazio |
| `404` | Pedido inexistente | vazio |
| `409` | Status não permite, pickup já retirado ou conflito de concorrência | `{ "error": ... }` |

`OrderDetailsResponseDto` **não** ganhou campos de cancelamento, portanto o motivo e o autor não são
expostos ao cliente nem ao storefront/tracking.

O `PATCH /api/orders/{orderId}/status` **rejeita `Cancelled`** com `400` e
`detail = "Invalid order status transition."`, sem alterar pedido, histórico, auditoria,
notificação ou outbox. `POST /api/orders/{orderId}/cancel` é o único caminho de cancelamento.

## 5. Persistência e migração

`Order` recebeu:

| Campo | Tipo | Observação |
| --- | --- | --- |
| `CancellationReason` | `string?` (varchar 500) | motivo aparado (`Trim`) |
| `CancelledBy` | `OrderCancelledBy?` (int) | enum `Store=1`, `Customer=2`, `System=3`; apenas `Store` é produzido hoje |
| `CancelledAtUtc` | `DateTime?` (`timestamp with time zone`) | `DateTime.UtcNow` |

Migration: `20261117232021_AddOrderCancellation` (3 colunas anuláveis em `Orders`, com `Down`
removendo-as). O snapshot do modelo foi atualizado apenas com essas propriedades.

Escrita transacional no mesmo `SaveChanges` (via `IEfUnitOfWork`):

1. `Order.Status = Cancelled`, `StatusVersion += 1`, motivo/autor/UTC, `MarkAsUpdated()`;
2. `OrderStatusHistory` (`PreviousStatus`, `NewStatus = Cancelled`, `ChangedByUserId = lojista`,
   `Notes = motivo`);
3. `AuditLog` (`Event = "OrderCancelled"`, `Entity = Order`, `EntityId`, descrição com o motivo,
   `UserId` do lojista e IP);
4. `Notification` durável para o cliente (`NotificationType.OrderCancelled`, título
   "Pedido cancelado", mensagem fixa — ver §6);
5. `OutboxMessage` `OrderStatusChanged` com `Source = "Seller"` e `Sequence = StatusVersion`.

## 6. Notificação, Outbox e SignalR

Mensagem fixa ao cliente (não inclui o texto livre do motivo):

> Seu pedido foi cancelado pela loja.

- `NotificationService.NotifyCustomerOrderStatusChangedAsync` grava a linha durável no mesmo
  `SaveChanges` e deduplica por `(RecipientUserId, OrderId, Type)`.
- `OrderSignalRHandler` (outbox `OrderStatusChanged`) envia `OrderStatusUpdated` ao cliente com o
  novo status; `NotificationHandler` envia `ReceiveCustomerNotification` com a linha persistida.
- A entrega SignalR é *at-least-once*; a notificação durável e o endpoint de listagem continuam
  sendo a fonte de verdade.

O evento `OrderStatusChanged` usa o mesmo contrato dos demais fluxos; nenhum evento novo foi criado.

## 7. Idempotência e concorrência

- **Replay:** se o pedido já está `Cancelled` e pertence à loja, o serviço retorna `200` com o
  pedido atual **sem** criar segundo histórico, auditoria, notificação ou evento de outbox. Isso
  cobre duplo clique e retry de rede.
- **Concorrência:** `Order.StatusVersion` é token de concorrência. Se outra transição vencer a
  corrida, `DbUpdateConcurrencyException` desconecta as entidades rastreadas e responde `409` com
  `{ "error": "The order status changed concurrently; reload and retry." }`.
- **Frontend:** `cancellingOrderId` bloqueia um segundo envio enquanto a requisição está em voo, o
  botão de confirmação fica desabilitado e o texto muda para "Cancelando...".

## 8. Reembolso (limitação real, follow-up)

Não existe integração financeira de estorno/reembolso no repositório (Pix mock, pagamento online e
pagamento na entrega não têm API de refund). Esta entrega **não simula** reembolso e não altera
`Payment`/`PaymentStatus`: apenas cancela o pedido e avisa o cliente.

Follow-up necessário (fora do escopo):

- definir a política por método de pagamento (`PixOnline`, `CardOnline`, pagamento na entrega);
- integrar o provedor de pagamento e registrar o estorno com idempotência própria;
- refletir o reembolso no acompanhamento do cliente e no relatório financeiro da loja;
- só então permitir cancelamento após captura/pagamento conforme a política definida.

## 9. UX (seller-orders)

- Botão **X** no canto superior direito do card, somente em `Received`/`Preparing` e sem
  `sellerCompletedAtUtc`.
- `aria-label` e `title` = "Cancelar pedido {código}"; alvo `44x44px`; estilo de perigo discreto
  (transparente, vermelho suave, fundo apenas no hover/focus); anel de foco visível.
- O X fica **fora** do botão de acordeão (um `button` não pode ser aninhado em outro) e é
  posicionado de forma absoluta; reserva-se `padding-right` no cabeçalho para o texto não correr
  sob ele. Clicar no X não expande o card nem abre os detalhes.
- Modal "Cancelar pedido?" com:
  - texto explicando que o pedido será cancelado, o cliente avisado e a ação é irreversível;
  - `textarea` "Motivo do cancelamento" (mín. 5, máx. 500, `aria-invalid`/`aria-describedby`);
  - erro inline (`role="alert"`) e também toast;
  - botões **Voltar** e **Cancelar pedido** (este em estilo de perigo), ambos com 44px.
- Sucesso: o pedido deixa as colunas operacionais e aparece na coluna terminal **Concluído**,
  identificado como **Cancelado** (mesmo card, chip e rodapé "Cancelado", sem ação de status e sem
  X), toast "Pedido cancelado." e reload silencioso do quadro. Não existe seção separada
  "Cancelados": cancelados, entregues e pickups concluídos compartilham a mesma coluna.
- Erro: o card permanece na coluna original, o modal continua aberto com a mensagem e o motivo
  digitado preservado para nova tentativa.
- Realtime: o quadro carrega `Cancelled` junto com os status operacionais do dia, então uma
  atualização do servidor move o card para a coluna Concluído sem refresh; eventos
  duplicados/atrasados são descartados pela versão (`changedAtUtc`).

## 10. Critérios de aceite

- [x] X presente apenas em `Received`/`Preparing` e ausente em `Ready`, `OnDelivery`, `Delivered`,
      `Cancelled` e pickup concluído.
- [x] Motivo obrigatório com mínimo 5 caracteres no cliente e no servidor.
- [x] `POST /api/orders/{orderId}/cancel` autenticado, com verificação de dono da loja.
- [x] `PATCH /api/orders/{orderId}/status` rejeita `Cancelled` sem efeitos colaterais.
- [x] `Status = Cancelled`, responsável `Store`, motivo e `CancelledAtUtc` UTC persistidos.
- [x] Histórico, auditoria, notificação durável e outbox no mesmo commit.
- [x] Cliente recebe "Seu pedido foi cancelado pela loja." (persistida + SignalR), sem o texto do
      motivo.
- [x] Replay idempotente e conflito de concorrência tratado.
- [x] Cancelados aparecem na coluna Concluído, identificados como Cancelado e sem ação/X.
- [x] Erro não altera o card nem o estado local.
- [x] Checkout, aceite, pickup, entrega e impressão sem regressões nas suítes executadas.
- [x] Migration, build e testes aprovados.

## 11. Riscos e decisões

- **Contexto administrativo delegado:** o endpoint usa a policy de loja, mas o serviço exige
  `Store.OwnerUserId == sellerUserId` — mesma regra de `PATCH /api/orders/{id}/status`. Um admin
  delegado recebe `403`. Se o produto quiser permitir, os dois fluxos precisam ser alinhados junto
  com a auditoria do contexto (`AdminStoreContextId`).
- **PATCH genérico bloqueado:** `UpdateStatusAsync` rejeita `Cancelled` com `InvalidTransition`
  (`400`) antes do state machine. O state machine segue modelando a transição para o fluxo
  dedicado, mas nenhum endpoint genérico consegue cancelar sem motivo/ator/UTC/auditoria/outbox.
- **Motivo sensível:** fica apenas em histórico/auditoria. Nenhum DTO compartilhado com cliente o
  expõe; se o suporte precisar exibi-lo, criar um DTO específico de loja/admin.
- **Cancelados no quadro do dia:** a coluna Concluído carrega até 50 pedidos por status do período
  (entregues e cancelados); não há paginação própria porque a coluna é operacional e o histórico
  completo já existe nos relatórios.
- **Notificação deduplicada por tipo:** um segundo cancelamento legítimo no mesmo pedido é
  impossível por regra de estado; a deduplicação só protege contra replay.

## 12. Testes

Backend:

- `backend/tests/Urbeat.UnitTests/Infrastructure/Services/OrderCancellationTests.cs` — status
  aceitos/rejeitados, pickup concluído, loja indevida, pedido inexistente, motivo curto/trim,
  persistência de status/autor/UTC/histórico/auditoria/outbox/notificação, idempotência,
  concorrência e bloqueio de `Cancelled` no `UpdateStatusAsync` (sem efeitos), além da transição
  normal preservada.
- `backend/tests/Urbeat.UnitTests/Application/Validators/CancelOrderRequestDtoValidatorTests.cs` —
  motivo mínimo/máximo.
- `backend/tests/Urbeat.IntegrationTests/Api/OrderCancellationTests.cs` — endpoint `200/400/403/409`,
  persistência, notificação durável e push SignalR (`OrderStatusUpdated` +
  `ReceiveCustomerNotification`), idempotência do replay e `PATCH /status` recusando `Cancelled`
  (`400`) sem alterar pedido/histórico/auditoria/notificação/outbox.

Frontend:

- `frontend/src/app/features/seller-orders/seller-orders-page.component.spec.ts` — X apenas nos
  status permitidos, modal, validação do motivo, bloqueio de duplo envio, sucesso movendo para a
  coluna Concluído identificado como Cancelado, erro preservando o card, ausência de seção separada
  e contrato de alvo 44px.
- `frontend/src/app/features/seller-orders/seller-orders-realtime.spec.ts` — quadro recarrega os
  seis status (incluindo `Cancelled`) no reload em tempo real.
- `frontend/src/app/core/services/order.service.spec.ts` — contrato `POST .../cancel` enviando
  apenas `{ reason }`.

Comandos executados na entrega:

```
dotnet build backend/Urbeat.sln
dotnet test backend/tests/Urbeat.UnitTests --no-build
dotnet test backend/tests/Urbeat.IntegrationTests --no-build --filter "FullyQualifiedName~OrderCancellationTests|FullyQualifiedName~OrdersFlowTests|FullyQualifiedName~OrderStatusSignalRTests"
npx jest --no-coverage src/app/features/seller-orders
npx jest --no-coverage src/app/core/services/order.service.spec.ts
npx ng build --configuration production
git diff --check
```

Testes `Relational/*` dependem de Testcontainers/PostgreSQL e não foram executados neste ambiente
(Docker indisponível); eles se auto-ignoram quando o Docker não está presente.
