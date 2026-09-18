import { computed, Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class ServerAccessService {
  private readonly pending = signal(0);

  readonly activeRequests = this.pending.asReadonly();
  readonly isLoading = computed(() => this.pending() > 0);

  begin(): void {
    this.pending.update((count) => count + 1);
  }

  end(): void {
    this.pending.update((count) => (count > 0 ? count - 1 : 0));
  }
}
