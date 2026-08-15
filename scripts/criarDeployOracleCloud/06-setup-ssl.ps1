<#
.SYNOPSIS
    Installs and configures SSL certificates via Let's Encrypt (Certbot)
.DESCRIPTION
    Installs Certbot and generates SSL certificates for:
    - www.urbeat.com.br
    - api.urbeat.com.br
    Then updates NGINX configurations to use HTTPS.
#>

param(
    [Parameter(Mandatory=$false)]
    [string]$ServerIP = "136.248.115.135",

    [Parameter(Mandatory=$false)]
    [string]$SSHUser = "dexter",

    [Parameter(Mandatory=$false)]
    [int]$SSHPort = 2208,

    [Parameter(Mandatory=$false)]
    [string]$SSHKeyPath = "~/.ssh/id_ed25519",

    [Parameter(Mandatory=$false)]
    [string]$Email = "contato@urbeat.com.br"
)

Write-Host "🔒 Setting up SSL Certificates (Let's Encrypt)..." -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

# Resolve SSH key path
$resolvedKeyPath = (Resolve-Path $SSHKeyPath -ErrorAction SilentlyContinue).Path
if (-not $resolvedKeyPath) {
    $resolvedKeyPath = Resolve-Path "$env:USERPROFILE\.ssh\id_rsa" -ErrorAction SilentlyContinue
}

$sslScript = @"
#!/bin/bash
set -e

echo "🔒 Starting SSL Certificate Setup..."

echo "📦 Installing Certbot..."
sudo apt-get update -y
sudo apt-get install -y certbot python3-certbot-nginx

echo "🌐 Requesting SSL certificates..."
echo "  Domain 1: www.urbeat.com.br"
echo "  Domain 2: api.urbeat.com.br"

# Request certificates for both domains
sudo certbot --nginx \
    -d www.urbeat.com.br \
    -d urbeat.com.br \
    --non-interactive \
    --agree-tos \
    --email $Email \
    --redirect

sudo certbot --nginx \
    -d api.urbeat.com.br \
    --non-interactive \
    --agree-tos \
    --email $Email \
    --redirect

echo "🔄 Testing NGINX configuration..."
sudo nginx -t

echo "🔄 Reloading NGINX..."
sudo nginx -s reload || sudo systemctl reload nginx

echo "⏰ Setting up auto-renewal..."
sudo systemctl enable certbot.timer
sudo systemctl start certbot.timer

echo "🔍 Testing auto-renewal..."
sudo certbot renew --dry-run

echo "📋 Certificate status:"
sudo certbot certificates

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "✅ SSL certificates installed successfully!"
echo "  🔒 https://www.urbeat.com.br"
echo "  🔒 https://api.urbeat.com.br"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
"@

$tempSSL = [System.IO.Path]::GetTempFileName() + ".sh"
$sslScript | Out-File -FilePath $tempSSL -Encoding UTF8 -NoNewline

Write-Host "`n📤 Uploading SSL setup script..." -ForegroundColor Yellow
$sshOpts = @("-p", $SSHPort, "-i", $resolvedKeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes", "-o", "ConnectTimeout=180", "-o", "GSSAPIAuthentication=no")
$scpOpts = @("-P", $SSHPort, "-i", $resolvedKeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes", "-o", "ConnectTimeout=180", "-o", "GSSAPIAuthentication=no")
scp @scpOpts $tempSSL "${SSHUser}@${ServerIP}:/tmp/setup-ssl.sh"

Write-Host "🚀 Executing SSL setup..." -ForegroundColor Yellow
ssh @sshOpts "${SSHUser}@${ServerIP}" "chmod +x /tmp/setup-ssl.sh && /tmp/setup-ssl.sh && rm /tmp/setup-ssl.sh"

Remove-Item $tempSSL -Force

Write-Host "`n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray
Write-Host "🎉 SSL setup completed!" -ForegroundColor Green
Write-Host "  🔒 https://www.urbeat.com.br" -ForegroundColor Cyan
Write-Host "  🔒 https://api.urbeat.com.br" -ForegroundColor Cyan
