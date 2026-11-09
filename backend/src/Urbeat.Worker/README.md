# Urbeat.Worker

Host genérico (.NET 9) que processa jobs do Hangfire e entregas do Outbox separado da API HTTP.

## Papéis

A configuração `Hangfire:Role` é obrigatória neste projeto e aceita somente:

| Papel | Filas consumidas | Canal de Outbox | Jobs recorrentes |
| --- | --- | --- | --- |
| `Business` | `business`, `default` | `Business` | sim (único scheduler) |
| `Communications` | `communications` | `Communications` | não |

A API usa `Hangfire:Role=Web` no deploy: mantém storage e dashboard, enfileira jobs via
`IBackgroundJobClient`, mas não inicia Hangfire Server nem processa Outbox.

Quando `Hangfire:Role` não é configurado, a API mantém o comportamento legado `All` (todas as filas,
os dois canais de Outbox e recurring jobs) para não quebrar ambientes existentes. O host Worker
nunca aceita `All`/`Web`, evitando processamento duplicado por engano.

## Execução local

Com PostgreSQL/Redis do `docker compose` local e a API rodando:

```powershell
dotnet run --project backend/src/Urbeat.Worker -- --Hangfire:Role=Business
dotnet run --project backend/src/Urbeat.Worker -- --Hangfire:Role=Communications
```

Use a mesma `ConnectionStrings:DefaultConnection` e as demais variáveis de ambiente da API
(`Email`, `WhatsApp`, `Redis`, etc.). Segredos nunca devem ser colocados no repositório.

## Filas

As filas estão centralizadas em `Urbeat.Infrastructure.Hangfire.HangfireQueues`:

- `business`: jobs de manutenção/importação, heartbeat e notificações de assinatura;
- `communications`: envio de e-mail de confirmação e redefinição de senha;
- `default`: compatibilidade com jobs antigos e jobs sem classificação explícita. O worker de
  negócio também consome esta fila.

## Outbox por canal

O dispatcher não percorre mais as mensagens diretamente. Cada mensagem é distribuída em entregas
independentes na tabela `OutboxHandlerDeliveries` (criada pela migration
`AddOutboxHandlerDeliveries`; o startup da API aplica a migration, nunca use
`dotnet ef database update` em produção).

Cada entrega tem `Id`, `OutboxMessageId`, `HandlerKey`, `Channel`, `Status`, `AttemptCount`,
`AvailableAtUtc`, `LockedUntilUtc`, `ProcessedAtUtc`, `LastError` e `CreatedAtUtc`, com constraint
única `(OutboxMessageId, HandlerKey)`. A classificação dos handlers é centralizada em
`OutboxHandlerClassifier`:

| Handler | Channel | HandlerKey |
| --- | --- | --- |
| `NotificationHandler` | `Business` | `business.notification` |
| `OrderSignalRHandler` | `Business` | `business.order-signalr` |
| `OrderAcceptedForPrintingSignalRHandler` | `Business` | `business.order-accepted-for-printing-signalr` |
| `PrintHandler` | `Business` | `business.print` |
| `EmailHandler` | `Communications` | `communications.email` |

O canal `Communications` está preparado para um futuro handler de WhatsApp
(`communications.whatsapp`), mas nenhum fluxo de negócio publica esse evento hoje.

Regras de processamento:

- o fan-out é idempotente e roda no primeiro dispatch antes do claim; mensagens existentes em
  `Pending` recebem as entregas sem alterar payload ou status. Mensagens legadas em `Processed` ou
  `Failed` são preservadas e não são reenviadas; `Processing` só é retomada depois que o lock legado
  expira;
- o claim usa `FOR UPDATE SKIP LOCKED` no PostgreSQL, com `LockedUntilUtc` para reaquisição após
  expiração e lote limitado por `Outbox:BatchSize`;
- a ordenação é por `AggregateId`/`Sequence` dentro de cada canal. Eventos com `Sequence = 0` são
  tratados como sem ordenação e podem ser processados imediatamente;
- uma falha de e-mail não altera as entregas irmãs de SignalR/impressão e vice-versa. O status da
  mensagem (`OutboxMessages`) é apenas uma projeção para compatibilidade operacional; as entregas
  são a fonte autoritativa.

O endpoint administrativo `GET /api/outbox/summary` mantém os totais de mensagens e adiciona
`deliveries.business` e `deliveries.communications` com `pending`, `processing`, `processed` e
`failed`. `POST /api/outbox/{id}/retry` reenfileira somente as entregas em `Failed`, preservando as
que já foram processadas. O health check `/health` reporta backlog/falhas por entrega e por canal,
sem expor payload. Os contadores são agregados em duas consultas e reutilizados por uma janela curta
(`Outbox:HealthCheckCacheDuration`, padrão 5s) para que rajadas de probes não multipliquem consultas;
o heartbeat do worker continua sendo lido a cada chamada. `/health/ready` expõe as mesmas
verificações de dependência e `/health/live` responde sem consultar PostgreSQL/Redis.

## Jobs recorrentes

Somente o papel `Business` (ou o modo legado `All`) chama
`HangfireRecurringJobs.TryRegisterBusinessJobs`. Os IDs são fixos e o `AddOrUpdate` do Hangfire é
idempotente, portanto reinícios ou mais de uma instância de negócio não criam definições duplicadas.

## Limitações conhecidas

- O health check `/health` da API não observa a liveness do Outbox remoto (o heartbeat é em memória
  por processo). Ele valida backlog/falhas e não falha por ausência de heartbeat local quando o
  papel é `Web`.
- Adicionar um novo handler para um tipo de evento já publicado não cria entregas para mensagens que
  já passaram pelo fan-out (elas já possuem ao menos uma entrega). Mensagens sem nenhuma entrega são
  expandidas automaticamente; para o novo handler em eventos já distribuídos é necessário um replay
  operacional explícito, nunca reenvio automático.
