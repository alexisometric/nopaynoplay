using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Jellyfin.Plugin.NoPayNoPlay.Configuration;

namespace Jellyfin.Plugin.NoPayNoPlay.Api;

/// <summary>Payload to record a payment.</summary>
public class PaymentDto
{
    [Range(0, 100000)]
    public decimal Amount { get; set; }

    [StringLength(50)]
    public string Method { get; set; } = string.Empty;

    [Range(1, 60)]
    public int MonthsAdded { get; set; } = 1;

    [StringLength(500)]
    public string Note { get; set; } = string.Empty;

    /// <summary>
    /// Optional date the payment actually occurred. When provided, this is the date stored in
    /// the transaction history (useful for backfilling past payments). The expiry is still
    /// extended by <see cref="MonthsAdded"/>; only the recorded date changes.
    /// Future dates are clamped to "now".
    /// </summary>
    public DateTime? Date { get; set; }

    /// <summary>
    /// Optional idempotency key (generated per payment form) so a double-click or a browser
    /// retry cannot record the same payment twice within the dedup window.
    /// </summary>
    [StringLength(64)]
    public string? IdempotencyKey { get; set; }
}

/// <summary>Payload to toggle the exemption flag.</summary>
public class ExemptDto
{
    public bool IsExempt { get; set; }
}

/// <summary>User row used by the admin dashboard.</summary>
public class UserSubscriptionDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public DateTime SubscriptionDate { get; set; }
    public DateTime ExpiryDate { get; set; }
    public bool IsExempt { get; set; }
    public bool IsBlocked { get; set; }
    public string State { get; set; } = string.Empty;
    public int DaysLeft { get; set; }
    public List<TransactionEntry> Transactions { get; set; } = new();

    /// <summary>True when the member has self-declared a payment awaiting confirmation.</summary>
    public bool HasPendingPaymentClaim { get; set; }

    /// <summary>UTC timestamp of the latest pending claim (null when none).</summary>
    public DateTime? PendingPaymentClaimAt { get; set; }

    /// <summary>Method declared by the user when self-claiming.</summary>
    public string PendingPaymentMethod { get; set; } = string.Empty;

    /// <summary>Tag key (family / friends / guests / …); empty when none.</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>Total amount paid by the user across every recorded transaction.</summary>
    public decimal TotalPaid { get; set; }

    /// <summary>Number of full months the user is currently behind on (0 when up to date).</summary>
    public int ArrearsMonths { get; set; }

    /// <summary>Effective monthly price applied to this user (after tag overrides).</summary>
    public decimal EffectiveMonthlyPrice { get; set; }
}

/// <summary>
/// A payment row exposed to the member on <c>/Me</c>. Deliberately omits the
/// admin-only <see cref="TransactionEntry.AdminNote"/> so internal notes are never
/// serialized into a response the member can read via DevTools.
/// </summary>
public class MeTransactionDto
{
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public int MonthsAdded { get; set; }
    public string Method { get; set; } = string.Empty;
}

/// <summary>Payload returned by the self-service <c>/Me</c> endpoint.</summary>
public class MeDto
{
    /// <summary>The caller's own Jellyfin username (used to build a payment reference).</summary>
    public string Username { get; set; } = string.Empty;

    public DateTime ExpiryDate { get; set; }
    public int DaysLeft { get; set; }
    public string State { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "EUR";
    public string PaypalMeUrl { get; set; } = string.Empty;
    public string LydiaUrl { get; set; } = string.Empty;
    public string CustomNote { get; set; } = string.Empty;
    public int WarningDaysBefore { get; set; }
    public int GraceDays { get; set; }

    /// <summary>Resolved UI culture (e.g. "en", "fr").</summary>
    public string Lang { get; set; } = "en";

    /// <summary>Translation strings for the resolved culture.</summary>
    public IReadOnlyDictionary<string, string> Strings { get; set; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Stable hash of the <see cref="Strings"/> bundle for the resolved culture. The client
    /// echoes it back via <c>?strings=</c>; when it matches, the server returns an empty
    /// bundle so the few-KB translation payload is not re-sent on every refresh.
    /// </summary>
    public string StringsHash { get; set; } = string.Empty;

    /// <summary>Personal payment history (most recent first), without admin notes.</summary>
    public List<MeTransactionDto> Transactions { get; set; } = new();

    /// <summary>
    /// True when the response is rendered for an administrator. Administrators have
    /// no real subscription, but the client uses sample values so the modal is
    /// browsable for previewing the user-facing UI.
    /// </summary>
    public bool IsAdminPreview { get; set; }

    /// <summary>True when the member has a pending self-declared payment.</summary>
    public bool HasPendingPaymentClaim { get; set; }

    /// <summary>UTC timestamp of the pending claim (null when none).</summary>
    public DateTime? PendingPaymentClaimAt { get; set; }

    /// <summary>Method declared by the user when self-claiming.</summary>
    public string PendingPaymentMethod { get; set; } = string.Empty;

    /// <summary>Subscription tiers offered to the user (already filtered for display).</summary>
    public List<SubscriptionTier> Tiers { get; set; } = new();

    /// <summary>Optional contact email — used to build a mailto link in the modal.</summary>
    public string ContactEmail { get; set; } = string.Empty;

    /// <summary>True when the API caller is currently authenticated as a Jellyfin administrator.</summary>
    public bool IsAdmin { get; set; }
}

/// <summary>Payload to patch a single transaction.</summary>
public class TransactionPatchDto
{
    [Range(0, 100000)]
    public decimal? Amount { get; set; }

    [StringLength(50)]
    public string? Method { get; set; }

    [Range(1, 60)]
    public int? MonthsAdded { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }

    public DateTime? Date { get; set; }
}

/// <summary>Bulk-action payload (list of user IDs).</summary>
public class BulkUserDto
{
    public List<Guid> UserIds { get; set; } = new();
}

/// <summary>Bulk payment payload.</summary>
public class BulkPaymentDto : PaymentDto
{
    public List<Guid> UserIds { get; set; } = new();
}

/// <summary>Bulk exemption payload.</summary>
public class BulkExemptDto : BulkUserDto
{
    public bool IsExempt { get; set; }
}

/// <summary>Self-service "I paid" claim from the user.</summary>
public class MarkPaidDto
{
    [StringLength(50)]
    public string Method { get; set; } = string.Empty;
}

/// <summary>Promo code creation / update payload.</summary>
public class PromoCodeDto
{
    [Required]
    [StringLength(32)]
    public string Code { get; set; } = string.Empty;

    [Range(1, 60)]
    public int MonthsGranted { get; set; } = 1;

    [Range(0, 100000)]
    public int MaxUses { get; set; }

    public DateTime? ExpiresAt { get; set; }
}

/// <summary>Payload to redeem a promo code.</summary>
public class RedeemCodeDto
{
    [Required]
    [StringLength(32)]
    public string Code { get; set; } = string.Empty;
}

/// <summary>Payload to assign / clear a tag on a user.</summary>
public class UserTagAssignmentDto
{
    [StringLength(32)]
    public string Tag { get; set; } = string.Empty;
}
