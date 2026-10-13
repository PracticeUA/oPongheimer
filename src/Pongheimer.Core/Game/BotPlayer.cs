namespace Pongheimer.Core.Game;

public enum BotDifficulty
{
    Easy,
    Medium,
    Hard,
}

/// <summary>
/// Характер бота. Він навмисно неідеальний на будь-якій складності: що повільніший, що гірше
/// цілиться і що пізніше реагує, то частіше помиляється.
/// </summary>
/// <param name="Speed">Швидкість ракетки, пікселів на секунду (у гравця — <see cref="GameRules.PaddleSpeed"/>).</param>
/// <param name="AimError">Найбільший промах повз центр ракетки, пікселів.</param>
/// <param name="ReactionX">Бот починає рухатись, коли м'яч перетнув цю координату X.</param>
public sealed record BotProfile(double Speed, double AimError, double ReactionX)
{
    public static BotProfile For(BotDifficulty difficulty) => difficulty switch
    {
        // Похибка більша за пів ракетки: іноді промахується навіть по повільному м'ячу.
        BotDifficulty.Easy => new BotProfile(250, GameRules.PaddleHeight * 0.6, GameRules.FieldWidth * 0.45),
        BotDifficulty.Hard => new BotProfile(500, GameRules.PaddleHeight * 0.2, GameRules.FieldWidth * 0.1),
        _ => new BotProfile(360, GameRules.PaddleHeight * 0.4, GameRules.FieldWidth * 0.3),
    };
}

/// <summary>
/// Керує правою ракеткою відповідно до свого <see cref="BotProfile"/>. Грає лише в м'яч: бомби
/// ядерного режиму не ловить і не обминає, а їхні ефекти (швидкість, заморозка, інверсія)
/// діють на нього так само, як на гравця.
/// </summary>
public sealed class BotPlayer
{
    private readonly BotProfile _profile;
    private readonly Random _random;
    private double _aim;
    private bool _wasIncoming;

    public BotPlayer(BotProfile profile, Random? random = null)
    {
        _profile = profile;
        _random = random ?? Random.Shared;
    }

    public void Update(PongSimulation simulation, double dt)
    {
        var state = simulation.State;
        var incoming = state.Phase == GamePhase.Playing && simulation.BallVelocityX > 0;
        if (incoming && !_wasIncoming)
        {
            // Нова точка прицілювання на кожен м'яч, що летить до бота.
            _aim = (_random.NextDouble() * 2 - 1) * _profile.AimError;
        }

        _wasIncoming = incoming;

        var modifiers = simulation.ModifiersOf(Side.Right);
        if (modifiers.IsFrozen)
        {
            return;
        }

        var chasing = incoming && state.BallX > _profile.ReactionX;
        var targetCenter = chasing
            ? state.BallY + state.BallSize / 2 + _aim
            : GameRules.FieldHeight / 2;
        var delta = targetCenter - (state.RightPaddleY + state.RightPaddleHeight / 2);
        var maxStep = _profile.Speed * modifiers.SpeedFactor * dt;
        var step = Math.Clamp(delta, -maxStep, maxStep);

        // Під інверсією бот «плутає» вгору й вниз так само, як плутав би гравець.
        simulation.SetPaddle(Side.Right, state.RightPaddleY + (modifiers.IsInverted ? -step : step));
    }
}
