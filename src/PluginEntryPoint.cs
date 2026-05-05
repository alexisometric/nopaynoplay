using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.NoPayNoPlay;

/// <summary>
/// Background service that registers the JS injection callback with the
/// optional "File Transformation" plugin.
/// </summary>
public class PluginEntryPoint : IHostedService
{
    private readonly ILogger<PluginEntryPoint> _logger;

    /// <summary>True once the File Transformation registration has succeeded.</summary>
    public static bool FileTransformationRegistered { get; private set; }

    public PluginEntryPoint(ILogger<PluginEntryPoint> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Try immediately, then retry with a back-off in case the
        // File Transformation plugin assembly hasn't been loaded yet.
        if (!TryRegisterFileTransformation())
        {
            _ = Task.Run(async () =>
            {
                int[] delaysSec = { 3, 8, 20, 45 };
                foreach (int delay in delaysSec)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken).ConfigureAwait(false);
                    }
                    catch (TaskCanceledException) { return; }

                    if (TryRegisterFileTransformation())
                    {
                        return;
                    }
                }
            }, cancellationToken);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private bool TryRegisterFileTransformation()
    {
        if (FileTransformationRegistered)
        {
            return true;
        }

        try
        {
            Assembly? ftAssembly = AssemblyLoadContext.All
                .SelectMany(ctx => ctx.Assemblies)
                .FirstOrDefault(a => a.FullName?.Contains(".FileTransformation", StringComparison.OrdinalIgnoreCase) == true);

            if (ftAssembly == null)
            {
                _logger.LogWarning(
                    "NoPayNoPlay: 'File Transformation' plugin not found. Install it to enable the user-facing UI (header button, modal, banner).");
                return false;
            }

            Type? pluginInterfaceType = ftAssembly.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface");
            MethodInfo? register = pluginInterfaceType?.GetMethod("RegisterTransformation");
            if (register == null)
            {
                _logger.LogWarning("NoPayNoPlay: could not locate PluginInterface.RegisterTransformation");
                return false;
            }

            var payload = new Newtonsoft.Json.Linq.JObject
            {
                ["id"] = Plugin.Instance!.Id.ToString(),
                ["fileNamePattern"] = "index\\.html",
                ["callbackAssembly"] = typeof(Web.WebTransformer).Assembly.FullName,
                ["callbackClass"] = typeof(Web.WebTransformer).FullName,
                ["callbackMethod"] = nameof(Web.WebTransformer.Transform)
            };

            register.Invoke(null, new object?[] { payload });
            FileTransformationRegistered = true;
            _logger.LogInformation("NoPayNoPlay: index.html transformation registered with File Transformation");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NoPayNoPlay: failed to register File Transformation callback");
            return false;
        }
    }
}
