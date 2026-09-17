import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
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

  it('supports an in-flow placement without viewport anchoring', () => {
    const source = readFileSync(resolve(__dirname, 'sticky-action-bar.component.ts'), 'utf8');

    expect(source).toContain("@Input() placement: 'fixed' | 'inline' = 'fixed';");
    expect(source).toContain('[class.in-flow]="placement === \'inline\'"');
    expect(source).toMatch(/:host\(\.in-flow\)\s*\{[\s\S]*position:\s*static/);
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

describe('StickyActionBarComponent footer clearance', () => {
  it('anchors the fixed bar above the measured storefront footer with a safe-area fallback', () => {
    const source = readFileSync(resolve(__dirname, 'sticky-action-bar.component.ts'), 'utf8');

    expect(source).toMatch(/:host\s*\{[\s\S]*bottom:\s*var\(--store-footer-clearance, calc\(64px \+ max\(8px, env\(safe-area-inset-bottom, 0px\)\)\)\);/);
    expect(source).not.toMatch(/bottom:\s*calc\(64px \+ max\(8px, env\(safe-area-inset-bottom, 0px\)\)\);/);
  });
});
