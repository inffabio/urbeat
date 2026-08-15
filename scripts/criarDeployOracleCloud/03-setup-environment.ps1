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
    [string]$SSHUser = "dexter",

    [Parameter(Mandatory=$false)]
    [int]$SSHPort = 2208,

    [Parameter(Mandatory=$false)]
    [string]$SSHKeyPath = "~/.ssh/id_ed25519",

    [Parameter(Mandatory=$false)]
    [string]$VaultName = "urbeat",

    [Parameter(Mandatory=$false)]
    [string]$AppDir = "/opt/urbeat"
)

# Suppress OCI CLI file permission warnings that break JSON parsing
$env:OCI_CLI_SUPPRESS_FILE_PERMISSIONS_WARNING = "True"

Write-Host "🌍 Setting up Urbeat Environment..." -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

# Resolve SSH key path
$resolvedKeyPath = (Resolve-Path $SSHKeyPath -ErrorAction SilentlyContinue).Path
if (-not $resolvedKeyPath) {
    $resolvedKeyPath = Resolve-Path "$env:USERPROFILE\.ssh\id_rsa" -ErrorAction SilentlyContinue
}

# ─────────────────────────────────────────
# Step 1: Fetch secrets using secrets-map.json
# ─────────────────────────────────────────

Write-Host "`n🔐 Fetching secrets from Oracle Vault using secrets-map.json..." -ForegroundColor Yellow

$secretsMapPath = Join-Path $PSScriptRoot "configs\secrets-map.json"
if (-not (Test-Path $secretsMapPath)) {
    Write-Host "❌ secrets-map.json not found at: $secretsMapPath" -ForegroundColor Red
    exit 1
}

$secretsMap = Get-Content $secretsMapPath -Raw | ConvertFrom-Json
Write-Host "✅ Loaded $(($secretsMap.PSObject.Properties | Measure-Object).Count) secret mappings" -ForegroundColor Green

function Get-VaultSecretByOcid {
    param([string]$SecretOcid)

    try {
        $result = oci secrets secret-bundle get --secret-id $SecretOcid 2>&1
        $content = ($result -join "`n" | ConvertFrom-Json).data.'secret-bundle-content'.content
        if ([string]::IsNullOrEmpty($content) -or $content -eq "null") { return $null }
        return [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($content))
    } catch {
        return $null
    }
}

$envVars = @{}
foreach ($prop in $secretsMap.PSObject.Properties) {
    $envName = $prop.Name
    $ocid = $prop.Value
    Write-Host "  🔑 Fetching: $envName" -ForegroundColor White -NoNewline
    $value = Get-VaultSecretByOcid -SecretOcid $ocid
    if ($value) {
        $envVars[$envName] = $value
        Write-Host " ✅" -ForegroundColor Green
    } else {
        Write-Host " ❌ (not found or error)" -ForegroundColor Red
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
FRONTEND_BASE_URL=$($envVars['URBEAT_FRONTEND_URL'])
FRONTEND_URL=$($envVars['URBEAT_FRONTEND_URL'])
API_URL=$($envVars['URBEAT_API_URL'])
CORS_ORIGINS=$($envVars['URBEAT_CORS_ORIGINS'])

# ─── PostgreSQL ──────────────────────────────────────────────
DB_HOST=$($envVars['URBEAT_DB_HOST'])
DB_PORT=$($envVars['URBEAT_DB_PORT'])
DB_NAME=$($envVars['URBEAT_DB_NAME'])
DB_USER=$($envVars['URBEAT_DB_USER'])
DB_PASSWORD=$($envVars['URBEAT_DB_PASSWORD'])
URBEAT_DB_CONNECTION=$($envVars['URBEAT_DB_CONNECTION'])
DATABASE_URL=postgresql://$($envVars['URBEAT_DB_USER']):$([System.Uri]::EscapeDataString($envVars['URBEAT_DB_PASSWORD']))@$($envVars['URBEAT_DB_HOST']):$($envVars['URBEAT_DB_PORT'])/$($envVars['URBEAT_DB_NAME'])
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

# ─── Cloudinary (Image Uploads) ──────────────────────────────
CLOUDINARY_CLOUD_NAME=$($envVars['CLOUDINARY_CLOUD_NAME'])
CLOUDINARY_API_KEY=$($envVars['CLOUDINARY_API_KEY'])
CLOUDINARY_API_SECRET=$($envVars['CLOUDINARY_API_SECRET'])
Cloudinary__CloudName=$($envVars['CLOUDINARY_CLOUD_NAME'])
Cloudinary__ApiKey=$($envVars['CLOUDINARY_API_KEY'])
Cloudinary__ApiSecret=$($envVars['CLOUDINARY_API_SECRET'])

# ─── Customer Verification SMS ───────────────────────────────
CUSTOMER_VERIFICATION_CHANNEL=Sms
CUSTOMER_VERIFICATION_SMS_PROVIDER=Infobip
INFOBIP_BASE_URL=$(if ($envVars['URBEAT_INFOBIP_BASE_URL']) { $envVars['URBEAT_INFOBIP_BASE_URL'] } else { 'https://m9zq59.api.infobip.com' })
INFOBIP_API_KEY=$($envVars['URBEAT_INFOBIP_API_KEY'])
INFOBIP_SENDER=$($envVars['URBEAT_INFOBIP_SENDER'])
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
sudo mkdir -p $AppDir/{backend,frontend,configs,data,logs,ssl,downloads}
sudo mkdir -p $AppDir/data/{postgres,grafana,prometheus}
sudo mkdir -p $AppDir/configs/{nginx,prometheus,grafana/provisioning/{datasources,dashboards}}
sudo mkdir -p $AppDir/logs/{backend,frontend,nginx}

# Set permissions
sudo chown -R __SSH_USER__:__SSH_USER__ $AppDir
sudo chmod -R 755 $AppDir
sudo chmod -R 777 $AppDir/data
sudo chmod -R 777 $AppDir/logs

echo "✅ Directory structure created:"
find $AppDir -type d | head -30

echo "✅ Directories created successfully!"
"@

$tempDirScript = [System.IO.Path]::GetTempFileName() + ".sh"
# Convert CRLF to LF and write as UTF-8 without BOM for Linux bash compatibility
$cleanDirScript = $setupDirsScript -replace "`r`n", "`n"
$cleanDirScript = $cleanDirScript.Replace("__SSH_USER__", $SSHUser)
[System.IO.File]::WriteAllText($tempDirScript, $cleanDirScript, [System.Text.UTF8Encoding]::new($false))

$sshOpts = @("-p", $SSHPort, "-i", $resolvedKeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes", "-o", "ConnectTimeout=180", "-o", "GSSAPIAuthentication=no")
$scpOpts = @("-P", $SSHPort, "-i", $resolvedKeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes", "-o", "ConnectTimeout=180", "-o", "GSSAPIAuthentication=no")
scp @scpOpts $tempDirScript "${SSHUser}@${ServerIP}:/tmp/setup-dirs.sh" | Out-Null
ssh @sshOpts "${SSHUser}@${ServerIP}" "chmod +x /tmp/setup-dirs.sh && /tmp/setup-dirs.sh && rm /tmp/setup-dirs.sh"

Remove-Item $tempDirScript -Force

# ─────────────────────────────────────────
# Step 4: Upload .env file to server
# ─────────────────────────────────────────

Write-Host "`n📤 Uploading environment file to server..." -ForegroundColor Yellow

$tempEnvFile = [System.IO.Path]::GetTempFileName()
# Convert CRLF to LF and write as UTF-8 without BOM for Linux compatibility
$cleanEnv = $mainEnv -replace "`r`n", "`n"
[System.IO.File]::WriteAllText($tempEnvFile, $cleanEnv, [System.Text.UTF8Encoding]::new($false))

scp @scpOpts $tempEnvFile "${SSHUser}@${ServerIP}:/tmp/.env.urbeat" | Out-Null
ssh @sshOpts "${SSHUser}@${ServerIP}" "cp /tmp/.env.urbeat $AppDir/.env && chmod 600 $AppDir/.env && rm /tmp/.env.urbeat"

Remove-Item $tempEnvFile -Force

Write-Host "✅ Environment file uploaded and secured (chmod 600)" -ForegroundColor Green

Write-Host "`n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray
Write-Host "🎉 Environment setup completed!" -ForegroundColor Green
