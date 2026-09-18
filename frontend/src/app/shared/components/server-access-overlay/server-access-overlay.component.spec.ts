import { TestBed } from '@angular/core/testing';
import { readFileSync } from 'fs';
import { resolve } from 'path';
import { ServerAccessOverlayComponent } from './server-access-overlay.component';

function componentSource(): string {
  return readFileSync(resolve(__dirname, 'server-access-overlay.component.ts'), 'utf8');
}

function ruleBlock(selector: string): string {
  const match = componentSource().match(new RegExp(`${selector.replace('.', '\\.')}\\s*\\{([\\s\\S]*?)\\}`));
  return match ? match[1] : '';
}

describe('ServerAccessOverlayComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ServerAccessOverlayComponent],
    }).compileComponents();
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('renders the food delivery spinner over a full-screen backdrop', () => {
    const fixture = TestBed.createComponent(ServerAccessOverlayComponent);
    fixture.detectChanges();

    const overlay = fixture.nativeElement.querySelector('.server-access-overlay') as HTMLElement;

    expect(overlay).not.toBeNull();
    expect(overlay.querySelector('app-food-delivery-spinner')).not.toBeNull();
  });

  it('exposes status and busy semantics for assistive technology', () => {
    const fixture = TestBed.createComponent(ServerAccessOverlayComponent);
    fixture.detectChanges();

    const overlay = fixture.nativeElement.querySelector('.server-access-overlay') as HTMLElement;

    expect(overlay.getAttribute('role')).toBe('status');
    expect(overlay.getAttribute('aria-busy')).toBe('true');
    expect(overlay.getAttribute('aria-live')).toBe('polite');
    expect(overlay.getAttribute('aria-label')).toBeTruthy();
  });

  it('keeps safe-area padding and disables animation for reduced motion', () => {
    const source = readFileSync(resolve(__dirname, 'server-access-overlay.component.ts'), 'utf8');

    expect(source).toContain('env(safe-area-inset-top');
    expect(source).toContain('env(safe-area-inset-bottom');
    expect(source).toContain('prefers-reduced-motion: reduce');
  });

  it('pins the overlay to the full viewport and centers the panel with grid', () => {
    const overlay = ruleBlock('.server-access-overlay');

    expect(overlay).toMatch(/position:\s*fixed/);
    expect(overlay).toMatch(/inset:\s*0/);
    expect(overlay).toMatch(/display:\s*grid/);
    expect(overlay).toMatch(/place-items:\s*center/);
  });

  it('centers the panel on the viewport with no positional bias', () => {
    const overlay = ruleBlock('.server-access-overlay');
    const panel = ruleBlock('.server-access-panel');

    expect(overlay).not.toMatch(/(^|[;{\s])(top|right|bottom|left)\s*:/);
    expect(overlay).not.toMatch(/transform\s*:/);
    expect(overlay).not.toMatch(/margin\s*:/);
    expect(overlay).not.toMatch(/padding\s*:/);

    expect(panel).not.toMatch(/(^|[;{\s])(top|right|bottom|left)\s*:/);
    expect(panel).not.toMatch(/transform\s*:/);
    expect(panel).not.toMatch(/margin\s*:/);
  });

  it('keeps safe-area gutters on the panel so centering stays viewport-based', () => {
    const panel = ruleBlock('.server-access-panel');

    expect(panel).toMatch(/env\(safe-area-inset-left/);
    expect(panel).toMatch(/env\(safe-area-inset-right/);
    expect(panel).toMatch(/env\(safe-area-inset-top/);
    expect(panel).toMatch(/env\(safe-area-inset-bottom/);
  });

  it('wraps the spinner in a circular container with a very transparent white background', () => {
    const panel = ruleBlock('.server-access-panel');
    const background = panel.match(
      /background\s*:\s*rgba\(\s*255\s*,\s*255\s*,\s*255\s*,\s*(0?\.\d+)\s*\)/,
    );

    expect(panel).toMatch(/aspect-ratio:\s*1/);
    expect(panel).toMatch(/border-radius:\s*50%/);
    expect(background).not.toBeNull();

    const alpha = Number(background![1]);
    expect(alpha).toBeGreaterThan(0);
    expect(alpha).toBeLessThanOrEqual(0.5);

    expect(panel).not.toMatch(/border-radius\s*:\s*var\(/);
  });

  it('keeps the panel translucent white while the overlay remains a pointer-blocking backdrop', () => {
    const overlay = ruleBlock('.server-access-overlay');
    const panel = ruleBlock('.server-access-panel');

    expect(panel).toMatch(/background\s*:\s*rgba\(\s*255\s*,\s*255\s*,\s*255/);
    expect(panel).not.toMatch(/box-shadow\s*:/);

    expect(overlay).toMatch(/position:\s*fixed/);
    expect(overlay).toMatch(/inset:\s*0/);
    expect(overlay).toMatch(/touch-action:\s*none/);
    expect(overlay).toMatch(/background\s*:\s*var\(--app-overlay/);
  });
});
