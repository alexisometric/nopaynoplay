using System;
using System.Collections.Concurrent;

namespace Jellyfin.Plugin.NoPayNoPlay.Services;

/// <summary>
/// Minimal in-memory rate limiter (per-key). Used to stop a member from spamming
/// the "I paid" self-service button and to throttle promo-code brute-force.
/// Volatile by design — if the server restarts, the cooldown resets, which is
/// acceptable for a UX guardrail (not a security boundary). The failure counters
/// are pruned periodically so the per-IP / per-key dictionaries cannot grow
/// without bound.
/// </summary>
public class RateLimiter
{
    private readonly Func<DateTime> _now;
    private readonly ConcurrentDictionary<string, DateTime> _last = new();
    private readonly ConcurrentDictionary<string, FailureEntry> _failures = new();

    private readonly struct FailureEntry
    {
        public readonly int Count;
        public readonly DateTime Until;   // lockout end (DateTime.MinValue when not locked)
        public readonly DateTime LastAt;  // last failure timestamp (used for pruning)

        public FailureEntry(int count, DateTime until, DateTime lastAt)
        {
            Count = count;
            Until = until;
            LastAt = lastAt;
        }
    }

    /// <summary>Production constructor (uses the system UTC clock).</summary>
    public RateLimiter()
        : this(null)
    {
    }

    /// <summary>Test-friendly constructor allowing a deterministic injected clock.</summary>
    internal RateLimiter(Func<DateTime>? now)
    {
        _now = now ?? (() => DateTime.UtcNow);
    }

    /// <summary>
    /// Returns true if the action is allowed for <paramref name="key"/>, and
    /// records the new timestamp. Returns false (without updating) when the
    /// previous call happened less than <paramref name="window"/> ago.
    /// </summary>
    /// <remarks>
    /// Implemented lock-free with a compare-and-swap loop so the decision is never
    /// computed as a side-effect inside a dictionary factory (which may run more
    /// than once under contention).
    /// </remarks>
    public bool TryAcquire(string key, TimeSpan window)
    {
        DateTime now = _now();
        PruneIfNeeded();

        while (true)
        {
            if (_last.TryGetValue(key, out var last))
            {
                if (now - last < window)
                {
                    return false;
                }

                if (_last.TryUpdate(key, now, last))
                {
                    return true;
                }
            }
            else if (_last.TryAdd(key, now))
            {
                return true;
            }
        }
    }

    /// <summary>Returns the remaining cooldown for a key (zero if free).</summary>
    public TimeSpan Remaining(string key, TimeSpan window)
    {
        if (!_last.TryGetValue(key, out var last)) return TimeSpan.Zero;
        TimeSpan elapsed = _now() - last;
        return elapsed >= window ? TimeSpan.Zero : window - elapsed;
    }

    /// <summary>
    /// Records a failed attempt for <paramref name="key"/> and returns true when
    /// the threshold has been reached, in which case the key is locked for
    /// <paramref name="lockout"/>. Used to throttle promo-code brute-force.
    /// </summary>
    /// <remarks>
    /// Lock-free (compare-and-swap loop) — the lock decision is never made as a
    /// side-effect of a dictionary value factory, so it is always accurate.
    /// </remarks>
    public bool RegisterFailureAndShouldLock(string key, int threshold, TimeSpan lockout)
    {
        DateTime now = _now();
        PruneIfNeeded();

        while (true)
        {
            if (_failures.TryGetValue(key, out var prev))
            {
                // A previous lockout has fully elapsed: restart the counter at 1.
                if (prev.Until > DateTime.MinValue && now >= prev.Until)
                {
                    bool willLockFirst = 1 >= threshold;
                    var fresh = new FailureEntry(1, willLockFirst ? now + lockout : DateTime.MinValue, now);
                    if (_failures.TryUpdate(key, fresh, prev))
                    {
                        return willLockFirst;
                    }

                    continue;
                }

                int next = prev.Count + 1;
                DateTime until = prev.Until;
                if (next >= threshold && until == DateTime.MinValue)
                {
                    until = now + lockout;
                }

                bool willLock = next >= threshold;
                var updated = new FailureEntry(next, until, now);
                if (_failures.TryUpdate(key, updated, prev))
                {
                    return willLock;
                }
            }
            else
            {
                bool willLock = 1 >= threshold;
                var fresh = new FailureEntry(1, willLock ? now + lockout : DateTime.MinValue, now);
                if (_failures.TryAdd(key, fresh))
                {
                    return willLock;
                }
            }
        }
    }

    /// <summary>True if the key is currently locked because of too many failures.</summary>
    public bool IsLocked(string key)
    {
        if (!_failures.TryGetValue(key, out var entry)) return false;
        if (entry.Until <= DateTime.MinValue) return false;
        return _now() < entry.Until;
    }

    /// <summary>Clears the failure counter for the key (e.g. after a success).</summary>
    public void ClearFailures(string key) => _failures.TryRemove(key, out _);

    /// <summary>
    /// Bounds the dictionaries: once they grow past a threshold, drop entries that
    /// can no longer affect any decision (expired lockouts, stale timestamps), so
    /// the per-IP / per-key tables cannot grow without bound.
    /// </summary>
    private void PruneIfNeeded()
    {
        if (_last.Count + _failures.Count < 2048)
        {
            return;
        }

        DateTime now = _now();
        // Cooldowns older than a day can never matter (all windows are ≤ 30 min).
        foreach (var kv in _last)
        {
            if (now - kv.Value > TimeSpan.FromDays(1))
            {
                _last.TryRemove(kv.Key, out _);
            }
        }

        // Failures: drop expired lockouts and counters untouched for a day.
        foreach (var kv in _failures)
        {
            bool expiredLockout = kv.Value.Until > DateTime.MinValue && now >= kv.Value.Until;
            bool stale = now - kv.Value.LastAt > TimeSpan.FromDays(1);
            if (expiredLockout || stale)
            {
                _failures.TryRemove(kv.Key, out _);
            }
        }
    }
}
