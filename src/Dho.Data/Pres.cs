using System.Buffers.Binary;
using System.Text;

namespace Dho.Data;

/// <summary>
/// 이름 붙은 묶음 <c>PRES</c> — <c>0003</c> 의 지형·텍스처·모형 파일이 이 꼴이다.
/// </summary>
/// <remarks>
/// <c>"PRES"</c>, u32 파일 크기, u32 개수, u32 자리 × 개수, (u32 이름 길이, 이름) × 개수, 자료들.
/// 이름은 바이트마다 0x5A 가 더해져 있다. 자료는 다음 자리(마지막은 파일 크기)까지다.
/// 파일이 크므로 머리만 읽어 두고 자료는 달라고 할 때 읽는다.
/// </remarks>
public sealed class Pres
{
    public sealed record Entry(string File, string Name, int Offset, int Size);

    public List<Entry> Entries { get; } = [];

    /// <summary>
    /// <c>…0.bin</c>(LPRB: 매직, u32 크기, u32 묶음 파일 수)이 말하는 만큼 <c>…1.bin</c>, <c>…2.bin</c> 을 잇는다.
    /// </summary>
    /// <param name="prefix"><c>0003\gm00000</c> 처럼 끝 한 자리를 뺀 경로.</param>
    public static Pres LoadSet(string prefix)
    {
        var head = GvoFiles.Read(prefix + "0.bin");
        int files = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(8));
        var pres = new Pres();
        for (int k = 1; k <= files; k++) pres.Append(GvoFiles.PathOf($"{prefix}{k}.bin"));
        return pres;
    }

    private void Append(string path)
    {
        using var stream = File.OpenRead(path);
        var head = new byte[12];
        stream.ReadExactly(head);
        int size = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(4));
        int count = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(8));

        var offsets = new byte[count * 4];
        stream.ReadExactly(offsets);
        var lengthBytes = new byte[4];
        for (int i = 0; i < count; i++)
        {
            stream.ReadExactly(lengthBytes);
            var name = new byte[BinaryPrimitives.ReadInt32LittleEndian(lengthBytes)];
            stream.ReadExactly(name);
            for (int c = 0; c < name.Length; c++) name[c] -= 0x5A;

            int at = BinaryPrimitives.ReadInt32LittleEndian(offsets.AsSpan(i * 4));
            int next = i + 1 < count ? BinaryPrimitives.ReadInt32LittleEndian(offsets.AsSpan(i * 4 + 4)) : size;
            Entries.Add(new Entry(path, Encoding.Latin1.GetString(name), at, next - at));
        }
    }

    public static byte[] Read(Entry entry)
    {
        using var stream = File.OpenRead(entry.File);
        stream.Position = entry.Offset;
        var data = new byte[entry.Size];
        stream.ReadExactly(data);
        return data;
    }
}
