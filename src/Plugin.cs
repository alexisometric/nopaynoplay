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
        SeedDefaultsAndDedupe();
    }

    /// <summary>
    /// Seeds first-run tier and tag defaults, and removes duplicates that
    /// previous releases (≤ 1.2.1) accumulated on every Jellyfin restart
    /// because <see cref="System.Xml.Serialization.XmlSerializer"/> appends
    /// to collections initialised inline.
    /// </summary>
    private void SeedDefaultsAndDedupe()
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
                cfg.Tiers.Add(new Configuration.SubscriptionTier { Months = 1,  Price = 10m,  Highlight = false });
                cfg.Tiers.Add(new Configuration.SubscriptionTier { Months = 3,  Price = 27m,  Highlight = true });
                cfg.Tiers.Add(new Configuration.SubscriptionTier { Months = 12, Price = 100m, Highlight = false });
            }
            if (cfg.Tags.Count == 0)
            {
                cfg.Tags.Add(new Configuration.UserTag { Key = "family",  Label = "Family",  Color = "#9b59b6" });
                cfg.Tags.Add(new Configuration.UserTag { Key = "friends", Label = "Friends", Color = "#3498db" });
                cfg.Tags.Add(new Configuration.UserTag { Key = "guests",  Label = "Guests",  Color = "#95a5a6" });
            }
            cfg.DefaultsSeeded = true;
            dirty = true;
        }

        // Backfill missing transaction IDs once, at startup, so the read paths
        // (GET /Me, GET /Users) never have to turn into a disk write later on.
        foreach (var s in cfg.Subscriptions)
        {
            foreach (var t in s.Transactions)
            {
                if (t.Id == Guid.Empty)
                {
                    t.Id = Guid.NewGuid();
                    dirty = true;
                }
            }
        }

        if (dirty)
        {
            try { SaveConfiguration(); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[NoPayNoPlay] Failed to persist seeded/deduped configuration.");
            }
        }
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
