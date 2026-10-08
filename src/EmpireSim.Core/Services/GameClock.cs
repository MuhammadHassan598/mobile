using EmpireSim.Core.Models;

namespace EmpireSim.Core.Services;

/// <summary>
/// The heartbeat of the game: fires <see cref="DayElapsed"/> once per
/// in-game day at a rate determined by <see cref="Speed"/>.
/// Pure timer — it knows nothing about game rules.
/// </summary>
public sealed class GameClock : IDisposable
{
    public GameSpeed Speed { get; private set; } = GameSpeed.Paused;

    /// <summary>Raised on a background thread once per in-game day. UI must marshal via InvokeAsync.</summary>
    public event Action? DayElapsed;

    private CancellationTokenSource? _cts;
    private bool _disposed;

    public void SetSpeed(GameSpeed speed)
    {
        if (_disposed) return;
        Speed = speed;
        RestartLoop();
    }

    private void RestartLoop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        if (Speed == GameSpeed.Paused) return;

        _cts = new CancellationTokenSource();
        _ = RunLoopAsync(_cts.Token);
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        var interval = TimeSpan.FromSeconds(1.0 / Balance.DaysPerSecond(Speed));
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(token))
                DayElapsed?.Invoke();
        }
        catch (OperationCanceledException)
        {
            // Expected when speed changes or clock is disposed.
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
