using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 조선소 — 배를 사서 갈아탄다. 배의 이름·크기·모형은 클라이언트 표 28 의 것이고,
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

    /// <summary>지금 배를 팔고 그 값을 보탠 차액.</summary>
    public int ShipCost(ShipData ship) => ShipStats.Of(ship, Settings.Ships).Price - Stats.SellPrice;

    public string? ShipBlocker(ShipData ship)
    {
        var stats = ShipStats.Of(ship, Settings.Ships);
        if (Money < ShipCost(ship)) return "돈이 모자라다";
        if (CargoCount > stats.Hold) return "짐이 새 배의 창고보다 많다";
        return null;
    }

    public void BuyShip(ShipData ship)
    {
        if (Mode != Mode.Port || ShipBlocker(ship) != null) return;
        Money -= ShipCost(ship);
        Say($"{Ship.Name}을(를) 팔고 {ship.Name}을(를) 샀다.");
        Ship = ship;
        Stats = ShipStats.Of(ship, Settings.Ships);
        Durability = Stats.Durability;
        Crew = Math.Min(Crew, Stats.MaxCrew);
    }
}
