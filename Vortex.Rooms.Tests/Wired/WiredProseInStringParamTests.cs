using FluentAssertions;
using Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Actions;
using Xunit;

namespace Vortex.Rooms.Tests.Wired;

/// <summary>
/// What a bot-talk box hands the hotel-wide word filter: the line after the bot's name, never the
/// name itself — the room finds the bot by that name, and "bobba" in it would lose the bot.
/// </summary>
public sealed class WiredProseInStringParamTests
{
    [Fact]
    public void ABotTalkBox_OffersOnlyTheLineAfterTheBotsName()
    {
        const string param = "Frank\tsalut con";

        (int Start, int Length)? prose = WiredActionBotTalk.LineAfterBotName(param);

        prose.Should().NotBeNull();
        param.Substring(prose!.Value.Start, prose.Value.Length).Should().Be("salut con");
    }

    [Fact]
    public void ABotTalkBoxWithNoLine_OffersNothing()
    {
        WiredActionBotTalk.LineAfterBotName("Frank").Should().BeNull();
    }

    /// <summary>A message may contain the separator itself; only the first one splits.</summary>
    [Fact]
    public void ALineWithATabInIt_IsOfferedWhole()
    {
        const string param = "Frank\tsalut\tcon";

        (int Start, int Length)? prose = WiredActionBotTalk.LineAfterBotName(param);

        param.Substring(prose!.Value.Start, prose.Value.Length).Should().Be("salut\tcon");
    }
}
