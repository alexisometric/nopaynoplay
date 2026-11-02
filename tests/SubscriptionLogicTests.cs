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
    public void ApplyPayment_PreservesExpiryAnchorDay()
    {
        // Regression for the "pay on the 5th, lose 15 days" bug: while the current
        // period is still active, a payment must extend from the existing expiry and
        // keep its anchor day. Anchoring on now.Day used to move a period ending on
        // the 10th to the 5th of the next month, silently shortening access.
        var cfg = Config(trial: 7);
        var svc = TestSupport.Service(cfg);
        var uid = Guid.NewGuid();

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
        Assert.Equal(SubscriptionService.ComputeNextExpiry(future, 1, 10), sub.ExpiryDate);
        Assert.True(sub.ExpiryDate > future);
        Assert.NotEqual(25, sub.ExpiryDate.Day); // never the signup day
    }

    [Fact]
    public void ApplyPayment_NeverShortensAnActivePeriod()
    {
        var cfg = Config(trial: 7);
        var svc = TestSupport.Service(cfg);
        var uid = Guid.NewGuid();
        DateTime now = DateTime.UtcNow;
        DateTime future = new DateTime(now.Year, now.Month, 1, 23, 59, 59, DateTimeKind.Utc)
            .AddMonths(1).AddDays(19); // day 20, still active
        cfg.Subscriptions.Add(new UserSubscription { UserId = uid, ExpiryDate = future });

        svc.ApplyPayment(uid, 10m, "PayPal", 1, string.Empty);

        var sub = cfg.Subscriptions.Single(s => s.UserId == uid);
        Assert.True(sub.ExpiryDate > future, "a payment must never move the expiry backwards");
        Assert.Equal(20, sub.ExpiryDate.Day);
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
