import { Component, computed, input, output } from '@angular/core';
import { StoreAnalyticsPeriod } from '../../../../shared/models/order.model';
import {
  analyticsDaysInMonth,
  analyticsMaxWeekBlock,
  analyticsWeekBlockLabel,
} from '../../../../core/utils/sao-paulo-date.helper';

interface MonthOption {
  value: number;
  label: string;
}

interface WeekBlockOption {
  value: number;
  label: string;
  text: string;
}

const MONTHS: MonthOption[] = [
  { value: 1, label: 'Janeiro' },
  { value: 2, label: 'Fevereiro' },
  { value: 3, label: 'Março' },
  { value: 4, label: 'Abril' },
  { value: 5, label: 'Maio' },
  { value: 6, label: 'Junho' },
  { value: 7, label: 'Julho' },
  { value: 8, label: 'Agosto' },
  { value: 9, label: 'Setembro' },
  { value: 10, label: 'Outubro' },
  { value: 11, label: 'Novembro' },
  { value: 12, label: 'Dezembro' },
];

@Component({
  selector: 'app-dashboard-period-filter',
  standalone: true,
  templateUrl: './dashboard-period-filter.component.html',
  styleUrl: './dashboard-period-filter.component.scss',
})
export class DashboardPeriodFilterComponent {
  readonly period = input.required<StoreAnalyticsPeriod>();
  readonly anchorDate = input.required<string>();
  readonly weekBlock = input.required<number>();
  readonly availableYears = input.required<readonly number[]>();
  readonly todayIso = input.required<string>();

  readonly periodChange = output<StoreAnalyticsPeriod>();
  readonly anchorDateChange = output<string>();
  readonly weekBlockChange = output<number>();

  readonly periods: ReadonlyArray<{ value: StoreAnalyticsPeriod; label: string }> = [
    { value: 'day', label: 'Dia' },
    { value: 'week', label: 'Semana' },
    { value: 'month', label: 'Mês' },
    { value: 'year', label: 'Ano' },
  ];

  readonly currentYear = computed(() => {
    const year = Number(this.todayIso().slice(0, 4));
    return Number.isFinite(year) && year > 0 ? year : new Date().getFullYear();
  });

  readonly currentMonth = computed(() => {
    const month = Number(this.todayIso().slice(5, 7));
    return Number.isFinite(month) && month >= 1 && month <= 12 ? month : new Date().getMonth() + 1;
  });

  readonly anchorYear = computed(() => this.parts(this.anchorDate()).year);

  readonly anchorMonth = computed(() => this.parts(this.anchorDate()).month);

  readonly years = computed<readonly number[]>(() => {
    const currentYear = this.currentYear();
    const values = new Set<number>(
      this.availableYears().filter((year) => year > 0 && year <= currentYear),
    );
    values.add(currentYear);

    const anchorYear = this.anchorYear();
    if (anchorYear > 0 && anchorYear <= currentYear) {
      values.add(anchorYear);
    }

    return Array.from(values).sort((left, right) => right - left);
  });

  readonly monthOptions = computed<readonly MonthOption[]>(() => {
    const limit = this.anchorYear() === this.currentYear() ? this.currentMonth() : 12;
    return MONTHS.slice(0, limit);
  });

  readonly blockOptions = computed<readonly WeekBlockOption[]>(() => {
    const year = this.anchorYear();
    const month = this.anchorMonth();
    const maxBlock = analyticsMaxWeekBlock(year, month);
    const daysInMonth = analyticsDaysInMonth(year, month);
    return Array.from({ length: maxBlock }, (_, index) => {
      const block = index + 1;
      const startDay = (block - 1) * 7 + 1;
      const endDay = Math.min(startDay + 6, daysInMonth);
      return {
        value: block,
        label: analyticsWeekBlockLabel(block, year, month),
        text: `${startDay}–${endDay}`,
      };
    });
  });

  readonly effectiveWeekBlock = computed(() => {
    const maxBlock = analyticsMaxWeekBlock(this.anchorYear(), this.anchorMonth());
    return Math.min(Math.max(this.weekBlock(), 1), maxBlock);
  });

  selectPeriod(period: StoreAnalyticsPeriod): void {
    this.periodChange.emit(period);
  }

  onDayChange(value: string): void {
    if (!value) return;
    this.anchorDateChange.emit(value);
  }

  onMonthChange(value: string): void {
    const month = this.clampMonth(Number(value));
    this.anchorDateChange.emit(`${this.anchorYear()}-${this.pad(month)}-01`);
  }

  onYearChange(value: string): void {
    const year = Math.min(Number(value), this.currentYear());
    if (!Number.isFinite(year) || year <= 0) return;

    if (this.period() === 'year') {
      this.anchorDateChange.emit(`${year}-01-01`);
      return;
    }

    const month = this.clampMonth(this.anchorMonth(), year);
    this.anchorDateChange.emit(`${year}-${this.pad(month)}-01`);
  }

  selectBlock(block: number): void {
    this.weekBlockChange.emit(block);
  }

  private clampMonth(month: number, year = this.anchorYear()): number {
    const safeMonth = Number.isFinite(month) ? Math.min(Math.max(Math.trunc(month), 1), 12) : 1;
    return year === this.currentYear() ? Math.min(safeMonth, this.currentMonth()) : safeMonth;
  }

  private parts(value: string): { year: number; month: number; day: number } {
    const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
    if (!match) {
      const today = /^(\d{4})-(\d{2})-(\d{2})$/.exec(this.todayIso());
      if (today) {
        return { year: Number(today[1]), month: Number(today[2]), day: Number(today[3]) };
      }
      return { year: this.currentYear(), month: this.currentMonth(), day: 1 };
    }

    return { year: Number(match[1]), month: Number(match[2]), day: Number(match[3]) };
  }

  private pad(value: number): string {
    return String(value).padStart(2, '0');
  }
}
