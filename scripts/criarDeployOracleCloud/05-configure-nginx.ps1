<#
.SYNOPSIS
    Configures NGINX as reverse proxy for Urbeat application
.DESCRIPTION
    Creates NGINX virtual host configurations for:
    - www.urbeat.com.br (Frontend - Angular/Ionic)
    - api.urbeat.com.br (Backend - .NET 9 API)
    - Grafana and Prometheus internal access
    NGINX is NOT containerized - runs directly on host.
#>

param(
    [Parameter(Mandatory=$false)]
    [string]$ServerIP = "136.248.115.135",

    [Parameter(Mandatory=$false)]
    [string]$SSHUser = "ubuntu",

    [Parameter(Mandatory=$false)]
    [string]$SSHKeyPath = "~/.ssh/id_ed25519"
)

Write-Host "🌐 Configuring NGINX Reverse Proxy..." -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

# Resolve SSH key path
$resolvedKeyPath = (Resolve-Path $SSHKeyPath -ErrorAction SilentlyContinue).Path
if (-not $resolvedKeyPath) {
    $resolvedKeyPath = Resolve-Path "$env:USERPROFILE\.ssh\id_rsa" -ErrorAction SilentlyContinue
}

# ─────────────────────────────────────────
# NGINX Config: www.urbeat.com.br (Frontend)
# ─────────────────────────────────────────

$nginxFrontend = @'
# ═══════════════════════════════════════════════════════════
# NGINX - URBEAT FRONTEND
# Domain: www.urbeat.com.br
# Serves static files from /opt/urbeat/frontend-dist
# Proxies /api and /hubs to the backend
# ═══════════════════════════════════════════════════════════

server {
    # SSL Configuration (Managed by Certbot)
    listen [::]:443 ssl ipv6only=on; # managed by Certbot
    listen 443 ssl; # managed by Certbot
    ssl_certificate /etc/letsencrypt/live/www.urbeat.com.br/fullchain.pem; # managed by Certbot
    ssl_certificate_key /etc/letsencrypt/live/www.urbeat.com.br/privkey.pem; # managed by Certbot
    include /etc/letsencrypt/options-ssl-nginx.conf; # managed by Certbot
    ssl_dhparam /etc/letsencrypt/ssl-dhparams.pem; # managed by Certbot

    server_name www.urbeat.com.br urbeat.com.br;

    root /opt/urbeat/frontend-dist;
    index index.html;

    # Proxy API requests to the .NET Backend
    location /api/ {
        proxy_pass http://127.0.0.1:5000;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    # Proxy SignalR Hubs to the .NET Backend
    location /hubs/ {
        proxy_pass http://127.0.0.1:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 3600s;
    }

    # Downloads do dashboard (agents, drivers, guias)
    location /downloads/ {
        alias /opt/urbeat/downloads/;
        autoindex off;
        add_header Cache-Control "public, max-age=300" always;
        add_header X-Content-Type-Options "nosniff" always;
        try_files $request_filename =404;
    }

    # Angular routing support (fallback for SPA)
    location / {
        try_files $uri $uri/ /index.html;
    }

    # Security headers
    add_header X-Frame-Options "SAMEORIGIN" always;
    add_header X-XSS-Protection "1; mode=block" always;
    add_header X-Content-Type-Options "nosniff" always;
    add_header Referrer-Policy "no-referrer-when-downgrade" always;
    add_header Content-Security-Policy "default-src 'self' https: data: blob: 'unsafe-inline'" always;

    # Gzip compression
    gzip on;
    gzip_vary on;
    gzip_min_length 1024;
    gzip_types text/plain text/css text/xml text/javascript
               application/javascript application/xml+rss
               application/json application/xml;

    # Static assets caching
    location ~* \.(js|css|png|jpg|jpeg|gif|ico|svg|woff|woff2|ttf|eot)$ {
        expires 1y;
        add_header Cache-Control "public, immutable";
        access_log off;
    }

    # Logs
    access_log /var/log/nginx/urbeat-frontend-access.log;
    error_log /var/log/nginx/urbeat-frontend-error.log;
}

# HTTP to HTTPS Redirect (Managed by Certbot)
server {
    if ($host = urbeat.com.br) {
        return 301 https://$host$request_uri;
    } # managed by Certbot

    if ($host = www.urbeat.com.br) {
        return 301 https://$host$request_uri;
    } # managed by Certbot

    listen 80;
    listen [::]:80;
    server_name www.urbeat.com.br urbeat.com.br;
    return 404; # managed by Certbot
}
'@

# ─────────────────────────────────────────
# NGINX Config: api.urbeat.com.br (Backend)
# ─────────────────────────────────────────

$nginxBackend = @'
# ═══════════════════════════════════════════════════════════
# NGINX - URBEAT BACKEND API
# Domain: api.urbeat.com.br
# Proxy: localhost:5000 (.NET 9 Docker Container)
# ═══════════════════════════════════════════════════════════

upstream urbeat_api {
    server 127.0.0.1:5000;
    keepalive 32;
}

server {
    listen 80;
    listen [::]:80;
    server_name api.urbeat.com.br;

    # Redirect HTTP to HTTPS (after SSL setup)
    # return 301 https://$server_name$request_uri;

    # API proxy
    location / {
        proxy_pass http://urbeat_api;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection 'upgrade';
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_cache_bypass $http_upgrade;
        proxy_read_timeout 300s;
        proxy_connect_timeout 75s;
        proxy_send_timeout 300s;

        # Buffer settings for API
        proxy_buffering on;
        proxy_buffer_size 128k;
        proxy_buffers 4 256k;
        proxy_busy_buffers_size 256k;

        # Max body size for file uploads
        client_max_body_size 50M;
    }

    # Health check endpoint
    location /health {
        proxy_pass http://urbeat_api/health;
        proxy_set_header Host $host;
        access_log off;
    }

    # Swagger UI (restrict in production if needed)
    location /swagger {
        proxy_pass http://urbeat_api/swagger;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    # Security headers
    # Note: CORS is handled natively by the .NET backend to avoid NGINX 'if' block quirks
    add_header X-Frame-Options "DENY" always;
    add_header X-XSS-Protection "1; mode=block" always;
    add_header X-Content-Type-Options "nosniff" always;

    # Logs
    access_log /var/log/nginx/urbeat-api-access.log;
    error_log /var/log/nginx/urbeat-api-error.log;
}
'@

# ─────────────────────────────────────────
# NGINX Config: Monitoring (internal only)
# ─────────────────────────────────────────

$nginxMonitoring = @'
# ═══════════════════════════════════════════════════════════
# NGINX - MONITORING (INTERNAL ACCESS ONLY)
# Grafana: localhost:3000
# Prometheus: localhost:9090
# ⚠️  These are NOT publicly exposed - internal only
# ═══════════════════════════════════════════════════════════

# Grafana - accessible only via SSH tunnel
# ssh -L 3000:localhost:3000 ubuntu@136.248.115.135
# Then access: http://localhost:3000

# Prometheus - accessible only via SSH tunnel
# ssh -L 9090:localhost:9090 ubuntu@136.248.115.135
# Then access: http://localhost:9090
'@

# ─────────────────────────────────────────
# Upload and apply NGINX configurations
# ─────────────────────────────────────────

Write-Host "`n📤 Uploading NGINX configurations..." -ForegroundColor Yellow

function Upload-NginxConfig {
    param(
        [string]$Content,
        [string]$FileName
    )

    $tempFile = [System.IO.Path]::GetTempFileName()
    # Convert CRLF to LF and write as UTF-8 without BOM for Linux compatibility
    $cleanContent = $Content -replace "`r`n", "`n"
    [System.IO.File]::WriteAllText($tempFile, $cleanContent, [System.Text.UTF8Encoding]::new($false))

    Write-Host "  📄 Uploading: $FileName" -ForegroundColor White -NoNewline

    $sshOpts = @("-i", $resolvedKeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes", "-o", "ConnectTimeout=180", "-o", "GSSAPIAuthentication=no")
    scp @sshOpts $tempFile "${SSHUser}@${ServerIP}:/tmp/$FileName" | Out-Null

    if ($LASTEXITCODE -eq 0) {
        ssh @sshOpts "${SSHUser}@${ServerIP}" `
            "sudo mv /tmp/$FileName /etc/nginx/sites-available/$FileName && echo '✅ Moved to sites-available'"
        Write-Host " ✅" -ForegroundColor Green
    } else {
        Write-Host " ❌" -ForegroundColor Red
    }

    Remove-Item $tempFile -Force
}

Upload-NginxConfig -Content $nginxFrontend -FileName "urbeat-frontend.conf"
Upload-NginxConfig -Content $nginxBackend -FileName "urbeat-api.conf"
Upload-NginxConfig -Content $nginxMonitoring -FileName "urbeat-monitoring.conf"

# ─────────────────────────────────────────
# Enable sites and reload NGINX
# ─────────────────────────────────────────

Write-Host "`n🔧 Enabling NGINX sites and testing configuration..." -ForegroundColor Yellow

$nginxSetupScript = @'
#!/bin/bash
set -e

echo "🔗 Enabling NGINX sites..."
sudo ln -sf /etc/nginx/sites-available/urbeat-frontend.conf /etc/nginx/sites-enabled/
sudo ln -sf /etc/nginx/sites-available/urbeat-api.conf /etc/nginx/sites-enabled/

# Remove default site if exists
sudo rm -f /etc/nginx/sites-enabled/default

echo "🔍 Testing NGINX configuration..."
sudo nginx -t

if [ $? -eq 0 ]; then
    echo "✅ NGINX configuration is valid"
    echo "🔄 Reloading NGINX..."
    sudo systemctl reload nginx
    echo "✅ NGINX reloaded successfully"
    sudo systemctl status nginx --no-pager
else
    echo "❌ NGINX configuration has errors!"
    exit 1
fi

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "✅ NGINX configured successfully!"
echo "  🌐 Frontend: http://www.urbeat.com.br"
echo "  🔌 API: http://api.urbeat.com.br"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
'@

$tempNginxScript = [System.IO.Path]::GetTempFileName() + ".sh"
# Convert CRLF to LF and write as UTF-8 without BOM for Linux bash compatibility
$cleanScript = $nginxSetupScript -replace "`r`n", "`n"
[System.IO.File]::WriteAllText($tempNginxScript, $cleanScript, [System.Text.UTF8Encoding]::new($false))

$sshOpts = @("-i", $resolvedKeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes", "-o", "ConnectTimeout=180", "-o", "GSSAPIAuthentication=no")
scp @sshOpts $tempNginxScript "${SSHUser}@${ServerIP}:/tmp/nginx-setup.sh" | Out-Null
ssh @sshOpts "${SSHUser}@${ServerIP}" "chmod +x /tmp/nginx-setup.sh && /tmp/nginx-setup.sh && rm /tmp/nginx-setup.sh"

Remove-Item $tempNginxScript -Force

Write-Host "`n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray
Write-Host "🎉 NGINX configuration completed!" -ForegroundColor Green
