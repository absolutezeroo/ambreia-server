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

        // "client, proxy1, proxy2": read from the right, the side the proxies wrote. The first hop
        // that is not a listed proxy is the client. Never the leftmost: a client can send its own
        // X-Forwarded-For, and a proxy that appends rather than overwrites leaves it in front.
        if (forwardedFor is null)
        {
            return transport.ToString();
        }

        string[] hops = forwardedFor.Split(',');

        for (int i = hops.Length - 1; i >= 0; i--)
        {
            if (!IPAddress.TryParse(hops[i].Trim(), out IPAddress? hop))
            {
                // A hop that is not an address is a header nobody trustworthy wrote.
                return transport.ToString();
            }

            if (!trusted.Contains(hop))
            {
                return hop.ToString();
            }
        }

        // Every hop was one of ours: the proxy is the client (a probe from the proxy itself).
        return transport.ToString();
    }

    /// <summary>The configured list, parsed once; an entry that is not an address is dropped.</summary>
    public static IReadOnlySet<IPAddress> ParseTrusted(IEnumerable<string> entries) =>
        entries
            .Select(e => IPAddress.TryParse(e.Trim(), out IPAddress? address) ? address : null)
            .OfType<IPAddress>()
            .ToHashSet();
}
