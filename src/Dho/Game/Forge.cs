using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 대장간의 단련 — 입은 장비의 공격력 · 방어력을 올리거나 서로 바꾸고, 단 대포의 관통력을 올린다.
/// 단련의 갈래와 조건 글은 클라이언트 표 110 의 것이다(「공격력 강화」 · 「방어력 강화」 · 「공격을 방어로 변환」 · 「방어를 공격으로 변환」 · 「대포 관통력 강화」:
/// *기본 값이 0인 경우 및 제한을 초과했을 경우에는 강화할 수 없다*). 제한의 크기 · 드는 돈 · 한 번에 오르는 양은 표에 없어 지은 것이다.
/// 단련은 장비 한 벌이 아니라 그 장비의 갈래(번호)에 붙는다 — 같은 장비를 여럿 가져도 하나로 친다.
/// </summary>
internal sealed partial class Voyage
{
    /// <summary>장비 번호 → 단련으로 더해진 (공격력, 방어력). 대포는 부품 번호 → (관통력, 0).</summary>
    public Dictionary<int, int[]> Forged { get; } = [];

    public static readonly string[] ForgeKinds = ["공격력 강화", "방어력 강화", "공격을 방어로 변환", "방어를 공격으로 변환"];

    public int ForgedOf(int id, int stat) => Forged.TryGetValue(id, out var added) && stat < added.Length ? added[stat] : 0;

    /// <summary>그 능력치를 단련으로 올릴 수 있는 끝 — 기본 값의 3할(적어도 3). 지은 값.</summary>
    public static int ForgeLimit(int basis) => basis <= 0 ? 0 : Math.Max(3, basis * 3 / 10);

    public int ForgePrice(int id) => 3000 + 2000 * (ForgedOf(id, 0) + ForgedOf(id, 1));

    public string? ForgeBlocker(GearItem gear, int kind)
    {
        if (Mode != Mode.Port) return "항구에서만 한다";
        int attack = gear.Stats.ElementAtOrDefault(0), defense = gear.Stats.ElementAtOrDefault(1);
        int from = kind is 0 or 2 ? attack : defense, to = kind is 0 or 3 ? attack : defense;
        if ((kind < 2 ? from : Math.Min(from, to)) <= 0) return "기본 값이 0 이다";
        int toStat = kind is 0 or 3 ? 0 : 1, fromStat = kind is 0 or 2 ? 0 : 1;
        if (ForgedOf(gear.Id, toStat) >= ForgeLimit(to)) return "제한을 넘는다";
        if (kind >= 2 && from + ForgedOf(gear.Id, fromStat) <= 1) return "더 옮길 것이 없다";
        return Money < ForgePrice(gear.Id) ? "돈이 모자라다" : null;
    }

    public void Forge(GearItem gear, int kind)
    {
        if (ForgeBlocker(gear, kind) != null) return;
        Money -= ForgePrice(gear.Id);
        if (!Forged.TryGetValue(gear.Id, out var added)) Forged[gear.Id] = added = [0, 0];
        int toStat = kind is 0 or 3 ? 0 : 1;
        added[toStat]++;
        if (kind >= 2) added[1 - toStat]--;
        Cues.Enqueue("Part");
        Say($"{gear.Name} — {ForgeKinds[kind]}. (공격력 {gear.Stats.ElementAtOrDefault(0) + added[0]} · 방어력 {gear.Stats.ElementAtOrDefault(1) + added[1]})");
    }

    /// <summary>대포 관통력 강화 — 단 대포마다 관통력의 1할(적어도 2)까지.</summary>
    public string? CannonForgeBlocker(ShipPart cannon) =>
        Mode != Mode.Port ? "항구에서만 한다" : ForgedOf(cannon.Id, 0) >= Math.Max(2, cannon.B / 10) ? "제한을 넘는다" : Money < ForgePrice(cannon.Id) ? "돈이 모자라다" : null;

    public void ForgeCannon(ShipPart cannon)
    {
        if (CannonForgeBlocker(cannon) != null) return;
        Money -= ForgePrice(cannon.Id);
        if (!Forged.TryGetValue(cannon.Id, out var added)) Forged[cannon.Id] = added = [0, 0];
        added[0]++;
        Cues.Enqueue("Part");
        Say($"{cannon.Name} — 대포 관통력 강화. (관통 {cannon.B + added[0]})");
    }
}
