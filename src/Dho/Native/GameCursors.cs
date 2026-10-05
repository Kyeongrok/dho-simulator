using System.Runtime.InteropServices;
using Dho.Data;

namespace Dho.Native;

/// <summary>
/// 원본의 마우스 커서 — <c>0010\0000\00000001.bin</c>.
/// </summary>
/// <remarks>
/// MWC 덩이 하나: u32 크기, u32 개수(12), (u16 x, u16 y) × 12 — 누르는 점(16 × 16 기준이라 두 배 한다), 그 뒤 32 × 32 BGRA × 12.
/// 차례: 0 화살표 · 1 ~ 4 크기 바꾸기(세로 · 가로 · 두 대각) · 5 모래시계 · 6 금지 · 7 걷기 · 8 · 9 손 · 10 ~ 11 손(대포 · 금지).
/// </remarks>
internal sealed class GameCursors : IDisposable
{
    public const int Arrow = 0, Wait = 5, Walk = 7, Hand = 8;
    private const int Size = 32, Count = 12;

    private readonly IntPtr[] _cursors = new IntPtr[Count];

    /// <param name="scale">화면 배율 — 2 배 화면이면 커서도 두 배로 키운다(점을 그대로 키워 원본의 맛을 살린다).</param>
    public GameCursors(float scale)
    {
        try
        {
            var data = GvoFiles.MwcChunks(GvoFiles.Read(@"0010\0000\00000001.bin"))[0];
            if (BitConverter.ToInt32(data, 4) != Count || data.Length < 8 + Count * 4 + Count * Size * Size * 4) return;
            int grow = Math.Clamp((int)MathF.Round(scale), 1, 4), side = Size * grow;
            for (int i = 0; i < Count; i++)
            {
                int from = 8 + Count * 4 + i * Size * Size * 4;
                var pixels = new byte[side * side * 4];
                for (int y = 0; y < side; y++)
                for (int x = 0; x < side; x++)
                    Buffer.BlockCopy(data, from + (y / grow * Size + x / grow) * 4, pixels, (y * side + x) * 4, 4);
                var info = new IconInfo
                {
                    Icon = false,
                    HotX = BitConverter.ToUInt16(data, 8 + i * 4) * 2 * grow,
                    HotY = BitConverter.ToUInt16(data, 8 + i * 4 + 2) * 2 * grow,
                    Mask = CreateBitmap(side, side, 1, 1, new byte[side * side / 8]),
                    Color = CreateBitmap(side, side, 1, 32, pixels),
                };
                _cursors[i] = CreateIconIndirect(ref info);
                DeleteObject(info.Mask);
                DeleteObject(info.Color);
            }
        }
        catch (Exception) { }                      // 게임 폴더에 없으면 윈도 커서를 그대로 쓴다
    }

    /// <summary>그 커서로 바꾼다. 못 읽었으면 false(윈도가 제 커서를 쓰게 둔다).</summary>
    public bool Show(int which)
    {
        var cursor = which >= 0 && which < Count ? _cursors[which] : IntPtr.Zero;
        if (cursor == IntPtr.Zero) return false;
        SetCursor(cursor);
        return true;
    }

    public void Dispose()
    {
        foreach (var cursor in _cursors)
            if (cursor != IntPtr.Zero) DestroyIcon(cursor);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)] public bool Icon;
        public int HotX, HotY;
        public IntPtr Mask, Color;
    }

    [DllImport("user32.dll")] private static extern IntPtr CreateIconIndirect(ref IconInfo info);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern IntPtr SetCursor(IntPtr cursor);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, byte[] bits);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
}
