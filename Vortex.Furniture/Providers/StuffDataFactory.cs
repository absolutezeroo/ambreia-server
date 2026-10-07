using System;
using System.Collections.Immutable;
using System.Text.Json;
using Vortex.Furniture.StuffData;
using Vortex.Primitives.Furniture;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Furniture.Snapshots.StuffData;
using Vortex.Primitives.Furniture.StuffData;

namespace Vortex.Furniture.Providers;

public sealed class StuffDataFactory : IStuffDataFactory
{
    /// <summary>
    /// <see cref="ExtraDataWriter"/> serializes sections with <see cref="JsonNamingPolicy.CamelCase"/>,
    /// so a stored section reads <c>{"data":"..."}</c> while the stuff-data classes declare
    /// <c>Data</c>. Deserializing with the default (case-sensitive) options silently matched nothing
    /// and handed back a freshly defaulted instance, which is why saved furni state — a multistate
    /// furni's state, a gate's open flag — came back as "0" after a reload.
    /// </summary>
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public IStuffData CreateStuffData(StuffDataType type)
    {
        return type switch
        {
            StuffDataType.MapKey => new MapStuffData(),
            StuffDataType.StringKey => new StringStuffData(),
            StuffDataType.VoteKey => new VoteStuffData(),
            StuffDataType.EmptyKey => new EmptyStuffData(),
            StuffDataType.NumberKey => new NumberStuffData(),
            StuffDataType.HighscoreKey => new HighscoreStuffData(),
            StuffDataType.CrackableKey => new CrackableStuffData(),
            _ => new LegacyStuffData(),
        };
    }

    public IStuffData CreateStuffDataFromJson(StuffDataType type, string? jsonData)
    {
        type = (StuffDataType)((int)type & StuffDataBase.TYPE_MASK);

        if (!string.IsNullOrEmpty(jsonData))
        {
            ExtraData reader = new ExtraData(jsonData);

            return CreateStuffDataFromExtraData(type, reader);
        }

        return CreateStuffData(type);
    }

    public IStuffData CreateStuffDataFromExtraData(StuffDataType type, IExtraData extraData)
    {
        type = (StuffDataType)((int)type & StuffDataBase.TYPE_MASK);

        if (!extraData.TryGetSection(ExtraDataSectionType.STUFF, out JsonElement stuffElement))
        {
            return CreateStuffData(type);
        }

        // Valid JSON of the wrong shape (a number where a list is declared) throws, and this runs
        // inside a room's activation: the item falls back to its default state, as ExtraDataReader
        // does for JSON it cannot parse at all, rather than keeping the whole room shut.
        try
        {
            return type switch
            {
                StuffDataType.MapKey => stuffElement.Deserialize<MapStuffData>(ReadOptions)!,
                StuffDataType.StringKey => stuffElement.Deserialize<StringStuffData>(ReadOptions)!,
                StuffDataType.VoteKey => stuffElement.Deserialize<VoteStuffData>(ReadOptions)!,
                StuffDataType.EmptyKey => stuffElement.Deserialize<EmptyStuffData>(ReadOptions)!,
                StuffDataType.NumberKey => stuffElement.Deserialize<NumberStuffData>(ReadOptions)!,
                StuffDataType.HighscoreKey => stuffElement.Deserialize<HighscoreStuffData>(
                    ReadOptions
                )!,
                StuffDataType.CrackableKey => stuffElement.Deserialize<CrackableStuffData>(
                    ReadOptions
                )!,
                _ => stuffElement.Deserialize<LegacyStuffData>(ReadOptions)!,
            };
        }
        catch (JsonException)
        {
            return CreateStuffData(type);
        }
    }

    public StuffDataSnapshot FromStuffData(IStuffData data)
    {
        int bitmask = data.GetBitmask();
        int uniqueNumber = data.UniqueNumber;
        int uniqueSeries = data.UniqueSeries;

        return data switch
        {
            ILegacyStuffData legacy => new LegacyStuffSnapshot
            {
                StuffBitmask = bitmask,
                UniqueNumber = uniqueNumber,
                UniqueSeries = uniqueSeries,
                Data = legacy.GetLegacyString(),
            },
            IMapStuffData map => new MapStuffSnapshot
            {
                StuffBitmask = bitmask,
                UniqueNumber = uniqueNumber,
                UniqueSeries = uniqueSeries,
                Data = map.Data.ToImmutableDictionary(),
            },
            INumberStuffData number => new NumberStuffSnapshot
            {
                StuffBitmask = bitmask,
                UniqueNumber = uniqueNumber,
                UniqueSeries = uniqueSeries,
                Data = [.. number.Data],
            },
            IStringStuffData str => new StringStuffSnapshot
            {
                StuffBitmask = bitmask,
                UniqueNumber = uniqueNumber,
                UniqueSeries = uniqueSeries,
                Data = [.. str.Data],
            },
            IVoteStuffData vote => new VoteStuffSnapshot
            {
                StuffBitmask = bitmask,
                UniqueNumber = uniqueNumber,
                UniqueSeries = uniqueSeries,
                Data = vote.GetLegacyString(),
                Result = vote.Result,
            },
            IHighscoreStuffData highscore => new HighscoreStuffSnapshot
            {
                StuffBitmask = bitmask,
                UniqueNumber = uniqueNumber,
                UniqueSeries = uniqueSeries,
                Data = highscore.GetLegacyString(),
                ScoreType = highscore.ScoreType,
                ClearType = highscore.ClearType,
                Scores = highscore.HighscoreData.ToImmutableDictionary(
                    kv => kv.Key,
                    kv => kv.Value.ToImmutableArray()
                ),
            },
            ICrackableStuffData crackable => new CrackableStuffSnapshot
            {
                StuffBitmask = bitmask,
                UniqueNumber = uniqueNumber,
                UniqueSeries = uniqueSeries,
                Data = crackable.GetLegacyString(),
                Hits = crackable.Hits,
                Target = crackable.Target,
            },
            IEmptyStuffData empty => new EmptyStuffSnapshot
            {
                StuffBitmask = bitmask,
                UniqueNumber = uniqueNumber,
                UniqueSeries = uniqueSeries,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(data), "Unknown stuff data type"),
        };
    }
}
