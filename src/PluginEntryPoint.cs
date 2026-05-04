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
/// Service d'arrière-plan : enregistre l'injection JS auprès du plugin File Transformation.
/// </summary>
public class PluginEntryPoint : IHostedService
{
    private readonly ILogger<PluginEntryPoint> _logger;

    public PluginEntryPoint(ILogger<PluginEntryPoint> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        TryRegisterFileTransformation();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void TryRegisterFileTransformation()
    {
        try
        {
            Assembly? ftAssembly = AssemblyLoadContext.All
                .SelectMany(ctx => ctx.Assemblies)
                .FirstOrDefault(a => a.FullName?.Contains(".FileTransformation", StringComparison.OrdinalIgnoreCase) == true);

            if (ftAssembly == null)
            {
                _logger.LogWarning(
                    "NoPayNoPlay : plugin 'File Transformation' introuvable. Installez-le pour activer l'UI utilisateur (bouton, modal, bannière).");
                return;
            }

            Type? pluginInterfaceType = ftAssembly.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface");
            MethodInfo? register = pluginInterfaceType?.GetMethod("RegisterTransformation");
            if (register == null)
            {
                _logger.LogWarning("NoPayNoPlay : impossible de localiser PluginInterface.RegisterTransformation");
                return;
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
            _logger.LogInformation("NoPayNoPlay : transformation index.html enregistrée auprès de File Transformation");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NoPayNoPlay : échec de l'enregistrement de la transformation File Transformation");
        }
    }
}
