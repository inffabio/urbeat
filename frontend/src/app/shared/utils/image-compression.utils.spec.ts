import { compressImage, canCompressImage, DEFAULT_COMPRESSION_TARGET_BYTES } from './image-compression.utils';

interface EncodeAttempt {
  width: number;
  height: number;
  quality?: number;
  type: string;
}

interface StubOptions {
  naturalWidth?: number;
  naturalHeight?: number;
  /** Returns the encoded size in bytes, or null to simulate an unsupported/failed encoder. */
  sizeFor: (attempt: EncodeAttempt) => number | null;
  /** Overrides the MIME returned by the encoder (simulates browser format fallback). */
  producedType?: string | ((type: string) => string);
  /** Data URL returned by the FileReader used to decode the image. */
  dataUrl?: string;
  /** Source text returned by the FileReader used to read/prepare SVG documents. */
  svgText?: string;
}

describe('image-compression.utils', () => {
  const originalImage = window.Image;
  const originalFileReader = window.FileReader;
  const originalCreateElement = document.createElement.bind(document);
  const textEncoder = new TextEncoder();

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
      onload: ((event: { target: { result: string | ArrayBuffer } }) => void) | null = null;
      result: string | ArrayBuffer | null = null;

      readAsDataURL() {
        this.result = options.dataUrl ?? 'data:image/png;base64,AAAA';
        this.onload?.({ target: { result: this.result } });
      }

      readAsArrayBuffer() {
        const bytes = textEncoder.encode(options.svgText ?? '');
        this.result = bytes.buffer;
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
        const attempt = { width: canvas.width, height: canvas.height, quality, type: type ?? '' };
        attempts.push(attempt);
        const size = options.sizeFor(attempt);
        if (size === null || size <= 0) {
          callback(null);
          return;
        }

        const produced =
          typeof options.producedType === 'function'
            ? options.producedType(type ?? '')
            : options.producedType ?? type ?? '';
        callback(new Blob([new Uint8Array(size)], { type: produced }));
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

  function svgFileOf(content: string, name = 'logo.svg'): File {
    return new File([content], name, { type: 'image/svg+xml' });
  }

  it('classifies every accepted upload format as compressible', () => {
    expect(canCompressImage(fileOf('a.png', 'image/png'))).toBe(true);
    expect(canCompressImage(fileOf('a.jpg', 'image/jpeg'))).toBe(true);
    expect(canCompressImage(fileOf('a.jpeg', 'image/jpg'))).toBe(true);
    expect(canCompressImage(fileOf('a.webp', 'image/webp'))).toBe(true);
    expect(canCompressImage(fileOf('a.avif', 'image/avif'))).toBe(true);
    expect(canCompressImage(fileOf('a.svg', 'image/svg+xml'))).toBe(true);
    expect(canCompressImage(fileOf('a.png', ''))).toBe(true);
    expect(canCompressImage(fileOf('a.avif', 'application/octet-stream'))).toBe(true);
    expect(canCompressImage(fileOf('a.gif', 'image/gif'))).toBe(false);
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

  it('leaves SVG and AVIF originals untouched when they already fit the target', async () => {
    const { canvas } = stubBrowser({ sizeFor: () => 10, svgText: '<svg/>' });

    for (const file of [svgFileOf('<svg/>'), fileOf('photo.avif', 'image/avif', 500)]) {
      await expect(compressImage(file, { maxBytes: 1000 })).resolves.toBe(file);
    }

    expect(canvas.toBlob).not.toHaveBeenCalled();
  });

  it('reduces quality before reducing dimensions and keeps the aspect ratio for JPEG', async () => {
    const { canvas, attempts } = stubBrowser({
      naturalWidth: 1000,
      naturalHeight: 500,
      // The first two qualities are too heavy; 0.7 fits without resizing.
      sizeFor: ({ quality }) => ((quality ?? 0) > 0.75 ? 1500 : 900),
    });

    const file = fileOf('photo.jpg', 'image/jpeg', 2000);
    const result = await compressImage(file, { maxBytes: 1000 });

    expect(result).not.toBe(file);
    expect(result.size).toBe(900);
    expect(result.type).toBe('image/jpeg');
    expect(result.name).toBe('photo.jpg');
    expect(attempts.map((attempt) => attempt.quality)).toEqual([0.9, 0.8, 0.7]);
    expect(attempts.every((attempt) => attempt.type === 'image/jpeg')).toBe(true);
    expect(attempts.every((attempt) => attempt.width === 1000 && attempt.height === 500)).toBe(true);
    expect(canvas.width).toBe(1000);
    expect(canvas.height).toBe(500);
  });

  it('reduces dimensions proportionally only after every JPEG quality attempt fails', async () => {
    const { canvas, attempts } = stubBrowser({
      naturalWidth: 1000,
      naturalHeight: 500,
      sizeFor: ({ width }) => (width === 1000 ? 2000 : 900),
    });

    const result = await compressImage(fileOf('photo.jpg', 'image/jpeg', 2000), { maxBytes: 1000 });

    expect(result.size).toBe(900);
    expect(result.type).toBe('image/jpeg');

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

  it('tries lossless PNG first, then WebP at the same dimensions before resizing', async () => {
    const { attempts } = stubBrowser({
      sizeFor: ({ type }) => (type === 'image/png' ? 2000 : 900),
    });

    const result = await compressImage(fileOf('logo.png', 'image/png', 2000), { maxBytes: 1000 });

    expect(result.type).toBe('image/webp');
    expect(result.name).toBe('logo.webp');
    expect(result.size).toBe(900);

    expect(attempts[0]).toMatchObject({ type: 'image/png', width: 1000, height: 500 });
    expect(attempts[1]).toMatchObject({ type: 'image/webp', quality: 0.9, width: 1000, height: 500 });
  });

  it('preserves transparency for PNG by encoding PNG without flattening the canvas', async () => {
    const { context } = stubBrowser({ sizeFor: () => 900 });

    const result = await compressImage(fileOf('logo.png', 'image/png', 2000), { maxBytes: 1000 });

    expect(result.type).toBe('image/png');
    expect(context.clearRect).toHaveBeenCalled();
    expect(context.fillRect).not.toHaveBeenCalled();
  });

  it('keeps transparency when a PNG has to be converted to WebP', async () => {
    const { context } = stubBrowser({
      sizeFor: ({ type }) => (type === 'image/png' ? 2000 : 900),
    });

    const result = await compressImage(fileOf('logo.png', 'image/png', 2000), { maxBytes: 1000 });

    expect(result.type).toBe('image/webp');
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

  it('falls back to the original when every encoder returns an unsupported format', async () => {
    stubBrowser({ sizeFor: () => null, producedType: 'image/png' });
    const file = fileOf('photo.avif', 'image/avif', 2000);

    await expect(compressImage(file, { maxBytes: 1000 })).resolves.toBe(file);
  });

  it('converts AVIF to WebP when the browser cannot encode AVIF', async () => {
    const { attempts } = stubBrowser({
      sizeFor: ({ type }) => (type === 'image/avif' ? null : type === 'image/webp' ? 900 : 2000),
    });

    const file = fileOf('photo.avif', 'image/avif', 2000);
    const result = await compressImage(file, { maxBytes: 1000 });

    expect(result.type).toBe('image/webp');
    expect(result.name).toBe('photo.webp');
    expect(result.size).toBe(900);
    expect(attempts[0].type).toBe('image/avif');
    expect(attempts[1].type).toBe('image/webp');
  });

  it('keeps AVIF when the browser can actually encode it', async () => {
    stubBrowser({ sizeFor: ({ type }) => (type === 'image/avif' ? 900 : 2000) });

    const result = await compressImage(fileOf('photo.avif', 'image/avif', 2000), { maxBytes: 1000 });

    expect(result.type).toBe('image/avif');
    expect(result.name).toBe('photo.avif');
    expect(result.size).toBe(900);
  });

  it('converts AVIF to PNG when neither AVIF nor WebP encoding is available', async () => {
    stubBrowser({ sizeFor: ({ type }) => (type === 'image/png' ? 900 : null) });

    const result = await compressImage(fileOf('photo.avif', 'image/avif', 2000), { maxBytes: 1000 });

    expect(result.type).toBe('image/png');
    expect(result.name).toBe('photo.png');
    expect(result.size).toBe(900);
  });

  it('converts WebP to PNG when WebP encoding is unavailable', async () => {
    stubBrowser({ sizeFor: ({ type }) => (type === 'image/webp' ? null : 900) });

    const result = await compressImage(fileOf('logo.webp', 'image/webp', 2000), { maxBytes: 1000 });

    expect(result.type).toBe('image/png');
    expect(result.name).toBe('logo.png');
    expect(result.size).toBe(900);
  });

  it('optimizes an oversized SVG without rasterizing it when metadata/comments make it fit', async () => {
    const comments = '<!-- remove me -->'.repeat(400);
    const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 50">${comments}<rect width="100" height="50" fill="#b02a37"/></svg>`;
    const { canvas } = stubBrowser({ svgText: svg, sizeFor: () => 900 });

    const file = svgFileOf(svg);
    const result = await compressImage(file, { maxBytes: 1000 });

    expect(result).not.toBe(file);
    expect(result.type).toBe('image/svg+xml');
    expect(result.name).toBe('logo.svg');
    expect(result.size).toBeLessThanOrEqual(1000);
    expect(result.size).toBeLessThan(file.size);
    expect(canvas.toBlob).not.toHaveBeenCalled();
  });

  it('rasterizes an oversized SVG to WebP using the viewBox dimensions when optimization is not enough', async () => {
    const path = `M0 0 ${'L1 1 '.repeat(2000)}`;
    const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1000 500"><path d="${path}"/></svg>`;
    const { attempts, context } = stubBrowser({
      svgText: svg,
      naturalWidth: 300,
      naturalHeight: 150,
      sizeFor: ({ type }) => (type === 'image/webp' ? 900 : 2000),
    });

    const result = await compressImage(svgFileOf(svg), { maxBytes: 1000 });

    expect(result.type).toBe('image/webp');
    expect(result.name).toBe('logo.webp');
    expect(result.size).toBe(900);
    expect(attempts[0]).toMatchObject({ type: 'image/webp', width: 1000, height: 500 });
    expect(context.clearRect).toHaveBeenCalled();
  });

  it('rasterizes a tiny fractional viewBox without producing a zero-sized canvas', async () => {
    const path = `M0 0 ${'L1 1 '.repeat(2000)}`;
    const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 0.4 0.2"><path d="${path}"/></svg>`;
    const { attempts } = stubBrowser({
      svgText: svg,
      sizeFor: ({ type }) => (type === 'image/webp' ? 900 : 2000),
    });

    const result = await compressImage(svgFileOf(svg), { maxBytes: 1000 });

    expect(result.type).toBe('image/webp');
    expect(attempts[0].width).toBeGreaterThanOrEqual(1);
    expect(attempts[0].height).toBeGreaterThanOrEqual(1);
  });

  it('does not rasterize an SVG that contains active content or external references', async () => {
    const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 50"><script>alert(1)</script><path d="${'M0 0 '.repeat(2000)}"/></svg>`;
    const { canvas } = stubBrowser({ svgText: svg, sizeFor: () => 900 });

    const file = svgFileOf(svg);
    const result = await compressImage(file, { maxBytes: 1000 });

    expect(result).toBe(file);
    expect(canvas.toBlob).not.toHaveBeenCalled();
  });

  it('does not rasterize SVG with external references, event handlers or external CSS urls', async () => {
    const padding = `<path d="${'M0 0 '.repeat(2000)}"/>`;
    const cases = [
      `<use xmlns:xlink="http://www.w3.org/1999/xlink" xlink:href="https://evil.example/x.svg#a"/>`,
      `<rect onload="alert(1)"/>`,
      `<rect fill="url(https://evil.example/x.svg#f)"/>`,
    ];

    for (const malicious of cases) {
      const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 50">${malicious}${padding}</svg>`;
      const { canvas } = stubBrowser({ svgText: svg, sizeFor: () => 900 });

      const file = svgFileOf(svg);
      const result = await compressImage(file, { maxBytes: 1000 });

      expect(result).toBe(file);
      expect(canvas.toBlob).not.toHaveBeenCalled();
    }
  });

  it('does not accept a PNG blob silently returned for an AVIF encode request', async () => {
    stubBrowser({
      sizeFor: ({ type }) => (type === 'image/avif' ? 900 : type === 'image/webp' ? 800 : 2000),
      producedType: (type) => (type === 'image/avif' ? 'image/png' : type),
    });

    const result = await compressImage(fileOf('photo.avif', 'image/avif', 2000), { maxBytes: 1000 });

    expect(result.type).toBe('image/webp');
    expect(result.name).toBe('photo.webp');
    expect(result.size).toBe(800);
  });

  it('does not rasterize an SVG that declares a DTD', async () => {
    const svg = `<!DOCTYPE svg [<!ENTITY xxe SYSTEM "file:///etc/passwd">]><svg xmlns="http://www.w3.org/2000/svg"><text>&xxe;</text><path d="${'M0 0 '.repeat(2000)}"/></svg>`;
    const { canvas } = stubBrowser({ svgText: svg, sizeFor: () => 900 });

    const file = svgFileOf(svg);
    const result = await compressImage(file, { maxBytes: 1000 });

    expect(result).toBe(file);
    expect(canvas.toBlob).not.toHaveBeenCalled();
  });

  it('returns a malformed SVG unchanged instead of rasterizing it', async () => {
    const svg = '<svg><unclosed>';
    const { canvas } = stubBrowser({ svgText: svg, sizeFor: () => 900 });

    const file = svgFileOf(svg);

    await expect(compressImage(file, { maxBytes: 1000 })).resolves.toBe(file);
    expect(canvas.toBlob).not.toHaveBeenCalled();
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
