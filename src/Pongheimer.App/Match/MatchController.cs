using System.Diagnostics;
using Microsoft.Xna.Framework.Input;
using Pongheimer.Audio;
using Pongheimer.Core.Diagnostics;
using Pongheimer.Core.Game;
using Pongheimer.Core.Sessions;
using Pongheimer.Infrastructure;
using Pongheimer.Input;
using Pongheimer.Localization;

namespace Pongheimer.Match;

/// <summary>Удар м'яча об ракетку, який ще показується на полі.</summary>
public sealed class Impact
{
    /// <summary>Скільки секунд видно іскри.</summary>
    public const double Lifetime = 0.32;

    public Impact(double x, double y, Side side)
    {
        X = x;
        Y = y;
        Side = side;
    }

    /// <summary>Центр м'яча в момент удару.</summary>
    public double X { get; }

    public double Y { get; }

    /// <summary>Чия ракетка відбила.</summary>
    public Side Side { get; }

    public double Age { get; set; }
}

/// <summary>Вибух бомби, який ще показується на полі.</summary>
public sealed class Blast
{
    /// <summary>Скільки секунд видно вибух і напис над ним.</summary>
    public const double Lifetime = 1.4;

    public Blast(Detonation detonation) => Detonation = detonation;

    public Detonation Detonation { get; }

    public double Age { get; set; }
}

/// <summary>
/// Ігровий цикл партії: щокадру передає ввід у <see cref="IGameSession"/>, запам'ятовує стан для
/// малювання, озвучує події й рахує плавність. Як саме малювати — справа <c>FieldRenderer</c>.
/// </summary>
public sealed class MatchController
{
    private readonly ISoundPlayer _sounds;
    private readonly ILog _log;
    private readonly GameEventDetector _events = new();
    private readonly FrameStats _stats = new();
    private readonly List<Blast> _blasts = [];
    private readonly List<Impact> _impacts = [];

    private IGameSession? _session;

    public MatchController(ISoundPlayer sounds, ILog log)
    {
        _sounds = sounds;
        _log = log;
    }

    /// <summary>Партія завершилась; аргумент — пояснення для гравця (може бути порожнім).</summary>
    public event Action<Func<Texts, string>>? Ended;

    public bool IsActive => _session is not null;

    /// <summary>Останній відомий стан; лишається на екрані під меню й після завершення партії.</summary>
    public GameState State { get; private set; } = GameState.Initial;

    /// <summary>Бік гравця за цією машиною або null, якщо за нею грають обидва.</summary>
    public Side? LocalSide { get; private set; } = Side.Left;

    /// <summary>Скільки секунд триває фінал гри — за цим годинником іде анімація вибуху.</summary>
    public double GameOverSeconds { get; private set; }

    /// <summary>Щойно підірвані бомби — для анімації маленьких вибухів і написів над ними.</summary>
    public IReadOnlyList<Blast> Blasts => _blasts;

    /// <summary>Щойні удари м'яча об ракетку — для іскор і спалаху ракетки.</summary>
    public IReadOnlyList<Impact> Impacts => _impacts;

    public bool IsPerformanceVisible { get; set; }

    /// <summary>Щосекундний звіт про плавність.</summary>
    public string PerformanceText { get; private set; } = "";

    public void Start(IGameSession session)
    {
        End(Texts.None);
        _session = session;
        _events.Reset();
        _blasts.Clear();
        _impacts.Clear();
        State = session.State;
        LocalSide = session.LocalSide;
        GameOverSeconds = 0;
    }

    public void Leave() => End(Texts.None);

    /// <summary>Примусово завершує партію після збою.</summary>
    public void Abort(Func<Texts, string> reason) => End(reason);

    /// <param name="dt">Секунди від попереднього кадру.</param>
    /// <param name="input">Ввід цього кадру; null, коли клавіатура зараз належить меню.</param>
    public void Update(double dt, InputState? input)
    {
        // Годинник вибуху цокає й під меню: гриб продовжує «дихати» на тлі.
        GameOverSeconds = State.Phase == GamePhase.GameOver ? GameOverSeconds + dt : 0;

        foreach (var blast in _blasts)
        {
            blast.Age += dt;
        }

        _blasts.RemoveAll(blast => blast.Age >= Blast.Lifetime);

        foreach (var impact in _impacts)
        {
            impact.Age += dt;
        }

        _impacts.RemoveAll(impact => impact.Age >= Impact.Lifetime);

        if (_session is not { } session)
        {
            return;
        }

        try
        {
            if (input is not null && input.WasPressed(Keys.Escape))
            {
                End(Texts.None);
                return;
            }

            var before = State;
            var started = Stopwatch.GetTimestamp();
            session.Update(dt, ReadInput(input));
            State = session.State;
            PlaySounds(_events.Observe(State), session.LocalSide);
            var updateMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            var ballInMotion = State.Phase == GamePhase.Playing && before.Phase == GamePhase.Playing
                && !State.IsPaused && !before.IsPaused;
            var ballStalled = ballInMotion && State.BallX == before.BallX && State.BallY == before.BallY;
            if (_stats.Record(dt, updateMilliseconds, ballInMotion, ballStalled) is { } report)
            {
                PerformanceText = report;
                if (IsPerformanceVisible)
                {
                    // У журнал — поза ігровим потоком, щоб саме вимірювання не гальмувало кадр.
                    _ = Task.Run(() => _log.Info("Perf: " + report));
                }
            }

            if (session.Fault is { } fault)
            {
                End(texts => texts.Describe(fault));
            }
        }
        catch (Exception ex)
        {
            // Збій у партії не має валити застосунок: повертаємо гравця в меню.
            _log.Error("Game loop failure", ex);
            End(texts => texts.UnexpectedError);
        }
    }

    public string Banner(Texts texts)
    {
        if (State.Winner is { } winner)
        {
            var result = LocalSide switch
            {
                null => winner == Side.Left ? texts.LeftPlayerWins : texts.RightPlayerWins,
                _ => winner == LocalSide ? texts.Victory : texts.Defeat,
            };
            return result + "\n" + texts.RematchHint;
        }

        return State.IsPaused ? texts.Paused + "\n" + texts.ResumeHint : "";
    }

    public string Hint(Texts texts) => LocalSide is null ? texts.DuelControlsHint : texts.ControlsHint;

    // Хто якими клавішами керує, вирішує партія: тут лише зчитуємо обидві «руки».
    private static PlayerInput ReadInput(InputState? input)
    {
        if (input is null)
        {
            return default;
        }

        return new PlayerInput(
            Direction(input.IsDown(Keys.W), input.IsDown(Keys.S)),
            Direction(input.IsDown(Keys.Up), input.IsDown(Keys.Down)),
            input.IsDown(Keys.Space),
            input.WasPressed(Keys.P) || input.WasPressed(Keys.Pause));
    }

    private static int Direction(bool up, bool down) => (down ? 1 : 0) - (up ? 1 : 0);

    // Звучить одна, найважливіша подія кадру: фінал гри важливіший за гол, гол — за відбиття.
    // Коли за машиною обидва гравці (localSide == null), «програшних» звуків немає: хтось із них завжди радіє.
    private void PlaySounds(GameEvents events, Side? localSide)
    {
        if (events.Winner is { } winner)
        {
            _sounds.Play(winner == (localSide ?? winner) ? GameSound.Victory : GameSound.Defeat);
        }
        else if (events.ScoredBy is { } scorer)
        {
            _sounds.Play(scorer == (localSide ?? scorer) ? GameSound.PointWon : GameSound.PointLost);
        }
        else if (events.Detonated is { } detonation)
        {
            _blasts.Add(new Blast(detonation));
            _sounds.Play(NuclearRules.CategoryOf(detonation.Kind) switch
            {
                BonusCategory.Good => GameSound.BombGood,
                BonusCategory.Bad => GameSound.BombBad,
                _ => GameSound.BombBall,
            });
        }
        else if (events.PaddleHit)
        {
            // Удар стався там, де м'яч зараз: він щойно відскочив від ракетки на своїй половині.
            var radius = State.BallSize / 2;
            var side = State.BallX + radius < GameRules.FieldWidth / 2 ? Side.Left : Side.Right;
            _impacts.Add(new Impact(State.BallX + radius, State.BallY + radius, side));
            _sounds.Play(GameSound.PaddleHit);
        }
        else if (events.WallBounce)
        {
            _sounds.Play(GameSound.WallBounce);
        }
    }

    private void End(Func<Texts, string> reason)
    {
        if (_session is not { } session)
        {
            return;
        }

        _session = null;
        try
        {
            session.Dispose();
        }
        catch (Exception ex)
        {
            _log.Error("Session dispose failure", ex);
        }

        Ended?.Invoke(reason);
    }
}
