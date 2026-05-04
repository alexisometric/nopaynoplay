using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.NoPayNoPlay.Configuration;

/// <summary>
/// État courant calculé d'un abonnement utilisateur.
/// </summary>
public enum SubscriptionState
{
    /// <summary>À jour, échéance lointaine.</summary>
    Ok,

    /// <summary>Échéance proche (≤ WarningDaysBefore).</summary>
    WarningSoon,

    /// <summary>Échéance dépassée mais en période de grâce.</summary>
    InGrace,

    /// <summary>Bloqué (lecture désactivée).</summary>
    Blocked,

    /// <summary>Exempt de paiement (gratuit).</summary>
    Exempt
}

/// <summary>
/// Snapshot de la UserPolicy avant blocage, utilisé pour restauration.
/// </summary>
public class UserPolicySnapshot
{
    public bool EnableMediaPlayback { get; set; } = true;
    public bool EnableAudioPlaybackTranscoding { get; set; } = true;
    public bool EnableVideoPlaybackTranscoding { get; set; } = true;
    public bool EnablePlaybackRemuxing { get; set; } = true;
}

/// <summary>
/// Données de souscription persistées pour un utilisateur Jellyfin.
/// </summary>
public class UserSubscription
{
    /// <summary>Identifiant Jellyfin de l'utilisateur.</summary>
    public Guid UserId { get; set; }

    /// <summary>Date de souscription initiale (sert de jour anniversaire).</summary>
    public DateTime SubscriptionDate { get; set; } = DateTime.UtcNow;

    /// <summary>Prochaine échéance.</summary>
    public DateTime ExpiryDate { get; set; } = DateTime.UtcNow;

    /// <summary>Si true, l'utilisateur n'est jamais bloqué (accès gratuit).</summary>
    public bool IsExempt { get; set; }

    /// <summary>Indique si le blocage de lecture est actuellement appliqué.</summary>
    public bool IsBlocked { get; set; }

    /// <summary>Dernier état pour lequel une notification a été envoyée (anti-rebond).</summary>
    public SubscriptionState LastNotifiedState { get; set; } = SubscriptionState.Ok;

    /// <summary>Snapshot de la policy d'origine pour restauration.</summary>
    public UserPolicySnapshot? PolicySnapshot { get; set; }

    /// <summary>Historique des paiements validés.</summary>
    public List<TransactionEntry> Transactions { get; set; } = new();
}
