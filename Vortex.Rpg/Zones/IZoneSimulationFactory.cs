using Vortex.Primitives.World;

namespace Vortex.Rpg.Zones;

/// <summary>Creates a zone's simulation (spec M1a §4.1), declared by DI by the module that runs
/// combat and monsters; a zone never names it.</summary>
public interface IZoneSimulationFactory
{
    public IZoneSimulation Create(IZoneRuntime runtime, ZoneDocument zone);
}
