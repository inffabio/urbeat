import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import {
  IonContent, IonIcon, IonModal, IonHeader, IonToolbar,
  IonTitle, IonButtons, IonButton
} from '@ionic/angular/standalone';
import { addIcons } from 'ionicons';
import { timeOutline, addOutline, trashOutline, copyOutline, arrowBackOutline, arrowForwardOutline, lockClosed, closeOutline, checkmarkCircle, globeOutline, bicycleOutline, calendarOutline } from 'ionicons/icons';
import { StoreService } from '../../../core/services/store.service';
import { ToastService } from '../../../core/services/toast.service';
import { UpsertStoreBusinessHoursRequest, BusinessHour } from '../../../shared/models/store.model';
import { WizardFooterComponent } from '../../../shared/components/wizard-footer/wizard-footer.component';
import { WizardHeaderComponent } from '../../../shared/components/wizard-header/wizard-header.component';
import { createStepperSteps } from '../../../shared/config/wizard-steps.config';

addIcons({
  'time-outline': timeOutline, 'add-outline': addOutline, 'trash-outline': trashOutline,
  'copy-outline': copyOutline, 'arrow-back-outline': arrowBackOutline, 'arrow-forward-outline': arrowForwardOutline,
  'lock-closed': lockClosed, 'close-outline': closeOutline, 'checkmark-circle': checkmarkCircle,
  'globe-outline': globeOutline, 'bicycle-outline': bicycleOutline, 'calendar-outline': calendarOutline,
});

interface Shift {
  id?: string;
  startTime: string;
  endTime: string;
}

interface DaySchedule {
  isOpen: boolean;
  shifts: Shift[];
}

interface WeekDay {
  id: string;
  label: string;
  short: string;
  dayOfWeek: number; // .NET DayOfWeek: 0=Sunday, 1=Monday, ..., 6=Saturday
}

interface Preset {
  id: string;
  name: string;
  shifts: { start: string; end: string }[];
}

const WEEKDAYS: WeekDay[] = [
  { id: 'domingo', label: 'Domingo', short: 'Dom', dayOfWeek: 0 },
  { id: 'segunda', label: 'Segunda-feira', short: 'Seg', dayOfWeek: 1 },
  { id: 'terca', label: 'Terça-feira', short: 'Ter', dayOfWeek: 2 },
  { id: 'quarta', label: 'Quarta-feira', short: 'Qua', dayOfWeek: 3 },
  { id: 'quinta', label: 'Quinta-feira', short: 'Qui', dayOfWeek: 4 },
  { id: 'sexta', label: 'Sexta-feira', short: 'Sex', dayOfWeek: 5 },
  { id: 'sabado', label: 'Sábado', short: 'Sab', dayOfWeek: 6 },
];

const PRESETS: Preset[] = [
  {
    id: 'comercial', name: 'Comercial',
    shifts: [{ start: '09:00', end: '12:00' }, { start: '13:00', end: '18:00' }],
  },
  {
    id: 'almoco', name: 'Almoço',
    shifts: [{ start: '11:00', end: '16:00' }],
  },
  {
    id: 'restaurante', name: 'Restaurante',
    shifts: [{ start: '11:00', end: '14:00' }, { start: '18:00', end: '23:00' }],
  },
  {
    id: '24h', name: '24 horas',
    shifts: [{ start: '00:00', end: '23:59' }],
  },
];

@Component({
  selector: 'app-store-hours-page',
  standalone: true,
  imports: [
    CommonModule, FormsModule,
    IonContent, IonIcon, IonModal, IonHeader, IonToolbar,
    IonTitle, IonButtons, IonButton,
    WizardHeaderComponent, WizardFooterComponent,
  ],
  templateUrl: './store-hours-page.component.html',
  styleUrl: './store-hours-page.component.scss',
  host: { '[class.urbeat-onboarding]': '!isDashboardView()' },
})
export class StoreHoursPageComponent implements OnInit {
  private readonly storeService = inject(StoreService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  readonly stepperSteps = createStepperSteps(1);
  readonly isDashboardView = computed(() => (this.router.url ?? '').startsWith('/app/'));

  readonly storeId = signal<string | null>(null);
  readonly loading = signal(true);
  readonly isSaving = signal(false);
  readonly saveStatus = signal<'idle' | 'saving' | 'saved' | 'error'>('idle');
  readonly hasChanges = signal(false);
  readonly syncAll = signal(false);
  readonly activePreset = signal<string | null>(null);
  readonly copySourceDay = signal<string | null>(null);
  readonly isCopyModalOpen = signal(false);
  readonly copyTargets = signal<Set<string>>(new Set());

  readonly presets = PRESETS;

  readonly schedule = signal<Record<string, DaySchedule>>({});
  private savedScheduleSnapshot: Record<string, DaySchedule> | null = null;

  readonly weekDays = WEEKDAYS;
  readonly displayWeekDays = [...WEEKDAYS.slice(1), WEEKDAYS[0]];

  hasUnsavedChanges(): boolean {
    return this.hasChanges() && !this.isSaving();
  }

  getDayShort(dayId: string): string {
    return WEEKDAYS.find(d => d.id === dayId)?.short ?? '';
  }

  readonly weeklyHours = computed(() => {
    let total = 0;
    for (const day of WEEKDAYS) {
      const s = this.schedule()[day.id];
      if (!s?.isOpen) continue;
      for (const shift of s.shifts) {
        if (!shift.startTime || !shift.endTime) continue;
        const [sh, sm] = shift.startTime.split(':').map(Number);
        const [eh, em] = shift.endTime.split(':').map(Number);
        let start = sh * 60 + sm;
        let end = eh * 60 + em;
        if (end <= start) end += 1440;
        total += end - start;
      }
    }
    const h = Math.floor(total / 60);
    const m = total % 60;
    return m > 0 ? `${h}h ${m}min` : `${h}h`;
  });

  readonly copyTargetDays = computed(() =>
    WEEKDAYS.filter(d => d.id !== this.copySourceDay())
  );

  ngOnInit(): void {
    this.initDefaultSchedule();
    this.storeService.getMyStore().subscribe({
      next: store => {
        this.storeId.set(store.id);
        this.loadHours(store.id);
      },
      error: () => {
        this.loading.set(false);
        this.toast.showError('Erro ao carregar dados da loja.');
      },
    });
  }

  private initDefaultSchedule(): void {
    const s: Record<string, DaySchedule> = {};
    for (const day of WEEKDAYS) {
      s[day.id] = { isOpen: true, shifts: [{ startTime: '11:00', endTime: '23:00' }] };
    }
    s['domingo'] = { isOpen: false, shifts: [] };
    this.schedule.set(s);
    this.savedScheduleSnapshot = this.cloneSchedule(s);
  }

  private loadHours(storeId: string): void {
    this.storeService.getStoreBusinessHours(storeId).subscribe({
      next: res => {
        if (res?.items?.length) {
          const s: Record<string, DaySchedule> = {};
          for (const day of WEEKDAYS) {
            const item = res.items.find(i => i.dayOfWeek === day.dayOfWeek);
            s[day.id] = item
              ? { isOpen: item.isOpen, shifts: item.shifts?.map(sh => ({ startTime: sh.startTime, endTime: sh.endTime })) ?? [] }
              : { isOpen: false, shifts: [] };
          }
          this.schedule.set(s);
        }
        this.savedScheduleSnapshot = this.cloneSchedule(this.schedule());
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.toast.showError('Erro ao carregar horários.');
      },
    });
  }

  toggleDay(dayId: string): void {
    this.schedule.update(s => {
      const day = s[dayId];
      const wasOpen = day.isOpen;
      const copy = { ...s };
      copy[dayId] = {
        isOpen: !wasOpen,
        shifts: !wasOpen && day.shifts.length === 0 ? [{ startTime: '09:00', endTime: '18:00' }] : [...day.shifts],
      };
      return copy;
    });
    this.markDirty();
    if (this.syncAll()) this.syncAllDays(dayId);
  }

  addShift(dayId: string): void {
    this.schedule.update(s => {
      const day = s[dayId];
      const last = day.shifts[day.shifts.length - 1];
      let start = '18:00';
      let end = '22:00';
      if (last?.endTime) {
        const [h, m] = last.endTime.split(':').map(Number);
        const total = (h * 60 + m + 60) % 1440;
        start = `${String(Math.floor(total / 60)).padStart(2, '0')}:${String(total % 60).padStart(2, '0')}`;
        const endTotal = (total + 240) % 1440;
        end = `${String(Math.floor(endTotal / 60)).padStart(2, '0')}:${String(endTotal % 60).padStart(2, '0')}`;
      }
      return { ...s, [dayId]: { ...day, shifts: [...day.shifts, { startTime: start, endTime: end }] } };
    });
    this.markDirty();
    if (this.syncAll()) this.syncAllDays(dayId);
  }

  removeShift(dayId: string, index: number): void {
    this.schedule.update(s => {
      const day = s[dayId];
      const shifts = [...day.shifts];
      shifts.splice(index, 1);
      return { ...s, [dayId]: { ...day, shifts } };
    });
    this.markDirty();
    if (this.syncAll()) this.syncAllDays(dayId);
  }

  updateTime(dayId: string, index: number, field: 'startTime' | 'endTime', value: string): void {
    this.schedule.update(s => {
      const day = s[dayId];
      const shifts = [...day.shifts];
      shifts[index] = { ...shifts[index], [field]: value };
      return { ...s, [dayId]: { ...day, shifts } };
    });
    this.markDirty();
    if (this.syncAll()) this.syncAllDays(dayId);
  }

  isOvernight(dayId: string, index: number): boolean {
    const shift = this.schedule()[dayId]?.shifts[index];
    if (!shift?.startTime || !shift?.endTime) return false;
    const [sh, sm] = shift.startTime.split(':').map(Number);
    const [eh, em] = shift.endTime.split(':').map(Number);
    return (eh * 60 + em) <= (sh * 60 + sm);
  }

  getShiftCount(dayId: string): string {
    const day = this.schedule()[dayId];
    if (!day?.isOpen) return 'Fechado';
    const n = day.shifts.length;
    return `${n} ${n === 1 ? 'turno' : 'turnos'}`;
  }

  toggleSyncAll(): void {
    this.syncAll.update(v => !v);
    if (this.syncAll()) {
      const firstOpen = WEEKDAYS.find(d => this.schedule()[d.id]?.isOpen);
      if (firstOpen) this.syncAllDays(firstOpen.id);
    }
  }

  private syncAllDays(sourceId: string): void {
    const source = this.schedule()[sourceId];
    this.schedule.update(s => {
      const copy = { ...s };
      for (const day of WEEKDAYS) {
        copy[day.id] = { isOpen: source.isOpen, shifts: source.shifts.map(sh => ({ ...sh })) };
      }
      return copy;
    });
  }

  applyPreset(presetId: string): void {
    const preset = PRESETS.find(p => p.id === presetId);
    if (!preset) return;
    this.activePreset.set(presetId === this.activePreset() ? null : presetId);

    this.schedule.update(s => {
      const copy = { ...s };
      for (const day of WEEKDAYS) {
        if (presetId === '24h' || copy[day.id].isOpen) {
          copy[day.id] = { isOpen: true, shifts: preset.shifts.map(sh => ({ startTime: sh.start, endTime: sh.end })) };
        }
      }
      return copy;
    });
    this.markDirty();
    this.toast.showInfo(`Modelo "${preset.name}" aplicado.`);
  }

  clearAll(): void {
    this.schedule.update(s => {
      const copy = { ...s };
      for (const day of WEEKDAYS) {
        copy[day.id] = { isOpen: false, shifts: [] };
      }
      return copy;
    });
    this.syncAll.set(false);
    this.activePreset.set(null);
    this.markDirty();
    this.toast.showInfo('Todos os horários foram removidos.');
  }

  openCopyModal(dayId: string): void {
    this.copySourceDay.set(dayId);
    this.copyTargets.set(new Set());
    this.isCopyModalOpen.set(true);
  }

  closeCopyModal(): void {
    this.isCopyModalOpen.set(false);
    this.copySourceDay.set(null);
    this.copyTargets.set(new Set());
  }

  toggleCopyTarget(dayId: string): void {
    this.copyTargets.update(s => {
      const n = new Set(s);
      n.has(dayId) ? n.delete(dayId) : n.add(dayId);
      return n;
    });
  }

  confirmCopy(): void {
    const targets = [...this.copyTargets()];
    if (targets.length === 0) {
      this.toast.showInfo('Selecione pelo menos um dia.');
      return;
    }
    const source = this.schedule()[this.copySourceDay()!];
    this.schedule.update(s => {
      const copy = { ...s };
      for (const t of targets) {
        copy[t] = { isOpen: source.isOpen, shifts: source.shifts.map(sh => ({ ...sh })) };
      }
      return copy;
    });
    this.markDirty();
    this.closeCopyModal();
    this.toast.showInfo(`Horários copiados para ${targets.length} ${targets.length === 1 ? 'dia' : 'dias'}.`);
  }

  validateDay(dayId: string): string | null {
    const day = this.schedule()[dayId];
    if (!day) return null;
    if (day.isOpen && day.shifts.length === 0) return 'Adicione pelo menos um turno ou feche o dia.';
    if (!day.isOpen) return null;
    const incomplete = day.shifts.some(s => !s.startTime || !s.endTime);
    if (incomplete) return 'Preencha início e fim de todos os turnos.';
    for (let i = 0; i < day.shifts.length; i++) {
      for (let j = i + 1; j < day.shifts.length; j++) {
        if (this.shiftsOverlap(day.shifts[i], day.shifts[j])) {
          return `Turnos ${i + 1} e ${j + 1} sobrepostos.`;
        }
      }
    }
    return null;
  }

  private shiftsOverlap(a: Shift, b: Shift): boolean {
    const a0 = this.toMinutes(a.startTime);
    let a1 = this.toMinutes(a.endTime);
    if (a1 <= a0) a1 += 1440;
    const b0 = this.toMinutes(b.startTime);
    let b1 = this.toMinutes(b.endTime);
    if (b1 <= b0) b1 += 1440;
    return a0 < b1 && b0 < a1;
  }

  private toMinutes(t: string): number {
    if (!t) return 0;
    const [h, m] = t.split(':').map(Number);
    return h * 60 + m;
  }

  anyOpen(): boolean {
    return WEEKDAYS.some(d => this.schedule()[d.id]?.isOpen);
  }

  private markDirty(): void {
    this.hasChanges.set(true);
  }

  private buildRequest(): UpsertStoreBusinessHoursRequest {
    return {
      items: WEEKDAYS.map(day => {
        const s = this.schedule()[day.id];
        return {
          dayOfWeek: day.dayOfWeek,
          isOpen: s?.isOpen ?? false,
          shifts: (s?.shifts ?? []).map(sh => ({
            startTime: sh.startTime || '09:00',
            endTime: sh.endTime || '18:00',
          })),
        };
      }),
    };
  }

  async goNext(): Promise<void> {
    if (!this.anyOpen()) {
      this.toast.showError('Abra pelo menos um dia para continuar.');
      return;
    }
    const errors: string[] = [];
    for (const day of WEEKDAYS) {
      const err = this.validateDay(day.id);
      if (err) errors.push(`${day.label}: ${err}`);
    }
    if (errors.length > 0) {
      this.toast.showGrouped(errors.slice(0, 3).map(e => ({ type: 'error' as const, text: e })));
      return;
    }
    const success = await this.saveHours();
    if (success) this.router.navigate(['/configurar-loja/entrega']);
  }

  goBack(): void {
    this.router.navigate(['/configurar-loja']);
  }

  async saveDraft(): Promise<void> {
    this.saveStatus.set('saving');
    const ok = await this.saveHours();
    this.saveStatus.set(ok ? 'saved' : 'error');
    if (ok) this.hasChanges.set(false);
    setTimeout(() => { if (this.saveStatus() === 'saved') this.saveStatus.set('idle'); }, 2000);
  }

  cancelChanges(): void {
    if (!this.savedScheduleSnapshot) return;

    this.schedule.set(this.cloneSchedule(this.savedScheduleSnapshot));
    this.hasChanges.set(false);
    this.syncAll.set(false);
    this.activePreset.set(null);
    this.saveStatus.set('idle');
  }

  private saveHours(): Promise<boolean> {
    return new Promise(resolve => {
      const id = this.storeId();
      if (!id) { resolve(false); return; }
      this.isSaving.set(true);
      this.storeService.upsertStoreBusinessHours(id, this.buildRequest()).subscribe({
        next: () => {
          this.isSaving.set(false);
          this.savedScheduleSnapshot = this.cloneSchedule(this.schedule());
          resolve(true);
        },
        error: () => {
          this.isSaving.set(false);
          this.toast.showError('Erro ao salvar horários.');
          resolve(false);
        },
      });
    });
  }

  private cloneSchedule(source: Record<string, DaySchedule>): Record<string, DaySchedule> {
    return Object.fromEntries(
      Object.entries(source).map(([dayId, day]) => [dayId, {
        isOpen: day.isOpen,
        shifts: day.shifts.map(shift => ({ ...shift })),
      }]),
    );
  }
}
