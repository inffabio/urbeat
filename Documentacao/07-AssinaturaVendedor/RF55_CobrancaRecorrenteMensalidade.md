# [MVP] [Assinatura] RF55 - Cobrança recorrente da mensalidade

**Épico:** Assinatura do vendedor  
**Fase:** MVP  
**Perfil:** Vendedor  
**Prioridade:** Alta  

## Descrição
Gerenciar a cobrança mensal da assinatura da loja.

## Decisão técnica
**Gateway recomendado:** Asaas

## Regras de negócio
- A cobrança pode ser:
  - PIX
  - boleto
  - cartão
- Vencimento deve ser visível no painel.
- Sistema deve refletir o estado real da cobrança.

## Critérios de aceite
- Sistema registra os ciclos de cobrança.
- Vendedor consegue visualizar vencimento.
- Situação da assinatura acompanha o pagamento.

## Checklist técnico
- [ ] Mapear fluxo Asaas usando classes abstratas ou chamadas HTTP para o Gateway.
- [ ] Como a estrutura já possui CQRS, criar Commands específicos (ex: `CreateSellerSubscriptionCommand`)
- [ ] Persistir na tabela `SellerSubscriptionChargeHistory` o log e IDs de faturas.
- [ ] Utilizar o background worker `SellerSubscriptionNotificationJob` já configurado no `Program.cs` com Hangfire para conferência e notificações offline (ex: `Cron.Daily`).

## Dependências
- RF54 - Contratação da assinatura

## Próximo card sugerido
- RF56 - Webhook da assinatura
- RF57 - Bloqueio de loja inadimplente