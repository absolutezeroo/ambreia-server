using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.PacketHandlers.Room.Engine;
using Vortex.PacketHandlers.Room.Furniture;
using Vortex.Primitives.Bots;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Object;
using Vortex.Protocol.Messages.Incoming.Room.Engine;
using Vortex.Protocol.Messages.Incoming.Room.Furniture;
using Xunit;

namespace Vortex.PacketHandlers.Tests;

/// <summary>The hotel-wide word filter on what players write on things: post-its, mannequins, bots.</summary>
public sealed class WordFilterItemHandlerTests : WordFilterHandlerFixture
{
    [Fact]
    public async Task ARefusedPostIt_IsNeverWritten()
    {
        await new AddSpamWallPostItMessageHandler(Grains(), Refusing()).HandleAsync(
            new AddSpamWallPostItMessage
            {
                ObjectId = new RoomObjectId(3),
                Location = ":w=1,2 l=3,4 l",
                ColorHex = "FFFF33",
                Text = "con",
            },
            Context(roomId: 4),
            CancellationToken.None
        );

        GrainCalls.Should().NotContain(c => c.Method.Name == "SetPostItAsync");
    }

    [Fact]
    public async Task AnEditedPostIt_IsWrittenReplaced_AndItsColourIsLeftAlone()
    {
        await new SetItemDataMessageHandler(Grains(), Replacing("bobba")).HandleAsync(
            new SetItemDataMessage
            {
                ItemId = 3,
                ColorHex = "FFFF33",
                Text = "con",
            },
            Context(roomId: 4),
            CancellationToken.None
        );

        object?[] args = GrainCall("SetPostItAsync");
        args[2].Should().Be("FFFF33");
        args[3].Should().Be("bobba");
        AppliedSources.Should().OnlyContain(s => s == WordFilterSource.PostIt);
    }

    [Fact]
    public async Task AReplacedMannequinName_IsSetReplaced()
    {
        await new SetMannequinNameMessageHandler(Grains(), Replacing("bobba")).HandleAsync(
            new SetMannequinNameMessage { ObjectId = new RoomObjectId(3), Name = "con" },
            Context(roomId: 4),
            CancellationToken.None
        );

        GrainCall("SetMannequinNameAsync")[2].Should().Be("bobba");
    }
}
