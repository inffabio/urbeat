# Shared Store Media Upload

## Goal

Centralize the store logo and banner upload experience so the wizard and seller dashboard accept the same formats, show the same guidance, and validate files consistently.

## Scope

- Add a standalone shared Angular component for logo and banner uploads.
- Use it in the store configuration wizard and the dashboard's store bio/configuration flow.
- Accept AVIF, PNG, SVG, WEBP, JPG, and JPEG for both logo and banner.
- Keep the current size limits: 2 MB for logos and 5 MB for banners.
- Display the accepted formats inside each upload container.
- Preserve the selected file format instead of converting every image to JPEG.
- Update backend upload configuration and validation to match the frontend contract.

## Component Design

Create `MediaUploadComponent` under `frontend/src/app/shared/components/media-upload/`.

Inputs:

- `kind`: `logo` or `banner`.
- `previewUrl`: existing remote URL or local preview URL.
- `disabled`: prevents selection while saving.
- `altText`: accessible preview description.

Outputs:

- `fileSelected`: emits the validated `File`.
- `fileRemoved`: requests removal of the current preview/file.

The component owns the hidden file input, accepted MIME list, extension guidance, size limits, preview creation, validation messages, and accessible interaction states. The parent owns upload requests, persisted URLs, and save lifecycle state.

The component accepts the following MIME types:

- `image/avif`
- `image/png`
- `image/svg+xml`
- `image/webp`
- `image/jpeg`

The visible copy is: `Formatos aceitos: AVIF, PNG, SVG, WEBP, JPG e JPEG.`

## Data Flow

1. The user selects a file in the shared component.
2. The component validates MIME type and size.
3. Valid files are previewed locally and emitted to the parent.
4. The parent stores the pending file and sends it through the existing `StoreService.uploadImage` flow on save.
5. The uploaded URL is persisted through the existing store update request.

No new upload endpoint or persistence model is required.

## Backend

- Update the `Upload.AllowedExtensions` seeded configuration to include `.avif` and `.svg` alongside `.jpg`, `.jpeg`, `.png`, and `.webp`.
- Ensure the upload endpoint validates the file extension and MIME type against the same allowlist.
- Keep Cloudinary as the storage and delivery provider.
- Do not force client-side JPEG conversion, preserving SVG, AVIF, PNG transparency, and the original upload format.

## Error Handling

- Invalid type: show a clear localized error and do not emit a file.
- File above the per-kind limit: show the applicable limit and do not emit a file.
- Preview failure: retain the current preview, clear the pending selection, and show an error.
- Upload failure: preserve the pending selection so the user can retry.

## Verification

- Unit tests for accepted/rejected MIME types, size limits, preview selection, and removal events.
- Update store configuration tests to verify logo and banner files are passed to the upload service unchanged.
- Run focused frontend Jest tests and the production Angular build.
- Run relevant backend tests and build.
