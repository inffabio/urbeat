import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { StickyActionBarComponent } from './sticky-action-bar.component';

describe('StickyActionBarComponent', () => {
  let fixture: ComponentFixture<StickyActionBarComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StickyActionBarComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(StickyActionBarComponent);
    fixture.componentRef.setInput('icon', 'wallet-outline');
    fixture.componentRef.setInput('label', 'Continuar');
    fixture.componentRef.setInput('detail', 'Escolha endereço e pagamento');
    fixture.detectChanges();
  });

  it('renders the compact fixed action structure used by the storefront cart', () => {
    const button = fixture.debugElement.query(By.css('button.sticky-action'));

    expect(button).not.toBeNull();
    expect(button.query(By.css('.action-icon ion-icon')).componentInstance.name).toBe('wallet-outline');
    expect(button.query(By.css('strong')).nativeElement.textContent).toContain('Continuar');
    expect(button.query(By.css('.action-detail')).nativeElement.textContent).toContain('Escolha endereço');
    expect(button.query(By.css('.action-divider'))).not.toBeNull();
  });

  it('emits the action and forwards disabled state', () => {
    const action = jest.fn();
    fixture.componentInstance.action.subscribe(action);
    fixture.componentRef.setInput('disabled', true);
    fixture.detectChanges();

    const button = fixture.debugElement.query(By.css('button')).nativeElement as HTMLButtonElement;
    button.click();

    expect(button.disabled).toBe(true);
    expect(action).not.toHaveBeenCalled();
  });
});
