import { compressImage, canCompressImage } from './image-compression.utils';

describe('image-compression.utils', () => {
  const originalImage = window.Image;
  const originalFileReader = window.FileReader;
  const originalCreateElement = document.createElement.bind(document);

  afterEach(() => {
    window.Image = originalImage;
    window.FileReader = originalFileReader;
    jest.restoreAllMocks();
    jest.useRealTimers();
  });

  const pngFile = () => new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], 'product.png', { type: 'image/png' });

  function stubSuccessfulBrowser(): { toBlob: jest.Mock; drawImage: jest.Mock } {
    (window as unknown as { FileReader: unknown }).FileReader = class {
      onerror: (() => void) | null = null;
      onabort: (() => void) | null = null;
      onload: ((event: { target: { result: string } }) => void) | null = null;
      result: string | null = null;
      readAsDataURL() {
        this.result = 'data:image/png;base64,AAAA';
        this.onload?.({ target: { result: this.result } });
      }
    };

    (window as unknown as { Image: unknown }).Image = class {
      onerror: (() => void) | null = null;
      onload: (() => void) | null = null;
      naturalWidth = 100;
      naturalHeight = 50;
      width = 100;
      height = 50;
      set src(_value: string) {
        this.onload?.();
      }
    };

    const drawImage = jest.fn();
    const toBlob = jest.fn((callback: BlobCallback, type?: string) => {
      callback(new Blob(['compressed'], { type }));
    });

    jest.spyOn(document, 'createElement').mockImplementation((tagName: string) => {
      if (tagName === 'canvas') {
        return {
          width: 0,
          height: 0,
          getContext: jest.fn(() => ({ drawImage })),
          toBlob,
        } as unknown as HTMLElement;
      }
      return originalCreateElement(tagName);
    });

    return { toBlob, drawImage };
  }

  it('does not compress AVIF or SVG originals', () => {
    expect(canCompressImage(new File(['x'], 'a.avif', { type: 'image/avif' }))).toBe(false);
    expect(canCompressImage(new File(['x'], 'a.svg', { type: 'image/svg+xml' }))).toBe(false);
    expect(canCompressImage(pngFile())).toBe(true);
  });

  it('returns the compressed file when the browser pipeline succeeds', async () => {
    const { toBlob, drawImage } = stubSuccessfulBrowser();
    const file = pngFile();

    const result = await compressImage(file);

    expect(toBlob).toHaveBeenCalled();
    expect(drawImage).toHaveBeenCalled();
    expect(result).not.toBe(file);
    expect(result.type).toBe('image/png');
    expect(result.name).toBe('product.png');
  });

  it('falls back to the original when FileReader errors', async () => {
    (window as unknown as { FileReader: unknown }).FileReader = class {
      onerror: (() => void) | null = null;
      onabort: (() => void) | null = null;
      onload: (() => void) | null = null;
      readAsDataURL() {
        this.onerror?.();
      }
    };
    const file = pngFile();

    await expect(compressImage(file)).resolves.toBe(file);
  });

  it('falls back to the original when the image cannot be decoded', async () => {
    (window as unknown as { FileReader: unknown }).FileReader = class {
      onerror: (() => void) | null = null;
      onabort: (() => void) | null = null;
      onload: ((event: { target: { result: string } }) => void) | null = null;
      result: string | null = null;
      readAsDataURL() {
        this.onload?.({ target: { result: 'data:image/png;base64,AAAA' } });
      }
    };
    (window as unknown as { Image: unknown }).Image = class {
      onerror: (() => void) | null = null;
      onload: (() => void) | null = null;
      width = 0;
      height = 0;
      set src(_value: string) {
        this.onerror?.();
      }
    };
    const file = pngFile();

    await expect(compressImage(file)).resolves.toBe(file);
  });

  it('never uploads a blank canvas when the 2d context is unavailable', async () => {
    const toBlob = jest.fn();
    (window as unknown as { FileReader: unknown }).FileReader = class {
      onerror: (() => void) | null = null;
      onabort: (() => void) | null = null;
      onload: ((event: { target: { result: string } }) => void) | null = null;
      readAsDataURL() {
        this.onload?.({ target: { result: 'data:image/png;base64,AAAA' } });
      }
    };
    (window as unknown as { Image: unknown }).Image = class {
      onerror: (() => void) | null = null;
      onload: (() => void) | null = null;
      naturalWidth = 10;
      naturalHeight = 10;
      width = 10;
      height = 10;
      set src(_value: string) {
        this.onload?.();
      }
    };
    jest.spyOn(document, 'createElement').mockImplementation((tagName: string) => {
      if (tagName === 'canvas') {
        return { width: 0, height: 0, getContext: jest.fn(() => null), toBlob } as unknown as HTMLElement;
      }
      return originalCreateElement(tagName);
    });
    const file = pngFile();

    await expect(compressImage(file)).resolves.toBe(file);
    expect(toBlob).not.toHaveBeenCalled();
  });

  it('falls back to the original when the encoder returns no blob', async () => {
    const { toBlob } = stubSuccessfulBrowser();
    toBlob.mockImplementation((callback: BlobCallback) => callback(null));
    const file = pngFile();

    await expect(compressImage(file)).resolves.toBe(file);
  });

  it('falls back to the original when the encoder returns an empty blob', async () => {
    const { toBlob } = stubSuccessfulBrowser();
    toBlob.mockImplementation((callback: BlobCallback, type?: string) => {
      callback(new Blob([], { type }));
    });
    const file = pngFile();

    await expect(compressImage(file)).resolves.toBe(file);
  });

  it('falls back to the original when toBlob never calls back', async () => {
    const { toBlob } = stubSuccessfulBrowser();
    toBlob.mockImplementation(() => undefined);
    jest.useFakeTimers();
    const file = pngFile();

    const promise = compressImage(file, { timeoutMs: 100 });
    jest.advanceTimersByTime(100);

    await expect(promise).resolves.toBe(file);
  });

  it('falls back to the original when FileReader never calls back', async () => {
    (window as unknown as { FileReader: unknown }).FileReader = class {
      onerror: (() => void) | null = null;
      onabort: (() => void) | null = null;
      onload: (() => void) | null = null;
      readAsDataURL() {
        // Simulates a FileReader that never fires any callback.
      }
    };
    jest.useFakeTimers();
    const file = pngFile();

    const promise = compressImage(file, { timeoutMs: 100 });
    jest.advanceTimersByTime(100);

    await expect(promise).resolves.toBe(file);
  });
});
