using Pongheimer.Core.Game;
using Pongheimer.Core.Networking;

namespace Pongheimer.Core.Sessions;

/// <summary>
/// Мережева партія на боці клієнта: показує стан від хоста, а власну (праву) ракетку
/// рухає локально, щоб керування не відчувало мережевої затримки.
/// </summary>
/// <remarks>
/// Пакети хоста й кадри клієнта не синхронні: в один кадр приходить два пакети, в інший — жодного.
/// Якби м'яч просто стрибав у позицію з останнього пакета, він сіпався б навіть в ідеальній мережі.
/// Тому клієнт сам веде м'яч зі швидкістю від хоста щокадру, а пакети лише м'яко його підправляють.
/// </remarks>
public sealed class ClientSession : IGameSession
{
    private const double MaxFrameTime = 0.1;

    // Частка розбіжності з хостом, яку прибираємо за один пакет: досить, щоб не відставати,
    // і замало, щоб корекцію було видно.
    private const double CorrectionGain = 0.15;

    // Більша розбіжність — уже не тремтіння доставки, а щось пропущене; тоді чесно стрибаємо.
    private const double SnapDistance = 48;

    private readonly IPeerConnection _connection;
    private double _paddleY = GameRules.CenteredPaddleY;
    private double _paddleHeight = GameRules.PaddleHeight;
    private byte _pauseToggles;
    private StateMessage? _lastSnapshot;
    private double _ballX = GameState.Initial.BallX;
    private double _ballY = GameState.Initial.BallY;
    private double _velocityX;
    private double _velocityY;
    private bool _bouncedAheadOfHost;

    public ClientSession(IPeerConnection connection) => _connection = connection;

    public Side? LocalSide => Side.Right;

    public GameState State { get; private set; } = GameState.Initial;

    public ConnectionFault? Fault => _connection.Fault;

    public void Update(double dt, PlayerInput input)
    {
        var frameTime = double.IsFinite(dt) && dt > 0 ? Math.Min(dt, MaxFrameTime) : 0;
        var snapshot = _connection.LatestState;
        var hostState = snapshot?.State ?? State;

        MovePaddle(hostState, input.Direction, frameTime);

        if (snapshot is not null)
        {
            MoveBall(snapshot, frameTime);
        }

        State = hostState with { BallX = _ballX, BallY = _ballY, RightPaddleY = _paddleY };

        if (input.TogglePause)
        {
            // Переповнення байта безпечне: хост порівнює лічильник лише на рівність.
            unchecked
            {
                _pauseToggles++;
            }
        }

        var wantsRematch = input.Confirm && State.Phase == GamePhase.GameOver;
        _connection.Send(new InputMessage(_paddleY, wantsRematch, _pauseToggles));
    }

    public void Dispose() => _connection.Dispose();

    private static bool IsBallMoving(StateMessage snapshot) =>
        snapshot.State.Phase == GamePhase.Playing && !snapshot.State.IsPaused;

    /// <summary>
    /// Власна ракетка рухається тут, а не на хості, тому ефекти ядерного режиму клієнт
    /// застосовує сам — за тими самими правилами, що й симуляція хоста.
    /// </summary>
    private void MovePaddle(GameState hostState, int direction, double frameTime)
    {
        // Ракетка змінила висоту — лишаємо її центр на місці, як це робить хост.
        var height = hostState.RightPaddleHeight;
        _paddleY += (_paddleHeight - height) / 2;
        _paddleHeight = height;

        var modifiers = NuclearRules.ModifiersFor(hostState.Nuclear, Side.Right);
        if (!hostState.IsPaused && !modifiers.IsFrozen)
        {
            var step = direction * GameRules.PaddleSpeed * modifiers.SpeedFactor * frameTime;
            _paddleY += modifiers.IsInverted ? -step : step;
        }

        _paddleY = GameRules.ClampPaddle(_paddleY, height);
    }

    private void MoveBall(StateMessage snapshot, double frameTime)
    {
        var previous = _lastSnapshot;
        var isNew = !ReferenceEquals(snapshot, previous);
        _lastSnapshot = snapshot;

        // М'яч стоїть (подача, пауза, кінець гри) або щойно рушив — показуємо рівно те, що каже хост.
        // Те саме, коли бомба змінила розмір м'яча: його кут зсунувся, і прогноз за старим розміром хибний.
        if (!IsBallMoving(snapshot) || previous is null || !IsBallMoving(previous)
            || snapshot.State.BallSize != previous.State.BallSize)
        {
            SnapBallTo(snapshot);
            return;
        }

        _ballX += _velocityX * frameTime;
        _ballY += _velocityY * frameTime;

        // Від стіни прогноз відбивається сам, не чекаючи хоста: правило просте й однозначне.
        var maxBallY = GameRules.FieldHeight - snapshot.State.BallSize;
        if (_ballY < 0 || _ballY > maxBallY)
        {
            _ballY = Math.Clamp(_ballY < 0 ? -_ballY : 2 * maxBallY - _ballY, 0, maxBallY);
            _velocityY = -_velocityY;
            _bouncedAheadOfHost = true;
        }

        if (!isNew)
        {
            return;
        }

        var sameVelocity = snapshot.BallVelocityX == _velocityX && snapshot.BallVelocityY == _velocityY;
        if (_bouncedAheadOfHost && !sameVelocity
            && snapshot.BallVelocityX == _velocityX && snapshot.BallVelocityY == -_velocityY)
        {
            // Хост ще не дійшов до стіни, від якої прогноз уже відбився: його позиція поки «з того боку»
            // відбиття, тож порівнювати з нею нема сенсу. Чекаємо, доки він підтвердить відбиття.
            return;
        }

        var errorX = snapshot.State.BallX - _ballX;
        var errorY = snapshot.State.BallY - _ballY;
        if (!sameVelocity || Math.Sqrt(errorX * errorX + errorY * errorY) > SnapDistance)
        {
            // Швидкість змінюється лише на відбитті; ракетку хост бачить краще за прогноз.
            SnapBallTo(snapshot);
            return;
        }

        _bouncedAheadOfHost = false;

        // Розбіжність через тремтіння доставки лежить уздовж траєкторії («трохи раніше чи пізніше»),
        // тому й підправляємо лише вздовж неї: м'яч ніколи не смикається вбік чи назад.
        var speed = Math.Sqrt(_velocityX * _velocityX + _velocityY * _velocityY);
        if (speed > 0)
        {
            var directionX = _velocityX / speed;
            var directionY = _velocityY / speed;
            // Корекція завжди менша за власний рух м'яча за кадр, тож назад він не відкочується.
            var limit = speed * frameTime * 0.5;
            var alongTrajectory = Math.Clamp(
                (errorX * directionX + errorY * directionY) * CorrectionGain,
                -limit,
                limit);
            _ballX += directionX * alongTrajectory;
            _ballY += directionY * alongTrajectory;
        }
    }

    private void SnapBallTo(StateMessage snapshot)
    {
        _ballX = snapshot.State.BallX;
        _ballY = snapshot.State.BallY;
        _velocityX = snapshot.BallVelocityX;
        _velocityY = snapshot.BallVelocityY;
        _bouncedAheadOfHost = false;
    }
}
