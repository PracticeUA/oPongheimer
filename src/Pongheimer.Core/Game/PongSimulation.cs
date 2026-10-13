namespace Pongheimer.Core.Game;

/// <summary>
/// Правила та фізика Pong. Не знає ні про UI, ні про мережу, ні про те, хто керує ракетками.
/// </summary>
public sealed class PongSimulation
{
    private readonly Random _random;

    private double _leftY = GameRules.CenteredPaddleY;
    private double _rightY = GameRules.CenteredPaddleY;
    private double _ballX;
    private double _ballY;
    private double _ballVx;
    private double _ballVy;
    private double _ballSpeed;
    private double _serveTimer;
    private int _serveDirection = 1;
    private int _scoreLeft;
    private int _scoreRight;
    private GamePhase _phase;
    private bool _isPaused;

    public PongSimulation(Random? random = null)
    {
        _random = random ?? Random.Shared;
        BeginServe();
    }

    public GameState State => new(_ballX, _ballY, _leftY, _rightY, _scoreLeft, _scoreRight, _phase, _isPaused);

    public double BallVelocityX => _ballVx;

    public double BallVelocityY => _ballVy;

    public void SetPaddle(Side side, double y)
    {
        if (!double.IsFinite(y))
        {
            return;
        }

        if (side == Side.Left)
        {
            _leftY = GameRules.ClampPaddle(y);
        }
        else
        {
            _rightY = GameRules.ClampPaddle(y);
        }
    }

    public void MovePaddle(Side side, int direction, double dt)
    {
        var current = side == Side.Left ? _leftY : _rightY;
        SetPaddle(side, current + Math.Sign(direction) * GameRules.PaddleSpeed * dt);
    }

    public void Restart()
    {
        _scoreLeft = 0;
        _scoreRight = 0;
        _isPaused = false;
        BeginServe();
    }

    /// <summary>Ставить гру на паузу або знімає з неї. Завершену гру на паузу не поставити.</summary>
    public void TogglePause()
    {
        if (_phase != GamePhase.GameOver)
        {
            _isPaused = !_isPaused;
        }
    }

    public void Step(double dt)
    {
        if (_isPaused || !double.IsFinite(dt) || dt <= 0)
        {
            return;
        }

        switch (_phase)
        {
            case GamePhase.Serve:
                _serveTimer -= dt;
                if (_serveTimer <= 0)
                {
                    var angle = (_random.NextDouble() * 2 - 1) * GameRules.MaxServeAngle;
                    LaunchBall(
                        _ballX,
                        _ballY,
                        _serveDirection * GameRules.StartBallSpeed * Math.Cos(angle),
                        GameRules.StartBallSpeed * Math.Sin(angle));
                }

                break;

            case GamePhase.Playing:
                MoveBall(dt);
                break;
        }
    }

    /// <summary>Ставить м'яч у гру із заданими позицією та швидкістю.</summary>
    internal void LaunchBall(double x, double y, double vx, double vy)
    {
        _ballX = x;
        _ballY = y;
        _ballVx = vx;
        _ballVy = vy;
        _ballSpeed = Math.Sqrt(vx * vx + vy * vy);
        _phase = GamePhase.Playing;
    }

    private void BeginServe()
    {
        _ballX = GameState.Initial.BallX;
        _ballY = GameState.Initial.BallY;
        _ballVx = 0;
        _ballVy = 0;
        _serveTimer = GameRules.ServeDelay;
        _phase = GamePhase.Serve;
    }

    private void MoveBall(double dt)
    {
        var previousX = _ballX;
        _ballX += _ballVx * dt;
        _ballY += _ballVy * dt;

        const double maxBallY = GameRules.FieldHeight - GameRules.BallSize;
        if (_ballY < 0)
        {
            _ballY = Math.Min(-_ballY, maxBallY);
            _ballVy = -_ballVy;
        }
        else if (_ballY > maxBallY)
        {
            _ballY = Math.Max(2 * maxBallY - _ballY, 0);
            _ballVy = -_ballVy;
        }

        // Перевіряємо перетин площини ракетки між кроками, щоб швидкий м'яч не пролітав крізь неї.
        const double leftFace = GameRules.LeftPaddleX + GameRules.PaddleWidth;
        const double rightFace = GameRules.RightPaddleX - GameRules.BallSize;
        if (_ballVx < 0 && previousX >= leftFace && _ballX <= leftFace && OverlapsPaddle(_leftY))
        {
            _ballX = leftFace;
            Bounce(_leftY, direction: 1);
        }
        else if (_ballVx > 0 && previousX <= rightFace && _ballX >= rightFace && OverlapsPaddle(_rightY))
        {
            _ballX = rightFace;
            Bounce(_rightY, direction: -1);
        }

        if (_ballX + GameRules.BallSize < 0)
        {
            _scoreRight++;
            EndRally(serveDirection: -1);
        }
        else if (_ballX > GameRules.FieldWidth)
        {
            _scoreLeft++;
            EndRally(serveDirection: 1);
        }
    }

    private bool OverlapsPaddle(double paddleY) =>
        _ballY + GameRules.BallSize >= paddleY && _ballY <= paddleY + GameRules.PaddleHeight;

    private void Bounce(double paddleY, int direction)
    {
        // Чим далі від центру ракетки влучив м'яч, тим крутіший кут відбиття.
        var ballCenter = _ballY + GameRules.BallSize / 2;
        var paddleCenter = paddleY + GameRules.PaddleHeight / 2;
        var offset = Math.Clamp((ballCenter - paddleCenter) / (GameRules.PaddleHeight / 2), -1, 1);
        var angle = offset * GameRules.MaxBounceAngle;
        _ballSpeed = Math.Min(_ballSpeed * GameRules.SpeedUpPerHit, GameRules.MaxBallSpeed);
        _ballVx = direction * _ballSpeed * Math.Cos(angle);
        _ballVy = _ballSpeed * Math.Sin(angle);
    }

    private void EndRally(int serveDirection)
    {
        // Подача летить у бік того, хто щойно пропустив.
        _serveDirection = serveDirection;
        BeginServe();
        if (_scoreLeft >= GameRules.WinScore || _scoreRight >= GameRules.WinScore)
        {
            _phase = GamePhase.GameOver;
        }
    }
}
