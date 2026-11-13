Describe "AdminRecovery enabled mapping" {
    BeforeAll {
        . (Join-Path $PSScriptRoot "..\deploy-env.ps1")
    }

    It "keeps the break-glass endpoint disabled when the secret is missing or invalid" {
        ConvertTo-AdminRecoveryEnabledValue -Value $null | Should Be 'false'
        ConvertTo-AdminRecoveryEnabledValue -Value "" | Should Be 'false'
        ConvertTo-AdminRecoveryEnabledValue -Value "   " | Should Be 'false'
        ConvertTo-AdminRecoveryEnabledValue -Value "yes" | Should Be 'false'
        ConvertTo-AdminRecoveryEnabledValue -Value "1" | Should Be 'false'
    }

    It "accepts an explicit boolean true from the Vault" {
        ConvertTo-AdminRecoveryEnabledValue -Value "true" | Should Be 'true'
        ConvertTo-AdminRecoveryEnabledValue -Value " TRUE " | Should Be 'true'
        ConvertTo-AdminRecoveryEnabledValue -Value $true | Should Be 'true'
    }

    It "keeps an explicit false disabled" {
        ConvertTo-AdminRecoveryEnabledValue -Value "false" | Should Be 'false'
        ConvertTo-AdminRecoveryEnabledValue -Value $false | Should Be 'false'
    }
}

Describe "AdminRecovery deployment configuration" {
    BeforeAll {
        $script:environmentScript = Join-Path $PSScriptRoot "..\03-setup-environment.ps1"
        $script:applicationScript = Join-Path $PSScriptRoot "..\04-deploy-application.ps1"
        $script:repositoryCompose = Join-Path $PSScriptRoot "..\..\..\docker\docker-compose.yml"
        $script:developmentCompose = Join-Path $PSScriptRoot "..\..\..\docker\docker-compose.dev.yml"
        $script:appSettings = Join-Path $PSScriptRoot "..\..\..\backend\src\Urbeat.WebApi\appsettings.json"

        function Get-GeneratedCompose {
            param([string]$Path)
            $content = Get-Content -LiteralPath $Path -Raw
            $match = [regex]::Match($content, '(?s)\$dockerCompose = @''\r?\n(.*?)\r?\n''@')
            return $match.Groups[1].Value
        }

        function Get-BackendEnvironmentBlock {
            param([string]$Compose)
            $match = [regex]::Match($Compose, '(?ms)^x-backend-environment: &backend-environment[ \t]*\r?\n(.*?)(?=^\S)')
            return $match.Groups[1].Value
        }
    }

    It "resolves both Vault secrets by exact display name without an OCID in secrets-map.json" {
        $content = Get-Content -LiteralPath $script:environmentScript -Raw

        $content | Should Match '\$adminRecoveryEnabledSecretName\s*=\s*"AdminRecovery__Enabled"'
        $content | Should Match '\$adminRecoveryKeySecretName\s*=\s*"AdminRecovery__Key"'
        $content | Should Match '(?ms)\$adminRecoveryEnabledRaw\s*=\s*Resolve-VaultSecretByName\s+-SecretName\s+\$adminRecoveryEnabledSecretName'
        $content | Should Match '(?ms)\$adminRecoveryKeyValue\s*=\s*Resolve-VaultSecretByName\s+-SecretName\s+\$adminRecoveryKeySecretName'

        $secretsMap = Get-Content -LiteralPath (Join-Path $PSScriptRoot "..\configs\secrets-map.json") -Raw | ConvertFrom-Json
        ($secretsMap.PSObject.Properties.Name -contains "AdminRecovery__Key") | Should Be $false
        ($secretsMap.PSObject.Properties.Name -contains "AdminRecovery__Enabled") | Should Be $false
    }

    It "writes both AdminRecovery keys to the generated .env from the resolved values" {
        $content = Get-Content -LiteralPath $script:environmentScript -Raw

        $content | Should Match '(?m)^AdminRecovery__Enabled=\$adminRecoveryEnabledValue\r?$'
        $content | Should Match '(?m)^AdminRecovery__Key=\$adminRecoveryKeyValue\r?$'
        $content | Should Match 'ConvertTo-AdminRecoveryEnabledValue'
    }

    It "never logs or aliases the AdminRecovery secret values" {
        $content = Get-Content -LiteralPath $script:environmentScript -Raw

        ($content -match '(?i)ADMIN_RECOVERY_(ENABLED|KEY)\b') | Should Be $false
        ($content -match 'Write-Host[^\r\n]*\$adminRecoveryKeyValue') | Should Be $false
        ($content -match 'Write-Host[^\r\n]*\$adminRecoveryEnabledRaw') | Should Be $false
    }

    It "keeps the feature disabled and the endpoint unavailable when a secret does not resolve" {
        $content = Get-Content -LiteralPath $script:environmentScript -Raw

        # Enabled falls back to false through the pure helper, and the unresolved key is emptied
        # instead of aborting the deployment.
        $content | Should Match '(?ms)\$adminRecoveryEnabledValue\s*=\s*ConvertTo-AdminRecoveryEnabledValue'
        $content | Should Match '(?ms)if \(-not \$adminRecoveryKeyResolved\)[\s\S]*?\$adminRecoveryKeyValue\s*=\s*""'
        ($content -match '(?ms)if \(-not \$adminRecoveryKeyResolved\)[\s\S]{0,200}?exit 1') | Should Be $false
    }

    It "forwards AdminRecovery to every production backend process through the shared anchor" {
        $compose = Get-GeneratedCompose -Path $script:applicationScript
        $block = Get-BackendEnvironmentBlock -Compose $compose

        ($block -ne "") | Should Be $true
        $block | Should Match 'AdminRecovery__Enabled:\s*\$\{AdminRecovery__Enabled:-false\}'
        $block | Should Match 'AdminRecovery__Key:\s*\$\{AdminRecovery__Key\}'
        ($block -match 'AdminRecovery__Key:\s*\$\{AdminRecovery__Key:-[^}\s]+\}') | Should Be $false
        ($block -match 'AdminRecovery__Key:\s*["'']?[A-Za-z0-9+/=_\-]{16,}') | Should Be $false
        ([regex]::Matches($compose, '(?m)^[ \t]*<<:[ \t]*\*backend-environment[ \t]*\r?$')).Count | Should Be 4
    }

    It "forwards AdminRecovery from the .env in the repository compose file" {
        $content = Get-Content -LiteralPath $script:repositoryCompose -Raw
        $block = Get-BackendEnvironmentBlock -Compose $content

        ($block -ne "") | Should Be $true
        $block | Should Match 'AdminRecovery__Enabled:\s*"\$\{AdminRecovery__Enabled:-false\}"'
        $block | Should Match 'AdminRecovery__Key:\s*"\$\{AdminRecovery__Key\}"'
        ($block -match 'AdminRecovery__Key:\s*"\$\{AdminRecovery__Key:-[^}\s]+\}"') | Should Be $false
        ($block -match 'AdminRecovery__Key:\s*["'']?[A-Za-z0-9+/=_\-]{16,}') | Should Be $false
    }

    It "forwards AdminRecovery in the development compose file" {
        $content = Get-Content -LiteralPath $script:developmentCompose -Raw

        $content | Should Match 'AdminRecovery__Enabled:\s*"\$\{AdminRecovery__Enabled:-false\}"'
        $content | Should Match 'AdminRecovery__Key:\s*"\$\{AdminRecovery__Key\}"'
        ($content -match 'AdminRecovery__Key:\s*"\$\{AdminRecovery__Key:-[^}\s]+\}"') | Should Be $false
    }

    It "keeps the feature disabled and keyless in appsettings" {
        $settings = Get-Content -LiteralPath $script:appSettings -Raw | ConvertFrom-Json

        $settings.AdminRecovery.Enabled | Should Be $false
        ($settings.AdminRecovery.PSObject.Properties.Name -contains "Key") | Should Be $false
    }
}
