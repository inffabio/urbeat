# Urbeat Print Agent Downloads

Este diretório representa a estrutura esperada em produção para os downloads servidos pelo mesmo domínio do dashboard.

## URLs esperadas

- `/downloads/urbeat-print-agent/windows/Urbeat.PrintAgent-win-x64.zip`
- `/downloads/urbeat-print-agent/windows/install-local-agent.ps1`
- `/downloads/urbeat-print-agent/windows/urbeat-print-agent-startup.bat`
- `/downloads/urbeat-print-agent/windows/README.md`
- `/downloads/urbeat-print-agent/linux/Urbeat.PrintAgent-linux-x64.tar.gz`
- `/downloads/urbeat-print-agent/linux/install-local-agent.sh`
- `/downloads/urbeat-print-agent/linux/urbeat-print-agent.service`
- `/downloads/POSPrinterDriverSetup58mm.exe`

## Observações

- O binário Windows deve conter o executável do agent e os arquivos mínimos de configuração.
- O pacote Windows deve ser acompanhado por script de instalação e opção de inicialização automática.
- O pacote Linux deve conter o binário publicado, `appsettings.json` e a documentação de instalação.
- O arquivo `POSPrinterDriverSetup58mm.exe` é servido no mesmo root de `/downloads/`.
- Em produção, esses arquivos devem ser copiados para `/opt/urbeat/downloads/` no servidor.
