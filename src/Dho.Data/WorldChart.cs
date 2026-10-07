using System.Buffers.Binary;

namespace Dho.Data;

/// <summary>
/// 세계지도 그림 — <c>0010\0000\kp000000.bin</c>: u32 장 수(2,048), (u32 자리, u32 크기) × 장 수, 자리마다 MWC 덩이(48 × 48 BGRA).
/// 장은 세계 조각 격자(가로 64 × 세로 32, 한 조각 = 세계 좌표 256)의 차례다 — 이으면 3,072 × 1,536 의 그림(세계 좌표 1 = 0.1875 픽셀).
/// 뭍만 그려져 있고 바다는 비어 있다(알파 0). 원본의 지도 창(M)이 이 그림을 쓰는지, 양피지 지도가 따로 있는지는 못 가렸다.
/// </summary>
public static class WorldChart
{
    public const int Tile = 48, Width = 64 * Tile, Height = 32 * Tile;
    /// <summary>세계 좌표 1 이 그림의 몇 픽셀인가.</summary>
    public const double Scale = Tile / 256.0;

    private static byte[]? _pixels;
    private static bool _failed;

    /// <summary>이어 붙인 그림(BGRA) — 바다는 옅은 청회색으로 칠해 둔다. 게임 폴더에 없으면 null.</summary>
    public static byte[]? Pixels()
    {
        if (_pixels != null || _failed) return _pixels;
        try
        {
            var file = File.ReadAllBytes(GvoFiles.PathOf(@"0010\0000\kp000000.bin"));
            int count = BinaryPrimitives.ReadInt32LittleEndian(file);
            var pixels = new byte[Width * Height * 4];
            for (int i = 0; i < Math.Min(count, 64 * 32); i++)
            {
                int at = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(4 + i * 8)), size = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(8 + i * 8));
                var tile = GvoFiles.MwcChunks(file.AsSpan(at, size).ToArray())[0];
                if (tile.Length < Tile * Tile * 4) continue;
                int tx = i % 64 * Tile, ty = i / 64 * Tile;
                for (int row = 0; row < Tile; row++)
                    Buffer.BlockCopy(tile, row * Tile * 4, pixels, ((ty + row) * Width + tx) * 4, Tile * 4);
            }
            // 바다(알파 0)는 옛 지도의 옅은 청회색, 뭍은 그 위에 알파로 얹는다
            for (int p = 0; p < pixels.Length; p += 4)
            {
                int a = pixels[p + 3];
                pixels[p] = (byte)((pixels[p] * a + 186 * (255 - a)) / 255);
                pixels[p + 1] = (byte)((pixels[p + 1] * a + 196 * (255 - a)) / 255);
                pixels[p + 2] = (byte)((pixels[p + 2] * a + 176 * (255 - a)) / 255);
                pixels[p + 3] = 255;
            }
            _pixels = pixels;
        }
        catch (Exception) { _failed = true; }
        return _pixels;
    }

    /// <summary>그림의 한 네모(가로는 감긴다)를 떼어 낸다.</summary>
    public static (int Width, int Height, byte[] Bgra)? Crop(int left, int top, int width, int height)
    {
        if (Pixels() is not { } pixels) return null;
        var crop = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            int sy = Math.Clamp(top + y, 0, Height - 1);
            for (int x = 0; x < width; x++)
            {
                int sx = ((left + x) % Width + Width) % Width;
                Buffer.BlockCopy(pixels, (sy * Width + sx) * 4, crop, (y * width + x) * 4, 4);
            }
        }
        return (width, height, crop);
    }
}
