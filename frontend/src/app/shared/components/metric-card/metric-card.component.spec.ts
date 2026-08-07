import { TestBed } from '@angular/core/testing';
import { MetricCardComponent } from './metric-card.component';

describe('MetricCardComponent', () => {
  it('renders metric label and value', () => {
    const fixture = TestBed.createComponent(MetricCardComponent);
    fixture.componentRef.setInput('label', 'Pedidos hoje');
    fixture.componentRef.setInput('value', '9');
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Pedidos hoje');
    expect(fixture.nativeElement.textContent).toContain('9');
  });
});
