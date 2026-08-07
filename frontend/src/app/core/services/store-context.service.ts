import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class StoreContextService {
  readonly storeName = signal<string | null>(null);
  readonly phoneNumber = signal<string | null>(null);
  readonly isOpen = signal(true);
}
