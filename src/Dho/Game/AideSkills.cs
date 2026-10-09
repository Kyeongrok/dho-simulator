using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 부관의 스킬 — 원본에서 부관은 담당 자리마다 두 가지를 갖는다(사용자가 준 글 「부관을 키워보자」 인벤 498/20820 · 「벨벳온라인」 네이버 hankhan23/220599567414, 2026-10-08):
/// 보조스킬(그 담당을 맡은 동안 선장의 같은 스킬 +1)과 부관스킬(부관만의 스킬 — 클라이언트 스킬 표의 1000번대: 1004 「방화 — 화재를 미리 막는다」 · 1302 「봉제 보조 — 봉제로 생산할 때 소비행동력이 적어진다」 …).
/// 담당 차례는 표 36 과 같다: 0 항해장 · 1 감시 · 2 회계사 · 3 창고당번 · 4 부함장 · 5 선의.
/// 어느 부관이 무엇을 갖는지는 글에 적힌 사람만 안다(한스 · 후란시느) — 나머지 부관은 아직 스킬이 없다.
/// </summary>
internal sealed record AideKit(int Id, string Name, string[] Cities, (int Duty, string Skill)[] Assist, (int Duty, int Skill)[] Skills);

internal sealed partial class Voyage
{
    public const int FireGuardSkill = 1004, SewAidSkill = 1302, SewSkill = 73;

    // 글에 적힌 부관 — 한스(암스테르담 · 튀니스) · 후란시느(마르세이유): 「봉제 보조, 봉제+1, 섬유거래+1, 직물거래+1, 방화」, 한스는 회계, 후란시느는 사교와 적재화물 강탈.
    // 스킬이 어느 담당의 것인지는 인벤 글의 담당별 표를 따랐다. 번호(9001 ~)는 지은 것(클라이언트의 부관 번호를 모른다)
    public static readonly AideKit[] AideKits =
    [
        new(9001, "한스", ["암스테르담", "튀니스"], [(2, "봉제"), (2, "직물 거래"), (2, "회계"), (3, "섬유 거래")], [(2, SewAidSkill), (3, FireGuardSkill)]),
        new(9002, "후란시느", ["마르세이유"], [(2, "봉제"), (2, "직물 거래"), (3, "섬유 거래"), (5, "사교")], [(2, SewAidSkill), (3, FireGuardSkill), (4, 1503)]),
    ];

    // 재해를 미리 막는 부관스킬 → 막는 재해 번호(스킬 설명 글대로)
    private static readonly Dictionary<int, int[]> AidePrevention = new()
    {
        [1002] = [5], [1003] = [14], [1004] = [1], [1006] = [4], [1007] = [18], [1008] = [17], [1009] = [3, 15], [1010] = [10], [1011] = [16], [1012] = [9], [1013] = [19],
    };

    private static readonly Dictionary<int, string> AideSkillNames = new()
    {
        [1004] = "방화", [1302] = "봉제 보조", [1503] = "적재화물 강탈",
    };

    /// <summary>dhoguide 의 부관 가운데 클라이언트의 부관 표에 이름이 없는 사람의 번호 = 이 값 + 그 사이트의 번호(지은 번호).</summary>
    public const int FactAideBase = 20000;

    private readonly Dictionary<int, AideKit?> _factKits = [];
    private readonly Dictionary<string, int> _aideSkillIds = AideSkillNames.ToDictionary(n => n.Value, n => n.Key);
    private readonly Dictionary<int, string> _aideSkillExtra = [];

    /// <summary>부관스킬의 이름 — 번호를 아는 셋 말고는 dhoguide 의 이름에 지은 번호(5000 ~)를 붙여 둔 것.</summary>
    public string AideSkillName(int skill) => AideSkillNames.GetValueOrDefault(skill) ?? Data.AideSkillLabels.GetValueOrDefault(skill) ?? _aideSkillExtra.GetValueOrDefault(skill) ?? $"스킬 {skill}";

    /// <summary>부관스킬 · 부관 선장 스킬의 이름 → 클라이언트 스킬 표의 번호(1000 이상에서 같은 이름의 가장 작은 번호, 띄어쓰기는 안 본다). 없으면 0.</summary>
    public int AideSkillIdOf(string name) => Data.AideSkillLabels.Where(l => l.Key >= 1000 && l.Value.Replace(" ", "") == name.Replace(" ", "")).Select(l => l.Key).DefaultIfEmpty(0).Min();

    /// <summary>그 사람의 dhoguide 자료 — 지은 번호면 그 번호로, 클라이언트의 부관이면 이름으로 찾는다.</summary>
    public AideFact? AideFactOf(NamedData who) => who.Id >= FactAideBase ? Data.AideFacts.Find(a => a.No == who.Id - FactAideBase) : Data.AideFacts.Find(a => a.Name == who.Name);

    /// <summary>dhoguide 의 부관을 게임의 사람으로 — 이름이 클라이언트의 부관 표에 있으면 그 번호(초상화가 그 번호에 있다), 없으면 지은 번호.</summary>
    public NamedData AideOfFact(AideFact fact) => Data.Aides.Find(a => a.Name == fact.Name) ?? new NamedData { Id = FactAideBase + fact.No, Name = fact.Name };

    /// <summary>
    /// 부관의 스킬 묶음 — 글에 적힌 두 사람은 손으로 적은 것, 그 밖은 dhoguide 의 스킬 표에서 짓는다:
    /// 「특성」 칸의 담당(「감시 40」 → 감시)을 맡은 동안 듣는다고 보고, 모험 · 교역 · 전투 갈래는 보조스킬(선장의 같은 스킬 +1), 부관 갈래는 부관스킬.
    /// 표의 레벨 · 특성 값 조건은 아직 안 본다(여기 부관은 레벨이 하나다) — 지은 단순화. 담당이 안 적힌 스킬(언어 · 부관선장)은 넣지 않는다.
    /// </summary>
    public AideKit? KitOf(NamedData who)
    {
        if (Array.Find(AideKits, k => k.Id == who.Id) is { } written) return written;
        if (_factKits.TryGetValue(who.Id, out var made)) return made;
        AideKit? kit = null;
        if (AideFactOf(who) is { Skills.Count: > 0 } fact)
        {
            int DutyOf(string trait) => trait.LastIndexOf(' ') is > 0 and var cut && Data.Duties.Find(d => d.Name == trait[..cut]) is { } duty ? duty.Id : -1;
            var placed = fact.Skills.Select(s => (Skill: s, Duty: DutyOf(s.Trait))).Where(s => s.Duty >= 0).ToList();
            int IdOf(string name)
            {
                if (_aideSkillIds.TryGetValue(name, out int id)) return id;
                // 클라이언트 스킬 표의 부관스킬(1000번대)에 같은 이름이 있으면 그 번호(띄어쓰기는 안 본다), 없으면 지은 번호
                var same = Data.AideSkillLabels.Where(l => l.Key >= 1000 && l.Value.Replace(" ", "") == name.Replace(" ", "")).Select(l => l.Key).DefaultIfEmpty(0).Min();
                id = same > 0 ? same : 5000 + _aideSkillExtra.Count;
                _aideSkillIds[name] = id;
                if (same == 0) _aideSkillExtra[id] = name;
                return id;
            }
            kit = new AideKit(who.Id, who.Name, fact.City.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                placed.Where(s => s.Skill.Kind is "모험" or "교역" or "전투").Select(s => (s.Duty, s.Skill.Name)).ToArray(),
                placed.Where(s => s.Skill.Kind == "부관").Select(s => (s.Duty, IdOf(s.Skill.Name))).ToArray());
        }
        return _factKits[who.Id] = kit;
    }

    /// <summary>
    /// 그 부관이 그 스킬을 익혔는가 — dhoguide 스킬 표의 모험 · 교역 · 전투 값을 「익히는 데 드는 부관의 레벨」로 본다
    /// (쪽의 「최대 필요 레벨」이 열마다 가장 큰 값과 맞는다 — 빈센트: 30 · 31 · 33). 세 레벨이 모두 그 값 이상이어야 한다.
    /// 자료에 없는 스킬(손으로 적은 한스 · 후란시느의 것)은 늘 익힌 것으로 본다. 특성 값 조건은 아직 안 본다(특성 값이 없다).
    /// </summary>
    public bool AideSkillReady(Aide aide, string skill) => AideSkillNeed(aide, skill) == "";

    /// <summary>못 익힌 까닭 — 「모험 30 · 전투 12 필요」. 익혔으면 빈 글.</summary>
    public string AideSkillNeed(Aide aide, string skill)
    {
        if (Array.Exists(AideKits, k => k.Id == aide.Who.Id) || AideFactOf(aide.Who)?.Skills.Find(s => s.Name == skill) is not { } row) return "";
        var lacks = new[] { ("모험", row.Adventure, 0), ("교역", row.Trade, 1), ("전투", row.Battle, 2) }.Where(n => aide.Levels[n.Item3] < n.Item2).Select(n => $"{n.Item1} {n.Item2}").ToList();
        return lacks.Count == 0 ? "" : string.Join(" · ", lacks) + " 필요";
    }

    /// <summary>부관 번호로 사람을 찾는다 — 클라이언트의 부관 후보이거나 글에서 온 부관, dhoguide 에서 온 부관.</summary>
    public NamedData? AideById(int id) => Data.Aides.Find(a => a.Id == id) ?? (Array.Find(AideKits, k => k.Id == id) is { } kit ? new NamedData { Id = kit.Id, Name = kit.Name } : null)
        ?? (id >= FactAideBase && Data.AideFacts.Find(a => a.No == id - FactAideBase) is { } fact ? new NamedData { Id = id, Name = fact.Name } : null);

    /// <summary>부관이 가진 스킬을 담당별로 적은 글(부관 창) — 스킬을 모르는 부관은 빈 글.</summary>
    public string AideKitNote(NamedData who) => KitOf(who) is not { } kit ? ""
        : string.Join(" · ", Enumerable.Range(0, 6).Select(duty =>
        {
            var parts = kit.Assist.Where(a => a.Duty == duty).Select(a => a.Skill + "+1").Concat(kit.Skills.Where(s => s.Duty == duty).Select(s => AideSkillName(s.Skill)));
            return parts.Any() ? $"{DutyName(duty)}: {string.Join(", ", parts)}" : "";
        }).Where(t => t != ""));

    /// <summary>고용한 부관의 스킬 가운데 지금 맡은 담당에서 듣는 것과, 다른 담당이라 안 듣는 것(부관 창) — 스킬을 모르는 부관은 빈 글 둘.</summary>
    public (string On, string Off) AideKitStatus(Aide aide)
    {
        if (KitOf(aide.Who) is not { } kit) return ("", "");
        // 레벨이 모자라 아직 못 익힌 것은 빼고 적는다(부관 정보 칸에서 조건을 볼 수 있다)
        string Names(Func<int, bool> duty) => string.Join(", ", kit.Assist.Where(a => duty(a.Duty) && AideSkillReady(aide, a.Skill)).Select(a => a.Skill + "+1")
            .Concat(kit.Skills.Where(s => duty(s.Duty) && AideSkillReady(aide, AideSkillName(s.Skill))).Select(s => AideSkillName(s.Skill) + (s.Skill == SewAidSkill && aide.Level < 18 ? "(레벨 18부터)" : ""))));
        string off = string.Join(" / ", Enumerable.Range(0, 6).Where(d => d != aide.Duty).Select(d => Names(x => x == d) is { Length: > 0 } there ? $"{DutyName(d)}: {there}" : "").Where(t => t != ""));
        return (Names(d => d == aide.Duty), off);
    }

    /// <summary>그 부관이 가진 부관스킬의 원본 설명 글(클라이언트 스킬 표의 1000번대) — 「방화: 화재를 미리 막는다」.</summary>
    public string AideSkillNotes(NamedData who) => KitOf(who) is not { } kit ? ""
        : string.Join("   ", kit.Skills.Select(s => Data.SkillNotes.GetValueOrDefault(s.Skill) is { Length: > 0 } told ? $"{AideSkillName(s.Skill)}: {told.Replace("\n", " ").Trim()}" : "").Where(t => t != ""));

    /// <summary>보조스킬 — 그 담당을 맡은 부관이 가진 스킬이면 +1(부관 한 사람마다가 아니라 한 번만).</summary>
    public int AideRank(int skillId)
    {
        if (Aides.Count == 0) return 0;
        foreach (var aide in Aides)
            if (KitOf(aide.Who) is { } kit && kit.Assist.Any(a => a.Duty == aide.Duty && Data.Skills.Find(s => s.Name == a.Skill)?.Id == skillId && AideSkillReady(aide, a.Skill))) return 1;
        return 0;
    }

    /// <summary>그 부관스킬이 지금 듣는가 — 가진 부관이 그 스킬의 담당을 맡고 있다.</summary>
    public bool AideSkillOn(int aideSkill, int minLevel = 0) =>
        Aides.Any(aide => aide.Level >= minLevel && KitOf(aide.Who) is { } kit && kit.Skills.Any(s => s.Duty == aide.Duty && s.Skill == aideSkill && AideSkillReady(aide, AideSkillName(s.Skill))));

    /// <summary>
    /// 부관이 그 재해를 미리 막는가 — 「물 또는 빵이 0일 경우, 항해중 재해방지스킬은 작동하지 않는다」(인벤 글).
    /// 다 막지는 못한다 — 「확률을 크게 낮춰 주지만 100% 차단하지는 못한다」(사용자가 준 글, 2026-10-08): 여기서 참이면 재해 굴림의 확률에 <see cref="AideGuardFactor"/>(0.2 — 지은 값)를 곱한다.
    /// </summary>
    public bool AidePrevents(int disaster) =>
        Aides.Count > 0 && Water > 0 && Food > 0 && AidePrevention.Any(p => p.Value.Contains(disaster) && AideSkillOn(p.Key));

    /// <summary>대본용 — 한스를 (레벨 조건 없이) 그 담당의 레벨 18 부관으로 두고, 화재 굴림을 2,000일 돌려 난 횟수를 적는다.</summary>
    public void AideKitForTest(int duty)
    {
        if (!Aides.Exists(a => a.Who.Id == AideKits[0].Id)) Aides.Add(new Aide { Who = AideById(AideKits[0].Id)!, Duty = duty });
        var hans = Aides.Find(a => a.Who.Id == AideKits[0].Id)!;
        (hans.Duty, hans.Level) = (duty, 18);
        var fire = Data.Disasters.Find(d => d.Id == 1)!;
        int fires = 0;
        for (int day = 0; day < 2000; day++)
            if (Roll(fire.ChancePerDay * (AidePrevents(fire.Id) ? AideGuardFactor : 1), 1)) fires++;
        Say($"(시험) 한스 — {DutyName(duty)} · 물 {Water:0} 식량 {Food:0} · 화재 굴림 2,000일에 {fires}번 · 봉제 보조 −{AideCraftSave(SewSkill)} · 봉제 +{AideRank(SewSkill)} · 섬유 거래 +{AideRank(57)}");
    }

    /// <summary>봉제 보조 — 「부관이 상인레벨 18이 되면 봉제보조가 생겨서 소비행동력 −1」(벨벳 글). 여기 부관은 레벨이 하나라 그 레벨로 본다.</summary>
    public int AideCraftSave(int craftSkill) => craftSkill == SewSkill && AideSkillOn(SewAidSkill, 18) ? 1 : 0;
}
