<#
.SYNOPSIS
    Performs local, non-destructive validation of the OCI deployment pipeline.
#>

$ErrorActionPreference = "Stop"
$scriptRoot = $PSScriptRoot
$scripts = Get-ChildItem -LiteralPath $scriptRoot -Filter "*.ps1" -File
$errors = [System.Collections.Generic.List[string]]::new()

foreach ($script in $scripts) {
    $tokens = $null
    $parseErrors = $null
    [System.Management.Automation.Language.Parser]::ParseFile(
        $script.FullName,
        [ref]$tokens,
        [ref]$parseErrors) | Out-Null
    foreach ($parseError in $parseErrors) {
        $errors.Add("$($script.Name): $($parseError.Message)")
    }
}

$mapPath = Join-Path $scriptRoot "configs\secrets-map.json"
try {
    $map = Get-Content -LiteralPath $mapPath -Raw | ConvertFrom-Json
    foreach ($property in $map.PSObject.Properties) {
        if ([string]$property.Value -notmatch '^ocid1\.vaultsecret\.') {
            $errors.Add("secrets-map.json contains a non-secret OCID value for '$($property.Name)'.")
        }
    }
} catch {
    $errors.Add("Could not parse configs/secrets-map.json.")
}

foreach ($script in $scripts) {
    $content = Get-Content -LiteralPath $script.FullName -Raw
    if ($content -match '\[string\]\$SSHUser\s*=\s*"ubuntu"') {
        $errors.Add("$($script.Name): default SSH user must be dexter.")
    }
    if ($content -match 'C:\\Projetos\\urbeat') {
        $errors.Add("$($script.Name): contains a fixed local repository path.")
    }
}

# SSH/knocking invariants: the SSH port is protected by port knocking, so every
# SSH/SCP connection must be preceded by a knock. Defaults must stay dexter/2208,
# and deploy-all must forward the SSH settings to every step script.
$sshCallRegex = [regex]'\b(?:ssh|scp)\s+@[A-Za-z_][A-Za-z0-9_]*'
$knockCallRegex = [regex]'^(?:Send-PortKnock\s+-ServerIP|port-knock\.ps1\s+-ServerIP|&\s*\$knockScript\s+-ServerIP)'

foreach ($script in $scripts) {
    $connectLines = @(Get-Content -LiteralPath $script.FullName | Where-Object {
        $trimmed = $_.Trim()
        $trimmed -ne '' -and -not $trimmed.StartsWith('#')
    })

    $knocked = $false
    foreach ($line in $connectLines) {
        if ($knockCallRegex.IsMatch($line.Trim())) {
            $knocked = $true
        }
        if ($sshCallRegex.IsMatch($line) -and -not $knocked) {
            $errors.Add("$($script.Name): an SSH/SCP call must be preceded by a port knock.")
        }
    }

    $content = Get-Content -LiteralPath $script.FullName -Raw
    if ($sshCallRegex.IsMatch($content)) {
        if ($content -notmatch '\[string\]\$SSHUser\s*=\s*"dexter"') {
            $errors.Add("$($script.Name): default SSH user must be dexter.")
        }
        if ($content -notmatch '\[int\]\$SSHPort\s*=\s*2208') {
            $errors.Add("$($script.Name): default SSH port must be 2208.")
        }
    }

    if ($script.Name -eq "deploy-all.ps1") {
        if ($content -notmatch '\[string\]\$SSHUser\s*=\s*"dexter"') {
            $errors.Add("deploy-all.ps1: default SSH user must be dexter.")
        }
        if ($content -notmatch '\[int\]\$SSHPort\s*=\s*2208') {
            $errors.Add("deploy-all.ps1: default SSH port must be 2208.")
        }
        if ($content -notmatch '@commonParams') {
            $errors.Add("deploy-all.ps1: must forward SSH settings to each step via @commonParams.")
        }
        if ($content -notmatch '\$commonParams\s*=\s*@\{') {
            $errors.Add("deploy-all.ps1: must define the @commonParams splat with ServerIP/SSHUser/SSHPort/SSHKeyPath.")
        }
        if ($content -notmatch '\[switch\]\$AllowDirty') {
            $errors.Add("deploy-all.ps1: must expose -AllowDirty.")
        }
        if ($content -notmatch '\[string\]\$ExpectedCommit') {
            $errors.Add("deploy-all.ps1: must expose -ExpectedCommit.")
        }
        if ($content -notmatch '\$applicationParams') {
            $errors.Add("deploy-all.ps1: must forward -AllowDirty/-ExpectedCommit to the application step.")
        }
        if ($content -notmatch '\-ExtraParams') {
            $errors.Add("deploy-all.ps1: Run-Step must accept -ExtraParams.")
        }
    }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ }
    exit 1
}

# ─────────────────────────────────────────
# Deploy hardening invariants
# ─────────────────────────────────────────

$manifestHelper = Join-Path $scriptRoot "deploy-manifest.ps1"
if (-not (Test-Path -LiteralPath $manifestHelper -PathType Leaf)) {
    $errors.Add("deploy-manifest.ps1 is missing; deploy source validation and manifests are required.")
} else {
    $helperContent = Get-Content -LiteralPath $manifestHelper -Raw
    foreach ($functionName in @("Assert-DeploySource", "Get-FileSha256", "New-DeploymentManifest", "Write-DeploymentManifest")) {
        if ($helperContent -notmatch [regex]::Escape("function $functionName")) {
            $errors.Add("deploy-manifest.ps1: missing function $functionName.")
        }
    }
}

$applicationScript = Join-Path $scriptRoot "04-deploy-application.ps1"
if (Test-Path -LiteralPath $applicationScript -PathType Leaf) {
    $applicationContent = Get-Content -LiteralPath $applicationScript -Raw
    foreach ($invariant in @(
        @{ Pattern = '\[switch\]\$AllowDirty'; Message = "04-deploy-application.ps1 must expose -AllowDirty." },
        @{ Pattern = '\[string\]\$ExpectedCommit'; Message = "04-deploy-application.ps1 must expose -ExpectedCommit." },
        @{ Pattern = 'Assert-DeploySource'; Message = "04-deploy-application.ps1 must validate the deploy source." },
        @{ Pattern = 'deployment-manifest\.json'; Message = "04-deploy-application.ps1 must upload deployment-manifest.json." }
    )) {
        if ($applicationContent -notmatch $invariant.Pattern) {
            $errors.Add($invariant.Message)
        }
    }
} else {
    $errors.Add("04-deploy-application.ps1 is missing.")
}

$internalScript = Join-Path (Split-Path -Parent $scriptRoot) "deploy-internal.ps1"
if (Test-Path -LiteralPath $internalScript -PathType Leaf) {
    $internalContent = Get-Content -LiteralPath $internalScript -Raw
    foreach ($invariant in @(
        @{ Pattern = '\[switch\]\$AllowDirty'; Message = "deploy-internal.ps1 must expose -AllowDirty." },
        @{ Pattern = '\[string\]\$ExpectedCommit'; Message = "deploy-internal.ps1 must expose -ExpectedCommit." },
        @{ Pattern = 'Assert-DeploySource'; Message = "deploy-internal.ps1 must validate the deploy source." },
        @{ Pattern = 'deployment-manifest\.json'; Message = "deploy-internal.ps1 must write deployment-manifest.json." }
    )) {
        if ($internalContent -notmatch $invariant.Pattern) {
            $errors.Add($invariant.Message)
        }
    }
    if ($internalContent -match 'git push origin master') {
        $errors.Add("deploy-internal.ps1 must not push to origin during deploy.")
    }
    if ($internalContent -match 'git commit') {
        $errors.Add("deploy-internal.ps1 must not create git commits during deploy.")
    }
    if ($internalContent -match '\$SkipUpload') {
        $errors.Add("deploy-internal.ps1 must always produce a deployment manifest (no -SkipUpload escape hatch).")
    }
    if ($internalContent -match 'Set-Content|ConvertTo-Json') {
        $errors.Add("deploy-internal.ps1 must not rewrite version files (package.json/landing-page.component.ts).")
    }
    if ($internalContent -match 'StrictHostKeyChecking=no') {
        $errors.Add("deploy-internal.ps1 must not disable SSH host key checking.")
    }
} else {
    $errors.Add("scripts/deploy-internal.ps1 is missing.")
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host "OCI deployment scripts, JSON map, local paths, and deploy-hardening invariants are valid." -ForegroundColor Green
