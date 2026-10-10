using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vortex.Primitives.World.Rules;
using Vortex.Rooms.Grains.Zones.Triggers.Actions;
using Vortex.Rooms.Grains.Zones.Triggers.Conditions;
using Vortex.Rpg.Zones.Rules;

namespace Vortex.Rooms.Grains.Zones.Triggers;

/// <summary>
/// Who answers each condition and action (spec Z1 §4.2): the room's own rules, then those the
/// modules declare; and the parts of what a zone knows of a player, with their sources (§4.3). A
/// rule declared twice is the last declaration's, said once at start. A condition nobody answers,
/// or one that throws, is false — content gated on it stays shut; an action nobody carries out is
/// logged and skipped. One instance serves every room: the pieces it holds keep no state.
/// </summary>
public sealed class ZoneRuleRegistry
{
    private readonly Dictionary<Type, IRoomConditionEvaluator> _roomConditions;
    private readonly Dictionary<Type, IRoomActionExecutor> _roomActions;
    private readonly Dictionary<Type, IZoneConditionEvaluator> _conditions = [];
    private readonly Dictionary<Type, IZoneActionExecutor> _actions = [];
    private readonly Dictionary<Type, IZoneFactsSource> _sources = [];
    private readonly ILogger<ZoneRuleRegistry> _logger;

    public ZoneRuleRegistry(
        IEnumerable<IZoneConditionEvaluator> conditions,
        IEnumerable<IZoneActionExecutor> actions,
        IEnumerable<IZoneFactsSource> sources,
        ILogger<ZoneRuleRegistry> logger
    )
    {
        _logger = logger;
        _roomConditions = RoomRules.Conditions.ToDictionary(c => c.ConditionType);
        _roomActions = RoomRules.Actions.ToDictionary(a => a.ActionType);

        foreach (IZoneConditionEvaluator condition in conditions)
        {
            Declare(_conditions, _roomConditions.ContainsKey(condition.RuleType), condition);
        }

        foreach (IZoneActionExecutor action in actions)
        {
            Declare(_actions, _roomActions.ContainsKey(action.RuleType), action);
        }

        foreach (IZoneFactsSource source in sources)
        {
            _sources[source.FactsType] = source;
        }
    }

    /// <summary>Every declared part of the zone's facts, read when a player enters.</summary>
    public IReadOnlyCollection<IZoneFactsSource> FactsSources => _sources.Values;

    /// <summary>The source of a part, to work out what a change of it sets off; null if undeclared.</summary>
    public IZoneFactsSource? SourceOf(Type factsType) => _sources.GetValueOrDefault(factsType);

    /// <summary>Whether anything answers this condition type: else it is always false.</summary>
    public bool Answers(Type conditionType) =>
        _conditions.ContainsKey(conditionType) || _roomConditions.ContainsKey(conditionType);

    private void Declare<TPiece>(Dictionary<Type, TPiece> pieces, bool roomHasIt, TPiece piece)
        where TPiece : IZoneRuleDeclaration
    {
        if (roomHasIt || pieces.ContainsKey(piece.RuleType))
        {
            _logger.LogWarning(
                "Zone rule {Rule} is declared twice; {Piece}, the last one, answers it.",
                piece.RuleType.Name,
                piece.GetType().Name
            );
        }

        pieces[piece.RuleType] = piece;
    }

    public bool AllHold(IEnumerable<ZoneCondition> conditions, ZoneTriggerContext ctx) =>
        conditions.All(c => Holds(c, ctx));

    private bool Holds(ZoneCondition condition, ZoneTriggerContext ctx)
    {
        Type type = condition.GetType();

        try
        {
            if (_conditions.TryGetValue(type, out IZoneConditionEvaluator? declared))
            {
                return declared.Holds(condition, ctx.ToRuleContext());
            }

            return _roomConditions.TryGetValue(type, out IRoomConditionEvaluator? own)
                && own.Holds(condition, ctx);
        }
        catch (Exception ex)
        {
            // A step or a passage asks this; a broken piece shuts its content, not the walk.
            _logger.LogWarning(
                ex,
                "Zone condition {Condition} threw; it counts as false.",
                type.Name
            );

            return false;
        }
    }

    public Task RunAsync(ZoneAction action, ZoneTriggerContext ctx, CancellationToken ct)
    {
        Type type = action.GetType();

        if (_actions.TryGetValue(type, out IZoneActionExecutor? declared))
        {
            return declared.RunAsync(action, ctx.ToRuleContext(), ct);
        }

        if (_roomActions.TryGetValue(type, out IRoomActionExecutor? own))
        {
            return own.RunAsync(action, ctx, ct);
        }

        _logger.LogDebug("Zone action {Action} has no effect: no module carries it out", type.Name);

        return Task.CompletedTask;
    }
}
