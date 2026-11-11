export interface CompressImageOptions {
  /** Hard target for the produced file. Defaults to `DEFAULT_COMPRESSION_TARGET_BYTES` (950 KiB). */
  maxBytes?: number;
  /** Optional cap applied before the adaptive search. Omit to preserve the original dimensions. */
  maxWidth?: number;
  /** Starting encoder quality. Defaults to 0.9 so the first attempt keeps visual quality high. */
  quality?: number;
  /** Lowest encoder quality attempted before dimensions are reduced. Defaults to 0.4. */
  minQuality?: number;
  timeoutMs?: number;
}

const COMPRESSIBLE_MIME_TYPES = new Set(['image/jpeg', 'image/png', 'image/webp']);

const MIME_BY_EXTENSION: Record<string, string> = {
  jpg: 'image/jpeg',
  jpeg: 'image/jpeg',
  png: 'image/png',
  webp: 'image/webp',
};

// Quality is reduced progressively before dimensions are touched, because keeping
// the original resolution is the primary goal of the adaptive search.
const QUALITY_STEPS = [0.9, 0.8, 0.7, 0.6, 0.5];
const DEFAULT_MIN_QUALITY = 0.4;
const DIMENSION_SCALE = 0.8;
const MIN_DIMENSION = 64;
const MAX_DIMENSION_ROUNDS = 12;

/** Leaves a safety margin below the backend's authoritative 1 MiB limit. */
export const DEFAULT_COMPRESSION_TARGET_BYTES = 950 * 1024;

export const DEFAULT_COMPRESSION_TIMEOUT_MS = 10000;

export function canCompressImage(file: File): boolean {
  return resolveCompressibleMime(file) !== null;
}

/**
 * Compresses a browser-selected raster image (JPEG/PNG/WebP) so the produced file
 * fits `options.maxBytes`, preserving the original dimensions and aspect ratio for
 * as long as possible. Quality is reduced progressively first; only when every
 * quality step still exceeds the target are the dimensions scaled down
 * proportionally.
 *
 * The original file is returned unchanged when compression is not needed or cannot
 * be completed safely (unsupported format such as SVG/AVIF, missing browser APIs,
 * decode failure, missing 2D context, encoder failure, MIME mismatch or timeout).
 * SVG/AVIF are therefore never re-encoded and keep their original bytes.
 */
export function compressImage(file: File, options: CompressImageOptions = {}): Promise<File> {
  const maxBytes = normalizeMaxBytes(options.maxBytes);
  const timeoutMs = options.timeoutMs ?? DEFAULT_COMPRESSION_TIMEOUT_MS;
  const outputMime = resolveCompressibleMime(file);

  if (!outputMime || file.size <= maxBytes) {
    return Promise.resolve(file);
  }

  if (typeof FileReader === 'undefined' || typeof Image === 'undefined' || typeof document === 'undefined') {
    return Promise.resolve(file);
  }

  return new Promise<File>((resolve) => {
    let settled = false;
    let timer: ReturnType<typeof setTimeout> | undefined;

    const finish = (result: File): void => {
      if (settled) return;
      settled = true;
      if (timer !== undefined) clearTimeout(timer);
      resolve(result);
    };

    const fallback = (): void => finish(file);

    timer = setTimeout(fallback, Math.max(0, timeoutMs));

    let reader: FileReader;
    try {
      reader = new FileReader();
    } catch {
      fallback();
      return;
    }

    const buildCompressedFile = (blob: Blob): File =>
      new File([blob], file.name, { type: outputMime, lastModified: Date.now() });

    const encode = (
      canvas: HTMLCanvasElement,
      quality: number,
    ): Promise<Blob | { failed: true }> =>
      new Promise((resolveEncode) => {
        if (typeof canvas.toBlob !== 'function') {
          resolveEncode({ failed: true });
          return;
        }

        try {
          canvas.toBlob((blob) => {
            if (!blob || blob.size === 0) {
              // A missing/empty blob means the encoder failed; the original file is
              // the only safe result, never a blank canvas.
              resolveEncode({ failed: true });
              return;
            }

            const producedType = (blob.type || outputMime).toLowerCase();
            if (producedType !== outputMime) {
              // The browser fell back to another format (usually PNG). Uploading it
              // with the original extension would fail the backend MIME check.
              resolveEncode({ failed: true });
              return;
            }

            resolveEncode(blob);
          }, outputMime, quality);
        } catch {
          resolveEncode({ failed: true });
        }
      });

    const run = async (image: HTMLImageElement, naturalWidth: number, naturalHeight: number): Promise<void> => {
      let canvas: HTMLCanvasElement;
      try {
        canvas = document.createElement('canvas');
      } catch {
        fallback();
        return;
      }

      let context: CanvasRenderingContext2D | null;
      try {
        context = canvas.getContext('2d');
      } catch {
        fallback();
        return;
      }

      if (!context) {
        // Drawing without a context would silently produce a blank canvas.
        fallback();
        return;
      }

      let width = naturalWidth;
      let height = naturalHeight;
      const maxWidth = options.maxWidth;
      if (typeof maxWidth === 'number' && maxWidth > 0 && width > maxWidth) {
        height = Math.max(1, Math.round((height * maxWidth) / width));
        width = maxWidth;
      }

      const qualitySteps = buildQualitySteps(options.quality, options.minQuality);
      const isJpeg = outputMime === 'image/jpeg';

      for (let round = 0; round <= MAX_DIMENSION_ROUNDS; round++) {
        if (settled) return;

        try {
          canvas.width = width;
          canvas.height = height;

          // Setting the canvas size resets the rendering state.
          context.imageSmoothingEnabled = true;
          context.imageSmoothingQuality = 'high';

          if (isJpeg) {
            context.fillStyle = '#ffffff';
            context.fillRect(0, 0, width, height);
          } else {
            // PNG/WebP keep the transparent pixels instead of flattening them.
            context.clearRect(0, 0, width, height);
          }

          context.drawImage(image, 0, 0, width, height);
        } catch {
          fallback();
          return;
        }

        for (const quality of qualitySteps) {
          const blob = await encode(canvas, quality);
          if (settled) return;

          if ('failed' in blob) {
            fallback();
            return;
          }

          if (blob.size <= maxBytes) {
            finish(buildCompressedFile(blob));
            return;
          }
        }

        const nextWidth = Math.max(1, Math.round(width * DIMENSION_SCALE));
        const nextHeight = Math.max(1, Math.round(height * DIMENSION_SCALE));
        if (nextWidth === width && nextHeight === height) break;
        if (nextWidth < MIN_DIMENSION || nextHeight < MIN_DIMENSION) break;

        width = nextWidth;
        height = nextHeight;
      }

      // Nothing fit the target even after reducing dimensions: return the original
      // so the caller can reject it with a clear message instead of sending an
      // oversized file silently.
      fallback();
    };

    reader.onerror = fallback;
    reader.onabort = fallback;
    reader.onload = (event) => {
      if (settled) return;

      const dataUrl = event.target?.result;
      if (typeof dataUrl !== 'string' || !dataUrl) {
        fallback();
        return;
      }

      let image: HTMLImageElement;
      try {
        image = new Image();
      } catch {
        fallback();
        return;
      }

      image.onerror = fallback;
      image.onload = () => {
        if (settled) return;

        const width = image.naturalWidth || image.width;
        const height = image.naturalHeight || image.height;
        if (!width || !height) {
          fallback();
          return;
        }

        void run(image, width, height);
      };

      image.src = dataUrl;
    };

    try {
      reader.readAsDataURL(file);
    } catch {
      fallback();
    }
  });
}

function normalizeMaxBytes(value: number | undefined): number {
  return typeof value === 'number' && Number.isFinite(value) && value > 0
    ? Math.floor(value)
    : DEFAULT_COMPRESSION_TARGET_BYTES;
}

function buildQualitySteps(start: number | undefined, minValue: number | undefined): number[] {
  const floor = clamp(minValue ?? DEFAULT_MIN_QUALITY, 0.05, 0.95);
  const first = clamp(start ?? QUALITY_STEPS[0], floor, 1);
  const steps = [first];

  for (const quality of QUALITY_STEPS) {
    if (quality < first - 0.001 && quality >= floor) {
      steps.push(quality);
    }
  }

  if (steps[steps.length - 1] > floor + 0.001) {
    steps.push(floor);
  }

  return [...new Set(steps)];
}

function clamp(value: number, min: number, max: number): number {
  if (!Number.isFinite(value)) return min;
  return Math.min(max, Math.max(min, value));
}

function resolveCompressibleMime(file: File): string | null {
  const mime = (file.type || '').trim().toLowerCase();
  const normalized = mime === 'image/jpg' ? 'image/jpeg' : mime;

  if (COMPRESSIBLE_MIME_TYPES.has(normalized)) {
    return normalized;
  }

  // A specific but unsupported MIME (SVG, AVIF, GIF, ...) must never be re-encoded.
  if (normalized && normalized !== 'application/octet-stream') {
    return null;
  }

  // Browsers may omit the type (or report application/octet-stream); infer it from
  // the extension. Decoding and the content check remain authoritative.
  return MIME_BY_EXTENSION[getExtension(file.name)] ?? null;
}

function getExtension(fileName: string): string {
  const dotIndex = fileName.lastIndexOf('.');
  return dotIndex > -1 && dotIndex < fileName.length - 1
    ? fileName.slice(dotIndex + 1).toLowerCase()
    : '';
}
