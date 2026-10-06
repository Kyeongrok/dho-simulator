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
    /// <summary>특수 조선에서 재료의 조합으로 붙은 옵션 스킬 — 받을 때 배에 붙어 있다.</summary>
    public List<int> Skills { get; init; } = [];
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

    private readonly Dictionary<int, int> _hullColors = new();
    private ImageSet? _materialIcons;

    /// <summary>
    /// 재질을 따로 안 정한 배의 타고난 재질 — 「월광 ○○」은 「하얀 달」 특별 주문 도료로 지은 배다(교환권 설명: 「특수 목재 / 금속으로 만들어진 월광 …」,
    /// 배 설명: 「달빛처럼 하얀 …」). 전열함은 금속, 나머지는 목재. 없으면 0.
    /// </summary>
    public int NativeMaterial(ShipData ship) =>
        !ship.Name.StartsWith("월광") ? 0
        : Data.ShipMaterials.Find(m => m.Name.Contains("특별 주문 도료") && m.Name.Contains("하얀 달") && m.Name.Contains(ship.Name.Contains("전열함") ? "금속" : "목재"))?.Id ?? 0;

    /// <summary>그 배의 선체 빛깔 — 재질이 정해져 있으면 그것, 아니면 타고난 재질의 것.</summary>
    public int HullColorOf(ShipData ship, int material) => HullColor(material != 0 ? material : NativeMaterial(ship));

    /// <summary>
    /// 그 재질로 지은 배의 선체 빛깔(0xRRGGBB). 나라 · 의식용 재질은 그 선박재료 아이템의 그림에서 뽑는다
    /// (불투명한 밝은 점들의 평균을 밝게 올린 것) — 그림과 배의 빛깔이 맞는다. 바탕 나무(삼나무 ~ 철)는 전처럼 이름에서 지은 옅은 빛.
    /// 어두운 재질(야전용의 검정, 검녹색 · 검보라색)은 그림에서 뽑지 않는다 — 밝은 점만 모아 밝게 올리는 셈이라 검은 배가 희게 나왔다.
    /// </summary>
    public int HullColor(int material)
    {
        if (_hullColors.TryGetValue(material, out int known)) return known;
        int color = MaterialOf(material)?.Color ?? 0xFFFFFF;
        bool dark = Math.Max(color >> 16 & 255, Math.Max(color >> 8 & 255, color & 255)) < 128;
        try
        {
            if (!dark && IsSpecial(material) && Data.MaterialItems.TryGetValue(material, out int item)
                && (_materialIcons ??= new ImageSet(@"0010\0001\sb")).Pixels(22, item) is { } icon)
            {
                double r = 0, g = 0, b = 0, n = 0;
                for (int at = 0; at + 3 < icon.Bgra.Length; at += 4)
                {
                    int bb = icon.Bgra[at], gg = icon.Bgra[at + 1], rr = icon.Bgra[at + 2];
                    // 바탕(어두운 초록 칸)과 테두리는 빼고, 재료 더미의 빛깔만
                    if (icon.Bgra[at + 3] < 200 || Math.Max(rr, Math.Max(gg, bb)) < 110) continue;
                    (r, g, b, n) = (r + rr, g + gg, b + bb, n + 1);
                }
                if (n > 30)
                {
                    double top = Math.Max(r, Math.Max(g, b)) / n, lift = 235 / Math.Max(1, top);
                    color = (int)Math.Min(255, r / n * lift) << 16 | (int)Math.Min(255, g / n * lift) << 8 | (int)Math.Min(255, b / n * lift);
                }
            }
        }
        catch (Exception) { }
        return _hullColors[material] = color;
    }

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
        Data.ShipMaterials.Where(m => IsSpecial(m.Id) ? WoodItemOwned(m.Id) > 0 : m.MinRank <= ShipbuildingRank).ToList();

    /// <summary>
    /// 그 재질을 고르는 데 드는 「장비 재료」 아이템 가운데 가진 것 — 원본의 「○○ 선박재료」(조빌 아이템, 클라이언트 글 49214 「필요 장비 재료:%s」)를
    /// 먼저 보고, 없으면 전에 지어 넣은 재질 아이템을 본다. 없으면 0.
    /// </summary>
    public int WoodItemOwned(int material)
    {
        int real = Items.Keys.FirstOrDefault(id => id is >= ShipItems and < ShipItems + 100_000 && WoodOf(id)?.Id == material);
        return real > 0 ? real : Items.GetValueOrDefault(MaterialItem + material) > 0 ? MaterialItem + material : 0;
    }

    /// <summary>그 재질에 드는 원본 장비 재료의 이름(「포르투갈군 공용 선박재료」) — 아이템 표에 없으면 null.</summary>
    public string? WoodItemName(int material) =>
        Data.Papers.Find(p => p.Id is >= ShipItems and < ShipItems + 100_000 && WoodOf(p.Id)?.Id == material)?.Name;

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
        if (IsSpecial(material) && WoodItemOwned(material) == 0) return $"필요 장비 재료: {WoodItemName(material) ?? MaterialOf(material)?.Name}";
        return null;
    }

    /// <summary>건조를 맡긴다. 날이 차면 어느 조선소에서나 받는다.</summary>
    public void OrderShip(ShipData ship, int material, int load)
    {
        load = Math.Clamp(load, -25, 25);
        if (Mode != Mode.Port || BuildBlocker(ship, material, load) != null) return;
        Money -= BuildCost(ship, material);
        if (IsSpecial(material) && WoodItemOwned(material) is > 0 and var wood && --Items[wood] <= 0) Items.Remove(wood);
        Ordered = new ShipOrder { Ship = ship, Material = material, Load = load, DaysLeft = BuildDays(ship) };
        TrainEffect("Shipbuilding", 40 + ship.SizeClass * 30);
        Studied("Build");
        Say($"{ship.Name}의 건조를 맡겼다. 재질 {MaterialOf(material)?.Name}, 건조일수 {BuildDays(ship)}일.");
    }

    /// <summary>특수 조선 한 건 — 그 선체 아이템으로 그 도시에서 짓는 배.</summary>
    public sealed record HullPlan(ShipData Ship, int HullItem, string Hull, int Rank, int Material, string MaterialName, string City);

    /// <summary>아이템 표의 조선 부품(조빌 아이템) 무리 — 선체 · 돛 · 망 · 선실 · 선박재료 ….</summary>
    public const int ShipItems = 2_200_000;

    public static bool IsShipItem(int item) => item is >= ShipItems and < ShipItems + 100_000;
    public static bool IsHullName(string name) => name.Contains("선체") || name.Contains("도선");
    /// <summary>「주요 돛」으로 치는 조빌 아이템(개프세일 · 라틴세일 · 스퀘어세일 …)과 「포문」.</summary>
    public static bool IsSailName(string name) => name.Contains("세일") || name.EndsWith("돛") || name.EndsWith("사각돛");
    public static bool IsGunportName(string name) => name.Contains("포문");

    /// <summary>가진 선체 아이템들.</summary>
    public List<PaperItem> HullsOwned() =>
        Data.Papers.Where(p => IsShipItem(p.Id) && IsHullName(p.Name) && Items.GetValueOrDefault(p.Id) > 0).ToList();

    /// <summary>
    /// 그 선체로 짓는 배들(도시마다 한 줄) — 배 상세(ssjoy)의 「특수 건조 도시」에서 온다(모은 배만 있다).
    /// 선체는 아이템 표(14)의 조선 부품(2200000 ~)이다.
    /// </summary>
    public List<HullPlan> HullPlans(int hullItem)
    {
        var plans = new List<HullPlan>();
        string hull = Data.Papers.Find(p => p.Id == hullItem)?.Name ?? "";
        foreach (var detail in Data.ShipDetails)
            foreach (var special in detail.Special.Where(s => s.Hull == hull))
            {
                if (Data.Ships.Find(s => s.Name == detail.Name) is not { } ship) continue;
                var material = Data.ShipMaterials.Find(m => m.Name == special.Material) ?? Data.ShipMaterials.Find(m => m.Name == "너도밤나무") ?? Data.ShipMaterials[0];
                plans.Add(new HullPlan(ship, hullItem, special.Hull, special.Rank, material.Id, material.Name, special.City));
            }
        // 배(와 재질)마다 한 줄 — 이 도시에서 지을 수 있으면 그것, 아니면 짓는 도시들을 이어 적은 줄. 지을 수 있는 것을 앞에
        return plans.GroupBy(p => (p.Ship.Id, p.Material))
            .Select(g => g.FirstOrDefault(p => p.City == City.Name) ?? g.First() with { City = string.Join(" · ", g.Select(p => p.City).Distinct()) })
            .OrderBy(p => p.City == City.Name ? 0 : 1).ToList();
    }

    /// <summary>고른 재료들로 붙는 옵션 스킬 — 그 배의 스킬 가운데 재료가 모두 들어 있는 것(없으면 null).</summary>
    public OptionSkill? HullSkill(HullPlan plan, IReadOnlyCollection<int> materials)
    {
        var names = materials.Select(ItemName).ToHashSet();
        var granted = Data.ShipDetail(plan.Ship.Name)?.Skills.Find(s => s.Parts.Count > 0 && s.Parts.All(names.Contains));
        return granted == null ? null : Data.OptionSkills.Find(o => o.Name == granted.Name);
    }

    public string? HullBlocker(HullPlan plan, IReadOnlyCollection<int> materials)
    {
        if (Ordered != null) return "이미 맡겨 둔 배가 있다";
        if (plan.City != City.Name) return $"{plan.City}의 조선소에서만 짓는다";
        if (ShipbuildingRank < plan.Rank) return $"조선 랭크 {plan.Rank} 이 있어야 한다";
        if (Items.GetValueOrDefault(plan.HullItem) <= 0) return $"필요 선체: {plan.Hull}";
        if (!materials.Any(m => IsSailName(ItemName(m)))) return "「주요 돛」이 있어야 한다";
        if (!materials.Any(m => IsGunportName(ItemName(m)))) return "「포문」이 있어야 한다";
        if (materials.Count > 4) return "재료는 넷까지";
        if (Money < BuildCost(plan.Ship, plan.Material)) return "돈이 모자라다";
        return null;
    }

    /// <summary>특수 조선의 신규건조를 맡긴다 — 선체와 고른 재료가 든다. 날이 차면 「선박 받기」로 받는다.</summary>
    public void OrderHull(HullPlan plan, IReadOnlyCollection<int> materials)
    {
        if (Mode != Mode.Port || HullBlocker(plan, materials) != null) return;
        var skill = HullSkill(plan, materials);
        Money -= BuildCost(plan.Ship, plan.Material);
        foreach (int used in materials.Append(plan.HullItem))
            if (--Items[used] <= 0) Items.Remove(used);
        Ordered = new ShipOrder { Ship = plan.Ship, Material = plan.Material, Load = 0, DaysLeft = BuildDays(plan.Ship), Skills = skill == null ? [] : [skill.SkillId] };
        TrainEffect("Shipbuilding", 40 + plan.Ship.SizeClass * 30);
        Studied("Build");
        Say($"{plan.Hull}(으)로 {plan.Ship.Name}의 특수 조선을 맡겼다. 재질 {plan.MaterialName}, 건조일수 {BuildDays(plan.Ship)}일." + (skill == null ? "" : $" 옵션 스킬 「{skill.Name}」이(가) 붙는다."));
    }

    public string? ReceiveBlocker => Ordered == null ? "맡겨 둔 배가 없다" : Ordered.DaysLeft > 0 ? $"{Math.Ceiling(Ordered.DaysLeft):0}일 더 걸린다" : Dock.Count >= DockSlots ? "부두에 둘 자리가 없다" : null;

    /// <summary>다 지어진 배를 받아 부두에 둔다.</summary>
    public void ReceiveShip()
    {
        if (Mode != Mode.Port || ReceiveBlocker != null || Ordered is not { } order) return;
        Cues.Enqueue("Bank");                 // 맡긴 배를 받을 때도 은행 저금과 같은 소리(0:14)
        var stats = StatsOf(order.Ship, order.Material, order.Load);
        var work = new ShipWork();
        work.Skills.AddRange(order.Skills);
        Dock.Add(new DockedShip { Ship = order.Ship, Durability = stats.Durability, Material = order.Material, Load = order.Load, Work = work });
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
        if (order.DaysLeft <= 0) { Say($"맡겨 둔 {order.Ship.Name}이(가) 다 지어졌을 것이다. 조선소에서 받는다."); Cues.Enqueue("SkillUp"); }      // 건조가 끝나면 레벨업 소리(0:7)
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
        // 이름이 같은 줄만 하나로 줄인다 — 모형이 같아도 다른 배다(대형 카락과 탐험용 대형 카락). 타고 있는 배와 같은 것도 또 살 수 있다
        return Data.Ships.Where(s => s.Kind == 0 && s.SizeClass <= maxClass && !GameData.ShipVariants.Any(s.Name.Contains))
            .GroupBy(s => s.Name).Select(g => g.First())
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

    // 그레이드를 정해서 살 때의 값 — 그레이드 하나에 본디 값의 절반씩 더 든다(원본에는 없는 기능, 값은 지은 것)
    public int ShipCostAt(ShipData ship, int grade) => (int)Math.Min(int.MaxValue, ShipCost(ship) * (1 + 0.5 * Math.Clamp(grade, 0, MaxGrade)));

    public void BuyShip(ShipData ship, int grade = 0)
    {
        grade = Math.Clamp(grade, 0, MaxGrade);
        if (Mode != Mode.Port || ShipBlocker(ship) != null || Money < ShipCostAt(ship, grade)) return;
        // 사기만 한다 — 산 배는 부두에 매어 두고, 타는 것은 선박교환에서 한다
        Money -= ShipCostAt(ship, grade);
        var work = new ShipWork { Grade = grade };
        Dock.Add(new DockedShip { Ship = ship, Durability = Worked(StatsOf(ship, 0, 0), work, ship).Durability, Work = work });
        Say($"{ship.Name}" + (grade > 0 ? $"(그레이드 {grade})" : "") + "을(를) 사서 부두에 매어 두었다. 선박교환에서 갈아탄다.");
    }
}
