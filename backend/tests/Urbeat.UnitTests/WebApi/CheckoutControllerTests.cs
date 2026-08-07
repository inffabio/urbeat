using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Urbeat.Application.DTOs;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.WebApi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Urbeat.UnitTests.WebApi;

public sealed class CheckoutControllerTests
{
    [Fact]
    public async Task Preview_ShouldReturnSummaryOk_WhenOrderIsBelowMinimum()
    {
        var request = new CheckoutRequestDto
        {
            StoreId = Guid.NewGuid(),
            FulfillmentType = FulfillmentType.Delivery,
            Items = []
        };

        var checkoutService = new Mock<ICheckoutService>();
        checkoutService
            .Setup(x => x.PreviewAsync(null, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckoutResultDto
            {
                BelowMinimum = true,
                Summary = new CheckoutSummaryResponseDto
                {
                    StoreId = request.StoreId,
                    FulfillmentType = FulfillmentType.Delivery,
                    MinimumOrderValue = 20m,
                    Subtotal = 10m,
                    Total = 10m
                }
            });

        var validator = new Mock<IValidator<CheckoutRequestDto>>();
        validator
            .Setup(x => x.ValidateAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        var otpService = new Mock<ICustomerOtpService>();
        var controller = new CheckoutController(checkoutService.Object, validator.Object, otpService.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var result = await controller.Preview(request, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<CheckoutSummaryResponseDto>()
            .Which.MinimumOrderValue.Should().Be(20m);
    }

    [Fact]
    public async Task ConfirmCustomerVerification_ShouldSetRefreshCookie_WhenCodeIsValid()
    {
        var request = new ConfirmCustomerVerificationRequestDto
        {
            VerificationId = Guid.NewGuid(),
            Code = "123456"
        };
        var addressId = Guid.NewGuid();
        var checkoutService = new Mock<ICheckoutService>();
        var validator = new Mock<IValidator<CheckoutRequestDto>>();
        var otpService = new Mock<ICustomerOtpService>();
        otpService
            .Setup(x => x.ConfirmAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConfirmCustomerVerificationResponseDto
            {
                Succeeded = true,
                AccessToken = "access-token",
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15),
                RefreshToken = "refresh-token",
                RefreshTokenExpiresAtUtc = DateTime.UtcNow.AddDays(7),
                CustomerAddressId = addressId
            });

        var httpContext = new DefaultHttpContext();
        var controller = new CheckoutController(checkoutService.Object, validator.Object, otpService.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };

        var result = await controller.ConfirmCustomerVerification(request, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<ConfirmCustomerVerificationResponseDto>()
            .Which.CustomerAddressId.Should().Be(addressId);
        httpContext.Response.Headers.SetCookie.ToString().Should().Contain("urbeat.refresh_token=refresh-token");
        httpContext.Response.Headers.SetCookie.ToString().Should().Contain("httponly");
    }

    [Fact]
    public async Task CreateCustomerSession_ShouldSetSecureRefreshCookie()
    {
        var request = new StartCustomerVerificationRequestDto
        {
            StoreId = Guid.NewGuid(),
            Customer = new CustomerVerificationCustomerDto
            {
                FullName = "Maria Oliveira",
                Email = "maria@email.com",
                PhoneNumber = "22999999999"
            },
            Address = new CustomerVerificationAddressDto
            {
                Cep = "28000000",
                Street = "Rua Principal",
                Number = "123",
                Neighborhood = "Centro",
                City = "Campos",
                State = "RJ"
            }
        };
        var addressId = Guid.NewGuid();
        var checkoutService = new Mock<ICheckoutService>();
        var validator = new Mock<IValidator<CheckoutRequestDto>>();
        var otpService = new Mock<ICustomerOtpService>();
        otpService
            .Setup(x => x.CreateCustomerSessionAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConfirmCustomerVerificationResponseDto
            {
                Succeeded = true,
                AccessToken = "access-token",
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15),
                RefreshToken = "refresh-token",
                RefreshTokenExpiresAtUtc = DateTime.UtcNow.AddDays(7),
                CustomerAddressId = addressId
            });

        var httpContext = new DefaultHttpContext();
        var controller = new CheckoutController(checkoutService.Object, validator.Object, otpService.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };

        var result = await controller.CreateCustomerSession(request, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<ConfirmCustomerVerificationResponseDto>()
            .Which.CustomerAddressId.Should().Be(addressId);
        var setCookie = httpContext.Response.Headers.SetCookie.ToString();
        setCookie.Should().Contain("urbeat.refresh_token=refresh-token");
        setCookie.Should().Contain("httponly");
        setCookie.Should().Contain("secure");
    }
}
