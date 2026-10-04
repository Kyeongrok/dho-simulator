using System.Buffers.Binary;
using System.Text;

namespace Dho.Data;

/// <summary>
/// 화면 글 표 (<c>0000\local\dt000002.bin</c> 등).
/// </summary>
/// <remarks>
/// u32 키 길이, 키(저작권 문구), u32 개수, u32 ?, (u32 id, u32 자리) × 개수, u32 덩이 크기, 덩이.
/// 덩이는 키로 XOR 돼 있고, 레코드는 u16 길이, u16 조각 수, (u16 갈래, u16 자리, u16 바이트 수) × 조각 수.
/// 글자는 UTF-16BE 이고 낱자마다 <c>id + 조각 안 차례</c> 가 XOR 돼 있다. 갈래 1 은 끼워 넣는 자리(%s).
/// </remarks>
public sealed class StringTable
{
    private readonly Dictionary<uint, string> _texts = new();

    public StringTable(byte[] chunk)
    {
        int keyLength = BinaryPrimitives.ReadInt32LittleEndian(chunk);
        var key = chunk.AsSpan(4, keyLength);
        var body = chunk.AsSpan(4 + keyLength);
        int count = BinaryPrimitives.ReadInt32LittleEndian(body);
        int blobStart = 8 + count * 8 + 4;

        var blob = body[blobStart..].ToArray();
        for (int i = 0; i < blob.Length; i++) blob[i] ^= key[i % keyLength];

        var text = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(body[(8 + i * 8)..]);
            int at = BinaryPrimitives.ReadInt32LittleEndian(body[(12 + i * 8)..]);
            int pieces = BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(at + 2));

            text.Clear();
            for (int p = 0; p < pieces; p++)
            {
                int start = BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(at + 4 + p * 6 + 2));
                int bytes = BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(at + 4 + p * 6 + 4));
                for (int c = 0; c + 1 < bytes; c += 2)
                {
                    int ch = (blob[at + start + c] << 8 | blob[at + start + c + 1]) ^ (int)((id + (uint)(c / 2)) & 0xFFFF);
                    if (ch != 0) text.Append((char)ch);
                }
            }
            _texts[id] = text.ToString();
        }
    }

    public static StringTable Load(string relative, int language = GvoFiles.Korean) =>
        new(GvoFiles.ReadMwc(relative, language));

    public string this[uint id] => _texts.TryGetValue(id, out var text) ? text : $"#{id}";
}
