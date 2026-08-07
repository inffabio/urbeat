import { Component, signal, computed, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { addIcons } from 'ionicons';
import {
  rocketOutline, pencilOutline, imageOutline, checkmarkCircle,
  warning, checkmarkOutline, arrowBackOutline, arrowForwardOutline,
  informationCircleOutline, chevronBackOutline, chevronForwardOutline,
  ellipseOutline, checkmark, storefrontOutline, cartOutline, downloadOutline
} from 'ionicons/icons';
import {
  IonContent, IonIcon, IonSpinner
} from '@ionic/angular/standalone';
import { createStepperSteps } from '../../../shared/config/wizard-steps.config';
import { WizardHeaderComponent } from '../../../shared/components/wizard-header/wizard-header.component';
import { WizardFooterComponent } from '../../../shared/components/wizard-footer/wizard-footer.component';
import { StoreService } from '../../../core/services/store.service';
import { ToastService } from '../../../core/services/toast.service';
import { StorePublishSummary } from '../../../shared/models/store.model';

addIcons({
  'rocket-outline': rocketOutline,
  'pencil-outline': pencilOutline,
  'image-outline': imageOutline,
  'checkmark-circle': checkmarkCircle,
  'warning': warning,
  'checkmark-outline': checkmarkOutline,
  'arrow-back-outline': arrowBackOutline,
  'arrow-forward-outline': arrowForwardOutline,
  'information-circle-outline': informationCircleOutline,
  'chevron-back-outline': chevronBackOutline,
  'chevron-forward-outline': chevronForwardOutline,
  'ellipse-outline': ellipseOutline,
  'checkmark': checkmark,
  'storefront-outline': storefrontOutline,
  'cart-outline': cartOutline,
  'download-outline': downloadOutline,
});

const DAY_NAMES: string[] = ['Segunda', 'Terça', 'Quarta', 'Quinta', 'Sexta', 'Sábado', 'Domingo'];
const PAGE_SIZE = 5;

const CONFETTI_COLORS = ['#D54A51', '#B63A41', '#FDECEE', '#FBBF24', '#F59E0B', '#EF4444', '#EC4899', '#8B5CF6', '#3B82F6', '#10B981', '#FFD700', '#FFFFFF'];

@Component({
  selector: 'app-store-publish-page',
  standalone: true,
  imports: [CommonModule, IonContent, IonIcon, IonSpinner, WizardHeaderComponent, WizardFooterComponent],
  templateUrl: './store-publish-page.component.html',
  styleUrl: './store-publish-page.component.scss',
})
export class StorePublishPageComponent implements OnInit {
  private readonly storeService = inject(StoreService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  readonly storeId = signal<string | null>(null);
  readonly loading = signal(true);
  readonly summary = signal<StorePublishSummary | null>(null);
  readonly publishing = signal(false);

  readonly showConfetti = signal(false);
  readonly storeNameForConfetti = signal('');
  readonly storeSlugForConfetti = signal('');

  readonly areaPage = signal(0);
  readonly productPage = signal(0);
  readonly Math = Math;
  readonly pageSize = PAGE_SIZE;

  readonly stepperSteps = createStepperSteps(4);

  readonly maxVisibleAreas = 8;

  readonly backendReady = computed(() => {
    const r = this.summary()?.rules;
    return r ? (r.detailsOk && r.hoursOk && r.deliveryOk && r.productsOk) : false;
  });

  readonly missingSections = computed(() => {
    const r = this.summary()?.rules;
    if (!r) return [];
    const missing: string[] = [];
    if (!r.detailsOk) missing.push('Dados da loja');
    if (!r.hoursOk) missing.push('Horários de atendimento');
    if (!r.deliveryOk) missing.push('Bairros e entrega');
    if (!r.productsOk) missing.push('Produtos cadastrados');
    return missing;
  });

  readonly allConfirmed = this.backendReady;

  readonly areas = computed(() => this.summary()?.deliveryAreas ?? []);

  readonly totalAreaPages = computed(() => {
    const len = this.areas().length;
    if (len <= this.maxVisibleAreas) return 1;
    return Math.ceil((len - this.maxVisibleAreas) / this.pageSize) + 1;
  });

  readonly pagedAreas = computed(() => {
    const all = this.areas();
    if (all.length <= this.maxVisibleAreas) return all;
    if (this.areaPage() === 0) return all.slice(0, this.maxVisibleAreas);
    const start = this.maxVisibleAreas + (this.areaPage() - 1) * this.pageSize;
    return all.slice(start, start + this.pageSize);
  });

  readonly showAreaPagination = computed(() => this.areas().length > this.maxVisibleAreas);

  readonly products = computed(() => this.summary()?.productsPreview ?? []);

  readonly totalProductPages = computed(() => Math.max(1, Math.ceil(this.products().length / this.pageSize)));

  readonly pagedProducts = computed(() => {
    const start = this.productPage() * this.pageSize;
    return this.products().slice(start, start + this.pageSize);
  });

  readonly showProductPagination = computed(() => this.products().length > this.pageSize);

  readonly allDays = computed(() => {
    const hours = this.summary()?.businessHours ?? [];
    const map = new Map<number, { opensAt: string; closesAt: string }>();
    for (const h of hours) {
      const isClosed = h.opensAt === '00:00' && h.closesAt === '00:00';
      map.set(h.dayOfWeek, {
        opensAt: isClosed ? '' : h.opensAt,
        closesAt: isClosed ? '' : h.closesAt,
      });
    }
    return DAY_NAMES.map((name, i) => ({
      dayOfWeek: i,
      name,
      opensAt: map.get(i)?.opensAt ?? null,
      closesAt: map.get(i)?.closesAt ?? null,
    }));
  });

  readonly filteredCategories = computed(() => {
    const stats = this.summary()?.productsStats?.byCategory ?? [];
    return [{ name: 'Todos', count: this.summary()?.productsStats?.total ?? 0 }, ...stats];
  });

  scrollTo(id: string): void {
    document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  goToManage(): void {
    this.router.navigate(['/configurar-loja']);
  }

  constructor() {}

  ngOnInit() {
    this.storeService.getMyStore().subscribe({
      next: (store) => {
        this.storeId.set(store.id);
        this.storeSlugForConfetti.set(store.slug);
        this.loadSummary(store.id);
      },
      error: () => {
        this.loading.set(false);
        this.toast.showError('Erro ao carregar dados da loja.');
      }
    });
  }

  private loadSummary(storeId: string) {
    this.storeService.getStorePublishSummary(storeId).subscribe({
      next: (summary) => {
        this.summary.set(summary);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.toast.showError('Erro ao carregar resumo da publicação.');
      }
    });
  }

  formatCurrency(value: number): string {
    if (value == null || isNaN(value)) return 'R$ 0,00';
    return value.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
  }

  prevAreaPage() { if (this.areaPage() > 0) this.areaPage.update(p => p - 1); }
  nextAreaPage() { if (this.areaPage() < this.totalAreaPages() - 1) this.areaPage.update(p => p + 1); }

  prevProductPage() { if (this.productPage() > 0) this.productPage.update(p => p - 1); }
  nextProductPage() { if (this.productPage() < this.totalProductPages() - 1) this.productPage.update(p => p + 1); }

  goTo(path: string) {
    this.router.navigate(['/', path]);
  }

  goToCardapio() {
    const slug = this.storeSlugForConfetti();
    this.router.navigate(['/', slug]);
  }

  goBack() {
    this.router.navigate(['/configurar-loja/produtos']);
  }

  publishStore() {
    if (!this.allConfirmed() || this.publishing()) return;

    const sid = this.storeId();
    if (!sid) return;

    this.publishing.set(true);
    this.storeNameForConfetti.set(this.summary()?.storeDetails?.name ?? '');
    this.storeSlugForConfetti.set(this.storeSlugForConfetti() || '');

    this.storeService.publishStore(sid).subscribe({
      next: () => {
        this.publishing.set(false);
        this.showConfetti.set(true);
      },
      error: (err) => {
        this.publishing.set(false);
        const message = err?.error?.message || 'Erro ao publicar a loja. Verifique os dados e tente novamente.';
        this.toast.showError(message);
      }
    });
  }
}
