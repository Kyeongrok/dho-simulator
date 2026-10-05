using System.Buffers.Binary;

namespace Dho.Data;

/// <summary>
/// 화면 부품 묶음 — <c>0010\local\gm00000N.bin</c>. 창틀·단추·작은 표식이 조각으로 들어 있다.
/// </summary>
/// <remarks>
/// MWC 덩이 하나. u32 조각 수, u32 장 수, u32 장들의 자리.
/// 조각(28바이트): u32 장 번호, f32 u0, v0, u1, v1(장 안의 자리, 반 픽셀 밀려 있다), u32 너비, u32 높이.
/// 장: u16 너비, u16 높이, u32 21(A8R8G8B8), u32 ?, u32 바이트 수 × 2, 그 뒤 BGRA.
/// gm000002 의 주변 지도 부품: 154 ~ 157 바다 무늬(32 × 32, 물결이 흐르는 네 장), 158 구름(64 × 64),
/// 159 둥근 가림판(128 × 128), 162 나침반 바늘(32 × 32, 긴 쪽이 북), 163 초록 세모(8 × 8, 내 배),
/// 407 · 408 바람 화살(깃 54 × 32 + 촉 18 × 32).
/// </remarks>
public sealed class UiParts
{
    private readonly byte[] _data;
    private readonly List<(int Width, int Height, int At)> _sheets = [];
    private readonly int _count;
    private readonly bool _shifted;

    public UiParts(int number)
    {
        _data = GvoFiles.MwcChunks(GvoFiles.Read($@"0010\local\gm{number:D6}.bin"))[0];
        _count = BinaryPrimitives.ReadInt32LittleEndian(_data);
        _shifted = number == 0 && _count >= 1266;
        int sheets = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(4));
        int at = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(8));
        for (int i = 0; i < sheets; i++)
        {
            int width = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(at)), height = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(at + 2));
            _sheets.Add((width, height, at + 20));
            at += 20 + BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(at + 12));
        }
    }

    /// <summary>조각 하나의 BGRA. 없는 번호면 null.</summary>
    public (int Width, int Height, byte[] Bgra)? Pixels(int index)
    {
        // 코드의 번호는 2022-12 클라이언트(넷마블)의 것이다. 지금 클라이언트(파파야)의 gm000000 은 228 번부터 조각 여섯이 끼어들어
        // 뒤가 여섯씩 밀렸다(세로돛 306 → 312, 선원 330 → 336 … — 그림을 나란히 놓고 맞춘 것, 228 이라는 경계는 짐작).
        if (_shifted && index >= 228) index += 6;
        if (index < 0 || index >= _count) return null;
        int record = 12 + index * 28;
        int sheet = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(record));
        if (sheet >= _sheets.Count) return null;
        var (sheetWidth, sheetHeight, pixels) = _sheets[sheet];
        int x = (int)MathF.Round(BinaryPrimitives.ReadSingleLittleEndian(_data.AsSpan(record + 4)) * sheetWidth - 0.5f);
        int y = (int)MathF.Round(BinaryPrimitives.ReadSingleLittleEndian(_data.AsSpan(record + 8)) * sheetHeight - 0.5f);
        int width = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(record + 20)), height = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(record + 24));
        if (x < 0 || y < 0 || x + width > sheetWidth || y + height > sheetHeight) return null;

        var bgra = new byte[width * height * 4];
        for (int row = 0; row < height; row++)
            Array.Copy(_data, pixels + ((y + row) * sheetWidth + x) * 4, bgra, row * width * 4, width * 4);
        return (width, height, bgra);
    }
}
