using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Pongheimer.Rendering;

public enum TextAlign
{
    Left,
    Center,
    Right,
}

/// <summary>
/// Полотно для всього, що малює гра: пласкі фігури (трикутниками, зі згладжуванням MSAA) і текст.
/// Координати задаються в поточному просторі — ігрових одиницях поля чи одиницях інтерфейсу —
/// який встановлює <see cref="SetSpace"/>; у пікселі їх переводить саме полотно.
/// Фігури й текст можна викликати впереміш: порядок малювання зберігається.
/// </summary>
public sealed class Canvas : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly BasicEffect _effect;
    private readonly SpriteBatch _sprites;
    private readonly TextRenderer _text;
    private readonly RasterizerState _rasterizer = new() { CullMode = CullMode.None, MultiSampleAntiAlias = true };
    private readonly RasterizerState _clippedRasterizer = new()
    {
        CullMode = CullMode.None,
        MultiSampleAntiAlias = true,
        ScissorTestEnable = true,
    };

    private VertexPositionColor[] _vertices = new VertexPositionColor[6144];
    private int _vertexCount;
    private bool _spritesOpen;
    private float _scale = 1;
    private Vector2 _offset;
    private Rectangle? _clip;

    // Робочі буфери для контурів, щоб не виділяти пам'ять щокадру.
    private Vector2[] _outline = new Vector2[256];
    private Vector2[] _innerOutline = new Vector2[256];

    public Canvas(GraphicsDevice device, TextRenderer text)
    {
        _device = device;
        _text = text;
        _sprites = new SpriteBatch(device);
        _effect = new BasicEffect(device) { VertexColorEnabled = true };
    }

    public void Begin(int width, int height)
    {
        _effect.Projection = Matrix.CreateOrthographicOffCenter(0, width, height, 0, 0, 1);
        _scale = 1;
        _offset = Vector2.Zero;
        _clip = null;
    }

    public void End()
    {
        Flush();
        _text.EndFrame();
    }

    /// <summary>Задає простір координат: скільки пікселів в одній одиниці і де її початок.</summary>
    public void SetSpace(float scale, Vector2 offset)
    {
        _scale = scale;
        _offset = offset;
    }

    /// <summary>Обмежує малювання прямокутником у пікселях; null знімає обмеження.</summary>
    public void SetClip(Rectangle? pixels)
    {
        Flush();
        _clip = pixels;
    }

    public void FillRect(float x, float y, float width, float height, Color color)
    {
        var a = ToPixels(x, y);
        var b = ToPixels(x + width, y + height);
        AddQuad(a, new Vector2(b.X, a.Y), b, new Vector2(a.X, b.Y), color);
    }

    public void FillRoundedRect(float x, float y, float width, float height, float radius, Color color)
    {
        var count = BuildRoundedRect(ref _outline, x, y, width, height, radius);
        FillConvex(_outline, count, color);
    }

    public void StrokeRoundedRect(float x, float y, float width, float height, float radius, float thickness, Color color)
    {
        var count = BuildRoundedRect(ref _outline, x, y, width, height, radius);
        BuildRoundedRect(
            ref _innerOutline,
            x + thickness,
            y + thickness,
            width - thickness * 2,
            height - thickness * 2,
            Math.Max(radius - thickness, 0),
            cornerSegments: (count / 4) - 1);
        FillBetween(_outline, _innerOutline, count, color);
    }

    public void FillEllipse(float centerX, float centerY, float radiusX, float radiusY, Color color)
    {
        var count = BuildEllipse(ref _outline, centerX, centerY, radiusX, radiusY, segments: 0);
        FillConvex(_outline, count, color);
    }

    public void StrokeEllipse(float centerX, float centerY, float radiusX, float radiusY, float thickness, Color color)
    {
        var count = BuildEllipse(ref _outline, centerX, centerY, radiusX, radiusY, segments: 0);
        BuildEllipse(
            ref _innerOutline,
            centerX,
            centerY,
            Math.Max(radiusX - thickness, 0),
            Math.Max(radiusY - thickness, 0),
            segments: count);
        FillBetween(_outline, _innerOutline, count, color);
    }

    /// <summary>Зафарбовує смугу між двома ламаними з однаковою кількістю точок (у поточному просторі).</summary>
    public void FillStrip(ReadOnlySpan<Vector2> left, ReadOnlySpan<Vector2> right, Color color)
    {
        for (var i = 0; i + 1 < left.Length && i + 1 < right.Length; i++)
        {
            AddQuad(ToPixels(left[i]), ToPixels(right[i]), ToPixels(right[i + 1]), ToPixels(left[i + 1]), color);
        }
    }

    /// <summary>Розмір рядка в одиницях поточного простору.</summary>
    public Vector2 MeasureText(string text, float size, TextStyle style)
    {
        var rendered = _text.Get(text, PixelSize(size), style);
        return new Vector2(rendered.Width, rendered.Height) / _scale;
    }

    /// <summary>Малює один рядок; <paramref name="x"/> — лівий край, центр або правий край залежно від вирівнювання.</summary>
    public void DrawText(
        string text,
        float x,
        float y,
        float size,
        TextStyle style,
        Color color,
        TextAlign align = TextAlign.Left)
    {
        var rendered = _text.Get(text, PixelSize(size), style);
        if (rendered.Texture is null)
        {
            return;
        }

        var anchor = ToPixels(x, y);
        var left = align switch
        {
            TextAlign.Center => anchor.X - rendered.Width / 2f,
            TextAlign.Right => anchor.X - rendered.Width,
            _ => anchor.X,
        };

        // Цілі пікселі — інакше літери розмиваються.
        var position = new Vector2(MathF.Round(left) - rendered.TextureOffset, MathF.Round(anchor.Y));
        OpenSprites();
        _sprites.Draw(rendered.Texture, position, color);
    }

    /// <summary>
    /// Малює текст із кількох рядків (розділених \n) по центру навколо <paramref name="centerX"/>,
    /// переносячи слова, якщо рядок ширший за <paramref name="maxWidth"/>. Повертає висоту блоку.
    /// </summary>
    public float DrawTextBlock(
        string text,
        float centerX,
        float top,
        float size,
        TextStyle style,
        Color color,
        float maxWidth = float.MaxValue,
        float outline = 0,
        Color outlineColor = default)
    {
        var lineHeight = MeasureText("Ag", size, style).Y;
        var y = top;
        foreach (var paragraph in text.Split('\n'))
        {
            foreach (var line in Wrap(paragraph, size, style, maxWidth))
            {
                if (outline > 0)
                {
                    // Темний контур із восьми зсунутих копій: напис читається на будь-якому тлі.
                    for (var i = 0; i < 8; i++)
                    {
                        var angle = i * MathF.PI / 4;
                        DrawText(
                            line,
                            centerX + MathF.Cos(angle) * outline,
                            y + MathF.Sin(angle) * outline,
                            size,
                            style,
                            outlineColor,
                            TextAlign.Center);
                    }
                }

                DrawText(line, centerX, y, size, style, color, TextAlign.Center);
                y += lineHeight;
            }
        }

        return y - top;
    }

    public void Dispose()
    {
        _effect.Dispose();
        _sprites.Dispose();
        _rasterizer.Dispose();
        _clippedRasterizer.Dispose();
    }

    private int PixelSize(float size) => (int)MathF.Round(size * _scale);

    private Vector2 ToPixels(float x, float y) => new(x * _scale + _offset.X, y * _scale + _offset.Y);

    private Vector2 ToPixels(Vector2 point) => point * _scale + _offset;

    private IEnumerable<string> Wrap(string paragraph, float size, TextStyle style, float maxWidth)
    {
        if (maxWidth == float.MaxValue || MeasureText(paragraph, size, style).X <= maxWidth)
        {
            yield return paragraph;
            yield break;
        }

        var line = "";
        foreach (var word in paragraph.Split(' '))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && MeasureText(candidate, size, style).X > maxWidth)
            {
                yield return line;
                line = word;
            }
            else
            {
                line = candidate;
            }
        }

        if (line.Length > 0)
        {
            yield return line;
        }
    }

    /// <summary>Контур прямокутника із заокругленими кутами, за годинниковою стрілкою, у пікселях.</summary>
    private int BuildRoundedRect(
        ref Vector2[] target,
        float x,
        float y,
        float width,
        float height,
        float radius,
        int cornerSegments = -1)
    {
        width = Math.Max(width, 0);
        height = Math.Max(height, 0);
        radius = Math.Clamp(radius, 0, Math.Min(width, height) / 2);
        if (cornerSegments < 0)
        {
            cornerSegments = Math.Clamp((int)MathF.Ceiling(radius * _scale / 1.5f), 2, 16);
        }

        var count = (cornerSegments + 1) * 4;
        EnsureCapacity(ref target, count);

        // Центри чотирьох кутових дуг, починаючи з лівого верхнього.
        Span<Vector2> centers =
        [
            new(x + radius, y + radius),
            new(x + width - radius, y + radius),
            new(x + width - radius, y + height - radius),
            new(x + radius, y + height - radius),
        ];
        var index = 0;
        for (var corner = 0; corner < 4; corner++)
        {
            var startAngle = MathF.PI + corner * MathF.PI / 2;
            for (var i = 0; i <= cornerSegments; i++)
            {
                var angle = startAngle + i * (MathF.PI / 2) / cornerSegments;
                target[index++] = ToPixels(
                    centers[corner].X + MathF.Cos(angle) * radius,
                    centers[corner].Y + MathF.Sin(angle) * radius);
            }
        }

        return count;
    }

    private int BuildEllipse(ref Vector2[] target, float centerX, float centerY, float radiusX, float radiusY, int segments)
    {
        if (segments <= 0)
        {
            // Що більша фігура на екрані, то більше відрізків, щоб край лишався гладким.
            var pixelRadius = Math.Max(radiusX, radiusY) * _scale;
            segments = Math.Clamp((int)MathF.Ceiling(pixelRadius * MathF.Tau / 4), 12, 128);
        }

        EnsureCapacity(ref target, segments);
        for (var i = 0; i < segments; i++)
        {
            var angle = i * MathF.Tau / segments;
            target[i] = ToPixels(centerX + MathF.Cos(angle) * radiusX, centerY + MathF.Sin(angle) * radiusY);
        }

        return segments;
    }

    private static void EnsureCapacity(ref Vector2[] buffer, int count)
    {
        if (buffer.Length < count)
        {
            buffer = new Vector2[count * 2];
        }
    }

    private void FillConvex(Vector2[] outline, int count, Color color)
    {
        for (var i = 1; i + 1 < count; i++)
        {
            AddTriangle(outline[0], outline[i], outline[i + 1], color);
        }
    }

    private void FillBetween(Vector2[] outer, Vector2[] inner, int count, Color color)
    {
        for (var i = 0; i < count; i++)
        {
            var next = (i + 1) % count;
            AddQuad(outer[i], outer[next], inner[next], inner[i], color);
        }
    }

    private void AddQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
    {
        AddTriangle(a, b, c, color);
        AddTriangle(a, c, d, color);
    }

    private void AddTriangle(Vector2 a, Vector2 b, Vector2 c, Color color)
    {
        if (_spritesOpen)
        {
            _sprites.End();
            _spritesOpen = false;
        }

        if (_vertexCount + 3 > _vertices.Length)
        {
            Array.Resize(ref _vertices, _vertices.Length * 2);
        }

        _vertices[_vertexCount++] = new VertexPositionColor(new Vector3(a, 0), color);
        _vertices[_vertexCount++] = new VertexPositionColor(new Vector3(b, 0), color);
        _vertices[_vertexCount++] = new VertexPositionColor(new Vector3(c, 0), color);
    }

    private void OpenSprites()
    {
        if (_spritesOpen)
        {
            return;
        }

        FlushShapes();
        ApplyClip();
        _sprites.Begin(
            SpriteSortMode.Deferred,
            BlendState.AlphaBlend,
            SamplerState.PointClamp,
            DepthStencilState.None,
            _clip is null ? _rasterizer : _clippedRasterizer);
        _spritesOpen = true;
    }

    private void Flush()
    {
        if (_spritesOpen)
        {
            _sprites.End();
            _spritesOpen = false;
        }

        FlushShapes();
    }

    private void FlushShapes()
    {
        if (_vertexCount == 0)
        {
            return;
        }

        ApplyClip();
        _device.BlendState = BlendState.AlphaBlend;
        _device.DepthStencilState = DepthStencilState.None;
        _device.RasterizerState = _clip is null ? _rasterizer : _clippedRasterizer;
        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawUserPrimitives(PrimitiveType.TriangleList, _vertices, 0, _vertexCount / 3);
        }

        _vertexCount = 0;
    }

    private void ApplyClip()
    {
        if (_clip is { } clip)
        {
            _device.ScissorRectangle = Rectangle.Intersect(clip, _device.Viewport.Bounds);
        }
    }
}
