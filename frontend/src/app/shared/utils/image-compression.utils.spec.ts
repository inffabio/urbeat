import { compressImage, canCompressImage, DEFAULT_COMPRESSION_TARGET_BYTES } from './image-compression.utils';

interface EncodeAttempt {
  width: number;
  height: number;
  quality: number;
  type: string;
}

interface StubOptions {
  naturalWidth?: number;
  naturalHeight?: number;
  /** Returns the encoded size in bytes, or null to simulate an encoder failure. */
  sizeFor: (attempt: Omit<EncodeAttempt, 'type'> & { type: string }) => number | null;
  /** Overrides the MIME returned by the encoder (simulates browser format fallback). */
  producedType?: string;
}

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

  const fileOf = (name: string, type: string, size = 2000): File =>
    new File([new Uint8Array(size)], name, { type });

  function stubBrowser(options: StubOptions): {
    canvas: { width: number; height: number; toBlob: jest.Mock; getContext: jest.Mock };
    context: { drawImage: jest.Mock; clearRect: jest.Mock; fillRect: jest.Mock };
    attempts: EncodeAttempt[];
  } {
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
      naturalWidth = options.naturalWidth ?? 1000;
      naturalHeight = options.naturalHeight ?? 500;
      width = this.naturalWidth;
      height = this.naturalHeight;
      set src(_value: string) {
        this.onload?.();
      }
    };

    const attempts: EncodeAttempt[] = [];
    const context = {
      drawImage: jest.fn(),
      clearRect: jest.fn(),
      fillRect: jest.fn(),
      fillStyle: '',
      imageSmoothingEnabled: false,
      imageSmoothingQuality: 'low',
    };

    const canvas = {
      width: 0,
      height: 0,
      getContext: jest.fn(() => context),
      toBlob: jest.fn((callback: BlobCallback, type?: string, quality?: number) => {
        const attempt = { width: canvas.width, height: canvas.height, quality: quality ?? 0, type: type ?? '' };
        attempts.push(attempt);
        const size = options.sizeFor(attempt);
        if (size === null) {
          callback(null);
          return;
        }

        callback(new Blob([new Uint8Array(size)], { type: options.producedType ?? type }));
      }),
    };

    jest.spyOn(document, 'createElement').mockImplementation((tagName: string) => {
      if (tagName === 'canvas') {
        return canvas as unknown as HTMLElement;
      }
      return originalCreateElement(tagName);
    });

    return { canvas, context, attempts };
  }

  it('classifies only raster formats the browser can re-encode', () => {
    expect(canCompressImage(fileOf('a.png', 'image/png'))).toBe(true);
    expect(canCompressImage(fileOf('a.jpg', 'image/jpeg'))).toBe(true);
    expect(canCompressImage(fileOf('a.jpeg', 'image/jpg'))).toBe(true);
    expect(canCompressImage(fileOf('a.webp', 'image/webp'))).toBe(true);
    expect(canCompressImage(fileOf('a.png', ''))).toBe(true);
    expect(canCompressImage(fileOf('a.avif', 'image/avif'))).toBe(false);
    expect(canCompressImage(fileOf('a.svg', 'image/svg+xml'))).toBe(false);
  });

  it('defaults the compression target to 950 KiB', () => {
    expect(DEFAULT_COMPRESSION_TARGET_BYTES).toBe(950 * 1024);
  });

  it('returns a file that is already under the target without touching the canvas', async () => {
    const { canvas } = stubBrowser({ sizeFor: () => 10 });
    const file = fileOf('logo.png', 'image/png', 500);

    const result = await compressImage(file, { maxBytes: 1000 });

    expect(result).toBe(file);
    expect(canvas.toBlob).not.toHaveBeenCalled();
    expect(canvas.getContext).not.toHaveBeenCalled();
  });

  it('never re-encodes SVG or AVIF originals', async () => {
    const { canvas } = stubBrowser({ sizeFor: () => 10 });

    for (const file of [fileOf('logo.svg', 'image/svg+xml'), fileOf('photo.avif', 'image/avif')]) {
      await expect(compressImage(file, { maxBytes: 1000 })).resolves.toBe(file);
    }

    expect(canvas.toBlob).not.toHaveBeenCalled();
  });

  it('reduces quality before reducing dimensions and keeps the aspect ratio', async () => {
    const { canvas, attempts } = stubBrowser({
      naturalWidth: 1000,
      naturalHeight: 500,
      // The first two qualities are too heavy; 0.7 fits without resizing.
      sizeFor: ({ quality }) => (quality > 0.75 ? 1500 : 900),
    });

    const file = fileOf('photo.jpg', 'image/jpeg', 2000);
    const result = await compressImage(file, { maxBytes: 1000 });

    expect(result).not.toBe(file);
    expect(result.size).toBe(900);
    expect(result.type).toBe('image/jpeg');
    expect(result.name).toBe('photo.jpg');
    expect(attempts.map((attempt) => attempt.quality)).toEqual([0.9, 0.8, 0.7]);
    expect(attempts.every((attempt) => attempt.width === 1000 && attempt.height === 500)).toBe(true);
    expect(canvas.width).toBe(1000);
    expect(canvas.height).toBe(500);
  });

  it('reduces dimensions proportionally only after every quality attempt fails', async () => {
    const { canvas, attempts } = stubBrowser({
      naturalWidth: 1000,
      naturalHeight: 500,
      sizeFor: ({ width }) => (width === 1000 ? 2000 : 900),
    });

    const result = await compressImage(fileOf('photo.png', 'image/png', 2000), { maxBytes: 1000 });

    expect(result.size).toBe(900);
    expect(result.type).toBe('image/png');

    const firstRound = attempts.filter((attempt) => attempt.width === 1000);
    expect(firstRound.map((attempt) => attempt.quality)).toEqual([0.9, 0.8, 0.7, 0.6, 0.5, 0.4]);

    const resized = attempts.find((attempt) => attempt.width !== 1000);
    expect(resized).toBeDefined();
    expect(resized!.width).toBe(800);
    expect(resized!.height).toBe(400);
    expect(resized!.width / resized!.height).toBe(2);
    expect(resized!.quality).toBe(0.9);
    expect(canvas.width).toBe(800);
    expect(canvas.height).toBe(400);
  });

  it('preserves transparency for PNG by encoding PNG without flattening the canvas', async () => {
    const { context } = stubBrowser({ sizeFor: () => 900 });

    const result = await compressImage(fileOf('logo.png', 'image/png', 2000), { maxBytes: 1000 });

    expect(result.type).toBe('image/png');
    expect(context.clearRect).toHaveBeenCalled();
    expect(context.fillRect).not.toHaveBeenCalled();
  });

  it('preserves transparency semantics for WebP output', async () => {
    const { canvas, context } = stubBrowser({ sizeFor: () => 900 });

    const result = await compressImage(fileOf('logo.webp', 'image/webp', 2000), { maxBytes: 1000 });

    expect(result.type).toBe('image/webp');
    expect(canvas.toBlob).toHaveBeenCalledWith(expect.any(Function), 'image/webp', expect.any(Number));
    expect(context.clearRect).toHaveBeenCalled();
  });

  it('flattens JPEG output over white because JPEG has no alpha channel', async () => {
    const { context } = stubBrowser({ sizeFor: () => 900 });

    const result = await compressImage(fileOf('photo.jpg', 'image/jpeg', 2000), { maxBytes: 1000 });

    expect(result.type).toBe('image/jpeg');
    expect(context.fillRect).toHaveBeenCalled();
    expect(context.clearRect).not.toHaveBeenCalled();
  });

  it('normalizes the image/jpg alias to image/jpeg', async () => {
    const { canvas } = stubBrowser({ sizeFor: () => 900 });

    const result = await compressImage(fileOf('photo.jpg', 'image/jpg', 2000), { maxBytes: 1000 });

    expect(result.type).toBe('image/jpeg');
    expect(canvas.toBlob).toHaveBeenCalledWith(expect.any(Function), 'image/jpeg', expect.any(Number));
  });

  it('infers the raster MIME from the extension when the browser reports none', async () => {
    const { canvas } = stubBrowser({ sizeFor: () => 900 });

    const result = await compressImage(fileOf('asset.webp', '', 2000), { maxBytes: 1000 });

    expect(result.type).toBe('image/webp');
    expect(canvas.toBlob).toHaveBeenCalledWith(expect.any(Function), 'image/webp', expect.any(Number));
  });

  it('respects an explicit maxWidth by scaling proportionally before encoding', async () => {
    const { attempts } = stubBrowser({ naturalWidth: 1000, naturalHeight: 500, sizeFor: () => 900 });

    await compressImage(fileOf('photo.png', 'image/png', 2000), { maxBytes: 1000, maxWidth: 400 });

    expect(attempts[0].width).toBe(400);
    expect(attempts[0].height).toBe(200);
  });

  it('returns the original when nothing fits even after reducing dimensions', async () => {
    const { canvas } = stubBrowser({
      naturalWidth: 100,
      naturalHeight: 50,
      sizeFor: () => 1000,
    });
    const file = fileOf('huge.png', 'image/png', 2000);

    const result = await compressImage(file, { maxBytes: 900 });

    expect(result).toBe(file);
    expect(canvas.toBlob).toHaveBeenCalled();
  });

  it('falls back to the original when the browser falls back to another MIME', async () => {
    stubBrowser({ sizeFor: () => 900, producedType: 'image/png' });
    const file = fileOf('photo.jpg', 'image/jpeg', 2000);

    await expect(compressImage(file, { maxBytes: 1000 })).resolves.toBe(file);
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
    const file = fileOf('photo.png', 'image/png', 2000);

    await expect(compressImage(file, { maxBytes: 1000 })).resolves.toBe(file);
  });

  it('falls back to the original when the image cannot be decoded', async () => {
    stubBrowser({ sizeFor: () => 900 });
    (window as unknown as { Image: unknown }).Image = class {
      onerror: (() => void) | null = null;
      onload: (() => void) | null = null;
      width = 0;
      height = 0;
      set src(_value: string) {
        this.onerror?.();
      }
    };
    const file = fileOf('photo.png', 'image/png', 2000);

    await expect(compressImage(file, { maxBytes: 1000 })).resolves.toBe(file);
  });

  it('never uploads a blank canvas when the 2d context is unavailable', async () => {
    const { canvas } = stubBrowser({ sizeFor: () => 900 });
    canvas.getContext.mockReturnValue(null);
    const file = fileOf('photo.png', 'image/png', 2000);

    await expect(compressImage(file, { maxBytes: 1000 })).resolves.toBe(file);
    expect(canvas.toBlob).not.toHaveBeenCalled();
  });

  it('falls back to the original when the encoder returns no blob', async () => {
    stubBrowser({ sizeFor: () => null });
    const file = fileOf('photo.png', 'image/png', 2000);

    await expect(compressImage(file, { maxBytes: 1000 })).resolves.toBe(file);
  });

  it('falls back to the original when the encoder returns an empty blob', async () => {
    stubBrowser({ sizeFor: () => 0 });
    const file = fileOf('photo.png', 'image/png', 2000);

    await expect(compressImage(file, { maxBytes: 1000 })).resolves.toBe(file);
  });

  it('falls back to the original when toBlob never calls back', async () => {
    const { canvas } = stubBrowser({ sizeFor: () => 900 });
    canvas.toBlob.mockImplementation(() => undefined);
    jest.useFakeTimers();
    const file = fileOf('photo.png', 'image/png', 2000);

    const promise = compressImage(file, { maxBytes: 1000, timeoutMs: 100 });
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
    const file = fileOf('photo.png', 'image/png', 2000);

    const promise = compressImage(file, { maxBytes: 1000, timeoutMs: 100 });
    jest.advanceTimersByTime(100);

    await expect(promise).resolves.toBe(file);
  });
});
