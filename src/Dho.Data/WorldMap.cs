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

    // 뭍 판정 자료는 해안 가까이에만 있다 — 내륙 조각(64 × 64 칸)은 통째로 비어 있어 그대로 읽으면 네모난 바다로 보인다.
    // 빈 조각마다 이웃 조각의 맞닿은 가장자리를 보고 뭍 칸이 반을 넘으면 「뭍」으로 친다(이미 정한 빈 조각은 통째로 뭍 · 바다로 본다).
    // 자료가 있는 조각에서 바깥으로 한 겹씩 번져 간다. 지은 보정이다 — 2,048 조각 가운데 빈 것 1,346, 그 가운데 420 이 뭍이 된다.
    private bool[]? _inland;

    private bool[] Inland()
    {
        int tilesY = CellsY / TileCells, count = TilesX * tilesY;
        var state = new sbyte[count];          // −1 모름 · 0 바다(빈 조각) · 1 뭍(빈 조각) · 2 자료 있음
        bool Raw(int tile, int row, int column) => (_tiles[4 + tile * TileBytes + 4 + row * 8 + column / 8] >> (column & 7) & 1) != 0;
        for (int tile = 0; tile < count; tile++)
        {
            bool any = false;
            for (int k = 0; k < TileCells * 8 && !any; k++) any = _tiles[4 + tile * TileBytes + 4 + k] != 0;
            state[tile] = (sbyte)(any ? 2 : -1);
        }
        // 그 조각에서 side(0 위 · 1 아래 · 2 왼 · 3 오른) 가장자리의 뭍 칸 수
        int Edge(int tile, int side)
        {
            if (state[tile] == 0) return 0;
            if (state[tile] == 1) return TileCells;
            int land = 0;
            for (int k = 0; k < TileCells; k++)
                if (side switch { 0 => Raw(tile, 0, k), 1 => Raw(tile, TileCells - 1, k), 2 => Raw(tile, k, 0), _ => Raw(tile, k, TileCells - 1) }) land++;
            return land;
        }
        (int Dy, int Dx, int Side)[] around = [(-1, 0, 1), (1, 0, 0), (0, -1, 3), (0, 1, 2)];
        for (bool changed = true; changed;)
        {
            changed = false;
            var next = (sbyte[])state.Clone();
            for (int ty = 0; ty < tilesY; ty++)
                for (int tx = 0; tx < TilesX; tx++)
                {
                    if (state[ty * TilesX + tx] != -1) continue;
                    int land = 0, known = 0;
                    foreach (var (dy, dx, side) in around)
                    {
                        int ny = ty + dy, nx = (tx + dx + TilesX) % TilesX;
                        if (ny < 0 || ny >= tilesY || state[ny * TilesX + nx] == -1) continue;
                        known += TileCells;
                        land += Edge(ny * TilesX + nx, side);
                    }
                    if (known == 0) continue;
                    next[ty * TilesX + tx] = (sbyte)(land * 2 > known ? 1 : 0);
                    changed = true;
                }
            state = next;
        }
        var inland = new bool[count];
        for (int tile = 0; tile < count; tile++) inland[tile] = state[tile] == 1;
        // 항구 앞바다가 든 조각은 메우지 않는다(혹시 걸리더라도 배가 갇히지 않게)
        foreach (var (x, y) in CityAtSea.Values)
            if (y >= 0 && y < Height) inland[y / CellSize / TileCells * TilesX + (x / CellSize % CellsX + CellsX) % CellsX / TileCells] = false;
        return inland;
    }

    /// <summary>칸(4x4 단위)이 뭍인가. 가로는 감기고, 세로 밖은 뭍으로 친다. 자료가 빈 내륙 조각은 메워서 본다.</summary>
    public bool IsLandCell(int cellX, int cellY)
    {
        if (cellY < 0 || cellY >= CellsY) return true;
        cellX = (cellX % CellsX + CellsX) % CellsX;
        int tile = cellY / TileCells * TilesX + cellX / TileCells;
        if ((_inland ??= Inland())[tile]) return true;
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
