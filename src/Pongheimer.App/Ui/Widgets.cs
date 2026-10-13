using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Pongheimer.Input;
using Pongheimer.Rendering;

namespace Pongheimer.Ui;

/// <summary>Прямокутник в одиницях інтерфейсу.</summary>
public readonly record struct Box(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;

    public float Bottom => Y + Height;

    public float CenterX => X + Width / 2;

    public bool Contains(Vector2 point) =>
        point.X >= X && point.X < Right && point.Y >= Y && point.Y < Bottom;
}

/// <summary>Ввід для елементів інтерфейсу: курсор уже переведено в одиниці інтерфейсу.</summary>
public readonly record struct UiInput(InputState Raw, Vector2 Pointer);

public enum ButtonKind
{
    Outline,
    LeftPlayer,
    RightPlayer,
}

/// <summary>
/// Кнопка: стежить за наведенням і натисканням та малює себе в стилі гри.
/// Спрацьовує, як звичайні кнопки Windows, — коли кнопку миші відпустили над тією ж кнопкою,
/// над якою натиснули.
/// </summary>
public sealed class Button
{
    private const float CornerRadius = 6;

    private bool _hovered;
    private bool _armed;

    public Box Bounds { get; set; }

    public bool IsEnabled { get; set; } = true;

    public bool IsVisible { get; set; } = true;

    /// <summary>Оновлює стан; повертає true в кадрі, коли кнопку натиснули.</summary>
    public bool Update(UiInput input)
    {
        if (!IsVisible || !IsEnabled)
        {
            _hovered = false;
            _armed = false;
            return false;
        }

        _hovered = Bounds.Contains(input.Pointer);
        if (input.Raw.WasLeftButtonPressed)
        {
            _armed = _hovered;
        }

        if (!input.Raw.WasLeftButtonReleased)
        {
            return false;
        }

        var clicked = _armed && _hovered;
        _armed = false;
        return clicked;
    }

    /// <summary>Звичайна кнопка дії.</summary>
    public void Draw(Canvas canvas, string text, ButtonKind kind, float fontSize = 15)
    {
        if (!IsVisible)
        {
            return;
        }

        var opacity = IsEnabled ? 1f : 0.35f;
        var (fill, foreground) = kind switch
        {
            ButtonKind.LeftPlayer => (Palette.LeftPlayer, Palette.Window),
            ButtonKind.RightPlayer => (Palette.RightPlayer, Palette.Window),
            _ => (Color.Transparent, Palette.Text),
        };

        if (kind == ButtonKind.Outline)
        {
            canvas.StrokeRoundedRect(
                Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height, CornerRadius, 1, Palette.Muted.WithOpacity(opacity));
        }
        else
        {
            canvas.FillRoundedRect(
                Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height, CornerRadius, fill.WithOpacity(opacity));
        }

        DrawTint(canvas, hoverOpacity: 0.14f);
        DrawLabel(canvas, text, fontSize, foreground.WithOpacity(opacity));
    }

    /// <summary>Сегмент перемикача: обраний (або ввімкнений) підсвічено.</summary>
    public void DrawSegment(Canvas canvas, string text, bool isChecked)
    {
        if (!IsVisible)
        {
            return;
        }

        var opacity = IsEnabled ? 1f : 0.35f;
        if (isChecked)
        {
            canvas.FillRoundedRect(
                Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height, CornerRadius, Palette.Muted.WithOpacity(opacity));
        }

        DrawTint(canvas, hoverOpacity: 0.08f);
        DrawLabel(canvas, text, 13, (isChecked ? Palette.Bright : Palette.SubtleText).WithOpacity(opacity));
    }

    /// <summary>Рамка навколо групи сегментів.</summary>
    public static void DrawGroupFrame(Canvas canvas, Box bounds) =>
        canvas.StrokeRoundedRect(bounds.X, bounds.Y, bounds.Width, bounds.Height, 9, 1, Palette.Line);

    private void DrawTint(Canvas canvas, float hoverOpacity)
    {
        // Наведення злегка висвітлює кнопку, натискання — затемнює.
        if (_hovered && _armed)
        {
            canvas.FillRoundedRect(
                Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height, CornerRadius, Color.Black.WithOpacity(0.22f));
        }
        else if (_hovered)
        {
            canvas.FillRoundedRect(
                Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height, CornerRadius, Color.White.WithOpacity(hoverOpacity));
        }
    }

    private void DrawLabel(Canvas canvas, string text, float fontSize, Color color)
    {
        var size = canvas.MeasureText(text, fontSize, TextStyle.Semibold);
        canvas.DrawText(
            text,
            Bounds.CenterX,
            Bounds.Y + (Bounds.Height - size.Y) / 2,
            fontSize,
            TextStyle.Semibold,
            color,
            TextAlign.Center);
    }
}

/// <summary>
/// Однорядкове поле вводу. Приймає символи з урахуванням розкладки, Backspace і вставку (Ctrl+V).
/// У меню воно одне, тому вважається завжди у фокусі, поки ввімкнене.
/// </summary>
public sealed class TextField
{
    private const float CornerRadius = 6;
    private const float Padding = 10;
    private const float FontSize = 16;
    private const double CaretBlinkSeconds = 0.53;

    private double _caretClock;

    public Box Bounds { get; set; }

    public string Text { get; set; } = "";

    public int MaxLength { get; set; } = 64;

    public bool IsEnabled { get; set; } = true;

    /// <summary>Звідки брати текст для вставки; задається зовні, бо буфер обміну — справа платформи.</summary>
    public Func<string>? ClipboardText { get; set; }

    /// <summary>Обробляє набране за кадр; повертає true, якщо натиснули Enter.</summary>
    public bool Update(UiInput input, double dt)
    {
        _caretClock += dt;
        if (!IsEnabled)
        {
            return false;
        }

        var submitted = false;
        var changed = false;
        foreach (var character in input.Raw.Typed)
        {
            switch (character)
            {
                case '\b' when Text.Length > 0:
                    Text = Text[..^1];
                    changed = true;
                    break;
                case '\r' or '\n':
                    submitted = true;
                    break;
                default:
                    changed |= Append(character.ToString());
                    break;
            }
        }

        if (input.Raw.IsControlDown && input.Raw.WasPressed(Keys.V) && ClipboardText is not null)
        {
            changed |= Append(ClipboardText());
        }

        if (changed)
        {
            // Після набору каретка одразу видима, а не посеред «блимання».
            _caretClock = 0;
        }

        return submitted;
    }

    public void Draw(Canvas canvas)
    {
        var opacity = IsEnabled ? 1f : 0.35f;
        canvas.FillRoundedRect(
            Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height, CornerRadius, Palette.Field.WithOpacity(opacity));
        canvas.StrokeRoundedRect(
            Bounds.X,
            Bounds.Y,
            Bounds.Width,
            Bounds.Height,
            CornerRadius,
            1,
            (IsEnabled ? Palette.RightPlayer : Palette.Muted).WithOpacity(opacity));

        // Якщо текст не вміщається, показуємо його кінець — там, де каретка.
        var available = Bounds.Width - Padding * 2;
        var visible = Text;
        while (visible.Length > 0 && canvas.MeasureText(visible, FontSize, TextStyle.Regular).X > available)
        {
            visible = visible[1..];
        }

        var size = canvas.MeasureText(visible, FontSize, TextStyle.Regular);
        var textY = Bounds.Y + (Bounds.Height - size.Y) / 2;
        canvas.DrawText(visible, Bounds.X + Padding, textY, FontSize, TextStyle.Regular, Palette.Bright.WithOpacity(opacity));

        if (IsEnabled && _caretClock % (CaretBlinkSeconds * 2) < CaretBlinkSeconds)
        {
            canvas.FillRect(Bounds.X + Padding + size.X + 1, textY + 2, 1.5f, size.Y - 4, Palette.Bright);
        }
    }

    private bool Append(string addition)
    {
        var changed = false;
        foreach (var character in addition)
        {
            if (!char.IsControl(character) && Text.Length < MaxLength)
            {
                Text += character;
                changed = true;
            }
        }

        return changed;
    }
}
