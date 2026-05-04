using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.NoPayNoPlay.Configuration;
using Jellyfin.Plugin.NoPayNoPlay.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.NoPayNoPlay.ScheduledTasks;

/// <summary>
/// Tâche planifiée : applique les politiques et envoie les notifications.
/// </summary>
public class EnforcementTask : IScheduledTask
{
    private readonly IUserManager _userManager;
    private readonly IActivityManager _activityManager;
    private readonly SubscriptionService _subscriptionService;
    private readonly UserPolicyEnforcer _enforcer;
    private readonly ILogger<EnforcementTask> _logger;

    public EnforcementTask(
        IUserManager userManager,
        IActivityManager activityManager,
        SubscriptionService subscriptionService,
        UserPolicyEnforcer enforcer,
        ILogger<EnforcementTask> logger)
    {
        _userManager = userManager;
        _activityManager = activityManager;
        _subscriptionService = subscriptionService;
        _enforcer = enforcer;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "NoPayNoPlay - Vérification des abonnements";

    /// <inheritdoc />
    public string Key => "NoPayNoPlay.Enforcement";

    /// <inheritdoc />
    public string Description =>
        "Vérifie l'échéance de chaque utilisateur, applique le blocage de lecture si nécessaire et notifie les utilisateurs concernés.";

    /// <inheritdoc />
    public string Category => "NoPayNoPlay";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromHours(12).Ticks
        };
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        PluginConfiguration cfg = Plugin.Instance!.Configuration;
        var users = _userManager.Users.ToList();
        int total = users.Count;
        int done = 0;

        foreach (var user in users)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                UserSubscription sub = _subscriptionService.EnsureUserTracked(user.Id);
                SubscriptionState state = _subscriptionService.EvaluateState(sub);
                await _enforcer.ApplyAsync(sub, state).ConfigureAwait(false);
                await NotifyIfNeededAsync(sub, state, user.Username).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NoPayNoPlay : erreur sur l'utilisateur {UserId}", user.Id);
            }

            done++;
            progress.Report(100.0 * done / Math.Max(1, total));
        }

        _subscriptionService.Save();
    }

    private async Task NotifyIfNeededAsync(UserSubscription sub, SubscriptionState state, string username)
    {
        if (state == SubscriptionState.Exempt || state == SubscriptionState.Ok)
        {
            sub.LastNotifiedState = state;
            return;
        }

        if (sub.LastNotifiedState == state)
        {
            return; // anti-rebond
        }

        string title = state switch
        {
            SubscriptionState.WarningSoon => "NoPayNoPlay : échéance proche",
            SubscriptionState.InGrace => "NoPayNoPlay : période de grâce",
            SubscriptionState.Blocked => "NoPayNoPlay : lecture bloquée",
            _ => "NoPayNoPlay"
        };

        string overview = state switch
        {
            SubscriptionState.WarningSoon =>
                $"L'abonnement de {username} expire le {sub.ExpiryDate:dd/MM/yyyy}.",
            SubscriptionState.InGrace =>
                $"L'abonnement de {username} a expiré ; période de grâce en cours.",
            SubscriptionState.Blocked =>
                $"L'abonnement de {username} est expiré : lecture désactivée.",
            _ => string.Empty
        };

        try
        {
            await _activityManager.CreateAsync(new ActivityLog(
                title,
                "NoPayNoPlay",
                sub.UserId)
            {
                Overview = overview,
                ShortOverview = overview,
                LogSeverity = state == SubscriptionState.Blocked
                    ? Microsoft.Extensions.Logging.LogLevel.Warning
                    : Microsoft.Extensions.Logging.LogLevel.Information
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NoPayNoPlay : impossible de créer l'activité de notification");
        }

        sub.LastNotifiedState = state;
    }
}
