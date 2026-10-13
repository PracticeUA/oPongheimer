using Pongheimer.Core.Game;

namespace Pongheimer.Core.Tests;

public class BotPlayerTests
{
    private const double Step = 1.0 / 120;

    [Fact]
    public void HarderBot_ConcedesFewerPoints()
    {
        var easy = PointsConcededAgainstFlawlessOpponent(BotDifficulty.Easy);
        var medium = PointsConcededAgainstFlawlessOpponent(BotDifficulty.Medium);
        var hard = PointsConcededAgainstFlawlessOpponent(BotDifficulty.Hard);

        Assert.True(easy > medium && medium > hard, $"easy={easy}, medium={medium}, hard={hard}");
    }

    [Theory]
    [InlineData(BotDifficulty.Easy)]
    [InlineData(BotDifficulty.Medium)]
    [InlineData(BotDifficulty.Hard)]
    public void Bot_IsBeatableOnEveryDifficulty(BotDifficulty difficulty)
    {
        Assert.True(PointsConcededAgainstFlawlessOpponent(difficulty) > 0);
    }

    [Fact]
    public void UnknownDifficulty_FallsBackToMedium()
    {
        Assert.Equal(BotProfile.For(BotDifficulty.Medium), BotProfile.For((BotDifficulty)99));
    }

    /// <summary>
    /// Ліва ракетка ніколи не промахується й відбиває під різними кутами,
    /// тож кожне очко — це помилка бота.
    /// </summary>
    private static int PointsConcededAgainstFlawlessOpponent(BotDifficulty difficulty)
    {
        var simulation = new PongSimulation(new Random(5));
        var bot = new BotPlayer(BotProfile.For(difficulty), new Random(5));
        var conceded = 0;

        for (var t = 0.0; t < 1200; t += Step)
        {
            var state = simulation.State;
            if (state.Phase == GamePhase.GameOver)
            {
                conceded += state.ScoreLeft;
                simulation.Restart();
                continue;
            }

            var hitOffset = Math.Sin(t * 1.7) * GameRules.PaddleHeight * 0.4;
            var paddleCenter = state.BallY + GameRules.BallSize / 2 + hitOffset;
            simulation.SetPaddle(Side.Left, paddleCenter - GameRules.PaddleHeight / 2);
            bot.Update(simulation, Step);
            simulation.Step(Step);
        }

        return conceded + simulation.State.ScoreLeft;
    }
}
