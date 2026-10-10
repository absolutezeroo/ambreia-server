using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Vortex.Networking.Session;

/// <summary>
/// The client's address as the caps, the audit and the logs should see it (audit P0-4). Behind the
/// reverse proxy the README recommends, the transport address is the proxy's for everyone; the
/// client's is what the proxy says — PROXY protocol first (the transport's own word), the first
/// <c>X-Forwarded-For</c> hop otherwise — and only from a proxy the operator listed, because anyone
/// can write either.
/// </summary>
public static class ClientAddress
{
    /// <param name="transport">The peer the socket is talking to.</param>
    /// <param name="proxyProtocolSource">What a PROXY protocol header named, if one was read.</param>
    /// <param name="forwardedFor">The <c>X-Forwarded-For</c> header of a WebSocket handshake, if any.</param>
    /// <param name="trusted">The proxies whose word is taken.</param>
    public static string? Resolve(
        IPAddress? transport,
        IPAddress? proxyProtocolSource,
        string? forwardedFor,
        IReadOnlySet<IPAddress> trusted
    )
    {
        if (transport is null)
        {
            return null;
        }

        if (!trusted.Contains(transport))
        {
            return transport.ToString();
        }

        if (proxyProtocolSource is not null)
        {
            return proxyProtocolSource.ToString();
        }

        // "client, proxy1, proxy2": the first hop is the client; the rest are proxies on the way.
        string? firstHop = forwardedFor?.Split(',', 2)[0].Trim();

        return firstHop is { Length: > 0 } && IPAddress.TryParse(firstHop, out IPAddress? client)
            ? client.ToString()
            : transport.ToString();
    }

    /// <summary>The configured list, parsed once; an entry that is not an address is dropped.</summary>
    public static IReadOnlySet<IPAddress> ParseTrusted(IEnumerable<string> entries) =>
        entries
            .Select(e => IPAddress.TryParse(e.Trim(), out IPAddress? address) ? address : null)
            .OfType<IPAddress>()
            .ToHashSet();
}
