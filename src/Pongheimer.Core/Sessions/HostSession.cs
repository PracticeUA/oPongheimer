using Pongheimer.Core.Game;
using Pongheimer.Core.Networking;

namespace Pongheimer.Core.Sessions;

/// <summary>Мережева партія на боці хоста: веде симуляцію й розсилає її стан клієнту.</summary>
public sealed class HostSession : AuthoritativeSession
{
    private readonly IPeerConnection _connection;
    private byte _seenPauseToggles;

    public HostSession(IPeerConnection connection, Random? random = null, GameMode mode = GameMode.Classic)
        : base(new PongSimulation(random, mode))
    {
        _connection = connection;
    }

    public override ConnectionFault? Fault => _connection.Fault;

    protected override bool OpponentWantsRematch => _connection.LatestInput?.RematchRequested == true;

    public override void Dispose() => _connection.Dispose();

    protected override void UpdateRightPaddle(double dt, PlayerInput input)
    {
        // Заморожену ракетку клієнт не рухає й сам, але хост на це не покладається.
        if (_connection.LatestInput is { } remote && !Simulation.ModifiersOf(Side.Right).IsFrozen)
        {
            Simulation.SetPaddle(Side.Right, remote.PaddleY);
        }
    }

    protected override bool ConsumeOpponentPauseToggle()
    {
        // Клієнт шле лічильник натискань, а не саму подію: загублений чи повторений пакет
        // не загубить і не продублює паузу.
        var toggles = _connection.LatestInput?.PauseToggles ?? _seenPauseToggles;
        if (toggles == _seenPauseToggles)
        {
            return false;
        }

        _seenPauseToggles = toggles;
        return true;
    }

    protected override void OnUpdated() =>
        _connection.Send(new StateMessage(State, Simulation.BallVelocityX, Simulation.BallVelocityY));
}
