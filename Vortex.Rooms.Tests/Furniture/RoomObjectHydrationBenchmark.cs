using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Vortex.Furniture;
using Vortex.Furniture.Providers;
using Vortex.Primitives.Furniture;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Furniture.Snapshots;
using Vortex.Primitives.Furniture.StuffData;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Rooms.Object.Logic.Furniture.Floor;
using Vortex.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace Vortex.Rooms.Tests.Furniture;

/// <summary>
/// What hydrating a room full of interactive furniture costs, and whether the reflection in that
/// path is worth removing. This is OQ-7, measured rather than argued.
/// </summary>
/// <remarks>
/// <para>
/// Every room object with behaviour gets its logic built by
/// <c>ActivatorUtilities.CreateInstance(sp, logicType, ctx)</c> — reflection, once per object, at
/// hydration. A room with thousands of items pays it thousands of times while somebody waits at the
/// door, and the note's instruction was explicit: measure first. The alternative is not exotic —
/// <c>ActivatorUtilities.CreateFactory</c> compiles the same construction once and hands back a
/// delegate — so the only question ever worth asking was how much it buys.
/// </para>
/// <para>
/// Off unless <c>VORTEX_BENCH=1</c>: a wall-clock
/// assertion in the gate is a flake with a schedule.
/// </para>
/// <para>
/// <c>VORTEX_BENCH=1 dotnet test Vortex.Rooms.Tests --filter RoomObjectHydrationBenchmark</c>
/// </para>
/// </remarks>
public sealed class RoomObjectHydrationBenchmark(ITestOutputHelper output)
{
    /// <summary>A big room. Habbo's own cap is 2,400 items, so this is the shape of the worst case.</summary>
    private const int ITEMS = 2_000;

    private const int ROUNDS = 5;

    [Fact]
    public void MeasureAndWriteTheBaseline()
    {
        if (Environment.GetEnvironmentVariable("VORTEX_BENCH") != "1")
        {
            output.WriteLine("VORTEX_BENCH is not 1 — skipping. See the class summary.");

            return;
        }

        ServiceProvider services = new ServiceCollection()
            .AddSingleton<IStuffDataFactory, StuffDataFactory>()
            .BuildServiceProvider();

        IRoomFloorItemContext ctx = Context();

        // Discarded: whichever path runs first pays the JIT for both.
        Reflection(services, ctx, 50);
        Compiled(services, ctx, 50);

        double reflection = Best(() => Reflection(services, ctx, ITEMS));
        double compiled = Best(() => Compiled(services, ctx, ITEMS));

        string report = Report(reflection, compiled);

        output.WriteLine(report);

        Write(report);

        services.Dispose();
    }

    /// <summary>The bare context a logic's base class reads while it is built: the definition's
    /// stuff-data type and the object's extra data.</summary>
    private static IRoomFloorItemContext Context()
    {
        FurnitureDefinitionSnapshot definition = new()
        {
            Id = 1,
            SpriteId = 1,
            Name = "bench_gate",
            ProductType = ProductType.Floor,
            FurniCategory = FurnitureCategory.Default,
            LogicName = "default_floor",
            TotalStates = 1,
            Width = 1,
            Length = 1,
            StackHeight = Altitude.FromInt(100),
            CanStack = true,
            CanWalk = false,
            CanSit = false,
            CanLay = false,
            CanRecycle = false,
            CanTrade = true,
            CanGroup = false,
            CanSell = true,
            UsagePolicy = FurnitureUsageType.Nobody,
            ExtraData = null,
            StuffDataType = StuffDataType.LegacyKey,
        };
        IExtraData extraData = new ExtraData(null);
        IRoomFloorItem item = FakeProxy.Create<IRoomFloorItem>(call =>
            call.Method.Name == "get_ExtraData" ? extraData : null
        );

        return FakeProxy.Create<IRoomFloorItemContext>(call =>
            call.Method.Name switch
            {
                "get_Definition" => definition,
                "get_RoomObject" => item,
                _ => null,
            }
        );
    }

    /// <summary>What the emulator does today: reflect over the constructor on every object.</summary>
    private static double Reflection(
        IServiceProvider services,
        IRoomFloorItemContext ctx,
        int items
    )
    {
        long startedAt = Stopwatch.GetTimestamp();

        for (int i = 0; i < items; i++)
        {
            _ = ActivatorUtilities.CreateInstance(services, typeof(FurnitureGateLogic), ctx);
        }

        return Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
    }

    /// <summary>
    /// The alternative: compile the construction once per logic type and call a delegate per object.
    /// Registration already happens once per type, so the factory has an obvious place to live.
    /// </summary>
    private static double Compiled(IServiceProvider services, IRoomFloorItemContext ctx, int items)
    {
        ObjectFactory factory = ActivatorUtilities.CreateFactory(
            typeof(FurnitureGateLogic),
            [typeof(IRoomFloorItemContext)]
        );

        long startedAt = Stopwatch.GetTimestamp();

        for (int i = 0; i < items; i++)
        {
            _ = factory(services, [ctx]);
        }

        return Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
    }

    /// <summary>
    /// The fastest of several rounds, not the mean. A benchmark this short is measuring the machine's
    /// worst interruptions as much as the code, and the minimum is the run that was interrupted least.
    /// </summary>
    private static double Best(Func<double> round)
    {
        double best = double.MaxValue;

        for (int i = 0; i < ROUNDS; i++)
        {
            best = Math.Min(best, round());
        }

        return best;
    }

    private static string Report(double reflection, double compiled)
    {
        StringBuilder sb = new();

        sb.AppendLine("# Room hydration baseline (OQ-7)");
        sb.AppendLine();
        sb.AppendLine(
            CultureInfo.InvariantCulture,
            $"Building the logic for {ITEMS:N0} interactive floor items — the shape of a full room — "
                + $"best of {ROUNDS} rounds."
        );
        sb.AppendLine();
        sb.AppendLine("| Path | ms for the room | µs per item |");
        sb.AppendLine("|---|---:|---:|");
        sb.AppendLine(
            CultureInfo.InvariantCulture,
            $"| `ActivatorUtilities.CreateInstance` (today) | {reflection:F2} | {reflection * 1000 / ITEMS:F2} |"
        );
        sb.AppendLine(
            CultureInfo.InvariantCulture,
            $"| `ActivatorUtilities.CreateFactory` (compiled once per type) | {compiled:F2} | {compiled * 1000 / ITEMS:F2} |"
        );
        sb.AppendLine();
        string ratio =
            compiled > 0
                ? string.Format(CultureInfo.InvariantCulture, " ({0:F1}×)", reflection / compiled)
                : string.Empty;

        sb.AppendLine(
            string.Format(
                CultureInfo.InvariantCulture,
                "Difference for a full room: **{0:F2} ms**{1}.",
                reflection - compiled,
                ratio
            )
        );
        sb.AppendLine();
        sb.AppendLine(
            "Read it against what a player is waiting for. Room entry already costs a database read "
                + "for the items themselves; a saving smaller than that read is a saving nobody can "
                + "perceive, and the reflection stays because it is the version that needs no cache "
                + "to invalidate when a plugin unloads."
        );
        sb.AppendLine();
        sb.AppendLine("## Measured on");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"- {Environment.OSVersion.VersionString}");
        sb.AppendLine(
            CultureInfo.InvariantCulture,
            $"- {Environment.ProcessorCount} logical processors"
        );
        sb.AppendLine(CultureInfo.InvariantCulture, $"- .NET {Environment.Version}");

        return sb.ToString();
    }

    private void Write(string report)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);

        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "docs")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            output.WriteLine("No docs/ directory above the test binary — report not written.");

            return;
        }

        string target = Path.Combine(dir.FullName, "docs", "architecture-v4", "benchmarks");

        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "room-hydration.md"), report);

        output.WriteLine($"Wrote {Path.Combine(target, "room-hydration.md")}");
    }
}
