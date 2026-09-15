using Microsoft.Extensions.Options;

namespace Urbeat.Infrastructure.Services.Payments;

/// <summary>
/// Wires <see cref="MockPixOptions.Validate"/> into the options validation pipeline so an invalid
/// <c>Payments</c> configuration fails at startup instead of silently falling back to defaults.
/// </summary>
public sealed class MockPixOptionsValidator : IValidateOptions<MockPixOptions>
{
    public ValidateOptionsResult Validate(string? name, MockPixOptions options)
    {
        var errors = options.Validate();
        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
