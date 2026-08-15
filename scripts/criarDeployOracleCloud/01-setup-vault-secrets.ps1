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
    [string]$VaultName = "urbeat",

    [Parameter(Mandatory=$false)]
    [string]$Region = "sa-saopaulo-1",

    [Parameter(Mandatory=$false)]
    [string]$SecretsFile = $env:URBEAT_VAULT_SECRETS_FILE,

    # Common parameters passed by deploy-all.ps1 (ignored by this script but required to prevent binding errors)
    [Parameter(Mandatory=$false)]
    [string]$ServerIP,

    [Parameter(Mandatory=$false)]
    [string]$SSHUser,

    [Parameter(Mandatory=$false)]
    [int]$SSHPort,

    [Parameter(Mandatory=$false)]
    [string]$SSHKeyPath
)

# Suppress OCI CLI file permission warnings that break JSON parsing
$env:OCI_CLI_SUPPRESS_FILE_PERMISSIONS_WARNING = "True"

# Secret values must come from a local, ignored JSON file. The file path may
# also be supplied through URBEAT_VAULT_SECRETS_FILE.
if ([string]::IsNullOrWhiteSpace($SecretsFile)) {
    Write-Error "SecretsFile is required. Use -SecretsFile with an unversioned local JSON file or set URBEAT_VAULT_SECRETS_FILE."
    exit 1
}
$SecretsFile = [System.IO.Path]::GetFullPath($SecretsFile)
if (-not (Test-Path -LiteralPath $SecretsFile -PathType Leaf)) {
    Write-Error "Secrets file not found: $SecretsFile"
    exit 1
}

try {
    $secrets = @{}
    $localSecrets = Get-Content -LiteralPath $SecretsFile -Raw | ConvertFrom-Json
    foreach ($property in $localSecrets.PSObject.Properties) {
        if ([string]::IsNullOrWhiteSpace([string]$property.Value)) {
            Write-Error "Secret '$($property.Name)' is empty in the local secrets file."
            exit 1
        }
        $secrets[$property.Name] = [string]$property.Value
    }
} catch {
    Write-Error "Could not parse the local secrets file."
    exit 1
}
if ($secrets.Count -eq 0) {
    Write-Error "The local secrets file does not contain any secrets."
    exit 1
}

Write-Host "🔐 Starting Oracle Vault Secrets Setup for Urbeat..." -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

# ─────────────────────────────────────────
# Step 1: Get Vault OCID and Compartment
# ─────────────────────────────────────────

Write-Host "`n📦 Fetching Vault information..." -ForegroundColor Yellow

# Use tenancy OCID as default compartment if vault is in root compartment
$compartmentId = $env:OCI_COMPARTMENT_OCID
if ([string]::IsNullOrWhiteSpace($compartmentId)) {
    Write-Error "OCI_COMPARTMENT_OCID is required."
    exit 1
}

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

Write-Host "✅ Vault found and management endpoint resolved." -ForegroundColor Green

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
Write-Host "✅ Encryption key resolved." -ForegroundColor Green

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
            Write-Host " ❌ Failed (OCI command returned an error)" -ForegroundColor Red
            $failCount++
        }
    }
}

# ─────────────────────────────────────────
# Step 6: Export Secret OCIDs to file
# ─────────────────────────────────────────

Write-Host "`n📄 Exporting Secret OCIDs to secrets-map.json..." -ForegroundColor Yellow

if (-not (Test-Path (Join-Path $PSScriptRoot "configs"))) {
    New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot "configs") -Force | Out-Null
}

$secretsMap = @{}
foreach ($secretName in $secrets.Keys) {
    $secretOcid = $existingSecrets[$secretName]
    if (-not $secretOcid) {
        # If it was just created, fetch it one last time using a simple JMESPath
        $secretList = oci vault secret list --compartment-id $COMPARTMENT_OCID --all 2>$null | ConvertFrom-Json
        $secretOcid = @($secretList.data | Where-Object { $_.'secret-name' -eq $secretName } | Select-Object -First 1 -ExpandProperty id)
    }
    if ([string]::IsNullOrWhiteSpace($secretOcid) -or $secretOcid -notmatch '^ocid1\.vaultsecret\.') {
        Write-Host "  ❌ Could not resolve an OCID for $secretName" -ForegroundColor Red
        $failCount++
        continue
    }
    $secretsMap[$secretName] = $secretOcid
}

if ($failCount -gt 0) {
    Write-Error "Secret processing failed; secrets-map.json was not changed."
    exit 1
}

$secretsMap | ConvertTo-Json | Out-File -FilePath (Join-Path $PSScriptRoot "configs\secrets-map.json") -Encoding UTF8

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
