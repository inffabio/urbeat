import { ComponentFixture, TestBed } from '@angular/core/testing';

import { MediaUploadComponent } from './media-upload.component';

describe('MediaUploadComponent', () => {
  let fixture: ComponentFixture<MediaUploadComponent>;
  let component: MediaUploadComponent;
  let selected: jest.Mock;
  let removed: jest.Mock;

  const fileEvent = (file: File): Event => ({
    target: { files: [file] },
  } as unknown as Event);

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
});
