import { DEFAULT_COMPRESSION_TARGET_BYTES } from './image-compression.utils';

export const ACCEPTED_IMAGE_EXTENSIONS = ['avif', 'png', 'svg', 'webp', 'jpg', 'jpeg'] as const;

export const ACCEPTED_IMAGE_MIME_TYPES = [
  'image/avif',
  'image/png',
  'image/svg+xml',
  'image/webp',
  'image/jpeg',
  'image/jpg',
] as const;

const ACCEPTED_EXTENSION_SET = new Set<string>(ACCEPTED_IMAGE_EXTENSIONS);

// Browsers and transports can report an unknown/absent MIME as application/octet-stream.
// The backend treats it as unknown too and relies on content sniffing, so mirror that here.
const UNKNOWN_MIME_TYPES = new Set(['application/octet-stream']);

const MIME_TYPES_BY_EXTENSION: Record<string, readonly string[]> = {
  avif: ['image/avif'],
  png: ['image/png'],
  svg: ['image/svg+xml'],
  webp: ['image/webp'],
  jpg: ['image/jpeg', 'image/jpg'],
  jpeg: ['image/jpeg', 'image/jpg'],
};

export const IMAGE_ACCEPT_ATTRIBUTE = [
  ...ACCEPTED_IMAGE_MIME_TYPES,
  ...ACCEPTED_IMAGE_EXTENSIONS.map((extension) => `.${extension}`),
].join(',');

/** Client-side target for automatic compression, leaving margin below the backend limit. */
export const IMAGE_TARGET_MAX_BYTES = DEFAULT_COMPRESSION_TARGET_BYTES;

/** Authoritative per-upload limit, mirrored from `StoreMediaUploadValidator.MaxBytes` (1 MiB). */
export const IMAGE_MAX_BYTES = 1024 * 1024;

/**
 * @deprecated The legacy product endpoint still documents a 6 MB limit. New upload
 * flows must compress against `IMAGE_TARGET_MAX_BYTES` and reject above `IMAGE_MAX_BYTES`.
 */
export const PRODUCT_IMAGE_MAX_BYTES = 6 * 1024 * 1024;

export const IMAGE_FORMAT_ERROR = 'Formato não aceito. Use AVIF, PNG, SVG, WEBP, JPG ou JPEG.';

export const IMAGE_OVER_LIMIT_ERROR = 'A imagem deve ter no máximo 1 MB. Escolha um arquivo menor.';

/** Thrown when a selected image is still above the authoritative limit after compression. */
export class ImageOverLimitError extends Error {
  constructor() {
    super(IMAGE_OVER_LIMIT_ERROR);
    this.name = 'ImageOverLimitError';
  }
}

export function getFileExtension(fileName: string): string {
  const dotIndex = fileName.lastIndexOf('.');
  return dotIndex > -1 && dotIndex < fileName.length - 1
    ? fileName.slice(dotIndex + 1).toLowerCase()
    : '';
}

export function isAllowedImageFile(file: File): boolean {
  const extension = getFileExtension(file.name);
  if (!extension || !ACCEPTED_EXTENSION_SET.has(extension)) return false;

  const allowedMimeTypes = MIME_TYPES_BY_EXTENSION[extension];
  if (!allowedMimeTypes) return false;

  // Empty/missing and unknown (application/octet-stream) MIME types are accepted
  // because the backend sniffs the actual content. When the browser does provide a
  // specific type it must match the extension, mirroring the backend's MIME policy.
  const mime = (file.type || '').trim().toLowerCase();
  if (mime && !UNKNOWN_MIME_TYPES.has(mime) && !allowedMimeTypes.includes(mime)) return false;

  return true;
}

export function imageSizeError(maxSizeLabel: string): string {
  return `O arquivo deve ter no máximo ${maxSizeLabel}.`;
}
