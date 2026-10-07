using System.Collections.Immutable;
using System.Threading.Tasks;
using Orleans;

namespace Vortex.Primitives.Server.Grains;

/// <summary>
/// Cluster-wide singleton (grain key <c>SingletonGrainId.GLOBAL</c>) serving runtime-tunable server
/// configuration as typed key/value pairs. Writes go through <see cref="SetValueAsync"/>
/// (write-through: DB + in-memory cache), so every read is immediately consistent without a reload —
/// the reason tunable gameplay config lives here rather than in a boot-cached provider or (for
/// runtime-editable knobs) appsettings. Bootstrap/secret settings stay in appsettings.
/// </summary>
public interface IServerConfigGrain : IGrainWithStringKey
{
    /// <summary>Raw string value for <paramref name="key"/>, or null if unset.</summary>
    Task<string?> GetValueAsync(string key);

    /// <summary>Value parsed as an int, or <paramref name="fallback"/> if unset/unparseable.</summary>
    Task<int> GetIntAsync(string key, int fallback);

    /// <summary>Value parsed as a bool, or <paramref name="fallback"/> if unset/unparseable.</summary>
    Task<bool> GetBoolAsync(string key, bool fallback);

    /// <summary>
    /// Value parsed as a double, or <paramref name="fallback"/> if unset/unparseable. Parsed
    /// invariantly, so a rate typed as "0.5" reads the same whatever locale the host runs under.
    /// </summary>
    Task<double> GetDoubleAsync(string key, double fallback);

    /// <summary>Upserts a config value (write-through: DB then cache, so reads are instantly live).</summary>
    Task SetValueAsync(string key, string value, string? description);

    /// <summary>A snapshot of every currently-set config key/value (whatever exists in the cache/DB).</summary>
    Task<ImmutableDictionary<string, string>> GetAllAsync();

    /// <summary>
    /// All requested keys that are currently set, in one round trip — the way a subsystem resolves a
    /// whole settings group (a feature's knobs) without N sequential grain calls to
    /// this cluster singleton. Missing keys are simply absent; parse with the
    /// <c>ServerConfigValues</c> readers so fallbacks apply per key.
    /// </summary>
    Task<ImmutableDictionary<string, string>> GetManyAsync(ImmutableArray<string> keys);

    /// <summary>Re-reads the whole config from the database (for out-of-band / direct-DB edits).</summary>
    Task ReloadAsync();

    /// <summary>The message-of-the-day lines (key <c>motd.lines</c>, stored as a JSON string array).</summary>
    Task<ImmutableArray<string>> GetMotdAsync();

    /// <summary>Replaces the message-of-the-day lines.</summary>
    Task SetMotdAsync(ImmutableArray<string> lines);
}
