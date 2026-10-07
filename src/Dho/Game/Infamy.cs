using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 악명과 타국 적대도 — 남의 상선 · 군함에 싸움을 걸면 오르고, 날이 가면 내린다.
/// 낱말(「악명」 화면 글 324, 「타국 적대도」 2311)과 내려갈 때의 글(3120 「악명이 %d 떨어졌습니다.」 · 3121 「%s 적대도가 낮아졌습니다」)은 원본의 것이다.
/// 얼마나 오르고 내리는가, 무엇을 부르는가는 클라이언트에 없다 — 모두 지은 값:
/// 상선에 싸움을 걸면 악명 +10 · 그 나라 적대도 +10, 군함이면 +20 · +20. 날마다 악명 −1, 적대도 −1. 해적을 물리치면 악명 −3.
/// 적대도가 50 이상인 나라의 군함은 이쪽을 보면 덤벼든다.
/// </summary>
internal sealed partial class Voyage
{
    public int Infamy { get; private set; }
    /// <summary>나라마다의 적대도.</summary>
    public Dictionary<int, int> Hostility { get; } = [];

    public const int HostileFrom = 50;
    private int _infamyDay = -1;

    public bool Hostile(int nation) => Hostility.GetValueOrDefault(nation) >= HostileFrom;

    // 이쪽에서 싸움을 건 때 — 해적 · 괴물은 빼고
    private void Offend(SeaShip foe)
    {
        if (foe.Kind is not (0 or 2) || foe.Monster > 0) return;
        int much = foe.Kind == 2 ? 20 : 10;
        Infamy += much;
        Hostility[foe.NationId] = Hostility.GetValueOrDefault(foe.NationId) + much;
        string nation = Data.Nations.Find(n => n.Id == foe.NationId)?.Name ?? "";
        Say($"악명 +{much} ({Infamy}) · {nation} 적대도 +{much} ({Hostility[foe.NationId]})" + (Hostility[foe.NationId] >= HostileFrom && Hostility[foe.NationId] - much < HostileFrom ? $" — 이제 {nation}의 군함이 덤벼든다." : ""));
    }

    // 해적을 물리치면 악명이 조금 씻긴다
    private void Atone(int much)
    {
        if (Infamy <= 0) return;
        much = Math.Min(much, Infamy);
        Infamy -= much;
        Say(Fill(Text(3120, "악명이 %d 떨어졌습니다."), $"{much}"));
    }

    // 날이 바뀔 때마다 한 번 — 바다에서든 항구에서든
    private void UpdateInfamy()
    {
        int today = (int)Today;
        if (_infamyDay < 0) _infamyDay = today;
        int days = today - _infamyDay;
        if (days <= 0) return;
        _infamyDay = today;
        Infamy = Math.Max(0, Infamy - days);
        foreach (int nation in Hostility.Keys.ToList())
        {
            int before = Hostility[nation], after = Math.Max(0, before - days);
            if (after == 0) Hostility.Remove(nation); else Hostility[nation] = after;
            // 덤벼드는 문턱 아래로 내려갈 때만 알린다(원본 글 3121)
            if (before >= HostileFrom && after < HostileFrom)
                Say(Fill(Text(3121, "%s 적대도가 낮아졌습니다"), Data.Nations.Find(n => n.Id == nation)?.Name ?? ""));
        }
    }

    /// <summary>
    /// 뇌물 — 원본의 글(화면 글 49134): 「타국에 뇌물을 전달합니다. 뇌물을 전달하면 대상 국가와의 적대도가 내려가지만 악명도 상승합니다.」
    /// 어디서 얼마를 내는지는 클라이언트에 없다 — 지은 값: 그 나라의 본거지에서, 적대도 1마다 5,000 두캇(한 번에 30까지), 악명 +5.
    /// </summary>
    public (int Nation, string Name, int Drop, int Cost)? BribeHere()
    {
        if (Mode != Mode.Port || City.Kind != 0 || Hostility.GetValueOrDefault(City.Nation) <= 0) return null;
        int drop = Math.Min(30, Hostility[City.Nation]);
        // 「사교」(설명: 「교섭 실패의 영향을 경감한다」)가 뇌물 값을 랭크마다 3% 깎는다(지은 값)
        return (City.Nation, Data.Nations.Find(n => n.Id == City.Nation)?.Name ?? "", drop, (int)(drop * 5000 * (1 - Social)));
    }

    public void Bribe()
    {
        if (BribeHere() is not { } deal || Money < deal.Cost) return;
        Money -= deal.Cost;
        int left = Hostility[deal.Nation] - deal.Drop;
        if (left <= 0) Hostility.Remove(deal.Nation); else Hostility[deal.Nation] = left;
        Infamy += 5;
        Cues.Enqueue("Buy");
        TrainEffect("Social", 20);
        Say($"{Fill(Text(3121, "%s 적대도가 낮아졌습니다"), deal.Name)} ({left}) — 뇌물 {deal.Cost:N0} 두캇, 악명 +5 ({Infamy})");
    }

    /// <summary>대본용: 어느 나라의 적대도를 정한다.</summary>
    public void SetHostilityForTest(int nation, int value) => (Hostility[nation], Infamy) = (value, Math.Max(Infamy, value));

    /// <summary>적대도가 있는 나라들 — (이름, 값, 군함이 덤비는가).</summary>
    public List<(string Name, int Value, bool Hunts)> HostilityList() =>
        Hostility.OrderByDescending(h => h.Value).Select(h => (Data.Nations.Find(n => n.Id == h.Key)?.Name ?? $"나라 {h.Key}", h.Value, h.Value >= HostileFrom)).ToList();
}
