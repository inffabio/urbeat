import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';

import { BackToMenuLinkComponent } from './back-to-menu-link.component';

describe('BackToMenuLinkComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BackToMenuLinkComponent],
    }).compileComponents();
  });

  it('should render an accessible secondary back-to-menu button by default', () => {
    const fixture = TestBed.createComponent(BackToMenuLinkComponent);
    fixture.detectChanges();

    const button = fixture.debugElement.query(By.css('button')).nativeElement as HTMLButtonElement;

    expect(button.type).toBe('button');
    expect(button.classList.contains('link')).toBe(true);
    expect(button.textContent?.trim()).toBe('Voltar ao cardápio');
  });

  it('should emit navigateBack when clicked', () => {
    const fixture = TestBed.createComponent(BackToMenuLinkComponent);
    const emitSpy = jest.spyOn(fixture.componentInstance.navigateBack, 'emit');
    fixture.detectChanges();

    fixture.debugElement.query(By.css('button')).triggerEventHandler('click');

    expect(emitSpy).toHaveBeenCalledTimes(1);
  });

  it('should support primary variant and disabled state', () => {
    const fixture = TestBed.createComponent(BackToMenuLinkComponent);
    fixture.componentInstance.variant = 'primary';
    fixture.componentInstance.disabled = true;
    fixture.detectChanges();

    const button = fixture.debugElement.query(By.css('button')).nativeElement as HTMLButtonElement;

    expect(button.classList.contains('primary')).toBe(true);
    expect(button.disabled).toBe(true);
  });
});
