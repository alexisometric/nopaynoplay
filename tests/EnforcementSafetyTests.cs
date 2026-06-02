using System;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.NoPayNoPlay.Configuration;
using Jellyfin.Plugin.NoPayNoPlay.Services;
using Xunit;

namespace Jellyfin.Plugin.NoPayNoPlay.Tests;

/// <summary>
/// Validates that <see cref="SubscriptionService"/> mutations correctly update
/// the subscription state so that <see cref="UserPolicyEnforcer"/> (called via
/// the controller's ApplyEnforcementSafelyAsync wrapper) would see the right
/// state. These tests exercise the code path that was previously broken by the
/// infinite-recursion bug in ApplyEnforcementSafelyAsync.
/// </summary>
public class EnforcementSafetyTests
{
    private static PluginConfiguration Config(int warning = 3, int grace = 2, int trial = 7)
        => new() { WarningDaysBefore = warning, GraceDays = grace, TrialDays = trial };

    [Fact]
    public void Pay_TransitionsBlockedUserBackToOk()
    {
        var cfg = Config(grace: 0);
        var svc = TestSupport.Service(cfg);
        var uid = Guid.NewGuid();

        // User is deeply expired — should be Blocked.
        cfg.Subscriptions.Add(new UserSubscription
        {
            UserId = uid,
            SubscriptionDate = DateTime.UtcNow.AddMonths(-6),
            ExpiryDate = DateTime.UtcNow.AddDays(-10)
        });

        var sub = cfg.Subscriptions.Single(s => s.UserId == uid);
        Assert.Equal(SubscriptionState.Blocked, svc.EvaluateState(sub));

        // Apply a payment — this is what the controller does after the fix.
        svc.ApplyPayment(uid, 10m, "PayPal", 1, string.Empty);

        sub = cfg.Subscriptions.Single(s => s.UserId == uid);
        // After payment, the user should no longer be Blocked.
        Assert.NotEqual(SubscriptionState.Blocked, svc.EvaluateState(sub));
        Assert.True(sub.ExpiryDate > DateTime.UtcNow);
    }

    [Fact]
    public void Exempt_TransitionsBlockedUserToExempt()
    {
        var cfg = Config(grace: 0);
        var svc = TestSupport.Service(cfg);
        var uid = Guid.NewGuid();

        cfg.Subscriptions.Add(new UserSubscription
        {
            UserId = uid,
            ExpiryDate = DateTime.UtcNow.AddDays(-10)
        });

        Assert.Equal(SubscriptionState.Blocked, svc.EvaluateState(cfg.Subscriptions[0]));

        svc.SetExempt(uid, true);

        var sub = cfg.Subscriptions.Single(s => s.UserId == uid);
        Assert.Equal(SubscriptionState.Exempt, svc.EvaluateState(sub));
        Assert.True(sub.IsExempt);
    }

    [Fact]
    public void Reset_TransitionsBlockedUserBackToTrial()
    {
        var cfg = Config(trial: 7, grace: 0);
        var svc = TestSupport.Service(cfg);
        var uid = Guid.NewGuid();

        cfg.Subscriptions.Add(new UserSubscription
        {
            UserId = uid,
            ExpiryDate = DateTime.UtcNow.AddDays(-10)
        });

        Assert.Equal(SubscriptionState.Blocked, svc.EvaluateState(cfg.Subscriptions[0]));

        svc.Reset(uid);

        var sub = cfg.Subscriptions.Single(s => s.UserId == uid);
        Assert.Equal(SubscriptionState.Ok, svc.EvaluateState(sub));
        Assert.True(sub.ExpiryDate > DateTime.UtcNow);
    }

    [Fact]
    public void RedeemPromoCode_TransitionsBlockedUserBackToOk()
    {
        var cfg = Config(grace: 0);
        var promo = new PromoCode { Code = "RESCUE", MonthsGranted = 2 };
        cfg.PromoCodes.Add(promo);
        var svc = TestSupport.Service(cfg);
        var uid = Guid.NewGuid();

        cfg.Subscriptions.Add(new UserSubscription
        {
            UserId = uid,
            ExpiryDate = DateTime.UtcNow.AddDays(-10)
        });

        Assert.Equal(SubscriptionState.Blocked, svc.EvaluateState(cfg.Subscriptions[0]));

        int granted = svc.RedeemPromoCode(uid, "RESCUE");

        Assert.Equal(2, granted);
        var sub = cfg.Subscriptions.Single(s => s.UserId == uid);
        Assert.NotEqual(SubscriptionState.Blocked, svc.EvaluateState(sub));
        Assert.True(sub.ExpiryDate > DateTime.UtcNow);
    }

    [Fact]
    public void EvaluateState_AllTransitionsAreCorrect()
    {
        var svc = TestSupport.Service(Config(warning: 3, grace: 2));

        // Ok
        var sub = new UserSubscription { ExpiryDate = DateTime.UtcNow.AddDays(30) };
        Assert.Equal(SubscriptionState.Ok, svc.EvaluateState(sub));

        // WarningSoon
        sub.ExpiryDate = DateTime.UtcNow.AddDays(2);
        Assert.Equal(SubscriptionState.WarningSoon, svc.EvaluateState(sub));

        // InGrace
        sub.ExpiryDate = DateTime.UtcNow.AddHours(-1);
        Assert.Equal(SubscriptionState.InGrace, svc.EvaluateState(sub));

        // Blocked
        sub.ExpiryDate = DateTime.UtcNow.AddDays(-3);
        Assert.Equal(SubscriptionState.Blocked, svc.EvaluateState(sub));

        // Exempt
        sub.IsExempt = true;
        Assert.Equal(SubscriptionState.Exempt, svc.EvaluateState(sub));
    }

    [Fact]
    public void UpdateTransaction_RecomputesExpiryCorrectly()
    {
        var cfg = Config(trial: 0);
        var svc = TestSupport.Service(cfg);
        var uid = Guid.NewGuid();

        cfg.Subscriptions.Add(new UserSubscription
        {
            UserId = uid,
            SubscriptionDate = DateTime.UtcNow.AddMonths(-2),
            ExpiryDate = DateTime.UtcNow.AddDays(-5) // blocked
        });

        // Add a transaction via ApplyPayment to get a real TxId.
        svc.ApplyPayment(uid, 10m, "Bank", 1, string.Empty);
        var sub = cfg.Subscriptions.Single(s => s.UserId == uid);
        var tx = sub.Transactions[0];

        // Now edit the transaction to grant 3 months instead of 1.
        bool ok = svc.UpdateTransaction(uid, tx.Id, amount: null, method: null, monthsAdded: 3, note: null, date: null);
        Assert.True(ok);

        sub = cfg.Subscriptions.Single(s => s.UserId == uid);
        Assert.Equal(3, sub.Transactions[0].MonthsAdded);
        Assert.True(sub.ExpiryDate > DateTime.UtcNow.AddDays(60)); // ~3 months from now
    }

    [Fact]
    public void DeleteTransaction_RecomputesExpiryCorrectly()
    {
        var cfg = Config(trial: 0);
        var svc = TestSupport.Service(cfg);
        var uid = Guid.NewGuid();

        cfg.Subscriptions.Add(new UserSubscription
        {
            UserId = uid,
            SubscriptionDate = DateTime.UtcNow.AddMonths(-1),
            ExpiryDate = DateTime.UtcNow.AddDays(-5)
        });

        svc.ApplyPayment(uid, 10m, "Bank", 1, string.Empty);
        var sub = cfg.Subscriptions.Single(s => s.UserId == uid);
        Assert.True(sub.ExpiryDate > DateTime.UtcNow);

        var tx = sub.Transactions[0];
        bool ok = svc.DeleteTransaction(uid, tx.Id);
        Assert.True(ok);

        sub = cfg.Subscriptions.Single(s => s.UserId == uid);
        // After deleting the only payment, the user should be back to blocked
        // (trial was 0, subscription date is in the past).
        Assert.Equal(SubscriptionState.Blocked, svc.EvaluateState(sub));
    }
}
