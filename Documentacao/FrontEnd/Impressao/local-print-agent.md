# Local Print Agent

> Este documento detalha o agente local dentro da política oficial de impressão do Urbeat.
> Leia junto com `Documentacao/FrontEnd/Impressao/politica-oficial-impressao-urbeat.md`.

## Escopo operacional

- A configuracao de impressao e salva por loja atual do dashboard.
- A configuracao usada no aceite do pedido e sempre a configuracao atual salva dessa loja.
- O preset preferencial e `POS-58`.
- O padrao operacional dessa impressora e `58mm sem guilhotina`, portanto `autoCut` deve permanecer desligado.

## Regra por plataforma

- Android: usar `Bluetooth` como conexao preferencial.
- iOS: usar `Wi-Fi` como conexao preferencial.
- Windows: usar `local-agent` como caminho desktop preferencial quando a operacao precisar de impressao automatica real sem dialogo do navegador.
- Linux: usar `local-agent` com impressora cadastrada no `CUPS` como caminho desktop preferencial.
- macOS: usar `local-agent` como caminho desktop preferencial quando a operacao precisar de impressao automatica real sem dialogo do navegador.
- Wi-Fi direto para ESC/POS em rede local pode existir como alternativa, mas no Linux o padrão inicial deve ser `CUPS`.

## Linux

- O padrão oficial do Urbeat para Linux é: `CUPS + local-agent`.
- A impressora deve ser cadastrada primeiro no sistema operacional.
- O agent passa a usar a fila já conhecida pelo sistema para impressão automática.
- `raw ESC/POS` por rede pode ser adotado depois como opção avançada, mas não é o caminho padrão inicial.
- O guia operacional detalhado está em `Documentacao/FrontEnd/Impressao/configuracao-linux-cups.md`.

## Browser Print

- `browser-print` no desktop e manual/interativo por padrao.
- `browser-print` so deve ser tratado como automatico quando o ambiente estiver configurado em `kiosk` ou `silent print`.
- Fora de `kiosk`/`silent print`, o navegador pode abrir dialogo de confirmacao e bloquear a automacao completa.

## Aceite do pedido

- Ao aceitar um pedido (`Received -> Preparing`), a impressao dispara automaticamente usando a configuracao atual da loja.
- Nessa etapa nao deve haver prompt para escolher impressora.
- Se a configuracao atual estiver em Android Bluetooth, Wi-Fi ou `local-agent`, o fluxo tenta imprimir diretamente por esse modo.
- Se a configuracao atual estiver em `browser-print`, o comportamento continua dependente do ambiente do navegador; sem `kiosk`/`silent print`, o fluxo permanece manual/interativo.
