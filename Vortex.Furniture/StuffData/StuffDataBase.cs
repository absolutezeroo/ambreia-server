using System;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Vortex.Primitives.Furniture.Snapshots.StuffData;
using Vortex.Primitives.Furniture.StuffData;

namespace Vortex.Furniture.StuffData;

internal abstract class StuffDataBase : IStuffData
{
    public const int TYPE_MASK = 0xFF;
    public const int FLAGS_MASK = 0xFF00;
    protected const string DEFAULT_STATE = "0";
    protected const int STATE_INDEX = 0;
    protected const string STATE_KEY = "state";

    [JsonIgnore]
    public abstract StuffDataType StuffType { get; }

    public int UniqueNumber { get; set; } = 0;
    public int UniqueSeries { get; set; } = 0;

    protected Func<Task>? _onSnapshotChanged;
    protected bool _dirty = true;
    protected StuffDataSnapshot? _snapshot;

    public static int CreateBitmask(StuffDataType type, StuffDataFlags flags) =>
        ((int)type & TYPE_MASK) | ((int)flags & FLAGS_MASK);

    public int GetBitmask() =>
        CreateBitmask(StuffType, IsUnique() ? StuffDataFlags.Unique : StuffDataFlags.None);

    public bool IsUnique() => UniqueNumber > 0 && UniqueSeries > 0;

    /// <summary>
    /// The state this data holds, or zero when it does not hold one.
    /// </summary>
    /// <remarks>
    /// Eight classes share this one implementation over eight different payloads — a photo's json, a
    /// sticky's text and colour, a crackable's counter, nothing at all — and only some of those are a
    /// number. It was <c>int.Parse</c>, which turned "this furni has no state" into an exception
    /// thrown out of a room grain's turn: clicking a photo took the whole use-item handler down with
    /// a <see cref="FormatException"/>, and an empty <see cref="GetLegacyString"/> — the default on
    /// this very class — did the same. Zero is what <see cref="DEFAULT_STATE"/> already calls an
    /// absent state.
    /// </remarks>
    public virtual int GetState() =>
        int.TryParse(
            GetLegacyString(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int state
        )
            ? state
            : 0;

    public virtual void SetState(string state) { }

    public virtual string GetLegacyString() => string.Empty;

    public void SetAction(Func<Task>? onSnapshotChanged) => _onSnapshotChanged = onSnapshotChanged;

    public void MarkDirty()
    {
        _dirty = true;

        _ = _onSnapshotChanged?.Invoke();
    }

    public virtual StuffDataSnapshot GetSnapshot()
    {
        if (_dirty || _snapshot is null)
        {
            _snapshot = BuildSnapshot();
            _dirty = false;
        }

        return _snapshot;
    }

    protected abstract StuffDataSnapshot BuildSnapshot();
}
