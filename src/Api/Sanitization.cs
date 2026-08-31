using System;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.NoPayNoPlay.Api;

/// <summary>
/// Input-sanitization helpers shared by the plugin's API surface. Kept in a single
/// static class so every endpoint applies identical rules (URLs, emails, codes,
/// colours, CSV escaping, HTML encoding) and the rules are unit-testable without
/// instantiating a controller.
/// </summary>
public static class Sanitization
{
    /// <summary>Truncates a string to at most <paramref name="max"/> characters.</summary>
    public static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= max ? value : value.Substring(0, max);
    }

    /// <summary>
    /// Validates and normalizes an email address (minimal RFC-5322-ish check — enough to
    /// reject obvious garbage). Returns the trimmed address or an empty string when invalid.
    /// </summary>
    public static string SanitizeEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        string trimmed = Truncate(value.Trim(), 254);
        // Minimal RFC-5322-ish validation — enough to reject obvious garbage.
        if (!Regex.IsMatch(trimmed, @"^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$"))
        {
            return string.Empty;
        }

        return trimmed;
    }

    /// <summary>
    /// Normalizes a currency code (uppercase, 2–5 letters) or returns "EUR" when invalid.
    /// </summary>
    public static string SanitizeCurrency(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "EUR";
        }

        string trimmed = value.Trim().ToUpperInvariant();
        if (trimmed.Length > 5 || !Regex.IsMatch(trimmed, "^[A-Z]{2,5}$"))
        {
            return "EUR";
        }

        return trimmed;
    }

    /// <summary>
    /// Normalizes a culture override (e.g. "fr" or "pt-BR") or returns an empty string when invalid.
    /// </summary>
    public static string SanitizeCulture(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string trimmed = value.Trim();
        if (!Regex.IsMatch(trimmed, "^[a-zA-Z]{2,3}(-[a-zA-Z]{2,4})?$"))
        {
            return string.Empty;
        }

        return trimmed.ToLowerInvariant();
    }

    /// <summary>
    /// Accepts only absolute http/https URLs — everything else (including a
    /// <c>javascript:</c> scheme) is rejected with an empty string. This is the server-side
    /// boundary that keeps the client-rendered payment links safe.
    /// </summary>
    public static string SanitizeUrl(string? value)
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

    /// <summary>
    /// Normalizes a promo code (uppercase, 6–32 chars of A-Z / 0-9 / _ / -).
    /// Minimum length 6 raises the keyspace enough that enumeration is impractical even
    /// before the per-IP/global brute-force throttle kicks in.
    /// </summary>
    public static string SanitizePromoCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        string normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length > 32) normalized = normalized.Substring(0, 32);
        if (!Regex.IsMatch(normalized, "^[A-Z0-9_-]{6,32}$"))
        {
            return string.Empty;
        }

        return normalized;
    }

    /// <summary>Validates a CSS hex colour (#RGB, #RRGGBB, #RRGGBBAA) or returns an empty string.</summary>
    public static string SanitizeColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        string trimmed = value.Trim();
        return Regex.IsMatch(trimmed, "^#[0-9a-fA-F]{3,8}$")
            ? trimmed
            : string.Empty;
    }

    /// <summary>
    /// CSV-escapes a value: a leading formula character (<c>= + - @ tab CR</c>) is prefixed
    /// with a single quote so Excel/LibreOffice/Sheets treats it as text, and the value is
    /// quoted when it contains separators, quotes or newlines.
    /// </summary>
    public static string CsvEscape(string? value)
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

    /// <summary>HTML-encodes a string so it is safe to embed in markup (e.g. the activity feed).</summary>
    public static string HtmlEncode(string? value) =>
        System.Net.WebUtility.HtmlEncode(value ?? string.Empty);
}
