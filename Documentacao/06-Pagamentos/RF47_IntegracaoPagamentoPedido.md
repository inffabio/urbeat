# [MVP] [Pagamentos] RF47 - Integração de pagamento do pedido

**Épico:** Pagamentos  
**Fase:** MVP  
**Perfil:** Cliente  
**Prioridade:** Alta  

## Descrição
Integrar o pagamento online do pedido do consumidor usando gateway externo.

## Decisão técnica
**Gateway recomendado:** Mercado Pago  
**Estratégia do MVP:** Checkout Pro

## Justificativa da escolha
- mais rápido para lançar
- reduz complexidade no front
- reduz preocupação com PCI
- suporta PIX e cartão
- muito aderente ao mercado brasileiro

## Regras de negócio
- Pedido online nasce com status `PendingPayment`.
- O vendedor só recebe o pedido após confirmação do pagamento.
- Se a forma de pagamento for "na entrega", o fluxo não depende do Mercado Pago.

## Critérios de aceite
- Sistema cria preferência/transação no Mercado Pago.
- Cliente consegue iniciar e concluir o pagamento.
- O pedido fica vinculado ao identificador da transação.
- O sistema consegue consultar e rastrear o estado do pagamento.

## Checklist técnico
- [ ] O backend usa CQRS/MediatR com `CreateOrderPaymentCommand`.
- [ ] Criar ou atualizar `PaymentsController` com `POST /api/payments/order`. Recebe `CreateOrderPaymentRequestDto`.
- [ ] O MediatR encaminha para o Handler correspondente, que invoca o `PaymentService` e/ou `MercadoPagoService`.
- [ ] Validar status do Pedido com `InvalidOrderState` se não tiver em `PendingPayment`.
- [ ] Lidar com `UnsupportedMethod` quando for dinheiro na entrega.
- [ ] Retornar os dados configurados para redirect ou init_point de Checkout Pro do MercadoPago em `OrderPaymentResponseDto`.

## Dependências
- RF34 - Seleção da forma de pagamento
- RF39 - Criação do pedido

## Próximo card sugerido
- RF49 - Registro do status de pagamento
- RF48 - Webhook de confirmação do pagamento do pedido

## Observações técnicas
- No MVP, prefira iniciar com Checkout Pro.
- Em fase posterior, pode evoluir para Checkout Transparente.