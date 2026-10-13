using Microsoft.Xna.Framework;
using Pongheimer.Core.Game;

namespace Pongheimer.Rendering;

/// <summary>
/// Як вікно ділиться на простори координат. Поле завжди 800×450 ігрових одиниць і вписується
/// у вікно зі збереженням пропорцій; інтерфейс масштабується разом із ним, тож за будь-якого
/// розміру вікна все виглядає однаково, лише більшим чи меншим.
/// </summary>
public readonly record struct ScreenLayout(int Width, int Height)
{
    // За стартового вікна 960×600 одна одиниця інтерфейсу — рівно один піксель.
    private const float FieldScaleAtDesignSize = 960f / (float)GameRules.FieldWidth;

    public float FieldScale => MathF.Min(Width / (float)GameRules.FieldWidth, Height / (float)GameRules.FieldHeight);

    public Vector2 FieldOffset => new(
        MathF.Round((Width - (float)GameRules.FieldWidth * FieldScale) / 2),
        MathF.Round((Height - (float)GameRules.FieldHeight * FieldScale) / 2));

    public Rectangle FieldPixels => new(
        (int)FieldOffset.X,
        (int)FieldOffset.Y,
        (int)MathF.Ceiling((float)GameRules.FieldWidth * FieldScale),
        (int)MathF.Ceiling((float)GameRules.FieldHeight * FieldScale));

    public float UiScale => FieldScale / FieldScaleAtDesignSize;

    /// <summary>Розмір вікна в одиницях інтерфейсу.</summary>
    public float UiWidth => Width / UiScale;

    public float UiHeight => Height / UiScale;

    public Vector2 ToUi(Vector2 pixels) => pixels / UiScale;
}
