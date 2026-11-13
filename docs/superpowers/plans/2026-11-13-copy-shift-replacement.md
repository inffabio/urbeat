# Cópia de turno com substituição de horários Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fazer a cópia de um turno individual substituir todos os turnos de cada dia de destino selecionado e remover, do frontend e do validador da API, todas as regras de sobreposição (mesmo dia e cross-day), mantendo as demais validações.

**Architecture:** Três mudanças coordenadas. (1) `StoreHoursState.confirmCopy()` passa a gravar um único turno por destino em vez de concatenar; `reportCopyResult` perde os avisos e `validateDay`/`collectScheduleErrors` perdem as regras de sobreposição, junto com os helpers `conflictsWithDay`, `shiftsOverlap`, `getNextDayId`, `getPreviousDayId` e `validateCrossDayConflicts`. (2) Os dois templates que compartilham o estado (wizard e dashboard) ganham uma nota de substituição no modal de cópia. (3) O `UpsertStoreBusinessHoursRequestDtoValidator` perde as regras `HasOverlappingShifts` e `HaveNoCrossDayOvernightConflicts`. Contrato HTTP, persistência, migrations, `StoreOpeningHoursCalculator`, Postman, DOCX e deploy não mudam.

**Tech Stack:** Angular 20 standalone + Signals + Ionic 8 + TypeScript strict + Jest; .NET 9 + FluentValidation + xUnit + FluentAssertions.

## Global Constraints

- Contrato HTTP inalterado: `PUT /api/stores/{storeId}/business-hours` com `{ items: [{ dayOfWeek, isOpen, shifts: [{ startTime, endTime }] }] }`. Nenhum DTO, controller, mapper, entidade, `ApplicationDbContext`, migration, `IStoreBusinessHoursService`, `StoreBusinessHoursService.UpsertAsync` ou `StoreOpeningHoursCalculator` é alterado.
- A cópia sempre produz **exatamente 1 turno** por dia de destino aplicado; os turnos anteriores do destino são descartados.
- Nunca produzir aviso de `sobreposição` nem de `limite` na cópia. Sucesso: `Turno N copiado para X dia(s).`; nenhum destino aplicado: `Nenhum dia recebeu o turno.`
- Somente dias abertos e diferentes do dia de origem recebem cópia; destino fechado durante o modal é ignorado em silêncio.
- `Selecione pelo menos um dia.` mantém o modal aberto e o rascunho limpo.
- Validações mantidas no frontend: máximo de 5 turnos por dia, dia aberto sem turno, turno incompleto, formato `HH:mm` / `HH:mm:ss[.f{1,7}]`, início igual ao fim, `canAddShift` bloqueado com horário inválido no turno anterior.
- Validações mantidas no backend: `Items` não vazio, `DayOfWeek` único, dia aberto com ao menos 1 turno, máximo de 5 turnos, `StartTime`/`EndTime` obrigatórios, início diferente do fim, turnos de dias fechados ignorados.
- Overnight continua permitido (`end < start`), inclusive `23:59 -> 00:00` e `00:00 -> 23:59`, sem regra de sobreposição.
- Textos novos não citam marca; a marca continua minúscula `urbeat` onde já existe.
- Não alterar layout, breakpoints, rotas, `WizardFooterComponent`, `FormsModule`, PWA/Capacitor, alvos de toque (>= 44px) ou tema (`.urbeat-onboarding` no wizard, bordeaux no dashboard).
- Usar apenas tokens existentes: `var(--app-*)` no wizard e `var(--hours-*)` no dashboard.
- Não alterar `backend/tests/Urbeat.IntegrationTests/**` (payloads usados não têm sobreposição), `postman/`, `Documentacao/*.docx`, `docker/`, `scripts/` ou secrets.
- Timestamps e regras de fuso não são afetados; nada de `DateTime.Now` novo.
- Não incluir no commit alterações não relacionadas já existentes no working tree.

## File Map

| Arquivo | Responsabilidade | Ação |
| --- | --- | --- |
| `backend/src/Urbeat.Application/Validators/UpsertStoreBusinessHoursRequestDtoValidator.cs` | validação autoritativa do payload de horários | modificar (remover 2 regras + 3 métodos privados) |
| `backend/tests/Urbeat.UnitTests/Application/Validators/UpsertStoreBusinessHoursRequestDtoValidatorTests.cs` | cobertura unitária do validador | modificar (converter 4 testes, remover 1 asserção, adicionar 2 testes) |
| `frontend/src/app/shared/state/store-hours.state.ts` | estado compartilhado de horários (wizard + dashboard) | modificar (`confirmCopy`, `reportCopyResult`, `validateDay`, `collectScheduleErrors`; remover helpers de conflito) |
| `frontend/src/app/shared/state/store-hours.state.spec.ts` | cobertura do estado compartilhado | modificar (bloco `individual shift copy` + testes de validação) |
| `frontend/src/app/features/store-config/hours/store-hours-page.component.html` | modal de cópia do wizard | modificar (nota de substituição) |
| `frontend/src/app/features/store-config/hours/store-hours-page.component.scss` | estilos do wizard | modificar (`.copy-modal-note`) |
| `frontend/src/app/features/store-config/hours/store-hours-page.component.spec.ts` | cobertura do wizard | modificar (nota no template + variação de destino com 2 turnos) |
| `frontend/src/app/features/seller-hours/seller-hours-page.component.html` | modal de cópia do dashboard | modificar (nota de substituição) |
| `frontend/src/app/features/seller-hours/seller-hours-page.component.scss` | estilos do dashboard | modificar (`.copy-modal-note`) |
| `frontend/src/app/features/seller-hours/seller-hours-page.component.spec.ts` | cobertura do dashboard | modificar (2 testes de cópia + nota no template) |

Fora do mapa: `store-hours-page.component.ts`, `seller-hours-page.component.ts`, rotas, serviços, DTOs, persistência, migrations, storefront.

---

### Task 1: Aceitar sobreposição no validador da API

**Files:**
- Modify: `backend/src/Urbeat.Application/Validators/UpsertStoreBusinessHoursRequestDtoValidator.cs`
- Test: `backend/tests/Urbeat.UnitTests/Application/Validators/UpsertStoreBusinessHoursRequestDtoValidatorTests.cs`

**Interfaces:**
- Consumes: `UpsertStoreBusinessHoursRequestDto`, `StoreBusinessHourItemDto`, `StoreBusinessHourShiftDto` (inalterados).
- Produces: `UpsertStoreBusinessHoursRequestDtoValidator` sem as mensagens `"Shifts cannot overlap within the same day."` e `"Overnight shifts cannot overlap the next day's shifts."`; mantém `"Each day of week can be configured only once."`, `"Open days must have at least one shift."`, `"Maximum 5 shifts per day."`, `"Start time is required."`, `"End time is required."`, `"Start and end times cannot be equal."`.

- [ ] **Step 1: Converter o teste de sobreposição same-day para aceite**

Em `UpsertStoreBusinessHoursRequestDtoValidatorTests.cs`, substituir o teste `ValidateAsync_ShouldRejectOverlappingShiftsWithinTheSameDay` (linhas 68–78) por:

```csharp
    [Fact]
    public async Task ValidateAsync_ShouldAcceptOverlappingShiftsWithinTheSameDay()
    {
        var request = BuildRequest(
            Day(DayOfWeek.Monday, true, Shift("00:30", "02:00"), Shift("23:00", "01:00")));

        var result = await new UpsertStoreBusinessHoursRequestDtoValidator().ValidateAsync(request);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().NotContain(x => x.ErrorMessage == "Shifts cannot overlap within the same day.");
    }
```

- [ ] **Step 2: Converter os três testes de conflito cross-day para aceite**

Substituir `ValidateAsync_ShouldRejectOvernightShiftConflictWithNextDayStart` (linhas 80–91), `ValidateAsync_ShouldRejectOvernightShiftConflictFromSundayToMonday` (linhas 93–104) e `ValidateAsync_ShouldRejectOvernightShiftConflictFromSaturdayToSunday` (linhas 106–117) por:

```csharp
    [Fact]
    public async Task ValidateAsync_ShouldAcceptOvernightShiftOverlappingNextDayStart()
    {
        var request = BuildRequest(
            Day(DayOfWeek.Monday, true, Shift("23:00", "01:00")),
            Day(DayOfWeek.Tuesday, true, Shift("00:30", "02:00")));

        var result = await new UpsertStoreBusinessHoursRequestDtoValidator().ValidateAsync(request);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_ShouldAcceptOvernightShiftOverlappingMondayStart()
    {
        var request = BuildRequest(
            Day(DayOfWeek.Sunday, true, Shift("23:00", "01:00")),
            Day(DayOfWeek.Monday, true, Shift("00:30", "02:00")));

        var result = await new UpsertStoreBusinessHoursRequestDtoValidator().ValidateAsync(request);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_ShouldAcceptOvernightShiftOverlappingSundayStart()
    {
        var request = BuildRequest(
            Day(DayOfWeek.Saturday, true, Shift("23:00", "01:00")),
            Day(DayOfWeek.Sunday, true, Shift("00:30", "02:00")));

        var result = await new UpsertStoreBusinessHoursRequestDtoValidator().ValidateAsync(request);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }
```

- [ ] **Step 3: Remover a asserção da mensagem cross-day do teste de duplicidade**

Em `ValidateAsync_ShouldReportOnlyUniquenessForDuplicatedDayOfWeek` (linhas 42–55), apagar a linha:

```csharp
        result.Errors.Should().NotContain(x => x.ErrorMessage == "Overnight shifts cannot overlap the next day's shifts.");
```

O corpo restante passa a ser:

```csharp
        var request = BuildRequest(
            Day(DayOfWeek.Monday, true, Shift("23:00", "01:00")),
            Day(DayOfWeek.Monday, true, Shift("09:00", "12:00")),
            Day(DayOfWeek.Tuesday, true, Shift("00:30", "02:00")));

        var result = await new UpsertStoreBusinessHoursRequestDtoValidator().ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(x => x.ErrorMessage == "Each day of week can be configured only once.");
```

- [ ] **Step 4: Adicionar os dois testes de garantia**

Inserir após `ValidateAsync_ShouldReportOnlyUniquenessForDuplicatedDayOfWeek`:

```csharp
    [Fact]
    public async Task ValidateAsync_ShouldAcceptCopyReplacementPayload()
    {
        var request = BuildRequest(
            Day(
                DayOfWeek.Monday,
                true,
                Shift("08:00", "09:00"),
                Shift("10:00", "11:00"),
                Shift("14:00", "16:00")),
            Day(DayOfWeek.Tuesday, true, Shift("14:00", "16:00")));

        var result = await new UpsertStoreBusinessHoursRequestDtoValidator().ValidateAsync(request);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_ShouldStillRejectEqualStartAndEndTimes()
    {
        var request = BuildRequest(
            Day(DayOfWeek.Monday, true, Shift("00:30", "02:00"), Shift("09:00", "09:00")));

        var result = await new UpsertStoreBusinessHoursRequestDtoValidator().ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.ErrorMessage == "Start and end times cannot be equal.");
    }
```

Não renomear nem alterar `ValidateAsync_ShouldRejectMoreThanFiveShiftsOnOpenDays`, `ValidateAsync_ShouldRejectMissingStartAndEndTimes`, `ValidateAsync_ShouldIgnoreShiftsOfClosedDays`, `ValidateAsync_ShouldAcceptMidnightBoundaryTimes`, `ValidateAsync_ShouldAcceptOvernightShiftWhenNextDayStartsAfterItEnds`, `ValidateAsync_ShouldAcceptOvernightShiftWhenNextDayIsClosed`, `ValidateAsync_ShouldRejectEqualStartAndEndTimes` e `ValidateAsync_ShouldAcceptNonOverlappingWeekSchedule`.

- [ ] **Step 5: Rodar os testes e confirmar que falham**

From the repository root:

```
dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~UpsertStoreBusinessHoursRequestDtoValidatorTests"
```

Expected: FAIL em `ValidateAsync_ShouldAcceptOverlappingShiftsWithinTheSameDay` e nos três testes de overnight, porque o validador ainda rejeita sobreposição.

- [ ] **Step 6: Remover as regras de sobreposição do validador**

Em `UpsertStoreBusinessHoursRequestDtoValidator.cs`:

1. Remover o bloco (linhas 19–21):

```csharp
        RuleFor(x => x.Items)
            .Must(HaveNoCrossDayOvernightConflicts)
            .WithMessage("Overnight shifts cannot overlap the next day's shifts.");
```

2. Remover o bloco (linhas 50–52):

```csharp
                    item.RuleFor(x => x.Shifts)
                        .Must(shifts => !HasOverlappingShifts(shifts))
                        .WithMessage("Shifts cannot overlap within the same day.");
```

3. Remover os métodos privados `HaveNoCrossDayOvernightConflicts` (linhas 61–99), `HasOverlappingShifts` (linhas 101–114) e `ShiftsOverlap` (linhas 116–135).

O arquivo final deve ficar exatamente assim:

```csharp
using FluentValidation;
using Urbeat.Application.DTOs;

namespace Urbeat.Application.Validators;

public sealed class UpsertStoreBusinessHoursRequestDtoValidator : AbstractValidator<UpsertStoreBusinessHoursRequestDto>
{
    private const int MaxShiftsPerDay = 5;

    public UpsertStoreBusinessHoursRequestDtoValidator()
    {
        RuleFor(x => x.Items)
            .NotEmpty();

        RuleFor(x => x.Items)
            .Must(items => items.Select(i => i.DayOfWeek).Distinct().Count() == items.Count)
            .WithMessage("Each day of week can be configured only once.");

        RuleForEach(x => x.Items)
            .ChildRules(item =>
            {
                // Closed days must not fail validation because of shifts that are hidden
                // by the editor and are never persisted for that day.
                item.When(x => x.IsOpen, () =>
                {
                    item.RuleFor(x => x.Shifts)
                        .NotEmpty()
                        .WithMessage("Open days must have at least one shift.");

                    item.RuleFor(x => x.Shifts.Count)
                        .LessThanOrEqualTo(MaxShiftsPerDay)
                        .WithMessage($"Maximum {MaxShiftsPerDay} shifts per day.");

                    item.RuleForEach(x => x.Shifts)
                        .ChildRules(shift =>
                        {
                            shift.RuleFor(x => x.StartTime)
                                .NotNull()
                                .WithMessage("Start time is required.");

                            shift.RuleFor(x => x.EndTime)
                                .NotNull()
                                .WithMessage("End time is required.");
                        });

                    item.RuleFor(x => x.Shifts)
                        .Must(shifts => !HasEqualStartAndEndTimes(shifts))
                        .WithMessage("Start and end times cannot be equal.");
                });
            });
    }

    private static bool HasEqualStartAndEndTimes(IEnumerable<StoreBusinessHourShiftDto> shifts)
        => shifts.Any(shift =>
            shift.StartTime.HasValue
            && shift.EndTime.HasValue
            && shift.StartTime.Value == shift.EndTime.Value);
}
```

- [ ] **Step 7: Rodar os testes e confirmar que passam**

```
dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~UpsertStoreBusinessHoursRequestDtoValidatorTests"
```

Expected: PASS em todos os testes da classe, incluindo `ValidateAsync_ShouldAcceptOverlappingShiftsWithinTheSameDay`, `ValidateAsync_ShouldAcceptOvernightShiftOverlappingNextDayStart`, `ValidateAsync_ShouldAcceptOvernightShiftOverlappingMondayStart`, `ValidateAsync_ShouldAcceptOvernightShiftOverlappingSundayStart` e `ValidateAsync_ShouldAcceptCopyReplacementPayload`.

- [ ] **Step 8: Compilar a solução**

```
dotnet build backend/Urbeat.sln
```

Expected: `Build succeeded` sem novos warnings (o projeto não trata warnings como erro).

- [ ] **Step 9: Commit**

```
git add backend/src/Urbeat.Application/Validators/UpsertStoreBusinessHoursRequestDtoValidator.cs backend/tests/Urbeat.UnitTests/Application/Validators/UpsertStoreBusinessHoursRequestDtoValidatorTests.cs
git commit -m "feat: accept overlapping business hours in validator"
```

**Critérios de aceite da Task 1:**
- [ ] `dotnet test` da classe passa 100%.
- [ ] Nenhuma ocorrência de `"Shifts cannot overlap within the same day."`, `"Overnight shifts cannot overlap the next day's shifts."`, `HasOverlappingShifts`, `ShiftsOverlap` ou `HaveNoCrossDayOvernightConflicts` no arquivo do validador.
- [ ] `UpsertStoreBusinessHoursRequestDto`, `StoresController` e `StoreBusinessHoursService` sem diff.

---

### Task 2: Substituir turnos do destino em `confirmCopy()`

**Files:**
- Modify: `frontend/src/app/shared/state/store-hours.state.ts` (`confirmCopy` linhas 389–442, `reportCopyResult` linhas 444–473)
- Test: `frontend/src/app/shared/state/store-hours.state.spec.ts` (bloco `individual shift copy`, linhas 420–741)

**Interfaces:**
- Consumes: `copySourceShift()` → `CopySourceShift | null`; `copyTargets()` → `Set<string>`; `schedule()` → `Record<string, DaySchedule>`; `createShiftKey()`; `getDayLabel(dayId)`; `markDirty()`; `closeCopyModal()`; `toast.showInfo(message: string)`.
- Produces: `confirmCopy(): void` (substitui `shifts` do destino por `[{ key, startTime, endTime }]`) e `reportCopyResult(shiftIndex: number, copiedCount: number): void` (sem avisos, sem `showGrouped`).

Mapa dos testes existentes do bloco de cópia:

| Teste atual | Novo teste |
| --- | --- |
| `copies only the first shift of a day that has two shifts` | `replaces the destination with the first shift of a day that has two shifts` |
| `copies only the shift at index %i` | `replaces the destination with only the shift at index %i` |
| `clones the selected shift with a new key and preserves backend times` | mantém nome, adiciona `toHaveLength(1)` no destino |
| `uses a snapshot of the shift taken when the modal was opened` | mantém nome, espera `['09:00-12:00']` |
| `applies the copy to valid days and warns about each conflicting day` | `does not show grouped warnings when all destinations are replaced` |
| `blocks a copy that overlaps an overnight shift on the destination day` | `replaces a destination that already has an overnight shift` |
| `blocks a copied overnight shift that conflicts with the next day of the destination` | `replaces the destination even when the copied overnight shift overlaps the next day` |
| `blocks a normal copy that starts before the previous day overnight shift ends` | `accepts a copied shift that overlaps the destination previous overnight shift` |
| `blocks a normal copy into Monday when the Sunday overnight shift ends after midnight` | `replaces Monday even when the Sunday overnight shift ends after midnight` |
| `copies a normal shift when the previous day overnight shift ends before it starts` | `replaces the destination when the previous day overnight shift ends before the copy starts` |
| `copies an overnight shift when the next day has no conflicting shift` | `replaces the destination with an overnight shift when the next day already has a shift` |
| `does not exceed the maximum shifts per day and warns about the day at the limit` | `replaces a day that already has five shifts` |
| `keeps the draft clean and closes the modal when no destination receives the shift` | mantém nome; cenário passa a ser destinos fechados durante o modal |

- [ ] **Step 1: Substituir o bloco de testes de cópia**

Substituir integralmente o `describe('individual shift copy', () => { ... })` (linhas 420–741 de `store-hours.state.spec.ts`) por:

```ts
  describe('individual shift copy', () => {
    type HoursItem = { dayOfWeek: number; isOpen: boolean; shifts: Array<{ startTime: string; endTime: string }> };

    function hours(...days: Array<{ dayOfWeek: number; shifts: Array<[string, string]> }>): HoursItem[] {
      return days.map(day => ({
        dayOfWeek: day.dayOfWeek,
        isOpen: true,
        shifts: day.shifts.map(([startTime, endTime]) => ({ startTime, endTime })),
      }));
    }

    function shiftTimes(dayId: string): string[] {
      return component.schedule()[dayId].shifts.map(shift => `${shift.startTime}-${shift.endTime}`);
    }

    it('lists only open days other than the origin as copy targets', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00']] },
        { dayOfWeek: 3, shifts: [['18:00', '22:00']] },
      ));

      component.openCopyModal('segunda', 0);

      expect(component.isCopyModalOpen()).toBe(true);
      expect(component.copyTargetDays().map(day => day.id)).toEqual(['quarta']);
    });

    it('exposes the origin day label and shift number used by the modal', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00'], ['14:00', '18:00']] },
        { dayOfWeek: 2, shifts: [['19:00', '21:00']] },
      ));

      component.openCopyModal('segunda', 1);

      expect(component.copySourceShiftNumber()).toBe(2);
      expect(component.copySourceDayLabel()).toBe('Segunda-feira');
    });

    it('ignores an invalid day or shift index', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00']] },
        { dayOfWeek: 2, shifts: [['19:00', '21:00']] },
      ));

      component.openCopyModal('segunda', 4);
      component.openCopyModal('domingo', 0);

      expect(component.isCopyModalOpen()).toBe(false);
      expect(component.copySourceShift()).toBeNull();
    });

    it('replaces the destination with the first shift of a day that has two shifts', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00'], ['14:00', '18:00']] },
        { dayOfWeek: 2, shifts: [['19:00', '21:00']] },
      ));

      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(shiftTimes('terca')).toEqual(['09:00-12:00']);
      expect(shiftTimes('segunda')).toEqual(['09:00-12:00', '14:00-18:00']);
      expect(component.hasChanges()).toBe(true);
      expect(component.isCopyModalOpen()).toBe(false);
    });

    it.each([1, 2])('replaces the destination with only the shift at index %i', async (index) => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['08:00', '10:00'], ['11:00', '13:00'], ['14:00', '16:00']] },
        { dayOfWeek: 2, shifts: [['19:00', '21:00']] },
      ));

      component.openCopyModal('segunda', index);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      const expected = ['08:00-10:00', '11:00-13:00', '14:00-16:00'][index];
      expect(shiftTimes('terca')).toEqual([expected]);
      expect(component.hasChanges()).toBe(true);
    });

    it('clones the selected shift with a new key and preserves backend times', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['08:00:30.2500000', '12:30:45.5000000'], ['14:00', '18:00']] },
        { dayOfWeek: 2, shifts: [['19:00', '21:00']] },
      ));

      const sourceKeys = new Set(component.schedule().segunda.shifts.map(shift => shift.key));
      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      const destinationShifts = component.schedule().terca.shifts;
      expect(destinationShifts).toHaveLength(1);
      expect(destinationShifts[0].startTime).toBe('08:00:30.2500000');
      expect(destinationShifts[0].endTime).toBe('12:30:45.5000000');
      expect(destinationShifts[0].key).toBeTruthy();
      expect(sourceKeys.has(destinationShifts[0].key)).toBe(false);
    });

    it('uses a snapshot of the shift taken when the modal was opened', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00'], ['14:00', '18:00']] },
        { dayOfWeek: 2, shifts: [['19:00', '21:00']] },
      ));

      component.openCopyModal('segunda', 0);
      component.updateTime('segunda', 0, 'startTime', '10:00');
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(shiftTimes('terca')).toEqual(['09:00-12:00']);
    });

    it('never copies to a closed day', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00']] },
        { dayOfWeek: 2, shifts: [['19:00', '21:00']] },
      ));
      component.toggleDay('terca');

      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(component.schedule().terca.isOpen).toBe(false);
      expect(shiftTimes('terca')).toEqual(['19:00-21:00']);
      expect(component.isCopyModalOpen()).toBe(false);
    });

    it('replaces all shifts of each selected day with the copied shift', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00'], ['14:00', '18:00']] },
        { dayOfWeek: 2, shifts: [['08:00', '10:00'], ['11:00', '13:00'], ['19:00', '22:00']] },
        { dayOfWeek: 3, shifts: [['10:00', '11:00'], ['18:00', '23:00']] },
      ));

      component.openCopyModal('segunda', 1);
      component.toggleCopyTarget('terca');
      component.toggleCopyTarget('quarta');
      component.confirmCopy();

      expect(shiftTimes('terca')).toEqual(['14:00-18:00']);
      expect(shiftTimes('quarta')).toEqual(['14:00-18:00']);
      expect(shiftTimes('segunda')).toEqual(['09:00-12:00', '14:00-18:00']);
      expect(component.hasChanges()).toBe(true);
    });

    it('keeps the destination day open and the origin untouched', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00']] },
        { dayOfWeek: 2, shifts: [['19:00', '21:00'], ['22:00', '23:00']] },
      ));

      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(component.schedule().terca.isOpen).toBe(true);
      expect(component.schedule().terca.shifts).toHaveLength(1);
      expect(shiftTimes('segunda')).toEqual(['09:00-12:00']);
      expect(component.copySourceShift()).toBeNull();
      expect(component.copyTargets().size).toBe(0);
    });

    it('accepts a copied shift that overlaps the destination previous overnight shift', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['23:00', '01:00']] },
        { dayOfWeek: 2, shifts: [['18:00', '20:00']] },
        { dayOfWeek: 3, shifts: [['00:30', '02:00']] },
      ));

      component.openCopyModal('quarta', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(shiftTimes('terca')).toEqual(['00:30-02:00']);
      expect(component.hasChanges()).toBe(true);
      expect(toastServiceMock.showGrouped).not.toHaveBeenCalled();
      expect(toastServiceMock.showInfo).toHaveBeenCalledWith('Turno 1 copiado para 1 dia.');
    });

    it('does not show grouped warnings when all destinations are replaced', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00']] },
        { dayOfWeek: 2, shifts: [['11:00', '13:00']] },
        { dayOfWeek: 3, shifts: [['18:00', '22:00']] },
      ));

      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.toggleCopyTarget('quarta');
      component.confirmCopy();

      expect(toastServiceMock.showGrouped).not.toHaveBeenCalled();
      expect(toastServiceMock.showInfo).toHaveBeenCalledTimes(1);
      expect(toastServiceMock.showInfo).toHaveBeenCalledWith('Turno 1 copiado para 2 dias.');
    });

    it('does not call the API during copy', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00']] },
        { dayOfWeek: 2, shifts: [['19:00', '21:00']] },
      ));

      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(storeServiceMock.upsertStoreBusinessHours).not.toHaveBeenCalled();
      expect(component.hasChanges()).toBe(true);
    });

    it('replaces a destination that already has an overnight shift', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['00:30', '02:00']] },
        { dayOfWeek: 2, shifts: [['23:00', '01:00']] },
      ));

      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(shiftTimes('terca')).toEqual(['00:30-02:00']);
      expect(component.hasChanges()).toBe(true);
      expect(toastServiceMock.showGrouped).not.toHaveBeenCalled();
    });

    it('replaces the destination even when the copied overnight shift overlaps the next day', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['23:00', '01:00']] },
        { dayOfWeek: 2, shifts: [['18:00', '20:00']] },
        { dayOfWeek: 3, shifts: [['00:30', '02:00']] },
      ));

      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(shiftTimes('terca')).toEqual(['23:00-01:00']);
      expect(shiftTimes('quarta')).toEqual(['00:30-02:00']);
      expect(component.hasChanges()).toBe(true);
      expect(toastServiceMock.showGrouped).not.toHaveBeenCalled();
    });

    it('replaces Monday even when the Sunday overnight shift ends after midnight', async () => {
      await load(hours(
        { dayOfWeek: 0, shifts: [['23:00', '01:00']] },
        { dayOfWeek: 1, shifts: [['18:00', '20:00']] },
        { dayOfWeek: 3, shifts: [['00:30', '02:00']] },
      ));

      component.openCopyModal('quarta', 0);
      component.toggleCopyTarget('segunda');
      component.confirmCopy();

      expect(shiftTimes('segunda')).toEqual(['00:30-02:00']);
      expect(component.hasChanges()).toBe(true);
      expect(toastServiceMock.showGrouped).not.toHaveBeenCalled();
    });

    it('replaces the destination when the previous day overnight shift ends before the copy starts', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['23:00', '01:00']] },
        { dayOfWeek: 2, shifts: [['18:00', '20:00']] },
        { dayOfWeek: 3, shifts: [['02:00', '04:00']] },
      ));

      component.openCopyModal('quarta', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(shiftTimes('terca')).toEqual(['02:00-04:00']);
      expect(component.hasChanges()).toBe(true);
      expect(toastServiceMock.showGrouped).not.toHaveBeenCalled();
    });

    it('replaces the destination with an overnight shift when the next day already has a shift', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['23:00', '01:00']] },
        { dayOfWeek: 2, shifts: [['18:00', '20:00']] },
        { dayOfWeek: 3, shifts: [['02:00', '04:00']] },
      ));

      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(shiftTimes('terca')).toEqual(['23:00-01:00']);
      expect(shiftTimes('quarta')).toEqual(['02:00-04:00']);
      expect(component.hasChanges()).toBe(true);
      expect(toastServiceMock.showGrouped).not.toHaveBeenCalled();
    });

    it('replaces a day that already has five shifts', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '10:00']] },
        {
          dayOfWeek: 2,
          shifts: [['11:00', '12:00'], ['12:00', '13:00'], ['13:00', '14:00'], ['14:00', '15:00'], ['15:00', '16:00']],
        },
      ));

      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(component.schedule().terca.shifts).toHaveLength(1);
      expect(shiftTimes('terca')).toEqual(['09:00-10:00']);
      expect(component.hasChanges()).toBe(true);
      expect(toastServiceMock.showGrouped).not.toHaveBeenCalled();
    });

    it('keeps the draft clean and closes the modal when no destination receives the shift', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00']] },
        { dayOfWeek: 2, shifts: [['10:00', '11:00']] },
        { dayOfWeek: 3, shifts: [['08:00', '09:30']] },
      ));

      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.toggleCopyTarget('quarta');
      component.schedule.update(schedule => ({
        ...schedule,
        terca: { isOpen: false, shifts: schedule.terca.shifts },
        quarta: { isOpen: false, shifts: schedule.quarta.shifts },
      }));
      component.confirmCopy();

      expect(component.hasChanges()).toBe(false);
      expect(component.isCopyModalOpen()).toBe(false);
      expect(component.copyTargets().size).toBe(0);
      expect(component.copySourceShift()).toBeNull();
      expect(shiftTimes('terca')).toEqual(['10:00-11:00']);
      expect(shiftTimes('quarta')).toEqual(['08:00-09:30']);
      expect(toastServiceMock.showInfo).toHaveBeenCalledWith('Nenhum dia recebeu o turno.');
      expect(toastServiceMock.showGrouped).not.toHaveBeenCalled();
    });

    it('requires at least one selected destination', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00']] },
        { dayOfWeek: 2, shifts: [['19:00', '21:00']] },
      ));

      component.openCopyModal('segunda', 0);
      component.confirmCopy();

      expect(toastServiceMock.showInfo).toHaveBeenCalledWith('Selecione pelo menos um dia.');
      expect(component.isCopyModalOpen()).toBe(true);
      expect(component.hasChanges()).toBe(false);
    });

    it('clears the copy state when the modal is cancelled', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00']] },
        { dayOfWeek: 2, shifts: [['19:00', '21:00']] },
      ));

      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.closeCopyModal();

      expect(component.isCopyModalOpen()).toBe(false);
      expect(component.copySourceShift()).toBeNull();
      expect(component.copyTargets().size).toBe(0);
      expect(component.hasChanges()).toBe(false);
      expect(shiftTimes('terca')).toEqual(['19:00-21:00']);
    });
  });
```

O helper `groupedText()` deixa de existir: nenhum teste novo consulta o texto de `showGrouped`.

- [ ] **Step 2: Rodar o spec e confirmar que falha**

From `frontend/`:

```
npx jest --no-coverage src/app/shared/state/store-hours.state.spec.ts
```

Expected: FAIL em `replaces the destination with the first shift of a day that has two shifts` (recebe `['09:00-12:00', '19:00-21:00']`), nos dois casos de `it.each` e em todos os testes que esperam 1 turno por destino; os testes de `showGrouped` também falham porque o código atual emite avisos.

- [ ] **Step 3: Reescrever `confirmCopy()`**

Substituir as linhas 389–442 por:

```ts
  confirmCopy(): void {
    const source = this.copySourceShift();
    if (!source) {
      this.closeCopyModal();
      return;
    }

    const targets = [...this.copyTargets()];
    if (targets.length === 0) {
      this.toast.showInfo('Selecione pelo menos um dia.');
      return;
    }

    const applied: string[] = [];
    for (const targetId of targets) {
      const day = this.schedule()[targetId];
      // Closing the day while the modal is open makes it an invalid destination.
      if (!day?.isOpen || targetId === source.dayId) continue;
      applied.push(targetId);
    }

    if (applied.length > 0) {
      this.schedule.update(s => {
        const copy = { ...s };
        for (const targetId of applied) {
          // The copied shift replaces every shift the destination had before.
          copy[targetId] = {
            isOpen: true,
            shifts: [{
              key: createShiftKey(),
              startTime: source.shift.startTime,
              endTime: source.shift.endTime,
            }],
          };
        }
        return copy;
      });
      this.markDirty();
    }

    this.closeCopyModal();
    this.reportCopyResult(source.shiftIndex, applied.length);
  }
```

- [ ] **Step 4: Reduzir `reportCopyResult`**

Substituir as linhas 444–473 por:

```ts
  private reportCopyResult(shiftIndex: number, copiedCount: number): void {
    if (copiedCount === 0) {
      this.toast.showInfo('Nenhum dia recebeu o turno.');
      return;
    }

    const shiftNumber = shiftIndex + 1;
    const copiedLabel = `${copiedCount} ${copiedCount === 1 ? 'dia' : 'dias'}`;
    this.toast.showInfo(`Turno ${shiftNumber} copiado para ${copiedLabel}.`);
  }
```

- [ ] **Step 5: Rodar o spec e confirmar que passa**

```
npx jest --no-coverage src/app/shared/state/store-hours.state.spec.ts
```

Expected: PASS. `conflictsWithDay`, `getNextDayId`, `getPreviousDayId` e `shiftsOverlap` continuam compilando (ainda são usados por `validateDay` e `validateCrossDayConflicts`); serão removidos na Task 3.

- [ ] **Step 6: Commit**

```
git add frontend/src/app/shared/state/store-hours.state.ts frontend/src/app/shared/state/store-hours.state.spec.ts
git commit -m "feat: replace destination shifts when copying a shift"
```

**Critérios de aceite da Task 2:**
- [ ] Cada destino aplicado termina com exatamente 1 turno, com `startTime`/`endTime` do snapshot.
- [ ] Origem intacta; destino permanece `isOpen: true`.
- [ ] `hasChanges` só vira `true` se `applied.length > 0`.
- [ ] `showGrouped` não é chamado em nenhum caminho da cópia.
- [ ] Nenhuma requisição HTTP é feita por `confirmCopy`.

---

### Task 3: Remover validação de sobreposição do estado compartilhado

**Files:**
- Modify: `frontend/src/app/shared/state/store-hours.state.ts` (`validateDay` linhas 510–536, `shiftsOverlap` 538–548, `validateCrossDayConflicts` 550–572, `collectScheduleErrors` 615–623, `conflictsWithDay` 475–508)
- Test: `frontend/src/app/shared/state/store-hours.state.spec.ts` (testes de validação, linhas 178–269)

**Interfaces:**
- Consumes: `schedule()`, `maxShiftsPerDay`, `isValidTime(time: string): boolean`.
- Produces: `validateDay(dayId: string): string | null` sem a regra de pares sobrepostos; `collectScheduleErrors(): string[]` sem chamada cross-day; `validateCrossDayConflicts`, `conflictsWithDay`, `shiftsOverlap`, `getNextDayId` e `getPreviousDayId` deixam de existir. `isOvernight(dayId, index): boolean` e `isOvernightShift(shift): boolean` permanecem.

- [ ] **Step 1: Converter o teste de sobreposição overnight para aceite**

Substituir o teste (linhas 178–191):

```ts
  it('detects the backend overnight overlap between an early and a late shift on the same day', async () => {
    await load([
      {
        dayOfWeek: 1,
        isOpen: true,
        shifts: [
          { startTime: '00:30', endTime: '02:00' },
          { startTime: '23:00', endTime: '01:00' },
        ],
      },
    ]);

    expect(component.validateDay('segunda')).not.toBeNull();
  });
```

por:

```ts
  it('accepts an overnight overlap between an early and a late shift on the same day', async () => {
    await load([
      {
        dayOfWeek: 1,
        isOpen: true,
        shifts: [
          { startTime: '00:30', endTime: '02:00' },
          { startTime: '23:00', endTime: '01:00' },
        ],
      },
    ]);

    expect(component.validateDay('segunda')).toBeNull();
  });

  it('accepts two overlapping shifts on the same day', async () => {
    await load([
      {
        dayOfWeek: 1,
        isOpen: true,
        shifts: [
          { startTime: '09:00', endTime: '13:00' },
          { startTime: '12:00', endTime: '15:00' },
        ],
      },
    ]);

    expect(component.validateDay('segunda')).toBeNull();
  });
```

- [ ] **Step 2: Remover os testes cross-day e trocar o de `saveDraft`**

Substituir os três testes das linhas 237–269 (`reports an overnight shift that conflicts with the next day, including Sunday to Monday`, `does not report a next-day shift that starts after the overnight shift ends`, `blocks saveDraft when there is a cross-day conflict`) por:

```ts
  it('saves a schedule whose overnight shift overlaps the next day', async () => {
    await load([
      { dayOfWeek: 0, isOpen: true, shifts: [{ startTime: '23:00', endTime: '01:00' }] },
      { dayOfWeek: 1, isOpen: true, shifts: [{ startTime: '00:30', endTime: '02:00' }] },
    ]);

    await component.saveDraft();

    expect(storeServiceMock.upsertStoreBusinessHours).toHaveBeenCalledTimes(1);
    const [, request] = storeServiceMock.upsertStoreBusinessHours.mock.calls[0] as [
      string,
      { items: Array<{ dayOfWeek: number; shifts: Array<{ startTime: string; endTime: string }> }> },
    ];
    expect(request.items.find(item => item.dayOfWeek === 0)?.shifts)
      .toEqual([{ startTime: '23:00', endTime: '01:00' }]);
    expect(request.items.find(item => item.dayOfWeek === 1)?.shifts)
      .toEqual([{ startTime: '00:30', endTime: '02:00' }]);
    expect(toastServiceMock.showGrouped).not.toHaveBeenCalled();
    expect(component.hasChanges()).toBe(false);
  });
```

- [ ] **Step 3: Rodar o spec e confirmar que falha**

From `frontend/`:

```
npx jest --no-coverage src/app/shared/state/store-hours.state.spec.ts
```

Expected: FAIL em `accepts an overnight overlap between an early and a late shift on the same day`, `accepts two overlapping shifts on the same day` e `saves a schedule whose overnight shift overlaps the next day`; os dois testes removidos não aparecem mais no relatório.

- [ ] **Step 4: Remover o laço de sobreposição de `validateDay`**

Substituir as linhas 510–536 por:

```ts
  validateDay(dayId: string): string | null {
    const day = this.schedule()[dayId];
    if (!day) return null;
    if (day.isOpen && day.shifts.length > this.maxShiftsPerDay) {
      return `Máximo de ${this.maxShiftsPerDay} turnos por dia.`;
    }
    if (day.isOpen && day.shifts.length === 0) return 'Adicione pelo menos um turno ou feche o dia.';
    if (!day.isOpen) return null;
    const incomplete = day.shifts.some(s => !s.startTime || !s.endTime);
    if (incomplete) return 'Preencha início e fim de todos os turnos.';
    for (const shift of day.shifts) {
      if (!this.isValidTime(shift.startTime) || !this.isValidTime(shift.endTime)) {
        return 'Informe horários válidos no formato HH:MM.';
      }
      if (shift.startTime === shift.endTime) {
        return 'Início e fim do turno não podem ser iguais.';
      }
    }
    return null;
  }
```

- [ ] **Step 5: Remover a chamada cross-day de `collectScheduleErrors`**

Em `collectScheduleErrors()`, remover a linha `errors.push(...this.validateCrossDayConflicts());`, deixando:

```ts
  private collectScheduleErrors(): string[] {
    const errors: string[] = [];
    for (const day of WEEKDAYS) {
      const err = this.validateDay(day.id);
      if (err) errors.push(`${day.label}: ${err}`);
    }
    return errors;
  }
```

- [ ] **Step 6: Apagar os helpers sem consumidores, nesta ordem**

Nesta ordem, para o arquivo continuar compilando a cada passo:

1. Apagar `conflictsWithDay` (linhas 475–498). Seu único consumidor era `confirmCopy`, já reescrito na Task 2.
2. Apagar `getNextDayId` (linhas 500–503) e `getPreviousDayId` (linhas 505–508). Eram usados apenas por `conflictsWithDay`.
3. Apagar `shiftsOverlap` (linhas 538–548). Era usado por `conflictsWithDay` e pelo laço de sobreposição removido de `validateDay`.
4. Apagar `validateCrossDayConflicts` (linhas 550–572). Era chamado apenas por `collectScheduleErrors`, ajustado no Step 5.

Não apagar `isOvernight` nem `isOvernightShift`: continuam usados pelo selo de turno que vira o dia nos templates.

- [ ] **Step 7: Rodar o spec e confirmar que passa**

```
npx jest --no-coverage src/app/shared/state/store-hours.state.spec.ts
```

Expected: PASS, incluindo `limits each day to five shifts`, `rejects a shift whose start time equals its end time`, `rejects time values that are not backend-compatible` e `preserves backend time values with seconds and fractions when loading`.

- [ ] **Step 8: Confirmar que não restou referência morta**

```
git grep -n "validateCrossDayConflicts\|conflictsWithDay\|shiftsOverlap\|getNextDayId\|getPreviousDayId" -- frontend/src
```

Expected: nenhuma saída.

- [ ] **Step 9: Commit**

```
git add frontend/src/app/shared/state/store-hours.state.ts frontend/src/app/shared/state/store-hours.state.spec.ts
git commit -m "feat: drop shift overlap validation from store hours state"
```

**Critérios de aceite da Task 3:**
- [ ] `validateDay` mantém, nesta ordem: limite de 5 turnos, dia aberto sem turno, turno incompleto, formato `HH:mm`, início igual ao fim; não acusa sobreposição.
- [ ] `saveDraft()` envia payload com sobreposição same-day e cross-day.
- [ ] `validateScheduleForSubmit()` (usado pelo `goNext()` do wizard) não contém validação cross-day.
- [ ] `isOvernight`, `canAddShift`, `addShift`, `weekDays`, `copyTargetDays`, `openCopyModal`, `closeCopyModal` e `toggleCopyTarget` inalterados.

---

### Task 4: Nota de substituição no modal do wizard

**Files:**
- Modify: `frontend/src/app/features/store-config/hours/store-hours-page.component.html` (linhas 175–180)
- Modify: `frontend/src/app/features/store-config/hours/store-hours-page.component.scss` (bloco `.copy-modal-desc`, linhas 470–476)
- Test: `frontend/src/app/features/store-config/hours/store-hours-page.component.spec.ts`

**Interfaces:**
- Consumes: `copySourceShift()`, `copySourceShiftNumber()`, `copySourceDayLabel()`, `copyTargetDays()`, `copyTargets()`, `toggleCopyTarget(dayId)`, `confirmCopy()`, `closeCopyModal()`.
- Produces: parágrafo `.copy-modal-desc` contendo `.copy-modal-note` com o texto `Os horários atuais dos dias selecionados serão substituídos.`; nenhuma mudança de API pública da classe.

- [ ] **Step 1: Adicionar os testes do wizard**

Em `store-hours-page.component.spec.ts`, adicionar os imports no topo do arquivo:

```ts
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
```

E adicionar dois testes antes do fechamento do `describe('StoreHoursPageComponent', ...)`:

```ts
  it('replaces every shift of the destination with the copied shift', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;
    setMondayShifts(fixture, [{ startTime: '11:00', endTime: '13:00' }]);
    component.schedule.update(schedule => ({
      ...schedule,
      terca: {
        isOpen: true,
        shifts: [
          { startTime: '08:00', endTime: '09:00' },
          { startTime: '15:00', endTime: '18:00' },
        ],
      },
    }));
    fixture.detectChanges();

    component.openCopyModal('segunda', 0);
    component.toggleCopyTarget('terca');
    component.confirmCopy();

    expect(component.schedule().terca.shifts.map(shift => `${shift.startTime}-${shift.endTime}`))
      .toEqual(['11:00-13:00']);
    expect(component.schedule().segunda.shifts).toHaveLength(1);
  });

  it('tells the user that the selected days will be replaced', () => {
    const template = readFileSync(resolve(__dirname, 'store-hours-page.component.html'), 'utf8');

    expect(template).toContain('Os horários atuais dos dias selecionados serão substituídos.');
    expect(template).toContain('class="copy-modal-note"');
  });
```

- [ ] **Step 2: Rodar o spec e confirmar a falha**

From `frontend/`:

```
npx jest --no-coverage src/app/features/store-config/hours/store-hours-page.component.spec.ts
```

Expected: `replaces every shift of the destination with the copied shift` PASS (integração com a Task 2) e `tells the user that the selected days will be replaced` FAIL, porque a nota ainda não existe no template.

- [ ] **Step 3: Adicionar a nota ao template**

Substituir o parágrafo (linhas 176–180) por:

```html
        <p class="copy-modal-desc">
          @if (copySourceShift()) {
            Copiar turno <strong>{{ copySourceShiftNumber() }}</strong> de <strong>{{ copySourceDayLabel() }}</strong> para:
            <span class="copy-modal-note">Os horários atuais dos dias selecionados serão substituídos.</span>
          }
        </p>
```

- [ ] **Step 4: Estilizar a nota com token existente**

No `store-hours-page.component.scss`, substituir o bloco `.copy-modal-desc` (linhas 470–476) por:

```scss
.copy-modal-desc {
  font-size: 14px;
  color: var(--app-ink);
  margin: 0 0 14px;

  strong { color: var(--app-brand); }

  .copy-modal-note {
    display: block;
    margin-top: 6px;
    font-size: 12px;
    line-height: 1.4;
    color: var(--app-ink-soft);
  }
}
```

- [ ] **Step 5: Rodar o spec e confirmar que passa**

```
npx jest --no-coverage src/app/features/store-config/hours/store-hours-page.component.spec.ts
```

Expected: PASS em todos os testes, incluindo os testes de SCSS existentes (`renders one copy action per shift...`, `disables every per-shift copy action while sync all is enabled`).

- [ ] **Step 6: Commit**

```
git add frontend/src/app/features/store-config/hours/store-hours-page.component.html frontend/src/app/features/store-config/hours/store-hours-page.component.scss frontend/src/app/features/store-config/hours/store-hours-page.component.spec.ts
git commit -m "feat: warn about shift replacement in hours wizard"
```

**Critérios de aceite da Task 4:**
- [ ] O modal do wizard exibe a linha de substituição abaixo de `Copiar turno N de <Dia> para:`.
- [ ] `aria-label` dos botões de cópia, checkboxes, botões `Cancelar`/`Copiar turno` e alvos de toque >= 44px inalterados.
- [ ] Nenhuma classe nova fora de `.copy-modal-note`; nenhum token novo.

---

### Task 5: Nota de substituição no modal do dashboard

**Files:**
- Modify: `frontend/src/app/features/seller-hours/seller-hours-page.component.html` (linhas 190–195)
- Modify: `frontend/src/app/features/seller-hours/seller-hours-page.component.scss` (após `.copy-modal-desc strong`, linha 363)
- Test: `frontend/src/app/features/seller-hours/seller-hours-page.component.spec.ts`

**Interfaces:**
- Consumes: as mesmas propriedades do estado compartilhado usadas na Task 4.
- Produces: `.copy-modal-desc` do dashboard contendo `.copy-modal-note` com o texto de substituição.

- [ ] **Step 1: Adicionar os testes do dashboard**

Em `seller-hours-page.component.spec.ts`, substituir o teste `copies only the first shift of a two-shift day from its row action` (linhas 179–223) pelo mesmo teste com a nova expectativa:

```ts
  it('copies only the first shift of a two-shift day from its row action', async () => {
    const fixture = TestBed.createComponent(SellerHoursPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();

    const component = fixture.componentInstance;
    component.schedule.update(s => ({
      ...s,
      segunda: {
        isOpen: true,
        shifts: [
          { startTime: '08:00', endTime: '10:00' },
          { startTime: '11:00', endTime: '13:00' },
        ],
      },
      terca: {
        isOpen: true,
        shifts: [{ startTime: '20:00', endTime: '22:00' }],
      },
    }));
    fixture.detectChanges();

    const segundaRow = fixture.nativeElement.querySelectorAll('.settings-row')[0] as HTMLElement;
    const copyButtons = Array.from(segundaRow.querySelectorAll('.copy-action')) as HTMLButtonElement[];
    expect(copyButtons).toHaveLength(2);
    expect(copyButtons.map(button => button.getAttribute('aria-label'))).toEqual([
      'Copiar turno 1 de Segunda-feira',
      'Copiar turno 2 de Segunda-feira',
    ]);
    expect(copyButtons.every(button => button.type === 'button')).toBe(true);

    copyButtons[0].click();
    fixture.detectChanges();

    expect(component.copySourceShift()).toMatchObject({ dayId: 'segunda', shiftIndex: 0 });
    expect(component.copySourceShiftNumber()).toBe(1);
    expect(component.copySourceDayLabel()).toBe('Segunda-feira');

    component.toggleCopyTarget('terca');
    component.confirmCopy();

    expect(component.schedule().terca.shifts.map(shift => `${shift.startTime}-${shift.endTime}`))
      .toEqual(['08:00-10:00']);
    expect(component.schedule().segunda.shifts).toHaveLength(2);
  });
```

Substituir o teste `copies the third shift individually and reports the conflicting destination` (linhas 225–273) por:

```ts
  it('copies the third shift individually and replaces each selected destination', async () => {
    const fixture = TestBed.createComponent(SellerHoursPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();

    const component = fixture.componentInstance;
    component.schedule.update(s => ({
      ...s,
      segunda: {
        isOpen: true,
        shifts: [
          { startTime: '08:00', endTime: '09:00' },
          { startTime: '10:00', endTime: '11:00' },
          { startTime: '14:00', endTime: '16:00' },
        ],
      },
      terca: {
        isOpen: true,
        shifts: [{ startTime: '15:00', endTime: '18:00' }],
      },
      quarta: {
        isOpen: true,
        shifts: [{ startTime: '20:00', endTime: '22:00' }],
      },
    }));
    fixture.detectChanges();

    const segundaRow = fixture.nativeElement.querySelectorAll('.settings-row')[0] as HTMLElement;
    const copyButtons = segundaRow.querySelectorAll('.copy-action') as NodeListOf<HTMLButtonElement>;
    expect(copyButtons).toHaveLength(3);

    copyButtons[2].click();
    fixture.detectChanges();

    expect(component.copySourceShiftNumber()).toBe(3);
    component.toggleCopyTarget('terca');
    component.toggleCopyTarget('quarta');
    component.confirmCopy();

    expect(component.schedule().quarta.shifts.map(shift => `${shift.startTime}-${shift.endTime}`))
      .toEqual(['14:00-16:00']);
    expect(component.schedule().terca.shifts.map(shift => `${shift.startTime}-${shift.endTime}`))
      .toEqual(['14:00-16:00']);
    expect(component.schedule().segunda.shifts).toHaveLength(3);
    expect(toastServiceMock.showGrouped).not.toHaveBeenCalled();
    expect(toastServiceMock.showInfo).toHaveBeenCalledWith('Turno 3 copiado para 2 dias.');
  });
```

Adicionar, antes do fechamento do `describe('SellerHoursPageComponent', ...)`:

```ts
  it('tells the user that the selected days will be replaced', () => {
    const template = readFileSync(resolve(__dirname, 'seller-hours-page.component.html'), 'utf8');

    expect(template).toContain('Os horários atuais dos dias selecionados serão substituídos.');
    expect(template).toContain('class="copy-modal-note"');
  });
```

`readFileSync` e `resolve` já estão importados neste spec (linhas 4–5).

- [ ] **Step 2: Rodar o spec e confirmar a falha**

From `frontend/`:

```
npx jest --no-coverage src/app/features/seller-hours/seller-hours-page.component.spec.ts
```

Expected: os dois testes de cópia PASS (integração com a Task 2) e `tells the user that the selected days will be replaced` FAIL, porque a nota ainda não existe no template.

- [ ] **Step 3: Adicionar a nota ao template**

Substituir o parágrafo (linhas 191–195) por:

```html
        <p class="copy-modal-desc">
          @if (copySourceShift()) {
            Copiar turno <strong>{{ copySourceShiftNumber() }}</strong> de <strong>{{ copySourceDayLabel() }}</strong> para:
            <span class="copy-modal-note">Os horários atuais dos dias selecionados serão substituídos.</span>
          }
        </p>
```

- [ ] **Step 4: Estilizar a nota com token do dashboard**

Em `seller-hours-page.component.scss`, após a linha `.copy-modal-desc strong { color: var(--hours-orange); }`, adicionar:

```scss
.copy-modal-note {
  display: block;
  margin-top: 6px;
  font-size: 12px;
  line-height: 1.4;
  color: var(--hours-muted);
}
```

- [ ] **Step 5: Rodar o spec e confirmar que passa**

```
npx jest --no-coverage src/app/features/seller-hours/seller-hours-page.component.spec.ts
```

Expected: PASS em todos os testes, incluindo os testes que leem o SCSS (`lays each day out as a compact grid...`, `keeps desktop time fields wide enough...`, `stacks the shift actions below two full-width fields on mobile`).

- [ ] **Step 6: Commit**

```
git add frontend/src/app/features/seller-hours/seller-hours-page.component.html frontend/src/app/features/seller-hours/seller-hours-page.component.scss frontend/src/app/features/seller-hours/seller-hours-page.component.spec.ts
git commit -m "feat: warn about shift replacement in hours dashboard"
```

**Critérios de aceite da Task 5:**
- [ ] O modal do dashboard exibe a mesma nota de substituição.
- [ ] Tema bordeaux preservado (`--hours-*`), nenhum token novo, nenhum breakpoint alterado.
- [ ] `aria-label`, ordem dos botões e alvos de toque inalterados.

---

### Task 6: Verificação final

**Files:**
- Review only: os dez arquivos do File Map

- [ ] **Step 1: Rodar os specs frontend direcionados**

From `frontend/`:

```
npx jest --no-coverage src/app/shared/state/store-hours.state.spec.ts src/app/features/store-config/hours/store-hours-page.component.spec.ts src/app/features/seller-hours/seller-hours-page.component.spec.ts
```

Expected: PASS, 0 falhas.

- [ ] **Step 2: Rodar a suíte frontend completa**

```
npx jest --no-coverage
```

Expected: PASS. Se houver falha em `branding-consistency.spec.ts`, corrigir apenas o texto novo (a marca não é citada, então não deve ocorrer).

- [ ] **Step 3: Rodar o build de produção Angular**

```
npx ng build --configuration production
```

Expected: `Application bundle generation complete` sem novos warnings/erros.

- [ ] **Step 4: Rodar testes unitários backend**

From the repository root:

```
dotnet test backend/tests/Urbeat.UnitTests
```

Expected: PASS.

- [ ] **Step 5: Rodar testes de integração backend**

```
dotnet test backend/tests/Urbeat.IntegrationTests
```

Expected: PASS (os testes `Relational/*` se auto-ignoram sem Docker; nenhum payload deles tem sobreposição).

- [ ] **Step 6: Compilar a solução**

```
dotnet build backend/Urbeat.sln
```

Expected: `Build succeeded`.

- [ ] **Step 7: Verificar whitespace e escopo do diff**

```
git diff --check
git status --porcelain
```

Expected: `git diff --check` sem saída; `git status` sem arquivos novos fora de `frontend/src/app/shared/state/`, `frontend/src/app/features/store-config/hours/`, `frontend/src/app/features/seller-hours/` e `backend/src|tests`.

- [ ] **Step 8: Confirmar escopo intocado**

```
git diff --name-only
```

Expected: exatamente os dez arquivos listados no File Map. Nenhum diff em `backend/src/Urbeat.Domain/**`, `backend/src/Urbeat.Infrastructure/**`, `backend/src/Urbeat.WebApi/**`, `backend/tests/Urbeat.IntegrationTests/**`, `postman/`, `docker/`, `scripts/`, `Documentacao/` ou migrations.

**Critérios de aceite da Task 6:**
- [ ] `PUT /api/stores/{storeId}/business-hours` aceita payload com sobreposição same-day e cross-day, mantendo 400 para `Items` vazio, dia duplicado, dia aberto sem turno, mais de 5 turnos, tempos nulos e início igual ao fim.
- [ ] `StoreOpeningHoursCalculator` sem diff e testes de `IsOpenNow` passando.
- [ ] Nenhuma alteração em PWA/Capacitor, rotas, deploy, secrets ou DOCX.
