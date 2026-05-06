using System;

namespace Jellyfin.Plugin.NoPayNoPlay.Configuration;

/// <summary>
/// A redeemable code that grants free months to a member.
/// </summary>
public class PromoCode
{
    /// <summary>Gets or sets the unique identifier of the code (used for admin actions).</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the user-facing code (uppercase, alphanumeric).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Gets or sets the number of months added to the redeemer's expiry.</summary>
    public int MonthsGranted { get; set; } = 1;

    /// <summary>Gets or sets the maximum number of times this code can be redeemed (0 = unlimited).</summary>
    public int MaxUses { get; set; }

    /// <summary>Gets or sets how many times the code has been redeemed.</summary>
    public int UsedCount { get; set; }

    /// <summary>Gets or sets the optional expiry date of the code itself.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Gets or sets the creation timestamp.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
