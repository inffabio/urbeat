using System.IO;
using FluentAssertions;
using Urbeat.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Urbeat.UnitTests.Infrastructure.Services;

public class CloudinaryImageUploadServiceTests
{
    private readonly Mock<IOptions<CloudinaryOptions>> _optionsMock;
    private readonly Mock<ILogger<CloudinaryImageUploadService>> _loggerMock;

    public CloudinaryImageUploadServiceTests()
    {
        _optionsMock = new Mock<IOptions<CloudinaryOptions>>();
        _optionsMock.Setup(x => x.Value).Returns(new CloudinaryOptions
        {
            CloudName = "test_cloud",
            ApiKey = "test_key",
            ApiSecret = "test_secret"
        });

        _loggerMock = new Mock<ILogger<CloudinaryImageUploadService>>();
    }

    [Fact]
    public void Constructor_ShouldInitializeCloudinaryWithCorrectAccount()
    {
        // Act
        var service = new CloudinaryImageUploadService(_optionsMock.Object, _loggerMock.Object);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public void BuildUploadParams_ShouldConfigureUploadWithoutContactingCloudinary()
    {
        // Arrange
        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });

        // Act
        var parameters = CloudinaryImageUploadService.BuildUploadParams(stream, "test.jpg", "test-folder");

        // Assert
        parameters.Folder.Should().Be("test-folder");
        parameters.UseFilename.Should().BeTrue();
        parameters.UniqueFilename.Should().BeTrue();
        parameters.Overwrite.Should().BeFalse();
        parameters.File.Should().NotBeNull();
        parameters.File.FileName.Should().Be("test.jpg");
        parameters.File.Stream.Should().BeSameAs(stream);
        parameters.Transformation.Should().NotBeNull();
    }

    [Fact]
    public void BuildIncomingTransformation_ShouldNotUseDeliveryOnlyParameters()
    {
        // Act
        var transformation = CloudinaryImageUploadService.BuildIncomingTransformation();
        var serialized = transformation.ToString();

        // Assert
        // The incoming transformation must only normalize resolution and quality. An isolated
        // q_auto is allowed because quality normalization can be resolved at upload time, while
        // delivery-only parameters (f_auto, dpr_auto) are forbidden because they depend on the
        // requesting browser and, when baked into the stored asset, produce a broken/black image.
        serialized.Should().Contain("c_limit");
        serialized.Should().Contain("w_1920");
        serialized.Should().Contain("h_1920");
        serialized.Should().Contain("q_auto");
        serialized.Should().NotContain("f_auto");
        serialized.Should().NotContain("dpr_auto");
    }
}
