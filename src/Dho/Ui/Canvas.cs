using System.Numerics;
using Dho.Render;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dho.Ui;

/// <summary>이번 프레임의 마우스 — 단추가 스스로 눌렸는지 가린다.</summary>
internal struct Pointer
{
    public float X, Y;
    public bool Clicked;      // 이번 프레임에 왼쪽 단추를 뗐다
    public bool Consumed;     // 어느 창이 이미 받아 갔다
}

/// <summary>
/// 화면 위에 창·단추·글을 그리는 Direct2D 붓. 그 자리에서 그리고 그 자리에서 눌림을 가리는 방식이다.
/// </summary>
internal sealed class Canvas : IDisposable
{
    public static readonly Color4 PanelFill = new(0.05f, 0.09f, 0.30f, 0.86f);
    public static readonly Color4 PanelEdge = new(0.80f, 0.68f, 0.36f, 1f);
    public static readonly Color4 White = new(1f, 1f, 1f, 1f);
    public static readonly Color4 Dim = new(0.75f, 0.78f, 0.88f, 1f);
    public static readonly Color4 Gold = new(1f, 0.86f, 0.45f, 1f);

    private readonly Gfx _gfx;
    private readonly ID2D1SolidColorBrush _brush;
    private readonly Dictionary<(float, bool, int), IDWriteTextFormat> _formats = new();
    private readonly Dictionary<string, ID2D1Bitmap?> _images = new();

    public Pointer Pointer;

    public Canvas(Gfx gfx)
    {
        _gfx = gfx;
        _brush = gfx.D2D.CreateSolidColorBrush(White);
    }

    /// <summary>위에서 비워 두는 높이(제목 줄). 그 밑이 y = 0 이 되고, 제목 줄은 y 가 음수인 자리에 그린다.</summary>
    public float Top;

    /// <summary>화면 글과 창의 배율 — 4K 처럼 촘촘한 화면에서 키운다. 너비·높이·마우스는 모두 이 배율로 나눈 값이다.</summary>
    public float Scale = 1;

    public float Width => _gfx.Width / Scale;
    public float Height => _gfx.Height / Scale - Top;

    public void Begin()
    {
        _gfx.D2D.BeginDraw();
        _gfx.D2D.Transform = Matrix3x2.CreateTranslation(0, Top) * Matrix3x2.CreateScale(Scale);
    }
    public void End() => _gfx.D2D.EndDraw();

    public void Fill(float x, float y, float w, float h, Color4 color)
    {
        _brush.Color = color;
        _gfx.D2D.FillRectangle(new Rect(x, y, w, h), _brush);
    }

    public void Frame(float x, float y, float w, float h, Color4 color, float stroke = 1.5f)
    {
        _brush.Color = color;
        _gfx.D2D.DrawRectangle(new Rect(x, y, w, h), _brush, stroke);
    }

    public void Line(float x0, float y0, float x1, float y1, Color4 color, float stroke = 1.5f)
    {
        _brush.Color = color;
        _gfx.D2D.DrawLine(new Vector2(x0, y0), new Vector2(x1, y1), _brush, stroke);
    }

    public void Circle(float x, float y, float radius, Color4 color, bool filled = true, float stroke = 1.5f)
    {
        _brush.Color = color;
        var ellipse = new Ellipse(new Vector2(x, y), radius, radius);
        if (filled) _gfx.D2D.FillEllipse(ellipse, _brush);
        else _gfx.D2D.DrawEllipse(ellipse, _brush, stroke);
    }

    /// <summary>
    /// BGRA 그림을 그린다. <paramref name="key"/> 로 한 번만 올려 두고 다시 쓴다. 그림이 없으면 false.
    /// </summary>
    /// <param name="angle">가운데를 축으로 돌리는 각(라디안, 시계 방향).</param>
    /// <param name="refresh">참이면 올려 둔 것을 버리고 다시 올린다 — 프레임마다 바뀌는 그림(주변 지도).</param>
    public bool Image(string key, Func<(int Width, int Height, byte[] Bgra)?> load, float x, float y, float w, float h,
                      float angle = 0, bool refresh = false, float opacity = 1)
    {
        if (refresh && _images.Remove(key, out var old)) old?.Dispose();
        if (angle != 0)
        {
            var before = _gfx.D2D.Transform;
            _gfx.D2D.Transform = Matrix3x2.CreateRotation(angle, new Vector2(x + w / 2, y + h / 2)) * before;
            bool drawn = Image(key, load, x, y, w, h, 0, false, opacity);
            _gfx.D2D.Transform = before;
            return drawn;
        }
        if (!_images.TryGetValue(key, out var bitmap))
        {
            bitmap = null;
            try
            {
                if (load() is { } image)
                {
                    // Direct2D 는 알파를 미리 곱한 색을 받는다
                    var pixels = image.Bgra;
                    for (int i = 0; i < pixels.Length; i += 4)
                    {
                        int a = pixels[i + 3];
                        pixels[i] = (byte)(pixels[i] * a / 255);
                        pixels[i + 1] = (byte)(pixels[i + 1] * a / 255);
                        pixels[i + 2] = (byte)(pixels[i + 2] * a / 255);
                    }
                    unsafe
                    {
                        fixed (byte* data = pixels)
                            bitmap = _gfx.D2D.CreateBitmap(new SizeI(image.Width, image.Height), (IntPtr)data, (uint)image.Width * 4,
                                new BitmapProperties(new Vortice.DCommon.PixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied)));
                    }
                }
            }
            catch (Exception) { bitmap = null; }      // 게임 폴더에 그림이 없어도 창은 뜬다
            _images[key] = bitmap;
        }
        if (bitmap == null) return false;
        _gfx.D2D.DrawBitmap(bitmap, new Rect(x, y, w, h), opacity, BitmapInterpolationMode.Linear, null);
        return true;
    }

    /// <summary>창 바탕 — 짙은 남색에 금빛 테두리.</summary>
    public void Panel(float x, float y, float w, float h)
    {
        Fill(x, y, w, h, PanelFill);
        Frame(x, y, w, h, PanelEdge);
    }

    /// <param name="align">0 왼쪽, 1 가운데, 2 오른쪽.</param>
    public void Text(string text, float x, float y, float w, float h, float size, Color4 color,
                     int align = 0, bool bold = false, bool shadow = true)
    {
        text = Dho.Data.Korean.Particles(text);      // 「을(를)」 따위를 앞말의 받침에 맞춘다
        var format = Format(size, bold, align);
        if (shadow)
        {
            _brush.Color = new Color4(0, 0, 0, 0.8f);
            _gfx.D2D.DrawText(text, format, new Rect(x + 1.5f, y + 1.5f, w, h), _brush);
        }
        _brush.Color = color;
        _gfx.D2D.DrawText(text, format, new Rect(x, y, w, h), _brush);
    }

    public bool Hover(float x, float y, float w, float h) =>
        Pointer.X >= x && Pointer.X < x + w && Pointer.Y >= y && Pointer.Y < y + h;

    /// <summary>단추 하나. 이번 프레임에 눌렸으면 true.</summary>
    public bool Button(string label, float x, float y, float w, float h, bool enabled = true, float size = 15f)
    {
        bool hover = enabled && Hover(x, y, w, h);
        Fill(x, y, w, h, hover ? new Color4(0.22f, 0.32f, 0.66f, 0.95f) : new Color4(0.10f, 0.16f, 0.42f, 0.95f));
        Frame(x, y, w, h, hover ? Gold : PanelEdge, 1.2f);
        Text(label, x, y + (h - size * 1.35f) / 2, w, h, size, enabled ? White : new Color4(0.5f, 0.5f, 0.6f, 1), 1);

        if (hover && Pointer.Clicked)
        {
            Pointer.Consumed = true;
            Pointer.Clicked = false;            // 한 번의 클릭은 단추 하나만 누른다(새로 뜬 창의 단추까지 눌리지 않게)
            return true;
        }
        return false;
    }

    /// <summary>이 네모 안의 클릭은 뒤의 바다로 새지 않게 한다.</summary>
    public void Block(float x, float y, float w, float h)
    {
        if (Hover(x, y, w, h)) Pointer.Consumed = true;
    }

    private IDWriteTextFormat Format(float size, bool bold, int align)
    {
        if (_formats.TryGetValue((size, bold, align), out var format)) return format;
        format = _gfx.DWrite.CreateTextFormat("Malgun Gothic", bold ? FontWeight.Bold : FontWeight.Normal,
            FontStyle.Normal, FontStretch.Normal, size);
        format.TextAlignment = align switch { 1 => TextAlignment.Center, 2 => TextAlignment.Trailing, _ => TextAlignment.Leading };
        format.WordWrapping = WordWrapping.Wrap;
        return _formats[(size, bold, align)] = format;
    }

    public void Dispose()
    {
        foreach (var format in _formats.Values) format.Dispose();
        foreach (var image in _images.Values) image?.Dispose();
        _brush.Dispose();
    }
}
