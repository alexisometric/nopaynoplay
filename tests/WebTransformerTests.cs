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

        string result = WebTransformer.Transform(payload);

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

        string once = WebTransformer.Transform(payload);
        payload["contents"] = once;
        string twice = WebTransformer.Transform(payload);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void Transform_HandlesEmptyContents()
    {
        var payload = new JObject { ["contents"] = string.Empty };
        string result = WebTransformer.Transform(payload);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Transform_AppendsWhenNoBodyTag()
    {
        var payload = new JObject { ["contents"] = "<html></html>" };
        string result = WebTransformer.Transform(payload);
        Assert.EndsWith("/NoPayNoPlay/Web/client.js\"></script>", result);
    }
}
