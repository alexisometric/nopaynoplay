using System;

namespace Jellyfin.Plugin.NoPayNoPlay.Configuration;

/// <summary>
/// Represents a payment manually validated by the administrator.
/// </summary>
public class TransactionEntry
{
    /// <summary>
    /// Stable identifier used by edit/delete endpoints. Defaults to a freshly-allocated
    /// Guid so brand-new entries always carry an id; entries deserialized from older
    /// configurations may have <see cref="Guid.Empty"/> until <c>SubscriptionService</c>
    /// migrates them on first access.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Validation date.</summary>
    public DateTime Date { get; set; } = DateTime.UtcNow;

    /// <summary>Amount received (currency from global config).</summary>
    public decimal Amount { get; set; }

    /// <summary>Number of months added to the expiry date.</summary>
    public int MonthsAdded { get; set; } = 1;

    /// <summary>Payment method (PayPal, Lydia, Transfer, Cash, Other).</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>Free-form note entered by the administrator.</summary>
    public string AdminNote { get; set; } = string.Empty;
}
