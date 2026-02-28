namespace Timecut.Infrastructure.Processing;

/// <summary>
/// Controls concurrent job processing using a SemaphoreSlim.
/// </summary>
public class ConcurrencyControl
{
    private SemaphoreSlim _semaphore;
    private int _limit;
    private readonly object _lock = new();

    public ConcurrencyControl(int initialLimit = 3)
    {
        _limit = initialLimit;
        _semaphore = new SemaphoreSlim(initialLimit);
    }

    public int CurrentLimit
    {
        get { lock (_lock) return _limit; }
    }

    public void SetLimit(int limit)
    {
        if (limit < 1) limit = 1;
        lock (_lock)
        {
            _limit = limit;
            _semaphore = new SemaphoreSlim(limit);
        }
    }

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
    }

    public void Release()
    {
        try { _semaphore.Release(); } catch { }
    }
}
