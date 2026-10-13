using System.Net;
using System.Net.Sockets;
using Pongheimer.Core.Diagnostics;
using Pongheimer.Core.Game;
using Pongheimer.Core.Networking;

namespace Pongheimer.Core.Tests;

/// <summary>Наскрізні тести мережі на справжніх сокетах через loopback.</summary>
public class NetworkTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task HostAndClient_ExchangeMessages()
    {
        var service = CreateService(out _);
        using var timeout = new CancellationTokenSource(TestTimeout);
        var hosting = service.HostAsync(timeout.Token);
        using var client = await service.JoinAsync("127.0.0.1", timeout.Token);
        using var host = await hosting;

        var state = new GameState(321, 123, 40, 80, 2, 5, GamePhase.Playing);
        var input = new InputMessage(55, RematchRequested: true);
        host.Send(new StateMessage(state));
        client.Send(input);

        await Eventually(() => client.LatestState?.State == state && host.LatestInput == input);
        Assert.Null(host.Fault);
        Assert.Null(client.Fault);
    }

    [Fact]
    public async Task Host_KeepsWaitingAfterStrangerConnects()
    {
        var service = CreateService(out var port);
        using var timeout = new CancellationTokenSource(TestTimeout);
        var hosting = service.HostAsync(timeout.Token);

        await SendAndWaitForClose(port, "GET / HTTP/1.1\r\n\r\n"u8.ToArray(), timeout.Token);
        await SendAndWaitForClose(port, Protocol.EncodeHello((byte)(Protocol.Version + 1)), timeout.Token);
        Assert.False(hosting.IsCompleted);

        using var client = await service.JoinAsync("localhost", timeout.Token);
        using var host = await hosting;
        Assert.Null(host.Fault);
    }

    [Fact]
    public async Task Host_DropsPeerThatBreaksProtocolAfterHandshake()
    {
        var service = CreateService(out var port);
        using var timeout = new CancellationTokenSource(TestTimeout);
        var hosting = service.HostAsync(timeout.Token);

        using var rogue = new TcpClient();
        await rogue.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
        await rogue.GetStream().WriteAsync(Protocol.EncodeHello(), timeout.Token);
        using var host = await hosting;
        Assert.Null(host.Fault);

        await rogue.GetStream().WriteAsync(new byte[] { 250, 1, 2, 3 }, timeout.Token);

        await Eventually(() => host.Fault == ConnectionFault.ProtocolViolation);
    }

    [Fact]
    public async Task Disconnect_IsReportedToTheOtherSide()
    {
        var service = CreateService(out _);
        using var timeout = new CancellationTokenSource(TestTimeout);
        var hosting = service.HostAsync(timeout.Token);
        var client = await service.JoinAsync("127.0.0.1", timeout.Token);
        using var host = await hosting;

        client.Dispose();

        await Eventually(() => host.Fault == ConnectionFault.Closed);
        Assert.Equal(ConnectionFault.Closed, client.Fault);

        // Надсилання в мертве з'єднання — тиха відсутність дії, а не виняток.
        host.Send(new StateMessage(GameState.Initial));
        client.Send(new InputMessage(0, false));
    }

    [Fact]
    public async Task Join_FailsCleanlyWhenNobodyIsHosting()
    {
        var service = CreateService(out _);

        var ex = await Assert.ThrowsAsync<ConnectionException>(
            () => service.JoinAsync("127.0.0.1", CancellationToken.None));

        Assert.Contains(ex.Error, new[] { ConnectionError.ConnectionRefused, ConnectionError.HostUnreachable });
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not an address!")]
    [InlineData("http://192.168.1.1/")]
    public async Task Join_RejectsMalformedAddress(string address)
    {
        var service = CreateService(out _);

        var ex = await Assert.ThrowsAsync<ConnectionException>(
            () => service.JoinAsync(address, CancellationToken.None));

        Assert.Equal(ConnectionError.InvalidAddress, ex.Error);
    }

    [Fact]
    public async Task Join_DetectsIncompatibleHost()
    {
        var service = CreateService(out var port);
        using var timeout = new CancellationTokenSource(TestTimeout);
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        try
        {
            var joining = service.JoinAsync("127.0.0.1", timeout.Token);
            using var server = await listener.AcceptTcpClientAsync(timeout.Token);
            await server.GetStream().WriteAsync(Protocol.EncodeHello((byte)(Protocol.Version + 1)), timeout.Token);

            var ex = await Assert.ThrowsAsync<ConnectionException>(() => joining);
            Assert.Equal(ConnectionError.VersionMismatch, ex.Error);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Host_ReportsBusyPortAndCanBeCancelled()
    {
        var service = CreateService(out _);
        using var cancel = new CancellationTokenSource();
        var hosting = service.HostAsync(cancel.Token);

        var ex = await Assert.ThrowsAsync<ConnectionException>(() => service.HostAsync(CancellationToken.None));
        Assert.Equal(ConnectionError.PortInUse, ex.Error);

        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hosting);

        // Після скасування порт вільний для нової спроби.
        using var retry = new CancellationTokenSource();
        var hostingAgain = service.HostAsync(retry.Token);
        Assert.False(hostingAgain.IsFaulted);
        retry.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hostingAgain);
    }

    private static TcpConnectionService CreateService(out int port)
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return new TcpConnectionService(NullLog.Instance, port, IPAddress.Loopback);
    }

    private static async Task SendAndWaitForClose(int port, byte[] data, CancellationToken cancellationToken)
    {
        using var stranger = new TcpClient();
        await stranger.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
        var stream = stranger.GetStream();
        await stream.WriteAsync(data, cancellationToken);
        try
        {
            var buffer = new byte[64];
            while (await stream.ReadAsync(buffer, cancellationToken) > 0)
            {
            }
        }
        catch (IOException)
        {
            // Хост обірвав з'єднання — саме цього й чекаємо.
        }
    }

    private static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition was not met in time");
            await Task.Delay(10);
        }
    }
}
