import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ACCEPTED_IMAGE_EXTENSIONS, IMAGE_FORMAT_ERROR } from '../../utils/image-upload.utils';
import { MediaUploadComponent } from './media-upload.component';

describe('MediaUploadComponent', () => {
  let fixture: ComponentFixture<MediaUploadComponent>;
  let component: MediaUploadComponent;
  let selected: jest.Mock;
  let removed: jest.Mock;

  const fileEvent = (file: File): Event => ({
    target: { files: [file] },
  } as unknown as Event);

  const fileEventWithInput = (file: File): { event: Event; input: { files: File[]; value: string } } => {
    const input = { files: [file], value: `C:\\fakepath\\${file.name}` };
    return { event: { target: input } as unknown as Event, input };
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [MediaUploadComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(MediaUploadComponent);
    component = fixture.componentInstance;
    Object.defineProperty(URL, 'createObjectURL', {
      configurable: true,
      value: jest.fn(() => 'blob:preview'),
    });
    Object.defineProperty(URL, 'revokeObjectURL', {
      configurable: true,
      value: jest.fn(),
    });
    selected = jest.fn();
    removed = jest.fn();
    component.fileSelected.subscribe(selected);
    component.fileRemoved.subscribe(removed);
    fixture.componentRef.setInput('kind', 'logo');
    fixture.detectChanges();
  });

  it('accepts AVIF, PNG, SVG, WEBP and JPEG within the logo limit', () => {
    for (const [type, extension] of [
      ['image/avif', 'avif'],
      ['image/png', 'png'],
      ['image/svg+xml', 'svg'],
      ['image/webp', 'webp'],
      ['image/jpeg', 'jpg'],
    ]) {
      const file = new File(['x'], `asset.${extension}`, { type });

      component.onFileSelected(fileEvent(file));

      expect(selected).toHaveBeenCalledWith(file);
      selected.mockClear();
    }
  });

  it('declares extensions and matching MIME types in the accept attribute', () => {
    const accept = component.acceptedMimeTypes;

    for (const mime of ['image/avif', 'image/png', 'image/svg+xml', 'image/webp', 'image/jpeg']) {
      expect(accept).toContain(mime);
    }
    for (const ext of ['.avif', '.png', '.svg', '.webp', '.jpg', '.jpeg']) {
      expect(accept).toContain(ext);
    }
  });

  it('accepts allowed extensions with empty or alias MIME types', () => {
    const files = [
      new File(['x'], 'logo.PNG', { type: '' }),
      new File(['x'], 'photo.JPG', { type: 'image/jpg' }),
      new File(['x'], 'banner.JPEG', { type: 'image/jpeg' }),
      new File(['x'], 'asset.WebP', { type: '' }),
    ];

    for (const file of files) {
      component.onFileSelected(fileEvent(file));
      expect(selected).toHaveBeenCalledWith(file);
      selected.mockClear();
    }
  });

  it('rejects formats outside the allowlist by extension or MIME', () => {
    const files = [
      new File(['x'], 'asset.gif', { type: 'image/gif' }),
      new File(['x'], 'spoofed.png', { type: 'image/gif' }),
      new File(['x'], 'payload.exe', { type: '' }),
      new File(['x'], 'noextension', { type: 'image/png' }),
    ];

    for (const file of files) {
      component.onFileSelected(fileEvent(file));
      expect(selected).not.toHaveBeenCalled();
    }
    expect(component.errorMessage()).toContain('Formato');
  });

  it('shows the effective accepted-formats copy inside the upload container', () => {
    fixture.componentRef.setInput('kind', 'logo');
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Formatos aceitos: AVIF, PNG, SVG, WEBP, JPG e JPEG.');
  });

  it('does not advertise a format it rejects nor reject one it advertises', () => {
    const copy = (fixture.nativeElement.textContent ?? '').toLowerCase();
    const error = IMAGE_FORMAT_ERROR.toLowerCase();

    for (const extension of ACCEPTED_IMAGE_EXTENSIONS) {
      expect(copy).toContain(extension);
      expect(error).toContain(extension);
    }
  });

  it('shows the recommended dimension only for banner uploads', () => {
    fixture.componentRef.setInput('kind', 'logo');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('Dimensão recomendada');

    fixture.componentRef.setInput('kind', 'banner');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Dimensão recomendada: 1200 x 400 px.');
  });

  it('rejects unsupported formats and files above the logo limit', () => {
    const unsupported = new File(['x'], 'asset.gif', { type: 'image/gif' });
    component.onFileSelected(fileEvent(unsupported));
    expect(selected).not.toHaveBeenCalled();
    expect(component.errorMessage()).toContain('Formato');

    const oversized = new File([new Uint8Array(2 * 1024 * 1024 + 1)], 'logo.png', { type: 'image/png' });
    component.onFileSelected(fileEvent(oversized));
    expect(selected).not.toHaveBeenCalled();
    expect(component.errorMessage()).toContain('2 MB');
  });

  it('uses the banner limit and emits removal requests', () => {
    fixture.componentRef.setInput('kind', 'banner');
    fixture.detectChanges();

    const file = new File([new Uint8Array(5 * 1024 * 1024)], 'banner.webp', { type: 'image/webp' });
    component.onFileSelected(fileEvent(file));

    expect(selected).toHaveBeenCalledWith(file);
    component.removeFile();
    expect(removed).toHaveBeenCalled();
  });

  it('marks logo uploads separately from banner uploads', () => {
    const upload = fixture.nativeElement.querySelector('.media-upload');
    const uploadBox = fixture.nativeElement.querySelector('.upload-box');

    expect(upload.classList).not.toContain('is-banner');
    expect(uploadBox.classList).toContain('is-logo');

    fixture.componentRef.setInput('kind', 'banner');
    fixture.detectChanges();

    expect(upload.classList).toContain('is-banner');
    expect(uploadBox.classList).not.toContain('is-logo');
  });

  it('resets the file input after a rejected selection so the same file can be retried', () => {
    const { event, input } = fileEventWithInput(new File(['x'], 'asset.gif', { type: 'image/gif' }));

    component.onFileSelected(event);

    expect(component.errorMessage()).toContain('Formato');
    expect(input.value).toBe('');

    const { event: retryEvent, input: retryInput } = fileEventWithInput(new File(['x'], 'asset.gif', { type: 'image/gif' }));
    component.onFileSelected(retryEvent);
    expect(retryInput.value).toBe('');
    expect(selected).not.toHaveBeenCalled();
  });

  it('resets the file input after an oversized selection', () => {
    const oversized = new File([new Uint8Array(2 * 1024 * 1024 + 1)], 'logo.png', { type: 'image/png' });
    const { event, input } = fileEventWithInput(oversized);

    component.onFileSelected(event);

    expect(component.errorMessage()).toContain('2 MB');
    expect(input.value).toBe('');
  });

  it('associates the error with the file input via aria-describedby', () => {
    const { event } = fileEventWithInput(new File(['x'], 'asset.gif', { type: 'image/gif' }));

    component.onFileSelected(event);
    fixture.detectChanges();

    const input = fixture.nativeElement.querySelector('input[type="file"]') as HTMLInputElement;
    const error = fixture.nativeElement.querySelector('.upload-error') as HTMLElement;

    expect(error).toBeTruthy();
    expect(error.id).toBeTruthy();
    expect(input.getAttribute('aria-describedby')).toBe(error.id);
    expect(input.getAttribute('aria-invalid')).toBe('true');
  });
});
