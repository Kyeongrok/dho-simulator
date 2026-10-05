using System.Buffers.Binary;
using System.Numerics;

namespace Dho.Data;

/// <summary>
/// 바다 빛깔 벌(<c>0003\cm000000.bin</c>, "PSCD") — 세계를 64 × 32 조각으로 나눠 조각마다 벌 번호(0 ~ 12)를 붙여 둔 것.
/// </summary>
/// <remarks>
/// "PSCD", u32 가로 64, u32 세로 32, u32 벌 수 13, u32 벌 크기 152, u32 벌 번호 × 2048, 벌 × 13.
/// 벌은 u32 서른여덟 칸 — 색(바이트 차례 R, G, B, A)과 f32 가 섞여 있다. 읽어 낸 것은 둘뿐이다:
/// 칸 0 = 하늘 빛깔(지중해 57,101,188 · 홍해 쪽 세 조각만 모래빛 191,127,52), 칸 22 = 물빛(지중해 0,20,122 · 카리브 0,48,98 · 열대 0,53,146).
/// 나머지 칸(얕은 물빛으로 보이는 칸 2, 안개빛으로 보이는 칸 18, 끝의 색 열두 개 …)은 뜻을 못 밝혔다.
/// </remarks>
public sealed class SeaColors
{
    private const int ChunksX = 64, ChunksY = 32, SkyField = 0, WaterField = 22;
    private readonly int[] _index = new int[ChunksX * ChunksY];
    private readonly Vector3[] _sky, _water;

    /// <summary>벌 0(지중해 · 서유럽 앞바다)의 하늘과 물 — 다른 바다는 이것과의 차이로 물들인다.</summary>
    public Vector3 HomeSky => _sky[0];
    public Vector3 HomeWater => _water[0];

    public SeaColors()
    {
        var data = GvoFiles.Read(@"0003\cm000000.bin");
        int count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(12)), size = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(16));
        for (int i = 0; i < _index.Length; i++) _index[i] = Math.Clamp(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(20 + i * 4)), 0, count - 1);
        int sets = 20 + _index.Length * 4;
        Vector3 Color(int set, int field) => new Vector3(data[sets + set * size + field * 4], data[sets + set * size + field * 4 + 1], data[sets + set * size + field * 4 + 2]) / 255f;
        _sky = Enumerable.Range(0, count).Select(k => Color(k, SkyField)).ToArray();
        _water = Enumerable.Range(0, count).Select(k => Color(k, WaterField)).ToArray();
    }

    /// <summary>세계 좌표의 하늘빛과 물빛 — 조각 가운데 사이를 부드럽게 잇는다(동서로는 이어진다).</summary>
    public (Vector3 Sky, Vector3 Water) At(double x, double y)
    {
        double fx = x / (WorldMap.Width / (double)ChunksX) - 0.5, fy = Math.Clamp(y / (WorldMap.Height / (double)ChunksY) - 0.5, 0, ChunksY - 1);
        int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
        float tx = (float)(fx - x0), ty = (float)(fy - y0);
        int Set(int cx, int cy) => _index[Math.Clamp(cy, 0, ChunksY - 1) * ChunksX + ((cx % ChunksX) + ChunksX) % ChunksX];
        Vector3 Mix(Vector3[] colors) =>
            Vector3.Lerp(Vector3.Lerp(colors[Set(x0, y0)], colors[Set(x0 + 1, y0)], tx), Vector3.Lerp(colors[Set(x0, y0 + 1)], colors[Set(x0 + 1, y0 + 1)], tx), ty);
        return (Mix(_sky), Mix(_water));
    }
}
