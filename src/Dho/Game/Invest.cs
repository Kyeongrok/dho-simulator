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
        : City.Kind == 0 ? "본거지에는 투자할 수 없다"
        : NationId == 0 ? "나라가 없다"
        : Money < amount ? "돈이 모자라다" : null;

    public void Invest(int amount)
    {
        if (InvestBlocker(amount) != null) return;
        if (City.Nation == NationId && !Invested.ContainsKey(City.Id)) _homeShare.Add(City.Id);
        Money -= amount;
        int growth = GrowthOf(City);
        Invested[City.Id] = InvestedIn(City) + amount;
        int merit = InvestMerit(amount);
        Merit += merit;
        Cues.Enqueue("Buy");
        Say($"{City.Name}에 {amount:N0} 두캇을 투자했다. 공적 +{merit} (점유 {ShareIn(City)}%)");
        if (GrowthOf(City) > growth) Say($"{City.Name}의 발전도가 {GrowthOf(City)}(으)로 올랐다.");
        if (City.Nation != NationId && ShareIn(City) > 50)
        {
            City.Nation = NationId;
            Merit += 20;
            Say($"{City.Name}이(가) {NationName}의 동맹항이 되었다! 공적 +20");
            Cues.Enqueue("Done");
        }
        if (TitleDue) Say("공적이 찼다 — 본국의 왕궁에서 작위를 받자.");
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
