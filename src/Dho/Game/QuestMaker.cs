using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 조합의 의뢰를 발견물마다 하나씩 지어낸다.
/// 클라이언트에는 의뢰 목록도, 발견물이 어디에 있는지도 없다(분석 6 · 작업 w-353) — 그래서 **어느 발견물을 어디서 찾는가는 모두 지은 것**이다:
/// 「항구-마을」(갈래 15)을 뺀 발견물마다 자리를 아는 상륙지 하나를 붙인다. 이름 · 설명 글에 지방을 가리키는 말(이집트 · 그리스 · 로마 …)이 있으면
/// 그 문화권의 상륙지 가운데서, 없으면 모든 상륙지 가운데서 번호로 고른다(늘 같은 자리). 의뢰는 그 상륙지가 딸린 도시의 조합이 낸다.
/// 필요한 스킬 랭크 = 별 수(스킬 상한까지), 보수 = 5,000 + 경험 × 40, 선금은 그 1할 — 지은 값.
/// 손으로 지은 의뢰(<c>quests.json</c>)가 쓰는 발견물은 건드리지 않는다. 지어낸 의뢰의 번호는 100000 + 발견물 번호.
/// </summary>
internal sealed partial class Voyage
{
    public const int MadeQuestBase = 100_000, RealQuestBase = 200_000;

    // 이름 · 설명에 이 말이 있으면 그 문화권(표 1 의 번호)의 상륙지에서 찾는다 — 짐작으로 고른 말들
    private static readonly (string[] Words, int[] Cultures)[] QuestRegions =
    [
        (["이집트", "나일", "파라오", "피라미드", "카르타고", "페니키아"], [11]),
        (["그리스", "아테네", "스파르타", "크레타", "미케네", "마케도니아"], [8]),
        (["로마", "이탈리아", "에트루리아", "폼페이", "베네치아", "피렌체", "제노바"], [7]),
        (["메소포타미아", "수메르", "바빌론", "아시리아", "페르시아"], [15]),
        (["인도", "힌두", "무굴", "불교"], [16]),
        (["켈트", "브리튼", "잉글랜드", "스코틀랜드", "아일랜드"], [4]),
        (["프랑스", "갈리아", "노르망디"], [5]),
        (["에스파니아", "이베리아", "포르투갈", "무어", "카스티야"], [6]),
        (["바이킹", "북유럽", "스칸디나비아", "발트"], [1]),
        (["독일", "게르만", "한자"], [2]),
        (["네덜란드", "플랑드르"], [3]),
        (["터키", "오스만", "비잔틴", "콘스탄티노플", "아나톨리아"], [9]),
        (["예루살렘", "헤브라이", "유대", "시리아", "레바논"], [10]),
        (["아랍", "이슬람", "아라비아"], [14]),
        (["아프리카"], [12, 13]),
        (["중국", "명나라", "황하", "장강"], [20, 32]),
        (["일본"], [22]), (["조선", "고려", "신라"], [21]),
        (["마야", "아즈텍", "멕시코", "메소아메리카", "올멕"], [26]), (["잉카", "안데스", "페루"], [27]), (["카리브"], [24]),
        (["동남아", "자바", "크메르", "앙코르", "샴", "말레이"], [17, 18]),
        (["오스트레일리아", "마오리", "오세아니아"], [19]),
        (["러시아", "시베리아"], [23]), (["북미", "인디언"], [25, 31]),
    ];

    /// <summary>지어낸 의뢰를 내는 발견물 갈래 — 지금은 지리뿐.</summary>
    private static readonly int[] MadeKinds = [];      // 사용자(2026-10-07): 진짜 의뢰가 들어왔으니 지어낸 지리 의뢰는 뺀다

    // 진짜 의뢰의 필요 언어(일본어 이름) → 스킬 이름
    private static readonly Dictionary<string, string> QuestTongues = new()
    {
        ["英語"] = "영어", ["ドイツ語"] = "독일어", ["オランダ語"] = "네덜란드어", ["ノルド語"] = "노르웨이어", ["スペイン語"] = "스페인어", ["ポルトガル語"] = "포르투갈어",
        ["フランス語"] = "프랑스어", ["イタリア語"] = "이탈리아어", ["ラテン語"] = "라틴어", ["スラブ諸語"] = "슬라브어", ["スラブ語"] = "슬라브어", ["ギリシャ語"] = "그리스어", ["ケルト語"] = "켈트어",
        ["アラビア語"] = "아라비아어", ["ヘブライ語"] = "헤브라이어", ["古代エジプト語"] = "고대 이집트어", ["トルコ語"] = "터키어", ["朝鮮語"] = "조선어", ["インド諸語"] = "인도어",
        ["ペルシャ語"] = "페르시아어", ["中国語"] = "중국어", ["タイ・ビルマ諸語"] = "태국 미얀마어", ["マラユ・タガログ語"] = "말레이 타갈로그어", ["オセアニア諸語"] = "오세아니아어",
        ["モン・クメール諸語"] = "크메르어", ["スワヒリ語"] = "스와힐리어", ["西アフリカ諸語"] = "서아프리카어", ["北米諸語"] = "북미어", ["マヤ諸語"] = "마야어", ["ケチュア語"] = "케추아어",
        ["極北諸語"] = "북극어", ["日本語"] = "일본어", ["ナワトル語"] = "나우아틀어",
    };

    /// <summary>의뢰를 못 받는 까닭 — 필요한 언어를 모르면 막힌다(바디 랭귀지 · 모드 「어디서나 말이 통한다」는 통한다). 받을 수 있으면 null.</summary>
    public string? AcceptBlocker(QuestData quest)
    {
        if (quest.Languages.Count == 0 || Data.Settings.ModAllLanguages || Has("BodyTalk")) return null;
        var lacking = quest.Languages.Where(id => Rank(id) <= 0).Select(SkillName).ToList();
        return lacking.Count == 0 ? null : $"{string.Join(" · ", lacking)}을(를) 알아야 받을 수 있다";
    }

    private List<QuestData>? _madeQuests;

    /// <summary>지어낸 의뢰 전부(처음 쓸 때 한 번 만든다).</summary>
    public List<QuestData> MadeQuests => _madeQuests ??= MakeQuests();

    /// <summary>번호로 의뢰를 찾는다 — 손으로 지은 것과 지어낸 것 모두.</summary>
    public QuestData? QuestById(int id) => id >= MadeQuestBase ? MadeQuests.Find(q => q.Id == id) : Data.Quests.Find(q => q.Id == id);

    private List<QuestData> MakeQuests()
    {
        var made = new List<QuestData>();
        var sites = Data.Landings.Where(l => l.X != 0 || l.Y != 0).OrderBy(l => l.Id).ToList();
        if (sites.Count == 0) return made;
        var ports = Data.Cities.Where(c => c.SeaX != 0 || c.SeaY != 0).ToList();
        var taken = Data.Quests.Select(q => q.DiscoveryId).ToHashSet();
        int most = Data.Settings.MaxSkillRank;
        // 상륙지 → 의뢰를 내는 도시: 딸린 도시에 바다 자리가 있으면 그 도시, 없으면 가장 가까운 항구
        var giver = sites.ToDictionary(l => l.Id, l =>
            ports.Find(c => c.Id == l.City) ?? ports.MinBy(c => Math.Pow(WorldMap.DeltaX(l.X, c.SeaX), 2) + Math.Pow(c.SeaY - l.Y, 2)));
        // 바다에서 찾는 갈래(해양생물 · 지리 · 천문 · 기상 현상): 해역 하나를 붙인다 — 이름이 해역 이름과 같으면 그 해역, 아니면 항구가 있는 해역 가운데 번호로.
        // 의뢰는 그 해역 밖의 가까운 항구 여덟 가운데 하나가 낸다(지은 것)
        var zonePorts = ports.GroupBy(c => Zones.ZoneAt(c.SeaX, c.SeaY)).Where(g => Data.Seas.Exists(s => s.Id == g.Key)).ToDictionary(g => g.Key, g => g.ToList());
        var zoneIds = zonePorts.Keys.OrderBy(z => z).ToList();
        var facts = Data.DiscoveryFacts.ToDictionary(f => f.Id);
        // 설명 글에 해역 · 도시 · 문화권의 이름이 그대로 나오면 그 바다다(「브리튼 섬 북부, 서쪽에 있는 섬」) — 긴 이름부터 맞춘다
        var seaNames = Data.Seas.Where(s => zonePorts.ContainsKey(s.Id) && s.Name.Length >= 3).OrderByDescending(s => s.Name.Length).ToList();
        var cityNames = ports.Where(c => c.Name.Length >= 2).OrderByDescending(c => c.Name.Length).ToList();
        var cultureNames = Data.Cultures.OrderByDescending(c => c.Name.Length).ToList();
        int? NamedZone(string text)
        {
            if (seaNames.Find(s => text.Contains(s.Name)) is { } sea) return sea.Id;
            // 「희망봉 앞바다」 · 「카나리아 앞바다」처럼 뒤에 붙은 말을 뗀 이름으로도 맞춘다
            if (seaNames.Find(s => s.Name.Replace(" 앞바다", "").Replace(" 해협", "") is { Length: >= 3 } stem && stem != s.Name && text.Contains(stem)) is { } offshore) return offshore.Id;
            if (cityNames.Find(c => text.Contains(c.Name)) is { } town && zonePorts.ContainsKey(Zones.ZoneAt(town.SeaX, town.SeaY))) return Zones.ZoneAt(town.SeaX, town.SeaY);
            foreach (var culture in cultureNames)
                foreach (string word in culture.Name.Split('/'))
                    if (word.Length >= 2 && text.Contains(word) && zoneIds.Where(z => zonePorts[z].Exists(c => c.Culture == culture.Id)).ToList() is { Count: > 0 } zones)
                        return zones[(int)((uint)(text.Length * 40503u) % (uint)zones.Count)];
            return null;
        }
        // 진짜 의뢰(大航海時代DB) — 내는 도시들 · 필요 스킬 랭크 · 보수 · 선금 · 바다의 좌표가 그쪽 값이다. 제목과 의뢰 글은 일본어라 우리말 틀로 지었다.
        // 좌표가 있는 것만 넣는다(차례 가운데의 사람 찾아가기는 건너뛰고 그 자리로 바로 간다). 도시마다 한 건씩 — 번호 200000 + 차례 × 32 + 도시 차례
        for (int n = 0; n < Data.QuestFacts.Count; n++)
        {
            var real = Data.QuestFacts[n];
            if (taken.Contains(real.DiscoveryId) || Data.Discoveries.Find(d => d.Id == real.DiscoveryId) is not { } target) continue;
            string kindName = Data.DiscoveryKinds.Find(k => k.Id == target.Kind)?.Name ?? "발견물";
            // 자리: 바다의 좌표 / 자리를 아는 상륙지 / 도시 안. 그 밖(못 읽은 것 · 자리를 모르는 상륙지)은 건너뛴다
            var site = real.Place == 2 ? sites.Find(l => l.Id == real.LandingId) : null;
            var town = real.Place == 3 ? ports.Find(c => c.Id == real.TownId) : null;
            if (!(real.Place == 1 && real.X > 0 && real.Y > 0) && site == null && town == null) continue;
            string where = site?.Name ?? town?.Name ?? (_seas.TryGetValue(Zones.ZoneAt(real.X, real.Y), out var seaAt) ? seaAt : "먼 바다");
            // 학문(감정) 스킬의 랭크와 찾는 스킬의 랭크 — 언어 · 자물쇠 따기는 안 본다
            int study = real.Skills.Where(s => s.Name is not ("視認" or "探索" or "生態調査" or "開錠") && !s.Name.EndsWith('語')).Select(s => s.Rank).DefaultIfEmpty(Math.Max(1, real.Difficulty)).Max();
            int look = real.Skills.Where(s => s.Name is "視認" or "探索" or "生態調査").Select(s => s.Rank).DefaultIfEmpty(0).Max();
            var tongues = real.Skills.Select(s => QuestTongues.GetValueOrDefault(s.Name)).OfType<string>().Select(name => Data.Skills.Find(s => s.Name == name)?.Id ?? 0).Where(id => id > 0).Distinct().ToList();
            int pay = real.Reward > 0 ? real.Reward : (5000 + target.Exp * 40) / 100 * 100;      // 보수는 도시별 쪽에서 본 것만 그쪽 값, 나머지는 지은 식
            for (int k = 0; k < Math.Min(real.Cities.Count, 32); k++)
            {
                if (ports.Find(c => c.Id == real.Cities[k]) is not { } giverCity) continue;
                made.Add(new QuestData
                {
                    Id = RealQuestBase + n * 32 + k, Title = $"{where}의 {kindName}", Client = "모험가 조합", CityId = giverCity.Id, DiscoveryId = real.DiscoveryId,
                    SeaX = site == null && town == null ? real.X : 0, SeaY = site == null && town == null ? real.Y : 0,
                    LandingId = site?.Id ?? 0, SearchCity = town?.Id ?? 0, Languages = tongues, Rank = Math.Clamp(study, 1, most), FindRank = look,
                    Request = site != null ? $"{where}에 눈여겨볼 {kindName}이(가) 있다는 이야기가 들어와 있네. 가서 확인하고 돌아와 주게."
                            : town != null ? $"{where}에 있는 {kindName}에 관한 의뢰가 들어와 있네. 그 도시에 가서 찾아봐 주게."
                            : $"{where} ({real.X}, {real.Y}) 부근을 조사해 달라는 의뢰가 들어와 있네. 가서 확인하고 돌아와 주게.",
                    Hint = site != null ? $"{where}에 상륙해 주변을 탐색한다." : town != null ? $"{where}에 입항해 「의뢰 탐색」을 한다." : $"{where} ({real.X}, {real.Y}) 부근에서 둘레를 살핀다(F).",
                    LandingText = site != null ? $"{where}에 올랐다.\n소문으로 듣던 자리를 찾아 둘레를 살핀다." : "",
                    Advance = real.Advance, Reward = pay,
                });
            }
            taken.Add(real.DiscoveryId);
        }
        foreach (var found in Data.Discoveries)
        {
            if (found.Kind == 15 || found.Stars <= 0 || taken.Contains(found.Id)) continue;
            // 사용자(2026-10-07): 「지리만 먼저 다른건 아직 하지마」 — 지어낸 의뢰는 지리(갈래 17)만 낸다. 다른 갈래는 다시 말할 때 MadeKinds 에 더한다
            if (!MadeKinds.Contains(found.Kind)) continue;
            if (found.Kind is 14 or 17 or 18 or 19 && zoneIds.Count > 1)
            {
                // 글에 지방을 가리키는 말이 있으면 그 문화권의 항구가 있는 해역 가운데서 고른다
                string seaText = found.Name + found.Description;
                var seaCultures = QuestRegions.Where(r => r.Words.Any(seaText.Contains)).SelectMany(r => r.Cultures).ToHashSet();
                var fitting = seaCultures.Count > 0 ? zoneIds.Where(z => zonePorts[z].Exists(c => seaCultures.Contains(c.Culture))).ToList() : [];
                if (fitting.Count == 0) fitting = zoneIds;
                int zone = Data.Seas.Find(s => s.Name == found.Name && zonePorts.ContainsKey(s.Id))?.Id ?? NamedZone(seaText) ?? fitting[(int)((uint)(found.Id * 2654435761u) % (uint)fitting.Count)];
                var inside = zonePorts[zone][0];
                // 그 해역 밖의 가까운 항구 여덟 가운데 번호로 — 가장 가까운 하나로만 하면 몇 항구에 몰린다
                var around = ports.Where(c => Zones.ZoneAt(c.SeaX, c.SeaY) != zone).OrderBy(c => Math.Pow(WorldMap.DeltaX(inside.SeaX, c.SeaX), 2) + Math.Pow(c.SeaY - inside.SeaY, 2)).Take(8).ToList();
                var from = around.Count == 0 ? null : around[(int)((uint)(found.Id * 40503u) % (uint)around.Count)];
                if (from == null) continue;
                string seaName = Data.Seas.Find(s => s.Id == zone)?.Name ?? "먼 바다", seaKind = Data.DiscoveryKinds.Find(k => k.Id == found.Kind)?.Name ?? "발견물";
                int seaReward = 5000 + found.Exp * 40;
                made.Add(new QuestData
                {
                    Id = MadeQuestBase + found.Id, Title = $"{seaName}의 {seaKind}", Client = "모험가 조합", CityId = from.Id, DiscoveryId = found.Id, SeaZone = zone,
                    Request = $"{seaName}을(를) 지나던 뱃사람들이 눈여겨볼 {seaKind}에 관한 이야기를 하고 있네. 그 바다에 나가 살펴 주게.",
                    Hint = $"{seaName}에 나가 둘레를 살핀다(F).",
                    Advance = seaReward / 10 / 100 * 100, Reward = seaReward / 100 * 100,
                    Rank = Math.Clamp(facts.TryGetValue(found.Id, out var fact) ? fact.Difficulty : found.Stars, 1, most),      // 위키의 難度가 있으면 그것이 필요 랭크다
                });
                continue;
            }
            string text = found.Name + found.Description;
            var cultures = QuestRegions.Where(r => r.Words.Any(text.Contains)).SelectMany(r => r.Cultures).ToHashSet();
            var near = cultures.Count > 0 ? sites.Where(l => cultures.Contains(l.Region)).ToList() : [];
            if (near.Count == 0) near = sites;
            var site = near[(int)((uint)(found.Id * 2654435761u) % (uint)near.Count)];
            if (giver[site.Id] is not { } city) continue;
            string kind = Data.DiscoveryKinds.Find(k => k.Id == found.Kind)?.Name ?? "발견물";
            int rank = Math.Clamp(found.Stars, 1, most), reward = 5000 + found.Exp * 40;
            made.Add(new QuestData
            {
                Id = MadeQuestBase + found.Id, Title = $"{site.Name}의 {kind}", Client = "모험가 조합", CityId = city.Id, DiscoveryId = found.Id, LandingId = site.Id,
                Request = QuestAsk(found.Kind, site.Name, kind),
                Hint = $"{site.Name}에 상륙해 주변을 탐색한다.",
                LandingText = $"{site.Name}에 올랐다.\n소문으로 듣던 자리를 찾아 둘레를 살핀다.",
                Advance = reward / 10 / 100 * 100, Reward = reward / 100 * 100, Rank = rank,
            });
        }
        return made;
    }

    private static string QuestAsk(int kind, string site, string kindName) => kind switch
    {
        1 or 2 => $"{site} 쪽에 옛사람들이 남긴 {kindName}이(가) 있다는 이야기가 들어왔네. 학자들이 확인해 달라고 하니 가서 보고 와 주게.",
        3 or 4 or 5 or 6 => $"{site} 근처에 귀한 {kindName}이(가) 묻혀 있다는 소문이 도네. 찾아내면 조합에서 값을 쳐 주겠네.",
        7 or 8 or 9 or 10 or 11 or 12 or 13 or 14 => $"{site} 둘레에서 본 적 없는 {kindName}을(를) 보았다는 뱃사람이 있네. 무엇인지 살펴 주게.",
        _ => $"{site} 쪽에서 눈여겨볼 {kindName}에 관한 이야기가 들어왔네. 가서 확인해 주게.",
    };

    /// <summary>이 도시가 낼 지어낸 의뢰 — 아직 못 찾은 것 가운데 랭크가 낮은 것부터 여섯.</summary>
    private IEnumerable<QuestData> MadeQuestsHere() =>
        MadeQuests.Where(q => q.CityId == City.Id && !_done.Contains(q.Id) && !Found.Contains(q.DiscoveryId) && q != Quest).OrderBy(q => q.Id >= RealQuestBase ? 0 : 1).ThenBy(q => q.Rank).ThenBy(q => q.Id).Take(8);      // 진짜 의뢰가 먼저
}
