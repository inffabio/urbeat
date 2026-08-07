# Empacotamento do Print Agent

## Objetivo

Gerar os pacotes Windows e Linux do `Urbeat.PrintAgent` no formato esperado pela tela `/app/instalar` e pela estrutura de downloads do servidor.

## Script de build

```powershell
./scripts/print-agent/build-print-agent-packages.ps1
```

## Saída esperada

- `downloads/urbeat-print-agent/windows/Urbeat.PrintAgent-win-x64.zip`
- `downloads/urbeat-print-agent/linux/Urbeat.PrintAgent-linux-x64.tar.gz`

## Conteúdo esperado

### Windows

- binário publicado do agent
- `install-local-agent.ps1`
- `urbeat-print-agent-startup.bat`
- `README.md`

### Linux

- binário publicado do agent
- `install-local-agent.sh`
- `urbeat-print-agent.service`
- `README.md`

## Publicação no servidor

```powershell
./scripts/print-agent/publish-print-agent-to-downloads.ps1 -ServerIP "136.248.115.135" -SSHUser "ubuntu"
```

O script copia os binários e arquivos auxiliares para `/opt/urbeat/downloads/` no layout esperado pelo NGINX.
