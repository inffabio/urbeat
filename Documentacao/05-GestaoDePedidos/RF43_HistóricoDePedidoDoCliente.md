# [MVP] [Pedidos] RF43 - Histórico de pedidos do cliente

**Épico:** Gestão de pedidos  
**Fase:** MVP  
**Perfil:** Cliente  
**Prioridade:** Média  

## Descrição
Permitir ao cliente consultar pedidos anteriores.

## Regras de negócio
- Mostrar apenas pedidos do usuário autenticado.

## Critérios de aceite
- Cliente vê lista de pedidos anteriores.
- Cliente abre detalhes do pedido.

## Checklist técnico
- [ ] Criar endpoint `GET /api/orders/my-orders` protegido por `Authorize(Policy = AuthorizationPolicies.CustomerOnly)`
- [ ] Retornar array `IReadOnlyCollection<OrderSummaryResponseDto>` através de chamada para `_orderService.ListCustomerOrdersAsync`
- [ ] Criar tela de histórico
- [ ] Exibir resumo em listagem simples com data da criação, loja (`StoreId`), `Status` no front (traduzido do Enum), e `Total`

## Dependências
- RF39 - Pedido
- RF42 - Acompanhamento do pedido

## Próximo card sugerido
- Fase 2: RF79 - Avaliação da loja e do pedido