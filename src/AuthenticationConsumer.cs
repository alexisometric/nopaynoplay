using System;
using System.Threading.Tasks;
using Jellyfin.Plugin.NoPayNoPlay.Services;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Authentication;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.NoPayNoPlay;

/// <summary>
/// Suit les authentifications réussies pour créer/initialiser la souscription utilisateur.
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
            Guid userId = eventArgs?.User?.Id ?? Guid.Empty;
            if (userId != Guid.Empty)
            {
                _subscriptionService.EnsureUserTracked(userId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NoPayNoPlay : erreur lors du suivi d'authentification");
        }

        return Task.CompletedTask;
    }
}
