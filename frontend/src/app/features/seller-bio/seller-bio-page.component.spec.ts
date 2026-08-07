import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { StoreService } from '../../core/services/store.service';
import { ToastService } from '../../core/services/toast.service';
import { SellerBioPageComponent } from './seller-bio-page.component';

describe('SellerBioPageComponent', () => {
  const store = {
    id: 'store-1',
    ownerUserId: 'owner-1',
    name: 'Loja Teste',
    slug: 'loja-teste',
    phoneNumber: '21999999999',
    description: 'Bio da loja',
    cuisineType: 'Lanches',
    logoUrl: 'https://res.cloudinary.com/demo/image/upload/v1/urbeat/logo-old.png',
    bannerUrl: 'https://res.cloudinary.com/demo/image/upload/v1/urbeat/banner-old.png',
    isOpen: true,
    isSubscriptionBlocked: false,
    supportsDelivery: true,
    supportsPickup: true,
    deliveryFee: 0,
    minimumOrderValue: 0,
    deliveryAreas: [],
    averageRating: 0,
    totalReviews: 0,
  };

  let storeServiceMock: { getMyStore: jest.Mock; updateStore: jest.Mock; uploadImage: jest.Mock };

  beforeEach(async () => {
    if (!URL.createObjectURL) URL.createObjectURL = jest.fn(() => 'blob:test') as unknown as typeof URL.createObjectURL;
    if (!URL.revokeObjectURL) URL.revokeObjectURL = jest.fn() as typeof URL.revokeObjectURL;
    storeServiceMock = {
      getMyStore: jest.fn().mockReturnValue(of(store)),
      updateStore: jest.fn().mockReturnValue(of(store)),
      uploadImage: jest.fn(),
    };

    await TestBed.configureTestingModule({
      imports: [SellerBioPageComponent],
      providers: [
        provideRouter([]),
        { provide: StoreService, useValue: storeServiceMock },
        { provide: ToastService, useValue: { showError: jest.fn(), showSuccess: jest.fn(), showWarning: jest.fn() } },
      ],
    }).compileComponents();
  });

  it('sends null for a removed logo so the backend removes the Cloudinary asset', async () => {
    const fixture = TestBed.createComponent(SellerBioPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.removeLogo();
    fixture.componentInstance.save();
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(storeServiceMock.updateStore).toHaveBeenCalledWith('store-1', expect.objectContaining({
      logoUrl: null,
      bannerUrl: store.bannerUrl,
    }));
  });

  it('uploads a replaced banner before saving its new Cloudinary URL', async () => {
    storeServiceMock.uploadImage.mockReturnValue(of({ url: 'https://res.cloudinary.com/demo/image/upload/v2/urbeat/banner-new.png' }));
    const fixture = TestBed.createComponent(SellerBioPageComponent);
    fixture.detectChanges();

    const file = new File(['banner'], 'banner.png', { type: 'image/png' });
    fixture.componentInstance.onBannerFile({ target: { files: [file] } } as unknown as Event);
    fixture.componentInstance.save();
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(storeServiceMock.uploadImage).toHaveBeenCalledWith(file, 'banner');
    expect(storeServiceMock.updateStore).toHaveBeenCalledWith('store-1', expect.objectContaining({
      logoUrl: store.logoUrl,
      bannerUrl: 'https://res.cloudinary.com/demo/image/upload/v2/urbeat/banner-new.png',
    }));
  });

  it('opens the store storefront in a new tab from the preview', () => {
    const fixture = TestBed.createComponent(SellerBioPageComponent);
    fixture.detectChanges();
    const openSpy = jest.spyOn(window, 'open').mockImplementation(() => null);

    fixture.componentInstance.openStorefront();

    expect(openSpy).toHaveBeenCalledWith('/loja-teste', '_blank', 'noopener,noreferrer');
    openSpy.mockRestore();
  });
});
