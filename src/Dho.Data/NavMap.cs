using System.Buffers.Binary;
using System.IO.Compression;

namespace Dho.Data;

/// <summary>
/// 내비게이션 지도 — 이용자가 만든 세계 지도 그림(DHOMAP 지도 모음, 4,096 × 2,048 PNG)을 읽어 그 위에 내 자리를 찍는 데 쓴다.
/// 그림은 세계 좌표(16,384 × 8,192)를 꼭 4분의 1로 줄인 것이라 세계 좌표 ÷ 4 가 그림의 픽셀이다.
/// 그림은 저장소에 넣지 않는다(남이 만든 것) — <c>data\navmaps</c> 폴더나 바탕 화면의 「…DHOMAP…」 폴더에서 찾는다.
/// </summary>
public static class NavMap
{
    public const double Scale = 0.25;

    private static List<string>? _files;
    private static readonly Dictionary<int, (int Width, int Height, byte[] Bgra)?> Loaded = new();

    /// <summary>찾은 지도 그림들(이름 차례).</summary>
    public static List<string> Files()
    {
        if (_files != null) return _files;
        var folders = new List<string> { Path.Combine(GameData.FindDirectory(), "navmaps") };
        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            folders.AddRange(Directory.GetDirectories(desktop, "*DHOMAP*"));
        }
        catch (Exception) { }
        _files = [];
        foreach (string folder in folders)
            if (Directory.Exists(folder)) _files.AddRange(Directory.GetFiles(folder, "*.png"));
        // 「지도1」 · 「지도2」 … 「지도10」 — 숫자 차례로
        _files = _files.OrderBy(f => int.TryParse(new string(Path.GetFileName(f).SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray()), out int n) ? n : 999).ThenBy(f => f).ToList();
        return _files;
    }

    public static string NameOf(int index) => index >= 0 && index < Files().Count ? Path.GetFileNameWithoutExtension(Files()[index]) : "";

    /// <summary>그 지도의 한 네모(가로는 감긴다)를 떼어 낸다. 못 읽으면 null. 한 번에 한 장만 메모리에 둔다.</summary>
    public static (int Width, int Height, byte[] Bgra)? Crop(int index, int left, int top, int width, int height)
    {
        if (index < 0 || index >= Files().Count) return null;
        if (!Loaded.TryGetValue(index, out var map))
        {
            Loaded.Clear();
            try { map = ReadPng(File.ReadAllBytes(Files()[index])); } catch (Exception) { map = null; }
            Loaded[index] = map;
        }
        if (map is not { } full) return null;
        var crop = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            int sy = Math.Clamp(top + y, 0, full.Height - 1);
            for (int x = 0; x < width; x++)
            {
                int sx = ((left + x) % full.Width + full.Width) % full.Width;
                Buffer.BlockCopy(full.Bgra, (sy * full.Width + sx) * 4, crop, (y * width + x) * 4, 4);
            }
        }
        return (width, height, crop);
    }

    /// <summary>가장 작은 PNG 읽개 — 8비트 RGB · RGBA · 팔레트 · 회색, 얽지 않은(non-interlaced) 것만.</summary>
    public static (int Width, int Height, byte[] Bgra) ReadPng(byte[] file)
    {
        int width = 0, height = 0, depth = 0, kind = 0, interlace = 0;
        byte[]? palette = null, alpha = null;
        using var packed = new MemoryStream();
        for (int at = 8; at + 8 <= file.Length;)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(at));
            string type = System.Text.Encoding.ASCII.GetString(file, at + 4, 4);
            var body = file.AsSpan(at + 8, length);
            if (type == "IHDR") (width, height, depth, kind, interlace) = (BinaryPrimitives.ReadInt32BigEndian(body), BinaryPrimitives.ReadInt32BigEndian(body[4..]), body[8], body[9], body[12]);
            else if (type == "PLTE") palette = body.ToArray();
            else if (type == "tRNS") alpha = body.ToArray();
            else if (type == "IDAT") packed.Write(body);
            else if (type == "IEND") break;
            at += 12 + length;
        }
        if (depth != 8 || interlace != 0) throw new NotSupportedException("PNG: 8비트 · 얽지 않은 것만 읽는다");
        int channels = kind switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new NotSupportedException("PNG 색 갈래") };
        int stride = width * channels;
        var raw = new byte[(stride + 1) * height];
        packed.Position = 0;
        using (var inflate = new ZLibStream(packed, CompressionMode.Decompress)) inflate.ReadExactly(raw);
        var pixels = new byte[width * height * 4];
        var line = new byte[stride];
        var above = new byte[stride];
        for (int y = 0; y < height; y++)
        {
            int filter = raw[y * (stride + 1)];
            Buffer.BlockCopy(raw, y * (stride + 1) + 1, line, 0, stride);
            for (int i = 0; i < stride; i++)
            {
                int a = i >= channels ? line[i - channels] : 0, b = above[i], c = i >= channels ? above[i - channels] : 0;
                int add = filter switch
                {
                    1 => a, 2 => b, 3 => (a + b) / 2,
                    4 => Math.Abs(b - c) <= Math.Abs(a - c) && Math.Abs(b - c) <= Math.Abs(a + b - 2 * c) ? a : Math.Abs(a - c) <= Math.Abs(a + b - 2 * c) ? b : c,
                    _ => 0,
                };
                line[i] = (byte)(line[i] + add);
            }
            for (int x = 0; x < width; x++)
            {
                int p = (y * width + x) * 4, s = x * channels;
                switch (kind)
                {
                    case 2: (pixels[p], pixels[p + 1], pixels[p + 2], pixels[p + 3]) = (line[s + 2], line[s + 1], line[s], 255); break;
                    case 6: (pixels[p], pixels[p + 1], pixels[p + 2], pixels[p + 3]) = (line[s + 2], line[s + 1], line[s], line[s + 3]); break;
                    case 0: (pixels[p], pixels[p + 1], pixels[p + 2], pixels[p + 3]) = (line[s], line[s], line[s], 255); break;
                    case 4: (pixels[p], pixels[p + 1], pixels[p + 2], pixels[p + 3]) = (line[s], line[s], line[s], line[s + 1]); break;
                    default:
                        int n = line[s];
                        if (palette != null && n * 3 + 2 < palette.Length) (pixels[p], pixels[p + 1], pixels[p + 2]) = (palette[n * 3 + 2], palette[n * 3 + 1], palette[n * 3]);
                        pixels[p + 3] = alpha != null && n < alpha.Length ? alpha[n] : (byte)255;
                        break;
                }
            }
            (line, above) = (above, line);
        }
        return (width, height, pixels);
    }
}
