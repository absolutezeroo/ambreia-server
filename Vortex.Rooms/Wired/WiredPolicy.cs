using System;
using Vortex.Primitives.Rooms.Enums.Wired;
using Vortex.Primitives.Rooms.Wired;

namespace Vortex.Rooms.Wired;

public sealed class WiredPolicy : IWiredPolicy
{
    public WiredConditionModeType ConditionMode { get; set; } = WiredConditionModeType.All;
    public int ConditionCompareValue { get; set; }
    public WiredEffectModeType EffectMode { get; set; } = WiredEffectModeType.All;
    public int EffectPickCount { get; set; } = 1;
    public int EffectAvoidRecentExecutions { get; set; }
    public int ExecutionLimit { get; set; }
    public int ExecutionWindowMs { get; set; }
    public WiredAnimationModeType AnimationMode { get; set; } = WiredAnimationModeType.Smooth;

    // The default slide duration for wired moves. 50ms (one wired tick) was imperceptible — furni and
    // users snapped to their destination instead of sliding. 500ms is Habbo's standard object/avatar
    // slide; the animation-time addon (wf_xtra_anim_time) can still tune it 50-2000ms per box.
    public int AnimationTimeMs { get; set; } = 500;
    public int CurveStrength { get; set; }
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;
    public bool ShortCircuitOnFirstEffectSuccess { get; set; }
}
