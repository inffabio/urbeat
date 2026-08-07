# Política Oficial de Impressão do Urbeat

## 1. Padrão operacional

- A impressora padrão do Urbeat é a `POS-58`.
- Papel padrão: `58mm`.
- A `POS-58` é tratada como impressora **sem guilhotina**.
- Portanto, `autoCut` deve permanecer desligado nesse perfil.

## 2. Regra de automação

- Ao **aceitar o pedido**, a impressão deve disparar **automaticamente**.
- Não deve aparecer tela de confirmação.
- Não deve aparecer escolha manual de impressora.
- A impressão usa sempre a **configuração atual da loja no dashboard**.

## 3. Prioridade por plataforma

- **Android**: preferir `Bluetooth`.
- **iOS**: preferir `Wi-Fi/Ethernet`.
- **Windows / Linux / macOS**: preferir `local-agent`.
- **Browser print**: usar apenas como fallback/manual.

## 4. Desktop

- Em desktop, a solução preferencial do Urbeat é o `local-agent`.
- O `local-agent` é o caminho oficial para impressão automática robusta.
- `browser-print` não deve ser tratado como automação principal.

## 4.1. Padrão Linux

- Em Linux, o padrão oficial do Urbeat é usar `CUPS` / fila do sistema junto com o `local-agent`.
- Para impressoras USB e também para boa parte das impressoras de rede, a loja deve primeiro cadastrar a impressora no sistema.
- `raw ESC/POS` por rede pode existir como opção avançada no futuro, mas não é o padrão inicial de operação.

## 5. Browser print

- No desktop, `browser-print` é **manual/interativo** por padrão.
- Só pode ser tratado como automático quando a máquina estiver em:
  - `kiosk`
  - `silent print`
  - ambiente controlado e dedicado

## 6. Configuração por loja

- A configuração de impressão é sempre vinculada à **loja atual do dashboard**.
- Cada loja pode ter:
  - modelo de impressora
  - tipo de conexão
  - largura do papel
  - número de vias
  - comportamento de impressão

## 7. Hierarquia de escolha

1. `POS-58 / 58mm / sem guilhotina`
2. `Android Bluetooth`
3. `Desktop com local-agent`
4. `Wi-Fi/Ethernet`
5. `browser-print como fallback`

## 8. Objetivo

- Garantir impressão rápida, previsível e sem intervenção do operador.
- Reduzir dependência do navegador.
- Favorecer uma operação estável em balcão e cozinha.
