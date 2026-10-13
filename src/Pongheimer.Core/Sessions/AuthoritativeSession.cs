using Pongheimer.Core.Game;
using Pongheimer.Core.Networking;

namespace Pongheimer.Core.Sessions;

/// <summary>
/// Партія, у якій симуляцію веде ця машина. Ліву ракетку рухає клавіатура;
/// нащадки вирішують, хто керує правою.
/// </summary>
public abstract class AuthoritativeSession : IGameSession
{
    // Дрібні кроки тримають фізику стабільною за будь-якої частоти кадрів,
    // а обмеження кадру не дає м'ячу стрибнути через пів поля після фризу.
    private const double MaxStep = 1.0 / 120;
    private const double MaxFrameTime = 0.1;

    protected AuthoritativeSession(PongSimulation simulation) => Simulation = simulation;

    public virtual Side? LocalSide => Side.Left;

    public GameState State => Simulation.State;

    public virtual ConnectionFault? Fault => null;

    protected PongSimulation Simulation { get; }

    protected virtual bool OpponentWantsRematch => false;

    public void Update(double dt, PlayerInput input)
    {
        // Паузу може перемкнути будь-хто з гравців; два натискання в одному кадрі взаємно гасяться.
        if (input.TogglePause)
        {
            Simulation.TogglePause();
        }

        if (ConsumeOpponentPauseToggle())
        {
            Simulation.TogglePause();
        }

        if (!Simulation.State.IsPaused && double.IsFinite(dt) && dt > 0)
        {
            var remaining = Math.Min(dt, MaxFrameTime);
            while (remaining > 0)
            {
                var step = Math.Min(remaining, MaxStep);
                remaining -= step;
                Simulation.MovePaddle(Side.Left, LeftPaddleDirection(input), step);
                UpdateRightPaddle(step, input);
                Simulation.Step(step);
            }
        }

        if (Simulation.State.Phase == GamePhase.GameOver && (input.Confirm || OpponentWantsRematch))
        {
            Simulation.Restart();
        }

        OnUpdated();
    }

    public virtual void Dispose()
    {
    }

    /// <summary>За замовчуванням лівий гравець за клавіатурою сам і керує будь-якими клавішами.</summary>
    protected virtual int LeftPaddleDirection(PlayerInput input) => input.Direction;

    protected abstract void UpdateRightPaddle(double dt, PlayerInput input);

    /// <summary>Повертає true рівно один раз на кожне натискання паузи суперником.</summary>
    protected virtual bool ConsumeOpponentPauseToggle() => false;

    protected virtual void OnUpdated()
    {
    }
}
