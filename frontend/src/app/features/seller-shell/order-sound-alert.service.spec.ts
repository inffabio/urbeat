import { TestBed } from '@angular/core/testing';
import * as fs from 'fs';
import * as path from 'path';
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

  it('should report playback failure when audio is blocked during enable', async () => {
    jest.spyOn(window.HTMLMediaElement.prototype, 'play').mockRejectedValue(new DOMException('blocked'));
    const service = TestBed.inject(OrderSoundAlertService);

    const enabled = await service.enable();

    expect(enabled).toBe(false);
    expect(service.enabled()).toBe(true);
    expect(service.needsActivation()).toBe(true);
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

  it('should clear the activation request once a later playback attempt succeeds', async () => {
    jest
      .spyOn(window.HTMLMediaElement.prototype, 'play')
      .mockRejectedValueOnce(new DOMException('blocked'))
      .mockResolvedValueOnce(undefined);
    const service = TestBed.inject(OrderSoundAlertService);

    await service.enable();
    expect(service.needsActivation()).toBe(true);

    const played = await service.playNewOrder();

    expect(played).toBe(true);
    expect(service.needsActivation()).toBe(false);
  });

  it('should not attempt playback while disabled', async () => {
    const play = jest.spyOn(window.HTMLMediaElement.prototype, 'play');
    const service = TestBed.inject(OrderSoundAlertService);

    const played = await service.playNewOrder();

    expect(played).toBe(false);
    expect(play).not.toHaveBeenCalled();
    expect(service.needsActivation()).toBe(false);
  });

  it('should disable sound, persist the choice, and clear the activation request', async () => {
    jest.spyOn(window.HTMLMediaElement.prototype, 'play').mockResolvedValueOnce(undefined);
    const service = TestBed.inject(OrderSoundAlertService);
    await service.enable();
    expect(service.needsActivation()).toBe(false);

    service.disable();

    expect(service.enabled()).toBe(false);
    expect(service.needsActivation()).toBe(false);
    expect(localStorage.getItem('urbeat:seller-order-sound')).toBe('off');
  });

  it('should start enabled when a persisted on preference exists', () => {
    localStorage.setItem('urbeat:seller-order-sound', 'on');
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({});

    const service = TestBed.inject(OrderSoundAlertService);

    expect(service.enabled()).toBe(true);
    expect(service.needsActivation()).toBe(false);
  });

  it('should start disabled when a persisted off preference exists', () => {
    localStorage.setItem('urbeat:seller-order-sound', 'off');
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({});

    const service = TestBed.inject(OrderSoundAlertService);

    expect(service.enabled()).toBe(false);
  });
});

describe('new-order sound asset', () => {
  it('should ship the mp3 at the path the service URL resolves to', () => {
    const expectedAsset = path.resolve(__dirname, '../../../assets/sounds/new-order.mp3');

    expect(fs.existsSync(expectedAsset)).toBe(true);
    expect(fs.statSync(expectedAsset).size).toBeGreaterThan(0);
  });
});
