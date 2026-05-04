using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.NoPayNoPlay.Services;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Authentication;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.NoPayNoPlay;

/// <summary>
/// Listens for successful authentications and ensures the user has a tracked subscription.
/// </summary>
public class AuthenticationConsumer : IEventConsumer<AuthenticationResultEventArgs>
{
    private readonly SubscriptionService _subscriptionService;
    private readonly ILogger<AuthenticationConsumer> _logger;

    public AuthenticationConsumer(SubscriptionService subscriptionService, ILogger<AuthenticationConsumer> logger)
    {
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task OnEvent(AuthenticationResultEventArgs eventArgs)
    {
        try
        {
            Guid? userId = eventArgs?.User?.Id;
            if (userId is null || userId == Guid.Empty)
            {
                return Task.CompletedTask;
            }

            _subscriptionService.EnsureUserTracked(userId.Value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NoPayNoPlay: failed to track user on authentication");
        }

        return Task.CompletedTask;
    }
}
