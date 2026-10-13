using Pongheimer.Core.Game;
using Pongheimer.Core.Networking;
using Pongheimer.Core.Sessions;

namespace Pongheimer.Core.Tests;

public class SessionTests
{
    private const double Frame = 1.0 / 60;
    private static readonly PlayerInput Idle = new(0, 0, false);

    [Fact]
    public void Host_PublishesStateEveryUpdate()
    {
        var connection = new FakeConnection();
        var session = new HostSession(connection);

        session.Update(Frame, Idle);
        session.Update(Frame, Idle);

        Assert.Equal(2, connection.SentStates.Count);
        Assert.Equal(session.State, connection.SentStates[^1].State);
    }

    [Fact]
    public void Host_MovesOwnPaddleAndAppliesRemoteOne()
    {
        var connection = new FakeConnection { LatestInput = new InputMessage(12, false) };
        var session = new HostSession(connection);

        session.Update(Frame, new PlayerInput(1, 0, false));

        Assert.Equal(Side.Left, session.LocalSide);
        Assert.True(session.State.LeftPaddleY > GameRules.CenteredPaddleY);
        Assert.Equal(12, session.State.RightPaddleY);
    }

    [Fact]
    public void Host_RestartsFinishedGameWhenClientAsksForRematch()
    {
        var connection = new FakeConnection { LatestInput = new InputMessage(0, false) };
        var session = new HostSession(connection, new Random(7));
        PlayUntilGameOver(session);

        session.Update(Frame, Idle);
        Assert.Equal(GamePhase.GameOver, session.State.Phase);

        connection.LatestInput = new InputMessage(0, RematchRequested: true);
        session.Update(Frame, Idle);

        Assert.Equal(GamePhase.Serve, session.State.Phase);
        Assert.Equal(0, session.State.ScoreLeft + session.State.ScoreRight);
    }

    [Fact]
    public void Host_ReportsConnectionFaultAndClosesConnectionOnDispose()
    {
        var connection = new FakeConnection();
        var session = new HostSession(connection);
        Assert.Null(session.Fault);

        connection.Fault = ConnectionFault.TimedOut;
        Assert.Equal(ConnectionFault.TimedOut, session.Fault);

        session.Dispose();
        Assert.True(connection.IsDisposed);
    }

    [Fact]
    public void Client_ShowsHostStateButOwnsRightPaddle()
    {
        var hostState = new GameState(100, 50, 20, 999, 2, 1, GamePhase.Playing);
        var connection = new FakeConnection { LatestState = new StateMessage(hostState) };
        var session = new ClientSession(connection);

        session.Update(Frame, new PlayerInput(0, -1, false));

        Assert.Equal(Side.Right, session.LocalSide);
        var expectedPaddle = GameRules.CenteredPaddleY - GameRules.PaddleSpeed * Frame;
        Assert.Equal(hostState with { RightPaddleY = expectedPaddle }, session.State);
        Assert.Equal(new InputMessage(expectedPaddle, false), connection.SentInputs.Single());
    }

    [Fact]
    public void Client_AsksForRematchOnlyWhenGameIsOver()
    {
        var connection = new FakeConnection();
        var session = new ClientSession(connection);

        session.Update(Frame, new PlayerInput(0, 0, Confirm: true));
        Assert.False(connection.SentInputs[^1].RematchRequested);

        connection.LatestState = new StateMessage(
            GameState.Initial with { ScoreLeft = GameRules.WinScore, Phase = GamePhase.GameOver });
        session.Update(Frame, new PlayerInput(0, 0, Confirm: true));
        Assert.True(connection.SentInputs[^1].RematchRequested);
    }

    [Fact]
    public void Client_SurvivesMissingHostStateAndBadFrameTimes()
    {
        var connection = new FakeConnection();
        var session = new ClientSession(connection);

        session.Update(double.NaN, new PlayerInput(1, 0, false));
        session.Update(-5, new PlayerInput(1, 0, false));
        session.Update(1e9, new PlayerInput(1, 0, false));

        Assert.InRange(session.State.RightPaddleY, 0, GameRules.MaxPaddleY);
        Assert.Equal(GamePhase.Serve, session.State.Phase);
    }

    [Theory]
    [InlineData(BotDifficulty.Easy)]
    [InlineData(BotDifficulty.Medium)]
    [InlineData(BotDifficulty.Hard)]
    public void Solo_ManyGamesWithErraticInput_NeverLeavesValidState(BotDifficulty difficulty)
    {
        var random = new Random(42);
        var session = new SoloSession(difficulty, new Random(42));
        var finishedGames = 0;

        for (var frame = 0; frame < 200_000; frame++)
        {
            var gameOver = session.State.Phase == GamePhase.GameOver;
            finishedGames += gameOver ? 1 : 0;

            // Нерівні кадри, включно з фризами, нулем і сміттям.
            var dt = random.Next(20) switch
            {
                0 => 0.5,
                1 => 0,
                2 => double.NaN,
                _ => Frame * (0.5 + random.NextDouble()),
            };
            session.Update(dt, new PlayerInput(random.Next(-1, 2), random.Next(-1, 2), Confirm: gameOver));

            var state = session.State;
            Assert.InRange(state.BallY, 0, GameRules.FieldHeight - GameRules.BallSize);
            Assert.InRange(state.BallX, -GameRules.BallSize - 20, GameRules.FieldWidth + 20);
            Assert.InRange(state.LeftPaddleY, 0, GameRules.MaxPaddleY);
            Assert.InRange(state.RightPaddleY, 0, GameRules.MaxPaddleY);
            Assert.InRange(state.ScoreLeft, 0, GameRules.WinScore);
            Assert.InRange(state.ScoreRight, 0, GameRules.WinScore);
        }

        Assert.True(finishedGames > 3, $"Only {finishedGames} games finished");
    }

    [Fact]
    public void Pause_FreezesBallAndPaddlesUntilToggledAgain()
    {
        var session = new LocalDuelSession(new Random(1));
        var pause = new PlayerInput(0, 0, false, TogglePause: true);
        var moveBoth = new PlayerInput(1, -1, false);
        for (var t = 0.0; t < GameRules.ServeDelay + 0.1; t += Frame)
        {
            session.Update(Frame, Idle);
        }

        Assert.Equal(GamePhase.Playing, session.State.Phase);

        session.Update(Frame, pause);
        var frozen = session.State;
        Assert.True(frozen.IsPaused);

        for (var i = 0; i < 120; i++)
        {
            session.Update(Frame, moveBoth);
        }

        Assert.Equal(frozen, session.State);

        session.Update(Frame, pause);
        session.Update(Frame, moveBoth);
        Assert.False(session.State.IsPaused);
        Assert.NotEqual(frozen.BallX, session.State.BallX);
        Assert.NotEqual(frozen.LeftPaddleY, session.State.LeftPaddleY);
    }

    [Fact]
    public void Pause_HeldServeDoesNotStartWhilePaused()
    {
        var session = new SoloSession(BotDifficulty.Medium, new Random(1));

        session.Update(Frame, new PlayerInput(0, 0, false, TogglePause: true));
        for (var i = 0; i < 300; i++)
        {
            session.Update(Frame, Idle);
        }

        Assert.Equal(GamePhase.Serve, session.State.Phase);
    }

    [Fact]
    public void Pause_CannotBeSetOnFinishedGameAndIsClearedByRematch()
    {
        var session = new LocalDuelSession(new Random(3));
        PlayUntilGameOver(session);

        session.Update(Frame, new PlayerInput(0, 0, false, TogglePause: true));
        Assert.False(session.State.IsPaused);

        session.Update(Frame, new PlayerInput(0, 0, Confirm: true));
        session.Update(Frame, new PlayerInput(0, 0, false, TogglePause: true));
        Assert.True(session.State.IsPaused);
    }

    [Fact]
    public void Host_TogglesPauseOncePerClientPress_HoweverManyPacketsRepeatIt()
    {
        var connection = new FakeConnection { LatestInput = new InputMessage(0, false, PauseToggles: 0) };
        var session = new HostSession(connection);
        session.Update(Frame, Idle);
        Assert.False(session.State.IsPaused);

        // Той самий лічильник приходить багато кадрів поспіль — пауза вмикається один раз.
        connection.LatestInput = new InputMessage(0, false, PauseToggles: 1);
        for (var i = 0; i < 10; i++)
        {
            session.Update(Frame, Idle);
            Assert.True(session.State.IsPaused);
        }

        Assert.True(connection.SentStates[^1].State.IsPaused);

        connection.LatestInput = new InputMessage(0, false, PauseToggles: 2);
        session.Update(Frame, Idle);
        Assert.False(session.State.IsPaused);
    }

    [Fact]
    public void Host_IgnoresRemotePaddleWhilePaused()
    {
        var connection = new FakeConnection { LatestInput = new InputMessage(100, false) };
        var session = new HostSession(connection);
        session.Update(Frame, new PlayerInput(0, 0, false, TogglePause: true));
        var paddle = session.State.RightPaddleY;

        connection.LatestInput = new InputMessage(300, false);
        session.Update(Frame, Idle);

        Assert.Equal(paddle, session.State.RightPaddleY);
    }

    [Fact]
    public void Client_CountsPausePressesAndFreezesOwnPaddleWhileHostIsPaused()
    {
        var connection = new FakeConnection();
        var session = new ClientSession(connection);

        session.Update(Frame, new PlayerInput(0, 0, false, TogglePause: true));
        session.Update(Frame, Idle);
        Assert.Equal(1, connection.SentInputs[^1].PauseToggles);

        connection.LatestState = new StateMessage(GameState.Initial with { IsPaused = true });
        session.Update(Frame, new PlayerInput(1, 0, false));
        Assert.Equal(GameRules.CenteredPaddleY, session.State.RightPaddleY);
        Assert.True(session.State.IsPaused);

        session.Update(Frame, new PlayerInput(0, 0, false, TogglePause: true));
        Assert.Equal(2, connection.SentInputs[^1].PauseToggles);
    }

    [Theory]
    [InlineData(1, 0, 1)]
    [InlineData(0, -1, -1)]
    [InlineData(1, 1, 1)]
    [InlineData(-1, 1, 0)]
    public void SinglePlayer_CanSteerWithEitherHand(int leftHand, int rightHand, int expected)
    {
        Assert.Equal(expected, new PlayerInput(leftHand, rightHand, false).Direction);
    }

    [Fact]
    public void LocalDuel_GivesEachPlayerTheirOwnKeys()
    {
        var session = new LocalDuelSession();
        Assert.Null(session.LocalSide);

        session.Update(Frame, new PlayerInput(LeftHand: 1, RightHand: 0, false));
        Assert.True(session.State.LeftPaddleY > GameRules.CenteredPaddleY);
        Assert.Equal(GameRules.CenteredPaddleY, session.State.RightPaddleY);

        session.Update(Frame, new PlayerInput(LeftHand: 0, RightHand: -1, false));
        Assert.True(session.State.RightPaddleY < GameRules.CenteredPaddleY);
    }

    [Fact]
    public void LocalDuel_PlayersMoveIndependentlyAtTheSameTime()
    {
        var session = new LocalDuelSession();

        session.Update(Frame, new PlayerInput(LeftHand: -1, RightHand: 1, false));

        Assert.True(session.State.LeftPaddleY < GameRules.CenteredPaddleY);
        Assert.True(session.State.RightPaddleY > GameRules.CenteredPaddleY);
    }

    [Fact]
    public void LocalDuel_RestartsFinishedGameOnConfirm()
    {
        var session = new LocalDuelSession(new Random(3));
        PlayUntilGameOver(session);

        session.Update(Frame, new PlayerInput(0, 0, Confirm: true));

        Assert.Equal(GamePhase.Serve, session.State.Phase);
        Assert.Equal(0, session.State.ScoreLeft + session.State.ScoreRight);
    }

    private static void PlayUntilGameOver(IGameSession session)
    {
        for (var i = 0; i < 100_000 && session.State.Phase != GamePhase.GameOver; i++)
        {
            session.Update(0.1, Idle);
        }

        Assert.Equal(GamePhase.GameOver, session.State.Phase);
    }

    private sealed class FakeConnection : IPeerConnection
    {
        public List<StateMessage> SentStates { get; } = [];

        public List<InputMessage> SentInputs { get; } = [];

        public StateMessage? LatestState { get; set; }

        public InputMessage? LatestInput { get; set; }

        public ConnectionFault? Fault { get; set; }

        public bool IsDisposed { get; private set; }

        public void Send(StateMessage message) => SentStates.Add(message);

        public void Send(InputMessage message) => SentInputs.Add(message);

        public void Dispose() => IsDisposed = true;
    }
}
