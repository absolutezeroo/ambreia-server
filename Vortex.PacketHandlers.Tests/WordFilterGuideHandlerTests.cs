using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.PacketHandlers.Help;
using Vortex.Primitives.Moderation.WordFilter;
using Vortex.Protocol.Messages.Incoming.Help;
using Vortex.Protocol.Messages.Outgoing.Help;
using Vortex.Protocol.Messages.Outgoing.Notifications;
using Xunit;

namespace Vortex.PacketHandlers.Tests;

/// <summary>The hotel-wide word filter on guide sessions: their chat and the request a guide reads.</summary>
public sealed class WordFilterGuideHandlerTests : WordFilterHandlerFixture
{
    [Fact]
    public async Task ARefusedGuideLine_ReachesNeitherSide()
    {
        await new GuideSessionMessageMessageHandler(Partnered(), Refusing()).HandleAsync(
            new GuideSessionMessageMessage { Message = "con" },
            Context(),
            CancellationToken.None
        );

        Sent.Should().ContainSingle().Which.Should().BeOfType<NotificationDialogMessageComposer>();
        GrainCalls.Should().NotContain(c => c.Method.Name == "SendComposerAsync");
    }

    [Fact]
    public async Task AReplacedGuideLine_IsEchoedReplaced()
    {
        await new GuideSessionMessageMessageHandler(Partnered(), Replacing("bobba")).HandleAsync(
            new GuideSessionMessageMessage { Message = "con" },
            Context(),
            CancellationToken.None
        );

        Sent.Should()
            .ContainSingle()
            .Which.As<GuideSessionMessageMessageComposer>()
            .ChatMessage.Should()
            .Be("bobba");
        AppliedSources.Should().OnlyContain(s => s == WordFilterSource.Guide);
    }

    [Fact]
    public async Task ARefusedHelpRequest_IsNeverOffered()
    {
        await new GuideSessionCreateMessageHandler(Grains(), Refusing()).HandleAsync(
            new GuideSessionCreateMessage { HelpRequestType = 1, Description = "con" },
            Context(),
            CancellationToken.None
        );

        GrainCalls.Should().NotContain(c => c.Method.Name == "CreateRequestAsync");
    }

    // The directory says the player is in a session with player 7.
    private Orleans.IGrainFactory Partnered() =>
        Grains(call => call.Method.Name == "GetPartnerAsync" ? Task.FromResult(7) : null);
}
