using Urbeat.Application.Dtos;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Urbeat.Infrastructure.Services;

public sealed class PrinterConfigService : IPrinterConfigService
{
    private readonly ApplicationDbContext _context;

    public PrinterConfigService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<PrinterPresetResponseDto>> GetPresetsAsync(CancellationToken cancellationToken)
    {
        return await _context.PrinterPresets
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new PrinterPresetResponseDto
            {
                Id = x.Id,
                Name = x.Name,
                Manufacturer = x.Manufacturer,
                ConnectionType = x.ConnectionType,
                PaperWidth = x.PaperWidth,
                CommandSet = x.CommandSet,
                AdapterId = x.AdapterId,
                Description = x.Description,
                IsActive = x.IsActive,
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<StorePrinterConfigResponseDto?> GetStoreConfigAsync(Guid sellerUserId, CancellationToken cancellationToken)
    {
        var storeId = await _context.Stores
            .Where(s => s.OwnerUserId == sellerUserId)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (storeId is null) return null;

        var entity = await _context.StorePrinterConfigs
            .FirstOrDefaultAsync(x => x.StoreId == storeId.Value, cancellationToken);

        if (entity is null) return null;

        return MapToResponse(entity);
    }

    public async Task<StorePrinterConfigResponseDto> SaveStoreConfigAsync(Guid sellerUserId, StorePrinterConfigRequestDto request, CancellationToken cancellationToken)
    {
        var storeId = await _context.Stores
            .Where(s => s.OwnerUserId == sellerUserId)
            .Select(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var entity = await _context.StorePrinterConfigs
            .FirstOrDefaultAsync(x => x.StoreId == storeId, cancellationToken);

        if (entity is null)
        {
            entity = new StorePrinterConfig { StoreId = storeId };
            _context.StorePrinterConfigs.Add(entity);
        }
        else
        {
            entity.MarkAsUpdated();
        }

        entity.PrinterPresetId = request.PrinterPresetId;
        entity.PrinterName = request.PrinterName;
        entity.MacAddress = request.MacAddress;
        entity.Copies = request.Copies;
        entity.AutoPrint = request.AutoPrint;
        entity.AutoCut = request.AutoCut;
        entity.PrintKitchenCopy = request.PrintKitchenCopy;
        entity.PrintCounterCopy = request.PrintCounterCopy;
        entity.PrintCustomerReceipt = request.PrintCustomerReceipt;
        entity.PrintLogo = request.PrintLogo;
        entity.HighlightOrderNumber = request.HighlightOrderNumber;
        entity.FooterText = request.FooterText;

        await _context.SaveChangesAsync(cancellationToken);

        return MapToResponse(entity);
    }

    private static StorePrinterConfigResponseDto MapToResponse(StorePrinterConfig entity)
    {
        return new StorePrinterConfigResponseDto
        {
            Id = entity.Id,
            StoreId = entity.StoreId,
            PrinterPresetId = entity.PrinterPresetId,
            PrinterName = entity.PrinterName,
            MacAddress = entity.MacAddress,
            Copies = entity.Copies,
            AutoPrint = entity.AutoPrint,
            AutoCut = entity.AutoCut,
            PrintKitchenCopy = entity.PrintKitchenCopy,
            PrintCounterCopy = entity.PrintCounterCopy,
            PrintCustomerReceipt = entity.PrintCustomerReceipt,
            PrintLogo = entity.PrintLogo,
            HighlightOrderNumber = entity.HighlightOrderNumber,
            FooterText = entity.FooterText,
        };
    }
}
