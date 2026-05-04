using System.IO;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.NoPayNoPlay.Api;

/// <summary>
/// Sert le script client embarqué (chargé par l'index.html patché par File Transformation).
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("NoPayNoPlay/Web")]
public class ClientAssetsController : ControllerBase
{
    /// <summary>Renvoie le script client.js embarqué.</summary>
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
