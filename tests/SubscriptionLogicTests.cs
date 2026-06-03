using System;
using System.Linq;
using Jellyfin.Plugin.NoPayNoPlay.Configuration;
using Jellyfin.Plugin.NoPayNoPlay.Services;
using Xunit;

namespace Jellyfin.Plugin.NoPayNoPlay.Tests;

/// <summary>
/// Covers the subscription state machine boundaries and the trial -> first-payment
/// expiry computation (the over-grant regression).
/// </summary>
public class SubscriptionLogicTests
{
    private static PluginConfiguration Config(int warning = 3, int grace = 2, int trial = 7)
        => new PluginConfiguration { WarningDaysBefore = warning, GraceDays = grace, TrialDays = trial };

    [Fact]
    public void EvaluateState_Exempt()
    {
        var svc = TestSupport.Service(Config());
        var sub = new UserSubscription { IsExempt = true, ExpiryDate = DateTime.UtcNow.AddYears(-1) };
        Assert.Equal(SubscriptionState.Exempt, svc.EvaluateState(sub));
    }

    [Fact]
    public void EvaluateState_OkWhenFarFromExpiry()
    {
        var svc = TestSupport.Service(Config());
        var sub = new UserSubscription { ExpiryDate = DateTime.UtcNow.AddDays(30) };
        Assert.Equal(SubscriptionState.Ok, svc.EvaluateState(sub));
    }

    [Fact]
    public void EvaluateState_WarningWithinWindow()
    {
        var svc = TestSupport.Service(Config(warning: 3));
        var sub = new UserSubscription { ExpiryDate = DateTime.UtcNow.AddDays(2) };
        Assert.Equal(SubscriptionState.WarningSoon, svc.EvaluateState(sub));
    }

    [Fact]
    public void EvaluateState_InGraceJustAfterExpiry()
    {
        var svc = TestSupport.Service(Config(grace: 2));
        var sub = new UserSubscription { ExpiryDate = DateTime.UtcNow.AddHours(-1) };
        Assert.Equal(SubscriptionState.InGrace, svc.EvaluateState(sub));
    }

    [Fact]
    public void EvaluateState_BlockedAfterGrace()
    {
        var svc = TestSupport.Service(Config(grace: 2));
        var sub = new UserSubscription { ExpiryDate = DateTime.UtcNow.AddDays(-3) };
        Assert.Equal(SubscriptionState.Blocked, svc.EvaluateState(sub));
    }

    [Fact]
    public void ApplyPayment_AnchorsOnPaymentDay()
    {
        // Regression for the trial -> first-payment over-grant: the signup day (25)
        // differs from the trial-end day. The renewed expiry must anchor on the
        // payment date's day (now.Day) — NOT the signup day, and NOT the expiry day.
        var cfg = Config(trial: 7);
        var svc = TestSupport.Service(cfg);
        var uid = Guid.NewGuid();

        // Future expiry on day 10 of a month two months out: definitely after "now",
        // and day 10 always exists so no end-of-month clamping noise.
        DateTime now = DateTime.UtcNow;
        DateTime future = new DateTime(now.Year, now.Month, 1, 23, 59, 59, DateTimeKind.Utc)
            .AddMonths(2).AddDays(9); // day 10
        cfg.Subscriptions.Add(new UserSubscription
        {
            UserId = uid,
            SubscriptionDate = new DateTime(2026, 1, 25, 12, 0, 0, DateTimeKind.Utc),
            ExpiryDate = future
        });

        svc.ApplyPayment(uid, 10m, "PayPal", 1, string.Empty);

        var sub = cfg.Subscriptions.Single(s => s.UserId == uid);
        // Anchored on the payment date's day (now.Day), NOT the expiry day (10)
        // and NOT the signup day (25).
        Assert.Equal(now.Day, sub.ExpiryDate.Day);
        Assert.Equal(SubscriptionService.ComputeNextExpiry(future, 1, now.Day), sub.ExpiryDate);
    }

    [Fact]
    public void ApplyPayment_LapsedSubscriptionExtendsFromNow()
    {
        var cfg = Config(trial: 7);
        var svc = TestSupport.Service(cfg);
        var uid = Guid.NewGuid();
        cfg.Subscriptions.Add(new UserSubscription
        {
            UserId = uid,
            SubscriptionDate = DateTime.UtcNow.AddMonths(-6),
            ExpiryDate = DateTime.UtcNow.AddMonths(-2) // already lapsed
        });

        svc.ApplyPayment(uid, 10m, "PayPal", 1, string.Empty);

        var sub = cfg.Subscriptions.Single(s => s.UserId == uid);
        // ~1 month of access from now, not retroactively from the old lapsed date.
        Assert.True(sub.ExpiryDate > DateTime.UtcNow.AddDays(25));
        Assert.True(sub.ExpiryDate < DateTime.UtcNow.AddDays(40));
    }

    [Fact]
    public void PurgeOrphanedSubscriptions_RemovesUnknownUsersButKeepsLive()
    {
        var cfg = Config();
        var svc = TestSupport.Service(cfg);
        var live = Guid.NewGuid();
        var orphan = Guid.NewGuid();
        cfg.Subscriptions.Add(new UserSubscription { UserId = live });
        cfg.Subscriptions.Add(new UserSubscription { UserId = orphan });

        int removed = svc.PurgeOrphanedSubscriptions(new[] { live });

        Assert.Equal(1, removed);
        Assert.Single(cfg.Subscriptions);
        Assert.Equal(live, cfg.Subscriptions[0].UserId);
    }

    [Fact]
    public void PurgeOrphanedSubscriptions_EmptyLiveSetIsNoOp()
    {
        var cfg = Config();
        var svc = TestSupport.Service(cfg);
        cfg.Subscriptions.Add(new UserSubscription { UserId = Guid.NewGuid() });

        int removed = svc.PurgeOrphanedSubscriptions(Array.Empty<Guid>());

        Assert.Equal(0, removed);
        Assert.Single(cfg.Subscriptions);
    }
}
