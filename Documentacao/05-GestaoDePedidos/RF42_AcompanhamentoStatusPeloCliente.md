# [MVP] [Pedidos] RF42 - Acompanhamento de status pelo cliente

**Épico:** Gestão de pedidos  
**Fase:** MVP  
**Perfil:** Cliente  
**Prioridade:** Alta  

## Descrição
Permitir ao cliente acompanhar o andamento do pedido.

## Regras de negócio
- Cliente só vê seus próprios pedidos.
- Linha do tempo simples é suficiente no MVP.

## Critérios de aceite
- Cliente visualiza status atual.
- Cliente vê as etapas do pedido.
- Atualizações aparecem corretamente.

## Checklist técnico
- [ ] Criar endpoint `GET /api/orders/my-orders/{orderId}` protegido por `Authorize(Policy = AuthorizationPolicies.CustomerOnly)`
- [ ] Construir DTO `OrderDetailsResponseDto` retornando lista de `Items`, `Status`, `CustomerAddress`, `DeliveryFee`, `Total`
- [ ] Validar ownership na camada de serviço: `_orderService.GetCustomerOrderAsync(customerUserId.Value, orderId, cancellationToken)` (impede ver pedidos de outros)
- [ ] UI de linha do tempo deve basear o display na atual configuração numérica do `OrderStatus`

## Dependências
- RF39 - Pedido
- RF41 - Atualização de status

## Próximo card sugerido
- RF43 - Histórico do cliente