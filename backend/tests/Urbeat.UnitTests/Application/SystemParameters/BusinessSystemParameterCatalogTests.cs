using FluentAssertions;
using Urbeat.Application.SystemParameters;

namespace Urbeat.UnitTests.Application.SystemParameters;

public sealed class BusinessSystemParameterCatalogTests
{
    [Fact]
    public void Keys_ShouldContainExactlyTheBusinessCatalog()
    {
        BusinessSystemParameterCatalog.Keys.Should().BeEquivalentTo(
        [
            "Customer.MaxAddresses",
            "Order.DefaultPageSize",
            "Order.MaxPageSize",
            "Order.CodePrefix",
            "Order.CodeLength",
            "Order.CodeChars",
            "Order.CodeMaxAttempts",
            "Notification.MaxReturned",
            "Store.DefaultDeliveryFee",
            "Store.DefaultMinimumOrderValue",
            "Subscription.DueSoonDays",
            "Subscription.DefaultBillingCycleDays",
            "Subscription.BillingType",
            "Subscription.Cycle",
            "Subscription.Description"
        ]);
    }

    [Theory]
    [InlineData("Jwt.ExpirationMinutes")]
    [InlineData("Password.RequiredLength")]
    [InlineData("Lockout.MaxFailedAccessAttempts")]
    [InlineData("Email.SmtpPort")]
    [InlineData("Email.FromAddress")]
    [InlineData("Url.Asaas")]
    [InlineData("Http.ViaCepTimeoutSeconds")]
    [InlineData("Upload.MaxFileSizeBytes")]
    [InlineData("Payment.Currency")]
    [InlineData("Hangfire.WorkerCount")]
    [InlineData("Cors.AllowAnyOrigin")]
    [InlineData("Cookie.Secure")]
    public void Contains_ShouldRejectTechnicalInfrastructureAndIntegrationKeys(string key)
    {
        BusinessSystemParameterCatalog.Contains(key).Should().BeFalse();
    }

    [Theory]
    [InlineData("Customer.MaxAddresses")]
    [InlineData("Store.DefaultDeliveryFee")]
    [InlineData("Subscription.Description")]
    public void Contains_ShouldAcceptBusinessKeys(string key)
    {
        BusinessSystemParameterCatalog.Contains(key).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Contains_ShouldRejectBlankKeys(string key)
    {
        BusinessSystemParameterCatalog.Contains(key).Should().BeFalse();
    }
}
