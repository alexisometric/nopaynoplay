using Jellyfin.Plugin.NoPayNoPlay.Api;
using Xunit;

namespace Jellyfin.Plugin.NoPayNoPlay.Tests;

/// <summary>
/// Validates the shared <see cref="Sanitization"/> helpers that every API endpoint
/// relies on (URLs, emails, currency, cultures, promo codes, colours, CSV escaping).
/// </summary>
public class SanitizationTests
{
    [Theory]
    [InlineData("https://paypal.me/jdoe/12EUR", "https://paypal.me/jdoe/12EUR")]
    [InlineData("http://example.com/x", "http://example.com/x")]
    [InlineData("javascript:alert(1)", "")]
    [InlineData("data:text/html,<b>hi</b>", "")]
    [InlineData("ftp://example.com", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void SanitizeUrl_OnlyAllowsHttpHttps(string? input, string expected)
    {
        Assert.Equal(expected, Sanitization.SanitizeUrl(input));
    }

    [Theory]
    [InlineData("admin@example.com", "admin@example.com")]
    [InlineData(" a.b+tag@sub.example.co.uk ", "a.b+tag@sub.example.co.uk")]
    [InlineData("not-an-email", "")]
    [InlineData("a@b", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void SanitizeEmail_AcceptsValidRejectsInvalid(string? input, string expected)
    {
        Assert.Equal(expected, Sanitization.SanitizeEmail(input));
    }

    [Theory]
    [InlineData("EUR", "EUR")]
    [InlineData("usd", "USD")]
    [InlineData("ZZZZZZ", "EUR")]
    [InlineData("EU1", "EUR")]
    [InlineData("", "EUR")]
    [InlineData(null, "EUR")]
    public void SanitizeCurrency_NormalizesOrFallsBack(string? input, string expected)
    {
        Assert.Equal(expected, Sanitization.SanitizeCurrency(input));
    }

    [Theory]
    [InlineData("fr", "fr")]
    [InlineData("pt-BR", "pt-br")]
    [InlineData("EN", "en")]
    [InlineData("bad value!", "")]
    [InlineData("toolonglanguagecode", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void SanitizeCulture_AcceptsValidRejectsInvalid(string? input, string expected)
    {
        Assert.Equal(expected, Sanitization.SanitizeCulture(input));
    }

    [Theory]
    [InlineData("summer", "SUMMER")]
    [InlineData("noel26", "NOEL26")]
    [InlineData("ab", "")]
    [InlineData("bad code!", "")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890ABCD", "ABCDEFGHIJKLMNOPQRSTUVWXYZ123456")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void SanitizePromoCode_NormalizesAndEnforcesRules(string? input, string expected)
    {
        Assert.Equal(expected, Sanitization.SanitizePromoCode(input));
    }

    [Theory]
    [InlineData("#fff", "#fff")]
    [InlineData("#12345678", "#12345678")]
    [InlineData("red", "")]
    [InlineData("#ggg", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void SanitizeColor_AcceptsHexOnly(string? input, string expected)
    {
        Assert.Equal(expected, Sanitization.SanitizeColor(input));
    }

    [Theory]
    [InlineData("=1+1", "\"'=1+1\"")]
    [InlineData("+44 1234", "\"'+44 1234\"")]
    [InlineData("-2", "\"'-2\"")]
    [InlineData("@cmd", "\"'@cmd\"")]
    [InlineData("plain", "plain")]
    [InlineData("with,comma", "\"with,comma\"")]
    [InlineData("with\"quote", "\"with\"\"quote\"")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void CsvEscape_BlocksFormulaInjectionAndQuotes(string? input, string expected)
    {
        Assert.Equal(expected, Sanitization.CsvEscape(input));
    }

    [Theory]
    [InlineData("short", 10, "short")]
    [InlineData("this is long", 5, "this ")]
    [InlineData("", 5, "")]
    [InlineData(null, 5, "")]
    public void Truncate_CapsLength(string? input, int max, string expected)
    {
        Assert.Equal(expected, Sanitization.Truncate(input, max));
    }

    [Theory]
    [InlineData("<script>alert(1)</script>", "&lt;script&gt;alert(1)&lt;/script&gt;")]
    [InlineData("a & b", "a &amp; b")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void HtmlEncode_EscapesMarkup(string? input, string expected)
    {
        Assert.Equal(expected, Sanitization.HtmlEncode(input));
    }
}
