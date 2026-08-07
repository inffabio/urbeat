# [MVP] [Compra] RF33 - Checkout do pedido

**Épico:** Descoberta, carrinho e checkout  
**Fase:** MVP  
**Perfil:** Cliente  
**Prioridade:** Alta  

## Descrição
Permitir revisar o pedido antes da confirmação, selecionando endereço, observações e pagamento.

## Regras de negócio
- Exibir:
  - itens
  - subtotal
  - taxa de entrega
  - pedido mínimo
  - total final
- Loja fechada não pode prosseguir.
- Pedido abaixo do mínimo deve ser bloqueado.

## Critérios de aceite
- Cliente vê o resumo correto.
- Sistema valida pedido mínimo.
- Sistema bloqueia loja fechada.
- Cliente seleciona endereço e observações.

## Checklist técnico
- [ ] Criar endpoint e serviço `POST /api/checkout/preview` no `CheckoutController` (pode ser AllowAnonymous, mas espera dados do carrinho)
- [ ] Construir lógica no `PreviewAsync` em `ICheckoutService` que retorne `CheckoutSummaryResponseDto`
- [ ] Validar e calcular totais: Subtotal, DeliveryFee (se aplicável), MinimumOrderValue
- [ ] Validar condições de bloqueio (StoreBlocked, StoreClosed) 
- [ ] Retornar os booleanos como: `StoreNotFound`, `AddressNotFound`, `StoreClosed`, `StoreBlocked`, `BelowMinimum`, `MinimumNotMetForPickUp` em `CheckoutResultDto`

## Dependências
- RF31 - Carrinho
- RF32 - Endereço
- RF15 - Taxa e mínimo
- RF34 - Forma de pagamento

## Próximo card sugerido
- RF39 - Pedido
- RF47 - Pagamento do pedido