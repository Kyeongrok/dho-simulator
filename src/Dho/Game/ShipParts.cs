using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 배 부품 — 보조돛 · 장갑 · 선수상. 이름과 수치는 클라이언트 표(25 · 23 · 27)의 것이고,
/// 값과 그 수치가 하는 일(속도 · 피해 · 재해)은 지은 규칙이다.
/// </summary>
internal sealed partial class Voyage
{
    /// <summary>배마다 다른 칸 수 — 보조돛은 돛대 수만큼(셋까지), 장갑 하나, 선수상 하나. 원본의 배마다의 칸 수는 자료를 못 찾아 지은 것이다.</summary>
    public int SlotsOf(int slot) =>
        slot == 4 ? CannonSlots(0) + CannonSlots(1) + CannonSlots(2)
        : slot == 5 ? (ShipStats.Facts.TryGetValue(Ship.Name, out var special) && special.Slots is { Length: >= 3 } counts ? Math.Clamp(counts[1], 0, 5) : 1)      // 특수장비 칸(위키의 特) — 자료가 없으면 하나(지은 값)
        : ShipStats.Facts.TryGetValue(Ship.Name, out var fact) && fact.Slots is { Length: >= 3 } real
            ? slot switch { 0 => Math.Clamp(real[0], 0, 5), 1 => Math.Clamp(real[2], 0, 5), _ => 1 }      // 배 자료의 칸 수(보조돛 · 추가장갑)
            : slot == 0 ? Math.Clamp(Ship.Masts, 1, 3) : 1;

    public static readonly string[] CannonSpots = ["선측포", "선수포", "선미포"];

    /// <summary>대포 칸 수 — 자리(0 선측 · 1 선수 · 2 선미)마다. 배 자료(위키의 側 · 首 · 尾)에 없으면 선측 둘(지은 값).</summary>
    public int CannonSlots(int spot) =>
        ShipStats.Facts.TryGetValue(Ship.Name, out var fact) && fact.Slots is { Length: >= 6 } real ? Math.Clamp(real[3 + spot], 0, 5) : spot == 0 ? 2 : 0;

    /// <summary>단 대포의 문 수(배의 포문 수를 넘지 못한다)와 평균 관통력.</summary>
    public int GunsFitted => Math.Min(Stats.Guns, Parts.Where(p => p.Slot == 4).Sum(p => p.A));
    public double GunPierce => Parts.Where(p => p.Slot == 4).Sum(p => p.A) is > 0 and var guns ? Parts.Where(p => p.Slot == 4).Sum(p => p.A * (p.B + ForgedOf(p.Id, 0))) / (double)guns : 0;

    /// <summary>부품을 값 없이 받는다(아이템 추가 창) — 가진 부품 칸이 차 있으면 못 받는다.</summary>
    public void GivePart(ShipPart part)
    {
        if (PartStock.Count >= PartStockLimit) { Say("부품을 더 가질 수 없다."); Cues.Enqueue("Error"); return; }
        PartStock.Add(part);
        Say($"{part.Name}을(를) 얻었다. — B 로 배에 단다.");
    }

    /// <summary>가지고만 있는 부품(소유 선박부품) — 사면 여기 들어오고, 탈착 창(B)에서 배에 단다.</summary>
    public List<ShipPart> PartStock { get; } = [];
    public const int PartStockLimit = 30;

    public string? FitBlocker(ShipPart part) =>
        !YardOpen ? "항구에서만 단다"
        : part.Slot == 4 ? (Parts.Count(p => p.Slot == 4 && SpotOf(p) == SpotOf(part)) >= CannonSlots(SpotOf(part)) ? $"{CannonSpots[SpotOf(part)]} 칸이 {(CannonSlots(SpotOf(part)) == 0 ? "없다" : "찼다")}" : null)
        : Parts.Count(p => p.Slot == part.Slot) >= SlotsOf(part.Slot) ? $"{SlotName[part.Slot]} 칸이 찼다" : null;

    /// <summary>가진 부품을 배에 단다.</summary>
    public void Fit(ShipPart part)
    {
        if (FitBlocker(part) != null || !PartStock.Remove(part)) return;
        Parts.Add(part);
        Cues.Enqueue("Part");
        Say($"{part.Name}을(를) 달았다.");
    }

    /// <summary>배에서 떼어 가진 부품으로 돌린다.</summary>
    public void Unfit(ShipPart part)
    {
        if (!YardOpen || PartStock.Count >= PartStockLimit || !Parts.Remove(part)) return;
        PartStock.Add(part);
        Cues.Enqueue("Part");
        Say($"{part.Name}을(를) 떼었다.");
    }
    public static readonly string[] SlotName = ["보조돛", "장갑", "선수상", "문장", "대포", "특수장비"];

    /// <summary>대포가 다는 자리(0 선측 · 1 선수 · 2 선미)와 탄 — 대포 줄의 D 는 자리 + 탄 갈래 × 10.</summary>
    public static int SpotOf(ShipPart cannon) => Math.Clamp(cannon.D % 10, 0, 2);
    public string AmmoName(int ammo) => Data.Ammo.Find(a => a.Id == ammo)?.Name ?? $"탄 {ammo}";

    // 특수장비(표 24)의 갈래 — 충각 · 추가돛 · 조교의 이름은 설명 글에서 읽은 것이고, 하는 일의 크기는 지은 것이다
    public static readonly string[] GearKinds = ["충각", "특수장비", "선수 추가돛", "선미 추가돛", "조교", "기관포", "화염방사기", "방벽"];
    public static string GearNote(ShipPart gear) => gear.A switch
    {
        0 => $"들이받으면 피해 {gear.B * 40}",
        2 => $"가로돛 +{gear.B}",
        3 => $"세로돛 +{gear.B}",
        4 => $"백병전에서 선원이 {gear.B * 5}% 더 세다",
        5 => "가까이(사정의 절반)에서 포를 쏠 때 적의 선원을 더 줄인다",
        6 => "가까이에서 포를 쏠 때 불을 붙인다(내구 피해 +20%)",
        7 => "백병전에서 잃는 선원 −15%",
        _ => "효과 없음(아직)",
    };
    // 「병기기술」(설명: 「특정 배가 소유한 장비를 유효하게 활용할 수 있다」) — 특수장비의 세기가 랭크마다 +3%(지은 값)
    private double GearPower(int kind) => Parts.Where(p => p.Slot == 5 && p.A == kind).Select(p => p.B).DefaultIfEmpty(0).Max() * (1 + FormBonus("GearUse"));

    /// <summary>타고 있는 배에 단 부품.</summary>
    public List<ShipPart> Parts { get; private set; } = [];

    /// <summary>부품 값 — gvdb 의 장인 값이 있으면 그것(254가지), 없는 부품은 아래의 식(지은 값).</summary>
    public int PartPrice(ShipPart part) => Data.PartPrices.TryGetValue(part.Id, out int price) ? price : part.Slot switch
    {
        0 => 1500 + (part.A + part.B) * 400,
        1 => 2000 + part.A * 1500,
        3 => 5000,
        4 => 1000 + part.A * part.B * 30,
        5 => 4000 + part.B * 2500,
        _ => 3000 + (part.A + part.B + part.C + part.D) * 1200,
    };

    public string PartNote(ShipPart part) => part.Slot switch
    {
        0 => $"가로돛 +{part.A} · 세로돛 +{part.B}",
        1 => $"장갑 {part.A} · 속도 −{part.B}%",
        3 => "돛에 그리는 문장(모양뿐이다)",
        4 => $"{CannonSpots[SpotOf(part)].Replace("포", "")} 관통{part.B} 사정{part.C} 장전{part.Reload} {AmmoName(part.D / 10)}",
        5 => $"{GearKinds[Math.Clamp(part.A, 0, GearKinds.Length - 1)]}" + (part.B > 0 ? $" {part.B}" : "") + (GearNote(part) is { Length: > 0 } does ? $" — {does}" : ""),
        _ => $"재해 {part.A} · 피로 {part.B} · 장악 {part.C} · 회피 {part.D}" + (Data.FigureheadUses.TryGetValue(part.Id, out var use) && FigureheadCures.TryGetValue(use, out int cured) && Data.Disasters.Find(d => d.Id == cured) is { } harm ? $" · 쓰면 {harm.Name} 해소" : Data.FigureheadUses.TryGetValue(part.Id, out var chase) && FigureheadRepels.TryGetValue(chase, out int beast) ? $" · 쓰면 {(beast == 1 ? "상어" : "크라켄")} 격퇴" : ""),      // 재해 수호 · 피로 경감 · 선원 장악 · 포탄 회피
    };

    /// <summary>
    /// 이 도시에서 파는 부품 — gvdb 에 그 도시의 장인(무기 · 돛 · 조각 · 도장 · 제재) 목록이 있으면 그대로(원본의 판매 목록, 87 도시).
    /// 원본은 장인마다 따로 팔지만 여기서는 조선소의 「선박부품」 한 곳에 모았다(줄인 것). 목록이 없는 도시는 지은 규칙: 가까운 도시의 목록을 빌린다(자료가 아예 없을 때만 「도시가 클수록 비싼 것까지」).
    /// </summary>
    public List<ShipPart> PartsForSale()
    {
        // 목록이 없는 도시는 가장 가까운(같은 문화권 먼저) 목록 있는 도시의 것을 빌려 쓴다 — 지은 규칙(Shipyard.cs 의 NearestListed)
        if (Data.PartShops.TryGetValue(City.Id, out var sold) || (NearestListed(Data.PartShops.Keys) is { } lender && Data.PartShops.TryGetValue(lender, out sold)))
            return Data.ShipParts.Where(p => sold.ContainsKey(p.Id)).OrderBy(p => p.Slot).ThenBy(PartPrice).ToList();
        int limit = City.Kind switch { 0 => int.MaxValue, 1 => 40000, _ => 15000 };
        return Data.ShipParts.Where(p => !p.Name.Contains("명품") && PartPrice(p) <= limit)
            .GroupBy(p => p.Name).Select(g => g.First())
            .OrderBy(p => p.Slot).ThenBy(PartPrice).ToList();
    }

    public string? PartBlocker(ShipPart part)
    {
        if (Money < PartPrice(part)) return "돈이 모자라다";
        if (PartStock.Count >= PartStockLimit) return "부품을 더 가질 수 없다";
        return null;
    }

    public void BuyPart(ShipPart part)
    {
        if (!YardOpen || PartBlocker(part) != null) return;
        Money -= PartPrice(part);
        Cues.Enqueue("Buy");
        PartStock.Add(part);
        Say($"{part.Name}을(를) 샀다. ({PartPrice(part):N0} 두캇) — B 로 배에 단다.");
    }

    /// <summary>떼어서 반값에 판다.</summary>
    public void SellPart(ShipPart part)
    {
        if (!YardOpen || !PartStock.Remove(part)) return;
        Money += PartPrice(part) / 2;
        Say($"{part.Name}을(를) 팔았다. ({PartPrice(part) / 2:N0} 두캇)");
    }

    /// <summary>보조돛이 보태고 장갑이 깎는 속도 배율.</summary>
    public double PartSpeed =>
        1 - Parts.Where(p => p.Slot == 1).Sum(p => p.B) / 100.0;       // 보조돛의 몫은 이제 돛 성능과 속도에 바로 얹힌다(Stats)

    /// <summary>장갑이 줄여 주는 내구 피해 배율.</summary>
    public double PartDamage => 1 - Math.Min(0.6, Parts.Where(p => p.Slot == 1).Sum(ArmorOf) * 0.02);

    // 선수상의 네 수치(클라이언트 표 27 의 A ~ D)는 gvdb 아이템 목록의 글과 값이 맞는다(팔바티상 5/6/1/4 = 「災害守護：5 疲労軽減：6 船員掌握：1 砲弾回避：4」):
    // A 재해 수호 · B 피로 경감 · C 선원 장악 · D 포탄 회피. 수치 1 이 얼마의 효과인지는 자료가 없다 — 아래 곱(3% · 3% · 2%)은 지은 값.
    // 선원 장악은 이 게임에 선원의 충성이 없어 쓰이지 않는다. 「使用時効果」는 재해를 푸는 것만 이었다(아래 선수상의 쓰는 효과 — UseFigurehead)
    /// <summary>선수상의 「재해 수호」가 줄여 주는 재해 확률 배율.</summary>
    public double PartLuck => 1 - Math.Min(0.5, Parts.Where(p => p.Slot == 2).Sum(p => p.A) * 0.03);
    /// <summary>선수상의 「피로 경감」이 줄여 주는 피로 배율.</summary>
    public double PartFatigue => 1 - Math.Min(0.4, Parts.Where(p => p.Slot == 2).Sum(p => p.B) * 0.03);
    /// <summary>
    /// 단 대포의 「장전 속도」(클라이언트 대포 표의 값 1 ~ 9, gvdb 글 「装填速度」 — 클수록 빠르다)가 장전 시간에 곱하는 배율: 문 수로 고른 평균이 4(가장 흔한 값)면 1,
    /// 하나 높을 때마다 5% 짧다(곱은 지은 값 — 수 1 이 몇 초인지는 자료가 없다). 탄속 · 폭발 범위는 아직 안 쓴다.
    /// </summary>
    public double CannonReload
    {
        get
        {
            var guns = Parts.Where(p => p.Slot == 4 && p.Reload > 0).ToList();
            if (guns.Count == 0) return 1;
            double mean = guns.Sum(p => (double)p.Reload * p.A) / Math.Max(1, guns.Sum(p => p.A));
            return Math.Clamp(1 - (mean - 4) * 0.05, 0.7, 1.2);
        }
    }

    // ── 선수상의 쓰는 효과(gvdb 「使用時効果」 35개) — 재해를 푸는 것만 잇는다: 일본어 글 → 이 게임의 재해 번호(data\disasters.json).
    // 크라켄 격퇴 · 주술 · 피로 회복 · 충성도 상승 · 외과의술 · 구조 · 회피 따위는 받을 자리가 없어 안 잇는다.
    // 원본에서 얼마 만에 다시 쓰는지는 자료가 없다 — 바다에서 하루에 한 번(지은 값)
    private static readonly Dictionary<string, int> FigureheadCures = new()
    {
        ["消火"] = 1, ["浸水回復"] = 2, ["壊血病回復"] = 3, ["ネズミ退治"] = 4, ["駆除"] = 4, ["藻除去"] = 5, ["サメ撃退"] = 7,
        ["疫病回復"] = 10, ["疾病回復"] = 10, ["セイレーン撃退"] = 12, ["タコ撃退"] = 13,
    };
    private double _figureheadUsedAt = double.MinValue;

    /// <summary>단 선수상 가운데 재해를 푸는 것과 그 재해 — 없으면 null.</summary>
    public (ShipPart Part, DisasterData Cures)? FigureheadCure =>
        Parts.Where(p => p.Slot == 2).Select(p => Data.FigureheadUses.TryGetValue(p.Id, out var use) && FigureheadCures.TryGetValue(use, out int id) && Data.Disasters.Find(d => d.Id == id) is { } cures ? ((ShipPart, DisasterData)?)(p, cures) : null)
            .FirstOrDefault(f => f != null);

    public string? FigureheadBlocker =>
        FigureheadCure is not { } use ? "쓸 수 있는 선수상이 없다"
        : Clock - _figureheadUsedAt < Settings.SecondsPerDay ? "오늘은 이미 썼다"
        : !Disasters.Exists(d => d.Data.Id == use.Cures.Id) ? $"{use.Cures.Name}이(가) 나지 않았다" : null;

    // 바다 괴물을 쫓는 선수상(gvdb 「サメ撃退」 · 「クラーケン撃退」) → 괴물 갈래(1 상어 · 2 크라켄). 쓰면 싸우던 그 괴물이 물러가 싸움이 끝난다
    // (얼마나 아프게 하는지 같은 크기를 짓지 않으려고 「격퇴 = 물러간다」로 옮겼다 — 전리품 · 경험은 없다). 하루에 한 번은 재해 쪽과 같이 센다
    private static readonly Dictionary<string, int> FigureheadRepels = new() { ["サメ撃退"] = 1, ["クラーケン撃退"] = 2 };

    /// <summary>단 선수상 가운데 지금 싸우는 괴물을 쫓는 것 — 없으면 null.</summary>
    public ShipPart? FigureheadRepel =>
        Battle is { Result: null, Foe.Monster: > 0 } fight
            ? Parts.Find(p => p.Slot == 2 && Data.FigureheadUses.TryGetValue(p.Id, out var use) && FigureheadRepels.TryGetValue(use, out int kind) && kind == fight.Foe.Monster) : null;

    /// <summary>선수상을 쓴다 — 싸우는 괴물을 쫓거나 그 재해를 푼다.</summary>
    public void UseFigurehead()
    {
        if (FigureheadRepel is { } guard && Battle is { } fight)
        {
            if (Clock - _figureheadUsedAt < Settings.SecondsPerDay) { Say("선수상 — 오늘은 이미 썼다."); Cues.Enqueue("Error"); return; }
            _figureheadUsedAt = Clock;
            Say($"{guard.Name}의 가호!");
            fight.Result = $"{fight.Foe.Name}이(가) 물러갔다.";
            SeaShips.Remove(fight.Foe);
            Say(fight.Result);
            Dialog = Dialog.Battle;
            return;
        }
        if (FigureheadBlocker is { } why) { Say($"선수상 — {why}."); Cues.Enqueue("Error"); return; }
        var (part, cures) = FigureheadCure!.Value;
        _figureheadUsedAt = Clock;
        Say($"{part.Name}의 가호!");
        End(Disasters.Find(d => d.Data.Id == cures.Id)!);
    }

    private int _dodged;
    /// <summary>대본용 — 적의 포격이 모두 빗나가게.</summary>
    public bool DodgeAllForTest { get; set; }
    /// <summary>대본용 — 부품이 주는 배율들과 이번 실행에서 빗나간 적 포격 수.</summary>
    public void PartsReportForTest() =>
        Say($"(시험) 장전 배율 {CannonReload:0.00} · 포탄 회피 {PartDodge:P0} · 재해 {PartLuck:0.00} · 피로 {PartFatigue:0.00} · 빗나간 적 포격 {_dodged}번 · 내구 {Durability:0}/{Stats.Durability}");

    /// <summary>선수상의 「포탄 회피」 — 적의 한 번 포격이 빗나갈 확률.</summary>
    public double PartDodge => DodgeAllForTest ? 1 : Math.Min(0.3, Parts.Where(p => p.Slot == 2).Sum(p => p.D) * 0.02);
}
