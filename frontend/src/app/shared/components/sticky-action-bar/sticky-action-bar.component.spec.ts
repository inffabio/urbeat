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
    fixture.componentRef.setInput('detail', 'Registrar endereço e pagamento');
    fixture.detectChanges();
  });

  it('renders the compact fixed action structure used by the storefront cart', () => {
    const button = fixture.debugElement.query(By.css('button.sticky-action'));

    expect(button).not.toBeNull();
    expect(button.query(By.css('.action-icon ion-icon')).componentInstance.name).toBe('wallet-outline');
    expect(button.query(By.css('strong')).nativeElement.textContent).toContain('Continuar');
    expect(button.query(By.css('.action-detail')).nativeElement.textContent).toContain('Registrar endereço');
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

  it('adds the safe-area inset only to in-flow bars that ask for it', () => {
    const source = readFileSync(resolve(__dirname, 'sticky-action-bar.component.ts'), 'utf8');

    expect(source).toContain('@Input() safeArea = false;');
    expect(source).toMatch(/:host\(\.in-flow\.in-flow-safe-area\)\s*\{[\s\S]*padding-bottom:\s*calc\(16px \+ env\(safe-area-inset-bottom, 0px\)\)/);

    expect(fixture.nativeElement.classList.contains('in-flow-safe-area')).toBe(false);

    fixture.componentRef.setInput('safeArea', true);
    fixture.detectChanges();
    expect(fixture.nativeElement.classList.contains('in-flow-safe-area')).toBe(false);

    fixture.componentRef.setInput('placement', 'inline');
    fixture.detectChanges();
    expect(fixture.nativeElement.classList.contains('in-flow-safe-area')).toBe(true);
  });

  it('never squeezes the action label: the label keeps its width and the detail ellipsizes', () => {
    const source = readFileSync(resolve(__dirname, 'sticky-action-bar.component.ts'), 'utf8');

    expect(source).toMatch(/grid-template-columns:\s*54px auto 1px minmax\(0, 1fr\);/);
    expect(source).not.toMatch(/grid-template-columns:\s*54px 1fr 1px auto;/);
    expect(source).toMatch(/\.action-detail\s*\{[\s\S]*min-width:\s*0;/);
    // 48px keeps the touch target above the 44px minimum.
    expect(source).toMatch(/\.sticky-action\s*\{[\s\S]*height:\s*48px;/);
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

  it('keeps the primary disabled state legible instead of fading white text over bordeaux', () => {
    const source = readFileSync(resolve(__dirname, 'sticky-action-bar.component.ts'), 'utf8');

    expect(source).toMatch(/:host\(\.sticky-action-primary\)\s+\.sticky-action:disabled\s*\{[\s\S]*background:\s*var\(--app-border-medium/);
    expect(source).toMatch(/:host\(\.sticky-action-primary\)\s+\.sticky-action:disabled\s*\{[\s\S]*color:\s*var\(--app-ink/);
    expect(source).toMatch(/:host\(\.sticky-action-primary\)\s+\.sticky-action:disabled\s*\{[\s\S]*opacity:\s*1;/);
    expect(source).toMatch(/:host\(\.sticky-action-primary\)\s+\.sticky-action:disabled\s+\.action-icon[\s\S]*color:\s*var\(--app-ink/);
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
  it('keeps an 8px visible gap above the measured footer and never drops below the safe-area inset', () => {
    const source = readFileSync(resolve(__dirname, 'sticky-action-bar.component.ts'), 'utf8');

    expect(source).toMatch(/:host\s*\{[\s\S]*bottom:\s*calc\(max\(var\(--store-footer-clearance, 0px\), env\(safe-area-inset-bottom, 0px\)\) \+ 8px\);/);
    expect(source).not.toMatch(/bottom:\s*var\(--store-footer-clearance,/);
    expect(source).not.toMatch(/bottom:\s*calc\(64px \+ max\(8px, env\(safe-area-inset-bottom, 0px\)\)\);/);
  });

  it('does not float the bar over the footer menu buttons', () => {
    const source = readFileSync(resolve(__dirname, 'sticky-action-bar.component.ts'), 'utf8');

    expect(source).not.toMatch(/bottom:\s*0(?:px)?;/);
    expect(source).toMatch(/calc\(max\(var\(--store-footer-clearance, 0px\), env\(safe-area-inset-bottom, 0px\)\) \+ 8px\)/);
  });
});

describe('StickyActionBarComponent layering', () => {
  function ruleZIndex(source: string, selector: string): number {
    const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const block = source.match(new RegExp(`${escaped}\\s*\\{([\\s\\S]*?)\\}`))?.[1] ?? '';
    return Number(block.match(/z-index:\s*(\d+)/)?.[1]);
  }

  it('stacks above the storefront footer and below the cart sheet and its confirmation modal', () => {
    const sticky = readFileSync(resolve(__dirname, 'sticky-action-bar.component.ts'), 'utf8');
    const footer = readFileSync(resolve(__dirname, '../footer-nav/footer-nav.component.ts'), 'utf8');
    const sheet = readFileSync(resolve(__dirname, '../cart-sheet/cart-sheet.component.ts'), 'utf8');

    const stickyZ = ruleZIndex(sticky, ':host');
    const footerZ = ruleZIndex(footer, ':host');
    const sheetZ = ruleZIndex(sheet, '.cart-sheet');
    const modalZ = ruleZIndex(sheet, '.modal-overlay');

    expect(Number.isFinite(stickyZ)).toBe(true);
    expect(Number.isFinite(footerZ)).toBe(true);
    expect(Number.isFinite(sheetZ)).toBe(true);
    expect(Number.isFinite(modalZ)).toBe(true);

    expect(stickyZ).toBeGreaterThan(footerZ);
    expect(stickyZ).toBeLessThan(sheetZ);
    expect(stickyZ).toBeLessThan(modalZ);
  });
});
