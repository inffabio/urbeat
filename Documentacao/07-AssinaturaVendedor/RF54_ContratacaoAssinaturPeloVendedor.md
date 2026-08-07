# [MVP] [Assinatura] RF54 - Contratação da assinatura pelo vendedor

**Épico:** Assinatura do vendedor  
**Fase:** MVP  
**Perfil:** Vendedor  
**Prioridade:** Alta  

## Descrição
Permitir ao vendedor contratar um plano para usar a plataforma.

## Decisão técnica
**Gateway recomendado:** Asaas

## Justificativa da escolha
- excelente para cobrança recorrente no Brasil
- API simples
- suporta PIX, boleto e cartão
- muito aderente a SaaS

## Regras de negócio
- Assinatura deve ficar vinculada à loja.
- Loja só opera com assinatura válida.
- No MVP, o vendedor escolhe um plano e confirma a cobrança.

## Critérios de aceite
- Vendedor seleciona um plano.
- Sistema cria cliente no Asaas.
- Sistema cria assinatura/cobrança.
- Status inicial da assinatura é salvo corretamente.

## Checklist técnico
- [ ] Endpoint `POST /api/subscriptions/contract` recebe `ContractSellerSubscriptionRequestDto`.
- [ ] Usar camada de aplicação `ISellerSubscriptionStatusService` invocando `AsaasService` ou gateway configurável.
- [ ] Entidades chave: `Plan`, `SellerSubscription`, salvando status no banco local com `GatewayCustomerId` e `GatewaySubscriptionId`.
- [ ] Entidades de log de cobrança recorrente: `SellerSubscriptionChargeHistory`.

## Dependências
- RF53 - Cadastro de planos
- RF09 - Cadastro da loja

## Próximo card sugerido
- RF55 - Cobrança recorrente
- RF58 - Tela da assinatura