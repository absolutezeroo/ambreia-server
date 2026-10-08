using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Rpg.Characters;
using Vortex.Revisions.Revision20260701.Parsers.Rpg.Characters;
using Vortex.Revisions.Revision20260701.Serializers.Rpg.Characters;

namespace Vortex.Revisions.Revision20260701.Maps;

/// <summary>The character screen and the character state (spec F3a §6).</summary>
internal sealed class RpgCharactersMap : IRevisionMap
{
    public void RegisterInto(IRevisionMapBuilder builder)
    {
        builder.MapParser(
            MessageEvent.RpgCharacterListRequestMessageEvent,
            new RpgCharacterListRequestMessageParser()
        );
        builder.MapParser(
            MessageEvent.RpgCharacterCreateMessageEvent,
            new RpgCharacterCreateMessageParser()
        );
        builder.MapParser(
            MessageEvent.RpgCharacterChooseClassMessageEvent,
            new RpgCharacterChooseClassMessageParser()
        );
        builder.MapParser(MessageEvent.RpgEnterWorldMessageEvent, new RpgEnterWorldMessageParser());

        builder.MapSerializer(
            typeof(RpgCharacterListMessageComposer),
            new RpgCharacterListMessageComposerSerializer(
                MessageComposer.RpgCharacterListMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(RpgClassListMessageComposer),
            new RpgClassListMessageComposerSerializer(MessageComposer.RpgClassListMessageComposer)
        );
        builder.MapSerializer(
            typeof(RpgCharacterResultMessageComposer),
            new RpgCharacterResultMessageComposerSerializer(
                MessageComposer.RpgCharacterResultMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(RpgCharacterStateMessageComposer),
            new RpgCharacterStateMessageComposerSerializer(
                MessageComposer.RpgCharacterStateMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(RpgCharacterProgressMessageComposer),
            new RpgCharacterProgressMessageComposerSerializer(
                MessageComposer.RpgCharacterProgressMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(RpgCharacterStatsMessageComposer),
            new RpgCharacterStatsMessageComposerSerializer(
                MessageComposer.RpgCharacterStatsMessageComposer
            )
        );
    }
}
