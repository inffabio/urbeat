import { CommonModule } from '@angular/common';
import { Component, computed, input, OnDestroy, output, signal } from '@angular/core';
import { IonIcon } from '@ionic/angular/standalone';
import { addIcons } from 'ionicons';
import { cloudUploadOutline, trashOutline } from 'ionicons/icons';

type MediaKind = 'logo' | 'banner';

const ACCEPTED_MIME_TYPES = [
  'image/avif',
  'image/png',
  'image/svg+xml',
  'image/webp',
  'image/jpeg',
] as const;

const ACCEPTED_MIME_ATTRIBUTE = ACCEPTED_MIME_TYPES.join(',');

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
  private readonly localPreviewUrl = signal<string | null>(null);

  readonly acceptedMimeTypes = ACCEPTED_MIME_ATTRIBUTE;
  readonly maxSizeLabel = computed(() => this.kind() === 'logo' ? '2 MB' : '5 MB');
  readonly emptyTitle = computed(() => this.kind() === 'logo' ? 'Envie a logo da loja' : 'Envie o banner da loja');
  readonly effectivePreviewUrl = computed(() => this.localPreviewUrl() ?? this.previewUrl());

  onFileSelected(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;

    const maxBytes = this.kind() === 'logo' ? 2 * 1024 * 1024 : 5 * 1024 * 1024;
    if (!ACCEPTED_MIME_TYPES.includes(file.type as (typeof ACCEPTED_MIME_TYPES)[number])) {
      this.errorMessage.set('Formato não aceito. Use AVIF, PNG, SVG, WEBP, JPG ou JPEG.');
      return;
    }

    if (file.size > maxBytes) {
      this.errorMessage.set(`O arquivo deve ter no máximo ${this.maxSizeLabel()}.`);
      return;
    }

    this.revokeLocalPreview();
    this.localPreviewUrl.set(URL.createObjectURL(file));
    this.errorMessage.set(null);
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
