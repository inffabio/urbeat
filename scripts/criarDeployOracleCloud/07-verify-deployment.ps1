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

# Docker Status
echo ""
echo "🐳 DOCKER STATUS"
if command -v docker &> /dev/null; then
    echo "  Version: $(docker --version)"
    echo "  Compose: $(docker compose version)"
    echo ""
    echo "  📦 CONTAINERS:"
    docker ps --format "  {{.Names}}\t{{.Status}}"
else
    echo "  ❌ Docker not installed!"
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
    fi
}

check_service "Frontend (nginx/domain)" "https://www.urbeat.com.br/"
check_service "Backend API (localhost:5000)" "http://localhost:5000/health"
check_service "Prometheus (localhost:9090)" "http://localhost:9090/-/healthy"
check_service "Grafana (localhost:3000)" "http://localhost:3000/api/health"

# NGINX Status
echo ""
echo "🌐 NGINX STATUS"
if systemctl is-active --quiet nginx; then
    echo "  ✅ NGINX: Running"
    echo "  Config test: $(sudo nginx -t 2>&1 | tail -1)"
else
    echo "  ❌ NGINX: Not running"
fi

# Check enabled sites
echo ""
echo "  📋 ENABLED SITES:"
ls /etc/nginx/sites-enabled/ | while read f; do echo "    - $f"; done

# Domain checks
echo ""
echo "🌍 DOMAIN CHECKS"
check_service "www.urbeat.com.br" "http://www.urbeat.com.br"
check_service "api.urbeat.com.br" "http://api.urbeat.com.br/health"

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
if docker exec urbeat-postgres pg_isready -U urbeatPostg -d urbeatdb 2>/dev/null; then
    echo "  ✅ PostgreSQL: Ready"
    TABLE_COUNT=$(docker exec urbeat-postgres psql -U urbeatPostg -d urbeatdb -c "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='urbeat';" -t 2>/dev/null | tr -d ' ')
    echo "  📊 Tables in urbeat schema: $TABLE_COUNT"
else
    echo "  ❌ PostgreSQL: Not ready"
fi

# Docker Volumes
echo ""
echo "💾 DOCKER VOLUMES"
docker volume ls --format "  {{.Name}}\t{{.Driver}}" | grep urbeat || true

# Recent logs summary
echo ""
echo "📋 RECENT LOGS (last 5 lines per service)"
echo ""
echo "  --- Backend ---"
docker logs urbeat-backend --tail=5 2>/dev/null | sed 's/^/  /'
echo ""
echo "  --- Frontend build ---"
docker logs urbeat-frontend-build --tail=5 2>/dev/null | sed 's/^/  /'
echo ""
echo "  --- PostgreSQL ---"
docker logs urbeat-postgres --tail=5 2>/dev/null | sed 's/^/  /'

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "✅ Verification completed!"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
'@

$tempVerify = [System.IO.Path]::GetTempFileName() + ".sh"
$verifyScript | Out-File -FilePath $tempVerify -Encoding UTF8 -NoNewline

$sshOpts = @("-p", $SSHPort, "-i", $resolvedKeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes", "-o", "ConnectTimeout=180", "-o", "GSSAPIAuthentication=no")
$scpOpts = @("-P", $SSHPort, "-i", $resolvedKeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes", "-o", "ConnectTimeout=180", "-o", "GSSAPIAuthentication=no")
scp @scpOpts $tempVerify "${SSHUser}@${ServerIP}:/tmp/verify.sh"
ssh @sshOpts "${SSHUser}@${ServerIP}" "chmod +x /tmp/verify.sh && /tmp/verify.sh && rm /tmp/verify.sh"

Remove-Item $tempVerify -Force
