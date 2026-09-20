using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Vortex.Runtime.AssemblyProcessing;

public static class AssemblyMemoryLoader
{
    public static LoadedAssembly LoadFromBytes(string mainDllPath)
    {
        if (!File.Exists(mainDllPath))
        {
            throw new FileNotFoundException("Plugin entry DLL not found.", mainDllPath);
        }

        string baseDir = Path.GetDirectoryName(mainDllPath)!;
        string entryName = Path.GetFileNameWithoutExtension(mainDllPath);
        Dictionary<string, (byte[] asm, byte[]? pdb)> managed = new Dictionary<
            string,
            (byte[] asm, byte[]? pdb)
        >(StringComparer.OrdinalIgnoreCase);
        List<string> shadowed = [];

        static byte[] ReadAll(string path) => File.ReadAllBytes(path);

        foreach (string dll in Directory.EnumerateFiles(baseDir, "*.dll"))
        {
            string name = Path.GetFileNameWithoutExtension(dll);

            if (managed.ContainsKey(name))
            {
                continue;
            }

            // A contract assembly copied into the plugin folder used to be byte-loaded here, giving
            // the plugin its own IVortexPlugin, its own IEventHandler<> and its own event records —
            // none of which the host would ever match, and nothing said so. Packaging was expected
            // to avoid it and nothing enforced it. Now it resolves from the default context like
            // every other shared type, so a mis-packaged plugin works instead of failing silently.
            if (
                !string.Equals(name, entryName, StringComparison.OrdinalIgnoreCase)
                && ByteLoadingAlc.IsHostContractAssembly(name)
            )
            {
                shadowed.Add(name);

                continue;
            }

            byte[] asmBytes = ReadAll(dll);
            string pdbPath = Path.ChangeExtension(dll, ".pdb");
            byte[]? pdbBytes = File.Exists(pdbPath) ? ReadAll(pdbPath) : null;

            managed[name] = (asmBytes, pdbBytes);
        }

        ByteLoadingAlc alc = new ByteLoadingAlc(baseDir, managed, entryName);
        Assembly asm = alc.LoadFromAssemblyName(new AssemblyName(entryName));

        return new LoadedAssembly(asm, alc, baseDir, shadowed);
    }

    /// <summary>
    ///     Requests the unload of a collectible context and answers a <see cref="WeakReference"/> to
    ///     it, so a caller that wants to know whether it really went away can ask later.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     This replaces an <c>UnloadAndWaitAsync</c> that polled the weak reference itself, for up
    ///     to five seconds, from inside the method whose own parameter held the context. An
    ///     <c>async</c> method hoists its parameters into a state machine that outlives every await
    ///     in it, so that method held the context alive for exactly as long as it waited for the
    ///     context to die: it returned <c>false</c> on every input, including an assembly nothing
    ///     else had ever touched, and the callers' "possible memory leak" warning fired every time
    ///     and meant nothing. It cost five seconds per plugin teardown to learn nothing.
    ///     </para>
    ///     <para>
    ///     Nothing is waited for here because nothing useful can be: a context is collected once the
    ///     frames that used it unwind, which by definition has not happened yet in the caller.
    ///     Whether the contexts actually come back out is a question for a test whose frame never
    ///     held them — <c>FailedActivationRetentionTests</c> and <c>DashboardUnloadabilityTests</c>
    ///     ask it, and answer yes.
    ///     </para>
    ///     <para>
    ///     Never inlined: inlined into its caller, these locals would share that frame and keep the
    ///     context reachable for as long as the caller runs.
    ///     </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static WeakReference Unload(ByteLoadingAlc alc)
    {
        WeakReference weak = new WeakReference(alc);

        alc.Unload();

        return weak;
    }
}
