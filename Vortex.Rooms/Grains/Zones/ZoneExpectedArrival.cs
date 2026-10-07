namespace Vortex.Rooms.Grains.Zones;

/// <summary>Where an expected player lands: a named entry, or a bare cell (X negative = none).</summary>
public readonly record struct ZoneExpectedArrival(string? EntryId, int X, int Y, long ExpiresAtMs);
