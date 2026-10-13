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
    bool IsPaused = false,
    double BallSize = GameRules.BallSize,
    double LeftPaddleHeight = GameRules.PaddleHeight,
    double RightPaddleHeight = GameRules.PaddleHeight,
    NuclearSnapshot? Nuclear = null)
{
    public GameMode Mode => Nuclear is null ? GameMode.Classic : GameMode.Nuclear;

    public double PaddleHeight(Side side) => side == Side.Left ? LeftPaddleHeight : RightPaddleHeight;

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
