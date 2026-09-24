using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.PacketHandlers.Room.Bots;
using Vortex.PacketHandlers.Room.Engine;
using Vortex.PacketHandlers.Room.Furniture;
using Vortex.Primitives.Bots;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Primitives.Rooms;
using Vortex.Primitives.Rooms.Object;
using Vortex.Protocol.Messages.Incoming.Room.Bots;
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

    [Fact]
    public async Task ARefusedBotName_IsNeverSet_AndIsFilteredAsAKey()
    {
        await new CommandBotMessageHandler(Grains(), Refusing()).HandleAsync(
            new CommandBotMessage
            {
                BotId = 8,
                CommandId = BotSkillId.ChangeName,
                Data = "con",
            },
            Context(roomId: 4),
            CancellationToken.None
        );

        GrainCalls.Should().NotContain(c => c.Method.Name == "SetBotSkillAsync");
        AppliedSources.Should().OnlyContain(s => s == WordFilterSource.BotName);
    }

    /// <summary>
    /// Only the lines are prose; the flags after them are the bot's settings and must come back
    /// byte for byte, or the bot stops talking on its own or talks every second.
    /// </summary>
    [Theory]
    [InlineData("salut con;#;true;#;30;#;false", "salut bobba;#;true;#;30;#;false")]
    [InlineData("salut con;true;30", "salut bobba;true;30")]
    [InlineData("salut con", "salut bobba")]
    public async Task ABotsLines_AreFilteredAndItsSettingsKept(string data, string expected)
    {
        await new CommandBotMessageHandler(
            Grains(),
            Filter(text => new WordFilterResult(text.Replace("con", "bobba"), false, true))
        ).HandleAsync(
            new CommandBotMessage
            {
                BotId = 8,
                CommandId = BotSkillId.Chatter,
                Data = data,
            },
            Context(roomId: 4),
            CancellationToken.None
        );

        GrainCall("SetBotSkillAsync")[3].Should().Be(expected);
        AppliedSources.Should().OnlyContain(s => s == WordFilterSource.BotChatter);
    }

    [Fact]
    public async Task ABotCommandWithNoText_IsNotFiltered()
    {
        await new CommandBotMessageHandler(Grains(), Refusing()).HandleAsync(
            new CommandBotMessage
            {
                BotId = 8,
                CommandId = 1,
                Data = "",
            },
            Context(roomId: 4),
            CancellationToken.None
        );

        FilterCalls.Should().BeEmpty();
        GrainCalls.Should().Contain(c => c.Method.Name == "SetBotSkillAsync");
    }
}
