param(
    [Parameter(Mandatory = $false)]
    [string]$Configuration = "Release",

    [Parameter(Mandatory = $false)]
    [string]$OutputRoot = "downloads/urbeat-print-agent"
)

$ErrorActionPreference = 'Stop'

$projectRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$agentProject = Join-Path $projectRoot "print-agent/Urbeat.PrintAgent/Urbeat.PrintAgent.csproj"
$windowsPublish = Join-Path $projectRoot "print-agent/out/win-x64"
$linuxPublish = Join-Path $projectRoot "print-agent/out/linux-x64"
$downloadsRoot = Join-Path $projectRoot $OutputRoot
$windowsTarget = Join-Path $downloadsRoot "windows"
$linuxTarget = Join-Path $downloadsRoot "linux"

New-Item -ItemType Directory -Force -Path $windowsPublish | Out-Null
New-Item -ItemType Directory -Force -Path $linuxPublish | Out-Null
New-Item -ItemType Directory -Force -Path $windowsTarget | Out-Null
New-Item -ItemType Directory -Force -Path $linuxTarget | Out-Null

dotnet publish $agentProject -c $Configuration -r win-x64 --self-contained false -o $windowsPublish
dotnet publish $agentProject -c $Configuration -r linux-x64 --self-contained false -o $linuxPublish

Copy-Item (Join-Path $downloadsRoot "windows/install-local-agent.ps1") $windowsPublish -Force
Copy-Item (Join-Path $downloadsRoot "windows/urbeat-print-agent-startup.bat") $windowsPublish -Force
Copy-Item (Join-Path $downloadsRoot "windows/README.md") $windowsPublish -Force

Copy-Item (Join-Path $downloadsRoot "linux/install-local-agent.sh") $linuxPublish -Force
Copy-Item (Join-Path $downloadsRoot "linux/urbeat-print-agent.service") $linuxPublish -Force
Copy-Item (Join-Path $downloadsRoot "linux/README.md") $linuxPublish -Force

$windowsZip = Join-Path $windowsTarget "Urbeat.PrintAgent-win-x64.zip"
$linuxTarGz = Join-Path $linuxTarget "Urbeat.PrintAgent-linux-x64.tar.gz"

if (Test-Path $windowsZip) { Remove-Item $windowsZip -Force }
Compress-Archive -Path (Join-Path $windowsPublish "*") -DestinationPath $windowsZip -Force

if (Test-Path $linuxTarGz) { Remove-Item $linuxTarGz -Force }
tar -czf $linuxTarGz -C $linuxPublish .

"Pacotes gerados:"
"- $windowsZip"
"- $linuxTarGz"
