import { TestBed } from '@angular/core/testing';
import { StoreLogoAvatarComponent } from './store-logo-avatar.component';

describe('StoreLogoAvatarComponent', () => {
  async function render(name: string, logoUrl: string | null, decorative = false) {
    TestBed.resetTestingModule();
    await TestBed.configureTestingModule({ imports: [StoreLogoAvatarComponent] }).compileComponents();

    const fixture = TestBed.createComponent(StoreLogoAvatarComponent);
    fixture.componentRef.setInput('name', name);
    fixture.componentRef.setInput('logoUrl', logoUrl);
    fixture.componentRef.setInput('decorative', decorative);
    fixture.detectChanges();
    return fixture;
  }

  it('renders the first letter of the store name when there is no logo', async () => {
    const fixture = await render('Pizza Hunter', null);

    expect(fixture.nativeElement.querySelector('img')).toBeNull();
    expect(
      fixture.nativeElement.querySelector('.store-logo-initial')?.textContent?.trim(),
    ).toBe('P');
  });

  it('renders a lazy image with a decorative alt when followed by the store name', async () => {
    const fixture = await render('Pizza Hunter', 'https://cdn.example/logo.png', true);

    const img = fixture.nativeElement.querySelector('img') as HTMLImageElement;
    expect(img).not.toBeNull();
    expect(img.getAttribute('alt')).toBe('');
    expect(img.getAttribute('loading')).toBe('lazy');
    expect(img.getAttribute('decoding')).toBe('async');
  });

  it('announces the logo when used standalone', async () => {
    const fixture = await render('Pizza Hunter', 'https://cdn.example/logo.png');

    expect(fixture.nativeElement.querySelector('img')?.getAttribute('alt')).toBe(
      'Logo da Pizza Hunter',
    );
  });

  it('replaces a broken image with the placeholder', async () => {
    const fixture = await render('Pizza Hunter', 'https://cdn.example/broken.png');

    const img = fixture.nativeElement.querySelector('img') as HTMLImageElement;
    img.dispatchEvent(new Event('error'));
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('img')).toBeNull();
    expect(
      fixture.nativeElement.querySelector('.store-logo-initial')?.textContent?.trim(),
    ).toBe('P');
  });
});
