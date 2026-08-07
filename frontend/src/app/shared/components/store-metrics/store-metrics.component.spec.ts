import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';

import { StoreMetricsComponent } from './store-metrics.component';

describe('StoreMetricsComponent', () => {
  let fixture: ComponentFixture<StoreMetricsComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StoreMetricsComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(StoreMetricsComponent);
  });

  it('should mark store status as open when store is open', () => {
    fixture.componentRef.setInput('isOpen', true);
    fixture.detectChanges();

    const status = fixture.debugElement.query(By.css('.metric')).nativeElement as HTMLElement;
    expect(status.classList.contains('status-open')).toBe(true);
    expect(status.classList.contains('status-closed')).toBe(false);
  });

  it('should mark store status as closed when store is closed', () => {
    fixture.componentRef.setInput('isOpen', false);
    fixture.detectChanges();

    const status = fixture.debugElement.query(By.css('.metric')).nativeElement as HTMLElement;
    expect(status.classList.contains('status-closed')).toBe(true);
    expect(status.classList.contains('status-open')).toBe(false);
  });
});
