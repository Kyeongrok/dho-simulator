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
    public int HoldFree => Stats.Hold - CargoCount;

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
    private double Haggle => Math.Min(Settings.Trade.MaxHaggle, Bonus("Haggle"));

    public int BuyPrice(GoodData good) => Math.Max(1, (int)(BasePrice(good) * MarketIndex(good, City) * (1 - Haggle)));

    /// <summary>여기서 팔 때의 단가 — 산지에서 멀수록, 지방이 다를수록 비싸다.</summary>
    public int SellPrice(GoodData good) => SellPrice(good, City);

    private int SellPrice(GoodData good, CityData city)
    {
        var rules = Settings.Trade;
        double price = BasePrice(good) * MarketIndex(good, city) * (1 + Haggle);
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
            TradeExp += (int)(profit / 100);
            TrainEffect("Haggle", Math.Min(60, profit / 50.0));
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
