using System.Buffers.Binary;
using System.Text;

namespace Dho.Data;

public sealed record City(int Id, string Name, int Kind, int Nation, int Culture);
public sealed record SeaZone(int Id, string Name, int Ocean);
public sealed record Landing(int Id, string Name, int City, int Region);
public sealed record Discovery(int Id, string Name, string Description, int Kind, int Stars, int Exp, int Fame);
public sealed record Skill(int Id, string Name, string Description, int Group, int Sub, int Basic, int Cost, int Job);
public sealed record Good(int Id, string Name, string Description, int Kind);
public sealed record Ship(int Id, string Name, string Description, int Model, int Height, int Width, int Length, int SizeClass, int Kind, int Masts);
public sealed record Nation(int Id, string Name, string Description);
public sealed record Job(int Id, string Name, string Description, int Line);

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
    private const int SkillTable = 6, GoodKindTable = 18, GoodTable = 19, ShipTable = 28, NationTable = 4, JobTable = 5;

    public IReadOnlyDictionary<int, City> Cities { get; }
    public IReadOnlyDictionary<int, SeaZone> Seas { get; }
    public IReadOnlyDictionary<int, Landing> Landings { get; }
    public IReadOnlyDictionary<int, string> DiscoveryKinds { get; }
    public IReadOnlyDictionary<int, Discovery> Discoveries { get; }
    /// <summary>스킬: id, 이름, 설명, u16 갈래(0 모험 · 1 교역 · 2 전투 · 3 언어 …), u16 작은 갈래, u16 × 3(창 안 자리로 보인다),
    /// u16 기본, u32 습득 비용, u16 직업 id.</summary>
    public IReadOnlyList<Skill> Skills { get; }
    public IReadOnlyDictionary<int, string> GoodKinds { get; }
    /// <summary>교역품: id(1600001~), 이름, 설명, u16 갈래. 값과 무게는 표에 없다.</summary>
    public IReadOnlyList<Good> Goods { get; }
    /// <summary>배: id, 이름, 설명, u16 모형 번호, u32 높이, u32 ?, u32 폭, u32 길이, u32 크기 등급, u32 갈래(0 범선 · 2 갤리 · 4 증기선),
    /// u32 돛대 수, f32 × 6. 내구·창고·값 같은 능력치는 표에 없다.</summary>
    public IReadOnlyList<Ship> Ships { get; }
    /// <summary>나라: id, 이름, 소개(첫 줄이 「본거지：도시」).</summary>
    public IReadOnlyList<Nation> Nations { get; }
    /// <summary>직업: id, 이름, 설명, u16 계열(0 모험 · 1 교역 · 2 전투).</summary>
    public IReadOnlyList<Job> Jobs { get; }

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
        Skills = Rows(Table(SkillTable), (r, id) =>
        {
            string name = r.Text(id), description = r.Text(id);
            int group = r.UInt16(), sub = r.UInt16();
            r.UInt16(); r.UInt16(); r.UInt16();
            return new Skill(id, name, description, group, sub, r.UInt16(), r.Int32(), r.UInt16());
        });
        GoodKinds = Rows(Table(GoodKindTable), (r, id) => (Id: id, Name: r.Text(id))).ToDictionary(k => k.Id, k => k.Name);
        Goods = Rows(Table(GoodTable), (r, id) => new Good(id, r.Text(id), r.Text(id), r.UInt16()));
        Ships = Rows(Table(ShipTable), (r, id) =>
        {
            string name = r.Text(id), description = r.Text(id);
            int model = r.UInt16(), height = r.Int32();
            r.Int32();
            var ship = new Ship(id, name, description, model, height, r.Int32(), r.Int32(), r.Int32(), r.Int32(), r.Int32());
            r.Skip(24);
            return ship;
        });
        Nations = Rows(Table(NationTable), (r, id) => new Nation(id, r.Text(id), r.Text(id)));
        Jobs = Rows(Table(JobTable), (r, id) => new Job(id, r.Text(id), r.Text(id), r.UInt16()));
    }

    private static List<T> Rows<T>(byte[] table, Func<Reader, int, T> row)
    {
        var reader = new Reader(table);
        int count = reader.Int32();
        var rows = new List<T>(count);
        for (int i = 0; i < count; i++) rows.Add(row(reader, reader.Int32()));
        return rows;
    }

    /// <summary>표 밖(장면 표 따위)의 같은 꼴 글 하나를 푼다. <paramref name="at"/> 은 u16 길이의 자리.</summary>
    public static string TextAt(byte[] data, int at, int rowId)
    {
        var reader = new Reader(data);
        reader.Skip(at);
        return reader.Text(rowId);
    }

    private sealed class Reader(byte[] data)
    {
        private int _at;

        public int Byte() => data[_at++];
        public void Skip(int bytes) => _at += bytes;
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
