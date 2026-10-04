using System.Buffers.Binary;

namespace Dho.Data;

/// <summary>
/// <c>0001\*.bin</c> 묶음 — 이름 없이 차례가 곧 번호다.
/// </summary>
/// <remarks>
/// 큰 묶음(sh·md·hm·fi·ef): u8 0xFF, u16 개수, u32 자리 × (개수+1).
/// 작은 묶음(em·sa·fm): u8 개수, u32 자리 × (개수+1). 자리는 파일 처음부터, 마지막 자리가 파일 크기다.
/// </remarks>
public sealed class Pack
{
    private readonly string _path;
    private readonly int[] _offsets;

    public int Count => _offsets.Length - 1;

    public Pack(string relative)
    {
        _path = GvoFiles.PathOf(relative);
        using var stream = File.OpenRead(_path);
        int first = stream.ReadByte(), count = first, tableAt = 1;
        if (first == 0xFF)
        {
            var two = new byte[2];
            stream.ReadExactly(two);
            count = BinaryPrimitives.ReadUInt16LittleEndian(two);
            tableAt = 3;
        }
        stream.Position = tableAt;
        var table = new byte[(count + 1) * 4];
        stream.ReadExactly(table);
        _offsets = new int[count + 1];
        for (int i = 0; i <= count; i++) _offsets[i] = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(i * 4));
    }

    public byte[] Entry(int index)
    {
        using var stream = File.OpenRead(_path);
        stream.Position = _offsets[index];
        var data = new byte[_offsets[index + 1] - _offsets[index]];
        stream.ReadExactly(data);
        return data;
    }
}
