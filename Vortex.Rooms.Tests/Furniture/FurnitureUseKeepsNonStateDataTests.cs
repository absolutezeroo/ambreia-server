using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Furniture;
using Vortex.Furniture.Providers;
using Vortex.Primitives.Action;
using Vortex.Primitives.Furniture;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.Snapshots;
using Vortex.Primitives.Furniture.StuffData;
using Vortex.Primitives.Rooms.Enums;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Rooms.Object.Logic.Furniture.Floor;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Furniture;

/// <summary>
/// Clicking a furni whose stuff data is not a state.
/// </summary>
/// <remarks>
/// <para>
/// A definition declaring <c>TotalStates &gt; 0</c> is not a promise that the item holds a state.
/// <c>furniture_external_image_wallitem</c> and <c>furniture_stickie</c> are bound to no logic of
/// their own, so a photo and a note arrive on the generic toggle path carrying a payload — and the
/// generic path used to <c>int.Parse</c> it. The string below is the one that did it in production:
/// a <see cref="System.FormatException"/> thrown out of the room grain's turn, taking
/// <c>UseItemByIdAsync</c> down with it.
/// </para>
/// <para>
/// Not throwing is only half of it, and the smaller half. <c>SetStateAsync</c> writes the new state
/// <i>over</i> the legacy string, so a fix that merely read the photo's json as zero would have let
/// the click through — and the wall item would have become the string "1", persisted and broadcast,
/// with the photo gone for good. The throw was the only thing protecting the data. Hence the second
/// test, and the third: a guard wide enough to stop a photo toggling would also stop every ordinary
/// multistate furni in the hotel, and nothing else in the suite would have noticed.
/// </para>
/// <para>
/// Driven through the floor logic although a photo is a wall item: <c>OnUseAsync</c> lives on the
/// shared <c>FurnitureLogic</c> base that both families derive from, and the floor context is the
/// one this suite already knows how to stub.
/// </para>
/// </remarks>
public sealed class FurnitureUseKeepsNonStateDataTests
{
    private const string PhotoData =
        """{"w":"photos/A759D3C69E4E313ED87AD870FF443E2C.png","n":"1","t":"1789799311927"}""";

    [Fact]
    public async Task UsingAFurniWhoseDataIsNotAState_LeavesTheDataAloneAsync()
    {
        FurnitureFloorLogic logic = LogicHolding(PhotoData);

        await logic.OnUseAsync(ActionContext.System, 0, CancellationToken.None);

        logic.GetLegacyString().Should().Be(PhotoData);
    }

    /// <summary>The sticky note, the same shape with a different payload.</summary>
    [Fact]
    public async Task UsingAFurniHoldingText_LeavesTheTextAloneAsync()
    {
        const string note = "FFFF33 hello there";

        FurnitureFloorLogic logic = LogicHolding(note);

        await logic.OnUseAsync(ActionContext.System, 0, CancellationToken.None);

        logic.GetLegacyString().Should().Be(note);
    }

    /// <summary>
    /// The other side of the guard. Without this, narrowing "can this toggle" to nothing at all would
    /// pass both tests above and break every multistate furni in the hotel.
    /// </summary>
    [Theory]
    [InlineData("0", "1")]
    [InlineData("8", "0")]
    // Never used: the default on the base class is the empty string, which is a state of zero and
    // has to advance like one. It was an int.Parse("") crash of its own.
    [InlineData("", "1")]
    public async Task UsingAnOrdinaryMultistateFurni_StillAdvancesItAsync(
        string before,
        string after
    )
    {
        FurnitureFloorLogic logic = LogicHolding(before);

        await logic.OnUseAsync(ActionContext.System, 0, CancellationToken.None);

        logic.GetLegacyString().Should().Be(after);
    }

    /// <summary>
    /// The read on its own, for every caller that is not a click: a payload reads as no state rather
    /// than as an exception.
    /// </summary>
    [Theory]
    [InlineData(PhotoData)]
    [InlineData("FFFF33 hello there")]
    [InlineData("")]
    [InlineData("not a number")]
    public void ReadingTheStateOfAPayload_AnswersZeroRatherThanThrowing(string data)
    {
        IStuffData stuffData = new StuffDataFactory().CreateStuffDataFromJson(
            StuffDataType.LegacyKey,
            Blob(data)
        );

        stuffData.GetState().Should().Be(0);
    }

    private static FurnitureFloorLogic LogicHolding(string data) =>
        new(new StuffDataFactory(), StubContext(data));

    private static string Blob(string data)
    {
        ExtraData extraData = new(null);

        extraData.UpdateSection(ExtraDataSectionType.STUFF, new { Data = data });

        return extraData.GetJsonString();
    }

    private static IRoomFloorItemContext StubContext(string data)
    {
        FurnitureDefinitionSnapshot definition = new()
        {
            Id = 4066,
            SpriteId = 4066,
            Name = "external_image_wallitem_photo",
            ProductType = ProductType.Floor,
            FurniCategory = FurnitureCategory.Default,
            LogicName = "furniture_external_image_wallitem",
            // The number that made this reachable: the definition says it has states, so the usage
            // policy lets a click through to the toggle path.
            TotalStates = 9,
            Width = 1,
            Length = 1,
            StackHeight = default,
            CanStack = false,
            CanWalk = false,
            CanSit = false,
            CanLay = false,
            CanRecycle = false,
            CanTrade = true,
            CanGroup = false,
            CanSell = true,
            UsagePolicy = FurnitureUsageType.Everybody,
            ExtraData = null,
            StuffDataType = StuffDataType.LegacyKey,
        };

        IExtraData extraData = new ExtraData(Blob(data));

        IRoomFloorItem item = FakeProxy.Create<IRoomFloorItem>(call =>
            call.Method.Name switch
            {
                "get_ExtraData" => extraData,
                "get_Definition" => definition,
                _ => null,
            }
        );

        return FakeProxy.Create<IRoomFloorItemContext>(call =>
            call.Method.Name switch
            {
                "get_Definition" => definition,
                "get_RoomObject" => item,
                // The toggling half of the test reaches persistence; both of these are the room
                // telling everyone, which is not what is under test here.
                "RefreshStuffDataAsync" => Task.CompletedTask,
                "PublishRoomEventAsync" => Task.CompletedTask,
                _ => null,
            }
        );
    }
}
