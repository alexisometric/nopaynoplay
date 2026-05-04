using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.NoPayNoPlay.Configuration;
using Jellyfin.Plugin.NoPayNoPlay.Services;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.NoPayNoPlay.Api;

/// <summary>Charge utile pour valider un paiement.</summary>
public class PaymentDto
{
    [Required] public decimal Amount { get; set; }
    public string Method { get; set; } = string.Empty;
    public int MonthsAdded { get; set; } = 1;
    public string Note { get; set; } = string.Empty;
}

/// <summary>Charge utile pour basculer l'exemption.</summary>
public class ExemptDto
{
    public bool IsExempt { get; set; }
}

/// <summary>Vue résumée d'un utilisateur dans le tableau admin.</summary>
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

/// <summary>Vue retournée pour l'utilisateur courant.</summary>
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
}

/// <summary>API du plugin NoPayNoPlay.</summary>
[ApiController]
[Authorize]
[Route("NoPayNoPlay")]
[Produces("application/json")]
public class NoPayNoPlayController : ControllerBase
{
    private readonly IUserManager _userManager;
    private readonly SubscriptionService _service;
    private readonly UserPolicyEnforcer _enforcer;

    public NoPayNoPlayController(
        IUserManager userManager,
        SubscriptionService service,
        UserPolicyEnforcer enforcer)
    {
        _userManager = userManager;
        _service = service;
        _enforcer = enforcer;
    }

    private static PluginConfiguration Cfg => Plugin.Instance!.Configuration;

    private UserSubscriptionDto Project(UserSubscription sub)
    {
        var user = _userManager.GetUserById(sub.UserId);
        SubscriptionState state = _service.EvaluateState(sub);
        return new UserSubscriptionDto
        {
            UserId = sub.UserId,
            Username = user?.Username ?? "(supprimé)",
            SubscriptionDate = sub.SubscriptionDate,
            ExpiryDate = sub.ExpiryDate,
            IsExempt = sub.IsExempt,
            IsBlocked = sub.IsBlocked,
            State = state.ToString(),
            DaysLeft = (int)Math.Ceiling((sub.ExpiryDate - DateTime.UtcNow).TotalDays),
            Transactions = sub.Transactions.OrderByDescending(t => t.Date).ToList()
        };
    }

    /// <summary>Liste enrichie de toutes les souscriptions (admin).</summary>
    [HttpGet("Users")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<UserSubscriptionDto>> GetUsers()
    {
        // S'assurer que tous les utilisateurs Jellyfin sont représentés.
        foreach (var u in _userManager.Users)
        {
            _service.EnsureUserTracked(u.Id);
        }

        var dto = Cfg.Subscriptions.Select(Project).ToList();
        return Ok(dto);
    }

    /// <summary>Valide un paiement et étend l'échéance.</summary>
    [HttpPost("Users/{userId:guid}/Pay")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<ActionResult<UserSubscriptionDto>> Pay(Guid userId, [FromBody] PaymentDto body)
    {
        UserSubscription sub = _service.ApplyPayment(
            userId,
            body.Amount,
            body.Method,
            Math.Max(1, body.MonthsAdded),
            body.Note);

        SubscriptionState state = _service.EvaluateState(sub);
        await _enforcer.ApplyAsync(sub, state).ConfigureAwait(false);
        _service.Save();

        return Ok(Project(sub));
    }

    /// <summary>Active/désactive l'exemption pour un utilisateur.</summary>
    [HttpPost("Users/{userId:guid}/Exempt")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<ActionResult<UserSubscriptionDto>> Exempt(Guid userId, [FromBody] ExemptDto body)
    {
        UserSubscription sub = _service.SetExempt(userId, body.IsExempt);
        await _enforcer.ApplyAsync(sub, _service.EvaluateState(sub)).ConfigureAwait(false);
        _service.Save();
        return Ok(Project(sub));
    }

    /// <summary>Réinitialise un utilisateur à un nouvel essai.</summary>
    [HttpPost("Users/{userId:guid}/Reset")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<ActionResult<UserSubscriptionDto>> Reset(Guid userId)
    {
        UserSubscription sub = _service.Reset(userId);
        await _enforcer.ApplyAsync(sub, _service.EvaluateState(sub)).ConfigureAwait(false);
        _service.Save();
        return Ok(Project(sub));
    }

    /// <summary>Récupère les paramètres globaux.</summary>
    [HttpGet("Settings")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult<PluginConfiguration> GetSettings() => Ok(Cfg);

    /// <summary>Met à jour les paramètres globaux.</summary>
    [HttpPost("Settings")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult<PluginConfiguration> UpdateSettings([FromBody] PluginConfiguration body)
    {
        Cfg.MonthlyPrice = body.MonthlyPrice;
        Cfg.Currency = body.Currency ?? "EUR";
        Cfg.GraceDays = Math.Max(0, body.GraceDays);
        Cfg.TrialDays = Math.Max(0, body.TrialDays);
        Cfg.WarningDaysBefore = Math.Max(0, body.WarningDaysBefore);
        Cfg.PaypalMeUrl = body.PaypalMeUrl ?? string.Empty;
        Cfg.LydiaUrl = body.LydiaUrl ?? string.Empty;
        Cfg.IbanText = body.IbanText ?? string.Empty;
        Cfg.CustomNote = body.CustomNote ?? string.Empty;
        Plugin.Instance!.SaveConfiguration();
        return Ok(Cfg);
    }

    /// <summary>État de l'utilisateur courant + infos de paiement.</summary>
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

        return Ok(new MeDto
        {
            ExpiryDate = sub.ExpiryDate,
            DaysLeft = (int)Math.Ceiling((sub.ExpiryDate - DateTime.UtcNow).TotalDays),
            State = state.ToString(),
            Price = Cfg.MonthlyPrice,
            Currency = Cfg.Currency,
            PaypalMeUrl = Cfg.PaypalMeUrl,
            LydiaUrl = Cfg.LydiaUrl,
            IbanText = Cfg.IbanText,
            CustomNote = Cfg.CustomNote,
            WarningDaysBefore = Cfg.WarningDaysBefore,
            GraceDays = Cfg.GraceDays
        });
    }

    private Guid? GetCurrentUserId()
    {
        // Jellyfin expose l'id utilisateur dans la claim "Jellyfin-UserId" ou "sub".
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
