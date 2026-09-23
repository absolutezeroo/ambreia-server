using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Orleans;
using Vortex.Furniture.Providers;
using Vortex.Primitives.Players.Grains;
using Vortex.Primitives.Rooms.Grains;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Wired;
using Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Actions;
using Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Addons;
using Vortex.Rooms.Wired;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Wired;

/// <summary>
/// The achievement enabler (addon 2001) and the effect it gates (action 51): what a builder types
/// becomes the room's list, and only a listed achievement is ever advanced.
/// </summary>
public sealed class WiredAchievementBoxTests
{
    [Fact]
    public void TheEnablerReadsOneNamePerLine_WithoutThePrefixABuilderTypesAnyway() =>
        WiredAddonAchievementEnabler
            .Parse("Tag\r\n  ACH_WF_Race \n\nWF_Maze\ntag\n")
            .Should()
            .Equal("Tag", "Race", "Maze");

    [Fact]
    public async Task AddMode_AdvancesEveryResolvedUser_OnTheWiredAchievement()
    {
        Harness harness = new(enabled: ["Race"]);

        await harness.RunAsync("Race", mode: 1, amount: 3, players: [7, 8]);

        harness.Calls.Should().Equal(("add", 7, "WF_Race", 3), ("add", 8, "WF_Race", 3));
    }

    [Fact]
    public async Task SetMode_RaisesTheBarInsteadOfAddingToIt()
    {
        Harness harness = new(enabled: ["Race"]);

        await harness.RunAsync("Race", mode: 0, amount: 10, players: [7]);

        harness.Calls.Should().Equal(("set", 7, "WF_Race", 10));
    }

    [Fact]
    public async Task AnAchievementNoEnablerAllows_IsNeverAdvanced()
    {
        Harness harness = new(enabled: ["Maze"]);

        await harness.RunAsync("Race", mode: 1, amount: 3, players: [7]);

        harness.Calls.Should().BeEmpty();
    }

    private sealed class Harness(IReadOnlyList<string> enabled)
    {
        public List<(string Mode, long Player, string Name, int Amount)> Calls { get; } = [];

        public Task<bool> RunAsync(string name, int mode, int amount, int[] players)
        {
            WiredData data = new() { StringParam = name, IntParams = [mode, 0, amount, 0] };
            TestProgress action = new(Factory(), Context(), data);

            data.AttatchRules(action.GetIntParamRules());

            WiredSelectionSet selection = new();
            selection.SelectedPlayerIds.UnionWith(players);

            return action.ExecuteAsync(
                FakeProxy.Create<IWiredExecutionContext>(call =>
                    call.Method.Name == "GetEffectiveSelectionAsync"
                        ? Task.FromResult<IWiredSelectionSet>(selection)
                        : null
                ),
                CancellationToken.None
            );
        }

        /// <summary>The shared test context, with the room answering its enabled list.</summary>
        private IRoomFloorItemContext Context()
        {
            IRoomFloorItemContext inner = WiredTestBoxes.Context();
            IRoomWired room = FakeProxy.Create<IRoomWired>(call =>
                call.Method.Name == nameof(IRoomWired.GetEnabledAchievementsAsync)
                    ? Task.FromResult(enabled)
                    : null
            );

            return FakeProxy.Create<IRoomFloorItemContext>(call =>
                call.Method.Name == "RoomAs" ? room : call.Method.Invoke(inner, call.Args)
            );
        }

        private IGrainFactory Factory() =>
            FakeProxy.Create<IGrainFactory>(call =>
            {
                if (call.Method.Name != "GetGrain" || call.Args![0] is not long player)
                {
                    return null;
                }

                return FakeProxy.Create<IPlayerAchievementGrain>(grainCall =>
                {
                    string? what = grainCall.Method.Name switch
                    {
                        nameof(IPlayerAchievementGrain.ProgressAsync) => "add",
                        nameof(IPlayerAchievementGrain.ProgressToAsync) => "set",
                        _ => null,
                    };

                    if (what is not null)
                    {
                        Calls.Add(
                            (what, player, (string)grainCall.Args![0]!, (int)grainCall.Args[1]!)
                        );
                    }

                    return null;
                });
            });
    }

    private sealed class TestProgress : WiredActionProgressAchievement
    {
        public TestProgress(IGrainFactory factory, IRoomFloorItemContext ctx, WiredData data)
            : base(factory, new StuffDataFactory(), ctx) => _wiredData = data;
    }
}
