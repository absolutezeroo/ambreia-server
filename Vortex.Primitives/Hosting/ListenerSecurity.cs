using System;
using System.Net;

namespace Vortex.Primitives.Hosting;

/// <summary>
/// Outcome of a listener-security check. <see cref="IsAllowed"/> false means the service must refuse
/// to start; <see cref="Message"/> is then the operator-facing explanation (never null when refused).
/// A allowed result may still carry a <see cref="Message"/> — that is a warning to log, not a refusal.
/// </summary>
public readonly record struct ListenerSecurityResult(bool IsAllowed, string? Message)
{
    public static ListenerSecurityResult Allowed() => new(true, null);

    public static ListenerSecurityResult AllowedWithWarning(string message) => new(true, message);

    public static ListenerSecurityResult Refused(string message) => new(false, message);
}

/// <summary>
/// Guards the HTTP listener (the client web API) against being exposed off-box in cleartext. It
/// authenticates with credentials and carries session cookies, so a non-local
/// bind without TLS leaks those to anyone on the path. Binding to a non-local address over plain HTTP
/// is therefore refused unless the operator explicitly opts in.
/// </summary>
public static class ListenerSecurity
{
    /// <summary>
    /// True when the configured bind address only ever accepts connections from this machine.
    /// Wildcard binds (<c>0.0.0.0</c>, <c>::</c>, <c>*</c>, <c>+</c>) are deliberately NOT local:
    /// they accept traffic on every interface, which is exactly the remote-exposure case.
    /// </summary>
    public static bool IsLocalHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            // An empty host in Kestrel's URL syntax means "any interface" — treat as remote.
            return false;
        }

        string trimmed = host.Trim();

        if (
            trimmed is "*" or "+" or "0.0.0.0" or "::" or "[::]"
            || trimmed.Equals("0.0.0.0:0", StringComparison.Ordinal)
        )
        {
            return false;
        }

        if (trimmed.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Strip the brackets used for IPv6 literals in URL authority form (e.g. "[::1]").
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            trimmed = trimmed[1..^1];
        }

        return IPAddress.TryParse(trimmed, out IPAddress? address) && IPAddress.IsLoopback(address);
    }

    /// <summary>
    /// Validates one service's listener configuration.
    /// </summary>
    /// <param name="serviceName">Human-readable service name used in the message (e.g. "Vortex web API").</param>
    /// <param name="host">Configured bind address.</param>
    /// <param name="port">Configured cleartext port, used in the message.</param>
    /// <param name="httpsEnabled">Whether the service also serves HTTPS and redirects HTTP to it.</param>
    /// <param name="allowInsecureRemoteHttp">Operator's explicit opt-in to cleartext off-box exposure.</param>
    /// <param name="allowOptionPath">
    /// Exact configuration key of the opt-in, quoted verbatim in the refusal message so the operator
    /// does not have to guess it (e.g. "Vortex:WebApi:AllowInsecureRemoteHttp").
    /// </param>
    public static ListenerSecurityResult ValidateListener(
        string serviceName,
        string host,
        int port,
        bool httpsEnabled,
        bool allowInsecureRemoteHttp,
        string allowOptionPath
    )
    {
        if (IsLocalHost(host))
        {
            return ListenerSecurityResult.Allowed();
        }

        if (httpsEnabled)
        {
            return ListenerSecurityResult.Allowed();
        }

        if (allowInsecureRemoteHttp)
        {
            return ListenerSecurityResult.AllowedWithWarning(
                $"{serviceName} is bound to the non-local address '{host}' on port {port} over plain "
                    + $"HTTP because '{allowOptionPath}' is set. Credentials and session cookies are "
                    + "transmitted in cleartext and can be read or replayed by anyone on the network "
                    + "path. Terminate TLS in front of this listener or enable HTTPS on it."
            );
        }

        return ListenerSecurityResult.Refused(
            $"Refusing to start {serviceName}: it is configured to listen on the non-local address "
                + $"'{host}' port {port} with HTTPS disabled. Logins, passwords and session cookies "
                + "would be transmitted in cleartext and could be read or replayed by anyone on the "
                + "network path. Either enable HTTPS for this service, bind it back to a local "
                + "address (localhost, 127.0.0.1 or ::1) and put a TLS-terminating reverse proxy in "
                + $"front of it, or set '{allowOptionPath}' to true to explicitly accept cleartext "
                + "remote exposure."
        );
    }
}
