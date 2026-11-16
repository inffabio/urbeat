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

type ImageKind = 'jpeg' | 'png' | 'webp' | 'avif' | 'svg';

interface ImageSource {
  kind: ImageKind;
  mime: string;
}

interface EncodeTarget {
  mime: string;
  /** PNG ignores the quality argument; the other supported encoders are lossy. */
  lossy: boolean;
}

interface ImageSize {
  width: number;
  height: number;
}

interface SvgPreparation {
  file: File;
  fits: boolean;
  rasterizable: boolean;
  dimensions?: ImageSize;
}

interface CompressionSink {
  finish(file: File): void;
  fallback(): void;
  isSettled(): boolean;
}

const MIME_BY_KIND: Record<ImageKind, string> = {
  jpeg: 'image/jpeg',
  png: 'image/png',
  webp: 'image/webp',
  avif: 'image/avif',
  svg: 'image/svg+xml',
};

const KIND_BY_MIME: Record<string, ImageKind> = {
  'image/jpeg': 'jpeg',
  'image/png': 'png',
  'image/webp': 'webp',
  'image/avif': 'avif',
  'image/svg+xml': 'svg',
};

const KIND_BY_EXTENSION: Record<string, ImageKind> = {
  jpg: 'jpeg',
  jpeg: 'jpeg',
  png: 'png',
  webp: 'webp',
  avif: 'avif',
  svg: 'svg',
};

const EXTENSIONS_BY_MIME: Record<string, readonly string[]> = {
  'image/jpeg': ['jpg', 'jpeg'],
  'image/png': ['png'],
  'image/webp': ['webp'],
  'image/avif': ['avif'],
  'image/svg+xml': ['svg'],
};

const UNKNOWN_MIME_TYPES = new Set(['application/octet-stream']);

const JPEG_TARGET: EncodeTarget = { mime: 'image/jpeg', lossy: true };
const PNG_TARGET: EncodeTarget = { mime: 'image/png', lossy: false };
const WEBP_TARGET: EncodeTarget = { mime: 'image/webp', lossy: true };
const AVIF_TARGET: EncodeTarget = { mime: 'image/avif', lossy: true };

// The first target preserves the source format. A second target is only used when
// the browser cannot encode the source format (or when a lossless PNG is too heavy),
// so the produced bytes always stay within an accepted backend format.
const TARGETS_BY_KIND: Record<Exclude<ImageKind, 'svg'>, EncodeTarget[]> = {
  jpeg: [JPEG_TARGET],
  png: [PNG_TARGET, WEBP_TARGET],
  webp: [WEBP_TARGET, PNG_TARGET],
  avif: [AVIF_TARGET, WEBP_TARGET, PNG_TARGET],
};

const SVG_RASTER_TARGETS: EncodeTarget[] = [WEBP_TARGET, PNG_TARGET];

// Quality is reduced progressively before dimensions are touched, because keeping
// the original resolution is the primary goal of the adaptive search.
const QUALITY_STEPS = [0.9, 0.8, 0.7, 0.6, 0.5];
const DEFAULT_MIN_QUALITY = 0.4;
const DIMENSION_SCALE = 0.8;
const MIN_DIMENSION = 64;
const MAX_DIMENSION_ROUNDS = 12;

// Rasterizing a vector at its intrinsic 24x24 viewBox would destroy detail, so
// small SVGs are rendered at a usable resolution before the adaptive search.
const SVG_RASTER_MIN_DIMENSION = 512;
const SVG_RASTER_MAX_DIMENSION = 2048;

const UNSAFE_SVG_ELEMENTS = new Set([
  'script',
  'foreignobject',
  'iframe',
  'embed',
  'object',
  'applet',
  'frame',
  'frameset',
  'link',
  'meta',
  'base',
  'handler',
  'listener',
]);

const EXTERNAL_REFERENCE_ATTRIBUTES = new Set(['href', 'src', 'poster', 'data', 'action', 'formaction']);

const CSS_URL_ATTRIBUTES = new Set([
  'style',
  'fill',
  'stroke',
  'filter',
  'clip-path',
  'mask',
  'marker-start',
  'marker-mid',
  'marker-end',
  'cursor',
  'background',
]);

/** Leaves a safety margin below the backend's authoritative 1 MiB limit. */
export const DEFAULT_COMPRESSION_TARGET_BYTES = 950 * 1024;

export const DEFAULT_COMPRESSION_TIMEOUT_MS = 10000;

/**
 * Returns true when the format has a compression strategy. AVIF and SVG are now
 * included: oversize AVIF is decoded and re-encoded to an accepted raster format
 * when the browser lacks an AVIF encoder, and oversize SVG is optimized and, as a
 * last resort, safely rasterized. Actual browser support is probed at runtime and
 * an unsupported/failed path returns the original file so the caller can reject it.
 */
export function canCompressImage(file: File): boolean {
  return resolveImageSource(file) !== null;
}

/**
 * Compresses a browser-selected image so the produced file fits `options.maxBytes`.
 *
 * - JPEG stays JPEG, PNG tries lossless PNG before WebP, and WebP tries WebP before
 *   PNG. Quality is reduced progressively first; dimensions are only reduced when
 *   every quality step for every supported target still exceeds the target.
 * - AVIF is decoded (supported by modern browsers) and re-encoded as AVIF when the
 *   browser can encode it, otherwise as WebP and finally PNG. The produced file is
 *   renamed so its extension always matches the real MIME type accepted by the API.
 * - SVG is prepared by removing non-rendering comments/metadata; if it still does
 *   not fit, a structurally safe SVG is rasterized to WebP/PNG using its width,
 *   height or viewBox. SVGs with active content or external references are never
 *   rasterized, preserving the backend's security policy.
 *
 * The original file is returned unchanged when compression is not needed or cannot
 * be completed safely (unsupported format, missing browser APIs, decode failure,
 * missing 2D context, unsupported encoders, MIME mismatch or timeout).
 */
export function compressImage(file: File, options: CompressImageOptions = {}): Promise<File> {
  const maxBytes = normalizeMaxBytes(options.maxBytes);
  const source = resolveImageSource(file);

  if (!source || file.size <= maxBytes) {
    return Promise.resolve(file);
  }

  if (!hasCompressionApis(source.kind)) {
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

    timer = setTimeout(fallback, Math.max(0, options.timeoutMs ?? DEFAULT_COMPRESSION_TIMEOUT_MS));

    const sink: CompressionSink = {
      finish,
      fallback,
      isSettled: () => settled,
    };

    void (async () => {
      try {
        if (source.kind === 'svg') {
          const prepared = await prepareSvg(file, maxBytes);
          if (settled) return;

          if (!prepared) {
            fallback();
            return;
          }

          if (prepared.fits) {
            finish(prepared.file);
            return;
          }

          if (!prepared.rasterizable) {
            // Rendering active or externally referenced SVG is never safe, and the
            // backend would reject it anyway. Let the caller surface the size error.
            fallback();
            return;
          }

          await rasterize(
            prepared.file,
            SVG_RASTER_TARGETS,
            prepared.dimensions,
            SVG_RASTER_MAX_DIMENSION,
            maxBytes,
            options,
            sink,
          );
          return;
        }

        await rasterize(file, TARGETS_BY_KIND[source.kind], undefined, undefined, maxBytes, options, sink);
      } catch {
        fallback();
      }
    })();
  });
}

async function rasterize(
  file: File,
  targets: EncodeTarget[],
  preferredSize: ImageSize | undefined,
  maxDimension: number | undefined,
  maxBytes: number,
  options: CompressImageOptions,
  sink: CompressionSink,
): Promise<void> {
  if (typeof Image === 'undefined' || typeof document === 'undefined') {
    sink.fallback();
    return;
  }

  const dataUrl = await readAsDataUrl(file);
  if (sink.isSettled()) return;
  if (!dataUrl) {
    sink.fallback();
    return;
  }

  const image = await loadImage(dataUrl);
  if (sink.isSettled()) return;
  if (!image) {
    sink.fallback();
    return;
  }

  const naturalWidth = preferredSize?.width ?? image.naturalWidth ?? image.width;
  const naturalHeight = preferredSize?.height ?? image.naturalHeight ?? image.height;
  if (!isPositiveDimension(naturalWidth) || !isPositiveDimension(naturalHeight)) {
    sink.fallback();
    return;
  }

  let width = Math.max(1, Math.round(naturalWidth));
  let height = Math.max(1, Math.round(naturalHeight));

  if (typeof maxDimension === 'number' && maxDimension > 0) {
    const largest = Math.max(width, height);
    if (largest < SVG_RASTER_MIN_DIMENSION) {
      const scale = SVG_RASTER_MIN_DIMENSION / largest;
      width = Math.max(1, Math.round(width * scale));
      height = Math.max(1, Math.round(height * scale));
    }
  }

  const maxWidth = options.maxWidth;
  if (typeof maxWidth === 'number' && Number.isFinite(maxWidth) && maxWidth >= 1 && width > maxWidth) {
    height = Math.max(1, Math.round((height * maxWidth) / width));
    width = Math.max(1, Math.round(maxWidth));
  }

  if (typeof maxDimension === 'number' && maxDimension > 0 && Math.max(width, height) > maxDimension) {
    const scale = maxDimension / Math.max(width, height);
    width = Math.max(1, Math.round(width * scale));
    height = Math.max(1, Math.round(height * scale));
  }

  let canvas: HTMLCanvasElement;
  try {
    canvas = document.createElement('canvas');
  } catch {
    sink.fallback();
    return;
  }

  let context: CanvasRenderingContext2D | null;
  try {
    context = canvas.getContext('2d');
  } catch {
    sink.fallback();
    return;
  }

  if (!context) {
    // Drawing without a context would silently produce a blank canvas.
    sink.fallback();
    return;
  }

  const qualitySteps = buildQualitySteps(options.quality, options.minQuality);
  const unsupported = new Set<string>();

  for (let round = 0; round <= MAX_DIMENSION_ROUNDS; round++) {
    if (sink.isSettled()) return;

    if (targets.every((target) => unsupported.has(target.mime))) {
      sink.fallback();
      return;
    }

    try {
      canvas.width = width;
      canvas.height = height;

      // Setting the canvas size resets the rendering state.
      context.imageSmoothingEnabled = true;
      context.imageSmoothingQuality = 'high';

      if (targets.some((target) => target.mime === 'image/jpeg')) {
        context.fillStyle = '#ffffff';
        context.fillRect(0, 0, width, height);
      } else {
        // PNG/WebP/AVIF keep the transparent pixels instead of flattening them.
        context.clearRect(0, 0, width, height);
      }

      context.drawImage(image, 0, 0, width, height);
    } catch {
      sink.fallback();
      return;
    }

    for (const target of targets) {
      if (unsupported.has(target.mime)) continue;

      const qualities: Array<number | undefined> = target.lossy ? qualitySteps : [undefined];

      for (const quality of qualities) {
        const blob = await encode(canvas, target.mime, quality);
        if (sink.isSettled()) return;

        if (!blob) {
          // The browser does not support this encoder (Safari returns PNG for
          // image/webp and no browser currently exposes an AVIF canvas encoder).
          unsupported.add(target.mime);
          break;
        }

        if (blob.size <= maxBytes) {
          sink.finish(buildOutputFile(blob, target.mime, file.name));
          return;
        }
      }
    }

    if (targets.every((target) => unsupported.has(target.mime))) {
      sink.fallback();
      return;
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
  sink.fallback();
}

async function prepareSvg(file: File, maxBytes: number): Promise<SvgPreparation | null> {
  const text = await readFileText(file);
  if (text === null) return null;

  const document = parseSvgDocument(text);
  if (!document) return null;

  const dimensions = readSvgDimensions(document);
  const rasterizable = isSvgSafeToRasterize(document);
  const optimized = optimizeSvgDocument(document);

  let candidate = file;
  if (optimized !== null) {
    const optimizedFile = new File([optimized], file.name, {
      type: MIME_BY_KIND.svg,
      lastModified: Date.now(),
    });

    if (optimizedFile.size < file.size) {
      candidate = optimizedFile;
    }
  }

  return {
    file: candidate,
    fits: candidate.size <= maxBytes,
    rasterizable,
    dimensions,
  };
}

function readAsDataUrl(file: File): Promise<string | null> {
  return new Promise((resolve) => {
    let reader: FileReader;
    try {
      reader = new FileReader();
    } catch {
      resolve(null);
      return;
    }

    const done = (value: string | null): void => resolve(value);

    reader.onerror = () => done(null);
    reader.onabort = () => done(null);
    reader.onload = (event) => {
      const result = reader.result || event.target?.result;
      done(typeof result === 'string' && result.length > 0 ? result : null);
    };

    try {
      reader.readAsDataURL(file);
    } catch {
      done(null);
    }
  });
}

function loadImage(src: string): Promise<HTMLImageElement | null> {
  return new Promise((resolve) => {
    let image: HTMLImageElement;
    try {
      image = new Image();
    } catch {
      resolve(null);
      return;
    }

    image.onerror = () => resolve(null);
    image.onload = () => resolve(image);

    try {
      image.src = src;
    } catch {
      resolve(null);
    }
  });
}

function encode(canvas: HTMLCanvasElement, mime: string, quality: number | undefined): Promise<Blob | null> {
  return new Promise((resolve) => {
    if (typeof canvas.toBlob !== 'function') {
      resolve(null);
      return;
    }

    try {
      canvas.toBlob(
        (blob) => {
          if (!blob || blob.size === 0) {
            // A missing/empty blob means the encoder failed; the original file is
            // the only safe result, never a blank canvas.
            resolve(null);
            return;
          }

          // The browser may silently fall back to PNG for unsupported types. An
          // empty type is treated as the requested type for older implementations.
          const producedType = normalizeMime(blob.type) || mime;
          if (producedType !== mime) {
            resolve(null);
            return;
          }

          resolve(blob);
        },
        mime,
        quality,
      );
    } catch {
      resolve(null);
    }
  });
}

function buildOutputFile(blob: Blob, mime: string, originalName: string): File {
  const normalizedMime = normalizeMime(mime);
  const extension = EXTENSIONS_BY_MIME[normalizedMime]?.[0] ?? 'bin';
  const name = hasMatchingExtension(originalName, normalizedMime)
    ? originalName
    : replaceExtension(originalName, extension);

  return new File([blob], name, { type: normalizedMime, lastModified: Date.now() });
}

function hasMatchingExtension(fileName: string, mime: string): boolean {
  return (EXTENSIONS_BY_MIME[mime] ?? []).includes(getExtension(fileName));
}

function replaceExtension(fileName: string, extension: string): string {
  const dotIndex = fileName.lastIndexOf('.');
  const baseName = dotIndex > 0 ? fileName.slice(0, dotIndex) : fileName;
  return `${baseName || 'image'}.${extension}`;
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

function resolveImageSource(file: File): ImageSource | null {
  const mime = normalizeMime(file.type);

  if (mime && !UNKNOWN_MIME_TYPES.has(mime)) {
    const kind = KIND_BY_MIME[mime];
    return kind ? { kind, mime: MIME_BY_KIND[kind] } : null;
  }

  // Browsers may omit the type (or report application/octet-stream); infer it from
  // the extension. Decoding and the content check remain authoritative.
  const kind = KIND_BY_EXTENSION[getExtension(file.name)];
  return kind ? { kind, mime: MIME_BY_KIND[kind] } : null;
}

function normalizeMime(value: string | null | undefined): string {
  const normalized = (value ?? '').split(';', 1)[0].trim().toLowerCase();
  return normalized === 'image/jpg' ? 'image/jpeg' : normalized;
}

function getExtension(fileName: string): string {
  const dotIndex = fileName.lastIndexOf('.');
  return dotIndex > -1 && dotIndex < fileName.length - 1
    ? fileName.slice(dotIndex + 1).toLowerCase()
    : '';
}

function isPositiveDimension(value: number): boolean {
  return Number.isFinite(value) && value > 0;
}

function hasCompressionApis(kind: ImageKind): boolean {
  if (typeof FileReader === 'undefined') return false;

  if (kind === 'svg') {
    return typeof DOMParser !== 'undefined' && typeof XMLSerializer !== 'undefined';
  }

  return typeof Image !== 'undefined' && typeof document !== 'undefined';
}

async function readFileText(file: File): Promise<string | null> {
  return new Promise((resolve) => {
    let reader: FileReader;
    try {
      reader = new FileReader();
    } catch {
      resolve(null);
      return;
    }

    let settled = false;
    const done = (value: string | null): void => {
      if (settled) return;
      settled = true;
      resolve(value);
    };

    reader.onerror = () => done(null);
    reader.onabort = () => done(null);
    reader.onload = (event) => {
      const result = reader.result || event.target?.result;

      if (result && typeof result === 'object' && 'byteLength' in result) {
        done(decodeText(new Uint8Array(result as ArrayBuffer)));
        return;
      }

      done(typeof result === 'string' ? result : null);
    };

    try {
      const blobWithArrayBuffer = file as File & { arrayBuffer?: () => Promise<ArrayBuffer> };
      if (typeof blobWithArrayBuffer.arrayBuffer === 'function') {
        void blobWithArrayBuffer
          .arrayBuffer()
          .then((buffer) => done(decodeText(new Uint8Array(buffer))))
          .catch(() => done(null));
        return;
      }

      reader.readAsArrayBuffer(file);
    } catch {
      done(null);
    }
  });
}

function decodeText(bytes: Uint8Array): string {
  if (typeof TextDecoder === 'undefined') {
    let text = '';
    for (const byte of bytes) {
      text += String.fromCharCode(byte);
    }
    return text;
  }

  try {
    if (bytes.length >= 2 && bytes[0] === 0xff && bytes[1] === 0xfe) {
      return new TextDecoder('utf-16le').decode(bytes.subarray(2));
    }

    if (bytes.length >= 2 && bytes[0] === 0xfe && bytes[1] === 0xff) {
      return new TextDecoder('utf-16be').decode(bytes.subarray(2));
    }

    if (bytes.length >= 3 && bytes[0] === 0xef && bytes[1] === 0xbb && bytes[2] === 0xbf) {
      return new TextDecoder('utf-8').decode(bytes.subarray(3));
    }

    return new TextDecoder('utf-8').decode(bytes);
  } catch {
    // Unknown BOM/encoding support: UTF-8 is the only safe fallback.
    try {
      return new TextDecoder('utf-8').decode(bytes);
    } catch {
      return '';
    }
  }
}

function parseSvgDocument(text: string): Document | null {
  if (typeof DOMParser === 'undefined') return null;

  // Mirrors the backend's DtdProcessing.Prohibit: never hand a document with a
  // DTD/internal subset to the browser parser, which avoids entity expansion and
  // external subset resolution entirely.
  if (/<!DOCTYPE/i.test(text)) return null;

  try {
    const document = new DOMParser().parseFromString(text, 'image/svg+xml');
    if (!document || document.getElementsByTagName('parsererror').length > 0) {
      return null;
    }

    const root = document.documentElement;
    if (!root || (root.localName ?? '').toLowerCase() !== 'svg') {
      return null;
    }

    return document;
  } catch {
    return null;
  }
}

function readSvgDimensions(document: Document): ImageSize | undefined {
  const root = document.documentElement;
  if (!root) return undefined;

  const width = parseSvgLength(root.getAttribute('width'));
  const height = parseSvgLength(root.getAttribute('height'));

  let viewBoxWidth: number | null = null;
  let viewBoxHeight: number | null = null;

  const viewBox = root.getAttribute('viewBox');
  if (viewBox) {
    const parts = viewBox.trim().split(/[\s,]+/).map(Number);
    if (parts.length === 4 && parts.every(Number.isFinite) && parts[2] > 0 && parts[3] > 0) {
      viewBoxWidth = parts[2];
      viewBoxHeight = parts[3];
    }
  }

  if (width && height) return { width, height };
  if (width && viewBoxWidth && viewBoxHeight) {
    return { width, height: (width * viewBoxHeight) / viewBoxWidth };
  }
  if (height && viewBoxWidth && viewBoxHeight) {
    return { width: (height * viewBoxWidth) / viewBoxHeight, height };
  }
  if (viewBoxWidth && viewBoxHeight) {
    return { width: viewBoxWidth, height: viewBoxHeight };
  }

  return undefined;
}

function parseSvgLength(value: string | null): number | null {
  if (!value) return null;

  const match = /^\s*([+-]?(?:\d+\.?\d*|\.\d+)(?:e[+-]?\d+)?)\s*(?:px)?\s*$/i.exec(value);
  if (!match) return null;

  const parsed = Number(match[1]);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
}

function isSvgSafeToRasterize(document: Document): boolean {
  // The backend already rejects active/external SVG content. Rasterizing must not
  // bypass that policy by rendering content the server would never accept.
  if (document.doctype) return false;

  const elements = Array.from(document.getElementsByTagName('*'));
  for (const element of elements) {
    const name = (element.localName ?? element.nodeName ?? '').toLowerCase();

    if (UNSAFE_SVG_ELEMENTS.has(name)) {
      return false;
    }

    if (name === 'style' && containsDangerousCss(element.textContent ?? '')) {
      return false;
    }

    const attributes = element.attributes;
    for (let index = 0; index < attributes.length; index++) {
      const attribute = attributes.item(index);
      if (!attribute) continue;

      const attributeName = (attribute.localName ?? attribute.name ?? '').toLowerCase();
      const value = attribute.value;

      // Event handlers (onload, onclick, ...) are active content.
      if (attributeName.startsWith('on')) {
        return false;
      }

      if (hasDangerousScheme(value)) {
        return false;
      }

      if (EXTERNAL_REFERENCE_ATTRIBUTES.has(attributeName) && isExternalReference(value)) {
        return false;
      }

      if (CSS_URL_ATTRIBUTES.has(attributeName) && containsDangerousCss(value)) {
        return false;
      }
    }
  }

  for (let index = 0; index < document.childNodes.length; index++) {
    const node = document.childNodes.item(index);
    if (node && node.nodeType === 7 && (node.nodeName ?? '').toLowerCase().includes('stylesheet')) {
      return false;
    }
  }

  return true;
}

function hasDangerousScheme(value: string): boolean {
  const compact = stripWhitespaceAndControl(value).toLowerCase();
  return compact.includes('javascript:') || compact.includes('vbscript:');
}

function containsDangerousCss(value: string): boolean {
  if (!value) return false;

  const compact = stripWhitespaceAndControl(value).toLowerCase();
  if (
    compact.includes('javascript:') ||
    compact.includes('vbscript:') ||
    compact.includes('expression(') ||
    compact.includes('-moz-binding') ||
    compact.includes('behavior:')
  ) {
    return true;
  }

  if (value.toLowerCase().includes('@import')) {
    return true;
  }

  return containsExternalUrl(value);
}

function containsExternalUrl(value: string): boolean {
  const lower = value.toLowerCase();
  let index = lower.indexOf('url(');

  while (index >= 0) {
    let rest = lower.slice(index + 4).trimStart();
    if (rest.startsWith('"') || rest.startsWith("'")) {
      rest = rest.slice(1).trimStart();
    }

    if (!rest.startsWith('#')) {
      return true;
    }

    index = lower.indexOf('url(', index + 4);
  }

  return false;
}

function stripWhitespaceAndControl(value: string): string {
  return value.replace(/[\s\u0000-\u001f\u007f]/g, '');
}

function isExternalReference(value: string | null | undefined): boolean {
  const trimmed = (value ?? '').trim();
  return trimmed.length > 0 && !trimmed.startsWith('#');
}

function optimizeSvgDocument(document: Document): string | null {
  if (typeof XMLSerializer === 'undefined') return null;

  try {
    const clone = document.cloneNode(true) as Document;
    removeNonRenderingNodes(clone);

    const serialized = new XMLSerializer().serializeToString(clone);
    if (!serialized) return null;

    // Re-parse so a serializer regression never produces an SVG the API would
    // reject for invalid content.
    return parseSvgDocument(serialized) ? serialized : null;
  } catch {
    return null;
  }
}

function removeNonRenderingNodes(node: Node): void {
  const children = Array.from(node.childNodes);

  for (const child of children) {
    // Comments (8) and processing instructions (7) never render. Stylesheet
    // processing instructions are active content and must be dropped as well.
    if (child.nodeType === 7 || child.nodeType === 8) {
      node.removeChild(child);
      continue;
    }

    if (child.nodeType === 1) {
      const element = child as Element;
      if ((element.localName ?? '').toLowerCase() === 'metadata') {
        node.removeChild(child);
        continue;
      }
    }

    removeNonRenderingNodes(child);
  }
}
