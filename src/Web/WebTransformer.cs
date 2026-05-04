using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.NoPayNoPlay.Web;

/// <summary>
/// Callback invoqué par le plugin "File Transformation" pour patcher index.html.
/// Insère la balise script juste avant la fermeture de body.
/// </summary>
public static class WebTransformer
{
    private const string ScriptTag =
        "<script defer src=\"/NoPayNoPlay/Web/client.js\"></script>";

    /// <summary>Méthode statique conforme au contrat File Transformation.</summary>
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
