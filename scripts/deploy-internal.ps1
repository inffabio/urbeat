# ─────────────────────────────────────────────────────────────
# Deploy Urbeat → Servidor Interno (192.168.1.15)
# ─────────────────────────────────────────────────────────────
# Empacota o projeto em tar.gz, envia via scp e (re)constrói os
# containers via docker compose no servidor remoto.
#
# Pré-requisitos LOCAIS:
#   - ssh, scp (OpenSSH Windows nativo)
#   - tar (Windows 10+ tem nativo: C:\Windows\System32\tar.exe)
# Pré-requisitos REMOTOS:
#   - Docker + Docker Compose v2 instalados
#   - chave pública já em ~/.ssh/authorized_keys
# ─────────────────────────────────────────────────────────────

[CmdletBinding()]
param(
    [string]$ServerHost = "192.168.1.15",
    [string]$ServerUser = "fabio",
    [string]$RemoteRoot = "~/urbeat",
    [switch]$NoRebuild,
    [switch]$Down,
    [switch]$LogsOnly,
    [switch]$SkipUpload
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot

Write-Host "== Urbeat deploy (Servidor Interno) ==" -ForegroundColor Cyan
Write-Host "Local root : $ProjectRoot"
Write-Host "Remote     : ${ServerUser}@${ServerHost}:${RemoteRoot}"
Write-Host ""

function Invoke-Ssh {
    param([string]$Cmd, [switch]$IgnoreExitCode)
    Write-Host "[ssh] $Cmd" -ForegroundColor DarkGray
    & ssh -o StrictHostKeyChecking=accept-new -o ServerAliveInterval=30 "$ServerUser@$ServerHost" $Cmd
    if (-not $IgnoreExitCode -and $LASTEXITCODE -ne 0) {
        throw "SSH command failed (exit $LASTEXITCODE): $Cmd"
    }
}

# ─── LOGS ONLY ───────────────────────────────────────────────
if ($LogsOnly) {
    Invoke-Ssh "cd $RemoteRoot/docker && docker compose -f docker-compose.dev.yml ps && echo '--- webapi logs ---' && docker logs --tail 80 urbeat_webapi 2>&1 | tail -80"
    exit 0
}

# ─── DOWN ────────────────────────────────────────────────────
if ($Down) {
    Invoke-Ssh "cd $RemoteRoot/docker && docker compose -f docker-compose.dev.yml down -v --remove-orphans"
    Write-Host "Stack derrubada." -ForegroundColor Green
    exit 0
}

# ─── 1. Garantir diretório remoto ───────────────────────────
Invoke-Ssh "mkdir -p $RemoteRoot && rm -rf $RemoteRoot/_incoming && mkdir -p $RemoteRoot/_incoming"

# ─── 2. Empacotar e enviar ──────────────────────────────────
if (-not $SkipUpload) {
    Write-Host "==> Atualizando versão da aplicação..." -ForegroundColor Yellow
    $pkgPath = Join-Path $ProjectRoot "frontend\package.json"
    $pkg = Get-Content $pkgPath -Raw | ConvertFrom-Json
    $v = $pkg.version -split '\.'
    $v[2] = [int]$v[2] + 1
    $newVersion = "$($v[0]).$($v[1]).$($v[2])"
    $pkg.version = $newVersion
    $pkg | ConvertTo-Json -Depth 100 | Set-Content $pkgPath

    # Atualiza também no componente da landing page
    $tsPath = Join-Path $ProjectRoot "frontend\src\app\features\landing-page\landing-page.component.ts"
    $tsContent = Get-Content $tsPath -Raw
    $tsContent = $tsContent -replace "readonly appVersion = 'v\d+\.\d+\.\d+';", "readonly appVersion = 'v$newVersion';"
    Set-Content $tsPath $tsContent

    Write-Host "  > Versão atualizada para: v$newVersion" -ForegroundColor Green

    Write-Host "==> Empacotando projeto..." -ForegroundColor Yellow

    $tarball = Join-Path $env:TEMP "urbeat-deploy-$(Get-Date -Format 'yyyyMMddHHmmss').tar.gz"

    Push-Location $ProjectRoot
    try {
        # Usa tar nativo do Windows com excludes
        $excludes = @(
            "--exclude=.git",
            "--exclude=node_modules",
            "--exclude=bin",
            "--exclude=obj",
            "--exclude=dist",
            "--exclude=.angular",
            "--exclude=.vs",
            "--exclude=.vscode",
            "--exclude=TestResults",
            "--exclude=coverage",
            "--exclude=*.log",
            "--exclude=UrbeatLogs",
            "--exclude=Documentacao",
            "--exclude=.agents",
            "--exclude=.claude",
            "--exclude=.cursor",
            "--exclude=.github"
        )
        Write-Host "tar -czf $tarball backend frontend docker scripts" -ForegroundColor DarkGray
        & tar -czf $tarball @excludes backend frontend docker scripts
        if ($LASTEXITCODE -ne 0) { throw "tar local falhou" }
    } finally {
        Pop-Location
    }

    $sz = [math]::Round((Get-Item $tarball).Length / 1MB, 2)
    Write-Host "  > tarball: $sz MB" -ForegroundColor DarkGray

    Write-Host "==> Enviando via scp..." -ForegroundColor Yellow
    & scp -o StrictHostKeyChecking=accept-new $tarball "${ServerUser}@${ServerHost}:${RemoteRoot}/_incoming/deploy.tar.gz"
    if ($LASTEXITCODE -ne 0) { throw "scp falhou" }

    Remove-Item $tarball -Force

    Write-Host "==> Extraindo no servidor..." -ForegroundColor Yellow
    $extractCmd = @"
set -e
cd $RemoteRoot
mkdir -p _stage
tar -xzf _incoming/deploy.tar.gz -C _stage
rsync -a --delete _stage/backend/ ./backend/ 2>/dev/null || cp -rT _stage/backend ./backend
rsync -a --delete _stage/frontend/ ./frontend/ 2>/dev/null || cp -rT _stage/frontend ./frontend
rsync -a --delete _stage/docker/ ./docker/ 2>/dev/null || cp -rT _stage/docker ./docker
rsync -a --delete _stage/scripts/ ./scripts/ 2>/dev/null || cp -rT _stage/scripts ./scripts
rm -rf _stage _incoming
echo 'extracted'
"@
    Invoke-Ssh $extractCmd

    Write-Host "==> Garantindo .env em docker/" -ForegroundColor Yellow
    if (Test-Path "$ProjectRoot/docker/.env.production") {
        Write-Host "    Copiando .env.production local para o servidor..." -ForegroundColor DarkGray
        & scp -o StrictHostKeyChecking=accept-new "$ProjectRoot/docker/.env.production" "${ServerUser}@${ServerHost}:${RemoteRoot}/docker/.env"
        
        # Override FRONTEND_BASE_URL for internal development environment
        Write-Host "    Ajustando FRONTEND_BASE_URL para ambiente interno (192.168.1.15)..." -ForegroundColor DarkGray
        Invoke-Ssh "sed -i 's|^FRONTEND_BASE_URL=.*|FRONTEND_BASE_URL=http://192.168.1.15|' $RemoteRoot/docker/.env"
    } else {
        Invoke-Ssh "test -f $RemoteRoot/docker/.env || cp $RemoteRoot/docker/.env.example $RemoteRoot/docker/.env"
        Invoke-Ssh "sed -i 's|^FRONTEND_BASE_URL=.*|FRONTEND_BASE_URL=http://192.168.1.15|' $RemoteRoot/docker/.env"
    }
}

# ─── 3. Preparar Nginx para ambiente interno (SPA Padrão + SignalR) ───
Write-Host "==> Configurando Nginx para HTTP (SPA Padrão + SignalR)..." -ForegroundColor Yellow
$nginxConf = @"
cat << 'EOF' > $RemoteRoot/docker/nginx/conf.d/10-http.conf
map `$http_upgrade `$connection_upgrade {
    default upgrade;
    ''      close;
}

server {
    listen 80 default_server;
    listen [::]:80 default_server;
    server_name _;

    location = /index.html {
        root /usr/share/nginx/html;
        add_header Cache-Control 'no-cache, no-store, must-revalidate';
        add_header Pragma 'no-cache';
        add_header Expires '0';
    }

    location / {
        root /usr/share/nginx/html;
        index index.html;
        try_files `$uri `$uri/ /index.html;
    }

    location /api/ {
        proxy_pass http://urbeat_webapi;
        proxy_set_header Host `$host;
        proxy_set_header X-Forwarded-Proto `$scheme;
    }

    location /hubs/ {
        proxy_pass http://urbeat_webapi;
        proxy_http_version 1.1;
        proxy_set_header Upgrade `$http_upgrade;
        proxy_set_header Connection `$connection_upgrade;
        proxy_set_header Host `$host;
        proxy_read_timeout 86400s;
    }

    location /health   { proxy_pass http://urbeat_webapi; }
    location /swagger  { proxy_pass http://urbeat_webapi; proxy_set_header Host `$host; }
    location /hangfire { proxy_pass http://urbeat_webapi; proxy_set_header Host `$host; }
}
EOF
"@
    # Executa o here-doc diretamente via SSH para evitar qualquer problema de codificação do Windows
    Invoke-Ssh $nginxConf
    Invoke-Ssh "cd $RemoteRoot/docker/nginx/conf.d && mv 20-https.conf 20-https.conf.disabled 2>/dev/null || true"

    # Remove Certbot from internal deployment
    Write-Host "==> Removendo Certbot e volumes ssl localmente..." -ForegroundColor DarkGray

# ─── 4. Build + Up ──────────────────────────────────────────
Write-Host "==> docker compose up -d (build=$(-not $NoRebuild))..." -ForegroundColor Yellow
if ($NoRebuild) {
    Invoke-Ssh "cd $RemoteRoot/docker && docker compose -f docker-compose.dev.yml up -d --remove-orphans"
} else {
    Invoke-Ssh "cd $RemoteRoot/docker && docker compose -f docker-compose.dev.yml build --pull && docker compose -f docker-compose.dev.yml up -d --remove-orphans"
}

# ─── 4. Aguardar /health ────────────────────────────────────
Write-Host "==> Aguardando WebApi /health (até 3 min)..." -ForegroundColor Yellow
$ok = $false
for ($i = 1; $i -le 60; $i++) {
    Start-Sleep -Seconds 3
    # Verifica diretamente no container da API para evitar problemas de roteamento do Nginx
    $resp = (& ssh "$ServerUser@$ServerHost" "docker exec urbeat_webapi curl -s -o /dev/null -w '%{http_code}' http://localhost:8080/health" 2>$null)
    if ($resp -eq "200") { $ok = $true; break }
    Write-Host "   tentativa $i/60 (HTTP $resp)" -ForegroundColor DarkGray
}
if ($ok) {
    Write-Host "==> /health OK" -ForegroundColor Green
} else {
    Write-Host "AVISO: /health nao respondeu. Veja os logs:" -ForegroundColor Red
    Invoke-Ssh "docker logs --tail 60 urbeat_webapi 2>&1" -IgnoreExitCode
}

# ─── 5. Git commit & push ───────────────────────────────────
Write-Host "==> Commitando e enviando para GitHub..." -ForegroundColor Yellow
Push-Location $ProjectRoot
try {
    git add -A
    git commit -m "deploy v$newVersion" --allow-empty
    git push origin master
    Write-Host "  > Push concluido." -ForegroundColor Green
} catch {
    Write-Host "AVISO: git push falhou. Verifique manualmente." -ForegroundColor Red
} finally {
    Pop-Location
}

# ─── 6. Status ──────────────────────────────────────────────
Invoke-Ssh "cd $RemoteRoot/docker && docker compose -f docker-compose.dev.yml ps"

Write-Host ""
Write-Host "═════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Pronto. Endpoints:"                                    -ForegroundColor Cyan
Write-Host "  Site           : http://$ServerHost"                   -ForegroundColor White
Write-Host "  Lojas demo     : /burguer_do_rafa  /pizza_do_rafa  /sushi_rafa" -ForegroundColor White
Write-Host "  Swagger        : http://$ServerHost/swagger"           -ForegroundColor White
Write-Host "  Hangfire       : http://$ServerHost/hangfire (basic auth)" -ForegroundColor White
Write-Host "  Grafana        : http://$ServerHost:3000"              -ForegroundColor White
Write-Host "  Prometheus     : http://$ServerHost:9090"              -ForegroundColor White
Write-Host "═════════════════════════════════════════════════════" -ForegroundColor Cyan