using System;
using System.Collections.Generic;

namespace Vortex.Primitives.Permissions;

/// <summary>
/// An immutable, resolved snapshot of a subject's effective authorization: the union of capabilities
/// granted by its roles. A subject holding <see cref="Capabilities.Wildcard"/> passes every check.
/// </summary>
public sealed class PermissionSet
{
    public static readonly PermissionSet Empty = new([], []);

    private readonly HashSet<string> _capabilities;
    private readonly bool _wildcard;

    public PermissionSet(
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> capabilities
    )
    {
        Roles = roles;
        _capabilities = new HashSet<string>(capabilities, StringComparer.Ordinal);
        _wildcard = _capabilities.Contains(Vortex.Primitives.Permissions.Capabilities.Wildcard);
    }

    public IReadOnlyCollection<string> Roles { get; }

    public IReadOnlyCollection<string> Capabilities => _capabilities;

    public bool IsSuperUser => _wildcard;

    public bool Has(string capability) =>
        _wildcard || (!string.IsNullOrEmpty(capability) && _capabilities.Contains(capability));

    public bool HasAny(params string[] capabilities) =>
        HasAny((IReadOnlyCollection<string>)capabilities);

    /// <summary>
    /// Overload for the declared capability sets (<see cref="Capabilities.Server.All"/> and
    /// friends), which are exposed as read-only collections rather than arrays so no caller can
    /// mutate the canonical list.
    /// </summary>
    public bool HasAny(IReadOnlyCollection<string> capabilities)
    {
        if (_wildcard)
        {
            return true;
        }

        foreach (string capability in capabilities)
        {
            if (_capabilities.Contains(capability))
            {
                return true;
            }
        }

        return false;
    }
}
