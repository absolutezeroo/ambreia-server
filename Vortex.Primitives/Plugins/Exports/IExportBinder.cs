using System;
using System.Threading.Tasks;

namespace Vortex.Primitives.Plugins.Exports;

/// <summary>
/// Handed to a plugin during activation to publish the objects it exports.
/// </summary>
/// <remarks>
/// Not generic on purpose. The contract type usually comes from the plugin's own collectible load
/// context, and a generic virtual method instantiated over such a type spun forever inside the
/// runtime's dispatch once another plugin context had been unloaded (hang dumps of
/// <c>Vortex.Plugins.Tests</c>, 2026-10-07). The generic form is a static extension
/// (<see cref="ExportBinderExtensions" />): it is resolved at compile time and never goes through
/// virtual dispatch.
/// </remarks>
public interface IExportBinder
{
    Task ExportAsync(Type contract, string exportKey, object instance);
}
