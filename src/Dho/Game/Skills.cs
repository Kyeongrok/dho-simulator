using Dho.Data;

namespace Dho.Game;

/// <summary>익힌 스킬 하나의 랭크와 숙련도.</summary>
internal sealed class SkillState
{
    public int Rank { get; set; } = 1;
    public double Exp { get; set; }
    /// <summary>연성한 스킬 — 랭크 +2, 다음 랭크까지의 숙련도가 8할.</summary>
    public bool Refined { get; set; }
}

/// <summary>
/// 스킬 — 배우고, 쓰면 숙련도가 쌓여 랭크가 오른다.
/// 이름·설명·습득 비용은 클라이언트 표 6 의 것이고, 랭크별 효과와 숙련도 곡선은 <c>skill-rules.json</c> 과 설정의 지은 값이다.
/// </summary>
internal sealed partial class Voyage
{
    public Dictionary<int, SkillState> Skills { get; } = new();

    /// <summary>랭크 — 내 직업의 전문 스킬이면 +1 이 붙는다(익힌 것만).</summary>
    public int Rank(int skillId) => Skills.TryGetValue(skillId, out var state) ? state.Rank + ExpertBoost(skillId) + BoostRank(skillId, state.Rank) + GearRank(skillId) + (state.Refined ? RefineBoost : 0) : 0;

    /// <summary>우대 스킬(노란 별) — 내 직업이 우대하는 스킬. 조건 없이 배운다.</summary>
    public bool IsFavored(int skillId) =>
        Data.JobFacts.Find(f => f.Name == JobName) is { } job && Data.Skills.Find(s => s.Id == skillId) is { } skill && job.Skills.Contains(skill.Name);

    /// <summary>전문 스킬(빨간 별) — 직업마다 하나. 랭크에 +1 이 붙는다.</summary>
    public bool IsExpert(int skillId) =>
        Data.JobFacts.Find(f => f.Name == JobName) is { Expert: not "" } job && Data.Skills.Find(s => s.Id == skillId)?.Name == job.Expert;

    public int ExpertBoost(int skillId) => IsExpert(skillId) ? 1 : 0;

    public string SkillName(int skillId) =>
        Data.Skills.Find(s => s.Id == skillId)?.Name ?? Data.SkillRules.Find(r => r.SkillId == skillId)?.Name ?? $"스킬 {skillId}";

    public int ExpToNext(int rank) => Settings.SkillExpBase * rank * rank;

    /// <summary>효과가 같은 스킬들의 (랭크 × 계수) 합.</summary>
    private double Bonus(string effect) =>
        Data.SkillRules.Where(r => r.Effect == effect && (!Sustained.Contains(effect) || SkillOn(r.SkillId))).Sum(r => Rank(r.SkillId) * r.PerRank);

    private bool Has(string effect) => Data.SkillRules.Any(r => r.Effect == effect && Rank(r.SkillId) > 0);

    /// <summary>측량을 켜 둔 동안 나침반 위에 주변 지도와 좌표가 보인다.</summary>
    public bool CanSurvey => Data.SkillRules.Any(r => r.Effect == "Survey" && SkillOn(r.SkillId));

    /// <summary>주변 지도가 보이는 반지름(세계 좌표).</summary>
    public double SurveyReach => 100 * (1 + Bonus("Survey")) * (1 + Option("Survey"));

    /// <summary>항구에서 배울 수 있는 스킬 — 게임에서 하는 일이 정해진 것 가운데 아직 안 익힌 것.</summary>
    public IEnumerable<SkillData> Learnable() =>
        Data.SkillRules.Where(r => Rank(r.SkillId) == 0)
            .Select(r => Data.Skills.Find(s => s.Id == r.SkillId)).OfType<SkillData>();

    /// <summary>
    /// 조합 마스터가 가르쳐 주는 스킬 — **내 직업의 우대 스킬만** 배운다(원본의 「우대 스킬 : 습득조건면제」).
    /// 직업의 우대 스킬 자료가 없으면(직업이 없거나 자료 파일이 없으면) 그 조합 갈래의, 하는 일이 정해진 스킬로 대신한다.
    /// </summary>
    public List<SkillData> SkillsTaught()
    {
        if (Data.JobFacts.Find(f => f.Name == JobName) is { } job)
            return job.Skills.Select(name => Data.Skills.Find(s => s.Name == name)).OfType<SkillData>().ToList();
        return Data.Skills.Where(s => (s.Group == Teacher || (s.Group == 3 && Teacher == 0)) && Data.SkillRules.Exists(r => r.SkillId == s.Id)).ToList();      // 언어(갈래 3)는 모험가조합이 가르친다(지은 것)
    }

    public bool CanLearn(SkillData skill) => Teacher >= 0 && SkillsTaught().Contains(skill);

    /// <param name="taught">가르치는 사람을 따지지 않는다(대본 · 개발용).</param>
    /// <summary>대본용: 스킬의 랭크를 바로 정한다.</summary>
    public void SetRankForTest(int skillId, int rank) => Skills[skillId] = new SkillState { Rank = rank };

    public void Learn(SkillData skill, bool taught = false)
    {
        if (Mode != Mode.Port || Rank(skill.Id) > 0 || Money < skill.Cost) return;
        // 스킬은 그 갈래의 조합 마스터에게 배운다 — 모험가조합 · 상인조합 · 해양조합
        if (!taught && !CanLearn(skill)) return;
        Money -= skill.Cost;
        Skills[skill.Id] = new SkillState();
        Say($"{skill.Name} 스킬을 익혔다. ({skill.Cost:N0} 두캇)");
        QuickSlotLearned(skill.Id);
    }

    /// <summary>숙련도를 얻고, 차면 랭크가 오른다.</summary>
    private readonly Dictionary<int, double> _gained = new();

    /// <summary>조선소에 띄우는 한 줄 — 조선 스킬의 랭크와 숙련도.</summary>
    public string ShipbuildingLine
    {
        get
        {
            if (Data.SkillRules.Find(r => r.Effect == "Shipbuilding") is not { } rule) return "";
            if (!Skills.TryGetValue(rule.SkillId, out var state)) return "조선 스킬 없음";
            return state.Rank >= Settings.MaxSkillRank ? $"조선 Rank {state.Rank} (최대)" : $"조선 Rank {state.Rank}  {state.Exp:0}/{ExpToNext(state.Rank)}";
        }
    }

    /// <summary>경험치 · 숙련도에 곱하는 값 — 모드 「경험치 · 숙련도 3배」를 켜면 3.</summary>
    private int GainFactor => Data.Settings.ModTripleGain ? 3 : 1;

    private void Train(int skillId, double exp)
    {
        if (!Skills.TryGetValue(skillId, out var state) || state.Rank >= Settings.MaxSkillRank) return;
        exp *= GainFactor;
        state.Exp += exp;
        // 숙련도가 오르면 기록에 알린다. 항해 중에 조금씩 오르는 것은 모아서 20 마다 한 번
        double gained = _gained[skillId] = _gained.GetValueOrDefault(skillId) + exp;
        bool ranked = false;
        while (state.Rank < Settings.MaxSkillRank && state.Exp >= ExpNeed(state))
        {
            state.Exp -= ExpNeed(state);
            state.Rank++;
            ranked = true;
        }
        if (exp >= 5 || gained >= 20 || ranked)
        {
            _gained.Remove(skillId);
            Say(state.Rank >= Settings.MaxSkillRank
                ? $"{SkillName(skillId)} 숙련도 +{gained:0}"
                : $"{SkillName(skillId)} 숙련도 +{gained:0} ({state.Exp:0}/{ExpNeed(state)})");
        }
        if (ranked) { Say($"{SkillName(skillId)} 스킬이 랭크 {state.Rank}(이)가 되었다!"); Cues.Enqueue("SkillUp"); }
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
        {
            // 일본 위키(Discovery 쪽)의 규칙: 의뢰에서 학문(감정) 스킬은 필요 랭크가 꼭 차야 하고, 인식 · 탐색 · 생태 조사는 두 랭크 모자라도 된다
            int need = effect == "Find" ? Math.Max(1, (Quest.FindRank > 0 ? Quest.FindRank : Quest.Rank) - 2) : Quest.Rank;
            if (DiscoveryRule(effect, found.Kind) is { } rule && Rank(rule.SkillId) < need)
                lacking.Add($"{SkillName(rule.SkillId)} 랭크 {need}");
        }
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
        Cues.Enqueue("Drunk");
        Fatigue = Math.Max(0, Fatigue - (10 + Rank(rule.SkillId) * rule.PerRank));
        Train(rule.SkillId, 20);
        Say(Text(3111, "선원들과 파티를 열었습니다."));
    }
}
