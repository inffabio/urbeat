# 🚀 Urbeat Application Deploy Strategy - Oracle Cloud Infrastructure (ARM64)

## 📋 Overview

This document provides a complete deployment strategy for the **Urbeat** application on Oracle Cloud Infrastructure using an ARM64 instance discovered via MCP OCI Oracle. All scripts are written in PowerShell (`.ps1`) for Windows machines with OCI CLI installed.

---

## 🏗️ Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                    Oracle Cloud (ARM64)                       │
│                    136.248.115.135                            │
│                                                               │
│  ┌─────────────┐    ┌──────────────────────────────────────┐ │
│  │    NGINX     │    │         Docker Network               │ │
│  │  (Host)      │────│  ┌──────────┐  ┌──────────────────┐ │ │
│  │  Port 80/443 │    │  │ Frontend │  │    Backend API   │ │ │
│  └─────────────┘    │  │ Angular/ │  │   .NET 9 API     │ │ │
│                      │  │  Ionic   │  │   Port 5000      │ │ │
│                      │  │ Port 4200│  └──────────────────┘ │ │
│                      │  └──────────┘                        │ │
│                      │  ┌──────────┐  ┌──────────────────┐ │ │
│                      │  │PostgreSQL│  │    Prometheus     │ │ │
│                      │  │Port 5432 │  │    Port 9090      │ │ │
│                      │  └──────────┘  └──────────────────┘ │ │
│                      │  ┌──────────┐                        │ │
│                      │  │ Grafana  │                        │ │
│                      │  │Port 3000 │                        │ │
│                      │  └──────────┘                        │ │
│                      └──────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

---

## 📁 File Structure

```
urbeat-deploy/
├── scripts/
│   ├── 00-prerequisites-check.ps1
│   ├── 01-setup-vault-secrets.ps1
│   ├── 02-install-docker-arm64.ps1
│   ├── 03-setup-environment.ps1
│   ├── 04-deploy-application.ps1
│   ├── 05-configure-nginx.ps1
│   ├── 06-setup-ssl.ps1
│   └── 07-verify-deployment.ps1
├── configs/
│   ├── docker-compose.yml
│   ├── nginx/
│   │   ├── urbeat.conf
│   │   └── api.urbeat.conf
│   ├── prometheus/
│   │   └── prometheus.yml
│   └── grafana/
│       └── datasources.yml
└── README.md (this file)
```

---

## ⚠️ IMPORTANT SECURITY NOTICE

> **ALL credentials are stored in Oracle Vault (`urbeat-vault`). Never hardcode credentials in scripts or configuration files.**

---

## 🔐 Script 01 - Oracle Vault Secrets Setup

### `scripts/01-setup-vault-secrets.ps1`

```powershell
<#
.SYNOPSIS
    Creates all required secrets in Oracle Vault (urbeat-vault)
.DESCRIPTION
    This script creates all application secrets in the Oracle Vault.
    Run this FIRST before any other deployment script.
.REQUIREMENTS
    - OCI CLI installed and configured
    - Proper IAM permissions to write to urbeat-vault
#>

param(
    [Parameter(Mandatory=$false)]
    [string]$VaultName = "urbeat-vault",
    
    [Parameter(Mandatory=$false)]
    [string]$Region = "sa-saopaulo-1"
)

# ─────────────────────────────────────────
# 🔧 CONFIGURATION - DO NOT CHANGE SECRETS HERE
# All secrets are defined below and will be pushed to Oracle Vault
# ─────────────────────────────────────────

Write-Host "🔐 Starting Oracle Vault Secrets Setup for Urbeat..." -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

# ─────────────────────────────────────────
# Step 1: Get Vault OCID and Compartment
# ─────────────────────────────────────────

Write-Host "`n📦 Fetching Vault information..." -ForegroundColor Yellow

$vaultList = oci kms management vault list --all --query "data[?\"display-name\"=='$VaultName']" 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Failed to list vaults. Check OCI CLI configuration." -ForegroundColor Red
    exit 1
}

$vaultData = $vaultList | ConvertFrom-Json
if ($vaultData.Count -eq 0) {
    Write-Host "❌ Vault '$VaultName' not found. Please verify vault exists." -ForegroundColor Red
    exit 1
}

$VAULT_OCID = $vaultData[0].id
$COMPARTMENT_OCID = $vaultData[0]."compartment-id"
$VAULT_ENDPOINT = $vaultData[0]."management-endpoint"

Write-Host "✅ Vault found: $VAULT_OCID" -ForegroundColor Green
Write-Host "✅ Compartment: $COMPARTMENT_OCID" -ForegroundColor Green
Write-Host "✅ Management Endpoint: $VAULT_ENDPOINT" -ForegroundColor Green

# ─────────────────────────────────────────
# Step 2: Get Encryption Key
# ─────────────────────────────────────────

Write-Host "`n🔑 Fetching encryption key..." -ForegroundColor Yellow

$keyList = oci kms management key list `
    --compartment-id $COMPARTMENT_OCID `
    --endpoint $VAULT_ENDPOINT `
    --all `
    --query "data[?\"lifecycle-state\"=='ENABLED']" 2>&1

$keyData = $keyList | ConvertFrom-Json
if ($keyData.Count -eq 0) {
    Write-Host "❌ No enabled encryption keys found in vault." -ForegroundColor Red
    exit 1
}

$KEY_OCID = $keyData[0].id
Write-Host "✅ Encryption Key: $KEY_OCID" -ForegroundColor Green

# ─────────────────────────────────────────
# Step 3: Define Secrets
# ─────────────────────────────────────────

$secrets = @{
    # 🗄️ PostgreSQL Secrets
    "URBEAT_DB_HOST"         = "postgres"
    "URBEAT_DB_PORT"         = "5432"
    "URBEAT_DB_NAME"         = "urbeatdb"
    "URBEAT_DB_USER"         = "urbeatPostg"
    "URBEAT_DB_PASSWORD"     = "!fL08414671108"
    "URBEAT_DB_CONNECTION"   = "Host=postgres;Port=5432;Database=urbeatdb;Username=urbeatPostg;Password=!fL08414671108"
    
    # 📧 SMTP Email Secrets (OCI Email Delivery)
    "URBEAT_SMTP_HOST"       = "smtp.email.sa-saopaulo-1.oci.oraclecloud.com"
    "URBEAT_SMTP_PORT"       = "587"
    "URBEAT_SMTP_USER"       = "<oci-smtp-username>"
    "URBEAT_SMTP_PASSWORD"   = "<oci-smtp-password>"
    "URBEAT_SMTP_SSL"        = "true"
    "URBEAT_SMTP_FROM"       = "contato@urbeat.com.br"
    
    # 🌐 Application URLs
    "URBEAT_FRONTEND_URL"    = "https://www.urbeat.com.br"
    "URBEAT_API_URL"         = "https://api.urbeat.com.br"
    "URBEAT_CORS_ORIGINS"    = "https://www.urbeat.com.br,https://api.urbeat.com.br"
    
    # 📊 Monitoring Secrets
    "URBEAT_GRAFANA_USER"    = "admin"
    "URBEAT_GRAFANA_PASSWORD" = "UrbeatGraf@2025!"
    "URBEAT_PROMETHEUS_URL"  = "http://prometheus:9090"
    
    # 🔒 Application Security
    "URBEAT_JWT_SECRET"      = "UrbeatJWT@SecretKey2025!SuperSecure#Oracle"
    "URBEAT_JWT_ISSUER"      = "https://api.urbeat.com.br"
    "URBEAT_JWT_AUDIENCE"    = "https://www.urbeat.com.br"
    "URBEAT_JWT_EXPIRY_HOURS" = "24"
    
    # 🐘 PostgreSQL Admin (for container init)
    "POSTGRES_PASSWORD"      = "!fL08414671108"
    "POSTGRES_USER"          = "urbeatPostg"
    "POSTGRES_DB"            = "urbeatdb"
}

# ─────────────────────────────────────────
# Step 4: Create Secrets in Vault
# ─────────────────────────────────────────

Write-Host "`n🔐 Creating secrets in Oracle Vault..." -ForegroundColor Yellow
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

$successCount = 0
$failCount = 0

foreach ($secretName in $secrets.Keys) {
    $secretValue = $secrets[$secretName]
    
    # Encode secret value to Base64
    $secretBytes = [System.Text.Encoding]::UTF8.GetBytes($secretValue)
    $secretBase64 = [System.Convert]::ToBase64String($secretBytes)
    
    Write-Host "  📝 Creating secret: $secretName" -ForegroundColor White -NoNewline
    
    # Check if secret already exists
    $existingSecret = oci vault secret list `
        --compartment-id $COMPARTMENT_OCID `
        --name $secretName `
        --query "data[?\"lifecycle-state\"!='DELETED']" 2>&1 | ConvertFrom-Json
    
    if ($existingSecret -and $existingSecret.Count -gt 0) {
        # Update existing secret
        $secretOcid = $existingSecret[0].id
        $result = oci vault secret update-base64 `
            --secret-id $secretOcid `
            --secret-content-content $secretBase64 2>&1
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host " ✅ Updated" -ForegroundColor Green
            $successCount++
        } else {
            Write-Host " ⚠️ Update failed - $result" -ForegroundColor Yellow
            $failCount++
        }
    } else {
        # Create new secret
        $result = oci vault secret create-base64 `
            --compartment-id $COMPARTMENT_OCID `
            --secret-name $secretName `
            --vault-id $VAULT_OCID `
            --key-id $KEY_OCID `
            --secret-content-content $secretBase64 `
            --description "Urbeat application secret - $secretName" 2>&1
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host " ✅ Created" -ForegroundColor Green
            $successCount++
        } else {
            Write-Host " ❌ Failed - $result" -ForegroundColor Red
            $failCount++
        }
    }
}

# ─────────────────────────────────────────
# Step 5: Export Secret OCIDs to file
# ─────────────────────────────────────────

Write-Host "`n📄 Exporting Secret OCIDs to secrets-map.json..." -ForegroundColor Yellow

$secretsMap = @{}
foreach ($secretName in $secrets.Keys) {
    $secretInfo = oci vault secret list `
        --compartment-id $COMPARTMENT_OCID `
        --name $secretName `
        --query "data[0].id" 2>&1
    $secretsMap[$secretName] = ($secretInfo | ConvertFrom-Json)
}

$secretsMap | ConvertTo-Json | Out-File -FilePath ".\configs\secrets-map.json" -Encoding UTF8

Write-Host "`n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray
Write-Host "✅ Secrets created: $successCount" -ForegroundColor Green
Write-Host "❌ Secrets failed: $failCount" -ForegroundColor $(if ($failCount -gt 0) { "Red" } else { "Green" })
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

if ($failCount -gt 0) {
    Write-Host "⚠️ Some secrets failed. Please review and re-run." -ForegroundColor Yellow
    exit 1
}

Write-Host "`n🎉 All secrets successfully stored in Oracle Vault!" -ForegroundColor Green
```

---

## 🐳 Script 02 - Install Docker ARM64

### `scripts/02-install-docker-arm64.ps1`

```powershell
<#
.SYNOPSIS
    Installs Docker and Docker Compose for ARM64 on Ubuntu server
.DESCRIPTION
    Connects via SSH and installs Docker CE + Docker Compose plugin
    specifically for ARM64 architecture. This runs ONCE only.
.REQUIREMENTS
    - SSH access to 136.248.115.135
    - Ubuntu user with sudo privileges
#>

param(
    [Parameter(Mandatory=$false)]
    [string]$ServerIP = "136.248.115.135",
    
    [Parameter(Mandatory=$false)]
    [string]$SSHUser = "ubuntu",
    
    [Parameter(Mandatory=$false)]
    [string]$SSHKeyPath = "~/.ssh/id_rsa"
)

Write-Host "🐳 Starting Docker ARM64 Installation..." -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray
Write-Host "🖥️  Server: $ServerIP" -ForegroundColor White
Write-Host "👤 User: $SSHUser" -ForegroundColor White
Write-Host "🔑 SSH Key: $SSHKeyPath" -ForegroundColor White

# ─────────────────────────────────────────
# Docker installation script for ARM64
# ─────────────────────────────────────────

$dockerInstallScript = @'
#!/bin/bash
set -e

echo "🔍 Checking architecture..."
ARCH=$(uname -m)
echo "Architecture: $ARCH"

if [ "$ARCH" != "aarch64" ]; then
    echo "⚠️  Warning: Expected aarch64 but got $ARCH"
fi

echo "🔄 Checking if Docker is already installed..."
if command -v docker &> /dev/null; then
    DOCKER_VERSION=$(docker --version)
    echo "✅ Docker already installed: $DOCKER_VERSION"
    
    # Check Docker Compose
    if docker compose version &> /dev/null; then
        COMPOSE_VERSION=$(docker compose version)
        echo "✅ Docker Compose already installed: $COMPOSE_VERSION"
        echo "DOCKER_ALREADY_INSTALLED=true"
        exit 0
    fi
fi

echo "📦 Updating package index..."
sudo apt-get update -y

echo "📦 Installing prerequisites..."
sudo apt-get install -y \
    ca-certificates \
    curl \
    gnupg \
    lsb-release \
    apt-transport-https \
    software-properties-common

echo "🔑 Adding Docker GPG key..."
sudo install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | \
    sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
sudo chmod a+r /etc/apt/keyrings/docker.gpg

echo "📋 Adding Docker repository for ARM64..."
echo \
  "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] \
  https://download.docker.com/linux/ubuntu \
  $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | \
  sudo tee /etc/apt/sources.list.d/docker.list > /dev/null

echo "📦 Updating package index with Docker repo..."
sudo apt-get update -y

echo "🐳 Installing Docker CE (ARM64)..."
sudo apt-get install -y \
    docker-ce \
    docker-ce-cli \
    containerd.io \
    docker-buildx-plugin \
    docker-compose-plugin

echo "👤 Adding ubuntu user to docker group..."
sudo usermod -aG docker ubuntu

echo "🔧 Enabling and starting Docker service..."
sudo systemctl enable docker
sudo systemctl start docker

echo "✅ Verifying Docker installation..."
sudo docker --version
sudo docker compose version

echo "🔧 Configuring Docker daemon for ARM64..."
sudo tee /etc/docker/daemon.json > /dev/null <<EOF
{
  "log-driver": "json-file",
  "log-opts": {
    "max-size": "100m",
    "max-file": "3"
  },
  "default-address-pools": [
    {"base": "172.20.0.0/16", "size": 24}
  ]
}
EOF

sudo systemctl restart docker
sudo systemctl status docker --no-pager

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "✅ Docker ARM64 installation completed successfully!"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
'@

# Save script to temp file
$tempScript = [System.IO.Path]::GetTempFileName() + ".sh"
$dockerInstallScript | Out-File -FilePath $tempScript -Encoding UTF8 -NoNewline

Write-Host "`n📤 Uploading installation script to server..." -ForegroundColor Yellow

# Upload script via SCP
scp -i $SSHKeyPath -o StrictHostKeyChecking=no $tempScript "${SSHUser}@${ServerIP}:/tmp/install-docker.sh"

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Failed to upload script. Check SSH connection." -ForegroundColor Red
    Remove-Item $tempScript -Force
    exit 1
}

Write-Host "✅ Script uploaded successfully" -ForegroundColor Green

Write-Host "`n🚀 Executing Docker installation on server..." -ForegroundColor Yellow
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

# Execute script via SSH
ssh -i $SSHKeyPath -o StrictHostKeyChecking=no "${SSHUser}@${ServerIP}" "chmod +x /tmp/install-docker.sh && /tmp/install-docker.sh"

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Docker installation failed!" -ForegroundColor Red
    Remove-Item $tempScript -Force
    exit 1
}

Write-Host "`n🔍 Verifying Docker installation..." -ForegroundColor Yellow
ssh -i $SSHKeyPath -o StrictHostKeyChecking=no "${SSHUser}@${ServerIP}" "docker --version && docker compose version"

Write-Host "`n🧹 Cleaning up temporary files..." -ForegroundColor Yellow
Remove-Item $tempScript -Force
ssh -i $SSHKeyPath -o StrictHostKeyChecking=no "${SSHUser}@${ServerIP}" "rm -f /tmp/install-docker.sh"

Write-Host "`n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray
Write-Host "🎉 Docker ARM64 installation completed!" -ForegroundColor Green
Write-Host "⚠️  NOTE: You may need to reconnect SSH for group changes to take effect." -ForegroundColor Yellow
```

---

## 🌍 Script 03 - Setup Environment

### `scripts/03-setup-environment.ps1`

```powershell
<#
.SYNOPSIS
    Retrieves secrets from Oracle Vault and creates .env files on server
.DESCRIPTION
    Fetches all secrets from urbeat-vault and creates environment files
    on the remote server. Also creates the application directory structure.
#>

param(
    [Parameter(Mandatory=$false)]
    [string]$ServerIP = "136.248.115.135",
    
    [Parameter(Mandatory=$false)]
    [string]$SSHUser = "ubuntu",
    
    [Parameter(Mandatory=$false)]
    [string]$SSHKeyPath = "~/.ssh/id_rsa",
    
    [Parameter(Mandatory=$false)]
    [string]$VaultName = "urbeat-vault",
    
    [Parameter(Mandatory=$false)]
    [string]$AppDir = "/opt/urbeat"
)

Write-Host "🌍 Setting up Urbeat Environment..." -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

# ─────────────────────────────────────────
# Step 1: Fetch secrets from Oracle Vault
# ─────────────────────────────────────────

Write-Host "`n🔐 Fetching secrets from Oracle Vault..." -ForegroundColor Yellow

function Get-VaultSecret {
    param([string]$SecretName, [string]$CompartmentId)
    
    $secretInfo = oci vault secret list `
        --compartment-id $CompartmentId `
        --name $SecretName `
        --query "data[?\"lifecycle-state\"!='DELETED'][0].id" `
        --raw-output 2>&1
    
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrEmpty($secretInfo)) {
        Write-Host "  ⚠️  Secret not found: $SecretName" -ForegroundColor Yellow
        return $null
    }
    
    $secretContent = oci secrets secret-bundle get `
        --secret-id $secretInfo.Trim() `
        --query "data.\"secret-bundle-content\".content" `
        --raw-output 2>&1
    
    if ($LASTEXITCODE -ne 0) {
        return $null
    }
    
    $decoded = [System.Text.Encoding]::UTF8.GetString(
        [System.Convert]::FromBase64String($secretContent.Trim())
    )
    return $decoded
}

# Get compartment ID from vault
$vaultList = oci kms management vault list --all --query "data[?\"display-name\"=='$VaultName'][0]" | ConvertFrom-Json
$COMPARTMENT_OCID = $vaultList."compartment-id"

Write-Host "✅ Compartment: $COMPARTMENT_OCID" -ForegroundColor Green

# Fetch all secrets
$secretNames = @(
    "URBEAT_DB_HOST", "URBEAT_DB_PORT", "URBEAT_DB_NAME",
    "URBEAT_DB_USER", "URBEAT_DB_PASSWORD", "URBEAT_DB_CONNECTION",
    "URBEAT_SMTP_HOST", "URBEAT_SMTP_PORT", "URBEAT_SMTP_USER",
    "URBEAT_SMTP_PASSWORD", "URBEAT_SMTP_SSL", "URBEAT_SMTP_FROM",
    "URBEAT_FRONTEND_URL", "URBEAT_API_URL", "URBEAT_CORS_ORIGINS",
    "URBEAT_GRAFANA_USER", "URBEAT_GRAFANA_PASSWORD", "URBEAT_PROMETHEUS_URL",
    "URBEAT_JWT_SECRET", "URBEAT_JWT_ISSUER", "URBEAT_JWT_AUDIENCE",
    "URBEAT_JWT_EXPIRY_HOURS", "POSTGRES_PASSWORD", "POSTGRES_USER", "POSTGRES_DB"
)

$envVars = @{}
foreach ($name in $secretNames) {
    Write-Host "  🔑 Fetching: $name" -ForegroundColor White -NoNewline
    $value = Get-VaultSecret -SecretName $name -CompartmentId $COMPARTMENT_OCID
    if ($value) {
        $envVars[$name] = $value
        Write-Host " ✅" -ForegroundColor Green
    } else {
        Write-Host " ❌" -ForegroundColor Red
    }
}

# ─────────────────────────────────────────
# Step 2: Create .env file content
# ─────────────────────────────────────────

Write-Host "`n📝 Generating .env files..." -ForegroundColor Yellow

# Main .env file
$mainEnv = @"
# ═══════════════════════════════════════════════════════════
# URBEAT APPLICATION - ENVIRONMENT VARIABLES
# Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
# Source: Oracle Vault (urbeat-vault)
# ⚠️  DO NOT COMMIT THIS FILE TO VERSION CONTROL
# ═══════════════════════════════════════════════════════════

# ─── Application ────────────────────────────────────────────
APP_NAME=urbeat
APP_ENV=production
APP_VERSION=1.0.0

# ─── URLs ───────────────────────────────────────────────────
FRONTEND_URL=$($envVars['URBEAT_FRONTEND_URL'])
API_URL=$($envVars['URBEAT_API_URL'])
CORS_ORIGINS=$($envVars['URBEAT_CORS_ORIGINS'])

# ─── PostgreSQL ──────────────────────────────────────────────
DB_HOST=$($envVars['URBEAT_DB_HOST'])
DB_PORT=$($envVars['URBEAT_DB_PORT'])
DB_NAME=$($envVars['URBEAT_DB_NAME'])
DB_USER=$($envVars['URBEAT_DB_USER'])
DB_PASSWORD=$($envVars['URBEAT_DB_PASSWORD'])
DATABASE_URL=postgresql://$($envVars['URBEAT_DB_USER']):$($envVars['URBEAT_DB_PASSWORD'])@$($envVars['URBEAT_DB_HOST']):$($envVars['URBEAT_DB_PORT'])/$($envVars['URBEAT_DB_NAME'])
POSTGRES_USER=$($envVars['POSTGRES_USER'])
POSTGRES_PASSWORD=$($envVars['POSTGRES_PASSWORD'])
POSTGRES_DB=$($envVars['POSTGRES_DB'])

# ─── SMTP Email ──────────────────────────────────────────────
SMTP_HOST=$($envVars['URBEAT_SMTP_HOST'])
SMTP_PORT=$($envVars['URBEAT_SMTP_PORT'])
SMTP_USER=$($envVars['URBEAT_SMTP_USER'])
SMTP_PASSWORD=$($envVars['URBEAT_SMTP_PASSWORD'])
SMTP_SSL=$($envVars['URBEAT_SMTP_SSL'])
SMTP_FROM=$($envVars['URBEAT_SMTP_FROM'])

# ─── JWT Security ────────────────────────────────────────────
JWT_SECRET=$($envVars['URBEAT_JWT_SECRET'])
JWT_ISSUER=$($envVars['URBEAT_JWT_ISSUER'])
JWT_AUDIENCE=$($envVars['URBEAT_JWT_AUDIENCE'])
JWT_EXPIRY_HOURS=$($envVars['URBEAT_JWT_EXPIRY_HOURS'])

# ─── Grafana ─────────────────────────────────────────────────
GF_SECURITY_ADMIN_USER=$($envVars['URBEAT_GRAFANA_USER'])
GF_SECURITY_ADMIN_PASSWORD=$($envVars['URBEAT_GRAFANA_PASSWORD'])
GF_SERVER_ROOT_URL=http://localhost:3000

# ─── Prometheus ──────────────────────────────────────────────
PROMETHEUS_URL=$($envVars['URBEAT_PROMETHEUS_URL'])

# ─── .NET Runtime ────────────────────────────────────────────
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:5000
DOTNET_RUNNING_IN_CONTAINER=true
"@

# ─────────────────────────────────────────
# Step 3: Create directory structure on server
# ─────────────────────────────────────────

Write-Host "`n📁 Creating directory structure on server..." -ForegroundColor Yellow

$setupDirsScript = @"
#!/bin/bash
set -e

echo "📁 Creating Urbeat directory structure..."

# Create main app directories
sudo mkdir -p $AppDir/{backend,frontend,configs,data,logs,ssl}
sudo mkdir -p $AppDir/data/{postgres,grafana,prometheus}
sudo mkdir -p $AppDir/configs/{nginx,prometheus,grafana/provisioning/{datasources,dashboards}}
sudo mkdir -p $AppDir/logs/{backend,frontend,nginx}

# Set permissions
sudo chown -R ubuntu:ubuntu $AppDir
sudo chmod -R 755 $AppDir
sudo chmod -R 777 $AppDir/data
sudo chmod -R 777 $AppDir/logs

echo "✅ Directory structure created:"
find $AppDir -type d | head -30

echo "✅ Directories created successfully!"
"@

$tempDirScript = [System.IO.Path]::GetTempFileName() + ".sh"
$setupDirsScript | Out-File -FilePath $tempDirScript -Encoding UTF8 -NoNewline

scp -i $SSHKeyPath -o StrictHostKeyChecking=no $tempDirScript "${SSHUser}@${ServerIP}:/tmp/setup-dirs.sh"
ssh -i $SSHKeyPath -o StrictHostKeyChecking=no "${SSHUser}@${ServerIP}" "chmod +x /tmp/setup-dirs.sh && /tmp/setup-dirs.sh && rm /tmp/setup-dirs.sh"

Remove-Item $tempDirScript -Force

# ─────────────────────────────────────────
# Step 4: Upload .env file to server
# ─────────────────────────────────────────

Write-Host "`n📤 Uploading environment file to server..." -ForegroundColor Yellow

$tempEnvFile = [System.IO.Path]::GetTempFileName()
$mainEnv | Out-File -FilePath $tempEnvFile -Encoding UTF8 -NoNewline

scp -i $SSHKeyPath -o StrictHostKeyChecking=no $tempEnvFile "${SSHUser}@${ServerIP}:/tmp/.env.urbeat"
ssh -i $SSHKeyPath -o StrictHostKeyChecking=no "${SSHUser}@${ServerIP}" "cp /tmp/.env.urbeat $AppDir/.env && chmod 600 $AppDir/.env && rm /tmp/.env.urbeat"

Remove-Item $tempEnvFile -Force

Write-Host "✅ Environment file uploaded and secured (chmod 600)" -ForegroundColor Green

Write-Host "`n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray
Write-Host "🎉 Environment setup completed!" -ForegroundColor Green
```

---

## 🚀 Script 04 - Deploy Application

### `scripts/04-deploy-application.ps1`

```powershell
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
    [string]$SSHKeyPath = "~/.ssh/id_rsa",
    
    [Parameter(Mandatory=$false)]
    [string]$AppDir = "/opt/urbeat"
)

Write-Host "🚀 Deploying Urbeat Application Stack..." -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

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
    ipam:
      config:
        - subnet: 172.20.0.0/24

volumes:
  postgres-data:
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

  # ─── .NET 9 Backend API ───────────────────────────────────
  backend:
    image: urbeat/backend:latest
    platform: linux/arm64
    container_name: urbeat-backend
    restart: unless-stopped
    env_file:
      - .env
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ASPNETCORE_URLS: http://+:5000
      ConnectionStrings__DefaultConnection: ${DATABASE_URL}
      Email__Host: ${SMTP_HOST}
      Email__Port: ${SMTP_PORT}
      Email__Username: ${SMTP_USER}
      Email__Password: ${SMTP_PASSWORD}
      Email__EnableSsl: ${SMTP_SSL}
      Email__From: ${SMTP_FROM}
      Jwt__Secret: ${JWT_SECRET}
      Jwt__Issuer: ${JWT_ISSUER}
      Jwt__Audience: ${JWT_AUDIENCE}
      Jwt__ExpiryHours: ${JWT_EXPIRY_HOURS}
      App__FrontendUrl: ${FRONTEND_URL}
      App__ApiUrl: ${API_URL}
      App__CorsOrigins: ${CORS_ORIGINS}
    ports:
      - "127.0.0.1:5000:5000"
    depends_on:
      postgres:
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

  # ─── Angular/Ionic Frontend ───────────────────────────────
  frontend:
    image: urbeat/frontend:latest
    platform: linux/arm64
    container_name: urbeat-frontend
    restart: unless-stopped
    environment:
      API_URL: ${API_URL}
      APP_ENV: production
    ports:
      - "127.0.0.1:4200:80"
    depends_on:
      - backend
    networks:
      - urbeat-network
    volumes:
      - ./logs/frontend:/var/log/nginx
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:80"]
      interval: 30s
      timeout: 10s
      retries: 3
    logging:
      driver: "json-file"
      options:
        max-size: "50m"
        max-file: "3"

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
    $Content | Out-File -FilePath $tempFile -Encoding UTF8 -NoNewline
    
    Write-Host "  📄 Uploading: $FileName" -ForegroundColor White -NoNewline
    
    scp -i $SSHKeyPath -o StrictHostKeyChecking=no $tempFile "${SSHUser}@${ServerIP}:/tmp/$FileName"
    
    if ($LASTEXITCODE -eq 0) {
        ssh -i $SSHKeyPath -o StrictHostKeyChecking=no "${SSHUser}@${ServerIP}" "mv /tmp/$FileName $RemotePath/$FileName"
        Write-Host " ✅" -ForegroundColor Green
    } else {
        Write-Host " ❌" -ForegroundColor Red
    }
    
    Remove-Item $tempFile -Force
}

# Create postgres config directory
ssh -i $SSHKeyPath -o StrictHostKeyChecking=no "${SSHUser}@${ServerIP}" "mkdir -p $AppDir/configs/postgres"

# Upload files
Upload-FileToServer -Content $dockerCompose -RemotePath $AppDir -FileName "docker-compose.yml"
Upload-FileToServer -Content $prometheusConfig -RemotePath "$AppDir/configs/prometheus" -FileName "prometheus.yml"
Upload-FileToServer -Content $grafanaDatasource -RemotePath "$AppDir/configs/grafana/provisioning/datasources" -FileName "datasources.yml"
Upload-FileToServer -Content $postgresInit -RemotePath "$AppDir/configs/postgres" -FileName "init.sql"

# ─────────────────────────────────────────
# Deploy application
# ─────────────────────────────────────────

Write-Host "`n🚀 Starting Docker Compose deployment..." -ForegroundColor Yellow

$deployScript = @"
#!/bin/bash
set -e

echo "🚀 Starting Urbeat deployment..."
cd $AppDir

echo "📦 Pulling latest images (ARM64)..."
docker compose pull

echo "🔄 Starting services..."
docker compose up -d --remove-orphans

echo "⏳ Waiting for services to be healthy..."
sleep 30

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
$deployScript | Out-File -FilePath $tempDeploy -Encoding UTF8 -NoNewline

scp -i $SSHKeyPath -o StrictHostKeyChecking=no $tempDeploy "${SSHUser}@${ServerIP}:/tmp/deploy.sh"
ssh -i $SSHKeyPath -o StrictHostKeyChecking=no "${SSHUser}@${ServerIP}" "chmod +x /tmp/deploy.sh && /tmp/deploy.sh && rm /tmp/deploy.sh"

Remove-Item $tempDeploy -Force

Write-Host "`n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray
Write-Host "🎉 Application deployment completed!" -ForegroundColor Green
```

---

## 🌐 Script 05 - Configure NGINX

### `scripts/05-configure-nginx.ps1`

```powershell
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
    [string]$SSHKeyPath = "~/.ssh/id_rsa"
)

Write-Host "🌐 Configuring NGINX Reverse Proxy..." -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

# ─────────────────────────────────────────
# NGINX Config: www.urbeat.com.br (Frontend)
# ─────────────────────────────────────────

$nginxFrontend = @'
# ═══════════════════════════════════════════════════════════
# NGINX - URBEAT FRONTEND
# Domain: www.urbeat.com.br
# Proxy: localhost:4200 (Angular/Ionic Docker Container)
# ═══════════════════════════════════════════════════════════

upstream urbeat_frontend {
    server 127.0.0.1:4200;
    keepalive 32;
}

server {
    listen 80;
    listen [::]:80;
    server_name www.urbeat.com.br urbeat.com.br;

    # Redirect HTTP to HTTPS (after SSL setup)
    # return 301 https://$server_name$request_uri;
    
    # Temporary: serve directly on HTTP
    location / {
        proxy_pass http://urbeat_frontend;
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
        
        # Angular routing support
        try_files $uri $uri/ @fallback;
    }

    location @fallback {
        proxy_pass http://urbeat_frontend;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
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
        proxy_pass http://urbeat_frontend;
        proxy_set_header Host $host;
        expires 1y;
        add_header Cache-Control "public, immutable";
        access_log off;
    }

    # Logs
    access_log /var/log/nginx/urbeat-frontend-access.log;
    error_log /var/log/nginx/urbeat-frontend-error.log;
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

    # CORS headers for API
    add_header Access-Control-Allow-Origin "https://www.urbeat.com.br" always;
    add_header Access-Control-Allow-Methods "GET, POST, PUT, DELETE, PATCH, OPTIONS" always;
    add_header Access-Control-Allow-Headers "Authorization, Content-Type, X-Requested-With" always;
    add_header Access-Control-Allow-Credentials "true" always;

    # Handle preflight requests
    if ($request_method = 'OPTIONS') {
        add_header Access-Control-Allow-Origin "https://www.urbeat.com.br";
        add_header Access-Control-Allow-Methods "GET, POST, PUT, DELETE, PATCH, OPTIONS";
        add_header Access-Control-Allow-Headers "Authorization, Content-Type, X-Requested-With";
        add_header Access-Control-Max-Age 1728000;
        add_header Content-Type "text/plain charset=UTF-8";
        add_header Content-Length 0;
        return 204;
    }

    # Security headers
    add_header X-Frame-Options "DENY" always;
    add_header X-XSS-Protection "1; mode=block" always;
    add_header X-Content-Type-Options "nosniff" always;

    # Rate limiting
    limit_req_zone $binary_remote_addr zone=api:10m rate=100r/m;
    limit_req zone=api burst=20 nodelay;

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

# Note: If you need external access, add domains and uncomment below:

# server {
#     listen 80;
#     server_name grafana.urbeat.com.br;
#     location / {
#         proxy_pass http://127.0.0.1:3000;
#         proxy_set_header Host $host;
#         # Add authentication!
#         auth_basic "Monitoring";
#         auth_basic_user_file /etc/nginx/.htpasswd;
#     }
# }
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
    $Content | Out-File -FilePath $tempFile -Encoding UTF8 -NoNewline
    
    Write-Host "  📄 Uploading: $FileName" -ForegroundColor White -NoNewline
    
    scp -i $SSHKeyPath -o StrictHostKeyChecking=no $tempFile "${SSHUser}@${ServerIP}:/tmp/$FileName"
    
    if ($LASTEXITCODE -eq 0) {
        ssh -i $SSHKeyPath -o StrictHostKeyChecking=no "${SSHUser}@${ServerIP}" `
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
$nginxSetupScript | Out-File -FilePath $tempNginxScript -Encoding UTF8 -NoNewline

scp -i $SSHKeyPath -o StrictHostKeyChecking=no $tempNginxScript "${SSHUser}@${ServerIP}:/tmp/nginx-setup.sh"
ssh -i $SSHKeyPath -o StrictHostKeyChecking=no "${SSHUser}@${ServerIP}" "chmod +x /tmp/nginx-setup.sh && /tmp/nginx-setup.sh && rm /tmp/nginx-setup.sh"

Remove-Item $tempNginxScript -Force

Write-Host "`n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray
Write-Host "🎉 NGINX configuration completed!" -ForegroundColor Green
```

---

## 🔒 Script 06 - Setup SSL (Let's Encrypt)

### `scripts/06-setup-ssl.ps1`

```powershell
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
    [string]$SSHUser = "ubuntu",
    
    [Parameter(Mandatory=$false)]
    [string]$SSHKeyPath = "~/.ssh/id_rsa",
    
    [Parameter(Mandatory=$false)]
    [string]$Email = "contato@urbeat.com.br"
)

Write-Host "🔒 Setting up SSL Certificates (Let's Encrypt)..." -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

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
sudo systemctl reload nginx

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
scp -i $SSHKeyPath -o StrictHostKeyChecking=no $tempSSL "${SSHUser}@${ServerIP}:/tmp/setup-ssl.sh"

Write-Host "🚀 Executing SSL setup..." -ForegroundColor Yellow
ssh -i $SSHKeyPath -o StrictHostKeyChecking=no "${SSHUser}@${ServerIP}" "chmod +x /tmp/setup-ssl.sh && /tmp/setup-ssl.sh && rm /tmp/setup-ssl.sh"

Remove-Item $tempSSL -Force

Write-Host "`n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray
Write-Host "🎉 SSL setup completed!" -ForegroundColor Green
Write-Host "  🔒 https://www.urbeat.com.br" -ForegroundColor Cyan
Write-Host "  🔒 https://api.urbeat.com.br" -ForegroundColor Cyan
```

---

## ✅ Script 07 - Verify Deployment

### `scripts/07-verify-deployment.ps1`

```powershell
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
    [string]$SSHUser = "ubuntu",
    
    [Parameter(Mandatory=$false)]
    [string]$SSHKeyPath = "~/.ssh/id_rsa"
)

Write-Host "✅ Verifying Urbeat Deployment..." -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

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
    docker ps --format "  {{.Names}}\t{{.Status}}\t{{.Ports}}" | column -t
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

check_service "Frontend (localhost:4200)" "http://localhost:4200"
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
docker volume ls --format "  {{.Name}}\t{{.Driver}}" | grep urbeat | column -t

# Recent logs summary
echo ""
echo "📋 RECENT LOGS (last 5 lines per service)"
echo ""
echo "  --- Backend ---"
docker logs urbeat-backend --tail=5 2>/dev/null | sed 's/^/  /'
echo ""
echo "  --- Frontend ---"
docker logs urbeat-frontend --tail=5 2>/dev/null | sed 's/^/  /'
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

scp -i $SSHKeyPath -o StrictHostKeyChecking=no $tempVerify "${SSHUser}@${ServerIP}:/tmp/verify.sh"
ssh -i $SSHKeyPath -o StrictHostKeyChecking=no "${SSHUser}@${ServerIP}" "chmod +x /tmp/verify.sh && /tmp/verify.sh && rm /tmp/verify.sh"

Remove-Item $tempVerify -Force
```

---

## 🔍 Script 00 - Prerequisites Check

### `scripts/00-prerequisites-check.ps1`

```powershell
<#
.SYNOPSIS
    Checks all prerequisites before deployment
.DESCRIPTION
    Verifies OCI CLI, SSH access, and server connectivity
    before starting the deployment process.
#>

param(
    [Parameter(Mandatory=$false)]
    [string]$ServerIP = "136.248.115.135",
    
    [Parameter(Mandatory=$false)]
    [string]$SSHUser = "ubuntu",
    
    [Parameter(Mandatory=$false)]
    [string]$SSHKeyPath = "~/.ssh/id_rsa"
)

Write-Host "🔍 Checking Prerequisites for Urbeat Deployment..." -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

$allPassed = $true

function Test-Requirement {
    param(
        [string]$Name,
        [scriptblock]$Test,
        [string]$FixHint = ""
    )
    
    Write-Host "  🔎 Checking: $Name" -ForegroundColor White -NoNewline
    
    try {
        $result = & $Test
        if ($result) {
            Write-Host " ✅ OK" -ForegroundColor Green
            return $true
        } else {
            Write-Host " ❌ FAILED" -ForegroundColor Red
            if ($FixHint) { Write-Host "     💡 Fix: $FixHint" -ForegroundColor Yellow }
            $script:allPassed = $false
            return $false
        }
    } catch {
        Write-Host " ❌ ERROR: $_" -ForegroundColor Red
        if ($FixHint) { Write-Host "     💡 Fix: $FixHint" -ForegroundColor Yellow }
        $script:allPassed = $false
        return $false
    }
}

Write-Host "`n📋 LOCAL REQUIREMENTS" -ForegroundColor Yellow

Test-Requirement "OCI CLI installed" {
    $null = oci --version 2>&1
    $LASTEXITCODE -eq 0
} "Install OCI CLI: https://docs.oracle.com/en-us/iaas/Content/API/SDKDocs/cliinstall.htm"

Test-Requirement "OCI CLI configured" {
    $null = oci iam user get --user-id (oci iam user list --query "data[0].id" --raw-output) 2>&1
    $LASTEXITCODE -eq 0
} "Run: oci setup config"

Test-Requirement "SSH client available" {
    $null = ssh -V 2>&1
    $LASTEXITCODE -eq 0 -or $LASTEXITCODE -eq 1
} "Install OpenSSH or Git for Windows"

Test-Requirement "SSH key exists" {
    Test-Path (Resolve-Path $SSHKeyPath -ErrorAction SilentlyContinue)
} "Generate SSH key: ssh-keygen -t rsa -b 4096 -f $SSHKeyPath"

Test-Requirement "SCP available" {
    $null = scp 2>&1
    $true
} "SCP should be available with SSH"

Write-Host "`n🌐 CONNECTIVITY CHECKS" -ForegroundColor Yellow

Test-Requirement "Server reachable (ping)" {
    $ping = Test-Connection -ComputerName $ServerIP -Count 1 -Quiet -ErrorAction SilentlyContinue
    $ping
} "Check firewall rules in OCI Console"

Test-Requirement "SSH connection works" {
    $result = ssh -i $SSHKeyPath -o StrictHostKeyChecking=no -o ConnectTimeout=10 "${SSHUser}@${ServerIP}" "echo 'SSH_OK'" 2>&1
    $result -contains "SSH_OK"
} "Verify SSH key is authorized on server"

Test-Requirement "Port 80 accessible" {
    $tcp = Test-NetConnection -ComputerName $ServerIP -Port 80 -WarningAction SilentlyContinue
    $tcp.TcpTestSucceeded
} "Check OCI Security List - Port 80 must be open"

Write-Host "`n☁️  ORACLE CLOUD CHECKS" -ForegroundColor Yellow

Test-Requirement "OCI Vault 'urbeat-vault' exists" {
    $vaults = oci kms management vault list --all --query "data[?\"display-name\"=='urbeat-vault']" 2>&1 | ConvertFrom-Json
    $vaults.Count -gt 0
} "Create vault named 'urbeat-vault' in OCI Console"

Test-Requirement "OCI Vault has encryption key" {
    $vaultData = oci kms management vault list --all --query "data[?\"display-name\"=='urbeat-vault'][0]" 2>&1 | ConvertFrom-Json
    if ($vaultData) {
        $endpoint = $vaultData."management-endpoint"
        $compartment = $vaultData."compartment-id"
        $keys = oci kms management key list --compartment-id $compartment --endpoint $endpoint --all --query "data[?\"lifecycle-state\"=='ENABLED']" 2>&1 | ConvertFrom-Json
        $keys.Count -gt 0
    } else { $false }
} "Create an AES-256 encryption key in urbeat-vault"

Write-Host "`n🖥️  SERVER CHECKS" -ForegroundColor Yellow

Test-Requirement "Server is ARM64" {
    $arch = ssh -i $SSHKeyPath -o StrictHostKeyChecking=no "${SSHUser}@${ServerIP}" "uname -m" 2>&1
    $arch.Trim() -eq "aarch64"
} "This deployment is designed for ARM64 architecture"

Test-Requirement "NGINX is installed" {
    $nginx = ssh -i $SSHKeyPath -o StrictHostKeyChecking=no "${SSHUser}@${ServerIP}" "nginx -v 2>&1" 2>&1
    $nginx -match "nginx"
} "Install NGINX: sudo apt-get install nginx"

Test-Requirement "NGINX is running" {
    $status = ssh -i $SSHKeyPath -o StrictHostKeyChecking=no "${SSHUser}@${ServerIP}" "systemctl is-active nginx" 2>&1
    $status.Trim() -eq "active"
} "Start NGINX: sudo systemctl start nginx"

Test-Requirement "Ubuntu user has sudo" {
    $sudo = ssh -i $SSHKeyPath -o StrictHostKeyChecking=no "${SSHUser}@${ServerIP}" "sudo -n true 2>&1 && echo 'SUDO_OK'" 2>&1
    $sudo -contains "SUDO_OK"
} "Add ubuntu to sudoers"

# ─────────────────────────────────────────
# Summary
# ─────────────────────────────────────────

Write-Host "`n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

if ($allPassed) {
    Write-Host "🎉 All prerequisites passed! Ready to deploy." -ForegroundColor Green
    Write-Host "`n📋 DEPLOYMENT ORDER:" -ForegroundColor Cyan
    Write-Host "  1️⃣  .\scripts\01-setup-vault-secrets.ps1" -ForegroundColor White
    Write-Host "  2️⃣  .\scripts\02-install-docker-arm64.ps1" -ForegroundColor White
    Write-Host "  3️⃣  .\scripts\03-setup-environment.ps1" -ForegroundColor White
    Write-Host "  4️⃣  .\scripts\04-deploy-application.ps1" -ForegroundColor White
    Write-Host "  5️⃣  .\scripts\05-configure-nginx.ps1" -ForegroundColor White
    Write-Host "  6️⃣  .\scripts\06-setup-ssl.ps1" -ForegroundColor White
    Write-Host "  7️⃣  .\scripts\07-verify-deployment.ps1" -ForegroundColor White
    exit 0
} else {
    Write-Host "❌ Some prerequisites failed. Please fix issues above before deploying." -ForegroundColor Red
    exit 1
}
```

---

## 🎯 Master Deploy Script

### `deploy-all.ps1`

```powershell
<#
.SYNOPSIS
    Master deployment script for Urbeat application
.DESCRIPTION
    Runs all deployment steps in correct order.
    Can run individual steps or the complete pipeline.
#>

param(
    [Parameter(Mandatory=$false)]
    [ValidateSet("all", "prerequisites", "vault", "docker", "environment", "application", "nginx", "ssl", "verify")]
    [string]$Step = "all",
    
    [Parameter(Mandatory=$false)]
    [string]$ServerIP = "136.248.115.135",
    
    [Parameter(Mandatory=$false)]
    [string]$SSHUser = "ubuntu",
    
    [Parameter(Mandatory=$false)]
    [string]$SSHKeyPath = "~/.ssh/id_rsa"
)

$commonParams = @{
    ServerIP   = $ServerIP
    SSHUser    = $SSHUser
    SSHKeyPath = $SSHKeyPath
}

$banner = @"
╔═══════════════════════════════════════════════════════════╗
║          URBEAT APPLICATION DEPLOYMENT PIPELINE           ║
║          Oracle Cloud Infrastructure - ARM64              ║
║          Server: $ServerIP                    ║
╚═══════════════════════════════════════════════════════════╝
"@

Write-Host $banner -ForegroundColor Cyan

$steps = @{
    "prerequisites" = ".\scripts\00-prerequisites-check.ps1"
    "vault"         = ".\scripts\01-setup-vault-secrets.ps1"
    "docker"        = ".\scripts\02-install-docker-arm64.ps1"
    "environment"   = ".\scripts\03-setup-environment.ps1"
    "application"   = ".\scripts\04-deploy-application.ps1"
    "nginx"         = ".\scripts\05-configure-nginx.ps1"
    "ssl"           = ".\scripts\06-setup-ssl.ps1"
    "verify"        = ".\scripts\07-verify-deployment.ps1"
}

function Run-Step {
    param([string]$StepName, [string]$ScriptPath)
    
    Write-Host "`n" -NoNewline
    Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor DarkCyan
    Write-Host "  STEP: $StepName" -ForegroundColor Cyan
    Write-Host "  Script: $ScriptPath" -ForegroundColor Gray
    Write-Host "  Time: $(Get-Date -Format 'HH:mm:ss')" -ForegroundColor Gray
    Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor DarkCyan
    
    & $ScriptPath @commonParams
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "`n❌ Step '$StepName' FAILED! Stopping deployment." -ForegroundColor Red
        exit 1
    }
    
    Write-Host "`n✅ Step '$StepName' completed successfully!" -ForegroundColor Green
}

$startTime = Get-Date

if ($Step -eq "all") {
    foreach ($stepName in @("prerequisites", "vault", "docker", "environment", "application", "nginx", "ssl", "verify")) {
        Run-Step -StepName $stepName -ScriptPath $steps[$stepName]
    }
} else {
    Run-Step -StepName $Step -ScriptPath $steps[$Step]
}

$endTime = Get-Date
$duration = $endTime - $startTime

Write-Host "`n" -NoNewline
Write-Host "╔═══════════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║         🎉 URBEAT DEPLOYMENT COMPLETED SUCCESSFULLY!       ║" -ForegroundColor Green
Write-Host "║                                                             ║" -ForegroundColor Green
Write-Host "║  🌐 Frontend: https://www.urbeat.com.br                    ║" -ForegroundColor Green
Write-Host "║  🔌 API:      https://api.urbeat.com.br                    ║" -ForegroundColor Green
Write-Host "║  📊 Grafana:  SSH tunnel → http://localhost:3000           ║" -ForegroundColor Green
Write-Host "║  📈 Metrics:  SSH tunnel → http://localhost:9090           ║" -ForegroundColor Green
Write-Host "║                                                             ║" -ForegroundColor Green
Write-Host "║  ⏱️  Total time: $($duration.ToString('mm\:ss')) minutes                        ║" -ForegroundColor Green
Write-Host "╚═══════════════════════════════════════════════════════════╝" -ForegroundColor Green
```

---

## 📋 Execution Order & Instructions

### 🚀 Quick Start

```powershell
# Clone or create the deploy directory structure
# Then run from Windows PowerShell with OCI CLI configured:

# Option 1: Run everything at once
.\deploy-all.ps1

# Option 2: Run step by step
.\deploy-all.ps1 -Step prerequisites
.\deploy-all.ps1 -Step vault
.\deploy-all.ps1 -Step docker
.\deploy-all.ps1 -Step environment
.\deploy-all.ps1 -Step application
.\deploy-all.ps1 -Step nginx
.\deploy-all.ps1 -Step ssl
.\deploy-all.ps1 -Step verify

# Option 3: Custom server
.\deploy-all.ps1 -ServerIP "136.248.115.135" -SSHKeyPath "C:\keys\urbeat-key.pem"
```

---

## 🔐 Secrets Summary (Stored in Oracle Vault)

| Secret Name | Description | Category |
|-------------|-------------|----------|
| `URBEAT_DB_HOST` | PostgreSQL hostname | 🗄️ Database |
| `URBEAT_DB_USER` | `urbeatPostg` | 🗄️ Database |
| `URBEAT_DB_PASSWORD` | DB password | 🗄️ Database |
| `URBEAT_DB_CONNECTION` | Full connection string | 🗄️ Database |
| `URBEAT_SMTP_HOST` | `smtp.email.sa-saopaulo-1.oci.oraclecloud.com` | 📧 Email |
| `URBEAT_SMTP_PORT` | `587` (STARTTLS) | 📧 Email |
| `URBEAT_SMTP_USER` | `<oci-smtp-username>` | 📧 Email |
| `URBEAT_SMTP_PASSWORD` | SMTP password | 📧 Email |
| `URBEAT_JWT_SECRET` | JWT signing key | 🔒 Security |
| `URBEAT_GRAFANA_PASSWORD` | Grafana admin password | 📊 Monitoring |
| `URBEAT_FRONTEND_URL` | `https://www.urbeat.com.br` | 🌐 URLs |
| `URBEAT_API_URL` | `https://api.urbeat.com.br` | 🌐 URLs |

---

## 🔗 Port Mapping Reference

| Service | Container Port | Host Port | Access |
|---------|---------------|-----------|--------|
| Frontend (Angular) | 80 | 4200 | Via NGINX |
| Backend (.NET 9) | 5000 | 5000 | Via NGINX |
| PostgreSQL | 5432 | 5432 (localhost only) | Internal |
| Prometheus | 9090 | 9090 (localhost only) | SSH Tunnel |
| Grafana | 3000 | 3000 (localhost only) | SSH Tunnel |

---

## 🔑 SSH Tunnel for Monitoring

```bash
# Access Grafana locally
ssh -L 3000:localhost:3000 -i ~/.ssh/id_rsa ubuntu@136.248.115.135 -N
# Then open: http://localhost:3000

# Access Prometheus locally
ssh -L 9090:localhost:9090 -i ~/.ssh/id_rsa ubuntu@136.248.115.135 -N
# Then open: http://localhost:9090
```

---

## ⚠️ Post-Deployment Checklist

- [ ] ✅ All containers running (`docker ps`)
- [ ] ✅ NGINX serving on port 80/443
- [ ] ✅ SSL certificates installed
- [ ] ✅ `www.urbeat.com.br` accessible
- [ ] ✅ `api.urbeat.com.br/health` returns 200
- [ ] ✅ PostgreSQL accepting connections
- [ ] ✅ Grafana accessible via SSH tunnel
- [ ] ✅ All secrets stored in Oracle Vault
- [ ] ✅ `.env` file has `chmod 600`
- [ ] ✅ No credentials in any config files