using System;

namespace Vortex.Main.Configuration;

/// <summary>
///     Silo networking endpoint, bound from <c>Vortex:Orleans</c>. Defaults match the previous
///     hardcoded single-node localhost setup, so binding this changes nothing until overridden.
/// </summary>
public sealed class OrleansHostConfig
{
    public const string SECTION_NAME = "Vortex:Orleans";

    public string AdvertisedIp { get; init; } = "127.0.0.1";
    public int SiloPort { get; init; } = 11111;
    public int GatewayPort { get; init; } = 3000;

    /// <summary>
    ///     How long a grain may sit idle before Orleans deactivates it, as <c>hh:mm:ss</c>. The
    ///     default matches the value this was hardcoded to. Raising it keeps hot rooms and players
    ///     resident (fewer rehydration round-trips to MySQL) at the cost of silo memory; Orleans
    ///     refuses anything below its one-minute collection quantum.
    /// </summary>
    public TimeSpan GrainCollectionAge { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    ///     Single-node localhost clustering with in-memory grain storage/streams loses all
    ///     <c>[PersistentState]</c> grain state on every process restart and cannot scale beyond one
    ///     silo. Outside the Development environment this is refused at startup unless explicitly
    ///     opted into here — set this only for a deliberate single-node, restart-tolerant-data-loss
    ///     deployment, and configure a persistent clustering/storage provider otherwise.
    /// </summary>
    public bool AllowUnclusteredOutsideDevelopment { get; init; }

    /// <summary>
    ///     The configuration can already form a multi-silo cluster; several components still assume
    ///     they are the only silo, and none of them fail loudly when they are not:
    ///     <list type="bullet">
    ///         <item><c>FurnitureDefinitionProvider</c> is a singleton whose <c>ReloadAsync</c>
    ///         reloads the calling process only — every other silo keeps serving the old
    ///         definitions indefinitely.</item>
    ///         <item>Room fan-out uses memory streams, which the official guidance restricts to
    ///         development and testing where durability matters.</item>
    ///     </list>
    ///     So a second silo is refused at startup unless it is a deliberate decision recorded here.
    ///     Set this only once those components are cluster-aware — the failure mode it guards
    ///     against is silent staleness, which is the kind nobody reports as a bug.
    /// </summary>
    public bool MultiSiloReady { get; init; }

    /// <summary>
    ///     "localhost" (default, unchanged) or "adonet" for multi-silo clustering backed by the same
    ///     MySQL database as <c>Vortex:Database:ConnectionString</c>. Selecting "adonet" requires
    ///     Orleans's official clustering SQL scripts (https://aka.ms/orleans-sql-scripts) to already
    ///     be applied to that database — this is a deployment prerequisite this process cannot apply
    ///     for you, the same way EF migrations must already be applied before startup.
    /// </summary>
    public string ClusteringProvider { get; init; } = "localhost";

    /// <summary>
    ///     "memory" (default, unchanged) or "adonet" to persist PubSubStore (the only grain storage
    ///     actually used — see ORL-01) so in-flight stream messages survive a silo restart. Same SQL
    ///     script prerequisite as <see cref="ClusteringProvider"/>.
    /// </summary>
    public string GrainStorageProvider { get; init; } = "memory";

    /// <summary>
    ///     ADO.NET provider invariant name for "adonet" mode. Defaults to the driver this project
    ///     already ships (MySqlConnector, via Pomelo.EntityFrameworkCore.MySql); override only if
    ///     pointing Orleans at a different ADO.NET provider/database engine.
    /// </summary>
    public string Invariant { get; init; } = "MySqlConnector";
}
