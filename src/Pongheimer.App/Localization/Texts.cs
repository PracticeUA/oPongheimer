using Pongheimer.Core.Networking;

namespace Pongheimer.Localization;

/// <summary>
/// Усі тексти інтерфейсу. Кожна мова — нащадок, тож забутий переклад є помилкою компіляції,
/// а не порожнім написом у застосунку.
/// </summary>
public abstract class Texts
{
    public abstract string HostGame { get; }

    public abstract string PlayBot { get; }

    public abstract string BotEasy { get; }

    public abstract string BotMedium { get; }

    public abstract string BotHard { get; }

    public abstract string PlayLocalDuel { get; }

    public abstract string JoinPrompt { get; }

    public abstract string Join { get; }

    public abstract string Cancel { get; }

    public abstract string ControlsHint { get; }

    public abstract string DuelControlsHint { get; }

    public abstract string LeftPlayerWins { get; }

    public abstract string RightPlayerWins { get; }

    public abstract string Paused { get; }

    public abstract string ResumeHint { get; }

    public abstract string Victory { get; }

    public abstract string Defeat { get; }

    public abstract string RematchHint { get; }

    public abstract string SoundOn { get; }

    public abstract string SoundOff { get; }

    public abstract string Connecting { get; }

    public abstract string UnexpectedError { get; }

    public abstract string StartupFailed { get; }

    /// <summary>Порожній текст будь-якою мовою.</summary>
    public static string None(Texts texts) => "";

    public abstract string WaitingForOpponent(IReadOnlyList<string> addresses, int port);

    public abstract string Describe(ConnectionError error, int port);

    public abstract string Describe(ConnectionFault fault);
}
