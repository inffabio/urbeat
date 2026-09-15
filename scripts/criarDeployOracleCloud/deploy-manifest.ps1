<#
.SYNOPSIS
    Shared deployment-source validation and manifest helpers.
.DESCRIPTION
    Both the OCI application step (04-deploy-application.ps1) and the internal
    deploy script (scripts/deploy-internal.ps1) dot-source this file to prove
    which git revision is being shipped and to record it in a deployment
    manifest. The manifest never contains file contents or secrets: it only
    stores git metadata and SHA256 hashes of the produced archives.
.NOTES
    Keep top-level side effects out of this file; it is dot-sourced, not run.
#>

function Get-GitValue {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    $output = & git -C $ProjectRoot @Arguments 2>$null
    if ($LASTEXITCODE -ne 0) {
        return $null
    }
    return (($output | Out-String).Trim())
}

function Get-DeploySourceState {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot
    )

    $commit = Get-GitValue -ProjectRoot $ProjectRoot -Arguments @("rev-parse", "HEAD")
    $branch = Get-GitValue -ProjectRoot $ProjectRoot -Arguments @("rev-parse", "--abbrev-ref", "HEAD")

    $status = @(& git -C $ProjectRoot status --porcelain 2>$null)
    $dirtyEntries = @($status | Where-Object { $_ -and $_.Trim() -ne "" })

    return [pscustomobject]@{
        ProjectRoot  = $ProjectRoot
        Commit       = $commit
        Branch       = $branch
        Dirty        = ($dirtyEntries.Count -gt 0)
        DirtyEntries = $dirtyEntries
    }
}

function Assert-DeploySource {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [switch]$AllowDirty,
        [string]$ExpectedCommit
    )

    $state = Get-DeploySourceState -ProjectRoot $ProjectRoot

    if ([string]::IsNullOrWhiteSpace($state.Commit)) {
        throw "Could not resolve a git HEAD for '$ProjectRoot'. Is this a git working tree?"
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedCommit)) {
        $matchesHead = ($state.Commit -eq $ExpectedCommit)
        if (-not $matchesHead -and $ExpectedCommit.Length -ge 7) {
            $matchesHead = $state.Commit.StartsWith($ExpectedCommit)
        }
        if (-not $matchesHead) {
            throw "ExpectedCommit '$ExpectedCommit' does not match git HEAD '$($state.Commit)'."
        }
    }

    if ($state.Dirty -and -not $AllowDirty) {
        $sample = @($state.DirtyEntries | Select-Object -First 10) -join "; "
        throw "Working tree is dirty ($($state.DirtyEntries.Count) path(s): $sample). Commit/stash the changes or pass -AllowDirty to ship an explicit local snapshot."
    }

    return $state
}

function Get-FileSha256 {
    param(
        [Parameter(Mandatory = $true)][string]$Path
    )

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function New-DeploymentManifest {
    param(
        [Parameter(Mandatory = $true)]$SourceState,
        [Parameter(Mandatory = $false)][hashtable]$Artifacts = @{},
        [Parameter(Mandatory = $false)][string]$GeneratedAtUtc
    )

    if ([string]::IsNullOrWhiteSpace($GeneratedAtUtc)) {
        $GeneratedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    }

    return [pscustomobject][ordered]@{
        commit         = [string]$SourceState.Commit
        branch         = [string]$SourceState.Branch
        dirty          = [bool]$SourceState.Dirty
        generatedAtUtc = $GeneratedAtUtc
        projectRoot    = [string]$SourceState.ProjectRoot
        artifacts      = $Artifacts
    }
}

function Write-DeploymentManifest {
    param(
        [Parameter(Mandatory = $true)]$Manifest,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $json = $Manifest | ConvertTo-Json -Depth 6
    [System.IO.File]::WriteAllText($Path, $json, [System.Text.UTF8Encoding]::new($false))
    return $Path
}
