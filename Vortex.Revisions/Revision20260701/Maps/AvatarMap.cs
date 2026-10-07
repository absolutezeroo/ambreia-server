using Vortex.Primitives.Networking.Revisions;
using Vortex.Protocol.Messages.Outgoing.Avatar;
using Vortex.Revisions.Revision20260701.Serializers.Avatar;

namespace Vortex.Revisions.Revision20260701.Maps;

internal sealed class AvatarMap : IRevisionMap
{
    public void RegisterInto(IRevisionMapBuilder builder)
    {
        // Same parser, second id: the onboarding dialog claims names on 879 (see Headers.cs).

        builder.MapSerializer(
            typeof(FigureUpdateEventMessageComposer),
            new FigureUpdateEventMessageComposerSerializer(MessageComposer.FigureUpdateComposer)
        );
    }
}
