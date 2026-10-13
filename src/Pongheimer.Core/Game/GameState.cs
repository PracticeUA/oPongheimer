namespace Pongheimer.Core.Game;

public enum Side
{
    Left,
    Right,
}

public enum GamePhase : byte
{
    Serve,
    Playing,
    GameOver,
}

/// <summary>Незмінний знімок стану гри: те, що малює UI і що передається мережею.</summary>
public readonly record struct GameState(
    double BallX,
    double BallY,
    double LeftPaddleY,
    double RightPaddleY,
    int ScoreLeft,
    int ScoreRight,
    GamePhase Phase,
    bool IsPaused = false)
{
    public static GameState Initial { get; } = new(
        (GameRules.FieldWidth - GameRules.BallSize) / 2,
        (GameRules.FieldHeight - GameRules.BallSize) / 2,
        GameRules.CenteredPaddleY,
        GameRules.CenteredPaddleY,
        0,
        0,
        GamePhase.Serve);

    public Side? Winner => Phase != GamePhase.GameOver
        ? null
        : ScoreLeft > ScoreRight ? Side.Left : Side.Right;
}
