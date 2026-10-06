namespace Dho.Game;

/// <summary>개인농장의 시설 한 칸 — 갈래(밭 · 과수원 …), 기르는 것, 랭크, 마지막으로 거둔 때.</summary>
internal sealed class FarmPlot
{
    public int Kind = -1, Product, Rank = 1;
    public double Since;
}

/// <summary>
/// 개인농장 — 시설을 지어 두면 날이 지나는 만큼 교역품이 쌓이고, 들러서 거둬 간다.
/// 시설의 갈래 이름(밭 · 과수원 · 허브 농장 · 논 · 광산 · 선착장 · 방목장 · 양계장)은 클라이언트 표 46 의 것이다.
/// 농장을 얻는 길, 칸 수, 시설마다 나는 것 · 나는 빠르기 · 값은 클라이언트에 없어 모두 지은 것이다.
/// </summary>
internal sealed partial class Voyage
{
    public bool FarmOwned { get; private set; }
    public FarmPlot[] Farm { get; } = [new(), new(), new(), new()];

    public const int FarmPrice = 300_000, FarmBuildPrice = 20_000, FarmMaxRank = 3;
    public static readonly string[] FarmKinds = ["밭", "과수원", "허브 농장", "논", "광산", "선착장", "방목장", "양계장"];
    // 시설마다 기를 수 있는 교역품(이름으로 찾는다 — 표에 없는 것은 빠진다)
    private static readonly string[][] FarmYields =
    [
        ["밀", "보리", "감자"], ["올리브", "아마"], ["로즈마리", "민트", "타임"], ["콩", "옥수수"],
        ["철광석", "동광석", "석탄"], ["청어", "대구", "새우"], ["양모", "우유"], ["달걀", "닭"],
    ];

    /// <summary>농장에 들 수 있는가 — 제 나라의 본거지에서(지은 규칙).</summary>
    public bool AtFarm => Mode == Mode.Port && City.Kind == 0 && (NationId == 0 || City.Nation == NationId);

    public List<Dho.Data.GoodData> FarmProducts(int kind) =>
        kind < 0 || kind >= FarmYields.Length ? [] : FarmYields[kind].Select(name => Data.Goods.Find(g => g.Name == name)).OfType<Dho.Data.GoodData>().ToList();

    public Dho.Data.GoodData? FarmProduct(FarmPlot plot) => FarmProducts(plot.Kind).ElementAtOrDefault(plot.Product);

    private double FarmDay => Clock / Settings.SecondsPerDay;
    public static int FarmRate(FarmPlot plot) => 10 * plot.Rank;
    public static int FarmStore(FarmPlot plot) => 100 * plot.Rank;
    public int FarmUpgradePrice(FarmPlot plot) => 100_000 * plot.Rank * plot.Rank;

    /// <summary>그 칸에 쌓인 수 — 하루에 랭크 × 10, 랭크 × 100 까지.</summary>
    public int FarmReady(FarmPlot plot) => plot.Kind < 0 ? 0 : (int)Math.Min(FarmStore(plot), Math.Max(0, FarmDay - plot.Since) * FarmRate(plot));

    public void BuyFarm()
    {
        if (!AtFarm || FarmOwned || Money < FarmPrice) return;
        Money -= FarmPrice;
        FarmOwned = true;
        Cues.Enqueue("Buy");
        Say($"개인농장의 권리를 샀다. ({FarmPrice:N0} 두캇) 시설을 지어 보자.");
    }

    /// <summary>칸에 시설을 짓거나 바꾼다 — 쌓여 있던 것은 사라진다.</summary>
    public void BuildFarm(int index, int kind, int product)
    {
        if (!AtFarm || !FarmOwned || index < 0 || index >= Farm.Length || Money < FarmBuildPrice || FarmProducts(kind).Count == 0) return;
        var plot = Farm[index];
        Money -= FarmBuildPrice;
        if (plot.Kind != kind) plot.Rank = 1;
        (plot.Kind, plot.Product, plot.Since) = (kind, Math.Clamp(product, 0, FarmProducts(kind).Count - 1), FarmDay);
        Say($"농장 {index + 1}번 칸에 {FarmKinds[kind]}을(를) 지었다 — {FarmProduct(plot)?.Name}. ({FarmBuildPrice:N0} 두캇)");
    }

    public void UpgradeFarm(int index)
    {
        if (!AtFarm || index < 0 || index >= Farm.Length) return;
        var plot = Farm[index];
        if (plot.Kind < 0 || plot.Rank >= FarmMaxRank || Money < FarmUpgradePrice(plot)) return;
        int ready = FarmReady(plot);
        Money -= FarmUpgradePrice(plot);
        plot.Rank++;
        plot.Since = FarmDay - (double)ready / FarmRate(plot);      // 쌓인 것은 지킨다
        Say($"농장 {index + 1}번 칸의 {FarmKinds[plot.Kind]}이(가) 랭크 {plot.Rank}이(가) 되었다.");
    }

    /// <summary>쌓인 것을 창고에 싣는다 — 창고가 모자라면 실을 수 있는 만큼만.</summary>
    public void Harvest()
    {
        if (!AtFarm || !FarmOwned) return;
        int total = 0;
        foreach (var plot in Farm)
        {
            if (FarmProduct(plot) is not { } good) continue;
            int take = Math.Min(FarmReady(plot), HoldFree);
            if (take <= 0) continue;
            GiveGood(good, take);
            plot.Since += (double)take / FarmRate(plot);
            plot.Since = Math.Max(plot.Since, FarmDay - (double)FarmStore(plot) / FarmRate(plot));
            total += take;
        }
        Say(total > 0 ? $"농장에서 {total}개를 거둬 실었다." : HoldFree <= 0 ? "창고가 가득 찼다." : "아직 거둘 것이 없다.");
        if (total > 0) GainExp(1, total / 5);
    }

    private double[] FarmSave() => [FarmOwned ? 1 : 0, .. Farm.SelectMany(p => new[] { p.Kind, p.Product, p.Rank, p.Since })];

    private void RestoreFarm(double[] saved)
    {
        if (saved.Length < 1 + Farm.Length * 4) return;
        FarmOwned = saved[0] != 0;
        for (int i = 0; i < Farm.Length; i++)
            (Farm[i].Kind, Farm[i].Product, Farm[i].Rank, Farm[i].Since) = ((int)saved[1 + i * 4], (int)saved[2 + i * 4], Math.Clamp((int)saved[3 + i * 4], 1, FarmMaxRank), saved[4 + i * 4]);
    }
}
