using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 대학 — 전공을 정하고, 그 전공의 연구(논문 테마)를 하나 골라, 연구 행동(항해 · 교역 · 발견 · 수리 …)으로 논문을 쓴다:
/// 행동 한 번에 그 행동에 적힌 쪽만큼 써지고, 필요 페이지에 닿으면 연구가 끝나 학점과 대학 스킬을 얻는다(w-927).
/// 연구 목록은 ssjoy 에서 모은 것 + 클라이언트의 연구 값 표(67)이고, 행동 가운데 이 게임에 있는 것만 진행된다
/// (행동의 이름 → 게임의 사건은 EventOf; 그런 행동이 하나라도 든 연구를 고를 수 있다 — 303건 가운데 275건).
/// 원본에 있고 여기 없는 것(화면 글로 본 것 — 분석 글 84 의 w-928): 연구동 고르기 · 소비 학점 · 논문 제출 횟수 조건 · 대학 레벨 · 학회,
/// 그리고 학과(항해장 · 감시 · 회계사 …)의 연구는 부관을 골라 시키는 것이다 — 여기서는 주인공의 전공처럼 보인다. 얻은 스킬의 효과는 원본 설명 글에 크기가 있는 것만 넣었다(StudyEffects · HasStudy 를 쓰는 자리들) — 나머지는 이름과 설명만 보인다.
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
        "기본 생산" or "상급 생산" => "Produce",
        "대성공 생산" => "Great",
        // 표 64 의 설명대로: 악천 항해 = 폭풍，눈보라가 칠 때 해상에 있다 · 선원 교류 = 동행한 부관의 신뢰도가 증가 · 직업 실습 = 전직 · 자물쇠 따기 = 보물상자를 연다 · 수탈 실전 = 수탈을 한다 · 침몰선조사 = 침몰선 조사를 완료
        "악천 항해" => "Storm", "선원 교류" => "Trust", "직업 실습" => "Job", "자물쇠 따기" => "Lock", "수탈 실전" => "Loot", "침몰선조사" => "Wreck",
        "고수익 교역" => "HighProfit", "거액 투자" => "BigInvest", "서고 조사" => "Library", "인명 구조 실전" => "Rescue", "공격 전술" => "Tactic", "순시 항해" => "Patrol", "초 장거리 항해" => "UltraVoyage", "증기선 항해" => "SteamVoyage",
        "코스 요리 만끽" => "Course",              // 주점에서 요리를 먹는 일로 본다(짐작)
        "야외 활동" => "Outdoor",                  // 「조달，낚시，채집으로 교역품을 입수한다」(표 64) — 채집 · 낚시로 물건을 얻을 때
        // 해전 · 육상전이 생기면서 할 수 있게 된 것들(이름은 연구 자료의 것 — 「관통 포격」 · 「근거리 포격」은 해전의 그 일이다)
        "포격 실전" => "Shot", "장거리 포격" => "FarShot", "근거리 포격" => "NearShot", "관통 포격" => "RakeShot",
        "백병전 실전" => "Melee", "갑판전 승리" => "MeleeWin", "전멸 승리" => "WipeWin", "단함격파" => "SeaWin", "국가소속선박 격파" => "NavyWin",
        "육상전 실전" => "LandFight", "가격깎기·올려받기 성공" => "Haggle",
        "선박 신규건조, 강화" => "Build",
        "지리 발견" or "생물 발견" or "사적·역사유물 발견" or "종교건축물·유물 발견" or "보물 발견" or "미술품 발견" or "천체 발견" => "Discover",
        _ when action.EndsWith("생산 대성공") || action.StartsWith("기본 생산 대성공") => "Great",
        _ when action.StartsWith("교역품 ") && action.EndsWith("구입") => "Buy",
        _ when action.StartsWith("발견물 발견") => "Discover",
        _ => null,
    };

    /// <summary>이 게임에서 끝낼 수 있는 연구인가 — 연구 행동 가운데 게임에 있는 것이 하나라도 있어야 하고, 직업 연구는 그 직업이어야 한다.</summary>
    public bool CanStudy(ResearchFact research) =>
        research.Actions.Exists(a => EventOf(a.Name) != null && a.Count > 0) &&      // 어느 행동으로든 논문이 써지니 하나만 있어도 된다(w-927)
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

    /// <summary>하던 연구를 그만둔다 — 쓰던 논문은 버려진다(원본의 「연구 중단」 · 「파기」; 화면이 한 번 더 묻는다).</summary>
    public void DropResearch()
    {
        if (Studying is not { } research) return;
        Say($"연구 「{research.Name}」을(를) 그만두었다." + (StudyPages > 0 ? $" (쓴 {StudyPages:N0}쪽을 버렸다)" : ""));
        Studying = null;
        StudyProgress.Clear();
    }

    public void StartResearch(ResearchFact research)
    {
        if (research.Major != Major || !CanStudy(research) || StudyDone.Contains(research.No)) return;
        Studying = research;
        StudyProgress.Clear();
        Say($"연구 「{research.Name}」을(를) 시작했다.");
    }

    /// <summary>그 사건이 일어났다 — 하고 있는 연구의 맞는 행동이 나아가고, 다 차면 연구가 끝난다.</summary>
    // 할 일의 이름이 조건을 달고 있으면 그것까지 맞아야 센다(조건의 뜻은 클라이언트 표 64 — 연구 과제 목록 — 의 설명 글):
    // 「발견물 발견(R7)」 = 필요 스킬 랭크 7 이상 · 「지리 발견」 = 지리적 발견물 · 「교역품 3종 구입」 = 그 갈래의 교역품 · 「봉제 생산 대성공」 = 봉제 레시피 · 「기본 생산」 = 조리 · 봉제 · 주조 · 보관 · 공예
    private static readonly int[] BasicCrafts = [72, 73, 74, 78, 81];
    private static bool Fits(string action, int level, int kind)
    {
        if (System.Text.RegularExpressions.Regex.Match(action, @"\(R(\d+)\)") is { Success: true } rank && level < int.Parse(rank.Groups[1].Value)) return false;
        if (System.Text.RegularExpressions.Regex.Match(action, @"^교역품 (\d)종 구입$") is { Success: true } buy) return GoodClasses[int.Parse(buy.Groups[1].Value) - 1].Contains(kind);
        return action switch
        {
            "지리 발견" => kind is 15 or 16 or 17,              // 항구 · 육지 · 지리(항구 · 육지를 넣은 것은 짐작)
            "생물 발견" => kind is >= 8 and <= 14,
            "사적·역사유물 발견" => kind is 1 or 3,
            "종교건축물·유물 발견" => kind is 2 or 4,
            "선박 재해 회복" => kind == 1,                   // 「화재 관련 재해，돛，키 손상을 회복」 — 게임에는 화재(1)뿐
            "보물 발견" => kind == 6, "미술품 발견" => kind == 5, "천체 발견" => kind == 18,
            "조리 생산 대성공" => kind == 72, "봉제 생산 대성공" => kind == 73, "주조 생산 대성공" => kind == 74, "보관 생산 대성공" => kind == 78, "공예 생산 대성공" => kind == 81,
            "연금술 생산 대성공" => kind == 82, "언어학 생산 대성공" => kind == 42,
            "기본 생산" => BasicCrafts.Contains(kind), "상급 생산" => kind is 42 or 82,
            _ when action.StartsWith("기본 생산 대성공") => BasicCrafts.Contains(kind),
            _ => true,
        };
    }

    /// <summary>연구를 마친 알림 — 화면 위쪽 가운데에 잠깐 뜬다(제목 · 내용 · 뜬 시각).</summary>
    public (string Title, string Text, double At) StudyNotice { get; private set; } = ("", "", -100);

    /// <summary>그 번호의 연구에 게임에 있는 행동이 하나라도 있는가(직업 조건은 안 본다 — 위키 목록이 흐리게 적는 데 쓴다).</summary>
    public bool StudyHasWay(int no) => Data.Research.Find(r => r.No == no)?.Actions.Exists(a => EventOf(a.Name) != null && a.Count > 0) ?? false;

    /// <summary>
    /// 그 연구 행동을 이 게임이 어떻게 세는지 — 원본 설명과 다르거나 문턱을 지어 넣은 것만(없으면 빈 글).
    /// 문턱은 세는 자리의 값과 같아야 한다: 항해는 Voyage.EnterPort(하루 · 15일 · 40일), 포격 거리는 SeaShips 의 Fire(사거리의 0.6 밖 · 0.35 안).
    /// </summary>
    public static string StudyWayNote(string action) => action switch
    {
        "보통 항해" => "하루 이상 항해하고 입항하면 한 번",
        "장거리 항해" => "15일 이상 항해하고 입항하면 한 번",
        "초 장거리 항해" => "40일 이상 항해하고 입항하면 한 번",
        "증기선 항해" => "증기선으로 하루 이상 항해하고 입항하면 한 번",
        "순시 항해" => "경계나 감시 스킬을 가진 채 하루 이상 항해하고 입항하면 한 번",
        "서고 조사" => "그날 읽을 수 있는 책을 다 읽으면 한 번",
        "공격 전술" => "기뢰나 충각으로 적선을 가라앉히면 한 번",
        "인명 구조 실전" => "해전에서 구조 · 외과의술 스킬이 선원을 살리면 한 번",
        "장거리 포격" => "사거리의 6할보다 먼 적에게 포를 쏘면 한 번",
        "근거리 포격" => "사거리의 3할 5푼보다 가까운 적에게 포를 쏘면 한 번",
        "코스 요리 만끽" => "주점에서 요리를 먹으면 한 번",
        _ => "",
    };

    /// <summary>게임에 있는 연구 행동인가(화면이 없는 행동을 「(없음)」으로 적는다).</summary>
    public bool HasStudyAction(string action) => EventOf(action) != null;

    /// <summary>하고 있는 연구의 논문에 쓴 페이지(행동별로 쓴 것의 합).</summary>
    public int StudyPages => StudyProgress.Values.Sum();

    private void Studied(string happened, int times = 1, int level = 0, int kind = 0)
    {
        // 연구 = 논문 쓰기(w-926 · w-927): 연구 행동을 한 번 하면 그 행동에 적힌 수만큼 논문이 써지고, 쓴 페이지가 필요 페이지(Pages)에 닿으면 끝난다.
        // 근거: 원본 화면 글 3222 · 1480 「필요 페이지 수」 · 1481 「기술 페이지 수」, 그리고 자료의 값(쉬운 행동 1 · 어려운 행동 96 · 480 에 필요 4,800 —
        // 필요 페이지가 행동의 수로 늘 나누어떨어진다). 원본 규칙을 글과 값으로 미루어 읽은 것(짐작)이다. StudyProgress 는 행동별로 쓴 페이지다(전에는 횟수였다).
        // 원본은 논문이 완성되면 대학에 보고해야 학점을 받는다(글 3223) — 여기서는 완성되는 자리에서 바로 받는다.
        if (Studying is not { } research) return;
        int before = StudyPages;
        foreach (var action in research.Actions.Where(a => EventOf(a.Name) == happened && Fits(a.Name, level, kind)))
            StudyProgress[action.Name] = StudyProgress.GetValueOrDefault(action.Name) + action.Count * times;
        int wrote = StudyPages - before, need = Math.Max(1, research.Pages);
        if (wrote <= 0) return;
        // 포격처럼 잦은 행동이 기록을 덮지 않게 — 쓴 페이지가 필요의 열에 하나를 넘길 때(또는 한 번에 스무에 하나 넘게 썼을 때)만 알린다
        if (StudyPages < need && (wrote * 20 >= need || before * 10 / need != StudyPages * 10 / need))
            Say($"{Fill(Text(3222, "논문을 %d쪽 썼다."), $"{wrote}")} ({Math.Min(StudyPages, need):N0} / {need:N0})");
        if (StudyPages < need) return;
        StudyDone.Add(research.No);
        Credits += research.Credit;
        Say($"{Text(3445, "논문을 다 썼다!")} 「{research.Name}」 — 학점 {research.Credit:N0}, 대학 스킬 「{research.Skill}」.");
        Cues.Enqueue("StudyDone");
        StudyNotice = (Text(3445, "논문을 다 썼다!"), $"「{research.Name}」  학점 +{research.Credit:N0}" + (research.Skill != "" ? $"  ·  {research.Skill}" : ""), Clock);
        Studying = null;
        StudyProgress.Clear();
    }

    // 대학 스킬의 효과 — 이름에서 하는 일을 알 수 있는 것만. 이름 끝의 수가 단계(없으면 1)이고, 단계마다의 크기는 지은 값이다
    private static readonly (string Name, string Effect, double PerStep)[] StudyEffects =
    [
        ("항해속도 상승", "Speed", 0.02), ("재해 발생 확률 감소", "Luck", 0.05), 
        ("백병전 강화", "Melee", 0.05), ("백병전술", "Melee", 0.04), 
        ("육상전 공격력, 방어력 상승", "LandBoth", 3), ("육상전 공격력 상승", "LandAttack", 4),
        ("교역품 거래 보조", "BuyCut", 0.005), 
        ("요리 효과 상승", "FoodGain", 0.5),      // 음식으로 차는 행동력이 는다(사용자가 준 글: 「회복량을 대폭」) — 단계마다 +50% 는 지은 값
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

    // 「N종 교역품 할인」의 갈래(스킬 설명 글의 묶음) — 교역품 갈래 번호(표 18)로: 1종 식료품 · 조미료 · 가축 · 의약품 · 잡화 / 2종 주류 · 염료 · 광석 · 공업품 · 기호품 / 3종 섬유 · 직물 · 무기류 · 총포류 · 공예품 · 미술품 / 4종 향신료 · 귀금속 · 향료 · 보석
    private static readonly int[][] GoodClasses = [[0, 1, 13, 5, 19], [2, 8, 10, 18, 3], [6, 7, 14, 15, 17, 16], [4, 9, 11, 12]];

    /// <summary>
    /// 그 교역품에 듣는 「N종 교역품 할인」의 합 — 할인1 은 구입액의 10%, 할인2 는 15%, 둘 다면 25%(사용자가 준 벨벳 글의 「3번 카테고리 할인 1, 2」, 2026-10-08).
    /// 글은 3종만 적었다 — 1 · 2 · 4종도 같은 크기로 본 것은 짐작. 원본은 낸 값의 일부를 돌려받는 꼴(「구입 비용 중 일부가 환급된다」)인데 여기서는 값에서 바로 뺀다.
    /// </summary>
    public double ClassCut(GoodData good)
    {
        double sum = 0;
        foreach (string skill in UniversitySkills())
            if (System.Text.RegularExpressions.Regex.Match(skill, @"^(\d)종 교역품 할인\s*(\d)$") is { Success: true } cut && int.Parse(cut.Groups[1].Value) is >= 1 and <= 4 and var kind && GoodClasses[kind - 1].Contains(good.Kind))
                sum += cut.Groups[2].Value == "1" ? 0.10 : 0.15;
        return sum;
    }

    // 설명 글에 크기가 적힌 대학 스킬(클라이언트 스킬 표 3300번대)
    public bool HasStudy(string skill) => UniversitySkills().Any(s => s.Replace(" ", "") == skill.Replace(" ", ""));

    // 「○○의 기술 1 — ○○ 생산 시 필요한 재료 수가 10% 감소한다. 그러나 적어도 하나는 필요하다」 — 생산 스킬 번호 → 대학 스킬 이름
    private static readonly (int Craft, string[] Skills)[] CraftStudies =
    [
        (72, ["조리사의 기술 1", "파티쉐의 기술 1"]), (73, ["재봉사의 기술 1"]), (74, ["대장공의 기술 1"]), (81, ["공예사의 기술 1"]), (78, ["아르티장의 기술 1"]), (82, ["필로조프의 기술 1", "연금술 견습생의 지혜 2"]),
    ];

    /// <summary>그 생산 스킬로 times 번 만들 때 재료가 안 드는 번 수 — 열 번에 한 번(10%), 적어도 한 번치는 든다.</summary>
    public int StudySpared(int craftSkill, int times) =>
        Array.Find(CraftStudies, c => c.Craft == craftSkill) is { Skills: not null } study && study.Skills.Any(HasStudy) ? Math.Min(times - 1, times / 10) : 0;

    /// <summary>대학 스킬의 원본 설명 글(클라이언트 스킬 표) — 없으면 빈 글. 학과의 스킬 연구가 주는 것은 보통 스킬과 이름 · 설명이 같은 줄(스킬 표 6200 + 스킬 번호)이라, 대학 스킬 글이 없으면 그 스킬의 설명을 준다.</summary>
    public string StudySkillNote(string skill) => (Data.StudySkillNotes.GetValueOrDefault(skill.Replace(" ", "")) ?? Data.Skills.Find(s => s.Name == skill)?.Description)?.Replace("\n", " ").Trim() ?? "";

    /// <summary>할 일의 원본 설명 글(클라이언트 표 64) — 없으면 빈 글.</summary>
    public string StudyTaskNote(string action) => Data.StudyTasks.Find(t => t.Name.Trim() == action.Trim())?.Description.Replace("\n", " ").Trim() ?? "";

    /// <summary>대본용 — 끝낼 수 있는 연구 수를 적고, 「교역품 3종 구입」이 든 연구를 잡아 1종(식료품)과 3종(섬유) 구입 사건을 넣어 진행이 어떻게 되는지 적는다.</summary>
    public void StudyCheckForTest()
    {
        int all = Data.Research.Count, able = Data.Research.Count(r => r.Actions.Exists(a => EventOf(a.Name) != null && a.Count > 0));
        Say($"(시험) 연구 {all}건 가운데 게임에 있는 행동이 하나라도 든 것 {able}건");
        if (Data.Research.Find(r => r.Actions.Exists(a => a.Name == "교역품 3종 구입")) is not { } research) { Say("(시험) 「교역품 3종 구입」이 든 연구가 없다"); return; }
        (Studying, _) = (research, 0); StudyProgress.Clear();
        Studied("Buy", 1, 0, 0);
        int afterFood = StudyProgress.GetValueOrDefault("교역품 3종 구입");
        Studied("Buy", 1, 0, 6);
        Say($"(시험) 「{research.Name}」 — 식료품을 사면 {afterFood} · 섬유를 사면 {StudyProgress.GetValueOrDefault("교역품 3종 구입")} (할 일: {string.Join(" · ", research.Actions.Select(a => $"{a.Name} {a.Count}"))})");
        (Studying, _) = (null, 0); StudyProgress.Clear();
    }

    /// <summary>대본용 — 그 이름의 대학 스킬을 주는 연구를 마친 것으로.</summary>
    /// <summary>대본용 — 대학 스킬 「정치상인의 교섭술 1」의 투자 10%와 연구 과제 「흑자 교역」의 10만 문턱을 실제 길(Invest · SellGood)로 돌려 적는다.</summary>
    public void StudyEffectsForTest()
    {
        Money += 10_000_000;
        _investedAt = (0, -1);
        long before = InvestedIn(City);
        Invest(100_000);
        long plain = InvestedIn(City) - before;
        StudyForTest("정치상인의 교섭술 1");
        _investedAt = (0, -1);
        before = InvestedIn(City);
        Invest(100_000);
        Say($"(시험) 10만 투자 — 스킬 없이 +{plain:N0} · 정치상인의 교섭술 +{InvestedIn(City) - before:N0}" + (InvestBlocker(100_000) is { } why && plain == 0 ? $" (막힘: {why})" : ""));
        if (City.Kind == 2) return;      // 동맹항에서는 투자만 본다(기록이 밀려 올라가지 않게)
        if (Data.Research.Find(r => r.Actions.Exists(a => a.Name == "흑자 교역")) is not { } research || Data.Goods.Count == 0) { Say("(시험) 「흑자 교역」이 든 연구가 없다"); return; }
        (Studying, _) = (research, 0); StudyProgress.Clear();
        var good = Data.Goods[0];
        int price = Math.Max(1, SellPrice(good));
        Cargo[good.Id] = new CargoItem { Count = 90_000 / price, Cost = 0 };
        SellGood(good, int.MaxValue);
        int small = StudyProgress.GetValueOrDefault("흑자 교역");
        price = Math.Max(1, SellPrice(good));
        Cargo[good.Id] = new CargoItem { Count = 130_000 / price + 1, Cost = 0 };
        SellGood(good, int.MaxValue);
        Say($"(시험) 흑자 교역 — 이익 10만 아래를 팔면 {small} · 10만 넘게 팔면 {StudyProgress.GetValueOrDefault("흑자 교역")}");
    }
    /// <summary>
    /// 대본용 — 싸움 중에 부른다. 대학 스킬 「해군사관의 기술 1」(포격 10%)을 한 발씩 번갈아 켜고 끄며 같은 적에게 포격 800발의 평균을 견주고
    /// (숙련이 오르는 것이 한쪽에 쏠리지 않게), 「현상금 사냥꾼의 기술 1」을 켠 채 적을 가라앉혀 받은 돈을 스킬 없는 식의 값과 견준다.
    /// </summary>
    public void BattleStudyForTest()
    {
        if (Battle is not { Result: null } battle) { Say("(시험) 싸움이 없다"); return; }
        var foe = battle.Foe;
        battle.MyReload = 0;
        if (GunsFitted <= 0 && Data.ShipParts.Find(p => p.Slot == 4 && p.A > 0) is { } gun) Parts.Add(gun);      // 시험용 배에는 대포가 없다
        if (FireBlocker() is { } why) { Say($"(시험) 포를 못 쏜다 — {why}"); return; }
        int[] navy = [.. Data.Research.Where(r => r.Skill.Replace(" ", "") == "해군사관의기술1").Select(r => r.No)];
        double[] sum = new double[2];
        for (int shot = 0; shot < 800; shot++)
        {
            foreach (int no in navy) { if (shot % 2 == 1) StudyDone.Add(no); else StudyDone.Remove(no); }
            (foe.Durability, foe.Crew, battle.MyReload) = (1e9, 1e6, 0);
            Fire();
            sum[shot % 2] += 1e9 - foe.Durability;
            battle.Shots.Clear(); battle.Hits.Clear();
        }
        string shots = ($"(시험) 포격 400발씩의 평균 — 스킬 없이 {sum[0] / 400:0.0} · 해군사관의 기술 {sum[1] / 400:0.0} (×{sum[1] / Math.Max(1, sum[0]):0.000})");
        _ = shots;
        StudyForTest("현상금 사냥꾼의 기술 1");
        int plain = foe.Monster > 0 ? 0 : (int)((500 + foe.MaxDurability * (foe.Kind == 0 ? 20 : 8)) * (1 + Bonus("Loot")));
        long before = Money;
        (foe.Durability, foe.Crew) = (0, 0);
        CheckBattleEnd(battle);
        Say($"(시험) 승리 상금 — 스킬 없는 식의 값 {plain:N0} · 현상금 사냥꾼의 기술로 받은 돈 {Money - before:N0}");
        Say(shots);
    }
    /// <summary>대본용 — 「서고 조사」가 든 연구를 잡고, 이 도시 서고의 첫 책을 오늘 읽을 수 있는 만큼 실제 길(ReadBook)로 읽어 진행이 오르는지 적는다.</summary>
    public void StudyHooksForTest()
    {
        if (Data.Research.Find(r => r.Actions.Exists(a => a.Name == "서고 조사")) is not { } research) { Say("(시험) 「서고 조사」가 든 연구가 없다"); return; }
        foreach (var rule in Data.SkillRules.Where(r => r.Effect is "Appraise" or "Language" or "BookLanguage")) SetRankForTest(rule.SkillId, 1);
        Money += 100_000;
        (Studying, _) = (research, 0); StudyProgress.Clear();
        var book = Books().FirstOrDefault();
        if (book == null || ReadBlocker(book) is { } why0 && why0 != "") { Say($"(시험) 책을 못 읽는다 — {(book == null ? "책이 없다" : ReadBlocker(book))}"); (Studying, _) = (null, 0); return; }
        int read = 0, midway = -1;
        while (ReadBlocker(book) == null) { ReadBook(book); read++; if (BooksLeft == 1) midway = StudyProgress.GetValueOrDefault("서고 조사"); }
        Say($"(시험) 「{research.Name}」 — 책 {read}권을 읽어 「서고 조사」 {StudyProgress.GetValueOrDefault("서고 조사")} (한 권 남았을 때 {midway})");
        (Studying, _) = (null, 0); StudyProgress.Clear();
    }
    /// <summary>대본용 — 그 이름의 연구를 (전공을 맞춰) 시작하고, 게임에 있는 첫 행동을 세 번 한 것으로 넣는다(대학 창의 「쓴 페이지」 줄을 보려고).</summary>
    public void StudyStartForTest(string name)
    {
        if (Data.Research.Find(r => r.Name == name) is not { } research) { Say($"(시험) 연구 「{name}」이 없다"); return; }
        ChooseMajor(research.Major);
        StartResearch(research);
        if (Studying != research || research.Actions.Find(a => EventOf(a.Name) != null) is not { } first) { Say("(시험) 연구를 시작하지 못했다"); return; }
        Studied(EventOf(first.Name)!, 3);
        Say($"(시험) 「{first.Name}」 세 번 — 쓴 페이지 {StudyPages} / {research.Pages}");
        // 목록의 ✓ · n/m 표시를 보려고 — 이름 차례로 앞의 두 전공에서 연구 하나씩을 마친 것으로 둔다
        foreach (string major in Majors().Take(2)) StudyDone.Add(ResearchOf(major)[0].No);
    }
    /// <summary>대본용 — 그 이름의 연구를 시작해 게임에 있는 첫 행동으로 필요 페이지를 다 채운다(끝나는 알림을 보려고).</summary>
    public void StudyFinishForTest(string name)
    {
        if (Data.Research.Find(r => r.Name == name) is not { } research) { Say($"(시험) 연구 「{name}」이 없다"); return; }
        ChooseMajor(research.Major);
        StartResearch(research);
        if (Studying != research || research.Actions.Find(a => EventOf(a.Name) != null && a.Count > 0) is not { } first) { Say("(시험) 연구를 시작하지 못했다"); return; }
        Studied(EventOf(first.Name)!, (research.Pages + first.Count - 1) / first.Count);
    }
    /// <summary>대본용 — 그 행동이 든 연구를 잡는다(전공은 안 따진다). 뒤이어 실제 길(입항 · 해전)을 타게 해 studyreport 로 진행을 본다.</summary>
    public void StudyHoldForTest(string action)
    {
        if (Data.Research.Find(r => r.Actions.Exists(a => a.Name == action)) is not { } research) { Say($"(시험) 「{action}」이 든 연구가 없다"); return; }
        Studying = research; StudyProgress.Clear();
        Say($"(시험) 「{research.Name}」을 잡았다 ({string.Join(" · ", research.Actions.Select(a => $"{a.Name} +{a.Count}"))} / 필요 {research.Pages})");
    }
    public void StudyForTest(string skill)
    {
        foreach (var research in Data.Research.Where(r => r.Skill.Replace(" ", "") == skill.Replace(" ", ""))) StudyDone.Add(research.No);
        Say($"(시험) 대학 스킬: {string.Join(" · ", UniversitySkills())}");
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
        if (name == "교역품 할인") return $"그 갈래의 교역품을 살 때 값 −{(step == 1 ? 10 : 15)}%";
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
