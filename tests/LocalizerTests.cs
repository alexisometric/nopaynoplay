using System.Collections.Generic;
using Jellyfin.Plugin.NoPayNoPlay.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.NoPayNoPlay.Tests;

/// <summary>Tests for the embedded-resource based <see cref="Localizer"/>.</summary>
public class LocalizerTests
{
    private static Localizer NewLocalizer()
        => new Localizer(NullLogger<Localizer>.Instance, serverConfig: null);

    [Fact]
    public void AvailableCultures_ContainsAtLeastEnglish()
    {
        var loc = NewLocalizer();
        Assert.Contains("en", loc.AvailableCultures);
    }

    [Fact]
    public void Get_ReturnsKeyItself_WhenMissing()
    {
        var loc = NewLocalizer();
        Assert.Equal("totally.unknown.key", loc.Get("totally.unknown.key", "en"));
    }

    [Fact]
    public void Get_FrenchKey_ReturnsFrenchValue()
    {
        var loc = NewLocalizer();
        // "admin.tabs.settings" is defined in both en and fr.
        string en = loc.Get("admin.tabs.settings", "en");
        string fr = loc.Get("admin.tabs.settings", "fr");
        Assert.False(string.IsNullOrEmpty(en));
        Assert.False(string.IsNullOrEmpty(fr));
        Assert.NotEqual(en, fr);
    }

    [Fact]
    public void Get_FallsBackToEnglish_WhenCultureMissingKey()
    {
        var loc = NewLocalizer();
        // Use a key that exists in en — request a non-existent culture.
        string val = loc.Get("admin.tabs.settings", "xx");
        string en = loc.Get("admin.tabs.settings", "en");
        Assert.Equal(en, val);
    }

    [Fact]
    public void Get_WithTokens_PerformsSubstitution()
    {
        var loc = NewLocalizer();
        // We can't depend on a specific localized template, so we craft a token-only key
        // and expect the key to come back unchanged when no template has the token.
        // Instead, validate the substitution itself on a known template containing {days}.
        // "user.banner.warningSoon" should contain {days} placeholder — verify substitution.
        string output = loc.Get("user.banner.warningSoon", "en", new Dictionary<string, string?> { ["days"] = "7" });
        Assert.DoesNotContain("{days}", output);
    }

    [Fact]
    public void GetBundle_ReturnsAllEnglishKeys_ForKnownCulture()
    {
        var loc = NewLocalizer();
        var en = loc.GetBundle("en");
        var fr = loc.GetBundle("fr");
        // Merging guarantees fr bundle has every en key (English fallback values for missing).
        foreach (var key in en.Keys)
        {
            Assert.True(fr.ContainsKey(key), $"French bundle is missing key '{key}'");
        }
    }

    [Fact]
    public void GetBundle_DefaultsToEnglish_ForUnknownCulture()
    {
        var loc = NewLocalizer();
        var bundle = loc.GetBundle("zz");
        var en = loc.GetBundle("en");
        Assert.Equal(en, bundle);
    }

    [Fact]
    public void ResolveCulture_PrefersExplicitLangQueryParam()
    {
        var loc = NewLocalizer();
        var ctx = new DefaultHttpContext();
        ctx.Request.QueryString = new QueryString("?lang=fr");
        ctx.Request.Headers["Accept-Language"] = "en";
        Assert.Equal("fr", loc.ResolveCulture(ctx));
    }

    [Fact]
    public void ResolveCulture_FallsBackToAcceptLanguageHeader()
    {
        var loc = NewLocalizer();
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["Accept-Language"] = "fr-FR,fr;q=0.9,en;q=0.8";
        Assert.Equal("fr", loc.ResolveCulture(ctx));
    }

    [Fact]
    public void ResolveCulture_NormalizesRegionCodes()
    {
        var loc = NewLocalizer();
        var ctx = new DefaultHttpContext();
        ctx.Request.QueryString = new QueryString("?lang=fr-CA");
        Assert.Equal("fr", loc.ResolveCulture(ctx));
    }

    [Fact]
    public void ResolveCulture_DefaultsToEnglish_WhenNothingMatches()
    {
        var loc = NewLocalizer();
        var ctx = new DefaultHttpContext();
        ctx.Request.QueryString = new QueryString("?lang=xx");
        ctx.Request.Headers["Accept-Language"] = "zz-ZZ";
        Assert.Equal("en", loc.ResolveCulture(ctx));
    }

    [Fact]
    public void ResolveCulture_HandlesNullHttpContext()
    {
        var loc = NewLocalizer();
        Assert.Equal("en", loc.ResolveCulture(null));
    }
}
