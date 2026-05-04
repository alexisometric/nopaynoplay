using Newtonsoft.Json.Linq;
using Jellyfin.Plugin.NoPayNoPlay.Web;
using Xunit;

namespace Jellyfin.Plugin.NoPayNoPlay.Tests;

public class WebTransformerTests
{
    [Fact]
    public void Transform_InsertsScriptBeforeBodyClose()
    {
        var payload = new JObject
        {
            ["contents"] = "<html><body><div>x</div></body></html>"
        };

        WebTransformer.Transform(payload);

        string result = payload["contents"]!.ToString();
        Assert.Contains("/NoPayNoPlay/Web/client.js", result);
        Assert.True(result.IndexOf("client.js") < result.IndexOf("</body>"));
    }

    [Fact]
    public void Transform_IsIdempotent()
    {
        var payload = new JObject
        {
            ["contents"] = "<html><body></body></html>"
        };

        WebTransformer.Transform(payload);
        string once = payload["contents"]!.ToString();
        WebTransformer.Transform(payload);
        string twice = payload["contents"]!.ToString();

        Assert.Equal(once, twice);
    }

    [Fact]
    public void Transform_HandlesEmptyContents()
    {
        var payload = new JObject { ["contents"] = string.Empty };
        WebTransformer.Transform(payload);
        Assert.Equal(string.Empty, payload["contents"]!.ToString());
    }
}
