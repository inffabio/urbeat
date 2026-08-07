import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ActivatedRoute, Router } from '@angular/router';
import { of } from 'rxjs';

import { StoreShellComponent } from './store-shell.component';
import { StoreService } from '../../core/services/store.service';

describe('StoreShellComponent', () => {
  let fixture: ComponentFixture<StoreShellComponent>;
  let routerMock: { url: string; navigate: jest.Mock };

  beforeEach(async () => {
    routerMock = { url: '/loja/carrinho', navigate: jest.fn() };

    await TestBed.configureTestingModule({
      imports: [StoreShellComponent],
      providers: [
        { provide: Router, useValue: routerMock },
        { provide: ActivatedRoute, useValue: { paramMap: of({ get: () => 'loja' }) } },
        { provide: StoreService, useValue: { getStoreByPath: jest.fn().mockReturnValue(of({ slug: 'loja', name: 'Loja' })) } },
      ],
    })
      .overrideComponent(StoreShellComponent, {
        set: {
          template: `
            @if (storeResolved() && showFooterNav()) {
              <footer class="footer-nav" aria-label="Navegação principal"></footer>
            }
          `,
        },
      })
      .compileComponents();

    fixture = TestBed.createComponent(StoreShellComponent);
    fixture.componentInstance.storeResolved.set(true);
    (fixture.componentInstance as any).storeSlug = 'loja';
  });

  it('should show the footer navigation on cart routes', () => {
    routerMock.url = '/loja/carrinho';

    fixture.detectChanges();

    expect(fixture.debugElement.query(By.css('.footer-nav'))).not.toBeNull();
  });

  it('should hide the footer navigation on checkout routes', () => {
    routerMock.url = '/loja/checkout/cadastro';

    fixture.detectChanges();

    expect(fixture.debugElement.query(By.css('.footer-nav'))).toBeNull();
  });

  it('should hide the footer navigation on the store home', () => {
    routerMock.url = '/loja';

    fixture.detectChanges();

    expect(fixture.debugElement.query(By.css('.footer-nav'))).toBeNull();
  });
});
