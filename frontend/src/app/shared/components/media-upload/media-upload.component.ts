import { CommonModule } from '@angular/common';
import { Component, computed, input, OnDestroy, output, signal } from '@angular/core';
import { IonIcon } from '@ionic/angular/standalone';
import { addIcons } from 'ionicons';
import { cloudUploadOutline, trashOutline } from 'ionicons/icons';
import { IMAGE_ACCEPT_ATTRIBUTE, IMAGE_FORMAT_ERROR, IMAGE_MAX_BYTES, IMAGE_OVER_LIMIT_ERROR, isAllowedImageFile } from '../../utils/image-upload.utils';
import { canCompressImage } from '../../utils/image-compression.utils';

type MediaKind = 'logo' | 'banner';

let mediaUploadIdCounter = 0;

addIcons({
  'cloud-upload-outline': cloudUploadOutline,
  'trash-outline': trashOutline,
});

@Component({
  selector: 'app-media-upload',
  standalone: true,
  imports: [CommonModule, IonIcon],
  templateUrl: './media-upload.component.html',
  styleUrl: './media-upload.component.scss',
})
export class MediaUploadComponent implements OnDestroy {
  readonly kind = input<MediaKind>('logo');
  readonly previewUrl = input<string | null>(null);
  readonly disabled = input(false);
  readonly altText = input('Pré-visualização da imagem');

  readonly fileSelected = output<File>();
  readonly fileRemoved = output<void>();

  readonly errorMessage = signal<string | null>(null);
  readonly errorId = `media-upload-error-${++mediaUploadIdCounter}`;
  private readonly localPreviewUrl = signal<string | null>(null);

  readonly acceptedMimeTypes = IMAGE_ACCEPT_ATTRIBUTE;
  readonly maxSizeLabel = computed(() => '1 MB');
  readonly emptyTitle = computed(() => this.kind() === 'logo' ? 'Envie a logo da loja' : 'Envie o banner da loja');
  readonly effectivePreviewUrl = computed(() => this.localPreviewUrl() ?? this.previewUrl());

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;

    const maxBytes = IMAGE_MAX_BYTES;
    if (!isAllowedImageFile(file)) {
      this.errorMessage.set(IMAGE_FORMAT_ERROR);
      // Reset so selecting the same invalid file again still fires a change event.
      input.value = '';
      return;
    }

    // Raster images above the limit are compressed before upload, so rejecting them
    // here would skip the compression step. Formats the browser cannot re-encode
    // (SVG/AVIF) have no way to shrink and are rejected immediately.
    if (!canCompressImage(file) && file.size > maxBytes) {
      this.errorMessage.set(IMAGE_OVER_LIMIT_ERROR);
      input.value = '';
      return;
    }

    this.revokeLocalPreview();
    this.localPreviewUrl.set(URL.createObjectURL(file));
    this.errorMessage.set(null);
    input.value = '';
    this.fileSelected.emit(file);
  }

  removeFile(): void {
    this.revokeLocalPreview();
    this.localPreviewUrl.set(null);
    this.errorMessage.set(null);
    this.fileRemoved.emit();
  }

  ngOnDestroy(): void {
    this.revokeLocalPreview();
  }

  private revokeLocalPreview(): void {
    const url = this.localPreviewUrl();
    if (url) URL.revokeObjectURL(url);
  }
}
