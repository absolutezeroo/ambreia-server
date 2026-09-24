using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.PacketHandlers.Catalog;
using Vortex.PacketHandlers.Users;
using Vortex.Primitives.Catalog.Snapshots;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Protocol.Messages.Incoming.Catalog;
using Vortex.Protocol.Messages.Incoming.Users;
using Vortex.Protocol.Messages.Outgoing.Catalog;
using Vortex.Protocol.Messages.Outgoing.Users;
using Xunit;

namespace Vortex.PacketHandlers.Tests;

/// <summary>The hotel-wide word filter on what a purchase carries: a pet's name, a gift's card.</summary>
public sealed class WordFilterCatalogHandlerTests : WordFilterHandlerFixture
{
    /// <summary>4 is the client's own "forbidden words" answer; anything above it buys anyway.</summary>
    [Fact]
    public async Task AFilteredPetName_IsAnsweredWithTheClientsBobbaCode()
    {
        await new ApproveNameMessageHandler(Replacing("bobba")).HandleAsync(
            new ApproveNameMessage { Name = "Connard" },
            Context(),
            CancellationToken.None
        );

        Sent.Should().ContainSingle().Which.As<ApproveNameMessageComposer>().Result.Should().Be(4);
        FilterCalls.Should().OnlyContain(c => c.Method.Name == nameof(IWordFilter.Test));
    }

    [Fact]
    public async Task ACleanPetName_IsApproved()
    {
        await new ApproveNameMessageHandler(PassingEverything()).HandleAsync(
            new ApproveNameMessage { Name = "Rex" },
            Context(),
            CancellationToken.None
        );

        Sent.Should().ContainSingle().Which.As<ApproveNameMessageComposer>().Result.Should().Be(0);
    }

    [Fact]
    public async Task ARefusedGiftCard_IsNeverBought()
    {
        await new PurchaseFromCatalogAsGiftMessageHandler(
            Receiver(),
            RefusingOnly("con")
        ).HandleAsync(Gift(message: "con", extra: ""), Context(), CancellationToken.None);

        GrainCalls.Should().NotContain(c => c.Method.Name == "PurchaseOfferAsGiftAsync");
        Sent.Should().ContainSingle(s => s is PurchaseErrorMessageComposer);
    }

    /// <summary>The name line only: the race and colour lines after it are the pet's, untouched.</summary>
    [Fact]
    public async Task AGiftedPetsName_IsFilteredAsAName_AndItsRaceAndColourKept()
    {
        await new PurchaseFromCatalogAsGiftMessageHandler(
            Receiver(),
            Filter(text => new WordFilterResult(text, false, false))
        ).HandleAsync(
            Gift(message: "joyeux anniversaire", extra: "Rex\n3\nFFFFFF"),
            Context(),
            CancellationToken.None
        );

        object?[] args = GrainCall("PurchaseOfferAsGiftAsync");
        args[2].Should().Be("Rex\n3\nFFFFFF");
        ((GiftWrappingSpec)args[4]!).Message.Should().Be("joyeux anniversaire");
        AppliedSources.Should().Equal(WordFilterSource.Gift, WordFilterSource.PetName);
    }

    /// <summary>A guild furni's group id is digits, and "455" folds to "ass".</summary>
    [Fact]
    public async Task ADigitsOnlyExtra_IsNotFiltered()
    {
        await new PurchaseFromCatalogAsGiftMessageHandler(
            Receiver(),
            RefusingOnly("455")
        ).HandleAsync(Gift(message: "", extra: "455"), Context(), CancellationToken.None);

        AppliedSources.Should().Equal(WordFilterSource.Gift);
        GrainCall("PurchaseOfferAsGiftAsync")[2].Should().Be("455");
    }

    private static PurchaseFromCatalogAsGiftMessage Gift(string message, string extra) =>
        new()
        {
            OfferCode = 11,
            RecieverName = "Friend",
            Message = message,
            ExtraParam = extra,
        };

    // The directory finds the receiver; everything else answers its type's default.
    private Orleans.IGrainFactory Receiver() =>
        Grains(call =>
            call.Method.Name == "GetPlayerIdAsync"
                ? Task.FromResult<Primitives.Players.PlayerId?>(
                    Primitives.Players.PlayerId.Parse(7)
                )
                : null
        );
}
