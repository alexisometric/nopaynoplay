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
                // Route the override through the same region->base resolution as the
                // request path so a region code (e.g. pt-BR) maps to its base bundle
                // (pt) instead of silently falling back to English.
                return _localizer.ResolveExplicit(overrideCulture);
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
        var users = _userManager.GetUsers().ToList();
        int total = users.Count;
        int done = 0;
        string culture = ServerCulture;

        // Process users in bounded parallel batches: the DB writes (policy updates,
        // activity inserts) are independent per user and the biggest cost, so serializing
        // one awaited round-trip at a time makes a large run needlessly slow. Config
        // mutations stay serialized by SubscriptionService's internal lock.
        const int batchSize = 16;
        for (int offset = 0; offset < users.Count; offset += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = users.Skip(offset).Take(batchSize).ToList();
            var tasks = batch.Select(user => Task.Run(async () =>
            {
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
                finally
                {
                    int d = Interlocked.Increment(ref done);
                    progress.Report(100.0 * d / Math.Max(1, total));
                }
            })).ToList();

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        // Drop subscription records whose Jellyfin user has since been deleted so
        // dead state (history, admin notes, policy snapshots) doesn't accumulate.
        _subscriptionService.PurgeOrphanedSubscriptions(users.Select(u => u.Id).ToList());

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
            // Update the dedup markers under the config lock so we never race the
            // admin/user endpoints (which mutate the same subscription under the lock).
            _subscriptionService.UpdateNotificationState(sub, state, string.Empty);
            return;
        }

        // Fine-grained per-day reminders so the user gets nudged at J-3, J-1, J0
        // and again the moment the grace window ends. The dedup key combines the
        // state and the milestone so each milestone fires at most once.
        DateTime now = DateTime.UtcNow;
        double daysUntilExpiry = (sub.ExpiryDate - now).TotalDays;
        int daysLeft = (int)Math.Ceiling(daysUntilExpiry);
        int daysOverdue = (int)Math.Floor(-daysUntilExpiry);

        string milestone = state switch
        {
            SubscriptionState.WarningSoon when daysLeft <= 1 => "warn-d1",
            SubscriptionState.WarningSoon when daysLeft <= 3 => "warn-d3",
            SubscriptionState.WarningSoon => "warn-soon",
            SubscriptionState.InGrace when daysOverdue == 0 => "grace-d0",
            SubscriptionState.InGrace => "grace-active",
            SubscriptionState.Blocked => "blocked",
            _ => state.ToString()
        };

        string dedupKey = state + ":" + milestone;
        if (sub.LastNotifiedState == state && sub.LastNotificationKey == dedupKey)
        {
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
            // HTML-encode the user-controlled username: it is rendered in the Jellyfin
            // activity feed shown to administrators.
            ["username"] = System.Net.WebUtility.HtmlEncode(username),
            ["date"] = sub.ExpiryDate.ToString("yyyy-MM-dd"),
            ["days"] = Math.Max(0, daysLeft).ToString(System.Globalization.CultureInfo.InvariantCulture)
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

        // Persist the dedup markers under the config lock (see comment on the Ok/Exempt branch).
        _subscriptionService.UpdateNotificationState(sub, state, dedupKey);
    }
}
