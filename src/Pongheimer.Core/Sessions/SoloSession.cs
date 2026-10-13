using Pongheimer.Core.Game;

namespace Pongheimer.Core.Sessions;

/// <summary>Партія проти бота.</summary>
public sealed class SoloSession : AuthoritativeSession
{
    private readonly BotPlayer _bot;

    public SoloSession(BotDifficulty difficulty, Random? random = null)
        : base(new PongSimulation(random))
    {
        _bot = new BotPlayer(BotProfile.For(difficulty), random);
    }

    protected override void UpdateRightPaddle(double dt, PlayerInput input) => _bot.Update(Simulation, dt);
}
