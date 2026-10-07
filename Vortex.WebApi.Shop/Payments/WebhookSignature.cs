using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Vortex.WebApi.Shop.Payments;

/// <summary>
/// The one check that decides whether a webhook body is worth reading: an HMAC-SHA256 over a
/// timestamp and the RAW request body, keyed by the provider's secret.
/// </summary>
/// <remarks>
/// <para>
/// The header is the format every serious PSP converged on:
/// <c>X-Vortex-Signature: t=1757635200,v1=&lt;hex&gt;</c>, signing <c>"{t}.{body}"</c>. The timestamp
/// is INSIDE the signed string, which is what makes it worth anything: outside it, an attacker who
/// captured one valid request could put any timestamp they liked on it and replay it forever.
/// </para>
/// <para>
/// Three properties, and each of them has been the hole in somebody's integration:
/// </para>
/// <list type="bullet">
/// <item><description>
/// The comparison is constant time. <c>==</c> on two hex strings returns as soon as the first byte
/// differs, which is a measurable oracle for guessing the rest one byte at a time.
/// </description></item>
/// <item><description>
/// The body is signed as the bytes that arrived, never as a re-serialisation of a parsed object —
/// key order and whitespace are not preserved by any JSON round trip, and a signature that fails for
/// that reason gets "fixed" by turning the check off.
/// </description></item>
/// <item><description>
/// A missing header, a malformed one, a stale one and a wrong one all return the same false. Saying
/// which is how a forger finds out they got the format right.
/// </description></item>
/// </list>
/// </remarks>
internal static class WebhookSignature
{
    public const string HeaderName = "X-Vortex-Signature";

    /// <summary>
    /// True when <paramref name="body"/> was signed with <paramref name="secret"/> no longer than
    /// <paramref name="tolerance"/> ago.
    /// </summary>
    public static bool Verify(
        IReadOnlyDictionary<string, string> headers,
        string body,
        string secret,
        TimeSpan tolerance,
        DateTimeOffset now
    )
    {
        if (headers is null || string.IsNullOrEmpty(secret))
        {
            return false;
        }

        if (!TryGetHeader(headers, out string header))
        {
            return false;
        }

        if (!TryParse(header, out long timestamp, out string signature))
        {
            return false;
        }

        // Both directions. A notification from the future is as much a sign of a forged or replayed
        // message as a stale one, and clocks that disagree by more than the tolerance are an
        // operational problem to fix rather than a case to wave through.
        TimeSpan drift = now - DateTimeOffset.FromUnixTimeSeconds(timestamp);

        if (drift > tolerance || drift < -tolerance)
        {
            return false;
        }

        return SignedEquals(Sign(timestamp, body, secret), signature);
    }

    /// <summary>
    /// The header a caller must send. Used by the tests, and by whatever hands an operator the
    /// command line for a manual capture — there is no second implementation of the format.
    /// </summary>
    public static string Build(string body, string secret, DateTimeOffset now)
    {
        long timestamp = now.ToUnixTimeSeconds();

        return string.Create(
            CultureInfo.InvariantCulture,
            $"t={timestamp},v1={Sign(timestamp, body, secret)}"
        );
    }

    private static string Sign(long timestamp, string body, string secret)
    {
        string signed = string.Create(CultureInfo.InvariantCulture, $"{timestamp}.{body}");

        byte[] mac = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(signed)
        );

        return Convert.ToHexStringLower(mac);
    }

    private static bool TryGetHeader(IReadOnlyDictionary<string, string> headers, out string value)
    {
        // Case-insensitively, because HTTP header names are, and the dictionary a caller builds from
        // a request may not be. Looking up the exact spelling only is how a signature check quietly
        // becomes a signature check that never finds a signature.
        foreach (KeyValuePair<string, string> header in headers)
        {
            if (string.Equals(header.Key, HeaderName, StringComparison.OrdinalIgnoreCase))
            {
                value = header.Value ?? string.Empty;

                return value.Length > 0;
            }
        }

        value = string.Empty;

        return false;
    }

    private static bool TryParse(string header, out long timestamp, out string signature)
    {
        timestamp = 0;
        signature = string.Empty;

        foreach (Range part in header.AsSpan().Split(','))
        {
            ReadOnlySpan<char> field = header.AsSpan()[part].Trim();

            if (field.StartsWith("t=", StringComparison.Ordinal))
            {
                if (
                    !long.TryParse(
                        field[2..],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out timestamp
                    )
                )
                {
                    return false;
                }
            }
            else if (field.StartsWith("v1=", StringComparison.Ordinal))
            {
                signature = field[3..].ToString();
            }
        }

        return timestamp > 0 && signature.Length > 0;
    }

    private static bool SignedEquals(string expected, string actual)
    {
        // FixedTimeEquals refuses unequal lengths outright, which leaks only the length — and the
        // length of a SHA-256 hex digest is not a secret. Comparing the decoded bytes rather than the
        // hex text also makes the check indifferent to the case the caller sent.
        if (!TryDecodeHex(actual, out byte[] actualBytes))
        {
            return false;
        }

        return TryDecodeHex(expected, out byte[] expectedBytes)
            && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private static bool TryDecodeHex(string value, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromHexString(value);

            return true;
        }
        catch (FormatException)
        {
            bytes = [];

            return false;
        }
    }
}
