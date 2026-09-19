using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Vortex.Primitives.Camera;

/// <summary>
/// Decides whether a sprite name that is a url may be fetched.
/// </summary>
/// <remarks>
/// <para>
/// This is the camera's server-side request forgery guard. A sprite's <c>name</c> is usually an asset
/// key, but the client substitutes <c>%image.library.url%</c> and <c>%group.badge.url%</c> into it, so
/// a name beginning <c>http</c> is an image the renderer is expected to go and fetch — at an address
/// chosen by the payload. Without this, <c>RenderRoom</c> is a request the hotel makes on behalf of
/// anybody who can open the camera.
/// </para>
/// <para>
/// The comparison is on the <em>parsed</em> url, never on the string. A prefix match over raw text
/// reads <c>http://assets.example/</c> as a prefix of <c>http://assets.example@evil.test/</c> and of
/// <c>http://assets.example.evil.test/</c>, and both of those fetch from somewhere else entirely.
/// Scheme, host and port are matched exactly and the path is matched unescaped, which leaves none of
/// those shapes anywhere to hide.
/// </para>
/// <para>
/// What it does not cover, and what belongs to whoever writes the fetch: the allowed host still
/// resolves through DNS at request time, so an operator who allowlists a name they do not control is
/// trusting that name. Pin the resolved address, refuse redirects, cap the response and check the
/// bytes rather than the <c>Content-Type</c>.
/// </para>
/// </remarks>
public static class CameraExternalImagePolicy
{
    public static bool IsAllowed(string candidate, ImmutableArray<string> allowedPrefixes)
    {
        if (allowedPrefixes.IsDefaultOrEmpty)
        {
            // Nothing allowlisted means no external image is fetched at all. That is the default,
            // and it is the only setting that is right without somebody having thought about it.
            return false;
        }

        if (!TryParseFetchable(candidate, out Uri? url))
        {
            return false;
        }

        foreach (string prefix in allowedPrefixes)
        {
            if (TryParseFetchable(prefix, out Uri? allowed) && Matches(url, allowed))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Splits a configured <c>;</c>-separated list into prefixes, dropping blanks.
    /// </summary>
    public static ImmutableArray<string> ParsePrefixes(string configured) =>
        string.IsNullOrWhiteSpace(configured)
            ? []
            :
            [
                .. configured.Split(
                    ';',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                ),
            ];

    private static bool TryParseFetchable(string value, [NotNullWhen(true)] out Uri? parsed)
    {
        parsed = null;

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        // "http://allowed.host@evil.test/" parses to host evil.test while reading as the allowed one.
        // Nothing legitimate here carries credentials, so the shape is refused outright.
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        parsed = uri;
        return true;
    }

    private static bool Matches(Uri candidate, Uri allowed)
    {
        if (!string.Equals(candidate.Scheme, allowed.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // IdnHost rather than Host: two spellings of one internationalised name must not be two
        // different hosts to this check when they are one host to the resolver.
        if (!string.Equals(candidate.IdnHost, allowed.IdnHost, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (candidate.Port != allowed.Port)
        {
            return false;
        }

        string candidatePath = Unescape(candidate);
        string allowedPath = Unescape(allowed);

        // A traversal that survives unescaping would climb out of the allowed folder, which is the
        // whole point of naming a folder rather than a host.
        if (candidatePath.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        return candidatePath.StartsWith(allowedPath, StringComparison.Ordinal);
    }

    private static string Unescape(Uri uri) =>
        uri.GetComponents(UriComponents.Path, UriFormat.Unescaped);
}
