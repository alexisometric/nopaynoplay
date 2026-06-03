using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.NoPayNoPlay.Configuration;
using Jellyfin.Plugin.NoPayNoPlay.Localization;
using Jellyfin.Plugin.NoPayNoPlay.Services;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Activity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.NoPayNoPlay.Api;

/// <summary>Payload to record a payment.</summary>
public class PaymentDto
{
    [Range(0, 100000)]
    public decimal Amount { get; set; }

    [StringLength(50)]
    public string Method { get; set; } = string.Empty;

    [Range(1, 60)]
    public int MonthsAdded { get; set; } = 1;

    [StringLength(500)]
    public string Note { get; set; } = string.Empty;

    /// <summary>
    /// Optional date the payment actually occurred. When provided, this is the date stored in
    /// the transaction history (useful for backfilling past payments). The expiry is still
    /// extended by <see cref="MonthsAdded"/>; only the recorded date changes.
    /// Future dates are clamped to "now".
    /// </summary>
    public DateTime? Date { get; set; }
}

/// <summary>Payload to toggle the exemption flag.</summary>
public class ExemptDto
{
    public bool IsExempt { get; set; }
}

/// <summary>User row used by the admin dashboard.</summary>
public class UserSubscriptionDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public DateTime SubscriptionDate { get; set; }
    public DateTime ExpiryDate { get; set; }
    public bool IsExempt { get; set; }
    public bool IsBlocked { get; set; }
    public string State { get; set; } = string.Empty;
    public int DaysLeft { get; set; }
    public List<TransactionEntry> Transactions { get; set; } = new();

    /// <summary>True when the member has self-declared a payment awaiting confirmation.</summary>
    public bool HasPendingPaymentClaim { get; set; }

    /// <summary>UTC timestamp of the latest pending claim (null when none).</summary>
    public DateTime? PendingPaymentClaimAt { get; set; }

    /// <summary>Method declared by the user when self-claiming.</summary>
    public string PendingPaymentMethod { get; set; } = string.Empty;

    /// <summary>Tag key (family / friends / guests / …); empty when none.</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>Total amount paid by the user across every recorded transaction.</summary>
    public decimal TotalPaid { get; set; }

    /// <summary>Number of full months the user is currently behind on (0 when up to date).</summary>
    public int ArrearsMonths { get; set; }

    /// <summary>Effective monthly price applied to this user (after tag overrides).</summary>
    public decimal EffectiveMonthlyPrice { get; set; }
}
/// <summary>
/// A payment row exposed to the member on <c>/Me</c>. Deliberately omits the
/// admin-only <see cref="TransactionEntry.AdminNote"/> so internal notes are never
/// serialized into a response the member can read via DevTools.
/// </summary>
public class MeTransactionDto
{
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public int MonthsAdded { get; set; }
    public string Method { get; set; } = string.Empty;
}

public class MeDto
{
    /// <summary>The caller's own Jellyfin username (used to build a payment reference).</summary>
    public string Username { get; set; } = string.Empty;

    public DateTime ExpiryDate { get; set; }
    public int DaysLeft { get; set; }
    public string State { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "EUR";
    public string PaypalMeUrl { get; set; } = string.Empty;
    public string LydiaUrl { get; set; } = string.Empty;
    public string CustomNote { get; set; } = string.Empty;
    public int WarningDaysBefore { get; set; }
    public int GraceDays { get; set; }

    /// <summary>Resolved UI culture (e.g. "en", "fr").</summary>
    public string Lang { get; set; } = "en";

    /// <summary>Translation strings for the resolved culture.</summary>
    public IReadOnlyDictionary<string, string> Strings { get; set; } =
        new Dictionary<string, string>();

    /// <summary>Personal payment history (most recent first), without admin notes.</summary>
    public List<MeTransactionDto> Transactions { get; set; } = new();

    /// <summary>
    /// True when the response is rendered for an administrator. Administrators have
    /// no real subscription, but the client uses sample values so the modal is
    /// browsable for previewing the user-facing UI.
    /// </summary>
    public bool IsAdminPreview { get; set; }

    /// <summary>True when the member has a pending self-declared payment.</summary>
    public bool HasPendingPaymentClaim { get; set; }

    /// <summary>UTC timestamp of the pending claim (null when none).</summary>
    public DateTime? PendingPaymentClaimAt { get; set; }

    /// <summary>Method declared by the user when self-claiming.</summary>
    public string PendingPaymentMethod { get; set; } = string.Empty;

    /// <summary>Subscription tiers offered to the user (already filtered for display).</summary>
    public List<SubscriptionTier> Tiers { get; set; } = new();

    /// <summary>Optional contact email — used to build a mailto link in the modal.</summary>
    public string ContactEmail { get; set; } = string.Empty;

    /// <summary>True when the API caller is currently authenticated as a Jellyfin administrator.</summary>
    public bool IsAdmin { get; set; }
}
public class TransactionPatchDto
{
    [Range(0, 100000)]
    public decimal? Amount { get; set; }

    [StringLength(50)]
    public string? Method { get; set; }

    [Range(1, 60)]
    public int? MonthsAdded { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }

    public DateTime? Date { get; set; }
}

/// <summary>Bulk-action payload (list of user IDs).</summary>
public class BulkUserDto
{
    public List<Guid> UserIds { get; set; } = new();
}

/// <summary>Bulk payment payload.</summary>
public class BulkPaymentDto : PaymentDto
{
    public List<Guid> UserIds { get; set; } = new();
}

/// <summary>Bulk exemption payload.</summary>
public class BulkExemptDto : BulkUserDto
{
    public bool IsExempt { get; set; }
}

/// <summary>Self-service "I paid" claim from the user.</summary>
public class MarkPaidDto
{
    [StringLength(50)]
    public string Method { get; set; } = string.Empty;
}

/// <summary>Promo code creation / update payload.</summary>
public class PromoCodeDto
{
    [Required]
    [StringLength(32)]
    public string Code { get; set; } = string.Empty;

    [Range(1, 60)]
    public int MonthsGranted { get; set; } = 1;

    [Range(0, 100000)]
    public int MaxUses { get; set; }

    public DateTime? ExpiresAt { get; set; }
}

/// <summary>Payload to redeem a promo code.</summary>
public class RedeemCodeDto
{
    [Required]
    [StringLength(32)]
    public string Code { get; set; } = string.Empty;
}

/// <summary>Payload to assign / clear a tag on a user.</summary>
public class UserTagAssignmentDto
{
    [StringLength(32)]
    public string Tag { get; set; } = string.Empty;
}

/// <summary>NoPayNoPlay public REST API.</summary>
[ApiController]
[Authorize]
[Route("NoPayNoPlay")]
[Produces("application/json")]
public class NoPayNoPlayController : ControllerBase
{
    private readonly IUserManager _userManager;
    private readonly SubscriptionService _service;
    private readonly UserPolicyEnforcer _enforcer;
    private readonly Localizer _localizer;
    private readonly RateLimiter _rateLimiter;
    private readonly IActivityManager _activityManager;
    private readonly ILogger<NoPayNoPlayController> _logger;

    public NoPayNoPlayController(
        IUserManager userManager,
        SubscriptionService service,
        UserPolicyEnforcer enforcer,
        Localizer localizer,
        RateLimiter rateLimiter,
        IActivityManager activityManager,
        ILogger<NoPayNoPlayController> logger)
    {
        _userManager = userManager;
        _service = service;
        _enforcer = enforcer;
        _localizer = localizer;
        _rateLimiter = rateLimiter;
        _activityManager = activityManager;
        _logger = logger;
    }

    /// <summary>
    /// Applies the playback policy without letting a transient enforcement failure
    /// (DB lock, IO) surface as a 500 *after* the subscription change has already
    /// been persisted. The gap is logged; the 12 h scheduled task reconciles it.
    /// </summary>
    private async Task ApplyEnforcementSafelyAsync(UserSubscription sub, SubscriptionState state)
    {
        try
        {
            await _enforcer.ApplyAsync(sub, state).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "NoPayNoPlay: subscription updated for {UserId} but playback policy could not be applied; the scheduled task will reconcile within 12h",
                sub.UserId);
        }
    }

    private static PluginConfiguration Cfg => Plugin.Instance!.Configuration;

    private string ResolveCulture()
    {
        // Admin override wins, then headers/server config.
        string? overrideCulture = Cfg.UiCultureOverride;
        if (!string.IsNullOrWhiteSpace(overrideCulture))
        {
            // Route through MatchAvailable so a region code (pt-BR) resolves to its
            // base bundle (pt) rather than silently falling back to English.
            return _localizer.ResolveExplicit(overrideCulture);
        }

        return _localizer.ResolveCulture(HttpContext);
    }

    private UserSubscriptionDto Project(UserSubscription sub, string culture)
    {
        var user = _userManager.GetUserById(sub.UserId);
        SubscriptionState state = _service.EvaluateState(sub);
        decimal totalPaid = sub.Transactions.Sum(t => t.Amount);
        return new UserSubscriptionDto
        {
            UserId = sub.UserId,
            Username = user?.Username ?? _localizer.Get("common.deletedUser", culture),
            SubscriptionDate = sub.SubscriptionDate,
            ExpiryDate = sub.ExpiryDate,
            IsExempt = sub.IsExempt,
            IsBlocked = sub.IsBlocked,
            State = state.ToString(),
            DaysLeft = (int)Math.Ceiling((sub.ExpiryDate - DateTime.UtcNow).TotalDays),
            Transactions = sub.Transactions.OrderByDescending(t => t.Date).ToList(),
            HasPendingPaymentClaim = sub.HasPendingPaymentClaim,
            PendingPaymentClaimAt = sub.PendingPaymentClaimAt,
            PendingPaymentMethod = sub.PendingPaymentMethod,
            Tag = sub.Tag ?? string.Empty,
            TotalPaid = totalPaid,
            ArrearsMonths = _service.GetArrearsMonths(sub),
            EffectiveMonthlyPrice = _service.GetEffectiveMonthlyPrice(sub)
        };
    }

    /// <summary>Returns translation strings for the current culture.</summary>
    [HttpGet("Strings")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> GetStrings()
    {
        string culture = ResolveCulture();
        return Ok(new
        {
            lang = culture,
            available = _localizer.AvailableCultures,
            strings = _localizer.GetBundle(culture)
        });
    }

    /// <summary>Returns plugin runtime status (admin).</summary>
    [HttpGet("Status")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> GetStatus()
    {
        return Ok(new
        {
            fileTransformationRegistered = PluginEntryPoint.FileTransformationRegistered,
            trackedUsers = Cfg.Subscriptions.Count
        });
    }

    /// <summary>Returns detailed diagnostics about the File Transformation hookup (admin).</summary>
    [HttpGet("Diagnostics")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> GetDiagnostics()
    {
        var d = PluginEntryPoint.LastDiagnostics;
        return Ok(new
        {
            registered = d.Registered,
            timestamp = d.Timestamp,
            foundAssembly = d.FoundAssembly,
            matchingAssemblies = d.MatchingAssemblies,
            callbackAssembly = d.CallbackAssemblyFullName,
            callbackClass = d.CallbackClass,
            callbackMethod = d.CallbackMethod,
            needsTransformationAck = d.NeedsTransformationAck,
            notes = d.Notes
        });
    }

    /// <summary>Re-attempts File Transformation registration immediately (admin).</summary>
    [HttpPost("RetryRegistration")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> RetryRegistration([FromServices] ILogger<PluginEntryPoint> logger)
    {
        bool ok = PluginEntryPoint.ForceRetry(logger);
        var d = PluginEntryPoint.LastDiagnostics;
        return Ok(new
        {
            ok,
            diagnostics = new
            {
                registered = d.Registered,
                timestamp = d.Timestamp,
                foundAssembly = d.FoundAssembly,
                matchingAssemblies = d.MatchingAssemblies,
                callbackAssembly = d.CallbackAssemblyFullName,
                callbackClass = d.CallbackClass,
                callbackMethod = d.CallbackMethod,
                needsTransformationAck = d.NeedsTransformationAck,
                notes = d.Notes
            }
        });
    }

    /// <summary>Lists every tracked subscription (admin). Administrator accounts are hidden.</summary>
    [HttpGet("Users")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<UserSubscriptionDto>> GetUsers()
    {
        // Make sure every non-admin Jellyfin user is represented.
        var adminIds = new HashSet<Guid>();
        var liveUserIds = new HashSet<Guid>();
        foreach (var u in _userManager.GetUsers())
        {
            if (u.HasPermission(PermissionKind.IsAdministrator))
            {
                adminIds.Add(u.Id);
                continue;
            }

            liveUserIds.Add(u.Id);
            _service.EnsureUserTracked(u.Id);
        }

        string culture = ResolveCulture();
        // Hide subscriptions whose Jellyfin user has been deleted; the orphaned
        // entries are kept in configuration for audit purposes but are not
        // surfaced in the admin list.
        var dto = Cfg.Subscriptions
            .Where(s => !adminIds.Contains(s.UserId) && liveUserIds.Contains(s.UserId))
            .Select(s => Project(s, culture))
            .ToList();
        return Ok(dto);
    }

    /// <summary>Aggregated activity log across all members (admin).</summary>
    [HttpGet("Activity")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> GetActivity()
    {
        var adminIds = new HashSet<Guid>();
        var liveUserIds = new HashSet<Guid>();
        foreach (var u in _userManager.GetUsers())
        {
            if (u.HasPermission(PermissionKind.IsAdministrator))
            {
                adminIds.Add(u.Id);
                continue;
            }

            liveUserIds.Add(u.Id);
        }

        var rows = Cfg.Subscriptions
            .Where(s => !adminIds.Contains(s.UserId) && liveUserIds.Contains(s.UserId))
            .SelectMany(s => s.Transactions.Select(t => new
            {
                t.Id,
                UserId = s.UserId,
                Username = _userManager.GetUserById(s.UserId)?.Username ?? "(deleted)",
                t.Date,
                t.Amount,
                t.MonthsAdded,
                t.Method,
                t.AdminNote
            }))
            .OrderByDescending(t => t.Date)
            .ToList();

        return Ok(rows);
    }

    /// <summary>Records a payment and extends the expiry date.</summary>
    [HttpPost("Users/{userId:guid}/Pay")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<ActionResult<UserSubscriptionDto>> Pay(Guid userId, [FromBody] PaymentDto body)
    {
        if (body is null)
        {
            return BadRequest();
        }

        if (_userManager.GetUserById(userId) is null)
        {
            return NotFound();
        }

        UserSubscription sub = _service.ApplyPayment(
            userId,
            Math.Max(0m, body.Amount),
            body.Method ?? string.Empty,
            Math.Clamp(body.MonthsAdded, 1, 60),
            body.Note ?? string.Empty,
            body.Date);

        SubscriptionState state = _service.EvaluateState(sub);
        await ApplyEnforcementSafelyAsync(sub, state).ConfigureAwait(false);
        _service.Save();
        _service.Audit(ResolveActor(), "payment.add", userId,
            _userManager.GetUserById(userId)?.Username ?? string.Empty,
            $"amount={body.Amount} months={body.MonthsAdded} method={body.Method}");

        return Ok(Project(sub, ResolveCulture()));
    }

    /// <summary>Toggles the exemption flag for a user.</summary>
    [HttpPost("Users/{userId:guid}/Exempt")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<ActionResult<UserSubscriptionDto>> Exempt(Guid userId, [FromBody] ExemptDto body)
    {
        if (body is null)
        {
            return BadRequest();
        }

        if (_userManager.GetUserById(userId) is null)
        {
            return NotFound();
        }

        UserSubscription sub = _service.SetExempt(userId, body.IsExempt);
        await ApplyEnforcementSafelyAsync(sub, _service.EvaluateState(sub)).ConfigureAwait(false);
        _service.Save();
        _service.Audit(ResolveActor(), "exempt.toggle", userId,
            _userManager.GetUserById(userId)?.Username ?? string.Empty,
            "isExempt=" + body.IsExempt);
        return Ok(Project(sub, ResolveCulture()));
    }

    /// <summary>Resets a user back to a fresh trial.</summary>
    [HttpPost("Users/{userId:guid}/Reset")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<ActionResult<UserSubscriptionDto>> Reset(Guid userId)
    {
        if (_userManager.GetUserById(userId) is null)
        {
            return NotFound();
        }

        UserSubscription sub = _service.Reset(userId);
        await ApplyEnforcementSafelyAsync(sub, _service.EvaluateState(sub)).ConfigureAwait(false);
        _service.Save();
        _service.Audit(ResolveActor(), "reset", userId,
            _userManager.GetUserById(userId)?.Username ?? string.Empty, string.Empty);
        return Ok(Project(sub, ResolveCulture()));
    }

    /// <summary>Returns the global plugin settings.</summary>
    [HttpGet("Settings")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult<PluginConfiguration> GetSettings() => Ok(Cfg);

    /// <summary>Updates the global plugin settings.</summary>
    [HttpPost("Settings")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult<PluginConfiguration> UpdateSettings([FromBody] PluginConfiguration body)
    {
        if (body is null)
        {
            return BadRequest();
        }

        _service.MutateConfig(cfg =>
        {
            cfg.MonthlyPrice = Math.Clamp(body.MonthlyPrice, 0m, 100000m);
            cfg.Currency = SanitizeCurrency(body.Currency);
            cfg.GraceDays = Math.Clamp(body.GraceDays, 0, 365);
            cfg.TrialDays = Math.Clamp(body.TrialDays, 0, 365);
            cfg.WarningDaysBefore = Math.Clamp(body.WarningDaysBefore, 0, 90);
            cfg.PaypalMeUrl = SanitizeUrl(body.PaypalMeUrl);
            cfg.LydiaUrl = SanitizeUrl(body.LydiaUrl);
            cfg.CustomNote = Truncate(body.CustomNote, 1000);
            cfg.ContactEmail = SanitizeEmail(body.ContactEmail);
            cfg.UiCultureOverride = SanitizeCulture(body.UiCultureOverride);
            return true;
        });
        return Ok(Cfg);
    }

    private static string SanitizeEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        string trimmed = Truncate(value.Trim(), 254);
        // Minimal RFC-5322-ish validation — enough to reject obvious garbage.
        if (!System.Text.RegularExpressions.Regex.IsMatch(
                trimmed,
                @"^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$"))
        {
            return string.Empty;
        }
        return trimmed;
    }

    private static string SanitizeCurrency(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "EUR";
        }

        string trimmed = value.Trim().ToUpperInvariant();
        if (trimmed.Length > 5 || !System.Text.RegularExpressions.Regex.IsMatch(trimmed, "^[A-Z]{2,5}$"))
        {
            return "EUR";
        }

        return trimmed;
    }

    private static string SanitizeCulture(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string trimmed = value.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(trimmed, "^[a-zA-Z]{2,3}(-[a-zA-Z]{2,4})?$"))
        {
            return string.Empty;
        }

        return trimmed.ToLowerInvariant();
    }

    private static string SanitizeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string trimmed = Truncate(value, 500);
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return uri.ToString();
        }

        return string.Empty;
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= max ? value : value.Substring(0, max);
    }

    /// <summary>Returns the current user's subscription state and payment info.</summary>
    [HttpGet("Me")]
    public ActionResult<MeDto> GetMe()
    {
        Guid? userId = GetCurrentUserId();
        if (userId is null || userId == Guid.Empty)
        {
            return Unauthorized();
        }

        UserSubscription sub = _service.EnsureUserTracked(userId.Value);
        SubscriptionState state = _service.EvaluateState(sub);
        string culture = ResolveCulture();

        // Administrators are always exempt from enforcement, but to let them preview
        // the user-facing modal we return sample data flagged with IsAdminPreview.
        var user = _userManager.GetUserById(userId.Value);
        bool isAdmin = user is not null && user.HasPermission(PermissionKind.IsAdministrator);
        if (isAdmin)
        {
            state = SubscriptionState.Exempt;
        }

        DateTime expiry = sub.ExpiryDate;
        int daysLeft = (int)Math.Ceiling((expiry - DateTime.UtcNow).TotalDays);
        List<MeTransactionDto> transactions = sub.Transactions
            .OrderByDescending(t => t.Date)
            .Select(t => new MeTransactionDto
            {
                Date = t.Date,
                Amount = t.Amount,
                MonthsAdded = t.MonthsAdded,
                Method = t.Method
            })
            .ToList();

        if (isAdmin)
        {
            // Sample data: subscription "about to expire" + a couple of past payments.
            DateTime now = DateTime.UtcNow;
            expiry = now.AddDays(3);
            daysLeft = 3;
            decimal samplePrice = Cfg.MonthlyPrice > 0 ? Cfg.MonthlyPrice : 10m;
            transactions = new List<MeTransactionDto>
            {
                new MeTransactionDto { Date = now.AddMonths(-1), Amount = samplePrice, MonthsAdded = 1, Method = "PayPal" },
                new MeTransactionDto { Date = now.AddMonths(-2), Amount = samplePrice, MonthsAdded = 1, Method = "Bank" },
                new MeTransactionDto { Date = now.AddMonths(-3), Amount = samplePrice, MonthsAdded = 1, Method = "Cash" }
            };
        }

        return Ok(new MeDto
        {
            Username = user?.Username ?? string.Empty,
            ExpiryDate = expiry,
            DaysLeft = daysLeft,
            State = state.ToString(),
            Price = isAdmin ? Cfg.MonthlyPrice : _service.GetEffectiveMonthlyPrice(sub),
            Currency = Cfg.Currency,
            PaypalMeUrl = Cfg.PaypalMeUrl,
            LydiaUrl = Cfg.LydiaUrl,
            CustomNote = Cfg.CustomNote,
            WarningDaysBefore = Cfg.WarningDaysBefore,
            GraceDays = Cfg.GraceDays,
            Lang = culture,
            Strings = _localizer.GetBundle(culture),
            Transactions = transactions,
            IsAdminPreview = isAdmin,
            HasPendingPaymentClaim = sub.HasPendingPaymentClaim && !isAdmin,
            PendingPaymentClaimAt = isAdmin ? null : sub.PendingPaymentClaimAt,
            PendingPaymentMethod = isAdmin ? string.Empty : sub.PendingPaymentMethod,
            Tiers = Cfg.Tiers.OrderBy(t => t.Months).ToList(),
            ContactEmail = Cfg.ContactEmail,
            IsAdmin = isAdmin
        });
    }

    /// <summary>Updates an existing transaction.</summary>
    [HttpPatch("Users/{userId:guid}/Transactions/{txId:guid}")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<ActionResult<UserSubscriptionDto>> UpdateTransaction(
        Guid userId,
        Guid txId,
        [FromBody] TransactionPatchDto body)
    {
        if (body is null) return BadRequest();
        if (_userManager.GetUserById(userId) is null) return NotFound();

        bool ok = _service.UpdateTransaction(
            userId, txId,
            body.Amount,
            body.Method is null ? null : Truncate(body.Method, 50),
            body.MonthsAdded,
            body.Note is null ? null : Truncate(body.Note, 500),
            body.Date);
        if (!ok) return NotFound();

        var sub = _service.EnsureUserTracked(userId);
        await ApplyEnforcementSafelyAsync(sub, _service.EvaluateState(sub)).ConfigureAwait(false);
        _service.Save();
        return Ok(Project(sub, ResolveCulture()));
    }

    /// <summary>Deletes a transaction.</summary>
    [HttpDelete("Users/{userId:guid}/Transactions/{txId:guid}")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<ActionResult<UserSubscriptionDto>> DeleteTransaction(Guid userId, Guid txId)
    {
        if (_userManager.GetUserById(userId) is null) return NotFound();

        bool ok = _service.DeleteTransaction(userId, txId);
        if (!ok) return NotFound();

        var sub = _service.EnsureUserTracked(userId);
        await ApplyEnforcementSafelyAsync(sub, _service.EvaluateState(sub)).ConfigureAwait(false);
        _service.Save();
        return Ok(Project(sub, ResolveCulture()));
    }

    /// <summary>Bulk-records the same payment for several users.</summary>
    [HttpPost("Users/BulkPay")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<ActionResult<object>> BulkPay([FromBody] BulkPaymentDto body)
    {
        if (body is null || body.UserIds.Count == 0) return BadRequest();
        int processed = 0;
        foreach (var uid in body.UserIds.Distinct())
        {
            if (_userManager.GetUserById(uid) is null) continue;
            UserSubscription sub = _service.ApplyPayment(
                uid,
                Math.Max(0m, body.Amount),
                body.Method ?? string.Empty,
                Math.Clamp(body.MonthsAdded, 1, 60),
                body.Note ?? string.Empty,
                body.Date);
            await ApplyEnforcementSafelyAsync(sub, _service.EvaluateState(sub)).ConfigureAwait(false);
            processed++;
        }
        _service.Save();
        return Ok(new { processed });
    }

    /// <summary>Bulk-toggles exemption.</summary>
    [HttpPost("Users/BulkExempt")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<ActionResult<object>> BulkExempt([FromBody] BulkExemptDto body)
    {
        if (body is null || body.UserIds.Count == 0) return BadRequest();
        int processed = 0;
        foreach (var uid in body.UserIds.Distinct())
        {
            if (_userManager.GetUserById(uid) is null) continue;
            UserSubscription sub = _service.SetExempt(uid, body.IsExempt);
            await ApplyEnforcementSafelyAsync(sub, _service.EvaluateState(sub)).ConfigureAwait(false);
            processed++;
        }
        _service.Save();
        return Ok(new { processed });
    }

    /// <summary>Bulk-resets users to a fresh trial.</summary>
    [HttpPost("Users/BulkReset")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<ActionResult<object>> BulkReset([FromBody] BulkUserDto body)
    {
        if (body is null || body.UserIds.Count == 0) return BadRequest();
        int processed = 0;
        foreach (var uid in body.UserIds.Distinct())
        {
            if (_userManager.GetUserById(uid) is null) continue;
            UserSubscription sub = _service.Reset(uid);
            await ApplyEnforcementSafelyAsync(sub, _service.EvaluateState(sub)).ConfigureAwait(false);
            processed++;
        }
        _service.Save();
        return Ok(new { processed });
    }

    /// <summary>Aggregated revenue stats (admin).</summary>
    [HttpGet("Stats")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult<object> GetStats()
    {
        var adminIds = new HashSet<Guid>();
        foreach (var u in _userManager.GetUsers())
        {
            if (u.HasPermission(PermissionKind.IsAdministrator))
            {
                adminIds.Add(u.Id);
            }
        }

        DateTime now = DateTime.UtcNow;
        DateTime startOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        // The 12-month window is anchored on calendar months: it starts on the
        // first day of (currentMonth - 11) so the chart bars and the
        // "last 12 months" total always cover the exact same set of
        // transactions. Using a rolling "now - 12 months" instead would
        // double-count or drop transactions on the edge month.
        DateTime startOfYearWindow = startOfMonth.AddMonths(-11);

        decimal totalAllTime = 0m;
        decimal totalThisMonth = 0m;
        decimal total12m = 0m;
        int txAllTime = 0;

        // Build a 12-month series ending with the current month so the admin
        // page can render an inline bar chart without requesting any third-
        // party JS dependency.
        var monthly = new decimal[12];
        var monthlyLabels = new string[12];
        for (int i = 0; i < 12; i++)
        {
            DateTime m = startOfMonth.AddMonths(-(11 - i));
            monthlyLabels[i] = m.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        }

        foreach (var sub in Cfg.Subscriptions.Where(s => !adminIds.Contains(s.UserId)))
        {
            foreach (var t in sub.Transactions)
            {
                // Compare on UTC ticks regardless of the persisted Kind: dates
                // loaded from the XML configuration come back as Unspecified
                // and must not be reinterpreted as Local during comparison.
                DateTime txUtc = t.Date.Kind == DateTimeKind.Utc
                    ? t.Date
                    : DateTime.SpecifyKind(t.Date, DateTimeKind.Utc);

                txAllTime++;
                totalAllTime += t.Amount;
                if (txUtc >= startOfMonth) totalThisMonth += t.Amount;

                if (txUtc >= startOfYearWindow)
                {
                    total12m += t.Amount;

                    // Bucket into the right month slot. Computed via month
                    // arithmetic so it stays correct across DST and year
                    // boundaries (no string parse roundtrip required).
                    int idx = ((txUtc.Year - startOfYearWindow.Year) * 12)
                              + (txUtc.Month - startOfYearWindow.Month);
                    if (idx >= 0 && idx < 12) monthly[idx] += t.Amount;
                }
            }
        }

        return Ok(new
        {
            currency = Cfg.Currency,
            revenueThisMonth = totalThisMonth,
            revenueLast12Months = total12m,
            revenueAllTime = totalAllTime,
            transactionCount = txAllTime,
            monthlyLabels,
            monthlyAmounts = monthly
        });
    }

    /// <summary>Lightweight health probe (admin).</summary>
    [HttpGet("Health")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult<object> GetHealth()
    {
        var d = PluginEntryPoint.LastDiagnostics;
        return Ok(new
        {
            ok = true,
            version = typeof(NoPayNoPlayController).Assembly.GetName().Version?.ToString() ?? "0",
            ftRegistered = PluginEntryPoint.FileTransformationRegistered,
            ftLastCheck = d.Timestamp,
            trackedUsers = Cfg.Subscriptions.Count,
            serverTime = DateTime.UtcNow
        });
    }

    /// <summary>Exports the members table as CSV (admin).</summary>
    [HttpGet("Users/Export.csv")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [Produces("text/csv")]
    public IActionResult ExportUsersCsv()
    {
        var adminIds = new HashSet<Guid>();
        var liveUserIds = new HashSet<Guid>();
        foreach (var u in _userManager.GetUsers())
        {
            if (u.HasPermission(PermissionKind.IsAdministrator))
            {
                adminIds.Add(u.Id);
                continue;
            }

            liveUserIds.Add(u.Id);
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("UserId,Username,State,IsExempt,SubscriptionDate,ExpiryDate,DaysLeft,LastPaymentDate,LastPaymentAmount,LastPaymentMethod,TotalPaid");
        DateTime now = DateTime.UtcNow;
        foreach (var s in Cfg.Subscriptions.Where(s => !adminIds.Contains(s.UserId) && liveUserIds.Contains(s.UserId)))
        {
            string username = _userManager.GetUserById(s.UserId)?.Username ?? "(deleted)";
            string state = _service.EvaluateState(s).ToString();
            int daysLeft = (int)Math.Ceiling((s.ExpiryDate - now).TotalDays);
            var last = s.Transactions.OrderByDescending(t => t.Date).FirstOrDefault();
            decimal total = s.Transactions.Sum(t => t.Amount);
            sb.AppendLine(string.Join(",", new[]
            {
                s.UserId.ToString(),
                CsvEscape(username),
                state,
                s.IsExempt ? "true" : "false",
                s.SubscriptionDate.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                s.ExpiryDate.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                daysLeft.ToString(System.Globalization.CultureInfo.InvariantCulture),
                last is null ? string.Empty : last.Date.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                last is null ? string.Empty : last.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                last is null ? string.Empty : CsvEscape(last.Method),
                total.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }));
        }
        return File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()), "text/csv; charset=utf-8", "nopaynoplay-users.csv");
    }

    /// <summary>Exports the activity log as CSV (admin).</summary>
    [HttpGet("Activity/Export.csv")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [Produces("text/csv")]
    public IActionResult ExportActivityCsv()
    {
        var adminIds = new HashSet<Guid>();
        var liveUserIds = new HashSet<Guid>();
        foreach (var u in _userManager.GetUsers())
        {
            if (u.HasPermission(PermissionKind.IsAdministrator))
            {
                adminIds.Add(u.Id);
                continue;
            }

            liveUserIds.Add(u.Id);
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Date,UserId,Username,Method,Amount,MonthsAdded,Note");
        var rows = Cfg.Subscriptions
            .Where(s => !adminIds.Contains(s.UserId) && liveUserIds.Contains(s.UserId))
            .SelectMany(s => s.Transactions.Select(t => new
            {
                Username = _userManager.GetUserById(s.UserId)?.Username ?? "(deleted)",
                UserId = s.UserId,
                Tx = t
            }))
            .OrderByDescending(r => r.Tx.Date);

        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(",", new[]
            {
                r.Tx.Date.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                r.UserId.ToString(),
                CsvEscape(r.Username),
                CsvEscape(r.Tx.Method),
                r.Tx.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                r.Tx.MonthsAdded.ToString(System.Globalization.CultureInfo.InvariantCulture),
                CsvEscape(r.Tx.AdminNote)
            }));
        }
        return File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()), "text/csv; charset=utf-8", "nopaynoplay-activity.csv");
    }

    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        // CSV formula-injection guard: a cell starting with =, +, -, @, tab or CR
        // is interpreted as a formula by Excel/LibreOffice/Sheets. Prefix it with a
        // single quote so the spreadsheet treats it as text. The Username comes
        // straight from Jellyfin (user-controlled), so this matters even though the
        // export is admin-only.
        if ("=+-@\t\r".IndexOf(value[0]) >= 0)
        {
            value = "'" + value;
        }

        bool needsQuote = value.IndexOfAny(new[] { ',', '"', '\n', '\r', '\t' }) >= 0
                          || value[0] == '\'';
        string v = value.Replace("\"", "\"\"", StringComparison.Ordinal);
        return needsQuote ? "\"" + v + "\"" : v;
    }

    private static readonly TimeSpan PendingClaimCooldown = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Self-service "I paid" button. Records a pending claim that an admin must
    /// confirm. Rate-limited to one call per 30 minutes per user.
    /// </summary>
    [HttpPost("Me/MarkPaid")]
    public async Task<ActionResult<object>> MarkPaid([FromBody] MarkPaidDto? body)
    {
        Guid? userId = GetCurrentUserId();
        if (userId is null || userId == Guid.Empty) return Unauthorized();

        string key = "markpaid:" + userId.Value.ToString("N");
        if (!_rateLimiter.TryAcquire(key, PendingClaimCooldown))
        {
            TimeSpan remaining = _rateLimiter.Remaining(key, PendingClaimCooldown);
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                ok = false,
                retryAfterSeconds = (int)Math.Ceiling(remaining.TotalSeconds)
            });
        }

        string method = body?.Method is null ? string.Empty : Truncate(body.Method, 50);
        _service.MarkPaymentPending(userId.Value, method);

        // Surface the claim to administrators through the Jellyfin activity feed/bell
        // (in addition to the pending badge on the dashboard).
        try
        {
            string uname = _userManager.GetUserById(userId.Value)?.Username ?? string.Empty;
            string culture = ResolveCulture();
            var tokens = new Dictionary<string, string?> { ["username"] = uname };
            string overview = _localizer.Get("notif.markPaid.body", culture, tokens);
            await _activityManager.CreateAsync(new ActivityLog(
                _localizer.Get("notif.markPaid.title", culture),
                "NoPayNoPlay",
                Guid.Empty)
            {
                Overview = overview,
                ShortOverview = overview,
                LogSeverity = Microsoft.Extensions.Logging.LogLevel.Information
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NoPayNoPlay: failed to post 'I paid' admin activity for {UserId}", userId);
        }

        _service.Save();
        return Ok(new { ok = true });
    }

    /// <summary>
    /// Posts the state-appropriate reminder notification (J-3 / grace / blocked) to a
    /// user's activity feed. Used by the admin "send reminder" actions. Returns true
    /// when a notification was actually posted (skips Ok/Exempt users).
    /// </summary>
    private async Task<bool> PostStateReminderAsync(UserSubscription sub, string username, string culture)
    {
        SubscriptionState state = _service.EvaluateState(sub);
        if (state == SubscriptionState.Ok || state == SubscriptionState.Exempt)
        {
            return false;
        }

        string titleKey = state switch
        {
            SubscriptionState.WarningSoon => "notif.warningSoon.title",
            SubscriptionState.InGrace => "notif.inGrace.title",
            SubscriptionState.Blocked => "notif.blocked.title",
            _ => "plugin.name"
        };
        string bodyKey = state switch
        {
            SubscriptionState.WarningSoon => "notif.warningSoon.body",
            SubscriptionState.InGrace => "notif.inGrace.body",
            SubscriptionState.Blocked => "notif.blocked.body",
            _ => string.Empty
        };

        int daysLeft = (int)Math.Ceiling((sub.ExpiryDate - DateTime.UtcNow).TotalDays);
        var tokens = new Dictionary<string, string?>
        {
            ["username"] = username,
            ["date"] = sub.ExpiryDate.ToString("yyyy-MM-dd"),
            ["days"] = Math.Max(0, daysLeft).ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        string title = _localizer.Get(titleKey, culture);
        string overview = string.IsNullOrEmpty(bodyKey) ? string.Empty : _localizer.Get(bodyKey, culture, tokens);

        await _activityManager.CreateAsync(new ActivityLog(title, "NoPayNoPlay", sub.UserId)
        {
            Overview = overview,
            ShortOverview = overview,
            LogSeverity = state == SubscriptionState.Blocked
                ? Microsoft.Extensions.Logging.LogLevel.Warning
                : Microsoft.Extensions.Logging.LogLevel.Information
        }).ConfigureAwait(false);
        return true;
    }

    /// <summary>Admin: re-sends the current reminder notification to one member.</summary>
    [HttpPost("Users/{userId:guid}/Notify")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<ActionResult<object>> NotifyUser(Guid userId)
    {
        var u = _userManager.GetUserById(userId);
        if (u is null) return NotFound();
        var sub = _service.EnsureUserTracked(userId);
        bool sent = false;
        try
        {
            sent = await PostStateReminderAsync(sub, u.Username, ResolveCulture()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NoPayNoPlay: failed to send reminder to {UserId}", userId);
        }

        _service.Audit(ResolveActor(), "user.notify", userId, u.Username, sent ? "reminder sent" : "no reminder (state ok/exempt)");
        return Ok(new { ok = true, sent });
    }

    /// <summary>Admin: re-sends reminders to a batch of members (skips up-to-date ones).</summary>
    [HttpPost("Users/BulkNotify")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<ActionResult<object>> BulkNotify([FromBody] BulkUserDto body)
    {
        if (body?.UserIds is null) return BadRequest();
        string culture = ResolveCulture();
        int sent = 0;
        foreach (var id in body.UserIds.Distinct())
        {
            var u = _userManager.GetUserById(id);
            if (u is null) continue;
            try
            {
                if (await PostStateReminderAsync(_service.EnsureUserTracked(id), u.Username, culture).ConfigureAwait(false))
                {
                    sent++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "NoPayNoPlay: failed to send reminder to {UserId}", id);
            }
        }

        _service.Audit(ResolveActor(), "users.notify", null, string.Empty, "count=" + sent);
        return Ok(new { ok = true, sent });
    }

    /// <summary>Admin: confirms a pending claim and records a real payment.</summary>
    [HttpPost("Users/{userId:guid}/ConfirmPending")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<ActionResult<UserSubscriptionDto>> ConfirmPending(
        Guid userId,
        [FromBody] PaymentDto body)
    {
        if (body is null) return BadRequest();
        if (_userManager.GetUserById(userId) is null) return NotFound();

        UserSubscription sub = _service.ApplyPayment(
            userId,
            Math.Max(0m, body.Amount),
            string.IsNullOrWhiteSpace(body.Method) ? string.Empty : body.Method,
            Math.Clamp(body.MonthsAdded, 1, 60),
            body.Note ?? string.Empty,
            body.Date);
        await ApplyEnforcementSafelyAsync(sub, _service.EvaluateState(sub)).ConfigureAwait(false);
        _service.Save();
        _service.Audit(ResolveActor(), "pending.confirm", userId,
            _userManager.GetUserById(userId)?.Username ?? string.Empty,
            $"amount={body.Amount} months={body.MonthsAdded}");
        return Ok(Project(sub, ResolveCulture()));
    }

    /// <summary>Admin: rejects a pending claim without recording a payment.</summary>
    [HttpPost("Users/{userId:guid}/RejectPending")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult<UserSubscriptionDto> RejectPending(Guid userId)
    {
        if (_userManager.GetUserById(userId) is null) return NotFound();
        _service.ClearPendingClaim(userId);
        var sub = _service.EnsureUserTracked(userId);
        _service.Audit(ResolveActor(), "pending.reject", userId,
            _userManager.GetUserById(userId)?.Username ?? string.Empty, string.Empty);
        _service.Save();
        return Ok(Project(sub, ResolveCulture()));
    }

    /// <summary>Lists every promo code (admin).</summary>
    [HttpGet("PromoCodes")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult<IEnumerable<PromoCode>> ListPromoCodes() => Ok(Cfg.PromoCodes);

    /// <summary>Creates a new promo code (admin).</summary>
    [HttpPost("PromoCodes")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult<PromoCode> CreatePromoCode([FromBody] PromoCodeDto body)
    {
        if (body is null) return BadRequest();
        string code = SanitizePromoCode(body.Code);
        if (string.IsNullOrEmpty(code)) return BadRequest(new { error = "invalid_code" });

        var promo = new PromoCode
        {
            Code = code,
            MonthsGranted = Math.Clamp(body.MonthsGranted, 1, 60),
            MaxUses = Math.Clamp(body.MaxUses, 0, 100000),
            ExpiresAt = body.ExpiresAt
        };

        bool duplicate = false;
        _service.MutateConfig(cfg =>
        {
            if (cfg.PromoCodes.Any(p => string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase)))
            {
                duplicate = true;
                return false;
            }

            cfg.PromoCodes.Add(promo);
            return true;
        });

        if (duplicate) return Conflict(new { error = "duplicate_code" });
        return Ok(promo);
    }

    /// <summary>Deletes a promo code (admin).</summary>
    [HttpDelete("PromoCodes/{id:guid}")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult DeletePromoCode(Guid id)
    {
        int removed = 0;
        _service.MutateConfig(cfg =>
        {
            removed = cfg.PromoCodes.RemoveAll(p => p.Id == id);
            return removed > 0;
        });
        if (removed == 0) return NotFound();
        return NoContent();
    }

    /// <summary>Self-service redeem of a promo code by the current user.</summary>
    [HttpPost("Me/RedeemCode")]
    public async Task<ActionResult<object>> RedeemCode([FromBody] RedeemCodeDto body)
    {
        Guid? userId = GetCurrentUserId();
        if (userId is null || userId == Guid.Empty) return Unauthorized();
        if (body is null || string.IsNullOrWhiteSpace(body.Code)) return BadRequest();

        string lockKey = "redeem-fail:" + userId.Value.ToString("N");
        string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        string ipKey = "redeem-fail:ip:" + clientIp;
        const string globalKey = "redeem-fail:global";

        // Aggregated throttle: a per-user lockout alone can be sidestepped by
        // creating many accounts, so we also gate on a per-IP and a global failure
        // budget that the per-user reset can't clear.
        if (_rateLimiter.IsLocked(lockKey) || _rateLimiter.IsLocked(ipKey) || _rateLimiter.IsLocked(globalKey))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { ok = false, locked = true });
        }

        // Hard floor between consecutive calls (UX-friendly debounce, prevents
        // the simplest scripted abuse).
        string key = "redeem:" + userId.Value.ToString("N");
        if (!_rateLimiter.TryAcquire(key, TimeSpan.FromSeconds(5)))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new { ok = false });
        }

        int months = _service.RedeemPromoCode(userId.Value, body.Code);
        if (months <= 0)
        {
            // Track failed attempts: 5/user, 20/IP, 100 global within the window all
            // trigger a 15-minute lockout, defeating slow brute-force enumeration.
            bool locked = _rateLimiter.RegisterFailureAndShouldLock(
                lockKey, threshold: 5, lockout: TimeSpan.FromMinutes(15));
            _rateLimiter.RegisterFailureAndShouldLock(ipKey, threshold: 20, lockout: TimeSpan.FromMinutes(15));
            _rateLimiter.RegisterFailureAndShouldLock(globalKey, threshold: 100, lockout: TimeSpan.FromMinutes(15));
            return Ok(new { ok = false, locked });
        }

        // Only clear the per-user counter on success — keep the IP/global budgets so
        // a single guessed valid code can't reset the aggregated protection.
        _rateLimiter.ClearFailures(lockKey);
        var sub = _service.EnsureUserTracked(userId.Value);
        await ApplyEnforcementSafelyAsync(sub, _service.EvaluateState(sub)).ConfigureAwait(false);
        _service.Save();
        return Ok(new { ok = true, monthsAdded = months, expiryDate = sub.ExpiryDate });
    }

    private static string SanitizePromoCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        string normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length > 32) normalized = normalized.Substring(0, 32);
        // Minimum length 6 raises the keyspace enough that enumeration is impractical
        // even before the per-IP/global brute-force throttle kicks in, while still
        // allowing memorable referral codes (e.g. SUMMER, NOEL26).
        if (!System.Text.RegularExpressions.Regex.IsMatch(normalized, "^[A-Z0-9_-]{6,32}$"))
        {
            return string.Empty;
        }
        return normalized;
    }

    private Guid? GetCurrentUserId()
    {
        // Jellyfin exposes the user id via "Jellyfin-UserId", NameIdentifier or "sub".
        var claim = User?.FindFirst("Jellyfin-UserId")
                    ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)
                    ?? User?.FindFirst("sub");
        if (claim != null && Guid.TryParse(claim.Value, out var id))
        {
            return id;
        }

        return null;
    }

    private string ResolveActor()
    {
        Guid? id = GetCurrentUserId();
        if (id is null) return "system";
        return _userManager.GetUserById(id.Value)?.Username ?? "(unknown)";
    }

    // ---------------------------------------------------------------------
    // Subscription tiers (admin CRUD + public read).
    // ---------------------------------------------------------------------

    /// <summary>Lists every configured subscription tier.</summary>
    [HttpGet("Tiers")]
    public ActionResult<IEnumerable<SubscriptionTier>> ListTiers() =>
        Ok(Cfg.Tiers.OrderBy(t => t.Months).ToList());

    /// <summary>Replaces the entire tier list (admin).</summary>
    [HttpPut("Tiers")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult<IEnumerable<SubscriptionTier>> SetTiers([FromBody] List<SubscriptionTier> body)
    {
        if (body is null) return BadRequest();
        var sanitized = body
            .Where(t => t != null)
            .Select(t => new SubscriptionTier
            {
                Id = t.Id == Guid.Empty ? Guid.NewGuid() : t.Id,
                Months = Math.Clamp(t.Months, 1, 60),
                Price = Math.Clamp(t.Price, 0m, 100000m),
                Label = Truncate(t.Label, 64),
                Highlight = t.Highlight
            })
            .OrderBy(t => t.Months)
            .Take(20)
            .ToList();
        // Enforce single highlighted tier.
        bool seen = false;
        foreach (var t in sanitized)
        {
            if (t.Highlight && !seen) { seen = true; continue; }
            t.Highlight = false;
        }
        _service.MutateConfig(cfg => { cfg.Tiers = sanitized; return true; });
        _service.Audit(ResolveActor(), "tiers.update", null, string.Empty,
            "tiers=" + sanitized.Count);
        return Ok(Cfg.Tiers);
    }

    // ---------------------------------------------------------------------
    // User tags / groups (admin CRUD + assignment).
    // ---------------------------------------------------------------------

    /// <summary>Lists every user tag.</summary>
    [HttpGet("Tags")]
    public ActionResult<IEnumerable<UserTag>> ListTags() => Ok(Cfg.Tags);

    /// <summary>Replaces the entire tag list (admin).</summary>
    [HttpPut("Tags")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult<IEnumerable<UserTag>> SetTags([FromBody] List<UserTag> body)
    {
        if (body is null) return BadRequest();
        var sanitized = body
            .Where(t => t != null)
            .Select(t =>
            {
                string key = (t.Key ?? string.Empty).Trim().ToLowerInvariant();
                if (!System.Text.RegularExpressions.Regex.IsMatch(key, "^[a-z0-9_-]{1,32}$"))
                {
                    key = string.Empty;
                }
                return new UserTag
                {
                    Id = t.Id == Guid.Empty ? Guid.NewGuid() : t.Id,
                    Key = key,
                    Label = Truncate(t.Label, 64),
                    MonthlyPriceOverride = Math.Clamp(t.MonthlyPriceOverride, 0m, 100000m),
                    Color = SanitizeColor(t.Color)
                };
            })
            .Where(t => !string.IsNullOrEmpty(t.Key))
            .GroupBy(t => t.Key)
            .Select(g => g.First())
            .Take(50)
            .ToList();
        _service.MutateConfig(cfg => { cfg.Tags = sanitized; return true; });
        _service.Audit(ResolveActor(), "tags.update", null, string.Empty,
            "tags=" + sanitized.Count);
        return Ok(Cfg.Tags);
    }

    private static string SanitizeColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        string trimmed = value.Trim();
        return System.Text.RegularExpressions.Regex.IsMatch(trimmed, "^#[0-9a-fA-F]{3,8}$")
            ? trimmed
            : string.Empty;
    }

    /// <summary>Assigns a tag to a member (admin). Empty value clears it.</summary>
    [HttpPost("Users/{userId:guid}/Tag")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult<UserSubscriptionDto> SetUserTag(Guid userId, [FromBody] UserTagAssignmentDto body)
    {
        if (body is null) return BadRequest();
        if (_userManager.GetUserById(userId) is null) return NotFound();
        var sub = _service.SetTag(userId, body.Tag ?? string.Empty);
        _service.Audit(ResolveActor(), "tag.assign", userId,
            _userManager.GetUserById(userId)?.Username ?? string.Empty,
            "tag=" + (body.Tag ?? string.Empty));
        _service.Save();
        return Ok(Project(sub, ResolveCulture()));
    }

    // ---------------------------------------------------------------------
    // Audit log (admin read).
    // ---------------------------------------------------------------------

    /// <summary>Returns the latest audit entries (admin).</summary>
    [HttpGet("AuditLog")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult<IEnumerable<AuditLogEntry>> GetAuditLog([FromQuery] int limit = 200)
    {
        int n = Math.Clamp(limit, 1, 500);
        var rows = Cfg.AuditLog
            .OrderByDescending(e => e.Timestamp)
            .Take(n)
            .ToList();
        return Ok(rows);
    }

    // ---------------------------------------------------------------------
    // Public health probe (no auth) for monitoring tools (uptime-kuma\u2026).
    // Returns minimal info: status only, no user counts or version.
    // ---------------------------------------------------------------------

    /// <summary>Anonymous health probe — returns 200 "ok" if the plugin is alive.</summary>
    [HttpGet("Public/Health")]
    [AllowAnonymous]
    [Produces("text/plain")]
    public ContentResult PublicHealth()
    {
        return Content("ok", "text/plain; charset=utf-8");
    }
}
