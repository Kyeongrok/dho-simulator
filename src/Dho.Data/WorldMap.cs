using System.Buffers.Binary;

namespace Dho.Data;

/// <summary>
/// 세계 좌표(16384 x 8192, 가로는 이어져 있다)에서 뭍과 바다, 도시 자리를 알려 준다.
/// </summary>
/// <remarks>
/// 통행 판정 <c>0000\bin\10000001.bin</c>: u32 조각 수(64 x 32), 조각마다 1060바이트 —
/// u32 갈래, 64x64 비트맵 A, 64x64 비트맵 B(A 를 부풀린 것), 16x16 비트맵. 비트 하나가 4x4, 1 이 뭍.
/// 도시 자리 <c>0003\sm000001.bin</c>(뭍 위) · <c>sm000002.bin</c>(항구 앞바다):
/// u16 개수, (u16 id, u32 x, u32 y) × 개수.
/// </remarks>
public sealed class WorldMap
{
    public const int Width = 16384, Height = 8192;
    public const int CellSize = 4, CellsX = Width / CellSize, CellsY = Height / CellSize;

    private const int TilesX = 64, TileCells = 64, TileBytes = 1060;

    private readonly byte[] _tiles;

    public IReadOnlyDictionary<int, (int X, int Y)> CityOnLand { get; }
    public IReadOnlyDictionary<int, (int X, int Y)> CityAtSea { get; }

    public WorldMap()
    {
        _tiles = GvoFiles.ReadMwc(@"0000\bin\10000001.bin");
        CityOnLand = Points(GvoFiles.Read(@"0003\sm000001.bin"));
        CityAtSea = Points(GvoFiles.Read(@"0003\sm000002.bin"));
    }

    private static Dictionary<int, (int, int)> Points(byte[] file)
    {
        int count = BinaryPrimitives.ReadUInt16LittleEndian(file);
        var points = new Dictionary<int, (int, int)>(count);
        for (int i = 0; i < count; i++)
        {
            var row = file.AsSpan(2 + i * 10);
            points[BinaryPrimitives.ReadUInt16LittleEndian(row)] =
                (BinaryPrimitives.ReadInt32LittleEndian(row[2..]), BinaryPrimitives.ReadInt32LittleEndian(row[6..]));
        }
        return points;
    }

    /// <summary>칸(4x4 단위)이 뭍인가. 가로는 감기고, 세로 밖은 뭍으로 친다.</summary>
    public bool IsLandCell(int cellX, int cellY)
    {
        if (cellY < 0 || cellY >= CellsY) return true;
        cellX = (cellX % CellsX + CellsX) % CellsX;
        int tile = cellY / TileCells * TilesX + cellX / TileCells;
        int row = cellY % TileCells, column = cellX % TileCells;
        return (_tiles[4 + tile * TileBytes + 4 + row * 8 + column / 8] >> (column & 7) & 1) != 0;
    }

    public bool IsLand(double x, double y) =>
        IsLandCell((int)Math.Floor(x / CellSize), (int)Math.Floor(y / CellSize));

    public static double WrapX(double x) => (x % Width + Width) % Width;

    /// <summary>가로가 감기는 세계에서 a 에서 b 로 가는 가장 짧은 가로 차.</summary>
    public static double DeltaX(double from, double to)
    {
        double d = (to - from) % Width;
        if (d > Width / 2) d -= Width;
        if (d < -Width / 2) d += Width;
        return d;
    }
}
