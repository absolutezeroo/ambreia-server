using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Vortex.Runtime.AssemblyProcessing;

public sealed class ByteLoadingAlc(
    string basePath,
    IReadOnlyDictionary<string, (byte[] asm, byte[]? pdb)> managed,
    string entryAssemblyName
) : AssemblyLoadContext(isCollectible: true)
{
    private readonly IReadOnlyDictionary<string, (byte[] asm, byte[]? pdb)> _managed = managed;
    private readonly AssemblyDependencyResolver _resolver = new(basePath);
    private readonly string _entryAssemblyName = entryAssemblyName;

    /// <summary>
    /// True for an assembly whose types the host and a plugin must agree on — <c>IVortexPlugin</c>,
    /// <c>IEventHandler&lt;&gt;</c>, every event record. Loading a second copy of one into this
    /// context produces types that are structurally identical and never equal, so a handler
    /// registers against an event the publisher does not raise and no error is ever reported.
    /// <para>
    /// It is not enough for the name to look like ours: a plugin split over
    /// <c>Vortex.MyPlugin.Core</c> and <c>Vortex.MyPlugin</c> must still load its own halves. What
    /// makes an assembly shared is that the host already has it, so that is what is asked — and
    /// nothing else. There used to be a <c>Vortex.</c> prefix test in front of this, and it was
    /// wrong for the same reason a name test is always wrong here: the assemblies a plugin and the
    /// host must agree on are not only the ones this project wrote.
    /// </para>
    /// <para>
    /// The dashboard is what proved it. It publishes <c>Microsoft.EntityFrameworkCore</c> beside
    /// itself, like any project with that package reference; the prefix test let that copy be
    /// byte-loaded, so the plugin got its own <c>DbContext</c> type while <c>VortexDbContext</c>
    /// still derived from the host's. Registering <c>IDbContextFactory&lt;VortexDbContext&gt;</c>
    /// then failed with "violates the constraint of type parameter 'TContext'" — a constraint that
    /// reads as impossible until you notice there are two <c>DbContext</c>s in the process.
    /// </para>
    /// <para>
    /// The consequence is deliberate: a plugin cannot carry its own version of a package the host
    /// already has. That is the ordinary rule for in-process plugins, and the alternative — two
    /// versions of EF Core, of ASP.NET, of the logging abstractions — is the failure above wearing
    /// a different type name each time.
    /// </para>
    /// </summary>
    public static bool IsHostContractAssembly(string simpleName)
    {
        return HOST_CONTRACT_CACHE.GetOrAdd(
            simpleName,
            static name =>
            {
                try
                {
                    return Default.LoadFromAssemblyName(new AssemblyName(name)) is not null;
                }
                catch (Exception ex)
                    when (ex
                            is FileNotFoundException
                                or FileLoadException
                                or BadImageFormatException
                    )
                {
                    return false;
                }
            }
        );
    }

    private static readonly ConcurrentDictionary<string, bool> HOST_CONTRACT_CACHE = new(
        StringComparer.OrdinalIgnoreCase
    );

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        string simple = assemblyName.Name!;

        // Returning null delegates to the default context, which is where the host's own copy is.
        if (
            !string.Equals(simple, _entryAssemblyName, StringComparison.OrdinalIgnoreCase)
            && IsHostContractAssembly(simple)
        )
        {
            return null;
        }

        if (_managed.TryGetValue(simple, out (byte[] asm, byte[]? pdb) blob))
        {
            using MemoryStream msAsm = new MemoryStream(blob.asm, writable: false);

            if (blob.pdb is { } pdb)
            {
                using MemoryStream msPdb = new MemoryStream(pdb, writable: false);

                return LoadFromStream(msAsm, msPdb);
            }

            return LoadFromStream(msAsm);
        }

        string? path = _resolver.ResolveAssemblyToPath(assemblyName);
        if (path is null)
        {
            return null;
        }

        if (Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            string pdbPath = Path.ChangeExtension(path, ".pdb");
            byte[] asmBytes = File.ReadAllBytes(path);
            byte[]? pdbBytes = File.Exists(pdbPath) ? File.ReadAllBytes(pdbPath) : null;

            using MemoryStream msAsm = new MemoryStream(asmBytes, writable: false);

            if (pdbBytes is { })
            {
                using MemoryStream msPdb = new MemoryStream(pdbBytes, writable: false);
                return LoadFromStream(msAsm, msPdb);
            }

            return LoadFromStream(msAsm);
        }

        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string? path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);

        if (path is null)
        {
            return IntPtr.Zero;
        }

        return LoadUnmanagedDllFromPath(path);
    }
}
