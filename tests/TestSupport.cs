using Jellyfin.Plugin.NoPayNoPlay.Configuration;
using Jellyfin.Plugin.NoPayNoPlay.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.NoPayNoPlay.Tests;

/// <summary>
/// Shared helpers to build a <see cref="SubscriptionService"/> backed by an
/// in-memory configuration (no <see cref="Plugin"/> singleton, no disk I/O), via
/// the internal test-only constructor exposed through InternalsVisibleTo.
/// </summary>
internal static class TestSupport
{
    public static SubscriptionService Service(PluginConfiguration cfg)
        => new SubscriptionService(null!, NullLogger<SubscriptionService>.Instance, () => cfg, () => { });
}
