using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Graphics;
using Gdi = System.Drawing;
using XnaColor = Microsoft.Xna.Framework.Color;

namespace Pongheimer.Rendering;

public enum TextStyle
{
    Regular,
    Semibold,
    Bold,
    Monospace,
}

/// <summary>Готовий до малювання рядок: біла текстура з прозорістю, яку рендер тонує потрібним кольором.</summary>
public sealed class RenderedText
{
    public RenderedText(Texture2D? texture, int width, int height)
    {
        Texture = texture;
        Width = width;
        Height = height;
    }

    /// <summary>Null для порожнього рядка.</summary>
    public Texture2D? Texture { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>На скільки пікселів лівий край тексту зсунутий усередині текстури.</summary>
    public int TextureOffset { get; init; }

    internal long LastUsedFrame { get; set; }
}

/// <summary>
/// Перетворює текст на текстури системними шрифтами Windows (через GDI+), тож гра не возить із собою
/// файли шрифтів і однаково добре показує кирилицю, латиницю та стрілки. Рядки кешуються:
/// текст у грі майже не змінюється, і після першого кадру малювання — це просто показ готової картинки.
/// </summary>
public sealed class TextRenderer : IDisposable
{
    private const int PurgeIntervalFrames = 300;
    private const int UnusedLifetimeFrames = 900;

    private readonly GraphicsDevice _device;
    private readonly Dictionary<(string Text, int Size, TextStyle Style), RenderedText> _cache = [];
    private readonly Dictionary<(int Size, TextStyle Style), Gdi.Font> _fonts = [];
    private readonly Gdi.Bitmap _measureSurface = new(1, 1);
    private readonly Gdi.Graphics _measure;
    private readonly Gdi.StringFormat _format;
    private long _frame;

    public TextRenderer(GraphicsDevice device)
    {
        _device = device;
        _measure = Gdi.Graphics.FromImage(_measureSurface);

        // «Типографський» формат міряє рядок без полів, які GDI+ зазвичай додає з боків.
        _format = new Gdi.StringFormat(Gdi.StringFormat.GenericTypographic);
        _format.FormatFlags |= Gdi.StringFormatFlags.MeasureTrailingSpaces | Gdi.StringFormatFlags.NoWrap;
    }

    public RenderedText Get(string text, int pixelSize, TextStyle style)
    {
        pixelSize = Math.Clamp(pixelSize, 4, 400);
        var key = (text, pixelSize, style);
        if (!_cache.TryGetValue(key, out var rendered))
        {
            rendered = Render(text, pixelSize, style);
            _cache[key] = rendered;
        }

        rendered.LastUsedFrame = _frame;
        return rendered;
    }

    /// <summary>Викликається раз на кадр; час від часу звільняє рядки, які давно не показувались.</summary>
    public void EndFrame()
    {
        _frame++;
        if (_frame % PurgeIntervalFrames != 0)
        {
            return;
        }

        foreach (var (key, rendered) in _cache.Where(e => _frame - e.Value.LastUsedFrame > UnusedLifetimeFrames).ToList())
        {
            rendered.Texture?.Dispose();
            _cache.Remove(key);
        }
    }

    public void Dispose()
    {
        foreach (var rendered in _cache.Values)
        {
            rendered.Texture?.Dispose();
        }

        foreach (var font in _fonts.Values)
        {
            font.Dispose();
        }

        _cache.Clear();
        _fonts.Clear();
        _format.Dispose();
        _measure.Dispose();
        _measureSurface.Dispose();
    }

    private Gdi.Font FontFor(int pixelSize, TextStyle style)
    {
        if (!_fonts.TryGetValue((pixelSize, style), out var font))
        {
            // Якщо потрібного шрифту в системі немає, GDI+ сам підставить найближчий.
            font = style switch
            {
                TextStyle.Bold => new Gdi.Font("Segoe UI", pixelSize, Gdi.FontStyle.Bold, Gdi.GraphicsUnit.Pixel),
                TextStyle.Semibold => new Gdi.Font("Segoe UI Semibold", pixelSize, Gdi.FontStyle.Regular, Gdi.GraphicsUnit.Pixel),
                TextStyle.Monospace => new Gdi.Font("Consolas", pixelSize, Gdi.FontStyle.Regular, Gdi.GraphicsUnit.Pixel),
                _ => new Gdi.Font("Segoe UI", pixelSize, Gdi.FontStyle.Regular, Gdi.GraphicsUnit.Pixel),
            };
            _fonts[(pixelSize, style)] = font;
        }

        return font;
    }

    private RenderedText Render(string text, int pixelSize, TextStyle style)
    {
        var font = FontFor(pixelSize, style);
        var height = (int)Math.Ceiling(font.GetHeight(_measure));
        if (text.Length == 0)
        {
            return new RenderedText(null, 0, height);
        }

        var measured = _measure.MeasureString(text, font, int.MaxValue, _format);
        var width = (int)Math.Ceiling(measured.Width);

        // Запас у піксель з кожного боку: згладжені краї літер трохи виходять за виміряну ширину.
        const int padding = 1;
        var bitmapWidth = Math.Clamp(width + padding * 2, 1, 4096);
        using var bitmap = new Gdi.Bitmap(bitmapWidth, height, PixelFormat.Format32bppArgb);
        using (var graphics = Gdi.Graphics.FromImage(bitmap))
        {
            graphics.Clear(Gdi.Color.Transparent);
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.DrawString(text, font, Gdi.Brushes.White, padding, 0, _format);
        }

        var area = new Gdi.Rectangle(0, 0, bitmapWidth, height);
        var locked = bitmap.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var pixels = new XnaColor[bitmapWidth * height];
        try
        {
            var row = new byte[bitmapWidth * 4];
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(locked.Scan0 + y * locked.Stride, row, 0, row.Length);
                for (var x = 0; x < bitmapWidth; x++)
                {
                    // Літери білі, тож після множення на альфу всі канали дорівнюють самій альфі.
                    var alpha = row[x * 4 + 3];
                    pixels[y * bitmapWidth + x] = new XnaColor(alpha, alpha, alpha, alpha);
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(locked);
        }

        var texture = new Texture2D(_device, bitmapWidth, height, false, SurfaceFormat.Color);
        texture.SetData(pixels);
        return new RenderedText(texture, width, height) { TextureOffset = padding };
    }
}
