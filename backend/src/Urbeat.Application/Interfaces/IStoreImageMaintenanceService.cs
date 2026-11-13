using Urbeat.Application.DTOs;

namespace Urbeat.Application.Interfaces;

/// <summary>
/// Locates and removes Cloudinary images under <c>stores/</c> that are not referenced by any
/// persisted URL. Scanning never deletes; cleanup requires explicit confirmation and always
/// re-validates the fixed prefix and the database references before each deletion.
/// </summary>
public interface IStoreImageMaintenanceService
{
    Task<StoreImageOrphanScanResultDto> ScanAsync(
        StoreImageOrphanScanRequestDto request,
        CancellationToken cancellationToken = default);

    Task<StoreImageOrphanCleanupResultDto> CleanupAsync(
        StoreImageOrphanCleanupRequestDto request,
        CancellationToken cancellationToken = default);
}
