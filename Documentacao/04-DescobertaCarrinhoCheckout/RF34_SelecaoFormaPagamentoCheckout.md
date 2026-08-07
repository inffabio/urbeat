# [MVP] [Compra] RF34 - Seleção da forma de pagamento no checkout

**Épico:** Descoberta, carrinho e checkout  
**Fase:** MVP  
**Perfil:** Cliente  
**Prioridade:** Alta  

## Descrição
Permitir escolher a forma de pagamento no fechamento do pedido.

## Regras de negócio
- Métodos iniciais:
  - PIX online
  - cartão online
  - pagar na entrega (opcional)
- O método escolhido deve ser persistido no pedido.

## Critérios de aceite
- Cliente vê os métodos disponíveis.
- Método selecionado fica salvo.
- Fluxo do pedido respeita o tipo de pagamento.

## Checklist técnico
- [ ] Mapear enum `PaymentMethod`: `PixOnline = 1`, `CardOnline = 2`, `CashOnDelivery = 3`, `CardOnDelivery = 4`
- [ ] No `CheckoutRequestDto` receber `PaymentMethod` (tipo numérico no enum ou string mapeada)
- [ ] Salvar o método na entidade `Order` sob o campo `PaymentMethod`
- [ ] Criar lógica futura para integração quando online, e passar direto se for pagamento na entrega

## Dependências
- RF33 - Checkout

## Próximo card sugerido
- RF39 - Criação do pedido
- RF47 - Integração de pagamento do pedido