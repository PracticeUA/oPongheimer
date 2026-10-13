using Pongheimer.Core.Game;

namespace Pongheimer.Core.Sessions;

/// <summary>Партія вдвох за однією клавіатурою: лівий гравець — W/S, правий — стрілки.</summary>
public sealed class LocalDuelSession : AuthoritativeSession
{
    public LocalDuelSession(Random? random = null, GameMode mode = GameMode.Classic)
        : base(new PongSimulation(random, mode))
    {
    }

    public override Side? LocalSide => null;

    protected override int LeftPaddleDirection(PlayerInput input) => input.LeftHand;

    protected override void UpdateRightPaddle(double dt, PlayerInput input) =>
        Simulation.MovePaddle(Side.Right, input.RightHand, dt);
}
