using Pongheimer.Audio;
using Pongheimer.Core.Diagnostics;
using Pongheimer.Core.Game;
using Pongheimer.Core.Networking;
using Pongheimer.Core.Sessions;
using Pongheimer.Infrastructure;
using Pongheimer.Localization;

namespace Pongheimer.Menu;

/// <summary>
/// Логіка головного меню без жодного малювання: запуск режимів, підключення, налаштування.
/// Усі методи викликаються з ігрового потоку; продовження після await теж повертаються в нього.
/// </summary>
public sealed class MenuController
{
    private readonly IGameSessionFactory _sessions;
    private readonly ISettingsStore _settings;
    private readonly Localizer _localizer;
    private readonly ISoundPlayer _sounds;
    private readonly ILog _log;

    private CancellationTokenSource? _connecting;

    public MenuController(
        IGameSessionFactory sessions,
        ISettingsStore settings,
        Localizer localizer,
        ISoundPlayer sounds,
        ILog log)
    {
        _sessions = sessions;
        _settings = settings;
        _localizer = localizer;
        _sounds = sounds;
        _log = log;
        HostAddress = settings.Current.LastHost;

        // Файл налаштувань могли відредагувати руками — невідоме значення замінюємо типовим.
        if (!Enum.IsDefined(settings.Current.BotDifficulty))
        {
            settings.Current.BotDifficulty = BotDifficulty.Medium;
        }
    }

    /// <summary>Партія готова до старту.</summary>
    public event Action<IGameSession>? SessionReady;

    public string HostAddress { get; set; }

    /// <summary>Триває очікування суперника або підключення.</summary>
    public bool IsBusy { get; private set; }

    /// <summary>
    /// Статус зберігається як «рецепт» тексту, а не готовий рядок, тому після зміни мови
    /// він одразу показується новою мовою.
    /// </summary>
    public Func<Texts, string> Status { get; set; } = Texts.None;

    public Language Language
    {
        get => _localizer.Language;
        set
        {
            if (_localizer.Language == value)
            {
                return;
            }

            _localizer.Language = value;
            _settings.Current.Language = Localizer.ToCode(value);
            _settings.Save();
        }
    }

    public BotDifficulty BotDifficulty
    {
        get => _settings.Current.BotDifficulty;
        set
        {
            if (_settings.Current.BotDifficulty == value)
            {
                return;
            }

            _settings.Current.BotDifficulty = value;
            _settings.Save();
        }
    }

    public bool IsSoundEnabled
    {
        get => _sounds.IsEnabled;
        set
        {
            if (_sounds.IsEnabled == value)
            {
                return;
            }

            _sounds.IsEnabled = value;
            _settings.Current.SoundEnabled = value;
            _settings.Save();

            // Одразу чутно, що звук справді ввімкнувся.
            _sounds.Play(GameSound.PaddleHit);
        }
    }

    public void PlayBot() => StartLocal(_sessions.CreateSolo(BotDifficulty));

    public void PlayLocalDuel() => StartLocal(_sessions.CreateLocalDuel());

    public void Host()
    {
        var addresses = _sessions.LocalAddresses;
        var port = _sessions.Port;
        _ = ConnectAsync(texts => texts.WaitingForOpponent(addresses, port), _sessions.HostAsync);
    }

    public void Join()
    {
        var host = HostAddress.Trim();
        _ = ConnectAsync(texts => texts.Connecting, async cancellationToken =>
        {
            var session = await _sessions.JoinAsync(host, cancellationToken);
            _settings.Current.LastHost = host;
            _settings.Save();
            return session;
        });
    }

    public void Cancel() => _connecting?.Cancel();

    private void StartLocal(IGameSession session)
    {
        if (IsBusy)
        {
            session.Dispose();
            return;
        }

        Status = Texts.None;
        SessionReady?.Invoke(session);
    }

    private async Task ConnectAsync(
        Func<Texts, string> busyStatus,
        Func<CancellationToken, Task<IGameSession>> connect)
    {
        if (IsBusy)
        {
            return;
        }

        using var connecting = new CancellationTokenSource();
        _connecting = connecting;
        IsBusy = true;
        Status = busyStatus;
        try
        {
            var session = await connect(connecting.Token);
            Status = Texts.None;
            SessionReady?.Invoke(session);
        }
        catch (OperationCanceledException)
        {
            Status = Texts.None;
        }
        catch (ConnectionException ex)
        {
            _log.Warn($"Connect failed: {ex.Error} ({ex.InnerException?.Message})");
            var port = _sessions.Port;
            Status = texts => texts.Describe(ex.Error, port);
        }
        catch (Exception ex)
        {
            _log.Error("Unexpected failure while connecting", ex);
            Status = texts => texts.UnexpectedError;
        }
        finally
        {
            _connecting = null;
            IsBusy = false;
        }
    }
}
