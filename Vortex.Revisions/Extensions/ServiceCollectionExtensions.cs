using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vortex.Primitives.Networking.Revisions;
using Vortex.Revisions.Configuration;
using RevisionType = Vortex.Revisions.Revision20260701.Revision20260701;

namespace Vortex.Revisions.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVortexRevisions(
        this IServiceCollection services,
        HostApplicationBuilder builder
    )
    {
        services.Configure<RevisionConfig>(
            builder.Configuration.GetSection(RevisionConfig.SECTION_NAME)
        );

        // Registered by contract (not the concrete type) so new revisions are picked up by adding
        // an IRevision implementation, without VortexEmulator ever knowing a concrete type exists.
        services.AddSingleton<IRevision, RevisionType>();
        services.AddHostedService<RevisionRegistrationService>();

        return services;
    }
}
