using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.NoPayNoPlay.Api;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace Jellyfin.Plugin.NoPayNoPlay.Tests;

/// <summary>
/// Locks the authorization level of every HTTP action of the plugin API. The
/// attribute is the only thing that protects an endpoint, so a new action added
/// without <c>[Authorize(Policy = RequiresElevation)]</c> would silently be
/// exposed to any authenticated member. Reflection is enough here: the filters
/// run before the action, so the metadata is the contract.
/// </summary>
public class ControllerAuthorizationTests
{
    // Endpoints that must stay reachable without a session, and the only ones:
    // the translation bundle (the in-app banner is rendered before/independently
    // of an admin session) and the liveness probe used by uptime checks.
    private static readonly string[] AnonymousActions = { "GetStrings", "PublicHealth" };

    // Endpoints a regular member needs for the "my subscription" card; they must
    // stay authenticated but must NOT require elevation.
    private static readonly string[] MemberActions = { "GetMe", "ListTiers", "MarkPaid", "RedeemCode" };

    [Fact]
    public void Controller_IsAuthorizedAtClassLevel()
    {
        Assert.NotNull(typeof(NoPayNoPlayController).GetCustomAttribute<AuthorizeAttribute>());
    }

    [Fact]
    public void EveryAction_IsEitherAnonymousByAllowListOrAuthorized()
    {
        var anonymous = new List<string>();
        var members = new List<string>();
        var elevated = new List<string>();

        foreach (var action in HttpActions())
        {
            var allowAnonymous = action.GetCustomAttribute<AllowAnonymousAttribute>() != null;
            var authorize = action.GetCustomAttribute<AuthorizeAttribute>();

            if (allowAnonymous)
            {
                Assert.Null(authorize);
                anonymous.Add(action.Name);
            }
            else if (authorize?.Policy == Policies.RequiresElevation)
            {
                elevated.Add(action.Name);
            }
            else
            {
                Assert.Null(authorize);
                members.Add(action.Name);
            }
        }

        Assert.Equal(AnonymousActions.OrderBy(x => x), anonymous.OrderBy(x => x));
        Assert.Equal(MemberActions.OrderBy(x => x), members.OrderBy(x => x));

        // Everything else must be elevation-gated: a new action that is neither in
        // the anonymous list nor in the member list fails right here.
        Assert.All(elevated, name => Assert.DoesNotContain(name, AnonymousActions));
        Assert.True(elevated.Count > 25, $"expected the admin surface to stay elevation-gated, found {elevated.Count}");
    }

    [Fact]
    public void EveryElevatedAction_UsesTheRequiresElevationPolicy()
    {
        foreach (var action in HttpActions())
        {
            var authorize = action.GetCustomAttribute<AuthorizeAttribute>();
            if (authorize is null || action.GetCustomAttribute<AllowAnonymousAttribute>() != null)
            {
                continue;
            }

            if (MemberActions.Contains(action.Name))
            {
                continue;
            }

            Assert.Equal(Policies.RequiresElevation, authorize.Policy);
        }
    }

    private static IEnumerable<MethodInfo> HttpActions()
        => typeof(NoPayNoPlayController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes().OfType<HttpMethodAttribute>().Any());
}
