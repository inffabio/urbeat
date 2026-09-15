export interface CompressImageOptions {
  maxWidth?: number;
  quality?: number;
  timeoutMs?: number;
}

const COMPRESSIBLE_MIME_TYPES = new Set(['image/jpeg', 'image/png', 'image/webp']);

export const DEFAULT_COMPRESSION_TIMEOUT_MS = 10000;

export function canCompressImage(file: File): boolean {
  return COMPRESSIBLE_MIME_TYPES.has((file.type || '').toLowerCase());
}

/**
 * Compresses a browser-selected image using a canvas. The original file is
 * always returned when compression cannot be completed safely (unsupported
 * format, decode failure, missing canvas context, encoder failure, or timeout),
 * so the caller can upload the valid original instead of hanging or uploading a
 * blank canvas. AVIF/SVG are never re-encoded and keep their original format.
 */
export function compressImage(file: File, options: CompressImageOptions = {}): Promise<File> {
  const maxWidth = options.maxWidth ?? 1200;
  const quality = options.quality ?? 0.75;
  const timeoutMs = options.timeoutMs ?? DEFAULT_COMPRESSION_TIMEOUT_MS;

  if (!canCompressImage(file)) {
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

    timer = setTimeout(fallback, timeoutMs);

    let reader: FileReader;
    try {
      reader = new FileReader();
    } catch {
      fallback();
      return;
    }

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

        try {
          const canvas = document.createElement('canvas');
          let width = image.naturalWidth || image.width;
          let height = image.naturalHeight || image.height;
          if (!width || !height) {
            fallback();
            return;
          }

          if (width > maxWidth) {
            height = Math.round((height * maxWidth) / width);
            width = maxWidth;
          }

          canvas.width = width;
          canvas.height = height;

          const context = canvas.getContext('2d');
          if (!context) {
            // Drawing without a context would silently produce a blank canvas.
            fallback();
            return;
          }

          context.drawImage(image, 0, 0, width, height);

          if (typeof canvas.toBlob !== 'function') {
            fallback();
            return;
          }

          canvas.toBlob(
            (blob) => {
              if (!blob || blob.size === 0) {
                fallback();
                return;
              }

              finish(new File([blob], file.name, { type: file.type, lastModified: Date.now() }));
            },
            file.type,
            quality,
          );
        } catch {
          fallback();
        }
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
