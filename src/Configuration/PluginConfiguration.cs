using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.NoPayNoPlay.Configuration;

/// <summary>
/// Plugin configuration persisted by Jellyfin (XML).
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Gets or sets the monthly subscription price.</summary>
    public decimal MonthlyPrice { get; set; } = 10m;

    /// <summary>Gets or sets the displayed currency code.</summary>
    public string Currency { get; set; } = "EUR";

    /// <summary>Gets or sets the number of grace days after expiry before playback is blocked.</summary>
    public int GraceDays { get; set; } = 2;

    /// <summary>Gets or sets the trial length granted on first authentication.</summary>
    public int TrialDays { get; set; } = 7;

    /// <summary>Gets or sets the number of days before expiry when the warning banner is shown.</summary>
    public int WarningDaysBefore { get; set; } = 3;

    /// <summary>Gets or sets the full PayPal.me URL (e.g. https://paypal.me/handle/10).</summary>
    public string PaypalMeUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the full Lydia URL.</summary>
    public string LydiaUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the IBAN / bank details displayed to the user.</summary>
    public string IbanText { get; set; } = string.Empty;

    /// <summary>Gets or sets a free-form note shown in the user modal.</summary>
    public string CustomNote { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional contact email shown in the user modal (mailto link).</summary>
    public string ContactEmail { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional UI culture override (e.g. "en", "fr"). Empty falls back to auto-detection.
    /// </summary>
    public string UiCultureOverride { get; set; } = string.Empty;

    /// <summary>Gets or sets the per-user subscription records.</summary>
    public List<UserSubscription> Subscriptions { get; set; } = new();

    /// <summary>
    /// Promo / referral codes that members can redeem from the user modal.
    /// </summary>
    public List<PromoCode> PromoCodes { get; set; } = new();

    /// <summary>
    /// Predefined subscription tiers shown to members in the user modal
    /// (e.g. 1 month / 3 months / 12 months packages with a discount).
    /// </summary>
    /// <remarks>
    /// Default is intentionally empty: <see cref="System.Xml.Serialization.XmlSerializer"/>
    /// appends to existing collections during deserialization, so any inline
    /// initializer here would be re-injected on every restart and produce
    /// duplicates. Seeding happens once via <see cref="DefaultsSeeded"/>.
    /// </remarks>
    public List<SubscriptionTier> Tiers { get; set; } = new();

    /// <summary>
    /// User groups (family / friends / guests / …) that override the
    /// default monthly price for tagged members.
    /// </summary>
    /// <remarks>Empty by default for the same reason as <see cref="Tiers"/>.</remarks>
    public List<UserTag> Tags { get; set; } = new();

    /// <summary>
    /// Set to <c>true</c> after the plugin has populated <see cref="Tiers"/>
    /// and <see cref="Tags"/> with their first-run defaults. Prevents the
    /// defaults from being re-seeded if an admin deliberately empties the
    /// lists.
    /// </summary>
    public bool DefaultsSeeded { get; set; }

    /// <summary>
    /// In-memory audit trail of administrative actions. Capped at <see cref="AuditLogMaxEntries"/>;
    /// older entries are evicted on insert.
    /// </summary>
    public List<AuditLogEntry> AuditLog { get; set; } = new();

    /// <summary>Maximum number of entries kept in <see cref="AuditLog"/>.</summary>
    public int AuditLogMaxEntries { get; set; } = 500;
}
