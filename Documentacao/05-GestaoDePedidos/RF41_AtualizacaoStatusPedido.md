# [MVP] [Pedidos] RF41 - Atualização de status do pedido

**Épico:** Gestão de pedidos  
**Fase:** MVP  
**Perfil:** Vendedor  
**Prioridade:** Alta  

## Descrição
Permitir ao vendedor alterar o status operacional do pedido.

## Regras de negócio
- Fluxo mínimo:
  - `Received`
  - `Preparing`
  - `Ready`
  - `OnDelivery`
  - `Delivered`
  - `Cancelled`
- O sistema deve validar transições.

## Critérios de aceite
- Vendedor altera status da própria loja.
- Sistema bloqueia transições inválidas.
- Histórico de mudança é registrado.
- Cliente visualiza a atualização.

## Checklist técnico
- [ ] Criar endpoint `PATCH /api/orders/{orderId}/status` no `OrdersController` protegido por `Authorize(Policy = AuthorizationPolicies.SellerOnly)`
- [ ] Receber e validar `UpdateOrderStatusRequestDto` contendo o enum numérico de Status a alterar
- [ ] Garantir validação `_updateStatusValidator`
- [ ] Implementar as transições no `_orderService.UpdateStatusAsync` validando regras (ex `Created -> Received -> Preparing -> Ready -> OnDelivery -> Delivered` e recusas de pulos em `InvalidTransition`)
- [ ] Salvar endereço IP no momento da alteração e possivelmente trilha de auditoria

## Dependências
- RF40 - Painel de pedidos
- RF39 - Pedido

## Próximo card sugerido
- RF42 - Acompanhamento do cliente
- RF68 - Notificar cliente sobre mudança