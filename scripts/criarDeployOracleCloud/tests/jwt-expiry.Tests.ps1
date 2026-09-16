Describe "JWT expiry environment mapping" {
    BeforeAll {
        . (Join-Path $PSScriptRoot "..\deploy-env.ps1")
    }

    It "converts Vault expiry hours into backend expiration minutes" {
        ConvertTo-JwtExpirationMinutes -Hours "24" | Should Be 1440
        ConvertTo-JwtExpirationMinutes -Hours 1 | Should Be 60
    }

    It "falls back to the documented 24-hour expiry when the secret is missing or invalid" {
        ConvertTo-JwtExpirationMinutes -Hours $null | Should Be 1440
        ConvertTo-JwtExpirationMinutes -Hours "not-a-number" | Should Be 1440
        ConvertTo-JwtExpirationMinutes -Hours "0" | Should Be 1440
    }

    It "maps the application compose service to Jwt__ExpirationMinutes from the minutes variable" {
        $content = Get-Content -LiteralPath (Join-Path $PSScriptRoot "..\04-deploy-application.ps1") -Raw

        $content | Should Match 'Jwt__ExpirationMinutes:\s*\$\{JWT_EXPIRY_MINUTES\}'
        ($content -match 'Jwt__ExpiryHours') | Should Be $false
    }

    It "generates JWT_EXPIRY_MINUTES in the environment file from the hours secret" {
        $content = Get-Content -LiteralPath (Join-Path $PSScriptRoot "..\03-setup-environment.ps1") -Raw

        $content | Should Match 'JWT_EXPIRY_MINUTES=\$jwtExpiryMinutes'
        $content | Should Match 'URBEAT_JWT_EXPIRY_HOURS'
        $content | Should Match 'ConvertTo-JwtExpirationMinutes'
    }
}
