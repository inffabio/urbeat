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
    [string]$SSHUser = "dexter",

    [Parameter(Mandatory=$false)]
    [int]$SSHPort = 2208,

    [Parameter(Mandatory=$false)]
    [string]$SSHKeyPath = "~/.ssh/id_ed25519",

    # Forwarded to the application step only.
    [Parameter(Mandatory=$false)]
    [switch]$AllowDirty,

    [Parameter(Mandatory=$false)]
    [string]$ExpectedCommit
)

$commonParams = @{
    ServerIP   = $ServerIP
    SSHUser    = $SSHUser
    SSHPort    = $SSHPort
    SSHKeyPath = $SSHKeyPath
}

$applicationParams = @{}
if ($AllowDirty) {
    $applicationParams["AllowDirty"] = $true
}
if (-not [string]::IsNullOrWhiteSpace($ExpectedCommit)) {
    $applicationParams["ExpectedCommit"] = $ExpectedCommit
}

$scriptRoot = $PSScriptRoot
. (Join-Path $scriptRoot "oci-context.ps1")

$banner = @"
╔═══════════════════════════════════════════════════════════╗
║          URBEAT APPLICATION DEPLOYMENT PIPELINE           ║
║          Oracle Cloud Infrastructure - aarch64            ║
║          Server: $ServerIP                                ║
╚═══════════════════════════════════════════════════════════╝
"@

Write-Host $banner -ForegroundColor Cyan

$steps = @{
    "prerequisites" = Join-Path $scriptRoot "00-prerequisites-check.ps1"
    "vault"         = Join-Path $scriptRoot "01-setup-vault-secrets.ps1"
    "docker"        = Join-Path $scriptRoot "02-install-docker-aarch64.ps1"
    "environment"   = Join-Path $scriptRoot "03-setup-environment.ps1"
    "application"   = Join-Path $scriptRoot "04-deploy-application.ps1"
    "nginx"         = Join-Path $scriptRoot "05-configure-nginx.ps1"
    "ssl"           = Join-Path $scriptRoot "06-setup-ssl.ps1"
    "verify"        = Join-Path $scriptRoot "07-verify-deployment.ps1"
}

function Run-Step {
    param(
        [string]$StepName,
        [string]$ScriptPath,
        [hashtable]$ExtraParams = @{}
    )

    Write-Host "`n" -NoNewline
    Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor DarkCyan
    Write-Host "  STEP: $StepName" -ForegroundColor Cyan
    Write-Host "  Script: $ScriptPath" -ForegroundColor Gray
    Write-Host "  Time: $(Get-Date -Format 'HH:mm:ss')" -ForegroundColor Gray
    Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor DarkCyan

    if ($ExtraParams.Count -gt 0) {
        $mergedParams = $commonParams.Clone()
        foreach ($key in $ExtraParams.Keys) {
            $mergedParams[$key] = $ExtraParams[$key]
        }
        & $ScriptPath @mergedParams
    } else {
        & $ScriptPath @commonParams
    }

    if ($LASTEXITCODE -ne 0) {
        Write-Host "`n❌ Step '$StepName' FAILED! Stopping deployment." -ForegroundColor Red
        exit 1
    }

    Write-Host "`n✅ Step '$StepName' completed successfully!" -ForegroundColor Green
}

$startTime = Get-Date

if ($Step -eq "all") {
    foreach ($stepName in @("prerequisites", "vault", "docker", "environment", "application", "nginx", "ssl", "verify")) {
        $extraParams = @{}
        if ($stepName -eq "application") { $extraParams = $applicationParams }
        Run-Step -StepName $stepName -ScriptPath $steps[$stepName] -ExtraParams $extraParams
    }
} else {
    $extraParams = @{}
    if ($Step -eq "application") { $extraParams = $applicationParams }
    Run-Step -StepName $Step -ScriptPath $steps[$Step] -ExtraParams $extraParams
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
