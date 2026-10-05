using Dho.Data;

namespace Dho.Game;

/// <summary>부두에 매어 둔 배.</summary>
internal sealed class DockedShip
{
    public required ShipData Ship { get; init; }
    public double Durability { get; set; }
    /// <summary>그 배에 달려 있는 부품.</summary>
    public List<ShipPart> Parts { get; init; } = [];
}

/// <summary>
/// 조선소 — 배를 산다. 타던 배는 부두에 남고, 항구의 선박교환에서 갈아타거나 판다. 배의 이름·크기·모형은 클라이언트 표 28 의 것이고,
/// 내구·창고·선원·속도·값은 크기에서 지어 낸 값이다(<see cref="ShipStats"/>).
/// </summary>
internal sealed partial class Voyage
{
    public ShipData Ship { get; private set; } = new();
    public ShipStats Stats { get; private set; } = null!;

    private void StartShip(int shipId)
    {
        Ship = Data.Ships.Find(s => s.Id == shipId) ?? Data.Ships.FirstOrDefault() ?? new ShipData { Name = "배", Length = 60, Width = 20, Height = 30, Masts = 2 };
        Stats = ShipStats.Of(Ship, Settings.Ships);
    }

    /// <summary>이 도시의 조선소가 파는 배 — 도시가 클수록 큰 배까지 판다.</summary>
    public List<ShipData> ShipsForSale()
    {
        var rules = Settings.Ships;
        int maxClass = City.Kind switch { 0 => rules.CapitalMaxClass, 1 => rules.TerritoryMaxClass, _ => rules.OtherMaxClass };
        return Data.Ships.Where(s => s.Kind == 0 && s.SizeClass <= maxClass && s.Id != Ship.Id)
            .GroupBy(s => s.Model).Select(g => g.First())       // 같은 모형은 하나만
            .OrderBy(s => ShipStats.Of(s, rules).Price).ToList();
    }

    /// <summary>부두에 둘 수 있는 배의 수(타고 있는 배는 빼고).</summary>
    public const int DockSlots = 4;

    /// <summary>타고 있지 않은 내 배들. 어느 항구에서나 불러낸다(원본은 맡긴 항구에 있다 — 줄였다).</summary>
    public List<DockedShip> Dock { get; } = [];

    public int ShipCost(ShipData ship) => ShipStats.Of(ship, Settings.Ships).Price;

    public string? ShipBlocker(ShipData ship)
    {
        var stats = ShipStats.Of(ship, Settings.Ships);
        if (Money < ShipCost(ship)) return "돈이 모자라다";
        if (Dock.Count >= DockSlots) return "부두에 둘 자리가 없다";
        if (CargoCount > stats.Hold) return "짐이 새 배의 창고보다 많다";
        return null;
    }

    private void Board(ShipData ship, double durability, List<ShipPart>? parts = null)
    {
        Dock.Add(new DockedShip { Ship = Ship, Durability = Durability, Parts = Parts });
        Parts = parts ?? [];
        Ship = ship;
        Stats = ShipStats.Of(ship, Settings.Ships);
        Durability = Math.Clamp(durability, 1, Stats.Durability);
        Crew = Math.Min(Crew, Stats.MaxCrew);
    }

    /// <summary>부두의 배로 갈아타지 못하는 까닭.</summary>
    public string? SwapBlocker(DockedShip docked) =>
        CargoCount > ShipStats.Of(docked.Ship, Settings.Ships).Hold ? "짐이 그 배의 창고보다 많다" : null;

    /// <summary>선박교환 — 부두의 배로 갈아탄다. 타던 배가 부두에 남는다.</summary>
    public void SwapShip(DockedShip docked)
    {
        if (Mode != Mode.Port || !Dock.Remove(docked)) return;
        if (SwapBlocker(docked) != null) { Dock.Add(docked); return; }
        Say($"{Ship.Name}에서 {docked.Ship.Name}(으)로 갈아탔다.");
        Board(docked.Ship, docked.Durability, docked.Parts);
    }

    /// <summary>부두의 배를 팔 때 받는 값 — 상한 만큼 깎인다.</summary>
    public int DockedPrice(DockedShip docked)
    {
        var stats = ShipStats.Of(docked.Ship, Settings.Ships);
        return (int)(stats.SellPrice * Math.Clamp(docked.Durability / stats.Durability, 0.2, 1));
    }

    public void SellDocked(DockedShip docked)
    {
        if (Mode != Mode.Port || !Dock.Remove(docked)) return;
        Money += DockedPrice(docked);
        Say($"{docked.Ship.Name}을(를) 팔았다. ({DockedPrice(docked):N0} 두캇)");
    }

    public void BuyShip(ShipData ship)
    {
        if (Mode != Mode.Port || ShipBlocker(ship) != null) return;
        Money -= ShipCost(ship);
        Say($"{ship.Name}을(를) 샀다. 타던 {Ship.Name}은(는) 부두에 매어 두었다.");
        Board(ship, double.MaxValue);
    }
}
