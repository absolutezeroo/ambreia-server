using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Vortex.Furniture.Providers;
using Vortex.Primitives.Players;
using Vortex.Primitives.Rooms.Enums.Wired;
using Vortex.Primitives.Rooms.Object;
using Vortex.Primitives.Rooms.Object.Avatars;
using Vortex.Primitives.Rooms.Object.Furniture;
using Vortex.Primitives.Rooms.Object.Furniture.Floor;
using Vortex.Primitives.Rooms.Snapshots.Wired.Variables;
using Vortex.Primitives.Rooms.Wired;
using Vortex.Primitives.Rooms.Wired.Variable;
using Vortex.Rooms.Object.Logic.Furniture.Floor.Wired.Selectors;
using Vortex.Rooms.Wired;
using Vortex.Tests.Support;
using Xunit;

namespace Vortex.Rooms.Tests.Wired;

/// <summary>
/// "Furni with variable" and "users with variable". Both were stubs: the furni one selected nothing
/// at all, the users one carried a copy of "furni of the same type", so a room built on either
/// selected the wrong things or nothing, silently.
/// </summary>
public sealed class WiredVariableSelectorTests
{
    private const string VariableId = "4242";

    private static readonly PlayerId Holder = new(7);
    private static readonly PlayerId Bystander = new(8);

    [Fact]
    public async Task FurniWithVariable_SelectsEveryFurniHoldingIt()
    {
        FakeVariable variable = new(WiredVariableTargetType.Furni);
        variable.Values[Key(WiredVariableTargetType.Furni, 10)] = new(1);

        IWiredSelectionSet selected = await new TestFurniSelector(
            Context(variable, items: [10, 11]),
            Config(HoldingOnly())
        )
            .SelectAsync(null!, CancellationToken.None)
            .ConfigureAwait(true);

        selected.SelectedFurniIds.Should().Equal(10);
        selected.SelectedPlayerIds.Should().BeEmpty();
    }

    [Fact]
    public async Task UsersWithVariable_FiltersByValue()
    {
        FakeVariable variable = new(WiredVariableTargetType.User);
        variable.Values[Key(WiredVariableTargetType.User, (int)Holder)] = new(5);
        variable.Values[Key(WiredVariableTargetType.User, (int)Bystander)] = new(2);

        IWiredSelectionSet selected = await new TestUserSelector(
            Context(variable, players: [Holder, Bystander]),
            Config(AgainstNumber(WiredComparisonType.GreaterThan, 3))
        )
            .SelectAsync(null!, CancellationToken.None)
            .ConfigureAwait(true);

        selected.SelectedPlayerIds.Should().Equal((int)Holder);
        selected.SelectedFurniIds.Should().BeEmpty();
    }

    /// <summary>[comparison, mode, long high, long low, reference target], as the client's
    /// <c>VariableSelector.readIntParamsFromForm</c> pushes them.</summary>
    private static List<int> HoldingOnly() => [(int)WiredComparisonType.GreaterThan, 0, 0, 0, 0];

    private static List<int> AgainstNumber(WiredComparisonType comparison, int value) =>
        [(int)comparison, 1, value < 0 ? -1 : 0, value, 0];

    private static WiredData Config(List<int> intParams) =>
        new() { IntParams = intParams, VariableIds = [VariableId, string.Empty] };

    private static WiredVariableKey Key(WiredVariableTargetType target, int targetId) =>
        new(WiredVariableId.Parse(VariableId), target, targetId);

    private static IRoomFloorItemContext Context(
        IWiredVariable variable,
        int[]? items = null,
        PlayerId[]? players = null
    )
    {
        List<IRoomPlayer> roomPlayers = [];

        foreach (PlayerId playerId in players ?? [])
        {
            roomPlayers.Add(
                FakeProxy.Create<IRoomPlayer>(call =>
                    call.Method.Name == $"get_{nameof(IRoomPlayer.PlayerId)}" ? playerId : null
                )
            );
        }

        FakeRoomLookup lookup = new([.. roomPlayers]);

        foreach (int id in items ?? [])
        {
            RoomObjectId objectId = new(id);

            lookup.ItemsById[objectId] = FakeProxy.Create<IRoomItem>(call =>
                call.Method.Name == $"get_{nameof(IRoomItem.ObjectId)}" ? objectId : null
            );
        }

        return WiredTestBoxes.Context(
            lookup: lookup,
            furniAccess: FakeProxy.Create<IRoomFurniAccess>(call =>
                call.Method.Name == nameof(IRoomFurniAccess.GetVariableById) ? variable : null
            )
        );
    }

    private sealed class TestFurniSelector : WiredSelectorItemsWithVariable
    {
        public TestFurniSelector(IRoomFloorItemContext ctx, WiredData data)
            : base(null!, new StuffDataFactory(), ctx)
        {
            data.AttatchRules(GetIntParamRules());
            _wiredData = data;
        }
    }

    private sealed class TestUserSelector : WiredSelectorEntitiesWithVariable
    {
        public TestUserSelector(IRoomFloorItemContext ctx, WiredData data)
            : base(null!, new StuffDataFactory(), ctx)
        {
            data.AttatchRules(GetIntParamRules());
            _wiredData = data;
        }
    }

    /// <summary>A variable holding what the test put in it, keyed like the real stores.</summary>
    private sealed class FakeVariable(WiredVariableTargetType target) : IWiredVariable
    {
        public Dictionary<WiredVariableKey, WiredVariableValue> Values { get; } = [];

        public bool CanBind(in WiredVariableKey key) => key.TargetType == target;

        public bool TryGetValue(in WiredVariableKey key, out WiredVariableValue value)
        {
            value = WiredVariableValue.Default;

            return Values.TryGetValue(key, out value);
        }

        public Task<bool> GiveValueAsync(
            WiredVariableKey key,
            WiredVariableValue value,
            bool replace = false
        ) => Task.FromResult(false);

        public Task<bool> SetValueAsync(
            IWiredExecutionContext ctx,
            WiredVariableKey key,
            WiredVariableValue value
        ) => Task.FromResult(false);

        public bool TryGetTimestamps(
            in WiredVariableKey key,
            out long createdAtMs,
            out long updatedAtMs
        )
        {
            createdAtMs = 0;
            updatedAtMs = 0;

            return false;
        }

        public bool RemoveValue(WiredVariableKey key) => false;

        public WiredVariableSnapshot GetVarSnapshot() =>
            new()
            {
                VariableId = WiredVariableId.Parse(VariableId),
                VariableName = "test",
                VariableType = WiredVariableType.Internal,
                VariableHash = default,
                AvailabilityType = WiredAvailabilityType.Internal,
                TargetType = target,
                Flags = WiredVariableFlags.HasValue,
                TextConnectors = new(),
            };
    }
}
