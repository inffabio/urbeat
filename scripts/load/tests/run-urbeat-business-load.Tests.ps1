$script:wrapper = Join-Path $PSScriptRoot "..\run-urbeat-business-load.ps1"

$powerShellCommand = Get-Command pwsh -ErrorAction SilentlyContinue
if (-not $powerShellCommand) {
    $powerShellCommand = Get-Command powershell -ErrorAction SilentlyContinue
}
if (-not $powerShellCommand) {
    throw "Neither pwsh nor powershell was found for wrapper safety tests."
}
$script:powerShellExe = $powerShellCommand.Source

function New-TemporaryPlanPath {
    return [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), "urbeat-business-load-" + [Guid]::NewGuid().ToString("N") + ".sql")
}

function Invoke-WrapperCli {
    param([string[]]$Arguments)

    $output = & $script:powerShellExe -NoProfile -NonInteractive -File $script:wrapper @Arguments 2>&1
    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output   = ($output -join "`n")
    }
}

Describe "urbeat business load wrapper safety" {
    It "rejects known production hostnames even with -AllowRemote" {
        $plan = New-TemporaryPlanPath
        try {
            $result = Invoke-WrapperCli -Arguments @(
                "-Mode", "Plan",
                "-BaseUrl", "https://api.urbeat.com.br",
                "-AllowRemote",
                "-CleanupPlanPath", $plan
            )
            $result.ExitCode | Should Not Be 0
            $result.Output | Should Match "production"
        }
        finally {
            Remove-Item -LiteralPath $plan -Force -ErrorAction SilentlyContinue
        }
    }

    It "rejects the production apex and www hostnames" {
        $plan = New-TemporaryPlanPath
        try {
            $apex = Invoke-WrapperCli -Arguments @(
                "-Mode", "Plan",
                "-BaseUrl", "https://urbeat.com.br",
                "-AllowRemote",
                "-CleanupPlanPath", $plan
            )
            $apex.ExitCode | Should Not Be 0
            $apex.Output | Should Match "production"

            $www = Invoke-WrapperCli -Arguments @(
                "-Mode", "Plan",
                "-BaseUrl", "https://www.urbeat.com.br",
                "-AllowRemote",
                "-CleanupPlanPath", $plan
            )
            $www.ExitCode | Should Not Be 0
            $www.Output | Should Match "production"
        }
        finally {
            Remove-Item -LiteralPath $plan -Force -ErrorAction SilentlyContinue
        }
    }

    It "rejects a non-loopback target without -AllowRemote" {
        $plan = New-TemporaryPlanPath
        try {
            $result = Invoke-WrapperCli -Arguments @(
                "-Mode", "Plan",
                "-BaseUrl", "https://staging.example.com",
                "-CleanupPlanPath", $plan
            )
            $result.ExitCode | Should Not Be 0
            $result.Output | Should Match "AllowRemote"
        }
        finally {
            Remove-Item -LiteralPath $plan -Force -ErrorAction SilentlyContinue
        }
    }

    It "rejects Run mode without -BusinessTestAuthorized" {
        $plan = New-TemporaryPlanPath
        try {
            $result = Invoke-WrapperCli -Arguments @(
                "-Mode", "Run",
                "-Cleanup",
                "-AllowSyntheticAccountCreation",
                "-CleanupPlanPath", $plan
            )
            $result.ExitCode | Should Not Be 0
            $result.Output | Should Match "BusinessTestAuthorized"
        }
        finally {
            Remove-Item -LiteralPath $plan -Force -ErrorAction SilentlyContinue
        }
    }

    It "rejects Run mode without -Cleanup" {
        $plan = New-TemporaryPlanPath
        try {
            $result = Invoke-WrapperCli -Arguments @(
                "-Mode", "Run",
                "-BusinessTestAuthorized",
                "-AllowSyntheticAccountCreation",
                "-CleanupPlanPath", $plan
            )
            $result.ExitCode | Should Not Be 0
            $result.Output | Should Match "Cleanup"
        }
        finally {
            Remove-Item -LiteralPath $plan -Force -ErrorAction SilentlyContinue
        }
    }

    It "rejects Run mode without an account source" {
        $plan = New-TemporaryPlanPath
        try {
            $result = Invoke-WrapperCli -Arguments @(
                "-Mode", "Run",
                "-BusinessTestAuthorized",
                "-Cleanup",
                "-CleanupPlanPath", $plan
            )
            $result.ExitCode | Should Not Be 0
            $result.Output | Should Match "pool|AllowSyntheticAccountCreation"
        }
        finally {
            Remove-Item -LiteralPath $plan -Force -ErrorAction SilentlyContinue
        }
    }

    It "rejects store creation without -AllowNeighborhoodImport" {
        $plan = New-TemporaryPlanPath
        try {
            $result = Invoke-WrapperCli -Arguments @(
                "-Mode", "Run",
                "-BusinessTestAuthorized",
                "-Cleanup",
                "-AllowSyntheticAccountCreation",
                "-CleanupPlanPath", $plan
            )
            $result.ExitCode | Should Not Be 0
            $result.Output | Should Match "NeighborhoodImport"
        }
        finally {
            Remove-Item -LiteralPath $plan -Force -ErrorAction SilentlyContinue
        }
    }

    It "rejects more than 300 VUs" {
        $plan = New-TemporaryPlanPath
        try {
            $result = Invoke-WrapperCli -Arguments @(
                "-Mode", "Plan",
                "-Vus", "301",
                "-AllowSyntheticAccountCreation",
                "-CleanupPlanPath", $plan
            )
            $result.ExitCode | Should Not Be 0
        }
        finally {
            Remove-Item -LiteralPath $plan -Force -ErrorAction SilentlyContinue
        }
    }

    It "rejects a RunId that would produce an invalid store slug" {
        $plan = New-TemporaryPlanPath
        try {
            $result = Invoke-WrapperCli -Arguments @(
                "-Mode", "Plan",
                "-RunId", "bad--runid",
                "-AllowSyntheticAccountCreation",
                "-CleanupPlanPath", $plan
            )
            $result.ExitCode | Should Not Be 0
            $result.Output | Should Match "RunId"
        }
        finally {
            Remove-Item -LiteralPath $plan -Force -ErrorAction SilentlyContinue
        }
    }

    It "rejects a pool containing production urbeat e-mail accounts" {
        $pool = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), "urbeat-business-pool-" + [Guid]::NewGuid().ToString("N") + ".json")
        $plan = New-TemporaryPlanPath
        try {
            '[{"email":"someone@urbeat.com.br","password":"dummy"},{"email":"other@example.test","password":"dummy"}]' |
                Set-Content -LiteralPath $pool -Encoding ASCII

            $result = Invoke-WrapperCli -Arguments @(
                "-Mode", "Plan",
                "-Vus", "1",
                "-SellerPoolPath", $pool,
                "-CleanupPlanPath", $plan
            )
            $result.ExitCode | Should Not Be 0
            $result.Output | Should Match "production urbeat e-mail"
        }
        finally {
            Remove-Item -LiteralPath $pool -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $plan -Force -ErrorAction SilentlyContinue
        }
    }

    It "writes a read-only cleanup inventory without destructive statements" {
        $plan = New-TemporaryPlanPath
        try {
            $result = Invoke-WrapperCli -Arguments @(
                "-Mode", "Plan",
                "-Vus", "2",
                "-RunId", "plancheck01",
                "-AllowSyntheticAccountCreation",
                "-CleanupPlanPath", $plan
            )
            $result.ExitCode | Should Be 0
            Test-Path -LiteralPath $plan | Should Be $true

            $content = Get-Content -LiteralPath $plan -Raw
            $content | Should Match "load\+plancheck01\+%@loadtest\.invalid"
            $content | Should Match "load-plancheck01-%"
            ($content -match "(?m)^\s*DELETE\s") | Should Be $false
        }
        finally {
            Remove-Item -LiteralPath $plan -Force -ErrorAction SilentlyContinue
        }
    }
}
