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
        Data.SkillRules.Where(r => r.Effect == "Language" && r.Targets.Contains(city.Culture)).Select(r => Data.Skills.Find(s => s.Id == r.SkillId)).OfType<SkillData>().ToList();

    /// <summary>그 도시의 문화권 이름(클라이언트 표 1) — 「북아프리카」 · 「브리튼 섬」 …</summary>
    public string CultureName(CityData city) => Data.Cultures.Find(c => c.Id == city.Culture)?.Name ?? "";

    /// <summary>그 도시에서 말이 통하는가.</summary>
    public bool Speaks(CityData city)
    {
        if (Data.Settings.ModAllLanguages || (NationId != 0 && city.Nation == NationId)) return true;
        if (Has("BodyTalk")) return true;
        var spoken = LanguagesOf(city);
        return spoken.Count == 0 || spoken.Exists(s => Rank(s.Id) > 0);
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
