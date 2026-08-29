# Acompanhamento de Pedidos do Cliente

## Objetivo

Permitir que o cliente acompanhe pedidos pagos e receba mudanças de status em tempo real, com acesso persistente pelo item `Pedidos` do menu inferior.

## Comportamento

- Após o pagamento aprovado ou o pedido recebido, o pedido é registrado no acompanhamento do navegador.
- O item `Pedidos` do footer fica habilitado somente quando houver pedidos ativos.
- Com pedidos ativos, o item usa a cor vermelha da marca e exibe a quantidade no badge.
- O toque em `Pedidos` abre uma lista de pedidos ativos.
- A seleção de um pedido abre `/pedido/:orderId`, com uma barra de status horizontal no topo e os detalhes do pedido abaixo.
- Pedidos entregues ou cancelados deixam a lista ativa, mas permanecem acessíveis como histórico recente.

## Estado e SignalR

- Um serviço global de acompanhamento mantém os IDs acompanhados e os detalhes carregados.
- Os IDs são persistidos no `localStorage` para sobreviver a recarregamentos.
- O Customer Hub é iniciado uma única vez pelo serviço compartilhado.
- O listener de `OrderStatusUpdated` é registrado antes do início do acompanhamento do pedido.
- Eventos são filtrados pelos IDs acompanhados e atualizam os detalhes imediatamente.
- Polling permanece como fallback quando o SignalR não puder conectar.

## Rotas e UI

- Adicionar uma rota de lista `/:storePath/pedidos`.
- Reutilizar a tela existente `/:storePath/pedido/:orderId` para detalhes e status.
- A lista deve permitir escolher qualquer pedido ativo, funcionar em mobile e manter alvos de toque de pelo menos 44px.
- A barra horizontal mostra as etapas `Recebido`, `Preparando`, `Pronto`, `Saiu para entregar` e `Entregue`, com o status atual destacado.
- Abaixo da barra aparecem os itens individualmente, quantidades, opções selecionadas, subtotal, frete quando aplicável e total.
- O estado vazio informa que não há pedidos ativos e oferece retorno ao cardápio.

## Segurança e consistência

- O cliente não calcula status, valores ou totais; usa os dados retornados pela API.
- Eventos de outro pedido não alteram a tela aberta.
- Respostas antigas de polling ou carregamento não podem sobrescrever dados mais novos.
- Falhas do SignalR não bloqueiam a consulta periódica do pedido.

## Testes

- Registro do pedido após pagamento/recebimento.
- Persistência e restauração dos IDs acompanhados.
- Atualização via `OrderStatusUpdated` e filtro por pedido.
- Fallback de polling.
- Footer habilitado, vermelho e com badge para um ou vários pedidos.
- Lista navega para cada pedido e estado vazio funciona.
- Tela de detalhes atualiza a barra de status sem regressões.
