# [MVP] [Assinatura] RF56 - Webhook de pagamento da assinatura

**Épico:** Assinatura do vendedor  
**Fase:** MVP  
**Perfil:** Sistema  
**Prioridade:** Alta  

## Descrição
Receber eventos do Asaas para refletir pagamento, inadimplência e vencimento da assinatura.

## Decisão técnica
**Gateway:** Asaas  
**Endpoint sugerido:** `POST /api/webhooks/asaas`

## Regras de negócio
- Webhook deve atualizar o status da assinatura.
- Processamento deve ser idempotente.
- Payload bruto deve ser armazenado.

## Critérios de aceite
- Sistema recebe evento com sucesso.
- Assinatura muda de status corretamente.
- Cobrança paga ativa a assinatura.
- Cobrança vencida ou inadimplente atualiza o status corretamente.

## Checklist técnico
- [ ] Endpoint de webhook já existe protegido via Token (`GET /api/webhooks/asaas`) em `WebhooksController`.
- [ ] Disparar e lidar com `ProcessAsaasWebhookCommand(payload, ip)` via MediatR
- [ ] Validar autenticidade por Header do Asaas.
- [ ] Atualizar tabela `SellerSubscription` para ativa ou inativa com base no evento de faturamento/vencimento
- [ ] O processamento do Payload Bruto tem de suportar salvamento estrutural em tabela de Log webhook `SubscriptionWebhookEvent` ou `PaymentWebhookEvent`.

## Dependências
- RF54 - Contratação da assinatura
- RF55 - Cobrança recorrente

## Próximo card sugerido
- RF57 - Bloqueio de loja inadimplente
- RF69 - Notificação de vencimento