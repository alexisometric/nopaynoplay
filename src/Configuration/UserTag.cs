using System;

namespace Jellyfin.Plugin.NoPayNoPlay.Configuration;

/// <summary>
/// Logical group a member belongs to (family, friends, guests…). Used to
/// override the default monthly price for that subset of users.
/// </summary>
public class UserTag
{
    /// <summary>Stable identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Short machine-friendly key (e.g. "family", "friends", "guests").</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Display label shown in the admin UI.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Optional price override applied to members carrying this tag. When 0,
    /// the global <see cref="PluginConfiguration.MonthlyPrice"/> is used.
    /// </summary>
    public decimal MonthlyPriceOverride { get; set; }

    /// <summary>Hex colour shown next to the tag (UI only).</summary>
    public string Color { get; set; } = string.Empty;
}
