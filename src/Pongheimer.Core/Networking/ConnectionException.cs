namespace Pongheimer.Core.Networking;

/// <summary>Причина, з якої з'єднання не вдалося встановити.</summary>
public enum ConnectionError
{
    InvalidAddress,
    ConnectionRefused,
    HostUnreachable,
    PortInUse,
    ListenFailed,
    NotPongheimer,
    VersionMismatch,
}

public sealed class ConnectionException : Exception
{
    public ConnectionException(ConnectionError error, Exception? innerException = null)
        : base($"Connection failed: {error}", innerException)
    {
        Error = error;
    }

    public ConnectionError Error { get; }
}
