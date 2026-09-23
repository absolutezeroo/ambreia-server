using System;
using Vortex.Primitives.Rooms.Enums.Wired;

namespace Vortex.Primitives.Rooms.Wired;

public interface IWiredPolicy
{
    public WiredConditionModeType ConditionMode { get; set; }

    /// <summary>The N used by the counting condition modes ("less than / exactly / more than N").</summary>
    public int ConditionCompareValue { get; set; }
    public WiredEffectModeType EffectMode { get; set; }

    /// <summary>How many effects the random mode draws. One unless the random add-on asks for
    /// more.</summary>
    public int EffectPickCount { get; set; }

    /// <summary>How many past firings the random mode avoids repeating, from the same add-on's
    /// second slider. Zero lets it draw the same effect twice in a row.</summary>
    public int EffectAvoidRecentExecutions { get; set; }

    /// <summary>How many times the pile may run inside <see cref="ExecutionWindowMs"/>. Zero is no
    /// limit at all, which is what a pile without the execution-limit add-on has.</summary>
    public int ExecutionLimit { get; set; }

    /// <summary>The window the limit is counted over.</summary>
    public int ExecutionWindowMs { get; set; }
    public WiredAnimationModeType AnimationMode { get; set; }
    public int AnimationTimeMs { get; set; }

    /// <summary>How high the pile's moves arc: a furni's <c>curveStrength</c>, a user's
    /// <c>jumpPower</c>. Zero is a flat slide, which is what a pile without the movement-curve
    /// add-on has.</summary>
    public int CurveStrength { get; set; }
    public TimeSpan Delay { get; set; }
    public bool ShortCircuitOnFirstEffectSuccess { get; set; }
}
