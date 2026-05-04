using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.NoPayNoPlay.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.NoPayNoPlay;

/// <summary>
/// NoPayNoPlay plugin entry point.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private const int MaxBackups = 10;

    private readonly IApplicationPaths _applicationPaths;
    private readonly ILogger<Plugin> _logger;

    /// <summary>
    /// Initializes a new instance of the plugin.
    /// </summary>
    public Plugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        ILogger<Plugin> logger)
        : base(applicationPaths, xmlSerializer)
    {
        _applicationPaths = applicationPaths;
        _logger = logger;
        Instance = this;
    }

    /// <summary>Gets the singleton instance of the plugin.</summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "NoPayNoPlay";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("f3b4d2c1-7e9a-4b1e-9c6d-9a1b2c3d4e5f");

    /// <inheritdoc />
    public override string Description =>
        "Manual-validation monthly subscription tracking with playback enforcement on expiry.";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = "NoPayNoPlay",
            EmbeddedResourcePath = $"{GetType().Namespace}.Web.config.html"
        };
        yield return new PluginPageInfo
        {
            Name = "NoPayNoPlayJs",
            EmbeddedResourcePath = $"{GetType().Namespace}.Web.config.js"
        };
    }

    /// <inheritdoc />
    public override void SaveConfiguration()
    {
        TryBackupConfiguration();
        base.SaveConfiguration();
    }

    private void TryBackupConfiguration()
    {
        try
        {
            string configPath = Path.Combine(
                _applicationPaths.PluginConfigurationsPath,
                $"{Id}.xml");

            if (!File.Exists(configPath))
            {
                return;
            }

            string backupDir = Path.Combine(
                _applicationPaths.PluginConfigurationsPath,
                "NoPayNoPlay.backups");
            Directory.CreateDirectory(backupDir);

            string stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            string backupPath = Path.Combine(backupDir, $"config.xml.bak-{stamp}");
            File.Copy(configPath, backupPath, overwrite: true);

            // Retention: keep the MaxBackups most recent files.
            FileInfo[] backups = new DirectoryInfo(backupDir)
                .GetFiles("config.xml.bak-*")
                .OrderByDescending(f => f.CreationTimeUtc)
                .ToArray();

            for (int i = MaxBackups; i < backups.Length; i++)
            {
                try
                {
                    backups[i].Delete();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "NoPayNoPlay: failed to delete old backup {File}", backups[i].FullName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NoPayNoPlay: configuration backup failed");
        }
    }
}
