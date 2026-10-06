using Vortex.Primitives.Networking.Revisions;
using Vortex.Revisions.Revision20260701.Parsers.Sound;

namespace Vortex.Revisions.Revision20260701.Maps;

internal sealed class SoundMap : IRevisionMap
{
    public void RegisterInto(IRevisionMapBuilder builder)
    {
        builder.MapParser(MessageEvent.GetSoundSettingsEvent, new GetSoundSettingsMessageParser());
    }
}
