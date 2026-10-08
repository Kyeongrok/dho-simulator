using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 교역 의뢰 — 상인조합에서 받아, 교역품을 그 수만큼 싣고 건네는 도시에 가서 건넨 뒤, 받은 도시의 상인조합에 보고한다.
/// 의뢰(내는 도시 · 교역품 · 수 · 건네는 도시 · 난이도)는 gvdb 의 교역 의뢰 차례 글에서 읽은 것이다(<c>tools\gvo\gvdb_tradequests.py</c> — 490건,
/// 건네는 도시는 글에서 짐작해 읽어 틀린 것이 섞여 있을 수 있다). 원본은 도시 안의 인물에게 건네지만 여기서는 그 도시에 입항해 상인조합(없으면 항구)에서 건넨다(줄인 것).
/// 지은 것: 의뢰의 이름(원본 이름은 일본어뿐이다) · 보수를 모르는 의뢰의 보수 · 한 번에 내는 수(난이도 낮은 여덟).
/// </summary>
internal sealed partial class Voyage
{
    /// <summary>맡은 교역 의뢰와 받은 도시, 건넸는가.</summary>
    public TradeQuest? TradeJob { get; private set; }
    public int TradeGiver { get; private set; }
    public bool TradeDelivered { get; private set; }
    private readonly HashSet<int> _tradeDone = [];

    /// <summary>의뢰의 이름 — 「모직 원단의 납품」(사용자, 2026-10-08: 제목은 「○○○의 납품」 꼴). 원본 이름은 일본어뿐이라 교역품 이름으로 짓는다.</summary>
    public string TradeTitle(TradeQuest quest) => $"{Good(quest.GoodId)?.Name ?? "교역품"}의 납품";

    /// <summary>의뢰의 속 — 「5개 → 튀니스」.</summary>
    public string TradeNote(TradeQuest quest) => $"{quest.Count}개 → {CityName(quest.ToCity)}";

    /// <summary>보수 — gvdb 에 적힌 것은 그대로. 모르는 것은 지은 값: 그 교역품 값(기준가) × 수 × 0.5 + 난이도 × 2,000.</summary>
    public int TradePay(TradeQuest quest) =>
        quest.Reward > 0 ? quest.Reward : (int)((Good(quest.GoodId) is { } good ? BasePrice(good) * quest.Count * 0.5 : 0) + quest.Difficulty * 2000) / 100 * 100;

    /// <summary>이 도시의 상인조합이 내는 교역 의뢰 — 아직 안 한 것 가운데 난이도 낮은 것부터 여덟(여덟은 지은 수).</summary>
    public List<TradeQuest> TradeQuestsHere() =>
        Data.TradeQuests.Where(q => q.Cities.Contains(City.Id) && !_tradeDone.Contains(q.Id) && q != TradeJob && Good(q.GoodId) != null && _cities.ContainsKey(q.ToCity))
            .OrderBy(q => _questRolls.TryGetValue(City.Id, out int roll) ? unchecked((uint)(q.Id * 2654435761u + roll * 40503u)) % 100003 : 0)      // 의뢰 알선서를 쓴 도시는 아무 여덟
            .ThenBy(q => q.Difficulty).ThenBy(q => q.Id).Take(8).OrderBy(q => q.Difficulty).ThenBy(q => q.Id).ToList();

    /// <summary>의뢰의 보상 아이템 · 교역품을 건넨다(gvdb 의 보상 칸에 적힌 것) — 교역품은 창고가 허락하는 만큼.</summary>
    private void GiveGifts(List<int[]> gifts)
    {
        foreach (var gift in gifts.Where(g => g.Length >= 3))
        {
            if (gift[0] == 0) AddItem(gift[1], gift[2]);
            else if (Good(gift[1]) is { } good && Math.Min(gift[2], HoldFree) is > 0 and var count) GiveGood(good, count);
        }
    }

    /// <summary>보상 아이템을 한 줄로 — 「의뢰 알선서 2 · 석유 12」. 없으면 빈 글.</summary>
    public string GiftText(List<int[]> gifts) =>
        string.Join(" · ", gifts.Where(g => g.Length >= 3).Select(g => $"{(g[0] == 0 ? ItemName(g[1]) : Good(g[1])?.Name ?? "교역품")} {g[2]}"));

    /// <summary>대본용 — 맡은 교역 의뢰의 교역품을 그 수만큼 싣는다.</summary>
    public void TradeLoadForTest() { if (TradeJob is { } job && Good(job.GoodId) is { } good) GiveGood(good, job.Count); }

    public void AcceptTrade(TradeQuest quest)
    {
        if (Mode != Mode.Port || TradeJob != null) return;
        (TradeJob, TradeGiver, TradeDelivered) = (quest, City.Id, false);
        Money += quest.Advance;
        Cues.Enqueue("Buy");
        Say($"교역 의뢰 「{TradeTitle(quest)}」을(를) 받았다." + (quest.Advance > 0 ? $" 선금 {quest.Advance:N0} 두캇." : ""));
    }

    /// <summary>지금 건넬 수 있는가 — 건네는 도시에 있고 그 교역품을 그 수만큼 실었다.</summary>
    public bool CanDeliverTrade => Mode == Mode.Port && TradeJob is { } job && !TradeDelivered && City.Id == job.ToCity && Cargo.TryGetValue(job.GoodId, out var held) && held.Count >= job.Count;

    public void DeliverTrade()
    {
        if (!CanDeliverTrade || TradeJob is not { } job) return;
        var held = Cargo[job.GoodId];
        held.Cost -= held.Cost * job.Count / held.Count;
        if ((held.Count -= job.Count) <= 0) Cargo.Remove(job.GoodId);
        TradeDelivered = true;
        Cues.Enqueue("Buy");
        Say($"{Good(job.GoodId)?.Name} {job.Count}개를 건넸다. {CityName(TradeGiver)}의 상인조합에 보고한다.");
        if (TradeGiver == City.Id) ReportTrade();      // 받은 도시와 건네는 도시가 같으면 그 자리에서 끝난다
    }

    public bool CanReportTrade => Mode == Mode.Port && TradeJob != null && TradeDelivered && City.Id == TradeGiver;

    public void ReportTrade()
    {
        if (!CanReportTrade || TradeJob is not { } job) return;
        int pay = TradePay(job);
        Money += pay;
        _tradeDone.Add(job.Id);
        GiveGifts(job.Gifts);
        GainExp(1, 30 + job.Difficulty * 40, 10 + job.Difficulty * 10);      // 교역 경험 · 명성 — 지은 값(원본 값은 의뢰마다 다르고 자료가 없다)
        Cues.Enqueue("Done");
        Say($"교역 의뢰 「{TradeTitle(job)}」을(를) 보고했다. 보수 {pay:N0} 두캇.");
        (TradeJob, TradeDelivered) = (null, false);
    }

    /// <summary>맡은 교역 의뢰를 그만둔다 — 선금을 돌려준다.</summary>
    public void DropTrade()
    {
        if (TradeJob is not { } job) return;
        Money -= Math.Min(Money, job.Advance);
        Say($"교역 의뢰 「{TradeTitle(job)}」을(를) 그만두었다." + (job.Advance > 0 ? " 선금을 돌려주었다." : ""));
        (TradeJob, TradeDelivered) = (null, false);
    }

    /// <summary>맡은 교역 의뢰의 지금 할 일 한 줄(의뢰 내용 창).</summary>
    public string TradeLine => TradeJob is not { } job ? ""
        : TradeDelivered ? $"「{TradeTitle(job)}」 — 물건을 건넸다. {CityName(TradeGiver)}의 상인조합에 보고한다."
        : $"「{TradeTitle(job)}」 — {Good(job.GoodId)?.Name} {job.Count}개를 싣고 {CityName(job.ToCity)}의 상인조합에 건넨다. (지금 {(Cargo.TryGetValue(job.GoodId, out var held) ? held.Count : 0)}개)";

    /// <summary>상인조합 마스터의 말.</summary>
    public string TradeGuildLine =>
        CanReportTrade ? "수고했네. 물건은 잘 건넸다고 들었네."
        : TradeJob is { } job ? (TradeDelivered ? $"「{TradeTitle(job)}」은(는) {CityName(TradeGiver)}의 상인조합에 보고하게."
            : $"「{TradeTitle(job)}」 — {Good(job.GoodId)?.Name} {job.Count}개를 싣고 {CityName(job.ToCity)}에 가서 건네게. (지금 {(Cargo.TryGetValue(job.GoodId, out var held) ? held.Count : 0)}개)")
        : TradeQuestsHere().Count == 0 ? "지금은 맡길 일이 없네. 다른 도시의 조합도 둘러보게." : "이런 납품 일이 들어와 있네. 어느 것을 맡겠나?";

    private (int Job, int Giver, bool Delivered, List<int> Done) TradeSave => (TradeJob?.Id ?? 0, TradeGiver, TradeDelivered, _tradeDone.ToList());

    private void RestoreTrade(int job, int giver, bool delivered, IEnumerable<int> done)
    {
        _tradeDone.Clear();
        foreach (int id in done) _tradeDone.Add(id);
        TradeJob = Data.TradeQuests.Find(q => q.Id == job);
        (TradeGiver, TradeDelivered) = TradeJob == null ? (0, false) : (giver, delivered);
    }

    /// <summary>대본용 — 교역 의뢰를 내는 도시 가운데 시내 지도에 상인조합 표식(장소 2)이 없는 곳을 센다.</summary>
    public void TradeGuildsForTest()
    {
        var givers = Data.TradeQuests.SelectMany(q => q.Cities.Select(c => (City: c, q.Id))).GroupBy(g => g.City).ToList();
        var none = givers.Where(g => Dho.Data.TownMap.Load(g.Key) is { Marks.Count: > 0 } map && !map.Marks.Any(m => m.Place == 2)).ToList();
        var blind = givers.Where(g => Dho.Data.TownMap.Load(g.Key) is not { Marks.Count: > 0 }).ToList();
        Say($"(시험) 교역 의뢰를 내는 도시 {givers.Count}곳 — 상인조합 표식이 없는 곳 {none.Count}곳({none.Sum(g => g.Count())}건: {string.Join(" · ", none.Take(8).Select(g => CityName(g.Key)))}) · 시내 지도가 없는 곳 {blind.Count}곳({blind.Sum(g => g.Count())}건) · 시내 장면이 없는 곳 {givers.Count(g => _cities.TryGetValue(g.Key, out var c) && c.TownScene == 0)}곳");
    }
}
