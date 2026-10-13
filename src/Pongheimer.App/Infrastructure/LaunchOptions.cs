using System.Globalization;

namespace Pongheimer.Infrastructure;

public enum AutoStart
{
    None,
    Bot,
    Duel,
    Host,
    Join,
}

/// <summary>
/// Службові параметри запуску — для діагностики й автоматичних перевірок, гравцеві вони не потрібні.
/// <code>
/// --perf                 одразу показати лічильник плавності (те саме, що F3)
/// --autostart=bot|duel|host|join   одразу почати режим без кліків у меню
/// --screenshot=2,5.5     зберегти знімки екрана на 2-й і 5,5-й секунді (у теку даних гри)
/// </code>
/// </summary>
public sealed record LaunchOptions(bool ShowPerformance, AutoStart AutoStart, IReadOnlyList<double> ScreenshotTimes)
{
    public static LaunchOptions Parse(string[] args)
    {
        var showPerformance = false;
        var autoStart = AutoStart.None;
        var screenshots = new List<double>();

        foreach (var arg in args)
        {
            var separator = arg.IndexOf('=');
            var name = (separator < 0 ? arg : arg[..separator]).ToLowerInvariant();
            var value = separator < 0 ? "" : arg[(separator + 1)..];
            switch (name)
            {
                case "--perf":
                    showPerformance = true;
                    break;
                case "--autostart" when Enum.TryParse<AutoStart>(value, ignoreCase: true, out var mode):
                    autoStart = mode;
                    break;
                case "--screenshot":
                    foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                        {
                            screenshots.Add(seconds);
                        }
                    }

                    break;
            }
        }

        screenshots.Sort();
        return new LaunchOptions(showPerformance, autoStart, screenshots);
    }
}
