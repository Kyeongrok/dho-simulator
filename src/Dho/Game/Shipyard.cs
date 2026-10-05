using Dho.Data;

namespace Dho.Game;

/// <summary>부두에 매어 둔 배.</summary>
internal sealed class DockedShip
{
    public required ShipData Ship { get; init; }
    public double Durability { get; set; }
    /// <summary>그 배에 달려 있는 부품.</summary>
    public List<ShipPart> Parts { get; init; } = [];
    /// <summary>커스텀설정 조선으로 지은 배면 그 재질 번호와 적재 변경(%). 조선소에서 산 배는 0, 0.</summary>
    public int Material { get; init; }
    public int Load { get; init; }
    /// <summary>그 배의 강화와 옵션 스킬.</summary>
    public ShipWork Work { get; init; } = new();
    /// <summary>그 배의 돛 무늬와 색 — 돛 도료는 배에 칠하는 것이라 배를 따라간다.</summary>
    public int SailPattern { get; init; }
    public int SailTint { get; init; }
}

/// <summary>조선소에 맡겨 둔 배.</summary>
internal sealed class ShipOrder
{
    public required ShipData Ship { get; init; }
    public int Material { get; init; }
    public int Load { get; init; }
    /// <summary>다 지어질 때까지 남은 날(바다에서 보낸 날로 센다).</summary>
    public double DaysLeft { get; set; }
}

/// <summary>
/// 조선소 — 배를 산다. 타던 배는 부두에 남고, 항구의 선박교환에서 갈아타거나 판다. 배의 이름·크기·모형은 클라이언트 표 28 의 것이고,
/// 내구·창고·선원·속도·값은 크기에서 지어 낸 값이다(<see cref="ShipStats"/>).
/// </summary>
internal sealed partial class Voyage
{
    public ShipData Ship { get; private set; } = new();
    public ShipStats Stats { get; private set; } = null!;
    /// <summary>타고 있는 배의 재질 번호와 적재 변경(%).</summary>
    public int ShipMaterialId { get; private set; }
    public int ShipLoad { get; private set; }
    /// <summary>조선소에 맡겨 둔 배.</summary>
    public ShipOrder? Ordered { get; private set; }

    public ShipMaterial? MaterialOf(int id) => Data.ShipMaterials.Find(m => m.Id == id);

    /// <summary>그 배의 능력치 — 재질과 적재 변경을 입힌 것.</summary>
    public ShipStats StatsOf(ShipData ship, int material, int load)
    {
        var stats = ShipStats.Of(ship, Settings.Ships);
        return material == 0 && load == 0 ? stats : stats.Built(MaterialOf(material), load, Settings.Ships);
    }

    public ShipStats StatsOf(DockedShip docked) => Worked(StatsOf(docked.Ship, docked.Material, docked.Load), docked.Work, docked.Ship);

    // ── 커스텀설정 조선 ──────────────────────────────────────────────────────

    /// <summary>조선 스킬의 랭크(스킬 규칙의 Shipbuilding).</summary>
    public int ShipbuildingRank => Data.SkillRules.Where(r => r.Effect == "Shipbuilding").Select(r => Rank(r.SkillId)).DefaultIfEmpty(0).Max();

    /// <summary>적재를 바꿀 수 있는 조선 랭크(원본은 20 — 이 게임의 랭크 상한에 맞춰 줄였다).</summary>
    public const int LoadRank = 5;

    /// <summary>지금 랭크로 고를 수 있는 재질.</summary>
    /// 나무 여섯 가지(1 ~ 6)는 조선 랭크로 열리고, 그 밖의 재질(나라별 · 제독 재료 · 특수 도장 …)은 **그 재질 아이템을 가지고 있어야** 고른다.
    public List<ShipMaterial> MaterialsToUse() =>
        Data.ShipMaterials.Where(m => IsSpecial(m.Id) ? Items.GetValueOrDefault(MaterialItem + m.Id) > 0 : m.MinRank <= ShipbuildingRank).ToList();

    /// <summary>재질 아이템의 번호 — 여기에 재질 번호를 더한다. 건조를 맡길 때 하나가 든다.</summary>
    public const int MaterialItem = 9_200_000;
    public static bool IsSpecial(int material) => material > 6;
    public IEnumerable<ShipMaterial> SpecialMaterials() => Data.ShipMaterials.Where(m => IsSpecial(m.Id));

    /// <summary>건조일수 — 화면 글에 칸만 있고 값은 없어서 크기 등급에서 짓는다.</summary>
    public static int BuildDays(ShipData ship) => 3 + ship.SizeClass * 4;

    /// <summary>맡기는 값 — 조선소에서 사는 것보다 싸다.</summary>
    public int BuildCost(ShipData ship, int material) => (int)(ShipStats.Of(ship, Settings.Ships).Price * (MaterialOf(material)?.Price ?? 1) * 0.8);

    public string? BuildBlocker(ShipData ship, int material, int load)
    {
        if (ShipbuildingRank <= 0) return "조선 스킬이 없다";
        if (Ordered != null) return "이미 맡겨 둔 배가 있다";
        if (ship.SizeClass * 2 > ShipbuildingRank) return $"조선 랭크 {ship.SizeClass * 2} 이 있어야 이 크기를 짓는다";
        if (load != 0 && ShipbuildingRank < LoadRank) return $"적재 변경은 조선 랭크 {LoadRank} 부터";
        if (Money < BuildCost(ship, material)) return "돈이 모자라다";
        if (IsSpecial(material) && Items.GetValueOrDefault(MaterialItem + material) <= 0) return "그 재질 아이템이 없다";
        return null;
    }

    /// <summary>건조를 맡긴다. 날이 차면 어느 조선소에서나 받는다.</summary>
    public void OrderShip(ShipData ship, int material, int load)
    {
        load = Math.Clamp(load, -25, 25);
        if (Mode != Mode.Port || BuildBlocker(ship, material, load) != null) return;
        Money -= BuildCost(ship, material);
        if (IsSpecial(material) && --Items[MaterialItem + material] <= 0) Items.Remove(MaterialItem + material);
        Ordered = new ShipOrder { Ship = ship, Material = material, Load = load, DaysLeft = BuildDays(ship) };
        TrainEffect("Shipbuilding", 40 + ship.SizeClass * 30);
        Studied("Build");
        Say($"{ship.Name}의 건조를 맡겼다. 재질 {MaterialOf(material)?.Name}, 건조일수 {BuildDays(ship)}일.");
    }

    public string? ReceiveBlocker => Ordered == null ? "맡겨 둔 배가 없다" : Ordered.DaysLeft > 0 ? $"{Math.Ceiling(Ordered.DaysLeft):0}일 더 걸린다" : Dock.Count >= DockSlots ? "부두에 둘 자리가 없다" : null;

    /// <summary>다 지어진 배를 받아 부두에 둔다.</summary>
    public void ReceiveShip()
    {
        if (Mode != Mode.Port || ReceiveBlocker != null || Ordered is not { } order) return;
        var stats = StatsOf(order.Ship, order.Material, order.Load);
        Dock.Add(new DockedShip { Ship = order.Ship, Durability = stats.Durability, Material = order.Material, Load = order.Load });
        Ordered = null;
        Say($"{order.Ship.Name}을(를) 넘겨받아 부두에 매어 두었다. 선박교환에서 갈아탄다.");
    }

    /// <summary>항구에서 하루를 보낸다 — 맡긴 배의 건조가 하루 나아가고, 교역소의 재고와 시세도 하루만큼 흐른다.</summary>
    public void PassDay()
    {
        if (Mode != Mode.Port) return;
        Clock += Settings.SecondsPerDay;
        UpdateBuild(1);
        Say("하루가 지났다." + (Ordered is { DaysLeft: > 0 } order ? $" (건조 {Math.Ceiling(order.DaysLeft):0}일 남음)" : ""));
    }

    /// <summary>바다에서 보낸 날만큼 건조가 나아간다.</summary>
    private void UpdateBuild(double days)
    {
        if (Ordered is not { DaysLeft: > 0 } order) return;
        order.DaysLeft -= days;
        if (order.DaysLeft <= 0) Say($"맡겨 둔 {order.Ship.Name}이(가) 다 지어졌을 것이다. 조선소에서 받는다.");
    }

    private void StartShip(int shipId)
    {
        Ship = Data.Ships.Find(s => s.Id == shipId) ?? Data.Ships.FirstOrDefault() ?? new ShipData { Name = "배", Length = 60, Width = 20, Height = 30, Masts = 2 };
        (ShipMaterialId, ShipLoad) = (0, 0);
        Work = new ShipWork();
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
    public const int DockSlots = 40;

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

    private void Board(ShipData ship, double durability, List<ShipPart>? parts = null, int material = 0, int load = 0, ShipWork? work = null, int sailPattern = 0, int sailTint = 0)
    {
        Dock.Add(new DockedShip { Ship = Ship, Durability = Durability, Parts = Parts, Material = ShipMaterialId, Load = ShipLoad, Work = Work, SailPattern = SailPattern, SailTint = SailTint });
        ShowSail(sailPattern, sailTint);            // 새로 타는 배의 돛(새 배는 민무늬)
        Work = work ?? new ShipWork();
        Parts = parts ?? [];
        Ship = ship;
        (ShipMaterialId, ShipLoad) = (material, load);
        Stats = Worked(StatsOf(ship, material, load), Work, ship);
        Durability = Math.Clamp(durability, 1, Stats.Durability);
        Crew = Math.Min(Crew, Stats.MaxCrew);
    }

    // 원본의 커스텀설정 조선은 **타고 있는 배는 건드리지 못하고 부두의 배를 강화**한다. 강화 셈은 타고 있는 배에 걸려 있어서,
    // 강화하는 동안만 그 배로 조용히 바꿔 탔다가(선원은 그대로) 창을 닫으면 돌아온다.
    private DockedShip? _workHome;
    private double _workCrew;
    public bool Working => _workHome != null;

    public void BeginWork(DockedShip docked)
    {
        if (Mode != Mode.Port || Working || !Dock.Remove(docked)) return;
        _workCrew = Crew;
        Board(docked.Ship, docked.Durability, docked.Parts, docked.Material, docked.Load, docked.Work, docked.SailPattern, docked.SailTint);
        _workHome = Dock[^1];
    }

    public void EndWork()
    {
        if (_workHome is not { } home) return;
        _workHome = null;
        if (!Dock.Remove(home)) return;
        Board(home.Ship, home.Durability, home.Parts, home.Material, home.Load, home.Work, home.SailPattern, home.SailTint);
        Crew = _workCrew;
    }

    /// <summary>부두의 배로 갈아타지 못하는 까닭.</summary>
    public string? SwapBlocker(DockedShip docked) =>
        CargoCount > StatsOf(docked).Hold ? "짐이 그 배의 창고보다 많다" : null;

    /// <summary>선박교환 — 부두의 배로 갈아탄다. 타던 배가 부두에 남는다.</summary>
    public void SwapShip(DockedShip docked)
    {
        if (Mode != Mode.Port || !Dock.Remove(docked)) return;
        if (SwapBlocker(docked) != null) { Dock.Add(docked); return; }
        Say($"{Ship.Name}에서 {docked.Ship.Name}(으)로 갈아탔다.");
        Board(docked.Ship, docked.Durability, docked.Parts, docked.Material, docked.Load, docked.Work, docked.SailPattern, docked.SailTint);
    }

    /// <summary>부두의 배를 팔 때 받는 값 — 상한 만큼 깎인다.</summary>
    public int DockedPrice(DockedShip docked)
    {
        var stats = StatsOf(docked);
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
