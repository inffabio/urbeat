import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IonIcon } from '@ionic/angular/standalone';
import { Product } from '../../models/product.model';

@Component({
  selector: 'app-product-card',
  standalone: true,
  imports: [CommonModule, IonIcon],
  templateUrl: './product-card.component.html',
  styleUrl: './product-card.component.scss',
})
export class ProductCardComponent {
  @Input({ required: true }) product!: Product;
  @Input() quantity = 0;
  @Input() minQuantity = 0;
  @Input() maxQuantity = 20;

  @Output() add = new EventEmitter<void>();
  @Output() remove = new EventEmitter<void>();
  @Output() open = new EventEmitter<void>();

  get isSimple(): boolean {
    const sm = this.product.saleMode as string;
    if (sm === 'size' || sm === 'fixed_weight' || sm === 'variable_weight') return false;
    const hasVariations = this.product.variations?.some((v) => v.isActive) ?? false;
    const hasChoices = this.product.choiceOptions?.some((c) => c.isActive) ?? false;
    const hasAdditionals = this.product.additionals?.some((a) => a.isActive) ?? false;
    const hasOptionGroups = (this.product.optionGroups?.length ?? 0) > 0;
    return !hasVariations && !hasChoices && !hasAdditionals && !hasOptionGroups;
  }

  get isInCart(): boolean {
    return this.quantity > 0;
  }

  priceLabel(): string {
    const sm = this.product.saleMode as string;
    if (sm === 'size' || sm === 'fixed_weight') {
      const active = (this.product.variations ?? []).filter(v => v.isActive && v.price > 0);
      if (active.length) {
        const min = active.map(v => v.price).reduce((a, b) => a < b ? a : b);
        return `A partir de R$ ${min.toFixed(2).replace('.', ',')}`;
      }
    }
    if (sm === 'variable_weight' && this.product.weightConfig) {
      const est = this.product.weightConfig.isEstimated ? ' (estimado)' : '';
      return `R$ ${this.product.weightConfig.pricePerKg.toFixed(2).replace('.', ',')}/kg${est}`;
    }
    if (this.product.price > 0) return `R$ ${this.product.price.toFixed(2).replace('.', ',')}`;
    return 'R$ 0,00';
  }

  onCardClick(): void {
    this.open.emit();
  }

  onCardKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      this.open.emit();
    }
  }

  onVisualActionClick(event: Event): void {
    event.stopPropagation();
    this.open.emit();
  }
}
