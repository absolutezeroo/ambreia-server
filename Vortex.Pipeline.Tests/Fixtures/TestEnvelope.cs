namespace Vortex.Pipeline.Tests.Fixtures;

/// <summary>
/// A small, self-contained envelope for exercising EnvelopeHost directly -- deliberately NOT any
/// real Vortex message or event type, per the TEST-01 brief.
/// </summary>
public class TestEnvelope
{
    public string Payload { get; init; } = string.Empty;
}
