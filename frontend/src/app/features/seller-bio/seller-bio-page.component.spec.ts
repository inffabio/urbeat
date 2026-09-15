import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
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
  const toastMock = { showError: jest.fn(), showSuccess: jest.fn(), showWarning: jest.fn() };

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
        { provide: ToastService, useValue: toastMock },
      ],
    }).compileComponents();
  });

  afterEach(() => {
    jest.clearAllMocks();
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

  it('uploads a replaced banner in its original format before saving its new Cloudinary URL', async () => {
    storeServiceMock.uploadImage.mockReturnValue(of({ url: 'https://res.cloudinary.com/demo/image/upload/v2/urbeat/banner-new.png' }));
    const fixture = TestBed.createComponent(SellerBioPageComponent);
    fixture.detectChanges();

    const file = new File(['banner'], 'banner.avif', { type: 'image/avif' });
    fixture.componentInstance.onBannerFile(file);
    fixture.componentInstance.save();
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(storeServiceMock.uploadImage).toHaveBeenCalledWith(file, 'banner');
    expect(storeServiceMock.updateStore).toHaveBeenCalledWith('store-1', expect.objectContaining({
      logoUrl: store.logoUrl,
      bannerUrl: 'https://res.cloudinary.com/demo/image/upload/v2/urbeat/banner-new.png',
    }));
  });

  it('does not save the bio as success when a logo upload fails', async () => {
    storeServiceMock.uploadImage.mockReturnValue(throwError(() => new Error('Upload failed')));
    const fixture = TestBed.createComponent(SellerBioPageComponent);
    fixture.detectChanges();

    const file = new File(['logo'], 'logo.png', { type: 'image/png' });
    fixture.componentInstance.onLogoFile(file);
    fixture.componentInstance.save();
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(storeServiceMock.uploadImage).toHaveBeenCalledWith(file, 'logo');
    expect(storeServiceMock.updateStore).not.toHaveBeenCalled();
    expect(fixture.componentInstance.saving()).toBe(false);
    expect(fixture.componentInstance.dirty()).toBe(true);
    expect(toastMock.showError).toHaveBeenCalledWith('Falha ao enviar logo.');
  });

  it('does not overwrite the interceptor 413 size message with a generic toast when a logo upload fails', async () => {
    storeServiceMock.uploadImage.mockReturnValue(throwError(() => ({ status: 413, message: 'Payload Too Large' })));
    const fixture = TestBed.createComponent(SellerBioPageComponent);
    fixture.detectChanges();

    const file = new File(['logo'], 'logo.png', { type: 'image/png' });
    fixture.componentInstance.onLogoFile(file);
    fixture.componentInstance.save();
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(storeServiceMock.uploadImage).toHaveBeenCalledWith(file, 'logo');
    expect(storeServiceMock.updateStore).not.toHaveBeenCalled();
    expect(fixture.componentInstance.saving()).toBe(false);
    expect(fixture.componentInstance.dirty()).toBe(true);
    expect(toastMock.showError).not.toHaveBeenCalledWith('Falha ao enviar logo.');
  });

  it('opens the store storefront in a new tab from the preview', () => {
    const fixture = TestBed.createComponent(SellerBioPageComponent);
    fixture.detectChanges();
    const openSpy = jest.spyOn(window, 'open').mockImplementation(() => null);

    fixture.componentInstance.openStorefront();

    expect(openSpy).toHaveBeenCalledWith('/loja-teste', '_blank', 'noopener,noreferrer');
    openSpy.mockRestore();
  });

  it('shows only the configured banner without promotional fallback copy', () => {
    const fixture = TestBed.createComponent(SellerBioPageComponent);
    fixture.detectChanges();

    const previewBanner = fixture.nativeElement.querySelector('.preview-banner') as HTMLElement;

    expect(previewBanner.textContent).not.toContain('Hambúrgueres');
    expect(previewBanner.textContent).not.toContain('Sabor que marca');
  });

  it('lists every supported image format in the requirements card', () => {
    const fixture = TestBed.createComponent(SellerBioPageComponent);
    fixture.detectChanges();

    const requirements = fixture.nativeElement.querySelector('.info-card:last-child') as HTMLElement;

    expect(requirements.textContent).toContain('AVIF');
    expect(requirements.textContent).toContain('PNG');
    expect(requirements.textContent).toContain('SVG');
    expect(requirements.textContent).toContain('WEBP');
    expect(requirements.textContent).toContain('JPG');
    expect(requirements.textContent).toContain('JPEG');
  });
});
