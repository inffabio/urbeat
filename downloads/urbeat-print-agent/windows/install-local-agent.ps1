param(
    [string]$InstallDir = "C:\Program Files\Urbeat Print Agent"
)

$sourceDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$startupDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup'
$startupBat = Join-Path $InstallDir 'urbeat-print-agent-startup.bat'
$startupShortcut = Join-Path $startupDir 'Urbeat Print Agent.bat'

Write-Host "[Urbeat Print Agent] instalando em $InstallDir"

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Path (Join-Path $sourceDir '*') -Destination $InstallDir -Recurse -Force

if (Test-Path $startupShortcut) {
    Remove-Item $startupShortcut -Force
}

Copy-Item -Path $startupBat -Destination $startupShortcut -Force

Write-Host "[Urbeat Print Agent] arquivos copiados"
Write-Host "[Urbeat Print Agent] inicialização automática configurada para o usuário atual"
Write-Host "[Urbeat Print Agent] execute o dashboard e configure o modo local-agent"
