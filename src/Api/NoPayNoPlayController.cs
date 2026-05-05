using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.NoPayNoPlay.Configuration;
using Jellyfin.Plugin.NoPayNoPlay.Localization;
using Jellyfin.Plugin.NoPayNoPlay.Services;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
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
}

/// <summary>Response for the current user.</summary>
public class MeDto
{
    public DateTime ExpiryDate { get; set; }
    public int DaysLeft { get; set; }
    public string State { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "EUR";
    public string PaypalMeUrl { get; set; } = string.Empty;
    public string LydiaUrl { get; set; } = string.Empty;
    public string IbanText { get; set; } = string.Empty;
    public string CustomNote { get; set; } = string.Empty;
    public int WarningDaysBefore { get; set; }
    public int GraceDays { get; set; }

    /// <summary>Resolved UI culture (e.g. "en", "fr").</summary>
    public string Lang { get; set; } = "en";

    /// <summary>Translation strings for the resolved culture.</summary>
    public IReadOnlyDictionary<string, string> Strings { get; set; } =
        new Dictionary<string, string>();

    /// <summary>Personal payment history (most recent first).</summary>
    public List<TransactionEntry> Transactions { get; set; } = new();

    /// <summary>
    /// True when the response is rendered for an administrator. Administrators have
    /// no real subscription, but the client uses sample values so the modal is
    /// browsable for previewing the user-facing UI.
    /// </summary>
    public bool IsAdminPreview { get; set; }
}

/// <summary>Payload to update an existing transaction.</summary>
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

    public NoPayNoPlayController(
        IUserManager userManager,
        SubscriptionService service,
        UserPolicyEnforcer enforcer,
        Localizer localizer)
    {
        _userManager = userManager;
        _service = service;
        _enforcer = enforcer;
        _localizer = localizer;
    }

    private static PluginConfiguration Cfg => Plugin.Instance!.Configuration;

    private string ResolveCulture()
    {
        // Admin override wins, then headers/server config.
        string? overrideCulture = Cfg.UiCultureOverride;
        if (!string.IsNullOrWhiteSpace(overrideCulture))
        {
            return overrideCulture.Trim().ToLowerInvariant();
        }

        return _localizer.ResolveCulture(HttpContext);
    }

    private UserSubscriptionDto Project(UserSubscription sub, string culture)
    {
        var user = _userManager.GetUserById(sub.UserId);
        SubscriptionState state = _service.EvaluateState(sub);
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
            Transactions = sub.Transactions.OrderByDescending(t => t.Date).ToList()
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
        foreach (var u in _userManager.Users)
        {
            if (u.HasPermission(PermissionKind.IsAdministrator))
            {
                adminIds.Add(u.Id);
                continue;
            }

            _service.EnsureUserTracked(u.Id);
        }

        string culture = ResolveCulture();
        var dto = Cfg.Subscriptions
            .Where(s => !adminIds.Contains(s.UserId))
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
        foreach (var u in _userManager.Users)
        {
            if (u.HasPermission(PermissionKind.IsAdministrator))
            {
                adminIds.Add(u.Id);
            }
        }

        var rows = Cfg.Subscriptions
            .Where(s => !adminIds.Contains(s.UserId))
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
        await _enforcer.ApplyAsync(sub, state).ConfigureAwait(false);
        _service.Save();

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
        await _enforcer.ApplyAsync(sub, _service.EvaluateState(sub)).ConfigureAwait(false);
        _service.Save();
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
        await _enforcer.ApplyAsync(sub, _service.EvaluateState(sub)).ConfigureAwait(false);
        _service.Save();
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

        Cfg.MonthlyPrice = Math.Clamp(body.MonthlyPrice, 0m, 100000m);
        Cfg.Currency = SanitizeCurrency(body.Currency);
        Cfg.GraceDays = Math.Clamp(body.GraceDays, 0, 365);
        Cfg.TrialDays = Math.Clamp(body.TrialDays, 0, 365);
        Cfg.WarningDaysBefore = Math.Clamp(body.WarningDaysBefore, 0, 90);
        Cfg.PaypalMeUrl = SanitizeUrl(body.PaypalMeUrl);
        Cfg.LydiaUrl = SanitizeUrl(body.LydiaUrl);
        Cfg.IbanText = Truncate(body.IbanText, 500);
        Cfg.CustomNote = Truncate(body.CustomNote, 1000);
        Cfg.UiCultureOverride = SanitizeCulture(body.UiCultureOverride);
        Plugin.Instance!.SaveConfiguration();
        return Ok(Cfg);
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
        List<TransactionEntry> transactions = sub.Transactions
            .OrderByDescending(t => t.Date)
            .ToList();

        if (isAdmin)
        {
            // Sample data: subscription "about to expire" + a couple of past payments.
            DateTime now = DateTime.UtcNow;
            expiry = now.AddDays(3);
            daysLeft = 3;
            decimal samplePrice = Cfg.MonthlyPrice > 0 ? Cfg.MonthlyPrice : 10m;
            transactions = new List<TransactionEntry>
            {
                new TransactionEntry
                {
                    Date = now.AddMonths(-1),
                    Amount = samplePrice,
                    MonthsAdded = 1,
                    Method = "PayPal",
                    AdminNote = string.Empty
                },
                new TransactionEntry
                {
                    Date = now.AddMonths(-2),
                    Amount = samplePrice,
                    MonthsAdded = 1,
                    Method = "Bank",
                    AdminNote = string.Empty
                },
                new TransactionEntry
                {
                    Date = now.AddMonths(-3),
                    Amount = samplePrice,
                    MonthsAdded = 1,
                    Method = "Cash",
                    AdminNote = string.Empty
                }
            };
        }

        return Ok(new MeDto
        {
            ExpiryDate = expiry,
            DaysLeft = daysLeft,
            State = state.ToString(),
            Price = Cfg.MonthlyPrice,
            Currency = Cfg.Currency,
            PaypalMeUrl = Cfg.PaypalMeUrl,
            LydiaUrl = Cfg.LydiaUrl,
            IbanText = Cfg.IbanText,
            CustomNote = Cfg.CustomNote,
            WarningDaysBefore = Cfg.WarningDaysBefore,
            GraceDays = Cfg.GraceDays,
            Lang = culture,
            Strings = _localizer.GetBundle(culture),
            Transactions = transactions,
            IsAdminPreview = isAdmin
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
        await _enforcer.ApplyAsync(sub, _service.EvaluateState(sub)).ConfigureAwait(false);
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
        await _enforcer.ApplyAsync(sub, _service.EvaluateState(sub)).ConfigureAwait(false);
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
            await _enforcer.ApplyAsync(sub, _service.EvaluateState(sub)).ConfigureAwait(false);
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
            await _enforcer.ApplyAsync(sub, _service.EvaluateState(sub)).ConfigureAwait(false);
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
            await _enforcer.ApplyAsync(sub, _service.EvaluateState(sub)).ConfigureAwait(false);
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
        foreach (var u in _userManager.Users)
        {
            if (u.HasPermission(PermissionKind.IsAdministrator))
            {
                adminIds.Add(u.Id);
            }
        }

        DateTime now = DateTime.UtcNow;
        DateTime startOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime startOfYearWindow = now.AddMonths(-12);

        decimal totalAllTime = 0m;
        decimal totalThisMonth = 0m;
        decimal total12m = 0m;
        int txAllTime = 0;

        foreach (var sub in Cfg.Subscriptions.Where(s => !adminIds.Contains(s.UserId)))
        {
            foreach (var t in sub.Transactions)
            {
                txAllTime++;
                totalAllTime += t.Amount;
                if (t.Date >= startOfMonth) totalThisMonth += t.Amount;
                if (t.Date >= startOfYearWindow) total12m += t.Amount;
            }
        }

        return Ok(new
        {
            currency = Cfg.Currency,
            revenueThisMonth = totalThisMonth,
            revenueLast12Months = total12m,
            revenueAllTime = totalAllTime,
            transactionCount = txAllTime
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
        foreach (var u in _userManager.Users)
        {
            if (u.HasPermission(PermissionKind.IsAdministrator)) adminIds.Add(u.Id);
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("UserId,Username,State,IsExempt,SubscriptionDate,ExpiryDate,DaysLeft,LastPaymentDate,LastPaymentAmount,LastPaymentMethod,TotalPaid");
        DateTime now = DateTime.UtcNow;
        foreach (var s in Cfg.Subscriptions.Where(s => !adminIds.Contains(s.UserId)))
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
        foreach (var u in _userManager.Users)
        {
            if (u.HasPermission(PermissionKind.IsAdministrator)) adminIds.Add(u.Id);
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Date,UserId,Username,Method,Amount,MonthsAdded,Note");
        var rows = Cfg.Subscriptions
            .Where(s => !adminIds.Contains(s.UserId))
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
        bool needsQuote = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
        string v = value.Replace("\"", "\"\"", StringComparison.Ordinal);
        return needsQuote ? "\"" + v + "\"" : v;
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
}
