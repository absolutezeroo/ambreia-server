using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.PacketHandlers.GroupForums;
using Vortex.PacketHandlers.Users;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Protocol.Messages.Incoming.GroupForums;
using Vortex.Protocol.Messages.Incoming.Users;
using Vortex.Protocol.Messages.Outgoing.Notifications;
using Vortex.Protocol.Messages.Outgoing.Users;
using Xunit;

namespace Vortex.PacketHandlers.Tests;

/// <summary>The hotel-wide word filter on groups and their forums.</summary>
public sealed class WordFilterSocialHandlerTests : WordFilterHandlerFixture
{
    private static CreateGuildMessage Guild(string name, string description) =>
        new()
        {
            Name = name,
            Description = description,
            PrimaryColorId = 1,
            SecondaryColorId = 2,
            BaseRoomId = 3,
            BadgeParts = [],
        };

    /// <summary>Before the grain: creating a group debits credits, and a refused one must not.</summary>
    [Fact]
    public async Task ARefusedGroupName_IsNeverCreated_AndTheDialogIsTold()
    {
        await new CreateGuildMessageHandler(Grains(), RefusingOnly("con")).HandleAsync(
            Guild("con", "ok"),
            Context(),
            CancellationToken.None
        );

        GrainCalls.Should().NotContain(c => c.Method.Name == "CreateGroupAsync");
        Sent.Should().ContainSingle(s => s is NotificationDialogMessageComposer);
        Sent.Should()
            .ContainSingle(s => s is GuildEditFailedMessageComposer)
            .Which.As<GuildEditFailedMessageComposer>()
            .Reason.Should()
            .Be(1);
    }

    [Fact]
    public async Task ARefusedGroupDescription_IsNeverCreated()
    {
        await new CreateGuildMessageHandler(Grains(), RefusingOnly("con")).HandleAsync(
            Guild("ok", "con"),
            Context(),
            CancellationToken.None
        );

        GrainCalls.Should().NotContain(c => c.Method.Name == "CreateGroupAsync");
    }

    [Fact]
    public async Task AReplacedGroup_IsCreatedReplaced_AsAGroup()
    {
        await new CreateGuildMessageHandler(Grains(), Replacing("bobba")).HandleAsync(
            Guild("con", "con"),
            Context(),
            CancellationToken.None
        );

        ProxyCallArgs("CreateGroupAsync")[1].Should().Be("bobba");
        ProxyCallArgs("CreateGroupAsync")[2].Should().Be("bobba");
        FilterCalls
            .Should()
            .OnlyContain(c => (WordFilterSource)c.Args![1]! == WordFilterSource.Group);
    }

    [Fact]
    public async Task ARefusedGroupEdit_IsNeverSaved_AndSaysInvalidName()
    {
        await new UpdateGuildIdentityMessageHandler(Grains(), Refusing()).HandleAsync(
            new UpdateGuildIdentityMessage
            {
                GroupId = 7,
                Name = "con",
                Description = "x",
            },
            Context(),
            CancellationToken.None
        );

        GrainCalls.Should().NotContain(c => c.Method.Name == "UpdateIdentityAsync");
        Sent.Should()
            .ContainSingle(s => s is GuildEditFailedMessageComposer)
            .Which.As<GuildEditFailedMessageComposer>()
            .Reason.Should()
            .Be(1);
    }

    [Fact]
    public async Task AReplacedGroupEdit_IsSavedReplaced()
    {
        await new UpdateGuildIdentityMessageHandler(Grains(), Replacing("bobba")).HandleAsync(
            new UpdateGuildIdentityMessage
            {
                GroupId = 7,
                Name = "con",
                Description = "con",
            },
            Context(),
            CancellationToken.None
        );

        ProxyCallArgs("UpdateIdentityAsync")[1].Should().Be("bobba");
        ProxyCallArgs("UpdateIdentityAsync")[2].Should().Be("bobba");
    }

    [Fact]
    public async Task ARefusedNewThread_IsNeverPosted()
    {
        await new PostMessageMessageHandler(Grains(), RefusingOnly("con")).HandleAsync(
            new PostMessageMessage
            {
                GroupId = 7,
                ThreadId = 0,
                Title = "con",
                Message = "ok",
            },
            Context(),
            CancellationToken.None
        );

        GrainCalls.Should().NotContain(c => c.Method.Name == "PostAsync");
        Sent.Should().ContainSingle(s => s is NotificationDialogMessageComposer);
    }

    /// <summary>A reply's title is ignored by the forum, so it is not the filter's business either.</summary>
    [Fact]
    public async Task AReply_FiltersItsMessage_AndNotTheTitleItDoesNotUse()
    {
        await new PostMessageMessageHandler(Grains(), Replacing("bobba")).HandleAsync(
            new PostMessageMessage
            {
                GroupId = 7,
                ThreadId = 3,
                Title = "con",
                Message = "con",
            },
            Context(),
            CancellationToken.None
        );

        FilterCalls.Should().ContainSingle().Which.Args![0].Should().Be("con");
        ProxyCallArgs("PostAsync")[3].Should().Be("bobba");
        FilterCalls
            .Should()
            .OnlyContain(c => (WordFilterSource)c.Args![1]! == WordFilterSource.Forum);
    }

    private object?[] ProxyCallArgs(string method) =>
        GrainCalls.Should().ContainSingle(c => c.Method.Name == method).Which.Args!;
}
