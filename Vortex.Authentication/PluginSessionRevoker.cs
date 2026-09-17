using System;
using Vortex.Primitives.Authentication;
using Vortex.Primitives.Plugins;
using Vortex.Primitives.Plugins.Exports;

namespace Vortex.Authentication;

/// <summary>
/// Revokes the sessions held by a plugin, without the hotel ever holding the plugin.
/// </summary>
/// <remarks>
/// <para>
/// A password change has to sign the account out everywhere, and "everywhere" includes surfaces the
/// hotel does not own: the dashboard keeps its own session store, and so would any plugin that
/// signs people in. <see cref="AccountPasswordService" /> takes
/// <c>IEnumerable&lt;IAccountSessionRevoker&gt;</c> and snapshots it into an array at construction,
/// so a plugin registering itself there directly would leave the host holding one of the plugin's
/// objects for the life of the process.
/// </para>
/// <para>
/// <b>That is not a tidiness problem, it is the whole obstacle.</b> One surviving reference to a
/// type inside a collectible <c>AssemblyLoadContext</c> keeps the context alive, so the plugin could
/// never be unloaded and never be reloaded — and a dashboard that cannot be reloaded is a dashboard
/// that ships by restarting the hotel.
/// </para>
/// <para>
/// So the host holds this instead: an object of its own that asks the export for the current
/// revoker each time it is used. <see cref="IExport{T}" /> is the indirection — the plugin swaps
/// what is behind it on every load, and nothing on this side changes or is retained.
/// </para>
/// </remarks>
public sealed class PluginSessionRevoker : IAccountSessionRevoker
{
    private readonly IExport<IAccountSessionRevoker> _export;

    public PluginSessionRevoker(IPluginCatalog plugins)
    {
        ArgumentNullException.ThrowIfNull(plugins);

        // Asked for once and kept: the export SLOT is the host's own object and outlives every
        // plugin that fills it. It is what is inside that comes and goes.
        _export = plugins.GetExport<IAccountSessionRevoker>(IAccountSessionRevoker.ExportKey);
    }

    /// <summary>
    /// What the log calls the sessions this dropped. Read through the export too, so the line says
    /// "dashboard" when the dashboard is loaded rather than a name the host invented for it.
    /// </summary>
    public string SessionKind => _export.IsBound ? _export.Current.SessionKind : "plugin";

    /// <summary>
    /// Zero when no plugin has published a revoker, which is the ordinary state of a hotel that
    /// installed none — not a failure, and not something to throw about during a password change.
    /// </summary>
    public int RemoveAllForAccount(int accountId) =>
        _export.IsBound ? _export.Current.RemoveAllForAccount(accountId) : 0;
}
