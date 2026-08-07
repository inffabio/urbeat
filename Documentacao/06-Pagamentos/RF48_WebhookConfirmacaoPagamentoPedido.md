# [MVP] [Pagamentos] RF48 - Webhook de confirmação do pagamento do pedido

**Épico:** Pagamentos  
**Fase:** MVP  
**Perfil:** Sistema  
**Prioridade:** Alta  

## Descrição
Receber a confirmação oficial do Mercado Pago sobre o pagamento do pedido.

## Decisão técnica
**Gateway:** Mercado Pago  
**Endpoint sugerido:** `POST /api/webhooks/mercadopago`

## Regras de negócio
- O webhook é a confirmação confiável do pagamento.
- O sistema não deve depender apenas do redirecionamento do navegador.
- O processamento deve ser idempotente.

## Critérios de aceite
- Backend recebe o webhook com sucesso.
- Sistema consulta/confirma a transação no gateway.
- Pedido e pagamento são atualizados corretamente.
- Reprocessar o mesmo evento não causa inconsistência.

## Checklist técnico
- [ ] Endpoint `POST /api/webhooks/mercadopago` público configurado em `WebhooksController`. O payload é lido com `StreamReader`.
- [ ] MediatR despacha `ProcessMercadoPagoWebhookCommand(payload, ip)`.
- [ ] O handler `ProcessMercadoPagoWebhookCommandHandler` (ou `MercadoPagoWebhookService`) interpreta o payload, checa se a notificação é de `topic="payment"`, atualizando status.
- [ ] Atualizar status do pedido para `Received` ou `Preparing` se aprovado.
- [ ] Idempotência deve fazer parte da validação de eventos consumidos previamente.

## Dependências
- RF47 - Integração de pagamento
- RF49 - Registro do status de pagamento

## Próximo card sugerido
- RF40 - Painel de pedidos do vendedor
- RF67 - Notificação de novo pedido

## Observações técnicas
- Nunca considerar pagamento confirmado apenas pelo retorno visual do frontend.