# Som de novos pedidos no painel do lojista

## Objetivo

Garantir que o som de novos pedidos comece ligado para lojistas que nunca
escolheram uma preferência e concentrar o controle do som na barra lateral do
painel.

## Comportamento aprovado

- Sem preferência salva, `OrderSoundAlertService.enabled` inicia como `true`.
- Uma preferência explícita `off` continua sendo respeitada.
- Uma preferência explícita `on` continua sendo respeitada.
- O banner/atalho superior `Ativar som de pedidos` será removido do shell
  principal do dashboard.
- O botão de som da barra lateral será o único controle do dashboard principal.
- Se o som estiver ligado, mas o navegador ainda não tiver liberado áudio
  automático, o primeiro toque no botão lateral deverá liberar o áudio em vez de
  silenciar imediatamente.
- Após o áudio ser liberado, o botão alternará entre ligar e silenciar.
- Em mobile, o comportamento será o mesmo após abrir o menu lateral; o toque do
  usuário satisfaz a exigência de gesto do navegador.
- Os controles da janela separada de pedidos serão preservados, pois nessa rota
  a barra lateral fica oculta.

## Limites

- Não alterar SignalR, recebimento de pedidos ou lógica de notificação.
- Não alterar a persistência das preferências explícitas existentes.
- Não alterar backend, checkout, pedidos ou autenticação.

## Implementação provável

- Ajustar `SellerAppShellComponent.toggleSound()` para considerar
  `soundNeedsActivation()` antes de silenciar.
- Remover o `activation-banner` do template do shell principal.
- Atualizar testes do `OrderSoundAlertService` e do shell para cobrir default
  ligado, preferência `off`, primeiro toque no botão lateral e remoção do
  atalho superior.

## Critérios de aceite

1. Novo lojista inicia com som ligado.
2. Lojista que escolheu desligar continua com som desligado.
3. Não existe atalho/banner de ativação no topo do dashboard.
4. O botão lateral libera o áudio no primeiro toque quando necessário.
5. O botão lateral permite silenciar e reativar o som posteriormente.
6. O comportamento funciona no layout mobile e na barra lateral desktop.
7. A janela separada de pedidos mantém seus controles atuais.
