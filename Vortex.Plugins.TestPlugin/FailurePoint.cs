namespace Vortex.Plugins.TestPlugin;

/// <summary>
/// Where <see cref="TestPlugin" /> should throw during activation.
/// <para>
/// Selected through an environment variable rather than a static field or a constructor argument:
/// the plugin is byte-loaded into its own collectible <c>AssemblyLoadContext</c>, so the test host
/// and the plugin get separate copies of every static. An environment variable is process-wide and
/// is the one channel both sides genuinely share.
/// </para>
/// </summary>
public enum FailurePoint
{
    None = 0,
    BindExports = 1,
    HostedServiceStart = 2,
    PluginStart = 3,
    Migration = 4,
}
