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

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host "OCI deployment scripts, JSON map, and local paths are valid." -ForegroundColor Green
