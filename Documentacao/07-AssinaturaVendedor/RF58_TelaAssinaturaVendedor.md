# [MVP] [Assinatura] RF58 - Tela da assinatura do vendedor

**Épico:** Assinatura do vendedor  
**Fase:** MVP  
**Perfil:** Vendedor  
**Prioridade:** Média  

## Descrição
Exibir ao vendedor os dados da assinatura e sua situação atual.

## Regras de negócio
- Mostrar:
  - plano
  - valor
  - status
  - vencimento
  - situação da última cobrança

## Critérios de aceite
- Vendedor consegue ver a assinatura.
- Situação exibida está correta.
- Tela orienta como regularizar a cobrança.

## Checklist técnico
- [ ] Endpoint de consumo já existe: `GET /api/subscriptions/my` chamando `_sellerSubscriptionStatusService.GetMySubscriptionAsync`
- [ ] Endpoint histórico: `GET /api/subscriptions/my/charges` que retorna `SellerSubscriptionChargeHistoryItemDto`.
- [ ] Criar tela Angular vinculada ao Painel de Seller
- [ ] Exibir `SellerSubscriptionBillingStatus` e `SellerSubscriptionStatus`.
- [ ] Caso a tela apresente inadimplência, fornecer instruções sobre acesso à Fatura (Billing URL do gateway).

## Dependências
- RF54 - Contratação da assinatura
- RF56 - Webhook da assinatura
- RF57 - Bloqueio por inadimplência

## Próximo card sugerido
- RF69 - Notificar vencimento da assinatura