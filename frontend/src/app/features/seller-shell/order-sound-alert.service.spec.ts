import { TestBed } from '@angular/core/testing';
import { OrderSoundAlertService } from './order-sound-alert.service';

describe('OrderSoundAlertService', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({});
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('should persist enabled preference when enabled', async () => {
    jest.spyOn(window.HTMLMediaElement.prototype, 'play').mockResolvedValueOnce(undefined);
    const service = TestBed.inject(OrderSoundAlertService);

    const enabled = await service.enable();

    expect(enabled).toBe(true);
    expect(service.enabled()).toBe(true);
    expect(localStorage.getItem('urbeat:seller-order-sound')).toBe('on');
  });

  it('should return false when audio playback is blocked', async () => {
    const service = TestBed.inject(OrderSoundAlertService);
    jest.spyOn(window.HTMLMediaElement.prototype, 'play').mockRejectedValue(new DOMException('blocked'));

    await service.enable();
    const played = await service.playNewOrder();

    expect(played).toBe(false);
    expect(service.needsActivation()).toBe(true);
  });
});
