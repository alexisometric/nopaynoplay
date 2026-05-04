using System;
using Jellyfin.Plugin.NoPayNoPlay.Configuration;
using Jellyfin.Plugin.NoPayNoPlay.Services;
using Xunit;

namespace Jellyfin.Plugin.NoPayNoPlay.Tests;

public class SubscriptionDateTests
{
    [Fact]
    public void NextExpiry_PreservesAnchorDay()
    {
        var current = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var next = SubscriptionService.ComputeNextExpiry(current, 1, 15);
        Assert.Equal(2, next.Month);
        Assert.Equal(15, next.Day);
    }

    [Fact]
    public void NextExpiry_HandlesEndOfMonth()
    {
        // anniversary day 31, but February has only 28/29 days
        var current = new DateTime(2026, 1, 31, 12, 0, 0, DateTimeKind.Utc);
        var next = SubscriptionService.ComputeNextExpiry(current, 1, 31);
        Assert.Equal(2, next.Month);
        Assert.Equal(28, next.Day); // 2026 is not a leap year
    }

    [Fact]
    public void NextExpiry_HandlesLeapYear()
    {
        var current = new DateTime(2024, 1, 31, 12, 0, 0, DateTimeKind.Utc);
        var next = SubscriptionService.ComputeNextExpiry(current, 1, 31);
        Assert.Equal(29, next.Day); // 2024 is a leap year
    }

    [Fact]
    public void NextExpiry_AddsMultipleMonths()
    {
        var current = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var next = SubscriptionService.ComputeNextExpiry(current, 3, 15);
        Assert.Equal(4, next.Month);
        Assert.Equal(15, next.Day);
    }
}
