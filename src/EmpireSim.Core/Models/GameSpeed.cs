namespace EmpireSim.Core.Models;

/// <summary>
/// Player-controlled speed of the game clock. The simulation advances
/// one in-game day per tick; speed only changes how fast ticks arrive.
/// </summary>
public enum GameSpeed
{
    Paused,
    Normal,
    Fast,
    VeryFast
}
