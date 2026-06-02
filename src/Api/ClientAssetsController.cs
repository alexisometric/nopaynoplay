using System.IO;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.NoPayNoPlay.Api;

/// <summary>
/// Serves the embedded client script (loaded by the index.html patched by File Transformation).
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("NoPayNoPlay/Web")]
public class ClientAssetsController : ControllerBase
{
    /// <summary>Returns the embedded client.js script.</summary>
    [HttpGet("client.js")]
    [Produces("application/javascript")]
    public IActionResult GetClientJs() => ServeScript("client.js", "no-cache");

    /// <summary>
    /// Returns the vendored QR-code generator (qrcode-generator, MIT). Served by the
    /// plugin so no outbound call is ever made; cached aggressively as it is static.
    /// </summary>
    [HttpGet("qrcode.js")]
    [Produces("application/javascript")]
    public IActionResult GetQrcodeJs() => ServeScript("qrcode.js", "public, max-age=604800, immutable");

    private IActionResult ServeScript(string fileName, string cacheControl)
    {
        Assembly asm = typeof(ClientAssetsController).Assembly;
        string resource = $"{typeof(Plugin).Namespace}.Web.{fileName}";
        Stream? stream = asm.GetManifestResourceStream(resource);
        if (stream == null)
        {
            return NotFound();
        }

        Response.Headers["Cache-Control"] = cacheControl;
        return File(stream, "application/javascript");
    }
}
