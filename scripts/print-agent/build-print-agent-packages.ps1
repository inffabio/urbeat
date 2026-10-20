param(
    [Parameter(Mandatory = $false)]
    [string]$Configuration = "Release",

    [Parameter(Mandatory = $false)]
    [string]$OutputRoot = "downloads/urbeat-print-agent",

    [Parameter(Mandatory = $false)]
    [string]$ProductVersion = ""
)

$ErrorActionPreference = 'Stop'

$projectRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$agentProject = Join-Path $projectRoot "print-agent/Urbeat.PrintAgent/Urbeat.PrintAgent.csproj"
$installerProject = Join-Path $projectRoot "print-agent/Urbeat.PrintAgent.Installer/Urbeat.PrintAgent.Installer.wixproj"
$windowsPublish = Join-Path $projectRoot "print-agent/out/win-x64"
$windowsMsiPublish = Join-Path $projectRoot "print-agent/out/win-x64-msi"
$linuxPublish = Join-Path $projectRoot "print-agent/out/linux-x64"
$downloadsRoot = Join-Path $projectRoot $OutputRoot
$windowsTarget = Join-Path $downloadsRoot "windows"
$linuxTarget = Join-Path $downloadsRoot "linux"

if ([string]::IsNullOrWhiteSpace($ProductVersion)) {
    $packageJson = Get-Content (Join-Path $projectRoot "frontend/package.json") -Raw | ConvertFrom-Json
    $ProductVersion = $packageJson.version
}

if ($ProductVersion -notmatch '^\d+\.\d+\.\d+') {
    throw "ProductVersion deve estar no formato MAJOR.MINOR.PATCH. Recebido: $ProductVersion"
}

New-Item -ItemType Directory -Force -Path $windowsPublish | Out-Null
New-Item -ItemType Directory -Force -Path $windowsMsiPublish | Out-Null
New-Item -ItemType Directory -Force -Path $linuxPublish | Out-Null
New-Item -ItemType Directory -Force -Path $windowsTarget | Out-Null
New-Item -ItemType Directory -Force -Path $linuxTarget | Out-Null

# ZIP fallback: framework-dependent publish.
dotnet publish $agentProject -c $Configuration -r win-x64 --self-contained false -o $windowsPublish
if ($LASTEXITCODE -ne 0) { throw "Falha ao publicar o ZIP Windows (exit $LASTEXITCODE)." }

# MSI: self-contained so a lay user does not need to install .NET 9 separately.
dotnet publish $agentProject -c $Configuration -r win-x64 --self-contained true -o $windowsMsiPublish
if ($LASTEXITCODE -ne 0) { throw "Falha ao publicar o agente self-contained para o MSI (exit $LASTEXITCODE)." }

if (-not (Test-Path (Join-Path $windowsMsiPublish "coreclr.dll"))) {
    throw "O publish do MSI não contém o runtime .NET (coreclr.dll). O MSI deve ser self-contained."
}

dotnet publish $agentProject -c $Configuration -r linux-x64 --self-contained false -o $linuxPublish
if ($LASTEXITCODE -ne 0) { throw "Falha ao publicar o pacote Linux (exit $LASTEXITCODE)." }

Copy-Item (Join-Path $downloadsRoot "windows/install-local-agent.ps1") $windowsPublish -Force
Copy-Item (Join-Path $downloadsRoot "windows/urbeat-print-agent-startup.bat") $windowsPublish -Force
Copy-Item (Join-Path $downloadsRoot "windows/README.md") $windowsPublish -Force

Copy-Item (Join-Path $downloadsRoot "linux/install-local-agent.sh") $linuxPublish -Force
Copy-Item (Join-Path $downloadsRoot "linux/urbeat-print-agent.service") $linuxPublish -Force
Copy-Item (Join-Path $downloadsRoot "linux/README.md") $linuxPublish -Force

$msiTarget = Join-Path $windowsTarget "Urbeat.PrintAgent-x64.msi"
if (Test-Path $msiTarget) { Remove-Item $msiTarget -Force }

dotnet build $installerProject -c $Configuration -p:ProductVersion=$ProductVersion -p:PublishDir=$windowsMsiPublish --nologo -v:minimal
if ($LASTEXITCODE -ne 0) { throw "Falha ao gerar o MSI (exit $LASTEXITCODE)." }

$msi = Get-ChildItem (Join-Path $projectRoot "print-agent/Urbeat.PrintAgent.Installer/bin") -Recurse -Filter "*.msi" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $msi -or $msi.Length -le 0) {
    throw "MSI não encontrado ou vazio após o build do instalador."
}

$wixDll = Get-ChildItem (Join-Path $env:USERPROFILE ".nuget/packages/wixtoolset.sdk") -Recurse -Filter "wix.dll" -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '[\\/]net6\.0[\\/]wix\.dll$' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1

if (-not $wixDll) {
    throw "WiX SDK não encontrado para validar o MSI."
}

dotnet $wixDll.FullName msi validate $msi.FullName
if ($LASTEXITCODE -ne 0) { throw "Validação do MSI falhou (exit $LASTEXITCODE)." }

Copy-Item $msi.FullName $msiTarget -Force
if (-not (Test-Path $msiTarget) -or (Get-Item $msiTarget).Length -le 0) {
    throw "Falha ao copiar o MSI para $msiTarget."
}

$windowsZip = Join-Path $windowsTarget "Urbeat.PrintAgent-win-x64.zip"
$linuxTarGz = Join-Path $linuxTarget "Urbeat.PrintAgent-linux-x64.tar.gz"

if (Test-Path $windowsZip) { Remove-Item $windowsZip -Force }
Compress-Archive -Path (Join-Path $windowsPublish "*") -DestinationPath $windowsZip -Force

if (Test-Path $linuxTarGz) { Remove-Item $linuxTarGz -Force }
tar -czf $linuxTarGz -C $linuxPublish .
if ($LASTEXITCODE -ne 0) { throw "Falha ao gerar o pacote Linux (exit $LASTEXITCODE)." }

"Pacotes gerados:"
"- MSI (obrigatório, self-contained): $msiTarget (versão $ProductVersion)"
"- ZIP fallback (framework-dependent): $windowsZip"
"- Linux: $linuxTarGz"
