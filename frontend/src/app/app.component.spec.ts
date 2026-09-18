import { TestBed } from '@angular/core/testing';
import { AppComponent } from './app.component';
import { AppUpdateService } from './core/services/app-update.service';
import { ServerAccessService } from './core/services/server-access.service';

describe('AppComponent', () => {
  let serverAccess: ServerAccessService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [{ provide: AppUpdateService, useValue: { init: jest.fn() } }],
    }).compileComponents();

    serverAccess = TestBed.inject(ServerAccessService);
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('does not render the overlay while the server is idle', () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('app-server-access-overlay')).toBeNull();
  });

  it('renders the global overlay at the app root while requests are in flight', () => {
    const fixture = TestBed.createComponent(AppComponent);

    serverAccess.begin();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('app-server-access-overlay')).not.toBeNull();

    serverAccess.end();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('app-server-access-overlay')).toBeNull();
  });

  it('marks the routed content inert while the overlay blocks interaction', () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();

    const outlet = fixture.nativeElement.querySelector('ion-router-outlet') as HTMLElement;
    expect(outlet.getAttribute('inert')).toBeNull();

    serverAccess.begin();
    fixture.detectChanges();

    expect(outlet.getAttribute('inert')).not.toBeNull();
  });
});
