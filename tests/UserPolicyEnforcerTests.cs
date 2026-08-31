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
/// Validates <see cref="UserPolicyEnforcer"/>: blocking snapshots the original
/// policy, restore only undoes what the block changed (preserving admin decisions
/// made during the block), administrators/exempt users are never blocked, and
/// active playback sessions are stopped on block.
/// </summary>
public class UserPolicyEnforcerTests
{
    private static User MakeUser(string name = "Test", bool isAdmin = false)
    {
        var user = new User(name, "auth", "pwd");
        user.SetPermission(PermissionKind.EnableMediaPlayback, true);
        user.SetPermission(PermissionKind.EnableAudioPlaybackTranscoding, true);
        user.SetPermission(PermissionKind.EnableVideoPlaybackTranscoding, true);
        user.SetPermission(PermissionKind.EnablePlaybackRemuxing, true);
        if (isAdmin)
        {
            user.SetPermission(PermissionKind.IsAdministrator, true);
        }

        return user;
    }

    private static UserSubscription Sub(Guid userId) => new() { UserId = userId };

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
    public async Task Block_DisablesPlaybackAndSnapshotsOriginalPolicy()
    {
        var user = MakeUser();
        var (enforcer, _, _) = Build(user);
        var sub = Sub(user.Id);

        await enforcer.ApplyAsync(sub, SubscriptionState.Blocked);

        Assert.True(sub.IsBlocked);
        Assert.False(user.HasPermission(PermissionKind.EnableMediaPlayback));
        Assert.False(user.HasPermission(PermissionKind.EnableAudioPlaybackTranscoding));
        Assert.False(user.HasPermission(PermissionKind.EnableVideoPlaybackTranscoding));
        Assert.False(user.HasPermission(PermissionKind.EnablePlaybackRemuxing));
        Assert.NotNull(sub.PolicySnapshot);
        Assert.True(sub.PolicySnapshot!.EnableMediaPlayback);
        Assert.True(sub.PolicySnapshot.EnableAudioPlaybackTranscoding);
        Assert.True(sub.PolicySnapshot.EnableVideoPlaybackTranscoding);
        Assert.True(sub.PolicySnapshot.EnablePlaybackRemuxing);
    }

    [Fact]
    public async Task Block_StopsActivePlaybackSessions()
    {
        var user = MakeUser();
        var (enforcer, _, sessions) = Build(user);
        var sub = Sub(user.Id);
        var session = new SessionInfo(sessions.Object, NullLogger.Instance)
        {
            Id = "sess-1",
            UserId = user.Id,
            NowPlayingItem = new BaseItemDto()
        };
        sessions.SetupGet(s => s.Sessions).Returns(new List<SessionInfo> { session });

        await enforcer.ApplyAsync(sub, SubscriptionState.Blocked);

        sessions.Verify(s => s.SendPlaystateCommand(
            null,
            session.Id,
            It.Is<PlaystateRequest>(r => r.Command == PlaystateCommand.Stop),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Block_DoesNotStopIdleSessions()
    {
        var user = MakeUser();
        var (enforcer, _, sessions) = Build(user);
        var sub = Sub(user.Id);
        // Session exists but nothing is playing.
        sessions.SetupGet(s => s.Sessions).Returns(new List<SessionInfo>
        {
            new SessionInfo(sessions.Object, NullLogger.Instance) { Id = "sess-idle", UserId = user.Id, NowPlayingItem = null }
        });

        await enforcer.ApplyAsync(sub, SubscriptionState.Blocked);

        sessions.Verify(s => s.SendPlaystateCommand(
            It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<PlaystateRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Restore_ReEnablesPlaybackFromSnapshot()
    {
        var user = MakeUser();
        var (enforcer, _, _) = Build(user);
        var sub = Sub(user.Id);

        await enforcer.ApplyAsync(sub, SubscriptionState.Blocked);
        await enforcer.ApplyAsync(sub, SubscriptionState.Ok);

        Assert.False(sub.IsBlocked);
        Assert.Null(sub.PolicySnapshot);
        Assert.True(user.HasPermission(PermissionKind.EnableMediaPlayback));
        Assert.True(user.HasPermission(PermissionKind.EnableAudioPlaybackTranscoding));
        Assert.True(user.HasPermission(PermissionKind.EnableVideoPlaybackTranscoding));
        Assert.True(user.HasPermission(PermissionKind.EnablePlaybackRemuxing));
    }

    [Fact]
    public async Task Restore_PreservesAdminGrantMadeDuringBlock()
    {
        var user = MakeUser();
        // The user originally had NO remux permission; block snapshots that.
        user.SetPermission(PermissionKind.EnablePlaybackRemuxing, false);
        var (enforcer, _, _) = Build(user);
        var sub = Sub(user.Id);

        await enforcer.ApplyAsync(sub, SubscriptionState.Blocked);

        // While blocked, an admin grants remux through the standard Jellyfin UI.
        user.SetPermission(PermissionKind.EnablePlaybackRemuxing, true);

        await enforcer.ApplyAsync(sub, SubscriptionState.Ok);

        // Restore must NOT revert the admin's decision.
        Assert.True(user.HasPermission(PermissionKind.EnablePlaybackRemuxing));
    }

    [Fact]
    public async Task Restore_WithMissingSnapshot_EnablesPlaybackOnly()
    {
        var user = MakeUser();
        // Simulate a config where the snapshot was lost and transcoding was left off.
        user.SetPermission(PermissionKind.EnableAudioPlaybackTranscoding, false);
        user.SetPermission(PermissionKind.EnableVideoPlaybackTranscoding, false);
        user.SetPermission(PermissionKind.EnablePlaybackRemuxing, false);
        var (enforcer, _, _) = Build(user);
        var sub = Sub(user.Id);
        sub.IsBlocked = true;
        sub.PolicySnapshot = null;

        await enforcer.ApplyAsync(sub, SubscriptionState.Ok);

        // Only the permission the block is guaranteed to have cleared is restored.
        Assert.True(user.HasPermission(PermissionKind.EnableMediaPlayback));
        Assert.False(user.HasPermission(PermissionKind.EnableAudioPlaybackTranscoding));
        Assert.False(user.HasPermission(PermissionKind.EnablePlaybackRemuxing));
        Assert.False(sub.IsBlocked);
    }

    [Fact]
    public async Task Admin_IsNeverBlocked_AndGetsRestoredIfFlagged()
    {
        var user = MakeUser(isAdmin: true);
        var (enforcer, _, _) = Build(user);
        var sub = Sub(user.Id);
        sub.IsBlocked = true;
        sub.PolicySnapshot = new UserPolicySnapshot { EnableMediaPlayback = true };

        await enforcer.ApplyAsync(sub, SubscriptionState.Blocked);

        Assert.False(sub.IsBlocked);
        Assert.True(user.HasPermission(PermissionKind.EnableMediaPlayback));
    }

    [Fact]
    public async Task ExemptUser_IsNeverBlocked()
    {
        var user = MakeUser();
        var (enforcer, _, _) = Build(user);
        var sub = Sub(user.Id);
        sub.IsExempt = true;

        await enforcer.ApplyAsync(sub, SubscriptionState.Blocked);

        Assert.False(sub.IsBlocked);
        Assert.True(user.HasPermission(PermissionKind.EnableMediaPlayback));
    }

    [Fact]
    public async Task MissingUser_IsNoOp()
    {
        var user = MakeUser();
        var (enforcer, users, _) = Build(user);
        // Point the manager at a different user so GetUserById returns null.
        users.Setup(u => u.GetUserById(It.IsAny<Guid>())).Returns((Guid id) => Guid.NewGuid() == id ? user : null);
        var sub = Sub(Guid.NewGuid());

        await enforcer.ApplyAsync(sub, SubscriptionState.Blocked);

        Assert.False(sub.IsBlocked);
    }
}
