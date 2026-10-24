# Encerramento do pedido e tela separada de pedidos

## Objetivo

Simplificar a navegação do acompanhamento do pedido, encerrar visualmente a experiência quando o pedido for entregue e permitir que o quadro de pedidos do lojista seja aberto em uma janela independente para uso em segundo monitor.

## Escopo

### Storefront do cliente

- Trocar o botão principal `ACOMPANHAR PEDIDO` por `VOLTAR AO CARDÁPIO`.
- Remover o link inferior duplicado `Voltar ao cardápio`.
- Ao status `Delivered`, exibir modal de encerramento com `Bom apetite!`.
- Após a confirmação do modal, limpar somente estado local do storefront: carrinho, pedido acompanhado e ações/overlays temporários; navegar para o cardápio.
- Preservar autenticação, usuário e histórico persistido no backend.
- Evitar reapresentação repetitiva do modal para o mesmo pedido na mesma sessão.

### Quadro de pedidos do lojista

- Adicionar `Separar tela pedidos` ao lado de `Tela cheia` na página `/app/pedidos`.
- Abrir a rota dedicada `/app/pedidos-separados` em nova janela, reutilizando o mesmo quadro e os mesmos controles do topo.
- A janela separada poderá usar o botão `Tela cheia`; para movê-la entre monitores, o lojista usa a barra nativa do navegador antes de ativar fullscreen ou posiciona a janela no monitor desejado antes do fullscreen.
- Sincronizar o modo entre a janela principal e a janela separada usando `BroadcastChannel`, com fallback por `localStorage`.
- No modo separado, a instância original mantém controles e navegação, mas apresenta a área de pedidos desabilitada/cinza e não renderiza novos pedidos.
- Na janela separada, os eventos SignalR e a lista de pedidos permanecem ativos; o dashboard original deixa de renderizar novos pedidos na área cinza.
- Alterar o rótulo para `Fixar ao Dashboard`; ao confirmar, fechar a janela separada e restaurar a área original.
- Se popup/tela cheia forem bloqueados ou a janela secundária for fechada, restaurar o modo normal sem perder o estado carregado.

## Arquitetura e estado

O comportamento será implementado no frontend Angular existente, sem exclusão ou alteração de dados no backend. Um serviço/estado compartilhado de modo separado será responsável por:

- identificar a janela do dashboard e a janela da rota `/app/pedidos-separados`;
- publicar transições `open`, `close` e `secondary-closed` entre contextos;
- impedir que a janela original processe/renderize novos pedidos enquanto estiver separada;
- limpar recursos de `BroadcastChannel`, listeners e referências a `Window` no destroy.

O `CustomerOrderTrackingService` e `CartService` continuarão sendo usados para limpar apenas estado local, sem logout e sem remover histórico remoto.

## Critérios de aceite

1. O botão de acompanhamento é substituído por `Voltar ao cardápio` e o link inferior é removido.
2. Pedido entregue exibe o modal `Bom apetite!`.
3. Confirmar o modal limpa o estado local do storefront, mantém o usuário autenticado e navega ao cardápio.
4. O lojista pode abrir pedidos em nova janela pelo botão ao lado de `Tela cheia`.
5. A área de pedidos da janela original fica cinza e não recebe/renderiza novos pedidos durante a separação, mas seus controles continuam acessíveis.
6. A janela separada recebe pedidos em tempo real.
7. `Fixar ao Dashboard` encerra a janela separada e restaura pedidos e funcionalidades no dashboard.
8. Bloqueio de popup, fechamento da janela ou reconexão SignalR não deixam o sistema preso no modo separado.
9. Testes unitários de estado, componentes e build de produção passam.

## Riscos

- Navegadores podem bloquear `window.open` ou exigir interação do usuário para tela cheia; o estado deve voltar ao normal nesses casos.
- `BroadcastChannel` pode não existir em ambientes antigos; o fallback por evento de `storage` deve cobrir essa situação.
- Eventos SignalR podem ser entregues em ambas as janelas; apenas a janela separada deve processar/renderizar pedidos durante o modo separado.
