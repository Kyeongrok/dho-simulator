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

    public Pointer Pointer;

    public Canvas(Gfx gfx)
    {
        _gfx = gfx;
        _brush = gfx.D2D.CreateSolidColorBrush(White);
    }

    public float Width => _gfx.Width;
    public float Height => _gfx.Height;

    public void Begin() => _gfx.D2D.BeginDraw();
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
        _brush.Dispose();
    }
}
