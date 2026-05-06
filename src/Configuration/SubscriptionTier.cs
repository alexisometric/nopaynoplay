using System;

namespace Jellyfin.Plugin.NoPayNoPlay.Configuration;

/// <summary>
/// Predefined subscription tier (e.g. 1 month / 3 months / 12 months).
/// Tiers are surfaced in the user modal so members can choose the package
/// that matches the amount they are about to pay.
/// </summary>
public class SubscriptionTier
{
    /// <summary>Stable identifier (used for admin edit/delete).</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Number of months covered by the tier (≥ 1).</summary>
    public int Months { get; set; } = 1;

    /// <summary>Total price for this tier (currency from the global config).</summary>
    public decimal Price { get; set; }

    /// <summary>
    /// Optional short label (e.g. "Best deal"). Localised by the client when empty.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Marks the tier as the recommended option in the user modal.
    /// At most one tier should be flagged at a time; the UI breaks ties on
    /// the first match.
    /// </summary>
    public bool Highlight { get; set; }
}
