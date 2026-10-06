using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Game.Lobby;
using Vortex.Revisions.Revision20260701.Parsers.Game.Lobby;
using Vortex.Revisions.Revision20260701.Serializers.Game.Lobby;

namespace Vortex.Revisions.Revision20260701.Maps;

internal sealed class GameMap : IRevisionMap
{
    public void RegisterInto(IRevisionMapBuilder builder)
    {
        // Game Lobby
        builder.MapParser(
            MessageEvent.GetResolutionAchievementsMessageEvent,
            new GetResolutionAchievementsMessageParser()
        );
        builder.MapParser(
            MessageEvent.ResetResolutionAchievementMessageEvent,
            new ResetResolutionAchievementMessageParser()
        );

        builder.MapSerializer(
            typeof(AchievementResolutionsMessageComposer),
            new AchievementResolutionsMessageComposerSerializer(
                MessageComposer.AchievementResolutionsMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(AchievementResolutionProgressMessageComposer),
            new AchievementResolutionProgressMessageComposerSerializer(
                MessageComposer.AchievementResolutionProgressMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(AchievementResolutionCompletedMessageComposer),
            new AchievementResolutionCompletedMessageComposerSerializer(
                MessageComposer.AchievementResolutionCompletedMessageComposer
            )
        );
    }
}
