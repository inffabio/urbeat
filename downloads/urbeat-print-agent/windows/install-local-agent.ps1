param(
    [string]$InstallDir = (Join-Path $env:ProgramFiles "Urbeat Print Agent"),

    [string]$ServiceName = "UrbeatPrintAgent",

    [switch]$ConsoleStartup
)

$ErrorActionPreference = 'Stop'

$sourceDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$startupDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup'
$startupBat = Join-Path $InstallDir 'urbeat-print-agent-startup.bat'
$startupShortcut = Join-Path $startupDir 'Urbeat Print Agent.bat'
$exePath = Join-Path $InstallDir 'Urbeat.PrintAgent.exe'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

$dotnetRuntimes = & dotnet --list-runtimes 2>$null
if (-not ($dotnetRuntimes -match 'Microsoft\.AspNetCore\.App 9\.')) {
    Write-Warning "ASP.NET Core Runtime 9 nao encontrado. O servico/agente nao iniciara ate instalar o Hosting Bundle x64."
}

Write-Host "[Urbeat Print Agent] instalando em $InstallDir"

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Path (Join-Path $sourceDir '*') -Destination $InstallDir -Recurse -Force

function Remove-LegacyStartup {
    if (Test-Path $startupShortcut) {
        Remove-Item $startupShortcut -Force
        Write-Host "[Urbeat Print Agent] inicializacao legada removida"
    }
}

if (-not $ConsoleStartup -and $isAdmin) {
    $existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

    if ($existing) {
        if ($existing.Status -ne 'Stopped') {
            Stop-Service -Name $ServiceName -Force
        }
        & sc.exe config $ServiceName "binPath= `"$exePath`"" start= auto | Out-Null
    }
    else {
        & sc.exe create $ServiceName "binPath= `"$exePath`"" start= auto "DisplayName= Urbeat Print Agent" | Out-Null
    }

    & sc.exe description $ServiceName "Servico local de impressao automatica de pedidos do Urbeat. Escuta somente em 127.0.0.1:43111." | Out-Null
    & sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null

    Start-Service -Name $ServiceName
    Remove-LegacyStartup

    Write-Host "[Urbeat Print Agent] Windows Service '$ServiceName' instalado e iniciado automaticamente"
    Write-Host "[Urbeat Print Agent] API local: http://127.0.0.1:43111"
    return
}

if (-not $isAdmin -and -not $ConsoleStartup) {
    Write-Warning "Sem privilegios de administrador: instalando apenas o modo console por usuario."
}

New-Item -ItemType Directory -Force -Path $startupDir | Out-Null
if (Test-Path $startupShortcut) {
    Remove-Item $startupShortcut -Force
}
Copy-Item -Path $startupBat -Destination $startupShortcut -Force

Write-Host "[Urbeat Print Agent] arquivos copiados"
Write-Host "[Urbeat Print Agent] inicializacao automatica configurada para o usuario atual"
Write-Host "[Urbeat Print Agent] para instalar como servico, abra o PowerShell como administrador e rode sem -ConsoleStartup"
