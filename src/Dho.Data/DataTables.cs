using System.Buffers.Binary;
using System.Text;

namespace Dho.Data;

public sealed record City(int Id, string Name, int Kind, int Nation, int Culture);
public sealed record SeaZone(int Id, string Name, int Ocean);
public sealed record Landing(int Id, string Name, int City, int Region);
public sealed record Discovery(int Id, string Name, string Description, int Kind, int Stars, int Exp, int Fame);

/// <summary>
/// 자료 표 묶음 <c>0000\local\dt000001.bin</c> — 표 143개 가운데 항구·바다·모험에 쓰는 것.
/// </summary>
/// <remarks>
/// 묶음: u32 개수, (u32 자리, u32 크기) × 개수. 표: u32 줄 수, 줄들.
/// 줄 안의 글: u16 바이트 수(4의 배수), 기호들. 기호 = v + v/7 (v 는 6비트) 이고 네 기호가 3바이트다.
/// 풀면 UTF-16LE 인데 4바이트마다 그 줄의 id(u32)가 XOR 돼 있다.
/// </remarks>
public sealed class DataTables
{
    private const int CityTable = 10, SeaTable = 8, LandingTable = 11, DiscoveryKindTable = 31, DiscoveryTable = 32;

    public IReadOnlyDictionary<int, City> Cities { get; }
    public IReadOnlyDictionary<int, SeaZone> Seas { get; }
    public IReadOnlyDictionary<int, Landing> Landings { get; }
    public IReadOnlyDictionary<int, string> DiscoveryKinds { get; }
    public IReadOnlyDictionary<int, Discovery> Discoveries { get; }

    public DataTables(int language = GvoFiles.Korean)
    {
        var pack = GvoFiles.ReadMwc(@"0000\local\dt000001.bin", language);
        byte[] Table(int index)
        {
            int at = BinaryPrimitives.ReadInt32LittleEndian(pack.AsSpan(4 + index * 8));
            int size = BinaryPrimitives.ReadInt32LittleEndian(pack.AsSpan(8 + index * 8));
            return pack.AsSpan(at, size).ToArray();
        }

        Cities = Rows(Table(CityTable), (r, id) => new City(id, r.Text(id), r.Int32(), r.Int32(), r.Byte()))
            .ToDictionary(c => c.Id);
        Seas = Rows(Table(SeaTable), (r, id) => new SeaZone(id, r.Text(id), r.Int32())).ToDictionary(s => s.Id);
        Landings = Rows(Table(LandingTable), (r, id) => new Landing(id, r.Text(id), r.Byte(), r.Byte()))
            .ToDictionary(l => l.Id);
        DiscoveryKinds = Rows(Table(DiscoveryKindTable), (r, id) => (Id: id, Name: r.Text(id)))
            .ToDictionary(k => k.Id, k => k.Name);
        Discoveries = Rows(Table(DiscoveryTable), (r, id) =>
        {
            var d = new Discovery(id, r.Text(id), r.Text(id), r.UInt16(), r.UInt16(), r.Int32(), r.Int32());
            r.UInt16();
            return d;
        }).ToDictionary(d => d.Id);
    }

    private static List<T> Rows<T>(byte[] table, Func<Reader, int, T> row)
    {
        var reader = new Reader(table);
        int count = reader.Int32();
        var rows = new List<T>(count);
        for (int i = 0; i < count; i++) rows.Add(row(reader, reader.Int32()));
        return rows;
    }

    private sealed class Reader(byte[] data)
    {
        private int _at;

        public int Byte() => data[_at++];
        public int UInt16() { int v = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(_at)); _at += 2; return v; }
        public int Int32() { int v = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(_at)); _at += 4; return v; }

        public string Text(int rowId)
        {
            int length = UInt16();
            var symbols = data.AsSpan(_at, length);
            _at += length;

            // 네 기호 → 3바이트
            var raw = new byte[length / 4 * 3];
            for (int i = 0; i < length / 4; i++)
            {
                int v = 0;
                for (int k = 0; k < 4; k++)
                {
                    int s = symbols[i * 4 + k];
                    v = v << 6 | (s - (s >> 3)) & 0x3F;
                }
                raw[i * 3] = (byte)(v >> 16);
                raw[i * 3 + 1] = (byte)(v >> 8);
                raw[i * 3 + 2] = (byte)v;
            }

            var text = new StringBuilder();
            for (int p = 0; p + 1 < raw.Length; p += 2)
            {
                int ch = raw[p] | raw[p + 1] << 8;
                ch ^= ((p / 2 & 1) == 1 ? rowId >> 16 : rowId) & 0xFFFF;
                if (ch == 0) break;
                text.Append((char)ch);
            }
            return text.ToString();
        }
    }
}
