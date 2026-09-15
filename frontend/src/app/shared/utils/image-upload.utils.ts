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

export const PRODUCT_IMAGE_MAX_BYTES = 6 * 1024 * 1024;

export const IMAGE_FORMAT_ERROR = 'Formato não aceito. Use AVIF, PNG, SVG, WEBP, JPG ou JPEG.';

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
