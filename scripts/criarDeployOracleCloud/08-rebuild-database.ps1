<##
.SYNOPSIS
    Backs up and rebuilds the remote Urbeat database using committed EF migrations.
.DESCRIPTION
    The WebApi applies EF migrations automatically during startup. This script
    creates a remote backup, optionally recreates the application database, and
    restarts the backend so the committed migrations are applied.
    It never changes OCI Vault secrets.
##>

param(
    [Parameter(Mandatory = $false)]
    [string]$ServerIP = "136.248.115.135",

    [Parameter(Mandatory = $false)]
    [string]$SSHUser = "dexter",

    [Parameter(Mandatory = $false)]
    [int]$SSHPort = 2208,

    [Parameter(Mandatory = $false)]
    [string]$SSHKeyPath = "~/.ssh/id_ed25519",

    [Parameter(Mandatory = $false)]
    [string]$AppDir = "/opt/urbeat",

    [Parameter(Mandatory = $false)]
    [switch]$ResetDatabase,

    [Parameter(Mandatory = $false)]
    [string]$ConfirmReset
)

$ErrorActionPreference = "Stop"

if ($ResetDatabase -and $ConfirmReset -ne "RESET URBEAT DATABASE") {
    throw 'A recriacao exige -ConfirmReset "RESET URBEAT DATABASE".'
}

$resolvedKeyPath = (Resolve-Path $SSHKeyPath -ErrorAction SilentlyContinue).Path
if (-not $resolvedKeyPath) {
    $resolvedKeyPath = (Resolve-Path "$env:USERPROFILE\.ssh\id_rsa" -ErrorAction SilentlyContinue).Path
}
if (-not $resolvedKeyPath) {
    throw "Chave SSH nao encontrada. Informe -SSHKeyPath."
}

$sshOptions = @(
    "-p", $SSHPort, "-i", $resolvedKeyPath,
    "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes",
    "-o", "ConnectTimeout=180", "-o", "GSSAPIAuthentication=no"
)
$target = "${SSHUser}@${ServerIP}"
$resetFlag = if ($ResetDatabase) { "true" } else { "false" }

$remoteScript = @"
#!/usr/bin/env bash
set -Eeuo pipefail

APP_DIR='$AppDir'
RESET_DATABASE='$resetFlag'
COMPOSE=(docker compose --env-file "`$APP_DIR/.env" -f "`$APP_DIR/docker-compose.yml")
BACKUP_DIR="`$APP_DIR/backups/database-rebuild-`$(date +%Y%m%d_%H%M%S)"

if [[ ! -f "`$APP_DIR/.env" ]]; then
  echo 'Arquivo .env remoto ausente. Abortando sem alterar o banco.' >&2
  exit 1
fi

mkdir -p "`$BACKUP_DIR"
chmod 700 "`$BACKUP_DIR"

echo 'Garantindo que o PostgreSQL esteja disponível...'
"`${COMPOSE[@]}" up -d postgres
"`${COMPOSE[@]}" exec -T postgres pg_isready

echo 'Criando backup do banco principal...'
"`${COMPOSE[@]}" exec -T postgres sh -c 'pg_dump -U "`$POSTGRES_USER" -d "`$POSTGRES_DB"' | gzip > "`$BACKUP_DIR/urbeatdb.sql.gz"

echo 'Tentando criar backup do banco de logs, se existir...'
if "`${COMPOSE[@]}" exec -T postgres sh -c 'psql -U "`$POSTGRES_USER" -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname = '\''UrbeatLogs'\''"' | grep -q 1; then
  "`${COMPOSE[@]}" exec -T postgres sh -c 'pg_dump -U "`$POSTGRES_USER" -d UrbeatLogs' | gzip > "`$BACKUP_DIR/urbeatlogs.sql.gz"
fi

if [[ "`$RESET_DATABASE" != 'true' ]]; then
  echo "Backup concluido em `$BACKUP_DIR. Nenhum banco foi recriado."
  exit 0
fi

echo 'Parando o backend antes da recriacao...'
"`${COMPOSE[@]}" stop backend

echo 'Recriando apenas o banco da aplicacao...'
"`${COMPOSE[@]}" exec -T postgres sh -c 'psql -U "`$POSTGRES_USER" -d postgres -v ON_ERROR_STOP=1 -c "DROP DATABASE IF EXISTS `$POSTGRES_DB WITH (FORCE);" -c "CREATE DATABASE `$POSTGRES_DB OWNER `$POSTGRES_USER;"'
"`${COMPOSE[@]}" exec -T postgres sh -c 'psql -U "`$POSTGRES_USER" -d "`$POSTGRES_DB" -v ON_ERROR_STOP=1 -c '\''CREATE EXTENSION IF NOT EXISTS "uuid-ossp";'\'' -c '\''CREATE EXTENSION IF NOT EXISTS pg_trgm;'\'' -c '\''CREATE EXTENSION IF NOT EXISTS unaccent;'\'''

echo 'Subindo o backend; o startup aplicara as migrations versionadas...'
"`${COMPOSE[@]}" up -d backend
for attempt in {1..60}; do
  status=`$(curl -s -o /dev/null -w '%{http_code}' http://localhost:5000/health || true)
  if [[ "`$status" == '200' ]]; then
    echo 'Backend saudavel e migrations aplicadas pelo startup.'
    exit 0
  fi
  sleep 5
done

echo 'Backend nao ficou saudavel apos a recriacao. Consulte os logs.' >&2
"`${COMPOSE[@]}" logs --tail=100 backend >&2 || true
exit 1
"@

$tempScript = [System.IO.Path]::GetTempFileName() + ".sh"
$cleanScript = $remoteScript -replace "`r`n", "`n"
[System.IO.File]::WriteAllText($tempScript, $cleanScript, [System.Text.UTF8Encoding]::new($false))

try {
    $scpOptions = @(
        "-P", $SSHPort, "-i", $resolvedKeyPath,
        "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes",
        "-o", "ConnectTimeout=180", "-o", "GSSAPIAuthentication=no"
    )
    scp @scpOptions $tempScript "${target}:/tmp/urbeat-rebuild-database.sh" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Falha ao enviar o script remoto." }
    ssh @sshOptions $target "chmod 700 /tmp/urbeat-rebuild-database.sh && /tmp/urbeat-rebuild-database.sh; status=`$?; rm -f /tmp/urbeat-rebuild-database.sh; exit `$status"
    if ($LASTEXITCODE -ne 0) { throw "A operacao remota falhou." }
} finally {
    Remove-Item -LiteralPath $tempScript -Force -ErrorAction SilentlyContinue
}
