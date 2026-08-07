# Local Print Agent Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Construir um agente local de impressão para desktop que permita impressão automática de pedidos do Urbeat sem diálogo interativo do navegador.

**Architecture:** O MVP adiciona um serviço local .NET 9 rodando em `127.0.0.1` na máquina da loja, recebendo jobs de impressão e enviando-os para a impressora térmica configurada. O frontend seller detecta e usa esse agente como caminho preferencial no desktop, preservando Android Bluetooth, Wi-Fi ESC/POS e browser print como fallback conforme plataforma.

**Tech Stack:** .NET 9 Worker/Minimal API, frontend Angular 20 standalone + Ionic 8, ESC/POS payload já existente no frontend seller, Jest, `dotnet test`, `dotnet build`.

## Global Constraints

- A configuração de impressora continua sendo da loja atual do dashboard.
- POS-58 é a configuração operacional prioritária.
- Papel padrão operacional é `58mm`.
- POS-58 não possui guilhotina; `autoCut` deve permanecer coerente com isso.
- Android prioriza Bluetooth.
- iOS, Windows, Linux e macOS priorizam Wi-Fi.
- `browser-print` no desktop é fallback/manual, só sendo realmente automático em `kiosk` / `silent print`.
- Ao aceitar pedido, a impressão deve disparar automaticamente com a configuração atual, sem prompt de escolha.
- Não tocar no wizard `configurar-loja/*`.

---

### Task 1: Criar o projeto do agente local e o contrato de health/configuração

**Files:**
- Create: `print-agent/Urbeat.PrintAgent/Urbeat.PrintAgent.csproj`
- Create: `print-agent/Urbeat.PrintAgent/Program.cs`
- Create: `print-agent/Urbeat.PrintAgent/appsettings.json`
- Create: `print-agent/Urbeat.PrintAgent/Models/AgentPrinterConfig.cs`
- Create: `print-agent/Urbeat.PrintAgent/Models/HealthResponse.cs`
- Create: `print-agent/Urbeat.PrintAgent/Models/SaveConfigRequest.cs`
- Create: `print-agent/Urbeat.PrintAgent/Storage/AgentConfigStore.cs`
- Create: `print-agent/Urbeat.PrintAgent.Tests/Urbeat.PrintAgent.Tests.csproj`
- Create: `print-agent/Urbeat.PrintAgent.Tests/AgentConfigStoreTests.cs`

**Interfaces:**
- Consumes: sem dependências anteriores
- Produces:
  - `GET /health` -> `HealthResponse`
  - `GET /config` -> `AgentPrinterConfig`
  - `POST /config` -> salva `AgentPrinterConfig`

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using Urbeat.PrintAgent.Models;
using Urbeat.PrintAgent.Storage;

namespace Urbeat.PrintAgent.Tests;

public class AgentConfigStoreTests
{
    [Fact]
    public async Task SaveAsync_persists_and_reads_back_agent_config()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var store = new AgentConfigStore(tempFile);
            var config = new AgentPrinterConfig
            {
                PreferredMode = "local-agent",
                PreferredProfile = "pos-58",
                PrinterName = "POS-58 Balcao",
                PaperWidth = "58mm",
                AutoCut = false,
                LocalToken = "secret-token"
            };

            await store.SaveAsync(config, CancellationToken.None);
            var loaded = await store.LoadAsync(CancellationToken.None);

            loaded.Should().NotBeNull();
            loaded!.PrinterName.Should().Be("POS-58 Balcao");
            loaded.AutoCut.Should().BeFalse();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test print-agent/Urbeat.PrintAgent.Tests --filter "FullyQualifiedName~AgentConfigStoreTests"`
Expected: FAIL because `AgentConfigStore` and model classes do not exist yet.

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace Urbeat.PrintAgent.Models;

public sealed class AgentPrinterConfig
{
    public string PreferredMode { get; set; } = "local-agent";
    public string PreferredProfile { get; set; } = "pos-58";
    public string PrinterName { get; set; } = string.Empty;
    public string PaperWidth { get; set; } = "58mm";
    public bool AutoCut { get; set; }
    public string LocalToken { get; set; } = string.Empty;
}
```

```csharp
using System.Text.Json;
using Urbeat.PrintAgent.Models;

namespace Urbeat.PrintAgent.Storage;

public sealed class AgentConfigStore
{
    private readonly string _filePath;

    public AgentConfigStore(string filePath)
    {
        _filePath = filePath;
    }

    public async Task SaveAsync(AgentPrinterConfig config, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_filePath, json, cancellationToken);
    }

    public async Task<AgentPrinterConfig?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath)) return null;
        var json = await File.ReadAllTextAsync(_filePath, cancellationToken);
        return JsonSerializer.Deserialize<AgentPrinterConfig>(json);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test print-agent/Urbeat.PrintAgent.Tests --filter "FullyQualifiedName~AgentConfigStoreTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add print-agent/Urbeat.PrintAgent print-agent/Urbeat.PrintAgent.Tests
git commit -m "feat: scaffold local print agent"
```

### Task 2: Expor API local mínima do agente em loopback

**Files:**
- Modify: `print-agent/Urbeat.PrintAgent/Program.cs`
- Modify: `print-agent/Urbeat.PrintAgent/Models/HealthResponse.cs`
- Modify: `print-agent/Urbeat.PrintAgent/Models/SaveConfigRequest.cs`
- Create: `print-agent/Urbeat.PrintAgent.Tests/HealthEndpointTests.cs`

**Interfaces:**
- Consumes: `AgentConfigStore`, `AgentPrinterConfig`
- Produces:
  - `GET /health`
  - `GET /config`
  - `POST /config`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Urbeat.PrintAgent.Tests;

public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_health_returns_ok_with_loopback_health_payload()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("local-agent");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test print-agent/Urbeat.PrintAgent.Tests --filter "FullyQualifiedName~HealthEndpointTests"`
Expected: FAIL because there is no minimal API running yet.

- [ ] **Step 3: Write minimal implementation**

```csharp
using Urbeat.PrintAgent.Models;
using Urbeat.PrintAgent.Storage;

var builder = WebApplication.CreateBuilder(args);

var configFile = Path.Combine(AppContext.BaseDirectory, "agent-config.json");
builder.Services.AddSingleton(new AgentConfigStore(configFile));

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new HealthResponse
{
    Status = "ok",
    Mode = "local-agent",
    BoundAddress = "127.0.0.1"
}));

app.MapGet("/config", async (AgentConfigStore store, CancellationToken cancellationToken) =>
{
    var config = await store.LoadAsync(cancellationToken) ?? new AgentPrinterConfig();
    return Results.Ok(config);
});

app.MapPost("/config", async (SaveConfigRequest request, AgentConfigStore store, CancellationToken cancellationToken) =>
{
    var config = new AgentPrinterConfig
    {
        PreferredMode = request.PreferredMode,
        PreferredProfile = request.PreferredProfile,
        PrinterName = request.PrinterName,
        PaperWidth = request.PaperWidth,
        AutoCut = request.AutoCut,
        LocalToken = request.LocalToken
    };

    await store.SaveAsync(config, cancellationToken);
    return Results.Ok(config);
});

app.Run("http://127.0.0.1:43111");

public partial class Program;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test print-agent/Urbeat.PrintAgent.Tests --filter "FullyQualifiedName~HealthEndpointTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add print-agent/Urbeat.PrintAgent/Program.cs print-agent/Urbeat.PrintAgent.Tests/HealthEndpointTests.cs
git commit -m "feat: add local print agent api"
```

### Task 3: Modelar catálogo de impressoras locais e perfis POS-58/80

**Files:**
- Create: `print-agent/Urbeat.PrintAgent/Models/AgentPrinterDescriptor.cs`
- Create: `print-agent/Urbeat.PrintAgent/Services/ILocalPrinterDiscovery.cs`
- Create: `print-agent/Urbeat.PrintAgent/Services/LocalPrinterDiscovery.cs`
- Modify: `print-agent/Urbeat.PrintAgent/Program.cs`
- Create: `print-agent/Urbeat.PrintAgent.Tests/LocalPrinterDiscoveryTests.cs`

**Interfaces:**
- Consumes: agent local runtime
- Produces:
  - `GET /printers`
  - descriptors with `58mm`, `80mm`, `supportsAutoCut`, `platformRecommendation`

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using Urbeat.PrintAgent.Services;

namespace Urbeat.PrintAgent.Tests;

public class LocalPrinterDiscoveryTests
{
    [Fact]
    public void Build_profiles_prioritizes_pos_58_first_and_marks_no_auto_cut()
    {
        var service = new LocalPrinterDiscovery();

        var profiles = service.GetRecommendedProfiles();

        profiles[0].ProfileId.Should().Be("pos-58");
        profiles[0].PaperWidth.Should().Be("58mm");
        profiles[0].SupportsAutoCut.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test print-agent/Urbeat.PrintAgent.Tests --filter "FullyQualifiedName~LocalPrinterDiscoveryTests"`
Expected: FAIL because discovery service does not exist yet.

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace Urbeat.PrintAgent.Models;

public sealed class AgentPrinterDescriptor
{
    public string ProfileId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PaperWidth { get; set; } = "58mm";
    public bool SupportsAutoCut { get; set; }
    public string PreferredConnection { get; set; } = string.Empty;
}
```

```csharp
using Urbeat.PrintAgent.Models;

namespace Urbeat.PrintAgent.Services;

public sealed class LocalPrinterDiscovery : ILocalPrinterDiscovery
{
    public IReadOnlyList<AgentPrinterDescriptor> GetRecommendedProfiles() =>
    [
        new() { ProfileId = "pos-58", DisplayName = "POS-58", PaperWidth = "58mm", SupportsAutoCut = false, PreferredConnection = "android-bluetooth|wifi" },
        new() { ProfileId = "thermal-80", DisplayName = "Thermal 80", PaperWidth = "80mm", SupportsAutoCut = true, PreferredConnection = "wifi" },
    ];

    public Task<IReadOnlyList<string>> ListInstalledPrintersAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test print-agent/Urbeat.PrintAgent.Tests --filter "FullyQualifiedName~LocalPrinterDiscoveryTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add print-agent/Urbeat.PrintAgent/Services print-agent/Urbeat.PrintAgent.Tests/LocalPrinterDiscoveryTests.cs
git commit -m "feat: add local printer profiles"
```

### Task 4: Implementar endpoint local de teste e job de impressão

**Files:**
- Create: `print-agent/Urbeat.PrintAgent/Models/PrintOrderRequest.cs`
- Create: `print-agent/Urbeat.PrintAgent/Models/PrintTestRequest.cs`
- Create: `print-agent/Urbeat.PrintAgent/Services/IPrintJobService.cs`
- Create: `print-agent/Urbeat.PrintAgent/Services/PrintJobService.cs`
- Modify: `print-agent/Urbeat.PrintAgent/Program.cs`
- Create: `print-agent/Urbeat.PrintAgent.Tests/PrintJobServiceTests.cs`

**Interfaces:**
- Consumes: `AgentPrinterConfig`, `AgentPrinterDescriptor`
- Produces:
  - `POST /print/test`
  - `POST /print/order`

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using Urbeat.PrintAgent.Models;
using Urbeat.PrintAgent.Services;

namespace Urbeat.PrintAgent.Tests;

public class PrintJobServiceTests
{
    [Fact]
    public async Task Build_order_job_uses_pos_58_without_auto_cut_by_default()
    {
        var service = new PrintJobService();
        var request = new PrintOrderRequest
        {
            PrinterProfile = "pos-58",
            PaperWidth = "58mm",
            AutoCut = false,
            Order = new PrintOrderPayload { Code = "1024", Total = 25m, CreatedAtUtc = "2026-08-04T12:00:00Z" }
        };

        var job = await service.BuildOrderJobAsync(request, CancellationToken.None);

        job.ProfileId.Should().Be("pos-58");
        job.AutoCut.Should().BeFalse();
        job.RawText.Should().Contain("1024");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test print-agent/Urbeat.PrintAgent.Tests --filter "FullyQualifiedName~PrintJobServiceTests"`
Expected: FAIL because job service does not exist yet.

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace Urbeat.PrintAgent.Models;

public sealed class PrintOrderRequest
{
    public string PrinterProfile { get; set; } = "pos-58";
    public string PaperWidth { get; set; } = "58mm";
    public bool AutoCut { get; set; }
    public PrintOrderPayload Order { get; set; } = new();
}

public sealed class PrintOrderPayload
{
    public string Code { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string CreatedAtUtc { get; set; } = string.Empty;
}
```

```csharp
namespace Urbeat.PrintAgent.Services;

public sealed class PrintJobService : IPrintJobService
{
    public Task<PrintJobResult> BuildOrderJobAsync(PrintOrderRequest request, CancellationToken cancellationToken)
    {
        var rawText = $"PEDIDO {request.Order.Code}\nTOTAL {request.Order.Total:0.00}\nUTC {request.Order.CreatedAtUtc}";
        return Task.FromResult(new PrintJobResult
        {
            ProfileId = request.PrinterProfile,
            AutoCut = request.AutoCut,
            RawText = rawText
        });
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test print-agent/Urbeat.PrintAgent.Tests --filter "FullyQualifiedName~PrintJobServiceTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add print-agent/Urbeat.PrintAgent/Models print-agent/Urbeat.PrintAgent/Services print-agent/Urbeat.PrintAgent.Tests/PrintJobServiceTests.cs
git commit -m "feat: add local print job endpoints"
```

### Task 5: Integrar frontend seller com o agente local como modo desktop preferencial

**Files:**
- Modify: `frontend/src/app/features/seller-printing/seller-printing.models.ts`
- Modify: `frontend/src/app/features/seller-printing/seller-printing.service.ts`
- Modify: `frontend/src/app/features/seller-printing/seller-printing-page.component.ts`
- Modify: `frontend/src/app/features/seller-printing/seller-printing-page.component.html`
- Modify: `frontend/src/app/features/seller-printing/seller-printing-page.component.scss`
- Test: `frontend/src/app/features/seller-printing/seller-printing.service.spec.ts`
- Test: `frontend/src/app/features/seller-printing/seller-printing-page.component.spec.ts`

**Interfaces:**
- Consumes:
  - `PrintingConfig`
  - existing `/api/printer-config/store`
  - local agent `GET /health`, `GET /printers`, `POST /print/test`, `POST /print/order`
- Produces:
  - new preferred desktop mode `local-agent`
  - per-platform help text

- [ ] **Step 1: Write the failing test**

```typescript
it('prefers local-agent for desktop POS-58 setup and keeps autoCut disabled for 58mm', async () => {
  api.get.mockImplementation((url: string) => {
    if (url === '/api/printer-config/store') {
      return of({
        printerName: 'POS-58 Balcao',
        connectionType: 'local-agent',
        paperWidth: '58mm',
        autoCut: true,
        autoPrint: true,
      });
    }
    return of([]);
  });

  await service.loadStoreConfig();

  expect(service.config().connectionType).toBe('local-agent');
  expect(service.config().paperWidth).toBe('58mm');
  expect(service.config().autoCut).toBe(false);
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx jest --no-coverage src/app/features/seller-printing/seller-printing.service.spec.ts`
Expected: FAIL because `local-agent` is not supported yet and `58mm` normalization does not force `autoCut=false`.

- [ ] **Step 3: Write minimal implementation**

```typescript
export type PrinterConnectionType = 'android-bluetooth' | 'browser-print' | 'mock' | 'wifi' | 'local-agent';
```

```typescript
private normalizeConfig(config: PrintingConfig): PrintingConfig {
  const normalized = {
    ...DEFAULT_CONFIG,
    ...config,
    copies: Math.min(5, Math.max(1, config.copies || 1)),
    footerText: (config.footerText ?? '').slice(0, 120),
    savedMacAddress: config.savedMacAddress ?? '',
  };

  if (normalized.paperWidth === '58mm') {
    normalized.autoCut = false;
  }

  return normalized;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npx jest --no-coverage src/app/features/seller-printing/seller-printing.service.spec.ts`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add frontend/src/app/features/seller-printing
git commit -m "feat: add local agent desktop print mode"
```

### Task 6: Disparar impressão automática no aceite do pedido via configuração atual da loja

**Files:**
- Modify: `frontend/src/app/features/seller-orders/seller-orders-page.component.ts`
- Modify: `frontend/src/app/features/seller-printing/seller-printing.service.ts`
- Test: `frontend/src/app/features/seller-orders/seller-orders-page.component.spec.ts`

**Interfaces:**
- Consumes:
  - `SellerPrintingService.printAcceptedOrder(orderId: string): Promise<void>`
  - status transition `Received -> Preparing`
- Produces:
  - auto print no aceite sem prompt de escolha

- [ ] **Step 1: Write the failing test**

```typescript
it('prints automatically when accepting a received order', () => {
  printingService.printAcceptedOrder = jest.fn().mockResolvedValue(undefined);

  component.pendingAction.set({ order: buildOrder({ id: 'order-1' }), nextStatus: OrderStatus.Preparing, label: 'Aceitar pedido' });
  component.executeAdvance();

  expect(printingService.printAcceptedOrder).toHaveBeenCalledWith('order-1');
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx jest --no-coverage src/app/features/seller-orders/seller-orders-page.component.spec.ts`
Expected: FAIL because no print call happens on accept yet.

- [ ] **Step 3: Write minimal implementation**

```typescript
if (pending.nextStatus === OrderStatus.Preparing) {
  void this.printing.printAcceptedOrder(orderId);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npx jest --no-coverage src/app/features/seller-orders/seller-orders-page.component.spec.ts`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add frontend/src/app/features/seller-orders frontend/src/app/features/seller-printing/seller-printing.service.ts
git commit -m "feat: print automatically on order acceptance"
```

### Task 7: Documentar comportamento de plataforma e operação kiosk

**Files:**
- Modify: `frontend/src/app/features/seller-printing/seller-printing-page.component.html`
- Modify: `frontend/src/app/features/seller-printing/seller-printing-page.component.scss`
- Create: `Documentacao/FrontEnd/Impressao/local-print-agent.md`

**Interfaces:**
- Consumes: final UX decisions
- Produces: help text clear enough for setup and support

- [ ] **Step 1: Write the failing test**

```typescript
it('shows desktop browser print as manual unless kiosk is configured', () => {
  fixture.detectChanges();
  expect(fixture.nativeElement.textContent).toContain('kiosk');
  expect(fixture.nativeElement.textContent).toContain('manual');
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx jest --no-coverage src/app/features/seller-printing/seller-printing-page.component.spec.ts`
Expected: FAIL because kiosk/manual guidance is not explicit enough yet.

- [ ] **Step 3: Write minimal implementation**

```html
<div class="seller-inline-banner is-warning">
  <strong>Desktop com navegador</strong>
  <p>No Windows, Linux e macOS, a impressão pelo navegador é manual/interativa por padrão. Só trate como automática quando a máquina estiver em modo kiosk ou silent print.</p>
</div>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npx jest --no-coverage src/app/features/seller-printing/seller-printing-page.component.spec.ts`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add frontend/src/app/features/seller-printing Documentacao/FrontEnd/Impressao/local-print-agent.md
git commit -m "docs: clarify desktop print behavior"
```

### Task 8: Verification

**Files:**
- Verify: `print-agent/**`
- Verify: `frontend/src/app/features/seller-printing/**`
- Verify: `frontend/src/app/features/seller-orders/**`

**Interfaces:**
- Consumes: all previous tasks
- Produces: green build and tested print flow MVP

- [ ] **Step 1: Run backend/agent tests**

Run: `dotnet test print-agent/Urbeat.PrintAgent.Tests`
Expected: PASS

- [ ] **Step 2: Run frontend focused tests**

Run: `npx jest --no-coverage src/app/features/seller-printing/seller-printing.service.spec.ts src/app/features/seller-printing/seller-printing-page.component.spec.ts src/app/features/seller-orders/seller-orders-page.component.spec.ts src/app/core/utils/sao-paulo-date.helper.spec.ts`
Expected: PASS

- [ ] **Step 3: Run production build**

Run: `npx ng build --configuration production`
Expected: PASS

- [ ] **Step 4: Manual verification checklist**

Run/Check:
- configure `POS-58` first in the seller dashboard
- verify `58mm` + `autoCut = false`
- accept a `Received` order and confirm no printer chooser prompt appears in Android/local-agent/Wi-Fi paths
- confirm desktop browser mode shows kiosk/manual warning

Expected: all checks complete

- [ ] **Step 5: Commit**

```bash
git add print-agent frontend/src/app/features/seller-printing frontend/src/app/features/seller-orders Documentacao/FrontEnd/Impressao/local-print-agent.md
git commit -m "feat: add local print agent mvp plan implementation"
```
