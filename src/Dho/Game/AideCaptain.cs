using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 부관 선장 — 부두에 매어 둔 배 한 척을 부관에게 맡겨 함께 다닌다.
/// 원본의 글(화면 글): 16217 「부관 선장」, 25332 「부관 선장 임명」 · 25333 「부관 선장 해임」, 16221 「부관 선장에게 부여할 선박을 선택해 주십시오.」,
/// 16219 「%s을 부관 선장에서 해임합니다. 이대로 진행하시겠습니까?」, 16227 「이곳에서는 부관 선장을 변경할 수 없습니다.」,
/// 6049 「부관 선박의 적재화물이 너무 많아서，%s 선박으로 옮길 수 없습니다.」, 16225 「부관 원군」 · 16611 「전투 참가」.
/// 부관의 배가 무엇을 하는가의 셈은 클라이언트에 없다 — 아래는 지은 값:
/// 그 배의 창고가 내 창고에 더해지고, 해전에서는 여덟 초마다 적선을 쏜다(대포 수 + 부관 레벨 × 2), 선장인 동안 급여는 두 배.
/// 임명 · 해임은 항구에서만. 바다에서는 내 배 뒤에 붙어 따라온다(그리기만 — GameWindow.DrawAideShips). 그 배의 선원 · 피로 · 내구가 따로 닳는 것, 해임 때의 신뢰도는 넣지 않았다.
/// </summary>
internal sealed partial class Voyage
{
    /// <summary>부관 선장의 배들 — 부두에서는 빠져 있다.</summary>
    public IEnumerable<DockedShip> CaptainShips => Aides.Where(a => a.Ship != null).Select(a => a.Ship!);

    /// <summary>부두의 배와 부관 선장의 배 — 저장할 때 한 줄로 적는다.</summary>
    private List<DockedShip> AllMoored => [.. Dock, .. CaptainShips];

    /// <summary>부관 선장의 배들이 보태는 창고.</summary>
    public int AideHold => CaptainShips.Sum(s => StatsOf(s).Hold);

    /// <summary>내 배와 부관 선장의 배를 합친 창고.</summary>
    public int TotalHold => Stats.Hold + AideHold;

    public string? CaptainBlocker(Aide aide) =>
        Mode != Mode.Port ? Text(16227, "여기서는 부관 선장을 바꿀 수 없다.")
        : aide.Ship == null && Dock.Count == 0 ? "부두에 맡길 배가 없다"
        : aide.Ship != null && CargoCount > TotalHold - StatsOf(aide.Ship).Hold ? Fill(Text(6049, "부관 배의 짐이 너무 많아 %s 배로 다 옮기지 못한다."), Ship.Name)
        : null;

    /// <summary>부두의 배를 부관에게 맡긴다. 이미 선장이면 타던 배는 부두로 돌아간다.</summary>
    public void AppointCaptain(Aide aide, DockedShip docked)
    {
        if (Mode != Mode.Port) { Say(Text(16227, "여기서는 부관 선장을 바꿀 수 없다.")); Cues.Enqueue("Error"); return; }
        if (!Dock.Remove(docked)) return;
        if (aide.Ship is { } old) Dock.Add(old);
        aide.Ship = docked;
        Cues.Enqueue("Done");
        Say($"{aide.Who.Name}을(를) {docked.Ship.Name}의 {Text(16217, "부관 선장")}(으)로 임명했다. (창고 +{StatsOf(docked).Hold})");
    }

    public void RelieveCaptain(Aide aide)
    {
        if (aide.Ship is not { } ship) return;
        if (CaptainBlocker(aide) is { } why) { Say(why); Cues.Enqueue("Error"); return; }
        aide.Ship = null;
        Dock.Add(ship);
        Say($"{aide.Who.Name}을(를) {Text(16217, "부관 선장")}에서 해임했다. {ship.Ship.Name}은(는) 부두로 돌아갔다.");
    }

    // 해전 — 부관 선장의 배가 여덟 초마다 한 번씩 적선을 쏜다
    private void AideShipsFire(SeaBattle battle, double dt)
    {
        foreach (var aide in Aides)
        {
            if (aide.Ship is not { } ship) continue;
            if ((aide.FireIn -= dt) > 0) continue;
            aide.FireIn = 8;
            double shell = (StatsOf(ship).Guns + aide.Level * 2) * (0.8 + _random.NextDouble() * 0.4);
            if (shell < 1) continue;
            battle.Foe.Durability -= shell;
            battle.Hits.Add(new SeaHit { X = battle.Foe.X, Y = battle.Foe.Y, Text = $"{aide.Who.Name} −{shell:0}" });
            battle.Log.Add($"{Text(16217, "부관 선장")} {aide.Who.Name}의 포격 — {Fill(Text(20007, "선체에 %d의 피해를 주었습니다!"), $"{shell:0}")}");
            Cues.Enqueue("Cannon");
            CheckBattleEnd(battle);
            if (Battle != battle || battle.Result != null) return;
        }
    }

    /// <summary>대본용: 첫 부관(없으면 하나 고용한 것으로 한다)을 부두의 첫 배의 선장으로.</summary>
    public void CaptainForTest()
    {
        if (Aides.Count == 0 && Data.Aides.Count > 0) Aides.Add(new Aide { Who = Data.Aides[0], Duty = 0 });
        if (Dock.Count == 0 && Data.Ships.Count > 0) Dock.Add(new DockedShip { Ship = Ship, Durability = Stats.Durability });
        if (Aides.Count > 0 && Dock.Count > 0) AppointCaptain(Aides[0], Dock[0]);
    }
}
