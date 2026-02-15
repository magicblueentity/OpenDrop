using System.Collections.Concurrent;

namespace OpenDrop.Infrastructure.Transfer;

internal sealed class TokenBucketLimiter
{
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _capacity;
    private readonly double _refillPerSecond;

    public TokenBucketLimiter(int capacity, double refillPerSecond)
    {
        _capacity = capacity;
        _refillPerSecond = refillPerSecond;
    }

    public bool TryConsume(string key, int tokens)
    {
        var bucket = _buckets.GetOrAdd(key, _ => new Bucket(_capacity, _refillPerSecond));
        return bucket.TryConsume(tokens);
    }

    private sealed class Bucket
    {
        private readonly int _capacity;
        private readonly double _refillPerSecond;
        private double _tokens;
        private long _lastTicks;
        private readonly object _gate = new();

        public Bucket(int capacity, double refillPerSecond)
        {
            _capacity = capacity;
            _refillPerSecond = refillPerSecond;
            _tokens = capacity;
            _lastTicks = DateTime.UtcNow.Ticks;
        }

        public bool TryConsume(int tokens)
        {
            lock (_gate)
            {
                Refill();
                if (_tokens < tokens) return false;
                _tokens -= tokens;
                return true;
            }
        }

        private void Refill()
        {
            var now = DateTime.UtcNow.Ticks;
            var dtSeconds = (now - _lastTicks) / (double)TimeSpan.TicksPerSecond;
            _lastTicks = now;
            _tokens = Math.Min(_capacity, _tokens + dtSeconds * _refillPerSecond);
        }
    }
}
