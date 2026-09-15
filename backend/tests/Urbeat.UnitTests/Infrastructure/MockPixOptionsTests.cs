using FluentAssertions;
using Urbeat.Infrastructure.Services.Payments;

namespace Urbeat.UnitTests.Infrastructure;

public sealed class MockPixOptionsTests
{
    [Fact]
    public void Defaults_ShouldKeepRealGatewayBehavior()
    {
        var options = new MockPixOptions();

        options.Provider.Should().Be("MercadoPago");
        options.IsMockEnabled.Should().BeFalse();
        options.WindowSeconds.Should().Be(60);
        options.ApprovalMinimumSeconds.Should().Be(10);
        options.ApprovalMaximumSeconds.Should().Be(50);
        options.WorkerPollingIntervalSeconds.Should().Be(2);
        options.WorkerEnabled.Should().BeTrue();
    }

    [Fact]
    public void IsMockEnabled_ShouldBeCaseInsensitive()
    {
        new MockPixOptions { Provider = "mock" }.IsMockEnabled.Should().BeTrue();
        new MockPixOptions { Provider = "Mock" }.IsMockEnabled.Should().BeTrue();
        new MockPixOptions { Provider = "MercadoPago" }.IsMockEnabled.Should().BeFalse();
        new MockPixOptions { Provider = "asaas" }.IsMockEnabled.Should().BeFalse();
    }

    [Fact]
    public void Validate_ShouldAcceptDefaults()
    {
        new MockPixOptions().Validate().Should().BeEmpty();
    }

    [Fact]
    public void Validate_ShouldRejectUnknownProvider()
    {
        new MockPixOptions { Provider = "Unknown" }
            .Validate()
            .Should().ContainSingle(x => x.Contains("Provider", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Validate_ShouldRejectNonPositiveWindow(int windowSeconds)
    {
        new MockPixOptions { WindowSeconds = windowSeconds }
            .Validate()
            .Should().Contain(x => x.Contains("WindowSeconds", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ShouldRejectApprovalRangeThatExceedsWindow()
    {
        new MockPixOptions { WindowSeconds = 60, ApprovalMaximumSeconds = 60 }
            .Validate()
            .Should().Contain(x => x.Contains("ApprovalMaximumSeconds", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ShouldRejectInvertedApprovalRange()
    {
        new MockPixOptions { ApprovalMinimumSeconds = 50, ApprovalMaximumSeconds = 10 }
            .Validate()
            .Should().Contain(x => x.Contains("ApprovalMaximumSeconds", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ShouldRejectNonPositiveWorkerPollingInterval()
    {
        new MockPixOptions { WorkerPollingIntervalSeconds = 0 }
            .Validate()
            .Should().Contain(x => x.Contains("WorkerPollingIntervalSeconds", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validator_ShouldSucceed_ForValidOptions()
    {
        var result = new MockPixOptionsValidator().Validate(null, new MockPixOptions { Provider = "Mock" });

        result.Succeeded.Should().BeTrue();
        result.Failed.Should().BeFalse();
    }

    [Fact]
    public void Validator_ShouldFail_ForInvalidOptions()
    {
        var result = new MockPixOptionsValidator().Validate(null, new MockPixOptions
        {
            Provider = "Unknown",
            WindowSeconds = 0
        });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Provider");
        result.FailureMessage.Should().Contain("WindowSeconds");
    }
}
