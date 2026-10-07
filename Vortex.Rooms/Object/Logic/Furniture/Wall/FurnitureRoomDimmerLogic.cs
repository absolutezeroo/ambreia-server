using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vortex.Primitives.Action;
using Vortex.Primitives.Furniture.Enums;
using Vortex.Primitives.Furniture.Providers;
using Vortex.Primitives.Rooms.Object.Furniture.Wall;
using Vortex.Primitives.Rooms.Object.Logic;
using Vortex.Primitives.Rooms.Snapshots.Furniture;

namespace Vortex.Rooms.Object.Logic.Furniture.Wall;

/// <summary>
/// The moodlight. A wall lamp that tints the whole room, holding three colour presets and a
/// selection between them.
/// </summary>
/// <remarks>
/// Two stores, because the client reads them through two different channels. The live setting —
/// on/off, which preset, its colour and brightness — travels as the furni's legacy stuff data, in
/// the exact five-field comma layout the client's dimmer logic splits:
/// <c>state,presetId,effectId,#RRGGBB,brightness</c>. The state field is one-based on the wire
/// (<c>parseInt(fields[0]) - 1</c> in <c>dispatchColorUpdateEvent</c>), so an off lamp writes 1 and
/// an on lamp 2; writing 0/1 there leaves the client showing an off lamp for both.
/// <para>
/// The other two presets are not on that string at all — the client only learns them from the
/// presets packet when its dialog opens — so they live in the item's own extra-data section and
/// never touch stuff data.
/// </para>
/// <para>
/// The dimmer dialog's packets have no handler since the cut of 2026-10-07: only tests call
/// <see cref="TogglePowerAsync"/> and <see cref="SavePresetAsync"/>.
/// </para>
/// </remarks>
[RoomObjectLogic("furniture_roomdimmer")]
public class FurnitureRoomDimmerLogic(IStuffDataFactory stuffDataFactory, IRoomWallItemContext ctx)
    : FurnitureWallLogic(stuffDataFactory, ctx)
{
    public const int PresetCount = 3;

    /// <summary>Tint the whole room. The client's own default when a preset carries nothing usable.</summary>
    private const int WholeRoomEffect = 1;
    private const int BackgroundOnlyEffect = 2;

    /// <summary>
    /// The floor of the widget's brightness slider (<c>DimmerFurniWidget.minLights</c>). Values
    /// under it are reachable on the wire but not in the dialog, and a dark room nobody can lighten
    /// again from the UI is a trap rather than a feature.
    /// </summary>
    private const int MinBrightness = 76;
    private const int MaxBrightness = 255;

    /// <summary>
    /// The first three swatches of the client's own palette (<c>DimmerFurniWidget.AVAILABLE_COLORS</c>),
    /// so a brand-new lamp opens with its selection already highlighted in the grid rather than on
    /// a colour the grid cannot show.
    /// </summary>
    private static readonly string[] DefaultColors = ["#74F5F5", "#0053F7", "#E759DE"];

    /// <summary>
    /// <c>ExtraDataWriter</c> stores sections camel-cased, so a preset is written as
    /// <c>{"colorHex":…}</c> while this record declares <c>ColorHex</c>. Reading it back with the
    /// default, case-sensitive options matches nothing — and because the record's members are
    /// <c>required</c>, that surfaces as a deserialization failure and three factory colours rather
    /// than as anything a log would show. Same trap <c>StuffDataFactory</c> documents.
    /// </summary>
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private List<RoomDimmerPresetSnapshot>? _presets;

    public bool IsOn => ReadStateField() > 1;

    public int SelectedPresetId
    {
        get
        {
            int selected = ReadField(1, 1);

            return selected is >= 1 and <= PresetCount ? selected : 1;
        }
    }

    public ImmutableArray<RoomDimmerPresetSnapshot> GetPresets() => [.. Presets];

    /// <summary>
    /// Flips the lamp. Turning it on re-applies the selected preset rather than trusting whatever
    /// colour was last written: the stuff data of a lamp that has never been configured holds no
    /// colour at all, and a "2,,,," string blanks the room to nothing on every client in it.
    /// </summary>
    public async Task<bool> TogglePowerAsync()
    {
        bool turningOn = !IsOn;
        RoomDimmerPresetSnapshot preset = GetPreset(SelectedPresetId);

        await WriteAsync(turningOn, preset).ConfigureAwait(true);

        return turningOn;
    }

    /// <summary>
    /// Overwrites one of the three presets, and switches the lamp to it when the dialog asked for
    /// that. The values are clamped here rather than trusted: the widget's sliders stay in range but
    /// the packet is just six numbers and a string.
    /// </summary>
    public async Task SavePresetAsync(
        int presetNumber,
        int effectId,
        string colorHex,
        int brightness,
        bool apply
    )
    {
        if (presetNumber is < 1 or > PresetCount)
        {
            return;
        }

        RoomDimmerPresetSnapshot preset = new()
        {
            Id = presetNumber,
            EffectId = effectId == BackgroundOnlyEffect ? BackgroundOnlyEffect : WholeRoomEffect,
            ColorHex = NormalizeColor(colorHex, presetNumber),
            Brightness = Math.Clamp(brightness, MinBrightness, MaxBrightness),
        };

        Presets[presetNumber - 1] = preset;

        PersistPresets();

        // Storing without applying still has to reach stuff data when this is the preset the lamp
        // is currently showing, or the room keeps the old colour until someone toggles the switch.
        if (apply || (IsOn && SelectedPresetId == presetNumber))
        {
            await WriteAsync(true, preset).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// The dimmer's own widget is what a click opens, client-side; there is no state to advance.
    /// Left as a no-op deliberately — the inherited toggle would run <c>SetStateAsync</c>, which
    /// replaces the five-field string with a bare state number and erases the room's colour.
    /// </summary>
    public override Task OnUseAsync(ActionContext ctx, int param, CancellationToken ct) =>
        Task.CompletedTask;

    private List<RoomDimmerPresetSnapshot> Presets => _presets ??= LoadPresets();

    private RoomDimmerPresetSnapshot GetPreset(int presetId) =>
        Presets[Math.Clamp(presetId, 1, PresetCount) - 1];

    private Task WriteAsync(bool on, RoomDimmerPresetSnapshot preset)
    {
        StuffData.SetState(
            string.Join(
                ',',
                (on ? 2 : 1).ToString(CultureInfo.InvariantCulture),
                preset.Id.ToString(CultureInfo.InvariantCulture),
                preset.EffectId.ToString(CultureInfo.InvariantCulture),
                preset.ColorHex,
                preset.Brightness.ToString(CultureInfo.InvariantCulture)
            )
        );

        return PersistStuffDataAsync();
    }

    private int ReadStateField() => ReadField(0, 1);

    private int ReadField(int index, int fallback)
    {
        string[] fields = GetLegacyString().Split(',');

        return
            fields.Length > index
            && int.TryParse(fields[index], CultureInfo.InvariantCulture, out int value)
            ? value
            : fallback;
    }

    private List<RoomDimmerPresetSnapshot> LoadPresets()
    {
        if (
            _ctx.RoomObject.ExtraData.TryGetSection(
                ExtraDataSectionType.DIMMER,
                out JsonElement element
            )
        )
        {
            try
            {
                RoomDimmerPresetSnapshot[]? stored =
                    element.Deserialize<RoomDimmerPresetSnapshot[]>(ReadOptions);

                if (stored is { Length: PresetCount } && stored.All(p => p is not null))
                {
                    return [.. stored];
                }
            }
            catch (JsonException)
            {
                // Falls through to the defaults: a lamp that renders the factory colours is a far
                // better outcome than a wall item the room cannot build at all.
            }
        }

        return
        [
            .. Enumerable
                .Range(1, PresetCount)
                .Select(id => new RoomDimmerPresetSnapshot
                {
                    Id = id,
                    EffectId = WholeRoomEffect,
                    ColorHex = DefaultColors[id - 1],
                    Brightness = MaxBrightness,
                }),
        ];
    }

    private void PersistPresets() =>
        _ctx.RoomObject.ExtraData.UpdateSection(ExtraDataSectionType.DIMMER, Presets);

    /// <summary>
    /// The client formats the colour itself and parses it back with <c>parseInt(substr(1), 16)</c>,
    /// so anything that is not a hash and six hex digits reaches it as NaN and paints the room
    /// black. Rejected values fall back to the slot's factory colour.
    /// </summary>
    private static string NormalizeColor(string colorHex, int presetNumber)
    {
        if (
            colorHex.Length == 7
            && colorHex[0] == '#'
            && int.TryParse(
                colorHex.AsSpan(1),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out _
            )
        )
        {
            return colorHex.ToUpperInvariant();
        }

        return DefaultColors[presetNumber - 1];
    }
}
