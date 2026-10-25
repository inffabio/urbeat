import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { DeliveryDriverService } from '../../core/services/delivery-driver.service';
import { DeliveryDriverInvitePreview } from '../../shared/models/delivery-driver.model';

function passwordsMatch(control: AbstractControl): ValidationErrors | null {
  const password = control.get('password')?.value;
  const confirmation = control.get('confirmPassword')?.value;
  return password === confirmation ? null : { passwordsMismatch: true };
}

@Component({
  selector: 'app-driver-invite-page',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './driver-invite-page.component.html',
  styleUrl: './driver-invite-page.component.scss',
})
export class DriverInvitePageComponent implements OnInit {
  private readonly deliveryDrivers = inject(DeliveryDriverService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly error = signal<string | null>(null);
  readonly previewError = signal(false);
  readonly preview = signal<DeliveryDriverInvitePreview | null>(null);
  readonly accepted = signal(false);
  readonly acceptedStoreName = signal<string | null>(null);
  readonly showPassword = signal(false);
  private token = '';

  readonly form = this.fb.nonNullable.group(
    {
      fullName: ['', [Validators.minLength(3), Validators.maxLength(120)]],
      password: ['', [Validators.required, Validators.minLength(8)]],
      confirmPassword: ['', [Validators.required]],
    },
    { validators: passwordsMatch },
  );

  ngOnInit(): void {
    this.token = this.route.snapshot.paramMap.get('token') ?? '';
    if (!this.token) {
      this.previewError.set(true);
      this.loading.set(false);
      return;
    }

    this.deliveryDrivers.previewInvite(this.token).subscribe({
      next: (preview) => {
        this.preview.set(preview);
        if (preview.driverName) this.form.controls.fullName.setValue(preview.driverName);
        this.loading.set(false);
      },
      error: () => {
        this.previewError.set(true);
        this.loading.set(false);
      },
    });
  }

  togglePassword(): void {
    this.showPassword.update((value) => !value);
  }

  submit(): void {
    if (this.form.invalid || this.submitting() || !this.preview()?.valid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    this.submitting.set(true);
    this.error.set(null);

    this.deliveryDrivers
      .acceptInvite(this.token, {
        password: value.password,
        fullName: value.fullName.trim() || undefined,
      })
      .subscribe({
        next: (response) => {
          this.submitting.set(false);
          this.accepted.set(true);
          this.acceptedStoreName.set(response.storeName ?? null);
        },
        error: (err) => {
          this.submitting.set(false);
          this.error.set(
            err?.error?.error
              ?? 'Não foi possível aceitar o convite. Confirme a senha da sua conta e tente novamente.',
          );
        },
      });
  }

  goToLogin(): void {
    void this.router.navigate(['/entregador/login']);
  }
}
