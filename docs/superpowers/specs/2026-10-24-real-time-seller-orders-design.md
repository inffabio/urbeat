# Atualização em tempo real dos pedidos do lojista

## Objetivo

Fazer com que um novo pedido criado pelo cliente apareça automaticamente na tela `/app/pedidos` do lojista, sem atualização manual do navegador.

## Diagnóstico

O frontend já possui conexão SignalR do vendedor, escuta `ReceiveSellerNotification` por meio de `SellerShellFacade` e dispara um pulso que recarrega a lista. O backend persiste a notificação e publica o evento pelo Outbox, mas os logs de produção mostram falhas repetidas em `NotificationHandler` ao realizar o push SignalR. O pedido permanece no banco, porém não chega à tela em tempo real.

## Solução

Preservar a arquitetura durável `OrderCreated -> Outbox -> NotificationHandler -> SellerNotificationHub -> ReceiveSellerNotification`. Corrigir a resolução/envio do contexto SignalR no backend, registrar a causa técnica de falhas sem expor dados sensíveis e manter o retry do Outbox. No frontend, preservar o facade como ponto único do evento e garantir que a notificação de novo pedido produza o pulso consumido pela página de pedidos.

Eventos duplicados devem ser inofensivos: a lista deve continuar sendo carregada do endpoint autoritativo e os sinais devem ser deduplicados pelo identificador da notificação/pulso.

## Escopo

- Backend: serviço de notificações, registro de SignalR e testes do handler/entrega.
- Frontend: `SignalRService`, `SellerShellFacade`, `SellerOrdersPageComponent` e testes somente se necessário.
- Não alterar modelo de pedido, checkout, contratos HTTP, persistência ou mecanismo de Outbox fora do necessário para a entrega SignalR.

## Critérios de aceite

1. Com `/app/pedidos` aberto, um pedido elegível criado pelo cliente aparece sem refresh manual.
2. O evento é entregue ao usuário vendedor correto.
3. Falhas transitórias continuam usando o retry do Outbox.
4. Reconnect do SignalR reconcilia a lista e não duplica pedidos.
5. Falhas são registradas sem tokens, senhas ou payloads sensíveis.
6. Testes backend/frontend e build de produção passam.

## Riscos e validações

- SignalR é at-least-once; o frontend deve tolerar duplicidade.
- O Outbox deve continuar sendo a fonte durável quando o vendedor estiver offline.
- Validar os fluxos de pagamento que geram `OrderCreated`/`OrderStatusChanged` e a autorização do hub.
- Confirmar que dashboard, notificações e impressão automática não perdem seus eventos existentes.
