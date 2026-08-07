# [MVP] [Notificações] RF69 - Notificar vendedor sobre vencimento da assinatura

**Épico:** Notificações  
**Fase:** MVP  
**Perfil:** Vendedor  
**Prioridade:** Média  

## Descrição
Avisar o vendedor sobre vencimento próximo, atraso e risco de bloqueio da assinatura.

## Regras de negócio
- Notificações mínimas:
  - próximo vencimento
  - cobrança vencida
  - loja bloqueada

## Critérios de aceite
- Vendedor recebe aviso em tempo hábil.
- Notificação mostra status e vencimento.
- Bloqueio não ocorre sem aviso prévio.

## Checklist técnico
- [ ] O `SellerSubscriptionNotificationJob` em `Urbeat.Infrastructure.Jobs` é ativado todo dia pelo Hangfire (`Cron.Daily`).
- [ ] Este Worker invoca `ISubscriptionNotificationService.ProcessSellerSubscriptionNotificationsAsync()`
- [ ] Construir envio de notificação in-app (`Notification`) e possivelmente `IEmailSender` configurado no appsettings de acordo com dias para expirar ou inadimplência detectada (Status).

## Dependências
- RF55 - Cobrança recorrente
- RF56 - Webhook da assinatura
- RF58 - Tela da assinatura

## Próximo card sugerido
- RF57 - Bloqueio da loja inadimplente