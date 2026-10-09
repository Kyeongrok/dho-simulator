namespace Dho.Game;

/// <summary>
/// 투자 — 도시에 돈을 넣어 공적을 쌓고(작위), 제 나라의 점유를 올린다.
/// 원본은 투자한 돈이 그 도시의 나라별 점유율과 발전도를 움직이고, 제 나라를 위한 투자가 공적(국가 공헌)이 되어 왕궁에서 작위를 받는다.
/// 여기서는 그 뼈대만 옮겼다 — 돈과 공적 · 점유 · 발전도의 비, 작위의 하사금은 모두 지은 값이다.
/// </summary>
internal sealed partial class Voyage
{
    /// <summary>도시마다 이제까지 넣은 돈.</summary>
    public Dictionary<int, long> Invested { get; } = [];

    public static readonly int[] InvestSteps = [50_000, 500_000, 5_000_000, 50_000_000];
    private const int DucatsPerMerit = 50_000, DucatsPerShare = 100_000, DucatsPerGrowth = 2_000_000;

    /// <summary>
    /// 도시의 갈래 이름 — 도시 표의 갈래 값 0 ~ 4. 이름은 클라이언트 표 100(본거지 · 영지 · 동맹항 · 개척지 · 해적섬 · 보급항 · 내륙 도시 …)의 것이고,
    /// 값과의 짝은 그 갈래의 도시들로 맞춘 것이다: 0 = 나라의 본거지 열 곳, 1 = 포르투 · 말라가 …, 3 = 나소 · 홀로 · 포트 로얄, 4 = 파리 · 로마 · 쿠스코 …
    /// </summary>
    public static string CityKindName(Dho.Data.CityData city) => city.Kind switch { 0 => "본거지", 1 => "영지", 2 => "동맹항", 3 => "해적섬", 4 => "내륙 도시", _ => "도시" };

    /// <summary>
    /// 이 도시에 투자할 수 있는가 — 동맹항, 그리고 제 나라의 본거지 · 영지(사용자가 가리킨 글, 2026-10-09: 「동맹항, 자국 본거지, 영지에서만 투자 가능 · 보급항과 타국 본거지/영지는 불가」).
    /// </summary>
    public bool CanInvestHere => City.Kind == 2 || (City.Kind is 0 or 1 && NationId != 0 && City.Nation == NationId);

    /// <summary>이 도시에서 투자를 받는 사람 — 본거지는 왕궁의 귀족, 그 밖은 도시관리(같은 글: 「대도시(본거지 등): 왕궁의 귀족 · 중소도시: 도시관리」).</summary>
    public string InvestHost => City.Kind == 0 ? "왕궁의 귀족" : "도시관리";

    /// <summary>대본용 — 시내 지도에 도시관리 표식(장소 20 · 401)이 있는 도시를 세어 기록에 적는다.</summary>
    public void InvestMarksForTest()
    {
        var counts = new Dictionary<int, List<string>> { [20] = [], [21] = [], [401] = [] };
        int towns = 0;
        foreach (var city in Data.Cities)
        {
            if (Dho.Data.TownMap.Load(city.Id) is not { Marks.Count: > 0 } town) continue;
            towns++;
            foreach (int place in counts.Keys) if (town.Marks.Any(m => m.Place == place)) counts[place].Add(city.Name);
        }
        foreach (var (place, names) in counts) Say($"(시험) 시내 지도 {towns}곳 가운데 장소 {place} 「{PlaceName(place)}」 표식이 있는 곳 {names.Count} — {string.Join(" · ", names.Take(8))}");
        Say($"(시험) 이 도시({City.Name}, {CityKindName(City)})의 표식: {string.Join(" ", (TownMap?.Marks ?? []).Select(m => m.Place).Distinct().OrderBy(p => p))}");
    }

    public long InvestedIn(Dho.Data.CityData city) => Invested.GetValueOrDefault(city.Id);

    /// <summary>그 도시에서 제 나라가 차지한 몫(%) — 제 나라 도시는 50 에서, 남의 도시는 0 에서 시작해 투자한 만큼 오른다.</summary>
    public int ShareIn(Dho.Data.CityData city) =>
        (int)Math.Min(100, (_homeShare.Contains(city.Id) || (city.Nation == NationId && !Invested.ContainsKey(city.Id)) ? 50 : 0) + InvestedIn(city) / DucatsPerShare);
    // 처음부터 제 나라 것이던 도시(투자로 넘어온 도시와 가른다)
    private readonly HashSet<int> _homeShare = [];

    /// <summary>발전도(1 ~ 10) — 투자가 쌓일수록 오른다.</summary>
    public int GrowthOf(Dho.Data.CityData city) => (int)Math.Min(10, 1 + InvestedIn(city) / DucatsPerGrowth);

    public int InvestMerit(int amount) => amount / DucatsPerMerit;

    public string? InvestBlocker(int amount) =>
        Mode != Mode.Port ? "항구에서만 투자한다"
        : !CanInvestHere ? (City.Kind is 0 or 1 ? $"남의 나라 {CityKindName(City)}에는 투자할 수 없다" : $"{CityKindName(City)}에는 투자할 수 없다 — 투자는 동맹항과 제 나라의 본거지 · 영지에 한다")
        : NationId == 0 ? "나라가 없다"
        : _investedAt == (City.Id, (int)(Clock / Settings.SecondsPerDay)) ? Text(7024, "※ 투자를 잇달아 할 수는 없다").TrimStart('※') + " — 하루 뒤에"
        : Money < amount ? "돈이 모자라다" : null;

    // 마지막으로 투자한 (도시, 날) — 원본 글 「※투자는 연속으로 할 수 없습니다」(화면 글 7024). 얼마나 기다리는지는 글에 없어 하루로 두었다(지은 값)
    private (int City, int Day) _investedAt = (0, -1);

    public void Invest(int amount)
    {
        if (InvestBlocker(amount) != null) return;
        if (City.Nation == NationId && !Invested.ContainsKey(City.Id)) _homeShare.Add(City.Id);
        Money -= amount;
        if (amount >= 100_000) Studied("BigInvest");      // 연구 과제 「거액 투자」(표 64: 100000 이상 투자)
        _investedAt = (City.Id, (int)(Clock / Settings.SecondsPerDay));
        int growth = GrowthOf(City);
        long before = InvestedIn(City);
        Invested[City.Id] = before + (HasStudy("정치상인의 교섭술 1") ? (long)(amount * 1.1) : amount);      // 대학 스킬 「투자총액과 개인 투자금액의 증가량이 10% 상승한다」
        // 투자 보수(gvdb 도시 쪽의 「投資報酬 … 必要投資額」) — 쌓인 투자가 그 금액을 넘는 순간 한 번 받는다
        if (Data.TownFacts.TryGetValue(City.Id, out var fact) && fact is { InvestReward: > 0, InvestNeed: > 0 } && before < fact.InvestNeed && before + amount >= fact.InvestNeed)
        {
            AddItem(fact.InvestReward, 1);
            Say($"{City.Name}에 {fact.InvestNeed:N0} 두캇을 투자한 보답으로 「{ItemName(fact.InvestReward)}」을(를) 받았다.");
            Cues.Enqueue("Done");
        }
        int merit = (int)(InvestMerit(amount) * (1 + HonorEffect("Invest")));
        Merit += merit;
        Cues.Enqueue("Buy");
        Say($"{City.Name}에 {amount:N0} 두캇을 투자했다. 공적 +{merit} (영향도 {ShareIn(City)}%)");
        if (GrowthOf(City) > growth) Say($"{City.Name}의 발전도가 {GrowthOf(City)}(으)로 올랐다.");
        if (City.Nation != NationId && ShareIn(City) > 50)
        {
            City.Nation = NationId;
            Merit += 20;
            Say($"{City.Name}이(가) {NationName}의 동맹항이 되었다! 공적 +20");
            Cues.Enqueue("Done");
        }
        // 원본의 알림 메모(클라이언트 표 121 의 2번) 글 그대로
        if (TitleDue) Say(Data.Memos.GetValueOrDefault(2) ?? "작위를 받을 만한 공적이 쌓였다. 자국 본거지의 관리를 찾아가자.");      // 원본의 알림 메모(표 121 의 2번) — 클라이언트에서 읽는다
    }

    public bool TitleDue => Title < Data.Orders.Titles.Count - 1 && Merit >= MeritToNext;
    public int TitleGift(int title) => 30_000 * title;

    /// <summary>왕궁에서 작위를 받는다 — 공적이 찬 만큼 오르고 하사금을 받는다.</summary>
    public void ReceiveTitle()
    {
        if (!AtCourt) return;
        while (TitleDue)
        {
            Merit -= MeritToNext;
            Title++;
            Money += TitleGift(Title);
            Say($"작위가 올랐다 — {TitleName}! 하사금 {TitleGift(Title):N0} 두캇.");
            Cues.Enqueue("SkillUp");
            LevelNotice = ($"작위 {TitleName}", LevelNotice.Count + 1);
        }
    }

    // 이어 하기: 투자로 넘어온 도시의 소속을 되살린다
    private void RestoreInvested(Dictionary<int, long> saved, List<int> home)
    {
        foreach (var (id, sum) in saved) Invested[id] = sum;
        foreach (int id in home) _homeShare.Add(id);
        foreach (var city in Data.Cities.Where(c => Invested.ContainsKey(c.Id) && c.Kind != 0 && c.Nation != NationId))
            if (ShareIn(city) > 50) city.Nation = NationId;
    }
}
