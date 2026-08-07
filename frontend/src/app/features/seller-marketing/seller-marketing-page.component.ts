import { CommonModule } from '@angular/common';
import { Component, computed, inject } from '@angular/core';
import { SellerShellFacade } from '../seller-shell/seller-shell.facade';

@Component({
  selector: 'app-seller-marketing-page',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './seller-marketing-page.component.html',
  styleUrl: './seller-marketing-page.component.scss',
})
export class SellerMarketingPageComponent {
  readonly shell = inject(SellerShellFacade);

  readonly publicPath = computed(() => {
    const slug = this.shell.store()?.slug;
    return slug ? `/${slug}` : '/sua-loja';
  });

  formatRating(value: number | undefined): string {
    return new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 1, maximumFractionDigits: 1 }).format(value ?? 0);
  }
}
