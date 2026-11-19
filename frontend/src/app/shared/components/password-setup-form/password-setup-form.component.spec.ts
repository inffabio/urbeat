import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PasswordSetupFormComponent } from './password-setup-form.component';

describe('PasswordSetupFormComponent', () => {
  let fixture: ComponentFixture<PasswordSetupFormComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PasswordSetupFormComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(PasswordSetupFormComponent);
    fixture.detectChanges();
  });

  function input(selector: string): HTMLInputElement {
    return fixture.nativeElement.querySelector(selector) as HTMLInputElement;
  }

  function type(selector: string, value: string): void {
    const element = input(selector);
    element.value = value;
    element.dispatchEvent(new Event('input'));
    element.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
  }

  it('uses autocomplete="new-password" and accessible labels', () => {
    expect(input('#new-password').getAttribute('autocomplete')).toBe('new-password');
    expect(input('#confirm-password').getAttribute('autocomplete')).toBe('new-password');
    expect(fixture.nativeElement.querySelector('label[for="new-password"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#password-policy')).not.toBeNull();
  });

  it('toggles visibility with an accessible button', () => {
    const toggle = fixture.nativeElement.querySelector('.toggle') as HTMLButtonElement;

    expect(toggle.getAttribute('aria-label')).toBe('Mostrar senha');
    expect(toggle.getAttribute('aria-pressed')).toBe('false');

    toggle.click();
    fixture.detectChanges();

    expect(input('#new-password').type).toBe('text');
    expect(toggle.getAttribute('aria-label')).toBe('Ocultar senha');
    expect(toggle.getAttribute('aria-pressed')).toBe('true');
  });

  it('reports mismatched passwords and never emits an invalid form', () => {
    const emitted = jest.fn();
    fixture.componentInstance.submitted.subscribe(emitted);

    type('#new-password', 'Senha1234');
    type('#confirm-password', 'Senha9999');

    fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(emitted).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('As senhas não coincidem.');
  });

  it('emits the password pair when the local convenience rules pass', () => {
    const emitted = jest.fn();
    fixture.componentInstance.submitted.subscribe(emitted);

    type('#new-password', 'Senha1234');
    type('#confirm-password', 'Senha1234');

    fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(emitted).toHaveBeenCalledWith({
      newPassword: 'Senha1234',
      confirmPassword: 'Senha1234',
    });
  });

  it('submits the form through the DOM instead of letting the browser reload the page', () => {
    const emitted = jest.fn();
    fixture.componentInstance.submitted.subscribe(emitted);

    type('#new-password', 'Senha1234');
    type('#confirm-password', 'Senha1234');

    const form = fixture.nativeElement.querySelector('form.password-form') as HTMLFormElement;
    const submit = new Event('submit', { bubbles: true, cancelable: true });
    form.dispatchEvent(submit);
    fixture.detectChanges();

    expect(submit.defaultPrevented).toBe(true);
    expect(emitted).toHaveBeenCalledWith({
      newPassword: 'Senha1234',
      confirmPassword: 'Senha1234',
    });
  });

  it('disables submission while busy and surfaces the server message', () => {
    fixture.componentRef.setInput('busy', true);
    fixture.componentRef.setInput('serverError', 'Senha não atende aos requisitos.');
    fixture.detectChanges();

    expect((fixture.nativeElement.querySelector('.submit') as HTMLButtonElement).disabled).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Senha não atende aos requisitos.');
  });
});
