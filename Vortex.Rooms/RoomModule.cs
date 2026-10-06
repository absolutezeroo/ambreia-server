using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vortex.Primitives.Hosting;
using Vortex.Primitives.Moderation;
using Vortex.Primitives.Plugins;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Providers;
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
    }
}
