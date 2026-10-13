using Microsoft.Xna.Framework;
using Pongheimer.Core.Game;
using Pongheimer.Localization;
using Pongheimer.Match;

namespace Pongheimer.Rendering;

/// <summary>Малює ігрове поле в його власних координатах 800×450: розмітку, рахунок, ракетки, м'яч, вибух і написи.</summary>
public static class FieldRenderer
{
    private const float Width = (float)GameRules.FieldWidth;
    private const float Height = (float)GameRules.FieldHeight;
    private const float LeftScoreX = 300;
    private const float RightScoreX = 500;

    public static void Draw(Canvas canvas, ScreenLayout layout, MatchController match, Texts texts)
    {
        var state = match.State;
        canvas.SetSpace(layout.FieldScale, layout.FieldOffset);

        // Усе, що вилітає за край поля (м'яч після гола, бомби, пил вибуху), обрізається по його межі.
        canvas.SetClip(layout.FieldPixels);
        canvas.FillRect(0, 0, Width, Height, Palette.Field);

        DrawCenterLine(canvas);
        canvas.DrawText(state.ScoreLeft.ToString(), LeftScoreX, 16, 56, TextStyle.Bold, Palette.Muted, TextAlign.Center);
        canvas.DrawText(state.ScoreRight.ToString(), RightScoreX, 16, 56, TextStyle.Bold, Palette.Muted, TextAlign.Center);

        if (state.Nuclear is { } nuclear)
        {
            DrawShields(canvas, nuclear);
        }

        canvas.FillRoundedRect(
            (float)GameRules.LeftPaddleX,
            (float)state.LeftPaddleY,
            (float)GameRules.PaddleWidth,
            (float)state.LeftPaddleHeight,
            3,
            Palette.LeftPlayer);
        canvas.FillRoundedRect(
            (float)GameRules.RightPaddleX,
            (float)state.RightPaddleY,
            (float)GameRules.PaddleWidth,
            (float)state.RightPaddleHeight,
            3,
            Palette.RightPlayer);

        if (state.Nuclear is not null)
        {
            DrawBombs(canvas, state.Nuclear);
        }

        var ballRadius = (float)state.BallSize / 2;
        canvas.FillEllipse(
            (float)state.BallX + ballRadius,
            (float)state.BallY + ballRadius,
            ballRadius,
            ballRadius,
            Palette.Bright);

        DrawImpacts(canvas, match, state);

        if (state.Nuclear is not null)
        {
            DrawBlasts(canvas, layout, match, texts);
            DrawActiveEffects(canvas, state.Nuclear, texts);
        }

        if (state.Phase == GamePhase.GameOver)
        {
            ExplosionRenderer.Draw(canvas, match.GameOverSeconds);
        }

        // Напис завжди поверх вибуху; темний контур тримає його читабельним на яскравому тлі.
        var banner = match.Banner(texts);
        if (banner.Length > 0)
        {
            canvas.DrawTextBlock(
                banner,
                Width / 2,
                170,
                32,
                TextStyle.Bold,
                Palette.Bright,
                outline: 2.5f,
                outlineColor: Palette.Window.WithOpacity(0.55f));
        }

        canvas.DrawText(match.Hint(texts), Width / 2, 426, 12, TextStyle.Regular, Palette.Muted, TextAlign.Center);
        canvas.SetClip(null);
    }

    private static void DrawCenterLine(Canvas canvas)
    {
        const float thickness = 3;
        const float dash = 12;
        for (var y = 0f; y < Height; y += dash * 2)
        {
            canvas.FillRect(Width / 2 - thickness / 2, y, thickness, dash, Palette.Line);
        }
    }

    /// <summary>
    /// Удар об ракетку: вона на мить спалахує, від точки удару розходиться кільце, а в поле летять іскри
    /// кольору того, хто відбив. Усе гасне за третину секунди, щоб не відволікати від м'яча.
    /// </summary>
    private static void DrawImpacts(Canvas canvas, MatchController match, GameState state)
    {
        const float paddleWidth = (float)GameRules.PaddleWidth;
        const int sparkCount = 7;
        const float sparkSpread = MathF.PI * 2 / 3;

        foreach (var impact in match.Impacts)
        {
            var progress = (float)Math.Clamp(impact.Age / Impact.Lifetime, 0, 1);
            var fade = 1 - progress;
            var reach = 1 - fade * fade;
            var isLeft = impact.Side == Side.Left;
            var color = isLeft ? Palette.LeftPlayer : Palette.RightPlayer;
            var heading = isLeft ? 1 : -1;

            // Точка удару — на грані ракетки, на висоті м'яча. Відскок помічаємо на кадр-два пізніше,
            // коли м'яч уже трохи відлетів, тому висоту тримаємо в межах самої ракетки.
            var paddleX = (float)(isLeft ? GameRules.LeftPaddleX : GameRules.RightPaddleX);
            var paddleY = (float)(isLeft ? state.LeftPaddleY : state.RightPaddleY);
            var paddleHeight = (float)state.PaddleHeight(impact.Side);
            var x = isLeft ? paddleX + paddleWidth : paddleX;
            var y = Math.Clamp((float)impact.Y, paddleY + 3, paddleY + paddleHeight - 3);
            canvas.FillRoundedRect(
                paddleX, paddleY, paddleWidth, paddleHeight, 3, Palette.Bright.WithOpacity(0.6f * fade * fade));

            var ring = 5 + 20 * reach;
            canvas.StrokeEllipse(x, y, ring, ring, 0.6f + 2 * fade, color.WithOpacity(0.75f * fade));

            for (var i = 0; i < sparkCount; i++)
            {
                // Віяло іскор у бік поля; сусідні летять на різну відстань, щоб віяло не було «лінійкою».
                var angle = -sparkSpread / 2 + i * sparkSpread / (sparkCount - 1);
                var distance = (i % 2 == 0 ? 40 : 26) * reach + 6;
                var size = 0.5f + 2.2f * fade;
                canvas.FillEllipse(
                    x + heading * MathF.Cos(angle) * distance,
                    y + MathF.Sin(angle) * distance,
                    size,
                    size,
                    (i % 2 == 0 ? color : Palette.Bright).WithOpacity(fade));
            }
        }
    }

    /// <summary>Щит — світна смуга за ракеткою, на тому самому місці, де м'яч від нього відіб'ється.</summary>
    private static void DrawShields(Canvas canvas, NuclearSnapshot nuclear)
    {
        const float thickness = 4;
        var inset = (float)NuclearRules.ShieldInset;
        if (nuclear.Left.HasShield)
        {
            canvas.FillRoundedRect(inset - thickness, 4, thickness, Height - 8, 2, Palette.LeftPlayer.WithOpacity(0.85f));
        }

        if (nuclear.Right.HasShield)
        {
            canvas.FillRoundedRect(Width - inset, 4, thickness, Height - 8, 2, Palette.RightPlayer.WithOpacity(0.85f));
        }
    }

    private static void DrawBombs(Canvas canvas, NuclearSnapshot nuclear)
    {
        foreach (var bomb in nuclear.Bombs)
        {
            DrawBomb(canvas, bomb);
        }
    }

    /// <summary>Авіабомба носом у бік руху; колір корпусу каже, що всередині — добро, шкода чи сюрприз для м'яча.</summary>
    private static void DrawBomb(Canvas canvas, Bomb bomb)
    {
        var x = (float)bomb.X;
        var y = (float)bomb.Y;
        var radius = (float)NuclearRules.BombRadius;
        var heading = bomb.VelocityY >= 0 ? 1 : -1;
        var color = Palette.For(NuclearRules.CategoryOf(bomb.Kind));

        // Стабілізатор на хвості, потім корпус поверх нього.
        var tailY = y - heading * radius;
        canvas.FillRoundedRect(x - 7, tailY - 3, 14, 6, 1.5f, color.WithOpacity(0.75f));
        canvas.FillRect(x - 2, Math.Min(tailY, tailY + heading * 6), 4, 6, color.WithOpacity(0.75f));
        canvas.FillEllipse(x, y + heading * 1.5f, radius * 0.72f, radius * 0.92f, color);

        // Позначка на корпусі.
        canvas.FillEllipse(x, y + heading * 1.5f, 4.2f, 4.2f, Palette.Field);
        canvas.FillEllipse(x, y + heading * 1.5f, 1.6f, 1.6f, color);
    }

    /// <summary>Маленький гриб на місці підірваної бомби й назва того, що з неї випало.</summary>
    private static void DrawBlasts(Canvas canvas, ScreenLayout layout, MatchController match, Texts texts)
    {
        foreach (var blast in match.Blasts)
        {
            var progress = (float)(blast.Age / Blast.Lifetime);
            var fade = Math.Clamp((1 - progress) * 2.5f, 0f, 1f);
            var x = (float)blast.Detonation.X;
            var y = (float)blast.Detonation.Y;

            // Той самий гриб, що й у фіналі, зменшений і поставлений «землею» трохи нижче центру бомби.
            const float miniature = 0.14f;
            var scale = layout.FieldScale * miniature;
            var ground = layout.FieldOffset + new Vector2(x, y + 22) * layout.FieldScale;
            canvas.SetSpace(scale, ground - new Vector2(400, 450) * scale);
            ExplosionRenderer.Draw(canvas, blast.Age * 1.6, withFlash: false, opacity: fade);
            canvas.SetSpace(layout.FieldScale, layout.FieldOffset);

            var color = Palette.For(NuclearRules.CategoryOf(blast.Detonation.Kind));
            canvas.DrawTextBlock(
                texts.BonusName(blast.Detonation.Kind),
                Math.Clamp(x, 90, Width - 90),
                Math.Max(y - 58 - progress * 14, 4),
                13,
                TextStyle.Semibold,
                color.WithOpacity(fade),
                outline: 1.5f,
                outlineColor: Palette.Window.WithOpacity(0.5f * fade));
        }
    }

    /// <summary>Під рахунком кожного гравця — що на нього зараз діє і скільки ще; по центру — ефекти м'яча.</summary>
    private static void DrawActiveEffects(Canvas canvas, NuclearSnapshot nuclear, Texts texts)
    {
        DrawSideEffects(canvas, nuclear.Left, LeftScoreX, Palette.LeftPlayer, texts);
        DrawSideEffects(canvas, nuclear.Right, RightScoreX, Palette.RightPlayer, texts);

        var y = 6f;
        DrawEffect(canvas, nuclear.BallSize, Width / 2, ref y, texts, withBackground: true);
        DrawEffect(canvas, nuclear.BallSpeed, Width / 2, ref y, texts, withBackground: true);
    }

    private static void DrawSideEffects(Canvas canvas, SideEffects effects, float x, Color sideColor, Texts texts)
    {
        if (effects.HasDoublePoint)
        {
            canvas.DrawText("×2", x + 34, 22, 18, TextStyle.Bold, sideColor);
        }

        var y = 88f;
        DrawEffect(canvas, effects.Size, x, ref y, texts);
        DrawEffect(canvas, effects.Speed, x, ref y, texts);
        DrawEffect(canvas, effects.Freeze, x, ref y, texts);
        DrawEffect(canvas, effects.Inversion, x, ref y, texts);
    }

    private static void DrawEffect(
        Canvas canvas,
        TimedEffect effect,
        float centerX,
        ref float y,
        Texts texts,
        bool withBackground = false)
    {
        if (!effect.IsActive)
        {
            return;
        }

        const float barWidth = 84;
        const float rowHeight = 22;
        var color = Palette.For(NuclearRules.CategoryOf(effect.Kind));
        if (withBackground)
        {
            // Ефекти м'яча лежать поверх центральної лінії — підкладка, щоб вона не перекреслювала текст.
            canvas.FillRoundedRect(centerX - barWidth / 2 - 8, y - 2, barWidth + 16, rowHeight, 5, Palette.Field);
        }

        canvas.DrawText(texts.BonusName(effect.Kind), centerX, y, 11, TextStyle.Semibold, color, TextAlign.Center);

        // Смужка часу, що лишився.
        var duration = NuclearRules.DurationOf(effect.Kind);
        var remaining = duration > 0 ? (float)Math.Clamp(effect.Remaining / duration, 0, 1) : 0;
        canvas.FillRoundedRect(centerX - barWidth / 2, y + 15, barWidth, 2.5f, 1.25f, Palette.Line);
        canvas.FillRoundedRect(centerX - barWidth / 2, y + 15, barWidth * remaining, 2.5f, 1.25f, color);
        y += rowHeight;
    }
}
