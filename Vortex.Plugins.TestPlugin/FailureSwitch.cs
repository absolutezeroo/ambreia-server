using System;

namespace Vortex.Plugins.TestPlugin;

/// <summary>Reads and writes the selected <see cref="FailurePoint" />.</summary>
public static class FailureSwitch
{
    public const string ENVIRONMENT_VARIABLE = "VORTEX_TEST_PLUGIN_FAILURE";

    /// <summary>Records what the plugin actually did, so the test can assert on it.</summary>
    public const string TRACE_VARIABLE = "VORTEX_TEST_PLUGIN_TRACE";

    public static FailurePoint Current =>
        Enum.TryParse(
            Environment.GetEnvironmentVariable(ENVIRONMENT_VARIABLE),
            ignoreCase: true,
            out FailurePoint point
        )
            ? point
            : FailurePoint.None;

    /// <summary>Whether the plugin should borrow a host service this run.</summary>
    /// <remarks>
    /// Its own variable rather than another <see cref="FailurePoint"/>: borrowing is not a failure
    /// mode, and the two are combined on purpose — a plugin that borrows a service the host does
    /// not have must be refused at activation, which is a borrowing question answered through the
    /// failure machinery.
    /// </remarks>
    public const string BORROW_VARIABLE = "VORTEX_TEST_PLUGIN_BORROWS";

    public static bool BorrowsHostService =>
        string.Equals(
            Environment.GetEnvironmentVariable(BORROW_VARIABLE),
            "1",
            StringComparison.Ordinal
        );

    /// <summary>Appends a step name to the shared trace.</summary>
    public static void Trace(string step)
    {
        string existing = Environment.GetEnvironmentVariable(TRACE_VARIABLE) ?? string.Empty;

        Environment.SetEnvironmentVariable(
            TRACE_VARIABLE,
            existing.Length == 0 ? step : existing + "," + step
        );
    }
}
