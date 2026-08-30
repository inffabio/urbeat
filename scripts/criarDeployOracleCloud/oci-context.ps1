$contextPath = $env:URBEAT_OCI_CONTEXT_FILE
if ([string]::IsNullOrWhiteSpace($contextPath)) {
    $contextPath = Join-Path $PSScriptRoot "configs\oci.local.json"
}

if (-not (Test-Path -LiteralPath $contextPath -PathType Leaf)) {
    return
}

try {
    $context = Get-Content -LiteralPath $contextPath -Raw | ConvertFrom-Json
} catch {
    throw "Could not parse OCI context file: $contextPath"
}

foreach ($name in @('OCI_COMPARTMENT_OCID', 'OCI_VAULT_MANAGEMENT_ENDPOINT')) {
    $value = [string]$context.$name
    $current = [Environment]::GetEnvironmentVariable($name)
    if ([string]::IsNullOrWhiteSpace($current) -and -not [string]::IsNullOrWhiteSpace($value)) {
        Set-Item -Path "Env:$name" -Value $value
    }
}
