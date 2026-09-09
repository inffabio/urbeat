<#
.SYNOPSIS
    Shared SSH/knocking helpers for the OCI deployment pipeline.
.DESCRIPTION
    Deployment step scripts dot-source this file to obtain Send-PortKnock.
    Every SSH/SCP connection to the OCI host must be preceded by
    Send-PortKnock because the SSH port is protected by port knocking.
.NOTES
    Keep top-level side effects out of this file; it is dot-sourced, not run.
#>

function Send-PortKnock {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ServerIP,

        [Parameter(Mandatory = $false)]
        [int[]]$Ports = @(1230, 5688, 9112),

        [Parameter(Mandatory = $false)]
        [string[]]$Protocols = @("tcp", "udp", "tcp")
    )

    $knockScript = Join-Path $PSScriptRoot "port-knock.ps1"
    # port-knock.ps1 signals configuration errors via throw, so let those propagate.
    & $knockScript -ServerIP $ServerIP -Ports $Ports -Protocols $Protocols
}
