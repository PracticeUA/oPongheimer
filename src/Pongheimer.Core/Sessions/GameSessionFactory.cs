using Pongheimer.Core.Game;
using Pongheimer.Core.Networking;

namespace Pongheimer.Core.Sessions;

public interface IGameSessionFactory
{
    int Port { get; }

    IReadOnlyList<string> LocalAddresses { get; }

    IGameSession CreateSolo(BotDifficulty difficulty, GameMode mode);

    IGameSession CreateLocalDuel(GameMode mode);

    /// <exception cref="ConnectionException">Не вдалося створити гру.</exception>
    Task<IGameSession> HostAsync(GameMode mode, CancellationToken cancellationToken);

    /// <summary>Підключається до хоста; режим гри вибирає хост.</summary>
    /// <exception cref="ConnectionException">Не вдалося підключитись до хоста.</exception>
    Task<IGameSession> JoinAsync(string host, CancellationToken cancellationToken);
}

public sealed class GameSessionFactory : IGameSessionFactory
{
    private readonly IConnectionService _connections;

    public GameSessionFactory(IConnectionService connections) => _connections = connections;

    public int Port => _connections.Port;

    public IReadOnlyList<string> LocalAddresses => _connections.GetLocalAddresses();

    public IGameSession CreateSolo(BotDifficulty difficulty, GameMode mode) =>
        new SoloSession(difficulty, mode: mode);

    public IGameSession CreateLocalDuel(GameMode mode) => new LocalDuelSession(mode: mode);

    public async Task<IGameSession> HostAsync(GameMode mode, CancellationToken cancellationToken) =>
        new HostSession(await _connections.HostAsync(cancellationToken).ConfigureAwait(false), mode: mode);

    public async Task<IGameSession> JoinAsync(string host, CancellationToken cancellationToken) =>
        new ClientSession(await _connections.JoinAsync(host, cancellationToken).ConfigureAwait(false));
}
