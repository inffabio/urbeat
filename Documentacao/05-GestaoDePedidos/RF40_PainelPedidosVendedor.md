# [MVP] [Pedidos] RF40 - Painel de pedidos do vendedor

**Épico:** Gestão de pedidos  
**Fase:** MVP  
**Perfil:** Vendedor  
**Prioridade:** Alta  

## Descrição
Exibir os pedidos da loja para operação do dia a dia.

## Regras de negócio
- Vendedor vê apenas pedidos da própria loja.
- Pedidos novos devem ficar destacados.

## Critérios de aceite
- Vendedor visualiza pedidos em andamento.
- Vendedor abre detalhes do pedido.
- Lista atualiza corretamente.

## Checklist técnico
- [ ] Criar endpoint `GET /api/orders/store` (recebe query `StoreOrdersHistoryQueryDto`, retorna `PagedOrderSummaryResponseDto`) com proteção `Authorize(Policy = AuthorizationPolicies.SellerOnly)` no `OrdersController`
- [ ] Filtrar pedidos pela loja baseada no `SellerUserId` (`_orderService.ListStoreOrdersAsync`)
- [ ] Incluir endpoint `GET /api/orders/store/report` para resumo financeiro (RF10 e RF40 convergentes)
- [ ] Criar tela de gerenciamento de pedidos
- [ ] Implementar polling ou websockets/SignalR na UI para novos pedidos

## Dependências
- RF39 - Criação do pedido
- RF06 - Permissões

## Próximo card sugerido
- RF41 - Atualização de status
- RF67 - Notificar vendedor sobre novo pedido