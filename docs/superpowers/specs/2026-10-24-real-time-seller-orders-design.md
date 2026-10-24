# Atualização em tempo real dos pedidos

## Objetivo

Fazer com que novos pedidos apareçam automaticamente na tela `/app/pedidos` do lojista e que mudanças de status apareçam automaticamente na tela de acompanhamento do cliente, sem atualização manual do navegador.

## Diagnóstico

O frontend já possui conexão SignalR do vendedor, escuta `ReceiveSellerNotification` por meio de `SellerShellFacade` e dispara um pulso que recarrega a lista. A tela do cliente também registra `OrderStatusUpdated` em `CustomerOrderTrackingService` e aplica o status imediatamente antes de atualizar os detalhes. O backend persiste as notificações e publica os eventos pelo Outbox, mas os logs de produção mostram falhas repetidas em `NotificationHandler` ao realizar o push SignalR. O pedido permanece no banco, porém as telas não recebem a atualização em tempo real.

## Solução

Preservar a arquitetura durável `OrderCreated/OrderStatusChanged -> Outbox -> NotificationHandler -> Hub`. Corrigir a resolução/envio dos contextos SignalR de vendedor e cliente, registrar a causa técnica de falhas sem expor dados sensíveis e manter o retry do Outbox. No frontend, preservar o facade do vendedor e o `CustomerOrderTrackingService` como pontos centrais, garantindo que novos pedidos produzam o pulso da página de pedidos e que `OrderStatusUpdated` atualize imediatamente o status e os detalhes visíveis ao cliente.

Eventos duplicados devem ser inofensivos: a lista deve continuar sendo carregada do endpoint autoritativo e os sinais devem ser deduplicados pelo identificador da notificação/pulso.

## Escopo

- Backend: serviço de notificações, registro de SignalR e testes dos handlers/entrega para vendedor e cliente.
- Frontend: `SignalRService`, `SellerShellFacade`, `SellerOrdersPageComponent`, `CustomerOrderTrackingService`, `TrackingPageComponent` e testes somente se necessário.
- Não alterar modelo de pedido, checkout, contratos HTTP, persistência ou mecanismo de Outbox fora do necessário para a entrega SignalR.

## Critérios de aceite

1. Com `/app/pedidos` aberto, um pedido elegível criado pelo cliente aparece sem refresh manual.
2. Com o acompanhamento do pedido aberto, uma mudança de status aparece no cliente sem refresh manual.
3. Os eventos são entregues ao usuário vendedor ou cliente correto.
4. Falhas transitórias continuam usando o retry do Outbox.
5. Reconnect do SignalR reconcilia as telas e não duplica pedidos ou transições.
6. Falhas são registradas sem tokens, senhas ou payloads sensíveis.
7. Testes backend/frontend e build de produção passam.

## Riscos e validações

- SignalR é at-least-once; o frontend deve tolerar duplicidade.
- O Outbox deve continuar sendo a fonte durável quando o vendedor estiver offline.
- Validar os fluxos de pagamento que geram `OrderCreated`/`OrderStatusChanged` e a autorização do hub.
- Confirmar que dashboard, notificações e impressão automática não perdem seus eventos existentes.
