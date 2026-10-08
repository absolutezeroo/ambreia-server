using Orleans;

namespace Vortex.Protocol.Messages.Outgoing.Notifications;

/// <summary>One key/value pair filling a notification dialog.</summary>
[GenerateSerializer, Immutable]
public sealed record NotificationDialogParameter
{
    [Id(0)]
    public required string Key { get; init; }

    [Id(1)]
    public required string Value { get; init; }
}
