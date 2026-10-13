using Pongheimer.Core.Game;
using Pongheimer.Core.Networking;

namespace Pongheimer.Core.Sessions;

public interface IGameSessionFactory
{
    int Port { get; }

    IReadOnlyList<string> LocalAddresses { get; }

    IGameSession CreateSolo(BotDifficulty difficulty);

    IGameSession CreateLocalDuel();

    /// <exception cref="ConnectionException">Не вдалося створити гру.</exception>
    Task<IGameSession> HostAsync(CancellationToken cancellationToken);

    /// <exception cref="ConnectionException">Не вдалося підключитись до хоста.</exception>
    Task<IGameSession> JoinAsync(string host, CancellationToken cancellationToken);
}

public sealed class GameSessionFactory : IGameSessionFactory
{
    private readonly IConnectionService _connections;

    public GameSessionFactory(IConnectionService connections) => _connections = connections;

    public int Port => _connections.Port;

    public IReadOnlyList<string> LocalAddresses => _connections.GetLocalAddresses();

    public IGameSession CreateSolo(BotDifficulty difficulty) => new SoloSession(difficulty);

    public IGameSession CreateLocalDuel() => new LocalDuelSession();

    public async Task<IGameSession> HostAsync(CancellationToken cancellationToken) =>
        new HostSession(await _connections.HostAsync(cancellationToken).ConfigureAwait(false));

    public async Task<IGameSession> JoinAsync(string host, CancellationToken cancellationToken) =>
        new ClientSession(await _connections.JoinAsync(host, cancellationToken).ConfigureAwait(false));
}
