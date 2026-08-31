using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.NoPayNoPlay.Api;
using Jellyfin.Plugin.NoPayNoPlay.Configuration;
using Jellyfin.Plugin.NoPayNoPlay.Localization;
using Jellyfin.Plugin.NoPayNoPlay.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.NoPayNoPlay.Tests;

/// <summary>
/// Exercises the <see cref="NoPayNoPlayController"/> API surface: identity from
/// claims (no IDOR), payment recording + idempotency, settings sanitization, bulk
/// caps, bounded activity, self-service rate limiting and promo redemption.
/// The ASP.NET <c>[Authorize]</c> filters are not run in unit tests, so the
/// endpoints are invoked directly with a synthetic <see cref="ClaimsPrincipal"/>.
/// </summary>
public class NoPayNoPlayControllerTests
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

    private static (NoPayNoPlayController Controller, Mock<IUserManager> Users, Mock<IActivityManager> Activity, PluginConfiguration Cfg) Build(
        Guid? currentUserId = null,
        List<User>? users = null)
    {
        var cfg = new PluginConfiguration();
        var svc = TestSupport.Service(cfg);
        users ??= new List<User>();

        var userManager = new Mock<IUserManager>();
        userManager.Setup(u => u.GetUserById(It.IsAny<Guid>())).Returns((Guid id) => users.FirstOrDefault(u => u.Id == id));
        userManager.Setup(u => u.GetUsers()).Returns(users);
        userManager.Setup(u => u.UpdateUserAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

        var sessionManager = new Mock<ISessionManager>();
        sessionManager.SetupGet(s => s.Sessions).Returns(new List<SessionInfo>());
        sessionManager.Setup(s => s.SendPlaystateCommand(
                It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<PlaystateRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var enforcer = new UserPolicyEnforcer(userManager.Object, sessionManager.Object, NullLogger<UserPolicyEnforcer>.Instance);
        var localizer = new Localizer(NullLogger<Localizer>.Instance);
        var rateLimiter = new RateLimiter();
        var activity = new Mock<IActivityManager>();
        activity.Setup(a => a.CreateAsync(It.IsAny<ActivityLog>())).Returns(Task.CompletedTask);

        var controller = new NoPayNoPlayController(
            userManager.Object,
            svc,
            enforcer,
            localizer,
            rateLimiter,
            activity.Object,
            NullLogger<NoPayNoPlayController>.Instance);

        var http = new DefaultHttpContext();
        if (currentUserId.HasValue)
        {
            http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("Jellyfin-UserId", currentUserId.Value.ToString())
            }, "test"));
        }

        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return (controller, userManager, activity, cfg);
    }

    // --- GET /Me ---

    [Fact]
    public void GetMe_ReturnsOwnState_AndIsNotAdmin()
    {
        var user = MakeUser();
        var (controller, _, _, _) = Build(currentUserId: user.Id, users: new List<User> { user });

        var result = Assert.IsType<OkObjectResult>(controller.GetMe().Result);
        var dto = Assert.IsType<MeDto>(result.Value);

        Assert.Equal(user.Id.ToString(), controller.ControllerContext.HttpContext.User.FindFirst("Jellyfin-UserId")?.Value);
        Assert.Equal("Ok", dto.State);
        Assert.False(dto.IsAdmin);
        Assert.False(dto.IsAdminPreview);
        Assert.Equal(user.Username, dto.Username);
    }

    [Fact]
    public void GetMe_Administrator_GetsSamplePreview()
    {
        var admin = MakeUser("Admin", isAdmin: true);
        var (controller, _, _, _) = Build(currentUserId: admin.Id, users: new List<User> { admin });

        var result = Assert.IsType<OkObjectResult>(controller.GetMe().Result);
        var dto = Assert.IsType<MeDto>(result.Value);

        Assert.True(dto.IsAdmin);
        Assert.True(dto.IsAdminPreview);
        Assert.Equal("Exempt", dto.State);
    }

    [Fact]
    public void GetMe_WithoutValidClaim_IsUnauthorized()
    {
        var user = MakeUser();
        var (controller, _, _, _) = Build(currentUserId: null, users: new List<User> { user });

        var result = controller.GetMe().Result;
        Assert.IsType<UnauthorizedResult>(result);
    }

    // --- POST /Users/{id}/Pay ---

    [Fact]
    public async Task Pay_RecordsPaymentAndExtendsExpiry()
    {
        var user = MakeUser();
        var (controller, _, _, _) = Build(users: new List<User> { user });

        var result = Assert.IsType<OkObjectResult>((await controller.Pay(user.Id, new PaymentDto
        {
            Amount = 10m,
            MonthsAdded = 1,
            Method = "PayPal",
            Note = "test"
        })).Result);
        var dto = Assert.IsType<UserSubscriptionDto>(result.Value);

        Assert.Single(dto.Transactions);
        Assert.True(dto.ExpiryDate > DateTime.UtcNow);
    }

    [Fact]
    public async Task Pay_UnknownUser_IsNotFound()
    {
        var user = MakeUser();
        var (controller, _, _, _) = Build(users: new List<User> { user });

        var result = await controller.Pay(Guid.NewGuid(), new PaymentDto { Amount = 10m, MonthsAdded = 1, Method = "Cash" });
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Pay_IsIdempotentWithinWindow()
    {
        var user = MakeUser();
        var (controller, _, _, _) = Build(users: new List<User> { user });
        var dto = new PaymentDto { Amount = 10m, MonthsAdded = 1, Method = "PayPal", IdempotencyKey = "dup-key" };

        var first = await controller.Pay(user.Id, dto);
        var second = await controller.Pay(user.Id, dto);

        var firstDto = Assert.IsType<UserSubscriptionDto>(Assert.IsType<OkObjectResult>(first.Result).Value);
        var secondDto = Assert.IsType<UserSubscriptionDto>(Assert.IsType<OkObjectResult>(second.Result).Value);

        // The second identical call must not record a second transaction.
        Assert.Single(firstDto.Transactions);
        Assert.Single(secondDto.Transactions);
    }

    // --- POST /Settings ---

    [Fact]
    public void UpdateSettings_SanitizesAndClamps()
    {
        var (controller, _, _, cfg) = Build();
        controller.UpdateSettings(new PluginConfiguration
        {
            MonthlyPrice = 99999999m,
            GraceDays = 5000,
            Currency = "EU1",
            PaypalMeUrl = "javascript:alert(1)",
            LydiaUrl = "https://lydia-app.com/pots/ok",
            ContactEmail = "not-an-email",
            UiCultureOverride = "fr"
        });

        Assert.Equal(100000m, cfg.MonthlyPrice);
        Assert.Equal(365, cfg.GraceDays);
        Assert.Equal("EUR", cfg.Currency);
        Assert.Equal(string.Empty, cfg.PaypalMeUrl); // javascript: scheme rejected
        Assert.Equal("https://lydia-app.com/pots/ok", cfg.LydiaUrl);
        Assert.Equal(string.Empty, cfg.ContactEmail); // invalid email rejected
        Assert.Equal("fr", cfg.UiCultureOverride);
    }

    // --- Bulk endpoints ---

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public async Task BulkPay_RejectsEmptyOrOversizedPayloads(int count)
    {
        var (controller, _, _, _) = Build();
        var body = new BulkPaymentDto
        {
            UserIds = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList(),
            Amount = 5m,
            MonthsAdded = 1,
            Method = "Cash"
        };

        var result = await controller.BulkPay(body);
        Assert.IsType<BadRequestResult>(result.Result);
    }

    // --- GET /Activity ---

    [Fact]
    public void GetActivity_RespectsLimit()
    {
        var users = new List<User>();
        for (int i = 0; i < 3; i++)
        {
            users.Add(MakeUser("User" + i));
        }

        var (controller, _, _, cfg) = Build(users: users);
        for (int i = 0; i < users.Count; i++)
        {
            var sub = new UserSubscription { UserId = users[i].Id, ExpiryDate = DateTime.UtcNow.AddDays(10) };
            sub.Transactions.Add(new TransactionEntry { Date = DateTime.UtcNow.AddDays(-1), Amount = 5m, MonthsAdded = 1, Method = "Cash" });
            sub.Transactions.Add(new TransactionEntry { Date = DateTime.UtcNow, Amount = 5m, MonthsAdded = 1, Method = "PayPal" });
            cfg.Subscriptions.Add(sub);
        }

        var result = Assert.IsType<OkObjectResult>(controller.GetActivity(limit: 2).Result);
        var rows = ((System.Collections.IEnumerable)result.Value!).Cast<object>().ToList();

        Assert.Equal(2, rows.Count);
    }

    // --- POST /Me/MarkPaid (self-service, rate-limited) ---

    [Fact]
    public async Task MarkPaid_IsRateLimitedPerUser()
    {
        var user = MakeUser();
        var (controller, _, activity, _) = Build(currentUserId: user.Id, users: new List<User> { user });

        var first = await controller.MarkPaid(new MarkPaidDto { Method = "PayPal" });
        Assert.IsType<OkObjectResult>(first.Result);

        var second = await controller.MarkPaid(new MarkPaidDto { Method = "PayPal" });
        Assert.IsType<ObjectResult>(second.Result);
        var obj = Assert.IsType<ObjectResult>(second.Result);
        Assert.Equal(StatusCodes.Status429TooManyRequests, obj.StatusCode);

        // The admin activity feed was notified once.
        activity.Verify(a => a.CreateAsync(It.IsAny<ActivityLog>()), Times.Once);
    }

    // --- POST /Me/RedeemCode ---

    [Fact]
    public async Task RedeemCode_ValidCode_GrantsMonths()
    {
        var user = MakeUser();
        var (controller, _, _, cfg) = Build(currentUserId: user.Id, users: new List<User> { user });
        cfg.PromoCodes.Add(new PromoCode { Code = "WELCOME", MonthsGranted = 2 });

        var result = Assert.IsType<OkObjectResult>((await controller.RedeemCode(new RedeemCodeDto { Code = "welcome" })).Result);
        var ok = (dynamic)result.Value!;

        Assert.True((bool)ok.ok);
        Assert.Equal(2, (int)ok.monthsAdded);
    }

    [Fact]
    public async Task RedeemCode_InvalidCode_ReportsFailure()
    {
        var user = MakeUser();
        var (controller, _, _, _) = Build(currentUserId: user.Id, users: new List<User> { user });

        var result = Assert.IsType<OkObjectResult>((await controller.RedeemCode(new RedeemCodeDto { Code = "NOPE" })).Result);
        var ok = (dynamic)result.Value!;

        Assert.False((bool)ok.ok);
    }
}
