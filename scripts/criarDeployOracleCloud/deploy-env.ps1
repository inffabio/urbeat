<#
.SYNOPSIS
    Helpers that translate Vault environment secrets into backend configuration.
#>

# JwtOptions binds Jwt:ExpirationMinutes. The Vault secret keeps its historical
# "expiry hours" semantics, so convert hours to minutes exactly once here instead
# of emitting the unmatched Jwt__ExpiryHours key.
function ConvertTo-JwtExpirationMinutes {
    param(
        [Parameter(Mandatory = $false)]
        [AllowNull()]
        [object]$Hours
    )

    $parsedHours = 0
    if ([int]::TryParse(([string]$Hours).Trim(), [ref]$parsedHours) -and $parsedHours -gt 0) {
        return $parsedHours * 60
    }

    return 24 * 60
}

# AdminRecovery:Enabled binds to a boolean switch that is disabled by default. The Vault display
# name AdminRecovery__Enabled may hold "true"/"false"; anything else (missing, empty, unrelated
# text) keeps the break-glass endpoint disabled instead of failing the deployment.
function ConvertTo-AdminRecoveryEnabledValue {
    param(
        [Parameter(Mandatory = $false)]
        [AllowNull()]
        [object]$Value
    )

    $parsed = $false
    if ([bool]::TryParse(([string]$Value).Trim(), [ref]$parsed)) {
        return $(if ($parsed) { 'true' } else { 'false' })
    }

    return 'false'
}
