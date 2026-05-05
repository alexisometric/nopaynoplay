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

    // Regression: File Transformation falls back to regex matching when no exact
    // key match is found, and our literal pattern "index.html" matches files like
    // "itemDetails-index-html.<hash>.chunk.js". Those are JS chunks emitted by
    // Webpack that may contain inline HTML templates with "</body>". Inserting
    // our <script> tag into them would break the web client with a SyntaxError.
    [Fact]
    public void Transform_DoesNotTouchWebpackChunkContainingBodyClose()
    {
        const string js = "(self.webpackChunkjellyfin_web=self.webpackChunkjellyfin_web||[]).push("
                          + "[[76331],{12345:e=>{e.exports='<div></body></div>'}}]);";
        var payload = new JObject { ["contents"] = js };
        string result = WebTransformer.Transform(payload);
        Assert.Equal(js, result);
        Assert.DoesNotContain("/NoPayNoPlay/Web/client.js", result);
    }

    [Fact]
    public void Transform_DoesNotTouchPlainScript()
    {
        const string js = "\"use strict\";console.log('</body>');";
        var payload = new JObject { ["contents"] = js };
        string result = WebTransformer.Transform(payload);
        Assert.Equal(js, result);
    }

    [Fact]
    public void Transform_AcceptsDoctypePreamble()
    {
        var payload = new JObject { ["contents"] = "<!DOCTYPE html>\n<html><body></body></html>" };
        string result = WebTransformer.Transform(payload);
        Assert.Contains("/NoPayNoPlay/Web/client.js", result);
    }
}
