import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class StoreContextService {
  readonly storeId = signal<string | null>(null);
  readonly storeName = signal<string | null>(null);
  readonly phoneNumber = signal<string | null>(null);
  readonly isOpen = signal(true);
}
