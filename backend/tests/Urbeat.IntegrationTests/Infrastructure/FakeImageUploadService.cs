using Urbeat.Application.Interfaces;

namespace Urbeat.IntegrationTests.Infrastructure;

internal sealed class FakeImageUploadService : IImageUploadService
{
    public Task<string> UploadAsync(
        Stream fileStream,
        string fileName,
        string folder,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult($"https://test.invalid/{folder}/{fileName}");
    }

    public Task DeleteAsync(string imageUrl, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
