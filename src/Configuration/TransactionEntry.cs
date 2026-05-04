using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace Jellyfin.Plugin.NoPayNoPlay.Configuration;

/// <summary>
/// Représente l'enregistrement d'un paiement validé manuellement par l'admin.
/// </summary>
public class TransactionEntry
{
    /// <summary>Date de validation du paiement.</summary>
    public DateTime Date { get; set; } = DateTime.UtcNow;

    /// <summary>Montant perçu (devise issue de la config globale).</summary>
    public decimal Amount { get; set; }

    /// <summary>Nombre de mois ajoutés à l'échéance.</summary>
    public int MonthsAdded { get; set; } = 1;

    /// <summary>Méthode de paiement (PayPal, Lydia, RIB, Espèces, Autre).</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>Note libre saisie par l'admin.</summary>
    public string AdminNote { get; set; } = string.Empty;
}
