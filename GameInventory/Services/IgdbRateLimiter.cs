namespace GameInventory.Services
{
    /// <summary>
    /// Process-wide limiter for outbound IGDB requests.
    ///
    /// IGDB permits roughly 4 requests per second per client; bursting past that
    /// (e.g. the Explore page firing one request per category, or many users at
    /// once) can get the client IP temporarily blocked. Registered as a singleton
    /// so every controller shares one budget and the application as a whole never
    /// exceeds the limit.
    ///
    /// Implemented as a sliding 1-second window: a request may proceed only if
    /// fewer than <see cref="_maxPerSecond"/> requests were issued in the previous
    /// second, otherwise the caller awaits until the oldest one ages out.
    /// </summary>
    public class IgdbRateLimiter
    {
        private readonly int _maxPerSecond;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly Queue<long> _timestamps = new();

        public IgdbRateLimiter(int maxPerSecond = 4)
        {
            _maxPerSecond = Math.Max(1, maxPerSecond);
        }

        /// <summary>
        /// Asynchronously blocks until it is safe to issue another IGDB request,
        /// then records the request against the current window.
        /// </summary>
        public async Task WaitTurnAsync(CancellationToken ct = default)
        {
            // Serialise the bookkeeping so the window stays consistent across callers.
            await _gate.WaitAsync(ct);
            try
            {
                while (true)
                {
                    var now = Environment.TickCount64;

                    // Evict timestamps older than one second.
                    while (_timestamps.Count > 0 && now - _timestamps.Peek() >= 1000)
                        _timestamps.Dequeue();

                    if (_timestamps.Count < _maxPerSecond)
                    {
                        _timestamps.Enqueue(now);
                        return;
                    }

                    // Window is full — wait until the oldest request leaves it.
                    var waitMs = (int)(1000 - (now - _timestamps.Peek())) + 5;
                    if (waitMs > 0)
                        await Task.Delay(waitMs, ct);
                }
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
