using Urbeat.Application.Dtos;

namespace Urbeat.Application.Interfaces;

public interface IPrinterConfigService
{
    Task<List<PrinterPresetResponseDto>> GetPresetsAsync(CancellationToken cancellationToken);
    Task<StorePrinterConfigResponseDto?> GetStoreConfigAsync(Guid sellerUserId, CancellationToken cancellationToken);
    Task<StorePrinterConfigResponseDto> SaveStoreConfigAsync(Guid sellerUserId, StorePrinterConfigRequestDto request, CancellationToken cancellationToken);
}
