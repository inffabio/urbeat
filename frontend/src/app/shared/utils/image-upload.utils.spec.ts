import {
  ACCEPTED_IMAGE_EXTENSIONS,
  ACCEPTED_IMAGE_MIME_TYPES,
  IMAGE_ACCEPT_ATTRIBUTE,
  PRODUCT_IMAGE_MAX_BYTES,
  getFileExtension,
  isAllowedImageFile,
} from './image-upload.utils';

describe('image-upload.utils', () => {
  it('declares every required extension and MIME type in the accept attribute', () => {
    for (const extension of ['avif', 'png', 'svg', 'webp', 'jpg', 'jpeg']) {
      expect(ACCEPTED_IMAGE_EXTENSIONS).toContain(extension);
      expect(IMAGE_ACCEPT_ATTRIBUTE).toContain(`.${extension}`);
    }

    for (const mime of ['image/avif', 'image/png', 'image/svg+xml', 'image/webp', 'image/jpeg']) {
      expect(ACCEPTED_IMAGE_MIME_TYPES).toContain(mime);
      expect(IMAGE_ACCEPT_ATTRIBUTE).toContain(mime);
    }
  });

  it('accepts required extensions regardless of case and alias MIME types', () => {
    const files = [
      new File(['x'], 'logo.AVIF', { type: 'image/avif' }),
      new File(['x'], 'logo.PNG', { type: '' }),
      new File(['x'], 'logo.SVG', { type: 'image/svg+xml' }),
      new File(['x'], 'logo.WebP', { type: 'image/webp' }),
      new File(['x'], 'logo.JPG', { type: 'image/jpg' }),
      new File(['x'], 'logo.jpeg', { type: 'image/jpeg' }),
    ];

    for (const file of files) {
      expect(isAllowedImageFile(file)).toBe(true);
    }
  });

  it('rejects unsupported formats and spoofed MIME types', () => {
    expect(isAllowedImageFile(new File(['x'], 'logo.gif', { type: 'image/gif' }))).toBe(false);
    expect(isAllowedImageFile(new File(['x'], 'spoofed.png', { type: 'image/gif' }))).toBe(false);
    expect(isAllowedImageFile(new File(['x'], 'noextension', { type: 'image/png' }))).toBe(false);
  });

  it('rejects MIME types that belong to a different allowed extension (backend parity)', () => {
    expect(isAllowedImageFile(new File(['x'], 'logo.png', { type: 'image/webp' }))).toBe(false);
    expect(isAllowedImageFile(new File(['x'], 'photo.jpg', { type: 'image/png' }))).toBe(false);
    expect(isAllowedImageFile(new File(['x'], 'asset.webp', { type: 'image/jpeg' }))).toBe(false);
    expect(isAllowedImageFile(new File(['x'], 'art.avif', { type: 'image/png' }))).toBe(false);
  });

  it('accepts MIME-less files for every allowed extension like the backend does', () => {
    for (const extension of ['avif', 'png', 'svg', 'webp', 'jpg', 'jpeg']) {
      expect(isAllowedImageFile(new File(['x'], `asset.${extension}`, { type: '' }))).toBe(true);
    }
  });

  it('treats application/octet-stream as an unknown MIME like the backend does', () => {
    for (const extension of ['avif', 'png', 'svg', 'webp', 'jpg', 'jpeg']) {
      expect(isAllowedImageFile(new File(['x'], `asset.${extension}`, { type: 'application/octet-stream' }))).toBe(true);
    }
  });

  it('reads the extension case-insensitively', () => {
    expect(getFileExtension('logo.PNG')).toBe('png');
    expect(getFileExtension('noextension')).toBe('');
  });

  it('uses the documented 6 MB product limit', () => {
    expect(PRODUCT_IMAGE_MAX_BYTES).toBe(6 * 1024 * 1024);
  });
});
