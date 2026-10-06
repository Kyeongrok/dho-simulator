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
        ShipStats.Facts.TryGetValue(Ship.Name, out var fact) && fact.Slots is { Length: >= 3 } real
            ? slot switch { 0 => Math.Clamp(real[0], 0, 5), 1 => Math.Clamp(real[2], 0, 5), _ => 1 }      // 배 자료의 칸 수(보조돛 · 추가장갑)
            : slot == 0 ? Math.Clamp(Ship.Masts, 1, 3) : 1;

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
        Mode != Mode.Port ? "항구에서만 단다" : Parts.Count(p => p.Slot == part.Slot) >= SlotsOf(part.Slot) ? $"{SlotName[part.Slot]} 칸이 찼다" : null;

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
    public static readonly string[] SlotName = ["보조돛", "장갑", "선수상", "문장"];

    /// <summary>타고 있는 배에 단 부품.</summary>
    public List<ShipPart> Parts { get; private set; } = [];

    public int PartPrice(ShipPart part) => part.Slot switch
    {
        0 => 1500 + (part.A + part.B) * 400,
        1 => 2000 + part.A * 1500,
        3 => 5000,
        _ => 3000 + (part.A + part.B + part.C + part.D) * 1200,
    };

    public string PartNote(ShipPart part) => part.Slot switch
    {
        0 => $"가로돛 +{part.A} · 세로돛 +{part.B}",
        1 => $"장갑 {part.A} · 속도 −{part.B}%",
        3 => "돛에 그리는 문장(모양뿐이다)",
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
    public double PartDamage => 1 - Math.Min(0.6, Parts.Where(p => p.Slot == 1).Sum(p => p.A) * 0.02);

    /// <summary>선수상이 줄여 주는 재해 확률 배율.</summary>
    public double PartLuck => 1 - Math.Min(0.5, Parts.Where(p => p.Slot == 2).Sum(p => p.A + p.B + p.C + p.D) * 0.012);
}
