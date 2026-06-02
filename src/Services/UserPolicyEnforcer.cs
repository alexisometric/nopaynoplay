using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.NoPayNoPlay.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.NoPayNoPlay.Services;

/// <summary>
/// Applies or restores the UserPolicy depending on the subscription state.
/// </summary>
public class UserPolicyEnforcer
{
    private readonly IUserManager _userManager;
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<UserPolicyEnforcer> _logger;

    public UserPolicyEnforcer(
        IUserManager userManager,
        ISessionManager sessionManager,
        ILogger<UserPolicyEnforcer> logger)
    {
        _userManager = userManager;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <summary>
    /// Applies the playback block if needed or restores from the snapshot.
    /// </summary>
    public async Task ApplyAsync(UserSubscription sub, SubscriptionState state)
    {
        var user = _userManager.GetUserById(sub.UserId);
        if (user == null)
        {
            return;
        }

        // Administrators and exempt users are never blocked.
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

        // Snapshot before modification.
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
        _logger.LogInformation("NoPayNoPlay: playback blocked for {UserId}", sub.UserId);

        // Clearing the permission only stops NEW playback decisions; a session that
        // is already streaming (especially direct-play clients that never re-check
        // the policy) would keep playing until the next scheduled pass, up to 12 h
        // later. Proactively stop the user's active sessions so the block is
        // effective immediately.
        await StopActiveSessionsAsync(sub.UserId).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a Stop playstate command to every active session of the user that is
    /// currently playing something. Best-effort: never throws into the caller.
    /// </summary>
    private async Task StopActiveSessionsAsync(Guid userId)
    {
        try
        {
            var sessions = _sessionManager.Sessions
                .Where(s => s.UserId == userId && s.NowPlayingItem != null)
                .ToList();

            foreach (var session in sessions)
            {
                await _sessionManager.SendPlaystateCommand(
                    null,
                    session.Id,
                    new PlaystateRequest { Command = PlaystateCommand.Stop },
                    CancellationToken.None).ConfigureAwait(false);
            }

            if (sessions.Count > 0)
            {
                _logger.LogInformation(
                    "NoPayNoPlay: stopped {Count} active session(s) for blocked user {UserId}",
                    sessions.Count,
                    userId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NoPayNoPlay: failed to stop active sessions for {UserId}", userId);
        }
    }

    private async Task RestoreAsync(UserSubscription sub)
    {
        var user = _userManager.GetUserById(sub.UserId);
        if (user == null)
        {
            return;
        }

        if (sub.PolicySnapshot is { } snap)
        {
            // Restore each permission to its snapshot value, but only undo what the
            // block actually changed: if the admin flipped one of these flags via
            // the standard Jellyfin UI while the user was blocked, keep the admin's
            // decision instead of silently reverting it to a stale snapshot.
            RestorePermission(user, PermissionKind.EnableMediaPlayback, snap.EnableMediaPlayback, sub.UserId);
            RestorePermission(user, PermissionKind.EnableAudioPlaybackTranscoding, snap.EnableAudioPlaybackTranscoding, sub.UserId);
            RestorePermission(user, PermissionKind.EnableVideoPlaybackTranscoding, snap.EnableVideoPlaybackTranscoding, sub.UserId);
            RestorePermission(user, PermissionKind.EnablePlaybackRemuxing, snap.EnablePlaybackRemuxing, sub.UserId);
        }
        else
        {
            // Snapshot lost (config edited/restored by hand, or migrated from a
            // version that didn't persist it). We can't prove the original
            // transcoding/remux flags, so only re-enable playback — the single
            // permission the block is guaranteed to have cleared — and leave the
            // rest untouched rather than blindly granting everything.
            _logger.LogWarning(
                "NoPayNoPlay: missing policy snapshot for {UserId}; restoring playback only, leaving transcoding/remux unchanged",
                sub.UserId);
            user.SetPermission(PermissionKind.EnableMediaPlayback, true);
        }

        await _userManager.UpdateUserAsync(user).ConfigureAwait(false);

        sub.IsBlocked = false;
        sub.PolicySnapshot = null;
        _logger.LogInformation("NoPayNoPlay: playback restored for {UserId}", sub.UserId);
    }

    private void RestorePermission(
        Jellyfin.Database.Implementations.Entities.User user,
        PermissionKind kind,
        bool snapshotValue,
        Guid userId)
    {
        bool current = user.HasPermission(kind);
        if (current == snapshotValue)
        {
            // Already matches the original state — nothing to undo.
            return;
        }

        if (!current && snapshotValue)
        {
            // The block cleared a permission the user originally had: restore it.
            user.SetPermission(kind, true);
        }
        else
        {
            // current == true while snapshot == false: the admin granted this
            // permission during the block. Preserve their decision and log it.
            _logger.LogInformation(
                "NoPayNoPlay: preserving admin change on {Permission} for {UserId} during restore (snapshot={Snapshot}, current={Current})",
                kind,
                userId,
                snapshotValue,
                current);
        }
    }
}
