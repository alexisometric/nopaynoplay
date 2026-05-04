using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.NoPayNoPlay.Configuration;
using Jellyfin.Plugin.NoPayNoPlay.Localization;
using Jellyfin.Plugin.NoPayNoPlay.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.NoPayNoPlay.ScheduledTasks;

/// <summary>
/// Scheduled task that enforces playback policies and emits dashboard notifications.
/// </summary>
public class EnforcementTask : IScheduledTask
{
    private readonly IUserManager _userManager;
    private readonly IActivityManager _activityManager;
    private readonly SubscriptionService _subscriptionService;
    private readonly UserPolicyEnforcer _enforcer;
    private readonly Localizer _localizer;
    private readonly ILogger<EnforcementTask> _logger;

    public EnforcementTask(
        IUserManager userManager,
        IActivityManager activityManager,
        SubscriptionService subscriptionService,
        UserPolicyEnforcer enforcer,
        Localizer localizer,
        ILogger<EnforcementTask> logger)
    {
        _userManager = userManager;
        _activityManager = activityManager;
        _subscriptionService = subscriptionService;
        _enforcer = enforcer;
        _localizer = localizer;
        _logger = logger;
    }

    private string ServerCulture
    {
        get
        {
            string? overrideCulture = Plugin.Instance?.Configuration?.UiCultureOverride;
            if (!string.IsNullOrWhiteSpace(overrideCulture))
            {
                return overrideCulture.Trim().ToLowerInvariant();
            }

            return _localizer.ResolveCulture(null);
        }
    }

    /// <inheritdoc />
    public string Name => _localizer.Get("task.enforcement.name", ServerCulture);

    /// <inheritdoc />
    public string Key => "NoPayNoPlay.Enforcement";

    /// <inheritdoc />
    public string Description => _localizer.Get("task.enforcement.description", ServerCulture);

    /// <inheritdoc />
    public string Category => _localizer.Get("task.category", ServerCulture);

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
        var users = _userManager.Users.ToList();
        int total = users.Count;
        int done = 0;
        string culture = ServerCulture;

        foreach (var user in users)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                UserSubscription sub = _subscriptionService.EnsureUserTracked(user.Id);
                SubscriptionState state = _subscriptionService.EvaluateState(sub);
                await _enforcer.ApplyAsync(sub, state).ConfigureAwait(false);
                await NotifyIfNeededAsync(sub, state, user.Username, culture).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NoPayNoPlay: error processing user {UserId}", user.Id);
            }

            done++;
            progress.Report(100.0 * done / Math.Max(1, total));
        }

        _subscriptionService.Save();
    }

    private async Task NotifyIfNeededAsync(
        UserSubscription sub,
        SubscriptionState state,
        string username,
        string culture)
    {
        if (state == SubscriptionState.Exempt || state == SubscriptionState.Ok)
        {
            sub.LastNotifiedState = state;
            return;
        }

        if (sub.LastNotifiedState == state)
        {
            // Already notified for this state; skip.
            return;
        }

        string titleKey = state switch
        {
            SubscriptionState.WarningSoon => "notif.warningSoon.title",
            SubscriptionState.InGrace => "notif.inGrace.title",
            SubscriptionState.Blocked => "notif.blocked.title",
            _ => "plugin.name"
        };

        string bodyKey = state switch
        {
            SubscriptionState.WarningSoon => "notif.warningSoon.body",
            SubscriptionState.InGrace => "notif.inGrace.body",
            SubscriptionState.Blocked => "notif.blocked.body",
            _ => string.Empty
        };

        var tokens = new Dictionary<string, string?>
        {
            ["username"] = username,
            ["date"] = sub.ExpiryDate.ToString("yyyy-MM-dd")
        };

        string title = _localizer.Get(titleKey, culture);
        string overview = string.IsNullOrEmpty(bodyKey)
            ? string.Empty
            : _localizer.Get(bodyKey, culture, tokens);

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
            _logger.LogWarning(ex, "NoPayNoPlay: failed to create notification activity");
        }

        sub.LastNotifiedState = state;
    }
}
