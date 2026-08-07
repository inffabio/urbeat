namespace Urbeat.Domain.Entities;

public sealed class StorePrinterConfig : BaseEntity
{
    public Guid StoreId { get; set; }

    public Guid PrinterPresetId { get; set; }

    public string PrinterName { get; set; } = string.Empty;

    public string? MacAddress { get; set; }

    public int Copies { get; set; } = 1;

    public bool AutoPrint { get; set; }

    public bool AutoCut { get; set; }

    public bool PrintKitchenCopy { get; set; } = true;

    public bool PrintCounterCopy { get; set; } = true;

    public bool PrintCustomerReceipt { get; set; }

    public bool PrintLogo { get; set; }

    public bool HighlightOrderNumber { get; set; } = true;

    public string? FooterText { get; set; }
}
