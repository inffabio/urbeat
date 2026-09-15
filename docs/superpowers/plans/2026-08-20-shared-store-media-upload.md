# Shared Store Media Upload Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace the duplicated store logo/banner upload UI with one shared component used by the wizard and seller dashboard, accepting AVIF, PNG, SVG, WEBP, JPG/JPEG with consistent limits and backend validation.

**Architecture:** `MediaUploadComponent` owns file input, accepted MIME types, size validation, local preview, visible guidance, and removal events. The parent pages continue to own persistence and call the existing `StoreService.uploadImage` API. The API validates store-media files before handing them to Cloudinary, and the existing seeded extension list is updated.

**Tech Stack:** Angular 20 standalone components, Ionic icons, Jest, ASP.NET Core .NET 9, Cloudinary image upload service.

## Global Constraints

- Accept `image/avif`, `image/png`, `image/svg+xml`, `image/webp`, and `image/jpeg` for both logo and banner.
- Enforce 2 MB maximum for logo files and 5 MB maximum for banner files.
- Display `Formatos aceitos: AVIF, PNG, SVG, WEBP, JPG e JPEG.` inside each upload container.
- Preserve the selected file format; do not convert every image to JPEG in the browser.
- Use shared Angular UI under `frontend/src/app/shared/components/`.
- Keep backend validation authoritative; browser `accept` is not security validation.
- Do not add PrimeNG, Bootstrap components, or a new upload dependency.

---

### Task 1: Add the Shared Media Upload Component

**Files:**
- Create: `frontend/src/app/shared/components/media-upload/media-upload.component.ts`
- Create: `frontend/src/app/shared/components/media-upload/media-upload.component.html`
- Create: `frontend/src/app/shared/components/media-upload/media-upload.component.scss`
- Create: `frontend/src/app/shared/components/media-upload/media-upload.component.spec.ts`

**Interfaces:**
- Consumes `kind: 'logo' | 'banner'`, `previewUrl: string | null`, `disabled: boolean`, and `altText: string`.
- Produces `fileSelected: EventEmitter<File>` and `fileRemoved: EventEmitter<void>`.

- [ ] **Step 1: Write the failing component tests**

Test the standalone component through `TestBed` and a host template. Cover:

```typescript
it('accepts AVIF, PNG, SVG, WEBP and JPEG within the logo limit', () => {
  for (const type of ['image/avif', 'image/png', 'image/svg+xml', 'image/webp', 'image/jpeg']) {
    const file = new File(['x'], `asset.${type.split('/')[1]}`, { type });
    component.kind = 'logo';
    component.onFileSelected({ target: { files: [file] } } as unknown as Event);
    expect(selected.emit).toHaveBeenCalledWith(file);
    selected.emit.mockClear();
  }
});

it('rejects an unsupported type and a file above the configured limit', () => {
  const unsupported = new File(['x'], 'asset.gif', { type: 'image/gif' });
  component.onFileSelected({ target: { files: [unsupported] } } as unknown as Event);
  expect(selected.emit).not.toHaveBeenCalled();
  expect(component.errorMessage()).toContain('Formato');

  const oversized = new File([new Uint8Array(2 * 1024 * 1024 + 1)], 'logo.png', { type: 'image/png' });
  component.onFileSelected({ target: { files: [oversized] } } as unknown as Event);
  expect(selected.emit).not.toHaveBeenCalled();
  expect(component.errorMessage()).toContain('2 MB');
});

it('uses the banner limit and emits removal requests', () => {
  component.kind = 'banner';
  const file = new File([new Uint8Array(5 * 1024 * 1024)], 'banner.webp', { type: 'image/webp' });
  component.onFileSelected({ target: { files: [file] } } as unknown as Event);
  expect(selected.emit).toHaveBeenCalledWith(file);
  component.removeFile();
  expect(removed.emit).toHaveBeenCalled();
});
```

- [ ] **Step 2: Run the focused test and verify it fails**

Run from `frontend/`:

```bash
npx jest --no-coverage src/app/shared/components/media-upload/media-upload.component.spec.ts
```

Expected: FAIL because the component and its public inputs/outputs do not exist.

- [ ] **Step 3: Implement the minimal standalone component**

Use Angular `input()` and `output()` signals or the existing project decorator style consistently. Define one shared MIME array and per-kind limits. The template must include:

```html
<input #fileInput type="file" [accept]="acceptedMimeTypes" (change)="onFileSelected($event)" hidden>
<button type="button" class="upload-box" [disabled]="disabled()" (click)="fileInput.click()">
  @if (previewUrl()) {
    <img [src]="previewUrl()" [alt]="altText()" class="upload-preview">
  } @else {
    <ion-icon name="cloud-upload-outline" aria-hidden="true"></ion-icon>
    <strong>{{ emptyTitle() }}</strong>
    <span>Formatos aceitos: AVIF, PNG, SVG, WEBP, JPG e JPEG.</span>
    <span>Até {{ maxSizeLabel() }}.</span>
  }
</button>
<button type="button" class="remove-button" (click)="removeFile()">Remover</button>
@if (errorMessage()) { <p class="upload-error" role="alert">{{ errorMessage() }}</p> }
```

Use `URL.createObjectURL(file)` only for local preview and revoke the previous object URL before replacing it and on component destruction. Do not transform the `File`.

- [ ] **Step 4: Run the focused test and verify it passes**

Run the same Jest command. Expected: all component tests PASS.

---

### Task 2: Integrate the Component into Store Configuration

**Files:**
- Modify: `frontend/src/app/features/store-config/store-config-page.component.ts`
- Modify: `frontend/src/app/features/store-config/store-config-page.component.html`
- Modify: `frontend/src/app/features/store-config/store-config-page.component.scss`
- Modify: `frontend/src/app/features/store-config/store-config-page.component.spec.ts`

**Interfaces:**
- Consumes `MediaUploadComponent` outputs as `onLogoSelected(file: File)` and `onBannerSelected(file: File)`.
- Produces the existing `logoFile`, `bannerFile`, `logoPreview`, and `bannerPreview` signals used by save logic.

- [ ] **Step 1: Add failing parent assertions**

Update the existing upload tests so they call the new file-based handlers and assert the exact original file is retained:

```typescript
it('keeps the selected logo format for upload', async () => {
  const file = new File(['svg'], 'logo.svg', { type: 'image/svg+xml' });
  await component.onLogoSelected(file);
  expect(component.logoFile()).toBe(file);
});

it('keeps the selected banner format for upload', async () => {
  const file = new File(['avif'], 'banner.avif', { type: 'image/avif' });
  await component.onBannerSelected(file);
  expect(component.bannerFile()).toBe(file);
});
```

- [ ] **Step 2: Run the focused parent test and verify it fails**

Run:

```bash
npx jest --no-coverage src/app/features/store-config/store-config-page.component.spec.ts
```

Expected: FAIL because the current handlers accept DOM events and compress to JPEG.

- [ ] **Step 3: Replace the duplicated upload markup and compression**

Import `MediaUploadComponent`, add it to the standalone imports, replace both upload blocks with:

```html
<app-media-upload kind="logo" [previewUrl]="logoPreview()" altText="Logo da loja" (fileSelected)="onLogoSelected($event)" />
<app-media-upload kind="banner" [previewUrl]="bannerPreview()" altText="Banner da loja" (fileSelected)="onBannerSelected($event)" />
```

Change the handlers to assign the original file and create a preview from that file. Remove `compressImage` and its JPEG conversion. Keep the existing save/upload request sequence unchanged.

- [ ] **Step 4: Run the focused parent test and verify it passes**

Run the same Jest command. Expected: all existing and new store configuration tests PASS.

---

### Task 3: Integrate the Component into Dashboard Bio

**Files:**
- Modify: `frontend/src/app/features/seller-bio/seller-bio-page.component.ts`
- Modify: `frontend/src/app/features/seller-bio/seller-bio-page.component.html`
- Modify: `frontend/src/app/features/seller-bio/seller-bio-page.component.scss`
- Modify: `frontend/src/app/features/seller-bio/seller-bio-page.component.spec.ts`

**Interfaces:**
- Consumes `MediaUploadComponent` `fileSelected` and `fileRemoved` events.
- Produces the existing `logoFile`, `bannerFile`, `logoPreview`, `bannerPreview`, `dirty`, and save behavior.

- [ ] **Step 1: Add failing dashboard assertions**

Assert that SVG logo and AVIF banner selections are accepted by the parent without duplicating MIME checks:

```typescript
it('accepts shared media selections for logo and banner', () => {
  const logo = new File(['svg'], 'logo.svg', { type: 'image/svg+xml' });
  const banner = new File(['avif'], 'banner.avif', { type: 'image/avif' });
  component.onLogoFile(logo);
  component.onBannerFile(banner);
  expect(component.logoFile()).toBe(logo);
  expect(component.bannerFile()).toBe(banner);
});
```

- [ ] **Step 2: Run the focused dashboard test and verify it fails**

Run:

```bash
npx jest --no-coverage src/app/features/seller-bio/seller-bio-page.component.spec.ts
```

Expected: FAIL because the current parent handlers reject SVG and AVIF.

- [ ] **Step 3: Replace dashboard upload UI and parent validation**

Import `MediaUploadComponent`, add it to the standalone imports, replace both file inputs with the shared component, and change parent handlers to receive `File` values. Keep `removeLogo`, `removeBanner`, dirty-state handling, and upload calls. Remove duplicate type and size validation from the parent.

- [ ] **Step 4: Run the focused dashboard test and verify it passes**

Run the same Jest command. Expected: all dashboard bio tests PASS.

---

### Task 4: Align Backend Upload Validation

**Files:**
- Modify: `backend/src/Urbeat.WebApi/Controllers/StoresController.cs:460-491`
- Modify: `backend/src/Urbeat.Infrastructure/Persistence/SystemParameterSeeder.cs:96`
- Create: `backend/tests/Urbeat.IntegrationTests/Api/StoreImageUploadTests.cs`

**Interfaces:**
- Consumes multipart files from the existing `POST /api/stores/upload-image?type=logo|banner` endpoint.
- Produces HTTP 400 for unsupported MIME/extension or an oversized logo/banner and continues returning `{ url }` for valid files.

- [ ] **Step 1: Add failing integration tests**

Add tests covering valid SVG/AVIF files, rejected GIF, logo over 2 MB, and banner over 5 MB. Assert status codes without asserting provider internals:

```csharp
[Theory]
[InlineData("logo.svg", "image/svg+xml")]
[InlineData("banner.avif", "image/avif")]
public async Task UploadImage_accepts_shared_media_formats(string name, string contentType)
{
    using var content = new MultipartFormDataContent();
    content.Add(new StreamContent(new MemoryStream("image"u8.ToArray())), "file", name);
    content.Headers.ContentType!.Parameters.First(p => p.Name == "boundary").Value = content.Headers.ContentType.Parameters.First(p => p.Name == "boundary").Value;
    var response = await _client.PostAsync($"/api/stores/upload-image?type=logo", content);
    response.StatusCode.Should().Be(HttpStatusCode.OK);
}
```

Use the existing integration-test authentication and image-upload stub setup from the repository; avoid sending real Cloudinary requests.

- [ ] **Step 2: Run the focused backend tests and verify they fail**

Run from the repository root:

```bash
dotnet test backend/tests/Urbeat.IntegrationTests --filter "FullyQualifiedName~StoreImageUploadTests"
```

Expected: the new AVIF/SVG cases fail against the current extension/MIME rules.

- [ ] **Step 3: Implement authoritative allowlist and limits**

Add a case-insensitive allowlist for `.avif`, `.png`, `.svg`, `.webp`, `.jpg`, and `.jpeg`, verify `file.ContentType`, and enforce 2 MB for `type=logo` and 5 MB for `type=banner`. Return a localized 400 error before opening the stream. Update the seeded `Upload.AllowedExtensions` value to `.avif,.jpg,.jpeg,.png,.svg,.webp` without changing unrelated upload types.

- [ ] **Step 4: Run focused backend tests and verify they pass**

Run the same `dotnet test` command. Expected: all valid and invalid upload cases PASS.

---

### Task 5: Full Verification

**Files:**
- No additional source files.

- [ ] **Step 1: Run all affected frontend tests**

```bash
npx jest --no-coverage src/app/shared/components/media-upload/media-upload.component.spec.ts src/app/features/store-config/store-config-page.component.spec.ts src/app/features/seller-bio/seller-bio-page.component.spec.ts
```

Expected: PASS.

- [ ] **Step 2: Build the frontend**

```bash
npx ng build --configuration production
```

Expected: successful build. Existing bundle budget warning may remain non-blocking.

- [ ] **Step 3: Build and test the backend**

```bash
dotnet build backend/Urbeat.sln
dotnet test backend/tests/Urbeat.IntegrationTests --filter "FullyQualifiedName~StoreImageUploadTests"
```

Expected: successful build and passing upload tests.

- [ ] **Step 4: Run the UI detector on changed frontend targets**

```bash
node C:\Projetos\urbeat\.opencode\skills\impeccable\scripts\detect.mjs --json frontend/src/app/shared/components/media-upload frontend/src/app/features/store-config frontend/src/app/features/seller-bio
```

Expected: no unexplained findings.
