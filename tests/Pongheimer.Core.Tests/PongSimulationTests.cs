using Pongheimer.Core.Game;

namespace Pongheimer.Core.Tests;

public class PongSimulationTests
{
    private const double Step = 1.0 / 120;
    private const double PaddleCenterBallY = GameRules.CenteredPaddleY + (GameRules.PaddleHeight - GameRules.BallSize) / 2;

    [Fact]
    public void Serve_StartsRallyAfterDelay()
    {
        var simulation = new PongSimulation(new Random(1));

        simulation.Step(GameRules.ServeDelay / 2);
        Assert.Equal(GamePhase.Serve, simulation.State.Phase);

        simulation.Step(GameRules.ServeDelay);
        Assert.Equal(GamePhase.Playing, simulation.State.Phase);
        Assert.NotEqual(0, simulation.BallVelocityX);
    }

    [Theory]
    [InlineData(5, -300)]
    [InlineData(GameRules.FieldHeight - GameRules.BallSize - 5, 300)]
    public void Ball_BouncesOffWalls(double startY, double velocityY)
    {
        var simulation = new PongSimulation();
        simulation.LaunchBall(400, startY, 0, velocityY);

        simulation.Step(0.05);

        Assert.Equal(-Math.Sign(velocityY), Math.Sign(simulation.BallVelocityY));
        Assert.InRange(simulation.State.BallY, 0, GameRules.FieldHeight - GameRules.BallSize);
    }

    [Fact]
    public void Paddle_ReturnsBallAndSpeedsItUp()
    {
        var simulation = new PongSimulation();
        simulation.LaunchBall(100, PaddleCenterBallY, -400, 0);

        Run(simulation, seconds: 0.5);

        Assert.True(simulation.BallVelocityX > 400);
        Assert.Equal(0, simulation.State.ScoreRight);
    }

    [Fact]
    public void Paddle_StopsBallAtMaxSpeedEvenWithCoarseSteps()
    {
        var simulation = new PongSimulation();
        simulation.LaunchBall(100, PaddleCenterBallY, -GameRules.MaxBallSpeed, 0);

        for (var i = 0; i < 5; i++)
        {
            simulation.Step(1.0 / 20);
        }

        Assert.True(simulation.BallVelocityX > 0);
        Assert.Equal(0, simulation.State.ScoreRight);
    }

    [Fact]
    public void Miss_GivesPointToOpponentAndServesTowardLoser()
    {
        var simulation = new PongSimulation(new Random(1));
        simulation.SetPaddle(Side.Left, 0);
        simulation.LaunchBall(100, 400, -400, 0);

        Run(simulation, seconds: 0.5);

        Assert.Equal(1, simulation.State.ScoreRight);
        Assert.Equal(GamePhase.Serve, simulation.State.Phase);

        simulation.Step(GameRules.ServeDelay + Step);
        Assert.True(simulation.BallVelocityX < 0);
    }

    [Fact]
    public void Game_EndsAtWinScoreAndRestartsClean()
    {
        var simulation = new PongSimulation();
        simulation.SetPaddle(Side.Left, 0);

        for (var point = 0; point < GameRules.WinScore; point++)
        {
            simulation.LaunchBall(100, 400, -400, 0);
            Run(simulation, seconds: 0.5);
        }

        Assert.Equal(GamePhase.GameOver, simulation.State.Phase);
        Assert.Equal(Side.Right, simulation.State.Winner);

        // Після завершення гри симуляція стоїть на місці.
        var frozen = simulation.State;
        Run(simulation, seconds: 2);
        Assert.Equal(frozen, simulation.State);

        simulation.Restart();
        Assert.Equal(GamePhase.Serve, simulation.State.Phase);
        Assert.Equal(0, simulation.State.ScoreRight);
        Assert.Null(simulation.State.Winner);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void SetPaddle_IgnoresNonFiniteValues(double value)
    {
        var simulation = new PongSimulation();

        simulation.SetPaddle(Side.Right, value);

        Assert.Equal(GameRules.CenteredPaddleY, simulation.State.RightPaddleY);
    }

    [Fact]
    public void Paddle_StaysInsideField()
    {
        var simulation = new PongSimulation();

        simulation.SetPaddle(Side.Left, -500);
        simulation.MovePaddle(Side.Right, 1, dt: 60);

        Assert.Equal(0, simulation.State.LeftPaddleY);
        Assert.Equal(GameRules.MaxPaddleY, simulation.State.RightPaddleY);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void Step_IgnoresInvalidTime(double dt)
    {
        var simulation = new PongSimulation();
        simulation.LaunchBall(400, 200, 300, 100);
        var before = simulation.State;

        simulation.Step(dt);

        Assert.Equal(before, simulation.State);
    }

    private static void Run(PongSimulation simulation, double seconds)
    {
        for (var t = 0.0; t < seconds; t += Step)
        {
            simulation.Step(Step);
        }
    }
}
