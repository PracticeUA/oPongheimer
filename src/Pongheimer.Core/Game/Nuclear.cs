namespace Pongheimer.Core.Game;

public enum GameMode : byte
{
    /// <summary>Звичайний Pong.</summary>
    Classic,

    /// <summary>Pong із бомбами, які м'яч підриває заради бонусів і вад.</summary>
    Nuclear,
}

/// <summary>Що лежить у бомбі. Числові значення йдуть у мережу — нові додавати лише в кінець.</summary>
public enum BonusKind : byte
{
    None = 0,
    BigBall,
    SmallBall,
    FastBall,
    NormalSpeedBall,
    Shockwave,
    LongPaddle,
    ShortPaddle,
    FastPaddle,
    SlowPaddle,
    Shield,
    DoublePoint,
    Freeze,
    Inversion,
}

public enum BonusCategory
{
    /// <summary>Допомагає тому, хто підірвав бомбу.</summary>
    Good,

    /// <summary>Шкодить тому, хто підірвав бомбу.</summary>
    Bad,

    /// <summary>Змінює м'яч — однаково для обох гравців.</summary>
    Ball,
}

/// <summary>Бомба на полі: координати центру й вертикальна швидкість.</summary>
public readonly record struct Bomb(double X, double Y, double VelocityY, BonusKind Kind);

/// <summary>
/// Бомби, що зараз на полі. Порівнюється за вмістом, а не за посиланням, тож два знімки з однаковими
/// бомбами рівні — на цьому тримаються і тести, і перевірка «чи змінився стан».
/// </summary>
public readonly struct BombList : IEquatable<BombList>, IReadOnlyList<Bomb>
{
    private readonly Bomb[]? _bombs;

    public BombList(IEnumerable<Bomb> bombs) => _bombs = bombs.ToArray();

    public static BombList Empty => default;

    public int Count => _bombs?.Length ?? 0;

    public Bomb this[int index] => (_bombs ?? [])[index];

    public static bool operator ==(BombList left, BombList right) => left.Equals(right);

    public static bool operator !=(BombList left, BombList right) => !left.Equals(right);

    public bool Equals(BombList other) => (_bombs ?? []).AsSpan().SequenceEqual(other._bombs ?? []);

    public override bool Equals(object? obj) => obj is BombList other && Equals(other);

    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var bomb in _bombs ?? [])
        {
            hash.Add(bomb);
        }

        return hash.ToHashCode();
    }

    public IEnumerator<Bomb> GetEnumerator() => ((IEnumerable<Bomb>)(_bombs ?? [])).GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Ефект, що діє обмежений час. <see cref="BonusKind.None"/> — ефекту немає.</summary>
public readonly record struct TimedEffect(BonusKind Kind, double Remaining)
{
    public bool IsActive => Kind != BonusKind.None && Remaining > 0;
}

/// <summary>Усе, що зараз діє на одного гравця.</summary>
public readonly record struct SideEffects(
    TimedEffect Size,
    TimedEffect Speed,
    TimedEffect Freeze,
    TimedEffect Inversion,
    bool HasShield,
    bool HasDoublePoint);

/// <summary>
/// Останній вибух бомби. <see cref="Count"/> зростає з кожним вибухом (1…255 по колу, 0 — ще не було),
/// тож клієнт помічає новий вибух, навіть якщо пакети губились чи дублювались.
/// </summary>
public readonly record struct Detonation(byte Count, double X, double Y, BonusKind Kind, Side Owner);

/// <summary>Як ефекти змінюють керування ракеткою.</summary>
public readonly record struct PaddleModifiers(double SpeedFactor, bool IsFrozen, bool IsInverted)
{
    public static PaddleModifiers None { get; } = new(1, false, false);
}

/// <summary>Незмінний знімок усього «ядерного» на полі: бомби, ефекти, останній вибух.</summary>
public sealed record NuclearSnapshot(
    BombList Bombs,
    TimedEffect BallSize,
    TimedEffect BallSpeed,
    SideEffects Left,
    SideEffects Right,
    Detonation LastDetonation)
{
    public static NuclearSnapshot Empty { get; } = new(BombList.Empty, default, default, default, default, default);

    public SideEffects For(Side side) => side == Side.Left ? Left : Right;
}

/// <summary>
/// Числа й правила ядерного режиму. Ними користуються і симуляція хоста, і клієнт, який сам рухає
/// свою ракетку та м'яч між пакетами, — тому обидва боки рахують однаково.
/// </summary>
public static class NuclearRules
{
    public const double EffectDuration = 8;
    public const double FreezeDuration = 1;

    public const double BombRadius = 13;
    public const double BombSpeed = 70;
    public const int MaxBombs = 5;
    public const double FirstBombDelay = 1;
    public const double MinBombInterval = 1.5;
    public const double MaxBombInterval = 2.5;

    public const double BigBallSize = 30;
    public const double SmallBallSize = 7;
    public const double FastBallFactor = 1.4;

    public const double LongPaddleHeight = 120;
    public const double ShortPaddleHeight = 48;
    public const double FastPaddleFactor = 1.5;
    public const double SlowPaddleFactor = 0.6;

    /// <summary>Наскільки щит відступає від краю поля.</summary>
    public const double ShieldInset = 6;

    public static IReadOnlyList<BonusKind> AllBonuses { get; } =
        Enum.GetValues<BonusKind>().Where(kind => kind != BonusKind.None).ToArray();

    public static BonusCategory CategoryOf(BonusKind kind) => kind switch
    {
        BonusKind.LongPaddle or BonusKind.FastPaddle or BonusKind.Shield or BonusKind.DoublePoint => BonusCategory.Good,
        BonusKind.ShortPaddle or BonusKind.SlowPaddle or BonusKind.Freeze or BonusKind.Inversion => BonusCategory.Bad,
        _ => BonusCategory.Ball,
    };

    /// <summary>Скільки триває ефект; 0 — він миттєвий або діє до першого спрацювання.</summary>
    public static double DurationOf(BonusKind kind) => kind switch
    {
        BonusKind.Freeze => FreezeDuration,
        BonusKind.NormalSpeedBall or BonusKind.Shockwave or BonusKind.Shield or BonusKind.DoublePoint => 0,
        BonusKind.None => 0,
        _ => EffectDuration,
    };

    public static double BallSize(TimedEffect effect) => effect switch
    {
        { IsActive: true, Kind: BonusKind.BigBall } => BigBallSize,
        { IsActive: true, Kind: BonusKind.SmallBall } => SmallBallSize,
        _ => GameRules.BallSize,
    };

    public static double BallSpeedFactor(TimedEffect effect) =>
        effect is { IsActive: true, Kind: BonusKind.FastBall } ? FastBallFactor : 1;

    public static double PaddleHeight(SideEffects effects) => effects.Size switch
    {
        { IsActive: true, Kind: BonusKind.LongPaddle } => LongPaddleHeight,
        { IsActive: true, Kind: BonusKind.ShortPaddle } => ShortPaddleHeight,
        _ => GameRules.PaddleHeight,
    };

    public static PaddleModifiers ModifiersFor(NuclearSnapshot? snapshot, Side side)
    {
        if (snapshot is null)
        {
            return PaddleModifiers.None;
        }

        var effects = snapshot.For(side);
        var speedFactor = effects.Speed switch
        {
            { IsActive: true, Kind: BonusKind.FastPaddle } => FastPaddleFactor,
            { IsActive: true, Kind: BonusKind.SlowPaddle } => SlowPaddleFactor,
            _ => 1,
        };
        return new PaddleModifiers(speedFactor, effects.Freeze.IsActive, effects.Inversion.IsActive);
    }
}
