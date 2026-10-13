namespace Pongheimer.Core.Game;

/// <summary>
/// Правила та фізика Pong. Не знає ні про UI, ні про мережу, ні про те, хто керує ракетками.
/// Ця частина — спільна для обох режимів; бомби й ефекти ядерного режиму — у сусідньому файлі.
/// </summary>
public sealed partial class PongSimulation
{
    private readonly Random _random;
    private readonly GameMode _mode;

    private double _leftY = GameRules.CenteredPaddleY;
    private double _rightY = GameRules.CenteredPaddleY;
    private double _ballX;
    private double _ballY;
    private double _ballVx;
    private double _ballVy;
    private double _ballSpeed;
    private double _ballSize = GameRules.BallSize;
    private double _serveTimer;
    private int _serveDirection = 1;
    private int _scoreLeft;
    private int _scoreRight;
    private GamePhase _phase;
    private bool _isPaused;

    // Хто останнім торкнувся м'яча — йому дістається вміст підірваної бомби.
    private Side _lastHitter = Side.Left;

    public PongSimulation(Random? random = null, GameMode mode = GameMode.Classic)
    {
        _random = random ?? Random.Shared;
        _mode = mode;
        BeginServe();
    }

    public GameState State => new(
        _ballX,
        _ballY,
        _leftY,
        _rightY,
        _scoreLeft,
        _scoreRight,
        _phase,
        _isPaused,
        _ballSize,
        PaddleHeight(Side.Left),
        PaddleHeight(Side.Right),
        NuclearState);

    /// <summary>Справжня швидкість м'яча, з урахуванням ефектів.</summary>
    public double BallVelocityX => _ballVx * BallSpeedFactor;

    public double BallVelocityY => _ballVy * BallSpeedFactor;

    public void SetPaddle(Side side, double y)
    {
        if (!double.IsFinite(y))
        {
            return;
        }

        if (side == Side.Left)
        {
            _leftY = GameRules.ClampPaddle(y, PaddleHeight(Side.Left));
        }
        else
        {
            _rightY = GameRules.ClampPaddle(y, PaddleHeight(Side.Right));
        }
    }

    /// <summary>Рух ракетки від клавіш гравця; тут діють ефекти швидкості, заморозки та інверсії.</summary>
    public void MovePaddle(Side side, int direction, double dt)
    {
        var modifiers = ModifiersOf(side);
        if (modifiers.IsFrozen)
        {
            return;
        }

        var step = Math.Sign(direction) * GameRules.PaddleSpeed * modifiers.SpeedFactor * dt;
        var current = side == Side.Left ? _leftY : _rightY;
        SetPaddle(side, current + (modifiers.IsInverted ? -step : step));
    }

    public void Restart()
    {
        _scoreLeft = 0;
        _scoreRight = 0;
        _isPaused = false;
        ResetNuclear();
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

        UpdateNuclear(dt);

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
                DetonateBombsHitByBall();
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

        // М'яч летить від того, хто його «подав».
        _lastHitter = vx >= 0 ? Side.Left : Side.Right;
    }

    private void BeginServe()
    {
        _ballX = (GameRules.FieldWidth - _ballSize) / 2;
        _ballY = (GameRules.FieldHeight - _ballSize) / 2;
        _ballVx = 0;
        _ballVy = 0;
        _serveTimer = GameRules.ServeDelay;
        _phase = GamePhase.Serve;
    }

    private void MoveBall(double dt)
    {
        var previousX = _ballX;
        var speedFactor = BallSpeedFactor;
        _ballX += _ballVx * speedFactor * dt;
        _ballY += _ballVy * speedFactor * dt;

        var maxBallY = GameRules.FieldHeight - _ballSize;
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
        var rightFace = GameRules.RightPaddleX - _ballSize;
        if (_ballVx < 0 && previousX >= leftFace && _ballX <= leftFace && OverlapsPaddle(Side.Left))
        {
            _ballX = leftFace;
            Bounce(Side.Left, direction: 1);
        }
        else if (_ballVx > 0 && previousX <= rightFace && _ballX >= rightFace && OverlapsPaddle(Side.Right))
        {
            _ballX = rightFace;
            Bounce(Side.Right, direction: -1);
        }

        if (TryBounceOffShield())
        {
            return;
        }

        if (_ballX + _ballSize < 0)
        {
            Score(Side.Right);
        }
        else if (_ballX > GameRules.FieldWidth)
        {
            Score(Side.Left);
        }
    }

    private bool OverlapsPaddle(Side side)
    {
        var paddleY = side == Side.Left ? _leftY : _rightY;
        return _ballY + _ballSize >= paddleY && _ballY <= paddleY + PaddleHeight(side);
    }

    private void Bounce(Side side, int direction)
    {
        // Чим далі від центру ракетки влучив м'яч, тим крутіший кут відбиття.
        var paddleY = side == Side.Left ? _leftY : _rightY;
        var halfHeight = PaddleHeight(side) / 2;
        var ballCenter = _ballY + _ballSize / 2;
        var offset = Math.Clamp((ballCenter - (paddleY + halfHeight)) / halfHeight, -1, 1);
        var angle = offset * GameRules.MaxBounceAngle;
        _ballSpeed = Math.Min(_ballSpeed * GameRules.SpeedUpPerHit, GameRules.MaxBallSpeed);
        _ballVx = direction * _ballSpeed * Math.Cos(angle);
        _ballVy = _ballSpeed * Math.Sin(angle);
        _lastHitter = side;
    }

    private void Score(Side scorer)
    {
        var points = ConsumeDoublePoint(scorer) ? 2 : 1;
        if (scorer == Side.Left)
        {
            _scoreLeft = Math.Min(_scoreLeft + points, GameRules.WinScore);
        }
        else
        {
            _scoreRight = Math.Min(_scoreRight + points, GameRules.WinScore);
        }

        // Подача летить у бік того, хто щойно пропустив; «подавальник» — той, хто забив.
        _serveDirection = scorer == Side.Left ? 1 : -1;
        _lastHitter = scorer;
        ClearEffects();
        BeginServe();
        if (_scoreLeft >= GameRules.WinScore || _scoreRight >= GameRules.WinScore)
        {
            _phase = GamePhase.GameOver;
        }
    }
}
