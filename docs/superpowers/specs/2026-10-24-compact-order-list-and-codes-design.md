# Lista compacta de pedidos e códigos simplificados

## Objetivo

Reduzir o espaço ocupado pelos pedidos aceitos na tela do vendedor e simplificar o código dos novos pedidos.

## Regras funcionais

### Quadro de pedidos

- Pedidos no status `Recebido` permanecem na área de novos pedidos para serem aceitos.
- Após o aceite, os pedidos voltam a ser organizados em colunas por status: `Preparando`, `Pronto`, `Em entrega` e `Concluído`.
- Pedidos concluídos pelo vendedor continuam fora do quadro.
- Cada coluna mantém os cards, detalhes, ações de status, impressão e ordenação decrescente por `createdAtUtc`.
- O comportamento vale tanto para o dashboard quanto para a janela `/app/pedidos-separados`.

### Janela separada

- A rota `/app/pedidos-separados` continua autenticada e reutiliza o componente de pedidos.
- A nova janela exibe somente a página de pedidos, seu cabeçalho e controles (`Tela cheia`, `Fixar ao Dashboard` e `Atualizar`).
- Sidebar, menu móvel, banners do shell, navegação geral e demais elementos do dashboard ficam ocultos nessa rota.
- O quadro de pedidos e o fluxo SignalR permanecem ativos na janela separada.

### Código do pedido

- Novos pedidos serão gerados pelo backend sem o prefixo `URB-`, mantendo oito caracteres alfanuméricos.
- Pedidos existentes não serão alterados; códigos antigos como `URB-ABC123` permanecem válidos.
- O valor persistido do código é usado sem transformação em checkout, API, SignalR, notificações e impressão.
- Não haverá migração ou atualização dos registros históricos.

## Solução técnica

No frontend, manter a grade de colunas de status derivada dos grupos já carregados e reutilizar o template de card existente. A área de novos pedidos mantém o fluxo de aceite. A rota separada será identificada no seller shell para ocultar o chrome do dashboard e deixar apenas o outlet da página de pedidos em largura total.

No backend, alterar apenas a geração em `CheckoutService.GenerateRandomCode()` para retornar o código alfanumérico sem prefixo. A unicidade global existente será preservada. O parâmetro legado `Order.CodePrefix` não será aplicado a novos pedidos; não será necessário migration, pois o formato não é restrito pelo schema.

## Critérios de aceite

1. Novos pedidos são criados sem `URB-` e continuam únicos.
2. Pedidos antigos não são modificados.
3. Pedido aceito desaparece de “Novos pedidos” e aparece na coluna `Preparando`.
4. As colunas mostram os pedidos mais recentes primeiro.
5. Cada coluna exibe o código, cliente, detalhes e ações do pedido.
6. Ações de status continuam funcionando em todos os cards.
7. A janela separada mostra somente a página de pedidos e seu cabeçalho de controles.
8. Dashboard e janela separada recebem pedidos em tempo real.
9. Testes backend, frontend e build passam.

## Riscos e compatibilidade

- Integrações que assumam o prefixo `URB-` devem aceitar o novo formato; o contrato continua sendo uma string única.
- Pedidos antigos e novos coexistirão, portanto a UI não deve remover prefixos de forma indiscriminada.
- A ordenação usa timestamps UTC já persistidos; a apresentação não altera o valor.
