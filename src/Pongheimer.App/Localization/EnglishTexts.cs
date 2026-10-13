using Pongheimer.Core.Game;
using Pongheimer.Core.Networking;

namespace Pongheimer.Localization;

public sealed class EnglishTexts : Texts
{
    public override string ModeClassic => "Classic";

    public override string ModeNuclear => "Nuclear";

    public override string HostGame => "Create game (host)";

    public override string PlayBot => "Play against bot";

    public override string BotEasy => "Easy";

    public override string BotMedium => "Medium";

    public override string BotHard => "Hard";

    public override string PlayLocalDuel => "Two players on this PC";

    public override string DuelControlsHint => "Left: W/S  ·  Right: ↑/↓  ·  P — pause  ·  Esc — menu";

    public override string LeftPlayerWins => "Left player wins!";

    public override string RightPlayerWins => "Right player wins!";

    public override string JoinPrompt => "or join by the host's IP:";

    public override string Join => "Join";

    public override string Cancel => "Cancel";

    public override string ControlsHint => "W/S or ↑/↓ — move  ·  P — pause  ·  Esc — menu";

    public override string Paused => "Paused";

    public override string ResumeHint => "P — resume";

    public override string Victory => "You win!";

    public override string Defeat => "You lose";

    public override string RematchHint => "Space — play again";

    public override string SoundOn => "Sound: on";

    public override string SoundOff => "Sound: off";

    public override string Connecting => "Connecting…";

    public override string UnexpectedError => "Something went wrong. See the log for details.";

    public override string StartupFailed => "oPongheimer could not start.";

    public override string WaitingForOpponent(IReadOnlyList<string> addresses, int port)
    {
        var ip = addresses.Count > 0 ? string.Join(", ", addresses) : "not found — check your network";
        return $"Waiting for an opponent…\nYour IP: {ip}\nPort: {port}";
    }

    public override string Describe(ConnectionError error, int port) => error switch
    {
        ConnectionError.InvalidAddress => "That doesn't look like an address. Enter the host's IP, e.g. 192.168.1.20.",
        ConnectionError.ConnectionRefused => "Nobody is waiting for a game at this address. Has the host clicked “Create game”?",
        ConnectionError.HostUnreachable => "The host is not responding. Check the IP, the network and the host's firewall.",
        ConnectionError.PortInUse => $"Port {port} is already in use — the game may already be running as a host.",
        ConnectionError.ListenFailed => $"Could not open port {port}.",
        ConnectionError.NotPongheimer => "Whatever answers at this address is not oPongheimer.",
        ConnectionError.VersionMismatch => "You and the host have different game versions. Use the same exe.",
        _ => UnexpectedError,
    };

    public override string BonusName(BonusKind kind) => kind switch
    {
        BonusKind.BigBall => "Big ball",
        BonusKind.SmallBall => "Small ball",
        BonusKind.FastBall => "Fast ball",
        BonusKind.NormalSpeedBall => "Normal speed",
        BonusKind.Shockwave => "Shockwave",
        BonusKind.LongPaddle => "Long paddle",
        BonusKind.ShortPaddle => "Short paddle",
        BonusKind.FastPaddle => "Fast paddle",
        BonusKind.SlowPaddle => "Slow paddle",
        BonusKind.Shield => "Shield",
        BonusKind.DoublePoint => "Double point",
        BonusKind.Freeze => "Freeze",
        BonusKind.Inversion => "Inversion",
        _ => "",
    };

    public override string Describe(ConnectionFault fault) => fault switch
    {
        ConnectionFault.Closed => "Your opponent left the game.",
        ConnectionFault.TimedOut => "Connection to your opponent was lost.",
        ConnectionFault.ProtocolViolation => "Your opponent sent invalid data — disconnected.",
        _ => UnexpectedError,
    };
}
