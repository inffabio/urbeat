# Dashboard analítico do lojista

**Data:** 2026-10-29
**Status:** especificação aprovada para implementação (proposta do solicitante)
**Escopo desta entrega:** somente este documento, atualizado com a grade paginada de vendas. Nenhum arquivo de aplicação, migração, teste ou coleção Postman foi criado ou alterado; nada foi commitado.

## 1. Objetivo

Substituir o conteúdo do dashboard atual (`/app/dashboard`) por um painel analítico com:

1. filtros de **dia**, **semana** (blocos do mês), **mês** e **ano**, com seletor de data/mês/ano iniciando no valor atual;
2. KPIs de **pedidos entregues**, **faturamento** e **ticket médio**;
3. **rosca de produtos por quantidade** em pedidos `Delivered`;
4. **barras dos 12 meses** do ano escolhido;
5. **distribuição por forma de pagamento**;
6. **grade paginada de vendas** com código do pedido, nome do cliente, itens expansíveis com preços, total da venda e taxa de entrega, com somatórios de **todos os registros filtrados** no rodapé, independentes da página;
7. remoção dos blocos **Atalhos rápidos** e **Resumo por serviço**;
8. agregação inteiramente no backend, por loja do lojista autenticado, com recorte de calendário em `America/Sao_Paulo`.

O painel continua operacional: aviso de mensalidade, aviso de impressora e "Pedidos em andamento" permanecem. A lista "Últimos pedidos" é substituída pela grade paginada de vendas (decisão D11), que usa a mesma regra `Delivered` e o mesmo recorte da analytics.

## 2. Fora de escopo

- Alterar checkout, cálculo de preços, status de pedido, impressão, assinatura ou qualquer fluxo transacional.
- Alterar os contratos existentes `GET /api/orders/store/report`, `GET /api/orders/store` e `GET /api/orders/store/customers`.
- Alterar o schema de `Order`, `OrderItem` ou criar migração. Não há `ProductId` em `OrderItem` e isso **não** será adicionado.
- Exportação CSV/Excel, comparação entre períodos, metas, alertas, cache distribuído ou pré-agregação.
- Persistir filtros em query string, `localStorage` ou preferências do usuário.
- Equipe/permissões por loja: hoje a autorização do lojista é por `Store.OwnerUserId` (índice único), e o painel agrega todas as lojas do proprietário.
- Nova biblioteca de gráficos, PrimeNG ou componentes Bootstrap novos.
- Criar endpoint por pedido para buscar itens: os itens da grade vêm embutidos na página; a composição completa (opções, adicionais, observações) continua em `/app/pedidos`.
- Exibir telefone, e-mail, documento, endereço, observações, PIN, rota ou motorista na grade.
- Sincronizar a coleção Postman nesta tarefa (o agente `postman-sync` deverá fazê-lo quando o endpoint existir).

## 3. Estado atual verificado (evidências)

Foram consultados `AGENTS.md`, `DESIGN.md`, o código do dashboard/relatórios, os testes existentes e a configuração de build. As regras aplicáveis foram preservadas: backend autoritativo, timestamps UTC, sem alteração de contratos existentes, tokens `--app-*`, alvos de toque ≥44px e comportamento mobile/PWA/Capacitor.

| Fonte | Situação encontrada |
| --- | --- |
| `frontend/src/app/features/seller-dashboard/seller-dashboard-page.component.ts` | Períodos `today`, `week`, `month`; `averageTicket = totalRevenue / totalOrders`; resumo de delivery/retirada e atalhos calculados no cliente; chamadas `getStoreReport` + `getStoreOrders({ pageSize: 100 })`. |
| `frontend/src/app/features/seller-dashboard/seller-dashboard-page.component.html` | 4 KPIs (incluindo "Pedidos em andamento"), tabela de pagamentos derivada dos até 100 pedidos carregados, "Atalhos rápidos", "Resumo por serviço", "Últimos pedidos" e "Pedidos em andamento". |
| `frontend/src/app/core/utils/sao-paulo-date.helper.ts` | `saoPauloPeriodRange` fixa o início em `03:00Z` e o fim em `now`; é compartilhado com `seller-orders-page.component.ts`. |
| `backend/src/Urbeat.WebApi/Controllers/OrdersController.cs` | `GET /api/orders/store/report` (`SellerOnly`) repassa datas UTC ao serviço. |
| `backend/src/Urbeat.Infrastructure/Services/OrderReportService.cs` | Filtra lojas por `OwnerUserId`, exclui `PendingPayment`, conta pedidos e soma `Total` apenas de `Delivered`; `EndDateUtc` é **inclusivo** (`<=`). |
| `backend/src/Urbeat.Infrastructure/Services/OrderService.cs` | `ListStoreOrdersAsync` limita `pageSize` a 100, ordena somente por `CreatedAtUtc` (sem desempate), devolve `CustomerPhoneNumber`, `AddressSummary` e `ItemsSummary` (sem preços) e não devolve somatórios agregados. |
| `backend/src/Urbeat.Domain/Entities/Order.cs` | `Status`, `CreatedAtUtc` (UTC), `PaymentMethod`, `Subtotal`, `DeliveryFee`, `Total`. |
| `backend/src/Urbeat.Infrastructure/Services/CheckoutService.cs` | `Total = Subtotal + DeliveryFee`; `OrderItem.Quantity` é a quantidade informada e `WeightGrams` guarda o peso de produtos vendidos por peso. |
| `backend/src/Urbeat.Domain/Entities/OrderItem.cs` | `ProductName` é snapshot; não existe `ProductId`; há `VariationName`, `WeightGrams`, `ChoiceOptionName`, `AdditionalNames`, `OptionPricesJson`. |
| `backend/src/Urbeat.Application/DTOs/OrderSummaryResponseDto.cs` | Expõe `CustomerName` e `CustomerPhoneNumber`, `AddressSummary`, `ItemsSummary`, rota/motorista; é o contrato atual de listagem e não deve ser reutilizado como contrato da grade (PII e itens sem preço). |
| `backend/src/Urbeat.Infrastructure/Persistence/ApplicationDbContext.cs` | Índice `(StoreId, Status, CreatedAtUtc)` em `Orders`; índice `OrderId` em `OrderItems`; `Store.OwnerUserId` é único. |
| `backend/src/Urbeat.Domain/Services/StoreOpeningHoursCalculator.cs` | Resolve `America/Sao_Paulo` com fallback Windows, mas o resolvedor é privado. |
| `frontend/package.json` | Não há biblioteca de gráficos; `bootstrap`, `bootstrap-icons` e `ionicons` estão disponíveis. |
| `frontend/src/app/shared/components/` | Existem `metric-card`, `empty-state`, `page-header`; não existe componente de gráfico. |
| `frontend/src/app/branding-consistency.spec.ts` | Copy voltada ao usuário deve usar `urbeat` minúsculo; o dashboard não está na lista verificada, mas a regra de marca continua válida. |

## 4. Decisões de escopo

| ID | Decisão |
| --- | --- |
| D1 | Remover **Atalhos rápidos** e **Resumo por serviço** (HTML, SCSS, computeds e imports que ficarem sem uso). |
| D2 | Manter mensalidade, aviso de impressora, "Pedidos em andamento" e a tabela/gráfico de formas de pagamento. "Últimos pedidos" é substituído pela grade paginada de vendas. |
| D3 | O dashboard passa a ter 3 KPIs: pedidos entregues, faturamento e ticket médio. O contador de pedidos em andamento deixa de ser KPI e passa a aparecer no cabeçalho do card "Pedidos em andamento". |
| D4 | A agregação é toda do backend; o cliente não soma valores nem calcula ticket. |
| D5 | O recorte é sempre pela **data de criação** (`Order.CreatedAtUtc`) convertida para o calendário local. |
| D6 | O ranking de produtos agrupa pelo **nome snapshot** de `OrderItem`; não há junção com `Product`. |
| D7 | Nenhuma etapa de agregação (KPIs, produtos, pagamentos e totais do rodapé) usa paginação, `Skip`/`Take` ou o limite de 100 pedidos do endpoint de listagem. A paginação se aplica apenas às linhas da grade. |
| D8 | O endpoint existente `/store/report` é preservado sem alteração e deixa de ser usado pelo dashboard. |
| D9 | O selo de pedidos da navegação lateral continua com a semântica atual (todos os pedidos do período, exceto `PendingPayment`), agora alimentado por `totalItems` da listagem, não pelo relatório antigo. |
| D10 | O seletor de data substitui o "date pill" estático (`Hoje, ...`), que seria ambíguo quando o usuário escolhe outro dia/mês/ano. |
| D11 | A grade paginada "Vendas do período" ocupa o lugar de "Últimos pedidos" (largura total, após os gráficos) e usa a mesma regra `Delivered`, o mesmo recorte e a mesma autorização por loja da analytics. |
| D12 | A grade usa um endpoint novo e aditivo, `GET /api/orders/store/sales`; `GET /api/orders/store` permanece inalterado e continua atendendo "Pedidos em andamento" e o selo da navegação. |
| D13 | O rodapé da grade soma `Order.Total` e `Order.DeliveryFee` de **todos** os registros filtrados. Como `Total` já inclui `DeliveryFee`, os dois valores são independentes e **nunca são somados**; rótulos e nota visível deixam isso explícito. |
| D14 | PII mínima na grade: apenas `customerName` (claim `FullName`). Telefone, e-mail, documento, endereço, observações, PIN, rota e motorista ficam fora do contrato. |
| D15 | Paginação por offset com ordenação determinística `CreatedAtUtc DESC, Id DESC`, página 1 e `pageSize` 20 por padrão, máximo 100. Itens são carregados apenas para os pedidos da página. |
| D16 | A grade mantém os totais e o número de itens da analytics para o mesmo filtro; divergência entre os dois endpoints é bug de contrato e deve ser coberta por teste. |

## 5. Calendário, filtros e seletores

### 5.1 Vocabulário

- `period`: `day` | `week` | `month` | `year`.
- `anchorDate`: data local (`yyyy-MM-dd`) que posiciona o período. É a data selecionada no filtro "Dia", o mês selecionado nos filtros "Semana"/"Mês" (dia 1) ou 1º de janeiro do ano selecionado no filtro "Ano". O backend usa apenas o dia, o mês ou o ano relevantes para o período.
- `weekBlock`: bloco de dias dentro do mês, de 1 a 5.
- "local" significa `America/Sao_Paulo`; "UTC" significa o instante persistido.

### 5.2 Intervalos (locais, com fim exclusivo)

| Período | Início local | Fim local (exclusivo) |
| --- | --- | --- |
| Dia | `anchorDate` 00:00 | `anchorDate + 1 dia` 00:00 |
| Semana | início do bloco no mês do `anchorDate` | fim do bloco (exclusivo) |
| Mês | primeiro dia do mês do `anchorDate` 00:00 | primeiro dia do mês seguinte 00:00 |
| Ano | 1º de janeiro do ano do `anchorDate` 00:00 | 1º de janeiro do ano seguinte 00:00 |

Blocos de semana (sempre dentro do mês):

| Bloco | Dias | Início | Fim exclusivo |
| --- | --- | --- | --- |
| 1 | 1–7 | dia 1 00:00 | dia 8 00:00 |
| 2 | 8–14 | dia 8 00:00 | dia 15 00:00 |
| 3 | 15–21 | dia 15 00:00 | dia 22 00:00 |
| 4 | 22–28 | dia 22 00:00 | dia 29 00:00 |
| 5 | 29–último dia | dia 29 00:00 | dia 1 do mês seguinte 00:00 |

Regras:

- O bloco 5 só existe quando o mês tem 29 dias ou mais. Fevereiro não bissexto tem apenas 4 blocos; fevereiro bissexto tem bloco 5 com o dia 29.
- Blocos não são semanas ISO e não atravessam meses.
- A semana nunca inclui dias do mês anterior ou seguinte.

Os limites locais são convertidos para UTC com `TimeZoneInfo` (seção 8.3), nunca com deslocamento fixo de `-03:00`.

### 5.3 Comportamento dos filtros

- Controle segmentado com `Dia`, `Semana`, `Mês` e `Ano`. Cada botão tem no mínimo 44px de altura e estado `aria-pressed`.
- Estado inicial: `period=day`, âncora = data local atual.
- O seletor contextual depende do período:
  - **Dia:** `<input type="date">` com `max` = data local atual; padrão = hoje.
  - **Semana:** `<select>` de mês + `<select>` de ano + grupo de blocos. O bloco padrão é o que contém o dia da âncora; se o mês/ano mudar, mantém o bloco atual quando ele existir e, caso contrário, usa o último bloco válido. O bloco 5 não é renderizado em fevereiro não bissexto.
  - **Mês:** `<select>` de mês + `<select>` de ano.
  - **Ano:** `<select>` de ano com as opções de `availableYears` (decrescente), sempre incluindo o ano corrente.
- Não são oferecidas datas futuras nos seletores de dia/mês/ano: dia máximo = hoje local; combinação ano/mês máxima = mês atual; ano máximo = ano atual. Os **blocos** de um mês passado ou do mês atual são sempre oferecidos, mesmo que o bloco comece depois de hoje (o recorte futuro simplesmente retorna zeros). O backend aceita qualquer data válida e devolve zeros para períodos sem dados.
- Trocar de período preserva a âncora quando ela faz sentido:
  - Dia → Semana/Mês: usa o mês da data selecionada.
  - Semana/Mês → Dia: usa o dia 1 do mês selecionado (o dia escolhido anteriormente não é preservado).
  - Qualquer → Ano: usa o ano selecionado.
  - Ano → Dia/Semana/Mês: usa 1º de janeiro do ano selecionado.
  - Ao voltar para Semana, se o bloco armazenado não existir no mês da âncora, usar o último bloco válido.
- Os seletores usam `<select>` nativos (não `input type="month"`) para funcionar de forma previsível no PWA/Capacitor iOS e Android. O dia usa `<input type="date">`.
- Cada seletor tem `<label>` visível e nome acessível; não há controle apenas por ícone.
- A URL não é alterada; recarregar a página volta ao padrão (decisão de escopo).

### 5.4 Rótulos

| Situação | Rótulo do período |
| --- | --- |
| Dia | `29/10/2026` |
| Semana | `29/10 a 31/10/2026` (inclui o ano uma vez) |
| Mês | `Outubro de 2026` |
| Ano | `2026` |

Os rótulos são de exibição; os valores enviados ao backend são `yyyy-MM-dd` e números.

## 6. Semântica dos indicadores

Todos os indicadores consideram **somente** pedidos com `Status == Delivered` e `CreatedAtUtc` no intervalo `[início, fim)`, restritos às lojas cujo `OwnerUserId` é o lojista autenticado.

| Indicador | Definição | Observações |
| --- | --- | --- |
| Pedidos entregues | `COUNT(orders)` do recorte | Não conta `PendingPayment`, `Cancelled` nem pedidos em andamento. |
| Faturamento | `SUM(Order.Total)` do recorte | `Total = Subtotal + DeliveryFee` no checkout. **Inclui taxas de entrega.** |
| Taxas de entrega | `SUM(Order.DeliveryFee)` do recorte | Informativo; **não é subtraído** do faturamento. |
| Ticket médio | `Faturamento / Pedidos entregues` | 2 casas, `MidpointRounding.AwayFromZero`; `0` quando não há pedidos entregues. |
| Distribuição de pagamento | Agrupamento por `Order.PaymentMethod` | Retorna quantidade de pedidos e faturamento por forma. |
| Produtos | Agrupamento por `OrderItem.ProductName` (trim) | Soma `Quantity` e `TotalPrice` dos itens de pedidos entregues do recorte. `TotalPrice` inclui variação/adicionais já cobrados no item. |
| Série mensal | 12 meses do `chartYear` | Cada mês usa seus próprios limites locais; meses sem pedidos entram com zero. |

### 6.1 Total, subtotal e taxa de entrega — sem dupla contagem

Avaliação sobre o modelo atual, confirmada no checkout:

- `Order.Subtotal = Σ(OrderItem.Quantity × OrderItem.UnitPrice)`.
- `OrderItem.TotalPrice = Quantity × UnitPrice`, portanto `Order.Subtotal = Σ(OrderItem.TotalPrice)`.
- `Order.Total = Order.Subtotal + Order.DeliveryFee` (`CheckoutService`).

Consequências obrigatórias para a grade e o rodapé:

1. **O total da venda já inclui a taxa de entrega.** A coluna "Taxa de entrega" é um **componente** do total, não um valor adicional.
2. **O rodapé tem dois somatórios independentes**: `salesTotal = Σ(Order.Total)` e `deliveryFees = Σ(Order.DeliveryFee)` sobre todos os registros filtrados. Nunca calcular `salesTotal + deliveryFees`, pois isso contaria a taxa duas vezes.
3. **Rótulos explícitos**: "Total das vendas (inclui taxas de entrega)" e "Taxas de entrega (já incluídas no total)", com nota visível "As taxas já estão dentro do total; não somar as duas colunas." O rodapé **não** exibe uma terceira linha de "total geral".
4. **Na linha expandida**, mostrar a identidade: `Subtotal dos itens` + `Taxa de entrega` = `Total da venda`. O subtotal exibido é `Order.Subtotal` (backend), e a soma dos itens exibidos deve fechar com ele.
5. **Nenhum total é recalculado no cliente.** Os valores por linha, o total e a taxa vêm persistidos; o cliente apenas formata. Se algum dia for necessário exibir "vendas sem taxa", o cálculo correto é `salesTotal - deliveryFees` (nunca uma soma).
6. **Frete grátis**: quando `DeliveryFee = 0`, a identidade continua `Total = Subtotal`; o somatório de taxas permanece correto.
7. **Consistência com a analytics**: `salesTotal` deve ser igual a `kpis.revenue` e `deliveryFees` deve ser igual a `kpis.deliveryFees` para o mesmo filtro. A grade e a analytics usam a mesma base `Delivered` e o mesmo intervalo.

### 6.2 Regras adicionais

- **Receita não usa `Payment`.** `Payment` só existe para pagamentos online; dinheiro e cartão na entrega não têm registro. A fonte é `Order.PaymentMethod` e `Order.Total`.
- **Faturamento não é caixa conciliado.** O cálculo não junta `Payment`; se um pedido `Delivered` tiver pagamento online `Pending`, `Failed` ou `Refunded`, ele continua contando, porque o status de entrega é a fonte aprovada para este painel. Essa é uma limitação conhecida, registrada em Riscos.
- **Ticket médio do dashboard antigo muda.** Hoje ele divide o faturamento por todos os pedidos do período; o novo divide por pedidos entregues. É uma correção de semântica deliberada. O endpoint `/store/report` mantém o comportamento antigo.
- **Quantidade de produto é unidade de item.** Produtos vendidos por peso (`WeightGrams`) contam como 1 item, não como gramas. Variações, escolhas e adicionais não geram linhas separadas: o agrupamento é pelo nome-base do produto no pedido.
- **Nomes são históricos.** Renomear um produto no cardápio não reescreve pedidos antigos: o histórico pode aparecer dividido entre o nome antigo e o novo. Não há normalização de caixa, acentos, espaços internos ou fusão por similaridade. Apenas `Trim()` é aplicado para evitar duplicidade por espaço nas pontas.
- **Nome vazio/nulo (dados legados):** o grupo é mantido e exibido como `Item sem nome`; a quantidade e a receita não são descartadas. Defensivamente, o serviço projeta `ProductName ?? string.Empty` antes do `Trim()`.
- **Ordenação determinística do ranking:** quantidade decrescente, depois faturamento decrescente, depois nome ordinal crescente.
- **Arredondamento:** somas monetárias são exatas (duas casas do banco); apenas o ticket médio é arredondado. Percentuais de pagamento são derivados no cliente a partir das contagens retornadas (não recalculam valores) e exibidos como inteiros.
- **`chartYear`** é o ano da âncora. No filtro `year`, é o ano escolhido; nos demais, é o ano da data/mês selecionado. A série sempre tem 12 posições, mesmo que o ano esteja no futuro ou sem dados (o seletor impede futuro, mas a API não falha).

## 7. Contrato HTTP

### 7.1 Endpoint

```
GET /api/orders/store/analytics
Authorization: Bearer <token de lojista>
Policy: SellerOnly (role "Seller")
```

- `401` sem autenticação; `403` para autenticado sem a role `Seller`.
- `200` com zeros quando o lojista não tem loja ou não tem pedidos; não é `404`.
- `400` (`ValidationProblemDetails`) para parâmetros inválidos.
- Somente leitura, idempotente, sem efeitos colaterais.
- A action recebe `[FromQuery] StoreAnalyticsQueryDto` e declara `ProducesResponseType<StoreAnalyticsResponseDto>(200)` e `(400)`, no padrão dos demais controllers.

### 7.2 Parâmetros de query

| Nome | Tipo | Obrigatório | Regra |
| --- | --- | --- | --- |
| `period` | string | não | `day` (padrão), `week`, `month` ou `year`; comparação exata em minúsculas. |
| `anchor` | string `yyyy-MM-dd` | não | Data local; padrão = data local atual. Formato inválido → `400`. |
| `weekBlock` | int | não | 1..5; usado apenas com `period=week`. Padrão = bloco que contém o dia da âncora. Bloco inexistente no mês → `400`. |

Valores de `weekBlock` enviados com outros períodos são ignorados (não geram erro), para manter URLs tolerantes.

### 7.3 Resposta

```json
{
  "period": "week",
  "anchorDate": "2026-10-29",
  "weekBlock": 5,
  "rangeStartUtc": "2026-10-29T03:00:00Z",
  "rangeEndUtcExclusive": "2026-11-01T03:00:00Z",
  "timeZone": "America/Sao_Paulo",
  "chartYear": 2026,
  "kpis": {
    "deliveredOrders": 12,
    "revenue": 1234.50,
    "deliveryFees": 60.00,
    "averageTicket": 102.88
  },
  "products": [
    { "productName": "X-Burger", "quantity": 30, "revenue": 900.00 },
    { "productName": "Batata grande", "quantity": 18, "revenue": 334.50 }
  ],
  "payments": [
    { "paymentMethod": 1, "deliveredOrders": 7, "revenue": 800.00 },
    { "paymentMethod": 3, "deliveredOrders": 5, "revenue": 434.50 }
  ],
  "months": [
    { "year": 2026, "month": 1, "deliveredOrders": 0, "revenue": 0.00 }
  ],
  "availableYears": [2026, 2025]
}
```

> Exemplo abreviado: `products`, `payments`, `months` e `availableYears` podem ter mais itens. `months` **sempre** contém 12 entradas, de janeiro a dezembro de `chartYear`.

| Campo | Semântica |
| --- | --- |
| `period` | Eco do período resolvido. |
| `anchorDate` | Eco da âncora resolvida (`yyyy-MM-dd`). |
| `weekBlock` | Bloco resolvido (somente em `week`); `null` nos demais. |
| `rangeStartUtc` | Início do intervalo em UTC, **inclusivo**. |
| `rangeEndUtcExclusive` | Fim do intervalo em UTC, **exclusivo**. |
| `timeZone` | `America/Sao_Paulo`. |
| `chartYear` | Ano da série de 12 meses. |
| `kpis.deliveredOrders` | Pedidos entregues no recorte. |
| `kpis.revenue` | Faturamento (inclui taxas). |
| `kpis.deliveryFees` | Soma das taxas de entrega do recorte. |
| `kpis.averageTicket` | `revenue / deliveredOrders`, 2 casas. |
| `products[]` | Todos os grupos do recorte, ordenados; sem paginação. |
| `payments[]` | Todos os métodos presentes no recorte; enum numérico (`1..4`), como no restante da API. |
| `months[]` | Exatamente 12 itens (janeiro a dezembro de `chartYear`), com zeros. |
| `availableYears[]` | Anos selecionáveis, decrescente; inclui o ano corrente. |

Observações de contrato:

- Datas em ISO 8601 UTC com `Z`; `anchorDate` no formato de data civil, sem fuso.
- `decimal` é serializado como número JSON; valores monetários têm no máximo 2 casas.
- `paymentMethod` continua como inteiro (não há `JsonStringEnumConverter` configurado no projeto).
- A resposta **da analytics** não inclui nomes de loja, cliente, endereço ou qualquer PII. A grade de vendas (seção 7.5) é a única que expõe `customerName`, de forma mínima e autorizada.

### 7.4 Erros de validação

`400` com `ValidationProblemDetails` e chaves `period`, `anchor` e/ou `weekBlock`, seguindo o padrão dos controllers existentes. Exemplos:

- `period=quarter` → `period`: "Período inválido."
- `anchor=2026-02-30` → `anchor`: "Data inválida."
- `period=week&weekBlock=5&anchor=2026-02-10` → `weekBlock`: "O mês não possui o bloco 5."

### 7.5 Grade paginada de vendas

```
GET /api/orders/store/sales
Authorization: Bearer <token de lojista>
Policy: SellerOnly (role "Seller")
```

Mesma autorização e o mesmo recorte da analytics: lojas com `OwnerUserId == usuário autenticado`, `Status == Delivered` e `CreatedAtUtc` em `[rangeStartUtc, rangeEndUtcExclusive)`, com os mesmos parâmetros locais (`period`, `anchor`, `weekBlock`). O cliente **não** envia datas UTC nem `storeId`. O nome do cliente só é acessível ao lojista proprietário da loja do pedido; não há exposição pública nem por outro lojista.

| Nome | Tipo | Obrigatório | Regra |
| --- | --- | --- | --- |
| `period` | string | não | `day` (padrão), `week`, `month` ou `year`, resolvido pelo mesmo `AnalyticsRangeResolver`. |
| `anchor` | string `yyyy-MM-dd` | não | Data local; padrão = data local atual. |
| `weekBlock` | int | não | 1..5; apenas em `week`. |
| `page` | int | não | Padrão 1; valores menores que 1 são normalizados para 1. |
| `pageSize` | int | não | Padrão 20; 1..100; valores fora da faixa são normalizados (menor que 1 → 20, maior que 100 → 100), como em `/store`. |

Resposta:

```json
{
  "period": "week",
  "anchorDate": "2026-10-29",
  "weekBlock": 5,
  "rangeStartUtc": "2026-10-29T03:00:00Z",
  "rangeEndUtcExclusive": "2026-11-01T03:00:00Z",
  "timeZone": "America/Sao_Paulo",
  "page": 1,
  "pageSize": 20,
  "totalItems": 57,
  "totalPages": 3,
  "totals": {
    "salesTotal": 12345.67,
    "deliveryFees": 456.78
  },
  "sales": [
    {
      "orderId": "0199f2c4-5a7b-7c3d-8e9f-000000000001",
      "code": "A1B2C3D4",
      "customerName": "Maria Silva",
      "createdAtUtc": "2026-10-29T16:42:00Z",
      "subtotal": 80.00,
      "deliveryFee": 5.00,
      "total": 85.00,
      "items": [
        { "productName": "X-Burger", "variationName": null, "quantity": 2, "unitPrice": 35.00, "totalPrice": 70.00 },
        { "productName": "Batata grande", "variationName": null, "quantity": 1, "unitPrice": 10.00, "totalPrice": 10.00 }
      ]
    }
  ]
}
```

`period`, `anchorDate`, `weekBlock` e `timeZone` têm o mesmo significado da seção 7.3.

| Campo | Semântica |
| --- | --- |
| `rangeStartUtc` / `rangeEndUtcExclusive` | Eco do intervalo autoritativo (inclusivo/exclusivo), igual ao da analytics para o mesmo filtro. |
| `page`, `pageSize`, `totalItems`, `totalPages` | Paginação da grade; `totalItems` conta todos os pedidos `Delivered` filtrados, não apenas a página. |
| `totals.salesTotal` | `Σ(Order.Total)` de **todos** os pedidos filtrados; **inclui** as taxas de entrega. |
| `totals.deliveryFees` | `Σ(Order.DeliveryFee)` de **todos** os pedidos filtrados; é um subconjunto de `salesTotal`. **Nunca somar com `salesTotal`.** |
| `sales[]` | Até `pageSize` pedidos da página, na ordem `CreatedAtUtc DESC, Id DESC`. |
| `sales[].customerName` | Nome do claim `FullName`; pode ser `null` (UI exibe "Cliente"). Única PII da resposta. |
| `sales[].createdAtUtc` | Data de criação em UTC; define a ordenação e pode ser exibida como metadado/tooltip, sem ser coluna obrigatória. |
| `sales[].subtotal` | `Order.Subtotal`; usado na expansão para mostrar `subtotal + taxa = total`. |
| `sales[].deliveryFee` | Taxa da venda; já contida em `total`. |
| `sales[].total` | `Order.Total`, já incluindo a taxa. |
| `sales[].items[]` | Itens **somente da página**; `productName`, `variationName?`, `quantity`, `unitPrice`, `totalPrice`. Sem observações, adicionais detalhados, opções ou PII. |

Regras de contrato:

- **Sem PII além do nome**: a resposta não inclui telefone, e-mail, documento, endereço, observações, PIN, motorista, rota ou ids de usuário. O nome não deve ser gravado em log de aplicação.
- **Lista de andamento**: continua usando `/store`, cujo payload legado inclui telefone/endereço; o dashboard não renderiza esses campos. Migrar essa lista para um contrato mínimo é tarefa separada e está registrada em Riscos.
- **Itens apenas da página**: a consulta de itens usa os `OrderId` da página; nenhum item de outra página é retornado. Não há chamada por linha.
- **Totais sem limite**: `totalItems` e `totals` vêm de uma agregação separada, sem `Skip`/`Take`, sem `pageSize` e sem o limite de 100 de `/store`.
- **Paginação determinística**: `OrderByDescending(CreatedAtUtc).ThenByDescending(Id)`; `Id` é único e serve de desempate. Para um conjunto de dados estável, não há duplicação nem omissão entre páginas.
- **Inserções concorrentes**: como a paginação é por offset, um pedido `Delivered` criado entre duas páginas pode deslocar as linhas seguintes. O próximo refresh (pulso ou "Atualizar") recarrega a página atual; a limitação está registrada em Riscos e na dúvida Q15.
- **Página além do fim**: `page > totalPages` retorna `sales: []`, mantendo `totalItems`, `totalPages` e `totals`; a UI oferece voltar à última página válida.
- **Ordem dos itens**: `ProductName` ascendente e `Id` ascendente, para exibição determinística.
- **Consistência com a analytics**: para o mesmo filtro, `totalItems == kpis.deliveredOrders`, `totals.salesTotal == kpis.revenue` e `totals.deliveryFees == kpis.deliveryFees`.
- **Erros**: `401`/`403` como na analytics; `400` para `period`/`anchor`/`weekBlock` inválidos. `page`/`pageSize` fora da faixa são normalizados, não geram erro.
- A action declara `ProducesResponseType<PagedStoreSalesResponseDto>(200)` e `(400)`, no padrão dos demais controllers.

## 8. Backend

### 8.1 Camadas e componentes

| Camada | Arquivo novo/alterado | Responsabilidade |
| --- | --- | --- |
| Domain | `backend/src/Urbeat.Domain/Services/SaoPauloCalendar.cs` (novo) | Resolver `TimeZoneInfo` (IANA + fallback Windows), obter a data local de um instante UTC, converter início de dia/mês/ano local para UTC e tratar horários inválidos/ambíguos. |
| Domain | `backend/src/Urbeat.Domain/Services/StoreOpeningHoursCalculator.cs` (ajuste interno) | Passa a reutilizar o resolvedor de `SaoPauloCalendar`, sem mudar comportamento. Testes existentes devem continuar passando. |
| Application | `backend/src/Urbeat.Application/Analytics/AnalyticsGranularity.cs` (novo) | Enum `Day`, `Week`, `Month`, `Year`. |
| Application | `backend/src/Urbeat.Application/Analytics/AnalyticsRange.cs` (novo) | `Period`, `AnchorDate`, `WeekBlock?`, `StartUtc`, `EndUtcExclusive`, `ChartYear`. |
| Application | `backend/src/Urbeat.Application/Analytics/AnalyticsRangeResolver.cs` (novo) | Validação e resolução pura do intervalo, sem banco. |
| Application | `backend/src/Urbeat.Application/DTOs/StoreAnalyticsQueryDto.cs` (novo) | Entrada `Period` (string), `Anchor` (string), `WeekBlock` (int?). |
| Application | `backend/src/Urbeat.Application/DTOs/StoreAnalyticsResponseDto.cs` + DTOs aninhados (novos) | Contrato da seção 7.3, com propriedades `RangeStartUtc` e `RangeEndUtcExclusive` para serializar exatamente `rangeStartUtc`/`rangeEndUtcExclusive`. |
| Application | `backend/src/Urbeat.Application/DTOs/StoreSalesQueryDto.cs` (novo) | `Period` (string), `Anchor` (string), `WeekBlock` (int?), `Page` (int), `PageSize` (int). |
| Application | `backend/src/Urbeat.Application/DTOs/PagedStoreSalesResponseDto.cs` + `StoreSaleRowDto`, `StoreSaleItemDto`, `StoreSalesTotalsDto` (novos) | Contrato da seção 7.5. |
| Application | `backend/src/Urbeat.Application/Interfaces/IOrderReportService.cs` (aditivo) | `GetStoreAnalyticsAsync(Guid sellerUserId, AnalyticsRange range, CancellationToken)` e `GetStoreSalesAsync(Guid sellerUserId, AnalyticsRange range, int page, int pageSize, CancellationToken)`. |
| Infrastructure | `backend/src/Urbeat.Infrastructure/Services/OrderReportService.cs` (aditivo) | Implementação das consultas agregadas e da grade. Método existente permanece intacto. |
| WebApi | `backend/src/Urbeat.WebApi/Controllers/OrdersController.cs` (aditivo) | Actions `GET store/analytics` e `GET store/sales`, resolução/validação do range e `Unauthorized` quando não há usuário. Injeta `TimeProvider` para resolver a âncora/ano corrente padrão. |

Não é necessário um novo serviço de DI: `IOrderReportService` já está registrado. `OrderReportService` passa a receber `TimeProvider` além de `ApplicationDbContext` para calcular o ano corrente de forma testável (`TimeProvider.System` já está registrado em DI).

### 8.2 Plano de consultas

Pré-condição comum:

```csharp
var storeIds = await _db.Stores.AsNoTracking()
    .Where(s => s.OwnerUserId == sellerUserId)
    .Select(s => s.Id)
    .ToListAsync(ct);

var deliveredInRange = _db.Orders.AsNoTracking()
    .Where(o => storeIds.Contains(o.StoreId)
        && o.Status == OrderStatus.Delivered
        && o.CreatedAtUtc >= range.StartUtc
        && o.CreatedAtUtc < range.EndUtcExclusive);
```

| Consulta | Forma | Observação |
| --- | --- | --- |
| KPIs | `Count`, `Sum((decimal?)Total)`, `Sum((decimal?)DeliveryFee)` na mesma query | `Sum` anulável evita exceção em conjunto vazio. |
| Pagamentos | `GroupBy(o => o.PaymentMethod)` + `Count` + `Sum(Total)` | Ordenar por faturamento decrescente e depois pelo enum. |
| Produtos | `Join` de `deliveredInRange` com `OrderItems` via `OrderId`, `GroupBy(i => (i.ProductName ?? string.Empty).Trim())`, `Sum(Quantity)`, `Sum(TotalPrice)` | Sem `Take`; retorna todos os grupos. Materializar e ordenar em memória para garantir a ordenação determinística (quantidade, faturamento, nome ordinal). |
| Série mensal | Para cada mês de `chartYear`, `Count` + `Sum(Total)` usando os limites locais calculados | 12 consultas pequenas e cobertas pelo índice; evita SQL específico de PostgreSQL (`AT TIME ZONE`) e mantém o teste com EF InMemory. |
| Anos disponíveis | `Min(CreatedAtUtc)` de todos os pedidos das lojas do lojista (qualquer status); converter para o ano local; gerar `minYear..currentYear` decrescente | Se não houver pedidos, `[currentYear]`. Se `minYear > currentYear`, `[currentYear]`. |

Regras:

- Nenhuma consulta **de agregação** usa `Skip`/`Take`, `pageSize` ou o endpoint de listagem. A única consulta com `Skip`/`Take` é a busca das linhas da página da grade (seção 8.6).
- Não há N+1: as consultas são independentes e agregadas no banco (exceto a ordenação final de produtos em memória).
- Todas usam `AsNoTracking()` e o `CancellationToken` recebido.
- O filtro de autorização é sempre `Store.OwnerUserId == sellerUserId`; nenhum `storeId` vem do cliente.

### 8.3 Fuso, limites e DST

- O banco guarda UTC; o recorte é calculado no calendário local.
- Conversão: `TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified), timeZone)`.
- **Horário local inválido** (início de horário de verão, quando 00:00 não existe): usar o primeiro instante válido do dia. Com a conversão padrão do .NET, o resultado já corresponde ao primeiro instante válido; o helper deve documentar e testar isso.
- **Horário ambíguo** (fim de horário de verão): escolher explicitamente o instante UTC mais cedo, usando `TimeZoneInfo.GetAmbiguousTimeOffsets` e o offset de horário de verão (maior offset); não confiar no default do .NET para horários ambíguos.
- Desde 2019 o Brasil não aplica horário de verão, então o caso prático atual é offset fixo `-03:00`. A regra existe porque o filtro de ano permite visualizar anos anteriores (ex.: 2018), quando o offset era `-02:00` no verão.
- Exemplos de teste:
  - `2018-11-04` local: o dia começa em `2018-11-04T03:00:00Z` (01:00 local, primeiro instante válido) e termina em `2018-11-05T02:00:00Z` — dia de 23 horas.
  - `2019-02-16` local: início `2019-02-16T02:00:00Z` (offset `-02:00`).
  - `2026-10-29` local: início `2026-10-29T03:00:00Z`.
- O helper `saoPauloPeriodRange` do frontend **não** é alterado: continua atendendo `seller-orders` com o comportamento atual. O dashboard não calcula limites UTC.

### 8.4 Validação autoritativa

- `period` ausente → `day`.
- `anchor` ausente → data local atual (`SaoPauloCalendar` + `TimeProvider`).
- `weekBlock` ausente em `week` → `((dia - 1) / 7) + 1`, limitado ao último bloco válido do mês.
- `weekBlock` fora de 1..5, não inteiro ou inexistente no mês → `400`.
- `anchor` não parseável com `DateOnly.TryParseExact("yyyy-MM-dd", CultureInfo.InvariantCulture)` → `400`.
- Não há validação de "data futura"; períodos futuros retornam zeros.
- O cliente nunca é fonte de soma, total ou ticket.

### 8.5 Desempenho

- O índice `(StoreId, Status, CreatedAtUtc)` cobre o filtro principal; `OrderItems.OrderId` cobre a junção.
- Analytics: 1 consulta de lojas + 1 de KPIs + 1 de pagamentos + 1 de produtos + 12 de meses + 1 de anos = 17 idas ao banco. A grade adiciona 1 consulta de lojas + 4 consultas por página (seção 8.6); trocar de página executa somente as 5 da grade. A carga inicial também executa a listagem de pedidos em andamento via `/store`. São consultas pequenas e indexadas; não há cache nesta fase.
- O filtro de ano pode varrer muitos pedidos entregues; a agregação é feita no banco e não trafega pedidos para a aplicação.
- A ordenação de produtos é feita em memória apenas sobre os grupos resultantes (quantidade de produtos distintos), não sobre todos os itens.

### 8.6 Consulta da grade de vendas

A grade usa a mesma base `deliveredInRange` da seção 8.2. Além da consulta de lojas (`storeIds` por `OwnerUserId`), executa quatro consultas por página:

| # | Consulta | Detalhe |
| --- | --- | --- |
| 1 | Agregado total | `Count`, `Sum((decimal?)Total)`, `Sum((decimal?)DeliveryFee)` sobre **todos** os pedidos filtrados, sem `Skip`/`Take`. Origem de `totalItems`, `totals.salesTotal` e `totals.deliveryFees`. |
| 2 | Página | `OrderByDescending(o => o.CreatedAtUtc).ThenByDescending(o => o.Id).Skip((page - 1) * pageSize).Take(pageSize)` com `AsNoTracking`. Retorna `Id, Code, CustomerUserId, Subtotal, DeliveryFee, Total, CreatedAtUtc`. |
| 3 | Itens da página | `Where(i => pageOrderIds.Contains(i.OrderId))`, projeção `OrderId, ProductName, VariationName, Quantity, UnitPrice, TotalPrice`, ordenada por `ProductName, Id`. Nenhum item de pedido fora da página. |
| 4 | Nomes da página | `DbSet<UserClaims>` com `UserId` da página e `ClaimType == "FullName"`; dicionário `UserId → ClaimValue`. Sem telefone/e-mail/endereço. |

Regras de implementação:

- `page` e `pageSize` são normalizados antes de qualquer consulta: `page = page < 1 ? 1 : page`; `pageSize = pageSize < 1 ? 20 : Math.Min(pageSize, 100)`.
- `totalPages = totalItems == 0 ? 0 : ceil(totalItems / pageSize)`; quando `page > totalPages`, a consulta 2 retorna vazia e as consultas 3 e 4 não são executadas; `totalItems`/`totals` continuam sendo devolvidos.
- `sales[].items` é montado agrupando a consulta 3 por `OrderId`, na ordem definida; pedido sem itens recebe `items: []`.
- `customerName` é o claim `FullName` do `CustomerUserId`; ausência do claim resulta em `null`. Não usar telefone/e-mail como fallback.
- A identidade `Σ(items.totalPrice) == sale.subtotal` e `sale.subtotal + sale.deliveryFee == sale.total` deve ser garantida pelos dados persistidos; os testes verificam essa invariante em fixtures de checkout.
- `salesTotal` e `deliveryFees` são somados em `decimal` no banco; nenhuma conversão para `double`.
- Ordenação e filtro usam o índice `(StoreId, Status, CreatedAtUtc)`; os itens usam `OrderItems.OrderId`.
- A consulta 1 não pode ser reaproveitada de `/store` (que limita a 100 e não agrega valores). A consulta de nomes é restrita aos pedidos da página, não a todos os pedidos filtrados.
- A action `GET store/sales` usa o mesmo `AnalyticsRangeResolver` da analytics; não recalcula limites com `DateTime.Now` nem recebe UTC do cliente.

## 9. Frontend

### 9.1 Arquivos e componentes

| Arquivo | Ação |
| --- | --- |
| `frontend/src/app/features/seller-dashboard/seller-dashboard-page.component.ts` | Reescrever o estado e o fluxo de carga; remover resumos/atalhos; manter avisos, listas e assinatura. |
| `frontend/src/app/features/seller-dashboard/seller-dashboard-page.component.html` | Novo layout (KPIs, gráficos, listas); remover "Atalhos rápidos" e "Resumo por serviço". |
| `frontend/src/app/features/seller-dashboard/seller-dashboard-page.component.scss` | Ajustar grid, cards de gráfico, responsividade; remover `.quick-grid`, `.quick-card` e estilos órfãos. |
| `frontend/src/app/features/seller-dashboard/components/dashboard-period-filter.component.{ts,html,scss}` | Novo componente de filtros (segmentado + seletores + blocos), standalone, `input()`/`output()`, sem lógica de negócio. |
| `frontend/src/app/features/seller-dashboard/components/dashboard-sales-grid.component.{ts,html,scss}` | Grade paginada de vendas: linhas, expansão de itens, rodapé de somatórios, paginação e estados. Inputs `result`, `loading`, `error`, `expandedOrders`, `rangeLabel`; outputs `pageChange`, `retry`, `toggleItems`. Não monta parâmetros de período. |
| `frontend/src/app/shared/components/analytics-donut-chart/analytics-donut-chart.component.{ts,html,scss}` | Rosca genérica: recebe fatias `{ label, value }`, total e rótulo acessível. Sem dependências. |
| `frontend/src/app/shared/components/analytics-bar-chart/analytics-bar-chart.component.{ts,html,scss}` | Barras genéricas de 12 colunas: recebe `{ label, value, highlighted }[]`, total e rótulo acessível. |
| `frontend/src/app/shared/models/order.model.ts` | Interfaces aditivas: `StoreAnalyticsQuery`, `StoreAnalytics`, `StoreAnalyticsProduct`, `StoreAnalyticsPayment`, `StoreAnalyticsMonth`, `StoreSalesQuery`, `PagedStoreSales`, `StoreSaleRow`, `StoreSaleItem`, `StoreSalesTotals`. |
| `frontend/src/app/core/services/order.service.ts` | Métodos aditivos `getStoreAnalytics(query)` e `getStoreSales(query)`. |
| `frontend/src/app/core/utils/sao-paulo-date.helper.ts` | Aditivo: `saoPauloTodayIso()` e formatação de data local a partir de `yyyy-MM-dd`, sem alterar `saoPauloPeriodRange`. |

Sem nova dependência, sem alteração de `angular.json` e sem CommonJS.

### 9.2 Modelo de estado (signals)

```ts
period = signal<'day' | 'week' | 'month' | 'year'>('day');
anchorDate = signal<string>(saoPauloTodayIso()); // yyyy-MM-dd, não recalcula a cada request
weekBlock = signal<number>(derivedBlock(anchorDate()));
analytics = signal<StoreAnalytics | null>(null);
sales = signal<PagedStoreSales | null>(null);
salesPage = signal(1);
salesLoading = signal(false);
salesError = signal(false);
expandedOrders = signal<ReadonlySet<string>>(new Set());
inProgressOrders = signal<OrderSummary[]>([]);
ordersTotal = signal<number | null>(null);
loading = signal(true);        // primeira carga / troca de filtro
refreshing = signal(false);    // atualização silenciosa
error = signal(false);         // falha sem dados
ordersError = signal(false);   // falha só da lista de pedidos em andamento
staleWarning = signal(false);  // falha em refresh com dados antigos
lastUpdatedAt = signal<Date | null>(null);
private filterSequence = 0;
private salesSequence = 0;
```

Derivados:

- `weekBlockOptions` — blocos válidos do mês da âncora, com rótulos `1–7`, `8–14`, `15–21`, `22–28`, `29–último`.
- `daysInMonth`, `isBlockFiveAvailable`.
- `rangeLabel` — seção 5.4.
- `paymentRows` — percentual por quantidade + faturamento.
- `donutSlices` — top 5 por quantidade + `Outros` quando houver mais de 5 grupos; `totalUnits`.
- `monthlyMaxRevenue`, `yearTotal`, `bestMonth`.
- `yearOptions` — `analytics()?.availableYears ?? [currentYear]`, incluindo o ano selecionado se ausente.
- `salesTotals`, `salesTotalPages`, `salesHasPrevious`, `salesHasNext`, `salesIsEmpty` — derivados de `sales()`.
- `salesRows` — `sales()?.sales ?? []`; a expansão usa `expandedOrders`.

### 9.3 Fluxo de carga

1. `load({ silent, resetPage })` monta `{ period, anchor, weekBlock }` a partir dos signals. A âncora é fixada quando o filtro muda; não é recalculada a cada requisição (evita divergência na virada do dia).
   - primeira carga e troca de filtro: `resetPage = true`;
   - botão "Atualizar" e pulso de atividade: `resetPage = false` (mantém a página atual).
2. Chama `getStoreAnalytics` com `filterSequence`; descarta resposta obsoleta.
3. Com sucesso:
   - `analytics.set(response)` e `lastUpdatedAt.set(...)`;
   - carrega a grade via `loadSales({ page: resetPage ? 1 : currentPageIfStillValid, resetExpansion: resetPage })`;
   - carrega "Pedidos em andamento" via `getStoreOrders` com `startDateUtc = rangeStartUtc` e `endDateUtc = rangeEndUtcExclusive - 1ms`, `pageSize: 100`; `totalItems` alimenta `shell.ordersCount` (semântica preservada) e `items` alimenta a lista de andamento;
   - ao final, `loading=false`, `refreshing=false`.
4. Falha na analytics:
   - primeira carga (sem dados) → `error=true`, `loading=false`, sem grade;
   - refresh com dados existentes → mantém `analytics`, `staleWarning=true`, `refreshing=false`, `loading=false`.
5. Falha na grade → `salesError=true`, mantém KPIs/gráficos e a última página válida; se não houver página anterior, mostra erro no card da grade com retry.
6. Falha na lista de pedidos em andamento → `ordersError=true`, mantém o restante; o selo não é zerado.
7. `loadSales({ page, resetExpansion })`:
   - monta `{ period, anchor, weekBlock, page, pageSize: 20 }` e chama `getStoreSales` com `salesSequence`;
   - ao sucesso, `sales.set(response)`, `salesPage.set(response.page)` e `expandedOrders` é podado para os `orderId` ainda presentes na página;
   - se `response.totalPages > 0 && response.page > response.totalPages`, refaz **uma única vez** a requisição para a última página válida;
   - `resetExpansion` limpa `expandedOrders`; troca de página sempre limpa a expansão;
   - descarta respostas cujo `salesSequence` não é o atual (troca rápida de página/filtro).
8. Trocar de página chama **somente** `loadSales`; KPIs, gráficos e pedidos em andamento não são recarregados.
9. Trocar filtro: incrementa `filterSequence` e `salesSequence`, `salesPage=1`, limpa `expandedOrders`, limpa `salesError`, limpa `sales` (a grade mostra skeleton, nunca linhas do recorte anterior) e recarrega analytics + grade + andamento.
10. Pulso de atividade (`shell.orderActivityPulse`) e botão "Atualizar" disparam `load({ silent: true, resetPage: false })`, mantendo a página atual; se o novo `totalPages` for menor, cai para a última página válida.
11. Com dados já carregados, `loading` apenas desabilita controles e sinaliza atualização (`aria-busy`); não substitui o conteúdo por skeleton. O skeleton da grade aparece na primeira carga e na troca de página (`salesLoading`), mantendo o rodapé de totais da página anterior quando o filtro não mudou.

Observação de compatibilidade: o endpoint de pedidos em andamento usa fim inclusivo. O dashboard subtrai 1ms do fim exclusivo retornado pela analytics apenas nessa chamada, documentado no código. A grade usa `period/anchor/weekBlock` diretamente e não envia UTC; a analytics e a grade resolvem o mesmo intervalo no backend.

### 9.4 Layout

Ordem no desktop (grid de 12 colunas):

1. Topbar: título, subtítulo, filtros, botão "Atualizar", sino de notificações.
2. Aviso de mensalidade e aviso de impressora (inalterados).
3. Linha de KPIs (3 cards, `app-metric-card` ou o card atual do dashboard):
   - **Pedidos entregues** — "Pela data de criação do pedido".
   - **Faturamento** — "Inclui taxas de entrega" ou, quando `deliveryFees > 0`, "R$ X em taxas de entrega".
   - **Ticket médio** — "Faturamento ÷ pedidos entregues".
4. Linha de gráficos:
   - **Produtos mais pedidos** (rosca + legenda/ranking completo).
   - **Faturamento por mês** (12 barras do `chartYear`).
5. Linha: **Formas de pagamento** (barras horizontais) ao lado de **Pedidos em andamento**.
6. **Vendas do período** (grade paginada, largura total): colunas **Pedido** (código), **Cliente**, **Itens** (botão que expande), **Total da venda** e **Taxa de entrega**; rodapé com somatórios de todos os registros filtrados e paginação de 20 linhas.

Em telas ≤1199px, os gráficos empilham em uma coluna; ≤767px tudo em coluna única, com a mesma ordem.

Remoções concretas:

- Bloco e estilos de "Atalhos rápidos".
- Bloco e estilos de "Resumo por serviço".
- Bloco "Últimos pedidos" (substituído pela grade).
- Computeds `deliverySummary`, `pickupSummary` e `recentOrders`.
- Import de `FulfillmentType` se ficar sem uso.

### 9.5 Renovação do conteúdo dos cards

- O subtítulo da página passa de "Visão geral da loja hoje" para uma descrição neutra ao período, por exemplo: "Pedidos entregues e faturamento da loja".
- Cada card de gráfico mostra o recorte atual (`rangeLabel`) no cabeçalho ou como texto de apoio.
- O ranking exibe uma nota curta: "Nomes conforme registrados no pedido." (transparência do snapshot).
- O card de produtos usa o total de unidades no centro da rosca com o rótulo "unidades".
- O card de meses mostra `Total do ano` e `Melhor mês` como texto de apoio; os valores por mês ficam em um `<details>` "Ver dados do ano" com tabela real.
- O card de pagamento mostra forma, quantidade, percentual e faturamento; percentual arredondado para inteiro e soma exibida como 100% (o resíduo de arredondamento fica no maior grupo, sem alterar dados).

Grade "Vendas do período":

- Título e subtítulo: "Vendas do período" / "Pedidos entregues, pela data de criação."; o `rangeLabel` aparece no cabeçalho.
- Colunas: **Pedido** (`code`), **Cliente** (`customerName` ou "Cliente"), **Itens** (botão "Ver itens" ou "N itens"), **Total da venda** (`total`) e **Taxa de entrega** (`deliveryFee`).
- O botão de itens usa `aria-expanded` e `aria-controls`, com rótulo "Ver itens do pedido {code}"; ao expandir, a linha de detalhe ocupa a largura da tabela e mostra cada item (`quantidade × unitário = total do item`), o subtotal dos itens, a taxa e a igualdade `subtotal + taxa = total`.
- Adicionais/opções já estão no preço do item e não são detalhados aqui; a composição completa fica em `/app/pedidos`. O texto de apoio da expansão diz isso quando houver `AdditionalNames`/opções.
- O rodapé usa `<tfoot>` com "Total das vendas (inclui taxas de entrega)" = `totals.salesTotal` e "Taxas de entrega (já incluídas no total)" = `totals.deliveryFees`, além da nota "As taxas já estão dentro do total; não somar as duas colunas."
- A paginação mostra "Página {page} de {totalPages}" e "{totalItems} vendas entregues", com botões "Anterior"/"Próxima"; a página 1 desabilita "Anterior" e a última desabilita "Próxima".
- O rodapé é sempre o total do **filtro**, não da página; trocar de página não altera os valores. O texto "Total do período" aparece junto aos somatórios para evitar leitura de "total da página".

### 9.6 Acessibilidade

- Controle de período: `role="group"` + `aria-label="Período do dashboard"`, botões com `aria-pressed`, `min-height: 44px`, foco visível com token de marca.
- Blocos de semana: `role="group"` + `aria-label="Semana do mês"`, `aria-pressed`, alvos ≥44px; rótulo acessível completo ("Dias 15 a 21 de outubro de 2026").
- Seletores com `<label for>` visível; não usar placeholder como rótulo.
- Rosca: `role="img"` + `aria-label` com total de unidades e nome do período; legenda é uma `<ul>` real com nome, quantidade e percentual (não depender de cor).
- Barras: `role="img"` + `aria-label` com total e melhor mês; `<details>` com tabela `<caption>` e cabeçalhos `scope="col"`; sem informação apenas em tooltip/hover.
- Lista de produtos: `<table>` com `<caption class="visually-hidden">`; região rolável com `tabindex="0"`, `role="region"` e nome acessível quando houver rolagem.
- Grade de vendas: `<table>` com `<caption>` ("Vendas entregues do período"), `<th scope="col">` nas cinco colunas e `<th scope="row">` no código do pedido; `<tfoot>` com `scope="rowgroup"` ou célula de resumo associada aos rótulos.
- Divulgação dos itens: botão nativo com `aria-expanded` e `aria-controls` apontando para o id da linha/região de detalhe; a região usa `hidden` quando recolhida, mantendo o foco no botão. Alvo ≥44px e foco visível.
- Paginação: `<nav aria-label="Paginação das vendas">`, botões com rótulos "Página anterior" e "Próxima página", `disabled` nos limites; a mudança de página é anunciada em `aria-live="polite"` ("Página 2 de 3, 57 vendas").
- Rodapé: a nota de não somar total e taxa é texto real (não tooltip), com contraste AA; o `aria-label` do valor de total inclui "inclui taxas de entrega".
- Estados: `role="status"`/`aria-live="polite"` para carregamento e atualização; `role="alert"` para erro; skeleton com `aria-hidden="true"`.
- Contraste: textos auxiliares usam `--app-text-secondary` ou superior; nunca `#8c8c91` para corpo.
- Movimento: transições de barras/rosca desativadas em `prefers-reduced-motion: reduce`; sem animação contínua.
- Cores dos gráficos: rampa monocromática de bordeaux (`--app-brand`, `--app-brand-dark`, misturas com `--app-brown`/`--app-muted-warm`) e um tom neutro para "Outros". Verde continua reservado a status; não usar verde/azul/roxo como categoria.

### 9.7 Mobile, PWA e Capacitor

- Layout de uma coluna ≤767px; sem rolagem horizontal da página.
- Filtros quebram em duas linhas; segmentado com rolagem horizontal interna se necessário, mantendo 44px.
- Rosca entre 180px e 220px no mobile, legenda abaixo.
- 12 barras cabem na largura: rótulos de mês com 3 letras; abaixo de 480px, exibir rótulos alternados, mantendo todos no `<details>` e nos `aria-label`.
- `padding-bottom` com `env(safe-area-inset-bottom)` no container do dashboard.
- Nenhuma informação depende de hover; toque/foco revelam os mesmos dados.
- `input type="date"` e `<select>` nativos preservam o comportamento de teclado e picker no Capacitor.
- Grade ≤767px: cada venda vira um card com código, cliente, total e taxa em linhas rotuladas e o botão "Ver itens" (44px). A expansão mostra os itens em lista, com `quantidade × unitário` e total; nada de tabela comprimida ou rolagem horizontal da página.
- Se a tabela for mantida em larguras intermediárias, ela fica dentro de uma região com rolagem horizontal focável (`tabindex="0"`); os cabeçalhos permanecem visíveis.
- Rodapé de totais no mobile: dois blocos empilhados com os mesmos rótulos e a nota; a paginação usa botões de largura total ou metade, sempre ≥44px.
- Nomes longos de cliente usam `text-overflow: ellipsis` com `title`/`aria-label` contendo o nome completo; o layout não quebra a linha de valores.

### 9.8 Estados de erro, vazio e carregamento

| Estado | Comportamento |
| --- | --- |
| Carregando (primeira vez) | Skeleton de 3 KPIs + 2 cards de gráfico; `aria-busy="true"`; textos "Carregando indicadores". |
| Atualizando | Mantém os dados; botão "Atualizar" desabilitado; `aria-live` anuncia "Atualizando". |
| Erro sem dados | Card de erro "Não foi possível carregar o dashboard" + "Tentar novamente". |
| Erro em refresh | Banner inline "Não foi possível atualizar os números. Exibindo os últimos dados carregados." + "Tentar novamente"; dados antigos permanecem. |
| Erro na lista de andamento | Banner inline no card "Pedidos em andamento": "Não foi possível carregar os pedidos." + "Tentar novamente"; KPIs/gráficos/grade permanecem. |
| Sem pedidos entregues no período | KPIs zerados; rosca com "Nenhum pedido entregue neste período."; ranking vazio; pagamentos vazio; meses continuam com 12 barras zeradas e o texto "Sem faturamento em {ano}."; grade com "Nenhuma venda entregue neste período." e totais `R$ 0,00` rotulados. |
| Grade carregando (1ª vez ou troca de filtro) | Skeleton de 5 linhas; rodapé oculto até a resposta; `aria-busy="true"`; botões de página desabilitados. |
| Grade carregando (troca de página) | Corpo com linha de carregamento/skeleton; rodapé e totais anteriores permanecem (são do período, não da página); `aria-live` anuncia "Carregando página {n}". |
| Grade vazia | "Nenhuma venda entregue neste período."; rodapé visível com `R$ 0,00` e os rótulos; paginação oculta. |
| Grade com erro | Banner inline no card "Não foi possível carregar as vendas." + "Tentar novamente"; se já havia página válida, mantê-la e marcar como desatualizada. |
| Página além do fim | "Esta página não existe mais." + "Ir para a última página"; dispara uma única requisição para `totalPages` (ou página 1 se `totalPages = 0`). |
| Expansão de itens | Estado por `orderId`; expandir/recolher não refaz requisição; a expansão é limpa ao trocar de página ou filtro. |
| Sem pedidos no período (listas) | "Nenhum pedido no período." e "Nenhum pedido em andamento." |
| Ano sem dados | Mesmo estado vazio; a seleção continua válida. |
| Bloco 5 inexistente | Não é oferecido no seletor; não há requisição inválida. |

Copy em pt-BR, sentence case, sem "Urbeat" maiúsculo.

## 10. Compatibilidade e impactos

| Contrato/arquivo | Impacto |
| --- | --- |
| `GET /api/orders/store/report` | **Não alterado.** Continua disponível e testado; deixa de ser chamado pelo dashboard. |
| `GET /api/orders/store` | **Não alterado.** Continua atendendo "Pedidos em andamento" e o selo, com o intervalo ajustado em 1ms no cliente. |
| `GET /api/orders/store/sales` | **Novo e aditivo.** Contrato da seção 7.5; não substitui nem altera `/store`. |
| `GET /api/orders/store/customers` | Não alterado. |
| `OrderItem` / schema | Não alterado; nenhuma migração. |
| `saoPauloPeriodRange` | Não alterado (usado por `seller-orders`). Novas funções são aditivas. |
| `SellerShellFacade.ordersCount` | Mantido; alimentado por `totalItems` de `/store` (todos os pedidos do período, exceto `PendingPayment`), não pelo total `Delivered` da grade. |
| `IOrderReportService` | Dois métodos novos; implementação existente intacta. |
| `OrderReportService` construtor | Recebe `TimeProvider`; atualizar o teste unitário existente. |
| `StoreOpeningHoursCalculator` | Refatoração interna para reutilizar o resolvedor de fuso; comportamento preservado e coberto por testes existentes. |
| `OrderSummaryResponseDto` | **Não alterado**; a grade usa DTO próprio e não reaproveita telefone/endereço/itens sem preço. |
| Postman | Endpoints novos (`/analytics` e `/sales`) deverão ser adicionados pelo agente `postman-sync` em outra tarefa. |
| `package.json` / `angular.json` | Sem alteração. |

Impacto em testes existentes:

- `frontend/src/app/features/seller-dashboard/seller-dashboard-page.component.spec.ts`: os casos de períodos (`today`/`week`), o "date pill" e o rótulo "Pedidos hoje" precisam ser reescritos; os casos de descarte de resposta obsoleta continuam válidos com os novos serviços mockados.
- `frontend/src/app/features/seller-dashboard/components/dashboard-sales-grid.component.spec.ts` (novo): paginação, expansão, rodapé e estados.
- `frontend/src/app/core/services/order.service.spec.ts`: adicionar os casos de codificação de `getStoreAnalytics` e `getStoreSales`.
- `frontend/src/app/core/utils/sao-paulo-date.helper.spec.ts`: adicionar casos das novas funções, sem alterar os existentes.
- `backend/tests/Urbeat.UnitTests/Infrastructure/Services/OrderReportServiceTests.cs`: ajustar o construtor e adicionar os casos da seção 11 (analytics e vendas).
- Nenhum teste atual de `/store/report` ou `/store` deve ser removido.

## 11. Testes

### 11.1 Backend — unitários

`AnalyticsRangeResolver`:

1. `day` com âncora `2026-10-29` → `[2026-10-29T03:00Z, 2026-10-30T03:00Z)`.
2. `week` blocos 1 a 4 em mês de 31 dias → limites 1/8, 8/15, 15/22, 22/29.
3. `week` bloco 5 em outubro/2026 → `[29/10 00:00, 01/11 00:00)`.
4. `week` bloco 5 em fevereiro/2026 (28 dias) → inválido.
5. `week` bloco 5 em fevereiro/2028 (bissexto) → `[29/02, 01/03)`.
6. `month` dezembro/2026 → `[01/12/2026, 01/01/2027)`.
7. `year` 2026 → `[01/01/2026, 01/01/2027)`.
8. DST 2018: dia `2018-11-04` → início `03:00Z`, fim `2018-11-05T02:00Z`.
9. DST 2019: dia `2019-02-16` → início `02:00Z`.
10. `weekBlock` omitido → bloco derivado do dia da âncora, inclusive dia 29/31.
11. `anchor` inválido, `period` inválido, `weekBlock` fora de 1..5 → erros por campo.

`OrderReportService.GetStoreAnalyticsAsync` (EF InMemory):

1. Só agrega lojas do `OwnerUserId`; loja de outro lojista não entra.
2. Só pedidos `Delivered`: `PendingPayment`, `Cancelled`, `Received`, `Preparing`, `Ready` e `OnDelivery` não entram.
3. Limite inclusivo/exclusivo: pedido exatamente em `StartUtc` entra; pedido em `EndUtcExclusive` não entra.
4. `revenue` inclui `DeliveryFee`; `deliveryFees` é a soma separada.
5. `averageTicket = revenue / deliveredOrders` e `0` sem pedidos.
6. Pagamentos: contagem e faturamento por `PaymentMethod`; métodos ausentes não aparecem.
7. Produtos: mesmo nome em pedidos diferentes soma quantidade; `Trim()` une " X-Burger " e "X-Burger"; nomes com caixa diferente permanecem separados; nome vazio/nulo gera o grupo exibível "Item sem nome" sem perder quantidade; produtos de pedidos não entregues não entram.
8. Produtos: ordenação por quantidade desc, faturamento desc, nome asc.
9. Série mensal: 12 itens, zero-fill; pedido `2026-12-31T23:30:00-03:00` (`2027-01-01T02:30Z`) cai em dezembro/2026, não em janeiro/2027; um pedido `2026-01-01T02:30Z` (31/12/2025 23:30 local) não entra na série de 2026.
10. Sem paginação: mais de 100 pedidos entregues são todos contados, somados e ranqueados.
11. `availableYears` inclui o ano corrente, é decrescente e cobre o ano local do pedido mais antigo.
12. Loja inexistente/lojista sem loja → resposta zerada, sem exceção.
13. Datas e somas nunca usam `DateTime.Now`; `TimeProvider` controla o ano corrente.

`OrderReportService.GetStoreSalesAsync` (EF InMemory):

1. Totais de `salesTotal` e `deliveryFees` são de **todos** os pedidos filtrados, não da página: com 3 páginas, os totais são idênticos em qualquer `page`.
2. `totalItems` é o total filtrado; `totalPages = ceil(totalItems / pageSize)`; página além do fim retorna `sales: []` mantendo totais.
3. Paginação estável: com vários pedidos com o mesmo `CreatedAtUtc`, a ordem `CreatedAtUtc DESC, Id DESC` não duplica nem omite pedidos entre a página 1 e a 2.
4. Itens somente da página: a página 2 não contém item de pedido da página 1 nem de pedido de outro lojista.
5. Cada venda: `Σ(items.totalPrice) == sale.subtotal` e `sale.subtotal + sale.deliveryFee == sale.total` (inclui frete grátis, taxa zero).
6. PII: `customerName` é o claim `FullName`; quando ausente, `null`; telefone/e-mail/endereço/observações não existem no DTO.
7. Somente `Delivered`; `PendingPayment`, `Cancelled` e em andamento não entram; pedido em `StartUtc` entra e em `EndUtcExclusive` não entra.
8. Isolamento: loja de outro `OwnerUserId` não entra em linhas, itens nem totais.
9. Normalização: `page < 1 → 1`; `pageSize < 1 → 20`; `pageSize > 100 → 100`.
10. Consistência com a analytics para o mesmo range: `totalItems == deliveredOrders`, `salesTotal == revenue`, `deliveryFees == deliveryFees`.
11. Loja/lojista sem pedidos → `sales: []`, `totalItems = 0`, `totalPages = 0`, totais zero.
12. Itens ordenados por `ProductName, Id`; pedido sem itens retorna `items: []`.

### 11.2 Backend — integração

`backend/tests/Urbeat.IntegrationTests/Api/StoreAnalyticsFlowTests.cs` (novo, `TestWebApplicationFactory`):

1. `GET /api/orders/store/analytics` sem token → `401`.
2. Token de cliente → `403`.
3. Token de lojista → `200` e contrato desserializável em `StoreAnalyticsResponseDto`.
4. Parâmetros inválidos → `400` com `ValidationProblemDetails` e chaves corretas.
5. Loja de outro lojista não aparece nos números (dois lojistas, pedidos entregues em cada um).
6. Pedido entregue criado no intervalo entra; pedido fora do intervalo e pedido cancelado não entram.
7. `rangeStartUtc`/`rangeEndUtcExclusive` coerentes com o período solicitado.

`backend/tests/Urbeat.IntegrationTests/Api/StoreSalesFlowTests.cs` (novo, `TestWebApplicationFactory`):

1. `GET /api/orders/store/sales` sem token → `401`; token de cliente → `403`.
2. Token de lojista → `200` com `PagedStoreSalesResponseDto`, `sales`, `totals` e paginação.
3. PII mínima: o JSON não contém telefone, e-mail, endereço, observações, PIN, motorista e rota; `customerName` é o único dado pessoal.
4. `page=2` retorna somente os pedidos e itens da página 2.
5. Totais e `totalItems` são iguais em `page=1` e `page=2` para o mesmo filtro.
6. Consistência com `/analytics` para o mesmo filtro (contagem e somas).
7. Isolamento entre lojistas.
8. Parâmetros inválidos de período → `400`; `page`/`pageSize` inválidos são normalizados.
9. Mais de 100 pedidos entregues: `totalItems` e totais incluem todos; a página continua limitada a 20.

### 11.3 Frontend — Jest

`dashboard-period-filter` e página:

1. Estado inicial: `period=day`, âncora = data local atual, uma chamada com `{ period: 'day', anchor: <hoje>, weekBlock: undefined }`.
2. Trocar para `week`: envia o bloco que contém a âncora; opções de bloco 1..4/5 conforme o mês; fevereiro não bissexto tem 4 opções.
3. Trocar para `month`/`year`: envia a âncora correspondente e o ano selecionado.
4. Trocar o mês mantém o bloco válido ou o último válido.
5. Seletores de dia/mês/ano não oferecem datas futuras (`max`/opções); blocos do mês atual continuam disponíveis.
6. Resposta fora de ordem é descartada (manter o padrão de `requestSequence`).
7. KPIs renderizam com `Intl.NumberFormat('pt-BR')`; zero aparece como `R$ 0,00` e ticket `R$ 0,00`.
8. Roscas: top 5 + "Outros" quando há mais de 5 grupos; legenda com quantidade e percentual; centro com total de unidades.
9. Barras: 12 colunas; mês destacado quando o período não é `year`; `details` com tabela de 12 linhas.
10. Pagamentos: percentuais somam 100% e valores por forma aparecem.
11. Estado vazio: textos definidos na seção 9.8 e nenhum `NaN`/`undefined`.
12. Erro sem dados: card de erro e retry; erro em refresh mantém dados e mostra banner.
13. Erro na lista de pedidos em andamento mantém gráficos, KPIs e grade.
14. Remoção: a tela não contém "Atalhos rápidos" nem "Resumo por serviço"; "Últimos pedidos" não existe mais.
15. `order.service.getStoreAnalytics` codifica `period`, `anchor` e `weekBlock` corretamente e omite `weekBlock` fora de `week`.
16. `saoPauloTodayIso` respeita a virada de dia em `America/Sao_Paulo` (teste com `jest.useFakeTimers`).

Grade de vendas (`dashboard-sales-grid` e página):

17. Estado inicial: uma chamada `getStoreSales` com página 1, `pageSize: 20`, `period`/`anchor`/`weekBlock` corretos.
18. "Próxima"/"Anterior" chamam `getStoreSales` somente com a nova `page`; a analytics não é recarregada.
19. Rodapé: `salesTotal` e `deliveryFees` vêm da resposta e permanecem iguais ao trocar de página; os rótulos contêm "inclui taxas" e "já incluídas"; não existe linha que some os dois.
20. Linha: código, cliente, total e taxa; total e taxa formatados em BRL; cliente nulo vira "Cliente".
21. Itens: botão "Ver itens" abre a lista com `quantidade × unitário = total`, subtotal, taxa e total; `aria-expanded` alterna e o botão tem 44px; recolher remove a região do DOM/`hidden`.
22. Expansão é limpa ao trocar de página e de filtro; expandir um pedido não dispara nova requisição.
23. Grade vazia: "Nenhuma venda entregue neste período.", rodapé `R$ 0,00` e paginação oculta.
24. Erro da grade: banner/retry sem derrubar KPIs e gráficos; erro de página mantém a última página válida.
25. Página além do fim: exibe "Esta página não existe mais." e oferece a última página; dispara no máximo uma nova requisição.
26. Trocar filtro volta para a página 1 e limpa a expansão.
27. `order.service.getStoreSales` codifica `period`, `anchor`, `weekBlock`, `page`, `pageSize` e omite `weekBlock` fora de `week`.
28. Layout mobile: linhas viram cards com rótulos, sem rolagem horizontal da página, e o botão de itens mantém 44px.

## 12. Critérios de aceite

1. O dashboard exibe apenas os blocos previstos: avisos, 3 KPIs, rosca de produtos, barras de 12 meses, formas de pagamento, pedidos em andamento e a grade paginada "Vendas do período"; "Atalhos rápidos", "Resumo por serviço" e "Últimos pedidos" não existem no HTML, SCSS ou TS.
2. Os filtros Dia/Semana/Mês/Ano funcionam; o padrão é o período atual; o bloco de semana segue 1–7, 8–14, 15–21, 22–28 e 29–fim, com 4 blocos em fevereiro não bissexto.
3. O seletor de dia/mês/ano é contextual, acessível, com alvos ≥44px e sem datas futuras em dia/mês/ano.
4. Os três KPIs usam exclusivamente pedidos `Delivered` no recorte, com faturamento incluindo taxas e ticket dividido por pedidos entregues.
5. A rosca representa a quantidade de itens dos pedidos entregues no período, agrupada por nome snapshot; renomear produto divide o histórico e a UI declara isso.
6. As barras mostram os 12 meses do ano escolhido, com meses sem dados zerados e valores acessíveis.
7. A distribuição de pagamento cobre todos os pedidos entregues do período, com quantidade, percentual e faturamento.
8. Nenhuma agregação é limitada por paginação ou pelo `pageSize` de 100, comprovado por teste com mais de 100 pedidos.
9. O endpoint novo é `SellerOnly`, agrega apenas as lojas do proprietário autenticado e retorna `200` com zeros quando não há dados.
10. Os limites são calculados em `America/Sao_Paulo` e convertidos para UTC com `TimeZoneInfo`; o início é inclusivo e o fim exclusivo; os casos de DST históricos passam nos testes.
11. Estados de carregamento, vazio, erro inicial, erro em refresh e erro parcial de listagem estão implementados e testados.
12. O layout é utilizável em 360px, sem rolagem horizontal, com gráficos legíveis, foco visível e informação não dependente de cor/hover.
13. `GET /api/orders/store/report`, `GET /api/orders/store`, `saoPauloPeriodRange` e o schema de `OrderItem` permanecem inalterados.
14. `dotnet build backend/Urbeat.sln`, os testes focados e `npx ng build --configuration production` passam; nenhuma migração pendente é gerada.
15. A grade mostra código, cliente, itens expansíveis com preços, total e taxa, com no máximo 20 linhas por página; os itens retornados são somente da página e não há requisição por linha.
16. O rodapé soma `Total` e `DeliveryFee` de todos os registros filtrados, independentemente da página; como `Total` já inclui a taxa, os valores são apresentados com rótulos explícitos e nunca somados entre si.
17. A grade é `SellerOnly`, restrita às lojas do proprietário, usa a mesma regra `Delivered`, o mesmo período e o mesmo fuso da analytics; `totalItems`, `salesTotal` e `deliveryFees` coincidem com os KPIs correspondentes.
18. A resposta da grade expõe apenas `customerName` como PII, sem telefone, e-mail, documento, endereço, observações, PIN, motorista ou rota.
19. Paginação determinística (`CreatedAtUtc DESC, Id DESC`), com `page`/`pageSize` normalizados, página além do fim tratada e ausência de duplicação/omissão para um conjunto estável.
20. A divulgação de itens é acessível (botão `aria-expanded`/`aria-controls`, alvo ≥44px), responsiva (cards no mobile, sem rolagem horizontal da página) e cobre loading, vazio, erro e página além do fim.

## 13. Riscos e mitigações

| Risco | Impacto | Mitigação |
| --- | --- | --- |
| Analytics (17) + grade (5/página) por carga | Latência em bancos lentos | Consultas pequenas e indexadas; troca de página executa só as 5 da grade; medir em homologação; se necessário, consolidar a série mensal com Dapper/`AT TIME ZONE` em uma tarefa futura. |
| Ano inteiro com muitos pedidos | CPU/tempo na agregação de produtos | Agregação no banco; índice existente; monitorar; não trafegar pedidos. |
| Ranking dividido por renomeação | Percepção de inconsistência | Nota de transparência no card; nenhuma fusão automática; `ProductId` não existe no snapshot. |
| `Delivered` sem pagamento conciliado | Faturamento acima do caixa real | Documentado; `Payment` não cobre dinheiro/cartão na entrega; reconciliar pagamentos é outro escopo. |
| Pedidos em andamento fora dos 100 mais recentes | Lista incompleta em períodos longos | Comportamento já existente; registrar como limitação; não corrigir via paginação nesta tarefa. |
| `endDateUtc` inclusivo no endpoint de listagem | Inclusão teórica de um pedido no limite | Subtrair 1ms no cliente e documentar; a analytics continua autoritativa. |
| Fuso histórico/DST | Números errados em anos antigos | `TimeZoneInfo` + testes de 2018/2019; nunca offset fixo. |
| `availableYears` com muitos anos | Select muito longo | Lista decrescente, faixa contígua de `minYear` ao ano corrente; se o histórico for muito antigo, avaliar anos distintos em tarefa futura. |
| iOS/Capacitor com `input type="month"` | Seletor quebrado | Usar `<select>` de mês/ano; `input type="date"` apenas para o dia. |
| Rampa monocromática com muitas fatias | Dificuldade de distinção | Legenda com rótulos/percentuais; top 5 + "Outros"; nunca cor como único canal. |
| Refresh concorrente | Dados de período antigo sobrescrevendo os atuais | `requestSequence`/`salesSequence`; descarte de respostas obsoletas. |
| Dupla contagem de taxa | Rodapé somar `Total` + `DeliveryFee` | `Total` já inclui a taxa; rótulos explícitos e nota "não somar"; nenhuma linha de total geral; teste de contrato e de UI. |
| Deslocamento de offset por inserções | Pedido novo entre páginas repete/omite linha | Ordenação determinística + refresh; registrar limitação; se necessário, evoluir para cursor/snapshot (Q15). |
| PII na grade | Exposição de telefone/endereço | Contrato próprio somente com `customerName`; teste que rejeita campos extras; não reutilizar `OrderSummaryResponseDto`. |
| PII no payload de andamento | `/store` continua devolvendo telefone/endereço para a lista de andamento | Fora do escopo da grade; não renderizar os campos e registrar a migração futura para contrato mínimo. |
| Payload de itens | Resposta grande com muitos itens por pedido | Itens somente da página, `pageSize` máximo 100; itens sem observações/adicionais detalhados. |
| Custo das 4 consultas da grade | Latência ao paginar | Índices existentes; agregação e página separadas; medir em homologação. |
| Loop de página além do fim | Requisições repetidas | Refazer no máximo uma vez para a última página válida. |
| Copy de marca | Teste de branding | Usar `urbeat` minúsculo em qualquer texto novo. |

## 14. Dúvidas registradas (premissas adotadas)

| ID | Dúvida | Premissa adotada nesta especificação | Como confirmar |
| --- | --- | --- | --- |
| Q1 | "Ranking histórico" é o ranking de produtos **dentro do período filtrado** ou um ranking **de todo o histórico**, independente do filtro? | Ranking e rosca usam o mesmo recorte do período. Se for all-time, será necessário um segundo conjunto de dados e um rótulo distinto. | Confirmar com o solicitante antes da implementação. |
| Q2 | O que significa "compatibilidade transparência" no pedido original? | Interpretado como: preservar o contrato de snapshot (`ProductName`, sem `ProductId`), não reescrever histórico e declarar na UI que os nomes são os do pedido. | Confirmar se havia outro requisito de transparência (ex.: contabilidade/auditoria). |
| Q3 | A distribuição de pagamento deve ser por **quantidade de pedidos** ou por **faturamento**? | A UI mostra os dois; o percentual destacado é por quantidade de pedidos. | Confirmar a leitura principal desejada. |
| Q4 | O KPI "pedidos em andamento" deve continuar como quarto card? | Não: os KPIs aprovados são três; o contador aparece no card "Pedidos em andamento". | Confirmar se o card deve voltar. |
| Q5 | O ticket médio deve incluir a taxa de entrega? | Sim, porque o faturamento inclui. | Confirmar se o lojista espera ticket sem taxa. |
| Q6 | Datas futuras devem ser bloqueadas? | Sim, no seletor; a API aceita e retorna zeros. | Confirmar se o lojista precisa lançar/planejar períodos futuros. |
| Q7 | O bloco 5 deve existir em todos os meses (mesmo vazio) ou apenas quando houver dias? | Apenas quando o mês tiver 29+ dias; fevereiro não bissexto tem 4 blocos. | Confirmar a expectativa visual. |
| Q8 | O card de produtos deve mostrar **todos** os itens do ranking ou um top N? | Todos, em região rolável; a rosca agrupa o excedente em "Outros". | Confirmar se um top 10 fixo é preferível. |
| Q9 | O endpoint deve devolver `availableYears` ou o seletor deve usar um intervalo fixo? | `availableYears` calculado no backend, para não inventar janela arbitrária. | Confirmar custo aceitável da consulta extra. |
| Q10 | A lista "Pedidos em andamento" deve continuar no dashboard? | Sim; ela usa `/store` e não é substituída pela grade de vendas. | Confirmar se o painel deve ser somente analítico. |
| Q11 | "Faturamento" deve considerar pedidos `Delivered` ou pagamentos `Paid`? | `Delivered`, conforme a proposta aprovada. | Confirmar se haverá conciliação por pagamento em outro painel. |
| Q12 | Produtos com o mesmo nome e produtos diferentes com nomes iguais serão fundidos? | Sim, sem `ProductId` não há como distinguir; nenhuma heurística será aplicada. | Aceitar explicitamente a limitação. |
| Q13 | A grade substitui "Últimos pedidos" ou os dois blocos devem coexistir? | Substitui; o painel fica com uma única lista de vendas paginada. | Confirmar antes da implementação. |
| Q14 | O cliente deve ver o nome completo ou apenas o primeiro nome? | Nome completo do claim `FullName`, como hoje; é a única PII exposta. | Confirmar requisito de privacidade/loja. |
| Q15 | "Paginação estável" exige apenas ordem determinística (offset) ou imunidade a inserções concorrentes (cursor/snapshot)? | Offset com `CreatedAtUtc DESC, Id DESC`; inserções entre páginas podem deslocar e são reconciliadas no refresh. | Confirmar se é necessário cursor ou `asOfUtc`. |
| Q16 | A expansão de itens deve detalhar adicionais/opções ou apenas nome, variação, quantidade e preços? | Apenas nome/variação/quantidade/unitário/total; adicionais já estão no preço e a composição completa fica em `/app/pedidos`. | Confirmar se o lojista precisa da composição na grade. |

## 15. Validação prevista (após implementação)

```powershell
dotnet build backend/Urbeat.sln

dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~OrderReportService|FullyQualifiedName~AnalyticsRange"

dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~StoreOpeningHoursCalculator"

dotnet test backend/tests/Urbeat.IntegrationTests --filter "FullyQualifiedName~StoreAnalytics|FullyQualifiedName~StoreSales"

cd frontend
npx jest --no-coverage src/app/features/seller-dashboard/seller-dashboard-page.component.spec.ts
npx jest --no-coverage src/app/features/seller-dashboard/components/dashboard-sales-grid.component.spec.ts
npx jest --no-coverage src/app/core/services/order.service.spec.ts
npx jest --no-coverage src/app/core/utils/sao-paulo-date.helper.spec.ts
npx ng build --configuration production
```

Conferência de schema (nenhuma migração esperada):

```powershell
cd backend
dotnet ef migrations has-pending-model-changes --startup-project src/Urbeat.WebApi --project src/Urbeat.Infrastructure
```

O repositório não possui lint/formatter/CI; a validação é o build de produção e os testes focados acima.

## 16. Rollback

Como a mudança é aditiva no backend (dois endpoints + DTOs + helper) e restrita ao dashboard no frontend, o rollback consiste em:

1. restaurar `seller-dashboard-page.component.*` e remover `dashboard-sales-grid.component.*` (voltando à tabela de últimos pedidos, se desejado);
2. remover `getStoreAnalytics`/`getStoreSales` e as interfaces novas (opcional; não afetam outras telas);
3. manter os endpoints novos no backend (inofensivos) ou removê-los junto com os testes.

Nenhuma migração, tabela ou dado persistido precisa ser revertido.
