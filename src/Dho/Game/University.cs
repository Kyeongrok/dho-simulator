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
        Studying = null;
        StudyProgress.Clear();
    }

    /// <summary>얻은 대학 스킬의 이름들.</summary>
    public IEnumerable<string> UniversitySkills() => Data.Research.Where(r => StudyDone.Contains(r.No) && r.Skill != "").Select(r => r.Skill).Distinct();
}
