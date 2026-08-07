import { TestBed } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AlertController, ToastController } from '@ionic/angular';
import { SellerNeighborhoodsPageComponent } from './seller-neighborhoods-page.component';
import { StoreService } from '../../core/services/store.service';
import { ToastService } from '../../core/services/toast.service';

describe('SellerNeighborhoodsPageComponent', () => {
  const storeServiceMock = {
    getMyStore: jest.fn(),
    getStoreAddress: jest.fn(),
    getDeliveryNeighborhoodsByStore: jest.fn(),
    updateDeliveryConfig: jest.fn(),
  };

  const toastServiceMock = {
    showError: jest.fn(),
    showSuccess: jest.fn(),
    showWarning: jest.fn(),
    showInfo: jest.fn(),
  };

  beforeEach(async () => {
    jest.clearAllMocks();
    jest.spyOn(window, 'confirm').mockReturnValue(true);

    storeServiceMock.getMyStore.mockReturnValue(of({
      id: 'store-123',
      deliveryAreas: [{ id: 'a1', neighborhood: 'Centro', deliveryFee: 5 }],
    }));
    storeServiceMock.getStoreAddress.mockReturnValue(of({ city: 'Sao Paulo', state: 'SP' }));
    storeServiceMock.getDeliveryNeighborhoodsByStore.mockReturnValue(of([]));

    await TestBed.configureTestingModule({
      imports: [SellerNeighborhoodsPageComponent],
      schemas: [NO_ERRORS_SCHEMA],
      providers: [
        provideRouter([]),
        { provide: StoreService, useValue: storeServiceMock },
        { provide: ToastService, useValue: toastServiceMock },
        { provide: AlertController, useValue: { create: jest.fn() } },
        { provide: ToastController, useValue: { create: jest.fn().mockResolvedValue({ present: jest.fn().mockResolvedValue(undefined) }) } },
      ],
    }).compileComponents();
  });

  it('asks for confirmation before removing a neighborhood', async () => {
    const confirmSpy = jest.spyOn(window, 'confirm').mockReturnValue(false);
    const fixture = TestBed.createComponent(SellerNeighborhoodsPageComponent);
    fixture.detectChanges();

    await fixture.componentInstance.removeArea(0);

    expect(confirmSpy).toHaveBeenCalledWith('Excluir o bairro "Centro"?');
    expect(fixture.componentInstance.areas.length).toBe(1);
  });

  it('restores the persisted neighborhood configuration when cancelling changes', () => {
    const fixture = TestBed.createComponent(SellerNeighborhoodsPageComponent);
    fixture.detectChanges();
    fixture.componentInstance.formDirty.set(true);
    fixture.componentInstance.areas.at(0).get('deliveryFee')?.setValue('99,00');

    fixture.componentInstance.cancelChanges();

    expect(fixture.componentInstance.formDirty()).toBe(false);
    expect(fixture.componentInstance.areas.at(0).value.deliveryFee).toBe('5,00');
  });
});
