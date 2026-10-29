import { Component, computed, input } from '@angular/core';

export interface BarDatum {
  label: string;
  value: number;
  highlighted?: boolean;
}

@Component({
  selector: 'app-analytics-bar-chart',
  standalone: true,
  templateUrl: './analytics-bar-chart.component.html',
  styleUrl: './analytics-bar-chart.component.scss',
})
export class AnalyticsBarChartComponent {
  readonly bars = input.required<readonly BarDatum[]>();
  readonly total = input.required<number>();
  readonly ariaLabel = input.required<string>();
  readonly caption = input.required<string>();
  readonly bestLabel = input<string>('');
  readonly detailsLabel = input<string>('Ver dados do ano');

  private readonly maxValue = computed(() => {
    const values = this.bars().map((bar) => Math.max(bar.value, 0));
    return values.length === 0 ? 0 : Math.max(...values);
  });

  heightPercent(value: number): number {
    const max = this.maxValue();
    if (max <= 0) return 0;
    return Math.max((Math.max(value, 0) / max) * 100, 0);
  }

  formatCurrency(value: number): string {
    return new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' })
      .format(value)
      .replace(/\u00a0/g, ' ');
  }
}
