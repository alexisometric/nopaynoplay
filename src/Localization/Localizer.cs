using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MediaBrowser.Controller.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.NoPayNoPlay.Localization;

/// <summary>
/// Loads embedded JSON translation files and resolves the active culture
/// based on (in order): explicit lang query string, Accept-Language header,
/// the Jellyfin server UI culture, and English as a final fallback.
/// </summary>
public sealed class Localizer
{
    private const string DefaultCulture = "en";

    private readonly IServerConfigurationManager? _serverConfig;
    private readonly ILogger<Localizer> _logger;
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _bundles;
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _mergedBundles;
    private readonly IReadOnlyDictionary<string, string> _defaultBundle;

    public Localizer(ILogger<Localizer> logger, IServerConfigurationManager? serverConfig = null)
    {
        _logger = logger;
        _serverConfig = serverConfig;
        _bundles = LoadBundles();
        _defaultBundle = _bundles.TryGetValue(DefaultCulture, out var en)
            ? en
            : new Dictionary<string, string>();
        _mergedBundles = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in _bundles)
        {
            _mergedBundles[kv.Key] = ReferenceEquals(kv.Value, _defaultBundle) ? kv.Value : Merge(kv.Value);
        }
    }

    /// <summary>Lists every culture code that ships with the plugin.</summary>
    public IReadOnlyCollection<string> AvailableCultures => _bundles.Keys.ToList();

    /// <summary>Returns the full translation dictionary for the requested culture.</summary>
    public IReadOnlyDictionary<string, string> GetBundle(string? culture)
    {
        string code = NormalizeCulture(culture);
        if (_mergedBundles.TryGetValue(code, out var merged))
        {
            return merged;
        }

        return _defaultBundle;
    }

    /// <summary>Resolves a single key for the given culture, with English fallback.</summary>
    public string Get(string key, string? culture = null)
    {
        var bundle = GetBundle(culture);
        if (bundle.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (_defaultBundle.TryGetValue(key, out var fallback) && !string.IsNullOrEmpty(fallback))
        {
            return fallback;
        }

        return key;
    }

    /// <summary>Convenience overload that performs {token} substitution.</summary>
    public string Get(string key, string? culture, IDictionary<string, string?> tokens)
    {
        string template = Get(key, culture);
        foreach (var kv in tokens)
        {
            template = template.Replace("{" + kv.Key + "}", kv.Value ?? string.Empty, StringComparison.Ordinal);
        }

        return template;
    }

    /// <summary>
    /// Resolves a plural form. Looks up <c>{key}.one</c> when <paramref name="count"/> is 1
    /// and <c>{key}.other</c> otherwise. Falls back to <paramref name="key"/> alone if
    /// the plural variants are missing. Substitutes <c>{n}</c> with the count.
    /// </summary>
    public string GetPlural(string key, long count, string? culture = null)
    {
        string suffix = count == 1 ? ".one" : ".other";
        var bundle = GetBundle(culture);
        string template;
        if (bundle.TryGetValue(key + suffix, out var v) && !string.IsNullOrEmpty(v))
        {
            template = v;
        }
        else if (_defaultBundle.TryGetValue(key + suffix, out var f) && !string.IsNullOrEmpty(f))
        {
            template = f;
        }
        else
        {
            template = Get(key, culture);
        }

        return template.Replace("{n}", count.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves an explicitly-provided culture code (e.g. the admin
    /// <c>UiCultureOverride</c>) to an available bundle, applying the same
    /// region → base fallback (<c>pt-BR</c> → <c>pt</c>) used for request-based
    /// resolution. Falls back to English when nothing matches.
    /// </summary>
    public string ResolveExplicit(string? culture) => MatchAvailable(culture ?? string.Empty);

    /// <summary>Picks the best culture for an HTTP request.</summary>
    public string ResolveCulture(HttpContext? httpContext)
    {
        // 1. Explicit ?lang= query parameter wins.
        if (httpContext is not null
            && httpContext.Request.Query.TryGetValue("lang", out var explicitLang)
            && !string.IsNullOrWhiteSpace(explicitLang))
        {
            string match = MatchAvailable(explicitLang.ToString());
            if (match != DefaultCulture || _bundles.ContainsKey(DefaultCulture))
            {
                return match;
            }
        }

        // 2. Accept-Language header (browser/Jellyfin web).
        if (httpContext is not null)
        {
            string? accept = httpContext.Request.Headers.AcceptLanguage.ToString();
            if (!string.IsNullOrWhiteSpace(accept))
            {
                foreach (var part in accept.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    string code = part.Split(';')[0].Trim();
                    if (string.IsNullOrEmpty(code))
                    {
                        continue;
                    }

                    string match = MatchAvailable(code);
                    if (_bundles.ContainsKey(match))
                    {
                        return match;
                    }
                }
            }
        }

        // 3. Server UI culture (Jellyfin global setting).
        try
        {
            string? serverCulture = _serverConfig?.Configuration?.UICulture;
            if (!string.IsNullOrWhiteSpace(serverCulture))
            {
                string match = MatchAvailable(serverCulture);
                if (_bundles.ContainsKey(match))
                {
                    return match;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read server UI culture");
        }

        return DefaultCulture;
    }

    private string MatchAvailable(string raw)
    {
        string code = NormalizeCulture(raw);
        if (_bundles.ContainsKey(code))
        {
            return code;
        }

        // Map common region codes (en-US, fr-FR, fr-CA…) to the base language.
        int dash = code.IndexOf('-', StringComparison.Ordinal);
        if (dash > 0)
        {
            string baseCode = code[..dash];
            if (_bundles.ContainsKey(baseCode))
            {
                return baseCode;
            }
        }

        return DefaultCulture;
    }

    private static string NormalizeCulture(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return DefaultCulture;
        }

        return raw.Trim().ToLowerInvariant().Replace('_', '-');
    }

    private IReadOnlyDictionary<string, string> Merge(IReadOnlyDictionary<string, string> bundle)
    {
        if (ReferenceEquals(bundle, _defaultBundle))
        {
            return bundle;
        }

        var merged = new Dictionary<string, string>(_defaultBundle);
        foreach (var kv in bundle)
        {
            merged[kv.Key] = kv.Value;
        }

        return merged;
    }

    private static Dictionary<string, IReadOnlyDictionary<string, string>> LoadBundles()
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var assembly = typeof(Localizer).Assembly;
        const string prefix = "Jellyfin.Plugin.NoPayNoPlay.Localization.strings.";
        const string suffix = ".json";

        foreach (string name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal)
                || !name.EndsWith(suffix, StringComparison.Ordinal))
            {
                continue;
            }

            string code = name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length)
                .ToLowerInvariant();

            try
            {
                using Stream stream = assembly.GetManifestResourceStream(name)
                    ?? throw new InvalidOperationException("missing stream for " + name);
                using var reader = new StreamReader(stream);
                string json = reader.ReadToEnd();
                JObject obj = JObject.Parse(json);
                var dict = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var prop in obj.Properties())
                {
                    dict[prop.Name] = prop.Value?.ToString() ?? string.Empty;
                }

                result[code] = dict;
            }
            catch (Exception)
            {
                // Embedded resources are produced at build time; if one fails to parse,
                // skip it rather than crashing the whole plugin.
            }
        }

        return result;
    }
}
