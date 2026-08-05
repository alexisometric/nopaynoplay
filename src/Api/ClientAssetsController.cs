using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
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
    // Embedded assets are immutable per plugin version: cache the raw bytes + ETag once,
    // plus the gzip-compressed variant, so no request re-reads or re-compresses them.
    private static readonly ConcurrentDictionary<string, (byte[] Raw, string Etag)> _assets = new();
    private static readonly ConcurrentDictionary<string, byte[]> _gzipCache = new();

    /// <summary>
    /// Returns the embedded client.js script. The injected URL carries a <c>?v=VERSION</c>
    /// cache-buster (see WebTransformer), so the asset is served as immutable — the browser
    /// reuses it for a year and only refetches when the plugin version changes.
    /// </summary>
    [HttpGet("client.js")]
    [Produces("application/javascript")]
    public IActionResult GetClientJs() => ServeScript("client.js", "public, max-age=31536000, immutable");

    /// <summary>
    /// Returns the vendored QR-code generator (qrcode-generator, MIT). Served by the
    /// plugin so no outbound call is ever made; cached aggressively as it is static.
    /// </summary>
    [HttpGet("qrcode.js")]
    [Produces("application/javascript")]
    public IActionResult GetQrcodeJs() => ServeScript("qrcode.js", "public, max-age=31536000, immutable");

    private IActionResult ServeScript(string fileName, string cacheControl)
    {
        var asset = _assets.GetOrAdd(fileName, name =>
        {
            Assembly asm = typeof(ClientAssetsController).Assembly;
            string resource = $"{typeof(Plugin).Namespace}.Web.{name}";
            using Stream? stream = asm.GetManifestResourceStream(resource);
            if (stream == null)
            {
                return (Array.Empty<byte>(), string.Empty);
            }

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            byte[] raw = ms.ToArray();
            // Strong ETag (SHA-1 of the bytes) so conditional requests revalidate accurately.
            string etag = "\"" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(raw)) + "\"";
            return (raw, etag);
        });

        if (asset.Raw.Length == 0)
        {
            return NotFound();
        }

        Response.Headers["Cache-Control"] = cacheControl;
        Response.Headers["ETag"] = asset.Etag;

        string? acceptEncoding = Request.Headers.AcceptEncoding.ToString();
        if (acceptEncoding != null && acceptEncoding.Contains("gzip", System.StringComparison.OrdinalIgnoreCase))
        {
            byte[] gz = _gzipCache.GetOrAdd(fileName, _ =>
            {
                using var ms = new MemoryStream();
                using (var gzip = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
                {
                    gzip.Write(asset.Raw, 0, asset.Raw.Length);
                }

                return ms.ToArray();
            });
            Response.Headers["Content-Encoding"] = "gzip";
            Response.Headers["Vary"] = "Accept-Encoding";
            return File(gz, "application/javascript");
        }

        return File(asset.Raw, "application/javascript");
    }
}

