using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 서고의 지도 — 서고에서 학문 서적을 읽다가 지도를 얻고, 지도가 가리키는 자리에 가서 찾으면 발견한다(조합에 보고하지 않는다 · 보수가 없다 — 원본대로).
/// 지도(나오는 서고의 도시 · 학문 갈래 · 필요 스킬 랭크 · 발견물 · 찾는 자리)는 gvdb 의뢰 표의 학문 갈래 줄에서 읽은 것이다
/// (<c>tools\gvo\gvdb_quests.py --maps</c> → <c>map-facts-gvdb.json</c> 878건 — 자리를 읽은 417건만 쓴다).
/// 지은 것: 지도의 이름(원본 이름 「○○の地図」는 일본어뿐 — 「(발견물 갈래)의 지도」) · 서적을 한 번 읽을 때 지도가 나올 확률(<see cref="MapChance"/>) ·
/// 한 번에 지도 하나만 든다는 것(모험 의뢰와 같은 자리를 쓴다 — 원본은 지도를 여러 장 갖는다) · 낮은 랭크의 지도 셋 가운데서 나오는 것.
/// </summary>
internal sealed partial class Voyage
{
    public const int MapQuestBase = 400_000;
    /// <summary>서적을 한 번 읽을 때 지도가 나올 확률 — 지은 값(원본의 확률은 자료가 없다).</summary>
    public const double MapChance = 0.4;

    // gvdb 의 학문 갈래 이름 → 게임의 학문 스킬 이름
    private static readonly Dictionary<string, string> MapFields = new()
    {
        ["生物学"] = "생물학", ["地理学"] = "지리학", ["考古学"] = "고고학", ["財宝鑑定"] = "보물 감정", ["天文学"] = "천문학", ["宗教学"] = "종교학", ["美術"] = "미술",
    };

    private List<QuestData>? _mapQuests;

    /// <summary>지도 전부(처음 쓸 때 한 번 만든다) — 모험 의뢰와 같은 꼴(QuestData)이라 찾는 길이 같다.</summary>
    public List<QuestData> MapQuests => _mapQuests ??= MakeMaps();

    private List<QuestData> MakeMaps()
    {
        var made = new List<QuestData>();
        var sites = Data.Landings.Where(l => l.X != 0 || l.Y != 0).ToList();
        var ports = Data.Cities.Where(c => c.SeaX != 0 || c.SeaY != 0).ToList();
        int most = Data.Settings.MaxSkillRank;
        for (int n = 0; n < Data.MapFacts.Count; n++)
        {
            var real = Data.MapFacts[n];
            if (Data.Discoveries.Find(d => d.Id == real.DiscoveryId) is not { } target) continue;
            if (!MapFields.TryGetValue(real.Field, out var fieldName) || Data.Skills.Find(s => s.Name == fieldName) is not { } field) continue;
            string kindName = Data.DiscoveryKinds.Find(k => k.Id == target.Kind)?.Name ?? "발견물";
            var site = real.Place == 2 ? sites.Find(l => l.Id == real.LandingId) : null;
            var town = real.Place == 3 ? ports.Find(c => c.Id == real.TownId) : null;
            int zoneId = real.Place == 4 && Data.Seas.Exists(s => s.Id == real.SeaZone) ? real.SeaZone : 0;
            if (!(real.Place == 1 && real.X > 0 && real.Y > 0) && site == null && town == null && zoneId == 0) continue;
            string where = site?.Name ?? town?.Name ?? (zoneId > 0 ? Data.Seas.Find(s => s.Id == zoneId)!.Name : _seas.TryGetValue(Zones.ZoneAt(real.X, real.Y), out var seaAt) ? seaAt : "먼 바다");
            // 학문 스킬의 랭크(지도를 읽는 데도 · 감정에도 쓴다)와 찾는 스킬의 랭크
            int study = real.Skills.Where(s => s.Name is not ("視認" or "探索" or "生態調査" or "開錠") && !s.Name.EndsWith('語')).Select(s => s.Rank).DefaultIfEmpty(1).Max();
            int look = real.Skills.Where(s => s.Name is "視認" or "探索" or "生態調査").Select(s => s.Rank).DefaultIfEmpty(0).Max();
            bool atSea = site == null && town == null && zoneId == 0;
            // 원본의 지도 이름과 길잡이 글(클라이언트 지도 표 17 — 「카이로 건너편에 상륙. 안쪽의 기자지방. 유적 내부. 탐색，고고학 랭크 1」). 번호를 못 가린 지도는 이름만 원본 것이다
            string title = Data.MapNames.GetValueOrDefault(real.MapId > 0 ? real.MapId : real.NameId) is { Length: > 0 } named ? named[0] : $"{kindName}의 지도";
            string guide = real.MapId > 0 && Data.MapNames.GetValueOrDefault(real.MapId) is { Length: > 1 } told ? told[1].Replace("\n", " ").Trim().Trim((char)34) : "";
            for (int k = 0; k < Math.Min(real.Cities.Count, 8); k++)
            {
                if (ports.Find(c => c.Id == real.Cities[k]) is not { } library) continue;
                made.Add(new QuestData
                {
                    Id = MapQuestBase + n * 8 + k, Title = title, Client = $"{library.Name} 서고", CityId = library.Id, DiscoveryId = real.DiscoveryId, MapSkill = field.Id,
                    SeaX = atSea ? real.X : 0, SeaY = atSea ? real.Y : 0, SeaZone = zoneId, LandingId = site?.Id ?? 0, SearchCity = town?.Id ?? 0,
                    NightOnly = real.Night, Rank = Math.Clamp(study, 1, most), FindRank = look,
                    Request = guide != "" ? guide : $"{fieldName} 서적 사이에서 나온 지도다. {where} 쪽의 {kindName}을(를) 가리킨다.",
                    Hint = site != null ? $"{where}에 상륙해 주변을 탐색한다." : town != null ? $"{where}에 입항해 「의뢰 탐색」을 한다."
                         : zoneId > 0 ? $"{where}에 나가 둘레를 살핀다(F)." + (real.Night ? " 밤에, 날씨가 거칠지 않을 때만 보인다." : "") : $"{where} ({real.X}, {real.Y}) 부근에서 둘레를 살핀다(F).",
                    LandingText = site != null ? $"{where}에 올랐다.\n지도에 그려진 자리를 찾아 둘레를 살핀다." : "",
                });
            }
        }
        return made;
    }

    /// <summary>이 도시 서고의 그 학문 서적에서 지금 나올 수 있는 지도 — 아직 못 찾은 발견물의 것, 그 학문의 랭크가 찬 것. 랭크 낮은 것부터.</summary>
    public List<QuestData> MapsHere(int skillId) =>
        MapQuests.Where(q => q.CityId == City.Id && q.MapSkill == skillId && !Found.Contains(q.DiscoveryId) && !_done.Contains(q.Id) && Rank(skillId) >= q.Rank)
            .GroupBy(q => q.DiscoveryId).Select(g => g.First()).OrderBy(q => q.Rank).ThenBy(q => q.Id).ToList();

    /// <summary>이 도시 서고에 그 학문의 지도가 (랭크와 상관없이) 몇 장 있는가 — 서고 창에 보인다.</summary>
    public int MapsShelved(int skillId) => MapQuests.Where(q => q.CityId == City.Id && q.MapSkill == skillId && !Found.Contains(q.DiscoveryId)).Select(q => q.DiscoveryId).Distinct().Count();

    /// <summary>
    /// 이 도시에 지도는 있는데 시내 지도에 서고 표식(장소 16)이 없는가 — 그런 도시(샌프란시스코 · 리마 · 암보이나 … 15곳)는 원본에서 서고 건물 없이 시내의 학자가 서적을 보여 준다
    /// (gvdb 판매 표에 파는 사람 「学者」가 있다 — 자리는 모른다). 여기서는 항구 앞 의뢰 중개인 창에 「서적 열람」을 둔다(줄인 것).
    /// </summary>
    public bool ScholarHere => _scholarTowns.TryGetValue(City.Id, out bool known) ? known
        : _scholarTowns[City.Id] = MapQuests.Exists(q => q.CityId == City.Id) && (TownMap.Load(City.Id) is not { Marks.Count: > 0 } town || !town.Marks.Any(m => m.Place == 16));
    private readonly Dictionary<int, bool> _scholarTowns = [];

    /// <summary>지금 든 것이 지도인가.</summary>
    public bool HoldsMap => Quest is { MapSkill: > 0 };

    // 서적을 읽은 뒤 — 지도가 나올 수 있으면 확률로 하나 얻는다. 의뢰나 지도를 이미 들고 있으면 안 나온다(한 번에 하나 — 지은 것)
    private void MaybeFindMap(SkillData book)
    {
        if (Quest != null || MapsHere(book.Id) is not { Count: > 0 } maps || _random.NextDouble() >= MapChance) return;
        TakeMap(maps[_random.Next(Math.Min(3, maps.Count))]);
    }

    private void TakeMap(QuestData map)
    {
        (Quest, QuestStage, Offered) = (map, QuestStage.Accepted, null);
        Cues.Enqueue("Quest");
        Say($"서적 사이에서 「{map.Title}」을(를) 찾았다! — {map.Hint}");
    }

    // 지도로 찾았으면 보고 없이 끝난다 — 발견 창이 닫힌 뒤에 지도를 내려놓는다
    private void FinishMapIfFound()
    {
        if (Quest is not { MapSkill: > 0 } map || QuestStage != QuestStage.Discovered || Dialog == Dialog.Discovery) return;
        _done.Add(map.Id);
        (Quest, QuestStage) = (null, QuestStage.None);
        Say($"「{map.Title}」의 발견을 마쳤다.");
    }

    /// <summary>지도를 버린다.</summary>
    public void DropMap()
    {
        if (Quest is not { MapSkill: > 0 } map) return;
        (Quest, QuestStage) = (null, QuestStage.None);
        Say($"「{map.Title}」을(를) 버렸다.");
    }

    /// <summary>대본용 — 이 도시 서고의 N 번째 지도(학문과 랭크를 안 가린다)를 얻은 것으로. 음수면 세는 글만.</summary>
    public void MapReadForTest(int index)
    {
        var here = MapQuests.Where(q => q.CityId == City.Id && !Found.Contains(q.DiscoveryId)).GroupBy(q => q.DiscoveryId).Select(g => g.First()).OrderBy(q => q.Rank).ThenBy(q => q.Id).ToList();
        Say($"(시험) 지도 {Data.MapFacts.Count}건 가운데 쓸 수 있는 것 {MapQuests.Select(q => q.Id / 8).Distinct().Count()}건 · 지도가 있는 서고 {MapQuests.Select(q => q.CityId).Distinct().Count()}곳 · {City.Name} 서고 {here.Count}장 — {string.Join(" · ", here.Take(6).Select(q => $"{SkillName(q.MapSkill)} {q.Rank} {QuestDiscoveryName(q)}"))}");
        var libraries = MapQuests.Select(q => q.CityId).Distinct().ToList();
        var unmarked = libraries.Where(id => Dho.Data.TownMap.Load(id) is not { Marks.Count: > 0 } town || !town.Marks.Any(m => m.Place == 16)).ToList();
        Say($"(시험) 지도가 있는 서고 {libraries.Count}곳 가운데 시내 지도에 서고 표식(장소 16)이 없는 곳 {unmarked.Count} — {string.Join(" · ", unmarked.Take(10).Select(CityName))}");
        // 99: 상륙지에서 찾는 첫 지도 · 98: 도시 안에서 찾는 첫 지도
        if ((index == 99 ? here.Find(q => q.LandingId > 0) : index == 98 ? here.Find(q => q.SearchCity > 0) : index >= 0 ? here.ElementAtOrDefault(index) : null) is { } map) TakeMap(map);
    }

    private string QuestDiscoveryName(QuestData quest) => _discoveries.TryGetValue(quest.DiscoveryId, out var found) ? found.Name : "?";
}
