# Urbeat Print Agent Downloads

Este diretório representa a estrutura esperada em produção para os downloads servidos pelo mesmo domínio do dashboard.

## URLs esperadas

- `/downloads/urbeat-print-agent/windows/Urbeat.PrintAgent-x64.msi` (principal)
- `/downloads/urbeat-print-agent/windows/Urbeat.PrintAgent-win-x64.zip` (fallback técnico)
- `/downloads/urbeat-print-agent/windows/install-local-agent.ps1`
- `/downloads/urbeat-print-agent/windows/urbeat-print-agent-startup.bat`
- `/downloads/urbeat-print-agent/windows/README.md`
- `/downloads/urbeat-print-agent/linux/Urbeat.PrintAgent-linux-x64.tar.gz`
- `/downloads/urbeat-print-agent/linux/install-local-agent.sh`
- `/downloads/urbeat-print-agent/linux/urbeat-print-agent.service`
- `/downloads/POSPrinterDriverSetup58mm.exe`

## Observações

- O MSI Windows é self-contained, instala o agente como serviço de inicialização automática e não
  exige runtime .NET separado.
- O ZIP Windows permanece como fallback framework-dependent e contém o executável, configuração e
  script PowerShell.
- O pacote Windows deve ser acompanhado por script de instalação e opção de inicialização automática.
- O pacote Linux deve conter o binário publicado, `appsettings.json` e a documentação de instalação.
- O arquivo `POSPrinterDriverSetup58mm.exe` é servido no mesmo root de `/downloads/`.
- Em produção, esses arquivos devem ser copiados para `/opt/urbeat/downloads/` no servidor.
