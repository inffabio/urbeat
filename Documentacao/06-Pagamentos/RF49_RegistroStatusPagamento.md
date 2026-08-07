# [MVP] [Pagamentos] RF49 - Registro do status de pagamento

**Épico:** Pagamentos  
**Fase:** MVP  
**Perfil:** Sistema  
**Prioridade:** Alta  

## Descrição
Persistir os dados do pagamento do pedido e seus estados internos.

## Regras de negócio
- Deve armazenar:
  - gateway
  - transaction id
  - valor
  - método
  - status
  - payload bruto
- Status internos recomendados:
  - `Pending`
  - `Paid`
  - `Failed`
  - `Cancelled`
  - `Refunded`

## Critérios de aceite
- Pagamento fica salvo com identificador do gateway.
- Status do pagamento é mapeado corretamente.
- Pedido e pagamento permanecem consistentes.

## Checklist técnico
- [ ] Criar entidade `Payment` (contem `OrderId`, `GatewayTransactionId`, `GatewayCheckoutUrl`, `ExternalReference`, `Amount`, `RawPayload`)
- [ ] O enum `PaymentStatus` deve conter: `Pending = 1`, `Paid = 2`, `Failed = 3`, `Cancelled = 4`, `Refunded = 5`
- [ ] O enum `PaymentGateway` diferencia gateways 
- [ ] Ao aprovar ou falhar via SDK/Webhook, atualizar a tabela de `Payment` (estado atual) bem como gerar uma entrada na entidade `PaymentStatusHistory`
- [ ] Criar endpoint `GET /api/payments/order/{orderId}` para exibir estado do pagamento. Retorna `OrderPaymentResponseDto`. 
- [ ] Criar endpoint `GET /api/payments/order/{orderId}/history` para auditar a mudança na UI, retornando coleções de `PaymentStatusHistoryResponseDto`.

## Dependências
- RF47 - Integração de pagamento
- RF48 - Webhook de confirmação

## Próximo card sugerido
- RF40 - Painel de pedidos
- RF42 - Acompanhamento do cliente