using System;
using Jellyfin.Plugin.NoPayNoPlay.Services;
using Xunit;

namespace Jellyfin.Plugin.NoPayNoPlay.Tests;

/// <summary>
/// Deterministic tests for <see cref="RateLimiter"/> using an injected clock so no
/// real time has to pass.
/// </summary>
public class RateLimiterTests
{
    [Fact]
    public void TryAcquire_AllowsFirstThenBlocksWithinWindow()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var rl = new RateLimiter(() => now);
        var window = TimeSpan.FromMinutes(30);

        Assert.True(rl.TryAcquire("k", window));   // first call always allowed
        Assert.False(rl.TryAcquire("k", window));  // within the window -> blocked

        now = now.AddMinutes(31);
        Assert.True(rl.TryAcquire("k", window));    // window elapsed -> allowed again
    }

    [Fact]
    public void RegisterFailure_LocksExactlyAtThreshold()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var rl = new RateLimiter(() => now);
        var lockout = TimeSpan.FromMinutes(15);

        Assert.False(rl.RegisterFailureAndShouldLock("k", 5, lockout)); // 1
        Assert.False(rl.RegisterFailureAndShouldLock("k", 5, lockout)); // 2
        Assert.False(rl.RegisterFailureAndShouldLock("k", 5, lockout)); // 3
        Assert.False(rl.RegisterFailureAndShouldLock("k", 5, lockout)); // 4
        Assert.True(rl.RegisterFailureAndShouldLock("k", 5, lockout));  // 5 -> lock
        Assert.True(rl.IsLocked("k"));
    }

    [Fact]
    public void IsLocked_ClearsAndCounterResetsAfterLockoutElapses()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var rl = new RateLimiter(() => now);
        var lockout = TimeSpan.FromMinutes(15);
        for (int i = 0; i < 5; i++)
        {
            rl.RegisterFailureAndShouldLock("k", 5, lockout);
        }

        Assert.True(rl.IsLocked("k"));

        now = now.AddMinutes(16);
        Assert.False(rl.IsLocked("k"));
        // The counter restarts at 1 after the lockout window, so a single new failure
        // must not immediately re-lock.
        Assert.False(rl.RegisterFailureAndShouldLock("k", 5, lockout));
    }

    [Fact]
    public void ClearFailures_Unlocks()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var rl = new RateLimiter(() => now);
        for (int i = 0; i < 5; i++)
        {
            rl.RegisterFailureAndShouldLock("k", 5, TimeSpan.FromMinutes(15));
        }

        Assert.True(rl.IsLocked("k"));
        rl.ClearFailures("k");
        Assert.False(rl.IsLocked("k"));
    }
}
