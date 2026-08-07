# Urbeat Print Agent para Windows

## Conteúdo esperado do pacote

- `Urbeat.PrintAgent.exe`
- `Urbeat.PrintAgent.runtimeconfig.json`
- `Urbeat.PrintAgent.deps.json`
- `appsettings.json`
- `install-local-agent.ps1`
- `urbeat-print-agent-startup.bat`

## Instalação rápida

1. Extraia `Urbeat.PrintAgent-win-x64.zip`.
2. Instale o driver da POS-58 se a impressora ainda não estiver cadastrada no Windows.
3. Abra o PowerShell como administrador.
4. Execute:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\install-local-agent.ps1
```

## Resultado esperado

- os arquivos são copiados para `C:\Program Files\Urbeat Print Agent`
- um atalho de inicialização é criado para o usuário atual
- o agent sobe em `http://127.0.0.1:43111`
- o dashboard pode usar o modo `local-agent` para impressão automática

## Observações

- Para operação de balcão, deixe o Windows com login automático e mantenha o atalho do agent na inicialização.
- Se preferir, a equipe pode evoluir depois para Windows Service, mas o startup em login já atende o MVP.
