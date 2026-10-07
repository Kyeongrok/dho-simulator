using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 대학 — 전공을 정하고, 그 전공의 연구를 하나 골라, 연구가 바라는 행동(항해 · 교역 · 발견 · 수리 …)을 횟수만큼 하면
/// 연구가 끝나 학점과 대학 스킬을 얻는다. 연구 목록은 ssjoy 에서 모은 것이고, 행동 가운데 이 게임에 있는 것만 진행된다
/// (전투 · 육상 · 침몰선 따위는 없어서 그런 연구는 고를 수 없다). 얻은 스킬의 효과는 아직 없다 — 이름만 적어 둔다.
/// </summary>
internal sealed partial class Voyage
{
    public string Major { get; private set; } = "";
    public ResearchFact? Studying { get; private set; }
    public Dictionary<string, int> StudyProgress { get; } = new();
    public int Credits { get; private set; }
    public HashSet<int> StudyDone { get; } = [];

    /// <summary>연구 행동의 이름 → 이 게임의 사건. 없는 행동은 null.</summary>
    private static string? EventOf(string action) => action switch
    {
        "보통 항해" => "Voyage",
        "장거리 항해" => "LongVoyage",
        "흑자 교역" => "Profit",
        "일확천금 교역" => "BigProfit",
        "선박 수리" => "Repair",
        "선박 재해 회복" => "Cure",
        "기본 생산" => "Produce",
        // 해전 · 육상전이 생기면서 할 수 있게 된 것들(이름은 연구 자료의 것 — 「관통 포격」 · 「근거리 포격」은 해전의 그 일이다)
        "포격 실전" => "Shot", "장거리 포격" => "FarShot", "근거리 포격" => "NearShot", "관통 포격" => "RakeShot",
        "백병전 실전" => "Melee", "갑판전 승리" => "MeleeWin", "전멸 승리" => "WipeWin", "단함격파" => "SeaWin", "국가소속선박 격파" => "NavyWin",
        "육상전 실전" => "LandFight", "가격깎기·올려받기 성공" => "Haggle",
        "선박 신규건조, 강화" => "Build",
        "지리 발견" or "생물 발견" or "사적·역사유물 발견" or "종교건축물·유물 발견" or "보물 발견" or "미술품 발견" => "Discover",
        _ when action.StartsWith("교역품 ") && action.EndsWith("구입") => "Buy",
        _ when action.StartsWith("발견물 발견") => "Discover",
        _ => null,
    };

    /// <summary>이 게임에서 끝낼 수 있는 연구인가 — 바라는 행동이 모두 있는 것이어야 하고, 직업 연구는 그 직업이어야 한다.</summary>
    public bool CanStudy(ResearchFact research) =>
        research.Actions.Count > 0 && research.Actions.All(a => EventOf(a.Name) != null) &&
        (research.Job == "" || research.Job == (Data.Jobs.Find(j => j.Id == JobId)?.Name ?? ""));

    /// <summary>고를 수 있는 전공들 — 끝낼 수 있는 연구가 하나라도 있는 것.</summary>
    public List<string> Majors() => Data.Research.Where(CanStudy).Select(r => r.Major).Distinct().Order().ToList();

    public List<ResearchFact> ResearchOf(string major) => Data.Research.Where(r => r.Major == major).OrderBy(r => r.Level).ThenBy(r => r.No).ToList();

    public void ChooseMajor(string major)
    {
        if (major == Major) return;
        Major = major;
        if (Studying != null && Studying.Major != major) { Studying = null; StudyProgress.Clear(); }
        Say($"전공을 「{major}」(으)로 정했다.");
    }

    public void StartResearch(ResearchFact research)
    {
        if (research.Major != Major || !CanStudy(research) || StudyDone.Contains(research.No)) return;
        Studying = research;
        StudyProgress.Clear();
        Say($"연구 「{research.Name}」을(를) 시작했다.");
    }

    /// <summary>그 사건이 일어났다 — 하고 있는 연구의 맞는 행동이 나아가고, 다 차면 연구가 끝난다.</summary>
    private void Studied(string happened, int times = 1)
    {
        if (Studying is not { } research) return;
        foreach (var action in research.Actions.Where(a => EventOf(a.Name) == happened))
            StudyProgress[action.Name] = Math.Min(action.Count, StudyProgress.GetValueOrDefault(action.Name) + times);
        if (!research.Actions.All(a => StudyProgress.GetValueOrDefault(a.Name) >= a.Count)) return;
        StudyDone.Add(research.No);
        Credits += research.Credit;
        Say($"연구 「{research.Name}」을(를) 마쳤다! 학점 {research.Credit:N0}, 대학 스킬 「{research.Skill}」.");
        Cues.Enqueue("StudyDone");
        Studying = null;
        StudyProgress.Clear();
    }

    // 대학 스킬의 효과 — 이름에서 하는 일을 알 수 있는 것만. 이름 끝의 수가 단계(없으면 1)이고, 단계마다의 크기는 지은 값이다
    private static readonly (string Name, string Effect, double PerStep)[] StudyEffects =
    [
        ("항해속도 상승", "Speed", 0.02), ("조타술", "Turn", 0.04), ("재해 발생 확률 감소", "Luck", 0.05), ("내구도 저하율 감소", "Storm", 0.08),
        ("백병전 강화", "Melee", 0.05), ("백병전술", "Melee", 0.04), ("해병대의 백병술", "Melee", 0.05),
        ("육상전 공격력, 방어력 상승", "LandBoth", 3), ("육상전 공격력 상승", "LandAttack", 4),
        ("효율 수리", "Repair", 0.1), ("교역품 할인", "BuyCut", 0.01), ("교역품 거래 보조", "BuyCut", 0.005), ("구명 기술", "CrewLoss", 0.05), ("군의관의 치료술", "CrewLoss", 0.05),
    ];

    // 「항해속도 상승 2」 · 「4종 교역품 할인1」 → (이름, 단계) — 끝의 수가 단계다(띄어 쓰기도 붙여 쓰기도 한다)
    private static (string Name, int Step) SkillStep(string skill)
    {
        var match = System.Text.RegularExpressions.Regex.Match(skill, @"^(.*?)\s*(\d+)$");
        string name = match.Success ? match.Groups[1].Value.Trim() : skill.Trim();
        // 「1종 교역품 할인」처럼 앞의 수는 갈래다 — 떼고 본다
        name = System.Text.RegularExpressions.Regex.Replace(name, @"^\d종 ", "");
        return (name, match.Success ? int.Parse(match.Groups[2].Value) : 1);
    }

    /// <summary>마친 연구의 대학 스킬이 주는 효과의 합.</summary>
    public double Study(string effect)
    {
        double sum = 0;
        foreach (string skill in UniversitySkills())
        {
            var (name, step) = SkillStep(skill);
            foreach (var known in StudyEffects)
                if (known.Name == name && known.Effect == effect) sum += known.PerStep * step;
        }
        return sum;
    }

    public static string? StudyNote(string skill)
    {
        var (name, step) = SkillStep(skill);
        foreach (var known in StudyEffects)
            if (known.Name == name)
                return known.Effect switch
                {
                    "Speed" => $"속도 +{known.PerStep * step * 100:0}%", "Turn" => $"선회 +{known.PerStep * step * 100:0}%", "Luck" => $"재해 −{known.PerStep * step * 100:0}%",
                    "Storm" => $"폭풍 피해 −{known.PerStep * step * 100:0}%", "Melee" => $"백병전 +{known.PerStep * step * 100:0}%", "LandBoth" => $"육상전 공격력 · 방어력 +{known.PerStep * step:0}",
                    "LandAttack" => $"육상전 공격력 +{known.PerStep * step:0}", "BuyCut" => $"살 때 값 −{known.PerStep * step * 100:0.#}%", "Repair" => $"수리 +{known.PerStep * step * 100:0}%", _ => $"선원 피해 −{known.PerStep * step * 100:0}%",
                };
        return null;
    }

    /// <summary>얻은 대학 스킬의 이름들.</summary>
    public IEnumerable<string> UniversitySkills() => Data.Research.Where(r => StudyDone.Contains(r.No) && r.Skill != "").Select(r => r.Skill).Distinct();
}
