Describe "Resolve-SecretInput" {
    BeforeAll {
        . (Join-Path $PSScriptRoot "..\secret-input.ps1")
    }

    It "reuses the Vault when every mapped secret already exists" {
        $result = Resolve-SecretInput `
            -SecretsFile "" `
            -ExpectedSecretNames @("POSTGRES_PASSWORD", "URBEAT_JWT_SECRET") `
            -ExistingSecretNames @("POSTGRES_PASSWORD", "URBEAT_JWT_SECRET")

        $result.ReuseExisting | Should Be $true
        @($result.MissingSecretNames).Count | Should Be 0
    }

    It "requires a local file when a mapped secret is missing" {
        {
            Resolve-SecretInput `
                -SecretsFile "" `
                -ExpectedSecretNames @("POSTGRES_PASSWORD", "URBEAT_JWT_SECRET") `
                -ExistingSecretNames @("POSTGRES_PASSWORD")
        } | Should Throw "SecretsFile is required because Vault is missing: URBEAT_JWT_SECRET"
    }
}
