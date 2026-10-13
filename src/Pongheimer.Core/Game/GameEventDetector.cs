namespace Pongheimer.Core.Game;

/// <summary>Те, що сталося в грі між двома сусідніми кадрами.</summary>
public readonly record struct GameEvents(bool PaddleHit, bool WallBounce, Side? ScoredBy, Side? Winner);

/// <summary>
/// Виводить ігрові події з послідовності станів. Працює однаково для хоста, клієнта й гри
/// з ботом, бо не потребує нічого, крім самих станів, — тож мережевий протокол подій не передає.
/// </summary>
public sealed class GameEventDetector
{
    private GameState? _previous;
    private int _directionX;
    private int _directionY;

    public void Reset()
    {
        _previous = null;
        _directionX = 0;
        _directionY = 0;
    }

    public GameEvents Observe(GameState state)
    {
        if (_previous is not { } previous)
        {
            _previous = state;
            return default;
        }

        _previous = state;

        Side? scoredBy = state.ScoreLeft > previous.ScoreLeft ? Side.Left
            : state.ScoreRight > previous.ScoreRight ? Side.Right
            : null;
        var winner = previous.Phase != GamePhase.GameOver ? state.Winner : null;
        if (scoredBy is not null || state.Phase != GamePhase.Playing || previous.Phase != GamePhase.Playing)
        {
            // М'яч переставили в центр або він стоїть: його «рух» між кадрами — не відбиття.
            _directionX = 0;
            _directionY = 0;
            return new GameEvents(false, false, scoredBy, winner);
        }

        // Напрямок по X змінює лише ракетка. Вона ж може змінити й напрямок по Y,
        // тому відбиття від стіни рахуємо тільки тоді, коли X не змінювався.
        var directionX = Math.Sign(state.BallX - previous.BallX);
        var directionY = Math.Sign(state.BallY - previous.BallY);
        var paddleHit = directionX != 0 && _directionX != 0 && directionX != _directionX;
        var wallBounce = !paddleHit && directionY != 0 && _directionY != 0 && directionY != _directionY;

        // Нульове зміщення (клієнт ще не отримав новий стан) не скидає відомий напрямок.
        if (directionX != 0)
        {
            _directionX = directionX;
        }

        if (directionY != 0)
        {
            _directionY = directionY;
        }

        return new GameEvents(paddleHit, wallBounce, null, null);
    }
}
