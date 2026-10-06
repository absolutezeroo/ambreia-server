namespace Vortex.Rooms.Games.Runtime;

/// <summary>
/// A furni whose countdown clock a game can drive — i.e. the game timer furni
/// (<see cref="Object.Logic.Furniture.Floor.FurnitureGameTimerLogic"/>). The furni owns and ticks
/// its own clock; these methods are just the control surface.
/// </summary>
public interface IGameCounter
{
    /// <summary>Begin counting down from the selected duration.</summary>
    void StartClock();

    /// <summary>Halt, keeping the current value.</summary>
    void HaltClock();

    /// <summary>Continue counting down.</summary>
    void ResumeClock();

    /// <summary>Halt and restore the selected duration.</summary>
    void ResetClock();

    /// <summary>Set the remaining value.</summary>
    void SetClockSeconds(int seconds);

    /// <summary>Increase / decrease (pass a signed delta).</summary>
    void AddClockSeconds(int seconds);
}
