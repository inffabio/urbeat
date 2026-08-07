import { ComponentFixture, TestBed, NO_ERRORS_SCHEMA } from '@angular/core/testing';

import { FooterNavComponent, FooterNavItem } from './footer-nav.component';

describe('FooterNavComponent', () => {
  let fixture: ComponentFixture<FooterNavComponent>;

  const items: FooterNavItem[] = [
    { id: 'menu', icon: 'storefront-outline', label: 'Cardapio', active: true },
    { id: 'orders', icon: 'receipt-outline', label: 'Pedidos', disabled: true },
    { id: 'cart', icon: 'bag-check-outline', label: 'Carrinho' },
    { id: 'account', icon: 'person-circle-outline', label: 'Conta', disabled: true },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [FooterNavComponent],
      schemas: [NO_ERRORS_SCHEMA],
    }).compileComponents();

    fixture = TestBed.createComponent(FooterNavComponent);
    fixture.componentRef.setInput('items', items);
    fixture.detectChanges();
  });

  it('renders inside a bottom safe zone so the full menu can scroll above browser chrome', () => {
    const safeZone: HTMLElement | null = fixture.nativeElement.querySelector('.footer-nav-safe-zone');
    const footer: HTMLElement | null = safeZone?.querySelector('footer.footer-nav') ?? null;

    expect(safeZone).not.toBeNull();
    expect(footer).not.toBeNull();
  });
});
