function Resolve-SecretInput {
    param(
        [Parameter(Mandatory = $false)]
        [string]$SecretsFile,

        [Parameter(Mandatory = $true)]
        [string[]]$ExpectedSecretNames,

        [Parameter(Mandatory = $true)]
        [string[]]$ExistingSecretNames
    )

    $missingSecretNames = @($ExpectedSecretNames | Where-Object {
        $_ -notin $ExistingSecretNames
    })

    if ([string]::IsNullOrWhiteSpace($SecretsFile)) {
        if ($missingSecretNames.Count -gt 0) {
            throw "SecretsFile is required because Vault is missing: $($missingSecretNames -join ', ')"
        }

        return [pscustomobject]@{
            ReuseExisting       = $true
            MissingSecretNames  = @()
            Secrets             = @{}
        }
    }

    $resolvedSecretsFile = [System.IO.Path]::GetFullPath($SecretsFile)
    if (-not (Test-Path -LiteralPath $resolvedSecretsFile -PathType Leaf)) {
        throw "Secrets file not found: $resolvedSecretsFile"
    }

    try {
        $secrets = @{}
        $localSecrets = Get-Content -LiteralPath $resolvedSecretsFile -Raw | ConvertFrom-Json
        foreach ($property in $localSecrets.PSObject.Properties) {
            if ([string]::IsNullOrWhiteSpace([string]$property.Value)) {
                throw "Secret '$($property.Name)' is empty in the local secrets file."
            }
            $secrets[$property.Name] = [string]$property.Value
        }
    } catch {
        throw "Could not parse the local secrets file."
    }

    if ($secrets.Count -eq 0) {
        throw "The local secrets file does not contain any secrets."
    }

    return [pscustomobject]@{
        ReuseExisting      = $false
        MissingSecretNames = $missingSecretNames
        Secrets            = $secrets
    }
}
