using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.NoPayNoPlay.Configuration;

/// <summary>
/// Computed state of a user's subscription.
/// </summary>
public enum SubscriptionState
{
    /// <summary>Up to date; expiry far in the future.</summary>
    Ok,

    /// <summary>Expiring soon (within WarningDaysBefore days).</summary>
    WarningSoon,

    /// <summary>Expired but still within the grace window.</summary>
    InGrace,

    /// <summary>Blocked (playback disabled).</summary>
    Blocked,

    /// <summary>Exempt from payment (free access).</summary>
    Exempt
}

/// <summary>
/// Snapshot of the UserPolicy taken before blocking, used to restore the original state.
/// </summary>
public class UserPolicySnapshot
{
    public bool EnableMediaPlayback { get; set; } = true;
    public bool EnableAudioPlaybackTranscoding { get; set; } = true;
    public bool EnableVideoPlaybackTranscoding { get; set; } = true;
    public bool EnablePlaybackRemuxing { get; set; } = true;
}

/// <summary>
/// Per-user subscription data persisted with the plugin configuration.
/// </summary>
public class UserSubscription
{
    /// <summary>Jellyfin user identifier.</summary>
    public Guid UserId { get; set; }

    /// <summary>Initial subscription date; provides the anchor day for renewals.</summary>
    public DateTime SubscriptionDate { get; set; } = DateTime.UtcNow;

    /// <summary>Next expiry date.</summary>
    public DateTime ExpiryDate { get; set; } = DateTime.UtcNow;

    /// <summary>When true, the user is never blocked (free access).</summary>
    public bool IsExempt { get; set; }

    /// <summary>Indicates whether the playback block is currently applied.</summary>
    public bool IsBlocked { get; set; }

    /// <summary>Last state for which a notification was emitted (anti-bounce).</summary>
    public SubscriptionState LastNotifiedState { get; set; } = SubscriptionState.Ok;

    /// <summary>Snapshot of the original policy, kept for restoration.</summary>
    public UserPolicySnapshot? PolicySnapshot { get; set; }

    /// <summary>History of validated payments.</summary>
    public List<TransactionEntry> Transactions { get; set; } = new();

    /// <summary>
    /// Day-of-year of the last per-day notification (J-3 / J-1 / J0 / grace expired).
    /// Used to deduplicate fine-grained reminders inside the warning/grace window.
    /// </summary>
    public string LastNotificationKey { get; set; } = string.Empty;

    /// <summary>
    /// True when the member has self-declared a payment that is awaiting admin
    /// confirmation. The admin can confirm (records a real transaction) or
    /// reject (clears the flag).
    /// </summary>
    public bool HasPendingPaymentClaim { get; set; }

    /// <summary>UTC timestamp of the latest pending claim.</summary>
    public DateTime? PendingPaymentClaimAt { get; set; }

    /// <summary>Method declared by the user when self-claiming a payment.</summary>
    public string PendingPaymentMethod { get; set; } = string.Empty;

    /// <summary>Promo codes the user has already redeemed (anti-replay).</summary>
    public List<Guid> RedeemedPromoCodeIds { get; set; } = new();
}
