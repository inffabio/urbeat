export type RuntimePlatform = 'android' | 'ios' | 'windows' | 'linux' | 'macos' | 'web';

interface NavigatorLike {
  userAgent?: string;
  platform?: string;
  userAgentData?: { platform?: string };
}

export function detectPlatform(nav?: NavigatorLike | null): RuntimePlatform {
  try {
    const source: NavigatorLike | null | undefined = nav ?? (typeof window !== 'undefined' ? (window.navigator as NavigatorLike) : null);
    if (!source) return 'web';

    const ua = (source.userAgent ?? '').toLowerCase();
    const platform = (source.platform ?? '').toLowerCase();
    const userAgentDataPlatform = source.userAgentData?.platform?.toLowerCase() ?? '';

    if (ua.includes('android')) return 'android';
    if (/iphone|ipad|ipod/.test(ua)) return 'ios';
    if (ua.includes('windows') || userAgentDataPlatform.includes('windows') || platform.includes('win')) return 'windows';
    if (ua.includes('linux') || platform.includes('linux')) return 'linux';
    if (/mac os|macintosh/.test(ua) || platform.includes('mac')) return 'macos';

    return 'web';
  } catch {
    return 'web';
  }
}

export function isWindowsPlatform(nav?: NavigatorLike | null): boolean {
  return detectPlatform(nav) === 'windows';
}
