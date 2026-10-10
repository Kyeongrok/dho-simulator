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
/// 도시별 판매 목록 · 사는 값 · 그 도시에 팔 때의 값은 gvdb 의 이용자 보고(<c>market-facts.json</c>)가 있으면 그것이고(기준가는 도시들 값의 가운데 값),
/// 자료가 없는 도시의 목록(<c>markets.json</c>)과 시세가 움직이는 법(<see cref="TradeRules"/>)은 지은 것이다.
/// </summary>
internal sealed partial class Voyage
{
    public Dictionary<int, CargoItem> Cargo { get; } = new();
    public int TradeExp { get; private set; }

    private Dictionary<int, GoodData>? _goods;
    private Dictionary<int, List<CityData>>? _sources;

    public int CargoCount => Cargo.Values.Sum(c => c.Count);
    /// <summary>
    /// 창고에 든 것 — 원본은 물자(물 · 식량 · 자재 · 포탄)도 창고를 차지한다: 원본 화면의 「221/1634」가 물 52 + 식량 130 + 교역품 39 이고,
    /// 올리면 「창고 (물자:182 교역품:39)」가 뜬다(사용자의 원본 화면, 2026-10-08). 전에는 교역품만 셌다.
    /// </summary>
    public int StoresCount => (int)Math.Ceiling(Water) + (int)Math.Ceiling(Food) + SupplyCount(2);
    public int HoldUsed => StoresCount + CargoCount;
    public int HoldFree => TotalHold - HoldUsed;

    public GoodData? Good(int id)
    {
        _goods ??= Data.Goods.ToDictionary(g => g.Id);
        return _goods.GetValueOrDefault(id);
    }

    /// <summary>이 도시 교역소가 파는 품목.</summary>
    public List<GoodData> GoodsHere() =>
        (Data.Markets.Find(m => m.CityId == City.Id)?.GoodIds() ?? []).Where(id => InvestNeed(City, id) <= InvestedIn(City)).Select(Good).OfType<GoodData>().ToList();

    /// <summary>금액을 모르는 「투자 필요」 품목에 쓰는 어림(지은 값).</summary>
    public const int UnknownInvest = 200_000;

    /// <summary>그 도시에서 그 품목이 교역소에 나오려면 들여야 하는 투자액 — 그냥 나오는 것은 0. gvdb 의 값(모르는 금액은 어림).</summary>
    public long InvestNeed(CityData city, int goodId) =>
        Data.Markets.Find(m => m.CityId == city.Id) is { } market && market.RealInvest.TryGetValue(goodId, out int need) ? (need > 0 ? need : UnknownInvest) : 0;

    /// <summary>아직 투자가 모자라 안 나오는 품목들.</summary>
    public List<(GoodData Good, long Need)> LockedGoodsHere() =>
        (Data.Markets.Find(m => m.CityId == City.Id)?.GoodIds() ?? []).Where(id => InvestNeed(City, id) > InvestedIn(City))
            .Select(id => (Good(id), InvestNeed(City, id))).Where(g => g.Item1 != null).Select(g => (g.Item1!, g.Item2)).ToList();

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

    public int BuyPrice(GoodData good) => Math.Max(1, (int)(BasePrice(good) * MarketIndex(good, City) * (1 - Haggle) * (1 - Math.Min(0.2, Study("BuyCut"))) * (1 - ClassCut(good)) * (1 + TaxRate) * NewsPrice(City)));

    /// <summary>
    /// 관세 — 교역품을 살 때 값에 붙고, 팔 때 값에서 떼인다(클라이언트 표 50 의 글: 「교역품 구입，매각 시의 관세」). 클라이언트에는 「관세 증가 · 감소」라는 말(화면 글 9542 · 9543)만 있고 세율은 없다.
    /// gvdb 의 판매 자료에서 본 것(2026-10-08): 같은 도시 · 같은 품목의 사는 값이 동맹항일 때보다 아닐 때 1.14배다(둘 다 적힌 396줄 가운데 262줄 — 나머지는 1.06 ~ 1.12). 아직 세율에는 안 썼다 —
    /// 기준가(gvdb 의 보통 값)가 이미 그 14% 를 품고 있어, 맞추려면 제 나라 항구에서 깎는 쪽으로 바꿔야 한다(사용자에게 물을 것).
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
        // 그 도시에 팔았을 때의 실제 값(gvdb 의 보고)이 있으면 그 값에 시세 · 흥정 · 관세만 얹는다
        if (Data.BuyPrices.TryGetValue((city.Id, good.Id), out int paid))
            return Math.Max(1, (int)(paid * MarketIndex(good, city) * (1 + Haggle) * (1 - TaxAt(city)) * NewsPrice(city)));
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

    /// <summary>
    /// 품목마다 다른 진열량의 배수 — 원본은 싼 것(곡물 · 가축 따위)은 많이, 비싼 것(귀금속 · 보석 · 대포)은 조금 판다(사용자, 2026-10-07).
    /// 품목별 실제 수량 자료가 없어 기준값으로 어림했다(지은 값): 100 두캇 아래 ×4, 300 아래 ×2.5, 800 아래 ×1.5, 2,000 아래 ×1, 그 위 ×0.6.
    /// </summary>
    public double StockScale(GoodData good) => BasePrice(good) switch { < 100 => 4, < 300 => 2.5, < 800 => 1.5, < 2000 => 1, _ => 0.6 };

    /// <summary>교역 창의 수량 단추 셋 — 많이 살 수 있는 품목은 단위가 크다.</summary>
    public int[] BuySteps(GoodData good) => StockScale(good) switch { >= 4 => [10, 50, 100], >= 2.5 => [5, 20, 50], _ => [1, 10, 50] };

    /// <summary>지금 살 수 있는 수 — 「○○ 거래」 스킬이 진열량을 늘린다. 며칠 지나면 다시 찬다.</summary>
    public int Stock(GoodData good)
    {
        double more = Data.SkillRules.Where(r => r.Effect == "TradeKind" && r.Targets.Contains(good.Kind)).Sum(r => Rank(r.SkillId) * r.PerRank);
        int stock = (int)(Settings.Trade.Stock * StockScale(good) * (1 + more));
        int full = stock;
        if (_bought.TryGetValue((City.Id, good.Id), out var bought) && Today - bought.Day < Settings.Trade.RestockDays) stock -= bought.Bought;
        // 걸어 둔 구입 발주서 한 장마다 진열량이 한 번 더 찬다(거래할 때 쓰인다)
        return Math.Max(0, stock) + SheetsMarked(good.Kind) * full;
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
        Studied("Buy", 1, 0, good.Kind);
    }

    /// <summary>적재화물파기 — 그 교역품을 바다에 버린다.</summary>
    public void DumpGood(GoodData good)
    {
        if (!Cargo.Remove(good.Id, out var item)) return;
        Say($"{good.Name} {item.Count}개를 버렸다.");
    }

    /// <summary>combo = 이번 거래의 명산품 콤보 수(<see cref="SpecialtyCombo"/>) — 여러 품목을 한 번에 팔 때 화면이 세어 넘긴다.</summary>
    public void SellGood(GoodData good, int count, int combo = 1)
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
            // 교역 경험 — 사용자가 준 원본 식(2026-10-10): 순이익 경험치 A = [{P + (T_Lv + 1)} / {100 × (T_Lv + 1)}] × M
            // (P 순이익 · T_Lv 교역 레벨 · M 은 순이익 1만 아래 1, 10만 아래 2, 그 위 4), 총 경험치 = (A + B) × {1 + 0.05 × (콤보 − 1)},
            // 상인 직업이 아니면 절반(한 번에 오르는 레벨의 상한은 GainExp 가 건다). 명산 경험치 B(개수 × 명산거리 × 50 / (T_Lv + 50), 품목당 500 까지)는
            // 명산거리 표가 없어 아직 0 이다.
            int level = LevelOf(TradeExp).Level;
            long exp = (profit + level + 1) / (100L * (level + 1)) * (profit < 10_000 ? 1 : profit < 100_000 ? 2 : 4);
            double total = exp * (1 + 0.05 * (Math.Max(1, combo) - 1));
            if (Data.Jobs.Find(j => j.Id == JobId)?.Group != 1) total /= 2;
            // 교역 명성은 이익 2000 에 1, 명산품을 그것이 나는 문화권 밖에서 팔면 반 더(둘 다 지은 값)
            GainExp(1, (int)Math.Min(int.MaxValue / 4, total), (int)Math.Min(1000, profit / 2000 * SpecialtyBonus(good)));
            TrainEffect("Haggle", Math.Min(60, profit / 50.0));
            // 연구 과제(표 64): 흑자 교역 = 1회 교역으로 10만 두캇 이상 · 고수익 교역 = 100만 이상 · 일확천금 교역 = 1000만 이상
            if (profit >= 100_000) Studied("Profit");
            if (profit >= 1_000_000) Studied("HighProfit");
            if (profit >= 10_000_000) Studied("BigProfit");
        }
        Say($"{good.Name} {count}개를 팔았다. ({(profit >= 0 ? "이익" : "손해")} {Math.Abs(profit):N0})");
    }

    /// <summary>그 교역품이 명산품이면 나는 문화권의 이름 — 아니면 빈 글.</summary>
    public string SpecialtyOf(GoodData good) => Data.Specialties.TryGetValue(good.Id, out int culture) ? CultureOf(culture) : "";
    private string CultureOf(int culture) => Data.Cultures.Find(c => c.Id == culture)?.Name ?? "";
    /// <summary>명산품 콤보 수 — 한 번에 파는 품목 가운데 50개 이상 파는 명산품의 가짓수(적어도 1).</summary>
    public int SpecialtyCombo(IEnumerable<(int Id, int Count)> sold) =>
        Math.Max(1, sold.Count(s => s.Count >= 50 && Data.Specialties.TryGetValue(s.Id, out int culture) && City.Culture != culture));
    private double SpecialtyBonus(GoodData good) => Data.Specialties.TryGetValue(good.Id, out int culture) && City.Culture != culture ? 1.5 : 1;

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
