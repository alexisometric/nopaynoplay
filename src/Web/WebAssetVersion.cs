using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace Jellyfin.Plugin.NoPayNoPlay.Web;

/// <summary>
/// Computes a short, content-addressed hash of the embedded web assets
/// (<c>client.js</c> + <c>qrcode.js</c>) so cache-busting URLs change whenever the
/// shipped bytes change. Using the assembly version alone risks serving stale
/// scripts from browser caches after a hotfix that keeps the same version.
/// </summary>
public static class WebAssetVersion
{
    // Computed once per process (thread-safe), reused by every request/transform.
    private static readonly Lazy<string> ValueLazy = new(Compute, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Gets a 12-character lowercase hex content hash of the web assets.</summary>
    public static string Value => ValueLazy.Value;

    private static string Compute()
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Assembly asm = typeof(WebAssetVersion).Assembly;
        string root = typeof(Plugin).Namespace ?? string.Empty;
        foreach (string name in new[] { "client.js", "qrcode.js" })
        {
            using Stream? stream = asm.GetManifestResourceStream(root + ".Web." + name);
            if (stream == null)
            {
                continue;
            }

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            hash.AppendData(ms.ToArray());
        }

        // SHA-256 → 64 hex chars; keeping 12 keeps URLs short while making
        // collisions negligible for cache-busting purposes.
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()[..12];
    }
}
