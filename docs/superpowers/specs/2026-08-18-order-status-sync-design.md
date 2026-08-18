# Sincronizacao de Status e Confirmacao de Entrega

## Objetivo

Sincronizar o fluxo operacional de pedidos entre o painel do lojista e o storefront, mantendo o historico e os snapshots do pedido. O cliente deve acompanhar as transicoes em tempo real e confirmar a entrega sem sair da tela de acompanhamento.

## Escopo Funcional

- Manter o fluxo `Received -> Preparing -> Ready -> OnDelivery -> Delivered`.
- Gravar cada transicao em `OrderStatusHistory`, com usuario, horario e observacao.
- Preservar os snapshots existentes de itens, precos, endereco e total.
- Adicionar `DeliveryConfirmedAtUtc` ao pedido.
- Adicionar `SellerCompletedAtUtc` ao pedido para ocultacao persistida no painel do dia.
- Permitir confirmacao somente ao cliente dono de um pedido `Delivered` cujo tipo seja delivery.
- Manter a confirmacao separada do status operacional.
- Permitir ao vendedor marcar um pedido como concluido apenas para remove-lo da tela de pedidos do dia.
- Manter pedidos concluidos no historico e preservar todos os snapshots.

## Sincronizacao

- O backend emitira `OrderStatusUpdated` no Customer Hub apos a transicao ser persistida.
- O evento incluira `orderId`, `orderCode`, `status` e `changedAtUtc`.
- O tracking do cliente escutara o evento e atualizara a barra de progresso, a etapa atual, os horarios e o botao de confirmacao.
- O tracking usara polling como fallback quando o SignalR estiver indisponivel ou apos reconexao.
- O painel do vendedor continuara recebendo novos pedidos em tempo real e recarregara as colunas apos transicoes.
- Eventos repetidos serao tratados de forma idempotente.

## Storefront

- A barra exibira as etapas `Received`, `Preparing`, `Ready`, `OnDelivery` e `Delivered`.
- Quando um pedido delivery atingir `Delivered`, exibira o botao flutuante `Confirmar entrega`.
- A confirmacao chamara uma API protegida pelo usuario do pedido.
- A API gravara `DeliveryConfirmedAtUtc` e um registro de auditoria da confirmacao, sem mudar o status.
- Apos sucesso, o botao desaparecera e o cliente permanecera na tela de pedidos.
- Pedidos de retirada nao exibirao o botao de confirmacao de entrega.

## Painel do Vendedor

- A coluna `Novos Pedidos` exibira cada pedido em duas linhas:
  - `#Numero do pedido`
  - `Primeiro nome - Telefone`
- O pedido aparecera imediatamente na coluna correspondente ao status atual apos o aceite.
- Cada transicao movera o pedido para a proxima coluna sem perder dados.
- A acao `Concluido` ocultara o pedido somente da tela de pedidos do dia.
- A acao nao excluira o pedido, nao removera snapshots e nao alterara o historico operacional.
- Erros de API exibirao retry e nenhuma transicao sera considerada concluida antes de resposta de sucesso.

## Persistencia e Autorizacao

- O backend validara que o vendedor possui a loja do pedido antes de alterar status.
- O backend validara que o cliente e o dono do pedido antes de confirmar a entrega.
- A confirmacao de entrega aceitara apenas pedidos no status `Delivered` e fulfillment `Delivery`.
- O snapshot de pedido permanecera imutavel apos a criacao.
- O historico de status continuara sendo a fonte auditavel das transicoes operacionais; a confirmacao e a ocultacao serao auditadas por seus campos e registros de auditoria.

## Abordagem Tecnica

1. Ajustar DTOs, entidade, migracao e servicos de pedido para a confirmacao de entrega e a acao de ocultar do painel.
2. Emitir um evento explicito de status no Customer Hub apos persistencia bem-sucedida.
3. Corrigir o cliente SignalR para escutar o evento explicito e manter polling de fallback.
4. Atualizar tracking, barra de progresso e confirmacao flutuante.
5. Atualizar painel do vendedor, colunas, resumo de novos pedidos e ocultacao visual persistida.
6. Cobrir transicoes, autorizacao, idempotencia, snapshots, eventos e estados de erro com testes unitarios, de componente e de integracao.

## Criterios de Aceite

- Ao aceitar um pedido, o cliente ve `Em preparo` sem atualizar manualmente.
- Ao marcar pronto, o cliente ve `Pronto` sem atualizar manualmente.
- Ao iniciar a entrega, o cliente ve `Em entrega` sem atualizar manualmente.
- Ao marcar entregue, o cliente ve `Entregue` e o botao `Confirmar entrega`.
- Ao confirmar, o cliente permanece na tela e a confirmacao fica gravada.
- O vendedor ve numero, primeiro nome e telefone na coluna de novos pedidos.
- O vendedor pode ocultar um pedido concluido sem apagar seu historico ou snapshots.
- Queda do SignalR nao impede a atualizacao eventual pelo polling.
- Nenhuma conta consegue alterar ou confirmar pedido de outro usuario.

## Fora de Escopo

- Criacao de um novo status `Confirmed`.
- Redirecionamento do cliente apos confirmar entrega.
- Exclusao fisica ou arquivamento definitivo de pedidos; a ocultacao do painel usa apenas `SellerCompletedAtUtc`.
- Alteracao dos snapshots de itens, precos, endereco ou total.
