namespace Pongheimer.Core.Game;

/// <summary>Те, що сталося в грі між двома сусідніми кадрами.</summary>
public readonly record struct GameEvents(
    bool PaddleHit,
    bool WallBounce,
    Side? ScoredBy,
    Side? Winner,
    Detonation? Detonated = null);

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
        var detonated = NewDetonation(previous, state);
        if (scoredBy is not null || state.Phase != GamePhase.Playing || previous.Phase != GamePhase.Playing)
        {
            // М'яч переставили в центр або він стоїть: його «рух» між кадрами — не відбиття.
            _directionX = 0;
            _directionY = 0;
            return new GameEvents(false, false, scoredBy, winner, detonated);
        }

        // Напрямок по X змінює лише ракетка. Вона ж може змінити й напрямок по Y,
        // тому відбиття від стіни рахуємо тільки тоді, коли X не змінювався.
        var directionX = Math.Sign(state.BallX - previous.BallX);
        var directionY = Math.Sign(state.BallY - previous.BallY);

        if (detonated is not null)
        {
            // Вибух бомби теж може розвернути м'яч чи змінити його розмір — це не удар і не стіна.
            // Забуваємо старий напрямок, щоб і наступний кадр не сприйняв розворот за відбиття.
            _directionX = 0;
            _directionY = 0;
            return new GameEvents(false, false, null, null, detonated);
        }

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

        return new GameEvents(paddleHit, wallBounce, null, null, detonated);
    }

    // Лічильник вибухів змінився — отже, щойно підірвали бомбу. Нуль означає «вибухів ще не було»
    // (початок партії або реванш), тож це не подія.
    private static Detonation? NewDetonation(GameState previous, GameState state)
    {
        if (state.Nuclear is not { } nuclear || nuclear.LastDetonation.Count == 0)
        {
            return null;
        }

        var previousCount = previous.Nuclear?.LastDetonation.Count ?? 0;
        return nuclear.LastDetonation.Count != previousCount ? nuclear.LastDetonation : null;
    }
}
