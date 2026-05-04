using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.NoPayNoPlay.Web;

/// <summary>
/// Callback invoked by the "File Transformation" plugin to patch index.html.
/// Inserts the script tag right before the closing &lt;/body&gt; element.
/// </summary>
public static class WebTransformer
{
    private const string ScriptTag =
        "<script defer src=\"/NoPayNoPlay/Web/client.js\"></script>";

    /// <summary>Static method matching the File Transformation callback contract.</summary>
    public static void Transform(JObject payload)
    {
        if (payload == null)
        {
            return;
        }

        string? contents = payload["contents"]?.ToString();
        if (string.IsNullOrEmpty(contents))
        {
            return;
        }

        if (contents.Contains(ScriptTag, System.StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        int idx = contents.LastIndexOf("</body>", System.StringComparison.OrdinalIgnoreCase);
        string patched = idx >= 0
            ? contents.Insert(idx, ScriptTag + "\n")
            : contents + ScriptTag;

        payload["contents"] = patched;
    }
}
