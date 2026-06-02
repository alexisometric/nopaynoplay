using System;
using System.Linq;
using Jellyfin.Plugin.NoPayNoPlay.Configuration;
using Jellyfin.Plugin.NoPayNoPlay.Services;
using Xunit;

namespace Jellyfin.Plugin.NoPayNoPlay.Tests;

/// <summary>
/// Exercises <see cref="SubscriptionService.RedeemPromoCode"/> — the money-granting
/// logic that previously had no test coverage (expiry, exhaustion, anti-replay).
/// </summary>
public class PromoCodeRedemptionTests
{
    private static (SubscriptionService Svc, PluginConfiguration Cfg, PromoCode Promo) Setup(
        int months = 1, int maxUses = 0, DateTime? expiresAt = null)
    {
        var cfg = new PluginConfiguration();
        var promo = new PromoCode
        {
            Code = "SUMMER",
            MonthsGranted = months,
            MaxUses = maxUses,
            ExpiresAt = expiresAt
        };
        cfg.PromoCodes.Add(promo);
        return (TestSupport.Service(cfg), cfg, promo);
    }

    [Fact]
    public void Redeem_ValidCode_GrantsMonthsAndRecords()
    {
        var (svc, cfg, promo) = Setup(months: 2);
        var uid = Guid.NewGuid();

        int granted = svc.RedeemPromoCode(uid, "SUMMER");

        Assert.Equal(2, granted);
        Assert.Equal(1, promo.UsedCount);
        var sub = cfg.Subscriptions.Single(s => s.UserId == uid);
        Assert.Contains(promo.Id, sub.RedeemedPromoCodeIds);
        Assert.Single(sub.Transactions);
    }

    [Fact]
    public void Redeem_IsCaseInsensitiveAndTrimmed()
    {
        var (svc, _, _) = Setup();
        Assert.Equal(1, svc.RedeemPromoCode(Guid.NewGuid(), "  summer "));
    }

    [Fact]
    public void Redeem_SameUserTwice_SecondReturnsZero()
    {
        var (svc, _, promo) = Setup();
        var uid = Guid.NewGuid();

        Assert.True(svc.RedeemPromoCode(uid, "SUMMER") > 0);
        Assert.Equal(0, svc.RedeemPromoCode(uid, "SUMMER")); // anti-replay
        Assert.Equal(1, promo.UsedCount);
    }

    [Fact]
    public void Redeem_UnknownCode_ReturnsZero()
    {
        var (svc, _, _) = Setup();
        Assert.Equal(0, svc.RedeemPromoCode(Guid.NewGuid(), "NOPE42"));
    }

    [Fact]
    public void Redeem_ExpiredCode_ReturnsZero()
    {
        var (svc, _, _) = Setup(expiresAt: DateTime.UtcNow.AddDays(-1));
        Assert.Equal(0, svc.RedeemPromoCode(Guid.NewGuid(), "SUMMER"));
    }

    [Fact]
    public void Redeem_MaxUsesAlreadyReached_ReturnsZero()
    {
        var (svc, _, promo) = Setup(maxUses: 1);
        promo.UsedCount = 1;
        Assert.Equal(0, svc.RedeemPromoCode(Guid.NewGuid(), "SUMMER"));
    }

    [Fact]
    public void Redeem_MaxUses_AllowsUpToLimitAcrossDistinctUsers()
    {
        var (svc, _, promo) = Setup(maxUses: 2);
        Assert.True(svc.RedeemPromoCode(Guid.NewGuid(), "SUMMER") > 0);
        Assert.True(svc.RedeemPromoCode(Guid.NewGuid(), "SUMMER") > 0);
        Assert.Equal(0, svc.RedeemPromoCode(Guid.NewGuid(), "SUMMER")); // limit hit
        Assert.Equal(2, promo.UsedCount);
    }
}
