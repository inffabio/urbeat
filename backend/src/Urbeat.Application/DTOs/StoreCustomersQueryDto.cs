namespace Urbeat.Application.DTOs;

public sealed class StoreCustomersQueryDto
{
    public string Search { get; set; } = string.Empty;

    public string Status { get; set; } = "all";

    public string Sort { get; set; } = "lastOrderDesc";

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}
