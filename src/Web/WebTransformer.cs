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
}
