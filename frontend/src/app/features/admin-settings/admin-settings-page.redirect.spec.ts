import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';
import { SystemParameterService } from '../../core/services/system-parameter.service';
import { AdminSettingsPageComponent } from './admin-settings-page.component';

/**
 * The administrators surface lived behind `/admin/configuracoes?secao=administradores`. That URL
 * may still be bookmarked, so it must land on the dedicated page instead of a dead tab.
 */
describe('AdminSettingsPageComponent legacy administrators URL', () => {
  let fixture: ComponentFixture<AdminSettingsPageComponent>;
  let navigateByUrl: jest.Mock;

  function setup(queryParams: Record<string, string>): void {
    navigateByUrl = jest.fn(() => Promise.resolve(true));

    TestBed.configureTestingModule({
      imports: [AdminSettingsPageComponent],
      providers: [
        { provide: Router, useValue: { navigate: jest.fn(), navigateByUrl } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap(queryParams) } },
        },
        { provide: SystemParameterService, useValue: { list: () => of([]) } },
      ],
    });

    fixture = TestBed.createComponent(AdminSettingsPageComponent);
    fixture.detectChanges();
  }

  it('redirects the legacy administrators query to the dedicated route', () => {
    setup({ secao: 'administradores' });

    expect(navigateByUrl).toHaveBeenCalledWith('/admin/administradores', { replaceUrl: true });
  });

  it('keeps the settings page when no legacy query is present', () => {
    setup({});

    expect(navigateByUrl).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('#admin-settings-title')).not.toBeNull();
  });
});
