using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.NoPayNoPlay.Configuration;

/// <summary>
/// Configuration globale persistée par Jellyfin (XML).
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Montant mensuel.</summary>
    public decimal MonthlyPrice { get; set; } = 10m;

    /// <summary>Devise affichée.</summary>
    public string Currency { get; set; } = "EUR";

    /// <summary>Nombre de jours de grâce après expiration avant blocage effectif.</summary>
    public int GraceDays { get; set; } = 2;

    /// <summary>Durée de l'essai gratuit accordé à la première authentification.</summary>
    public int TrialDays { get; set; } = 7;

    /// <summary>Nombre de jours avant échéance pour activer la bannière d'alerte.</summary>
    public int WarningDaysBefore { get; set; } = 3;

    /// <summary>Lien PayPal.me complet (ex: https://paypal.me/pseudo/10).</summary>
    public string PaypalMeUrl { get; set; } = string.Empty;

    /// <summary>Lien Lydia complet.</summary>
    public string LydiaUrl { get; set; } = string.Empty;

    /// <summary>Texte libre RIB / IBAN affiché à l'utilisateur.</summary>
    public string IbanText { get; set; } = string.Empty;

    /// <summary>Note libre affichée dans la modal utilisateur.</summary>
    public string CustomNote { get; set; } = string.Empty;

    /// <summary>Souscriptions par utilisateur.</summary>
    public List<UserSubscription> Subscriptions { get; set; } = new();
}
