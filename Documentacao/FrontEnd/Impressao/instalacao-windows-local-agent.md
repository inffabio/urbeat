# Instalação Windows do Local-Agent

## Pré-requisitos

- Windows com acesso ao dashboard da loja
- impressora POS-58 instalada no sistema ou driver disponível
- driver da POS-58, se necessário: `POSPrinterDriverSetup58mm.exe`

## Passos

1. Baixe `Urbeat.PrintAgent-win-x64.zip`.
2. Baixe o driver Windows da POS-58, se a impressora ainda não aparecer no sistema.
3. Extraia o pacote do agent.
4. Abra o PowerShell como administrador.
5. Rode:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\install-local-agent.ps1
```

## Comportamento esperado

- o agent inicia junto com o login do Windows
- escuta em `127.0.0.1:43111`
- a configuração de impressão continua sendo da loja atual do dashboard
- ao aceitar pedido, a impressão pode disparar automaticamente sem popup quando a loja estiver usando `local-agent`

## Observações

- Para um MVP produtivo, o startup por login é suficiente.
- Em evoluções futuras, o agent pode virar Windows Service.
