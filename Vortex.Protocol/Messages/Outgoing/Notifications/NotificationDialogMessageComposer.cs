using System.Collections.Immutable;
using Orleans;
using Vortex.Primitives.Networking;

namespace Vortex.Protocol.Messages.Outgoing.Notifications;

/// <summary>
/// A generic server-driven dialog (header 2243): a type the client resolves to a layout, plus the
/// parameters that fill it.
///
/// Shape from WIN63's parser (unknowns/_SafePkg_1810/_SafeCls_2688.as): a string, a count, then
/// that many key/value string pairs.
/// </summary>
[GenerateSerializer, Immutable]
public sealed record NotificationDialogMessageComposer : IComposer
{
    [Id(0)]
    public required string Type { get; init; }

    [Id(1)]
    public required ImmutableArray<NotificationDialogParameter> Parameters { get; init; }
}
