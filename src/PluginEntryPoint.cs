using System;
using System.Collections.Generic;
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
/// optional "File Transformation" plugin and exposes runtime diagnostics
/// so the admin dashboard can tell *why* the user UI may not appear.
/// </summary>
public class PluginEntryPoint : IHostedService
{
    private readonly ILogger<PluginEntryPoint> _logger;

    /// <summary>True once the File Transformation registration has succeeded.</summary>
    public static bool FileTransformationRegistered { get; private set; }

    /// <summary>Latest diagnostic snapshot (used by /Diagnostics endpoint).</summary>
    public static FtDiagnostics LastDiagnostics { get; private set; } = new();

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
                int[] delaysSec = { 3, 8, 20, 45, 90, 180 };
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

    /// <summary>
    /// Force a re-registration. Called by the admin /RetryRegistration endpoint.
    /// Resets the registered flag so the call is actually performed again.
    /// </summary>
    public static bool ForceRetry(ILogger logger)
    {
        FileTransformationRegistered = false;
        var entry = new PluginEntryPoint(logger as ILogger<PluginEntryPoint> ?? new LoggerWrapper(logger));
        return entry.TryRegisterFileTransformation();
    }

    private bool TryRegisterFileTransformation()
    {
        var diag = new FtDiagnostics
        {
            Timestamp = DateTime.UtcNow,
            CallbackAssemblyFullName = typeof(Web.WebTransformer).Assembly.FullName ?? string.Empty,
            CallbackClass = typeof(Web.WebTransformer).FullName ?? string.Empty,
            CallbackMethod = nameof(Web.WebTransformer.Transform)
        };

        if (FileTransformationRegistered)
        {
            diag.Registered = true;
            diag.Notes.Add("Already registered.");
            LastDiagnostics = diag;
            return true;
        }

        try
        {
            // 1. Inventory ALL assemblies whose name contains "FileTransformation"
            //    so we can tell exactly what the runtime sees.
            var matches = AssemblyLoadContext.All
                .SelectMany(ctx => ctx.Assemblies)
                .Where(a => a.FullName?.Contains(".FileTransformation", StringComparison.OrdinalIgnoreCase) == true)
                .Select(a => a.FullName!)
                .Distinct()
                .ToList();

            diag.MatchingAssemblies = matches;

            Assembly? ftAssembly = AssemblyLoadContext.All
                .SelectMany(ctx => ctx.Assemblies)
                .FirstOrDefault(a => a.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface") != null);

            if (ftAssembly == null)
            {
                diag.Notes.Add("File Transformation assembly not loaded yet (PluginInterface type not found).");
                _logger.LogWarning(
                    "NoPayNoPlay: 'File Transformation' plugin not found. Loaded matching assemblies: [{Matches}]",
                    string.Join(", ", matches));
                LastDiagnostics = diag;
                return false;
            }

            diag.FoundAssembly = ftAssembly.FullName ?? string.Empty;

            Type? pluginInterfaceType = ftAssembly.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface");
            MethodInfo? register = pluginInterfaceType?.GetMethod("RegisterTransformation");
            if (register == null)
            {
                diag.Notes.Add("PluginInterface.RegisterTransformation method not found in FT assembly.");
                _logger.LogWarning("NoPayNoPlay: could not locate PluginInterface.RegisterTransformation");
                LastDiagnostics = diag;
                return false;
            }

            var payload = new Newtonsoft.Json.Linq.JObject
            {
                ["id"] = Plugin.Instance!.Id.ToString(),
                // NOTE: must match the literal key other FT consumers use ("index.html"),
                // not an escaped regex. WebFileTransformationService.RunTransformation
                // first does ContainsKey(path) and only falls back to regex matching when
                // no exact key matches; using "index\.html" would silently shadow our
                // pipeline behind any plugin that registered the unescaped key.
                ["fileNamePattern"] = "index.html",
                ["callbackAssembly"] = diag.CallbackAssemblyFullName,
                ["callbackClass"] = diag.CallbackClass,
                ["callbackMethod"] = diag.CallbackMethod
            };

            register.Invoke(null, new object?[] { payload });
            FileTransformationRegistered = true;
            diag.Registered = true;
            diag.Notes.Add("RegisterTransformation invoked successfully.");
            _logger.LogInformation(
                "NoPayNoPlay: index.html transformation registered with File Transformation (assembly={Assembly})",
                diag.FoundAssembly);

            // 2. Verify FT actually accepted us by querying its read service.
            try
            {
                Type? ftPluginType = ftAssembly.GetType("Jellyfin.Plugin.FileTransformation.FileTransformationPlugin");
                object? ftInstance = ftPluginType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                object? sp = ftPluginType?.GetProperty("ServiceProvider", BindingFlags.Public | BindingFlags.Instance)?.GetValue(ftInstance);
                Type? readSvcInterface = ftAssembly.GetType("Jellyfin.Plugin.FileTransformation.Library.IWebFileTransformationReadService");
                if (sp != null && readSvcInterface != null)
                {
                    var getRequiredService = typeof(Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions)
                        .GetMethod(
                            nameof(Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService),
                            new[] { typeof(IServiceProvider), typeof(Type) });
                    object? readSvc = getRequiredService?.Invoke(null, new object?[] { sp, readSvcInterface });
                    var needs = readSvcInterface.GetMethod("NeedsTransformation");
                    bool? ack = needs?.Invoke(readSvc, new object?[] { "index.html" }) as bool?;
                    diag.NeedsTransformationAck = ack;
                    diag.Notes.Add(ack == true
                        ? "FT confirmed: NeedsTransformation(\"index.html\") = true."
                        : "FT did NOT acknowledge our pattern (NeedsTransformation returned false/null).");
                }
                else
                {
                    diag.Notes.Add("Could not resolve FT read service for verification.");
                }
            }
            catch (Exception verifyEx)
            {
                diag.Notes.Add("Verification step threw: " + verifyEx.GetType().Name + ": " + verifyEx.Message);
                _logger.LogDebug(verifyEx, "NoPayNoPlay: post-registration verification failed");
            }

            LastDiagnostics = diag;
            return true;
        }
        catch (Exception ex)
        {
            diag.Notes.Add("Exception: " + ex.GetType().Name + ": " + ex.Message);
            if (ex.InnerException != null)
            {
                diag.Notes.Add("Inner: " + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message);
            }
            LastDiagnostics = diag;
            _logger.LogError(ex, "NoPayNoPlay: failed to register File Transformation callback");
            return false;
        }
    }

    /// <summary>Adapter so static helpers can use any ILogger as ILogger&lt;PluginEntryPoint&gt;.</summary>
    private sealed class LoggerWrapper : ILogger<PluginEntryPoint>
    {
        private readonly ILogger _inner;
        public LoggerWrapper(ILogger inner) { _inner = inner; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _inner.BeginScope(state);
        public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => _inner.Log(logLevel, eventId, state, exception, formatter);
    }
}

/// <summary>Diagnostic snapshot of the last File Transformation registration attempt.</summary>
public class FtDiagnostics
{
    public DateTime Timestamp { get; set; }
    public bool Registered { get; set; }
    public string FoundAssembly { get; set; } = string.Empty;
    public List<string> MatchingAssemblies { get; set; } = new();
    public string CallbackAssemblyFullName { get; set; } = string.Empty;
    public string CallbackClass { get; set; } = string.Empty;
    public string CallbackMethod { get; set; } = string.Empty;
    public bool? NeedsTransformationAck { get; set; }
    public List<string> Notes { get; set; } = new();
}
