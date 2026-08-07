import { Injectable, signal } from '@angular/core';

export interface StoreFilterState {
  activeCategoryId: string | null;
  searchTerm: string;
  scrollPosition: number;
}

@Injectable({ providedIn: 'root' })
export class StoreFilterStateService {
  private readonly state = signal<StoreFilterState>({
    activeCategoryId: null,
    searchTerm: '',
    scrollPosition: 0,
  });

  save(state: Partial<StoreFilterState>): void {
    this.state.update((s) => ({ ...s, ...state }));
  }

  restore(): StoreFilterState {
    return this.state();
  }

  clear(): void {
    this.state.set({ activeCategoryId: null, searchTerm: '', scrollPosition: 0 });
  }
}
