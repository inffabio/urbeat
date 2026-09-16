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
