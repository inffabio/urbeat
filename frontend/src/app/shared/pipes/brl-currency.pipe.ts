import { Pipe, PipeTransform } from '@angular/core';

@Pipe({ name: 'brl', standalone: true, pure: true })
export class BrlCurrencyPipe implements PipeTransform {
  transform(value: number | string | null | undefined): string {
    if (value === null || value === undefined || value === '') return 'R$ 0,00';
    const num = typeof value === 'number' ? value : Number(value);
    if (Number.isNaN(num)) return 'R$ 0,00';
    return num.toLocaleString('pt-BR', {
      style: 'currency',
      currency: 'BRL',
      minimumFractionDigits: 2,
    });
  }
}
