import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { LandingPageComponent } from './landing-page.component';

describe('LandingPageComponent plans', () => {
  let fixture: ComponentFixture<LandingPageComponent>;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LandingPageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(LandingPageComponent);
    httpMock = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('renders active plans, prices, benefits and trial days from the public offer', () => {
    httpMock.expectOne('/api/landingpagecontent').flush([]);
    httpMock.expectOne('/api/public/plans').flush({
      plans: [
        {
          id: 'plan-crescer',
          name: 'Crescer',
          amount: 39.9,
          description: 'Para negócios em crescimento.',
          trialDays: 9,
          benefits: ['Relatórios de vendas', 'Base de clientes exportável'],
        },
      ],
    });

    fixture.detectChanges();
    const host: HTMLElement = fixture.nativeElement;
    const text = host.textContent ?? '';

    expect(host.querySelectorAll('.lp-plan-card')).toHaveLength(1);
    expect(text).toContain('Crescer');
    expect(text).toContain('39,90');
    expect(text).toContain('9 dias grátis');
    expect(text).toContain('Relatórios de vendas');
    expect(text).toContain('Base de clientes exportável');
  });

  it('keeps a coherent fallback when the public offer fails', () => {
    httpMock.expectOne('/api/landingpagecontent').flush([]);
    httpMock.expectOne('/api/public/plans').error(new ProgressEvent('error'));

    fixture.detectChanges();
    const host: HTMLElement = fixture.nativeElement;
    const text = host.textContent ?? '';

    expect(host.querySelectorAll('.lp-plan-card')).toHaveLength(3);
    expect(text).toContain('19,90');
    expect(text).toContain('39,90');
    expect(text).toContain('79,90');
    expect(text).toContain('15 dias grátis');
  });

  it('does not invent plans when the offer succeeds with no active plan', () => {
    httpMock.expectOne('/api/landingpagecontent').flush([]);
    httpMock.expectOne('/api/public/plans').flush({ plans: [] });

    fixture.detectChanges();
    const host: HTMLElement = fixture.nativeElement;
    const text = host.textContent ?? '';

    expect(host.querySelectorAll('.lp-plan-card')).toHaveLength(0);
    expect(text).toContain('Estamos atualizando nossos planos');
  });
});
