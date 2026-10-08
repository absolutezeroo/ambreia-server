using System.Threading;
using System.Threading.Tasks;

namespace Vortex.WebApi.Services;

public interface IWebApiPlayerService
{
    /// <summary>The account's oldest live character, or null when it has none.</summary>
    Task<int?> FirstPlayerIdAsync(int accountId, CancellationToken ct);
}
