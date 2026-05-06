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
                MigrateTransactionIds(sub);
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
    /// Backfills missing transaction IDs (older configs predate the <see cref="TransactionEntry.Id"/>
    /// field). Idempotent: only writes when at least one entry was patched.
    /// </summary>
    private static void MigrateTransactionIds(UserSubscription sub)
    {
        bool changed = false;
        foreach (var t in sub.Transactions)
        {
            if (t.Id == Guid.Empty)
            {
                t.Id = Guid.NewGuid();
                changed = true;
            }
        }

        if (changed)
        {
            Plugin.Instance!.SaveConfiguration();
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
    /// <param name="userId">User the payment is recorded for.</param>
    /// <param name="amount">Amount paid (informational, stored in the transaction).</param>
    /// <param name="method">Payment method label (PayPal, Bank, etc.).</param>
    /// <param name="monthsAdded">Number of months covered by the payment (clamped to ≥ 1).</param>
    /// <param name="note">Free-form admin note attached to the transaction.</param>
    /// <param name="recordedDate">
    /// Optional historical date for the transaction (used to backfill past payments).
    /// When null or "today/future", the expiry is extended from the current state
    /// (existing behaviour). When the date is clearly in the past (backfill mode),
    /// the new expiry becomes <c>max(currentExpiry, paymentDate + monthsAdded)</c>:
    /// historical entries that would land in the past do NOT push the expiry forward,
    /// only the most recent backfilled payment whose period reaches today actually
    /// extends remaining access. This matches admin expectations when recording months
    /// already paid in the past.
    /// </param>
    public UserSubscription ApplyPayment(Guid userId, decimal amount, string method, int monthsAdded, string note, DateTime? recordedDate = null)
    {
        lock (_lock)
        {
            UserSubscription sub = EnsureUserTracked(userId);
            int anchor = sub.SubscriptionDate.Day;
            int months = Math.Max(1, monthsAdded);
            DateTime now = DateTime.UtcNow;

            DateTime txDate = now;
            bool isBackfill = false;
            if (recordedDate.HasValue)
            {
                DateTime d = recordedDate.Value.Kind == DateTimeKind.Utc
                    ? recordedDate.Value
                    : recordedDate.Value.ToUniversalTime();
                // Never allow a transaction to be dated in the future, nor more than
                // 24 months in the past — admins occasionally pick a year by mistake
                // in the date picker; clamping prevents nonsensical history.
                DateTime earliest = now.AddMonths(-24);
                if (d > now) d = now;
                if (d < earliest) d = earliest;
                txDate = d;
                // Treat anything older than ~1 day as a historical backfill so that
                // late-of-day "today" entries still use the regular extend-from-current
                // path (no surprise truncation due to clock drift).
                isBackfill = (now - txDate).TotalDays > 1.0;
            }

            if (isBackfill)
            {
                // Use the payment day itself as anchor: a payment recorded on the
                // 17th naturally extends to the 17th of next month, not back to the
                // signup day (which would silently shorten the expiry when signupDay
                // < paymentDay).
                int backfillAnchor = txDate.Day;
                DateTime candidate = ComputeNextExpiry(txDate, months, backfillAnchor);
                if (candidate > sub.ExpiryDate)
                {
                    sub.ExpiryDate = candidate;
                }
                // else: historical entry whose covered window is already in the past
                //       — keep the current expiry untouched.
            }
            else
            {
                // Regular "current payment": extend from current expiry, or from now
                // if the subscription has already lapsed.
                DateTime baseDate = sub.ExpiryDate < now ? now : sub.ExpiryDate;
                sub.ExpiryDate = ComputeNextExpiry(baseDate, months, anchor);
            }

            sub.Transactions.Add(new TransactionEntry
            {
                Date = txDate,
                Amount = amount,
                MonthsAdded = monthsAdded,
                Method = method ?? string.Empty,
                AdminNote = note ?? string.Empty
            });
            sub.LastNotifiedState = SubscriptionState.Ok;
            sub.LastNotificationKey = string.Empty;
            // Confirming a payment naturally resolves any prior self-service claim.
            sub.HasPendingPaymentClaim = false;
            sub.PendingPaymentClaimAt = null;
            sub.PendingPaymentMethod = string.Empty;
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

    /// <summary>
    /// Recomputes a user's expiry from scratch by replaying every transaction in
    /// chronological order on top of <see cref="UserSubscription.SubscriptionDate"/>
    /// + the configured trial. Used by edit/delete to keep the expiry coherent
    /// with the persisted history.
    /// </summary>
    public DateTime RecomputeExpiry(UserSubscription sub)
    {
        DateTime now = DateTime.UtcNow;
        int signupAnchor = sub.SubscriptionDate.Day;
        DateTime expiry = sub.SubscriptionDate.AddDays(Math.Max(0, Config.TrialDays));

        var ordered = sub.Transactions.OrderBy(t => t.Date).ToList();
        foreach (var t in ordered)
        {
            int months = Math.Max(1, t.MonthsAdded);
            bool isBackfill = (now - t.Date).TotalDays > 1.0;
            if (isBackfill)
            {
                // Anchor on the transaction's own day for backfilled entries — see
                // ApplyPayment for the rationale (avoids pulling the result earlier
                // than t.Date + N months when signupDay < t.Date.Day).
                int backfillAnchor = t.Date.Day;
                DateTime candidate = ComputeNextExpiry(t.Date, months, backfillAnchor);
                if (candidate > expiry) expiry = candidate;
            }
            else
            {
                DateTime baseDate = expiry < now ? now : expiry;
                expiry = ComputeNextExpiry(baseDate, months, signupAnchor);
            }
        }

        sub.ExpiryDate = expiry;
        return expiry;
    }

    /// <summary>Updates an existing transaction in place and recomputes the expiry.</summary>
    /// <returns>True when the transaction was found and updated.</returns>
    public bool UpdateTransaction(
        Guid userId,
        Guid txId,
        decimal? amount,
        string? method,
        int? monthsAdded,
        string? note,
        DateTime? date)
    {
        lock (_lock)
        {
            UserSubscription? sub = Config.Subscriptions.FirstOrDefault(s => s.UserId == userId);
            if (sub == null) return false;
            MigrateTransactionIds(sub);
            var tx = sub.Transactions.FirstOrDefault(t => t.Id == txId);
            if (tx == null) return false;

            if (amount.HasValue) tx.Amount = Math.Max(0m, amount.Value);
            if (method != null) tx.Method = method;
            if (monthsAdded.HasValue) tx.MonthsAdded = Math.Clamp(monthsAdded.Value, 1, 60);
            if (note != null) tx.AdminNote = note;
            if (date.HasValue)
            {
                DateTime now = DateTime.UtcNow;
                DateTime d = date.Value.Kind == DateTimeKind.Utc ? date.Value : date.Value.ToUniversalTime();
                DateTime earliest = now.AddMonths(-24);
                if (d > now) d = now;
                if (d < earliest) d = earliest;
                tx.Date = d;
            }

            RecomputeExpiry(sub);
            Plugin.Instance!.SaveConfiguration();
            return true;
        }
    }

    /// <summary>Deletes a transaction and recomputes the expiry.</summary>
    /// <returns>True when the transaction was found and removed.</returns>
    public bool DeleteTransaction(Guid userId, Guid txId)
    {
        lock (_lock)
        {
            UserSubscription? sub = Config.Subscriptions.FirstOrDefault(s => s.UserId == userId);
            if (sub == null) return false;
            MigrateTransactionIds(sub);
            int removed = sub.Transactions.RemoveAll(t => t.Id == txId);
            if (removed == 0) return false;
            RecomputeExpiry(sub);
            Plugin.Instance!.SaveConfiguration();
            return true;
        }
    }

    /// <summary>
    /// Records a self-declared payment claim for a user. Does NOT extend the
    /// expiry — the admin must confirm. Returns true if the claim was accepted,
    /// false if a recent claim already exists (rate-limit collision).
    /// </summary>
    public bool MarkPaymentPending(Guid userId, string method)
    {
        lock (_lock)
        {
            UserSubscription sub = EnsureUserTracked(userId);
            sub.HasPendingPaymentClaim = true;
            sub.PendingPaymentClaimAt = DateTime.UtcNow;
            sub.PendingPaymentMethod = (method ?? string.Empty).Trim();
            if (sub.PendingPaymentMethod.Length > 50)
            {
                sub.PendingPaymentMethod = sub.PendingPaymentMethod.Substring(0, 50);
            }
            Plugin.Instance!.SaveConfiguration();
            return true;
        }
    }

    /// <summary>Clears a pending payment claim (admin reject or post-confirm).</summary>
    public void ClearPendingClaim(Guid userId)
    {
        lock (_lock)
        {
            UserSubscription? sub = Config.Subscriptions.FirstOrDefault(s => s.UserId == userId);
            if (sub == null) return;
            sub.HasPendingPaymentClaim = false;
            sub.PendingPaymentClaimAt = null;
            sub.PendingPaymentMethod = string.Empty;
            Plugin.Instance!.SaveConfiguration();
        }
    }

    /// <summary>
    /// Redeems a promo code for a user. Returns the granted month count when
    /// successful, 0 when the code is unknown, expired, exhausted, or already
    /// used by this user.
    /// </summary>
    public int RedeemPromoCode(Guid userId, string code)
    {
        lock (_lock)
        {
            string normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(normalized)) return 0;

            PromoCode? promo = Config.PromoCodes
                .FirstOrDefault(p => string.Equals(p.Code, normalized, StringComparison.OrdinalIgnoreCase));
            if (promo == null) return 0;

            DateTime now = DateTime.UtcNow;
            if (promo.ExpiresAt.HasValue && promo.ExpiresAt.Value < now) return 0;
            if (promo.MaxUses > 0 && promo.UsedCount >= promo.MaxUses) return 0;

            UserSubscription sub = EnsureUserTracked(userId);
            if (sub.RedeemedPromoCodeIds.Contains(promo.Id)) return 0;

            int months = Math.Max(1, promo.MonthsGranted);
            int anchor = sub.SubscriptionDate.Day;
            DateTime baseDate = sub.ExpiryDate < now ? now : sub.ExpiryDate;
            sub.ExpiryDate = ComputeNextExpiry(baseDate, months, anchor);
            sub.RedeemedPromoCodeIds.Add(promo.Id);
            sub.LastNotifiedState = SubscriptionState.Ok;

            sub.Transactions.Add(new TransactionEntry
            {
                Date = now,
                Amount = 0m,
                MonthsAdded = months,
                Method = "Promo:" + promo.Code,
                AdminNote = "Redeemed promo code"
            });

            promo.UsedCount++;
            Plugin.Instance!.SaveConfiguration();
            return months;
        }
    }
}
