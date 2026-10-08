using Microsoft.Extensions.Configuration;
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

        RevisionConfig revisions =
            builder.Configuration.GetSection(RevisionConfig.SECTION_NAME).Get<RevisionConfig>()
            ?? new RevisionConfig();

        // Registered by contract (not the concrete type), and one revision only, so it is also the
        // default every new session starts on. A production server never speaks AMBREIA-DEV: its ids
        // are in this repository.
        if (revisions.Release ?? !builder.Environment.IsDevelopment())
        {
            services.AddSingleton<IRevision>(_ => ReleaseRevision.LoadLatest());
        }
        else
        {
            services.AddSingleton<IRevision, RevisionType>();
        }

        services.AddHostedService<RevisionRegistrationService>();

        return services;
    }
}
