using System;
using System.Linq;
using Jellyfin.Plugin.NoPayNoPlay.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.NoPayNoPlay.Services;

/// <summary>
/// Business logic around per-user subscriptions.
/// </summary>
public class SubscriptionService
{
    private readonly IUserManager _userManager;
    private readonly ILogger<SubscriptionService> _logger;
    private static readonly object _lock = new();

    public SubscriptionService(IUserManager userManager, ILogger<SubscriptionService> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    private static PluginConfiguration Config => Plugin.Instance!.Configuration;

    /// <summary>
    /// Returns or creates the subscription for a user (granting a free trial on first call).
    /// </summary>
    public UserSubscription EnsureUserTracked(Guid userId)
    {
        lock (_lock)
        {
            UserSubscription? sub = Config.Subscriptions.FirstOrDefault(s => s.UserId == userId);
            if (sub != null)
            {
                return sub;
            }

            DateTime now = DateTime.UtcNow;
            sub = new UserSubscription
            {
                UserId = userId,
                SubscriptionDate = now,
                ExpiryDate = now.AddDays(Math.Max(0, Config.TrialDays)),
                IsExempt = false,
                IsBlocked = false,
                LastNotifiedState = SubscriptionState.Ok
            };
            Config.Subscriptions.Add(sub);
            Plugin.Instance!.SaveConfiguration();
            _logger.LogInformation(
                "NoPayNoPlay: started tracking new user {UserId}, trial expires {Expiry:o}",
                userId,
                sub.ExpiryDate);
            return sub;
        }
    }

    /// <summary>
    /// Computes the next expiry date while preserving the anchor day (handles end-of-month).
    /// </summary>
    public static DateTime ComputeNextExpiry(DateTime current, int monthsToAdd, int anchorDay)
    {
        DateTime target = current.AddMonths(monthsToAdd);
        int day = Math.Min(anchorDay, DateTime.DaysInMonth(target.Year, target.Month));
        return new DateTime(target.Year, target.Month, day, 23, 59, 59, DateTimeKind.Utc);
    }

    /// <summary>
    /// Evaluates the current state of a subscription.
    /// </summary>
    public SubscriptionState EvaluateState(UserSubscription sub)
    {
        if (sub.IsExempt)
        {
            return SubscriptionState.Exempt;
        }

        DateTime now = DateTime.UtcNow;
        TimeSpan delta = sub.ExpiryDate - now;

        if (delta.TotalDays > Config.WarningDaysBefore)
        {
            return SubscriptionState.Ok;
        }

        if (delta.TotalSeconds > 0)
        {
            return SubscriptionState.WarningSoon;
        }

        if (delta.TotalDays > -Config.GraceDays)
        {
            return SubscriptionState.InGrace;
        }

        return SubscriptionState.Blocked;
    }

    /// <summary>
    /// Records a payment and extends the expiry date.
    /// </summary>
    public UserSubscription ApplyPayment(Guid userId, decimal amount, string method, int monthsAdded, string note)
    {
        lock (_lock)
        {
            UserSubscription sub = EnsureUserTracked(userId);
            int anchor = sub.SubscriptionDate.Day;
            // If the expiry is in the past, restart from now; otherwise extend the current expiry.
            DateTime baseDate = sub.ExpiryDate < DateTime.UtcNow ? DateTime.UtcNow : sub.ExpiryDate;
            sub.ExpiryDate = ComputeNextExpiry(baseDate, Math.Max(1, monthsAdded), anchor);
            sub.Transactions.Add(new TransactionEntry
            {
                Date = DateTime.UtcNow,
                Amount = amount,
                MonthsAdded = monthsAdded,
                Method = method ?? string.Empty,
                AdminNote = note ?? string.Empty
            });
            sub.LastNotifiedState = SubscriptionState.Ok;
            Plugin.Instance!.SaveConfiguration();
            return sub;
        }
    }

    /// <summary>Toggles the exemption flag.</summary>
    public UserSubscription SetExempt(Guid userId, bool isExempt)
    {
        lock (_lock)
        {
            UserSubscription sub = EnsureUserTracked(userId);
            sub.IsExempt = isExempt;
            Plugin.Instance!.SaveConfiguration();
            return sub;
        }
    }

    /// <summary>Resets a user back to a fresh trial.</summary>
    public UserSubscription Reset(Guid userId)
    {
        lock (_lock)
        {
            UserSubscription sub = EnsureUserTracked(userId);
            DateTime now = DateTime.UtcNow;
            sub.SubscriptionDate = now;
            sub.ExpiryDate = now.AddDays(Math.Max(0, Config.TrialDays));
            sub.LastNotifiedState = SubscriptionState.Ok;
            Plugin.Instance!.SaveConfiguration();
            return sub;
        }
    }

    /// <summary>Persists current changes after direct mutation.</summary>
    public void Save()
    {
        lock (_lock)
        {
            Plugin.Instance!.SaveConfiguration();
        }
    }
}
