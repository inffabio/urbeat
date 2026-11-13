<#
.SYNOPSIS
    Retrieves secrets from Oracle Vault and creates .env files on server
.DESCRIPTION
    Fetches all secrets from the Urbeat OCI Vault and creates environment files
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
    [string]$AppDir = "/opt/urbeat",

    # Public Meta App ID (not a secret). Optional: leave empty to keep the manual profile
    # picture update disabled. Pass it explicitly when invoking this script.
    [Parameter(Mandatory=$false)]
    [string]$MetaAppId = ""
)

# Graph API version written to the generated .env. Keep it in sync with
# WhatsAppOptions.DefaultGraphApiVersion (backend/src/Urbeat.Infrastructure/Services/WhatsApp).
$WhatsAppGraphApiVersion = "v26.0"

. (Join-Path $PSScriptRoot "oci-ssh.ps1")
. (Join-Path $PSScriptRoot "deploy-env.ps1")
. (Join-Path $PSScriptRoot "oci-context.ps1")

# Suppress OCI CLI file permission warnings that break JSON parsing
$env:OCI_CLI_SUPPRESS_FILE_PERMISSIONS_WARNING = "True"

Write-Host "🌍 Setting up Urbeat Environment..." -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

if ($MetaAppId -and $MetaAppId -notmatch '^\d+$') {
    Write-Host "❌ MetaAppId must contain digits only." -ForegroundColor Red
    exit 1
}

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
# Mandatory application secrets
# ─────────────────────────────────────────
# RolePromotionChallenge__SigningKey has no default (neither here nor in docker-compose.yml), so
# the API and the workers abort at startup when it is missing. Fail here instead of shipping an
# .env/compose pair that crash-loops on startup.
if ([string]::IsNullOrWhiteSpace($envVars['ROLE_PROMOTION_SIGNING_KEY'])) {
    Write-Host "`n❌ ROLE_PROMOTION_SIGNING_KEY was not resolved from the Vault." -ForegroundColor Red
    Write-Host "   Add its OCID to configs/secrets-map.json; the API and the workers require it at startup." -ForegroundColor Yellow
    exit 1
}

# Resolves a Vault secret by display name when configs/secrets-map.json has no OCID for it.
# The OCID list is fetched once and cached for the whole run.
$vaultSecretsByName = $null
function Resolve-VaultSecretByName {
    param([string]$SecretName)

    if ($null -eq $script:vaultSecretsByName) {
        $script:vaultSecretsByName = @{}
        $compartmentId = $env:OCI_COMPARTMENT_OCID
        if (-not [string]::IsNullOrWhiteSpace($compartmentId)) {
            try {
                $rawList = oci vault secret list --compartment-id $compartmentId --all 2>$null
                $json = $rawList -join "`n"
                if (-not [string]::IsNullOrWhiteSpace($json)) {
                    foreach ($secret in ($json | ConvertFrom-Json).data) {
                        $name = $secret.'secret-name'
                        if ([string]::IsNullOrWhiteSpace($name)) { continue }
                        if ($secret.'lifecycle-state' -in @('ACTIVE', 'UPDATING')) {
                            $script:vaultSecretsByName[$name] = $secret.id
                        }
                    }
                }
            } catch {
                # Best-effort: the caller reports unresolved names without stopping the deploy.
            }
        }
    }

    $ocid = $script:vaultSecretsByName[$SecretName]
    if ([string]::IsNullOrWhiteSpace($ocid)) { return $null }
    return Get-VaultSecretByOcid -SecretOcid $ocid
}

# ─────────────────────────────────────────
# WhatsApp Cloud API (Meta): map the four Vault display names already used by the
# application and only emit configuration for values that actually resolved. A missing
# secret disables the webhook instead of writing an empty .env value; production then
# starts normally and the webhook endpoints stay fail-closed (403/401).
# ─────────────────────────────────────────
$whatsAppSecretBindings = [ordered]@{
    "WhatsApp__WebhookVerifyToken" = "WhatsAppWebhooksToken"
    "WhatsApp__AppSecret"          = "MetaChaveApp"
    "WhatsApp__WabaId"             = "MetaWhatsappWABA"
    "WhatsApp__AccessToken"        = "MetaTokenWhatsapp"
}

$whatsAppEnvLines = [System.Collections.Generic.List[string]]::new()
$missingWhatsAppSecrets = [System.Collections.Generic.List[string]]::new()

foreach ($binding in $whatsAppSecretBindings.GetEnumerator()) {
    $vaultName = $binding.Value
    $value = $envVars[$vaultName]
    if ([string]::IsNullOrWhiteSpace($value)) {
        $value = Resolve-VaultSecretByName -SecretName $vaultName
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            $envVars[$vaultName] = $value
        }
    }

    if ([string]::IsNullOrWhiteSpace($value)) {
        $missingWhatsAppSecrets.Add($vaultName)
        continue
    }

    $whatsAppEnvLines.Add("$($binding.Key)=$value")
}

$whatsAppWebhookEnabled = $missingWhatsAppSecrets.Count -eq 0
$whatsAppEnvLines.Add("WhatsApp__WebhookEnabled=$(if ($whatsAppWebhookEnabled) { 'true' } else { 'false' })")
$whatsAppEnvLines.Add("WhatsApp__PhoneNumberId=1436364406224525")
$whatsAppEnvLines.Add("WhatsApp__GraphApiVersion=$WhatsAppGraphApiVersion")
if (-not [string]::IsNullOrWhiteSpace($MetaAppId)) {
    $whatsAppEnvLines.Add("WhatsApp__MetaAppId=$MetaAppId")
}
$whatsAppEnvBlock = $whatsAppEnvLines -join "`n"

if ($missingWhatsAppSecrets.Count -gt 0) {
    Write-Host "  ⚠️ WhatsApp webhook disabled; unresolved Vault secrets: $($missingWhatsAppSecrets -join ', ')" -ForegroundColor Yellow
    Write-Host "     Add their OCIDs to configs/secrets-map.json, or set OCI_COMPARTMENT_OCID so they are resolved by display name." -ForegroundColor Yellow
}

# ─────────────────────────────────────────
# Admin recovery (break-glass): the operator publishes two Vault secrets whose display names are
# exactly the .NET configuration keys, so no secrets-map.json OCID is required.
#   AdminRecovery__Enabled -> "true"/"false" (anything else keeps the feature disabled)
#   AdminRecovery__Key     -> single-use recovery key, never printed and never defaulted
# Values are only forwarded to the generated .env; this script logs the secret name and whether it
# resolved, never the secret content.
# ─────────────────────────────────────────
$adminRecoveryEnabledSecretName = "AdminRecovery__Enabled"
$adminRecoveryKeySecretName = "AdminRecovery__Key"

$adminRecoveryEnabledRaw = $envVars[$adminRecoveryEnabledSecretName]
if ([string]::IsNullOrWhiteSpace($adminRecoveryEnabledRaw)) {
    $adminRecoveryEnabledRaw = Resolve-VaultSecretByName -SecretName $adminRecoveryEnabledSecretName
}
$adminRecoveryEnabledValue = ConvertTo-AdminRecoveryEnabledValue -Value $adminRecoveryEnabledRaw
$adminRecoveryEnabledResolved = -not [string]::IsNullOrWhiteSpace($adminRecoveryEnabledRaw)

$adminRecoveryKeyValue = $envVars[$adminRecoveryKeySecretName]
if ([string]::IsNullOrWhiteSpace($adminRecoveryKeyValue)) {
    $adminRecoveryKeyValue = Resolve-VaultSecretByName -SecretName $adminRecoveryKeySecretName
}
$adminRecoveryKeyResolved = -not [string]::IsNullOrWhiteSpace($adminRecoveryKeyValue)
if (-not $adminRecoveryKeyResolved) {
    # Empty value: the endpoint stays unavailable and the API starts normally.
    $adminRecoveryKeyValue = ""
}

Write-Host "  🔐 $adminRecoveryEnabledSecretName : $(if ($adminRecoveryEnabledResolved) { 'resolved' } else { 'not resolved (feature stays disabled)' })" -ForegroundColor $(if ($adminRecoveryEnabledResolved) { 'Green' } else { 'Yellow' })
Write-Host "  🔐 $adminRecoveryKeySecretName : $(if ($adminRecoveryKeyResolved) { 'resolved' } else { 'not resolved (endpoint stays unavailable)' })" -ForegroundColor $(if ($adminRecoveryKeyResolved) { 'Green' } else { 'Yellow' })

# ─────────────────────────────────────────
# Step 2: Create .env file content
# ─────────────────────────────────────────

Write-Host "`n📝 Generating .env files..." -ForegroundColor Yellow

# JwtOptions binds ExpirationMinutes; the Vault secret stays in hours.
$jwtExpiryMinutes = ConvertTo-JwtExpirationMinutes -Hours $envVars['URBEAT_JWT_EXPIRY_HOURS']

# Main .env file
$mainEnv = @"
# ═══════════════════════════════════════════════════════════
# URBEAT APPLICATION - ENVIRONMENT VARIABLES
# Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
# Source: Oracle Vault (display name is matched case-insensitively)
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
JWT_EXPIRY_MINUTES=$jwtExpiryMinutes

# ─── Role Promotion Signing ──────────────────────────────────
# Dedicated signing key for role promotion challenges. docker-compose.yml forwards it without a
# default, so an empty value keeps the API/workers from starting.
ROLE_PROMOTION_SIGNING_KEY=$($envVars['ROLE_PROMOTION_SIGNING_KEY'])

# ─── Admin Password Recovery (break-glass) ───────────────────
# Resolved from the Vault display names AdminRecovery__Enabled/AdminRecovery__Key by exact name
# (no secrets-map.json OCID needed). The feature is disabled by default; docker-compose.yml
# forwards both values and the key has no functional fallback. After a single recovery, publish
# Enabled=false and remove or rotate the key.
AdminRecovery__Enabled=$adminRecoveryEnabledValue
AdminRecovery__Key=$adminRecoveryKeyValue

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

# ─── WhatsApp Cloud API (Meta) ───────────────────────────────
# Only secrets resolved from the Vault are written (WhatsAppWebhooksToken/MetaChaveApp/
# MetaWhatsappWABA/MetaTokenWhatsapp). Empty lines are never emitted: a missing secret sets
# WhatsApp__WebhookEnabled=false and the API starts with the webhook fail-closed.
$whatsAppEnvBlock

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
Send-PortKnock -ServerIP $ServerIP
scp @scpOpts $tempDirScript "${SSHUser}@${ServerIP}:/tmp/setup-dirs.sh" | Out-Null
Send-PortKnock -ServerIP $ServerIP
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

Send-PortKnock -ServerIP $ServerIP
scp @scpOpts $tempEnvFile "${SSHUser}@${ServerIP}:/tmp/.env.urbeat" | Out-Null
Send-PortKnock -ServerIP $ServerIP
ssh @sshOpts "${SSHUser}@${ServerIP}" "cp /tmp/.env.urbeat $AppDir/.env && chmod 600 $AppDir/.env && rm /tmp/.env.urbeat"

Remove-Item $tempEnvFile -Force

Write-Host "✅ Environment file uploaded and secured (chmod 600)" -ForegroundColor Green

Write-Host "`n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray
Write-Host "🎉 Environment setup completed!" -ForegroundColor Green
