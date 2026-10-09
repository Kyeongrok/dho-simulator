using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 입항 허가 — 먼 바다의 항구는 허가를 얻어야 들어갈 수 있다.
/// 어느 바다가 허가를 요구하고 어디서 얻는가는 클라이언트의 알림 메모(표 121 의 32 ~ 37)에 글로 있다:
/// 중남미 동쪽 해안 = 자국 본거지의 칙명, 동남아시아 = 캘리컷의 집정관, 중남미 서쪽 해안 = 리우데자네이루, 동아시아 = 마닐라.
/// 원본은 칙명을 해내야 허가가 나온다 — 여기서는 명성(모험 + 교역 + 전투)이 차면 그 자리에서 내준다. 명성의 문턱은 지은 값이다.
/// 운하(파나마 · 수에즈)의 항해 허가는 넣지 않았다.
/// 혼자 하는 게임에서 길이 막히는 것이 싫으면 모드 「입항 허가 없이 다닌다」를 켠다(기본 켜짐).
/// </summary>
internal sealed partial class Voyage
{
    /// <summary>얻은 허가 — 큰 바다(표 9)의 번호.</summary>
    public HashSet<int> Permits { get; } = [];

    /// <summary>(큰 바다 번호, 이름, 허가를 내주는 도시 — 0 이면 자국 본거지, 필요한 명성).</summary>
    public static readonly (int Ocean, string Name, int CityId, int Fame)[] PermitRules =
    [
        (11, "중남미 동쪽 해안", 0, 1000), (IndianOcean, "인도양", 0, 16000), (12, "동남아시아", 70, 3000), (14, "중남미 서쪽 해안", 123, 6000), (18, "동아시아", 141, 10000),
    ];

    // 인도양 입항허가에 필요한 명성 — 나라마다 다르다(사용자가 준 벨벳 글, 2026-10-08): 에스파니아 20000 · 포르투갈 16000 · 베네치아 14000 · 프랑스 14000 · 네덜란드 16000 · 잉글랜드 24000. 그 밖의 나라는 16000 으로 본다(짐작)
    public const int IndianOcean = 10;
    private int IndiaFame => NationId switch { 1 => 20000, 2 => 16000, 3 => 14000, 4 => 14000, 5 => 16000, 6 => 24000, _ => 16000 };

    public int TotalFame => AdventureFame + TradeFame + BattleFame;

    private int OceanOf(CityData city) => Data.Seas.Find(s => s.Id == Zones.ZoneAt(city.SeaX, city.SeaY))?.Group ?? 0;

    /// <summary>그 도시에 들어가는 데 필요한데 아직 없는 허가 — 없으면(들어가도 되면) null.</summary>
    public string? PermitMissing(CityData city)
    {
        if (Data.Settings.ModNoPermits) return null;
        int ocean = OceanOf(city);
        foreach (var rule in PermitRules)
            if (rule.Ocean == ocean && !Permits.Contains(ocean)) return rule.Name;
        return null;
    }

    /// <summary>지금 이 도시에서 얻을 수 있는(아직 없는) 허가.</summary>
    public (int Ocean, string Name, int CityId, int Fame)? PermitOffered()
    {
        if (Mode != Mode.Port) return null;
        foreach (var rule in PermitRules)
            if (!Permits.Contains(rule.Ocean) && (rule.CityId == 0 ? AtCourt : City.Id == rule.CityId)) return rule.Ocean == IndianOcean ? rule with { Fame = IndiaFame } : rule;
        return null;
    }

    public void TakePermit()
    {
        if (PermitOffered() is not { } rule || TotalFame < rule.Fame) return;
        Permits.Add(rule.Ocean);
        Cues.Enqueue("Done");
        Say($"{rule.Name}으로의 입항 허가를 얻었다.");
        LevelNotice = ($"입항 허가 — {rule.Name}", LevelNotice.Count + 1);
    }
}
