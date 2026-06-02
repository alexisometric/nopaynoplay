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
    private readonly Func<PluginConfiguration> _configAccessor;
    private readonly Action _save;
    private static readonly object _lock = new();

    public SubscriptionService(IUserManager userManager, ILogger<SubscriptionService> logger)
        : this(userManager, logger, null, null)
    {
    }

    /// <summary>
    /// Test-friendly constructor: lets callers inject the configuration accessor and
    /// the persistence callback so the business logic can be exercised without the
    /// <see cref="Plugin"/> singleton. The public constructor binds both to
    /// <see cref="Plugin.Instance"/>.
    /// </summary>
    internal SubscriptionService(
        IUserManager userManager,
        ILogger<SubscriptionService> logger,
        Func<PluginConfiguration>? configAccessor,
        Action? save)
    {
        _userManager = userManager;
        _logger = logger;
        _configAccessor = configAccessor ?? (() => Plugin.Instance!.Configuration);
        _save = save ?? (() => Plugin.Instance!.SaveConfiguration());
    }

    private PluginConfiguration Config => _configAccessor();

    /// <summary>
    /// Runs <paramref name="mutator"/> under the same lock that guards every
    /// subscription mutation and persists when it returns <c>true</c>. Lets the
    /// controller mutate shared config lists (promo codes, tiers, tags, settings)
    /// without racing the scheduled task or the self-service endpoints, which would
    /// otherwise corrupt the XML or lose updates.
    /// </summary>
    public void MutateConfig(Func<PluginConfiguration, bool> mutator)
    {
        lock (_lock)
        {
            if (mutator(Config))
            {
                _save();
            }
        }
    }

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
            _save();
            _logger.LogInformation(
                "NoPayNoPlay: started tracking new user {UserId}, trial expires {Expiry:o}",
                userId,
                sub.ExpiryDate);
            return sub;
        }
    }

    /// <summary>
    /// Backfills missing transaction IDs (older configs predate the <see cref="TransactionEntry.Id"/>
    /// field) <b>in memory only</b> — never triggers a save, so it cannot turn a
    /// read path (GET /Me, GET /Users) into a disk write. Persistence happens once
    /// at startup (see <see cref="Plugin"/> seeding) and on the next real mutation.
    /// Returns <c>true</c> when at least one id was patched.
    /// </summary>
    internal static bool MigrateTransactionIds(UserSubscription sub)
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

        return changed;
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
                // if the subscription has already lapsed. Anchor on baseDate's own
                // day — NOT the signup day — so the trial -> first-payment transition
                // neither over-grants (signup day later in the month than the trial
                // end, which used to add up to ~3 free weeks) nor truncates (signup
                // day earlier, which used to shave off up to ~1 paid week).
                DateTime baseDate = sub.ExpiryDate < now ? now : sub.ExpiryDate;
                sub.ExpiryDate = ComputeNextExpiry(baseDate, months, baseDate.Day);
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
            _save();
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
            _save();
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
            sub.LastNotificationKey = string.Empty;
            _save();
            return sub;
        }
    }

    /// <summary>Persists current changes after direct mutation.</summary>
    public void Save()
    {
        lock (_lock)
        {
            _save();
        }
    }

    /// <summary>
    /// Removes subscription records whose Jellyfin user no longer exists, so a
    /// deleted account doesn't leave its payment history, admin notes and policy
    /// snapshot orphaned in the config forever. No-op (defensive) when the live
    /// user set is empty, to avoid wiping everything on a transient lookup failure.
    /// </summary>
    /// <returns>The number of orphaned records removed.</returns>
    public int PurgeOrphanedSubscriptions(IReadOnlyCollection<Guid> liveUserIds)
    {
        if (liveUserIds == null || liveUserIds.Count == 0)
        {
            return 0;
        }

        lock (_lock)
        {
            var live = liveUserIds as HashSet<Guid> ?? new HashSet<Guid>(liveUserIds);
            int removed = Config.Subscriptions.RemoveAll(s => !live.Contains(s.UserId));
            if (removed > 0)
            {
                _save();
                _logger.LogInformation("NoPayNoPlay: purged {Count} orphaned subscription record(s)", removed);
            }

            return removed;
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
                // Anchor on baseDate's day (mirrors ApplyPayment) so replaying the
                // history after an edit/delete reproduces the same expiry the live
                // path produced — no drift between the two.
                DateTime baseDate = expiry < now ? now : expiry;
                expiry = ComputeNextExpiry(baseDate, months, baseDate.Day);
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
            _save();
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
            _save();
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
            _save();
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
            _save();
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
            DateTime baseDate = sub.ExpiryDate < now ? now : sub.ExpiryDate;
            sub.ExpiryDate = ComputeNextExpiry(baseDate, months, baseDate.Day);
            sub.RedeemedPromoCodeIds.Add(promo.Id);
            sub.LastNotifiedState = SubscriptionState.Ok;
            // Re-arm the per-milestone notification dedup so the next J-3/J-1/J0
            // reminder fires for the renewed cycle.
            sub.LastNotificationKey = string.Empty;

            sub.Transactions.Add(new TransactionEntry
            {
                Date = now,
                Amount = 0m,
                MonthsAdded = months,
                Method = "Promo:" + promo.Code,
                AdminNote = "Redeemed promo code"
            });

            promo.UsedCount++;
            _save();
            return months;
        }
    }

    /// <summary>
    /// Returns the effective monthly price for a user, taking the optional tag
    /// override into account. Falls back to the global price when the user has
    /// no tag or the tag does not override the price.
    /// </summary>
    public decimal GetEffectiveMonthlyPrice(UserSubscription sub)
    {
        if (sub != null && !string.IsNullOrEmpty(sub.Tag))
        {
            var tag = Config.Tags.FirstOrDefault(t =>
                string.Equals(t.Key, sub.Tag, StringComparison.OrdinalIgnoreCase));
            if (tag != null && tag.MonthlyPriceOverride > 0m)
            {
                return tag.MonthlyPriceOverride;
            }
        }
        return Config.MonthlyPrice;
    }

    /// <summary>
    /// Returns how many full months of subscription the user is currently behind on,
    /// computed from the gap between today and their last covered period
    /// (subscription anchor + cumulated paid months). Returns 0 when up to date.
    /// </summary>
    public int GetArrearsMonths(UserSubscription sub)
    {
        if (sub == null || sub.IsExempt) return 0;
        DateTime now = DateTime.UtcNow;
        if (sub.ExpiryDate >= now) return 0;
        TimeSpan late = now - sub.ExpiryDate;
        // Use ~30.44 average days/month so a 31-day overdue still counts as 1.
        int months = (int)Math.Floor(late.TotalDays / 30.4375);
        return Math.Max(0, months);
    }

    /// <summary>Sets the tag on a user subscription. Empty string clears it.</summary>
    public UserSubscription SetTag(Guid userId, string tagKey)
    {
        lock (_lock)
        {
            UserSubscription sub = EnsureUserTracked(userId);
            string normalized = (tagKey ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized.Length > 32) normalized = normalized.Substring(0, 32);
            // Reject unknown tags silently (resets to empty).
            if (!string.IsNullOrEmpty(normalized)
                && !Config.Tags.Any(t => string.Equals(t.Key, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                normalized = string.Empty;
            }
            sub.Tag = normalized;
            _save();
            return sub;
        }
    }

    /// <summary>
    /// Appends an entry to the in-memory audit log, evicting the oldest entries
    /// once the configured cap is reached.
    /// </summary>
    public void Audit(string actor, string action, Guid? targetUserId, string targetUsername, string details)
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(action)) return;
            string trimmedDetails = details ?? string.Empty;
            if (trimmedDetails.Length > 500) trimmedDetails = trimmedDetails.Substring(0, 500);
            string trimmedActor = (actor ?? string.Empty);
            if (trimmedActor.Length > 64) trimmedActor = trimmedActor.Substring(0, 64);
            string trimmedUsername = (targetUsername ?? string.Empty);
            if (trimmedUsername.Length > 128) trimmedUsername = trimmedUsername.Substring(0, 128);

            Config.AuditLog.Add(new AuditLogEntry
            {
                Timestamp = DateTime.UtcNow,
                Actor = trimmedActor,
                Action = action,
                TargetUserId = targetUserId,
                TargetUsername = trimmedUsername,
                Details = trimmedDetails
            });

            int cap = Math.Max(50, Config.AuditLogMaxEntries);
            if (Config.AuditLog.Count > cap)
            {
                Config.AuditLog.RemoveRange(0, Config.AuditLog.Count - cap);
            }
            _save();
        }
    }
}
