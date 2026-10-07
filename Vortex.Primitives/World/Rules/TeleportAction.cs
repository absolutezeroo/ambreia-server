namespace Vortex.Primitives.World.Rules;

public sealed record TeleportAction(string Zone, string Entry) : ZoneAction;
