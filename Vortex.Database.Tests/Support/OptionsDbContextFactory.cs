using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;

namespace Vortex.Database.Tests.Support;

/// <summary>A context factory over fixed options, for services that take a factory.</summary>
internal sealed class OptionsDbContextFactory(DbContextOptions<VortexDbContext> options)
    : IDbContextFactory<VortexDbContext>
{
    public VortexDbContext CreateDbContext() => new(options);

    public Task<VortexDbContext> CreateDbContextAsync(CancellationToken ct = default) =>
        Task.FromResult(CreateDbContext());
}
