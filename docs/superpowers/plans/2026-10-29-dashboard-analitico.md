# Dashboard analítico do lojista — Plano de Implementação

> **For agentic workers:** REQUIRED SUB-SKILL: use `superpowers:subagent-driven-development` (recomendado) ou `superpowers:executing-plans` para executar este plano tarefa por tarefa. Cada passo tem checkbox (`- [ ]`). A execução é incremental e cada tarefa termina em um gate de teste/build.
>
> **Restrição desta entrega:** este documento é o único artefato produzido agora. Nenhum código de produto, migração, coleção Postman, commit, push ou deploy é criado nesta etapa. A execução futura também **não inclui** commit nem deploy: o diff fica para revisão humana e a validação termina em build/testes focados. Os blocos de código abaixo são **contratos/assinaturas**, não implementações.

**Goal:** substituir o dashboard do lojista por um painel analítico por período (dia, bloco de semana, mês e ano), alimentado por dois endpoints `SellerOnly`, com KPIs, produtos, série mensal, formas de pagamento, pedidos em andamento e uma grade paginada de vendas com somatórios de todo o filtro.

**Architecture:** O recorte de calendário é resolvido no backend em `America/Sao_Paulo` e convertido para UTC com `TimeZoneInfo`; o frontend envia apenas `period`, `anchor` e `weekBlock`. A agregação é 100% server-side, restrita às lojas de `Store.OwnerUserId == sellerUserId`. A grade usa um endpoint aditivo e consulta itens somente da página, enquanto totais e contagem vêm de uma agregação separada sem `Skip`/`Take`.

**Tech Stack:** .NET 9, ASP.NET Core, EF Core, PostgreSQL, xUnit/FluentAssertions, Angular 20 standalone com Signals, Ionic 8, Bootstrap 5.3, Jest.

---

## 0. Incoerências e lacunas detectadas na especificação

A especificação aprovada é a base funcional. Os pontos abaixo são ambiguidades ou contradições internas que **precisam de uma decisão explícita** antes da implementação. As resoluções não ampliam escopo nem alteram contratos existentes.

| ID | Onde | Evidência | Resolução adotada neste plano |
| --- | --- | --- | --- |
| I1 | Âncora de `week` | §5.1 diz que a âncora de `Semana`/`Mês` é o dia 1 do mês; §5.3 diz que o bloco padrão é o que contém o dia da âncora; §7.3 exemplifica `anchorDate: "2026-10-29"` com `weekBlock: 5`. Com âncora no dia 1, o bloco padrão seria sempre 1. | Para `week`, a âncora canônica é o **primeiro dia do bloco resolvido** (29/10 no exemplo). O backend normaliza a âncora para o início do bloco; o frontend envia sempre `weekBlock` explícito. Para `month`, a âncora é o dia 1; para `year`, 1º de janeiro. O exemplo da §7.3 permanece válido. |
| I2 | Validação de `weekBlock` fora de `week` | §7.2 diz que o valor é ignorado fora de `week`; §8.4 diz que `weekBlock` fora de 1..5 gera `400`. | `weekBlock` só é validado quando `period=week`. Com `day`/`month`/`year`, qualquer valor é ignorado e a resposta traz `weekBlock: null`. |
| I3 | DST em limite **fim exclusivo** | §8.3 manda escolher o instante UTC mais cedo para horário ambíguo. Se o fim usasse o instante mais tarde, a hora repetida apareceria no fim de um dia **e** no início do seguinte (sobreposição). | Usar o **mesmo critério** (instante mais cedo, maior offset) em todo limite: `EndOfDayExclusiveUtc(d)` é exatamente `StartOfDayUtc(d + 1)`. Assim os intervalos são contíguos e sem sobreposição. Convenção documentada: a hora repetida (23:00–23:59 do dia anterior) pertence ao dia seguinte; `2019-02-16` tem 24h (`02:00Z`→`02:00Z`) e `2019-02-17` começa às `02:00Z`. |
| I4 | Texto de adicionais na expansão | §7.5 não inclui `AdditionalNames`/opções no DTO; §9.5 manda mostrar o texto de apoio "quando houver `AdditionalNames`/opções". O frontend não recebe esse campo. | O texto de apoio da expansão é **incondicional**: "Adicionais e opções já estão incluídos no preço do item; a composição completa fica em Pedidos." Nenhum campo novo é adicionado ao contrato. |
| I5 | Contador de "Pedidos em andamento" | §9.3 carrega a lista com `pageSize: 100`; D3 exige o contador no cabeçalho do card. Acima de 100 pedidos no período, a contagem carregada é menor que a real. | O contador usa a lista carregada (`inProgressOrders().length`). Quando `ordersTotal >= 100`, a UI adiciona a nota "Entre os 100 pedidos mais recentes" e não promete contagem exata; a limitação fica registrada. |
| I6 | Dois relógios para "ano corrente" | §8.1 injeta `TimeProvider` no controller **e** em `OrderReportService`. Se forem instâncias diferentes em teste, a âncora padrão e `availableYears` podem divergir na virada do ano. | Uma única instância de `TimeProvider` é registrada em DI. O resolver recebe `todayLocal` já calculado pelo controller; o serviço usa o mesmo `TimeProvider` apenas para `availableYears`. Testes injetam o mesmo fake nos dois pontos quando exercitam a fronteira de ano. |
| I7 | Helper de data local já existe | `StoreOpeningHoursCalculator.GetSaoPauloDate(DateTimeOffset)` já é público e resolve o fuso com fallback Windows. §8.1 propõe um novo `SaoPauloCalendar` com a mesma responsabilidade. | `SaoPauloCalendar` passa a ser a única implementação do resolvedor e da data local. `StoreOpeningHoursCalculator` delega a ele e mantém a assinatura pública e o comportamento atuais. |
| I8 | `period` na resposta | §7.3 espera `"period": "week"`, mas o projeto não configura `JsonStringEnumConverter`; um enum no DTO seria serializado como número. | O DTO de resposta expõe `string Period`; o enum `AnalyticsGranularity` fica restrito ao domínio interno do resolver/serviço. `payments[].paymentMethod` continua numérico. |
| I9 | Claim `FullName` duplicado | `AspNetUserClaims` não tem unicidade por `(UserId, ClaimType)`; `ToDictionaryAsync` lançaria exceção se houvesse duplicata. O código atual assume uma linha. | A consulta de nomes agrupa por `UserId`, escolhe deterministicamente a primeira linha (`OrderBy(Id)`) e devolve `null` quando não há claim. Nunca usa telefone/e-mail como fallback. |
| I10 | Teste de layout mobile em Jest | §11.3.28 pede "linhas viram cards" em teste unitário, mas jsdom não aplica media queries/CSS de layout. | O teste verifica a estrutura e os contratos observáveis (células com `data-label`, classes, botão de 44px por classe/atributo e ausência de tabela comprimida). A verificação visual em 360px é manual e consta no aceite. |
| I11 | Tema do shell vs tokens do dashboard | `AGENTS.md`/`DESIGN.md` pedem bordeaux e `var(--app-*)` no dashboard; `frontend/src/styles/seller-dashboard-bootstrap.scss` define `--dash-primary: #6d5df2` e aplica `!important` em `.segmented button.active`, `.metric-card`, `.metrics-grid` etc. | Não alterar o SCSS global do shell (afetaria todas as telas do lojista). Os componentes novos usam classes próprias e `var(--app-*)`; os gráficos usam a rampa bordeaux. A divergência de paleta do chrome do shell fica registrada para decisão de design, sem regressão funcional. |
| I12 | `ordersTotal` sem uso definido | §9.2 lista `ordersTotal`, mas o fluxo não explica quem o consome. | `ordersTotal` guarda o `totalItems` de `/store` e é a fonte de `shell.ordersCount`; a lista de andamento usa apenas `items`. |
| I13 | Filtro "sem lógica de negócio" | §9.1 diz que o filtro não tem lógica de negócio, mas §5.3 exige blocos, limites e normalização de âncora. | O filtro pode conter **matemática pura de calendário** (dias do mês, blocos válidos, limites de seleção). Não faz HTTP, não soma valores e não decide sozinho o período efetivo; emite intenção e a página normaliza. |
| I14 | Caminho dos componentes de feature | §9.1 usa caminhos "planos" (`components/dashboard-period-filter.component.ts`), enquanto o repositório usa pasta por componente (`components/recent-orders-list/`). | Seguir o padrão do repositório: `components/dashboard-period-filter/dashboard-period-filter.component.*` e `components/dashboard-sales-grid/dashboard-sales-grid.component.*`. O conteúdo e os seletores são os previstos na spec. |
| I15 | "Cliente não soma valores" vs `yearTotal`/`bestMonth` | D4 diz que o cliente não soma valores; §9.2/§9.5 exigem `yearTotal` e `bestMonth` derivados de `months[]`. | O cliente soma apenas os **12 valores mensais já agregados** para exibição de "Total do ano" e escolha do "Melhor mês". Nunca soma pedidos, itens, taxas, páginas ou recalcula ticket. Nenhum valor autoritativo é recalculado. |

> Nenhuma resolução altera `GET /api/orders/store/report`, `GET /api/orders/store`, `GET /api/orders/store/customers`, `OrderSummaryResponseDto`, `saoPauloPeriodRange` ou o schema de `Order`/`OrderItem`.

---

## 1. Escopo

### 1.1 Entra

- Dois endpoints novos e aditivos:
  - `GET /api/orders/store/analytics`
  - `GET /api/orders/store/sales`
- Resolução autoritativa do calendário em `America/Sao_Paulo`, com DST histórico.
- KPIs de pedidos entregues, faturamento (com taxa), taxas de entrega e ticket médio.
- Produtos por quantidade (nome snapshot), série de 12 meses, formas de pagamento e anos disponíveis.
- Grade paginada de vendas com itens somente da página, totais de todo o filtro e PII mínima (`customerName`).
- Dashboard Angular com filtro Dia/Semana/Mês/Ano, gráficos sem biblioteca externa, grade expansível e estados de loading/vazio/erro.
- Remoção de "Atalhos rápidos", "Resumo por serviço" e "Últimos pedidos".

### 1.2 Não entra

- Alterar checkout, preços, status, impressão, assinatura ou qualquer fluxo transacional.
- Alterar contratos existentes ou o schema (`OrderItem` não ganha `ProductId`).
- Migração, tabela, índice ou coluna nova.
- Exportação CSV/Excel, comparação de períodos, metas, alertas, cache, pré-agregação ou cursor de paginação.
- Persistir filtro em URL, `localStorage` ou preferência.
- Equipe/permissões por loja; a autorização continua por `Store.OwnerUserId`.
- Biblioteca de gráficos, PrimeNG, novos componentes Bootstrap ou nova dependência npm/NuGet.
- Endpoint por pedido para buscar itens.
- Exibir telefone, e-mail, documento, endereço, observações, PIN, rota ou motorista na grade.
- Sincronizar a coleção Postman (tarefa do agente `postman-sync`).
- Commit, push ou deploy.

---

## 2. Contratos HTTP

### 2.1 `GET /api/orders/store/analytics`

- Autorização: `AuthorizationPolicies.SellerOnly` (`[Authorize(Policy = "SellerOnly")]`).
- Sem token → `401`; autenticado sem role `Seller` → `403`.
- Loja inexistente ou lojista sem loja → `200` com zeros/coleções vazias (`months` com 12 zeros e `availableYears` com o ano corrente). Nunca `404`.
- Parâmetros inválidos → `400` com `ValidationProblemDetails`.
- Somente leitura, idempotente, sem efeitos colaterais.

Parâmetros de query:

| Nome | Tipo | Obrigatório | Regra |
| --- | --- | --- | --- |
| `period` | string | não | `day` (padrão), `week`, `month`, `year`; comparação ordinal em minúsculas. `null`/vazio → `day`. |
| `anchor` | string `yyyy-MM-dd` | não | Data local em `America/Sao_Paulo`; padrão = data local atual. Formato inválido → `400`. |
| `weekBlock` | int? | não | 1..5; usado somente com `period=week`. Padrão = bloco que contém o dia da âncora, limitado ao último bloco válido. Bloco inexistente no mês → `400`. Ignorado nos demais períodos. |

Resposta `200` (exemplo abreviado; `months` sempre tem 12 itens):

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
    { "productName": "X-Burger", "quantity": 30, "revenue": 900.00 }
  ],
  "payments": [
    { "paymentMethod": 1, "deliveredOrders": 7, "revenue": 800.00 }
  ],
  "months": [
    { "year": 2026, "month": 1, "deliveredOrders": 0, "revenue": 0.00 }
  ],
  "availableYears": [2026, 2025]
}
```

Semântica:

| Campo | Regra |
| --- | --- |
| `period` | Eco do período resolvido, em minúsculas. |
| `anchorDate` | Âncora canônica: dia escolhido (`day`), início do bloco (`week`), dia 1 (`month`), 1º de janeiro (`year`). Sempre `yyyy-MM-dd`. |
| `weekBlock` | Bloco resolvido em `week`; `null` nos demais. |
| `rangeStartUtc` | Início inclusivo, UTC com `Z`. |
| `rangeEndUtcExclusive` | Fim exclusivo, UTC com `Z`. |
| `timeZone` | Literal `America/Sao_Paulo`. |
| `chartYear` | Ano da âncora; governa a série de 12 meses. |
| `kpis.deliveredOrders` | `COUNT` de pedidos `Delivered` no intervalo. |
| `kpis.revenue` | `SUM(Order.Total)`; inclui taxas de entrega. |
| `kpis.deliveryFees` | `SUM(Order.DeliveryFee)`; subconjunto de `revenue`, nunca somado a ele. |
| `kpis.averageTicket` | `revenue / deliveredOrders`, 2 casas, `MidpointRounding.AwayFromZero`; `0` sem pedidos. |
| `products[]` | Agrupado por `(ProductName ?? "").Trim()`, sem paginação, ordenado por quantidade desc, receita desc, nome ordinal asc. |
| `payments[]` | Métodos presentes no recorte, ordenados por receita desc e enum asc. |
| `months[]` | Exatamente 12 itens de janeiro a dezembro de `chartYear`, com zero-fill. |
| `availableYears[]` | Anos selecionáveis, decrescente, incluindo o ano corrente; derivado do pedido mais antigo de qualquer status das lojas do lojista. |

### 2.2 `GET /api/orders/store/sales`

Mesma autorização, mesma base (`Delivered` + `[rangeStartUtc, rangeEndUtcExclusive)`) e mesmo resolvedor de calendário da analytics. O cliente não envia `storeId` nem datas UTC.

| Nome | Tipo | Obrigatório | Regra |
| --- | --- | --- | --- |
| `period` | string | não | Igual à analytics. |
| `anchor` | string | não | Igual à analytics. |
| `weekBlock` | int? | não | Igual à analytics. |
| `page` | int | não | Padrão 1; `< 1` normaliza para 1. |
| `pageSize` | int | não | Padrão 20; `< 1` normaliza para 20; `> 100` normaliza para 100. |

Resposta `200`:

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
        { "productName": "X-Burger", "variationName": null, "quantity": 2, "unitPrice": 35.00, "totalPrice": 70.00 }
      ]
    }
  ]
}
```

Regras:

- `totalItems` e `totals` são de **todos** os pedidos `Delivered` do filtro, independentes da página.
- `totals.salesTotal = Σ(Order.Total)`; inclui taxas.
- `totals.deliveryFees = Σ(Order.DeliveryFee)`; já incluído em `salesTotal`. **Nunca somar os dois.**
- `sales[]` ordenado por `CreatedAtUtc DESC, Id DESC`; `Id` é o desempate determinístico.
- `sales[].items[]` contém somente itens de pedidos da página, ordenados por `ProductName` asc e `Id` asc.
- `page > totalPages` → `sales: []`, mantendo `totalItems`, `totalPages` e `totals`.
- `totalPages = 0` quando `totalItems = 0`.
- PII: somente `customerName` (claim `FullName`; `null` quando ausente). Telefone, e-mail, documento, endereço, observações, PIN, motorista, rota e IDs de usuário não existem no DTO.
- Consistência obrigatória com a analytics para o mesmo filtro: `totalItems == kpis.deliveredOrders`, `salesTotal == kpis.revenue`, `deliveryFees == kpis.deliveryFees`.
- Erros: `401`/`403` como na analytics; `400` para `period`/`anchor`/`weekBlock` inválidos; `page`/`pageSize` fora da faixa são normalizados, não geram erro.

### 2.3 Erros de validação

Formato `ValidationProblemDetails` com chaves em minúsculas:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "period": ["Período inválido."]
  }
}
```

Mensagens canônicas:

| Chave | Condição | Mensagem |
| --- | --- | --- |
| `period` | valor fora de `day/week/month/year` | `Período inválido.` |
| `anchor` | `DateOnly.TryParseExact("yyyy-MM-dd", InvariantCulture)` falha | `Data inválida.` |
| `weekBlock` | valor fora de 1..5 em `week` | `Bloco de semana inválido.` |
| `weekBlock` | bloco 5 em mês com menos de 29 dias | `O mês não possui o bloco 5.` |

`weekBlock=abc` é rejeitado pelo model binding do `[ApiController]` antes da action, também como `400` com chave `weekBlock`.

---

## 3. Contratos internos e dependências por camada

### 3.1 Domain

Novo `backend/src/Urbeat.Domain/Services/SaoPauloCalendar.cs`:

```csharp
public static class SaoPauloCalendar
{
    public const string TimeZoneId = "America/Sao_Paulo";
    public static TimeZoneInfo TimeZone { get; }

    public static DateOnly GetLocalDate(DateTimeOffset utcNow);
    public static DateOnly GetLocalDate(DateTime utcNow);

    public static DateTime StartOfDayUtc(DateOnly date);
    public static DateTime EndOfDayExclusiveUtc(DateOnly date);

    public static DateTime StartOfMonthUtc(int year, int month);
    public static DateTime EndOfMonthExclusiveUtc(int year, int month);

    public static DateTime StartOfYearUtc(int year);
    public static DateTime EndOfYearExclusiveUtc(int year);
}
```

Regras de implementação:

- `TimeZone` resolve primeiro `America/Sao_Paulo` e, em `TimeZoneNotFoundException`/`InvalidTimeZoneException`, `E. South America Standard Time`.
- Todos os retornos são `DateTime` com `Kind == Utc`.
- `GetLocalDate(DateTime)` trata `Unspecified` como UTC e converte `Local` com `ToUniversalTime()`; nunca usa `DateTime.Now`.
- Todo limite ambíguo usa o **maior** offset (instante UTC mais cedo). `EndOfDayExclusiveUtc(d)` deve ser exatamente `StartOfDayUtc(d + 1)`; o mesmo vale para mês/ano.
- Converter com `DateTime.SpecifyKind(local, DateTimeKind.Unspecified)` antes de `ConvertTimeToUtc`/`GetAmbiguousTimeOffsets`; nunca confiar no `Kind` ambiente.
- Horário inválido em início de dia (00:00 inexistente) usa o primeiro instante válido do dia; documentar e cobrir com teste.
- `StoreOpeningHoursCalculator` passa a usar `SaoPauloCalendar.TimeZone` e `GetSaoPauloDate` delega a `GetLocalDate`, preservando assinatura e comportamento.

### 3.2 Application

Novos tipos em `backend/src/Urbeat.Application/Analytics/`:

```csharp
public enum AnalyticsGranularity { Day = 1, Week = 2, Month = 3, Year = 4 }

public sealed record AnalyticsRange(
    AnalyticsGranularity Period,
    DateOnly AnchorDate,
    int? WeekBlock,
    DateTime StartUtc,
    DateTime EndUtcExclusive,
    int ChartYear);

public sealed class AnalyticsRangeResolution
{
    public AnalyticsRange? Range { get; }
    public IReadOnlyDictionary<string, string[]> Errors { get; }
    public bool IsValid { get; }
}

public static class AnalyticsRangeResolver
{
    public static AnalyticsRangeResolution Resolve(StoreAnalyticsQueryDto query, DateOnly todayLocal);
}
```

Resolução:

1. `period`: `null`/vazio → `Day`; comparação ordinal exata para os quatro valores; inválido → erro `period`.
2. `anchor`: `null`/vazio → `todayLocal`; caso contrário `DateOnly.TryParseExact` com `"yyyy-MM-dd"` e `CultureInfo.InvariantCulture`; inválido → erro `anchor`.
3. `weekBlock` (somente `Week`): valida 1..5; ausente → `((anchor.Day - 1) / 7) + 1`, limitado a `Math.Ceiling(daysInMonth / 7.0)`; fora da faixa → erro; bloco inexistente → erro.
4. Intervalos:
   - `Day`: `StartOfDayUtc(anchor)` até `EndOfDayExclusiveUtc(anchor)`.
   - `Week`: primeiro dia do bloco até `EndOfDayExclusiveUtc(último dia do bloco)`.
   - `Month`: `StartOfMonthUtc(anchor.Year, anchor.Month)` até `EndOfMonthExclusiveUtc(...)`.
   - `Year`: `StartOfYearUtc(anchor.Year)` até `EndOfYearExclusiveUtc(...)`.
5. Âncora canônica: início do bloco em `Week`, dia 1 em `Month`, 1º de janeiro em `Year`, data informada em `Day`.
6. `ChartYear = âncora canônica.Year`.
7. `WeekBlock` é `null` fora de `Week`; em `Week` é sempre o bloco resolvido.
8. Erros independentes por campo; `weekBlock` só é avaliado quando `period=week` e a âncora é válida.

DTOs de entrada:

```csharp
public class StoreAnalyticsQueryDto
{
    public string? Period { get; set; }
    public string? Anchor { get; set; }
    public int? WeekBlock { get; set; }
}

public sealed class StoreSalesQueryDto : StoreAnalyticsQueryDto
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
```

DTOs de resposta (classes `sealed`, propriedades `init`/`set` no padrão do projeto, coleções inicializadas com `[]`):

| DTO | Propriedades |
| --- | --- |
| `StoreAnalyticsResponseDto` | `string Period`, `DateOnly AnchorDate`, `int? WeekBlock`, `DateTime RangeStartUtc`, `DateTime RangeEndUtcExclusive`, `string TimeZone`, `int ChartYear`, `StoreAnalyticsKpisDto Kpis`, `IReadOnlyCollection<StoreAnalyticsProductDto> Products`, `IReadOnlyCollection<StoreAnalyticsPaymentDto> Payments`, `IReadOnlyCollection<StoreAnalyticsMonthDto> Months`, `IReadOnlyCollection<int> AvailableYears` |
| `StoreAnalyticsKpisDto` | `int DeliveredOrders`, `decimal Revenue`, `decimal DeliveryFees`, `decimal AverageTicket` |
| `StoreAnalyticsProductDto` | `string ProductName`, `int Quantity`, `decimal Revenue` |
| `StoreAnalyticsPaymentDto` | `PaymentMethod PaymentMethod`, `int DeliveredOrders`, `decimal Revenue` |
| `StoreAnalyticsMonthDto` | `int Year`, `int Month`, `int DeliveredOrders`, `decimal Revenue` |
| `PagedStoreSalesResponseDto` | `string Period`, `DateOnly AnchorDate`, `int? WeekBlock`, `DateTime RangeStartUtc`, `DateTime RangeEndUtcExclusive`, `string TimeZone`, `int Page`, `int PageSize`, `int TotalItems`, `int TotalPages`, `StoreSalesTotalsDto Totals`, `IReadOnlyCollection<StoreSaleRowDto> Sales` |
| `StoreSalesTotalsDto` | `decimal SalesTotal`, `decimal DeliveryFees` |
| `StoreSaleRowDto` | `Guid OrderId`, `string Code`, `string? CustomerName`, `DateTime CreatedAtUtc`, `decimal Subtotal`, `decimal DeliveryFee`, `decimal Total`, `IReadOnlyCollection<StoreSaleItemDto> Items` |
| `StoreSaleItemDto` | `string ProductName`, `string? VariationName`, `int Quantity`, `decimal UnitPrice`, `decimal TotalPrice` |

`IOrderReportService` recebe, de forma aditiva e em duas etapas para manter o build verde:

```csharp
// Tarefa 4
Task<StoreAnalyticsResponseDto> GetStoreAnalyticsAsync(
    Guid sellerUserId,
    AnalyticsRange range,
    CancellationToken cancellationToken = default);

// Tarefa 5
Task<PagedStoreSalesResponseDto> GetStoreSalesAsync(
    Guid sellerUserId,
    AnalyticsRange range,
    int page,
    int pageSize,
    CancellationToken cancellationToken = default);
```

### 3.3 Infrastructure

`OrderReportService`:

```csharp
public OrderReportService(ApplicationDbContext dbContext, TimeProvider timeProvider);
```

- O método `GetStoreSimpleReportAsync` existente permanece intacto.
- `_timeProvider` é usado apenas para o ano corrente de `availableYears`.
- Nenhuma consulta de agregação usa `Skip`/`Take`.
- Todas usam `AsNoTracking()` e o `CancellationToken` recebido.
- Filtro de autorização sempre `Store.OwnerUserId == sellerUserId`; nenhum `storeId` vem do cliente.
- `storeIds` é materializado uma vez; lista vazia curto-circuita para resposta zerada. `Store.OwnerUserId` tem índice único (0 ou 1 loja por lojista), mas a consulta usa lista para manter o contrato e tolerar dados legados.

Plano da analytics:

| Consulta | Forma |
| --- | --- |
| Lojas | `Stores.Where(OwnerUserId == sellerUserId).Select(Id)` |
| KPIs | `Count`, `Sum((decimal?)Total)`, `Sum((decimal?)DeliveryFee)` sobre `Delivered` no intervalo |
| Pagamentos | `GroupBy(PaymentMethod)` + `Count` + `Sum(Total)` |
| Produtos | `Join` da base com `OrderItems` por `OrderId`, `GroupBy((ProductName ?? "").Trim())`, `Sum(Quantity)`, `Sum(TotalPrice)`; ordenação final em memória |
| Meses | 12 consultas `Count`/`Sum` com os limites locais de cada mês de `chartYear` |
| Anos | `Min((DateTime?)CreatedAtUtc)` de todos os pedidos das lojas (qualquer status), convertido para ano local; `minYear..currentYear` decrescente |

Plano da grade:

| # | Consulta | Detalhe |
| --- | --- | --- |
| 1 | Agregado total | `Count`, `Sum((decimal?)Total)`, `Sum((decimal?)DeliveryFee)` sem `Skip`/`Take` |
| 2 | Página | `OrderByDescending(CreatedAtUtc).ThenByDescending(Id).Skip((page - 1) * pageSize).Take(pageSize)`; projeção `Id, Code, CustomerUserId, Subtotal, DeliveryFee, Total, CreatedAtUtc` |
| 3 | Itens | `OrderItems.Where(pageOrderIds.Contains(OrderId))`, projeção `OrderId, ProductName, VariationName, Quantity, UnitPrice, TotalPrice`, ordene por `ProductName, Id` |
| 4 | Nomes | `UserClaims.Where(UserId in page && ClaimType == "FullName")`, agrupado por `UserId`, `OrderBy(Id)` e primeiro `ClaimValue` |

Normalização antes de qualquer consulta: `page < 1 → 1`; `pageSize < 1 → 20`; `pageSize > 100 → 100`. Se `page > totalPages`, as consultas 2–4 não são executadas e `sales` volta vazio.

### 3.4 WebApi

`OrdersController` recebe `TimeProvider` e expõe:

```csharp
[HttpGet("store/analytics")]
[Authorize(Policy = AuthorizationPolicies.SellerOnly)]
[ProducesResponseType<StoreAnalyticsResponseDto>(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]

[HttpGet("store/sales")]
[Authorize(Policy = AuthorizationPolicies.SellerOnly)]
[ProducesResponseType<PagedStoreSalesResponseDto>(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
```

Fluxo comum:

1. `GetCurrentUserId()`; `null` → `Unauthorized()`.
2. `todayLocal = SaoPauloCalendar.GetLocalDate(_timeProvider.GetUtcNow())`.
3. `resolution = AnalyticsRangeResolver.Resolve(query, todayLocal)`.
4. `!resolution.IsValid` → `ValidationProblem(new ValidationProblemDetails(resolution.Errors.ToDictionary(x => x.Key, x => x.Value)))`.
5. Chamar o serviço com `sellerUserId.Value` e `resolution.Range!`; `Ok(resultado)`.

As rotas literais `store/analytics` e `store/sales` têm precedência sobre `store/{orderId}`; não é necessário mover a rota existente.

### 3.5 Frontend

Modelos aditivos em `frontend/src/app/shared/models/order.model.ts`:

```ts
export type StoreAnalyticsPeriod = 'day' | 'week' | 'month' | 'year';

export interface StoreAnalyticsQuery {
  period: StoreAnalyticsPeriod;
  anchor: string; // yyyy-MM-dd
  weekBlock?: number;
}

export interface StoreAnalytics {
  period: StoreAnalyticsPeriod;
  anchorDate: string;
  weekBlock: number | null;
  rangeStartUtc: string;
  rangeEndUtcExclusive: string;
  timeZone: string;
  chartYear: number;
  kpis: StoreAnalyticsKpis;
  products: StoreAnalyticsProduct[];
  payments: StoreAnalyticsPayment[];
  months: StoreAnalyticsMonth[];
  availableYears: number[];
}

export interface StoreAnalyticsKpis {
  deliveredOrders: number;
  revenue: number;
  deliveryFees: number;
  averageTicket: number;
}

export interface StoreAnalyticsProduct {
  productName: string;
  quantity: number;
  revenue: number;
}

export interface StoreAnalyticsPayment {
  paymentMethod: PaymentMethod;
  deliveredOrders: number;
  revenue: number;
}

export interface StoreAnalyticsMonth {
  year: number;
  month: number;
  deliveredOrders: number;
  revenue: number;
}

export interface StoreSalesQuery extends StoreAnalyticsQuery {
  page?: number;
  pageSize?: number;
}

export interface StoreSalesTotals {
  salesTotal: number;
  deliveryFees: number;
}

export interface StoreSaleItem {
  productName: string;
  variationName?: string | null;
  quantity: number;
  unitPrice: number;
  totalPrice: number;
}

export interface StoreSaleRow {
  orderId: string;
  code: string;
  customerName?: string | null;
  createdAtUtc: string;
  subtotal: number;
  deliveryFee: number;
  total: number;
  items: StoreSaleItem[];
}

export interface PagedStoreSales {
  period: StoreAnalyticsPeriod;
  anchorDate: string;
  weekBlock: number | null;
  rangeStartUtc: string;
  rangeEndUtcExclusive: string;
  timeZone: string;
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  totals: StoreSalesTotals;
  sales: StoreSaleRow[];
}
```

`OrderService` (métodos aditivos, seguindo o padrão atual com `URLSearchParams`):

```ts
getStoreAnalytics(query: StoreAnalyticsQuery): Observable<StoreAnalytics>;
getStoreSales(query: StoreSalesQuery): Observable<PagedStoreSales>;
```

Regras de codificação:

- Sempre enviar `period` e `anchor`.
- Enviar `weekBlock` **somente** quando `period === 'week'`.
- `getStoreSales` envia `page`/`pageSize` quando definidos (a página sempre envia `page` e `pageSize: 20`).
- Não usar `HttpParams` neste serviço; manter `URLSearchParams` como nos métodos existentes.

Helpers aditivos em `frontend/src/app/core/utils/sao-paulo-date.helper.ts` (não alterar `DashboardPeriod`, `saoPauloPeriodRange` nem as funções existentes):

```ts
export function saoPauloTodayIso(now?: Date): string;
export function formatSaoPauloIsoDate(isoDate: string): string; // yyyy-MM-dd -> dd/MM/yyyy, sem conversão de fuso
export function analyticsDaysInMonth(year: number, month: number): number;
export function analyticsMaxWeekBlock(year: number, month: number): number;
export function analyticsWeekBlockForDay(year: number, month: number, day: number): number;
export function analyticsWeekBlockStartIso(anchorIso: string, block: number): string;
export function analyticsWeekBlockEndIso(anchorIso: string, block: number): string; // último dia do bloco
export function analyticsWeekBlockLabel(block: number, year: number, month: number): string;
```

`formatSaoPauloIsoDate` faz `split('-')`; **não** usa `new Date('yyyy-MM-dd')`, que interpreta UTC e pode exibir o dia anterior no fuso local.

Contratos dos componentes:

| Componente | Inputs | Outputs |
| --- | --- | --- |
| `app-dashboard-period-filter` | `period: StoreAnalyticsPeriod`, `anchorDate: string`, `weekBlock: number`, `availableYears: readonly number[]`, `todayIso: string` | `periodChange: StoreAnalyticsPeriod`, `anchorDateChange: string`, `weekBlockChange: number` |
| `app-dashboard-sales-grid` | `result: PagedStoreSales \| null`, `loading: boolean`, `error: boolean`, `expandedOrders: ReadonlySet<string>`, `rangeLabel: string` | `pageChange: number`, `retry: void`, `toggleItems: string` |
| `app-analytics-donut-chart` | `slices: readonly DonutSlice[]` (`{ label, value }`), `total: number`, `ariaLabel: string`, `centerLabel?: string` | — |
| `app-analytics-bar-chart` | `bars: readonly BarDatum[]` (`{ label, value, highlighted }`), `total: number`, `ariaLabel: string`, `caption: string`, `bestLabel?: string` | — |

### 3.6 Dependências por camada

| Camada | Projeto/arquivo | Depende de | Novas dependências |
| --- | --- | --- | --- |
| Domain | `Urbeat.Domain` | BCL (`TimeZoneInfo`, `DateOnly`) | nenhuma |
| Application | `Urbeat.Application` | `Urbeat.Domain` (já referenciado), FluentValidation existente | nenhuma |
| Infrastructure | `Urbeat.Infrastructure` | `Urbeat.Application`, `Urbeat.Domain`, EF Core, `TimeProvider` registrado em DI | nenhuma |
| WebApi | `Urbeat.WebApi` | Application/Infrastructure, MVC, DI | nenhuma |
| Frontend | `frontend/` | Angular 20, Ionic 8, Bootstrap 5.3, ionicons | nenhuma; sem `angular.json` |
| Testes backend | `Urbeat.UnitTests`, `Urbeat.IntegrationTests` | xUnit, FluentAssertions, EF InMemory, `TestWebApplicationFactory` | nenhuma |
| Testes frontend | `frontend/` | Jest, jest-preset-angular, `HttpTestingController` | nenhuma |

---

## 4. Estrutura de arquivos

### 4.1 Backend — criar

| Caminho | Responsabilidade |
| --- | --- |
| `backend/src/Urbeat.Domain/Services/SaoPauloCalendar.cs` | Resolvedor único do fuso, data local e limites UTC com DST. |
| `backend/src/Urbeat.Application/Analytics/AnalyticsGranularity.cs` | Enum `Day`, `Week`, `Month`, `Year`. |
| `backend/src/Urbeat.Application/Analytics/AnalyticsRange.cs` | Record do intervalo resolvido + resultado/erros. |
| `backend/src/Urbeat.Application/Analytics/AnalyticsRangeResolver.cs` | Validação e resolução pura, sem banco. |
| `backend/src/Urbeat.Application/DTOs/StoreAnalyticsQueryDto.cs` | Entrada da analytics. |
| `backend/src/Urbeat.Application/DTOs/StoreAnalyticsResponseDto.cs` | Contrato da §2.1 e DTOs aninhados. |
| `backend/src/Urbeat.Application/DTOs/StoreSalesQueryDto.cs` | Entrada da grade (herda a analytics e adiciona paginação). |
| `backend/src/Urbeat.Application/DTOs/PagedStoreSalesResponseDto.cs` | Contrato da §2.2 e DTOs aninhados. |

### 4.2 Backend — alterar

| Caminho | Alteração |
| --- | --- |
| `backend/src/Urbeat.Domain/Services/StoreOpeningHoursCalculator.cs` | Delegar fuso/data local ao `SaoPauloCalendar`; nenhuma mudança de assinatura/comportamento. |
| `backend/src/Urbeat.Application/Interfaces/IOrderReportService.cs` | Dois métodos aditivos (em tarefas separadas). |
| `backend/src/Urbeat.Infrastructure/Services/OrderReportService.cs` | Construtor com `TimeProvider`; implementação das consultas; método existente intacto. |
| `backend/src/Urbeat.WebApi/Controllers/OrdersController.cs` | Injeção de `TimeProvider`; actions `store/analytics` e `store/sales`; resolução/validação. |

### 4.3 Backend — testes

| Caminho | Tipo |
| --- | --- |
| `backend/tests/Urbeat.UnitTests/Domain/SaoPauloCalendarTests.cs` | Novo |
| `backend/tests/Urbeat.UnitTests/Application/Analytics/AnalyticsRangeResolverTests.cs` | Novo |
| `backend/tests/Urbeat.UnitTests/Application/Dtos/StoreAnalyticsDtoSerializationTests.cs` | Novo |
| `backend/tests/Urbeat.UnitTests/Infrastructure/Services/OrderReportServiceTests.cs` | Alterado (construtor + casos) |
| `backend/tests/Urbeat.IntegrationTests/Api/StoreAnalyticsFlowTests.cs` | Novo |
| `backend/tests/Urbeat.IntegrationTests/Api/StoreSalesFlowTests.cs` | Novo |
| `backend/tests/Urbeat.IntegrationTests/Infrastructure/AnalyticsTestData.cs` | Novo helper de seed/timestamps |

### 4.4 Frontend — criar

| Caminho | Responsabilidade |
| --- | --- |
| `frontend/src/app/features/seller-dashboard/components/dashboard-period-filter/dashboard-period-filter.component.{ts,html,scss,spec.ts}` | Filtro segmentado + seletores + blocos; emite intenção, não faz HTTP. |
| `frontend/src/app/features/seller-dashboard/components/dashboard-sales-grid/dashboard-sales-grid.component.{ts,html,scss,spec.ts}` | Grade paginada, expansão, rodapé, estados e paginação. |
| `frontend/src/app/shared/components/analytics-donut-chart/analytics-donut-chart.component.{ts,html,scss,spec.ts}` | Rosca SVG/HTML com legenda acessível. |
| `frontend/src/app/shared/components/analytics-bar-chart/analytics-bar-chart.component.{ts,html,scss,spec.ts}` | 12 barras e tabela `<details>` acessível. |

### 4.5 Frontend — alterar

| Caminho | Alteração |
| --- | --- |
| `frontend/src/app/features/seller-dashboard/seller-dashboard-page.component.ts` | Novo estado/signals, fluxo de carga, remoção de atalhos/resumos, integração dos componentes. |
| `frontend/src/app/features/seller-dashboard/seller-dashboard-page.component.html` | Novo layout; remoção de "Atalhos rápidos", "Resumo por serviço", "Últimos pedidos" e date pill. |
| `frontend/src/app/features/seller-dashboard/seller-dashboard-page.component.scss` | Grids dos gráficos, KPIs, grade; remoção de `.quick-*`, `.date-pill` e estilos órfãos. |
| `frontend/src/app/features/seller-dashboard/seller-dashboard-page.component.spec.ts` | Reescrever casos de período/date pill/KPIs; manter descarte de resposta obsoleta. |
| `frontend/src/app/shared/models/order.model.ts` | Interfaces aditivas. |
| `frontend/src/app/core/services/order.service.ts` | Métodos aditivos. |
| `frontend/src/app/core/services/order.service.spec.ts` | Casos de codificação dos dois endpoints. |
| `frontend/src/app/core/utils/sao-paulo-date.helper.ts` | Helpers aditivos. |
| `frontend/src/app/core/utils/sao-paulo-date.helper.spec.ts` | Casos novos, sem alterar os existentes. |

### 4.6 Sem alteração

`GET /api/orders/store/report`, `GET /api/orders/store`, `GET /api/orders/store/customers`, `OrderSummaryResponseDto`, `Order`/`OrderItem`, `ApplicationDbContext`, migrações, `package.json`, `angular.json`, `saoPauloPeriodRange`, `DashboardPeriod`, `seller-orders-page.component.*`, `seller-dashboard-bootstrap.scss`.

---

## 5. Ordem de execução

```text
T1 Domain calendar
 └─ T2 Application resolver
     └─ T3 DTOs + serialização
         └─ T4 Infrastructure analytics
             └─ T5 Infrastructure sales
                 └─ T6 WebApi + integração
T7 Frontend models/service/helpers (pode começar após T3)
 ├─ T8 Chart components
 ├─ T9 Period filter
 └─ T10 Sales grid
     └─ T11 Dashboard page
         └─ T12 Validação final
```

Cada tarefa termina com um gate verde. Não iniciar T4 antes de T1–T3, nem T11 antes de T7–T10.

---

## 6. Tarefas

### Task 1: Calendário de São Paulo no Domain

**Files:**
- Create: `backend/src/Urbeat.Domain/Services/SaoPauloCalendar.cs`
- Modify: `backend/src/Urbeat.Domain/Services/StoreOpeningHoursCalculator.cs`
- Test: `backend/tests/Urbeat.UnitTests/Domain/SaoPauloCalendarTests.cs`

**Interfaces:**
- Consumes: BCL `TimeZoneInfo`, `DateOnly`, `DateTimeOffset`.
- Produces: `SaoPauloCalendar.TimeZone`, `GetLocalDate`, `StartOfDayUtc`, `EndOfDayExclusiveUtc`, `StartOfMonthUtc`, `EndOfMonthExclusiveUtc`, `StartOfYearUtc`, `EndOfYearExclusiveUtc` (assinaturas na §3.1).

- [ ] **Step 1: Escrever os testes que falham**

  Criar `SaoPauloCalendarTests` com:
  - `GetLocalDate_ShouldReturnPreviousLocalDate_WhenUtcIsBeforeMidnight`: `2026-10-29T02:30:00Z` → `2026-10-28`.
  - `StartOfDayUtc_ShouldReturn0300Z_ForRegularDay`: `2026-10-29` → `2026-10-29T03:00:00Z`.
  - `StartOfDayUtc_ShouldReturnFirstValidInstant_OnDstStart`: `2018-11-04` → `2018-11-04T03:00:00Z`.
  - `EndOfDayExclusiveUtc_ShouldReturn0200Z_OnDstStart`: `2018-11-04` → `2018-11-05T02:00:00Z`.
  - `StartOfDayUtc_ShouldUseDstOffset_On2019_02_16`: `2019-02-16` → `2019-02-16T02:00:00Z`.
  - `EndOfDayExclusiveUtc_ShouldReturnNextDayStart_OnDstEnd`: `2019-02-16` → `2019-02-17T02:00:00Z` (24 horas; a hora repetida pertence ao dia seguinte).
  - `StartOfDayUtc_ShouldChooseEarliestInstant_WhenMidnightIsAmbiguous`: `2019-02-17` → `2019-02-17T02:00:00Z`.
  - `AdjacentBoundaries_ShouldBeContiguous_OnDstEnd`: `EndOfDayExclusiveUtc(2019-02-16) == StartOfDayUtc(2019-02-17)`.
  - `StartOfMonthUtc_ShouldReturnDecemberFirst`: `(2026, 12)` → `2026-12-01T03:00:00Z`.
  - `EndOfMonthExclusiveUtc_ShouldCrossYear`: `(2026, 12)` → `2027-01-01T03:00:00Z`.
  - `StartOfYearUtc_ShouldReturnJanuaryFirst`: `2026` → `2026-01-01T03:00:00Z`.
  - `EndOfYearExclusiveUtc_ShouldReturnNextJanuaryFirst`: `2026` → `2027-01-01T03:00:00Z`.
  - `AllReturnedValues_ShouldBeUtcKind`.

- [ ] **Step 2: Rodar e confirmar a falha**

  ```powershell
  dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~SaoPauloCalendar"
  ```

  Esperado: falha de compilação (`SaoPauloCalendar` não existe).

- [ ] **Step 3: Implementar o helper e delegar o calculator**

  Implementar `SaoPauloCalendar` conforme §3.1. No `StoreOpeningHoursCalculator`, substituir o resolvedor privado por `SaoPauloCalendar.TimeZone` e fazer `GetSaoPauloDate` delegar a `SaoPauloCalendar.GetLocalDate`. Não mudar as assinaturas públicas nem o cálculo de turnos. Validar que o ambiente de teste (Windows/ICU) resolve as transições históricas de 2018/2019; não adicionar pacote de tzdata.

- [ ] **Step 4: Rodar o gate**

  ```powershell
  dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~SaoPauloCalendar|FullyQualifiedName~StoreOpeningHoursCalculator"
  ```

  Esperado: PASS nos casos novos e nos testes existentes de `StoreOpeningHoursCalculator`.

---

### Task 2: Resolvedor de período

**Files:**
- Create: `backend/src/Urbeat.Application/Analytics/AnalyticsGranularity.cs`
- Create: `backend/src/Urbeat.Application/Analytics/AnalyticsRange.cs`
- Create: `backend/src/Urbeat.Application/Analytics/AnalyticsRangeResolver.cs`
- Create: `backend/src/Urbeat.Application/DTOs/StoreAnalyticsQueryDto.cs`
- Test: `backend/tests/Urbeat.UnitTests/Application/Analytics/AnalyticsRangeResolverTests.cs`

**Interfaces:**
- Consumes: `SaoPauloCalendar` (Task 1), `StoreAnalyticsQueryDto`.
- Produces: `AnalyticsRangeResolver.Resolve(query, todayLocal)` → `AnalyticsRangeResolution` com `Range` ou `Errors` (assinaturas na §3.2).

- [ ] **Step 1: Escrever os testes que falham**

  Casos mínimos:
  1. `day` âncora `2026-10-29` → `[2026-10-29T03:00Z, 2026-10-30T03:00Z)`.
  2. `week` outubro/2026 blocos 1–4 → inícios `01, 08, 15, 22` e fins `08, 15, 22, 29` (03:00Z).
  3. `week` bloco 5 outubro/2026 → `[2026-10-29T03:00Z, 2026-11-01T03:00Z)`.
  4. `week` bloco 5 fevereiro/2026 (28 dias) → erro `weekBlock` `"O mês não possui o bloco 5."`.
  5. `week` bloco 5 fevereiro/2028 (bissexto) → `[2028-02-29T03:00Z, 2028-03-01T03:00Z)`.
  6. `month` dezembro/2026 → `[2026-12-01T03:00Z, 2027-01-01T03:00Z)`.
  7. `year` 2026 → `[2026-01-01T03:00Z, 2027-01-01T03:00Z)`.
  8. `day` `2018-11-04` → `[2018-11-04T03:00Z, 2018-11-05T02:00Z)`.
  9. `day` `2019-02-16` → início `2019-02-16T02:00Z`.
  10. `weekBlock` omitido com âncora `2026-10-29` → 5; âncora `2026-10-28` → 4; âncora `2026-10-31` → 5.
  11. Âncora de `week` normalizada para o início do bloco: `2026-10-30` + bloco 5 → `AnchorDate == 2026-10-29`.
  12. Âncora de `month` normalizada para o dia 1; de `year` para 1º de janeiro.
  13. `period` inválido → erro `period` `"Período inválido."`; `anchor` inválido → erro `anchor` `"Data inválida."`.
  14. `period=day&weekBlock=9` → sem erro e `WeekBlock == null`; `period=week&weekBlock=0` → erro `weekBlock` `"Bloco de semana inválido."`.
  15. `period` vazio/ausente → `Day`; `anchor` vazio/ausente → `todayLocal`.
  16. `ChartYear` é o ano da âncora canônica em todos os períodos.

- [ ] **Step 2: Rodar e confirmar a falha**

  ```powershell
  dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~AnalyticsRangeResolver"
  ```

  Esperado: falha de compilação.

- [ ] **Step 3: Implementar o resolver**

  Implementar a ordem de validação e a normalização da §3.2. Não acessar banco, `DateTime.Now`, `TimeZoneInfo.Local` ou configuração. Usar apenas `todayLocal` e `SaoPauloCalendar`.

- [ ] **Step 4: Rodar o gate**

  ```powershell
  dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~AnalyticsRangeResolver"
  ```

  Esperado: PASS em todos os casos.

---

### Task 3: DTOs e contrato JSON

**Files:**
- Create: `backend/src/Urbeat.Application/DTOs/StoreAnalyticsResponseDto.cs`
- Create: `backend/src/Urbeat.Application/DTOs/StoreSalesQueryDto.cs`
- Create: `backend/src/Urbeat.Application/DTOs/PagedStoreSalesResponseDto.cs`
- Test: `backend/tests/Urbeat.UnitTests/Application/Dtos/StoreAnalyticsDtoSerializationTests.cs`

**Interfaces:**
- Consumes: `StoreAnalyticsQueryDto` (Task 2), `PaymentMethod`.
- Produces: DTOs completos da §3.2, com nomes de propriedades que serializam em camelCase.

- [ ] **Step 1: Escrever o teste de contrato**

  Com `new JsonSerializerOptions(JsonSerializerDefaults.Web)`, serializar uma resposta mínima e verificar:
  - chaves `period`, `anchorDate`, `weekBlock`, `rangeStartUtc`, `rangeEndUtcExclusive`, `timeZone`, `chartYear`, `kpis`, `products`, `payments`, `months`, `availableYears`;
  - `anchorDate` como `"2026-10-29"` (sem hora);
  - `rangeStartUtc` terminando em `Z`, com `DateTimeKind.Utc`;
  - `payments[0].paymentMethod` como número (`1`), não string;
  - `sales[].customerName` aceita `null`;
  - `items[].variationName` aceita `null`;
  - `totals.salesTotal` e `totals.deliveryFees` como números decimais;
  - desserialização de volta para o DTO preserva os valores.

- [ ] **Step 2: Rodar e confirmar a falha**

  ```powershell
  dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~StoreAnalyticsDtoSerialization"
  ```

  Esperado: falha de compilação.

- [ ] **Step 3: Criar os DTOs**

  Seguir a tabela da §3.2. Não adicionar `JsonStringEnumConverter` global. Não incluir campos de PII além de `CustomerName`. Inicializar coleções com `[]`.

- [ ] **Step 4: Rodar o gate**

  ```powershell
  dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~StoreAnalyticsDtoSerialization"
  ```

  Esperado: PASS.

---

### Task 4: Analytics no `OrderReportService`

**Files:**
- Modify: `backend/src/Urbeat.Application/Interfaces/IOrderReportService.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Services/OrderReportService.cs`
- Test: `backend/tests/Urbeat.UnitTests/Infrastructure/Services/OrderReportServiceTests.cs`

**Interfaces:**
- Consumes: `AnalyticsRange`, `SaoPauloCalendar`, `ApplicationDbContext`, `TimeProvider`.
- Produces: `GetStoreAnalyticsAsync(...)` com `StoreAnalyticsResponseDto`; construtor `OrderReportService(db, timeProvider)`.

- [ ] **Step 1: Ajustar o teste existente e escrever os casos que falham**

  Trocar `new OrderReportService(_db)` por `new OrderReportService(_db, _timeProvider)`; o teste atual de `GetStoreSimpleReportAsync` deve continuar passando. Adicionar um fake de `TimeProvider` (subclasse que sobrescreve `GetUtcNow`) com padrão `2026-10-29T12:00:00Z`.

  Casos:
  1. Só agrega lojas do `OwnerUserId`; loja de outro lojista não entra.
  2. Só `Delivered`; `PendingPayment`, `Cancelled`, `Received`, `Preparing`, `Ready`, `OnDelivery` não entram.
  3. Limite inclusivo/exclusivo: pedido em `StartUtc` entra; em `EndUtcExclusive` não entra.
  4. `Revenue` inclui `DeliveryFee`; `DeliveryFees` é a soma separada.
  5. `AverageTicket = Revenue / DeliveredOrders`, `AwayFromZero`, e `0` sem pedidos.
  6. Pagamentos: contagem/receita por método; métodos ausentes não aparecem; ordenação por receita desc, enum asc.
  7. Produtos: mesmo nome soma; `Trim()` une `" X-Burger "` e `"X-Burger"`; caixa diferente permanece separada; nome vazio/nulo gera grupo com `ProductName == ""` (a UI exibe "Item sem nome") sem perder quantidade; itens de pedidos não entregues não entram.
  8. Produtos ordenados por quantidade desc, receita desc, nome ordinal asc.
  9. Série mensal: 12 itens e zero-fill; `2026-12-31T23:30:00-03:00` (`2027-01-01T02:30Z`) cai em dezembro/2026; `2026-01-01T02:30Z` (31/12/2025 23:30 local) não entra em 2026.
  10. Mais de 100 pedidos entregues são todos contados, somados e ranqueados (sem limite de 100).
  11. `AvailableYears` inclui o ano corrente, é decrescente e cobre o ano local do pedido mais antigo.
  12. Lojista sem loja → resposta zerada, `months` com 12 zeros, `availableYears == [currentYear]`, sem exceção.
  13. `TimeProvider` controla o ano corrente; `DateTime.Now` não é usado.

- [ ] **Step 2: Rodar e confirmar a falha**

  ```powershell
  dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~OrderReportService"
  ```

  Esperado: falha de compilação por método/construtor ausente.

- [ ] **Step 3: Implementar as consultas da analytics**

  Adicionar o método à interface e implementar conforme §3.3. Regras obrigatórias:
  - `storeIds` materializado; lista vazia → resposta zerada.
  - KPIs com `Sum((decimal?)...) ?? 0m`.
  - `AverageTicket` só com `DeliveredOrders > 0`.
  - Produtos: materializar grupos e ordenar em memória com `StringComparer.Ordinal`.
  - Meses: 12 consultas independentes, cada uma com limites locais; zero-fill.
  - Anos: `Min((DateTime?)CreatedAtUtc)` de qualquer status; converter para ano local; `minYear..currentYear` decrescente; se vazio ou `minYear > currentYear`, `[currentYear]`.
  - `Period` da resposta em minúsculas; `TimeZone` literal.

- [ ] **Step 4: Rodar o gate**

  ```powershell
  dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~OrderReportService"
  ```

  Esperado: PASS em todos os casos, incluindo o teste legado de `GetStoreSimpleReportAsync`.

---

### Task 5: Grade paginada no `OrderReportService`

**Files:**
- Modify: `backend/src/Urbeat.Application/Interfaces/IOrderReportService.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Services/OrderReportService.cs`
- Test: `backend/tests/Urbeat.UnitTests/Infrastructure/Services/OrderReportServiceTests.cs`

**Interfaces:**
- Consumes: `AnalyticsRange`, `ApplicationDbContext`.
- Produces: `GetStoreSalesAsync(...)` com `PagedStoreSalesResponseDto`.

- [ ] **Step 1: Escrever os casos que falham**

  1. `totals` são de todos os pedidos filtrados: com 3 páginas, os valores são idênticos em qualquer `page`.
  2. `totalItems` é o total filtrado; `totalPages = ceil(totalItems / pageSize)`; página além do fim retorna `sales: []` mantendo totais.
  3. Paginação estável: vários pedidos com o mesmo `CreatedAtUtc` não duplicam nem omitem entre páginas 1 e 2 (`CreatedAtUtc DESC, Id DESC`).
  4. Itens somente da página: página 2 não contém item da página 1 nem de outro lojista.
  5. Identidade: `Σ(items.totalPrice) == sale.subtotal` e `sale.subtotal + sale.deliveryFee == sale.total`; incluir frete grátis (`deliveryFee = 0`).
  6. PII: `customerName` é o claim `FullName`; ausente → `null`; claim duplicado não lança; telefone/e-mail/endereço/observações não existem no DTO.
  7. Só `Delivered`: `PendingPayment`, `Cancelled` e em andamento não entram; pedido em `StartUtc` entra e em `EndUtcExclusive` não entra.
  8. Isolamento: loja de outro `OwnerUserId` não entra em linhas, itens nem totais.
  9. Normalização: `page < 1 → 1`; `pageSize < 1 → 20`; `pageSize > 100 → 100`.
  10. Consistência com a analytics para o mesmo range: `totalItems == deliveredOrders`, `salesTotal == revenue`, `deliveryFees == deliveryFees`.
  11. Sem pedidos → `sales: []`, `totalItems = 0`, `totalPages = 0`, totais zero.
  12. Itens ordenados por `ProductName, Id`; pedido sem itens retorna `items: []`.

- [ ] **Step 2: Rodar e confirmar a falha**

  ```powershell
  dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~OrderReportService"
  ```

  Esperado: falha de compilação por método ausente.

- [ ] **Step 3: Implementar as consultas da grade**

  Adicionar o método à interface e implementar conforme §3.3. Regras obrigatórias:
  - Normalizar `page`/`pageSize` antes de consultar.
  - Consulta 1 sempre executa (quando há loja) para `totalItems`/`totals`.
  - Se `page > totalPages`, não executar página/itens/nomes.
  - Se a página vier vazia, não executar itens/nomes.
  - Nomes agrupados por `UserId` e primeira claim determinística.
  - `CreatedAtUtc` mapeado com `Kind == Utc`.
  - Itens agrupados por `OrderId`; pedido sem itens recebe `[]`.

- [ ] **Step 4: Rodar o gate**

  ```powershell
  dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~OrderReportService"
  ```

  Esperado: PASS.

---

### Task 6: Endpoints no `OrdersController` e testes de integração

**Files:**
- Modify: `backend/src/Urbeat.WebApi/Controllers/OrdersController.cs`
- Create: `backend/tests/Urbeat.IntegrationTests/Api/StoreAnalyticsFlowTests.cs`
- Create: `backend/tests/Urbeat.IntegrationTests/Api/StoreSalesFlowTests.cs`
- Create: `backend/tests/Urbeat.IntegrationTests/Infrastructure/AnalyticsTestData.cs`

**Interfaces:**
- Consumes: `IOrderReportService`, `AnalyticsRangeResolver`, `SaoPauloCalendar`, `TimeProvider`.
- Produces: rotas `GET /api/orders/store/analytics` e `GET /api/orders/store/sales` com os contratos da §2.

- [ ] **Step 1: Escrever os testes de integração que falham**

  Usar `TestWebApplicationFactory` e um helper `AnalyticsTestData` que:
  - registra/loga um lojista e cria a loja via API (reaproveitando o padrão de `OrdersFlowTests`);
  - cria cliente/claim `FullName` diretamente no `ApplicationDbContext` quando o nome não precisa de login;
  - cria `Order`/`OrderItem` com `CreatedAtUtc` controlado (reflection em `BaseEntity.CreatedAtUtc`, como `OrderServiceTests`);
  - gera códigos/slugs/e-mails únicos por teste.

  `StoreAnalyticsFlowTests`:
  1. Sem token → `401`.
  2. Token de cliente → `403`.
  3. Token de lojista → `200` e desserialização em `StoreAnalyticsResponseDto`.
  4. `period=quarter`, `anchor=2026-02-30`, `period=week&weekBlock=5&anchor=2026-02-10` → `400` e chaves corretas.
  5. Dois lojistas com pedidos entregues; cada um vê somente os próprios números.
  6. Pedido entregue no intervalo entra; fora do intervalo e cancelado não entram.
  7. `rangeStartUtc`/`rangeEndUtcExclusive` coerentes com `day`, `month`, `year` e bloco 5.
  8. Sem loja → `200` com KPIs zero, `months` com 12 itens e `availableYears == [anoCorrente]`.

  `StoreSalesFlowTests`:
  1. Sem token → `401`; cliente → `403`.
  2. Lojista → `200` com `PagedStoreSalesResponseDto`, `sales`, `totals` e paginação.
  3. JSON bruto não contém `customerPhoneNumber`, `addressSummary`, `itemsSummary`, `notes`, `deliveryPin`, `deliveryRouteId`, `customerUserId`, `customerEmail`; contém `customerName`.
  4. `page=2` retorna somente pedidos/itens da página 2.
  5. Totais e `totalItems` iguais em `page=1` e `page=2`.
  6. Consistência com `/analytics` para o mesmo filtro.
  7. Isolamento entre lojistas.
  8. Parâmetros inválidos de período → `400`; `page=0&pageSize=0` normaliza para `1/20`; `pageSize=101` → `100`.
  9. 105 pedidos entregues: `totalItems = 105`, `totalPages = 6`, página 1 com 20 linhas, totais somam todos.

- [ ] **Step 2: Rodar e confirmar a falha**

  ```powershell
  dotnet test backend/tests/Urbeat.IntegrationTests --filter "FullyQualifiedName~StoreAnalyticsFlowTests|FullyQualifiedName~StoreSalesFlowTests"
  ```

  Esperado: `404`/falha de desserialização por endpoints ausentes.

- [ ] **Step 3: Implementar as actions**

  Injetar `TimeProvider` no construtor. Adicionar as actions com `[FromQuery]`, `SellerOnly`, `ProducesResponseType` 200/400 e o fluxo da §3.4. Não alterar as actions existentes. Não logar `customerName` nem payloads de pedido.

- [ ] **Step 4: Rodar o gate**

  ```powershell
  dotnet build backend/Urbeat.sln
  dotnet test backend/tests/Urbeat.IntegrationTests --filter "FullyQualifiedName~StoreAnalyticsFlowTests|FullyQualifiedName~StoreSalesFlowTests"
  ```

  Esperado: build sem warnings novos; PASS nos dois fluxos.

---

### Task 7: Modelos, serviço HTTP e helpers de data no frontend

**Files:**
- Modify: `frontend/src/app/shared/models/order.model.ts`
- Modify: `frontend/src/app/core/services/order.service.ts`
- Modify: `frontend/src/app/core/utils/sao-paulo-date.helper.ts`
- Test: `frontend/src/app/core/services/order.service.spec.ts`
- Test: `frontend/src/app/core/utils/sao-paulo-date.helper.spec.ts`

**Interfaces:**
- Consumes: contratos da §2.
- Produces: `StoreAnalyticsPeriod`, `StoreAnalytics`, `PagedStoreSales`, `getStoreAnalytics`, `getStoreSales`, helpers da §3.5.

- [ ] **Step 1: Escrever os testes que falham**

  `order.service.spec.ts`:
  - `getStoreAnalytics({ period: 'week', anchor: '2026-10-29', weekBlock: 5 })` → `GET /api/orders/store/analytics?period=week&anchor=2026-10-29&weekBlock=5`.
  - `getStoreAnalytics({ period: 'day', anchor: '2026-10-29' })` → sem `weekBlock`.
  - `getStoreSales({ period: 'month', anchor: '2026-10-01', page: 2, pageSize: 20 })` → `GET /api/orders/store/sales?period=month&anchor=2026-10-01&page=2&pageSize=20`.
  - `getStoreSales({ period: 'year', anchor: '2026-01-01', page: 1, pageSize: 20 })` → sem `weekBlock`.
  - Flush de payload mínimo e validação dos tipos.

  `sao-paulo-date.helper.spec.ts`:
  - `saoPauloTodayIso(new Date('2026-10-29T02:30:00Z'))` → `2026-10-28`.
  - `saoPauloTodayIso(new Date('2026-10-29T12:00:00Z'))` → `2026-10-29`.
  - `saoPauloTodayIso` com `jest.useFakeTimers().setSystemTime(...)`.
  - `formatSaoPauloIsoDate('2026-10-29')` → `29/10/2026`; não desloca o dia; entrada inválida → `''`.
  - `analyticsDaysInMonth(2026, 2)` → 28; `(2028, 2)` → 29.
  - `analyticsMaxWeekBlock(2026, 2)` → 4; `(2028, 2)` → 5; `(2026, 10)` → 5.
  - `analyticsWeekBlockForDay(2026, 10, 29)` → 5; `(2026, 2, 28)` → 4.
  - `analyticsWeekBlockStartIso('2026-10-29', 5)` → `2026-10-29`; `analyticsWeekBlockEndIso('2026-10-29', 5)` → `2026-10-31`.
  - `analyticsWeekBlockLabel(5, 2026, 10)` contém `29` e `31`.

- [ ] **Step 2: Rodar e confirmar a falha**

  ```powershell
  cd frontend
  npx jest --no-coverage src/app/core/services/order.service.spec.ts src/app/core/utils/sao-paulo-date.helper.spec.ts
  ```

  Esperado: falha de compilação/métodos ausentes.

- [ ] **Step 3: Implementar modelos, métodos e helpers**

  Adicionar interfaces sem alterar as existentes. Implementar `getStoreAnalytics`/`getStoreSales` com `URLSearchParams` e regra de omissão do `weekBlock`. Implementar os helpers com matemática de calendário pura e `Intl` com `timeZone: 'America/Sao_Paulo'` apenas onde há instante UTC; `formatSaoPauloIsoDate` não converte fuso.

- [ ] **Step 4: Rodar o gate**

  ```powershell
  cd frontend
  npx jest --no-coverage src/app/core/services/order.service.spec.ts src/app/core/utils/sao-paulo-date.helper.spec.ts
  ```

  Esperado: PASS.

---

### Task 8: Componentes de gráfico

**Files:**
- Create: `frontend/src/app/shared/components/analytics-donut-chart/analytics-donut-chart.component.{ts,html,scss,spec.ts}`
- Create: `frontend/src/app/shared/components/analytics-bar-chart/analytics-bar-chart.component.{ts,html,scss,spec.ts}`

**Interfaces:**
- Consumes: `DonutSlice`, `BarDatum` (definidos no próprio componente ou em arquivo de tipos adjacente).
- Produces: componentes standalone com os inputs da §3.5; sem serviço, sem HTTP, sem dependência externa.

- [ ] **Step 1: Escrever os testes que falham**

  Donut:
  - Renderiza uma `<ul>` com um item por fatia, contendo rótulo, quantidade e percentual.
  - `role="img"` com `aria-label` que inclui o total e o período.
  - Centro mostra `total` e o rótulo (`unidades`).
  - `total = 0` → mensagem de vazio, sem divisão por zero/`NaN`.
  - Cores vêm de uma rampa monocromática; "Outros" usa o tom neutro (classe/token verificável).
  - `prefers-reduced-motion` desativa transição (classe/estilo declarado; verificação visual no aceite).

  Bar:
  - Renderiza exatamente 12 barras.
  - Barra com `highlighted: true` recebe a classe de destaque; as demais não.
  - `<details>` contém tabela com 12 linhas, `<caption>` e cabeçalhos `scope="col"`.
  - `role="img"` com `aria-label` contendo total e melhor mês.
  - `total = 0` → barras zeradas, sem `NaN`; texto de apoio "Sem faturamento em {ano}." fica na página, não no componente.

- [ ] **Step 2: Rodar e confirmar a falha**

  ```powershell
  cd frontend
  npx jest --no-coverage src/app/shared/components/analytics-donut-chart src/app/shared/components/analytics-bar-chart
  ```

  Esperado: falha de compilação.

- [ ] **Step 3: Implementar os componentes**

  Usar SVG/CSS puro:
  - Donut: `viewBox="0 0 100 100"`, círculos com `stroke-dasharray`/`stroke-dashoffset`, sem `stroke-linecap` arredondado (para não distorcer percentuais). Paleta fixa de 6 tons derivados de `--app-brand`, `--app-brand-dark`, `--app-brown`, `--app-muted-warm`; "Outros" em neutro. Não usar `color-mix` se o WebView alvo não garantir suporte; preferir hex/rgba declarados.
  - Bar: container flex/grid com 12 colunas; altura percentual calculada a partir do máximo; valor zero recebe altura mínima visual, mas o dado acessível é o texto/tabela.
  - Não usar `innerHTML`; não usar bibliotecas.
  - `aria-hidden="true"` na camada visual quando o wrapper já tem `role="img"` e a legenda/tabela é o conteúdo acessível.

- [ ] **Step 4: Rodar o gate**

  ```powershell
  cd frontend
  npx jest --no-coverage src/app/shared/components/analytics-donut-chart src/app/shared/components/analytics-bar-chart
  ```

  Esperado: PASS.

---

### Task 9: Filtro de período

**Files:**
- Create: `frontend/src/app/features/seller-dashboard/components/dashboard-period-filter/dashboard-period-filter.component.{ts,html,scss,spec.ts}`

**Interfaces:**
- Consumes: helpers da Task 7.
- Produces: componente standalone com inputs/outputs da §3.5; emite intenção, não executa HTTP.

- [ ] **Step 1: Escrever os testes que falham**

  1. Renderiza os quatro botões `Dia`, `Semana`, `Mês`, `Ano`; o ativo tem `aria-pressed="true"`; o grupo tem `role="group"` e `aria-label="Período do dashboard"`.
  2. `period=day` renderiza `<input type="date">` com `max` = `todayIso` e `<label for>` visível; não renderiza selects de mês/ano nem blocos.
  3. `period=week` renderiza selects de mês/ano e blocos 1–5 para outubro/2026; `fevereiro/2026` (28 dias) tem 4 blocos; `fevereiro/2028` tem 5.
  4. Bloco selecionado tem `aria-pressed="true"` e rótulo acessível "Dias 29 a 31 de outubro de 2026".
  5. `period=month` renderiza selects de mês/ano, sem dia nem blocos.
  6. `period=year` renderiza apenas o select de ano com `availableYears` decrescente e o ano corrente incluído.
  7. Seletores de mês/ano não oferecem combinação futura quando o ano é o corrente; blocos do mês atual continuam disponíveis.
  8. Clicar em `Semana` emite `periodChange('week')`; mudar mês/ano emite a nova âncora canônica (`yyyy-MM-01`); clicar em bloco emite `weekBlockChange(n)`.
  9. Mudar o dia emite `anchorDateChange` com `yyyy-MM-dd`.
  10. Cada controle tem alvo mínimo de 44px por classe/estrutura (a altura real é validada visualmente).

- [ ] **Step 2: Rodar e confirmar a falha**

  ```powershell
  cd frontend
  npx jest --no-coverage src/app/features/seller-dashboard/components/dashboard-period-filter
  ```

  Esperado: falha de compilação.

- [ ] **Step 3: Implementar o componente**

  Usar `input()`/`output()` e `computed()` somente para opções/estado de exibição. Derivar dias do mês e blocos com os helpers da Task 7. Usar `select` nativos (nunca `input type="month"`). Não usar `.segmented` (evita o override global com `!important` do shell); usar classe própria com `min-height: 44px`, foco visível em `--app-brand` e `aria-pressed`.

- [ ] **Step 4: Rodar o gate**

  ```powershell
  cd frontend
  npx jest --no-coverage src/app/features/seller-dashboard/components/dashboard-period-filter
  ```

  Esperado: PASS.

---

### Task 10: Grade de vendas

**Files:**
- Create: `frontend/src/app/features/seller-dashboard/components/dashboard-sales-grid/dashboard-sales-grid.component.{ts,html,scss,spec.ts}`

**Interfaces:**
- Consumes: `PagedStoreSales`, `StoreSaleRow`, `StoreSaleItem` (Task 7).
- Produces: componente standalone com inputs/outputs da §3.5.

- [ ] **Step 1: Escrever os testes que falham**

  1. Cabeçalho mostra `rangeLabel`, "Vendas do período" e "Pedidos entregues, pela data de criação.".
  2. Cinco colunas: Pedido, Cliente, Itens, Total da venda, Taxa de entrega; cliente nulo vira "Cliente".
  3. Rodapé mostra `totals.salesTotal` e `totals.deliveryFees` com os rótulos "Total das vendas (inclui taxas de entrega)" e "Taxas de entrega (já incluídas no total)", o texto "Total do período" e a nota "As taxas já estão dentro do total; não somar as duas colunas." e **nenhuma** linha "Total geral".
  4. Totais e rótulos permanecem iguais ao trocar de página (input `result` com outra `page`, mesmo `totals`).
  5. Botão "Ver itens" tem `aria-expanded`/`aria-controls`, 44px; expandir mostra `quantidade × unitário = total do item`, subtotal, taxa, a igualdade `subtotal + taxa = total` e o texto incondicional sobre adicionais/opções; recolher aplica `hidden`/remove a região e mantém o foco no botão.
  6. Clique em "Ver itens" emite `toggleItems(orderId)`; nenhuma requisição é feita pelo componente.
  7. Paginação: "Página 1 de 3" e "57 vendas entregues"; "Anterior" desabilitado na 1; "Próxima" desabilitado na última; emite `pageChange(2)`.
  8. Vazio: "Nenhuma venda entregue neste período.", rodapé com `R$ 0,00`, paginação oculta.
  9. Erro: banner "Não foi possível carregar as vendas." + "Tentar novamente" emite `retry`, sem sumir com o conteúdo anterior.
  10. Página além do fim: "Esta página não existe mais." + "Ir para a última página" emite `pageChange(totalPages)`.
  11. Mobile: cada linha contém células com `data-label` e o botão de itens mantém a classe de 44px; a estrutura permite o layout de cards por CSS.
  12. `loading && !result`: skeleton de 5 linhas, `aria-busy="true"`, rodapé oculto e botões de página desabilitados.
  13. `loading && result`: corpo com linha de carregamento, rodapé e totais anteriores preservados, anúncio "Carregando página {n}" em `aria-live="polite"`.

- [ ] **Step 2: Rodar e confirmar a falha**

  ```powershell
  cd frontend
  npx jest --no-coverage src/app/features/seller-dashboard/components/dashboard-sales-grid
  ```

  Esperado: falha de compilação.

- [ ] **Step 3: Implementar a grade**

  Usar `<table>` com `<caption class="visually-hidden">`, `<th scope="col">` e `<th scope="row">` no código. O `<tfoot>` usa `scope="rowgroup"` (ou célula de resumo associada aos rótulos); o `aria-label` do valor de total inclui "inclui taxas de entrega". O detalhe é uma linha `<tr>` com `[hidden]`/`@if` e id `sale-items-{orderId}`. Formatar BRL com `Intl.NumberFormat('pt-BR')`. Não recalcular totais; usar apenas `result.totals`. Banner de erro com `role="alert"`; status de carregamento com `role="status"`; skeleton com `aria-hidden="true"`. Não usar `.seller-table` sem necessidade; se usar, sobrescrever com seletor mais específico para o layout mobile. Paginação em `<nav aria-label="Paginação das vendas">` com `aria-live="polite"`.

- [ ] **Step 4: Rodar o gate**

  ```powershell
  cd frontend
  npx jest --no-coverage src/app/features/seller-dashboard/components/dashboard-sales-grid
  ```

  Esperado: PASS.

---

### Task 11: Página do dashboard

**Files:**
- Modify: `frontend/src/app/features/seller-dashboard/seller-dashboard-page.component.ts`
- Modify: `frontend/src/app/features/seller-dashboard/seller-dashboard-page.component.html`
- Modify: `frontend/src/app/features/seller-dashboard/seller-dashboard-page.component.scss`
- Modify: `frontend/src/app/features/seller-dashboard/seller-dashboard-page.component.spec.ts`

**Interfaces:**
- Consumes: `OrderService` (Task 7), `app-dashboard-period-filter` (Task 9), `app-dashboard-sales-grid` (Task 10), `app-analytics-donut-chart`/`app-analytics-bar-chart` (Task 8), `SellerShellFacade`, `SellerPrintingService`, `SubscriptionService`.
- Produces: dashboard operacional com o fluxo da §9.3.

- [ ] **Step 1: Reescrever os testes da página (falhando)**

  Mockar `OrderService` com `getStoreAnalytics`, `getStoreSales`, `getStoreOrders`. Usar `jest.useFakeTimers().setSystemTime(new Date('2026-10-29T12:00:00Z'))` antes de criar o componente.

  Casos:
  1. Estado inicial: uma chamada de analytics com `{ period: 'day', anchor: '2026-10-29' }`; uma de sales com `{ period: 'day', anchor: '2026-10-29', page: 1, pageSize: 20 }`; uma de `/store` com o intervalo do dia e `endDateUtc` subtraído 1ms.
  2. KPIs: 3 cards; "Pedidos entregues", "Faturamento", "Ticket médio"; valores formatados em BRL; `R$ 0,00` no vazio; sem percentuais fixos.
  3. Trocar para `Semana` envia bloco que contém a data e âncora no início do bloco; `Mês` envia dia 1; `Ano` envia 1º de janeiro.
  4. Trocar de mês mantém o bloco válido ou usa o último; fevereiro não bissexto tem 4 blocos.
  5. Roscas: top 5 + "Outros"; legenda com quantidade/percentual; centro com total de unidades; nota "Nomes conforme registrados no pedido."; tabela de ranking completo com `<caption class="visually-hidden">`, região rolável `tabindex="0"`, `role="region"` e nome acessível.
  6. Barras: 12 colunas; mês destacado quando o período não é `year`; `details` "Ver dados do ano" com tabela real de 12 linhas; textos "Total do ano" e "Melhor mês" (ou "Sem faturamento em {ano}." com zeros).
  7. Pagamentos: percentuais somam 100 (maior resto no maior grupo); valores por forma.
  8. `rangeLabel`: `29/10/2026` em `day`; `29/10 a 31/10/2026` em `week`; `Outubro de 2026` em `month`; `2026` em `year`.
  9. `ordersCount` recebe `totalItems` de `/store`; lista de andamento filtra `Preparing/Ready/OnDelivery`; falha de `/store` não zera o badge.
  10. Trocar de página chama **somente** `getStoreSales`; analytics e `/store` não recarregam.
  11. Resposta obsoleta de analytics é descartada; resposta obsoleta de sales também.
  12. Erro inicial de analytics → card "Não foi possível carregar o dashboard" + "Tentar novamente"; erro em refresh com dados → banner "Não foi possível atualizar os números..." e dados antigos; erro de sales mantém KPIs/gráficos; erro de andamento mantém o restante e oferece retry.
  13. Trocar filtro volta para a página 1, limpa expansão, limpa `sales` e mostra skeleton.
  14. Página além do fim refaz uma única vez para `totalPages`; com `totalPages = 0`, não entra em loop.
  15. Remoção: DOM/TS/SCSS não contêm "Atalhos rápidos", "Resumo por serviço" nem "Últimos pedidos"; date pill não existe.
  16. Aviso de mensalidade, aviso de impressora e "Pedidos em andamento" continuam.
  17. `aria-busy`, `aria-live` de atualização e foco visível nos controles.

- [ ] **Step 2: Rodar e confirmar a falha**

  ```powershell
  cd frontend
  npx jest --no-coverage src/app/features/seller-dashboard/seller-dashboard-page.component.spec.ts
  ```

  Esperado: falha de compilação/template.

- [ ] **Step 3: Implementar o estado e o fluxo**

  Signals e sequências da §9.2/§9.3:
  - `period`, `anchorDate`, `weekBlock`, `analytics`, `sales`, `salesPage`, `salesLoading`, `salesError`, `expandedOrders`, `inProgressOrders`, `ordersTotal`, `loading`, `refreshing`, `error`, `ordersError`, `staleWarning`, `lastUpdatedAt`, `filterSequence`, `salesSequence`.
  - `load({ silent, resetPage })` conforme §9.3; em falha de analytics não chamar sales/andamento e preservar dados antigos. O dashboard não chama mais `getStoreReport`.
  - `loadSales({ page, resetExpansion })` com descarte por `salesSequence`, poda de expansão e clamp único.
  - `loadInProgress(range)` com `/store`, `pageSize: 100`, fim inclusivo `rangeEndUtcExclusive - 1ms`, `shell.ordersCount.set(totalItems)`.
  - `onPeriodChange`, `onAnchorChange`, `onWeekBlockChange`, `onPageChange`, `retry`, `toggleItems`.
  - Normalização de filtro: `day → week` usa o bloco que contém o dia e ancora no início do bloco; `day → month` ancora no dia 1; `week/month → day` ancora no dia 1 do mês; qualquer → `year` ancora em 1º de janeiro; `year → day/week/month` ancora em 1º de janeiro. Ao trocar mês/ano, manter o bloco se existir; caso contrário, usar o último bloco válido. Nunca oferecer mês/ano futuro.
  - `rangeLabel` conforme §5.4: `dd/MM/yyyy` em `day`; `dd/MM a dd/MM/yyyy` em `week`; `Mês de yyyy` em `month`; `yyyy` em `year`, usando `formatSaoPauloIsoDate` e os limites do bloco.
  - `paymentRows`: percentual por quantidade com piso + distribuição do resíduo pelo maior resto (empate: contagem desc, enum asc); nunca recalcular receita.
  - `donutSlices`: top 5 por quantidade + `Outros`; `totalUnits` = soma das quantidades; produto com `productName` vazio é exibido como "Item sem nome" sem alterar o valor do backend.
  - `monthlyBars`: 12 itens; `highlighted` quando `period !== 'year'` e o mês é o da âncora.
  - `yearTotal`/`bestMonth`: soma/máximo apenas dos 12 valores mensais já agregados; `bestMonth = null` quando todos forem zero.
  - `yearOptions = analytics()?.availableYears ?? [currentYear]`, incluindo a âncora se ausente; `currentYear = Number(saoPauloTodayIso().slice(0, 4))`.
  - Remover `deliverySummary`, `pickupSummary`, `averageTicket`, `paymentMethodBreakdown`, `currentDateLabel`, `activePeriodLabel`, `recentOrders` e imports/computeds que ficarem sem uso (`FulfillmentType`, `paymentBadgeClass`, ícones não usados). Manter `statusLabel`/`statusBadgeClass` para a lista de andamento e `paymentLabel` para o card de pagamentos.
  - Não usar `DateTime.Now` para a âncora; a âncora é fixada no estado e só muda por ação do usuário.
  - Não calcular ticket nem somar pedidos/taxas/páginas no cliente. Percentuais de pagamento derivados das contagens e `yearTotal`/`bestMonth` derivados dos 12 meses já agregados são permitidos (ver I15).

- [ ] **Step 4: Implementar template e SCSS**

  Layout da §9.4:
  - Topbar com título/subtítulo neutros, filtro, "Atualizar" e sino; exibir `lastUpdatedAt` como "Atualizado às HH:mm" em `aria-live="polite"` quando houver dados.
  - Avisos de mensalidade/impressora.
  - 3 KPIs no card local do dashboard (não usar `app-metric-card`, cuja paleta é off-token; a spec permite "o card atual"): "Pedidos entregues" com "Pela data de criação do pedido."; "Faturamento" com "Inclui taxas de entrega" ou "R$ X em taxas de entrega" quando `deliveryFees > 0`; "Ticket médio" com "Faturamento ÷ pedidos entregues".
  - Linha de gráficos: produtos (rosca + tabela de ranking completo em região rolável) e meses (barras + `details`); cada card de gráfico mostra o `rangeLabel` no cabeçalho ou como texto de apoio.
  - Linha: formas de pagamento (barras horizontais + tabela) e "Pedidos em andamento" com contador no cabeçalho (`inProgressOrders().length`; se `ordersTotal >= 100`, exibir a nota "Entre os 100 pedidos mais recentes", sem prometer total exato).
  - Grade de vendas em largura total.
  - Remover `.quick-grid`, `.quick-card`, `.date-pill`, `.dashboard-table` e estilos órfãos.
  - Estados: skeleton com `aria-hidden="true"`; carregamento com `role="status"`/`aria-live="polite"`; erro com `role="alert"`; atualização silenciosa anuncia "Atualizando".
  - Usar `var(--app-*)`; seletor específico para o grid de 3 KPIs (o global `.seller-shell .metrics-grid` fixa 4 colunas). Não editar `seller-dashboard-bootstrap.scss`.
  - Mobile ≤767px: uma coluna, sem rolagem horizontal, `env(safe-area-inset-bottom)`, 44px.

- [ ] **Step 5: Rodar o gate**

  ```powershell
  cd frontend
  npx jest --no-coverage src/app/features/seller-dashboard/seller-dashboard-page.component.spec.ts
  ```

  Esperado: PASS (casos reescritos + descarte de resposta obsoleta preservado).

---

### Task 12: Validação final, aceite e compatibilidade

**Files:**
- Nenhum arquivo novo; somente verificação e correções pontuais se um gate falhar.

**Interfaces:**
- Consumes: todas as tarefas anteriores.
- Produces: evidência de aceite; nenhum commit/deploy.

- [ ] **Step 1: Build e testes backend**

  ```powershell
  dotnet build backend/Urbeat.sln

  dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~SaoPauloCalendar|FullyQualifiedName~AnalyticsRangeResolver|FullyQualifiedName~StoreAnalyticsDtoSerialization|FullyQualifiedName~OrderReportService|FullyQualifiedName~StoreOpeningHoursCalculator"

  dotnet test backend/tests/Urbeat.IntegrationTests --filter "FullyQualifiedName~StoreAnalyticsFlowTests|FullyQualifiedName~StoreSalesFlowTests"
  ```

  Esperado: build sem erros; todos os filtros PASS.

- [ ] **Step 2: Testes frontend focados**

  ```powershell
  cd frontend

  npx jest --no-coverage src/app/core/utils/sao-paulo-date.helper.spec.ts
  npx jest --no-coverage src/app/core/services/order.service.spec.ts
  npx jest --no-coverage src/app/shared/components/analytics-donut-chart
  npx jest --no-coverage src/app/shared/components/analytics-bar-chart
  npx jest --no-coverage src/app/features/seller-dashboard/components/dashboard-period-filter
  npx jest --no-coverage src/app/features/seller-dashboard/components/dashboard-sales-grid
  npx jest --no-coverage src/app/features/seller-dashboard/seller-dashboard-page.component.spec.ts
  ```

  Esperado: PASS em todos.

- [ ] **Step 3: Build de produção e branding**

  ```powershell
  cd frontend
  npx ng build --configuration production
  npx jest --no-coverage src/app/branding-consistency.spec.ts
  ```

  Esperado: build de produção sem erro; branding PASS (copy nova usa `urbeat` minúsculo; nenhuma ocorrência de `Urbeat`).

- [ ] **Step 4: Conferência de schema**

  ```powershell
  cd backend
  dotnet ef migrations has-pending-model-changes --startup-project src/Urbeat.WebApi --project src/Urbeat.Infrastructure
  ```

  Esperado: nenhuma alteração de modelo pendente; nenhuma migração criada.

- [ ] **Step 5: Checklist manual de aceite**

  - [ ] 360px: sem rolagem horizontal; filtros em duas linhas; gráficos legíveis; grade vira cards.
  - [ ] Grade ≤767px: cards com código, cliente, total e taxa; expansão em lista; rodapé em dois blocos empilhados; paginação full/half ≥44px; sem rolagem horizontal.
  - [ ] Nomes longos usam `text-overflow: ellipsis` com `title`/`aria-label` do nome completo; valores não quebram a linha.
  - [ ] 12 rótulos de mês com 3 letras; abaixo de 480px, rótulos alternados, mantendo todos no `<details>` e nos `aria-label`.
  - [ ] `padding-bottom` do dashboard respeita `env(safe-area-inset-bottom)`.
  - [ ] Copy em pt-BR, sentence case, marca `urbeat` minúscula.
  - [ ] Todos os botões de filtro/bloco, "Ver itens", paginação e retry têm alvo ≥44px.
  - [ ] Teclado: foco visível; `aria-expanded`/`aria-controls`; `details` abre por teclado.
  - [ ] Nenhuma informação depende de hover; `prefers-reduced-motion` desativa animações.
  - [ ] Gráficos usam rampa bordeaux + neutro para "Outros"; verde não é usado como categoria.
  - [ ] Rodapé nunca soma `salesTotal + deliveryFees`; a nota está visível.
  - [ ] `/store/report`, `/store`, `/store/customers` e `OrderSummaryResponseDto` continuam com o mesmo contrato.
  - [ ] Nenhuma migração, nenhuma dependência nova, nenhum deploy/commit.

---

## 7. Matriz de testes

### 7.1 Backend — unitários

| Arquivo | Casos obrigatórios |
| --- | --- |
| `SaoPauloCalendarTests.cs` | 13 casos da Task 1, incluindo DST 2018/2019, contiguidade e `Kind == Utc`. |
| `AnalyticsRangeResolverTests.cs` | 16 casos da Task 2, incluindo normalização de âncora e `weekBlock` ignorado fora de `week`. |
| `StoreAnalyticsDtoSerializationTests.cs` | camelCase, `DateOnly`, `Z`, enum numérico, nulos de PII. |
| `OrderReportServiceTests.cs` (analytics) | 13 casos da Task 4. |
| `OrderReportServiceTests.cs` (sales) | 12 casos da Task 5. |
| `StoreOpeningHoursCalculatorTests.cs` (existente) | Deve continuar PASS sem alteração de expectativa. |

### 7.2 Backend — integração

| Arquivo | Casos obrigatórios |
| --- | --- |
| `StoreAnalyticsFlowTests.cs` | 8 casos da Task 6. |
| `StoreSalesFlowTests.cs` | 9 casos da Task 6, incluindo 105 pedidos e varredura de PII no JSON. |

### 7.3 Frontend — Jest

| Arquivo | Casos obrigatórios |
| --- | --- |
| `sao-paulo-date.helper.spec.ts` | 9 casos da Task 7 + casos existentes intactos. |
| `order.service.spec.ts` | 5 casos novos + casos existentes intactos. |
| `analytics-donut-chart.component.spec.ts` | 6 casos da Task 8. |
| `analytics-bar-chart.component.spec.ts` | 5 casos da Task 8. |
| `dashboard-period-filter.component.spec.ts` | 10 casos da Task 9. |
| `dashboard-sales-grid.component.spec.ts` | 13 casos da Task 10. |
| `seller-dashboard-page.component.spec.ts` | 17 casos da Task 11. |

### 7.4 Casos que não podem ser removidos

- Testes existentes de `GET /api/orders/store/report` e `GET /api/orders/store`.
- Testes de `saoPauloPeriodRange` e `DashboardPeriod`.
- Teste de `StoreOpeningHoursCalculator`.
- Casos de descarte de resposta obsoleta da página (reescritos para os novos serviços, mas mantidos).

### 7.5 Cobertura de estados e acessibilidade

| Estado/comportamento | Onde é coberto |
| --- | --- |
| Skeleton de 3 KPIs + 2 cards na primeira carga | Task 11, caso 12 e teste de `aria-busy` |
| Atualização silenciosa mantém dados e anuncia "Atualizando" | Task 11, casos 11 e 17 |
| Erro sem dados + "Tentar novamente" | Task 11, caso 12 |
| Erro em refresh com dados antigos + banner | Task 11, caso 12 |
| Erro na lista de andamento + retry sem zerar badge | Task 11, casos 12 e 9 |
| Sem pedidos entregues: KPIs/rosca/ranking/pagamentos zerados; 12 barras zeradas; grade vazia | Task 11, casos 2, 5, 6, 7 e Task 10, caso 8 |
| Grade na primeira carga/troca de filtro: skeleton, rodapé oculto, botões desabilitados | Task 10, caso 12 e Task 11, caso 13 |
| Grade na troca de página: skeleton no corpo, rodapé anterior, "Carregando página n" | Task 10, casos 4 e 13 |
| Grade vazia: mensagem, rodapé `R$ 0,00`, paginação oculta | Task 10, caso 8 |
| Grade com erro: banner + retry mantendo página válida | Task 10, caso 9 |
| Página além do fim: mensagem + última página, no máximo uma requisição | Task 10, caso 10 e Task 11, caso 14 |
| Expansão por `orderId`, sem request; limpa ao trocar página/filtro | Task 10, casos 5/6 e Task 11, casos 10/13 |
| Sem pedidos nas listas: "Nenhum pedido no período." / "Nenhum pedido em andamento." | Task 11, casos 8 e 16 |
| Ano sem dados: seleção válida e barras zeradas | Task 11, caso 6 |
| Bloco 5 inexistente não é oferecido | Task 9, caso 3 |
| `role="status"`/`aria-live` de carregamento, `role="alert"` de erro, skeleton `aria-hidden` | Tasks 10/11 (estrutura) + checklist T12 |
| Foco visível, 44px e não depender de hover | Tasks 9/10/11 + checklist T12 |
| Layout 360px, rótulos de mês alternados, safe-area, cards mobile | Checklist manual T12 |

---

## 8. Comandos de referência

Todos os comandos partem de `C:\Projetos\urbeat`, salvo quando indicado `cd frontend`/`cd backend`. Em uma máquina limpa, rodar `cd frontend; npm ci` antes dos testes.

```powershell
# Build completo
dotnet build backend/Urbeat.sln

# Unitários focados
dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~SaoPauloCalendar"
dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~AnalyticsRangeResolver"
dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~StoreAnalyticsDtoSerialization"
dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~OrderReportService"
dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~StoreOpeningHoursCalculator"

# Integração
dotnet test backend/tests/Urbeat.IntegrationTests --filter "FullyQualifiedName~StoreAnalyticsFlowTests|FullyQualifiedName~StoreSalesFlowTests"

# Frontend
cd frontend
npx jest --no-coverage src/app/core/utils/sao-paulo-date.helper.spec.ts
npx jest --no-coverage src/app/core/services/order.service.spec.ts
npx jest --no-coverage src/app/shared/components/analytics-donut-chart
npx jest --no-coverage src/app/shared/components/analytics-bar-chart
npx jest --no-coverage src/app/features/seller-dashboard/components/dashboard-period-filter
npx jest --no-coverage src/app/features/seller-dashboard/components/dashboard-sales-grid
npx jest --no-coverage src/app/features/seller-dashboard/seller-dashboard-page.component.spec.ts
npx jest --no-coverage src/app/branding-consistency.spec.ts
npx ng build --configuration production

# Schema (nenhuma migração esperada)
cd ../backend
dotnet ef migrations has-pending-model-changes --startup-project src/Urbeat.WebApi --project src/Urbeat.Infrastructure
```

Não há lint, formatter, pre-commit hook, e2e ou CI no repositório; o aceite é o build de produção + testes focados + checklist manual.

---

## 9. Critérios de aceite

1. O dashboard exibe apenas: avisos, 3 KPIs, rosca de produtos, barras de 12 meses, formas de pagamento, pedidos em andamento e a grade "Vendas do período". "Atalhos rápidos", "Resumo por serviço" e "Últimos pedidos" não existem em TS/HTML/SCSS.
2. Filtros Dia/Semana/Mês/Ano funcionam; padrão é o dia local atual; blocos 1–7, 8–14, 15–21, 22–28, 29–fim; fevereiro não bissexto tem 4 blocos.
3. Seletores são contextuais, acessíveis, com alvos ≥44px e sem datas futuras em dia/mês/ano; blocos do mês atual continuam disponíveis.
4. KPIs usam somente `Delivered` no recorte; faturamento inclui taxas; ticket divide por pedidos entregues e arredonda `AwayFromZero`.
5. Rosca usa quantidade de itens agrupada por nome snapshot; renomeação divide o histórico e a UI declara isso.
6. Barras mostram os 12 meses do ano escolhido, com zeros e tabela acessível.
7. Pagamentos mostram quantidade, percentual (soma 100) e faturamento de todos os entregues do período.
8. Nenhuma agregação é limitada por paginação/100, comprovado por teste com mais de 100 pedidos.
9. Endpoints são `SellerOnly`, restritos ao `OwnerUserId`, com `200` e zeros sem dados.
10. Limites calculados em `America/Sao_Paulo` com `TimeZoneInfo`, início inclusivo e fim exclusivo; DST 2018/2019 coberto.
11. Estados de carregamento, vazio, erro inicial, erro em refresh, erro parcial de andamento e erro da grade estão implementados e testados.
12. Layout utilizável em 360px, sem rolagem horizontal, com foco visível e informação não dependente de cor/hover.
13. `GET /api/orders/store/report`, `GET /api/orders/store`, `GET /api/orders/store/customers`, `saoPauloPeriodRange` e o schema de `OrderItem` permanecem inalterados.
14. `dotnet build`, testes focados e `npx ng build --configuration production` passam; nenhuma migração pendente.
15. Grade mostra código, cliente, itens expansíveis, total e taxa; no máximo 20 linhas por página; itens somente da página; sem requisição por linha.
16. Rodapé soma `Total` e `DeliveryFee` de todos os registros filtrados, com rótulos explícitos e sem somar as duas colunas.
17. `totalItems`, `salesTotal` e `deliveryFees` coincidem com os KPIs para o mesmo filtro.
18. A grade expõe apenas `customerName` como PII.
19. Paginação determinística (`CreatedAtUtc DESC, Id DESC`), normalizada, com página além do fim tratada e sem duplicação/omissão em conjunto estável.
20. Divulgação de itens acessível, responsiva e com loading/vazio/erro/página além do fim.

---

## 10. Riscos e limitações

| Risco | Impacto | Mitigação |
| --- | --- | --- |
| 17 consultas da analytics + 5 por página da grade | Latência em banco lento | Consultas pequenas/indexadas; medir em homologação; consolidar a série mensal com Dapper/`AT TIME ZONE` só em tarefa futura. |
| Ano inteiro com muitos pedidos | CPU na agregação de produtos | Agregação no banco; não trafegar pedidos; índice existente. |
| Paginação por offset | Inserção concorrente desloca linhas | Ordem determinística + refresh; cursor/`asOfUtc` fora de escopo (Q15). |
| Lista de andamento limitada a 100 | Contador/lista incompletos em períodos longos | Contador dos 100 mais recentes + nota; não prometer total exato; não corrigir com paginação nesta tarefa. |
| DST ambíguo | Números errados em anos antigos | `TimeZoneInfo` + testes 2018/2019 + limites contíguos com o mesmo critério (início do dia seguinte). |
| Claim `FullName` duplicado | Exceção na montagem da página | Agrupar e escolher a primeira claim deterministicamente. |
| `availableYears` com histórico muito antigo | Select longo | Faixa contígua decrescente; avaliar anos distintos no futuro. |
| Rampa monocromática com muitas fatias | Dificuldade de distinção | Top 5 + "Outros"; legenda/tabela; cor nunca é o único canal. |
| Refresh concorrente | Resposta antiga sobrescreve atual | `filterSequence`/`salesSequence`; descarte de obsoletas. |
| Dupla contagem de taxa | Rodapé somar `Total` + `DeliveryFee` | Rótulos explícitos, nota visível, nenhuma linha de total geral, testes de contrato/UI. |
| PII na grade | Exposição de telefone/endereço | DTO próprio com apenas `customerName`; teste que rejeita chaves extras. |
| Tema do shell (`--dash-*`) | Paleta mista no dashboard | Componentes novos escopados em `--app-*`; não alterar o global; decisão visual registrada. |
| `endDateUtc` inclusivo em `/store` | Inclusão de pedido no limite | Subtrair 1ms no cliente; analytics/grade continuam autoritativas. |
| Build/test sem CI | Regressão silenciosa | Gates por tarefa + validação final da Task 12. |

---

## 11. Rollback

Como a mudança é aditiva no backend e restrita ao dashboard no frontend:

1. Restaurar `seller-dashboard-page.component.{ts,html,scss,spec.ts}` e remover a grade/filtro/gráficos.
2. Remover `getStoreAnalytics`/`getStoreSales` e as interfaces novas (opcional; não afetam outras telas).
3. Manter ou remover os dois endpoints e testes; não há migração, tabela ou dado persistido para reverter.

Nenhum deploy ou comando de banco é executado neste plano.

---

## 12. Rastreabilidade spec → tarefas

| Spec | Tarefas |
| --- | --- |
| §5 calendário/filtros | T1, T2, T9, T11 |
| §6 semântica dos indicadores | T2, T4, T5 |
| §7 contratos HTTP | T3, T6 |
| §8 backend | T1–T6 |
| §9 frontend | T7–T11 |
| §10 compatibilidade | T3, T6, T12 |
| §11 testes | T1–T12 (matriz §7) |
| §12 aceite | T12 |
| §13 riscos | §10 deste plano |
| §14 dúvidas | §0 deste plano (I1–I15) |
| §15 validação | §8 deste plano |
| §16 rollback | §11 deste plano |
