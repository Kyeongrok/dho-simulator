using System.Buffers.Binary;

namespace Dho.Data;

/// <summary>
/// 해역 격자 <c>0000\bin\10000000.bin</c> — 세계 좌표가 어느 해역에 드는지 알려 준다.
/// </summary>
/// <remarks>
/// u32 개수, u32 해역 id × 개수(위 16비트가 <c>0x04nn</c>), (u32 자리, u32 크기) × 개수.
/// 해역: u32 cx, u32 cy, u32, u32, u32 w, u32 h, 칸들. (w, h)는 테두리 한 겹을 낀 조각 수라서
/// 안쪽 네모는 <c>x ∈ [(cx+1)·256, (cx+w-1)·256)</c> 이다. x 는 16384 를 넘어 돌 수 있다.
/// </remarks>
public sealed class SeaZones
{
    private readonly List<(int Id, int X0, int Y0, int X1, int Y1)> _zones = [];

    public SeaZones()
    {
        var data = GvoFiles.ReadMwc(@"0000\bin\10000000.bin");
        int count = BinaryPrimitives.ReadInt32LittleEndian(data);
        for (int k = 0; k < count; k++)
        {
            int id = (int)(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4 + 4 * k)) >> 16);
            int at = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4 + 4 * count + 8 * k));
            int cx = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at));
            int cy = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at + 4));
            int w = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at + 16));
            int h = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at + 20));
            _zones.Add((id, (cx + 1) * 256, (cy + 1) * 256, (cx + w - 1) * 256, (cy + h - 1) * 256));
        }
    }

    /// <summary>해역 표(<see cref="DataTables.Seas"/>)의 id. 없으면 0.</summary>
    public int ZoneAt(double x, double y)
    {
        x = WorldMap.WrapX(x);
        foreach (var (id, x0, y0, x1, y1) in _zones)
            if (y >= y0 && y < y1 && ((x >= x0 && x < x1) || (x + WorldMap.Width >= x0 && x + WorldMap.Width < x1)))
                return id & 0xFF;
        return 0;
    }
}
