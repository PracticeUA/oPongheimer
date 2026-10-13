namespace Pongheimer.Core.Game;

/// <summary>Розміри поля та параметри гри. Єдине джерело правди для симуляції, протоколу й UI.</summary>
public static class GameRules
{
    public const double FieldWidth = 800;
    public const double FieldHeight = 450;

    public const double PaddleWidth = 12;
    public const double PaddleHeight = 80;
    public const double LeftPaddleX = 24;
    public const double RightPaddleX = FieldWidth - 24 - PaddleWidth;
    public const double MaxPaddleY = FieldHeight - PaddleHeight;
    public const double CenteredPaddleY = MaxPaddleY / 2;
    public const double PaddleSpeed = 520;

    public const double BallSize = 12;
    public const double StartBallSpeed = 380;
    public const double MaxBallSpeed = 900;
    public const double SpeedUpPerHit = 1.06;
    public const double MaxBounceAngle = 50 * Math.PI / 180;
    public const double MaxServeAngle = 20 * Math.PI / 180;

    public const double ServeDelay = 1.0;
    public const int WinScore = 7;

    public static double ClampPaddle(double y) => Math.Clamp(y, 0, MaxPaddleY);
}
