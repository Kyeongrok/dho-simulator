using Dho.Data;

namespace Dho.Game;

/// <summary>실은 교역품 한 가지 — 수와 산 값의 합.</summary>
internal sealed class CargoItem
{
    public int Count { get; set; }
    public long Cost { get; set; }
}

/// <summary>
/// 교역소 — 사서 싣고 다른 도시에서 판다. 교역품의 이름·갈래는 클라이언트 표 19 의 것이고,
/// 도시별 판매 목록(<c>markets.json</c>)과 값·시세(<see cref="TradeRules"/>)는 지은 것이다.
/// </summary>
internal sealed partial class Voyage
{
    public Dictionary<int, CargoItem> Cargo { get; } = new();
    public int TradeExp { get; private set; }

    private Dictionary<int, GoodData>? _goods;
    private Dictionary<int, List<CityData>>? _sources;

    public int CargoCount => Cargo.Values.Sum(c => c.Count);
    public int HoldFree => TotalHold - CargoCount;

    public GoodData? Good(int id)
    {
        _goods ??= Data.Goods.ToDictionary(g => g.Id);
        return _goods.GetValueOrDefault(id);
    }

    /// <summary>이 도시 교역소가 파는 품목.</summary>
    public List<GoodData> GoodsHere() =>
        (Data.Markets.Find(m => m.CityId == City.Id)?.GoodIds() ?? []).Select(Good).OfType<GoodData>().ToList();

    private bool SoldHere(int goodId) => Sells(City, goodId);
    private bool Sells(CityData city, int goodId) => Data.Markets.Find(m => m.CityId == city.Id)?.GoodIds().Contains(goodId) ?? false;

    /// <summary>품목마다 조금씩 다른 기준값 — 갈래 기준값의 0.7 ~ 1.3배.</summary>
    private double BasePrice(GoodData good)
    {
        if (Data.GoodPrices.TryGetValue(good.Id, out int real)) return real;      // 실제 판매 값(gvdb)을 아는 것은 그 값
        var prices = Settings.Trade.KindPrices;
        double kind = good.Kind >= 0 && good.Kind < prices.Count ? prices[good.Kind] : 100;
        return kind * (0.7 + 0.6 * Hash(good.Id));
    }

    /// <summary>도시와 갈래마다 따로 오르내리는 시세(1 ± Swing).</summary>
    private double MarketIndex(GoodData good, CityData city)
    {
        double day = Clock / Settings.SecondsPerDay;
        double phase = Hash(city.Id * 31 + good.Kind) * Math.Tau;
        return 1 + Settings.Trade.Swing * Math.Sin(day / Settings.Trade.SwingDays * Math.Tau + phase);
    }

    /// <summary>회계 스킬이 있으면 시세가 보이고 흥정이 된다.</summary>
    public bool CanSeeMarket => Has("Haggle");
    public int MarketPercent(GoodData good) => (int)Math.Round(MarketIndex(good, City) * 100);

    // ── 회계 — 원본 설명: 「교역소에서의 가격 흥정과 인근 도시의 시세 확인이 가능해진다」. 비율과 확률은 지은 것이다 ──

    private double _haggled;
    private int _haggleCity, _haggleDay;
    private bool _haggleShut;

    private int AccountingRank => Data.SkillRules.Find(r => r.Effect == "Haggle") is { } rule ? Rank(rule.SkillId) : 0;

    /// <summary>흥정은 그 도시에서 그날 한 것만 듣는다 — 도시를 옮기거나 날이 바뀌면 처음부터.</summary>
    private void FreshHaggle()
    {
        if (_haggleCity == City.Id && _haggleDay == (int)Today) return;
        (_haggleCity, _haggleDay, _haggled, _haggleShut) = (City.Id, (int)Today, 0, false);
    }

    /// <summary>지금까지 흥정으로 얻어 낸 비율(살 때 깎고 팔 때 올려 받는다).</summary>
    public double Haggled { get { FreshHaggle(); return _haggled; } }
    /// <summary>회계 랭크로 얻어 낼 수 있는 가장 큰 비율 — 랭크 1 에 4.2%, 랭크마다 1.2%, 설정의 상한까지.</summary>
    public double HaggleCap => Math.Min(Settings.Trade.MaxHaggle, 0.03 + AccountingRank * 0.012) + HonorEffect("Haggle");
    /// <summary>다음 흥정이 먹힐 확률 — 랭크가 높을수록 높고, 이미 깎은 만큼 낮아진다.</summary>
    public double HaggleChance => Math.Clamp(0.45 + AccountingRank * 0.04 - Haggled / Math.Max(0.001, HaggleCap) * 0.35, 0.1, 0.95);

    // 흥정 한 번에 드는 행동력
    public int HaggleVigour => Data.SkillRules.Find(r => r.Effect == "Haggle") is { } rule ? VigourCost(rule) : 10;

    public string? HaggleBlocker
    {
        get
        {
            FreshHaggle();
            return Mode != Mode.Port ? "항구에서만 한다" : !Has("Haggle") ? "회계 스킬이 없다" : !Speaks(City) ? "말이 통하지 않는다" : _haggleShut ? "주인이 더는 흥정에 응하지 않는다"
                : _haggled >= HaggleCap - 1e-9 ? "더는 깎을 수 없다" : Vigour < HaggleVigour ? $"행동력이 모자란다 ({Vigour:0}/{HaggleVigour})" : null;
        }
    }

    /// <summary>값을 흥정한다 — 먹히면 상한의 삼분의 일씩 얻어 내고, 안 먹히면 주인이 그날은 더 응하지 않는다(얻어 낸 것은 남는다).</summary>
    public void TryHaggle()
    {
        if (HaggleBlocker != null) { Cues.Enqueue("Error"); return; }
        SpendVigour(HaggleVigour);
        if (_random.NextDouble() < HaggleChance)
        {
            _haggled = Math.Min(HaggleCap, _haggled + HaggleCap / 3);
            Say($"흥정이 먹혔다. 살 때 {_haggled * 100:0.#}% 깎고, 팔 때 그만큼 더 받는다.");
            TrainEffect("Haggle", 12);
            Studied("Haggle");
            Cues.Enqueue("Skill");
        }
        else
        {
            _haggleShut = true;
            Say("교역소 주인: 「그 값으로는 안 되겠소. 오늘은 더 이야기하지 맙시다.」");
            TrainEffect("Haggle", 4);
            Cues.Enqueue("Error");
        }
    }

    private double Haggle => Mode == Mode.Port && !Speaks(City) ? 0 : Math.Min(Settings.Trade.MaxHaggle, Haggled + AideHaggle);      // 말이 안 통하면 흥정이 없다

    /// <summary>회계: 인근 도시의 시세 — 가까운 차례로 (도시, 거리, 그 품목의 시세 %, 거기서 팔 때의 값, 거기서도 파는가). 랭크가 높을수록 먼 도시까지 보인다.</summary>
    public List<(CityData City, double Distance, int Percent, int Price, bool Sells)> NearbyMarkets(GoodData good)
    {
        var found = new List<(CityData, double, int, int, bool)>();
        if (!CanSeeMarket) return found;
        double reach = Settings.Trade.NearbyReach * (1 + AccountingRank * 0.15);
        foreach (var city in Data.Cities)
        {
            if (city.Id == City.Id || (city.SeaX == 0 && city.SeaY == 0)) continue;
            double far = Distance(City, city);
            if (far <= reach) found.Add((city, far, (int)Math.Round(MarketIndex(good, city) * 100), SellPrice(good, city), Sells(city, good.Id)));
        }
        return found.OrderBy(f => f.Item2).ToList();
    }

    public int BuyPrice(GoodData good) => Math.Max(1, (int)(BasePrice(good) * MarketIndex(good, City) * (1 - Haggle) * (1 - Math.Min(0.2, Study("BuyCut"))) * (1 + TaxRate) * NewsPrice(City)));

    /// <summary>
    /// 관세 — 교역품을 살 때 값에 붙고, 팔 때 값에서 떼인다(클라이언트 표 50 의 글: 「교역품 구입，매각 시의 관세」). 클라이언트에는 「관세 증가 · 감소」라는 말(화면 글 9542 · 9543)만 있고 세율은 없다.
    /// 지은 값: 남의 나라 항구 10%, 제 나라 항구(본거지 · 영지 · 동맹항) 5%에서 작위 한 단계마다 1%p 씩 깎여 0%까지. 소속 없는 도시는 5%.
    /// </summary>
    public double TaxRate => TaxAt(City);
    private double TaxAt(CityData city) => Data.Settings.ModNoTax ? 0 : Math.Max(0, (city.Nation == 0 ? 0.05 : NationId != 0 && city.Nation == NationId ? Math.Max(0, 0.05 - Title * 0.01) : 0.10) + NewsTax(city));

    /// <summary>여기서 팔 때의 단가 — 산지에서 멀수록, 지방이 다를수록 비싸다.</summary>
    public int SellPrice(GoodData good) => SellPrice(good, City);

    private int SellPrice(GoodData good, CityData city)
    {
        var rules = Settings.Trade;
        double price = BasePrice(good) * MarketIndex(good, city) * (1 + Haggle) * (1 - TaxAt(city)) * NewsPrice(city);      // 팔 때도 관세가 떼인다
        if (Sells(city, good.Id)) return Math.Max(1, (int)(price * rules.HomeSellRate));

        _sources ??= BuildSources();
        double bonus = rules.MaxDistanceBonus;
        bool sameRegion = false;
        if (_sources.TryGetValue(good.Id, out var cities))
        {
            double nearest = cities.Min(c => Distance(city, c));
            bonus = Math.Min(rules.MaxDistanceBonus, nearest / 100 * rules.DistanceBonusPer100);
            sameRegion = cities.Any(c => c.Culture == city.Culture);
        }
        return Math.Max(1, (int)(price * (1 + bonus + (sameRegion ? 0 : rules.RegionBonus))));
    }

    private static double Distance(CityData a, CityData b) =>
        Math.Sqrt(Math.Pow(WorldMap.DeltaX(a.SeaX, b.SeaX), 2) + Math.Pow(b.SeaY - a.SeaY, 2));

    /// <summary>회계: 가까운 도시 가운데 이 품목을 가장 비싸게 사 주는 곳.</summary>
    public (CityData City, int Price)? BestNearby(GoodData good)
    {
        if (!CanSeeMarket) return null;
        (CityData, int)? best = null;
        foreach (var city in Data.Cities)
        {
            if (city.Id == City.Id || (city.SeaX == 0 && city.SeaY == 0) || Distance(City, city) > Settings.Trade.NearbyReach) continue;
            int price = SellPrice(good, city);
            if (best == null || price > best.Value.Item2) best = (city, price);
        }
        return best;
    }

    // ── 재고 ─────────────────────────────────────────────────────────────────

    private readonly Dictionary<(int City, int Good), (int Bought, double Day)> _bought = new();
    private double Today => Clock / Settings.SecondsPerDay;

    /// <summary>지금 살 수 있는 수 — 「○○ 거래」 스킬이 진열량을 늘린다. 며칠 지나면 다시 찬다.</summary>
    public int Stock(GoodData good)
    {
        double more = Data.SkillRules.Where(r => r.Effect == "TradeKind" && r.Targets.Contains(good.Kind)).Sum(r => Rank(r.SkillId) * r.PerRank);
        int stock = (int)(Settings.Trade.Stock * (1 + more));
        if (_bought.TryGetValue((City.Id, good.Id), out var bought) && Today - bought.Day < Settings.Trade.RestockDays) stock -= bought.Bought;
        return Math.Max(0, stock);
    }

    // 그 교역품을 파는 도시들(산지) — 지금 있는 곳에서 가까운 차례
    public List<CityData> SourcesOf(int goodId)
    {
        _sources ??= BuildSources();
        return _sources.TryGetValue(goodId, out var cities) ? cities.OrderBy(c => Distance(City, c)).ToList() : [];
    }

    // 교역품을 값 없이 싣는다(아이템 추가 창) — 창고에 드는 만큼만. 실은 수를 돌려준다
    public int GiveGood(GoodData good, int count)
    {
        count = Math.Min(count, HoldFree);
        if (count <= 0) { Say("창고가 가득 찼다."); Cues.Enqueue("Error"); return 0; }
        if (!Cargo.TryGetValue(good.Id, out var item)) Cargo[good.Id] = item = new CargoItem();
        item.Count += count;
        Say($"{good.Name} {count}개를 실었다.");
        return count;
    }

    private Dictionary<int, List<CityData>> BuildSources()
    {
        var sources = new Dictionary<int, List<CityData>>();
        foreach (var market in Data.Markets)
        {
            if (Data.Cities.Find(c => c.Id == market.CityId) is not { } city) continue;
            foreach (int id in market.GoodIds())
            {
                if (!sources.TryGetValue(id, out var list)) sources[id] = list = [];
                list.Add(city);
            }
        }
        return sources;
    }

    public void BuyGood(GoodData good, int count)
    {
        if (Mode != Mode.Port || !SoldHere(good.Id)) return;
        int price = BuyPrice(good);
        count = Math.Min(Math.Min(count, Stock(good)), Math.Min(HoldFree, Money / price));
        if (count <= 0) return;
        Money -= count * price;
        var key = (City.Id, good.Id);
        _bought[key] = _bought.TryGetValue(key, out var before) && Today - before.Day < Settings.Trade.RestockDays
            ? (before.Bought + count, before.Day) : (count, Today);
        foreach (var rule in Data.SkillRules.Where(r => r.Effect == "TradeKind" && r.Targets.Contains(good.Kind))) Train(rule.SkillId, count * 0.5);
        if (!Cargo.TryGetValue(good.Id, out var item)) Cargo[good.Id] = item = new CargoItem();
        item.Count += count;
        item.Cost += (long)count * price;
        Studied("Buy");
    }

    /// <summary>적재화물파기 — 그 교역품을 바다에 버린다.</summary>
    public void DumpGood(GoodData good)
    {
        if (!Cargo.Remove(good.Id, out var item)) return;
        Say($"{good.Name} {item.Count}개를 버렸다.");
    }

    public void SellGood(GoodData good, int count)
    {
        if (Mode != Mode.Port || !Cargo.TryGetValue(good.Id, out var item)) return;
        count = Math.Min(count, item.Count);
        if (count <= 0) return;
        int price = SellPrice(good);
        long cost = item.Cost * count / item.Count;
        long profit = (long)count * price - cost;
        Money += count * price;
        item.Count -= count;
        item.Cost -= cost;
        if (item.Count == 0) Cargo.Remove(good.Id);
        if (profit > 0)
        {
            GainExp(1, (int)Math.Min(100_000, profit / 100), (int)Math.Min(1000, profit / 2000));      // 교역 명성은 이익 2000 에 1(지은 값)
            TrainEffect("Haggle", Math.Min(60, profit / 50.0));
            Studied("Profit");
            if (profit >= 50_000) Studied("BigProfit");
        }
        Say($"{good.Name} {count}개를 팔았다. ({(profit >= 0 ? "이익" : "손해")} {Math.Abs(profit):N0})");
    }

    /// <summary>늘 같은 0 ~ 1 값.</summary>
    private static double Hash(int n)
    {
        unchecked
        {
            uint x = (uint)n * 2654435761u;
            x ^= x >> 15;
            x *= 2246822519u;
            x ^= x >> 13;
            return (x & 0xFFFFFF) / (double)0x1000000;
        }
    }
}
