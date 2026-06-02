using System;
using System.Collections.Concurrent;

namespace Jellyfin.Plugin.NoPayNoPlay.Services;

/// <summary>
/// Minimal in-memory rate limiter (per-user). Used to stop a member from spamming
/// the "I paid" self-service button. Volatile by design — if the server restarts,
/// the cooldown resets, which is acceptable for a UX guardrail (not a security
/// boundary).
/// </summary>
public class RateLimiter
{
    private readonly Func<DateTime> _now;
    private readonly ConcurrentDictionary<string, DateTime> _last = new();

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
    public bool TryAcquire(string key, TimeSpan window)
    {
        DateTime now = _now();
        bool allowed = false;
        _last.AddOrUpdate(
            key,
            _ =>
            {
                allowed = true;
                return now;
            },
            (_, last) =>
            {
                if (now - last >= window)
                {
                    allowed = true;
                    return now;
                }

                return last;
            });
        return allowed;
    }

    /// <summary>Returns the remaining cooldown for a key (zero if free).</summary>
    public TimeSpan Remaining(string key, TimeSpan window)
    {
        if (!_last.TryGetValue(key, out var last)) return TimeSpan.Zero;
        TimeSpan elapsed = _now() - last;
        return elapsed >= window ? TimeSpan.Zero : window - elapsed;
    }

    private readonly ConcurrentDictionary<string, (int Count, DateTime Until)> _failures = new();

    /// <summary>
    /// Records a failed attempt for <paramref name="key"/> and returns true when
    /// the threshold has been reached, in which case the key is locked for
    /// <paramref name="lockout"/>. Used to throttle promo-code brute-force.
    /// </summary>
    public bool RegisterFailureAndShouldLock(string key, int threshold, TimeSpan lockout)
    {
        DateTime now = _now();
        bool locked = false;
        _failures.AddOrUpdate(
            key,
            _ =>
            {
                int initial = 1;
                bool willLock = initial >= threshold;
                if (willLock) locked = true;
                return (initial, willLock ? now + lockout : DateTime.MinValue);
            },
            (_, prev) =>
            {
                // Fresh counter once the lockout has elapsed (slow brute-force protection).
                if (prev.Until > DateTime.MinValue && now >= prev.Until)
                {
                    return (1, DateTime.MinValue);
                }
                int next = prev.Count + 1;
                if (next >= threshold)
                {
                    locked = true;
                    return (next, prev.Until == DateTime.MinValue ? now + lockout : prev.Until);
                }
                return (next, prev.Until);
            });
        return locked;
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
}
