using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 칙명 — 제 나라 본거지의 왕궁에서 받는다. 해내면 보수와 공적을 받고, 공적이 차면 작위가 오른다.
/// 칙명의 내용과 보수는 지은 것이다(<c>orders.json</c>). 작위 이름은 원본의 것이다.
/// </summary>
internal sealed partial class Voyage
{
    public int Title { get; private set; }
    public int Merit { get; private set; }
    public OrderData? Order { get; private set; }
    public int OrderProgress { get; private set; }

    public double Social => Math.Min(0.45, Bonus("Social"));

    public string TitleName => Data.Orders.Titles.ElementAtOrDefault(Title) ?? $"작위 {Title}";
    public int MeritToNext => Data.Orders.MeritPerTitle * (Title + 1);

    /// <summary>왕궁에 들 수 있는가 — 제 나라의 본거지.</summary>
    public bool AtCourt => Mode == Mode.Port && City.Kind == 0 && (NationId == 0 || City.Nation == NationId);

    /// <summary>지금 내려 줄 칙명들.</summary>
    public List<OrderData> OrdersOffered() => Data.Orders.Orders.Where(o => o.MinTitle <= Title).ToList();

    public string OrderGoal(OrderData order) => order.Kind switch
    {
        "Visit" => "다른 나라의 본거지에 입항한다",
        "Far" => "다른 큰 바다의 항구에 입항한다",
        "Bring" => $"{Data.GoodKinds.Find(k => k.Id == order.Target)?.Name ?? "교역품"} {order.Count}개를 싣고 온다",
        "Discover" => $"발견물 {order.Count}개를 조합에 보고한다",
        _ => "",
    };

    public void AcceptOrder(OrderData order)
    {
        if (!AtCourt || Order != null) return;
        (Order, OrderProgress) = (order, 0);
        Say($"칙명 「{order.Title}」을(를) 받들었다. — {OrderGoal(order)}.");
    }

    public void AbandonOrder()
    {
        if (Order == null) return;
        Say($"칙명 「{Order.Title}」을(를) 반납했다.");
        Order = null;
    }

    /// <summary>싣고 있는 그 갈래 교역품의 수.</summary>
    private int Carried(int kind) => Cargo.Where(c => Good(c.Key)?.Kind == kind).Sum(c => c.Value.Count);

    /// <summary>칙명을 다 해냈는가.</summary>
    public bool OrderDone => Order is { } order && (order.Kind == "Bring" ? Carried(order.Target) >= order.Count : OrderProgress >= order.Count);

    public string OrderState => Order is not { } order ? "" : order.Kind == "Bring"
        ? $"{Math.Min(Carried(order.Target), order.Count)} / {order.Count}"
        : $"{Math.Min(OrderProgress, order.Count)} / {order.Count}";

    /// <summary>입항했을 때 — 들르는 칙명의 진행.</summary>
    private void OrderOnArrive()
    {
        if (Order is not { } order || OrderProgress >= order.Count) return;
        bool foreign = City.Kind == 0 && City.Nation != 0 && City.Nation != NationId;
        bool far = Data.Cities.Find(c => c.Kind == 0 && c.Nation == NationId) is { } home &&
                   Data.Seas.Find(s => s.Id == Zones.ZoneAt(City.SeaX, City.SeaY))?.Group != Data.Seas.Find(s => s.Id == Zones.ZoneAt(home.SeaX, home.SeaY))?.Group;
        if ((order.Kind == "Visit" && foreign) || (order.Kind == "Far" && far))
        {
            OrderProgress++;
            Say($"칙명 「{order.Title}」 — {City.Name}에 닿았다. 본국으로 돌아가 아뢰자.");
        }
    }

    /// <summary>발견을 보고했을 때.</summary>
    private void OrderOnReport()
    {
        if (Order is { Kind: "Discover" }) OrderProgress++;
    }

    /// <summary>왕궁에 아뢴다 — 보수와 공적, 작위.</summary>
    public void CompleteOrder()
    {
        if (!AtCourt || Order is not { } order || !OrderDone) return;
        if (order.Kind == "Bring")
        {
            int left = order.Count;
            foreach (var (id, item) in Cargo.Where(c => Good(c.Key)?.Kind == order.Target).ToList())
            {
                int take = Math.Min(left, item.Count);
                item.Cost -= item.Cost * take / Math.Max(1, item.Count);
                item.Count -= take;
                left -= take;
                if (item.Count <= 0) Cargo.Remove(id);
                if (left <= 0) break;
            }
        }
        Money += order.Reward;
        // 「사교」(설명: 「높은 신분의 사람과 이야기 나누기 쉬워진다」) — 칙명의 공적이 랭크마다 3% 더 붙는다(45%까지, 지은 값)
        int merit = (int)Math.Round(order.Merit * (1 + Social));
        Merit += merit;
        TrainEffect("Social", 30);
        Say($"칙명 「{order.Title}」을(를) 완수했다. 하사금 {order.Reward:N0} 두캇, 공적 {merit}.");
        Order = null;
        if (TitleDue) Say("공적이 찼다 — 「작위를 받는다」로 작위를 받자.");
    }
}
