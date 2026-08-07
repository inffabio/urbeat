using System.ComponentModel.DataAnnotations;

namespace Urbeat.Application.Dtos;

public sealed class StorePrinterConfigRequestDto
{
    [Required]
    public Guid PrinterPresetId { get; set; }

    [MaxLength(120)]
    public string PrinterName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? MacAddress { get; set; }

    [Range(1, 5)]
    public int Copies { get; set; } = 1;

    public bool AutoPrint { get; set; }

    public bool AutoCut { get; set; }

    public bool PrintKitchenCopy { get; set; } = true;

    public bool PrintCounterCopy { get; set; } = true;

    public bool PrintCustomerReceipt { get; set; }

    public bool PrintLogo { get; set; }

    public bool HighlightOrderNumber { get; set; } = true;

    [MaxLength(200)]
    public string? FooterText { get; set; }
}

public sealed class StorePrinterConfigResponseDto
{
    public Guid Id { get; set; }
    public Guid StoreId { get; set; }
    public Guid PrinterPresetId { get; set; }
    public string PrinterName { get; set; } = string.Empty;
    public string? MacAddress { get; set; }
    public int Copies { get; set; }
    public bool AutoPrint { get; set; }
    public bool AutoCut { get; set; }
    public bool PrintKitchenCopy { get; set; }
    public bool PrintCounterCopy { get; set; }
    public bool PrintCustomerReceipt { get; set; }
    public bool PrintLogo { get; set; }
    public bool HighlightOrderNumber { get; set; }
    public string? FooterText { get; set; }
}
