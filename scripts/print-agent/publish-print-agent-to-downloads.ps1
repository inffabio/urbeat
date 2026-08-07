param(
    [Parameter(Mandatory = $false)]
    [string]$ServerIP = "136.248.115.135",

    [Parameter(Mandatory = $false)]
    [string]$SSHUser = "ubuntu",

    [Parameter(Mandatory = $false)]
    [string]$SSHKeyPath = "~/.ssh/id_ed25519",

    [Parameter(Mandatory = $false)]
    [string]$RemoteDownloadsDir = "/opt/urbeat/downloads"
)

$ErrorActionPreference = 'Stop'

$projectRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$downloadsRoot = Join-Path $projectRoot "downloads"
$resolvedKeyPath = (Resolve-Path $SSHKeyPath -ErrorAction SilentlyContinue).Path

if (-not $resolvedKeyPath) {
    throw "SSH key não encontrada em $SSHKeyPath"
}

$requiredFiles = @(
    (Join-Path $downloadsRoot "urbeat-print-agent/windows/Urbeat.PrintAgent-win-x64.zip"),
    (Join-Path $downloadsRoot "urbeat-print-agent/linux/Urbeat.PrintAgent-linux-x64.tar.gz")
)

foreach ($file in $requiredFiles) {
    if (-not (Test-Path $file)) {
        throw "Arquivo obrigatório não encontrado: $file. Rode build-print-agent-packages.ps1 antes."
    }
}

$sshOpts = @("-F", "NUL", "-i", $resolvedKeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes", "-o", "ConnectTimeout=180", "-o", "GSSAPIAuthentication=no")

ssh @sshOpts "${SSHUser}@${ServerIP}" "sudo mkdir -p ${RemoteDownloadsDir}/urbeat-print-agent/windows ${RemoteDownloadsDir}/urbeat-print-agent/linux && sudo chown -R ${SSHUser}:${SSHUser} ${RemoteDownloadsDir}"

if (Test-Path (Join-Path $projectRoot "download/POSPrinterDriverSetup58mm.exe")) {
    scp @sshOpts (Join-Path $projectRoot "download/POSPrinterDriverSetup58mm.exe") "${SSHUser}@${ServerIP}:${RemoteDownloadsDir}/"
}
scp @sshOpts (Join-Path $downloadsRoot "urbeat-print-agent/windows/Urbeat.PrintAgent-win-x64.zip") "${SSHUser}@${ServerIP}:${RemoteDownloadsDir}/urbeat-print-agent/windows/"
scp @sshOpts (Join-Path $downloadsRoot "urbeat-print-agent/windows/install-local-agent.ps1") "${SSHUser}@${ServerIP}:${RemoteDownloadsDir}/urbeat-print-agent/windows/"
scp @sshOpts (Join-Path $downloadsRoot "urbeat-print-agent/windows/urbeat-print-agent-startup.bat") "${SSHUser}@${ServerIP}:${RemoteDownloadsDir}/urbeat-print-agent/windows/"
scp @sshOpts (Join-Path $downloadsRoot "urbeat-print-agent/windows/README.md") "${SSHUser}@${ServerIP}:${RemoteDownloadsDir}/urbeat-print-agent/windows/"
scp @sshOpts (Join-Path $downloadsRoot "urbeat-print-agent/linux/Urbeat.PrintAgent-linux-x64.tar.gz") "${SSHUser}@${ServerIP}:${RemoteDownloadsDir}/urbeat-print-agent/linux/"
scp @sshOpts (Join-Path $downloadsRoot "urbeat-print-agent/linux/install-local-agent.sh") "${SSHUser}@${ServerIP}:${RemoteDownloadsDir}/urbeat-print-agent/linux/"
scp @sshOpts (Join-Path $downloadsRoot "urbeat-print-agent/linux/urbeat-print-agent.service") "${SSHUser}@${ServerIP}:${RemoteDownloadsDir}/urbeat-print-agent/linux/"
scp @sshOpts (Join-Path $downloadsRoot "urbeat-print-agent/linux/README.md") "${SSHUser}@${ServerIP}:${RemoteDownloadsDir}/urbeat-print-agent/linux/"
scp @sshOpts (Join-Path $downloadsRoot "urbeat-print-agent/README.md") "${SSHUser}@${ServerIP}:${RemoteDownloadsDir}/urbeat-print-agent/"

"Downloads do print-agent publicados em ${RemoteDownloadsDir}"
