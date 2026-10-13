using Pongheimer.Core.Game;
using Pongheimer.Core.Networking;
using Pongheimer.Core.Sessions;

namespace Pongheimer.Core.Tests;

/// <summary>Плавність м'яча на клієнті, коли пакети хоста приходять не в такт із кадрами.</summary>
public class ClientSmoothingTests
{
    private const double Frame = 1.0 / 60;
    private static readonly PlayerInput Idle = new(0, 0, false);

    [Fact]
    public void Ball_KeepsMovingOnFramesWithoutNewPacket()
    {
        var connection = new StubConnection { LatestState = Playing(x: 100, y: 100, vx: 300, vy: 60) };
        var session = new ClientSession(connection);
        connection.LatestState = Playing(x: 105, y: 101, vx: 300, vy: 60);
        session.Update(Frame, Idle);
        session.Update(Frame, Idle);
        var before = session.State;

        // Три кадри поспіль хост «мовчить» — м'яч однаково летить далі своєю траєкторією.
        for (var i = 1; i <= 3; i++)
        {
            session.Update(Frame, Idle);
            Assert.Equal(before.BallX + 300 * Frame * i, session.State.BallX, precision: 6);
            Assert.Equal(before.BallY + 60 * Frame * i, session.State.BallY, precision: 6);
        }
    }

    [Fact]
    public void Ball_MovesEvenlyDespiteJitteredPacketArrival()
    {
        // Хост рівно шле позицію щокадру, але до клієнта пакети доходять нерівно:
        // то жодного за кадр, то одразу два (тоді видно лише останній).
        const double speed = 600;
        var connection = new StubConnection();
        var session = new ClientSession(connection);
        var random = new Random(11);
        var hostFrame = 0;
        var steps = new List<double>();
        double? previousX = null;

        for (var frame = 0; frame < 300; frame++)
        {
            var arrived = random.Next(3); // 0, 1 або 2 пакети за кадр; у середньому — один
            if (arrived > 0)
            {
                hostFrame += arrived;
                connection.LatestState = Playing(x: 50 + hostFrame * speed * Frame % 600, y: 200, vx: speed, vy: 0);
            }

            session.Update(Frame, Idle);
            var x = session.State.BallX;
            if (previousX is { } last && x > last)
            {
                steps.Add(x - last);
            }

            previousX = x;
        }

        // Без згладжування крок за кадр був би 0, 10 або 20 px. Зі згладжуванням він тримається
        // біля рівних 10 px і ніколи не дорівнює нулю (зворотні стрибки тут — лише перенос координати).
        var expected = speed * Frame;
        Assert.All(steps.Skip(5), step => Assert.InRange(step, expected * 0.5, expected * 1.5));
    }

    [Fact]
    public void Ball_NeverStepsBackwardsWhenPacketsArriveLate()
    {
        var connection = new StubConnection { LatestState = Playing(x: 100, y: 200, vx: 420, vy: 0) };
        var session = new ClientSession(connection);
        session.Update(Frame, Idle);
        connection.LatestState = Playing(x: 107, y: 200, vx: 420, vy: 0);
        session.Update(Frame, Idle);
        var last = session.State.BallX;

        for (var i = 0; i < 50; i++)
        {
            // Клієнт уже «попереду» хоста: кожен пакет каже, що м'яч мав би бути на 30 px позаду.
            connection.LatestState = Playing(x: session.State.BallX - 30, y: 200, vx: 420, vy: 0);
            session.Update(Frame, Idle);
            Assert.True(session.State.BallX > last, $"frame {i}: {session.State.BallX} <= {last}");
            last = session.State.BallX;
        }
    }

    [Fact]
    public void Bounce_IsShownExactlyWhereHostReportsIt()
    {
        var connection = new StubConnection { LatestState = Playing(x: 60, y: 200, vx: -500, vy: 0) };
        var session = new ClientSession(connection);
        session.Update(Frame, Idle);
        connection.LatestState = Playing(x: 52, y: 200, vx: -500, vy: 0);
        session.Update(Frame, Idle);

        connection.LatestState = Playing(x: 36, y: 200, vx: 530, vy: 120);
        session.Update(Frame, Idle);

        Assert.Equal(36, session.State.BallX);
        Assert.Equal(200, session.State.BallY);
    }

    [Fact]
    public void LargeDisagreementWithHost_IsResolvedInHostsFavour()
    {
        var connection = new StubConnection { LatestState = Playing(x: 100, y: 100, vx: 300, vy: 0) };
        var session = new ClientSession(connection);
        session.Update(Frame, Idle);
        connection.LatestState = Playing(x: 105, y: 100, vx: 300, vy: 0);
        session.Update(Frame, Idle);

        connection.LatestState = Playing(x: 400, y: 300, vx: 300, vy: 0);
        session.Update(Frame, Idle);

        Assert.Equal(400, session.State.BallX);
        Assert.Equal(300, session.State.BallY);
    }

    [Fact]
    public void StandingBall_StaysExactlyWhereHostPutIt()
    {
        var serve = new StateMessage(GameState.Initial, 0, 0);
        var paused = new StateMessage(
            GameState.Initial with { Phase = GamePhase.Playing, IsPaused = true, BallX = 300 },
            500,
            100);
        var connection = new StubConnection { LatestState = serve };
        var session = new ClientSession(connection);

        for (var i = 0; i < 10; i++)
        {
            session.Update(Frame, Idle);
        }

        Assert.Equal(GameState.Initial.BallX, session.State.BallX);

        connection.LatestState = paused;
        for (var i = 0; i < 10; i++)
        {
            session.Update(Frame, Idle);
        }

        Assert.Equal(300, session.State.BallX);
    }

    [Fact]
    public void PredictedBall_StaysInsideFieldVertically()
    {
        var connection = new StubConnection { LatestState = Playing(x: 400, y: 5, vx: 100, vy: -600) };
        var session = new ClientSession(connection);
        session.Update(Frame, Idle);
        connection.LatestState = Playing(x: 401, y: 3, vx: 100, vy: -600);

        for (var i = 0; i < 20; i++)
        {
            session.Update(Frame, Idle);
            Assert.InRange(session.State.BallY, 0, GameRules.FieldHeight - GameRules.BallSize);
        }
    }

    [Fact]
    public void SmoothedBall_ProducesExactlyOneHitSoundPerBounce()
    {
        // Справжній матч «хост — клієнт» через нерівну доставку: звукові події на клієнті
        // мають збігатися з подіями на хості, без фантомних відбиттів від корекцій.
        var simulation = new PongSimulation(new Random(9));
        var connection = new StubConnection();
        var session = new ClientSession(connection);
        var hostEvents = new GameEventDetector();
        var clientEvents = new GameEventDetector();
        var random = new Random(4);
        int hostHits = 0, clientHits = 0, hostWalls = 0, clientWalls = 0;

        for (var frame = 0; frame < 60 * 120; frame++)
        {
            // Обидві ракетки стежать за м'ячем зі зміщенням, щоб він літав під різними кутами.
            var ballCenter = simulation.State.BallY + GameRules.BallSize / 2;
            var offset = Math.Sin(frame * 0.05) * 30;
            simulation.SetPaddle(Side.Left, ballCenter + offset - GameRules.PaddleHeight / 2);
            simulation.SetPaddle(Side.Right, ballCenter - offset - GameRules.PaddleHeight / 2);
            simulation.Step(Frame);
            var hostEvent = hostEvents.Observe(simulation.State);
            hostHits += hostEvent.PaddleHit ? 1 : 0;
            hostWalls += hostEvent.WallBounce ? 1 : 0;

            if (random.Next(3) > 0)
            {
                connection.LatestState = new StateMessage(
                    simulation.State,
                    simulation.BallVelocityX,
                    simulation.BallVelocityY);
            }

            session.Update(Frame, Idle);
            var clientEvent = clientEvents.Observe(session.State);
            clientHits += clientEvent.PaddleHit ? 1 : 0;
            clientWalls += clientEvent.WallBounce ? 1 : 0;
        }

        Assert.True(hostHits > 50, $"too few rallies to judge: {hostHits}");
        Assert.Equal(hostHits, clientHits);
        Assert.InRange(clientWalls, hostWalls - 2, hostWalls + 2);
    }

    private static StateMessage Playing(double x, double y, double vx, double vy) =>
        new(GameState.Initial with { Phase = GamePhase.Playing, BallX = x, BallY = y }, vx, vy);

    private sealed class StubConnection : IPeerConnection
    {
        public StateMessage? LatestState { get; set; }

        public InputMessage? LatestInput => null;

        public ConnectionFault? Fault => null;

        public void Send(StateMessage message)
        {
        }

        public void Send(InputMessage message)
        {
        }

        public void Dispose()
        {
        }
    }
}
