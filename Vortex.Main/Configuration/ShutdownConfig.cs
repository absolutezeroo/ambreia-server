namespace Vortex.Main.Configuration;

/// <summary>
/// How much warning the hotel gives its players before it goes down, and what it tells them the
/// downtime will be.
///
/// <para>
/// <b>Off by default, and that is not timidity.</b> The notice is a delay the process takes inside
/// its own shutdown, so it only works when whatever stopped the container waits at least that long
/// before the kill: Coolify stops a container with <c>docker stop --time=30</c>, Docker Compose uses
/// the service's <c>stop_grace_period</c> (10s unless it says otherwise), and either one expiring
/// mid-notice is a SIGKILL — no room persistence flush, no Orleans drain, on every single deploy.
/// Raise the grace period first, then set this.
/// </para>
/// </summary>
public sealed class ShutdownConfig
{
    public const string SECTION_NAME = "Vortex:Shutdown";

    /// <summary>
    /// Minutes between the warning and the shutdown. 0 disables the notice entirely, which is the
    /// default and the behaviour every version before this one had.
    /// </summary>
    public int NoticeMinutes { get; set; }

    /// <summary>
    /// How long the hotel is expected to be gone, in minutes — the <c>%d%</c> in the client's
    /// "we'll be right back in approximately %d% minutes".
    ///
    /// <para>
    /// This is the container swap, not the build: an image is built before anything is stopped, so
    /// the minutes spent compiling are minutes the hotel is still serving. What a player actually
    /// waits through is the stop plus the start, and the image's own healthcheck allows 90 seconds
    /// for the latter.
    /// </para>
    /// </summary>
    public int DowntimeMinutes { get; set; } = 2;
}
