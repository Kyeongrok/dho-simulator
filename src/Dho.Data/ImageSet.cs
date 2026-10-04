using System.Buffers.Binary;
using System.IO.Compression;

namespace Dho.Data;

/// <summary>
/// 화면 그림 묶음(<c>0010</c>) — 색인 <c>xx000000.bin</c> 과 자료 <c>xx000001.bin</c> … 의 짝.
/// 발견물 그림은 <c>0010\0001\sd</c> 의 무리 0 이고 id 가 발견물 표의 id 다.
/// </summary>
/// <remarks>
/// 색인 머리 24바이트: u32 id 수 n, u32 무리 수, u32 너비, u32 높이, u32 그림 수 m, u32 자료 파일 수.
/// id 줄 20바이트 × n: u32 무리, u32 id, u32 그림 번호, u32 너비, u32 높이.
/// 그림 줄 28바이트 × m: u32 번호, u32 너비, u32 높이, u32 파일(0부터), u32 자리, u32 크기, u32 1.
/// 자리에 MWC 덩이 하나가 있고 풀면 너비 × 높이 × 4 바이트의 BGRA 다.
/// </remarks>
public sealed class ImageSet
{
    private readonly string _prefix;
    private readonly Dictionary<(int Group, int Id), int> _ids = new();
    private readonly Dictionary<int, (int Width, int Height, int File, int At)> _images = new();

    /// <param name="prefix">예) <c>0010\0001\sd</c></param>
    public ImageSet(string prefix)
    {
        _prefix = prefix;
        var index = GvoFiles.Read(prefix + "000000.bin");
        int U32(int at) => BinaryPrimitives.ReadInt32LittleEndian(index.AsSpan(at));
        int ids = U32(0), images = U32(16);
        for (int i = 0; i < ids; i++)
        {
            int at = 24 + i * 20;
            _ids[(U32(at), U32(at + 4))] = U32(at + 8);
        }
        for (int i = 0; i < images; i++)
        {
            int at = 24 + ids * 20 + i * 28;
            _images[U32(at)] = (U32(at + 4), U32(at + 8), U32(at + 12), U32(at + 16));
        }
    }

    /// <summary>그림 하나를 BGRA 로. 없으면 null.</summary>
    public (int Width, int Height, byte[] Bgra)? Pixels(int group, int id)
    {
        if (!_ids.TryGetValue((group, id), out int number) || !_images.TryGetValue(number, out var image)) return null;
        using var file = File.OpenRead(GvoFiles.PathOf($"{_prefix}{image.File + 1:D6}.bin"));
        file.Position = image.At;
        var head = new byte[12];
        file.ReadExactly(head);
        if (!head.AsSpan(0, 4).SequenceEqual("MWC\x1a"u8)) return null;
        var pixels = new byte[BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(4))];
        if (pixels.Length != image.Width * image.Height * 4) return null;
        using var zlib = new ZLibStream(file, CompressionMode.Decompress);
        zlib.ReadExactly(pixels);
        return (image.Width, image.Height, pixels);
    }
}
