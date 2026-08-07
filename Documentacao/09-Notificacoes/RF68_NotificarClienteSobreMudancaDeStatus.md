# [MVP] [Notificações] RF68 - Notificar cliente sobre mudança de status

**Épico:** Notificações  
**Fase:** MVP  
**Perfil:** Cliente  
**Prioridade:** Alta  

## Descrição
Informar o cliente quando o status do pedido mudar.

## Regras de negócio
- Eventos relevantes:
  - pedido recebido
  - preparando
  - pronto
  - saiu para entrega
  - entregue
  - cancelado

## Critérios de aceite
- Cada alteração gera uma notificação.
- Cliente visualiza aviso no sistema.
- Notificação corresponde ao pedido correto.

## Checklist técnico
- [ ] Construir serviço que salva as instâncias na tabela `Notification` para acesso posterior.
- [ ] Consumir do endpoint `GET /api/customer/notifications`.
- [ ] No front deve exibir uma central de notificação e emitir alerta sonoro usando websockets.
- [ ] Conectar Customer ao SignalR Hub `Urbeat.WebApi.Hubs.CustomerNotificationHub`.

## Dependências
- RF41 - Atualização de status
- RF42 - Acompanhamento do cliente

## Próximo card sugerido
- Fase 2: RF70 - Push notification