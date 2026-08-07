import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { StoreService } from '../../../core/services/store.service';
import { ToastService } from '../../../core/services/toast.service';
import { StoreHoursPageComponent } from './store-hours-page.component';

describe('StoreHoursPageComponent', () => {
  const storeServiceMock = {
    getMyStore: jest.fn().mockReturnValue(of({ id: 'store-123' })),
    getStoreBusinessHours: jest.fn().mockReturnValue(of({ items: [] })),
    upsertStoreBusinessHours: jest.fn(),
  };

  const toastServiceMock = {
    showError: jest.fn(),
    showInfo: jest.fn(),
    showGrouped: jest.fn(),
  };

  beforeEach(async () => {
    jest.clearAllMocks();

    await TestBed.configureTestingModule({
      imports: [StoreHoursPageComponent],
      providers: [
        provideRouter([]),
        { provide: StoreService, useValue: storeServiceMock },
        { provide: ToastService, useValue: toastServiceMock },
      ],
    }).compileComponents();
  });

  it('renders seven day rows with shift controls for open days', () => {
    const fixture = TestBed.createComponent(StoreHoursPageComponent);
    fixture.detectChanges();
    fixture.componentInstance.schedule.update(schedule => ({
      ...schedule,
      segunda: {
        isOpen: true,
        shifts: [
          { startTime: '11:00', endTime: '14:30' },
          { startTime: '18:00', endTime: '23:00' },
        ],
      },
    }));
    fixture.detectChanges();

    const rows = fixture.nativeElement.querySelectorAll('.day-row');
    expect(rows.length).toBe(7);

    const primeira = rows[1]; // Segunda-feira is index 1 (Domingo is 0)
    const shifts = primeira.querySelectorAll('.shift-row');
    expect(shifts.length).toBe(2);

    const copyBtn = primeira.querySelector('.btn-copy');
    expect(copyBtn).not.toBeNull();
  });
});
