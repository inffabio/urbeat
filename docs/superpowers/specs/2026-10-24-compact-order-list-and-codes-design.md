# Lista compacta de pedidos e códigos simplificados

## Objetivo

Reduzir o espaço ocupado pelos pedidos aceitos na tela do vendedor e simplificar o código dos novos pedidos.

## Regras funcionais

### Lista de pedidos aceitos

- Pedidos no status `Recebido` permanecem na área de novos pedidos para serem aceitos.
- Ao aceitar, o pedido passa para uma lista vertical única.
- A lista reúne pedidos em `Preparando`, `Pronto`, `Em entrega` e `Entregue`.
- Pedidos concluídos pelo vendedor continuam fora da lista.
- A ordenação é decrescente por `createdAtUtc`, com o mais recente primeiro.
- Cada pedido é um accordion fechado inicialmente, com chevron de abrir/fechar.
- O cabeçalho exibe o código e o nome do cliente.
- Ao abrir, o pedido exibe os detalhes atuais e mantém as ações de mudança de status.
- O comportamento vale tanto para o dashboard quanto para a janela `/app/pedidos-separados`.

### Código do pedido

- Novos pedidos serão gerados pelo backend sem o prefixo `URB-`, mantendo oito caracteres alfanuméricos.
- Pedidos existentes não serão alterados; códigos antigos como `URB-ABC123` permanecem válidos.
- O valor persistido do código é usado sem transformação em checkout, API, SignalR, notificações e impressão.
- Não haverá migração ou atualização dos registros históricos.

## Solução técnica

No frontend, substituir a grade de colunas de status por uma lista única derivada dos grupos já carregados, ordenada por `createdAtUtc`. O estado de expansão será local ao componente, identificado por `order.id`, e o cabeçalho do accordion será um botão acessível com `aria-expanded` e `aria-controls`. A área de novos pedidos mantém o fluxo de aceite.

No backend, alterar apenas a geração em `CheckoutService.GenerateRandomCode()` para retornar o código alfanumérico sem prefixo. A unicidade global existente será preservada. O parâmetro legado `Order.CodePrefix` não será aplicado a novos pedidos; não será necessário migration, pois o formato não é restrito pelo schema.

## Critérios de aceite

1. Novos pedidos são criados sem `URB-` e continuam únicos.
2. Pedidos antigos não são modificados.
3. Pedido aceito desaparece de “Novos pedidos” e aparece na lista vertical.
4. A lista mostra o pedido mais recente primeiro.
5. Cada pedido abre/fecha por clique no cabeçalho e exibe um chevron coerente.
6. O título mostra código e nome do cliente.
7. Ações de status continuam funcionando dentro do pedido expandido.
8. Dashboard e janela separada usam o mesmo layout compacto.
9. Testes backend, frontend e build passam.

## Riscos e compatibilidade

- Integrações que assumam o prefixo `URB-` devem aceitar o novo formato; o contrato continua sendo uma string única.
- Pedidos antigos e novos coexistirão, portanto a UI não deve remover prefixos de forma indiscriminada.
- A ordenação usa timestamps UTC já persistidos; a apresentação não altera o valor.
