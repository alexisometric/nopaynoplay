using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.NoPayNoPlay.Configuration;
using Jellyfin.Plugin.NoPayNoPlay.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.NoPayNoPlay.Tests;

/// <summary>
/// Regressions for the enforcement state machine: every permission a block removes
/// must be restored, a snapshot taken before the block must never be overwritten by
/// a later pass, an unrecoverable snapshot must not leave a paying member blocked,
/// and the active sessions must only be stopped when the block becomes effective.
/// </summary>
public class EnforcementRegressionTests
{
    private static readonly PermissionKind[] Managed =
    {
        PermissionKind.EnableMediaPlayback,
        PermissionKind.EnableAudioPlaybackTranscoding,
        PermissionKind.EnableVideoPlaybackTranscoding,
        PermissionKind.EnablePlaybackRemuxing,
        PermissionKind.EnableContentDownloading,
        PermissionKind.EnableSyncTranscoding,
        PermissionKind.EnableMediaConversion
    };

    private static User MakeUser()
    {
        var user = new User("Test", "auth", "pwd");
        foreach (PermissionKind kind in Managed)
        {
            user.SetPermission(kind, true);
        }

        return user;
    }

    private static (UserPolicyEnforcer Enforcer, Mock<IUserManager> Users, Mock<ISessionManager> Sessions) Build(User user)
    {
        var users = new Mock<IUserManager>();
        users.Setup(u => u.GetUserById(It.IsAny<Guid>())).Returns((Guid id) => user.Id == id ? user : null);
        users.Setup(u => u.UpdateUserAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

        var sessions = new Mock<ISessionManager>();
        sessions.SetupGet(s => s.Sessions).Returns(new List<SessionInfo>());
        sessions.Setup(s => s.SendPlaystateCommand(
                It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<PlaystateRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var enforcer = new UserPolicyEnforcer(
            users.Object,
            sessions.Object,
            NullLogger<UserPolicyEnforcer>.Instance);

        return (enforcer, users, sessions);
    }

    [Fact]
    public async Task Block_ClearsEveryManagedPermission()
    {
        var user = MakeUser();
        var (enforcer, _, _) = Build(user);
        var sub = new UserSubscription { UserId = user.Id };

        await enforcer.ApplyAsync(sub, SubscriptionState.Blocked);

        foreach (PermissionKind kind in Managed)
        {
            Assert.False(user.HasPermission(kind), $"{kind} must be revoked by the block");
        }
    }

    [Fact]
    public async Task Restore_ReturnsEveryManagedPermissionToItsOriginalValue()
    {
        var user = MakeUser();
        // The admin denied downloads before the block: restoring "true" for everything
        // would silently re-grant a permission the admin had taken away.
        user.SetPermission(PermissionKind.EnableContentDownloading, false);
        var (enforcer, _, _) = Build(user);
        var sub = new UserSubscription { UserId = user.Id };

        await enforcer.ApplyAsync(sub, SubscriptionState.Blocked);
        Assert.False(user.HasPermission(PermissionKind.EnableContentDownloading));

        await enforcer.ApplyAsync(sub, SubscriptionState.Ok);

        for (int i = 0; i < Managed.Length; i++)
        {
            bool expected = Managed[i] != PermissionKind.EnableContentDownloading;
            Assert.Equal(expected, user.HasPermission(Managed[i]));
        }
    }

    [Fact]
    public async Task Block_DoesNotOverwriteSnapshotOnRepeatedPasses()
    {
        var user = MakeUser();
        user.SetPermission(PermissionKind.EnableContentDownloading, false);
        var (enforcer, _, _) = Build(user);
        var sub = new UserSubscription { UserId = user.Id };

        await enforcer.ApplyAsync(sub, SubscriptionState.Blocked);
        UserPolicySnapshot original = sub.PolicySnapshot!;

        // The admin grants downloads while the user is blocked, then a scheduled pass
        // runs again. Recording a fresh snapshot from the (now cleared) policy would
        // both lose the original value and mark the snapshot as inferred.
        user.SetPermission(PermissionKind.EnableContentDownloading, true);
        await enforcer.ApplyAsync(sub, SubscriptionState.Blocked);

        Assert.Same(original, sub.PolicySnapshot);
        Assert.False(sub.PolicySnapshot!.Inferred);
        Assert.False(user.HasPermission(PermissionKind.EnableContentDownloading));
    }

    [Fact]
    public async Task Restore_WithInferredSnapshot_ReEnablesEverything()
    {
        var user = MakeUser();
        foreach (PermissionKind kind in Managed)
        {
            user.SetPermission(kind, false);
        }

        var (enforcer, _, _) = Build(user);
        var sub = new UserSubscription
        {
            UserId = user.Id,
            IsBlocked = true,
            PolicySnapshot = new UserPolicySnapshot { Inferred = true }
        };

        await enforcer.ApplyAsync(sub, SubscriptionState.Ok);

        foreach (PermissionKind kind in Managed)
        {
            Assert.True(user.HasPermission(kind), $"{kind} must be restored from an inferred snapshot");
        }

        Assert.False(sub.IsBlocked);
        Assert.Null(sub.PolicySnapshot);
    }

    [Fact]
    public async Task Block_StopsSessionsOnlyWhenTheBlockTakesEffect()
    {
        var user = MakeUser();
        var (enforcer, _, sessions) = Build(user);
        var sub = new UserSubscription { UserId = user.Id };
        var session = new SessionInfo(sessions.Object, NullLogger.Instance)
        {
            Id = "sess-1",
            UserId = user.Id,
            NowPlayingItem = new BaseItemDto()
        };
        sessions.SetupGet(s => s.Sessions).Returns(new List<SessionInfo> { session });

        await enforcer.ApplyAsync(sub, SubscriptionState.Blocked);
        await enforcer.ApplyAsync(sub, SubscriptionState.Blocked);

        // A second pass must not interrupt the user's playback again (or, worse, keep
        // stopping sessions that a still-blocked member legitimately cannot use).
        sessions.Verify(s => s.SendPlaystateCommand(
            null,
            session.Id,
            It.Is<PlaystateRequest>(r => r.Command == PlaystateCommand.Stop),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Apply_DoesNothingForAnUntouchedAccountWithoutSnapshot()
    {
        var user = MakeUser();
        var (enforcer, users, _) = Build(user);
        var sub = new UserSubscription { UserId = user.Id };

        await enforcer.ApplyAsync(sub, SubscriptionState.Ok);

        users.Verify(u => u.UpdateUserAsync(It.IsAny<User>()), Times.Never);
        Assert.False(sub.IsBlocked);
    }
}
