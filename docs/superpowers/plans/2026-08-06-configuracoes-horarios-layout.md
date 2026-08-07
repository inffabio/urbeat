# Configuracoes de Horarios Layout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reproduzir fielmente o layout de horarios da referencia HTML com inputs compactos, lixeira ao fim de cada turno e copia no extremo direito de cada dia.

**Architecture:** A logica de negocio permanece em `StoreHoursPageComponent`; somente a estrutura de apresentacao do template sera agrupada por turno e o SCSS passara a usar uma grade explicita. Um teste de componente verificara a semantica e a posicao estrutural das acoes antes da alteracao visual.

**Tech Stack:** Angular 20 standalone, Ionic 8, Bootstrap 5 utilities, Jest, SCSS.

## Global Constraints

- Preservar carregamento, persistencia, validacao, copia, cancelamento e fluxo do wizard.
- Inputs de horario devem ter largura constante de 100 a 112px no desktop.
- Cada turno segue `Abertura | Fechamento | Lixeira`.
- Copiar permanece na ultima coluna da linha do dia.
- Alvos interativos devem ter no minimo 44px no mobile.
- Nao introduzir overflow horizontal.
- Usar tokens `--app-*`/tokens existentes do dashboard e Plus Jakarta Sans conforme a aplicacao atual.
- Nao criar commit sem solicitacao explicita do usuario.

---

### Task 1: Contrato Estrutural dos Turnos

**Files:**
- Create: `frontend/src/app/features/store-config/hours/store-hours-page.component.spec.ts`
- Test: `frontend/src/app/features/store-config/hours/store-hours-page.component.spec.ts`

**Interfaces:**
- Consumes: `StoreHoursPageComponent.schedule`, `addShift`, `removeShift` e `openCopyModal`.
- Produces: contrato DOM com `.shift-group`, `.remove-shift` dentro do grupo e `.day-actions` como ultimo filho da linha.

- [ ] **Step 1: Criar o teste que descreve a estrutura desejada**

```typescript
it('groups each shift and keeps copy as the final day action', () => {
  const fixture = TestBed.createComponent(StoreHoursPageComponent);
  fixture.detectChanges();
  fixture.componentInstance.schedule.update(schedule => ({
    ...schedule,
    segunda: {
      isOpen: true,
      shifts: [
        { startTime: '11:00', endTime: '14:30' },
        { startTime: '18:00', endTime: '23:00' },
      ],
    },
  }));
  fixture.detectChanges();

  const monday = fixture.nativeElement.querySelector('.hours-row');
  const groups = monday.querySelectorAll('.shift-group');

  expect(groups).toHaveLength(2);
  expect(groups[1].querySelector('.remove-shift')).not.toBeNull();
  expect(monday.lastElementChild).toHaveClass('day-actions');
});
```

- [ ] **Step 2: Executar o teste e confirmar a falha correta**

Run: `npx jest --no-coverage src/app/features/store-config/hours/store-hours-page.component.spec.ts`

Expected: FAIL porque `.shift-group` ainda nao existe.

- [ ] **Step 3: Manter o teste falhando ate a Task 2**

Nao alterar o componente nesta tarefa. A falha comprova que o teste detecta a estrutura ausente.

---

### Task 2: Marcacao Fiel da Linha de Horarios

**Files:**
- Modify: `frontend/src/app/features/store-config/hours/store-hours-page.component.html:28-114`
- Test: `frontend/src/app/features/store-config/hours/store-hours-page.component.spec.ts`

**Interfaces:**
- Consumes: contrato `.shift-group` criado na Task 1.
- Produces: grupos de turnos independentes e acao de copia fora do fluxo flexivel dos turnos.

- [ ] **Step 1: Agrupar os campos de cada turno**

Substituir o corpo do `@for (shift ...)` por uma estrutura equivalente a:

```html
@if (i > 0) {
  <span class="interval-label">Intervalo</span>
}
<div class="shift-group">
  <div class="time-field">...</div>
  <div class="time-field">...</div>
  @if (schedule()[day.id].shifts.length > 1) {
    <button type="button" class="btn btn-sm remove-shift" (click)="removeShift(day.id, i)">
      <ion-icon name="trash-outline" aria-hidden="true"></ion-icon>
    </button>
  }
</div>
```

Preservar integralmente ids, labels, `ngModel`, eventos e atributos ARIA atuais.

- [ ] **Step 2: Manter adicionar turno dentro da area de turnos**

```html
<button type="button" class="btn btn-link add-shift-link" (click)="addShift(day.id)">
  <ion-icon name="add-outline" aria-hidden="true"></ion-icon>
  Adicionar turno
</button>
```

- [ ] **Step 3: Manter copiar como ultimo filho de `.hours-row`**

O botao `.day-actions` deve continuar depois da area aberta/fechada, sem ser movido para `.day-shifts`.

- [ ] **Step 4: Executar o teste estrutural**

Run: `npx jest --no-coverage src/app/features/store-config/hours/store-hours-page.component.spec.ts`

Expected: PASS.

---

### Task 3: Grade Visual e Responsividade

**Files:**
- Modify: `frontend/src/app/features/store-config/hours/store-hours-page.component.scss:7-451`
- Test: `frontend/src/app/features/store-config/hours/store-hours-page.component.spec.ts`

**Interfaces:**
- Consumes: `.shift-group`, `.day-shifts`, `.interval-label` e `.day-actions` da Task 2.
- Produces: layout compacto equivalente a `configuracoes-horarios.html`.

- [ ] **Step 1: Definir a grade desktop da linha**

```scss
.hours-row {
  display: grid;
  grid-template-columns: 156px minmax(0, 1fr) 44px;
  align-items: center;
  gap: 16px;
  min-height: 104px;
  padding: 18px 24px;
}

.day-shifts {
  display: flex;
  align-items: flex-end;
  gap: 12px;
  min-width: 0;
  flex-wrap: wrap;
}

.shift-group {
  display: grid;
  grid-template-columns: repeat(2, 106px) 44px;
  align-items: end;
  gap: 10px;
}
```

- [ ] **Step 2: Impedir crescimento dos inputs**

```scss
.time-field {
  width: 106px;
  min-width: 0;
  flex: none;
}

.time-control {
  width: 106px;
  height: 40px;
}
```

- [ ] **Step 3: Alinhar lixeira e copia**

```scss
.remove-shift,
.day-actions {
  width: 44px;
  height: 44px;
}

.remove-shift {
  align-self: end;
}

.day-actions {
  justify-self: end;
}
```

- [ ] **Step 4: Adaptar tablet e mobile sem overflow**

```scss
@media (max-width: 1000px) {
  .hours-row { grid-template-columns: 140px minmax(0, 1fr) 44px; }
  .shift-group { grid-template-columns: repeat(2, minmax(100px, 106px)) 44px; }
}

@media (max-width: 720px) {
  .hours-row { grid-template-columns: minmax(0, 1fr) 44px; padding: 16px; }
  .day-shifts { grid-column: 1 / -1; }
  .shift-group { width: 100%; grid-template-columns: repeat(2, minmax(0, 1fr)) 44px; }
  .time-field,
  .time-control { width: 100%; }
  .day-actions { grid-column: 2; grid-row: 1; }
}
```

- [ ] **Step 5: Conferir card principal e cards auxiliares contra a referencia**

Manter titulo e descricao com `24px` de padding, separadores entre dias, acoes no rodape direito e quatro cards `col-xl-3` abaixo. Remover qualquer combinacao local de borda e sombra no mesmo card, conforme `DESIGN.md`.

- [ ] **Step 6: Executar testes e build**

Run: `npx jest --no-coverage src/app/features/store-config/hours/store-hours-page.component.spec.ts`

Expected: PASS.

Run: `npx ng build --configuration production`

Expected: build concluido; apenas o warning conhecido de budget pode permanecer.

- [ ] **Step 7: Executar detector visual**

Run: `node C:/Projetos/urbeat/.opencode/skills/impeccable/scripts/detect.mjs --json frontend/src/app/features/store-config/hours/store-hours-page.component.html frontend/src/app/features/store-config/hours/store-hours-page.component.scss`

Expected: `[]` ou apenas achados explicados e corrigidos antes da entrega.
