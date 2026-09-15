namespace Urbeat.Infrastructure.Services.Payments;

/// <summary>
/// Options controlling the deterministic mock Pix simulation. The mock is only active when
/// <see cref="Provider"/> is <c>Mock</c>; every other value (including the <c>MercadoPago</c>
/// default) keeps the real gateway strategy unchanged.
/// </summary>
public sealed class MockPixOptions
{
    public const string SectionName = "Payments";

    public string Provider { get; init; } = "MercadoPago";

    public int WindowSeconds { get; init; } = 60;

    public int ApprovalMinimumSeconds { get; init; } = 10;

    public int ApprovalMaximumSeconds { get; init; } = 50;

    public int WorkerPollingIntervalSeconds { get; init; } = 2;

    public bool WorkerEnabled { get; init; } = true;

    public bool IsMockEnabled => string.Equals(Provider, "Mock", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> Validate()
    {
        var errors = new List<string>();

        if (!string.Equals(Provider, "MercadoPago", StringComparison.OrdinalIgnoreCase)
            && !IsMockEnabled)
        {
            errors.Add($"Payments:Provider must be 'MercadoPago' or 'Mock' (got '{Provider}').");
        }

        if (WindowSeconds <= 0)
        {
            errors.Add("Payments:WindowSeconds must be greater than zero.");
        }

        if (ApprovalMinimumSeconds < 0)
        {
            errors.Add("Payments:ApprovalMinimumSeconds must be zero or greater.");
        }

        if (ApprovalMaximumSeconds <= ApprovalMinimumSeconds)
        {
            errors.Add("Payments:ApprovalMaximumSeconds must be greater than ApprovalMinimumSeconds.");
        }

        if (ApprovalMaximumSeconds >= WindowSeconds)
        {
            errors.Add("Payments:ApprovalMaximumSeconds must be less than WindowSeconds.");
        }

        if (WorkerPollingIntervalSeconds <= 0)
        {
            errors.Add("Payments:WorkerPollingIntervalSeconds must be greater than zero.");
        }

        return errors;
    }
}
