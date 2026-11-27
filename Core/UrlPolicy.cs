using System.Text.RegularExpressions;

namespace NicPad.Core;

public enum UrlKind
{
    Link,
    Image,
}

/// <summary>
/// Allowlist for every URL that can reach the Markdown preview.
/// Links: http, https, mailto, in-page #anchors. Images: data:image (raster only), and http/https only when remote images are allowed.
/// Everything else (javascript:, file:, UNC \\host, protocol-relative //host, relative paths) is dropped.
/// </summary>
public static partial class UrlPolicy
{
    private const int MaxDataUrlLength = 10 * 1024 * 1024;

    public static string? Filter(string? url, UrlKind kind, bool allowRemoteImages)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var trimmed = url.Trim();

        // Control characters and backslashes are how scheme filters get bypassed ("jav\tascript:", "\\host\share").
        foreach (var c in trimmed)
        {
            if (char.IsControl(c) || c == '\\')
            {
                return null;
            }
        }

        if (trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return null;
        }

        if (trimmed.StartsWith('#'))
        {
            return kind == UrlKind.Link ? trimmed : null;
        }

        if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return kind == UrlKind.Image && IsSafeDataImage(trimmed) ? trimmed : null;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || uri.IsUnc || uri.IsFile)
        {
            return null;
        }

        var scheme = uri.Scheme;
        if (kind == UrlKind.Link)
        {
            return scheme == Uri.UriSchemeHttp || scheme == Uri.UriSchemeHttps || scheme == Uri.UriSchemeMailto
                ? uri.AbsoluteUri
                : null;
        }

        return allowRemoteImages && (scheme == Uri.UriSchemeHttp || scheme == Uri.UriSchemeHttps)
            ? uri.AbsoluteUri
            : null;
    }

    /// <summary>Only http/https/mailto URLs may be handed to the shell (default browser / mail client).</summary>
    public static bool CanOpenExternally(string? url)
    {
        return Filter(url, UrlKind.Link, allowRemoteImages: false) is { } safe
            && !safe.StartsWith('#')
            && Uri.TryCreate(safe, UriKind.Absolute, out _);
    }

    /// <summary>Decides which network requests the preview browser may make.</summary>
    public static bool IsAllowedPreviewRequest(string? uri, bool isImageRequest, bool allowRemoteImages)
    {
        if (string.IsNullOrEmpty(uri))
        {
            return false;
        }

        if (uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return isImageRequest && Filter(uri, UrlKind.Image, allowRemoteImages) is not null;
    }

    private static bool IsSafeDataImage(string url)
    {
        return url.Length <= MaxDataUrlLength && SafeDataImageRegex().IsMatch(url);
    }

    [GeneratedRegex(@"^data:image/(png|gif|jpeg|jpg|webp|bmp);base64,[A-Za-z0-9+/=\s]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SafeDataImageRegex();
}
