using Jellyfin.Plugin.NoPayNoPlay.Web;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Jellyfin.Plugin.NoPayNoPlay.Tests;

/// <summary>Additional edge cases for the index.html transformer.</summary>
public class WebTransformerEdgeCaseTests
{
    [Fact]
    public void Transform_HandlesUppercaseBodyTag()
    {
        var payload = new JObject { ["contents"] = "<HTML><BODY>x</BODY></HTML>" };
        string result = WebTransformer.Transform(payload);
        Assert.Contains("/NoPayNoPlay/Web/client.js", result);
        // Insertion happens just before the closing tag (case-insensitive).
        int scriptIdx = result.IndexOf("client.js", System.StringComparison.Ordinal);
        int bodyCloseIdx = result.IndexOf("</BODY>", System.StringComparison.OrdinalIgnoreCase);
        Assert.True(scriptIdx > 0);
        Assert.True(bodyCloseIdx > scriptIdx);
    }

    [Fact]
    public void Transform_OnlyInsertsOnceEvenAcrossMultipleCalls()
    {
        var payload = new JObject { ["contents"] = "<html><body></body></html>" };
        string a = WebTransformer.Transform(payload);
        payload["contents"] = a;
        string b = WebTransformer.Transform(payload);
        payload["contents"] = b;
        string c = WebTransformer.Transform(payload);
        // Only one occurrence of the script tag, no matter how many times we run.
        int count = System.Text.RegularExpressions.Regex.Matches(c, "/NoPayNoPlay/Web/client\\.js").Count;
        Assert.Equal(1, count);
    }

    [Fact]
    public void Transform_HandlesMissingContentsKey()
    {
        var payload = new JObject(); // no "contents"
        string result = WebTransformer.Transform(payload);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Transform_PreservesExistingContent()
    {
        const string original = "<html><head><title>JF</title></head><body><div id=\"app\">hi</div></body></html>";
        var payload = new JObject { ["contents"] = original };
        string result = WebTransformer.Transform(payload);
        Assert.Contains("<title>JF</title>", result);
        Assert.Contains("<div id=\"app\">hi</div>", result);
    }

    [Fact]
    public void Transform_InsertsBeforeLastBodyCloseWhenMultiplePresent()
    {
        // Pathological but possible: nested body strings inside scripts.
        const string html = "<html><body><script>'</body>'</script></body></html>";
        var payload = new JObject { ["contents"] = html };
        string result = WebTransformer.Transform(payload);
        int scriptIdx = result.IndexOf("/NoPayNoPlay/Web/client.js", System.StringComparison.Ordinal);
        int lastBody = result.LastIndexOf("</body>", System.StringComparison.OrdinalIgnoreCase);
        Assert.True(scriptIdx >= 0);
        Assert.True(scriptIdx < lastBody);
    }
}
