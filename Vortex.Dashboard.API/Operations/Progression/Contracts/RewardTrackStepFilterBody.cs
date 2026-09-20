using System;
using System.Collections.Generic;
using Vortex.Dashboard.API.Hosting;

namespace Vortex.Dashboard.API.Operations.Progression.Contracts;

/// <summary>
/// One test on a signal's facts. <c>Op</c> rather than <c>Operator</c>, which is a keyword in most
/// of the languages this JSON passes through.
/// </summary>
/// <remarks>
/// <c>Left</c> and <c>Right</c> name where each side of the comparison gets its value. A caller
/// that sends neither means what it has always meant: this fact, against this value.
/// </remarks>
public sealed record RewardTrackStepFilterBody(
    string FactKey,
    int Op,
    string Value,
    RewardTrackStepValueBody? Left = null,
    RewardTrackStepValueBody? Right = null
);
