## ✅ Delivery requested

Based on the uploaded documents **Horarios.html** and **Horarios01.md**, plus the patches already defined:

- ❌ Remove autosave.
- ✅ Save only when clicking **“Avançar para Entrega”**.
- ✅ Keep the screen principles from the attached documents.
- ✅ Add the weekday chooser menu.
- ✅ Put the weekday chooser button inside **“Dias e horários”**, top-right.
- ✅ Button should be understandable: recommended **“+ Escolher dias ▼”** instead of only `V`.
- ✅ Menu shows only weekdays not already selected.
- ✅ Multiple selection allowed.
- ✅ Blue **“Selecionar”** button at the bottom of the dropdown.
- ✅ If selected days are sequential, create one grouped row.
- ✅ If selected days are not sequential, create separate rows.
- ✅ When deleting a row, those days become available again.

---

# 1. 👁️ Pre-visualization HTML with new functionalities

`Documentacao/FrontEnd/preview-horarios.html`

# 🍽️ Urbeat — Cadastro da Loja — Etapa 2: Horários

## 🎯 Objective

Implement the store schedule setup page using **Angular 20 + Ionic**, following the principles from the  documents:

- `Documentacao/FrontEnd/CadastroLoja/Horarios.html`
- `Documentacao/FrontEnd/CadastroLoja/Horarios01.md`
- `Documentacao/FrontEnd/CadastroLoja/Horarios02.md`
- `Documentacao/FrontEnd/CadastroLoja/Horarios03.md`

Apply the approved patches:

- Remove autosave.
- Save only when the user clicks **Avançar para Entrega**.
- Add weekday chooser dropdown inside the **Dias e horários** card.
- Dropdown must show only weekdays not already selected.
- Allow selecting multiple weekdays.
- Create grouped rows for sequential weekdays.
- Create individual rows for non-sequential weekdays.
- Deleted row days become available again in the dropdown.

---

# ✅ Main business rule override

The original attached specification mentions autosave.

For this implementation, autosave is removed.

## New save behavior

- All changes remain in local state.
- Nothing is persisted while editing.
- The page must save only when clicking:

```txt
Avançar para Entrega
```

## Required save handler

```ts
onSaveAndGoNext(): Promise<void>
```

This handler must:

1. Validate the current schedule.
2. Build a normalized payload.
3. Call the backend/service.
4. Navigate to `/cadastro/entrega` only after successful save.
5. Show loading state while saving.
6. Show error message if saving fails.

---

# 🧭 Page context

This screen is:

```txt
Etapa 2: Horários
```

Progress must display:

```txt
40% concluído
```

Stepper:

1. Loja
2. Horários
3. Entrega
4. Produtos
5. Publicar

The current step is **Horários**.

---

# 🧱 Recommended layout

## 1. Header / Stepper

Show:

- logo / brand
- onboarding stepper
- current step highlighted
- progress bar
- `40% concluído`

## 2. Intro block

Title:

```txt
Defina os horários e área de atendimento
```

Description:

```txt
Informe quando sua loja funciona e onde entrega seus pedidos.
```

## 3. Quick shortcuts

Title:

```txt
Atalhos rápidos
```

Subtitle:

```txt
Escolha um horário pré-definido ou personalize abaixo
```

Buttons:

- Comercial — `9h às 18h`
- Almoço — `11h às 16h`
- Jantar — `18h às 23h`
- 24 horas — `Todos os dias`
- Limpar horários — `Remover todos`

## 4. Auxiliary actions

Show:

- checkbox/toggle: `Aplicar o mesmo horário para todos os dias`
- button: `Copiar para outros dias`

## 5. Schedule card

Title:

```txt
Dias e horários
```

Subtitle:

```txt
Configure os intervalos de funcionamento da loja
```

Inside this card, top-right, show the weekday chooser button.

Recommended label:

```txt
+ Escolher dias ▼
```

If product strictly requires the previous `V` button, use:

```txt
Dias ▼
```

Do not use only `V`, because it is unclear for the user.

---

# 🔽 Weekday chooser dropdown behavior

## Button position

The weekday chooser button must stay:

- inside `Dias e horários` card
- top-right on desktop
- full width below the title on mobile

## Dropdown contents

When clicking the button, show an Ionic dropdown/popover.

The menu must show:

- Segunda
- Terça
- Quarta
- Quinta
- Sexta
- Sábado
- Domingo

But only weekdays that are not already selected in any row.

## Footer button

At the bottom of the menu, show a blue button:

```txt
Selecionar
```

Button is disabled while no weekday is checked.

## Multiple selection

The user may select more than one weekday.

## Sequential grouping rule

If selected weekdays are sequential, create one row.

Examples:

Selected:

```txt
Segunda, Terça, Quarta
```

Create:

```txt
Segunda a Quarta
```

Selected:

```txt
Sexta, Sábado
```

Create:

```txt
Sexta e Sábado
```

## Non-sequential rule

If selected weekdays are not sequential, create one row per weekday.

Example:

Selected:

```txt
Segunda, Quarta, Sexta
```

Create:

```txt
Segunda
Quarta
Sexta
```

## Deleted rows

When a row is deleted:

- remove the row from state
- all days in that row become available again in the dropdown

Example:

If `Segunda a Quinta` is deleted, dropdown must show:

```txt
Segunda
Terça
Quarta
Quinta
```

---

# 📅 Initial schedule state

```ts
scheduleRows = [
  {
    id: 'segunda-quinta',
    days: ['segunda', 'terca', 'quarta', 'quinta'],
    label: 'Segunda a Quinta',
    open: true,
    intervals: [
      { start: '11:00', end: '23:00' }
    ]
  },
  {
    id: 'sexta-sabado',
    days: ['sexta', 'sabado'],
    label: 'Sexta e Sábado',
    open: true,
    intervals: [
      { start: '11:00', end: '00:00' }
    ]
  },
  {
    id: 'domingo',
    days: ['domingo'],
    label: 'Domingo',
    open: false,
    intervals: []
  }
];
```

---

# 🗂️ Domain model

```ts
export type DayId =
  | 'segunda'
  | 'terca'
  | 'quarta'
  | 'quinta'
  | 'sexta'
  | 'sabado'
  | 'domingo';

export interface WeekDay {
  id: DayId;
  label: string;
  order: number;
}

export interface ScheduleInterval {
  start: string;
  end: string;
}

export interface ScheduleRow {
  id: string;
  days: DayId[];
  label: string;
  open: boolean;
  intervals: ScheduleInterval[];
}

export type PresetId =
  | 'comercial'
  | 'almoco'
  | 'jantar'
  | '24h';
```

---

# 🧠 Business rules

## Open a closed row

When a row is opened:

- `open = true`
- status becomes `Aberto`
- if intervals are empty, add:

```ts
{ start: '11:00', end: '23:00' }
```

## Close an open row

When a row is closed:

- `open = false`
- `intervals = []`
- show `Loja fechada`
- status becomes `Fechado`

## Apply preset

For presets:

- Comercial: `09:00 → 18:00`
- Almoço: `11:00 → 16:00`
- Jantar: `18:00 → 23:00`
- 24 horas: `00:00 → 23:59`

When clicking Comercial, Almoço or Jantar:

- apply to rows that include Monday to Thursday
- apply to rows that include Friday or Saturday
- do not automatically open Sunday

When clicking 24 horas:

- apply to Monday to Thursday
- apply to Friday and Saturday
- also open Sunday
- set Sunday to `00:00 → 23:59`

## Clear schedules

When clicking `Limpar horários`:

- remove active preset
- close all existing rows
- clear all intervals
- do not delete rows

## Copy from primary

When clicking `Copiar para outros dias`:

- use row containing `segunda` as source
- copy to rows containing `sexta` or `sabado`
- do not copy to Sunday
- show temporary feedback:

```txt
Copiado!
```

After 1.5 seconds, return to:

```txt
Copiar para outros dias
```

## Apply same hours

When the toggle is active:

- use row containing `segunda` as source
- synchronize to rows containing `sexta` or `sabado`
- Sunday is not affected unless product explicitly changes this rule

---

# 📈 Insights

Show three cards:

## Insight 1

```txt
Lojas abertas até 00h recebem em média 27% mais pedidos
```

Highlight:

```txt
27% mais pedidos
```

## Insight 2

Dynamic:

```txt
Sua loja ficará aberta Xh por semana
```

Must calculate from state.

Important:

- Do not hardcode `72h`.
- Initial example from uploaded HTML actually results in 74h:
  - Segunda a Quinta: 12h × 4 = 48h
  - Sexta e Sábado: 13h × 2 = 26h
  - Domingo: 0h
  - Total: 74h

## Insight 3

If Sunday is closed:

```txt
Domingo está fechado
Considere abrir e vender mais!
```

If Sunday is open:

```txt
Domingo está aberto
Ótima oportunidade para vender mais.
```

---

# 🧮 Weekly hours calculation

Must support intervals that cross midnight.

Example:

```txt
11:00 → 00:00 = 13h
```

Suggested logic:

```ts
function durationInHours(start: string, end: string): number {
  const [sh, sm] = start.split(':').map(Number);
  const [eh, em] = end.split(':').map(Number);

  const startMinutes = sh * 60 + sm;
  let endMinutes = eh * 60 + em;

  if (endMinutes <= startMinutes) {
    endMinutes += 24 * 60;
  }

  return (endMinutes - startMinutes) / 60;
}
```

---

# 🧩 Angular 20 + Ionic page structure

Recommended files:

```txt
src/app/pages/store-setup/horarios/horarios.page.ts
src/app/pages/store-setup/horarios/horarios.page.html
src/app/pages/store-setup/horarios/horarios.page.scss
```

Recommended components:

```txt
StoreSetupStepperComponent
QuickShortcutsComponent
DayScheduleRowComponent
InsightCardComponent
CopyHoursButtonComponent
WeekdayChooserComponent
```

MVP may implement all in one page, but domain logic must remain clean and state-driven.

---

# 📄 Ionic HTML guidance

Use Angular 20 control flow:

```html
@for (row of scheduleRows; track row.id) {
  ...
}

@if (row.open) {
  ...
} @else {
  ...
}
```

Use Ionic components:

- `ion-header`
- `ion-toolbar`
- `ion-content`
- `ion-card`
- `ion-button`
- `ion-popover`
- `ion-checkbox`
- `ion-input`
- `ion-badge`
- `ion-progress-bar`
- `ion-spinner`

---

# 🔽 Ionic weekday chooser example

```html
<ion-button
  id="weekday-menu-trigger"
  color="primary"
  shape="round"
  [disabled]="availableDays().length === 0"
>
  + Escolher dias
  <ion-icon name="chevron-down-outline" slot="end"></ion-icon>
</ion-button>

<ion-popover
  #weekdayPopover
  trigger="weekday-menu-trigger"
  side="bottom"
  alignment="end"
  [dismissOnSelect]="false"
>
  <ng-template>
    <ion-content class="weekday-popover">
      <ion-list>
        <ion-list-header>
          <ion-label>Selecionar dias</ion-label>
        </ion-list-header>

        @if (availableDays().length > 0) {
          @for (day of availableDays(); track day.id) {
            <ion-item>
              <ion-checkbox
                slot="start"
                [checked]="selectedDaysToAdd.includes(day.id)"
                (ionChange)="toggleDaySelection(day.id, $event.detail.checked)"
              ></ion-checkbox>

              <ion-label>{{ day.label }}</ion-label>
            </ion-item>
          }
        } @else {
          <ion-item lines="none">
            <ion-label>
              Todos os dias da semana já foram selecionados.
            </ion-label>
          </ion-item>
        }
      </ion-list>

      <div class="popover-actions">
        <ion-button
          expand="block"
          color="primary"
          [disabled]="selectedDaysToAdd.length === 0"
          (click)="createRowsFromSelectedDays(); weekdayPopover.dismiss()"
        >
          Selecionar
        </ion-button>
      </div>
    </ion-content>
  </ng-template>
</ion-popover>
```

---

# 💾 Save only on next step

## Footer button

```html
<ion-button
  color="primary"
  [disabled]="saving"
  (click)="onSaveAndGoNext()"
>
  @if (saving) {
    <ion-spinner name="crescent" slot="start"></ion-spinner>
    Salvando...
  } @else {
    Avançar para Entrega
    <ion-icon name="arrow-forward-outline" slot="end"></ion-icon>
  }
</ion-button>
```

## Save function

```ts
saving = false;

async onSaveAndGoNext(): Promise<void> {
  if (this.saving) return;

  if (!this.validateSchedules()) {
    await this.showToast('Verifique os horários antes de avançar.', 'danger');
    return;
  }

  this.saving = true;

  try {
    const payload = this.buildSchedulePayload();

    await this.storeSetupService.saveSchedules(payload);

    await this.router.navigate(['/cadastro/entrega']);
  } catch (error) {
    await this.showToast(
      'Não foi possível salvar os horários. Tente novamente.',
      'danger'
    );
  } finally {
    this.saving = false;
  }
}
```

## Payload

```ts
private buildSchedulePayload() {
  return {
    step: 'horarios',
    schedules: this.scheduleRows.map(row => ({
      id: row.id,
      label: row.label,
      days: row.days,
      open: row.open,
      intervals: row.open ? row.intervals : []
    }))
  };
}
```

---

# ✅ Acceptance criteria

## Weekday chooser

- Button appears inside `Dias e horários`, top-right.
- On mobile, button becomes full width below title.
- Dropdown shows only weekdays not used by existing rows.
- User can select multiple weekdays.
- Footer has blue `Selecionar` button.
- If no weekdays selected, `Selecionar` is disabled.
- Sequential selected weekdays create one grouped row.
- Non-sequential selected weekdays create separate rows.
- Deleted row days become available again.

## Autosave removal

- No autosave UI is displayed.
- No save request happens while editing.
- Save happens only through `Avançar para Entrega`.

## Presets

- Comercial sets `09:00 → 18:00`.
- Almoço sets `11:00 → 16:00`.
- Jantar sets `18:00 → 23:00`.
- 24 horas sets `00:00 → 23:59`.
- 24 horas opens Sunday.
- Other presets do not open Sunday automatically.

## Open / close

- Opening a closed row creates `11:00 → 23:00` if empty.
- Closing a row shows `Loja fechada`.
- Closing a row clears intervals.

## Insights

- Weekly hours are calculated dynamically.
- Midnight crossing is handled correctly.
- Sunday insight reacts to Sunday open/closed state.

## Save

- Clicking `Avançar para Entrega` validates.
- Shows loading state.
- Sends normalized payload.
- Navigates only after success.
- Shows error on failure.

---

# 🎨 Visual direction

- Use rounded cards.
- Use comfortable spacing.
- Use badges for `Aberto` and `Fechado`.
- Use clear primary button for weekday chooser.
- Use accessible labels.
- Keep mobile layout stacked.
- Search assets in:

```txt
./Documentacao/frontend/images
```

If assets are missing, use Ionic icons or inline SVG.

```

---

# 3. ✅ Final recommendation

## Best location for the weekday chooser

Place the button here:

```txt
Dias e horários                         + Escolher dias ▼
Configure os intervalos de funcionamento da loja
```

## Why

- 📍 It is inside the exact card affected by the action.
- 👀 It is visible without confusing it with quick presets.
- 📱 It works well on mobile when converted to full width.
- 🧠 It clearly means “add/select days”, unlike a standalone `V`.

## Recommended label

Use:

```txt
+ Escolher dias ▼
```

Alternative if you want shorter:

```txt
Dias ▼
```
