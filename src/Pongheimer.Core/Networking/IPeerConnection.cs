namespace Pongheimer.Core.Networking;

/// <summary>Причина, з якої встановлене з'єднання перестало працювати.</summary>
public enum ConnectionFault
{
    Closed,
    TimedOut,
    ProtocolViolation,
}

/// <summary>
/// Встановлене з'єднання із суперником. Нічого не блокує й не кидає винятків:
/// надсилання ставить кадр у чергу, прийняте читається як «останнє відоме»,
/// а про обрив повідомляє <see cref="Fault"/>.
/// </summary>
public interface IPeerConnection : IDisposable
{
    StateMessage? LatestState { get; }

    InputMessage? LatestInput { get; }

    ConnectionFault? Fault { get; }

    void Send(StateMessage message);

    void Send(InputMessage message);
}
