# Cópia de turno com merge por posição Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Revisão 2026-11-13:** a regra anterior ("substituir todos os turnos do destino") foi substituída por **merge por posição**. O backend e a remoção das validações de sobreposição já estão implementados e commitados em `0f6caa3 feat: secure admin recovery and replace copied shifts`; esta revisão corrige apenas a semântica da cópia no frontend e o texto do modal.

**Goal:** Fazer a cópia de um turno individual aplicar o horário copiado **somente na posição correspondente** de cada dia de destino aberto, preservando os demais turnos e criando turnos intermediários vazios quando a posição ainda não existir, mantendo o limite de 5 turnos e a permissão de sobreposição.

**Architecture:** Duas mudanças coordenadas no frontend. (1) `StoreHoursState.confirmCopy()` deixa de substituir `shifts` e passa a fazer merge por índice: preserva a lista, sobrescreve `startTime`/`endTime` da posição existente (mantendo `key`/`id`), anexa intermediários vazios até a posição e rejeita a cópia quando o índice excede `maxShiftsPerDay`. (2) Os dois templates que compartilham o estado (wizard e dashboard) trocam a nota do modal de substituição para merge. Backend, contrato HTTP, persistência, migrations, `StoreOpeningHoursCalculator`, Postman, DOCX e deploy não mudam.

**Tech Stack:** Angular 20 standalone + Signals + Ionic 8 + TypeScript strict + Jest; .NET 9 + FluentValidation + xUnit + FluentAssertions (sem alteração nesta revisão).

## Status herdado (não reexecutar)

Já implementado e commitado; **não alterar** nesta revisão:

- `backend/src/Urbeat.Application/Validators/UpsertStoreBusinessHoursRequestDtoValidator.cs` sem `HasOverlappingShifts`, sem `HaveNoCrossDayOvernightConflicts` e sem `ShiftsOverlap`; mensagens `"Shifts cannot overlap within the same day."` e `"Overnight shifts cannot overlap the next day's shifts."` não existem mais.
- `frontend/src/app/shared/state/store-hours.state.ts` sem `conflictsWithDay`, `shiftsOverlap`, `getNextDayId`, `getPreviousDayId` e `validateCrossDayConflicts`; `validateDay` sem sobreposição e `collectScheduleErrors` sem validação cross-day.
- `backend/tests/Urbeat.UnitTests/Application/Validators/UpsertStoreBusinessHoursRequestDtoValidatorTests.cs` já cobre aceite de sobreposição e as regras mantidas.
- `confirmCopy()` atual substitui todos os turnos do destino (comportamento que esta revisão corrige) e `reportCopyResult(shiftIndex, copiedCount)` já não emite avisos.
- Ambos os modais exibem `.copy-modal-note` com o texto antigo de substituição total.

## Global Constraints

- Contrato HTTP inalterado: `PUT /api/stores/{storeId}/business-hours` com `{ items: [{ dayOfWeek, isOpen, shifts: [{ startTime, endTime }] }] }`. Nenhum DTO, controller, mapper, entidade, `ApplicationDbContext`, migration, `IStoreBusinessHoursService`, `StoreBusinessHoursService.UpsertAsync` ou `StoreOpeningHoursCalculator` é alterado.
- A cópia parte de **um turno individual** (`copySourceShift().shiftIndex`, 0-based) e altera **apenas a posição desse índice** em cada destino aplicado.
- Turnos existentes do destino em outras posições são preservados, na mesma ordem; `key`/`id` da posição existente é preservado.
- Posição inexistente: anexar turnos até o índice. Intermediários usam `{ key: createShiftKey(), startTime: '', endTime: '' }`; o turno copiado recebe o horário do snapshot. A validação existente (`Preencha início e fim de todos os turnos.`) bloqueia `saveDraft()`/`goNext()` até o preenchimento e nenhum turno vazio é enviado à API.
- Guarda de limite: se `shiftIndex + 1 > maxShiftsPerDay`, rejeitar a cópia inteira com `Máximo de 5 turnos por dia.`, sem alterar destinos, sem `markDirty()` e mantendo o modal aberto.
- Sobreposição de horários continua permitida; nenhuma regra de sobreposição é reintroduzida.
- Nunca produzir aviso de `sobreposição` na cópia. Sucesso: `Turno N copiado para X dia(s).`; nenhum destino aplicado: `Nenhum dia recebeu o turno.`; nenhum selecionado: `Selecione pelo menos um dia.`
- Somente dias abertos e diferentes do dia de origem recebem cópia; destino fechado durante o modal é ignorado em silêncio.
- `sortShifts()` **não** é chamado em `confirmCopy()` (posição é conceito de UI; `buildRequest()` continua ordenando o payload por `startTime`, comportamento existente).
- Nota do modal (texto exato nos dois templates): `O horário substitui apenas o turno de mesmo número em cada dia; os outros turnos são mantidos.`
- Validações mantidas no frontend: máximo de 5 turnos por dia, dia aberto sem turno, turno incompleto, formato `HH:mm` / `HH:mm:ss[.f{1,7}]`, início igual ao fim, `canAddShift` bloqueado com horário inválido no turno anterior.
- Validações mantidas no backend: `Items` não vazio, `DayOfWeek` único, dia aberto com ao menos 1 turno, máximo de 5 turnos, `StartTime`/`EndTime` obrigatórios, início diferente do fim, turnos de dias fechados ignorados.
- Overnight continua permitido (`end < start`), inclusive `23:59 -> 00:00` e `00:00 -> 23:59`.
- Textos novos não citam marca; a marca continua minúscula `urbeat` onde já existe.
- Não alterar layout, breakpoints, rotas, `WizardFooterComponent`, `FormsModule`, PWA/Capacitor, alvos de toque (>= 44px) ou tema (`.urbeat-onboarding` no wizard, bordeaux no dashboard).
- Usar apenas tokens existentes; `.copy-modal-note` já está estilizado nos dois SCSS — nenhuma mudança de estilo é esperada.
- Não alterar `backend/tests/Urbeat.IntegrationTests/**`, `postman/`, `Documentacao/*.docx`, `docker/`, `scripts/` ou secrets.
- Timestamps e regras de fuso não são afetados; nada de `DateTime.Now` novo.
- Não incluir no commit alterações não relacionadas já existentes no working tree.

## File Map

| Arquivo | Responsabilidade | Ação |
| --- | --- | --- |
| `frontend/src/app/shared/state/store-hours.state.ts` | estado compartilhado de horários (wizard + dashboard) | modificar (`confirmCopy` merge por posição; `reportCopyResult` inalterado) |
| `frontend/src/app/shared/state/store-hours.state.spec.ts` | cobertura do estado compartilhado | reescrever o bloco `individual shift copy` |
| `frontend/src/app/features/store-config/hours/store-hours-page.component.html` | modal de cópia do wizard | trocar o texto de `.copy-modal-note` |
| `frontend/src/app/features/store-config/hours/store-hours-page.component.spec.ts` | cobertura do wizard | ajustar 2 testes de cópia + teste da nota |
| `frontend/src/app/features/seller-hours/seller-hours-page.component.html` | modal de cópia do dashboard | trocar o texto de `.copy-modal-note` |
| `frontend/src/app/features/seller-hours/seller-hours-page.component.spec.ts` | cobertura do dashboard | ajustar 2 testes de cópia + teste da nota |

Fora do mapa: `store-hours-page.component.ts`, `seller-hours-page.component.ts`, ambos `.scss`, rotas, serviços, DTOs, validador backend, persistência, migrations, storefront.

---

### Task 1: Reescrever a cobertura de merge por posição

**Files:**
- Test: `frontend/src/app/shared/state/store-hours.state.spec.ts` (bloco `individual shift copy`, linhas 422-794)

**Interfaces:**
- Consumes: `openCopyModal(dayId, shiftIndex)`, `toggleCopyTarget(dayId)`, `confirmCopy()`, `closeCopyModal()`, `schedule()`, `validateDay(dayId)`, `saveDraft()`, `hasChanges()`, `copySourceShift()`, `copySourceShiftNumber()`, `copySourceDayLabel()`, `copyTargets()`.
- Produces: nenhuma API nova; a suíte passa a descrever o merge por posição.

- [ ] **Step 1: Substituir o bloco `individual shift copy`**

Substituir integralmente `describe('individual shift copy', () => { ... })` por:

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

    function shiftSlots(dayId: string): Array<[string, string]> {
      return component.schedule()[dayId].shifts.map(shift => [shift.startTime, shift.endTime]);
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

    it('overwrites the matching position and preserves the other destination shifts', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00'], ['14:00', '18:00']] },
        { dayOfWeek: 2, shifts: [['19:00', '21:00'], ['22:00', '23:00']] },
      ));

      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(shiftTimes('terca')).toEqual(['09:00-12:00', '22:00-23:00']);
      expect(shiftTimes('segunda')).toEqual(['09:00-12:00', '14:00-18:00']);
      expect(component.hasChanges()).toBe(true);
      expect(component.isCopyModalOpen()).toBe(false);
    });

    it.each([0, 1, 2])('overwrites only the position at index %i', async (index) => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['08:00', '10:00'], ['11:00', '13:00'], ['14:00', '16:00']] },
        { dayOfWeek: 2, shifts: [['18:00', '19:00'], ['19:00', '20:00'], ['20:00', '21:00']] },
      ));

      component.openCopyModal('segunda', index);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      const source = ['08:00-10:00', '11:00-13:00', '14:00-16:00'];
      const expected = ['18:00-19:00', '19:00-20:00', '20:00-21:00'];
      expected[index] = source[index];
      expect(shiftTimes('terca')).toEqual(expected);
      expect(component.hasChanges()).toBe(true);
    });

    it('appends the copied shift as the second shift when the destination has only one', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00'], ['14:00', '18:00']] },
        { dayOfWeek: 2, shifts: [['08:00', '10:00']] },
      ));

      component.openCopyModal('segunda', 1);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(shiftSlots('terca')).toEqual([['08:00', '10:00'], ['14:00', '18:00']]);
      expect(component.hasChanges()).toBe(true);
    });

    it('creates empty intermediate shifts until the copied position', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['08:00', '10:00'], ['11:00', '13:00'], ['14:00', '16:00']] },
        { dayOfWeek: 2, shifts: [['18:00', '20:00']] },
      ));

      component.openCopyModal('segunda', 2);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(shiftSlots('terca')).toEqual([['18:00', '20:00'], ['', ''], ['14:00', '16:00']]);
      expect(component.validateDay('terca')).toBe('Preencha início e fim de todos os turnos.');
      expect(component.hasChanges()).toBe(true);
    });

    it('blocks saving until the intermediate shifts are filled', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['08:00', '10:00'], ['11:00', '13:00'], ['14:00', '16:00']] },
        { dayOfWeek: 2, shifts: [['18:00', '20:00']] },
      ));

      component.openCopyModal('segunda', 2);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      await component.saveDraft();

      expect(storeServiceMock.upsertStoreBusinessHours).not.toHaveBeenCalled();
      expect(toastServiceMock.showGrouped).toHaveBeenCalled();
      const [errors] = toastServiceMock.showGrouped.mock.calls[0] as [Array<{ type: string; text: string }>];
      expect(errors.some(error => error.text.includes('Preencha início e fim de todos os turnos.'))).toBe(true);
    });

    it('creates the first shift of an open destination that has none', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00']] },
        { dayOfWeek: 2, shifts: [] },
      ));

      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(shiftSlots('terca')).toEqual([['09:00', '12:00']]);
      expect(component.hasChanges()).toBe(true);
    });

    it('preserves the destination key and creates keys only for new positions', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00'], ['14:00', '18:00']] },
        { dayOfWeek: 2, shifts: [['08:00', '10:00']] },
      ));

      const sourceKeys = component.schedule().segunda.shifts.map(shift => shift.key);
      const destinationKeys = component.schedule().terca.shifts.map(shift => shift.key);

      component.openCopyModal('segunda', 1);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      const shifts = component.schedule().terca.shifts;
      expect(shifts).toHaveLength(2);
      expect(shifts[0].key).toBe(destinationKeys[0]);
      expect(shifts[1].key).toBeTruthy();
      expect(destinationKeys).not.toContain(shifts[1].key);
      expect(sourceKeys).not.toContain(shifts[1].key);
    });

    it('uses a snapshot of the shift taken when the modal was opened', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00'], ['14:00', '18:00']] },
        { dayOfWeek: 2, shifts: [['19:00', '21:00'], ['22:00', '23:00']] },
      ));

      component.openCopyModal('segunda', 0);
      component.updateTime('segunda', 0, 'startTime', '10:00');
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(shiftSlots('terca')).toEqual([['09:00', '12:00'], ['22:00', '23:00']]);
    });

    it('preserves backend time values with seconds and fractions', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['08:00:30.2500000', '12:30:45.5000000'], ['14:00', '18:00']] },
        { dayOfWeek: 2, shifts: [['19:00', '21:00']] },
      ));

      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      const destinationShifts = component.schedule().terca.shifts;
      expect(destinationShifts).toHaveLength(1);
      expect(destinationShifts[0].startTime).toBe('08:00:30.2500000');
      expect(destinationShifts[0].endTime).toBe('12:30:45.5000000');
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

    it('merges into each selected destination independently', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['09:00', '12:00'], ['14:00', '18:00']] },
        { dayOfWeek: 2, shifts: [['08:00', '10:00'], ['11:00', '13:00'], ['19:00', '22:00']] },
        { dayOfWeek: 3, shifts: [['10:00', '11:00']] },
      ));

      component.openCopyModal('segunda', 1);
      component.toggleCopyTarget('terca');
      component.toggleCopyTarget('quarta');
      component.confirmCopy();

      expect(shiftTimes('terca')).toEqual(['08:00-10:00', '14:00-18:00', '19:00-22:00']);
      expect(shiftTimes('quarta')).toEqual(['10:00-11:00', '14:00-18:00']);
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
      expect(shiftTimes('terca')).toEqual(['09:00-12:00', '22:00-23:00']);
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

    it('does not show grouped warnings when destinations are merged', async () => {
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

    it('overwrites the first position of a destination that already has an overnight shift', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['00:30', '02:00']] },
        { dayOfWeek: 2, shifts: [['23:00', '01:00'], ['02:00', '03:00']] },
      ));

      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(shiftTimes('terca')).toEqual(['00:30-02:00', '02:00-03:00']);
      expect(component.hasChanges()).toBe(true);
      expect(toastServiceMock.showGrouped).not.toHaveBeenCalled();
    });

    it('merges a copied overnight shift even when it overlaps the next day', async () => {
      await load(hours(
        { dayOfWeek: 1, shifts: [['23:00', '01:00']] },
        { dayOfWeek: 2, shifts: [['18:00', '20:00'], ['21:00', '22:00']] },
        { dayOfWeek: 3, shifts: [['00:30', '02:00']] },
      ));

      component.openCopyModal('segunda', 0);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(shiftTimes('terca')).toEqual(['23:00-01:00', '21:00-22:00']);
      expect(shiftTimes('quarta')).toEqual(['00:30-02:00']);
      expect(component.hasChanges()).toBe(true);
      expect(toastServiceMock.showGrouped).not.toHaveBeenCalled();
    });

    it('merges into Monday even when the Sunday overnight shift ends after midnight', async () => {
      await load(hours(
        { dayOfWeek: 0, shifts: [['23:00', '01:00']] },
        { dayOfWeek: 1, shifts: [['18:00', '20:00'], ['21:00', '22:00']] },
        { dayOfWeek: 3, shifts: [['00:30', '02:00']] },
      ));

      component.openCopyModal('quarta', 0);
      component.toggleCopyTarget('segunda');
      component.confirmCopy();

      expect(shiftTimes('segunda')).toEqual(['00:30-02:00', '21:00-22:00']);
      expect(component.hasChanges()).toBe(true);
      expect(toastServiceMock.showGrouped).not.toHaveBeenCalled();
    });

    it('merges when the previous day overnight shift ends before the copy starts', async () => {
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

    it('merges an overnight shift when the next day already has a shift', async () => {
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

    it('keeps five shifts and overwrites the copied position', async () => {
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

      expect(component.schedule().terca.shifts).toHaveLength(5);
      expect(shiftTimes('terca')).toEqual([
        '09:00-10:00', '12:00-13:00', '13:00-14:00', '14:00-15:00', '15:00-16:00',
      ]);
      expect(component.hasChanges()).toBe(true);
      expect(toastServiceMock.showGrouped).not.toHaveBeenCalled();
    });

    it('rejects the copy when the position exceeds the maximum of five shifts', async () => {
      await load(hours(
        {
          dayOfWeek: 1,
          shifts: [
            ['08:00', '09:00'], ['09:00', '10:00'], ['10:00', '11:00'],
            ['11:00', '12:00'], ['12:00', '13:00'], ['13:00', '14:00'],
          ],
        },
        { dayOfWeek: 2, shifts: [['10:00', '11:00']] },
      ));

      component.openCopyModal('segunda', 5);
      component.toggleCopyTarget('terca');
      component.confirmCopy();

      expect(toastServiceMock.showInfo).toHaveBeenCalledWith('Máximo de 5 turnos por dia.');
      expect(component.hasChanges()).toBe(false);
      expect(component.isCopyModalOpen()).toBe(true);
      expect(shiftTimes('terca')).toEqual(['10:00-11:00']);
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

- [ ] **Step 2: Rodar o spec e confirmar que falha**

From `frontend/`:

```
npx jest --no-coverage src/app/shared/state/store-hours.state.spec.ts
```

Expected: FAIL nos testes de merge (`overwrites the matching position...`, `overwrites only the position at index %i`, `appends the copied shift...`, `creates empty intermediate shifts...`, `blocks saving...`, `merges into each selected destination independently`, `keeps five shifts...`, `rejects the copy when the position exceeds...`), porque o código atual substitui todos os turnos do destino e não tem a guarda de limite.

---

### Task 2: Implementar o merge por posição em `confirmCopy()`

**Files:**
- Modify: `frontend/src/app/shared/state/store-hours.state.ts` (`confirmCopy` linhas 389-431)
- Test: `frontend/src/app/shared/state/store-hours.state.spec.ts`

**Interfaces:**
- Consumes: `copySourceShift()` → `CopySourceShift | null`; `copyTargets()` → `Set<string>`; `schedule()` → `Record<string, DaySchedule>`; `maxShiftsPerDay: number`; `createShiftKey()`; `markDirty()`; `closeCopyModal()`; `toast.showInfo(message: string)`.
- Produces: `confirmCopy(): void` com merge por posição; `reportCopyResult(shiftIndex: number, copiedCount: number): void` inalterado.

- [ ] **Step 1: Reescrever `confirmCopy()`**

Substituir as linhas 389-431 por:

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

    // Every destination needs a slot at the source index, so the copy cannot exceed the daily limit.
    if (source.shiftIndex + 1 > this.maxShiftsPerDay) {
      this.toast.showInfo(`Máximo de ${this.maxShiftsPerDay} turnos por dia.`);
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
        const merged = { ...s };
        for (const targetId of applied) {
          const shifts = [...s[targetId].shifts];
          // Keep every existing shift. Positions before the copied index must exist,
          // so missing ones are appended empty and block saving until filled.
          while (shifts.length < source.shiftIndex) {
            shifts.push({ key: createShiftKey(), startTime: '', endTime: '' });
          }
          const existing = shifts[source.shiftIndex];
          shifts[source.shiftIndex] = existing
            ? { ...existing, startTime: source.shift.startTime, endTime: source.shift.endTime }
            : { key: createShiftKey(), startTime: source.shift.startTime, endTime: source.shift.endTime };
          merged[targetId] = { isOpen: true, shifts };
        }
        return merged;
      });
      this.markDirty();
    }

    this.closeCopyModal();
    this.reportCopyResult(source.shiftIndex, applied.length);
  }
```

- [ ] **Step 2: Confirmar que `reportCopyResult` não muda**

`reportCopyResult(shiftIndex, copiedCount)` permanece exatamente como está (sucesso ou `Nenhum dia recebeu o turno.`). Não reintroduzir `showGrouped` nem parâmetros de aviso.

- [ ] **Step 3: Rodar o spec e confirmar que passa**

```
npx jest --no-coverage src/app/shared/state/store-hours.state.spec.ts
```

Expected: PASS. `sortShifts()` continua existindo e é usado apenas em carga/preset/sync/payload; nenhuma chamada em `confirmCopy()`.

- [ ] **Step 4: Commit**

```
git add frontend/src/app/shared/state/store-hours.state.ts frontend/src/app/shared/state/store-hours.state.spec.ts
git commit -m "fix: merge copied shift by position"
```

**Critérios de aceite da Task 2:**
- [ ] Cada destino aplicado preserva os turnos fora da posição copiada, na mesma ordem.
- [ ] Posição existente mantém `key`/`id` e recebe apenas `startTime`/`endTime`.
- [ ] Posição inexistente é criada com intermediários vazios e o turno copiado na posição correta.
- [ ] Índice acima de 4 rejeita a cópia com `Máximo de 5 turnos por dia.`, sem `hasChanges` e sem alterar destinos.
- [ ] Origem intacta; destino permanece `isOpen: true`.
- [ ] `showGrouped` não é chamado em nenhum caminho da cópia e nenhuma requisição HTTP é feita por `confirmCopy`.

---

### Task 3: Atualizar nota e testes do modal do wizard

**Files:**
- Modify: `frontend/src/app/features/store-config/hours/store-hours-page.component.html` (linha 179)
- Test: `frontend/src/app/features/store-config/hours/store-hours-page.component.spec.ts`

**Interfaces:**
- Consumes: `openCopyModal`, `toggleCopyTarget`, `confirmCopy`, `schedule`, `validateDay`, `setMondayShifts`, `mondayRow`, `createComponent` (helpers já existentes no spec).
- Produces: nota nova no template; cobertura do wizard alinhada ao merge.

- [ ] **Step 1: Atualizar os testes do wizard**

Substituir o teste `copies only the clicked shift to an open target day` (linhas 119-141) por:

```ts
  it('fills the copied position of an empty destination and leaves earlier positions pending', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;
    setMondayShifts(fixture, [
      { startTime: '08:00', endTime: '10:00' },
      { startTime: '11:00', endTime: '13:00' },
    ]);
    component.schedule.update(schedule => ({
      ...schedule,
      terca: { isOpen: true, shifts: [] },
    }));
    fixture.detectChanges();

    const copyButtons = mondayRow(fixture).querySelectorAll('.btn-copy-shift') as NodeListOf<HTMLButtonElement>;
    copyButtons[1].click();
    fixture.detectChanges();
    component.toggleCopyTarget('terca');
    component.confirmCopy();

    expect(component.schedule().terca.shifts.map(shift => [shift.startTime, shift.endTime]))
      .toEqual([['', ''], ['11:00', '13:00']]);
    expect(component.validateDay('terca')).toBe('Preencha início e fim de todos os turnos.');
    expect(component.schedule().segunda.shifts).toHaveLength(2);
  });
```

Substituir `replaces every shift of the destination with the copied shift` (linhas 143-166) por:

```ts
  it('overwrites only the matching position and keeps the other destination shifts', () => {
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

    expect(component.schedule().terca.shifts.map(shift => [shift.startTime, shift.endTime]))
      .toEqual([['11:00', '13:00'], ['15:00', '18:00']]);
    expect(component.schedule().segunda.shifts).toHaveLength(1);
  });
```

Substituir `tells the user that the selected days will be replaced` (linhas 168-173) por:

```ts
  it('tells the user that the other shifts are kept', () => {
    const template = readFileSync(resolve(__dirname, 'store-hours-page.component.html'), 'utf8');

    expect(template).toContain(
      'O horário substitui apenas o turno de mesmo número em cada dia; os outros turnos são mantidos.',
    );
    expect(template).toContain('class="copy-modal-note"');
  });
```

- [ ] **Step 2: Rodar o spec e confirmar a falha**

From `frontend/`:

```
npx jest --no-coverage src/app/features/store-config/hours/store-hours-page.component.spec.ts
```

Expected: `overwrites only the matching position...` e `fills the copied position...` PASS após a Task 2; `tells the user that the other shifts are kept` FAIL, porque o template ainda usa o texto antigo.

- [ ] **Step 3: Trocar o texto da nota no template**

Substituir a linha 179 por:

```html
            <span class="copy-modal-note">O horário substitui apenas o turno de mesmo número em cada dia; os outros turnos são mantidos.</span>
```

Não alterar estrutura, classes, `aria-label` ou ordem dos botões.

- [ ] **Step 4: Rodar o spec e confirmar que passa**

```
npx jest --no-coverage src/app/features/store-config/hours/store-hours-page.component.spec.ts
```

Expected: PASS em todos os testes, incluindo os testes que leem o SCSS e os `aria-label` dos botões de cópia.

- [ ] **Step 5: Commit**

```
git add frontend/src/app/features/store-config/hours/store-hours-page.component.html frontend/src/app/features/store-config/hours/store-hours-page.component.spec.ts
git commit -m "fix: describe shift merge in hours wizard copy modal"
```

**Critérios de aceite da Task 3:**
- [ ] O modal do wizard exibe a nota de merge abaixo de `Copiar turno N de <Dia> para:`.
- [ ] Nenhuma mudança de SCSS, layout, breakpoint ou alvo de toque.
- [ ] O teste do wizard comprova posição intermediária vazia bloqueando `validateDay`.

---

### Task 4: Atualizar nota e testes do modal do dashboard

**Files:**
- Modify: `frontend/src/app/features/seller-hours/seller-hours-page.component.html` (linha 194)
- Test: `frontend/src/app/features/seller-hours/seller-hours-page.component.spec.ts`

**Interfaces:**
- Consumes: as mesmas propriedades do estado compartilhado usadas na Task 3.

- [ ] **Step 1: Atualizar os testes do dashboard**

Renomear `copies only the first shift of a two-shift day from its row action` (linhas 179-223) para `copies the first shift from its row action and overwrites the destination position`, mantendo o corpo e as expectativas atuais (`terca` = `['08:00-10:00']`, `segunda` com 2 turnos), que continuam válidas porque o destino tem apenas 1 turno.

Substituir `copies the third shift individually and replaces each selected destination` (linhas 225-271) por:

```ts
  it('copies the third shift individually and fills the third position on each selected destination', async () => {
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

    expect(component.schedule().quarta.shifts.map(shift => [shift.startTime, shift.endTime]))
      .toEqual([['20:00', '22:00'], ['', ''], ['14:00', '16:00']]);
    expect(component.schedule().terca.shifts.map(shift => [shift.startTime, shift.endTime]))
      .toEqual([['15:00', '18:00'], ['', ''], ['14:00', '16:00']]);
    expect(component.schedule().segunda.shifts).toHaveLength(3);
    expect(toastServiceMock.showGrouped).not.toHaveBeenCalled();
    expect(toastServiceMock.showInfo).toHaveBeenCalledWith('Turno 3 copiado para 2 dias.');
  });
```

Substituir `tells the user that the selected days will be replaced` (linhas 273-278) por:

```ts
  it('tells the user that the other shifts are kept', () => {
    const template = readFileSync(resolve(__dirname, 'seller-hours-page.component.html'), 'utf8');

    expect(template).toContain(
      'O horário substitui apenas o turno de mesmo número em cada dia; os outros turnos são mantidos.',
    );
    expect(template).toContain('class="copy-modal-note"');
  });
```

`readFileSync` e `resolve` já estão importados neste spec.

- [ ] **Step 2: Rodar o spec e confirmar a falha**

From `frontend/`:

```
npx jest --no-coverage src/app/features/seller-hours/seller-hours-page.component.spec.ts
```

Expected: o teste do terceiro turno PASS após a Task 2; `tells the user that the other shifts are kept` FAIL, porque o template ainda usa o texto antigo.

- [ ] **Step 3: Trocar o texto da nota no template**

Substituir a linha 194 por:

```html
            <span class="copy-modal-note">O horário substitui apenas o turno de mesmo número em cada dia; os outros turnos são mantidos.</span>
```

- [ ] **Step 4: Rodar o spec e confirmar que passa**

```
npx jest --no-coverage src/app/features/seller-hours/seller-hours-page.component.spec.ts
```

Expected: PASS em todos os testes, incluindo os que leem o SCSS do dashboard.

- [ ] **Step 5: Commit**

```
git add frontend/src/app/features/seller-hours/seller-hours-page.component.html frontend/src/app/features/seller-hours/seller-hours-page.component.spec.ts
git commit -m "fix: describe shift merge in hours dashboard copy modal"
```

**Critérios de aceite da Task 4:**
- [ ] O modal do dashboard exibe a mesma nota de merge.
- [ ] Tema bordeaux e tokens `--hours-*` preservados; nenhuma mudança de SCSS.
- [ ] `aria-label`, ordem dos botões e alvos de toque inalterados.

---

### Task 5: Verificação final

**Files:**
- Review only: os seis arquivos do File Map

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

- [ ] **Step 4: Compilar a solução backend**

From the repository root:

```
dotnet build backend/Urbeat.sln
```

Expected: `Build succeeded` (nenhum arquivo backend alterado; serve como confirmação de que o contrato continua compilando).

- [ ] **Step 5: Confirmar ausência de referências mortas e de regressão de sobreposição**

```
git grep -n "validateCrossDayConflicts\|conflictsWithDay\|shiftsOverlap\|getNextDayId\|getPreviousDayId" -- frontend/src
git grep -n "Os horários atuais dos dias selecionados serão substituídos" -- frontend/src
```

Expected: nenhuma saída em ambos.

- [ ] **Step 6: Verificar whitespace e escopo do diff**

```
git diff --check
git status --porcelain
```

Expected: `git diff --check` sem saída; `git status` sem arquivos novos fora de `frontend/src/app/shared/state/`, `frontend/src/app/features/store-config/hours/` e `frontend/src/app/features/seller-hours/`.

- [ ] **Step 7: Confirmar escopo intocado**

```
git diff --name-only
```

Expected: exatamente os seis arquivos do File Map. Nenhum diff em `backend/**`, `postman/`, `docker/`, `scripts/`, `Documentacao/` ou migrations.

**Critérios de aceite da Task 5:**
- [ ] Cópia preserva os demais turnos e altera somente a posição copiada.
- [ ] Intermediários vazios bloqueiam `saveDraft()`/`goNext()` com `Preencha início e fim de todos os turnos.`
- [ ] Índice acima do limite é rejeitado sem alterar o destino.
- [ ] `PUT /api/stores/{storeId}/business-hours` e `StoreOpeningHoursCalculator` sem diff.
- [ ] Nenhuma alteração em PWA/Capacitor, rotas, deploy, secrets, DOCX, Postman ou backend.
