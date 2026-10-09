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
/// <summary>배 부품. Slot: 0 보조돛(A 가로돛, B 세로돛) · 1 장갑(A 장갑, B 속도 줄임) · 2 선수상(A ~ D 효과 넷) · 3 문장(수치 없음).</summary>
/// <summary>주점의 차림 한 가지(표 37) — 갈래: 0 술 · 1 요리 · 2 음료 · 3 물담배.</summary>
public sealed record TavernDish(int Id, string Name, string Description, int Kind);
/// <summary>대학 연구의 할 일(표 64): 이름과 원본 설명 글(「1회 교역으로 10만 두캇 이상 흑자를 낸다」).</summary>
public sealed record StudyTask(int Id, string Name, string Description);
/// <summary>대포만: 장전 속도 · 탄속 · 폭발 범위(표 22 의 일곱째 · 다섯째 · 여섯째 값, 1 ~ 10 — 클수록 좋다: 「명품」이 늘 하나씩 높다).</summary>
public sealed record ShipPart(int Id, string Name, string Description, int Slot, int A, int B, int C, int D, int Durability, int Reload = 0, int ShotSpeed = 0, int Blast = 0);
/// <summary>
/// 선박 데코(표 138) — 줄 꼬리 10바이트: u8 × 5 붙일 수 있는 자리(마스트 톱, 전방 측면 둘, 뒤쪽 측면 둘), u16 모형으로 보이는 번호, u8 ?(깃발은 나라 차례), u8 0, u8 갈래
/// (0 마스트 톱의 깃발 · 상, 1 작은 배, 2 랜턴, 3 앵커, 4 상, 5 장식, 6 방패). 자리의 뜻은 설명 글(「마스트 톱」 · 「전방 측면」 · 「뒤쪽 측면」)과 맞춰 본 것이다.
/// </summary>
public sealed record ShipDeco(int Id, string Name, string Description, int[] Spots, int Model, int Extra, int Kind);
/// <summary>
/// 선원 장비(표 139) — 줄 꼬리 26바이트: u8 갈래(0 · 1 · 2 — 번호대 2600 · 2601 · 2602 과 같다), s8 × 5 수치(뜻은 못 밝혔다), u8 내구, 나머지 19바이트는 안 풀었다.
/// </summary>
public sealed record CrewGear(int Id, string Name, string Description, int Kind, int[] Values, int Durability, int Skill = 0);
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
    private const int SkillTable = 6, GoodKindTable = 18, GoodTable = 19, ShipTable = 28, NationTable = 4, JobTable = 5, RecipeTable = 16, PlaceTable = 40, AideTable = 131, DutyTable = 36, ArmorTable = 23, SailTable = 25, FigureheadTable = 27;

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
    /// <summary>배 부품 — 보조돛(표 25: u32 가로돛, 세로돛, ?, 내구) · 장갑(표 23: u16 장갑, u16 속도 줄임, u32 내구) · 선수상(표 27: u32 × 4, u32 내구).</summary>
    public IReadOnlyList<ShipPart> ShipParts { get; }
    /// <summary>레시피(표 16): id, 이름, 설명 — 3,409줄. 재료와 생산물은 표에 없다.</summary>
    public IReadOnlyList<(int Id, string Name, string Description)> Recipes { get; }
    /// <summary>부관 후보(표 131): id, 이름, u8 얼굴 번호로 짐작, u8 차례. 32명.</summary>
    public IReadOnlyList<(int Id, string Name, int A, int B)> Aides { get; }
    /// <summary>부관의 담당(표 36): 0 항해장 · 1 감시 · 2 회계사 · 3 창고당번 · 4 부함장 · 5 선의.</summary>
    public IReadOnlyDictionary<int, string> Duties { get; }
    public List<TavernDish> TavernMenu { get; }
    public List<StudyTask> StudyTasks { get; }
    /// <summary>상륙지 번호 → 세계지도 표식의 자리(세계 좌표, 뭍 위일 수 있다).</summary>
    public IReadOnlyDictionary<int, (int X, int Y)> LandingSpots { get; }
    /// <summary>뭍 탐색 지점(표 106): 상륙지마다 관찰 지점의 수와 채집 지점의 갈래(1 ~ 5, 갈래마다 셋).</summary>
    public IReadOnlyDictionary<int, (int Observe, List<int> Gather)> LandPoints { get; }
    public List<(int Zone, int Kind, int Target, int X, int Y)> ZoneMarks { get; }
    public List<(int Id, string Name, string Description)> Honors { get; }
    /// <summary>문화권 이름(표 1, 32줄) — 도시 표의 Culture 가 이 번호다: 1 북유럽 · 4 브리튼 섬 · 6 이베리아 · 11 북아프리카 …</summary>
    public List<(int Id, string Name)> Cultures { get; }
    /// <summary>애완동물(표 47, 57줄): 이름 + 36바이트(실수 여럿 — 놓는 자리 · 크기로 보인다, 안 쓴다).</summary>
    public List<(int Id, string Name)> Pets { get; }
    /// <summary>탄의 이름(표 21) — 번호는 대포 줄의 탄 갈래.</summary>
    public IReadOnlyDictionary<int, string> Ammo { get; }
    /// <summary>시내 장소 이름(표 40): 9 조선소 · 10 교역소 · 14 은행 … — 시내 지도의 표식이 이 번호를 쓴다.</summary>
    public IReadOnlyDictionary<int, string> Places { get; }
    /// <summary>아이템 표(14)의 이름 — 번호, 이름, 설명뿐인 표. 게임이 따로 다루지 않는 아이템(의뢰 알선서 · 책 · 부적 …)의 이름을 보이는 데 쓴다.</summary>
    public IReadOnlyDictionary<int, string> ItemNames { get; } = new Dictionary<int, string>();
    /// <summary>선박 재질의 설명 글(표 30: 번호 · 이름 · 설명, 99줄) — 이름 → 설명. 번호는 gvdb 재질 목록의 번호와 달라 이름으로 잇는다.</summary>
    public IReadOnlyDictionary<string, string> MaterialNotes { get; } = new Dictionary<string, string>();
    /// <summary>그레이드 보너스의 설명 글(표 90: 번호 · 이름 · 설명, 32줄 — 이름이 ※ 인 줄은 뺀다) — 번호 → 설명.</summary>
    public IReadOnlyDictionary<int, string> GradeBonusNotes { get; } = new Dictionary<int, string>();
    /// <summary>알림 메모의 풀이 글(표 121: 번호 · 제목 · 「…메모가 있습니다」 · 풀이, 53줄) — 번호 → 풀이. 2 작위 수여 · 3 입항허가 …</summary>
    public IReadOnlyDictionary<int, string> Memos { get; } = new Dictionary<int, string>();
    /// <summary>역사적 사건의 결말(표 87) — 번호 → 글 다섯(이름 · 본문 · 결과 줄 셋).</summary>
    public IReadOnlyDictionary<int, string[]> HistoryEnds { get; } = new Dictionary<int, string[]>();
    /// <summary>전승(표 124) — 번호 → 이름 · 본문 · 이어진 발견물 번호(글로). 뒤의 나머지 78 바이트(번호 하나와 좌표로 보이는 값)는 아직 안 쓴다.</summary>
    public IReadOnlyDictionary<int, string[]> Legends { get; } = new Dictionary<int, string[]>();
    /// <summary>단계가 있는 이야기(표 81) — 번호 → 이름 · 설명 · 그 뒤로 단계마다 「조건 값|단계 번호」(단계 번호는 표 82 의 번호).</summary>
    public IReadOnlyDictionary<int, string[]> Tales { get; } = new Dictionary<int, string[]>();
    /// <summary>이야기의 단계 글(표 82) — 번호 → 글.</summary>
    public IReadOnlyDictionary<int, string> TaleSteps { get; } = new Dictionary<int, string>();
    /// <summary>선박 데코(표 138) 321줄과 선원 장비(표 139) 214줄 — 이름이 「※」인 빈 줄까지 그대로.</summary>
    public IReadOnlyList<ShipDeco> Decos { get; }
    public IReadOnlyList<CrewGear> CrewGears { get; }

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
        var parts = Rows(Table(SailTable), (r, id) =>
        {
            string name = r.Text(id), description = r.Text(id);
            int square = r.Int32(), foreAft = r.Int32();
            r.Int32();
            return new ShipPart(id, name, description, 0, square, foreAft, 0, 0, r.Int32());
        });
        parts.AddRange(Rows(Table(ArmorTable), (r, id) => new ShipPart(id, r.Text(id), r.Text(id), 1, r.UInt16(), r.UInt16(), 0, 0, r.Int32())));
        parts.AddRange(Rows(Table(FigureheadTable), (r, id) => new ShipPart(id, r.Text(id), r.Text(id), 2, r.Int32(), r.Int32(), r.Int32(), r.Int32(), r.Int32())));
        // 문장(표 26): id(1100001 ~), 이름, 설명뿐 — 수치가 없다
        parts.AddRange(Rows(Table(26), (r, id) => new ShipPart(id, r.Text(id), r.Text(id), 3, 0, 0, 0, 0, 0)));
        // 대포(표 22): id(700100 ~), 이름, 설명, i32 × 10 — 문 수, 관통력, 다는 자리(0 선측 · 1 선수 · 2 선미), 사정거리, 탄속, ?, ?, 1, 내구, 탄 갈래.
        // 자리와 탄 갈래는 설명 글(「선측에 8문」 · 「선수에 4문」 · 「사슬탄」)과 맞춰 본 것이고, 다섯째 ~ 일곱째는 gvdb 아이템 목록의 글과 맞는다(팔콘포 2문: 「弾速：8 · 炸裂範囲：1 · 装填速度：6」) — 탄속 · 폭발 범위 · 장전 속도. 게임은 장전 속도만 쓴다(Voyage.CannonReload — 수가 클수록 빠르다고 본 것과 곱의 크기는 지은 값); 탄속 · 폭발 범위는 안 쓴다
        parts.AddRange(Rows(Table(22), (r, id) =>
        {
            string name = r.Text(id), description = r.Text(id);
            int count = r.Int32(), pierce = r.Int32(), spot = r.Int32(), range = r.Int32();
            int shotSpeed = r.Int32(), blast = r.Int32(), reload = r.Int32();
            r.Skip(4);
            int durability = r.Int32(), ammo = r.Int32();
            // D = 다는 자리 + 탄 갈래 × 10 (탄 갈래는 표 21 의 번호 — 0 통상탄 … 18 파쇄 유탄, 이름이 설명 글과 맞는다)
            return new ShipPart(id, name, description, 4, count, pierce, range, spot + ammo * 10, durability, reload, shotSpeed, blast);
        }));
        // 특수장비(표 24): id(900001 ~), 이름, 설명, i32 갈래(0 충각 · 1 백병전 지원(gvdb 「白兵戦支援」) · 2 선수 추가돛 · 3 선미 추가돛 · 4 조교 · 5 기관포 · 6 화염방사기 · 7 방벽), i32 세기, i32 내구, 그 뒤 10바이트(못 풀었다)
        parts.AddRange(Rows(Table(24), (r, id) =>
        {
            string name = r.Text(id), description = r.Text(id);
            int kind = r.Int32(), power = r.Int32(), durability = r.Int32();
            r.Skip(10);
            return new ShipPart(id, name, description, 5, kind, power, 0, 0, durability);
        }));
        // 탄(표 21): id 0 ~ 18, 이름 — 줄 꼬리는 풀지 않고 다음 줄의 id 를 찾아 넘어간다
        var ammo = new Dictionary<int, string>();
        try
        {
            byte[] shells = Table(21);
            int rows = BinaryPrimitives.ReadInt32LittleEndian(shells), at = 4;
            for (int n = 0; n < rows && at + 6 <= shells.Length; n++)
            {
                int size = BinaryPrimitives.ReadUInt16LittleEndian(shells.AsSpan(at + 4));
                ammo[n] = TextAt(shells, at + 4, n);
                at += 6 + size;
                while (at + 6 <= shells.Length && !(BinaryPrimitives.ReadInt32LittleEndian(shells.AsSpan(at)) == n + 1 && BinaryPrimitives.ReadUInt16LittleEndian(shells.AsSpan(at + 4)) is > 0 and < 60)) at++;
            }
        }
        catch (Exception) { }
        Ammo = ammo;
        // 주점의 차림(표 37): id, 이름, 설명, u16 갈래(0 술 · 1 요리 · 2 음료 · 3 물담배) — 244줄을 끝까지 읽어 표의 끝과 맞는다
        // 호칭(표 35): id, 이름, 설명 — 92줄을 끝까지 읽어 표의 끝과 맞는다
        Honors = Rows(Table(35), (r, id) => (id, r.Text(id), r.Text(id)));
        Cultures = Rows(Table(1), (r, id) => (id, r.Text(id)));
        Pets = Rows(Table(47), (r, id) => { string name = r.Text(id); r.Skip(36); return (id, name); });
        TavernMenu = Rows(Table(37), (r, id) => new TavernDish(id, r.Text(id), r.Text(id), r.UInt16()));
        StudyTasks = Rows(Table(64), (r, id) => new StudyTask(id, r.Text(id), r.Text(id)));
        // 세계지도의 표식(표 103): u32 id, u8 갈래(3 상륙지 · 4 해역 이름 · 7 · 8 · 9 ?), u16 대상 번호, u16 x, u16 y, 그 뒤 4바이트 0 — 15바이트 고정.
        // 자리는 640 × 320 짜리 세계지도 그림 위의 것이다: 세계 좌표 x ≈ (x × 25.6 + 8270) mod 16384, y = y × 25.6 (손으로 찍어 둔 상륙지 27곳과 맞춰 본 것)
        var spots = new Dictionary<int, (int X, int Y)>();
        try
        {
            byte[] marks = Table(103);
            int count = BinaryPrimitives.ReadInt32LittleEndian(marks);
            for (int k = 0; k < count && 4 + k * 15 + 11 <= marks.Length; k++)
            {
                int at = 4 + k * 15;
                if (marks[at + 4] != 3) continue;
                int target = BinaryPrimitives.ReadUInt16LittleEndian(marks.AsSpan(at + 5)), mx = BinaryPrimitives.ReadUInt16LittleEndian(marks.AsSpan(at + 7)), my = BinaryPrimitives.ReadUInt16LittleEndian(marks.AsSpan(at + 9));
                spots[target] = ((int)((mx * 25.6 + 8270) % 16384), (int)(my * 25.6));      // 8270: 찍어 둔 자리들과 견준 값(반 바퀴 8192 에서 그림 세 칸쯤 더 — 어림)
            }
        }
        catch (Exception) { }
        LandingSpots = spots;
        // 뭍 탐색 지점(표 106): u32 번호, u16 상륙지 id, u8 갈래(0 관찰 · 1 채집), u32 참조(채집이면 1 ~ 5), u16 x, u16 y — 15바이트 고정
        var landPoints = new Dictionary<int, (int Observe, List<int> Gather)>();
        var pointTable = Table(106);
        for (int at = 4, left = BitConverter.ToInt32(pointTable, 0); left > 0 && at + 15 <= pointTable.Length; at += 15, left--)
        {
            int site = BitConverter.ToUInt16(pointTable, at + 4), kind = pointTable[at + 6], about = BitConverter.ToInt32(pointTable, at + 7);
            var entry = landPoints.GetValueOrDefault(site, (0, new List<int>()));
            if (kind == 0) entry.Item1++;
            else if (!entry.Item2.Contains(about)) entry.Item2.Add(about);
            landPoints[site] = entry;
        }
        LandPoints = landPoints;
        // 해역 지도의 표식(표 102): u32 id, u8 해역 번호, u8 갈래(1 도시 · 3 상륙지 · 6 ?), u16 대상 번호, u16 x, u16 y, 4바이트 0 — 16바이트 고정.
        // 자리는 그 해역의 지도 그림(200 칸 안팎) 위의 것이라 세계지도 표식보다 여덟 배쯤 촘촘하다. 해역마다 배율이 달라 그 해역의 도시들로 맞춰야 한다
        var zoneMarks = new List<(int Zone, int Kind, int Target, int X, int Y)>();
        try
        {
            byte[] marks = Table(102);
            int count = BinaryPrimitives.ReadInt32LittleEndian(marks);
            for (int k = 0; k < count && 4 + k * 16 + 12 <= marks.Length; k++)
            {
                int at = 4 + k * 16;
                zoneMarks.Add((marks[at + 4], marks[at + 5], BinaryPrimitives.ReadUInt16LittleEndian(marks.AsSpan(at + 6)), BinaryPrimitives.ReadUInt16LittleEndian(marks.AsSpan(at + 8)), BinaryPrimitives.ReadUInt16LittleEndian(marks.AsSpan(at + 10))));
            }
        }
        catch (Exception) { }
        ZoneMarks = zoneMarks;
        ShipParts = parts;
        Recipes = Rows(Table(RecipeTable), (r, id) => (id, r.Text(id), r.Text(id)));
        Aides = Rows(Table(AideTable), (r, id) => (id, r.Text(id), r.Byte(), r.Byte()));
        Duties = Rows(Table(DutyTable), (r, id) => (Id: id, Name: r.Text(id))).ToDictionary(d => d.Id, d => d.Name);
        Places = Rows(Table(PlaceTable), (r, id) => (Id: id, Name: r.Text(id))).ToDictionary(p => p.Id, p => p.Name);
        try { ItemNames = Rows(Table(14), (r, id) => { string name = r.Text(id); r.Text(id); return (Id: id, Name: name); }).GroupBy(p => p.Id).ToDictionary(p => p.Key, p => p.First().Name); }
        catch (Exception e) { Console.Error.WriteLine("ItemNames: " + e.Message); }
        try { MaterialNotes = Rows(Table(30), (r, id) => (Name: r.Text(id), Note: r.Text(id))).Where(p => p.Name.Length > 0 && p.Note.Length > 0).GroupBy(p => p.Name).ToDictionary(p => p.Key, p => p.First().Note); }
        catch (Exception e) { Console.Error.WriteLine("MaterialNotes: " + e.Message); }
        try { GradeBonusNotes = Rows(Table(90), (r, id) => (Id: id, Name: r.Text(id), Note: r.Text(id))).Where(p => !p.Name.StartsWith('※') && p.Note.Length > 0).GroupBy(p => p.Id).ToDictionary(p => p.Key, p => p.First().Note); }
        catch (Exception e) { Console.Error.WriteLine("GradeBonusNotes: " + e.Message); }
        try { Memos = Rows(Table(121), (r, id) => { r.Text(id); r.Text(id); return (Id: id, Note: r.Text(id)); }).Where(p => p.Note.Length > 0).GroupBy(p => p.Id).ToDictionary(p => p.Key, p => p.First().Note); }
        catch (Exception e) { Console.Error.WriteLine("Memos: " + e.Message); }
        try { HistoryEnds = Rows(Table(87), (r, id) => (Id: id, Texts: new[] { r.Text(id), r.Text(id), r.Text(id), r.Text(id), r.Text(id) })).Where(p => p.Texts[0].Length > 0).GroupBy(p => p.Id).ToDictionary(p => p.Key, p => p.First().Texts); }
        catch (Exception e) { Console.Error.WriteLine("HistoryEnds: " + e.Message); }
        try { Legends = Rows(Table(124), (r, id) => { string name = r.Text(id), body = r.Text(id); int find = r.UInt16(); r.Skip(78); return (Id: id, Texts: new[] { name, body, find.ToString() }); }).Where(p => p.Texts[0].Length > 0).GroupBy(p => p.Id).ToDictionary(p => p.Key, p => p.First().Texts); }
        catch (Exception e) { Console.Error.WriteLine("Legends: " + e.Message); }
        try
        {
            Tales = Rows(Table(81), (r, id) =>
            {
                var texts = new List<string> { r.Text(id), r.Text(id) };
                // 7 바이트 칸 열 개: 조건 값 u8 · 단계 번호 u16 · 바이트 넷(안 쓴다) — 조건 값이 0 이면 빈 칸
                for (int k = 0; k < 10; k++) { int need = r.Byte(), step = r.UInt16(); r.Skip(4); if (need > 0) texts.Add($"{need}|{step}"); }
                return (Id: id, Texts: texts.ToArray());
            }).Where(p => p.Texts[0].Length > 0).GroupBy(p => p.Id).ToDictionary(p => p.Key, p => p.First().Texts);
            TaleSteps = Rows(Table(82), (r, id) => (Id: id, Note: r.Text(id))).Where(p => p.Note.Length > 0).GroupBy(p => p.Id).ToDictionary(p => p.Key, p => p.First().Note);
        }
        catch (Exception e) { Console.Error.WriteLine("Tales: " + e.Message); }
        Decos = Rows(Table(138), (r, id) =>
        {
            string name = r.Text(id), description = r.Text(id);
            int[] spots = [r.Byte(), r.Byte(), r.Byte(), r.Byte(), r.Byte()];
            int model = r.UInt16(), extra = r.Byte();
            r.Byte();
            return new ShipDeco(id, name, description, spots, model, extra, r.Byte());
        });
        CrewGears = Rows(Table(139), (r, id) =>
        {
            string name = r.Text(id), description = r.Text(id);
            int kind = r.Byte();
            int[] values = [(sbyte)r.Byte(), (sbyte)r.Byte(), (sbyte)r.Byte(), (sbyte)r.Byte(), (sbyte)r.Byte()];
            int durability = r.Byte();
            // 뒤 19바이트: 그림 갈래로 보이는 수 1 · **선박 스킬 번호 u16**(중량탄 → 2040 중량포격 · 강화 화염탄 → 2042 장갑열화탄 · 키 장인의 톱 → 2008 강화키 — 해전 글 「선원 장비 스킬 '%s'」의 그 스킬) · 0 열 개 · (갈래, 단계) 짝 셋
            r.Byte();
            int skill = r.UInt16();
            r.Skip(16);
            return new CrewGear(id, name, description, kind, values, durability, skill);
        });
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
