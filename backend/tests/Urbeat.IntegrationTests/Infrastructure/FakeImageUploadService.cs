using Urbeat.Application.Interfaces;

namespace Urbeat.IntegrationTests.Infrastructure;

public sealed class FakeImageUploadService : IImageUploadService
{
    public List<string> DeletedUrls { get; } = new();

    public HashSet<string> FailForUrls { get; } = new(StringComparer.Ordinal);

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
        if (FailForUrls.Contains(imageUrl))
        {
            throw new InvalidOperationException("Fake image deletion failure.");
        }

        DeletedUrls.Add(imageUrl);
        return Task.CompletedTask;
    }
}
