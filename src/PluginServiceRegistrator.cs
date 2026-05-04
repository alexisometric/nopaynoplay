using Jellyfin.Plugin.NoPayNoPlay.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Authentication;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.NoPayNoPlay;

/// <summary>
/// Enregistre les services du plugin auprès du conteneur DI Jellyfin.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<SubscriptionService>();
        serviceCollection.AddSingleton<UserPolicyEnforcer>();
        serviceCollection.AddHostedService<PluginEntryPoint>();
        serviceCollection.AddScoped<IEventConsumer<AuthenticationResultEventArgs>, AuthenticationConsumer>();
    }
}
