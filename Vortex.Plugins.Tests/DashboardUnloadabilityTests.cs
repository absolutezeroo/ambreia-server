using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Vortex.Primitives.Plugins;
using Vortex.Runtime.AssemblyProcessing;
using Xunit;

namespace Vortex.Plugins.Tests;

/// <summary>
///     Can the dashboard's assembly be unloaded once nothing uses it any more?
/// </summary>
/// <remarks>
///     <para>
///     This is the one question standing between the dashboard and becoming a real
///     <c>IVortexPlugin</c> — a unit an operator drops into a folder and reloads without restarting
///     the hotel. Everything else in that conversion is mechanical; this is not, because
///     <c>AssemblyLoadContext.Unload()</c> does not unload. It asks, and the answer is no for as long
///     as one reference survives anywhere: a rooted service provider, a host singleton holding a
///     plugin instance, a static, an event subscription — or a local variable in the very method
///     doing the asking.
///     </para>
///     <para>
///     <c>Vortex.Dashboard.API</c> is the hardest case in the repository — around 120 registrations,
///     a 38 MB gamedata cache, an ASP.NET Core host — so if it comes back out cleanly, the smaller
///     candidates will too. If it does not, this is where the pin gets found, before anybody spends
///     days on the conversion.
///     </para>
///     <para>
///     <b>Why the measurement is shaped like this.</b> The obvious version — hand the context to a
///     helper that unloads it and waits — cannot ever report success in a Debug build: the helper's
///     own parameter keeps the context alive for the whole call, so the weak reference it polls is
///     always alive. That is not a theory; the first version of this file used
///     <c>AssemblyMemoryLoader.UnloadAndWaitAsync</c> and the CONTROL failed, which is the only
///     reason it was caught. The documented shape is the one here: a non-inlined method loads,
///     uses, unloads and returns nothing but a <see cref="WeakReference"/>, and the caller — whose
///     frame never held the context — drives the collection.
///     </para>
///     <para>
///     <b>This test is not referenced by the dashboard and must never be.</b>
///     <c>ByteLoadingAlc.IsHostContractAssembly</c> delegates any <c>Vortex.*</c> assembly the host
///     already holds to the default context, so a project reference here would make the measurement
///     pass trivially by loading nothing collectible at all.
///     </para>
/// </remarks>
public sealed class DashboardUnloadabilityTests
{
    /// <summary>
    ///     The control. Without it the dashboard result is unreadable: an instrument that can never
    ///     report success fails on everything alike and looks exactly like a finding.
    /// </summary>
    [Fact]
    public void ASmallPluginAssembly_Unloads()
    {
        // Into a folder of its own, which is what a plugin folder is. Loaded from this project's
        // output instead, `LoadFromBytes` would byte-load the test host's own copies of
        // Microsoft.Extensions.* — they sit beside it there — giving the plugin a second
        // `IServiceCollection` that matches nothing, and `GetTypes()` fails with "does not have an
        // implementation" long before anything is measured. The dashboard's own bin folder does not
        // have that problem: it is a web project, so those assemblies come from the shared framework
        // and are never copied next to it.
        string dll = StagePluginAlone("Vortex.Plugins.TestPlugin.dll");

        WeakReference alc = LoadTouchAndUnload(dll);

        Collected(alc)
            .Should()
            .BeTrue(
                "if even this does not unload, the instrument is broken and the dashboard result "
                    + "says nothing about the dashboard"
            );
    }

    /// <remarks>
    ///     The web API went through the same conversion and is measured the same way; neither project
    ///     may be referenced from here, for the reason in the class remarks.
    /// </remarks>
    [Theory]
    [InlineData(
        "Vortex.Dashboard.API",
        "Vortex.Dashboard.API.DashboardPlugin",
        "turbo-dashboard-api"
    )]
    [InlineData("Vortex.WebApi", "Vortex.WebApi.WebApiPlugin", "turbo-webapi")]
    public void APluginAssembly_UnloadsOnceItsServiceProviderIsDisposed(
        string project,
        string pluginType,
        string key
    )
    {
        string? dll = FindPluginAssembly(project);

        // Built by a different project, so a machine that has not built it yet fails loudly rather
        // than reporting a pass it never measured.
        dll.Should()
            .NotBeNull($"{project}.dll must be built for this measurement to mean anything");

        WeakReference alc = LoadConfigureAndUnload(dll!, pluginType, key);

        Collected(alc)
            .Should()
            .BeTrue(
                "an assembly that will not unload cannot be a hot-reloadable plugin: every reload "
                    + "would leak the whole graph. If this fails, the pin is a reference the host "
                    + "keeps to a plugin type, and IExport<T> is the indirection that breaks it."
            );
    }

    /// <summary>
    ///     Loads an assembly, realises its types, unloads, and keeps nothing.
    /// </summary>
    /// <remarks>
    ///     <c>NoInlining</c> is not decoration. Inlined into the caller, these locals would share the
    ///     caller's frame and stay reachable from a stack slot for the whole test — one of the
    ///     documented ways an unload silently fails to happen.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LoadTouchAndUnload(string dll)
    {
        LoadedAssembly loaded = AssemblyMemoryLoader.LoadFromBytes(dll);

        // Touched rather than merely loaded, so the comparison with the dashboard is fair: an
        // assembly whose types were never realised is an easier thing to collect.
        loaded.Assembly.GetTypes();

        WeakReference weak = new(loaded.Alc);

        loaded.Alc.Unload();

        return weak;
    }

    /// <summary>
    ///     Loads a plugin, runs its registrations, disposes everything, unloads, and keeps nothing.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LoadConfigureAndUnload(string dll, string pluginType, string key)
    {
        LoadedAssembly loaded = AssemblyMemoryLoader.LoadFromBytes(dll);

        Type moduleType = loaded.Assembly.GetType(pluginType, throwOnError: true)!;
        object module = Activator.CreateInstance(moduleType)!;

        ServiceCollection services = [];

        // A manifest built here rather than read from the plugin's folder: ConfigureServices only
        // needs one to exist, and the question being asked is about types, not packaging.
        PluginManifest manifest = new()
        {
            Name = key,
            Key = key,
            Version = "1.0.0",
            Author = "Vortex",
            AssemblyFile = Path.GetFileName(dll),
        };

        // Through reflection because this project cannot reference the plugin — see the remarks
        // on the class. Registration alone is enough for the question being asked: it names every
        // service type the plugin knows, which is what has to come back out.
        moduleType
            .GetMethod("ConfigureServices", BindingFlags.Public | BindingFlags.Instance)!
            .Invoke(module, [services, manifest]);

        services.Count.Should().BeGreaterThan(0, "the plugin registers its services");

        ServiceProvider provider = services.BuildServiceProvider();

        provider.Dispose();

        WeakReference weak = new(loaded.Alc);

        loaded.Alc.Unload();

        return weak;
    }

    /// <summary>
    ///     Drives the collection from a frame that never held the context, and answers whether it
    ///     went away. Three collections per round because an unload finishes across more than one.
    /// </summary>
    private static bool Collected(WeakReference alc, int maxMs = 10000)
    {
        Stopwatch clock = Stopwatch.StartNew();

        while (clock.ElapsedMilliseconds < maxMs)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            if (!alc.IsAlive)
            {
                return true;
            }
        }

        return !alc.IsAlive;
    }

    /// <summary>Copies one assembly into an empty directory and answers its new path.</summary>
    private static string StagePluginAlone(string fileName)
    {
        string source = Path.Combine(AppContext.BaseDirectory, fileName);

        File.Exists(source).Should().BeTrue($"{fileName} is copied into this project's output");

        string folder = Path.Combine(Path.GetTempPath(), $"vortex-unload-{Guid.NewGuid():N}");

        Directory.CreateDirectory(folder);

        string destination = Path.Combine(folder, fileName);

        File.Copy(source, destination, overwrite: true);

        return destination;
    }

    /// <summary>
    ///     A plugin project's build output, found by walking up to the repository root rather than by
    ///     a fixed number of <c>..</c> segments, which changes with the build configuration.
    /// </summary>
    private static string? FindPluginAssembly(string project)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName,
                project,
                "bin",
                "Debug",
                "net10.0",
                $"{project}.dll"
            );

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
