using System;
using System.IO;

namespace Vortex.Dashboard.API.Infrastructure;

internal sealed class DashboardAssetStore
{
    private const string HtmlContentTypeValue = "text/html; charset=utf-8";
    private const string CssContentType = "text/css; charset=utf-8";
    private const string JsContentType = "application/javascript; charset=utf-8";
    private const string PngContentType = "image/png";
    private const string SvgContentType = "image/svg+xml";
    private const string Woff2ContentType = "font/woff2";
    private const string WoffContentType = "font/woff";

    public bool TryGetAsset(string assetName, out byte[] bytes, out string contentType)
    {
        bytes = [];
        contentType = "application/octet-stream";

        if (!IsSafeAsset(assetName))
        {
            return false;
        }

        bytes = DashboardPageResources.GetBytes(assetName);
        contentType = GetAssetContentType(assetName);

        return true;
    }

    public byte[] GetHtmlBytes(string htmlResource) =>
        DashboardPageResources.GetBytes(htmlResource);

    public string HtmlContentType => HtmlContentTypeValue;

    private static string GetAssetContentType(string assetName)
    {
        string extension = Path.GetExtension(assetName).ToLowerInvariant();

        return extension switch
        {
            ".css" => CssContentType,
            ".js" => JsContentType,
            ".html" => HtmlContentTypeValue,
            ".png" => PngContentType,
            ".svg" => SvgContentType,
            ".woff2" => Woff2ContentType,
            ".woff" => WoffContentType,
            _ => "application/octet-stream",
        };
    }

    private static bool IsSafeAsset(string assetName)
    {
        if (string.IsNullOrWhiteSpace(assetName))
        {
            return false;
        }

        if (assetName.Contains('/', StringComparison.Ordinal))
        {
            return false;
        }

        if (assetName.Contains('\\', StringComparison.Ordinal))
        {
            return false;
        }

        if (assetName.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        return DashboardPageResources.TryGetResourceName(assetName, out _);
    }
}
