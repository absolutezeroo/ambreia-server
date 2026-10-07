using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Pets.Snapshots;

namespace Vortex.Primitives.Pets.Providers;

public interface IPetSkinProvider
{
    IReadOnlyList<PetSkinEntry> GetSkinsForType(int petType);

    Task ReloadAsync(CancellationToken ct);
}
