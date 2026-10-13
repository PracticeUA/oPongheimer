using Microsoft.Xna.Framework;
using Pongheimer.Core.Game;

namespace Pongheimer.Rendering;

/// <summary>
/// Кольори гри: темне поле, синій — лівий гравець (хост), помаранчевий — правий (клієнт).
/// Ті самі значення використовує tools\make-icon.ps1.
/// </summary>
public static class Palette
{
    public static readonly Color Window = new(0x0B, 0x0F, 0x14);
    public static readonly Color Field = new(0x11, 0x16, 0x1D);
    public static readonly Color Line = new(0x2A, 0x34, 0x41);
    public static readonly Color Muted = new(0x3A, 0x46, 0x56);
    public static readonly Color SubtleText = new(0x8A, 0x97, 0xA8);
    public static readonly Color Text = new(0xC9, 0xD3, 0xDF);
    public static readonly Color Bright = Color.White;
    public static readonly Color LeftPlayer = new(0x4F, 0xC3, 0xF7);
    public static readonly Color RightPlayer = new(0xFF, 0xB7, 0x4D);

    // Ядерний гриб: від темного краю до розпеченої серцевини. Внутрішній шар — колір правого гравця.
    public static readonly Color ExplosionOuter = new(0xC8, 0x45, 0x2A);
    public static readonly Color ExplosionMiddle = new(0xF0, 0x8A, 0x32);
    public static readonly Color ExplosionInner = RightPlayer;
    public static readonly Color ExplosionCore = new(0xFF, 0xE7, 0xBF);

    // Бомби ядерного режиму: за кольором видно, чи варто в неї цілитись.
    public static readonly Color BonusGood = new(0x7B, 0xD8, 0x8F);
    public static readonly Color BonusBad = new(0xF0, 0x60, 0x5D);
    public static readonly Color BonusBall = new(0xB9, 0xA3, 0xFF);

    public static Color For(BonusCategory category) => category switch
    {
        BonusCategory.Good => BonusGood,
        BonusCategory.Bad => BonusBad,
        _ => BonusBall,
    };

    /// <summary>Напівпрозора версія кольору (у форматі з попередньо помноженою альфою, як чекає рендер).</summary>
    public static Color WithOpacity(this Color color, float opacity) => color * Math.Clamp(opacity, 0f, 1f);
}
