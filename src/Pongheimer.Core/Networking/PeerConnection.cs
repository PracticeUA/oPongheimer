using System.Net.Sockets;
using System.Threading.Channels;
using Pongheimer.Core.Diagnostics;

namespace Pongheimer.Core.Networking;

/// <summary>
/// TCP-реалізація <see cref="IPeerConnection"/>. Читання й запис ідуть у фонових задачах,
/// тож повільна мережа ніколи не гальмує ігровий цикл.
/// </summary>
public sealed class PeerConnection : IPeerConnection
{
    public static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(5);

    private const int NoFault = -1;

    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly ILog _log;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Timer _pingTimer;

    // Кожен кадр — повний знімок, тому за переповнення викидаємо найстаріші.
    private readonly Channel<byte[]> _outbox = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(8) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    private volatile StateMessage? _latestState;
    private volatile InputMessage? _latestInput;
    private long _lastReceivedTicks = Environment.TickCount64;
    private int _fault = NoFault;

    private PeerConnection(TcpClient client, NetworkStream stream, ILog log)
    {
        _client = client;
        _stream = stream;
        _log = log;

        // Пінг не залежить від ігрового циклу: згорнуте вікно не виглядає як обрив зв'язку.
        _pingTimer = new Timer(_ => _outbox.Writer.TryWrite(Protocol.PingFrame), null, PingInterval, PingInterval);
        _ = Task.Run(ReceiveLoopAsync);
        _ = Task.Run(SendLoopAsync);
    }

    public StateMessage? LatestState => _latestState;

    public InputMessage? LatestInput => _latestInput;

    public ConnectionFault? Fault
    {
        get
        {
            var idle = Environment.TickCount64 - Interlocked.Read(ref _lastReceivedTicks);
            if (idle > IdleTimeout.TotalMilliseconds)
            {
                Fail(ConnectionFault.TimedOut);
            }

            var fault = Volatile.Read(ref _fault);
            return fault == NoFault ? null : (ConnectionFault)fault;
        }
    }

    /// <summary>
    /// Обмінюється привітаннями й перевіряє, що на тому боці сумісна версія гри.
    /// За будь-якої невдачі закриває <paramref name="client"/>.
    /// </summary>
    /// <exception cref="ConnectionException">Співрозмовник не є сумісним oPongheimer.</exception>
    public static async Task<PeerConnection> EstablishAsync(TcpClient client, ILog log, CancellationToken cancellationToken)
    {
        try
        {
            client.NoDelay = true;
            var stream = client.GetStream();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(HandshakeTimeout);

            HelloResult result;
            try
            {
                var hello = Protocol.EncodeHello();
                await stream.WriteAsync(hello, timeout.Token).ConfigureAwait(false);
                await stream.ReadExactlyAsync(hello, timeout.Token).ConfigureAwait(false);
                result = Protocol.ValidateHello(hello);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                result = HelloResult.NotPongheimer;
            }
            catch (Exception ex) when (IsTransportFailure(ex))
            {
                result = HelloResult.NotPongheimer;
            }

            return result switch
            {
                HelloResult.Ok => new PeerConnection(client, stream, log),
                HelloResult.VersionMismatch => throw new ConnectionException(ConnectionError.VersionMismatch),
                _ => throw new ConnectionException(ConnectionError.NotPongheimer),
            };
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public void Send(StateMessage message) => Enqueue(Protocol.Encode(message));

    public void Send(InputMessage message) => Enqueue(Protocol.Encode(message));

    public void Dispose() => Fail(ConnectionFault.Closed);

    private static bool IsTransportFailure(Exception ex) =>
        ex is IOException or SocketException or ObjectDisposedException or InvalidOperationException;

    private void Enqueue(byte[] frame)
    {
        if (Volatile.Read(ref _fault) == NoFault)
        {
            _outbox.Writer.TryWrite(frame);
        }
    }

    private async Task ReceiveLoopAsync()
    {
        var header = new byte[1];
        var payload = new byte[Protocol.MaxPayloadSize];
        try
        {
            while (true)
            {
                await _stream.ReadExactlyAsync(header, _shutdown.Token).ConfigureAwait(false);
                var size = Protocol.PayloadSize(header[0]);
                if (size < 0)
                {
                    Fail(ConnectionFault.ProtocolViolation);
                    return;
                }

                await _stream.ReadExactlyAsync(payload.AsMemory(0, size), _shutdown.Token).ConfigureAwait(false);
                if (!Accept(header[0], payload.AsSpan(0, size)))
                {
                    Fail(ConnectionFault.ProtocolViolation);
                    return;
                }

                Interlocked.Exchange(ref _lastReceivedTicks, Environment.TickCount64);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            Fail(ConnectionFault.Closed);
        }
    }

    private bool Accept(byte type, ReadOnlySpan<byte> payload)
    {
        switch (type)
        {
            case Protocol.StateType when Protocol.TryDecodeState(payload, out var state):
                _latestState = state;
                return true;
            case Protocol.InputType when Protocol.TryDecodeInput(payload, out var input):
                _latestInput = input;
                return true;
            case Protocol.PingType:
                return true;
            default:
                // Повторне привітання чи пошкоджений кадр.
                return false;
        }
    }

    private async Task SendLoopAsync()
    {
        try
        {
            await foreach (var frame in _outbox.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
            {
                await _stream.WriteAsync(frame, _shutdown.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            Fail(ConnectionFault.Closed);
        }
    }

    private void Fail(ConnectionFault fault)
    {
        if (Interlocked.CompareExchange(ref _fault, (int)fault, NoFault) != NoFault)
        {
            return;
        }

        _log.Info($"Connection ended: {fault}");
        _pingTimer.Dispose();
        _shutdown.Cancel();
        _client.Dispose();
    }
}
