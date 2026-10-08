using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 언어 — 그 도시의 말을 알아야 교역소에서 흥정을 한다.
/// 도시의 문화권(도시 표의 Culture — 클라이언트의 것)과 언어 스킬을 잇는 표는 클라이언트에서 못 찾았다(작업 w-296).
/// 스킬 설명 글의 지방 이름(「브리튼 섬 주변」 · 「이베리아 주변」 …)을 보고 문화권에 맞춘 것이다 — 짐작(<c>skill-rules.json</c> 의 Language, Targets 가 문화권).
/// 제 나라의 도시에서는 늘 말이 통한다. 「바디 랭귀지」(설명: 「언어를 몰라도 도시의 사람들과 대화를 나눌 수 있다」)가 있으면 어디서나 통한다.
/// 말이 안 통할 때 무슨 일이 생기는가는 지은 것이다: 흥정을 못 한다(원본은 사람에게 말을 못 건다 — 그대로 하면 혼자 하는 게임이 막힌다).
/// 언어는 모험가조합에서 배운다(원본은 도시마다 가르치는 사람이 따로 있다 — 지은 것).
/// </summary>
internal sealed partial class Voyage
{
    /// <summary>그 도시에서 쓰는 언어 스킬들.</summary>
    public List<SkillData> LanguagesOf(CityData city) =>
        // gvdb 도시 쪽의 「必要言語」가 있으면 그것(원본의 값) — 없는 도시만 문화권으로 짐작한다
        Data.TownFacts.TryGetValue(city.Id, out var fact) && fact.Language.Count > 0 && fact.Language.Select(id => Data.Skills.Find(s => s.Id == id)).OfType<SkillData>().ToList() is { Count: > 0 } known ? known :
        Data.SkillRules.Where(r => r.Effect == "Language" && r.Targets.Contains(city.Culture)).Select(r => Data.Skills.Find(s => s.Id == r.SkillId)).OfType<SkillData>().ToList();

    /// <summary>대본용 — gvdb 의 필요 언어와 문화권으로 짐작한 언어가 다른 도시를 센다.</summary>
    public void LanguagesForTest()
    {
        var differ = new List<string>();
        foreach (var city in Data.Cities.Where(c => Data.TownFacts.ContainsKey(c.Id)))
        {
            var guess = Data.SkillRules.Where(r => r.Effect == "Language" && r.Targets.Contains(city.Culture)).Select(r => SkillName(r.SkillId)).OrderBy(n => n).ToList();
            var real = LanguagesOf(city).Select(k => k.Name).OrderBy(n => n).ToList();
            if (!guess.SequenceEqual(real)) differ.Add($"{city.Name}: {string.Join("/", guess)} → {string.Join("/", real)}");
        }
        Say($"(시험) 언어가 달라진 도시 {differ.Count}/{Data.TownFacts.Count} — {string.Join(" · ", differ.Take(14))}");
    }

    /// <summary>그 도시의 문화권 이름(클라이언트 표 1) — 「북아프리카」 · 「브리튼 섬」 …</summary>
    public string CultureName(CityData city) => Data.Cultures.Find(c => c.Id == city.Culture)?.Name ?? "";

    /// <summary>
    /// 번역메모(gvdb 「消耗品（スキル発動）」 — 「○○諸語翻訳」, 翻訳言語 줄의 언어들) — 메모 아이템 → 그 메모가 옮겨 주는 언어 스킬들.
    /// 게임의 언어 스킬에 없는 것(라틴어 · 켈트어 · 헤브라이어 · 고대 이집트어)은 뺐다. 남방 메모는 글이 「マラユ・タガログ語などの」라 말레이 타갈로그어만.
    /// </summary>
    public static readonly Dictionary<int, int[]> TranslationMemos = new()
    {
        [1500279] = [150, 151, 152, 153], [1500280] = [154, 155, 156, 157], [1500281] = [160, 159], [1500283] = [174, 175], [1500284] = [162],
        [1500285] = [165], [1500286] = [167, 168], [1500287] = [177, 181, 176], [1500288] = [171], [1500289] = [169, 170], [1500290] = [166, 180], [1500993] = [179],
    };

    // 번역메모로 지금 통하는 언어 — 이 항구에 머무는 동안(원본은 시간제 — 여기서는 떠날 때까지로 줄였다)
    private readonly HashSet<int> _translated = [];

    /// <summary>번역메모를 쓴다 — 지금 도시의 말이 그 메모의 언어일 때만. 썼으면 true.</summary>
    public bool UseTranslationMemo(int item)
    {
        if (!TranslationMemos.TryGetValue(item, out var tongues)) return false;
        if (Mode != Mode.Port) { Say($"{ItemName(item)} — 도시에서 쓴다."); Cues.Enqueue("Error"); return false; }
        if (Speaks(City)) { Say($"{ItemName(item)} — 이 도시에서는 이미 말이 통한다."); Cues.Enqueue("Error"); return false; }
        if (!LanguagesOf(City).Exists(s => tongues.Contains(s.Id))) { Say($"{ItemName(item)} — 이 도시의 말({string.Join(" · ", LanguagesOf(City).Select(s => s.Name))})은 이 메모로 옮길 수 없다."); Cues.Enqueue("Error"); return false; }
        foreach (int tongue in tongues) _translated.Add(tongue);
        Say($"{ItemName(item)}을(를) 펼쳤다. {City.Name}에 머무는 동안 말이 통한다.");
        return true;
    }

    /// <summary>그 도시에서 말이 통하는가.</summary>
    public bool Speaks(CityData city)
    {
        if (Data.Settings.ModAllLanguages || (NationId != 0 && city.Nation == NationId)) return true;
        if (Has("BodyTalk")) return true;
        var spoken = LanguagesOf(city);
        return spoken.Count == 0 || spoken.Exists(s => Rank(s.Id) > 0 || _translated.Contains(s.Id));
    }

    /// <summary>지금 도시의 말 — 도시 이름 곁에 보인다. 통하면 빈 글.</summary>
    public string LanguageNote =>
        Mode != Mode.Port || Speaks(City) ? "" : $"말이 안 통한다({string.Join(" · ", LanguagesOf(City).Select(s => s.Name))}) — 흥정 불가";

    // 항구에 들어올 때 — 통하면 쓰는 말이 자라고, 안 통하면 알린다
    private void HearLanguage()
    {
        if (Data.Settings.ModAllLanguages || (NationId != 0 && City.Nation == NationId)) return;
        var spoken = LanguagesOf(City);
        if (spoken.Count == 0) return;
        foreach (var skill in spoken.Where(s => Rank(s.Id) > 0)) Train(skill.Id, 15);
        if (spoken.Exists(s => Rank(s.Id) > 0)) return;
        // 바디 랭귀지는 따로 켜지 않아도 저절로 듣는다(사용자, 2026-10-07) — 어디서나 대화가 된다
        if (Has("BodyTalk")) { TrainEffect("BodyTalk", 15); Say($"바디 랭귀지로 {City.Name} 사람들과 말이 통한다."); return; }
        Say($"{City.Name}({CultureName(City)})의 말({string.Join(" · ", spoken.Select(s => s.Name))})을 모른다 — 교역소에서 흥정을 못 한다.");
    }
}
