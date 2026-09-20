using System.Collections.Immutable;

namespace Vortex.Primitives.Rooms.Enums;

/// <summary>
/// How an avatar was holding itself, as a signal carries it and content stores it.
/// </summary>
/// <remarks>
/// <para>
/// Its own vocabulary rather than <see cref="RoomAvatarStatusTypeExtensions.ToLegacyString"/>:
/// those are the client's wire abbreviations (<c>sit</c>, <c>lay</c>, <c>mv</c>) and belong to the
/// protocol. A task stored on <c>sit</c> would be a task stored on a protocol detail, and the
/// protocol is the one thing here that is allowed to change.
/// </para>
/// <para>
/// Three values and not two. <see cref="AvatarPostureType"/> mirrors the client's posture message,
/// which only asks to sit or stand; laying down is something furniture does to you, and it is a
/// posture all the same.
/// </para>
/// </remarks>
public static class AvatarPostures
{
    public const string Standing = "standing";
    public const string Sitting = "sitting";
    public const string Laying = "laying";

    public static readonly ImmutableArray<string> All = [Standing, Sitting, Laying];

    /// <summary>What the avatar's statuses say it is doing. Standing is the absence of the others.</summary>
    public static string Of(bool sitting, bool laying) =>
        laying ? Laying
        : sitting ? Sitting
        : Standing;
}
