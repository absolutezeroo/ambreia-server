using System.Collections.Generic;
using Vortex.Primitives.Observability;

namespace Vortex.WebApi.Tests;

/// <summary>Every audit record the routes emitted, in order.</summary>
internal sealed class FakeAuditSink : IAuditSink
{
    public List<AuditEvent> Events { get; } = [];

    public void Emit(in AuditEvent auditEvent) => Events.Add(auditEvent);
}
