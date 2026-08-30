# Seller Subscription History Design

## Goal

Remodelar a tela de Mensalidade do dashboard para exibir o histórico de cobranças do lojista em uma tabela ordenada do vencimento mais recente para o mais antigo, com período, vencimento, valor e status persistidos no banco.

## Approved Scope

- Reutilizar `SellerSubscriptionChargeHistory`; não criar uma segunda tabela de cobranças.
- Adicionar `BillingPeriodStartUtc` e `BillingPeriodEndUtc` à cobrança.
- Manter `DueDateUtc`, `Amount`, `BillingStatus`, `PaidAtUtc`, referência externa, status do gateway e payload bruto.
- Ordenar no backend por `DueDateUtc` descendente e por `CreatedAtUtc` descendente como desempate.
- Exibir a tabela no dashboard com as colunas `Período`, `Vencimento`, `Valor` e `Status`.
- Cobranças pagas exibem `Pago em dd/MM/yyyy`.
- Cobranças vencidas ou pendentes exibem o botão `Pagar`.
- O botão `Pagar` permanece visível, desabilitado e sem ação até a definição do método de pagamento.
- O período mensal é calculado no recebimento da cobrança como `DueDateUtc` até `DueDateUtc.AddMonths(1)` quando o gateway não fornecer datas de ciclo.
- Datas continuam armazenadas em UTC e formatadas com o helper de São Paulo.

## Data Model

`SellerSubscriptionChargeHistory` passa a representar uma cobrança/fatura mensal imutável por referência do gateway, com atualizações idempotentes de status e pagamento provenientes do webhook. `BillingPeriodStartUtc` e `BillingPeriodEndUtc` são persistidos para que a UI não precise inferir o período. A migration preenche registros existentes com o vencimento como início e um mês depois como fim, preservando a visualização histórica sem perder linhas.

## API

`GET /api/subscriptions/my/charges` continua sendo o endpoint do lojista. O DTO passa a retornar `BillingPeriodStartUtc` e `BillingPeriodEndUtc`. A consulta mantém isolamento por `SellerUserId`, ordenação decrescente e projeção somente dos campos necessários.

Não será criado endpoint de pagamento nesta etapa. O botão não inicia checkout, não chama gateway e informa visualmente que a opção estará disponível quando o método for definido.

## UI

- Remover controles de período que não têm comportamento definido.
- Preservar cabeçalho, atualização manual, estados de carregamento/erro/vazio e resumo da assinatura.
- Destacar a tabela como o elemento principal da página.
- Usar layout responsivo existente: tabela no desktop e linhas empilhadas no mobile.
- Aplicar estados visuais consistentes para pago, vencendo, em atraso, bloqueado e não contratado.
- Botão `Pagar` desabilitado deve ter `aria-disabled="true"`, tooltip/texto acessível e não emitir ação.

## Testing

- Backend: migration/model mapping, ordenação decrescente, isolamento por vendedor e projeção do período.
- Webhook: criação e atualização idempotente preservando/preenchendo o período.
- Frontend: renderização das quatro colunas, ordenação recebida, status pago/não pago e botão sem ação.
- Regressão: estados de loading, erro, vazio e dashboard existente.

## Exclusions

- Nenhum gateway ou método de pagamento será escolhido ou integrado agora.
- Nenhum cartão, Pix, boleto, QR Code ou checkout será criado nesta etapa.
- Nenhuma alteração no bloqueio de loja por inadimplência.
