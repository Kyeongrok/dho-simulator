using System.Buffers.Binary;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Dho.Data;

/// <summary>모험 의뢰 한 건. 조합 의뢰 목록은 클라이언트 자료에 없어서(서버가 내려 주던 것) 직접 짓는다.</summary>
public sealed class QuestData
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Client { get; set; } = "모험가 조합";
    /// <summary>조합 마스터가 의뢰를 내밀며 하는 말.</summary>
    public string Request { get; set; } = "";
    /// <summary>무엇을 하면 되는지 한 줄.</summary>
    public string Hint { get; set; } = "";
    /// <summary>의뢰를 받고 보고하는 도시(도시 표 id).</summary>
    public int CityId { get; set; }
    /// <summary>찾아낼 발견물(발견물 표 id).</summary>
    public int DiscoveryId { get; set; }
    /// <summary>상륙할 곳(상륙지 표 id). 자리는 상륙지의 X·Y.</summary>
    public int LandingId { get; set; }
    /// <summary>상륙했을 때 나오는 글.</summary>
    public string LandingText { get; set; } = "";
    public int Advance { get; set; }
    public int Reward { get; set; }
}

public sealed class CityData
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>0 본거지, 1 영지, 2 동맹항 …</summary>
    public int Kind { get; set; }
    public int Nation { get; set; }
    public int Culture { get; set; }
    /// <summary>뭍 위의 도시 자리(세계 좌표).</summary>
    public int X { get; set; }
    public int Y { get; set; }
    /// <summary>항구 앞바다 — 입항·출항하는 자리.</summary>
    public int SeaX { get; set; }
    public int SeaY { get; set; }
    /// <summary>항구 장면 번호(<c>0002</c> 파일 이름이 된다). 0 이면 없음.</summary>
    public int PortScene { get; set; }
}

public sealed class LandingData
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int City { get; set; }
    public int Region { get; set; }
    /// <summary>배를 대는 바다 자리(세계 좌표). 클라이언트 자료에 없어 직접 찍는다. 0 이면 아직 없음.</summary>
    public int X { get; set; }
    public int Y { get; set; }
}

public sealed class DiscoveryData
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int Kind { get; set; }
    public int Stars { get; set; }
    public int Exp { get; set; }
    public int Fame { get; set; }
}

public sealed class NamedData
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>해역이면 큰 바다 id.</summary>
    public int Group { get; set; }
}

/// <summary>항구 장면 안에서 배가 뜨는 자리.</summary>
public sealed class BerthData
{
    public int Scene { get; set; }
    public float X { get; set; }
    public float Z { get; set; }
    /// <summary>배 모형을 y 축으로 돌리는 각(라디안).</summary>
    public float Yaw { get; set; }
}

public sealed class SettingsData
{
    public string PlayerName { get; set; } = "김선민";
    public int Money { get; set; } = 1_093_057;
    public int StartCity { get; set; } = 28;
    /// <summary><c>0001\sh0000.bin</c> 안의 선체 항목 번호. 110 = SHIP16(큰 세 돛대 가로돛 배).</summary>
    public int ShipEntry { get; set; } = 110;
    /// <summary>시작할 때 하늘의 때. 0 = 자정, 0.5 = 한낮.</summary>
    public double StartSkyPhase { get; set; } = 0.40;
    public double SecondsPerDay { get; set; } = 60;
    public double SecondsPerSkyCycle { get; set; } = 600;
    public double MaxKnots { get; set; } = 14;
    /// <summary>1노트로 1초에 가는 세계 좌표.</summary>
    public double UnitsPerKnotSecond { get; set; } = 0.16;
    /// <summary>키를 돌리는 빠르기(초당 라디안).</summary>
    public double TurnRate { get; set; } = 0.9;
    public double PortRange { get; set; } = 14;
    public double LandingRange { get; set; } = 16;
    public List<BerthData> Berths { get; set; } = [new BerthData { Scene = 7004, X = 39500, Z = 28500, Yaw = 1.15f }];
}

/// <summary>
/// 게임이 돌아가는 자료 한 벌. <c>data</c> 폴더의 JSON 으로 읽고 쓴다.
/// </summary>
/// <remarks>
/// 직접 지은 것(저장소에 둔다): <c>settings.json</c>, <c>quests.json</c>, <c>landing-points.json</c>.
/// 게임 클라이언트에서 뽑은 것(저장소에 두지 않는다): <c>extracted\cities.json</c>, <c>seas.json</c>,
/// <c>landings.json</c>, <c>discoveries.json</c>, <c>discovery-kinds.json</c>. 없으면 클라이언트에서 다시 뽑는다.
/// </remarks>
public sealed class GameData
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string Directory { get; private set; } = "";
    public SettingsData Settings { get; set; } = new();
    public List<QuestData> Quests { get; set; } = [];
    public List<CityData> Cities { get; set; } = [];
    public List<NamedData> Seas { get; set; } = [];
    public List<LandingData> Landings { get; set; } = [];
    public List<DiscoveryData> Discoveries { get; set; } = [];
    public List<NamedData> DiscoveryKinds { get; set; } = [];

    /// <summary>
    /// 자료 폴더 — 환경 변수 <c>DHO_DATA</c>, 없으면 실행 파일에서 위로 올라가며 <c>data\settings.json</c> 을 찾고,
    /// 그래도 없으면 실행 파일 옆 <c>data</c>.
    /// </summary>
    public static string FindDirectory()
    {
        string? fromEnvironment = Environment.GetEnvironmentVariable("DHO_DATA");
        if (!string.IsNullOrEmpty(fromEnvironment)) return fromEnvironment;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "data", "settings.json")))
                return Path.Combine(dir.FullName, "data");
        return Path.Combine(AppContext.BaseDirectory, "data");
    }

    public static GameData Load(string? directory = null)
    {
        directory ??= FindDirectory();
        string extracted = Path.Combine(directory, "extracted");
        var data = new GameData { Directory = directory };

        if (!File.Exists(Path.Combine(extracted, "cities.json")))
        {
            data.ExtractFromClient();
            data.SaveExtracted();
        }
        else
        {
            data.Cities = Read<List<CityData>>(Path.Combine(extracted, "cities.json")) ?? [];
            data.Seas = Read<List<NamedData>>(Path.Combine(extracted, "seas.json")) ?? [];
            data.Landings = Read<List<LandingData>>(Path.Combine(extracted, "landings.json")) ?? [];
            data.Discoveries = Read<List<DiscoveryData>>(Path.Combine(extracted, "discoveries.json")) ?? [];
            data.DiscoveryKinds = Read<List<NamedData>>(Path.Combine(extracted, "discovery-kinds.json")) ?? [];
        }

        data.Settings = Read<SettingsData>(Path.Combine(directory, "settings.json")) ?? new SettingsData();
        data.Quests = Read<List<QuestData>>(Path.Combine(directory, "quests.json")) ?? [];
        var points = Read<List<LandingData>>(Path.Combine(directory, "landing-points.json")) ?? [];
        foreach (var point in points)
            if (data.Landings.Find(l => l.Id == point.Id) is { } landing) (landing.X, landing.Y) = (point.X, point.Y);
        return data;
    }

    /// <summary>직접 지은 것과 뽑은 것을 모두 적는다.</summary>
    public void Save()
    {
        System.IO.Directory.CreateDirectory(Directory);
        Write(Path.Combine(Directory, "settings.json"), Settings);
        Write(Path.Combine(Directory, "quests.json"), Quests);
        // 상륙지는 찍어 둔 자리만 따로 적는다 — 이름은 클라이언트 것이라 저장소에 두지 않는다
        Write(Path.Combine(Directory, "landing-points.json"),
              Landings.Where(l => l.X != 0 || l.Y != 0).Select(l => new { l.Id, l.X, l.Y }).ToList());
        SaveExtracted();
    }

    private void SaveExtracted()
    {
        string extracted = Path.Combine(Directory, "extracted");
        System.IO.Directory.CreateDirectory(extracted);
        Write(Path.Combine(extracted, "cities.json"), Cities);
        Write(Path.Combine(extracted, "seas.json"), Seas);
        Write(Path.Combine(extracted, "landings.json"), Landings);
        Write(Path.Combine(extracted, "discoveries.json"), Discoveries);
        Write(Path.Combine(extracted, "discovery-kinds.json"), DiscoveryKinds);
    }

    /// <summary>게임 클라이언트의 표에서 도시·해역·상륙지·발견물을 다시 뽑는다. 찍어 둔 상륙지 자리는 지킨다.</summary>
    public void ExtractFromClient()
    {
        var tables = new DataTables();
        var map = new WorldMap();
        var scenes = GvoFiles.ReadMwc(@"0000\local\dt000000.bin", GvoFiles.Korean);
        var points = Landings.Where(l => l.X != 0 || l.Y != 0).ToDictionary(l => l.Id, l => (l.X, l.Y));

        Cities = tables.Cities.Values.OrderBy(c => c.Id).Select(c =>
        {
            map.CityOnLand.TryGetValue(c.Id, out var land);
            map.CityAtSea.TryGetValue(c.Id, out var sea);
            return new CityData
            {
                Id = c.Id, Name = c.Name, Kind = c.Kind, Nation = c.Nation, Culture = c.Culture,
                X = land.X, Y = land.Y, SeaX = sea.X, SeaY = sea.Y, PortScene = PortSceneNumber(scenes, c.Id),
            };
        }).ToList();
        Seas = tables.Seas.Values.OrderBy(s => s.Id).Select(s => new NamedData { Id = s.Id, Name = s.Name, Group = s.Ocean }).ToList();
        Landings = tables.Landings.Values.OrderBy(l => l.Id).Select(l =>
        {
            points.TryGetValue(l.Id, out var point);
            return new LandingData { Id = l.Id, Name = l.Name, City = l.City, Region = l.Region, X = point.X, Y = point.Y };
        }).ToList();
        Discoveries = tables.Discoveries.Values.OrderBy(d => d.Id).Select(d => new DiscoveryData
        {
            Id = d.Id, Name = d.Name, Description = d.Description, Kind = d.Kind, Stars = d.Stars, Exp = d.Exp, Fame = d.Fame,
        }).ToList();
        DiscoveryKinds = tables.DiscoveryKinds.OrderBy(k => k.Key).Select(k => new NamedData { Id = k.Key, Name = k.Value }).ToList();
    }

    /// <summary>
    /// 장면 표(<c>dt000000</c>)에서 도시의 항구 장면 번호를 찾는다.
    /// 항구 줄: u32 장면 id(<c>(0x1C00 + 도시 id) &lt;&lt; 16</c>), u32 어미 장면, u32 도시 id, 이름,
    /// NUL 로 끝나는 글 셋(자원 이름 <c>TOWN_TUNIS_P_000</c>, 환경 <c>PORT004</c> 두 번), u32 장면 번호(7004).
    /// </summary>
    public static int PortSceneNumber(byte[] sceneTable, int cityId)
    {
        var key = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(key, (uint)(0x1C00 + cityId) << 16);
        for (int at = 0; ; at += 4)
        {
            int found = sceneTable.AsSpan(at).IndexOf(key);
            if (found < 0) return 0;
            at += found;
            if (at + 14 > sceneTable.Length || BinaryPrimitives.ReadInt32LittleEndian(sceneTable.AsSpan(at + 8)) != cityId) continue;

            int cursor = at + 12;
            cursor += 2 + BinaryPrimitives.ReadUInt16LittleEndian(sceneTable.AsSpan(cursor));
            string resource = "";
            for (int i = 0; i < 3; i++)
            {
                int end = Array.IndexOf(sceneTable, (byte)0, cursor);
                if (end < 0) return 0;
                if (i == 0) resource = Encoding.ASCII.GetString(sceneTable, cursor, end - cursor);
                cursor = end + 1;
            }
            if (!resource.StartsWith("TOWN_") || cursor + 4 > sceneTable.Length) continue;
            return BinaryPrimitives.ReadInt32LittleEndian(sceneTable.AsSpan(cursor));
        }
    }

    private static T? Read<T>(string path) where T : class =>
        File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) : null;

    private static void Write<T>(string path, T value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
}
