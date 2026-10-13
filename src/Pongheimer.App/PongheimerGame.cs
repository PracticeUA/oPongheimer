using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Pongheimer.Audio;
using Pongheimer.Core.Diagnostics;
using Pongheimer.Core.Networking;
using Pongheimer.Core.Sessions;
using Pongheimer.Infrastructure;
using Pongheimer.Input;
using Pongheimer.Localization;
using Pongheimer.Match;
using Pongheimer.Menu;
using Pongheimer.Rendering;
using Pongheimer.Ui;
using Forms = System.Windows.Forms;

namespace Pongheimer;

/// <summary>
/// Вікно гри та її головний цикл. Це ж і точка складання: тут і лише тут створюються
/// та з'єднуються всі частини застосунку.
/// </summary>
public sealed class PongheimerGame : Game
{
    private const int DesignWidth = 960;
    private const int DesignHeight = 600;
    private const int MinimumWidth = 520;
    private const int MinimumHeight = 400;

    private readonly GraphicsDeviceManager _graphics;
    private readonly LaunchOptions _options;
    private readonly ILog _log;
    private readonly string _dataDirectory;
    private readonly GameThreadContext _gameThread = new();
    private readonly InputState _input = new();
    private readonly Queue<char> _typed = new();
    private readonly Queue<double> _screenshotTimes;

    private Localizer _localizer = null!;
    private SynthSoundPlayer _sounds = null!;
    private MenuController _menu = null!;
    private MenuView _menuView = null!;
    private MatchController _match = null!;
    private TextRenderer _text = null!;
    private Canvas _canvas = null!;
    private bool _isMenuVisible = true;
    private double _uptime;

    public PongheimerGame(LaunchOptions options, ILog log, string dataDirectory)
    {
        _options = options;
        _log = log;
        _dataDirectory = dataDirectory;
        _screenshotTimes = new Queue<double>(options.ScreenshotTimes);

        // На екранах із масштабуванням вікно відкривається того ж видимого розміру, що й на звичайних.
        var dpiScale = GetDpiForSystem() / 96f;
        _graphics = new GraphicsDeviceManager(this)
        {
            GraphicsProfile = GraphicsProfile.HiDef,
            PreferredBackBufferWidth = (int)(DesignWidth * dpiScale),
            PreferredBackBufferHeight = (int)(DesignHeight * dpiScale),
            PreferMultiSampling = true,

            // Кадр показується рівно в такт з оновленням екрана — головна причина переходу на MonoGame.
            SynchronizeWithVerticalRetrace = true,
        };
        _graphics.PreparingDeviceSettings += (_, e) =>
            e.GraphicsDeviceInformation.PresentationParameters.MultiSampleCount = 8;

        // Один крок логіки на один показаний кадр; тривалість кроку — справжній час між кадрами.
        IsFixedTimeStep = false;

        // Мережева партія має йти з повною частотою, навіть коли вікно не на передньому плані.
        InactiveSleepTime = TimeSpan.Zero;

        IsMouseVisible = true;
        Window.Title = "oPongheimer";
        Window.AllowUserResizing = true;
        Window.TextInput += (_, e) => _typed.Enqueue(e.Character);
    }

    protected override void Initialize()
    {
        SynchronizationContext.SetSynchronizationContext(_gameThread);
        ConfigureWindow(GetDpiForSystem() / 96f);

        var connections = new TcpConnectionService(_log);
        var sessions = new GameSessionFactory(connections);
        var settings = new JsonSettingsStore(Path.Combine(_dataDirectory, "settings.json"), _log);
        _localizer = new Localizer(Localizer.Resolve(settings.Current.Language, CultureInfo.CurrentUICulture));
        _sounds = new SynthSoundPlayer(_log) { IsEnabled = settings.Current.SoundEnabled };

        _match = new MatchController(_sounds, _log) { IsPerformanceVisible = _options.ShowPerformance };
        _menu = new MenuController(sessions, settings, _localizer, _sounds, _log);
        _menuView = new MenuView(_menu, _localizer, ReadClipboard);

        _menu.SessionReady += session =>
        {
            _match.Start(session);
            _isMenuVisible = false;
        };
        _match.Ended += reason =>
        {
            _menu.Status = reason;
            _isMenuVisible = true;
        };

        base.Initialize();

        var adapter = GraphicsDevice.Adapter;
        _log.Info(
            $"Rendering: MonoGame/DirectX on '{adapter.Description}', "
            + $"{adapter.CurrentDisplayMode.Width}x{adapter.CurrentDisplayMode.Height}, "
            + $"MSAA x{GraphicsDevice.PresentationParameters.MultiSampleCount}");

        switch (_options.AutoStart)
        {
            case AutoStart.Bot:
                _menu.PlayBot();
                break;
            case AutoStart.Duel:
                _menu.PlayLocalDuel();
                break;
            case AutoStart.Host:
                _menu.Host();
                break;
            case AutoStart.Join:
                _menu.Join();
                break;
        }
    }

    protected override void LoadContent()
    {
        _text = new TextRenderer(GraphicsDevice);
        _canvas = new Canvas(GraphicsDevice, _text);
    }

    protected override void Update(GameTime gameTime)
    {
        var dt = gameTime.ElapsedGameTime.TotalSeconds;
        _uptime += dt;
        try
        {
            // Спершу — продовження async-операцій меню (підключився суперник, обірвалось очікування).
            _gameThread.Pump();
            _input.Update(IsActive, _typed);

            if (_input.WasPressed(Keys.F3))
            {
                _match.IsPerformanceVisible = !_match.IsPerformanceVisible;
            }

            var layout = CurrentLayout();
            if (_isMenuVisible)
            {
                _menuView.Update(new UiInput(_input, layout.ToUi(_input.MousePosition)), layout, dt);
                _match.Update(dt, input: null);
            }
            else
            {
                _match.Update(dt, _input);
            }
        }
        catch (Exception ex)
        {
            // Жоден збій кадру не має закривати гру: повертаємо гравця в меню з поясненням.
            _log.Error("Unhandled failure in update", ex);
            Recover();
        }

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        try
        {
            if (_screenshotTimes.Count > 0 && _uptime >= _screenshotTimes.Peek())
            {
                SaveScreenshot(_screenshotTimes.Dequeue());
            }

            Render();
        }
        catch (Exception ex)
        {
            _log.Error("Unhandled failure in draw", ex);
            Recover();
        }

        base.Draw(gameTime);
    }

    protected override void OnExiting(object sender, ExitingEventArgs args)
    {
        _menu.Cancel();
        _match.Leave();
        _log.Info("Application exited");
        base.OnExiting(sender, args);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _canvas?.Dispose();
            _text?.Dispose();
            _sounds?.Dispose();
        }

        base.Dispose(disposing);
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    private static string ReadClipboard()
    {
        try
        {
            return Forms.Clipboard.ContainsText() ? Forms.Clipboard.GetText() : "";
        }
        catch (Exception ex) when (ex is ExternalException or ThreadStateException)
        {
            // Буфер обміну зайнятий іншою програмою — вставка просто не спрацює цього разу.
            return "";
        }
    }

    private ScreenLayout CurrentLayout()
    {
        var parameters = GraphicsDevice.PresentationParameters;
        return new ScreenLayout(Math.Max(parameters.BackBufferWidth, 1), Math.Max(parameters.BackBufferHeight, 1));
    }

    private void ConfigureWindow(float dpiScale)
    {
        if (Forms.Control.FromHandle(Window.Handle) is not Forms.Form form)
        {
            return;
        }

        // Мінімум задаємо для вмісту вікна, тому додаємо рамку й заголовок.
        var frame = form.Size - form.ClientSize;
        form.MinimumSize = new System.Drawing.Size(
            (int)(MinimumWidth * dpiScale) + frame.Width,
            (int)(MinimumHeight * dpiScale) + frame.Height);

        using var icon = typeof(PongheimerGame).Assembly.GetManifestResourceStream("app.ico");
        if (icon is not null)
        {
            form.Icon = new System.Drawing.Icon(icon);
        }
    }

    private void Render()
    {
        var layout = CurrentLayout();
        var texts = _localizer.Current;

        GraphicsDevice.Clear(Palette.Window);
        _canvas.Begin(layout.Width, layout.Height);
        FieldRenderer.Draw(_canvas, layout, _match, texts);
        if (_isMenuVisible)
        {
            _menuView.Draw(_canvas, layout);
        }

        if (_match.IsPerformanceVisible)
        {
            _canvas.SetSpace(layout.UiScale, Vector2.Zero);
            _canvas.DrawText(_match.PerformanceText, 10, 8, 12, TextStyle.Monospace, Palette.SubtleText);
        }

        _canvas.End();
    }

    private void SaveScreenshot(double time)
    {
        var layout = CurrentLayout();
        using var target = new RenderTarget2D(
            GraphicsDevice,
            layout.Width,
            layout.Height,
            false,
            SurfaceFormat.Color,
            DepthFormat.None,
            GraphicsDevice.PresentationParameters.MultiSampleCount,
            RenderTargetUsage.DiscardContents);
        GraphicsDevice.SetRenderTarget(target);
        Render();
        GraphicsDevice.SetRenderTarget(null);

        var directory = Path.Combine(_dataDirectory, "screenshots");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, FormattableString.Invariant($"shot-{Environment.ProcessId}-{time:0.0}.png"));
        using var file = File.Create(path);
        target.SaveAsPng(file, layout.Width, layout.Height);
        _log.Info($"Screenshot saved: {path}");
    }

    private void Recover()
    {
        if (_match.IsActive)
        {
            _match.Abort(texts => texts.UnexpectedError);
        }
        else
        {
            _menu.Status = texts => texts.UnexpectedError;
            _isMenuVisible = true;
        }
    }
}
