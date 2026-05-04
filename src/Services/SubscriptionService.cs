using System;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.NoPayNoPlay.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.NoPayNoPlay.Services;

/// <summary>
/// Logique métier autour des souscriptions.
/// </summary>
public class SubscriptionService
{
    private readonly IUserManager _userManager;
    private readonly ILogger<SubscriptionService> _logger;
    private static readonly object _lock = new();

    public SubscriptionService(IUserManager userManager, ILogger<SubscriptionService> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    private static PluginConfiguration Config => Plugin.Instance!.Configuration;

    /// <summary>
    /// Récupère ou crée la souscription d'un utilisateur (essai gratuit à la création).
    /// </summary>
    public UserSubscription EnsureUserTracked(Guid userId)
    {
        lock (_lock)
        {
            UserSubscription? sub = Config.Subscriptions.FirstOrDefault(s => s.UserId == userId);
            if (sub != null)
            {
                return sub;
            }

            DateTime now = DateTime.UtcNow;
            sub = new UserSubscription
            {
                UserId = userId,
                SubscriptionDate = now,
                ExpiryDate = now.AddDays(Math.Max(0, Config.TrialDays)),
                IsExempt = false,
                IsBlocked = false,
                LastNotifiedState = SubscriptionState.Ok
            };
            Config.Subscriptions.Add(sub);
            Plugin.Instance!.SaveConfiguration();
            _logger.LogInformation(
                "NoPayNoPlay : nouvel utilisateur suivi {UserId}, essai jusqu'au {Expiry:o}",
                userId,
                sub.ExpiryDate);
            return sub;
        }
    }

    /// <summary>
    /// Calcule la nouvelle échéance en respectant le jour anniversaire (gestion fin de mois).
    /// </summary>
    public static DateTime ComputeNextExpiry(DateTime current, int monthsToAdd, int anchorDay)
    {
        DateTime target = current.AddMonths(monthsToAdd);
        int day = Math.Min(anchorDay, DateTime.DaysInMonth(target.Year, target.Month));
        return new DateTime(target.Year, target.Month, day, 23, 59, 59, DateTimeKind.Utc);
    }

    /// <summary>
    /// Évalue l'état d'une souscription à un instant donné.
    /// </summary>
    public SubscriptionState EvaluateState(UserSubscription sub)
    {
        if (sub.IsExempt)
        {
            return SubscriptionState.Exempt;
        }

        DateTime now = DateTime.UtcNow;
        TimeSpan delta = sub.ExpiryDate - now;

        if (delta.TotalDays > Config.WarningDaysBefore)
        {
            return SubscriptionState.Ok;
        }

        if (delta.TotalSeconds > 0)
        {
            return SubscriptionState.WarningSoon;
        }

        if (delta.TotalDays > -Config.GraceDays)
        {
            return SubscriptionState.InGrace;
        }

        return SubscriptionState.Blocked;
    }

    /// <summary>
    /// Enregistre un paiement et étend l'échéance.
    /// </summary>
    public UserSubscription ApplyPayment(Guid userId, decimal amount, string method, int monthsAdded, string note)
    {
        lock (_lock)
        {
            UserSubscription sub = EnsureUserTracked(userId);
            int anchor = sub.SubscriptionDate.Day;
            // Si l'échéance est passée, on repart de maintenant ; sinon on prolonge depuis l'échéance courante.
            DateTime baseDate = sub.ExpiryDate < DateTime.UtcNow ? DateTime.UtcNow : sub.ExpiryDate;
            sub.ExpiryDate = ComputeNextExpiry(baseDate, Math.Max(1, monthsAdded), anchor);
            sub.Transactions.Add(new TransactionEntry
            {
                Date = DateTime.UtcNow,
                Amount = amount,
                MonthsAdded = monthsAdded,
                Method = method ?? string.Empty,
                AdminNote = note ?? string.Empty
            });
            sub.LastNotifiedState = SubscriptionState.Ok;
            Plugin.Instance!.SaveConfiguration();
            return sub;
        }
    }

    /// <summary>
    /// Bascule l'état d'exemption.
    /// </summary>
    public UserSubscription SetExempt(Guid userId, bool isExempt)
    {
        lock (_lock)
        {
            UserSubscription sub = EnsureUserTracked(userId);
            sub.IsExempt = isExempt;
            Plugin.Instance!.SaveConfiguration();
            return sub;
        }
    }

    /// <summary>
    /// Réinitialise l'utilisateur à un nouvel essai.
    /// </summary>
    public UserSubscription Reset(Guid userId)
    {
        lock (_lock)
        {
            UserSubscription sub = EnsureUserTracked(userId);
            DateTime now = DateTime.UtcNow;
            sub.SubscriptionDate = now;
            sub.ExpiryDate = now.AddDays(Math.Max(0, Config.TrialDays));
            sub.LastNotifiedState = SubscriptionState.Ok;
            Plugin.Instance!.SaveConfiguration();
            return sub;
        }
    }

    /// <summary>Persiste les modifications après mutation directe.</summary>
    public void Save()
    {
        lock (_lock)
        {
            Plugin.Instance!.SaveConfiguration();
        }
    }
}
