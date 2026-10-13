using Microsoft.Xna.Framework;

namespace Pongheimer.Rendering;

/// <summary>
/// Ядерний гриб у фіналі гри. Намальований тими самими пласкими фігурами, що й решта гри,
/// у координатах поля 800×450; уся анімація — функція часу від моменту останнього гола.
/// </summary>
public static class ExplosionRenderer
{
    // Точка, з якої гриб «росте»: низ поля по центру.
    private static readonly Vector2 Ground = new(400, 450);

    // Центр шапки, навколо якого вона «дихає».
    private static readonly Vector2 CapCenter = new(400, 150);

    /// <param name="canvas">Полотно; простір координат має бути вже заданий.</param>
    /// <param name="seconds">Час від початку вибуху.</param>
    /// <param name="withFlash">Чи малювати спалах на все поле (для маленьких вибухів бомб він зайвий).</param>
    /// <param name="opacity">Загальна прозорість — щоб маленький вибух міг розтанути.</param>
    public static void Draw(Canvas canvas, double seconds, bool withFlash = true, float opacity = 1)
    {
        var t = (float)seconds;
        DrawShockwave(canvas, t, opacity);

        // Гриб росте від землі: спершу розходиться вшир, трохи повільніше — вгору.
        const float growthDelay = 0.1f;
        if (t >= growthDelay)
        {
            var scale = new Vector2(
                MathHelper.Lerp(0.15f, 1f, CubicEaseOut((t - growthDelay) / 1.1f)),
                CubicEaseOut((t - growthDelay) / 1.4f));
            DrawCloud(canvas, scale, CapBreathing(t), opacity);
        }

        // Спалах на все поле.
        var flash = 1 - QuadraticEaseOut(t / 0.7f);
        if (withFlash && flash > 0)
        {
            canvas.FillRect(0, 0, 800, 450, Palette.Bright.WithOpacity(flash));
        }
    }

    private static void DrawShockwave(Canvas canvas, float t, float opacity)
    {
        var progress = (t - 0.1f) / 1.2f;
        if (progress is <= 0 or >= 1)
        {
            return;
        }

        // Кільце по землі, що розширюється й тане.
        canvas.StrokeEllipse(
            Ground.X,
            Ground.Y,
            60 * 8 * progress,
            18 * 5 * progress,
            3 + 9 * progress,
            Palette.ExplosionCore.WithOpacity(0.9f * (1 - progress) * opacity));
    }

    private static void DrawCloud(Canvas canvas, Vector2 scale, Vector2 capScale, float opacity)
    {
        // Еліпс, заданий як у розмітці (лівий верхній кут і розмір), з урахуванням росту гриба.
        void Ellipse(float left, float top, float width, float height, Color color, bool isCap = false)
        {
            var center = new Vector2(left + width / 2, top + height / 2);
            var radii = new Vector2(width / 2, height / 2);
            if (isCap)
            {
                center = CapCenter + (center - CapCenter) * capScale;
                radii *= capScale;
            }

            center = Ground + (center - Ground) * scale;
            radii *= scale;
            canvas.FillEllipse(center.X, center.Y, radii.X, radii.Y, color.WithOpacity(opacity));
        }

        // Хмара пилу біля землі.
        Ellipse(235, 404, 330, 80, Palette.ExplosionOuter);
        Ellipse(205, 420, 130, 56, Palette.ExplosionOuter);
        Ellipse(465, 420, 130, 56, Palette.ExplosionOuter);
        Ellipse(300, 418, 200, 52, Palette.ExplosionMiddle);

        // Ніжка: три вкладені шари, кожен — смуга між двома кривими.
        DrawStem(canvas, scale, 352, 380, 374, 366, Palette.ExplosionOuter.WithOpacity(opacity));
        DrawStem(canvas, scale, 372, 390, 386, 382, Palette.ExplosionMiddle.WithOpacity(opacity));
        DrawStem(canvas, scale, 390, 396, 395, 394, Palette.ExplosionInner.WithOpacity(opacity));

        // Кільце на ніжці.
        Ellipse(330, 296, 140, 30, Palette.ExplosionOuter);
        Ellipse(350, 301, 100, 18, Palette.ExplosionMiddle);

        // Шапка.
        Ellipse(238, 150, 120, 84, Palette.ExplosionOuter, isCap: true);
        Ellipse(442, 150, 120, 84, Palette.ExplosionOuter, isCap: true);
        Ellipse(300, 166, 200, 76, Palette.ExplosionOuter, isCap: true);
        Ellipse(240, 48, 320, 170, Palette.ExplosionOuter, isCap: true);
        Ellipse(278, 66, 244, 124, Palette.ExplosionMiddle, isCap: true);
        Ellipse(320, 84, 160, 78, Palette.ExplosionInner, isCap: true);
        Ellipse(358, 100, 84, 38, Palette.ExplosionCore, isCap: true);
    }

    /// <summary>
    /// Шар ніжки, симетричний відносно центру. Лівий край — крива Безьє від основи
    /// (<paramref name="baseX"/>, 450) до верху (<paramref name="topX"/>, 190).
    /// </summary>
    private static void DrawStem(
        Canvas canvas,
        Vector2 scale,
        float baseX,
        float control1X,
        float control2X,
        float topX,
        Color color)
    {
        const int segments = 16;
        Span<Vector2> left = stackalloc Vector2[segments + 1];
        Span<Vector2> right = stackalloc Vector2[segments + 1];
        for (var i = 0; i <= segments; i++)
        {
            var u = i / (float)segments;
            var x = Bezier(baseX, control1X, control2X, topX, u);
            var y = Bezier(450, 400, 300, 190, u);
            left[i] = Ground + (new Vector2(x, y) - Ground) * scale;
            right[i] = Ground + (new Vector2(800 - x, y) - Ground) * scale;
        }

        canvas.FillStrip(left, right, color);
    }

    /// <summary>Після вибуху шапка повільно пульсує: 1,8 с туди й 1,8 с назад.</summary>
    private static Vector2 CapBreathing(float t)
    {
        const float start = 1.5f;
        const float halfPeriod = 1.8f;
        if (t < start)
        {
            return Vector2.One;
        }

        var phase = (t - start) / halfPeriod % 2;
        var wave = phase < 1 ? phase : 2 - phase;
        var eased = (1 - MathF.Cos(wave * MathF.PI)) / 2;
        return new Vector2(1 + 0.05f * eased, 1 + 0.03f * eased);
    }

    private static float Bezier(float p0, float p1, float p2, float p3, float u)
    {
        var v = 1 - u;
        return v * v * v * p0 + 3 * v * v * u * p1 + 3 * v * u * u * p2 + u * u * u * p3;
    }

    private static float CubicEaseOut(float progress)
    {
        var p = 1 - Math.Clamp(progress, 0f, 1f);
        return 1 - p * p * p;
    }

    private static float QuadraticEaseOut(float progress)
    {
        var p = 1 - Math.Clamp(progress, 0f, 1f);
        return 1 - p * p;
    }
}
