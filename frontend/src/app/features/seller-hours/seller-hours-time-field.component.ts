import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { IonIcon } from '@ionic/angular/standalone';
import { addIcons } from 'ionicons';
import { timeOutline } from 'ionicons/icons';

const HALF_HOUR_OPTIONS: readonly string[] = Array.from({ length: 48 }, (_, index) => {
  const minutes = index * 30;
  const hours = Math.floor(minutes / 60);
  const remainder = minutes % 60;

  return `${String(hours).padStart(2, '0')}:${String(remainder).padStart(2, '0')}`;
});

const STORED_TIME_PATTERN = /^([01]\d|2[0-3]):[0-5]\d(?::[0-5]\d(?:\.\d{1,7})?)?$/;
const TYPED_TIME_PATTERN = /^(\d{1,2}):(\d{2})$/;

function formatTimeForDisplay(value: string): string {
  const trimmed = value.trim();

  // Valid backend values are shown as HH:MM. Invalid values stay visible so the
  // user can see and correct the problem instead of having it silently truncated.
  return isStoredTime(trimmed) ? trimmed.slice(0, 5) : trimmed;
}

function normalizeTypedTime(value: string): string | null {
  const match = TYPED_TIME_PATTERN.exec(value.trim());
  if (!match) return null;

  const hours = Number(match[1]);
  const minutes = Number(match[2]);
  if (!Number.isInteger(hours) || !Number.isInteger(minutes) || hours > 23 || minutes > 59) {
    return null;
  }

  return `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}`;
}

function isStoredTime(value: string): boolean {
  return STORED_TIME_PATTERN.test(value.trim());
}

addIcons({ 'time-outline': timeOutline });

@Component({
  selector: 'app-seller-hours-time-field',
  standalone: true,
  imports: [IonIcon],
  templateUrl: './seller-hours-time-field.component.html',
  styleUrl: './seller-hours-time-field.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SellerHoursTimeFieldComponent {
  readonly fieldId = input.required<string>();
  readonly label = input.required<string>();
  readonly ariaLabel = input.required<string>();
  readonly value = input<string>('');
  readonly valueChange = output<string>();
  readonly draftChange = output<{ fieldId: string; hasDraft: boolean }>();

  readonly options = HALF_HOUR_OPTIONS;
  readonly isOpen = signal(false);
  readonly activeIndex = signal(-1);
  readonly touched = signal(false);

  private readonly draft = signal<string | null>(null);
  private readonly injector = inject(Injector);
  private readonly timeInput = viewChild<ElementRef<HTMLInputElement>>('timeInput');
  private readonly optionsList = viewChild<ElementRef<HTMLElement>>('optionsList');

  readonly committedDisplay = computed(() => formatTimeForDisplay(this.value()));
  readonly display = computed(() => this.draft() ?? this.committedDisplay());
  readonly isInvalid = computed(() => !isStoredTime(this.value()));
  readonly showError = computed(() => this.touched() && this.draft() === null && this.isInvalid());
  readonly errorMessage = computed(() => (
    this.value().trim() === ''
      ? 'Informe um horário.'
      : 'Use um horário válido no formato HH:MM (00:00 a 23:59).'
  ));
  readonly listId = computed(() => `${this.fieldId()}-options`);
  readonly errorId = computed(() => `${this.fieldId()}-error`);
  readonly activeOptionId = computed(() => {
    if (!this.isOpen() || this.activeIndex() < 0) return null;

    return this.optionId(this.activeIndex());
  });

  optionId(index: number): string {
    return `${this.fieldId()}-option-${index}`;
  }

  onInput(event: Event): void {
    const target = event.target as HTMLInputElement | null;
    this.setDraft(target?.value ?? '');
    this.closeList();
  }

  onBlur(): void {
    this.commitDraft();
    this.touched.set(true);
    this.closeList();
  }

  onKeydown(event: KeyboardEvent): void {
    switch (event.key) {
      case 'ArrowDown':
      case 'ArrowUp': {
        event.preventDefault();
        const delta = event.key === 'ArrowDown' ? 1 : -1;
        if (!this.isOpen()) {
          this.openList();
          return;
        }

        const current = this.activeIndex();
        const next = current < 0
          ? (delta > 0 ? 0 : this.options.length - 1)
          : (current + delta + this.options.length) % this.options.length;
        this.setActive(next);
        return;
      }
      case 'Home':
        if (this.isOpen()) {
          event.preventDefault();
          this.setActive(0);
        }
        return;
      case 'End':
        if (this.isOpen()) {
          event.preventDefault();
          this.setActive(this.options.length - 1);
        }
        return;
      case 'Enter': {
        event.preventDefault();
        if (this.isOpen()) {
          const option = this.options[this.activeIndex()];
          if (option) {
            this.selectOption(option);
            return;
          }
          this.closeList();
          return;
        }

        this.commitDraft();
        this.touched.set(true);
        return;
      }
      case 'Escape':
        event.preventDefault();
        if (this.isOpen()) {
          this.closeList();
          return;
        }
        if (this.draft() !== null) {
          this.setDraft(null);
          this.touched.set(false);
        }
        return;
      case 'Tab':
        this.closeList();
        return;
      default:
        return;
    }
  }

  toggleList(): void {
    if (this.isOpen()) {
      this.closeList();
      return;
    }

    this.openList();
  }

  closeList(): void {
    this.isOpen.set(false);
    this.activeIndex.set(-1);
  }

  setActive(index: number): void {
    this.activeIndex.set(index);
    this.scrollActiveIntoView();
  }

  selectOption(option: string): void {
    const correctInvalidValue = this.isInvalid();
    this.setDraft(null);
    this.touched.set(true);
    this.closeList();
    if (correctInvalidValue || option !== this.committedDisplay()) {
      this.valueChange.emit(option);
    }
    this.focusInput();
  }

  private setDraft(next: string | null): void {
    const previous = this.draft();
    if (previous === next) return;

    this.draft.set(next);
    if (previous === null && next !== null) {
      this.draftChange.emit({ fieldId: this.fieldId(), hasDraft: true });
      return;
    }
    if (previous !== null && next === null) {
      this.draftChange.emit({ fieldId: this.fieldId(), hasDraft: false });
    }
  }

  private commitDraft(): void {
    const pending = this.draft();
    if (pending === null) return;

    const normalized = normalizeTypedTime(pending);
    const correctInvalidValue = this.isInvalid();
    this.setDraft(null);
    if (normalized !== null) {
      if (correctInvalidValue || normalized !== this.committedDisplay()) {
        this.valueChange.emit(normalized);
      }
      return;
    }

    const raw = pending.trim();
    if (raw !== this.committedDisplay()) {
      // Keep the invalid text in the model so day validation blocks saving instead of
      // silently persisting the previous value while the field shows something else.
      this.valueChange.emit(raw);
    }
  }

  private openList(): void {
    this.isOpen.set(true);
    const draft = this.draft();
    const normalizedDraft = draft === null ? null : normalizeTypedTime(draft);
    this.activeIndex.set(this.closestOptionIndex(normalizedDraft ?? this.committedDisplay()));
    this.focusInput();
    this.scrollActiveIntoView();
  }

  private closestOptionIndex(value: string): number {
    const match = /^([01]\d|2[0-3]):([0-5]\d)/.exec(value);
    if (!match) return 0;

    const totalMinutes = Number(match[1]) * 60 + Number(match[2]);

    return Math.min(this.options.length - 1, Math.round(totalMinutes / 30));
  }

  private focusInput(): void {
    this.timeInput()?.nativeElement.focus();
  }

  private scrollActiveIntoView(): void {
    afterNextRender(() => {
      if (!this.isOpen()) return;
      const list = this.optionsList()?.nativeElement;
      const option = list?.children.item(this.activeIndex()) as HTMLElement | null;
      option?.scrollIntoView?.({ block: 'nearest' });
    }, { injector: this.injector });
  }
}
