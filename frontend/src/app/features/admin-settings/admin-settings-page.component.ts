import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { SystemParameterService } from '../../core/services/system-parameter.service';
import { SystemParameter } from '../../shared/models/system-parameter.model';
import { extractApiErrorMessage } from '../../core/utils/api-error.helper';
import {
  SystemParameterControl,
  formatParameterHint,
  formatParameterUpdatedAt,
  groupSystemParameters,
  parameterControl,
  parameterControlId,
  parameterDisplayLabel,
  parameterInputStep,
  parameterRangeHint,
} from '../../core/utils/system-parameter-format';

interface FeedbackMessage {
  type: 'success' | 'error';
  message: string;
}

@Component({
  selector: 'app-admin-settings-page',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './admin-settings-page.component.html',
  styleUrl: './admin-settings-page.component.scss',
})
export class AdminSettingsPageComponent implements OnInit {
  private readonly service = inject(SystemParameterService);

  readonly parameters = signal<SystemParameter[]>([]);
  readonly drafts = signal<Record<string, string>>({});
  readonly loading = signal(true);
  readonly loadError = signal<string | null>(null);
  readonly savingKey = signal<string | null>(null);
  readonly bulkSaving = signal(false);
  readonly restrictedPromptKey = signal<string | null>(null);
  readonly feedback = signal<FeedbackMessage | null>(null);

  readonly groups = computed(() => groupSystemParameters(this.parameters()));
  readonly totalCount = computed(() => this.parameters().length);
  readonly dirtyCount = computed(
    () => this.parameters().filter((parameter) => this.isDirty(parameter)).length,
  );

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.restrictedPromptKey.set(null);

    this.service.list().subscribe({
      next: (parameters) => {
        this.applyParameters(parameters);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loadError.set(
          extractApiErrorMessage(error, 'Não foi possível carregar os parâmetros da plataforma.'),
        );
        this.loading.set(false);
      },
    });
  }

  draftValue(parameter: SystemParameter): string {
    return this.drafts()[parameter.key] ?? parameter.value;
  }

  updateDraft(key: string, value: string): void {
    this.drafts.update((drafts) => ({ ...drafts, [key]: value }));
  }

  isDirty(parameter: SystemParameter): boolean {
    return !parameter.isReadOnly && this.draftValue(parameter) !== parameter.value;
  }

  control(parameter: SystemParameter): SystemParameterControl {
    return parameterControl(parameter);
  }

  inputType(parameter: SystemParameter): 'text' | 'number' | 'url' | 'email' {
    switch (this.control(parameter)) {
      case 'number':
      case 'decimal':
        return 'number';
      case 'url':
        return 'url';
      case 'email':
        return 'email';
      default:
        return 'text';
    }
  }

  inputStep(parameter: SystemParameter): string | null {
    return parameterInputStep(parameter);
  }

  controlId(key: string): string {
    return parameterControlId(key);
  }

  displayLabel(parameter: SystemParameter): string {
    return parameterDisplayLabel(parameter);
  }

  hint(parameter: SystemParameter): string | null {
    return formatParameterHint(parameter, this.draftValue(parameter));
  }

  rangeHint(parameter: SystemParameter): string | null {
    return parameterRangeHint(parameter);
  }

  textMaxLength(parameter: SystemParameter): number | null {
    return parameter.type === 'String' || parameter.type === 'Json'
      ? parameter.maxLength
      : null;
  }

  hintId(parameter: SystemParameter): string | null {
    return this.hint(parameter) ? `hint-${this.controlId(parameter.key)}` : null;
  }

  updatedAt(parameter: SystemParameter): string {
    return formatParameterUpdatedAt(parameter);
  }

  onTextInput(parameter: SystemParameter, event: Event): void {
    const target = event.target as HTMLInputElement | HTMLTextAreaElement | null;
    this.updateDraft(parameter.key, target?.value ?? '');
  }

  onBooleanInput(parameter: SystemParameter, event: Event): void {
    const target = event.target as HTMLInputElement | null;
    this.updateDraft(parameter.key, target?.checked ? 'true' : 'false');
  }

  async save(parameter: SystemParameter, confirmRestricted = false): Promise<void> {
    if (parameter.isReadOnly) return;

    if (!this.isDirty(parameter)) {
      this.feedback.set({
        type: 'error',
        message: `${parameter.key} não foi alterado.`,
      });
      return;
    }

    const value = this.draftValue(parameter);
    if (!value.trim()) {
      this.feedback.set({
        type: 'error',
        message: `Informe um valor para ${parameter.key}.`,
      });
      return;
    }

    if (parameter.requiresConfirmation && !confirmRestricted) {
      this.restrictedPromptKey.set(parameter.key);
      return;
    }

    this.savingKey.set(parameter.key);
    this.feedback.set(null);

    try {
      const updated = await firstValueFrom(
        this.service.update(parameter.key, value, confirmRestricted),
      );
      this.applyUpdated(updated);
      this.restrictedPromptKey.set(null);
      this.feedback.set({
        type: 'success',
        message: `${parameter.key} atualizado.`,
      });
    } catch (error) {
      this.feedback.set({
        type: 'error',
        message: extractApiErrorMessage(
          error,
          `Não foi possível salvar ${parameter.key}.`,
        ),
      });
    } finally {
      this.savingKey.set(null);
    }
  }

  cancelRestricted(): void {
    this.restrictedPromptKey.set(null);
  }

  async saveAll(): Promise<void> {
    if (this.bulkSaving()) return;

    const dirty = this.parameters().filter((parameter) => this.isDirty(parameter));
    if (!dirty.length) return;

    const restricted = dirty.filter((parameter) => parameter.requiresConfirmation);
    if (restricted.length) {
      this.restrictedPromptKey.set(restricted[0].key);
      this.feedback.set({
        type: 'error',
        message:
          'Parâmetros de alto impacto exigem confirmação individual. Confirme o parâmetro indicado abaixo.',
      });
      return;
    }

    this.bulkSaving.set(true);
    this.feedback.set(null);

    let saved = 0;
    const errors: string[] = [];

    for (const parameter of dirty) {
      try {
        const updated = await firstValueFrom(
          this.service.update(parameter.key, this.draftValue(parameter)),
        );
        this.applyUpdated(updated);
        saved++;
      } catch (error) {
        errors.push(
          `${parameter.key}: ${extractApiErrorMessage(error, 'falha ao salvar.')}`,
        );
      }
    }

    this.bulkSaving.set(false);
    this.feedback.set(
      errors.length
        ? { type: 'error', message: errors.join(' ') }
        : { type: 'success', message: `${saved} parâmetro(s) atualizado(s).` },
    );
  }

  private applyParameters(parameters: SystemParameter[]): void {
    this.parameters.set(parameters);
    this.drafts.set(
      parameters.reduce<Record<string, string>>((drafts, parameter) => {
        drafts[parameter.key] = parameter.value;
        return drafts;
      }, {}),
    );
    this.feedback.set(null);
  }

  private applyUpdated(updated: SystemParameter): void {
    this.parameters.update((parameters) =>
      parameters.map((parameter) => (parameter.key === updated.key ? updated : parameter)),
    );
    this.drafts.update((drafts) => ({ ...drafts, [updated.key]: updated.value }));
  }
}
