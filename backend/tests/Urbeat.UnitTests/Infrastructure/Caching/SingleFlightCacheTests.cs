using FluentAssertions;
using Urbeat.Infrastructure.Caching;

namespace Urbeat.UnitTests.Infrastructure.Caching;

public sealed class SingleFlightCacheTests
{
    [Fact]
    public async Task GetAsync_ShouldReuseValue_WithinLifetime()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var cache = new SingleFlightCache<object>(TimeSpan.FromSeconds(10), clock);
        var calls = 0;

        var first = await cache.GetAsync(_ => Task.FromResult<object>(++calls), CancellationToken.None);
        var second = await cache.GetAsync(_ => Task.FromResult<object>(++calls), CancellationToken.None);

        calls.Should().Be(1);
        second.Should().BeSameAs(first);
    }

    [Fact]
    public async Task GetAsync_ShouldReload_AfterLifetimeExpires()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var cache = new SingleFlightCache<object>(TimeSpan.FromSeconds(10), clock);
        var calls = 0;

        await cache.GetAsync(_ => Task.FromResult<object>(++calls), CancellationToken.None);
        clock.UtcNow = clock.UtcNow.AddSeconds(11);
        await cache.GetAsync(_ => Task.FromResult<object>(++calls), CancellationToken.None);

        calls.Should().Be(2);
    }

    [Fact]
    public async Task GetAsync_ShouldCoalesceConcurrentMisses_IntoOneFactoryCall()
    {
        var cache = new SingleFlightCache<object>(TimeSpan.FromSeconds(10), TimeProvider.System);
        var calls = 0;
        var gate = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

        var requests = Enumerable.Range(0, 50)
            .Select(_ => cache.GetAsync(
                _ =>
                {
                    Interlocked.Increment(ref calls);
                    return gate.Task;
                },
                CancellationToken.None))
            .ToArray();

        gate.SetResult(new object());
        var results = await Task.WhenAll(requests);

        calls.Should().Be(1);
        results.Should().OnlyContain(result => ReferenceEquals(result, results[0]));
    }

    [Fact]
    public async Task GetAsync_ShouldNotServeStaleValue_WhenRefreshFails()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var cache = new SingleFlightCache<object>(TimeSpan.FromSeconds(1), clock);

        await cache.GetAsync(_ => Task.FromResult<object>(new object()), CancellationToken.None);
        clock.UtcNow = clock.UtcNow.AddSeconds(2);

        var failure = new InvalidOperationException("database unavailable");
        var act = () => cache.GetAsync(_ => Task.FromException<object>(failure), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("database unavailable");
    }

    [Fact]
    public async Task Invalidate_ShouldForceNextCallToReload()
    {
        var cache = new SingleFlightCache<object>(TimeSpan.FromSeconds(30), TimeProvider.System);
        var calls = 0;

        await cache.GetAsync(_ => Task.FromResult<object>(++calls), CancellationToken.None);
        cache.Invalidate();
        await cache.GetAsync(_ => Task.FromResult<object>(++calls), CancellationToken.None);

        calls.Should().Be(2);
    }

    [Fact]
    public async Task Invalidate_DuringRefresh_ShouldNotCacheThePreInvalidationValue()
    {
        var cache = new SingleFlightCache<object>(TimeSpan.FromSeconds(30), TimeProvider.System);
        var gate = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        var pending = cache.GetAsync(
            _ =>
            {
                Interlocked.Increment(ref calls);
                return gate.Task;
            },
            CancellationToken.None);

        cache.Invalidate();
        gate.SetResult(new object());
        await pending;

        await cache.GetAsync(_ => Task.FromResult<object>(Interlocked.Increment(ref calls)), CancellationToken.None);

        calls.Should().Be(2);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; set; }

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
