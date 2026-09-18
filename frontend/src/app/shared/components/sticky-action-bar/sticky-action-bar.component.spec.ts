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
    expect(source).toMatch(/@HostBinding\('class\.in-flow'\)[\s\S]*return this\.placement === 'inline'/);
    expect(source).toMatch(/:host\(\.in-flow\)\s*\{[\s\S]*position:\s*static/);
  });

  it('applies the in-flow class to the component host', () => {
    fixture.componentRef.setInput('placement', 'inline');
    fixture.detectChanges();

    expect(fixture.nativeElement.classList.contains('in-flow')).toBe(true);
  });

  it('defaults to the default appearance without the primary class', () => {
    expect(fixture.nativeElement.classList.contains('sticky-action-primary')).toBe(false);
  });

  it('applies the primary appearance class to the component host when requested', () => {
    fixture.componentRef.setInput('appearance', 'primary');
    fixture.detectChanges();

    expect(fixture.nativeElement.classList.contains('sticky-action-primary')).toBe(true);
  });

  it('styles the primary appearance with brand tokens without changing the default bar', () => {
    const source = readFileSync(resolve(__dirname, 'sticky-action-bar.component.ts'), 'utf8');

    expect(source).toContain("appearance: 'default' | 'primary' = 'default'");
    expect(source).toMatch(/@HostBinding\('class\.sticky-action-primary'\)[\s\S]*return this\.appearance === 'primary'/);
    expect(source).toMatch(/:host\(\.sticky-action-primary\)\s+\.sticky-action\s*\{[\s\S]*background:\s*var\(--app-brand/);
    expect(source).toMatch(/:host\(\.sticky-action-primary\)\s+\.sticky-action:hover\s*\{[\s\S]*background:\s*var\(--app-brand-dark/);
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
