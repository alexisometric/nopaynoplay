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

    /// <summary>Gets or sets the optional UI culture override (e.g. "en", "fr"). Empty falls back to auto-detection.</summary>
    public string UiCultureOverride { get; set; } = string.Empty;

    /// <summary>Gets or sets the per-user subscription records.</summary>
    public List<UserSubscription> Subscriptions { get; set; } = new();

    /// <summary>
    /// Promo / referral codes that members can redeem from the user modal.
    /// </summary>
    public List<PromoCode> PromoCodes { get; set; } = new();
}
