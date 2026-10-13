using Pongheimer.Core.Game;
using Pongheimer.Localization;
using Pongheimer.Rendering;
using Pongheimer.Ui;

namespace Pongheimer.Menu;

/// <summary>
/// Вигляд головного меню: розкладає елементи, передає натискання в <see cref="MenuController"/>
/// і малює все поверх затемненого поля. Розміри задано в одиницях інтерфейсу й не залежать від мови:
/// написи міняються, кнопки стоять на місці.
/// </summary>
public sealed class MenuView
{
    private const float ColumnWidth = 320;
    private const float ButtonHeight = 40;
    private const float SegmentHeight = 28;
    private const float GroupPadding = 4;
    private const float JoinButtonWidth = 136;
    private const float SoundButtonWidth = 118;
    private const float UkrainianButtonWidth = 90;
    private const float EnglishButtonWidth = 66;
    private const float LanguageGap = 4;
    private const float Margin = 16;

    // Висота всього, що вище статусу, і місце під статус із кнопкою «Скасувати» —
    // щоб меню не стрибало, коли статус з'являється чи зникає.
    private const float FixedContentHeight = 401;
    private const float StatusAreaHeight = 120;

    private readonly MenuController _controller;
    private readonly Localizer _localizer;

    private readonly Button _modeClassic = new();
    private readonly Button _modeNuclear = new();
    private readonly Button _host = new();
    private readonly Button _bot = new();
    private readonly Button _botEasy = new();
    private readonly Button _botMedium = new();
    private readonly Button _botHard = new();
    private readonly Button _duel = new();
    private readonly Button _join = new();
    private readonly Button _cancel = new();
    private readonly Button _sound = new();
    private readonly Button _ukrainian = new();
    private readonly Button _english = new();
    private readonly TextField _address = new();

    private Box _modeFrame;
    private Box _difficultyFrame;
    private Box _soundFrame;
    private Box _languageFrame;
    private float _titleY;
    private float _promptY;
    private float _statusY;
    private float _columnX;

    public MenuView(MenuController controller, Localizer localizer, Func<string> clipboardText)
    {
        _controller = controller;
        _localizer = localizer;
        _address.ClipboardText = clipboardText;
        _address.Text = controller.HostAddress;
    }

    public void Update(UiInput input, ScreenLayout layout, double dt)
    {
        Arrange(layout);

        var idle = !_controller.IsBusy;
        _modeClassic.IsEnabled = idle;
        _modeNuclear.IsEnabled = idle;
        _host.IsEnabled = idle;
        _bot.IsEnabled = idle;
        _botEasy.IsEnabled = idle;
        _botMedium.IsEnabled = idle;
        _botHard.IsEnabled = idle;
        _duel.IsEnabled = idle;
        _join.IsEnabled = idle;
        _address.IsEnabled = idle;
        _cancel.IsVisible = !idle;

        // Мову й звук можна міняти будь-коли, навіть поки чекаємо на суперника.
        if (_sound.Update(input))
        {
            _controller.IsSoundEnabled = !_controller.IsSoundEnabled;
        }

        if (_ukrainian.Update(input))
        {
            _controller.Language = Language.Ukrainian;
        }

        if (_english.Update(input))
        {
            _controller.Language = Language.English;
        }

        if (_modeClassic.Update(input))
        {
            _controller.GameMode = GameMode.Classic;
        }

        if (_modeNuclear.Update(input))
        {
            _controller.GameMode = GameMode.Nuclear;
        }

        if (_botEasy.Update(input))
        {
            _controller.BotDifficulty = BotDifficulty.Easy;
        }

        if (_botMedium.Update(input))
        {
            _controller.BotDifficulty = BotDifficulty.Medium;
        }

        if (_botHard.Update(input))
        {
            _controller.BotDifficulty = BotDifficulty.Hard;
        }

        var enterPressed = _address.Update(input, dt);
        _controller.HostAddress = _address.Text;

        if (_cancel.Update(input))
        {
            _controller.Cancel();
        }

        if (_host.Update(input))
        {
            _controller.Host();
        }
        else if (_bot.Update(input))
        {
            _controller.PlayBot();
        }
        else if (_duel.Update(input))
        {
            _controller.PlayLocalDuel();
        }
        else if (_join.Update(input) || (enterPressed && idle))
        {
            _controller.Join();
        }
    }

    public void Draw(Canvas canvas, ScreenLayout layout)
    {
        Arrange(layout);
        var texts = _localizer.Current;
        canvas.SetSpace(layout.UiScale, Microsoft.Xna.Framework.Vector2.Zero);

        // Затемнення поля під меню.
        canvas.FillRect(0, 0, layout.UiWidth, layout.UiHeight, Palette.Window.WithOpacity(0.9f));

        Button.DrawGroupFrame(canvas, _soundFrame);
        _sound.DrawSegment(canvas, _controller.IsSoundEnabled ? texts.SoundOn : texts.SoundOff, _controller.IsSoundEnabled);

        // Назви мов не перекладаються: кожну видно її власною мовою.
        Button.DrawGroupFrame(canvas, _languageFrame);
        _ukrainian.DrawSegment(canvas, "Українська", _controller.Language == Language.Ukrainian);
        _english.DrawSegment(canvas, "English", _controller.Language == Language.English);

        DrawTitle(canvas, layout);

        Button.DrawGroupFrame(canvas, _modeFrame);
        _modeClassic.DrawSegment(canvas, texts.ModeClassic, _controller.GameMode == GameMode.Classic);
        _modeNuclear.DrawSegment(canvas, texts.ModeNuclear, _controller.GameMode == GameMode.Nuclear);

        _host.Draw(canvas, texts.HostGame, ButtonKind.LeftPlayer);
        _bot.Draw(canvas, texts.PlayBot, ButtonKind.Outline);
        Button.DrawGroupFrame(canvas, _difficultyFrame);
        _botEasy.DrawSegment(canvas, texts.BotEasy, _controller.BotDifficulty == BotDifficulty.Easy);
        _botMedium.DrawSegment(canvas, texts.BotMedium, _controller.BotDifficulty == BotDifficulty.Medium);
        _botHard.DrawSegment(canvas, texts.BotHard, _controller.BotDifficulty == BotDifficulty.Hard);
        _duel.Draw(canvas, texts.PlayLocalDuel, ButtonKind.Outline);

        canvas.DrawText(texts.JoinPrompt, _columnX, _promptY, 12, TextStyle.Regular, Palette.SubtleText);
        _address.Draw(canvas);
        _join.Draw(canvas, texts.Join, ButtonKind.RightPlayer);

        var statusHeight = canvas.DrawTextBlock(
            _controller.Status(texts),
            _columnX + ColumnWidth / 2,
            _statusY,
            14,
            TextStyle.Regular,
            Palette.Text,
            maxWidth: ColumnWidth);

        // «Скасувати» стоїть одразу під статусом, скільки б рядків той не зайняв.
        _cancel.Bounds = new Box(_columnX, _statusY + statusHeight + 14, ColumnWidth, 34);
        _cancel.Draw(canvas, texts.Cancel, ButtonKind.Outline);
    }

    private static void DrawTitle(Canvas canvas, float centerX, float y)
    {
        // Перша літера — кольором лівого гравця, як на іконці.
        const float size = 40;
        var first = canvas.MeasureText("o", size, TextStyle.Bold);
        var rest = canvas.MeasureText("Pongheimer", size, TextStyle.Bold);
        var left = centerX - (first.X + rest.X) / 2;
        canvas.DrawText("o", left, y, size, TextStyle.Bold, Palette.LeftPlayer);
        canvas.DrawText("Pongheimer", left + first.X, y, size, TextStyle.Bold, Palette.Bright);
    }

    private void DrawTitle(Canvas canvas, ScreenLayout layout) =>
        DrawTitle(canvas, layout.UiWidth / 2, _titleY);

    private void Arrange(ScreenLayout layout)
    {
        var width = layout.UiWidth;
        var height = layout.UiHeight;

        // Правий верхній кут: звук і мови.
        var languageWidth = UkrainianButtonWidth + LanguageGap + EnglishButtonWidth + GroupPadding * 2;
        var frameHeight = SegmentHeight + GroupPadding * 2;
        _languageFrame = new Box(width - Margin - languageWidth, Margin, languageWidth, frameHeight);
        _ukrainian.Bounds = new Box(
            _languageFrame.X + GroupPadding, Margin + GroupPadding, UkrainianButtonWidth, SegmentHeight);
        _english.Bounds = new Box(
            _ukrainian.Bounds.Right + LanguageGap, Margin + GroupPadding, EnglishButtonWidth, SegmentHeight);

        var soundWidth = SoundButtonWidth + GroupPadding * 2;
        _soundFrame = new Box(_languageFrame.X - 10 - soundWidth, Margin, soundWidth, frameHeight);
        _sound.Bounds = new Box(_soundFrame.X + GroupPadding, Margin + GroupPadding, SoundButtonWidth, SegmentHeight);

        // Центральна колонка.
        _columnX = (width - ColumnWidth) / 2;
        var y = MathF.Max(Margin, (height - FixedContentHeight - StatusAreaHeight) / 2);

        _titleY = y;
        y += 53 + 28;

        // Режим гри — для всього, що починається з цієї машини; клієнт грає в режимі хоста.
        _modeFrame = new Box(_columnX, y, ColumnWidth, frameHeight);
        var modeWidth = (ColumnWidth - GroupPadding * 2) / 2;
        _modeClassic.Bounds = new Box(_columnX + GroupPadding, y + GroupPadding, modeWidth, SegmentHeight);
        _modeNuclear.Bounds = new Box(_modeClassic.Bounds.Right, y + GroupPadding, modeWidth, SegmentHeight);
        y += frameHeight + 10;

        _host.Bounds = new Box(_columnX, y, ColumnWidth, ButtonHeight);
        y += ButtonHeight + 10;

        _bot.Bounds = new Box(_columnX, y, ColumnWidth, ButtonHeight);
        y += ButtonHeight + 6;

        _difficultyFrame = new Box(_columnX, y, ColumnWidth, frameHeight);
        var segmentWidth = (ColumnWidth - GroupPadding * 2) / 3;
        var segmentY = y + GroupPadding;
        _botEasy.Bounds = new Box(_columnX + GroupPadding, segmentY, segmentWidth, SegmentHeight);
        _botMedium.Bounds = new Box(_botEasy.Bounds.Right, segmentY, segmentWidth, SegmentHeight);
        _botHard.Bounds = new Box(_botMedium.Bounds.Right, segmentY, segmentWidth, SegmentHeight);
        y += frameHeight + 10;

        _duel.Bounds = new Box(_columnX, y, ColumnWidth, ButtonHeight);
        y += ButtonHeight + 28;

        _promptY = y;
        y += 16 + 8;

        _address.Bounds = new Box(_columnX, y, ColumnWidth - JoinButtonWidth - 10, ButtonHeight);
        _join.Bounds = new Box(_columnX + ColumnWidth - JoinButtonWidth, y, JoinButtonWidth, ButtonHeight);
        y += ButtonHeight + 22;

        _statusY = y;
        if (_cancel.Bounds == default)
        {
            _cancel.Bounds = new Box(_columnX, y, ColumnWidth, 34);
        }
    }
}
