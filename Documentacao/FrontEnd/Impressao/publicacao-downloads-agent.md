# Publicação de Downloads do Agent

## Objetivo

Servir os binários do `local-agent`, scripts de instalação Linux e driver Windows da POS-58 pelo mesmo domínio do dashboard via NGINX.

## Estrutura esperada no servidor

```text
/opt/urbeat/downloads/
  POSPrinterDriverSetup58mm.exe
  urbeat-print-agent/
    README.md
    windows/
      Urbeat.PrintAgent-win-x64.zip
      install-local-agent.ps1
      urbeat-print-agent-startup.bat
      README.md
    linux/
      Urbeat.PrintAgent-linux-x64.tar.gz
      install-local-agent.sh
      urbeat-print-agent.service
      README.md
```

## URL públicas esperadas

- `https://urbeat.com.br/downloads/POSPrinterDriverSetup58mm.exe`
- `https://urbeat.com.br/downloads/urbeat-print-agent/windows/Urbeat.PrintAgent-win-x64.zip`
- `https://urbeat.com.br/downloads/urbeat-print-agent/windows/install-local-agent.ps1`
- `https://urbeat.com.br/downloads/urbeat-print-agent/windows/urbeat-print-agent-startup.bat`
- `https://urbeat.com.br/downloads/urbeat-print-agent/linux/Urbeat.PrintAgent-linux-x64.tar.gz`
- `https://urbeat.com.br/downloads/urbeat-print-agent/linux/install-local-agent.sh`
- `https://urbeat.com.br/downloads/urbeat-print-agent/linux/urbeat-print-agent.service`

## NGINX

O deploy deve servir `/downloads/` com alias para `/opt/urbeat/downloads/`.

## Publicação sugerida

1. Publicar o binário Windows zipado em `windows/`.
2. Publicar também o script PowerShell e o `.bat` de inicialização em `windows/`.
3. Publicar o binário Linux em `linux/` como `.tar.gz`.
4. Copiar o driver Windows da POS-58 (`POSPrinterDriverSetup58mm.exe`) para o root de `/downloads/`.
5. Garantir permissões de leitura para o usuário do NGINX.
