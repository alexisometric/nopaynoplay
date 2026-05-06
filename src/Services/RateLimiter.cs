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
    private readonly ConcurrentDictionary<string, DateTime> _last = new();

    /// <summary>
    /// Returns true if the action is allowed for <paramref name="key"/>, and
    /// records the new timestamp. Returns false (without updating) when the
    /// previous call happened less than <paramref name="window"/> ago.
    /// </summary>
    public bool TryAcquire(string key, TimeSpan window)
    {
        DateTime now = DateTime.UtcNow;
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
        TimeSpan elapsed = DateTime.UtcNow - last;
        return elapsed >= window ? TimeSpan.Zero : window - elapsed;
    }
}
