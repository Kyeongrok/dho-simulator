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
        Mode != Mode.Port ? "항구에서만 단다"
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
        if (Mode != Mode.Port || PartStock.Count >= PartStockLimit || !Parts.Remove(part)) return;
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

    public int PartPrice(ShipPart part) => part.Slot switch
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
        4 => $"{CannonSpots[SpotOf(part)]} {part.A}문 · 관통 {part.B} · 사정 {part.C} · {AmmoName(part.D / 10)}",
        5 => $"{GearKinds[Math.Clamp(part.A, 0, GearKinds.Length - 1)]}" + (part.B > 0 ? $" {part.B}" : "") + (GearNote(part) is { Length: > 0 } does ? $" — {does}" : ""),
        _ => $"효과 {part.A}/{part.B}/{part.C}/{part.D}",
    };

    /// <summary>이 도시의 조선소가 파는 부품 — 도시가 클수록 비싼 것까지.</summary>
    public List<ShipPart> PartsForSale()
    {
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
        if (Mode != Mode.Port || PartBlocker(part) != null) return;
        Money -= PartPrice(part);
        Cues.Enqueue("Buy");
        PartStock.Add(part);
        Say($"{part.Name}을(를) 샀다. ({PartPrice(part):N0} 두캇) — B 로 배에 단다.");
    }

    /// <summary>떼어서 반값에 판다.</summary>
    public void SellPart(ShipPart part)
    {
        if (Mode != Mode.Port || !PartStock.Remove(part)) return;
        Money += PartPrice(part) / 2;
        Say($"{part.Name}을(를) 팔았다. ({PartPrice(part) / 2:N0} 두캇)");
    }

    /// <summary>보조돛이 보태고 장갑이 깎는 속도 배율.</summary>
    public double PartSpeed =>
        1 - Parts.Where(p => p.Slot == 1).Sum(p => p.B) / 100.0;       // 보조돛의 몫은 이제 돛 성능과 속도에 바로 얹힌다(Stats)

    /// <summary>장갑이 줄여 주는 내구 피해 배율.</summary>
    public double PartDamage => 1 - Math.Min(0.6, Parts.Where(p => p.Slot == 1).Sum(ArmorOf) * 0.02);

    /// <summary>선수상이 줄여 주는 재해 확률 배율.</summary>
    public double PartLuck => 1 - Math.Min(0.5, Parts.Where(p => p.Slot == 2).Sum(p => p.A + p.B + p.C + p.D) * 0.012);
}
