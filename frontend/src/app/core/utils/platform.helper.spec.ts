import { detectPlatform, isWindowsPlatform } from './platform.helper';

describe('detectPlatform', () => {
  it('detects Android from the user agent', () => {
    expect(detectPlatform({ userAgent: 'Mozilla/5.0 (Linux; Android 13)' })).toBe('android');
  });

  it('detects iOS from the user agent', () => {
    expect(detectPlatform({ userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17)' })).toBe('ios');
  });

  it('detects Windows from the user agent', () => {
    expect(detectPlatform({ userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)' })).toBe('windows');
  });

  it('detects Windows from the navigator.platform fallback', () => {
    expect(detectPlatform({ userAgent: 'Mozilla/5.0', platform: 'Win32' })).toBe('windows');
  });

  it('detects Linux from the user agent', () => {
    expect(detectPlatform({ userAgent: 'Mozilla/5.0 (X11; Linux x86_64)' })).toBe('linux');
  });

  it('detects macOS from the user agent', () => {
    expect(detectPlatform({ userAgent: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)' })).toBe('macos');
  });

  it('falls back to web for unknown agents', () => {
    expect(detectPlatform({ userAgent: 'Mozilla/5.0 (jsdom)' })).toBe('web');
  });

  it('returns web when no navigator is available', () => {
    expect(detectPlatform(null)).toBe('web');
    expect(detectPlatform(undefined)).toBe('web');
  });

  it('never throws for malformed input', () => {
    expect(() => detectPlatform({ userAgent: undefined, platform: undefined })).not.toThrow();
    expect(detectPlatform({} as never)).toBe('web');
  });
});

describe('isWindowsPlatform', () => {
  it('is true for a Windows navigator', () => {
    expect(isWindowsPlatform({ userAgent: 'Windows NT 10.0', platform: 'Win32' })).toBe(true);
  });

  it('is false for a Linux navigator', () => {
    expect(isWindowsPlatform({ userAgent: 'X11; Linux x86_64', platform: 'Linux' })).toBe(false);
  });

  it('is false for an Android navigator', () => {
    expect(isWindowsPlatform({ userAgent: 'Android 13', platform: 'Linux armv8l' })).toBe(false);
  });
});
