using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Incoming.Help;

/// <summary>
/// The appeal button in the "my reports" window. The client sends the report's own id and nothing
/// else (<c>MyReportStatus.as:250</c>, <c>new _SafeCls_3470(_shownObject.message.id)</c>), then
/// disables the button locally and waits for the refreshed list.
/// </summary>
public record AppealCfhMessage : IMessageEvent
{
    public required int IssueId { get; init; }
}
