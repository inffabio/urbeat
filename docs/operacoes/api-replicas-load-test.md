# Operação: API com duas réplicas e carga progressiva

## Decisão arquitetural

Na VM OCI AArch64 a API HTTP roda em dois serviços Compose explícitos, sem Swarm e sem
`deploy.replicas`:

```text
Internet -> Nginx host (least_conn)
                    |-> 127.0.0.1:5001 -> urbeat-backend (HTTP :5000)
                    `-> 127.0.0.1:5002 -> urbeat-backend-2 (HTTP :5000)
                              |                 |
                              +---- PostgreSQL--+
                              +------ Redis ----+

PostgreSQL Outbox/Hangfire <- worker-business (único consumidor business)
                            <- worker-communications (único consumidor communications)
```

`backend` mantém o nome operacional existente e é a réplica 1; `backend-replica-2` é a
segunda réplica. Os containers não têm bind mount de estado da aplicação. JWT, refresh tokens,
dados de sessão e locks são compartilhados pelos stores já existentes (JWT/DB/Redis), e o
backplane Redis do SignalR permanece habilitado. Não foi introduzida afinidade por IP/cookie.

O endpoint `/health` continua executando os checks legados (incluindo Outbox); `/health/live`
é liveness sem dependências e `/health/ready` é readiness. A migração/seed relacional continua
no startup. Em PostgreSQL, cada réplica mantém a conexão aberta durante migrations e seeders e
usa o advisory lock `(0x55524254, 20261028)`, liberado no `finally` assíncrono; assim somente uma
réplica executa essa seção por vez. O lock não é usado no provider InMemory dos testes.

## Serviços, portas e responsabilidades

| Serviço | Porta interna/host | Função |
|---|---:|---|
| `backend` | 5000/127.0.0.1:5001 | API, `Hangfire:Role=Web`, Outbox desligado |
| `backend-replica-2` | 5000/127.0.0.1:5002 | API, `Hangfire:Role=Web`, Outbox desligado |
| `worker-business` | sem porta | Outbox/jobs business |
| `worker-communications` | sem porta | Outbox communications/e-mail/WhatsApp |
| Nginx host | 80/443 | TLS, SPA, API e WebSocket |

Nginx usa `least_conn`, `max_fails=3` e `fail_timeout=10s`. `/api/`, `/hubs/`, `/health` e
Swagger preservam os headers, timeouts e upgrade WebSocket existentes. A falha passiva remove
temporariamente uma réplica; a verificação definitiva é feita em cada `:5001/health` e `:5002/health`.

## Estado compartilhado e riscos auditados

- JWT é stateless; refresh tokens persistem no PostgreSQL.
- SignalR usa o backplane Redis compartilhado (`SignalR__RedisBackplaneEnabled=true`).
- Caches/tokens Redis usam a mesma instância e multiplexer por processo. A política Redis
  `allkeys-lru` não deve ser alterada: Outbox/Hangfire estão no PostgreSQL, mas eviction ainda
  pode degradar cache/backplane e precisa ser monitorado.
- Rate limiting é local a cada processo. Isso não é uma dependência de sessão nem exige sticky
  session, mas os limites efetivos do cluster são aproximadamente duas vezes os limites por API.
- Não há Data Protection/cookie de autenticação usado pelo contrato atual; não criar chaves locais.
- Logs de `backend` e `backend-2` usam diretórios distintos e o container log driver tem rotação.

## Orçamento de conexões PostgreSQL

O limite configurado é explícito por processo: `DB_MAX_POOL_SIZE=10`, `DB_MIN_POOL_SIZE=0`.
O teto conservador verificável é `4 processos × 2 pools × 10 = 80` (duas APIs e dois workers,
contando separadamente os pools potenciais de EF e Hangfire). O sink de logs é outro pool, com
`DB_LOGS_MAX_POOL_SIZE=1`, totalizando até 4; reservamos 6 conexões para observabilidade e
manutenção. O orçamento é, portanto, 90 e só é seguro se a consulta abaixo confirmar
`max_connections >= 90` (o default não é uma premissa operacional). Nunca aumente o pool sem
recalcular:

```text
app = (api_replicas + worker_processes) * pools_por_processo * max_pool
logs = (api_replicas + worker_processes) * log_pool
total = app + logs + exporters + maintenance
total <= 90
```

Com os valores desta topologia: `80 + (4 × 1) + 6 = 90`. O log pool também é por processo; por
isso seu padrão é 1. Se o banco tiver `max_connections` menor, reduza
`DB_MAX_POOL_SIZE` antes do deploy; não use `Minimum Pool Size` positivo na VM pequena.

## Deploy, smoke e rollback

1. Validar localmente: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/criarDeployOracleCloud/validate-pipeline.ps1`.

## Gates de startup e verificação

- Seeders relacionais obrigatórios executam sob advisory lock; qualquer falha é registrada e
  relançada, impedindo que uma réplica anuncie startup saudável com estado parcial. O provider
  InMemory usado nos testes mantém o caminho separado e continua sendo semeado pelo fixture.
- O deploy remoto exige `redis-cli PING` com resposta `PONG`. A verificação pública exige exatamente
  HTTP 200 em `https://api.urbeat.com.br/health` e não segue redirects.
- Workers não possuem healthcheck HTTP. O verificador aceita explicitamente ausência de healthcheck,
  mas rejeita estado parado/reiniciando, healthcheck existente que não esteja saudável e crash loop
  (`RestartCount >= 3`).
- A validação do Compose real ocorre no preflight do servidor, pois Docker não está disponível neste
  ambiente local; essa limitação não reduz os gates do script nem substitui a validação remota.
2. Fazer deploy normal pelo `deploy-all.ps1`, com `-ExpectedCommit`; não usar `-AllowDirty` em produção.
3. Confirmar `docker compose ps`, `curl http://127.0.0.1:5001/health` e `:5002/health`, depois
   `nginx -t` e `curl https://api.urbeat.com.br/health`.
4. Observar logs, `pg_stat_activity`, Redis memory/evictions, erro HTTP, SignalR e backlog Outbox.

Rollback: o passo de aplicação valida `docker compose config` sem persistir sua saída interpolada
(que poderia conter segredos) e registra o compose bruto e `docker compose images` em
`/opt/urbeat/backups/deploy-<UTC>/` antes de construir. Em caso de falha, preserve esses arquivos,
restaure manualmente a revisão anterior do compose/imagem/tag registrada, reaplique a configuração
Nginx anterior e valide `nginx -t`; não remover volumes nem executar `docker compose down -v`.
O rollback não é atômico: confirme a revisão e faça backup antes de trocar a configuração. Se uma
réplica estiver ruim, retire temporariamente apenas o backend correspondente do upstream,
recarregue Nginx e corrija a causa antes de recolocar.

## Carga k6 progressiva (somente após aprovação)

Não executar carga deste change. Quando autorizada, use um `RUN_ID` único (UTC), registre commit,
imagem, configuração de pool e métricas antes/depois, e execute 50 → 100 → 150 → 200 VUs,
aguardando estabilização entre degraus. Aprovar somente se não houver erro de contrato/5xx,
falhas totais abaixo de 1%, p95 do fluxo customer-session abaixo de 3 s, ambas as réplicas
saudáveis, CPU/memória sem OOM/throttling, pool sem saturação e Outbox sem crescimento sustentado.
Abortar se qualquer réplica ficar unhealthy, Redis apresentar evictions anormais, PostgreSQL
atingir o limite, ou SignalR perder entrega.

Toda limpeza deve ser exclusivamente por `RUN_ID`: filtrar os registros/fixtures criados pelo
run, verificar contagens antes, apagar em ordem transacional e confirmar zero correspondências.
Nunca executar `DELETE` amplo por telefone, e-mail ou janela de tempo e nunca limpar dados de
produção sem o procedimento aprovado e backup.

## Comandos de observação (server-side, sem segredos)

```powershell
ssh -p 2208 dexter@HOST 'docker compose -f /opt/urbeat/docker-compose.yml ps'
ssh -p 2208 dexter@HOST 'curl -fsS http://127.0.0.1:5001/health; curl -fsS http://127.0.0.1:5002/health'
ssh -p 2208 dexter@HOST 'docker exec urbeat-postgres sh -c '\''psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "SHOW max_connections; SELECT usename,application_name,state,count(*) FROM pg_stat_activity GROUP BY 1,2,3 ORDER BY 4 DESC;"'\'''
ssh -p 2208 dexter@HOST 'docker exec urbeat-redis redis-cli INFO memory | egrep "used_memory|maxmemory|evicted_keys"'
```

Os comandos não imprimem `.env`, connection strings, JWT ou tokens. Não executar k6, deploy OCI,
push ou alteração de pagamentos/webhooks como parte desta preparação.
