using System;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.NoPayNoPlay.Web;

/// <summary>
/// Callback invoked by the "File Transformation" plugin to patch index.html.
/// Inserts the script tag right before the closing &lt;/body&gt; element.
/// </summary>
/// <remarks>
/// Contract (see <c>TransformationHelper.ApplyTransformation</c> in the
/// File Transformation plugin source): the callback is a public static method
/// that takes a payload object exposing a <c>contents</c> property and
/// **returns the transformed contents as a string**. Mutating the payload
/// in-place is not enough — File Transformation casts the return value:
/// <c>(string)method.Invoke(null, new object?[] { paramObj })</c>.
/// </remarks>
public static class WebTransformer
{
    /// <summary>
    /// Returns the &lt;script&gt; tag to inject. The src URL embeds the plugin
    /// version as a cache-buster so updating the plugin invalidates browser
    /// caches without requiring a hard refresh from end users.
    /// </summary>
    private static string BuildScriptTag()
    {
        string version = typeof(WebTransformer).Assembly.GetName().Version?.ToString() ?? "0";
        return "<script defer src=\"/NoPayNoPlay/Web/client.js?v=" + version + "\"></script>";
    }

    /// <summary>Static method matching the File Transformation callback contract.</summary>
    /// <param name="payload">JSON object provided by File Transformation, holding the original file contents.</param>
    /// <returns>The patched file contents.</returns>
    public static string Transform(JObject payload)
    {
        string contents = payload?["contents"]?.ToString() ?? string.Empty;
        if (string.IsNullOrEmpty(contents))
        {
            return contents;
        }

        // Defence-in-depth: File Transformation falls back to regex matching when no
        // exact-key match is found, and our literal pattern "index.html" matches any
        // file whose name contains "index<ANY>html" — including JS chunks like
        // "itemDetails-index-html.<hash>.chunk.js". Those chunks may carry inline HTML
        // templates with a "</body>" substring, so blindly inserting our <script> tag
        // would corrupt the JS source and break the web client.
        // Only act on payloads that *look* like a standalone HTML document.
        if (!LooksLikeHtmlDocument(contents))
        {
            return contents;
        }

        // Strip any previously injected NoPayNoPlay script tag (older version,
        // cached content, …) so we can re-emit a tag with the current version
        // suffix. This keeps the cache-buster effective across updates.
        contents = System.Text.RegularExpressions.Regex.Replace(
            contents,
            "<script[^>]*src=\"/NoPayNoPlay/Web/client\\.js[^\"]*\"[^>]*></script>\\s*",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        string scriptTag = BuildScriptTag();
        int idx = contents.LastIndexOf("</body>", System.StringComparison.OrdinalIgnoreCase);
        return idx >= 0
            ? contents.Insert(idx, scriptTag + "\n")
            : contents + scriptTag;
    }

    /// <summary>
    /// Heuristic: returns true only when the payload's first non-whitespace bytes
    /// start with an HTML preamble (<c>&lt;!doctype html&gt;</c> or <c>&lt;html</c>).
    /// JS chunks emitted by Webpack always start with tokens such as
    /// <c>(self.webpackChunkjellyfin_web=…</c> or <c>"use strict";</c>, so they fail
    /// this check even when they happen to contain a <c>&lt;/body&gt;</c> substring
    /// inside an inline HTML template.
    /// </summary>
    private static bool LooksLikeHtmlDocument(string contents)
    {
        int i = 0;
        while (i < contents.Length && char.IsWhiteSpace(contents[i]))
        {
            i++;
        }
        if (i >= contents.Length || contents[i] != '<')
        {
            return false;
        }

        // Compare the next few characters case-insensitively against expected HTML preambles.
        ReadOnlySpan<char> head = contents.AsSpan(i, System.Math.Min(contents.Length - i, 16));
        return head.StartsWith("<!doctype html", System.StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("<html", System.StringComparison.OrdinalIgnoreCase);
    }
}
