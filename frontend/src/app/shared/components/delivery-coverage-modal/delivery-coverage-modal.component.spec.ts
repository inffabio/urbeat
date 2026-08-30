import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { DeliveryCoverageModalComponent } from './delivery-coverage-modal.component';

describe('DeliveryCoverageModalComponent', () => {
  let fixture: ComponentFixture<DeliveryCoverageModalComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [DeliveryCoverageModalComponent] }).compileComponents();
    fixture = TestBed.createComponent(DeliveryCoverageModalComponent);
    fixture.componentRef.setInput('storeAddress', { city: 'Campos', state: 'RJ', zipCode: '28010000', neighborhood: 'Centro' });
    fixture.componentRef.setInput('customerAddress', { city: 'Campos', state: 'RJ', cep: '28000000', neighborhood: 'Jardim Aurora' });
    fixture.detectChanges();
  });

  it('renders both compact address columns and the blocking message', () => {
    expect(fixture.debugElement.queryAll(By.css('.delivery-coverage-column'))).toHaveLength(2);
    expect(fixture.nativeElement.textContent).toContain('28.010-000 · RJ · Campos');
    expect(fixture.nativeElement.textContent).toContain('28.000-000 · RJ · Campos');
    expect(fixture.nativeElement.textContent).toContain('Esta loja não faz entrega neste bairro');
  });
});
