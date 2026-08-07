import { ComponentFixture, TestBed, NO_ERRORS_SCHEMA } from '@angular/core/testing';

import { FloatingCartComponent } from './floating-cart.component';

describe('FloatingCartComponent', () => {
  let fixture: ComponentFixture<FloatingCartComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [FloatingCartComponent],
      schemas: [NO_ERRORS_SCHEMA],
    }).compileComponents();

    fixture = TestBed.createComponent(FloatingCartComponent);
    fixture.componentRef.setInput('itemCount', 3);
    fixture.componentRef.setInput('total', 42.5);
    fixture.detectChanges();
  });

  it('anchors the quantity badge to the bag icon frame', () => {
    const iconFrame: HTMLElement | null = fixture.nativeElement.querySelector('.bag-icon-frame');
    const count: HTMLElement | null = iconFrame?.querySelector('.cart-count') ?? null;

    expect(iconFrame).not.toBeNull();
    expect(count?.textContent?.trim()).toBe('3');
  });
});
