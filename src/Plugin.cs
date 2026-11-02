using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.NoPayNoPlay.Configuration;
using Jellyfin.Plugin.NoPayNoPlay.Services;
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

    /// <summary>Configurations larger than this are never copied to the backup folder.</summary>
    private const long MaxBackupBytes = 32L * 1024 * 1024;

    private readonly IApplicationPaths _applicationPaths;
    private readonly ILogger<Plugin> _logger;

    /// <summary>
    /// Serialises every configuration write of this plugin. The previous code relied
    /// only on the base class' internal saves lock, which does not cover the backup
    /// step nor the <see cref="PluginConfiguration"/> mutations performed by the API
    /// endpoints — two concurrent requests could serialise the same object twice and
    /// produce a truncated XML file.
    /// </summary>
    private readonly object _saveLock = new();

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

        // Never let a configuration problem prevent the plugin from loading: an
        // exception thrown here makes Jellyfin drop the whole plugin (and with it the
        // enforcement state) instead of surfacing a recoverable warning.
        try
        {
            PreserveSuspectConfiguration();
            Configuration.Normalize();
            bool dirty = SeedDefaultsAndDedupe();
            dirty |= SubscriptionService.MigrateEntityIds(Configuration);
            if (dirty)
            {
                SaveConfiguration();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NoPayNoPlay: failed to initialise the configuration");
        }
    }

    /// <summary>
    /// Seeds first-run tier and tag defaults, and removes duplicates that
    /// previous releases (≤ 1.2.1) accumulated on every Jellyfin restart
    /// because <see cref="System.Xml.Serialization.XmlSerializer"/> appends
    /// to collections initialised inline.
    /// </summary>
    /// <returns><c>true</c> when the configuration was modified.</returns>
    private bool SeedDefaultsAndDedupe()
    {
        var cfg = Configuration;
        var dirty = false;

        // Dedupe Tiers by (Months, Price, Label, Highlight).
        if (cfg.Tiers is { Count: > 0 })
        {
            var deduped = cfg.Tiers
                .GroupBy(t => (t.Months, t.Price, t.Label ?? string.Empty, t.Highlight))
                .Select(g => g.First())
                .ToList();
            if (deduped.Count != cfg.Tiers.Count)
            {
                cfg.Tiers = deduped;
                dirty = true;
                _logger.LogInformation("[NoPayNoPlay] Removed duplicate tiers from configuration.");
            }
        }

        // Dedupe Tags by Key (case-insensitive).
        if (cfg.Tags is { Count: > 0 })
        {
            var deduped = cfg.Tags
                .GroupBy(t => (t.Key ?? string.Empty).Trim().ToLowerInvariant())
                .Select(g => g.First())
                .ToList();
            if (deduped.Count != cfg.Tags.Count)
            {
                cfg.Tags = deduped;
                dirty = true;
                _logger.LogInformation("[NoPayNoPlay] Removed duplicate tags from configuration.");
            }
        }

        // First-run seeding: only when the flag is unset AND the lists are
        // empty (so we don't clobber an admin who deliberately deleted them).
        if (!cfg.DefaultsSeeded)
        {
            if (cfg.Tiers.Count == 0)
            {
                cfg.Tiers.Add(new Configuration.SubscriptionTier { Months = 1, Price = 10m, Highlight = false });
                cfg.Tiers.Add(new Configuration.SubscriptionTier { Months = 3, Price = 27m, Highlight = true });
                cfg.Tiers.Add(new Configuration.SubscriptionTier { Months = 12, Price = 100m, Highlight = false });
            }
            if (cfg.Tags.Count == 0)
            {
                cfg.Tags.Add(new Configuration.UserTag { Key = "family", Label = "Family", Color = "#9b59b6" });
                cfg.Tags.Add(new Configuration.UserTag { Key = "friends", Label = "Friends", Color = "#3498db" });
                cfg.Tags.Add(new Configuration.UserTag { Key = "guests", Label = "Guests", Color = "#95a5a6" });
            }
            cfg.DefaultsSeeded = true;
            dirty = true;
        }

        return dirty;
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
            DisplayName = "NoPayNoPlay",
            EmbeddedResourcePath = $"{GetType().Namespace}.Web.config.html",
            EnableInMainMenu = true,
            MenuSection = "server",
            MenuIcon = "payment"
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
        lock (_saveLock)
        {
            Configuration.Normalize();
            TryBackupConfiguration();
            WriteConfigurationFile(Configuration);
        }

        // Raised outside the lock: subscribers may read or even save the config.
        ConfigurationChanged?.Invoke(this, Configuration);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Overridden (the base class only overrides the parameterless overload) so the
    /// backup and the atomic write also apply to the framework's own save path — this
    /// is the path used when the configuration failed to deserialize and the base
    /// class silently replaces it with a fresh one.
    /// </remarks>
    public override void SaveConfiguration(PluginConfiguration configuration)
    {
        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        lock (_saveLock)
        {
            configuration.Normalize();

            if (!ReferenceEquals(configuration, Configuration))
            {
                // A payload that lost its collections must never replace live data:
                // this is exactly how a corrupted/re-read config wiped every
                // subscription (and the policy snapshots) in a single save.
                MergeMissingCollections(configuration);
            }

            TryBackupConfiguration();
            WriteConfigurationFile(configuration);
        }

        ConfigurationChanged?.Invoke(this, configuration ?? Configuration);
    }

    private void MergeMissingCollections(PluginConfiguration incoming)
    {
        var live = Configuration;
        if (incoming.Subscriptions.Count == 0 && live.Subscriptions.Count > 0)
        {
            _logger.LogWarning(
                "NoPayNoPlay: refusing to write a configuration with no subscriptions while {Count} are loaded; keeping the live records",
                live.Subscriptions.Count);
            incoming.Subscriptions = live.Subscriptions;
        }

        if (incoming.Tiers.Count == 0 && live.Tiers.Count > 0)
        {
            incoming.Tiers = live.Tiers;
        }

        if (incoming.Tags.Count == 0 && live.Tags.Count > 0)
        {
            incoming.Tags = live.Tags;
        }

        if (incoming.PromoCodes.Count == 0 && live.PromoCodes.Count > 0)
        {
            incoming.PromoCodes = live.PromoCodes;
        }

        if (incoming.AuditLog.Count == 0 && live.AuditLog.Count > 0)
        {
            incoming.AuditLog = live.AuditLog;
        }
    }

    /// <summary>
    /// Writes the configuration to a temporary file, flushes it to disk, then renames
    /// it over the real path. A crash or power loss during the write can therefore
    /// only leave the previous complete file, never a half-written XML that the base
    /// class would treat as corrupted and reset.
    /// </summary>
    private void WriteConfigurationFile(PluginConfiguration configuration)
    {
        string path = ConfigurationFilePath;
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            XmlSerializer.SerializeToFile(configuration, temporary);

            // SerializeToFile closes the stream before returning; reopen it to force
            // the bytes out to the physical device before the atomic rename.
            using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                // Atomic on POSIX (rename) and on Windows (ReplaceFile): readers see
                // either the old or the new file, never a partial one.
                File.Replace(temporary, path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporary, path);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "NoPayNoPlay: failed to delete the temporary configuration file");
            }
        }

    }

    /// <summary>
    /// Detects the signature of a silently-reset configuration (the file on disk is
    /// substantial but no subscription was loaded) and keeps a copy of the original
    /// under the backup folder, so the data is not destroyed by the first save.
    /// </summary>
    private void PreserveSuspectConfiguration()
    {
        try
        {
            string path = ConfigurationFilePath;
            if (!File.Exists(path))
            {
                return;
            }

            long length = new FileInfo(path).Length;
            if (length <= 512)
            {
                return;
            }

            if (Configuration.Subscriptions.Count > 0
                || Configuration.PromoCodes.Count > 0
                || Configuration.AuditLog.Count > 0)
            {
                return;
            }

            string backupDir = Path.Combine(
                _applicationPaths.PluginConfigurationsPath,
                "NoPayNoPlay.backups");
            Directory.CreateDirectory(backupDir);

            string stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
            string destination = Path.Combine(backupDir, $"config.xml.unreadable-{stamp}");
            File.Copy(path, destination, overwrite: false);
            _logger.LogWarning(
                "NoPayNoPlay: the configuration loaded empty although {Path} is {Bytes} bytes; a copy was preserved at {Backup}. If your subscriptions disappeared, restore that file and report the issue",
                path,
                length,
                destination);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NoPayNoPlay: could not preserve the suspect configuration file");
        }
    }

    private void TryBackupConfiguration()
    {
        try
        {
            // ConfigurationFileName is "Jellyfin.Plugin.NoPayNoPlay.xml" — the same
            // name BasePlugin<T> uses for ConfigurationFilePath. Using {Id}.xml here
            // (Id is the assembly GUID) would look for a file that never exists, so
            // the backup silently never ran.
            string configPath = Path.Combine(
                _applicationPaths.PluginConfigurationsPath,
                ConfigurationFileName);

            if (!File.Exists(configPath))
            {
                return;
            }

            // Never copy an unbounded file: a corrupted/hand-edited config could be
            // gigabytes and fill the plugin folder on every save.
            long length = new FileInfo(configPath).Length;
            if (length <= 0 || length > MaxBackupBytes)
            {
                return;
            }

            string backupDir = Path.Combine(
                _applicationPaths.PluginConfigurationsPath,
                "NoPayNoPlay.backups");
            Directory.CreateDirectory(backupDir);

            // Millisecond-resolution stamp + a uniqueness suffix so two saves within
            // the same second/millisecond (e.g. a BulkPay loop) never collide and
            // silently overwrite an earlier restore point.
            string stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
            string backupPath = Path.Combine(backupDir, $"config.xml.bak-{stamp}");
            int collision = 0;
            while (File.Exists(backupPath))
            {
                backupPath = Path.Combine(backupDir, $"config.xml.bak-{stamp}-{++collision}");
            }

            File.Copy(configPath, backupPath, overwrite: false);

            // The file contains usernames, payment history and admin notes: keep it
            // readable by the Jellyfin account only. Best-effort because the mode is
            // not meaningful on every platform.
            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    File.SetUnixFileMode(
                        backupPath,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "NoPayNoPlay: could not restrict backup permissions");
                }
            }

            // Retention: keep the MaxBackups most recent files. Sort by file name
            // (the embedded timestamp sorts chronologically) rather than by the
            // filesystem CreationTimeUtc, which is unreliable on many Linux/NFS/
            // container filesystems and breaks after a bulk copy/restore.
            FileInfo[] backups = new DirectoryInfo(backupDir)
                .GetFiles("config.xml.bak-*")
                .OrderByDescending(f => f.Name, StringComparer.Ordinal)
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
