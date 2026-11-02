using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.NoPayNoPlay.Configuration;
using Jellyfin.Plugin.NoPayNoPlay.Services;
using Xunit;

namespace Jellyfin.Plugin.NoPayNoPlay.Tests;

/// <summary>
/// Regressions for the subscription service: a backfilled payment is replayed by
/// <c>RecomputeExpiry</c> exactly like it was applied, purges are audited, and no
/// string helper can produce invalid UTF-16.
/// </summary>
public class SubscriptionRegressionTests
{
    private static PluginConfiguration Config() => new() { WarningDaysBefore = 3, GraceDays = 2, TrialDays = 7 };

    [Fact]
    public void Backfill_IsReplayedIdenticallyByRecomputeExpiry()
    {
        var cfg = Config();
        var svc = TestSupport.Service(cfg);
        var uid = Guid.NewGuid();
        cfg.Subscriptions.Add(new UserSubscription { UserId = uid, SubscriptionDate = DateTime.UtcNow.AddMonths(-2) });

        DateTime paymentDate = DateTime.UtcNow.AddDays(-45);
        UserSubscription sub = svc.ApplyPayment(uid, 30m, "PayPal", 3, string.Empty, paymentDate);
        DateTime applied = sub.ExpiryDate;

        svc.RecomputeExpiry(sub);

        Assert.Equal(applied, sub.ExpiryDate);
        Assert.True(sub.Transactions[0].IsBackfill);
    }

    [Fact]
    public void RecomputeExpiry_IgnoresUnboundedMonthsAddedValues()
    {
        var cfg = Config();
        var svc = TestSupport.Service(cfg);
        var uid = Guid.NewGuid();
        var sub = new UserSubscription
        {
            UserId = uid,
            SubscriptionDate = DateTime.UtcNow.AddMonths(-1),
            ExpiryDate = DateTime.UtcNow.AddMonths(1),
            Transactions = new List<TransactionEntry>
            {
                new() { Id = Guid.NewGuid(), Date = DateTime.UtcNow.AddDays(-10), Amount = 10m, MonthsAdded = 9999 }
            }
        };
        cfg.Subscriptions.Add(sub);

        svc.RecomputeExpiry(sub);

        Assert.True(sub.ExpiryDate <= DateTime.UtcNow.AddYears(6));
    }

    [Fact]
    public void PurgeOrphanedSubscriptions_IsAudited()
    {
        var cfg = Config();
        var svc = TestSupport.Service(cfg);
        var gone = Guid.NewGuid();
        cfg.Subscriptions.Add(new UserSubscription { UserId = gone, ExpiryDate = DateTime.UtcNow });

        svc.PurgeOrphanedSubscriptions(new List<Guid> { Guid.NewGuid() });

        Assert.Empty(cfg.Subscriptions);
        Assert.Contains(cfg.AuditLog, e => e.Action == "subscription.purge");
    }

    [Fact]
    public void SafeTruncate_DoesNotSplitSurrogatePairs()
    {
        string value = "abc\uD83D\uDE00def"; // 😀 is a surrogate pair

        string truncated = SubscriptionService.SafeTruncate(value, 4);

        Assert.Equal("abc", truncated);
        Assert.DoesNotContain('\uD83D', truncated);
    }

    [Fact]
    public void Normalize_ClampsOutOfRangeSettings()
    {
        var cfg = new PluginConfiguration
        {
            MonthlyPrice = -5m,
            GraceDays = 999,
            TrialDays = -3,
            WarningDaysBefore = 400,
            Currency = "eur",
            AuditLogMaxEntries = 1
        };

        cfg.Normalize();

        Assert.Equal(0m, cfg.MonthlyPrice);
        Assert.Equal(365, cfg.GraceDays);
        Assert.Equal(0, cfg.TrialDays);
        Assert.Equal(90, cfg.WarningDaysBefore);
        Assert.Equal("EUR", cfg.Currency);
        Assert.Equal(50, cfg.AuditLogMaxEntries);
        Assert.Equal(PluginConfiguration.CurrentConfigVersion, cfg.ConfigVersion);
    }
}
