import {
  closeExternalWindow,
  navigateExternalWindow,
  openExternalWindow,
  reserveExternalWindow,
  toAbsoluteHttpUrl,
  whatsAppNumber,
} from './whatsapp.utils';

describe('whatsapp.utils', () => {
  afterEach(() => {
    jest.restoreAllMocks();
  });

  describe('whatsAppNumber', () => {
    it('prefixes the country code for a local 11-digit mobile number', () => {
      expect(whatsAppNumber('11988887777')).toBe('5511988887777');
      expect(whatsAppNumber('(11) 98888-7777')).toBe('5511988887777');
    });

    it('keeps a number that already carries the +55 country code', () => {
      expect(whatsAppNumber('5511988887777')).toBe('5511988887777');
      expect(whatsAppNumber('+55 (11) 98888-7777')).toBe('5511988887777');
    });

    it('keeps a local DDD 55 number intact instead of treating it as a country code', () => {
      expect(whatsAppNumber('55999990001')).toBe('5555999990001');
    });

    it('returns null when there is no digit to dial', () => {
      expect(whatsAppNumber('')).toBeNull();
      expect(whatsAppNumber(null)).toBeNull();
    });
  });

  describe('toAbsoluteHttpUrl', () => {
    it('keeps an absolute https link', () => {
      expect(toAbsoluteHttpUrl('https://urbeat.com.br/rota/token')).toBe(
        'https://urbeat.com.br/rota/token',
      );
    });

    it('resolves a relative link against the current origin', () => {
      expect(toAbsoluteHttpUrl('/entrega/token')).toBe(
        new URL('/entrega/token', document.baseURI).toString(),
      );
    });

    it('rejects empty and unsafe links', () => {
      expect(toAbsoluteHttpUrl(null)).toBeNull();
      expect(toAbsoluteHttpUrl('javascript:alert(1)')).toBeNull();
    });
  });

  describe('external window helpers', () => {
    it('reserves a placeholder tab inside the user gesture and severs its opener', () => {
      const reserved = { opener: {} as unknown, close: jest.fn() } as unknown as Window;
      const openSpy = jest.spyOn(window, 'open').mockImplementation(() => reserved);

      expect(reserveExternalWindow()).toBe(reserved);
      expect(openSpy).toHaveBeenCalledWith('about:blank', '_blank');
      expect((reserved as { opener: unknown }).opener).toBeNull();
    });

    it('reports a blocked reservation instead of inventing a window', () => {
      jest.spyOn(window, 'open').mockImplementation(() => null);

      expect(reserveExternalWindow()).toBeNull();
    });

    it('opens an external URL without the noopener feature and severs the opener', () => {
      const opened = { opener: {} as unknown } as unknown as Window;
      const openSpy = jest.spyOn(window, 'open').mockImplementation(() => opened);

      expect(openExternalWindow('https://wa.me/5511988887777')).toBe(true);
      expect(openSpy).toHaveBeenCalledWith('https://wa.me/5511988887777', '_blank');
      expect((opened as { opener: unknown }).opener).toBeNull();
    });

    it('never opens a non-http(s) URL', () => {
      const openSpy = jest.spyOn(window, 'open').mockImplementation(() => null);

      expect(openExternalWindow('javascript:alert(1)')).toBe(false);
      expect(openSpy).not.toHaveBeenCalled();
    });

    it('navigates the reserved tab and closes it when it is no longer usable', () => {
      const reserved = { closed: false, location: { href: '' }, close: jest.fn() } as unknown as Window;

      expect(navigateExternalWindow(reserved, 'https://wa.me/5511988887777')).toBe(true);
      expect(reserved.location.href).toBe('https://wa.me/5511988887777');

      expect(navigateExternalWindow(reserved, 'javascript:alert(1)')).toBe(false);
      expect(reserved.location.href).toBe('https://wa.me/5511988887777');

      closeExternalWindow(reserved);
      expect((reserved as unknown as { close: jest.Mock }).close).toHaveBeenCalled();

      expect(navigateExternalWindow(null, 'https://wa.me/5511988887777')).toBe(false);
    });
  });
});
