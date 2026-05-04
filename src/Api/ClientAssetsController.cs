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
    public IActionResult GetClientJs()
    {
        Assembly asm = typeof(ClientAssetsController).Assembly;
        string resource = $"{typeof(Plugin).Namespace}.Web.client.js";
        Stream? stream = asm.GetManifestResourceStream(resource);
        if (stream == null)
        {
            return NotFound();
        }

        Response.Headers["Cache-Control"] = "no-cache";
        return File(stream, "application/javascript");
    }
}
