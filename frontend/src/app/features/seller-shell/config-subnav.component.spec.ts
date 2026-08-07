import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ConfigSubnavComponent } from './config-subnav.component';

describe('ConfigSubnavComponent', () => {
  let fixture: ComponentFixture<ConfigSubnavComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ConfigSubnavComponent],
      providers: [provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(ConfigSubnavComponent);
    fixture.detectChanges();
  });

  it('renders the configuration links as Bootstrap violet navigation pills', () => {
    const nav = fixture.nativeElement.querySelector('nav');
    const links = fixture.nativeElement.querySelectorAll('a');

    expect(nav.classList.contains('nav')).toBe(true);
    expect(nav.classList.contains('nav-pills')).toBe(true);
    expect(fixture.nativeElement.querySelectorAll('.nav-item')).toHaveLength(5);
    expect(fixture.nativeElement.querySelectorAll('.nav-link')).toHaveLength(5);
    expect(links[0].getAttribute('routerlink')).toBe('/app/configuracoes/horarios');
  });
});
