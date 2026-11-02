using System;
using System.Collections.Generic;
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
/// <remarks>
/// The enforcer is the only component allowed to change the managed permissions, and
/// it treats the *live* policy as the source of truth rather than the
/// <see cref="UserSubscription.IsBlocked"/> flag: a restored backup, a hand-edited
/// config or a crash between the database write and the config save used to leave the
/// flag and the real permissions out of sync forever. Every call now reconciles the
/// two in both directions, persists the resulting flags, and never overwrites a
/// snapshot it did not create.
/// </remarks>
public class UserPolicyEnforcer
{
    /// <summary>
    /// Permissions cleared while a user is blocked. Playback alone is not enough: an
    /// expired member could still download media and keep watching offline unless the
    /// download and sync permissions are cleared too.
    /// </summary>
    private static readonly PermissionKind[] ManagedPermissions =
    {
        PermissionKind.EnableMediaPlayback,
        PermissionKind.EnableAudioPlaybackTranscoding,
        PermissionKind.EnableVideoPlaybackTranscoding,
        PermissionKind.EnablePlaybackRemuxing,
        PermissionKind.EnableContentDownloading,
        PermissionKind.EnableSyncTranscoding,
        PermissionKind.EnableMediaConversion
    };

    private readonly IUserManager _userManager;
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<UserPolicyEnforcer> _logger;
    private readonly SubscriptionService? _subscriptions;

    /// <param name="userManager">Jellyfin user manager.</param>
    /// <param name="sessionManager">Session manager used to stop active playback.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="subscriptions">
    /// Optional subscription service used to persist the state right after a policy
    /// change. When omitted (unit tests), the caller is responsible for saving.
    /// </param>
    public UserPolicyEnforcer(
        IUserManager userManager,
        ISessionManager sessionManager,
        ILogger<UserPolicyEnforcer> logger,
        SubscriptionService? subscriptions = null)
    {
        _userManager = userManager;
        _sessionManager = sessionManager;
        _logger = logger;
        _subscriptions = subscriptions;
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

        // Administrators and exempt users are never blocked; reconcile them out of a
        // stale block instead of leaving the flag set forever.
        bool privileged = user.HasPermission(PermissionKind.IsAdministrator) || sub.IsExempt;
        bool shouldBlock = !privileged && state == SubscriptionState.Blocked;

        bool changed = shouldBlock
            ? await BlockAsync(sub, user).ConfigureAwait(false)
            : await RestoreAsync(sub, user).ConfigureAwait(false);

        if (changed)
        {
            Persist();
        }
    }

    private async Task<bool> BlockAsync(UserSubscription sub, Jellyfin.Database.Implementations.Entities.User user)
    {
        bool changed = false;

        // Snapshot before modification — but only once. Taking a new snapshot while a
        // block is already applied records the *blocked* values (all false), which
        // would then be "restored" to false forever after a payment.
        if (sub.PolicySnapshot == null)
        {
            var snapshot = new UserPolicySnapshot();
            bool anyGranted = false;
            foreach (PermissionKind kind in ManagedPermissions)
            {
                bool granted = user.HasPermission(kind);
                SetSnapshotValue(snapshot, kind, granted);
                anyGranted |= granted;
            }

            // All managed permissions already cleared: this is not a real pre-block
            // state (most likely a restored config or an upgrade that lost the
            // snapshot). Flag it so the restore path re-enables access instead of
            // restoring "false" values and keeping the user blocked forever.
            snapshot.Inferred = !anyGranted;
            sub.PolicySnapshot = snapshot;
            changed = true;
        }

        bool policyChanged = false;
        foreach (PermissionKind kind in ManagedPermissions)
        {
            if (user.HasPermission(kind))
            {
                user.SetPermission(kind, false);
                policyChanged = true;
            }
        }

        if (policyChanged)
        {
            await _userManager.UpdateUserAsync(user).ConfigureAwait(false);
            changed = true;
        }

        bool newlyBlocked = !sub.IsBlocked;
        if (newlyBlocked)
        {
            sub.IsBlocked = true;
            changed = true;
        }

        if (newlyBlocked || policyChanged)
        {
            _logger.LogInformation("NoPayNoPlay: playback blocked for {UserId}", sub.UserId);
        }

        // Clearing the permissions only stops NEW playback decisions; a session that
        // is already streaming (especially direct-play clients that never re-check
        // the policy) would keep playing until the next scheduled pass, up to 12 h
        // later. Proactively stop the user's active sessions when the block becomes
        // effective.
        if (newlyBlocked)
        {
            await StopActiveSessionsAsync(sub.UserId).ConfigureAwait(false);
        }

        return changed;
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
                // Bounded token: a slow/unresponsive session must not hold the
                // enforcement pass hostage.
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _sessionManager.SendPlaystateCommand(
                    null,
                    session.Id,
                    new PlaystateRequest { Command = PlaystateCommand.Stop },
                    timeout.Token).ConfigureAwait(false);
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

    private async Task<bool> RestoreAsync(UserSubscription sub, Jellyfin.Database.Implementations.Entities.User user)
    {
        // Only touch users the plugin is actually managing. Without a snapshot, a
        // block flag, or an all-cleared managed policy, this is an account whose
        // permissions were chosen by the administrator: leave them alone.
        bool hasSnapshot = sub.PolicySnapshot != null;
        bool managed = hasSnapshot || sub.IsBlocked || AllManagedPermissionsDisabled(user);
        if (!managed)
        {
            return false;
        }

        bool changed = false;
        bool policyChanged = false;

        UserPolicySnapshot? snap = sub.PolicySnapshot;
        bool restoreAll = snap == null || snap.Inferred;
        if (restoreAll)
        {
            // Missing or inferred snapshot: we cannot prove the original values, so
            // restore access for every managed permission. Keeping the user locked
            // out of content they paid for is far worse than granting a permission
            // the admin had denied before the block.
            if (snap?.Inferred == true)
            {
                _logger.LogWarning(
                    "NoPayNoPlay: inferred policy snapshot for {UserId} (state was already blocked); re-enabling managed permissions on restore",
                    sub.UserId);
            }
            else
            {
                _logger.LogWarning(
                    "NoPayNoPlay: missing policy snapshot for {UserId}; re-enabling managed permissions on restore",
                    sub.UserId);
            }

            foreach (PermissionKind kind in ManagedPermissions)
            {
                if (!user.HasPermission(kind))
                {
                    user.SetPermission(kind, true);
                    policyChanged = true;
                }
            }
        }
        else
        {
            foreach (PermissionKind kind in ManagedPermissions)
            {
                policyChanged |= RestorePermission(user, kind, GetSnapshotValue(snap!, kind), sub.UserId);
            }
        }

        if (policyChanged)
        {
            await _userManager.UpdateUserAsync(user).ConfigureAwait(false);
            changed = true;
        }

        if (sub.IsBlocked)
        {
            sub.IsBlocked = false;
            changed = true;
            _logger.LogInformation("NoPayNoPlay: playback restored for {UserId}", sub.UserId);
        }

        if (hasSnapshot)
        {
            sub.PolicySnapshot = null;
            changed = true;
        }

        return changed;
    }

    private bool RestorePermission(
        Jellyfin.Database.Implementations.Entities.User user,
        PermissionKind kind,
        bool snapshotValue,
        Guid userId)
    {
        bool current = user.HasPermission(kind);
        if (current == snapshotValue)
        {
            // Already matches the original state — nothing to undo.
            return false;
        }

        if (!current && snapshotValue)
        {
            // The block cleared a permission the user originally had: restore it.
            user.SetPermission(kind, true);
            return true;
        }

        // current == true while snapshot == false: the admin granted this permission
        // during the block. Preserve their decision and log it.
        _logger.LogInformation(
            "NoPayNoPlay: preserving admin change on {Permission} for {UserId} during restore (snapshot={Snapshot}, current={Current})",
            kind,
            userId,
            snapshotValue,
            current);
        return false;
    }

    private static bool AllManagedPermissionsDisabled(Jellyfin.Database.Implementations.Entities.User user)
    {
        foreach (PermissionKind kind in ManagedPermissions)
        {
            if (user.HasPermission(kind))
            {
                return false;
            }
        }

        return true;
    }

    private void Persist()
    {
        // Persist the flags together with the policy change: without this, a restart
        // between the database write and the next scheduled save leaves the config
        // claiming "not blocked" while the permissions are gone.
        try
        {
            _subscriptions?.Save();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NoPayNoPlay: failed to persist policy state after enforcement");
        }
    }

    private static bool GetSnapshotValue(UserPolicySnapshot snapshot, PermissionKind kind) => kind switch
    {
        PermissionKind.EnableMediaPlayback => snapshot.EnableMediaPlayback,
        PermissionKind.EnableAudioPlaybackTranscoding => snapshot.EnableAudioPlaybackTranscoding,
        PermissionKind.EnableVideoPlaybackTranscoding => snapshot.EnableVideoPlaybackTranscoding,
        PermissionKind.EnablePlaybackRemuxing => snapshot.EnablePlaybackRemuxing,
        PermissionKind.EnableContentDownloading => snapshot.EnableContentDownloading,
        PermissionKind.EnableSyncTranscoding => snapshot.EnableSyncTranscoding,
        PermissionKind.EnableMediaConversion => snapshot.EnableMediaConversion,
        _ => false
    };

    private static void SetSnapshotValue(UserPolicySnapshot snapshot, PermissionKind kind, bool value)
    {
        switch (kind)
        {
            case PermissionKind.EnableMediaPlayback:
                snapshot.EnableMediaPlayback = value;
                break;
            case PermissionKind.EnableAudioPlaybackTranscoding:
                snapshot.EnableAudioPlaybackTranscoding = value;
                break;
            case PermissionKind.EnableVideoPlaybackTranscoding:
                snapshot.EnableVideoPlaybackTranscoding = value;
                break;
            case PermissionKind.EnablePlaybackRemuxing:
                snapshot.EnablePlaybackRemuxing = value;
                break;
            case PermissionKind.EnableContentDownloading:
                snapshot.EnableContentDownloading = value;
                break;
            case PermissionKind.EnableSyncTranscoding:
                snapshot.EnableSyncTranscoding = value;
                break;
            case PermissionKind.EnableMediaConversion:
                snapshot.EnableMediaConversion = value;
                break;
        }
    }
}
