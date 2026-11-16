import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';

export interface PasswordSetupSubmission {
  newPassword: string;
  confirmPassword: string;
}

/**
 * Presentational new-password form shared by the administrative invitation and reset pages.
 * Local validation is a convenience only; the server remains authoritative.
 */
@Component({
  selector: 'app-password-setup-form',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './password-setup-form.component.html',
  styleUrl: './password-setup-form.component.scss',
})
export class PasswordSetupFormComponent {
  readonly submitLabel = input('Salvar nova senha');
  readonly busy = input(false);
  readonly serverError = input<string | null>(null);

  readonly submitted = output<PasswordSetupSubmission>();

  readonly newPassword = signal('');
  readonly confirmPassword = signal('');
  readonly showPassword = signal(false);
  readonly touched = signal(false);

  readonly hasMinLength = computed(() => this.newPassword().length >= 8);
  readonly hasUppercase = computed(() => /[A-Z]/.test(this.newPassword()));
  readonly hasLowercase = computed(() => /[a-z]/.test(this.newPassword()));
  readonly hasDigit = computed(() => /\d/.test(this.newPassword()));
  readonly matches = computed(
    () => this.newPassword().length > 0 && this.newPassword() === this.confirmPassword(),
  );

  readonly isValid = computed(
    () =>
      this.hasMinLength() &&
      this.hasUppercase() &&
      this.hasLowercase() &&
      this.hasDigit() &&
      this.matches(),
  );

  readonly showMismatch = computed(
    () => this.touched() && this.confirmPassword().length > 0 && !this.matches(),
  );

  onPasswordInput(event: Event): void {
    const target = event.target as HTMLInputElement | null;
    this.newPassword.set(target?.value ?? '');
  }

  onConfirmInput(event: Event): void {
    const target = event.target as HTMLInputElement | null;
    this.confirmPassword.set(target?.value ?? '');
  }

  toggleVisibility(): void {
    this.showPassword.update((visible) => !visible);
  }

  markTouched(): void {
    this.touched.set(true);
  }

  submit(): void {
    this.touched.set(true);
    if (!this.isValid() || this.busy()) {
      return;
    }

    this.submitted.emit({
      newPassword: this.newPassword(),
      confirmPassword: this.confirmPassword(),
    });
  }
}
