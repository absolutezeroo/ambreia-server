using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.NewNavigator;
using Vortex.Revisions.Revision20260701.Parsers.NewNavigator;
using Vortex.Revisions.Revision20260701.Serializers.NewNavigator;

namespace Vortex.Revisions.Revision20260701.Maps;

internal sealed class NewNavigatorMap : IRevisionMap
{
    public void RegisterInto(IRevisionMapBuilder builder)
    {
        builder.MapParser(MessageEvent.NewNavigatorInitEvent, new NewNavigatorInitMessageParser());
        builder.MapParser(
            MessageEvent.NewNavigatorSearchEvent,
            new NewNavigatorSearchMessageParser()
        );

        builder.MapSerializer(
            typeof(NavigatorCollapsedCategoriesMessage),
            new NavigatorCollapsedCategoriesMessageSerializer(
                MessageComposer.NavigatorCollapsedCategoriesMessageComposer
            )
        );
        builder.MapSerializer(
            typeof(NavigatorLiftedRoomsMessage),
            new NavigatorLiftedRoomsMessageSerializer(MessageComposer.NavigatorLiftedRoomsComposer)
        );
        builder.MapSerializer(
            typeof(NavigatorMetaDataMessage),
            new NavigatorMetaDataMessageSerializer(MessageComposer.NavigatorMetaDataComposer)
        );
        builder.MapSerializer(
            typeof(NavigatorSavedSearchesMessage),
            new NavigatorSavedSearchesMessageSerializer(
                MessageComposer.NavigatorSavedSearchesComposer
            )
        );
        builder.MapSerializer(
            typeof(NavigatorSearchResultBlocksMessageComposer),
            new NavigatorSearchResultBlocksMessageSerializer(
                MessageComposer.NavigatorSearchResultBlocksComposer
            )
        );
        builder.MapSerializer(
            typeof(NewNavigatorPreferencesMessageComposer),
            new NewNavigatorPreferencesMessageSerializer(
                MessageComposer.NewNavigatorPreferencesComposer
            )
        );
    }
}
