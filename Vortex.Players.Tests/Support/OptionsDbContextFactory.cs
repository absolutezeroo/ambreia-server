using Microsoft.EntityFrameworkCore;
using Vortex.Database.Context;

namespace Vortex.Players.Tests.Support;

/// <summary>A context factory over fixed options, for services that create their own contexts.</summary>
internal sealed class OptionsDbContextFactory(DbContextOptions<VortexDbContext> options)
    : IDbContextFactory<VortexDbContext>
{
    public VortexDbContext CreateDbContext() => new(options);
}
