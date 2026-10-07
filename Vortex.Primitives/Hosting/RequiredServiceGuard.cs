using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

/// <summary>
/// Central decision + reporting point for optional-vs-required auxiliary web services (the client
/// web API).
/// <para>
/// These run as background services inside the Orleans host. Historically a build or start failure
/// was logged and then swallowed, so the emulator carried on announcing itself as started while a
/// service an operator had deliberately enabled was simply absent — silently, until someone noticed
/// the port was dead. This makes the outcome explicit: a required service that fails takes the host
/// down with exit code 1, and an optional one that fails is recorded so the runtime can report
/// itself as degraded rather than healthy.
/// </para>
/// </summary>
public sealed class RequiredServiceGuard(
    IHostApplicationLifetime lifetime,
    ILogger<RequiredServiceGuard> logger
)
{
    private readonly HashSet<string> _degraded = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _sync = new();

    /// <summary>
    /// Services that were enabled, are not required, and failed to start. Non-empty means the
    /// emulator is running degraded and must not be described as fully operational.
    /// </summary>
    public IReadOnlyCollection<string> DegradedServices
    {
        get
        {
            lock (_sync)
            {
                return _degraded.ToArray();
            }
        }
    }

    /// <summary>True when at least one enabled optional service failed to start.</summary>
    public bool IsDegraded
    {
        get
        {
            lock (_sync)
            {
                return _degraded.Count > 0;
            }
        }
    }

    /// <summary>Pure policy: what should happen for this (enabled, required) combination.</summary>
    public static ServiceFailureAction DecideAction(bool enabled, bool required) =>
        !enabled ? ServiceFailureAction.Ignore
        : required ? ServiceFailureAction.FailHost
        : ServiceFailureAction.Degrade;

    /// <summary>
    /// Reports that <paramref name="serviceName"/> failed to build or start, and applies the policy
    /// for its (enabled, required) configuration. Returns the action that was taken.
    /// </summary>
    public ServiceFailureAction ReportStartupFailure(
        string serviceName,
        bool enabled,
        bool required,
        Exception error,
        string requiredOptionPath
    )
    {
        ServiceFailureAction action = DecideAction(enabled, required);

        switch (action)
        {
            case ServiceFailureAction.Ignore:
                return action;

            case ServiceFailureAction.Degrade:
                lock (_sync)
                {
                    _degraded.Add(serviceName);
                }

                logger.LogError(
                    error,
                    "{Service} is enabled but failed to start. The emulator continues in a DEGRADED "
                        + "state without it. Set '{RequiredOption}' to true to make this failure stop "
                        + "the host instead.",
                    serviceName,
                    requiredOptionPath
                );

                return action;

            case ServiceFailureAction.FailHost:
                logger.LogCritical(
                    error,
                    "{Service} is enabled and marked required via '{RequiredOption}', but failed to "
                        + "start. Shutting the emulator down with a non-zero exit code rather than "
                        + "running without a service that was declared mandatory.",
                    serviceName,
                    requiredOptionPath
                );

                // Non-zero exit code so a supervisor (systemd/k8s/container runtime) sees a failure
                // and restarts/alerts, instead of reading this as a clean shutdown. Program.cs uses
                // the same convention for a fatal host exception.
                Environment.ExitCode = 1;
                lifetime.StopApplication();

                return action;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(required),
                    action,
                    "Unhandled action."
                );
        }
    }

    /// <summary>Clears the degraded flag for a service that later started successfully.</summary>
    public void ReportStarted(string serviceName)
    {
        lock (_sync)
        {
            _degraded.Remove(serviceName);
        }
    }
}
