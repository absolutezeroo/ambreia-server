using System.Threading.Tasks;

namespace Vortex.Primitives.Plugins.Exports;

/// <summary>The generic form of <see cref="IExportBinder.ExportAsync"/>: a static extension, resolved
/// at compile time, so it never goes through the virtual dispatch that hung (see the interface).</summary>
public static class ExportBinderExtensions
{
    public static Task ExportAsync<T>(this IExportBinder binder, string exportKey, T instance)
        where T : class => binder.ExportAsync(typeof(T), exportKey, instance);
}
