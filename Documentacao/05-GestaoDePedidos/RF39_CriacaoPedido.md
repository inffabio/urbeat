# [MVP] [Pedidos] RF39 - Criação do pedido

**Épico:** Gestão de pedidos  
**Fase:** MVP  
**Perfil:** Cliente  
**Prioridade:** Alta  

## Descrição
Registrar o pedido com snapshot dos itens, preços, endereço e forma de pagamento.

## Regras de negócio
- Pedido pertence a 1 cliente e 1 loja.
- Deve guardar snapshot de:
  - nome do produto
  - preço
  - quantidade
  - endereço
- Status inicial:
  - `PendingPayment` para pagamento online
  - `Received` ou `Created` para pagar na entrega

## Critérios de aceite
- Pedido é salvo corretamente.
- Snapshot é armazenado.
- Loja fechada bloqueia criação.
- Produto indisponível bloqueia criação.

## Checklist técnico
- [ ] Criar endpoint e serviço `POST /api/orders` ou `POST /api/checkout/confirm` recebendo `CheckoutRequestDto` com proteção `Authorize(Policy = AuthorizationPolicies.CustomerOnly)`
- [ ] Criar entidade `Order` vinculada ao `CustomerUserId` e `StoreId`
- [ ] Criar coleção de `OrderItem`
- [ ] Salvar um snapshot (cópia) dos campos do `CustomerAddress` direto na tabela `Order` (`AddressCep`, `AddressStreet`, etc)
- [ ] Definir o status inicial na entidade `Order` usando o enum `OrderStatus`
- [ ] Salvar `FulfillmentType` (Delivery = 1, PickUp = 2)

## Dependências
- RF31 - Carrinho
- RF32 - Endereço
- RF33 - Checkout
- RF34 - Forma de pagamento
- RF15 - Taxa e pedido mínimo

## Próximo card sugerido
- RF40 - Painel de pedidos do vendedor
- RF47 - Integração de pagamento