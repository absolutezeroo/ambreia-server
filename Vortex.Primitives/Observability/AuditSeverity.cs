namespace Vortex.Primitives.Observability;

/// <summary>Severity of an audit record, used for incident triage and retention policy.</summary>
public enum AuditSeverity
{
    Info,
    Notice,
    Warning,
    Critical,
}
