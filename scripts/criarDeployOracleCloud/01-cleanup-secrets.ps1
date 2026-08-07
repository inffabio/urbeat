<#
.SYNOPSIS
    Cleans up existing Urbeat secrets in Oracle Vault to prevent duplication/update errors.
.DESCRIPTION
    Finds all secrets matching URBEAT_* or POSTGRES_* and schedules them for deletion.
    This allows the main deployment script to create them fresh without conflicts.
#>

param(
    [Parameter(Mandatory=$false)]
    [string]$VaultName = "urbeat-vault",

    [Parameter(Mandatory=$false)]
    [string]$Region = "sa-saopaulo-1"
)

$env:OCI_CLI_SUPPRESS_FILE_PERMISSIONS_WARNING = "True"

Write-Host "🧹 Starting Oracle Vault Secrets Cleanup for Urbeat..." -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

$compartmentId = "ocid1.tenancy.oc1..aaaaaaaah2m3lpf3efb7ulylcs4t3iurlzhjidsgwdp4tjiov2gvxzfdbv2q"

Write-Host "`n📦 Fetching all active secrets in compartment..." -ForegroundColor Yellow

# Get all active secrets
$secretsRaw = oci vault secret list --compartment-id $compartmentId --all --query "data[?\"lifecycle-state\"=='ACTIVE'].{name:\"secret-name\", id:id}" 2>$null
$jsonLines = $secretsRaw | Where-Object { $_ -match '^\s*[\[\{]' }
$jsonString = $jsonLines -join "`n"

if (-not $jsonString) {
    Write-Host "⚠️ No active secrets found or failed to parse JSON." -ForegroundColor Yellow
    exit 0
}

$secrets = $jsonString | ConvertFrom-Json

$targetPrefixes = @("URBEAT_", "POSTGRES_")
$deletedCount = 0
$skippedCount = 0

Write-Host "`n🗑️  Identifying and deleting target secrets..." -ForegroundColor Yellow
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

foreach ($secret in $secrets) {
    $name = $secret.name
    $id = $secret.id

    $isTarget = $false
    foreach ($prefix in $targetPrefixes) {
        if ($name -like "$prefix*") {
            $isTarget = $true
            break
        }
    }

    if ($isTarget) {
        Write-Host "  🗑️  Scheduling deletion for: $name" -ForegroundColor White -NoNewline
        
        # Schedule for deletion
        $result = oci vault secret schedule-deletion --secret-id $id --time-of-deletion $(Get-Date).AddHours(1).ToString("yyyy-MM-ddTHH:mm:ssZ") 2>$null
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host " ✅ Scheduled" -ForegroundColor Green
            $deletedCount++
        } else {
            Write-Host " ❌ Failed" -ForegroundColor Red
            $skippedCount++
        }
    }
}

Write-Host "`n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray
Write-Host "✅ Secrets scheduled for deletion: $deletedCount" -ForegroundColor Green
Write-Host "⚠️ Secrets skipped/failed: $skippedCount" -ForegroundColor $(if ($skippedCount -gt 0) { "Yellow" } else { "Green" })
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

Write-Host "`n💡 Note: Secrets are scheduled for deletion in 1 hour (OCI requirement)." -ForegroundColor Cyan
Write-Host "   You can now safely run .\01-setup-vault-secrets.ps1 to recreate them cleanly." -ForegroundColor Cyan