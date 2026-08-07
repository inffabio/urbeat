# [MVP] [Relatórios] RF72 - Relatório simples de pedidos da loja

**Épico:** Relatórios e evolução  
**Fase:** MVP  
**Perfil:** Vendedor  
**Prioridade:** Média  

## Descrição
Exibir ao vendedor um resumo simples da operação da loja.

## Regras de negócio
- Indicadores mínimos:
  - total de pedidos
  - total faturado
  - pedidos por período
- Definir se o faturamento considera pedidos pagos ou entregues.

## Critérios de aceite
- Vendedor visualiza os números básicos.
- Filtro simples por período funciona.
- Dados batem com a base de pedidos.

## Checklist técnico
- [ ] O endpoint já se encontra disponível: `GET /api/orders/store/report` no `OrdersController` (protegido pelo `Authorize(Policy = AuthorizationPolicies.SellerOnly)`).
- [ ] A consulta recebe via `FromQuery` um `startDateUtc` e um `endDateUtc`.
- [ ] O `IOrderReportService` retorna um Data Transfer Obejct `StoreOrdersSimpleReportResponseDto`.
- [ ] Construir dashboard em interface Angular visualizando faturamentos.

## Dependências
- RF44 - Histórico de pedidos da loja
- RF49 - Registro do status de pagamento

## Próximo card sugerido
- Fase 2: RF73 - Produto mais vendido