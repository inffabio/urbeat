<#
.SYNOPSIS
    Verifies all Urbeat services are running correctly
.DESCRIPTION
    Performs health checks on all deployed services and
    generates a deployment status report.
#>

param(
    [Parameter(Mandatory=$false)]
    [string]$ServerIP = "136.248.115.135",

    [Parameter(Mandatory=$false)]
    [string]$SSHUser = "dexter",

    [Parameter(Mandatory=$false)]
    [int]$SSHPort = 2208,

    [Parameter(Mandatory=$false)]
    [string]$SSHKeyPath = "~/.ssh/id_ed25519"
)

. (Join-Path $PSScriptRoot "oci-ssh.ps1")

Write-Host "✅ Verifying Urbeat Deployment..." -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

# Resolve SSH key path
$resolvedKeyPath = (Resolve-Path $SSHKeyPath -ErrorAction SilentlyContinue).Path
if (-not $resolvedKeyPath) {
    $resolvedKeyPath = Resolve-Path "$env:USERPROFILE\.ssh\id_rsa" -ErrorAction SilentlyContinue
}

$verifyScript = @'
#!/bin/bash

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "  URBEAT DEPLOYMENT VERIFICATION REPORT"
echo "  Generated: $(date)"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

# System Info
echo ""
echo "🖥️  SYSTEM INFORMATION"
echo "  OS: $(lsb_release -d | cut -f2)"
echo "  Arch: $(uname -m)"
echo "  Kernel: $(uname -r)"
echo "  Uptime: $(uptime -p)"
echo "  CPU: $(nproc) cores"
echo "  RAM: $(free -h | grep Mem | awk '{print $2}') total, $(free -h | grep Mem | awk '{print $7}') available"
echo "  Disk: $(df -h / | tail -1 | awk '{print $4}') available"

# Every failed essential check contributes to this value. Optional monitoring UI
# (Grafana) is reported but deliberately does not block verification.
verification_failed=0

# Docker Status
echo ""
echo "🐳 DOCKER STATUS"
docker_available=0
compose_available=0
if command -v docker &> /dev/null; then
    docker_available=1
    echo "  Version: $(docker --version)"
    if ! docker info >/dev/null 2>&1; then
        echo "  ❌ Docker daemon is unavailable!"
        verification_failed=1
        docker_available=0
    fi
    if docker compose version >/dev/null 2>&1; then
        compose_available=1
        echo "  Compose: $(docker compose version)"
    else
        echo "  ❌ Docker Compose plugin is unavailable!"
        verification_failed=1
    fi
    echo ""
    echo "  📦 CONTAINERS:"
    docker ps --format "  {{.Names}}\t{{.Status}}"
else
    echo "  ❌ Docker not installed!"
    verification_failed=1
fi

# Service Health Checks
echo ""
echo "🏥 SERVICE HEALTH CHECKS"
check_service() {
    local name=$1
    local url=$2
    local response=$(curl -s -o /dev/null -w "%{http_code}" --max-time 5 "$url" 2>/dev/null)
    if [ "$response" = "200" ] || [ "$response" = "301" ] || [ "$response" = "302" ]; then
        echo "  ✅ $name: OK (HTTP $response)"
    else
        echo "  ❌ $name: FAILED (HTTP $response)"
        verification_failed=1
    fi
}

# Public API health is intentionally an exact 200 check. Do not follow redirects here:
# an HTTPS/proxy misconfiguration must not be reported as a healthy API.
check_exact_200() {
    local name=$1
    local url=$2
    local response=$(curl -sS -o /dev/null -w "%{http_code}" --max-time 5 "$url" 2>/dev/null || true)
    if [ "$response" = "200" ]; then
        echo "  ✅ $name: OK (HTTP 200)"
    else
        echo "  ❌ $name: FAILED (HTTP $response; expected HTTP 200)"
        verification_failed=1
    fi
}

check_service "Frontend (nginx/domain)" "https://www.urbeat.com.br/"
check_service "Backend API replica 1 (localhost:5001)" "http://localhost:5001/health"
check_service "Backend API replica 2 (localhost:5002)" "http://localhost:5002/health"
check_service "Prometheus (localhost:9090)" "http://localhost:9090/-/healthy"
echo "  ℹ️ Grafana (localhost:3000): optional; not a verification blocker"
grafana_response=$(curl -s -o /dev/null -w "%{http_code}" --max-time 5 "http://localhost:3000/api/health" 2>/dev/null)
if [ "$grafana_response" = "200" ]; then
    echo "  ✅ Grafana (optional): OK (HTTP $grafana_response)"
else
    echo "  ⚠️ Grafana (optional): unavailable (HTTP $grafana_response)"
fi

# NGINX Status
echo ""
echo "🌐 NGINX STATUS"
if systemctl is-active --quiet nginx; then
    echo "  ✅ NGINX: Running"
    if sudo nginx -t >/tmp/urbeat-nginx-test.log 2>&1; then
        echo "  Config test: $(tail -1 /tmp/urbeat-nginx-test.log)"
    else
        echo "  ❌ NGINX config test: INVALID"
        sed 's/^/    /' /tmp/urbeat-nginx-test.log
        verification_failed=1
    fi
else
    echo "  ❌ NGINX: Not running"
    verification_failed=1
    if sudo nginx -t >/tmp/urbeat-nginx-test.log 2>&1; then
        echo "  Config test: valid, but NGINX is stopped"
    else
        echo "  ❌ NGINX config test: INVALID"
        sed 's/^/    /' /tmp/urbeat-nginx-test.log
        verification_failed=1
    fi
fi

# Check enabled sites
echo ""
echo "  📋 ENABLED SITES:"
ls /etc/nginx/sites-enabled/ | while read f; do echo "    - $f"; done

# Domain checks
echo ""
echo "🌍 DOMAIN CHECKS"
check_service "www.urbeat.com.br" "http://www.urbeat.com.br"
check_exact_200 "api.urbeat.com.br (HTTPS health)" "https://api.urbeat.com.br/health"

# SSL Certificates
echo ""
echo "🔒 SSL CERTIFICATES"
if command -v certbot &> /dev/null; then
    certbot certificates 2>/dev/null | grep -E "(Domains|Expiry Date|Status)" | sed 's/^/  /'
else
    echo "  ⚠️  Certbot not installed (SSL not configured)"
fi

# PostgreSQL
echo ""
echo "🐘 POSTGRESQL"
# Read the database credentials exported by the PostgreSQL image itself. The deployment can
# configure different POSTGRES_USER/POSTGRES_DB values, so the verifier must not assume names.
if [ "$docker_available" -eq 1 ] && docker exec urbeat-postgres sh -c 'pg_isready -U "$POSTGRES_USER" -d "$POSTGRES_DB"' 2>/dev/null; then
    echo "  ✅ PostgreSQL: Ready"
else
    echo "  ❌ PostgreSQL: Not ready"
    verification_failed=1
fi

# Redis and workers are essential runtime components, not merely informational containers.
echo ""
echo "🔄 REDIS AND WORKERS"
if [ "$docker_available" -eq 1 ] && docker exec urbeat-redis redis-cli ping 2>/dev/null | grep -q '^PONG$'; then
    echo "  ✅ Redis: Ready"
else
    echo "  ❌ Redis: Not ready"
    verification_failed=1
fi

check_container() {
    local name=$1
    local details=$(docker inspect -f '{{.State.Running}}|{{.State.Status}}|{{.State.Restarting}}|{{.RestartCount}}|{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$name" 2>/dev/null)
    if [ -z "$details" ]; then
        echo "  ❌ $name: unavailable"
        verification_failed=1
        return
    fi
    IFS='|' read -r running status restarting restart_count health <<< "$details"
    if [ "$running" = "true" ] && [ "$status" = "running" ] && [ "$restarting" = "false" ] && [ "$restart_count" -lt 3 ] && { [ "$health" = "none" ] || [ "$health" = "healthy" ]; }; then
        if [ "$health" = "none" ]; then
            echo "  ✅ $name: Running (no healthcheck configured; restart count $restart_count)"
        else
            echo "  ✅ $name: Running/healthy (restart count $restart_count)"
        fi
    else
        echo "  ❌ $name: unhealthy or crash-looping (state=$status restarting=$restarting restarts=$restart_count health=$health)"
        verification_failed=1
    fi
}
if [ "$docker_available" -eq 1 ]; then
    check_container urbeat-worker-business
    check_container urbeat-worker-communications
else
    echo "  ❌ Workers: cannot inspect without Docker"
    verification_failed=1
fi

# Docker Volumes
echo ""
echo "💾 DOCKER VOLUMES"
if [ "$docker_available" -eq 1 ]; then
    docker volume ls --format "  {{.Name}}\t{{.Driver}}" | grep urbeat || true
fi

# Recent logs summary
echo ""
echo "📋 RECENT LOGS (last 5 lines per service)"
echo ""
echo "  --- Backend ---"
if [ "$docker_available" -eq 1 ]; then
docker logs urbeat-backend --tail=5 2>/dev/null | sed 's/^/  /'
echo "  --- Backend replica 2 ---"
docker logs urbeat-backend-2 --tail=5 2>/dev/null | sed 's/^/  /'
echo ""
echo "  --- Frontend build ---"
docker logs urbeat-frontend-build --tail=5 2>/dev/null | sed 's/^/  /'
echo ""
echo "  --- PostgreSQL ---"
docker logs urbeat-postgres --tail=5 2>/dev/null | sed 's/^/  /'
else
    echo "  Docker unavailable; container logs omitted."
fi

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
if [ "`$verification_failed" -ne 0 ]; then
    echo "❌ Verification completed with failures."
else
    echo "✅ Verification completed!"
fi
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
exit "`$verification_failed"
'@

$tempVerify = [System.IO.Path]::GetTempFileName() + ".sh"
# Convert CRLF to LF and write as UTF-8 without BOM for Linux bash compatibility
$cleanVerifyScript = $verifyScript -replace "`r`n", "`n"
[System.IO.File]::WriteAllText($tempVerify, $cleanVerifyScript, [System.Text.UTF8Encoding]::new($false))

$sshOpts = @("-p", $SSHPort, "-i", $resolvedKeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes", "-o", "ConnectTimeout=180", "-o", "GSSAPIAuthentication=no")
$scpOpts = @("-P", $SSHPort, "-i", $resolvedKeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes", "-o", "ConnectTimeout=180", "-o", "GSSAPIAuthentication=no")
Send-PortKnock -ServerIP $ServerIP
try {
    scp @scpOpts $tempVerify "${SSHUser}@${ServerIP}:/tmp/verify.sh"
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to upload verification script (scp exit code $LASTEXITCODE)."
    }
    Send-PortKnock -ServerIP $ServerIP
    ssh @sshOpts "${SSHUser}@${ServerIP}" "chmod +x /tmp/verify.sh; /tmp/verify.sh; status=`$?; rm -f /tmp/verify.sh; exit `$status"
    if ($LASTEXITCODE -ne 0) {
        throw "Deployment verification failed (ssh exit code $LASTEXITCODE)."
    }
} finally {
    Remove-Item $tempVerify -Force -ErrorAction SilentlyContinue
}
