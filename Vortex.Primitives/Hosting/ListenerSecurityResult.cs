namespace Vortex.Primitives.Hosting;

/// <summary>
/// Outcome of a listener-security check. <see cref="IsAllowed"/> false means the service must refuse
/// to start; <see cref="Message"/> is then the operator-facing explanation (never null when refused).
/// A allowed result may still carry a <see cref="Message"/> — that is a warning to log, not a refusal.
/// </summary>
public readonly record struct ListenerSecurityResult(bool IsAllowed, string? Message)
{
    public static ListenerSecurityResult Allowed() => new(true, null);

    public static ListenerSecurityResult AllowedWithWarning(string message) => new(true, message);

    public static ListenerSecurityResult Refused(string message) => new(false, message);
}
