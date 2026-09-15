Describe "deploy source helpers" {
    BeforeAll {
        . (Join-Path $PSScriptRoot "..\deploy-manifest.ps1")
    }

    It "computes a stable SHA256 for a file" {
        $tmp = New-TemporaryFile
        try {
            [System.IO.File]::WriteAllText($tmp.FullName, "urbeat", [System.Text.UTF8Encoding]::new($false))
            (Get-FileSha256 -Path $tmp.FullName) | Should Match '^[0-9a-f]{64}$'
        } finally {
            Remove-Item -LiteralPath $tmp.FullName -Force
        }
    }

    It "builds a manifest without file contents or secrets" {
        $state = [pscustomobject]@{
            ProjectRoot = "C:\repo"
            Commit      = "abc123"
            Branch      = "main"
            Dirty       = $false
        }

        $manifest = New-DeploymentManifest `
            -SourceState $state `
            -Artifacts @{ backendArchiveSha256 = "deadbeef" } `
            -GeneratedAtUtc "2026-01-01T00:00:00Z"

        $manifest.commit | Should Be "abc123"
        $manifest.branch | Should Be "main"
        $manifest.dirty | Should Be $false
        $manifest.generatedAtUtc | Should Be "2026-01-01T00:00:00Z"
        $manifest.projectRoot | Should Be "C:\repo"
        $manifest.artifacts.backendArchiveSha256 | Should Be "deadbeef"
        ($manifest.PSObject.Properties.Name -contains "content") | Should Be $false
        ($manifest.PSObject.Properties.Name -contains "secret") | Should Be $false
    }

    It "serializes a manifest to disk as UTF-8 without BOM" {
        $state = [pscustomobject]@{
            ProjectRoot = "C:\repo"
            Commit      = "abc123"
            Branch      = "main"
            Dirty       = $true
        }
        $manifest = New-DeploymentManifest -SourceState $state -GeneratedAtUtc "2026-01-01T00:00:00Z"
        $target = Join-Path ([System.IO.Path]::GetTempPath()) ("manifest-" + [guid]::NewGuid() + ".json")
        try {
            Write-DeploymentManifest -Manifest $manifest -Path $target
            $bytes = [System.IO.File]::ReadAllBytes($target)
            $bytes[0] | Should Be ([byte][char]'{')
            (Get-Content -LiteralPath $target -Raw | ConvertFrom-Json).commit | Should Be "abc123"
        } finally {
            Remove-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue
        }
    }
}

Describe "Assert-DeploySource" {
    BeforeAll {
        . (Join-Path $PSScriptRoot "..\deploy-manifest.ps1")
        $script:repo = Join-Path ([System.IO.Path]::GetTempPath()) ("urbeat-git-" + [guid]::NewGuid())
        New-Item -ItemType Directory -Path $script:repo | Out-Null
        & git -C $script:repo init -q
        & git -C $script:repo config user.email "test@example.com"
        & git -C $script:repo config user.name "Urbeat Test"
        Set-Content -LiteralPath (Join-Path $script:repo "a.txt") -Value "hello"
        & git -C $script:repo add -A
        & git -C $script:repo commit -q -m "init"
        $script:head = (& git -C $script:repo rev-parse HEAD).Trim()
    }

    AfterAll {
        Remove-Item -LiteralPath $script:repo -Recurse -Force -ErrorAction SilentlyContinue
    }

    It "blocks a dirty working tree by default" {
        $dirtyFile = Join-Path $script:repo "b.txt"
        Set-Content -LiteralPath $dirtyFile -Value "dirty"
        try {
            { Assert-DeploySource -ProjectRoot $script:repo } | Should Throw "Working tree is dirty"
        } finally {
            Remove-Item -LiteralPath $dirtyFile -Force
        }
    }

    It "allows a dirty working tree with -AllowDirty" {
        $dirtyFile = Join-Path $script:repo "b.txt"
        Set-Content -LiteralPath $dirtyFile -Value "dirty"
        try {
            $state = Assert-DeploySource -ProjectRoot $script:repo -AllowDirty
            $state.Dirty | Should Be $true
            $state.Commit | Should Be $script:head
        } finally {
            Remove-Item -LiteralPath $dirtyFile -Force
        }
    }

    It "rejects a mismatched ExpectedCommit" {
        {
            Assert-DeploySource -ProjectRoot $script:repo -AllowDirty -ExpectedCommit "0000000000000000000000000000000000000000"
        } | Should Throw "does not match git HEAD"
    }

    It "accepts a matching ExpectedCommit" {
        $state = Assert-DeploySource -ProjectRoot $script:repo -ExpectedCommit $script:head
        $state.Commit | Should Be $script:head
        $state.Dirty | Should Be $false
    }
}

Describe "deploy hardening invariants" {
    It "OCI application step accepts AllowDirty and ExpectedCommit" {
        $content = Get-Content -LiteralPath (Join-Path $PSScriptRoot "..\04-deploy-application.ps1") -Raw

        $content | Should Match '\[switch\]\$AllowDirty'
        $content | Should Match '\[string\]\$ExpectedCommit'
    }

    It "OCI application step validates the source and writes a manifest" {
        $content = Get-Content -LiteralPath (Join-Path $PSScriptRoot "..\04-deploy-application.ps1") -Raw

        $content | Should Match 'Assert-DeploySource'
        $content | Should Match 'Get-FileSha256'
        $content | Should Match 'deployment-manifest\.json'
        $content | Should Match 'New-DeploymentManifest'
    }

    It "internal deploy accepts AllowDirty and ExpectedCommit" {
        $content = Get-Content -LiteralPath (Join-Path $PSScriptRoot "..\..\deploy-internal.ps1") -Raw

        $content | Should Match '\[switch\]\$AllowDirty'
        $content | Should Match '\[string\]\$ExpectedCommit'
        $content | Should Match 'Assert-DeploySource'
        $content | Should Match 'deployment-manifest\.json'
    }

    It "internal deploy no longer commits or pushes git" {
        $content = Get-Content -LiteralPath (Join-Path $PSScriptRoot "..\..\deploy-internal.ps1") -Raw

        ($content -match 'git push origin master') | Should Be $false
        ($content -match 'git commit') | Should Be $false
    }

    It "OCI application step aborts when a configuration upload fails" {
        $content = Get-Content -LiteralPath (Join-Path $PSScriptRoot "..\04-deploy-application.ps1") -Raw

        $content | Should Match 'Failed to upload \$FileName'
        $content | Should Match 'Failed to install \$FileName'
        $content | Should Match 'Remove-Item -LiteralPath \$tempFile -Force'
    }

    It "deploy-all exposes and forwards the hardening parameters to the application step" {
        $content = Get-Content -LiteralPath (Join-Path $PSScriptRoot "..\deploy-all.ps1") -Raw

        $content | Should Match '\[switch\]\$AllowDirty'
        $content | Should Match '\[string\]\$ExpectedCommit'
        $content | Should Match '\$applicationParams'
        $content | Should Match '-ExtraParams'
        $content | Should Match '-eq\s+["'']application["'']'
    }

    It "internal deploy validates the source before the first remote command" {
        $content = Get-Content -LiteralPath (Join-Path $PSScriptRoot "..\..\deploy-internal.ps1") -Raw

        $validateIndex = $content.IndexOf("Assert-DeploySource")
        $firstCall = [regex]::Match($content, '(?m)^\s*Invoke-Ssh\s+')
        $mkdirIndex = $content.IndexOf("mkdir -p `$RemoteRoot")

        ($validateIndex -ge 0) | Should Be $true
        ($firstCall.Success) | Should Be $true
        ($validateIndex -lt $firstCall.Index) | Should Be $true
        ($validateIndex -lt $mkdirIndex) | Should Be $true
    }

    It "internal deploy does not bump or rewrite version files" {
        $content = Get-Content -LiteralPath (Join-Path $PSScriptRoot "..\..\deploy-internal.ps1") -Raw

        ($content -match 'package\.json') | Should Be $false
        ($content -match 'landing-page\.component\.ts') | Should Be $false
        ($content -match 'Set-Content') | Should Be $false
        ($content -match 'ConvertTo-Json') | Should Be $false
        ($content -match 'newVersion') | Should Be $false
    }

    It "internal deploy requires a deployment manifest (no skip-upload escape hatch)" {
        $content = Get-Content -LiteralPath (Join-Path $PSScriptRoot "..\..\deploy-internal.ps1") -Raw

        ($content -match '\$SkipUpload') | Should Be $false
        $content | Should Match 'New-DeploymentManifest'
        $content | Should Match 'Write-DeploymentManifest'
        $content | Should Match 'scp[^\r\n]*deployment-manifest\.json'
        $content | Should Match 'Get-FileSha256 -Path \$tarball'
        ($content.IndexOf("tar -czf") -lt $content.IndexOf("New-DeploymentManifest")) | Should Be $true
    }

    It "internal deploy fails when the remote manifest cannot be installed" {
        $content = Get-Content -LiteralPath (Join-Path $PSScriptRoot "..\..\deploy-internal.ps1") -Raw

        $content | Should Match 'mv _incoming/deployment-manifest\.json \./deployment-manifest\.json'
        $content | Should Match 'test -s \./deployment-manifest\.json'
        ($content.IndexOf("mv _incoming/deployment-manifest.json ./deployment-manifest.json") -lt $content.IndexOf("test -s ./deployment-manifest.json")) | Should Be $true
        ($content -match 'deployment-manifest\.json\s+\./deployment-manifest\.json\s+2>/dev/null\s*\|\|\s*true') | Should Be $false
    }

    It "internal deploy verifies the .env.production upload" {
        $content = Get-Content -LiteralPath (Join-Path $PSScriptRoot "..\..\deploy-internal.ps1") -Raw

        $content | Should Match 'scp[^\r\n]*\.env\.production'
        $content | Should Match 'scp do \.env\.production falhou'
    }

    It "internal deploy does not disable SSH host key checking" {
        $content = Get-Content -LiteralPath (Join-Path $PSScriptRoot "..\..\deploy-internal.ps1") -Raw

        ($content -match 'StrictHostKeyChecking=no') | Should Be $false
        $content | Should Match 'StrictHostKeyChecking=accept-new'
    }
}
