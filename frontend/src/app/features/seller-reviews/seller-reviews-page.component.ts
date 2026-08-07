import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { StoreService } from '../../core/services/store.service';
import { formatSaoPauloDate } from '../../core/utils/sao-paulo-date.helper';
import { Review } from '../../shared/models/store.model';
import { SellerShellFacade } from '../seller-shell/seller-shell.facade';

@Component({
  selector: 'app-seller-reviews-page',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './seller-reviews-page.component.html',
  styleUrl: './seller-reviews-page.component.scss',
})
export class SellerReviewsPageComponent implements OnInit {
  private readonly storeService = inject(StoreService);
  readonly shell = inject(SellerShellFacade);

  readonly loading = signal(true);
  readonly error = signal(false);
  readonly reviews = signal<Review[]>([]);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(false);
    this.storeService.getSellerReviews().subscribe({
      next: (reviews) => {
        this.reviews.set(reviews);
        this.loading.set(false);
      },
      error: () => {
        this.error.set(true);
        this.loading.set(false);
      },
    });
  }

  formatRating(value: number | undefined): string {
    return new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 1, maximumFractionDigits: 1 }).format(value ?? 0);
  }

  formatDate(value: string): string {
    return formatSaoPauloDate(value);
  }
}
