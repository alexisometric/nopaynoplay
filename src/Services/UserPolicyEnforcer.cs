using System;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.NoPayNoPlay.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.NoPayNoPlay.Services;

/// <summary>
/// Applique ou restaure la UserPolicy en fonction de l'état d'abonnement.
/// </summary>
public class UserPolicyEnforcer
{
    private readonly IUserManager _userManager;
    private readonly ILogger<UserPolicyEnforcer> _logger;

    public UserPolicyEnforcer(IUserManager userManager, ILogger<UserPolicyEnforcer> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    /// <summary>
    /// Applique le blocage si nécessaire ou restaure depuis le snapshot.
    /// </summary>
    public async Task ApplyAsync(UserSubscription sub, SubscriptionState state)
    {
        var user = _userManager.GetUserById(sub.UserId);
        if (user == null)
        {
            return;
        }

        // Les administrateurs et les exemptés ne sont jamais bloqués.
        if (user.HasPermission(PermissionKind.IsAdministrator) || sub.IsExempt)
        {
            if (sub.IsBlocked)
            {
                await RestoreAsync(sub).ConfigureAwait(false);
            }

            return;
        }

        if (state == SubscriptionState.Blocked && !sub.IsBlocked)
        {
            await BlockAsync(sub).ConfigureAwait(false);
        }
        else if (state != SubscriptionState.Blocked && sub.IsBlocked)
        {
            await RestoreAsync(sub).ConfigureAwait(false);
        }
    }

    private async Task BlockAsync(UserSubscription sub)
    {
        var user = _userManager.GetUserById(sub.UserId);
        if (user == null)
        {
            return;
        }

        // Snapshot avant modification.
        sub.PolicySnapshot = new UserPolicySnapshot
        {
            EnableMediaPlayback = user.HasPermission(PermissionKind.EnableMediaPlayback),
            EnableAudioPlaybackTranscoding = user.HasPermission(PermissionKind.EnableAudioPlaybackTranscoding),
            EnableVideoPlaybackTranscoding = user.HasPermission(PermissionKind.EnableVideoPlaybackTranscoding),
            EnablePlaybackRemuxing = user.HasPermission(PermissionKind.EnablePlaybackRemuxing)
        };

        user.SetPermission(PermissionKind.EnableMediaPlayback, false);
        user.SetPermission(PermissionKind.EnableAudioPlaybackTranscoding, false);
        user.SetPermission(PermissionKind.EnableVideoPlaybackTranscoding, false);
        user.SetPermission(PermissionKind.EnablePlaybackRemuxing, false);

        await _userManager.UpdateUserAsync(user).ConfigureAwait(false);

        sub.IsBlocked = true;
        _logger.LogInformation("NoPayNoPlay : lecture bloquée pour {UserId}", sub.UserId);
    }

    private async Task RestoreAsync(UserSubscription sub)
    {
        var user = _userManager.GetUserById(sub.UserId);
        if (user == null)
        {
            return;
        }

        UserPolicySnapshot snap = sub.PolicySnapshot ?? new UserPolicySnapshot();

        user.SetPermission(PermissionKind.EnableMediaPlayback, snap.EnableMediaPlayback);
        user.SetPermission(PermissionKind.EnableAudioPlaybackTranscoding, snap.EnableAudioPlaybackTranscoding);
        user.SetPermission(PermissionKind.EnableVideoPlaybackTranscoding, snap.EnableVideoPlaybackTranscoding);
        user.SetPermission(PermissionKind.EnablePlaybackRemuxing, snap.EnablePlaybackRemuxing);

        await _userManager.UpdateUserAsync(user).ConfigureAwait(false);

        sub.IsBlocked = false;
        sub.PolicySnapshot = null;
        _logger.LogInformation("NoPayNoPlay : lecture restaurée pour {UserId}", sub.UserId);
    }
}
