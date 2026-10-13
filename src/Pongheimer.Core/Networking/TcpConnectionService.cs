using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Pongheimer.Core.Diagnostics;

namespace Pongheimer.Core.Networking;

public interface IConnectionService
{
    int Port { get; }

    IReadOnlyList<string> GetLocalAddresses();

    /// <summary>Чекає на суперника, доки він не підключиться або операцію не скасують.</summary>
    /// <exception cref="ConnectionException">Порт не вдалося відкрити.</exception>
    Task<IPeerConnection> HostAsync(CancellationToken cancellationToken);

    /// <exception cref="ConnectionException">До хоста не вдалося підключитись.</exception>
    Task<IPeerConnection> JoinAsync(string host, CancellationToken cancellationToken);
}

public sealed class TcpConnectionService : IConnectionService
{
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    private readonly ILog _log;
    private readonly IPAddress _bindAddress;

    public TcpConnectionService(ILog log, int port = Protocol.DefaultPort, IPAddress? bindAddress = null)
    {
        _log = log;
        Port = port;
        _bindAddress = bindAddress ?? IPAddress.Any;
    }

    public int Port { get; }

    public IReadOnlyList<string> GetLocalAddresses()
    {
        try
        {
            var addresses =
                from nic in NetworkInterface.GetAllNetworkInterfaces()
                where nic.OperationalStatus == OperationalStatus.Up
                    && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback
                from address in nic.GetIPProperties().UnicastAddresses
                where address.Address.AddressFamily == AddressFamily.InterNetwork
                select address.Address.ToString();
            return addresses.Distinct().ToList();
        }
        catch (NetworkInformationException ex)
        {
            _log.Warn($"Cannot enumerate network interfaces: {ex.Message}");
            return [];
        }
    }

    public async Task<IPeerConnection> HostAsync(CancellationToken cancellationToken)
    {
        var listener = new TcpListener(_bindAddress, Port);
        try
        {
            listener.Start();
        }
        catch (SocketException ex)
        {
            var error = ex.SocketErrorCode == SocketError.AddressAlreadyInUse
                ? ConnectionError.PortInUse
                : ConnectionError.ListenFailed;
            throw new ConnectionException(error, ex);
        }

        try
        {
            _log.Info($"Hosting on port {Port}");
            while (true)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                var remote = client.Client.RemoteEndPoint;
                try
                {
                    var connection = await PeerConnection.EstablishAsync(client, _log, cancellationToken)
                        .ConfigureAwait(false);
                    _log.Info($"Opponent connected from {remote}");
                    return connection;
                }
                catch (ConnectionException ex)
                {
                    // Сканер портів чи стара версія гри не мають зривати очікування справжнього суперника.
                    _log.Warn($"Rejected connection from {remote}: {ex.Error}");
                }
            }
        }
        catch (SocketException ex)
        {
            throw new ConnectionException(ConnectionError.ListenFailed, ex);
        }
        finally
        {
            listener.Stop();
        }
    }

    public async Task<IPeerConnection> JoinAsync(string host, CancellationToken cancellationToken)
    {
        host = host.Trim();
        if (host.Length == 0 || Uri.CheckHostName(host) == UriHostNameType.Unknown)
        {
            throw new ConnectionException(ConnectionError.InvalidAddress);
        }

        var client = new TcpClient();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ConnectTimeout);
            try
            {
                await client.ConnectAsync(host, Port, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ConnectionException(ConnectionError.HostUnreachable);
            }
            catch (SocketException ex)
            {
                var error = ex.SocketErrorCode switch
                {
                    SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain => ConnectionError.InvalidAddress,
                    SocketError.ConnectionRefused => ConnectionError.ConnectionRefused,
                    _ => ConnectionError.HostUnreachable,
                };
                throw new ConnectionException(error, ex);
            }
        }
        catch
        {
            client.Dispose();
            throw;
        }

        var connection = await PeerConnection.EstablishAsync(client, _log, cancellationToken).ConfigureAwait(false);
        _log.Info($"Connected to host {host}:{Port}");
        return connection;
    }
}
