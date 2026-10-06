using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Vortex.Rooms.Object;

/// <summary>
/// Carries the logger every room-object logic shares.
/// <para>
/// It is attached after construction rather than taken as a constructor parameter, and that is a
/// deliberate trade. Logic classes sit at the bottom of a base chain -- a floor furni derives from
/// FurnitureFloorLogic, which derives from FurnitureLogic, and so on -- so a constructor
/// parameter on any of those bases has to be declared and forwarded by every leaf below it,
/// including the great majority that never log. Attaching once, here, keeps the logger off every
/// leaf's signature and off every contract.
/// </para>
/// <para>
/// Logic constructors never log, so the null logger is only ever live between construction and
/// <see cref="AttachLogger"/> -- a window with no code in it.
/// </para>
/// </summary>
public abstract class RoomObjectLogicBase
{
    protected ILogger _logger = NullLogger.Instance;

    internal void AttachLogger(ILogger logger) => _logger = logger;
}
