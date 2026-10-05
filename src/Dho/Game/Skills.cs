using Dho.Data;

namespace Dho.Game;

/// <summary>익힌 스킬 하나의 랭크와 숙련도.</summary>
internal sealed class SkillState
{
    public int Rank { get; set; } = 1;
    public double Exp { get; set; }
}

/// <summary>
/// 스킬 — 배우고, 쓰면 숙련도가 쌓여 랭크가 오른다.
/// 이름·설명·습득 비용은 클라이언트 표 6 의 것이고, 랭크별 효과와 숙련도 곡선은 <c>skill-rules.json</c> 과 설정의 지은 값이다.
/// </summary>
internal sealed partial class Voyage
{
    public Dictionary<int, SkillState> Skills { get; } = new();

    public int Rank(int skillId) => Skills.TryGetValue(skillId, out var state) ? state.Rank : 0;

    public string SkillName(int skillId) =>
        Data.Skills.Find(s => s.Id == skillId)?.Name ?? Data.SkillRules.Find(r => r.SkillId == skillId)?.Name ?? $"스킬 {skillId}";

    public int ExpToNext(int rank) => Settings.SkillExpBase * rank * rank;

    /// <summary>효과가 같은 스킬들의 (랭크 × 계수) 합.</summary>
    private double Bonus(string effect) =>
        Data.SkillRules.Where(r => r.Effect == effect).Sum(r => Rank(r.SkillId) * r.PerRank);

    private bool Has(string effect) => Data.SkillRules.Any(r => r.Effect == effect && Rank(r.SkillId) > 0);

    /// <summary>측량을 익혀야 좌표가 보인다.</summary>
    public bool CanSurvey => Has("Survey");

    /// <summary>주변 지도가 보이는 반지름(세계 좌표).</summary>
    public double SurveyReach => 100 * (1 + Bonus("Survey")) * (1 + Option("Survey"));

    /// <summary>항구에서 배울 수 있는 스킬 — 게임에서 하는 일이 정해진 것 가운데 아직 안 익힌 것.</summary>
    public IEnumerable<SkillData> Learnable() =>
        Data.SkillRules.Where(r => Rank(r.SkillId) == 0)
            .Select(r => Data.Skills.Find(s => s.Id == r.SkillId)).OfType<SkillData>();

    public void Learn(SkillData skill)
    {
        if (Mode != Mode.Port || Rank(skill.Id) > 0 || Money < skill.Cost) return;
        Money -= skill.Cost;
        Skills[skill.Id] = new SkillState();
        Say($"{skill.Name} 스킬을 익혔다. ({skill.Cost:N0} 두캇)");
        QuickSlotLearned(skill.Id);
    }

    /// <summary>숙련도를 얻고, 차면 랭크가 오른다.</summary>
    private void Train(int skillId, double exp)
    {
        if (!Skills.TryGetValue(skillId, out var state) || state.Rank >= Settings.MaxSkillRank) return;
        state.Exp += exp;
        while (state.Rank < Settings.MaxSkillRank && state.Exp >= ExpToNext(state.Rank))
        {
            state.Exp -= ExpToNext(state.Rank);
            state.Rank++;
            Say($"{SkillName(skillId)} 스킬이 랭크 {state.Rank}(이)가 되었다!");
        }
    }

    private void TrainEffect(string effect, double exp)
    {
        foreach (var rule in Data.SkillRules.Where(r => r.Effect == effect)) Train(rule.SkillId, exp);
    }

    // ── 발견 ─────────────────────────────────────────────────────────────────

    /// <summary>이 갈래의 발견물을 찾는(또는 감정하는) 스킬 규칙.</summary>
    public SkillRuleData? DiscoveryRule(string effect, int kind) =>
        Data.SkillRules.Find(r => r.Effect == effect && r.Targets.Contains(kind));

    /// <summary>의뢰의 발견물을 찾아낼 수 있는가. 못 하면 까닭을 돌려준다.</summary>
    public string? SearchBlocker()
    {
        if (Quest == null || QuestDiscovery is not { } found || Quest.Rank <= 0) return null;
        var lacking = new List<string>();
        foreach (string effect in (string[])["Find", "Appraise"])
            if (DiscoveryRule(effect, found.Kind) is { } rule && Rank(rule.SkillId) < Quest.Rank)
                lacking.Add($"{SkillName(rule.SkillId)} 랭크 {Quest.Rank}");
        return lacking.Count == 0 ? null : string.Join(", ", lacking);
    }

    private void TrainDiscovery(DiscoveryData found)
    {
        foreach (string effect in (string[])["Find", "Appraise"])
            if (DiscoveryRule(effect, found.Kind) is { } rule) Train(rule.SkillId, found.Exp);
    }

    // ── 재해를 스킬로 풀기 ───────────────────────────────────────────────────

    /// <summary>이 재해를 풀 수 있는 익힌 스킬.</summary>
    public SkillRuleData? CureSkill(DisasterData disaster) =>
        Data.SkillRules.Find(r => r.Effect == "Cure" && r.Targets.Contains(disaster.Id) && Rank(r.SkillId) > 0);

    /// <summary>스킬로 재해를 풀어 본다. 성공률 50% + 랭크 × 계수, 쓸 때마다 선원이 조금 지친다.</summary>
    public void CureWithSkill(ActiveDisaster disaster)
    {
        if (CureSkill(disaster.Data) is not { } rule || !Disasters.Contains(disaster)) return;
        Fatigue = Math.Min(100, Fatigue + 4);
        Train(rule.SkillId, 30);
        if (_random.NextDouble() < 0.5 + Rank(rule.SkillId) * rule.PerRank) End(disaster);
        else Say($"{SkillName(rule.SkillId)} — 아직 가라앉지 않았다.");
    }

    /// <summary>주연처럼 피로를 푸는 스킬이 있는가.</summary>
    public SkillRuleData? RestSkill => Data.SkillRules.Find(r => r.Effect == "Rest" && Rank(r.SkillId) > 0);

    /// <summary>식량 5 를 풀어 선원을 쉬게 한다.</summary>
    public void Feast()
    {
        if (RestSkill is not { } rule || Mode != Mode.Sea || Food < 5 || Fatigue <= 0) return;
        Food -= 5;
        Fatigue = Math.Max(0, Fatigue - (10 + Rank(rule.SkillId) * rule.PerRank));
        Train(rule.SkillId, 20);
        Say(Text(3111, "선원들과 파티를 열었습니다."));
    }
}
