<#
.SYNOPSIS
    Deploys the Urbeat application using Docker Compose
.DESCRIPTION
    Uploads docker-compose.yml and all configuration files,
    then starts all containers for the Urbeat application stack.
#>

param(
    [Parameter(Mandatory=$false)]
    [string]$ServerIP = "136.248.115.135",

    [Parameter(Mandatory=$false)]
    [string]$SSHUser = "ubuntu",

    [Parameter(Mandatory=$false)]
    [string]$SSHKeyPath = "~/.ssh/id_ed25519",

    [Parameter(Mandatory=$false)]
    [string]$AppDir = "/opt/urbeat"
)

Write-Host "🚀 Deploying Urbeat Application Stack..." -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

# Resolve SSH key path
$resolvedKeyPath = (Resolve-Path $SSHKeyPath -ErrorAction SilentlyContinue).Path
if (-not $resolvedKeyPath) {
    $resolvedKeyPath = Resolve-Path "$env:USERPROFILE\.ssh\id_rsa" -ErrorAction SilentlyContinue
}

# ─────────────────────────────────────────
# Generate docker-compose.yml
# ─────────────────────────────────────────

$dockerCompose = @'
# ═══════════════════════════════════════════════════════════
# URBEAT APPLICATION - DOCKER COMPOSE
# Architecture: ARM64 (aarch64)
# Platform: linux/arm64
# ═══════════════════════════════════════════════════════════

version: '3.9'

networks:
  urbeat-network:
    driver: bridge

volumes:
  postgres-data:
    driver: local
  redis-data:
    driver: local
  grafana-data:
    driver: local
  prometheus-data:
    driver: local

services:

  # ─── PostgreSQL Database ─────────────────────────────────
  postgres:
    image: postgres:16-alpine
    platform: linux/arm64
    container_name: urbeat-postgres
    restart: unless-stopped
    env_file:
      - .env
    environment:
      POSTGRES_USER: ${POSTGRES_USER}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
      POSTGRES_DB: ${POSTGRES_DB}
      PGDATA: /var/lib/postgresql/data/pgdata
    volumes:
      - postgres-data:/var/lib/postgresql/data
      - ./configs/postgres/init.sql:/docker-entrypoint-initdb.d/init.sql:ro
    networks:
      - urbeat-network
    ports:
      - "127.0.0.1:5432:5432"
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U ${POSTGRES_USER} -d ${POSTGRES_DB}"]
      interval: 10s
      timeout: 5s
      retries: 5
      start_period: 30s
    logging:
      driver: "json-file"
      options:
        max-size: "50m"
        max-file: "3"

  # ─── Redis Cache ──────────────────────────────────────────
  redis:
    image: redis:7-alpine
    platform: linux/arm64
    container_name: urbeat-redis
    restart: unless-stopped
    command: ["redis-server", "--appendonly", "yes", "--maxmemory", "256mb", "--maxmemory-policy", "allkeys-lru"]
    volumes:
      - redis-data:/data
    networks:
      - urbeat-network
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 5s
      timeout: 3s
      retries: 5
      start_period: 5s
    logging:
      driver: "json-file"
      options:
        max-size: "50m"
        max-file: "3"

  # ─── .NET 9 Backend API ───────────────────────────────────
  backend:
    build:
      context: ./backend
      dockerfile: src/Urbeat.WebApi/Dockerfile
    platform: linux/arm64
    container_name: urbeat-backend
    restart: unless-stopped
    env_file:
      - .env
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ASPNETCORE_URLS: http://+:5000
      ConnectionStrings__DefaultConnection: ${URBEAT_DB_CONNECTION}
      Redis__ConnectionString: "redis:6379"
      Email__LogOnly: "false"
      Email__Smtp__Host: ${SMTP_HOST}
      Email__Smtp__Port: ${SMTP_PORT}
      Email__Smtp__Username: ${SMTP_USER}
      Email__Smtp__Password: ${SMTP_PASSWORD}
      Email__Smtp__UseStartTls: ${SMTP_SSL}
      Email__FromAddress: ${SMTP_FROM}
      Email__FromName: "Urbeat"
      Jwt__Secret: ${JWT_SECRET}
      Jwt__Issuer: ${JWT_ISSUER}
      Jwt__Audience: ${JWT_AUDIENCE}
      Jwt__ExpiryHours: ${JWT_EXPIRY_HOURS}
      GOOGLE_PLACES_API_KEY: ${GOOGLE_PLACES_API_KEY}
      App__FrontendUrl: ${FRONTEND_URL}
      App__ApiUrl: ${API_URL}
      App__CorsOrigins: ${CORS_ORIGINS}
    ports:
      - "127.0.0.1:5000:5000"
    depends_on:
      postgres:
        condition: service_healthy
      redis:
        condition: service_healthy
    networks:
      - urbeat-network
    volumes:
      - ./logs/backend:/app/logs
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:5000/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 60s
    logging:
      driver: "json-file"
      options:
        max-size: "100m"
        max-file: "5"

  # ─── Angular/Ionic Frontend (Build Only) ──────────────────
  frontend:
    build:
      context: ./frontend
      dockerfile: Dockerfile
    platform: linux/arm64
    container_name: urbeat-frontend-build
    restart: "no"
    volumes:
      - ./frontend-dist:/shared

  # ─── Prometheus Monitoring ────────────────────────────────
  prometheus:
    image: prom/prometheus:latest
    platform: linux/arm64
    container_name: urbeat-prometheus
    restart: unless-stopped
    command:
      - '--config.file=/etc/prometheus/prometheus.yml'
      - '--storage.tsdb.path=/prometheus'
      - '--storage.tsdb.retention.time=30d'
      - '--web.console.libraries=/usr/share/prometheus/console_libraries'
      - '--web.console.templates=/usr/share/prometheus/consoles'
      - '--web.enable-lifecycle'
    volumes:
      - ./configs/prometheus/prometheus.yml:/etc/prometheus/prometheus.yml:ro
      - prometheus-data:/prometheus
    ports:
      - "127.0.0.1:9090:9090"
    networks:
      - urbeat-network
    healthcheck:
      test: ["CMD", "wget", "--quiet", "--tries=1", "--spider", "http://localhost:9090/-/healthy"]
      interval: 30s
      timeout: 10s
      retries: 3
    logging:
      driver: "json-file"
      options:
        max-size: "50m"
        max-file: "3"

  # ─── Grafana Dashboard ────────────────────────────────────
  grafana:
    image: grafana/grafana:latest
    platform: linux/arm64
    container_name: urbeat-grafana
    restart: unless-stopped
    env_file:
      - .env
    environment:
      GF_SECURITY_ADMIN_USER: ${GF_SECURITY_ADMIN_USER}
      GF_SECURITY_ADMIN_PASSWORD: ${GF_SECURITY_ADMIN_PASSWORD}
      GF_SERVER_ROOT_URL: http://localhost:3000
      GF_INSTALL_PLUGINS: grafana-clock-panel,grafana-simple-json-datasource
      GF_USERS_ALLOW_SIGN_UP: "false"
    volumes:
      - grafana-data:/var/lib/grafana
      - ./configs/grafana/provisioning:/etc/grafana/provisioning:ro
    ports:
      - "127.0.0.1:3000:3000"
    depends_on:
      - prometheus
    networks:
      - urbeat-network
    healthcheck:
      test: ["CMD", "wget", "--quiet", "--tries=1", "--spider", "http://localhost:3000/api/health"]
      interval: 30s
      timeout: 10s
      retries: 3
    logging:
      driver: "json-file"
      options:
        max-size: "50m"
        max-file: "3"
'@

# ─────────────────────────────────────────
# Generate Prometheus config
# ─────────────────────────────────────────

$prometheusConfig = @'
# ═══════════════════════════════════════════════════════════
# PROMETHEUS CONFIGURATION - URBEAT
# ═══════════════════════════════════════════════════════════

global:
  scrape_interval: 15s
  evaluation_interval: 15s
  external_labels:
    monitor: 'urbeat-monitor'
    environment: 'production'

alerting:
  alertmanagers:
    - static_configs:
        - targets: []

rule_files: []

scrape_configs:
  - job_name: 'prometheus'
    static_configs:
      - targets: ['localhost:9090']

  - job_name: 'urbeat-backend'
    metrics_path: '/metrics'
    static_configs:
      - targets: ['backend:5000']
    scrape_interval: 15s

  - job_name: 'urbeat-frontend'
    static_configs:
      - targets: ['frontend:80']

  - job_name: 'postgres'
    static_configs:
      - targets: ['postgres:5432']

  - job_name: 'node-exporter'
    static_configs:
      - targets: ['host.docker.internal:9100']
'@

# ─────────────────────────────────────────
# Generate Grafana datasource config
# ─────────────────────────────────────────

$grafanaDatasource = @'
apiVersion: 1

datasources:
  - name: Prometheus
    type: prometheus
    access: proxy
    url: http://prometheus:9090
    isDefault: true
    editable: false
    jsonData:
      timeInterval: "15s"
'@

# ─────────────────────────────────────────
# Generate PostgreSQL init script
# ─────────────────────────────────────────

$postgresInit = @'
-- ═══════════════════════════════════════════════════════════
-- URBEAT DATABASE INITIALIZATION
-- ═══════════════════════════════════════════════════════════

-- Create extensions
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS "pg_trgm";
CREATE EXTENSION IF NOT EXISTS "unaccent";

-- Set timezone
SET timezone = 'America/Sao_Paulo';

-- Create schema
CREATE SCHEMA IF NOT EXISTS urbeat;

-- Grant permissions
GRANT ALL PRIVILEGES ON DATABASE urbeatdb TO "urbeatPostg";
GRANT ALL PRIVILEGES ON SCHEMA urbeat TO "urbeatPostg";
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA urbeat TO "urbeatPostg";
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA urbeat TO "urbeatPostg";

-- Set default search path
ALTER USER "urbeatPostg" SET search_path TO urbeat, public;

-- Log initialization
DO $$
BEGIN
    RAISE NOTICE 'Urbeat database initialized successfully at %', NOW();
END $$;
'@

# ─────────────────────────────────────────
# Upload all configuration files
# ─────────────────────────────────────────

Write-Host "`n📤 Uploading configuration files..." -ForegroundColor Yellow

function Upload-FileToServer {
    param(
        [string]$Content,
        [string]$RemotePath,
        [string]$FileName
    )

    $tempFile = [System.IO.Path]::GetTempFileName()
    # Convert CRLF to LF and write as UTF-8 without BOM for Linux compatibility
    $cleanContent = $Content -replace "`r`n", "`n"
    [System.IO.File]::WriteAllText($tempFile, $cleanContent, [System.Text.UTF8Encoding]::new($false))

    Write-Host "  📄 Uploading: $FileName" -ForegroundColor White -NoNewline

    $sshOpts = @("-F", "NUL", "-i", $resolvedKeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes", "-o", "ConnectTimeout=180", "-o", "GSSAPIAuthentication=no")
    scp @sshOpts $tempFile "${SSHUser}@${ServerIP}:/tmp/$FileName" | Out-Null

    if ($LASTEXITCODE -eq 0) {
        ssh @sshOpts "${SSHUser}@${ServerIP}" "sudo mv /tmp/$FileName $RemotePath/$FileName && sudo chown ubuntu:ubuntu $RemotePath/$FileName"
        Write-Host " ✅" -ForegroundColor Green
    } else {
        Write-Host " ❌" -ForegroundColor Red
    }

    Remove-Item $tempFile -Force
}

# Create postgres config directory
$sshOpts = @("-F", "NUL", "-i", $resolvedKeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes", "-o", "ConnectTimeout=180", "-o", "GSSAPIAuthentication=no")
ssh @sshOpts "${SSHUser}@${ServerIP}" "sudo mkdir -p $AppDir/configs/postgres && sudo chown -R ubuntu:ubuntu $AppDir/configs"

# Upload files
Upload-FileToServer -Content $dockerCompose -RemotePath $AppDir -FileName "docker-compose.yml"
Upload-FileToServer -Content $prometheusConfig -RemotePath "$AppDir/configs/prometheus" -FileName "prometheus.yml"
Upload-FileToServer -Content $grafanaDatasource -RemotePath "$AppDir/configs/grafana/provisioning/datasources" -FileName "datasources.yml"
Upload-FileToServer -Content $postgresInit -RemotePath "$AppDir/configs/postgres" -FileName "init.sql"

# ─────────────────────────────────────────
# Upload Source Code for Local Build
# ─────────────────────────────────────────

Write-Host "`n📦 Preparing and uploading source code for local build..." -ForegroundColor Yellow

$projectRoot = "C:\Projetos\urbeat"
$backendDir = Join-Path $projectRoot "backend"
$frontendDir = Join-Path $projectRoot "frontend"

$backendTar = [System.IO.Path]::GetTempFileName() + ".tar.gz"
$frontendTar = [System.IO.Path]::GetTempFileName() + ".tar.gz"

# Compress directories using tar (more reliable than zip on Linux)
Write-Host "  🗜️  Compressing backend..." -ForegroundColor White
& tar -czf $backendTar `
    --exclude="backend/**/bin" `
    --exclude="backend/**/obj" `
    --exclude="backend/**/TestResults" `
    --exclude="backend/**/*.md" `
    --exclude="backend/**/*.txt" `
    --exclude="backend/**/*.pdf" `
    --exclude="backend/**/*.doc" `
    --exclude="backend/**/*.docx" `
    -C $projectRoot "backend"

Write-Host "  🗜️  Compressing frontend..." -ForegroundColor White
& tar -czf $frontendTar `
    --exclude="frontend/node_modules" `
    --exclude="frontend/.angular" `
    --exclude="frontend/dist" `
    --exclude="frontend/coverage" `
    --exclude="frontend/**/*.md" `
    --exclude="frontend/**/*.txt" `
    --exclude="frontend/**/*.pdf" `
    --exclude="frontend/**/*.doc" `
    --exclude="frontend/**/*.docx" `
    -C $projectRoot "frontend"

# Upload tars using scp (with strict timeouts to prevent Windows hangs)
Write-Host "  📤 Uploading backend source (this may take a minute)..." -ForegroundColor White
$sshOpts = @("-F", "NUL", "-i", $resolvedKeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes", "-o", "ConnectTimeout=180", "-o", "GSSAPIAuthentication=no")
scp @sshOpts $backendTar "${SSHUser}@${ServerIP}:/tmp/backend.tar.gz" | Out-Null

Write-Host "  📤 Uploading frontend source (this may take a minute)..." -ForegroundColor White
scp @sshOpts $frontendTar "${SSHUser}@${ServerIP}:/tmp/frontend.tar.gz" | Out-Null

# Extract on server
Write-Host "  📂 Extracting source code on server..." -ForegroundColor White
ssh @sshOpts "${SSHUser}@${ServerIP}" "
    sudo rm -rf $AppDir/backend $AppDir/frontend
    sudo mkdir -p $AppDir/backend $AppDir/frontend
    sudo tar -xzf /tmp/backend.tar.gz -C $AppDir
    sudo tar -xzf /tmp/frontend.tar.gz -C $AppDir
    sudo chown -R ubuntu:ubuntu $AppDir/backend $AppDir/frontend
    rm -f /tmp/backend.tar.gz /tmp/frontend.tar.gz
    echo '✅ Source code extracted successfully'
"

Remove-Item $backendTar -Force
Remove-Item $frontendTar -Force

# ─────────────────────────────────────────
# Deploy application
# ─────────────────────────────────────────

Write-Host "`n🚀 Starting Docker Compose deployment..." -ForegroundColor Yellow

$deployScript = @"
#!/bin/bash
set -e

echo "🚀 Starting Urbeat deployment..."
cd $AppDir

echo "🔨 Building Docker images (aarch64)..."
docker compose build --no-cache

echo "🔄 Starting services..."
docker compose up -d --remove-orphans

echo "⏳ Waiting for backend to be healthy..."
for i in `$(seq 1 60); do
  STATUS=`$(curl -s -o /dev/null -w '%{http_code}' http://localhost:5000/health 2>/dev/null || echo '000')
  if [ "`$STATUS" = "200" ]; then
    echo "   Backend healthy (HTTP 200)"
    break
  fi
  echo "   tentativa `$i/60 (HTTP `$STATUS)"
  sleep 3
done

echo "🧹 Limpando cache Redis..."
docker compose exec -T redis redis-cli FLUSHALL 2>/dev/null && echo "   Cache Redis limpo." || echo "   Redis indisponivel, pulando..."

echo "🔄 Executando migrations (via health check)..."
curl -s -o /dev/null -w '   Migrations HTTP %{http_code}\n' http://localhost:5000/health || echo "   Backend ainda iniciando migrations..."

echo "📧 Verificando servidor de email..."
SMTP_HOST=`$(grep -oP '^SMTP_HOST=\K.*' .env 2>/dev/null | head -1)
SMTP_PORT=`$(grep -oP '^SMTP_PORT=\K.*' .env 2>/dev/null | head -1)
SMTP_HOST=`${SMTP_HOST:-smtp.gmail.com}
SMTP_PORT=`${SMTP_PORT:-587}
if timeout 5 bash -c "echo > /dev/tcp/`$SMTP_HOST/`$SMTP_PORT" 2>/dev/null; then
  echo "   Servidor SMTP `$SMTP_HOST:`$SMTP_PORT acessivel."
else
  echo "   ⚠️  ATENCAO: Servidor SMTP `$SMTP_HOST:`$SMTP_PORT NAO acessivel! Emails nao serao enviados."
fi

echo "📊 Checking service status..."
docker compose ps

echo "🔍 Checking logs for errors..."
docker compose logs --tail=20

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "✅ Urbeat deployment completed!"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
"@

$tempDeploy = [System.IO.Path]::GetTempFileName() + ".sh"
# Convert CRLF to LF and write as UTF-8 without BOM for Linux bash compatibility
$cleanScript = $deployScript -replace "`r`n", "`n"
[System.IO.File]::WriteAllText($tempDeploy, $cleanScript, [System.Text.UTF8Encoding]::new($false))

$sshOpts = @("-F", "NUL", "-i", $resolvedKeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes", "-o", "ConnectTimeout=180", "-o", "GSSAPIAuthentication=no")
scp @sshOpts $tempDeploy "${SSHUser}@${ServerIP}:/tmp/deploy.sh" | Out-Null
ssh @sshOpts "${SSHUser}@${ServerIP}" "chmod +x /tmp/deploy.sh && /tmp/deploy.sh && rm /tmp/deploy.sh"

Remove-Item $tempDeploy -Force

Write-Host "`n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray
Write-Host "🎉 Application deployment completed!" -ForegroundColor Green
