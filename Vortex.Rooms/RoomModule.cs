using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vortex.Database.Context;
using Vortex.Primitives.Camera;
using Vortex.Primitives.Hosting;
using Vortex.Primitives.Moderation;
using Vortex.Primitives.Plugins;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Providers;
using Vortex.Rooms.Camera;
using Vortex.Rooms.Camera.Render;
using Vortex.Rooms.Configuration;
using Vortex.Rooms.Events;
using Vortex.Rooms.Games;
using Vortex.Rooms.Object.Logic;
using Vortex.Rooms.Providers;
using Vortex.Runtime.AssemblyProcessing;

namespace Vortex.Rooms;

public sealed class RoomModule : IHostPluginModule
{
    public string Key => "turbo-rooms";

    public void ConfigureServices(IServiceCollection services, HostApplicationBuilder builder)
    {
        services.Configure<RoomConfig>(builder.Configuration.GetSection(RoomConfig.SECTION_NAME));

        services.AddSingleton<IRoomAvatarProvider, RoomAvatarProvider>();
        services.AddSingleton<IRoomItemsProvider, RoomItemsProvider>();
        services.AddSingleton<IRoomModelProvider, RoomModelProvider>();
        services.AddSingleton<IReferenceDataProvider>(sp =>
            (IReferenceDataProvider)sp.GetRequiredService<IRoomModelProvider>()
        );
        services.AddSingleton<IRoomObjectLogicProvider, RoomObjectLogicProvider>();
        services.AddSingleton<IRoomEventListenerProvider, RoomEventListenerProvider>();
        services.AddSingleton<IRoomGameProvider, RoomGameProvider>();

        services.AddSingleton<IAssemblyFeatureProcessor, RoomObjectLogicFeatureProcessor>();
        services.AddSingleton<IAssemblyFeatureProcessor, RoomEventListenerFeatureProcessor>();
        services.AddSingleton<IAssemblyFeatureProcessor, RoomGameFeatureProcessor>();

        services.AddSingleton<IRoomService, RoomService>();
        services.AddSingleton<IRoomModerationStore, RoomModerationStore>();
        services.AddSingleton<IModeratorChatlogService, ModeratorChatlogService>();
        services.AddSingleton<IModeratorRoomVisitService, ModeratorRoomVisitService>();
        services.AddSingleton<ICfhTicketService, CfhTicketService>();
        services.AddSingleton<ICameraPhotoStore, CameraPhotoStore>();

        services.AddSingleton<ICameraSpriteSource>(CameraSprites);
        services.AddSingleton<IRoomPhotoRenderer, RoomPhotoRenderer>();
        services.AddSingleton<IRoomAdvertisementService, RoomAdvertisementService>();
    }

    /// <summary>
    /// The camera's sprite source: an operator's override directory, the hotel's own <c>.nitro</c>
    /// pack, and the allowlisted urls a payload may name.
    /// </summary>
    /// <remarks>
    /// One <see cref="HttpClient"/> for all of it. The resolver owns it and the library borrows it —
    /// two would be two connection pools against the same asset host, and two things to dispose.
    /// </remarks>
    private static CameraSpriteResolver CameraSprites(IServiceProvider provider)
    {
        HttpClient http = CameraSpriteResolver.CreateHttpClient();

        return new CameraSpriteResolver(
            provider.GetRequiredService<IOptions<RoomConfig>>().Value.CameraSpriteRoot,
            http,
            new NitroSpriteLibrary(
                async ct => await ClassNamesAsync(provider, ct).ConfigureAwait(false),
                http,
                provider.GetRequiredService<ILogger<NitroSpriteLibrary>>()
            ),
            provider.GetRequiredService<ILogger<CameraSpriteResolver>>()
        );
    }

    /// <summary>
    /// Every furniture classname this hotel sells, read once and held by the library.
    /// </summary>
    /// <remarks>
    /// This is what tells a sprite name which bundle it belongs to, and it is also the guard that
    /// keeps a scene from becoming a request generator: a name matching nothing here never leaves the
    /// process.
    /// </remarks>
    private static async Task<IReadOnlyCollection<string>> ClassNamesAsync(
        IServiceProvider provider,
        CancellationToken ct
    )
    {
        await using VortexDbContext db = await provider
            .GetRequiredService<IDbContextFactory<VortexDbContext>>()
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        return await db
            .FurnitureDefinitions.AsNoTracking()
            .Where(definition => definition.DeletedAt == null)
            .Select(definition => definition.Name)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }
}
