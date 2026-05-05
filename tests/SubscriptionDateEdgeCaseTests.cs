using System;
using Jellyfin.Plugin.NoPayNoPlay.Services;
using Xunit;

namespace Jellyfin.Plugin.NoPayNoPlay.Tests;

/// <summary>Additional edge cases for <see cref="SubscriptionService.ComputeNextExpiry"/>.</summary>
public class SubscriptionDateEdgeCaseTests
{
    [Fact]
    public void NextExpiry_RollsOverYear()
    {
        var current = new DateTime(2026, 12, 10, 0, 0, 0, DateTimeKind.Utc);
        var next = SubscriptionService.ComputeNextExpiry(current, 1, 10);
        Assert.Equal(2027, next.Year);
        Assert.Equal(1, next.Month);
        Assert.Equal(10, next.Day);
    }

    [Fact]
    public void NextExpiry_TwelveMonthsAdvancesOneYear()
    {
        var current = new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc);
        var next = SubscriptionService.ComputeNextExpiry(current, 12, 5);
        Assert.Equal(2027, next.Year);
        Assert.Equal(6, next.Month);
        Assert.Equal(5, next.Day);
    }

    [Fact]
    public void NextExpiry_AnchorDay30FebNonLeap()
    {
        var current = new DateTime(2026, 1, 30, 0, 0, 0, DateTimeKind.Utc);
        var next = SubscriptionService.ComputeNextExpiry(current, 1, 30);
        Assert.Equal(2, next.Month);
        Assert.Equal(28, next.Day); // Feb 2026 max = 28
    }

    [Fact]
    public void NextExpiry_AnchorDay30FebLeapYear()
    {
        var current = new DateTime(2024, 1, 30, 0, 0, 0, DateTimeKind.Utc);
        var next = SubscriptionService.ComputeNextExpiry(current, 1, 30);
        Assert.Equal(2, next.Month);
        Assert.Equal(29, next.Day); // 2024 leap
    }

    [Fact]
    public void NextExpiry_AlwaysReturnsUtc()
    {
        var current = new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc);
        var next = SubscriptionService.ComputeNextExpiry(current, 1, 15);
        Assert.Equal(DateTimeKind.Utc, next.Kind);
    }

    [Fact]
    public void NextExpiry_EndsAtEndOfDay()
    {
        var current = new DateTime(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc);
        var next = SubscriptionService.ComputeNextExpiry(current, 1, 1);
        Assert.Equal(23, next.Hour);
        Assert.Equal(59, next.Minute);
        Assert.Equal(59, next.Second);
    }

    [Fact]
    public void NextExpiry_ClampsAnchorDay31InAprilTo30()
    {
        // Anchor day 31, target month is April (30 days).
        var current = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc);
        var next = SubscriptionService.ComputeNextExpiry(current, 1, 31);
        Assert.Equal(4, next.Month);
        Assert.Equal(30, next.Day);
    }
}
