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

    /// <summary>
    /// Regression for the backfill bug: a payment recorded on day 17 must extend
    /// to day 17 of the next month, regardless of the original signup day. Using
    /// the signup day (e.g. 12) as anchor would silently shorten the period to
    /// the 12th — 5 days earlier than expected.
    /// </summary>
    [Fact]
    public void NextExpiry_BackfillUsesPaymentDayNotSignupDay()
    {
        // Backfill scenario: payment date = 17 April 2026, anchor must be 17 (the
        // payment day itself), NOT 12 (the original signup day).
        var paymentDate = new DateTime(2026, 4, 17, 0, 0, 0, DateTimeKind.Utc);
        var next = SubscriptionService.ComputeNextExpiry(paymentDate, 1, paymentDate.Day);

        Assert.Equal(2026, next.Year);
        Assert.Equal(5, next.Month);
        Assert.Equal(17, next.Day);

        // Sanity: the buggy old behaviour (anchor = signup day 12) would have
        // landed on May 12 — 5 days too early.
        var buggy = SubscriptionService.ComputeNextExpiry(paymentDate, 1, 12);
        Assert.Equal(12, buggy.Day);
        Assert.True(next > buggy, "Fix must extend further than the buggy variant.");
    }
}
