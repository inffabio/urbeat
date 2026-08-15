# Urbeat — Deploy & Operacoes

> **Quem esta aqui pela 1a vez:** leia da secao "Layout no servidor" em diante. Tudo esta em **`/opt/urbeat/`** no host `136.248.115.135`.

---

## Infra atual

**Servidor:** Oracle Cloud Infrastructure `136.248.115.135` (aarch64, Ubuntu 24.04, Docker)
**Dominio:** `urbeat.com.br` (registro.br)
**SSL:** Let's Encrypt, renovacao automatica a cada 12h via certbot sidecar
**Stack:** 9 containers definidos em `/opt/urbeat/docker/docker-compose.yml`

| Container | Imagem | Porta host | Funcao |
|---|---|---|---|
| `urbeat_nginx` | nginx:1.27-alpine | 80, 443 | gateway HTTPS + SPA + proxy `/api` e `/hubs` |
| `urbeat_webapi` | urbeat/webapi:latest (build local) | 5000→8080 | .NET 9 + EF Core + Hangfire + SignalR |
| `urbeat_db` | postgres:16-alpine | 5432 | `UrbeatDb` + `UrbeatLogs` + schema hangfire |
| `urbeat_redis` | redis:7-alpine | 6379 | cache |
| `urbeat_certbot` | certbot/certbot | — | renova SSL automaticamente (loop 12h) |
| `urbeat_prometheus` | prom/prometheus | 9090 | scrape de metricas |
| `urbeat_grafana` | grafana/grafana | 3000 | dashboards |
| `urbeat_nodeexporter` | prom/node-exporter | 9100 | metricas do host |
| `urbeat_postgres_exporter` | postgres-exporter | 9187 | metricas do Postgres |

---

## Layout no servidor

```
/opt/urbeat/
├── backend/                    # codigo .NET 9 (espelho do repo local)
│   └── src/
│       ├── Urbeat.Domain/
│       ├── Urbeat.Application/
│       ├── Urbeat.Infrastructure/   ← migrations EF Core aqui
│       └── Urbeat.WebApi/           ← Dockerfile multi-stage aqui
├── frontend/                   # codigo Angular 20 + Dockerfile
├── docker/                     # ⭐ ARQUIVOS DE INFRA (compose, configs)
│   ├── docker-compose.yml      ← orquestracao principal
│   ├── .env                    ← SEGREDOS (nao versionar!)
│   ├── .env.example            ← template
│   ├── init-db/
│   │   └── init.sql            ← cria UrbeatLogs + extensoes
│   ├── nginx/
│   │   ├── nginx.conf          ← config global do nginx
│   │   └── conf.d/
│   │       ├── 00-shared.conf  ← upstream webapi:8080
│   │       ├── 10-http.conf    ← redirect 80→443 + ACME challenge
│   │       └── 20-https.conf   ← vhost HTTPS urbeat.com.br
│   ├── prometheus/
│   │   ├── prometheus.yml
│   │   └── urbeat_custom_metrics.prom
│   └── grafana-urbeat-dashboard.json
└── scripts/
    └── criarDeployOracleCloud/  ← deploy automatizado (Windows)
```

### Volumes Docker (dados persistentes)

| Volume | Conteudo |
|---|---|
| `urbeat_pgdata` | Postgres `UrbeatDb` + `UrbeatLogs` + schema hangfire |
| `urbeat_redis-data` | Redis AOF |
| `urbeat_letsencrypt` | Certificados SSL |
| `urbeat_certbot-webroot` | ACME challenge |
| `urbeat_frontend-assets` | Build do Angular |
| `urbeat_grafana-data` | Dashboards e config Grafana |
| `urbeat_prometheus-data` | TSDB do Prometheus |

---

## Endpoints publicos

| Recurso | URL |
|---|---|
| Site | https://urbeat.com.br |
| Lojas demo | /burguer_do_rafa · /pizza_do_rafa · /sushi_rafa |
| Cadastro vendedor | /cadastro |
| Configurar loja | /configurar-loja |
| Confirmacao email | /confirmacao-email · /confirmar-email |
| API | https://api.urbeat.com.br |
| Health | https://api.urbeat.com.br/health |
| Swagger | https://api.urbeat.com.br/swagger |
| Hangfire | https://api.urbeat.com.br/hangfire (Basic Auth — ver `.env`) |
| Grafana | http://136.248.115.135:3000 (SSH tunnel) |
| Prometheus | http://136.248.115.135:9090 (SSH tunnel) |

---

## Dados seed (DemoDataSeeder)

3 lojas × 10 produtos cada, criadas automaticamente no 1o startup se `Stores` estiver vazia.

| Loja | StorePath | Categorias |
|---|---|---|
| Burguer do Rafa | `burguer_do_rafa` | Hamburgueres (4), Porcoes (3), Bebidas (3) |
| Pizza do Rafa | `pizza_do_rafa` | Pizzas Salgadas (6), Pizzas Doces (2), Bebidas (2) |
| Sushi Rafa | `sushi_rafa` | Combinados (3), Sushis e Sashimis (4), Bebidas (3) |

**Sellers de teste:** `rafa@burguer.com` · `rafa@pizza.com` · `rafa@sushi.com` (senha `Teste1234`)
**Customers de teste:** `joao@cliente.com` · `maria@cliente.com` · `carlos@cliente.com` (senha `Teste1234`)
**Admin:** ver `.env` do servidor (`ADMIN_EMAIL` / `ADMIN_PASSWORD`).

---

## Comandos do dia-a-dia

**Importante:** todos os comandos `docker compose ...` devem rodar **com `sudo` dentro de `/opt/urbeat/docker/`** no servidor.

### Status / logs
```bash
ssh ubuntu@136.248.115.135
cd /opt/urbeat/docker

sudo docker compose ps                              # status de todos
sudo docker compose ps --format "table {{.Service}}\t{{.Status}}"  # formato curto

sudo docker compose logs -f webapi                  # tail backend
sudo docker compose logs -f nginx                   # tail nginx
sudo docker compose logs --since=10m                # ultimos 10min de tudo
sudo docker logs urbeat_webapi --tail 100           # alternativa direta
```

### Restart de um servico
```bash
sudo docker compose restart webapi
sudo docker compose restart nginx
sudo docker compose restart db redis
```

### Recriar 1 container (apos edicao de `.env` ou rebuild de imagem)
```bash
sudo docker compose up -d --force-recreate webapi
```

### Atualizar o codigo a partir da maquina local
```powershell
# Na maquina Windows, do repo root
Set-Location -LiteralPath "scripts\criarDeployOracleCloud"
.\deploy-all.ps1 -Step application -ServerIP "136.248.115.135" -SSHUser "ubuntu"
```

### Acessar Postgres direto
```bash
sudo docker compose exec db psql -U postgres -d UrbeatDb

# dentro do psql:
\dt                                            # listar tabelas
\d "Stores"                                    # descrever uma tabela
SELECT "Name", "StorePath" FROM "Stores";
SELECT COUNT(*) FROM "Products";
```

### Acessar Redis
```bash
sudo docker compose exec redis redis-cli
KEYS *
```

### Verificar o certificado SSL
```bash
ssh ubuntu@136.248.115.135 'echo | openssl s_client -servername urbeat.com.br -connect urbeat.com.br:443 2>/dev/null | openssl x509 -noout -subject -issuer -dates -ext subjectAltName'
```

---

## Backup do Postgres

### Backup manual (recomendado antes de migrations/destrutivas)
```bash
ssh -p 2208 dexter@136.248.115.135
cd /opt/urbeat
mkdir -p backups
TS=$(date +%Y%m%d_%H%M%S)
sudo docker compose --env-file .env -f docker-compose.yml exec -T postgres sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB"' \
  | gzip > backups/UrbeatDb_${TS}.sql.gz
sudo docker compose --env-file .env -f docker-compose.yml exec -T postgres sh -c 'pg_dump -U "$POSTGRES_USER" -d UrbeatLogs' \
  | gzip > backups/UrbeatLogs_${TS}.sql.gz
ls -lh backups/
```

## Recriar o banco usando migrations

O banco OCI atual e o ambiente remoto usado como producao neste momento. O WebApi aplica automaticamente as migrations versionadas no startup (`backend/src/Urbeat.WebApi/Program.cs`). Nao use `dotnet ef database update` no servidor.

O procedimento automatizado esta em `scripts/criarDeployOracleCloud/08-rebuild-database.ps1` e sempre cria backup antes de qualquer alteracao:

```powershell
Set-Location -LiteralPath "scripts\criarDeployOracleCloud"

# Somente backup e verificacao, sem recriar o banco
./08-rebuild-database.ps1 -SSHUser dexter -SSHPort 2208 -SSHKeyPath "$env:USERPROFILE\.ssh\id_ed25519"

# Recriacao destrutiva, somente apos validar os arquivos de backup
./08-rebuild-database.ps1 -ResetDatabase -ConfirmReset "RESET URBEAT DATABASE" `
  -SSHUser dexter -SSHPort 2208 -SSHKeyPath "$env:USERPROFILE\.ssh\id_ed25519"
```

Regras:

- A recriacao nao altera, apaga ou rotaciona secrets do OCI Vault.
- O script salva backups em `/opt/urbeat/backups/database-rebuild-<timestamp>/`.
- O reset recria somente o banco da aplicacao; depois reinicia o backend.
- O startup do backend aplica o schema pela cadeia de migrations commitada e executa os seeders previstos.
- O banco de logs e apenas salvo em backup quando existir; nao e apagado automaticamente.
- Depois da migracao, valide `/health`, logs de migration e o total de tabelas antes de importar bairros.

Enquanto este servidor continuar sendo o ambiente de producao, qualquer reset exige backup validado. Quando ele virar desenvolvimento, mantenha o mesmo procedimento e crie um servidor/banco separado para a producao real.

### Estado da reconstrução em 2026-08-14

- Backup validado em `/opt/urbeat/backups/database-rebuild-20260814_130826/urbeatdb.sql.gz`.
- Banco da aplicação recriado e WebApi saudável após o startup.
- `__EFMigrationsHistory`: 58 migrations aplicadas.
- `Cities`: 0 registros após o reset.
- `DeliveryNeighborhoods`: 0 registros após o reset.
- A base de bairros de referência está no banco PostgreSQL de produção. Antes de um reset, exporte os estados existentes para snapshots CSV e versione-os em `backend/scripts/import/snapshots/bairros_<uf>.csv`; a reconstrução deve restaurar esses CSVs sem consultar a API externa. O CSV pode conter bairros sem geolocalização, mantendo `Latitude` e `Longitude` vazios; valide o total, os geolocalizados e os pendentes. Coordenadas são aproximadas pela primeira rua/CEP encontrada, com e-DNE/CEP antes de fontes reais como Nominatim, nunca por centroide municipal. A restauração via CSV preserva vazios e nunca inventa coordenadas.

### Exportar bairros do banco de produção

O exportador não consulta Brasil Aberto nem outro serviço externo: ele lê a tabela `DeliveryNeighborhoods` e gera um snapshot por UF. Execute em uma máquina com Python, `psycopg2` e acesso ao PostgreSQL de produção. Nunca coloque a senha no repositório ou na documentação.

```bash
cd /opt/urbeat/backend/scripts/import
export URBEAT_DB_HOST=localhost
export URBEAT_DB_PORT=5432
export URBEAT_DB_NAME=UrbeatDb
export URBEAT_DB_USER=postgres
export URBEAT_DB_PASSWORD='use-a-senha-do-ambiente-sem-registrar-em-arquivo-versionado'

for uf in AC AL AP AM BA CE DF ES GO MA MT MS MG PA PB PR PE PI RJ RN RS RO RR SC SP SE TO; do
  python3 neighborhood_snapshot.py export --uf "$uf" --file "snapshots/bairros_${uf,,}.csv"
done
```

Os CSVs gerados devem ser copiados para `backend/scripts/import/snapshots/` no repositório e revisados antes de qualquer reset. Em 2026-08-14, todas as 27 UFs foram importadas na produção e exportadas: 56.580 bairros no total, sendo 53.015 geolocalizados e 3.565 pendentes sem par completo de coordenadas. Estados sem registros produzem um snapshot vazio e devem ser confirmados antes de versionar. Depois do reset, restaure somente os arquivos disponíveis e valide a contagem de `Cities` e `DeliveryNeighborhoods`.

### Restore
```bash
gunzip -c backups/UrbeatDb_20260528_180000.sql.gz \
  | sudo docker compose --env-file /opt/urbeat/.env -f /opt/urbeat/docker-compose.yml exec -T postgres sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB"'
```

---

## SSL — gestao do certificado

### Renovacao manual
```bash
cd /opt/urbeat/docker
sudo docker compose run --rm --entrypoint "" certbot \
  certbot renew --webroot -w /var/www/certbot
sudo docker compose exec nginx nginx -s reload
```

> Renovacao **automatica** ja roda a cada 12h dentro do container `urbeat_certbot`.

### Emitir certificado inicial (se precisar recriar)
```bash
cd /opt/urbeat/docker
sudo docker compose run --rm --entrypoint "" certbot \
  certbot certonly --webroot -w /var/www/certbot \
  --email admin@urbeat.com.br --agree-tos --no-eff-email \
  -d urbeat.com.br -d www.urbeat.com.br -d api.urbeat.com.br
sudo docker compose exec nginx nginx -s reload
```

> Importante: se for recriar o cert, o nginx precisa estar rodando em HTTP (sem HTTPS). Desabilite temporariamente `20-https.conf` antes.

### Inspecionar
```bash
ssh ubuntu@136.248.115.135 'sudo ls -la /var/lib/docker/volumes/urbeat_letsencrypt/_data/live/urbeat.com.br/'
ssh ubuntu@136.248.115.135 'sudo docker compose -f /opt/urbeat/docker/docker-compose.yml run --rm --entrypoint "" certbot certbot certificates'
```

---

## Rotacionar segredos (`.env`)

```bash
ssh ubuntu@136.248.115.135
cd /opt/urbeat/docker
sudo nano .env                                  # editar valores
sudo docker compose up -d --force-recreate webapi   # aplicar
```

**Impacto:**
- `JWT_SECRET` → invalida todos os tokens JWT ativos (usuarios precisam logar de novo)
- `ENCRYPTION_KEY` → invalida campos criptografados
- `POSTGRES_PASSWORD` → exige restart do container `db` E atualizar a string no `.env`
- `HANGFIRE_PASSWORD` → muda so o basic auth do `/hangfire`
- `ADMIN_PASSWORD` → **NAO altera** o admin ja criado no banco; so vale na 1a seed

Geracao de segredos novos:
```bash
openssl rand -hex 32      # JWT_SECRET, ENCRYPTION_KEY
openssl rand -hex 12      # HANGFIRE_PASSWORD
```

---

### .env — CUIDADO

- **Apos qualquer reset, restore ou redeploy, verifique se `/opt/urbeat/docker/.env` existe.** Sem ele, Docker Compose usa defaults vazios: SMTP desligado (`LogOnly=true`), JWT fraco, emails nao enviados.
- **Template seguro:** `sudo cp /opt/urbeat/docker/.env.production /opt/urbeat/docker/.env` restaura as configs reais (SMTP OCI, Cloudinary, secrets).

- **Secrets nunca versionados** — `.env` esta no `.gitignore`. O `.env.production` no servidor e a unica copia autoritativa.

### Email (OCI Email Delivery)

- **Remetente:** `contato@urbeat.com.br`
- **Servico:** OCI Email Delivery via SMTP (MailKit `SmtpEmailService` em `Urbeat.Infrastructure/Services/Email/`)
- Porta 587 com STARTTLS (MailKit usa `SecureSocketOptions.StartTls` quando `UseStartTls=true`)
- Credenciais SMTP armazenadas no **OCI Vault** (`urbeat-vault`, regiao `sa-saopaulo-1`)
- Script de deploy `03-setup-environment.ps1` busca os secrets do vault e gera o `.env`

| Config | Valor |
|---|---|
| `EMAIL_LOGONLY` | `false` |
| `SMTP_HOST` | `smtp.email.sa-saopaulo-1.oci.oraclecloud.com` |
| `SMTP_PORT` | `587` (STARTTLS) |
| `SMTP_USESTARTTLS` | `true` |
| `SMTP_USER` | OCI SMTP username (do vault: `URBEAT_SMTP_USER`) |
| `SMTP_PASS` | OCI SMTP password (do vault: `URBEAT_SMTP_PASSWORD`) |
| `SMTP_FROM` | `contato@urbeat.com.br` |

Mapeamento vault → `.env` em `scripts/criarDeployOracleCloud/configs/secrets-map.json`:

| Secret Vault | Variavel .env |
|---|---|
| `URBEAT_SMTP_HOST` | `SMTP_HOST` |
| `URBEAT_SMTP_PORT` | `SMTP_PORT` |
| `URBEAT_SMTP_USER` | `SMTP_USER` |
| `URBEAT_SMTP_PASSWORD` | `SMTP_PASS` |
| `URBEAT_SMTP_SSL` | convertido para `SMTP_USESTARTTLS` |
| `URBEAT_SMTP_FROM` | `SMTP_FROM` |

### Gerar/atualizar credenciais SMTP

Se o envio falhar com `535 Authentication credentials invalid`:

```bash
# 1. OCI Console → Email Delivery → urbeat.com.br → SMTP Credentials → Generate
# 2. Copiar Username e Password gerados
# 3. Atualizar no vault (via OCI Console ou CLI):
oci vault secret update-base64 --secret-id <ocid> --secret-bundle-content "{""content"":""<base64-da-credencial>""}"
# 4. Rodar deploy para gerar novo .env a partir do vault:
cd scripts/criarDeployOracleCloud
./deploy-all.ps1 -Step environment -ServerIP "136.248.115.135" -SSHUser "ubuntu"
# 5. Recriar webapi:
ssh ubuntu@136.248.115.135 'sudo docker compose -f /opt/urbeat/docker/docker-compose.yml up -d --force-recreate webapi'
```

### Testar envio de email

Sempre teste enviando para `intfabio@gmail.com`:

```bash
# 1. Verificar configuracao atual
ssh ubuntu@136.248.115.135 'sudo docker exec urbeat_webapi printenv | grep -iE "SMTP|EMAIL_LOGONLY"'

# 2. Registrar usuario de teste e disparar email de confirmacao
ssh ubuntu@136.248.115.135 'printf '"'"'{"fullName":"Teste","email":"intfabio@gmail.com","password":"Teste1234","phoneNumber":"11999999999"}'"'"' > /tmp/reg.json && curl -sk -X POST https://localhost/api/auth/register/customer -H "Content-Type: application/json" -d @/tmp/reg.json'

# 3. Verificar logs (deve mostrar EMAIL_SENT)
ssh ubuntu@136.248.115.135 'sudo docker logs urbeat_webapi --tail 20 2>&1 | grep -iE "EMAIL|email"'

# 4. Se mostrar EMAIL_LOG_ONLY, o .env esta ausente ou EMAIL_LOGONLY=true
# 5. Se mostrar EMAIL_FAILED com 535, as credenciais SMTP do vault estao invalidas — regerar no OCI Console
```

### Todas as variaveis (`/opt/urbeat/docker/.env`)

```
POSTGRES_PASSWORD=postgres
JWT_SECRET=…                  # 64 hex chars
ADMIN_EMAIL=admin@urbeat.local
ADMIN_PASSWORD=…              # senha do admin (so no 1o seed)
HANGFIRE_USER=admin
HANGFIRE_PASSWORD=…           # 24 hex chars
ENCRYPTION_KEY=…              # 64 hex chars
FRONTEND_BASE_URL=https://urbeat.com.br
LETSENCRYPT_EMAIL=admin@urbeat.com.br
GRAFANA_USER=admin
GRAFANA_PASSWORD=admin        # TROCAR!
SMTP_HOST=smtp.email.sa-saopaulo-1.oci.oraclecloud.com
SMTP_PORT=587
SMTP_USER=<oci-smtp-username>
SMTP_PASS=<oci-smtp-password>
SMTP_FROM=contato@urbeat.com.br
EMAIL_LOGONLY=false
```

---

## DNS (registro.br)

Configurar em **registro.br → Painel → DNS → Editar Zona**:

| Tipo | Nome | Valor |
|---|---|---|
| A | `@` (ou vazio) | 136.248.115.135 |
| A | `www` | 136.248.115.135 |
| A | `api` | 136.248.115.135 |

Verificar propagacao:
```bash
dig +short urbeat.com.br @1.1.1.1
dig +short www.urbeat.com.br @1.1.1.1
dig +short api.urbeat.com.br @1.1.1.1
```

---

## Troubleshooting

### "relation X does not exist" (HTTP 500 nas APIs)
Migration EF Core nao rodou no startup. Diagnostico:
```bash
ssh ubuntu@136.248.115.135
sudo docker logs urbeat_webapi 2>&1 | grep -iE "PendingModel|migration|seed|FATAL" | head -20
```
Se aparecer `PendingModelChangesWarning`, ha mudanca no modelo sem migration. Solucao: gerar nova migration localmente (`dotnet ef migrations add NomeDaMudanca` no `backend/`), commitar, redeploy.

Se for so migration nao aplicada:
```bash
cd /opt/urbeat/docker
sudo docker compose up -d --force-recreate webapi
```

### Certbot nao renova
```bash
sudo docker logs urbeat_certbot --tail 100
# Forcar tentativa imediata:
sudo docker compose run --rm --entrypoint "" certbot \
  certbot renew --webroot -w /var/www/certbot --force-renewal
sudo docker compose exec nginx nginx -s reload
```

### Nginx erro 502 Bad Gateway
```bash
sudo docker compose ps                             # webapi deve estar healthy
sudo docker compose logs --tail 50 webapi
sudo docker compose exec nginx wget -qO- http://webapi:8080/health
```

### Frontend 404 em rotas como `/burguer_do_rafa`
```bash
sudo docker compose exec nginx grep -nA2 try_files /etc/nginx/conf.d/20-https.conf
```
Deve mostrar `try_files $uri $uri/ /index.html;`.

### Porta 80 ocupada (nginx do host)
O Ubuntu pode vir com nginx pre-instalado ocupando a porta 80:
```bash
sudo systemctl stop nginx && sudo systemctl disable nginx
sudo docker compose restart nginx
```

### Reset completo do banco (⚠️ destrutivo)
```bash
cd /opt/urbeat/docker
sudo docker compose down -v                        # apaga TODOS os volumes!
sudo docker compose up -d --build                 # recria do zero (seed roda de novo)
# ⚠️ APOS O RESET, RESTAURE O .ENV:
sudo cp /opt/urbeat/docker/.env.production /opt/urbeat/docker/.env
sudo docker compose up -d --force-recreate webapi  # recriar webapi com as configs corretas
```

### .env ausente ou resetado
Se o `.env` foi perdido (reset, restore, redeploy), os defaults do Docker Compose assumem:
- `EMAIL_LOGONLY=true` → emails nao sao enviados
- `SMTP_HOST=""` → servidor SMTP vazio
- `SMTP_USER=""`, `SMTP_PASS=""` → sem autenticacao
- `JWT_SECRET` fraco → tokens inseguros

```bash
# Diagnosticar
ssh ubuntu@136.248.115.135
sudo docker exec urbeat_webapi printenv | grep -iE 'EMAIL|SMTP|JWT_SECRET'

# Corrigir
sudo cp /opt/urbeat/docker/.env.production /opt/urbeat/docker/.env
sudo docker compose -f /opt/urbeat/docker/docker-compose.yml up -d --force-recreate webapi
```

### Emails nao estao sendo enviados
Verificar configuracao:
```bash
ssh ubuntu@136.248.115.135
# 1. Checar se .env existe e tem as variaveis
sudo cat /opt/urbeat/docker/.env | grep -iE 'SMTP|EMAIL_LOGONLY'

# 2. Checar o que o container recebeu
sudo docker exec urbeat_webapi printenv | grep -iE 'EMAIL|SMTP'

# 3. Checar logs de erro de email
sudo docker logs urbeat_webapi 2>&1 | grep -iE 'email|smtp|mail'
```
**Valores esperados:** `Email__LogOnly=false`, `Email__Smtp__Host` configurado (OCI SMTP), `Email__Smtp__Port=587`.

### Limpar imagens antigas (libera disco)
```bash
sudo docker image prune -a -f
sudo docker builder prune -a -f
```

### Espaco em disco
```bash
df -h /
sudo docker system df
```

---

## Acessos rapidos

- **SSH:** `ssh ubuntu@136.248.115.135`
- **App dir:** `/opt/urbeat/docker/`
- **Site:** https://urbeat.com.br
- **Swagger:** https://api.urbeat.com.br/swagger
- **Hangfire:** https://api.urbeat.com.br/hangfire — `admin / <ver .env>`
- **Grafana:** http://136.248.115.135:3000 (SSH tunnel)
- **Prometheus:** http://136.248.115.135:9090 (SSH tunnel)
