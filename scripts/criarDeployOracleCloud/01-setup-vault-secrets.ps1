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
    [string]$Region = "sa-saopaulo-1",

    # Common parameters passed by deploy-all.ps1 (ignored by this script but required to prevent binding errors)
    [Parameter(Mandatory=$false)]
    [string]$ServerIP,

    [Parameter(Mandatory=$false)]
    [string]$SSHUser,

    [Parameter(Mandatory=$false)]
    [string]$SSHKeyPath
)

# Suppress OCI CLI file permission warnings that break JSON parsing
$env:OCI_CLI_SUPPRESS_FILE_PERMISSIONS_WARNING = "True"

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

# Use tenancy OCID as default compartment if vault is in root compartment
$compartmentId = "ocid1.tenancy.oc1..aaaaaaaah2m3lpf3efb7ulylcs4t3iurlzhjidsgwdp4tjiov2gvxzfdbv2q"

$vaultList = oci kms management vault list --compartment-id $compartmentId --all 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Failed to list vaults. Check OCI CLI configuration." -ForegroundColor Red
    exit 1
}

$vaultData = @((($vaultList | ConvertFrom-Json).data) | Where-Object { $_.'display-name' -eq $VaultName })
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
    --all 2>&1

# OCI CLI outputs JSON line-by-line in PowerShell, so we must join it first
$jsonString = $keyList -join "`n"
$keyData = ($jsonString | ConvertFrom-Json).data | Where-Object { $_.'lifecycle-state' -eq 'ENABLED' }

if (-not $keyData -or $keyData.Count -eq 0) {
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

    # 🖼️ Cloudinary (Image Uploads)
    "CLOUDINARY_CLOUD_NAME"  = "dcolnvyhb"
    "CLOUDINARY_API_KEY"     = "549543485246375"
    "CLOUDINARY_API_SECRET"  = "55CVhToYzFzzP2vA2Lv4FEv5Qg8"
}

if (-not [string]::IsNullOrWhiteSpace($env:URBEAT_INFOBIP_API_KEY)) {
    $secrets["URBEAT_INFOBIP_API_KEY"] = $env:URBEAT_INFOBIP_API_KEY
}
if (-not [string]::IsNullOrWhiteSpace($env:URBEAT_INFOBIP_BASE_URL)) {
    $secrets["URBEAT_INFOBIP_BASE_URL"] = $env:URBEAT_INFOBIP_BASE_URL
}
if (-not [string]::IsNullOrWhiteSpace($env:URBEAT_INFOBIP_SENDER)) {
    $secrets["URBEAT_INFOBIP_SENDER"] = $env:URBEAT_INFOBIP_SENDER
}

# ─────────────────────────────────────────
# Step 4: Fetch all existing secrets once (avoids JMESPath escaping issues in PowerShell)
# ─────────────────────────────────────────

Write-Host "`n🔐 Fetching existing secrets from Oracle Vault..." -ForegroundColor Yellow
$allSecretsRaw = oci vault secret list --compartment-id $COMPARTMENT_OCID --all 2>$null
$jsonString = $allSecretsRaw -join "`n"

$existingSecrets = @{}
if ($jsonString) {
    try {
        $parsed = $jsonString | ConvertFrom-Json
        foreach ($s in $parsed.data) {
            if ($s.'lifecycle-state' -eq 'ACTIVE' -or $s.'lifecycle-state' -eq 'UPDATING') {
                $existingSecrets[$s.'secret-name'] = $s.id
            }
        }
        Write-Host "  ✅ Found $($existingSecrets.Count) existing active secrets." -ForegroundColor Green
    } catch {
        Write-Host "  ⚠️ Warning: Could not parse existing secrets. Error: $_" -ForegroundColor Yellow
    }
}

# ─────────────────────────────────────────
# Step 5: Create or Update Secrets in Vault
# ─────────────────────────────────────────

Write-Host "`n🔐 Processing secrets in Oracle Vault..." -ForegroundColor Yellow
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

$successCount = 0
$failCount = 0

foreach ($secretName in $secrets.Keys) {
    $secretValue = $secrets[$secretName]

    # Encode secret value to Base64
    $secretBytes = [System.Text.Encoding]::UTF8.GetBytes($secretValue)
    $secretBase64 = [System.Convert]::ToBase64String($secretBytes)

    Write-Host "  📝 Processing secret: $secretName" -ForegroundColor White -NoNewline

    $secretOcid = $existingSecrets[$secretName]

    if ($secretOcid) {
        # Secret exists: Skip it (do not update to prevent unnecessary versioning)
        Write-Host " ⏭️ Skipped (already exists)" -ForegroundColor Cyan
        $successCount++
    } else {
        # Secret does not exist: Create it
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
            Write-Host " ❌ Failed: $result" -ForegroundColor Red
            $failCount++
        }
    }
}

# ─────────────────────────────────────────
# Step 6: Export Secret OCIDs to file
# ─────────────────────────────────────────

Write-Host "`n📄 Exporting Secret OCIDs to secrets-map.json..." -ForegroundColor Yellow

if (-not (Test-Path ".\configs")) {
    New-Item -ItemType Directory -Path ".\configs" -Force | Out-Null
}

$secretsMap = @{}
foreach ($secretName in $secrets.Keys) {
    $secretOcid = $existingSecrets[$secretName]
    if (-not $secretOcid) {
        # If it was just created, fetch it one last time using a simple JMESPath
        $secretList = oci vault secret list --compartment-id $COMPARTMENT_OCID --all 2>$null | ConvertFrom-Json
        $secretOcid = @($secretList.data | Where-Object { $_.'secret-name' -eq $secretName } | Select-Object -First 1 -ExpandProperty id)
    }
    $secretsMap[$secretName] = $(if ($secretOcid) { $secretOcid } else { "UNKNOWN" })
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
exit 0
