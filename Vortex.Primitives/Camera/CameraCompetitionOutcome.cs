using Orleans;

namespace Vortex.Primitives.Camera;

/// <summary>
/// What came of a <c>PhotoCompetition</c> submission.
/// </summary>
[GenerateSerializer, Immutable]
public sealed record CameraCompetitionOutcome
{
    /// <summary>
    /// The one reason code the client recognises by name, and has its own wording for. Anything
    /// else it treats as a generic failure, so inventing new codes only makes the screen less
    /// helpful.
    /// </summary>
    public const string TooManySubmits = "too-many-submits";

    [Id(0)]
    public required bool Ok { get; init; }

    [Id(1)]
    public required string ErrorReason { get; init; }

    public static CameraCompetitionOutcome Submitted() =>
        new() { Ok = true, ErrorReason = string.Empty };

    public static CameraCompetitionOutcome Refused(string reason) =>
        new() { Ok = false, ErrorReason = reason };
}
