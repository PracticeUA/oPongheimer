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

    public static void Draw(Canvas canvas, ScreenLayout layout, MatchController match, Texts texts)
    {
        var state = match.State;
        canvas.SetSpace(layout.FieldScale, layout.FieldOffset);

        // Усе, що вилітає за край поля (м'яч після гола, пил вибуху), обрізається по його межі.
        canvas.SetClip(layout.FieldPixels);
        canvas.FillRect(0, 0, Width, Height, Palette.Field);

        DrawCenterLine(canvas);
        canvas.DrawText(state.ScoreLeft.ToString(), 300, 16, 56, TextStyle.Bold, Palette.Muted, TextAlign.Center);
        canvas.DrawText(state.ScoreRight.ToString(), 500, 16, 56, TextStyle.Bold, Palette.Muted, TextAlign.Center);

        canvas.FillRoundedRect(
            (float)GameRules.LeftPaddleX,
            (float)state.LeftPaddleY,
            (float)GameRules.PaddleWidth,
            (float)GameRules.PaddleHeight,
            3,
            Palette.LeftPlayer);
        canvas.FillRoundedRect(
            (float)GameRules.RightPaddleX,
            (float)state.RightPaddleY,
            (float)GameRules.PaddleWidth,
            (float)GameRules.PaddleHeight,
            3,
            Palette.RightPlayer);

        var ballRadius = (float)GameRules.BallSize / 2;
        canvas.FillEllipse(
            (float)state.BallX + ballRadius,
            (float)state.BallY + ballRadius,
            ballRadius,
            ballRadius,
            Palette.Bright);

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
}
