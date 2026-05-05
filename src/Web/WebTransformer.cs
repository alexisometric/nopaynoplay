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
    private const string ScriptTag =
        "<script defer src=\"/NoPayNoPlay/Web/client.js\"></script>";

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

        if (contents.Contains(ScriptTag, System.StringComparison.OrdinalIgnoreCase))
        {
            return contents;
        }

        int idx = contents.LastIndexOf("</body>", System.StringComparison.OrdinalIgnoreCase);
        return idx >= 0
            ? contents.Insert(idx, ScriptTag + "\n")
            : contents + ScriptTag;
    }
}
