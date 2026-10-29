import { Component, input } from '@angular/core';

export interface DonutSlice {
  label: string;
  value: number;
}

const SLICE_COLORS = ['#D54A51', '#B63A41', '#9e3a3f', '#7f3a38', '#6c4634'];
const NEUTRAL_COLOR = '#b5a89e';

@Component({
  selector: 'app-analytics-donut-chart',
  standalone: true,
  templateUrl: './analytics-donut-chart.component.html',
  styleUrl: './analytics-donut-chart.component.scss',
})
export class AnalyticsDonutChartComponent {
  readonly slices = input.required<readonly DonutSlice[]>();
  readonly total = input.required<number>();
  readonly ariaLabel = input.required<string>();
  readonly centerLabel = input<string>('unidades');

  private readonly radius = 40;
  private readonly circumference = 2 * Math.PI * this.radius;

  percent(value: number): number {
    const total = this.total();
    return total > 0 ? Math.round((value / total) * 100) : 0;
  }

  dashArray(value: number): string {
    const total = this.total();
    const length = total > 0 ? (value / total) * this.circumference : 0;
    return `${length} ${this.circumference - length}`;
  }

  dashOffset(index: number): number {
    const total = this.total();
    if (total <= 0) return 0;
    const previousTotal = this.slices()
      .slice(0, index)
      .reduce((sum, slice) => sum + slice.value, 0);
    return -(previousTotal / total) * this.circumference;
  }

  sliceColor(index: number, label: string): string {
    return label === 'Outros' ? NEUTRAL_COLOR : SLICE_COLORS[index % SLICE_COLORS.length];
  }
}
