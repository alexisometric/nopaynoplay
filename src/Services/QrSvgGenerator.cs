using QRCoder;

namespace Jellyfin.Plugin.NoPayNoPlay.Services;

/// <summary>
/// Thin wrapper around <see cref="QRCoder.QRCodeGenerator"/> that emits a
/// self-contained SVG. Used by the user modal to render scannable QR codes
/// for PayPal / Lydia / IBAN payment links without leaking any data to a
/// remote service.
/// </summary>
public static class QrSvgGenerator
{
    /// <summary>
    /// Generates an SVG QR code for the given payload (URL or short text).
    /// Uses error-correction level Q so that mobile scanners stay reliable
    /// even when the SVG is rendered small or with a logo overlay.
    /// </summary>
    public static string Generate(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
        var svg = new SvgQRCode(data);
        return svg.GetGraphic(4);
    }
}
