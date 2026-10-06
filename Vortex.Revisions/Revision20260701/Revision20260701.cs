using Microsoft.Extensions.Options;
using Vortex.Primitives.Networking.Revisions;
using Vortex.Revisions.Configuration;
using Vortex.Revisions.Revision20260701.Maps;

namespace Vortex.Revisions.Revision20260701;

public sealed class Revision20260701(IOptions<ProtocolLimitsConfig> protocolLimits)
    : RevisionBase(
        new IRevisionMap[]
        {
            new AdvertisementMap(),
            new AvatarMap(),
            new CampaignMap(),
            new CatalogMap(),
            new CollectiblesMap(),
            new CompetitionMap(),
            new CraftingMap(),
            new FriendFurniMap(),
            new FriendListMap(protocolLimits.Value),
            new GameMap(),
            new GiftsMap(),
            new GroupForumsMap(),
            new HandshakeMap(),
            new HelpMap(protocolLimits.Value),
            new HotlooksMap(),
            new InventoryMap(protocolLimits.Value),
            new LandingViewMap(),
            new MarketplaceMap(protocolLimits.Value),
            new ModeratorMap(),
            new NavigatorMap(),
            new NewNavigatorMap(),
            new NftMap(),
            new NotificationsMap(),
            new NuxMap(),
            new PollMap(),
            new PreferencesMap(),
            new QuestMap(),
            new RegisterMap(),
            new RoomMap(),
            new RoomDirectoryMap(),
            new RoomSettingsMap(protocolLimits.Value),
            new RaidProtectionMap(),
            new SoundMap(),
            new TalentMap(),
            new TrackingMap(),
            new UserClassificationMap(),
            new UsersMap(),
            new VaultMap(),
            new AvailabilityMap(),
            new CallForHelpMap(),
            new PerkMap(),
            // Vortex-specific: the fishing system. Ids come from the 8000-8999 band, which appears in
            // no client registry — see Headers.cs and the client's docs/vortex-original/fishing.md.
            new FishingMap(),
        }
    )
{
    public override string Revision => "WIN63-202607011411-782849652";
}
