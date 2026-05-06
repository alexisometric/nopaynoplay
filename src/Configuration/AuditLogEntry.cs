using System;

namespace Jellyfin.Plugin.NoPayNoPlay.Configuration;

/// <summary>
/// Single audit-log entry: who did what, when, on whom.
/// </summary>
public class AuditLogEntry
{
    /// <summary>Stable identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>UTC timestamp of the action.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Username of the admin who performed the action (or "system").</summary>
    public string Actor { get; set; } = string.Empty;

    /// <summary>Action key (e.g. "payment.add", "payment.edit", "tx.delete", "tag.assign").</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Optional user the action targeted.</summary>
    public Guid? TargetUserId { get; set; }

    /// <summary>Cached username at the time of the action (target may have been deleted since).</summary>
    public string TargetUsername { get; set; } = string.Empty;

    /// <summary>Free-form contextual details (e.g. amount + months, tag key…). Truncated to 500 chars.</summary>
    public string Details { get; set; } = string.Empty;
}
