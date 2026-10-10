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

    /// <summary>방금 랭크가 오른 스킬 — 화면에 잠깐 빛 기둥과 아이콘이 뜬다.</summary>
    public (int SkillId, int Rank, double At) SkillUpNotice { get; private set; } = (0, 0, -100);
    public void SkillUpForTest(int skillId) => SkillUpNotice = (skillId, Math.Max(1, Rank(skillId)), Clock);
    /// <summary>화면 효과 보기: 레벨 업 알림과 획득 알림을 한 번 띄운다.</summary>
    public void LevelNoticeForTest() => LevelNotice = ("모험 레벨 (보기)", LevelNotice.Count + 1);
    public void GainNoticeForTest()
    {
        if (Data.Goods.Find(g => g.Id is >= 1_601_000 and < 1_602_000) is { } fish) GainNotice = (fish.Id, Text(15402, "%s 를 낚아 올렸습니다.").Replace("%s", fish.Name), 1, Clock);
    }

    /// <summary>랭크 — 내 직업의 전문 스킬이면 +1 이 붙는다(익힌 것만).</summary>
    public int Rank(int skillId) => Math.Max(skillId == _lentSkill || Battle is { Result: null } fought && fought.Lent.Contains(skillId) ? 1 : 0,
        Skills.TryGetValue(skillId, out var state) ? state.Rank + ExpertBoost(skillId) + BoostRank(skillId, state.Rank) + GearRank(skillId) + AideRank(skillId) + (state.Refined ? RefineBoost : 0) : 0);

    // 스킬 대신 쓰는 도구(포획망 · 간이 인양 로프)를 쓰는 동안 그 스킬 — 랭크 1 로 친다(랭크는 자료가 없어 낚시밥처럼 1 로 본 것, 짐작)
    private int _lentSkill;

    /// <summary>
    /// 스킬 대신 쓰는 도구를 쓴다 — 그 스킬이 없어도 한 번 한다(gvdb 의 「消耗品（スキル発動）」: 捕獲網 「使うと生態調査ができる」 · 簡易サルベージロープ).
    /// 썼으면 true(도구가 하나 준다). 쓸 자리가 아니거나 랭크가 모자라 못 찾았으면 false(안 준다 — 원본이 그때 도구를 깎는지는 모른다).
    /// </summary>
    public bool UseSkillTool(int skillId, string tool)
    {
        if (Data.SkillRules.Find(r => r.SkillId == skillId) is not { } rule) return false;
        _lentSkill = skillId;
        try
        {
            if (rule.Effect == "Salvage")
            {
                if (!WreckInReach) { Say("끌어올릴 침몰선이 가까이 없다."); Cues.Enqueue("Error"); return false; }
                double before = Vigour;
                Salvage();
                return Vigour != before;
            }
            if (rule.Effect == "Find")
            {
                if (QuestStage != QuestStage.Accepted || QuestDiscovery is not { } found || !rule.Targets.Contains(found.Kind)) { Say($"{tool}(으)로 찾을 것이 없다."); Cues.Enqueue("Error"); return false; }
                if (Dialog == Dialog.Landing) Search();
                else if (CitySiteHere) SearchInCity();
                else if (SeaSiteInReach()) SearchAtSea();
                else { Say($"{tool}(으)로 찾을 것이 가까이 없다."); Cues.Enqueue("Error"); return false; }
                return QuestStage == QuestStage.Discovered;
            }
            // 관찰 가이드(gvdb 「観察 — 付近に隠されている重要物の場所がわかる」) — 상륙지에서 한 번 둘러본다
            if (rule.Effect == "Observe")
            {
                if (Dialog != Dialog.Ashore || LooksLeft <= 0) { Say($"{tool}은(는) 상륙지를 둘러볼 때 쓴다."); Cues.Enqueue("Error"); return false; }
                LookAround();
                return true;
            }
            return false;
        }
        finally { _lentSkill = 0; }
    }

    /// <summary>우대 스킬(노란 별) — 내 직업이 우대하는 스킬. 조건 없이 배운다.</summary>
    public bool IsFavored(int skillId) =>
        Data.JobFacts.Find(f => f.Name == JobName) is { } job && Data.Skills.Find(s => s.Id == skillId) is { } skill && job.Skills.Contains(skill.Name);

    /// <summary>전문 스킬(빨간 별) — 직업마다 하나. 랭크에 +1 이 붙는다.</summary>
    public bool IsExpert(int skillId) =>
        Data.JobFacts.Find(f => f.Name == JobName) is { Expert: not "" } job && Data.Skills.Find(s => s.Id == skillId)?.Name == job.Expert;

    public int ExpertBoost(int skillId) => IsExpert(skillId) ? 1 : 0;

    /// <summary>
    /// 제 랭크(부스트를 뺀 것)의 상한 — 내 직업이 우대하지 않는 스킬은 10, 우대 스킬은 설정의 최대 랭크(15), 전문 스킬은 그보다 하나 더(16)
    /// (사용자가 준 기준, 2026-10-10: 「비우대 직업: 순수 10랭크가 최대 · 우대 직업: 순수 15 · 전문 직업: 순수 16」).
    /// 전문의 16 은 제 랭크다 — 전문 +1 부스트와는 따로다(사용자, 2026-10-10: 「공예가 전문스킬이라 16랭까지 되어야 하는데」).
    /// 이미 상한을 넘은 랭크는 깎지 않는다 — 숙련도만 더 안 오른다. 우대하는 직업으로 전직하면 다시 오른다.
    /// </summary>
    public const int UnfavoredSkillRank = 10;
    public int SkillCap(int skillId)
    {
        // 스킬마다의 최고 랭크(일본 위키 스킬 일람의 最高ランク — 인식 · 고고학 17, 측량 16, 보급 15, 관찰 1, 언어학 10 …; 표에 없는 스킬은 0)
        int top = Data.SkillFacts.GetValueOrDefault(skillId)?.Max ?? 0;
        if (top > 0 && top < UnfavoredSkillRank) return top;
        if (IsExpert(skillId)) return Math.Max(top, Settings.MaxSkillRank + 1);
        if (IsFavored(skillId)) return top > 0 ? Math.Min(top, Settings.MaxSkillRank) : Settings.MaxSkillRank;
        return Math.Min(UnfavoredSkillRank, Settings.MaxSkillRank);
    }

    /// <summary>상한이 직업 때문에 낮은가 — 우대하지 않는 직업이라 10 에서 멈추는 스킬(그 스킬의 최고 랭크가 본디 10 이하인 것은 아니다).</summary>
    public bool JobCapped(int skillId) => !IsExpert(skillId) && !IsFavored(skillId) && (Data.SkillFacts.GetValueOrDefault(skillId)?.Max ?? 0) is 0 or > UnfavoredSkillRank;

    public string SkillName(int skillId) =>
        Data.Skills.Find(s => s.Id == skillId)?.Name ?? Data.SkillRules.Find(r => r.SkillId == skillId)?.Name ?? Data.AideSkillLabels.GetValueOrDefault(skillId) ?? $"스킬 {skillId}";

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
    /// 조합 마스터가 가르쳐 주는 스킬 — 내 직업의 우대 스킬(조건 면제 — 원본의 「우대 스킬 : 습득조건면제」)과, 그 조합 갈래의 스킬 가운데 배우는 조건(레벨)이 알려진 것.
    /// 원본은 도시마다 조합 마스터가 가르치는 스킬이 다르다 — 그 자료가 없어 갈래로 묶었다(지은 것).
    /// 직업의 우대 스킬 자료가 없으면(직업이 없거나 자료 파일이 없으면) 그 조합 갈래의, 하는 일이 정해진 스킬로 대신한다.
    /// </summary>
    public List<SkillData> SkillsTaught()
    {
        // 그 조합 갈래의 스킬(하는 일이 정해진 것) — 우대 스킬이 아니면 배우는 조건(레벨, gvdb)을 채워야 한다
        var guild = Data.Skills.Where(s => (s.Group == Teacher || (s.Group == 3 && Teacher == 0)) && Data.SkillRules.Exists(r => r.SkillId == s.Id)).ToList();
        if (Data.JobFacts.Find(f => f.Name == JobName) is { } job)
        {
            // 우대 스킬(조건 면제)을 앞에, 그 뒤에 그 조합이 가르치는 나머지 — 전에는 우대 스킬만 가르쳤다(조건 자료가 없어서). 조건이 gvdb 에 적힌 스킬만 더한다
            var favored = job.Skills.Select(name => Data.Skills.Find(s => s.Name == name)).OfType<SkillData>().ToList();
            return favored.Concat(guild.Where(s => !favored.Contains(s) && Data.SkillLearn.ContainsKey(s.Id))).ToList();
        }
        return Data.Skills.Where(s => (s.Group == Teacher || (s.Group == 3 && Teacher == 0)) && Data.SkillRules.Exists(r => r.SkillId == s.Id)).ToList();      // 언어(갈래 3)는 모험가조합이 가르친다(지은 것)
    }

    public bool CanLearn(SkillData skill) => Teacher >= 0 && SkillsTaught().Contains(skill);

    /// <summary>
    /// 배우는 조건(레벨)을 못 채웠으면 그 글 — 채웠거나 조건이 없으면 null. 조건은 gvdb 의 「習得条件：모험/교역/전투/合計」(레벨), 우대 스킬은 조건이 면제된다(원본 화면의 「우대 스킬 : 습득조건면제」).
    /// </summary>
    public string? LearnNeed(SkillData skill)
    {
        if (!Data.SkillLearn.TryGetValue(skill.Id, out var need) || need.Length < 4 || IsFavored(skill.Id)) return null;
        int adventure = LevelOf(AdventureExp).Level, trade = LevelOf(TradeExp).Level, battle = LevelOf(BattleExp).Level;
        var lacks = new List<string>();
        if (adventure < need[0]) lacks.Add($"모험 레벨 {need[0]}");
        if (trade < need[1]) lacks.Add($"교역 레벨 {need[1]}");
        if (battle < need[2]) lacks.Add($"전투 레벨 {need[2]}");
        // 선행 스킬(일본 위키 스킬 일람의 習得条件 「他」 — 인식 ← 탐색 1, 채집 ← 탐색 2, 항해기술 ← 돛 조종 3 · 측량 1 …)
        foreach (var prior in Data.SkillFacts.GetValueOrDefault(skill.Id)?.Needs ?? [])
            if (prior.Length >= 2 && Rank(prior[0]) < prior[1]) lacks.Add($"{SkillName(prior[0])} 랭크 {prior[1]}");
        return lacks.Count == 0 ? null : string.Join(" · ", lacks) + " 필요";
    }

    /// <summary>배우는 조건을 적은 글(창에 보인다) — 「모험 7 · 합계 5」. 조건이 없으면 빈 글.</summary>
    public string LearnNeedText(SkillData skill) =>
        Data.SkillLearn.TryGetValue(skill.Id, out var need) && need.Length >= 4
            ? string.Join(" · ", new[] { need[0] > 0 ? $"모험 {need[0]}" : "", need[1] > 0 ? $"교역 {need[1]}" : "", need[2] > 0 ? $"전투 {need[2]}" : "" }
                .Concat((Data.SkillFacts.GetValueOrDefault(skill.Id)?.Needs ?? []).Where(p => p.Length >= 2).Select(p => $"{SkillName(p[0])} {p[1]}")).Where(t => t != ""))
            : "";

    /// <param name="taught">가르치는 사람을 따지지 않는다(대본 · 개발용).</param>
    /// <summary>대본용: 스킬의 랭크를 바로 정한다.</summary>
    public void SetRankForTest(int skillId, int rank) => Skills[skillId] = new SkillState { Rank = rank };
    /// <summary>대본용 — 그 스킬을 연성한 것으로(랭크는 그대로) · 행동력을 정한다.</summary>
    public void RefinedForTest(int skillId) { if (Skills.TryGetValue(skillId, out var state)) state.Refined = true; }
    public void VigourForTest(double value) => _vigour = value;

    public void Learn(SkillData skill, bool taught = false)
    {
        if (Mode != Mode.Port || Rank(skill.Id) > 0 || Money < skill.Cost) return;
        // 스킬은 그 갈래의 조합 마스터에게 배운다 — 모험가조합 · 상인조합 · 해양조합
        if (!taught && !CanLearn(skill)) return;
        if (!taught && LearnNeed(skill) is { } lacking) { Say($"{skill.Name} — {lacking}."); Cues.Enqueue("Error"); return; }
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
            return state.Rank >= SkillCap(rule.SkillId) ? $"조선 Rank {state.Rank} (최대)" : $"조선 Rank {state.Rank}  {state.Exp:0}/{ExpToNext(state.Rank)}";
        }
    }

    /// <summary>경험치 · 숙련도에 곱하는 값 — 모드에서 고른 1 · 2 · 3배.</summary>
    private int GainFactor => Data.Settings.Gain;

    /// <summary>랭크가 없는 스킬 — 익히면 그대로 쓴다(숙련도가 쌓이지 않고 랭크가 안 오른다). 구제(19) — 사용자, 2026-10-09: 「구제는 랭크가 없어 그냥 쓰는거다」.</summary>
    public static bool Rankless(int skillId) => skillId == 19;

    private void Train(int skillId, double exp)
    {
        if (Rankless(skillId)) return;
        int cap = SkillCap(skillId);
        if (!Skills.TryGetValue(skillId, out var state) || state.Rank >= cap) return;
        exp *= GainFactor * CharmFactor;      // 번개 시리즈를 쓴 동안 숙련도 +100%
        state.Exp += exp;
        // 숙련도가 오르면 기록에 알린다. 항해 중에 조금씩 오르는 것은 모아서 20 마다 한 번
        double gained = _gained[skillId] = _gained.GetValueOrDefault(skillId) + exp;
        bool ranked = false;
        while (state.Rank < cap && state.Exp >= ExpNeed(state))
        {
            state.Exp -= ExpNeed(state);
            state.Rank++;
            ranked = true;
        }
        if (exp >= 5 || gained >= 20 || ranked)
        {
            _gained.Remove(skillId);
            if (!ranked) Cues.Enqueue("Mastery");      // 숙련도가 오를 때의 소리(0:4 — 사용자, 2026-10-07). 랭크가 오르면 그 소리(SkillUp)가 대신 난다
            Say(state.Rank >= cap
                ? $"{SkillName(skillId)} 숙련도 +{gained:0}"
                : $"{SkillName(skillId)} 숙련도 +{gained:0} ({state.Exp:0}/{ExpNeed(state)})");
        }
        if (ranked) { Say($"{SkillName(skillId)} 스킬이 랭크 {state.Rank}(이)가 되었다!"); Cues.Enqueue("SkillUp"); SkillUpNotice = (skillId, state.Rank, Clock); }
        if (ranked && state.Rank >= cap && JobCapped(skillId)) Say($"{SkillName(skillId)} — 우대하지 않는 직업으로는 랭크 {cap} 까지다. 더 올리려면 이 스킬을 우대하는 직업이어야 한다.");
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
        // 랭크가 없는 스킬(구제)은 쓰면 그대로 가라앉는다 — 랭크로 확률을 따질 것이 없다(지은 값: 꼭 된다)
        if (Rankless(rule.SkillId) || _random.NextDouble() < 0.5 + Rank(rule.SkillId) * rule.PerRank) End(disaster);
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
