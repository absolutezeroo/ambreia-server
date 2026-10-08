namespace Vortex.Primitives.Hosting;

/// <summary>
/// What a web host should do when it fails to build or start.
/// </summary>
public enum ServiceFailureAction
{
    /// <summary>The component is switched off; a failure is not possible and not reported.</summary>
    Ignore,

    /// <summary>Enabled but optional: log the failure and keep running in a degraded state.</summary>
    Degrade,

    /// <summary>Enabled and required: the whole host must come down with a non-zero exit code.</summary>
    FailHost,
}
