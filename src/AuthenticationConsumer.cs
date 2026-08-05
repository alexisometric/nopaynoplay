using System;
using System.Threading.Tasks;
using Jellyfin.Plugin.NoPayNoPlay.Configuration;
using Jellyfin.Plugin.NoPayNoPlay.Services;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Authentication;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.NoPayNoPlay;

/// <summary>
/// Listens for successful authentications: ensures the user has a tracked
/// subscription and applies the playback policy immediately, so a lapsed user is
/// blocked at sign-in instead of waiting for the next scheduled pass (which could
/// otherwise leave a window of up to 12 h of playback after expiry).
/// </summary>
public class AuthenticationConsumer : IEventConsumer<AuthenticationResultEventArgs>
{
    private readonly SubscriptionService _subscriptionService;
    private readonly UserPolicyEnforcer _enforcer;
    private readonly ILogger<AuthenticationConsumer> _logger;

    public AuthenticationConsumer(
        SubscriptionService subscriptionService,
        UserPolicyEnforcer enforcer,
        ILogger<AuthenticationConsumer> logger)
    {
        _subscriptionService = subscriptionService;
        _enforcer = enforcer;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task OnEvent(AuthenticationResultEventArgs eventArgs)
    {
        try
        {
            Guid? userId = eventArgs?.User?.Id;
            if (userId is null || userId == Guid.Empty)
            {
                return;
            }

            UserSubscription sub = _subscriptionService.EnsureUserTracked(userId.Value);
            SubscriptionState state = _subscriptionService.EvaluateState(sub);
            // Best-effort: apply the policy right at login so the block is effective
            // immediately for lapsed users. Failures are logged and the 12 h scheduled
            // task reconciles them.
            await _enforcer.ApplyAsync(sub, state).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NoPayNoPlay: failed to enforce policy on authentication");
        }
    }
}
