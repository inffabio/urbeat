# Impressão automática de pedidos — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Implement task-by-task with tests and review checkpoints.

**Goal:** Implementar o fluxo híbrido de impressão automática após o aceite do pedido, com retries, idempotência, suporte por sistema operacional e instalador MSI/Windows Service.

**Architecture:** O backend continua autoritativo para a transição `Received -> Preparing` e grava um evento durável de pedido aceito. O dashboard recebe/reconcilia esse evento, enfileira a impressão local apenas quando `AutoPrint` estiver ativo e delega a execução ao adaptador/Print Agent. No Windows, o agente roda como Windows Service e imprime RAW via `winspool.drv`, sem qualquer diálogo.

**Tech Stack:** .NET 9, ASP.NET Core, EF Core/PostgreSQL, Outbox, SignalR, Angular 20/Ionic 8/Signals, TypeScript, PowerShell, .NET 9 Print Agent, WiX Toolset v4/MSI, CUPS.

## Global Constraints

- Backend autoritativo para status, permissões, pedidos e idempotência.
- Status e eventos usam timestamps UTC.
- O aceite nunca será revertido por falha de impressão.
- Retries automáticos em aproximadamente 2s, 5s e 15s.
- `AutoPrint=false` nunca dispara impressão automática.
- Windows não pode abrir seleção de impressora, `window.print()` ou diálogo.
- Alterações devem respeitar `AGENTS.md` e manter Angular standalone, Signals, `inject()` e control flow moderno.
- O Print Agent é alterado somente porque faz parte explícita do escopo de impressão.

## File Map

- Backend: eventos/outbox, contratos, persistência localizável e testes de transição/entrega.
- Frontend: `SellerOrdersPageComponent`, `SellerPrintingService`, fila de impressão, SignalR/reconciliação e testes.
- Print Agent: `Program.cs`, executor/discovery, armazenamento persistente, Windows Service hosting e testes.
- Packaging: scripts PowerShell, projeto WiX/MSI, downloads e página de instalação.

---

### Task 1: Tornar o evento de aceite consumível pelo dashboard

**Files:**
- Modify: `backend/src/Urbeat.Application/Outbox/OrderStatusChangedEvent.cs`
- Modify: `backend/src/Urbeat.Application/Outbox/OutboxEventTypes.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Services/OrderService.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Outbox/Handlers/OrderSignalRHandler.cs` ou handler seller equivalente
- Test: testes unitários/integração de `OrderService` e outbox existentes

**Requirements:**

- Reusar a transição existente e o evento durável sem criar evento para qualquer mudança de status.
- Emitir um evento de impressão somente para `PreviousStatus=Received` e `NewStatus=Preparing`.
- Incluir `OrderId`, `StoreId`, `SellerUserId`, `Code`, `Sequence` e `ChangedAtUtc`.
- Entregar evento ao grupo/usuário vendedor correto via SignalR.
- Manter entrega at-least-once e permitir reconciliação pelo endpoint de pedidos.
- Não imprimir nem depender de hardware no backend.

**Validation:**

```powershell
dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~Order"
dotnet test backend/tests/Urbeat.IntegrationTests --filter "FullyQualifiedName~Order"
```

---

### Task 2: Criar fila de impressão automática no frontend

**Files:**
- Create: `frontend/src/app/features/seller-printing/automatic-print-queue.service.ts`
- Modify: `frontend/src/app/features/seller-orders/seller-orders-page.component.ts`
- Modify: `frontend/src/app/features/seller-printing/seller-printing.service.ts`
- Modify: `frontend/src/app/features/seller-printing/seller-printing.models.ts`
- Modify: `frontend/src/app/features/seller-orders/seller-orders-page.component.html` e `.scss` quando necessário
- Test: specs da fila, `seller-orders-page.component.spec.ts`, `seller-printing.service.spec.ts`

**Requirements:**

- Usar Signals para estado da fila, item atual, tentativa e erro final.
- Usar chave idempotente baseada em pedido, sequência de aceite e configuração da impressora.
- Ignorar evento duplicado e job já concluído.
- Respeitar `config.autoPrint` antes de enfileirar.
- Executar tentativas em 2s, 5s e 15s; falha de impressão não rejeita a atualização de status.
- Buscar detalhes autoritativos do pedido antes de montar o recibo.
- Exibir no dashboard agente indisponível, retry e falha definitiva sem bloquear o quadro.
- Reconciliação após `load()`/reconexão deve encontrar pedidos aceitos ainda não impressos.
- Corrigir o caminho atual que chama `printAcceptedOrder()` sem verificar `autoPrint`.

**Validation:**

```powershell
cd frontend
npx jest --no-coverage src/app/features/seller-printing/automatic-print-queue.service.spec.ts
npx jest --no-coverage src/app/features/seller-orders/seller-orders-page.component.spec.ts src/app/features/seller-printing/seller-printing.service.spec.ts
```

---

### Task 3: Persistir jobs e endurecer execução do Print Agent

**Files:**
- Modify: `print-agent/Urbeat.PrintAgent/Storage/PrintJobStore.cs`
- Modify: `print-agent/Urbeat.PrintAgent/Program.cs`
- Modify: `print-agent/Urbeat.PrintAgent/Services/PrintJobService.cs`
- Modify: `print-agent/Urbeat.PrintAgent/Services/LocalPrintExecutor.cs`
- Modify: `print-agent/Urbeat.PrintAgent/Services/LocalPrinterDiscovery.cs`
- Test: `print-agent/Urbeat.PrintAgent.Tests/PrintJobServiceTests.cs`, `LocalPrinterDiscoveryTests.cs` e novos testes de persistência

**Requirements:**

- Persistir jobs em arquivo local seguro e limitado, preservando operação key e status após reinício.
- Não duplicar job já enviado com a mesma operation key.
- Validar impressora configurada antes de executar.
- Windows deve continuar usando `winspool.drv` com dados RAW e sem shell/dialog.
- Linux/macOS devem continuar usando CUPS (`lpstat`/`lp`) sem quebrar o comportamento atual.
- API deve permanecer somente em `127.0.0.1:43111`.
- Não aceitar comandos arbitrários ou caminhos fornecidos pelo cliente.

**Validation:**

```powershell
dotnet test print-agent/Urbeat.PrintAgent.Tests
dotnet build print-agent/Urbeat.PrintAgent/Urbeat.PrintAgent.csproj
```

---

### Task 4: Hospedar o agente como Windows Service

**Files:**
- Modify: `print-agent/Urbeat.PrintAgent/Program.cs`
- Modify: `print-agent/Urbeat.PrintAgent/Urbeat.PrintAgent.csproj`
- Modify: `print-agent/Urbeat.PrintAgent/appsettings.json`
- Create/modify: documentação e scripts de serviço em `downloads/urbeat-print-agent/windows/`
- Test: `print-agent/Urbeat.PrintAgent.Tests`

**Requirements:**

- Usar `AddWindowsService`/hosting apropriado somente quando executado no Windows Service.
- Manter execução console para desenvolvimento e testes.
- Serviço iniciar automaticamente, reiniciar em falha e gravar logs sem secrets.
- Manter bind loopback.
- Criar health endpoint para o dashboard.
- Documentar instalação, atualização e remoção.

**Validation:**

```powershell
dotnet test print-agent/Urbeat.PrintAgent.Tests
dotnet publish print-agent/Urbeat.PrintAgent/Urbeat.PrintAgent.csproj -c Release -r win-x64 --self-contained false
```

---

### Task 5: Criar instalador MSI Windows

**Files:**
- Create: projeto WiX v4 em `print-agent/Urbeat.PrintAgent.Installer/`
- Modify: `scripts/print-agent/build-print-agent-packages.ps1`
- Modify: `downloads/urbeat-print-agent/windows/README.md`
- Modify: `frontend/src/app/features/seller-install/seller-install-page.component.ts` e `.html`
- Test: validação de pacote e testes do instalador quando disponíveis

**Requirements:**

- Gerar MSI x64 com identidade/versionamento do Urbeat.
- Instalar binário/configuração em `Program Files`.
- Registrar e iniciar o Windows Service automaticamente.
- Permitir repair, upgrade e uninstall.
- Não exigir PowerShell nem execução manual do usuário final.
- Manter o ZIP/PowerShell como fallback técnico.
- Alterar o download principal do Windows para o MSI e rotular ZIP como alternativa.
- Não incluir credenciais ou secrets no pacote.

**Validation:**

```powershell
./scripts/print-agent/build-print-agent-packages.ps1 -Configuration Release
dotnet build print-agent/Urbeat.PrintAgent.Installer/Urbeat.PrintAgent.Installer.csproj -c Release
```

---

### Task 6: Validação integrada e revisão final

**Files:**
- Modify only files required by failing tests or integration findings.
- Test: suíte backend, frontend, Print Agent e validação de diff.

**Requirements:**

- Confirmar fluxo `Received -> Preparing -> Preparing` no dashboard sem duplicidade.
- Confirmar `AutoPrint=false` não imprime.
- Confirmar retry 2/5/15 e falha sem rollback.
- Confirmar Windows sem diálogo e com impressora escolhida.
- Confirmar pickup não cria etapa de entrega.
- Confirmar timestamps UTC e autorização por loja.

**Validation:**

```powershell
dotnet build backend/Urbeat.sln
dotnet test backend/tests/Urbeat.UnitTests
dotnet test backend/tests/Urbeat.IntegrationTests
dotnet test print-agent/Urbeat.PrintAgent.Tests
cd frontend
npx jest --no-coverage
npx ng build --configuration production
```

Depois, revisar `git diff`, arquivos alterados, migrations necessárias e ausência de secrets antes de concluir.
