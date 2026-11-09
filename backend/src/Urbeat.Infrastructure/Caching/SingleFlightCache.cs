namespace Urbeat.Infrastructure.Caching;

/// <summary>
/// Small in-process cache with a fixed lifetime and single-flight reloading.
/// <para>
/// Concurrent callers that miss the cache share one factory execution instead of issuing one query
/// per request. This is what prevents health probes and public read endpoints from multiplying
/// database work under load. The cached value is never served after its lifetime expires; if the
/// refresh fails, every waiter observes the failure so an unavailable dependency is not masked.
/// </para>
/// <para>
/// The refresh itself runs detached from any single request's cancellation token, so one aborted
/// client cannot poison the shared reload. Waiters still honor their own token while waiting.
/// </para>
/// </summary>
public sealed class SingleFlightCache<T> where T : class
{
    private sealed record Entry(T Value, DateTimeOffset CapturedAtUtc);

    private readonly object _sync = new();
    private readonly TimeSpan _lifetime;
    private readonly TimeProvider _timeProvider;
    private Entry? _entry;
    private Task<T>? _refresh;
    private long _version;

    public SingleFlightCache(TimeSpan lifetime, TimeProvider timeProvider)
    {
        if (lifetime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "Cache lifetime must not be negative.");
        }

        _lifetime = lifetime;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<T> GetAsync(
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(factory);

        Task<T> refresh;
        TaskCompletionSource<T>? start = null;
        long version = 0;

        lock (_sync)
        {
            var entry = Volatile.Read(ref _entry);
            if (IsFresh(entry))
            {
                return entry!.Value;
            }

            if (_refresh is null)
            {
                var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
                refresh = completion.Task;
                _refresh = refresh;
                version = _version;
                start = completion;
            }
            else
            {
                refresh = _refresh;
            }
        }

        if (start is not null)
        {
            _ = RefreshAsync(factory, start, version);
        }

        return await refresh.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Drops the cached value so the next caller reloads it.</summary>
    public void Invalidate()
    {
        lock (_sync)
        {
            // The version guard prevents a refresh that started before this invalidation from
            // repopulating the cache with the pre-write snapshot after the write commits.
            _version++;
            Volatile.Write(ref _entry, null);
        }
    }

    private bool IsFresh(Entry? entry)
        => entry is not null && _timeProvider.GetUtcNow() - entry.CapturedAtUtc < _lifetime;

    private async Task RefreshAsync(
        Func<CancellationToken, Task<T>> factory,
        TaskCompletionSource<T> completion,
        long version)
    {
        T value;
        try
        {
            value = await factory(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            lock (_sync)
            {
                _refresh = null;
            }

            completion.TrySetException(exception);
            return;
        }

        lock (_sync)
        {
            if (_version == version)
            {
                Volatile.Write(ref _entry, new Entry(value, _timeProvider.GetUtcNow()));
            }

            _refresh = null;
        }

        completion.TrySetResult(value);
    }
}
