using System.Buffers.Binary;
using System.Numerics;

namespace Dho.Data;

/// <summary>시내 지도 위의 시설 하나.</summary>
/// <param name="Place">장소 번호 — 자료 표 40 의 id(9 조선소, 10 교역소, 14 은행 …, 1001 ~ 은 저택).</param>
/// <param name="Scene">시내 장면 안의 자리(x, z).</param>
public sealed record TownMark(int Place, int MapX, int MapY, int Kind, Vector2 Scene, int Icon);

/// <summary>
/// 원본의 시내 지도 — 그림과 시설 자리.
/// </summary>
/// <remarks>
/// 그림 <c>0010\0000\tm000000.bin</c>: u32 장 수(247), (u32 자리, u32 크기) × 장 수, 자리마다 MWC 덩이 — 풀면 너비 180 의 BGRA(높이 139 ~ 141).
/// 자리 <c>0000\bin\00000008.bin</c>(MWC): u32 지도 수, 지도마다 u32 지도 id(= 도시 id), u32 표식 수,
/// 표식 25바이트: u32 장소 번호, u32 지도 x, u32 지도 y, u32 갈래, u32 장면 x ÷ 10, u32 장면 z ÷ 10, u8 표식 그림 번호.
/// 두 파일의 지도 차례가 같다. 지도는 장면을 돌려 놓은 것이다 — 지도의 오른쪽이 장면의 −z, 아래가 +x.
/// </remarks>
public sealed class TownMap
{
    public const int Width = 180;
    public int Height { get; }
    public byte[] Bgra { get; }
    public List<TownMark> Marks { get; } = [];

    // 장면 → 지도: mapX = _ax × z + _bx, mapY = _ay × x + _by (표식들에서 맞춘다)
    private readonly float _ax = -1 / 255f, _bx = Width / 2f, _ay = 1 / 255f, _by;

    private TownMap(byte[] picture, List<TownMark> marks)
    {
        Bgra = picture;
        Height = picture.Length / (Width * 4);
        Marks = marks;
        _by = Height / 2f;
        if (marks.Count >= 2)
        {
            (_ax, _bx) = Fit(marks.Select(m => (m.Scene.Y, (float)m.MapX)).ToList(), _ax, _bx);
            (_ay, _by) = Fit(marks.Select(m => (m.Scene.X, (float)m.MapY)).ToList(), _ay, _by);
        }
    }

    /// <summary>가장 잘 맞는 직선 y = a × x + b. 점들이 한곳에 몰려 있으면 주어진 값을 그대로 쓴다.</summary>
    private static (float A, float B) Fit(List<(float X, float Y)> points, float a, float b)
    {
        float meanX = points.Average(p => p.X), meanY = points.Average(p => p.Y);
        float spread = points.Sum(p => (p.X - meanX) * (p.X - meanX));
        if (spread < 1e4f) return (a, meanY - a * meanX);
        float slope = points.Sum(p => (p.X - meanX) * (p.Y - meanY)) / spread;
        return (slope, meanY - slope * meanX);
    }

    public Vector2 ToMap(Vector2 scene) => new(_ax * scene.Y + _bx, _ay * scene.X + _by);

    /// <summary>장면에서의 방향(x, z)을 지도 위의 방향으로.</summary>
    public Vector2 ToMapDirection(Vector2 scene) => new(MathF.Sign(_ax) * scene.Y, MathF.Sign(_ay) * scene.X);

    private static byte[]? _marks;

    /// <summary>그 도시의 지도. 파일이 없거나 지도가 없는 도시면 null.</summary>
    public static TownMap? Load(int cityId)
    {
        try
        {
            _marks ??= GvoFiles.MwcChunks(GvoFiles.Read(@"0000\bin\00000008.bin"))[0];
            int I32(byte[] data, int at) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at));
            int at = 4;
            for (int index = 0; index < I32(_marks, 0); index++)
            {
                int id = I32(_marks, at), count = I32(_marks, at + 4);
                at += 8;
                if (id != cityId) { at += count * 25; continue; }

                var marks = new List<TownMark>();
                for (int i = 0; i < count; i++, at += 25)
                    marks.Add(new TownMark(I32(_marks, at), I32(_marks, at + 4), I32(_marks, at + 8), I32(_marks, at + 12),
                        new Vector2(I32(_marks, at + 16) * 10f, I32(_marks, at + 20) * 10f), _marks[at + 24]));

                using var pictures = File.OpenRead(GvoFiles.PathOf(@"0010\0000\tm000000.bin"));
                var entry = new byte[8];
                pictures.Seek(4 + index * 8, SeekOrigin.Begin);
                pictures.ReadExactly(entry);
                var packed = new byte[I32(entry, 4)];
                pictures.Seek(I32(entry, 0), SeekOrigin.Begin);
                pictures.ReadExactly(packed);
                return new TownMap(GvoFiles.MwcChunks(packed)[0], marks);
            }
        }
        catch (Exception) { }
        return null;
    }
}
