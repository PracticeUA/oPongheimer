namespace Pongheimer.Core.Game;

/// <summary>
/// Ядерний режим: бомби, що перетинають поле, і ефекти з них. Вміст бомби дістається тому, хто
/// останнім торкнувся м'яча, — хороший він чи поганий. У класичному режимі все тут вимкнено.
/// </summary>
public sealed partial class PongSimulation
{
    // Бомби з'являються в цих смугах по X: подалі від ракеток і від центру, де м'яч стоїть перед подачею.
    private static readonly (double From, double To)[] BombLanes = [(170, 330), (470, 630)];

    private readonly Bomb?[] _bombs = new Bomb?[NuclearRules.MaxBombs];
    private readonly SideState _leftEffects = new();
    private readonly SideState _rightEffects = new();

    private double _bombTimer = NuclearRules.FirstBombDelay;
    private TimedEffect _ballSizeEffect;
    private TimedEffect _ballSpeedEffect;
    private Detonation _lastDetonation;
    private NuclearSnapshot? _snapshot;

    private bool IsNuclear => _mode == GameMode.Nuclear;

    private double BallSpeedFactor => NuclearRules.BallSpeedFactor(_ballSpeedEffect);

    /// <summary>
    /// У скільки разів бомби зараз летять швидше за свою базову швидкість. Вони розганяються разом
    /// із м'ячем: що гарячіший розіграш, то менше часу, щоб у них поцілити. Поки м'яч стоїть
    /// перед подачею, бомби летять у звичайному темпі.
    /// </summary>
    private double BombPace => _phase == GamePhase.Playing
        ? Math.Max(1, _ballSpeed * BallSpeedFactor / GameRules.StartBallSpeed)
        : 1;

    /// <summary>Знімок для <see cref="State"/>; перебудовується лише після змін.</summary>
    private NuclearSnapshot? NuclearState
    {
        get
        {
            if (!IsNuclear)
            {
                return null;
            }

            return _snapshot ??= new NuclearSnapshot(
                new BombList(_bombs.OfType<Bomb>()),
                _ballSizeEffect,
                _ballSpeedEffect,
                _leftEffects.ToSnapshot(),
                _rightEffects.ToSnapshot(),
                _lastDetonation);
        }
    }

    /// <summary>Як ефекти зараз змінюють керування ракеткою цього боку.</summary>
    public PaddleModifiers ModifiersOf(Side side) => NuclearRules.ModifiersFor(NuclearState, side);

    /// <summary>Ставить бомбу на поле (центр і вертикальна швидкість); false, якщо вільного місця немає.</summary>
    internal bool SpawnBomb(double x, double y, double velocityY, BonusKind kind)
    {
        var slot = Array.IndexOf(_bombs, null);
        if (!IsNuclear || slot < 0)
        {
            return false;
        }

        _bombs[slot] = new Bomb(x, y, velocityY, kind);
        _snapshot = null;
        return true;
    }

    /// <summary>Застосовує вміст бомби так, ніби її щойно підірвали в точці (x, y).</summary>
    internal void ApplyBonus(BonusKind kind, Side owner, double x = 0, double y = 0)
    {
        if (!IsNuclear || kind == BonusKind.None)
        {
            return;
        }

        var side = EffectsOf(owner);
        var previousHeight = PaddleHeight(owner);
        var timed = new TimedEffect(kind, NuclearRules.DurationOf(kind));
        switch (kind)
        {
            case BonusKind.BigBall or BonusKind.SmallBall:
                _ballSizeEffect = timed;
                ApplyBallSize();
                break;

            case BonusKind.FastBall:
                _ballSpeedEffect = timed;
                break;

            case BonusKind.NormalSpeedBall:
                // Знімає і прискорення від бомби, і розгін, набраний ударами: м'яч знову як на подачі.
                _ballSpeedEffect = default;
                SetBallSpeed(GameRules.StartBallSpeed);
                break;

            case BonusKind.Shockwave:
                // Вибух відкидає м'яч у випадковий бік під випадковим кутом, швидкість та сама.
                var angle = (_random.NextDouble() * 2 - 1) * GameRules.MaxBounceAngle;
                var direction = _random.Next(2) == 0 ? -1 : 1;
                _ballVx = direction * _ballSpeed * Math.Cos(angle);
                _ballVy = _ballSpeed * Math.Sin(angle);
                break;

            case BonusKind.LongPaddle or BonusKind.ShortPaddle:
                side.Size = timed;
                ApplyPaddleHeight(owner, previousHeight);
                break;

            case BonusKind.FastPaddle or BonusKind.SlowPaddle:
                side.Speed = timed;
                break;

            case BonusKind.Shield:
                side.HasShield = true;
                break;

            case BonusKind.DoublePoint:
                side.HasDoublePoint = true;
                break;

            case BonusKind.Freeze:
                side.Freeze = timed;
                break;

            case BonusKind.Inversion:
                side.Inversion = timed;
                break;
        }

        var count = (byte)(_lastDetonation.Count % byte.MaxValue + 1);
        _lastDetonation = new Detonation(count, x, y, kind, owner);
        _snapshot = null;
    }

    private static bool Tick(ref TimedEffect effect, double dt)
    {
        if (!effect.IsActive)
        {
            return false;
        }

        var remaining = effect.Remaining - dt;
        effect = remaining > 0 ? effect with { Remaining = remaining } : default;
        return !effect.IsActive;
    }

    private SideState EffectsOf(Side side) => side == Side.Left ? _leftEffects : _rightEffects;

    private double PaddleHeight(Side side) => NuclearRules.PaddleHeight(EffectsOf(side).ToSnapshot());

    private void UpdateNuclear(double dt)
    {
        if (!IsNuclear || _phase == GamePhase.GameOver)
        {
            return;
        }

        _snapshot = null;

        if (Tick(ref _ballSizeEffect, dt))
        {
            ApplyBallSize();
        }

        Tick(ref _ballSpeedEffect, dt);
        TickSide(Side.Left, dt);
        TickSide(Side.Right, dt);

        var bombPace = BombPace;
        for (var i = 0; i < _bombs.Length; i++)
        {
            if (_bombs[i] is not { } bomb)
            {
                continue;
            }

            var y = bomb.Y + bomb.VelocityY * bombPace * dt;
            var left = y < -NuclearRules.BombRadius || y > GameRules.FieldHeight + NuclearRules.BombRadius;
            _bombs[i] = left ? null : bomb with { Y = y };
        }

        // Лічильник до наступної бомби цокає в тому ж темпі, що летять самі бомби:
        // на розігнаному м'ячі вони і швидші, і частіші, тож на полі їх не меншає.
        _bombTimer -= dt * bombPace;
        if (_bombTimer <= 0)
        {
            LaunchRandomBomb();
            _bombTimer = NuclearRules.MinBombInterval
                + _random.NextDouble() * (NuclearRules.MaxBombInterval - NuclearRules.MinBombInterval);
        }
    }

    private void TickSide(Side side, double dt)
    {
        var effects = EffectsOf(side);
        var previousHeight = PaddleHeight(side);
        if (Tick(ref effects.Size, dt))
        {
            ApplyPaddleHeight(side, previousHeight);
        }

        Tick(ref effects.Speed, dt);
        Tick(ref effects.Freeze, dt);
        Tick(ref effects.Inversion, dt);
    }

    private void LaunchRandomBomb()
    {
        var lane = BombLanes[_random.Next(BombLanes.Length)];
        var x = lane.From + _random.NextDouble() * (lane.To - lane.From);
        var kind = NuclearRules.AllBonuses[_random.Next(NuclearRules.AllBonuses.Count)];
        if (_random.Next(2) == 0)
        {
            SpawnBomb(x, -NuclearRules.BombRadius, NuclearRules.BombSpeed, kind);
        }
        else
        {
            SpawnBomb(x, GameRules.FieldHeight + NuclearRules.BombRadius, -NuclearRules.BombSpeed, kind);
        }
    }

    private void DetonateBombsHitByBall()
    {
        if (!IsNuclear || _phase != GamePhase.Playing)
        {
            return;
        }

        for (var i = 0; i < _bombs.Length; i++)
        {
            if (_bombs[i] is not { } bomb)
            {
                continue;
            }

            var ballRadius = _ballSize / 2;
            var dx = _ballX + ballRadius - bomb.X;
            var dy = _ballY + ballRadius - bomb.Y;
            var reach = ballRadius + NuclearRules.BombRadius;
            if (dx * dx + dy * dy <= reach * reach)
            {
                _bombs[i] = null;
                ApplyBonus(bomb.Kind, _lastHitter, bomb.X, bomb.Y);
            }
        }
    }

    /// <summary>Щит за ракеткою відбиває м'яч один раз замість гола.</summary>
    private bool TryBounceOffShield()
    {
        if (_ballVx < 0 && _ballX <= NuclearRules.ShieldInset && _leftEffects.HasShield)
        {
            _ballX = NuclearRules.ShieldInset;
            _leftEffects.HasShield = false;
            _lastHitter = Side.Left;
        }
        else if (_ballVx > 0
            && _ballX + _ballSize >= GameRules.FieldWidth - NuclearRules.ShieldInset
            && _rightEffects.HasShield)
        {
            _ballX = GameRules.FieldWidth - NuclearRules.ShieldInset - _ballSize;
            _rightEffects.HasShield = false;
            _lastHitter = Side.Right;
        }
        else
        {
            return false;
        }

        _ballVx = -_ballVx;
        _snapshot = null;
        return true;
    }

    private bool ConsumeDoublePoint(Side scorer)
    {
        var effects = EffectsOf(scorer);
        var had = effects.HasDoublePoint;
        effects.HasDoublePoint = false;
        return had;
    }

    /// <summary>Гол знімає всі ефекти з обох гравців і з м'яча; бомби лишаються летіти.</summary>
    private void ClearEffects()
    {
        var leftHeight = PaddleHeight(Side.Left);
        var rightHeight = PaddleHeight(Side.Right);
        _leftEffects.Clear();
        _rightEffects.Clear();
        _ballSizeEffect = default;
        _ballSpeedEffect = default;
        ApplyPaddleHeight(Side.Left, leftHeight);
        ApplyPaddleHeight(Side.Right, rightHeight);
        ApplyBallSize();
        _snapshot = null;
    }

    private void ResetNuclear()
    {
        ClearEffects();
        Array.Clear(_bombs);
        _bombTimer = NuclearRules.FirstBombDelay;
        _lastDetonation = default;
    }

    /// <summary>Міняє розмір м'яча, лишаючи його центр на місці.</summary>
    private void ApplyBallSize()
    {
        var size = NuclearRules.BallSize(_ballSizeEffect);
        var shift = (_ballSize - size) / 2;
        _ballSize = size;
        _ballX += shift;
        _ballY = Math.Clamp(_ballY + shift, 0, GameRules.FieldHeight - size);
    }

    /// <summary>Після зміни висоти ракетка лишається по центру там, де була.</summary>
    private void ApplyPaddleHeight(Side side, double previousHeight)
    {
        var shift = (previousHeight - PaddleHeight(side)) / 2;
        if (side == Side.Left)
        {
            _leftY = GameRules.ClampPaddle(_leftY + shift, PaddleHeight(side));
        }
        else
        {
            _rightY = GameRules.ClampPaddle(_rightY + shift, PaddleHeight(side));
        }
    }

    private void SetBallSpeed(double speed)
    {
        if (_ballSpeed > 0)
        {
            var scale = speed / _ballSpeed;
            _ballVx *= scale;
            _ballVy *= scale;
        }

        _ballSpeed = speed;
    }

    /// <summary>Змінний стан ефектів одного гравця.</summary>
    private sealed class SideState
    {
        public TimedEffect Size;
        public TimedEffect Speed;
        public TimedEffect Freeze;
        public TimedEffect Inversion;

        public bool HasShield { get; set; }

        public bool HasDoublePoint { get; set; }

        public SideEffects ToSnapshot() => new(Size, Speed, Freeze, Inversion, HasShield, HasDoublePoint);

        public void Clear()
        {
            Size = default;
            Speed = default;
            Freeze = default;
            Inversion = default;
            HasShield = false;
            HasDoublePoint = false;
        }
    }
}
