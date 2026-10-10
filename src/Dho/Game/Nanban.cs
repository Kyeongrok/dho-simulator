using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 남만 무역 — 동아시아(조선 · 일본 · 대만 · 화남)의 도시에서 무역 상인에게 교역품을 건네고 남만품으로 바꿔 받는다(물물교환).
/// 나라 · 도시 · 남만품 · 그 나라가 반기는 교역품과 가치, 도시가 열리는 공헌도는 나무위키 「대항해시대 온라인/남만무역」의 표(<c>data\nanban.json</c>).
/// 흐름(같은 글): 그 나라에 교역품 한 가지를 선물해 선물도 1000 을 채우면 무역 허가 → 반기는 교역품을 남만품으로 교환 → 공헌도가 쌓이면 다른 도시 · 특산 남만품이 열린다.
/// 클라이언트에는 화면 글 여덟 줄(8408 ~ 8421 · 42014)뿐이고 셈은 없다 — 지은 것: 선물 한 개의 선물도, 교환한 남만품 한 개의 공헌도,
/// 가치에서 교환비를 내는 셈(가치 ÷ ValueFull, 1 까지 × 가장 좋은 교환비 100:33). 재고 · 도시 상태(전쟁 · 가뭄 …) · 철도는 아직 없다.
/// 남만품을 유럽에서 파는 값은 교역소가 이미 gvdb 의 보고 값으로 쳐 준다.
/// </summary>
internal sealed partial class Voyage
{
    /// <summary>나라(조선 · 일본 · 대만 · 화남) → 공헌도 · 선물도 · 선물로 고른 교역품.</summary>
    public Dictionary<string, int> NanbanMerit { get; } = [];
    public Dictionary<string, int> NanbanGift { get; } = [];
    public Dictionary<string, int> NanbanGiftGood { get; } = [];

    public NanbanLand? NanbanLandHere => Mode == Mode.Port ? Data.Nanban.Lands.Find(l => l.Cities.Exists(c => c.City == City.Name)) : null;
    public NanbanCity? NanbanCityHere => NanbanLandHere?.Cities.Find(c => c.City == City.Name);
    public int NanbanMeritOf(NanbanLand land) => NanbanMerit.GetValueOrDefault(land.Name);
    public int NanbanGiftOf(NanbanLand land) => NanbanGift.GetValueOrDefault(land.Name);
    /// <summary>이 도시가 열렸는가 — 그 나라의 공헌도가 도시의 문턱에 닿았는가.</summary>
    public bool NanbanOpenHere => NanbanLandHere is { } land && NanbanCityHere is { } city && NanbanMeritOf(land) >= city.Need;
    public bool NanbanPermitted(NanbanLand land) => NanbanGiftOf(land) >= Data.Nanban.GiftGoal;

    /// <summary>선물 한 개의 선물도 — 다른 문화권의 명산품이면 더 친다(글: 「명산 거리가 멀수록 좋다」 — 크기는 지은 값).</summary>
    public int NanbanGiftWorth(GoodData good) =>
        Data.Specialties.TryGetValue(good.Id, out int culture) && City.Culture != culture ? Data.Nanban.GiftPerSpecialty : Data.Nanban.GiftPerGood;

    /// <summary>무역 허가를 얻으려 교역품을 선물한다 — 한 나라에는 한 가지 교역품만(글: 「다른 교역품과 조합 선물은 불가능」).</summary>
    public void NanbanGive(GoodData good, int count)
    {
        if (NanbanLandHere is not { } land || !NanbanOpenHere || NanbanPermitted(land) || !Cargo.TryGetValue(good.Id, out var item)) return;
        if (NanbanGiftGood.TryGetValue(land.Name, out int chosen) && chosen != good.Id && NanbanGiftOf(land) > 0)
        {
            Say($"{land.Name}에는 {Good(chosen)?.Name ?? "먼저 고른 교역품"}(으)로만 선물할 수 있다.");
            Cues.Enqueue("Error");
            return;
        }
        int worth = Math.Max(1, NanbanGiftWorth(good));
        count = Math.Min(Math.Min(count, item.Count), (Data.Nanban.GiftGoal - NanbanGiftOf(land) + worth - 1) / worth);
        if (count <= 0) return;
        item.Cost -= item.Cost * count / item.Count;
        item.Count -= count;
        if (item.Count == 0) Cargo.Remove(good.Id);
        (NanbanGiftGood[land.Name], NanbanGift[land.Name]) = (good.Id, Math.Min(Data.Nanban.GiftGoal, NanbanGiftOf(land) + count * worth));
        Say($"{good.Name} {count}개를 선물했다. ({land.Name} 선물도 {NanbanGiftOf(land)} / {Data.Nanban.GiftGoal})");
        if (NanbanPermitted(land)) { Say($"{land.Name}의 남만 무역 허가를 얻었다."); Cues.Enqueue("Quest"); }
        else Cues.Enqueue("Done");
    }

    /// <summary>싣고 있는 것 가운데 이 나라가 반기는 교역품.</summary>
    public List<(GoodData Good, NanbanWant Want, int Count)> NanbanOffers() =>
        NanbanLandHere is not { } land ? [] : [.. land.Wants
            .Select(w => (Good: Data.Goods.Find(g => g.Name == w.Name), Want: w)).Where(w => w.Good != null && Cargo.ContainsKey(w.Good.Id))
            .Select(w => (w.Good!, w.Want, Cargo[w.Good!.Id].Count))];

    /// <summary>이 도시가 내주는 남만품(공헌도가 모자란 것도 함께 — 열렸는가는 Open).</summary>
    public List<(GoodData Good, NanbanGood Rule, bool Open)> NanbanGoodsHere() =>
        NanbanLandHere is not { } land || NanbanCityHere is not { } city ? [] : [.. city.Goods
            .Select(r => (Good: Data.Goods.Find(g => g.Name == r.Name), Rule: r)).Where(r => r.Good != null)
            .Select(r => (r.Good!, r.Rule, NanbanMeritOf(land) >= r.Rule.Need))];

    /// <summary>그 교역품 count 개로 받는 남만품의 수 — 가장 좋은 교환비(100:33)에 가치를 곱한다(가치 ÷ ValueFull, 1 까지 — 지은 셈).</summary>
    public int NanbanYield(NanbanWant want, int count) =>
        (int)(count * Data.Nanban.Ratio * Math.Min(1, want.Value / (double)Math.Max(1, Data.Nanban.ValueFull)));

    public void NanbanTrade(GoodData give, int count, GoodData get)
    {
        if (NanbanLandHere is not { } land || !NanbanOpenHere || !NanbanPermitted(land)) return;
        var offers = NanbanOffers();
        int at = offers.FindIndex(o => o.Good.Id == give.Id);
        if (at < 0 || !NanbanGoodsHere().Exists(g => g.Good.Id == get.Id && g.Open)) return;
        count = Math.Min(count, offers[at].Count);
        int got = NanbanYield(offers[at].Want, count);
        if (got <= 0) { Say("그 수로는 남만품을 하나도 못 받는다."); Cues.Enqueue("Error"); return; }
        var item = Cargo[give.Id];
        // 남만품의 원가는 건넨 교역품의 값이다(글: 「남만품 단가에 교환에 쓴 교역품의 가격이 반영된다」)
        long cost = item.Cost * count / item.Count;
        item.Cost -= cost;
        item.Count -= count;
        if (item.Count == 0) Cargo.Remove(give.Id);
        if (!Cargo.TryGetValue(get.Id, out var held)) Cargo[get.Id] = held = new CargoItem();
        (held.Count, held.Cost) = (held.Count + got, held.Cost + cost);
        int before = NanbanMeritOf(land);
        NanbanMerit[land.Name] = before + got * Data.Nanban.MeritPerGood;
        Say($"{give.Name} {count}개를 {get.Name} {got}개로 바꿨다. ({land.Name} 공헌도 {NanbanMeritOf(land):N0})");
        Cues.Enqueue("Buy");
        foreach (var opened in land.Cities.Where(c => c.Need > before && c.Need <= NanbanMeritOf(land))) Say($"{land.Name} 공헌도가 올라 {opened.City}에서도 남만 무역을 할 수 있다.");
        // 남만 무역을 하다 보면 그 나라의 유력자가 부른다 — 찾아가면 동아시아 조선 기법서를 준다(이용자 글: 열 번에 한 번쯤, 한 번에 다섯 장).
        // 유력자가 누구이고 어디 있는지는 자료가 없어 부르는 그 자리에서 받는다(지은 흐름)
        if (_random.NextDouble() < Data.Nanban.CallChance && Data.Papers.Find(p => p.Name == "동아시아 조선 기법서") is { } paper)
        {
            Items[paper.Id] = Items.GetValueOrDefault(paper.Id) + Data.Nanban.CallPapers;
            Say($"{land.Name}의 유력자가 불러 {paper.Name} {Data.Nanban.CallPapers}장을 내주었다.");
            Cues.Enqueue("Quest");
        }
    }

    /// <summary>대본용 — 그 나라의 허가와 공헌도를 바로 준다.</summary>
    public void NanbanForTest(int merit)
    {
        if (NanbanLandHere is not { } land) return;
        (NanbanGift[land.Name], NanbanMerit[land.Name]) = (Data.Nanban.GiftGoal, merit);
    }
}
