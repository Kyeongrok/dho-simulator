using System.Buffers.Binary;
using System.IO.Compression;

namespace Dho.Data;

/// <summary>
/// 게임 폴더(대항해시대 온라인 클라이언트)의 파일을 읽는다. 읽기만 한다.
/// </summary>
/// <remarks>폴더는 환경 변수 <c>GVO_DIR</c>, 없으면 <c>C:\Netmarble\GV Online Kr</c>.</remarks>
public static class GvoFiles
{
    public static readonly string Root =
        Environment.GetEnvironmentVariable("GVO_DIR") ?? @"C:\Netmarble\GV Online Kr";

    /// <summary>언어 묶음 차례 — <c>0000\local\dt*.bin</c> 은 MWC 덩이 다섯 개 = 언어 다섯.</summary>
    public const int Japanese = 0, Korean = 1;

    public static string PathOf(string relative) => Path.Combine(Root, relative);

    public static byte[] Read(string relative) => File.ReadAllBytes(PathOf(relative));

    /// <summary>
    /// 파일 안의 MWC 덩이를 차례로 푼다.
    /// 덩이 = <c>"MWC\x1a"</c>, u32 풀린 크기, u32 눌린 크기, zlib 스트림.
    /// </summary>
    public static List<byte[]> MwcChunks(byte[] file)
    {
        var chunks = new List<byte[]>();
        ReadOnlySpan<byte> magic = "MWC\x1a"u8;
        int at = 0;
        while (true)
        {
            int found = file.AsSpan(at).IndexOf(magic);
            if (found < 0) break;
            at += found;
            int unpacked = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(at + 4));
            int packed = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(at + 8));
            if (packed <= 0 || at + 12 + packed > file.Length) { at += 4; continue; }

            var output = new byte[unpacked];
            using (var zlib = new ZLibStream(new MemoryStream(file, at + 12, packed), CompressionMode.Decompress))
                zlib.ReadExactly(output);
            chunks.Add(output);
            at += 12 + packed;
        }
        return chunks;
    }

    public static byte[] ReadMwc(string relative, int index = 0) => MwcChunks(Read(relative))[index];
}
