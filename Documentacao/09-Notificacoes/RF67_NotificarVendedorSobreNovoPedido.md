# [MVP] [Notificações] RF67 - Notificar vendedor sobre novo pedido

**Épico:** Notificações  
**Fase:** MVP  
**Perfil:** Vendedor  
**Prioridade:** Alta  

## Descrição
Avisar a loja quando um novo pedido entrar no sistema.

## Regras de negócio
- Para pagamento online, o ideal é notificar após confirmação do pagamento.
- Para pagar na entrega, pode notificar logo após a criação.

## Critérios de aceite
- Vendedor recebe notificação no painel.
- Notificação está vinculada ao pedido correto.
- Notificação não duplica indevidamente.

## Checklist técnico
- [ ] Construir infraestrutura local de notificação e UI de painel com base no mapeamento do Hub de SignalR já existente `Urbeat.WebApi.Hubs.SellerNotificationHub`.
- [ ] A entidade `Notification` gerencia as caixas de avisos dos `Sellers`. O endpoint `GET /api/seller/notifications` resgata o histórico.
- [ ] Construir serviço `SendEvent` invocando `.Clients.User(userId).SendAsync(...)`.

## Dependências
- RF39 - Criação do pedido
- RF48 - Webhook de pagamento
- RF40 - Painel de pedidos

## Próximo card sugerido
- RF41 - Atualização de status do pedido