using Pongheimer.Core.Game;

namespace Pongheimer.Core.Tests;

public class GameEventDetectorTests
{
    private const double Step = 1.0 / 120;
    private const double PaddleCenterBallY = GameRules.CenteredPaddleY + (GameRules.PaddleHeight - GameRules.BallSize) / 2;

    [Fact]
    public void PaddleHit_IsReportedOnce()
    {
        var simulation = new PongSimulation();
        simulation.LaunchBall(100, PaddleCenterBallY, -400, 0);

        var events = Run(simulation, seconds: 0.5);

        Assert.Equal(1, events.Count(e => e.PaddleHit));
        Assert.DoesNotContain(events, e => e.WallBounce || e.ScoredBy is not null || e.Winner is not null);
    }

    [Fact]
    public void WallBounce_IsReportedOnce()
    {
        var simulation = new PongSimulation();
        simulation.LaunchBall(400, 30, 100, -300);

        var events = Run(simulation, seconds: 0.5);

        Assert.Equal(1, events.Count(e => e.WallBounce));
        Assert.DoesNotContain(events, e => e.PaddleHit);
    }

    [Fact]
    public void PaddleHitThatAlsoTurnsBallVertically_IsNotMistakenForWall()
    {
        var simulation = new PongSimulation();

        // М'яч летить униз і влучає у верхній край ракетки — вона відбиває його вгору.
        simulation.LaunchBall(100, GameRules.CenteredPaddleY - 40, -400, 200);

        var events = Run(simulation, seconds: 0.3);

        Assert.Equal(1, events.Count(e => e.PaddleHit));
        Assert.DoesNotContain(events, e => e.WallBounce);
    }

    [Fact]
    public void Goal_IsReportedWithScorerAndWithoutFalseBounce()
    {
        var simulation = new PongSimulation(new Random(1));
        simulation.SetPaddle(Side.Left, 0);
        simulation.LaunchBall(100, 400, -400, 0);

        // Достатньо довго, щоб після гола пройшла ще й наступна подача.
        var events = Run(simulation, seconds: 0.5 + GameRules.ServeDelay);

        Assert.Equal(Side.Right, events.Single(e => e.ScoredBy is not null).ScoredBy);
        Assert.DoesNotContain(events, e => e.PaddleHit || e.WallBounce || e.Winner is not null);
    }

    [Fact]
    public void Winner_IsReportedOnceOnFinalPoint()
    {
        var simulation = new PongSimulation();
        simulation.SetPaddle(Side.Left, 0);
        var detector = new GameEventDetector();
        var events = new List<GameEvents>();

        for (var point = 0; point < GameRules.WinScore; point++)
        {
            simulation.LaunchBall(100, 400, -400, 0);
            events.AddRange(Run(simulation, seconds: 0.5, detector));
        }

        events.AddRange(Run(simulation, seconds: 1, detector));

        Assert.Equal(GameRules.WinScore, events.Count(e => e.ScoredBy == Side.Right));
        Assert.Equal(Side.Right, events.Single(e => e.Winner is not null).Winner);
    }

    [Fact]
    public void RepeatedState_DoesNotHideNextBounce()
    {
        // Клієнт малює частіше, ніж приходять стани: між справжніми кадрами трапляються повтори.
        var detector = new GameEventDetector();
        var state = GameState.Initial with { Phase = GamePhase.Playing, BallX = 300 };
        var seen = new List<GameEvents>();

        foreach (var x in new[] { 300.0, 280, 280, 260, 260, 270, 270, 290 })
        {
            seen.Add(detector.Observe(state with { BallX = x }));
        }

        Assert.Equal(1, seen.Count(e => e.PaddleHit));
    }

    [Fact]
    public void Rematch_ProducesNoEvents()
    {
        var detector = new GameEventDetector();
        var finished = GameState.Initial with { ScoreLeft = GameRules.WinScore, Phase = GamePhase.GameOver };

        detector.Observe(finished);
        var events = detector.Observe(GameState.Initial);

        Assert.Equal(default, events);
    }

    private static List<GameEvents> Run(PongSimulation simulation, double seconds, GameEventDetector? detector = null)
    {
        detector ??= new GameEventDetector();
        var events = new List<GameEvents> { detector.Observe(simulation.State) };
        for (var t = 0.0; t < seconds; t += Step)
        {
            simulation.Step(Step);
            events.Add(detector.Observe(simulation.State));
        }

        return events;
    }
}
