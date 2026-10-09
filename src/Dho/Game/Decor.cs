using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 선박 데코와 선원 장비 — 이름 · 설명 · 붙일 수 있는 자리는 클라이언트 표(138 · 139)의 것이다.
/// 장비한 것은 소지품에 그대로 있다(의상과 같다). 수치가 하는 일은 못 밝혀서 달아도 성능은 바뀌지 않고,
/// 데코의 모형은 배에 그린다(현의 넷은 ShipModel.SetDecos 가 모형으로, 마스트 톱의 깃발은 SetFlag 로 — GameWindow 가 그릴 때마다 넘긴다). 배마다 따로 두지 않고 선장에게 딸린 것으로 쳤다(배를 갈아타도 그대로).
/// </summary>
internal sealed partial class Voyage
{
    /// <summary>데코를 붙이는 자리 다섯 — 표의 자리 다섯 칸과 설명 글(「마스트 톱」 · 「전방 측면」 · 「뒤쪽 측면」)을 맞춰 본 이름이다.</summary>
    public static readonly string[] DecoSpots = ["마스트 톱", "전방 측면", "전방 측면", "뒤쪽 측면", "뒤쪽 측면"];
    /// <summary>선원 장비의 갈래 셋 — 번호대(2600 · 2601 · 2602)의 물건을 보고 붙인 이름이다(나침반 · 일지 / 창고 · 계산기 / 포탄 · 훈련 기록).</summary>
    public static readonly string[] CrewKinds = ["모험", "교역", "전투"];

    /// <summary>자리마다 붙인 데코의 아이템 번호(없으면 0).</summary>
    public int[] DecoOn { get; } = new int[5];
    /// <summary>갈래마다 쥐여 준 선원 장비의 아이템 번호(없으면 0).</summary>
    public int[] CrewOn { get; } = new int[3];

    public ShipDeco? DecoOf(int item) => item is >= 2_500_000 and < 2_600_000 ? Data.Decos.Find(d => d.Id == item) : null;
    public CrewGear? CrewGearOf(int item) => item is >= 2_600_000 and < 2_700_000 ? Data.CrewGears.Find(g => g.Id == item) : null;

    /// <summary>데코를 빈 자리(그 데코를 붙일 수 있는 자리 가운데 첫째)에 붙인다. 가진 수만큼만.</summary>
    public void FitDeco(int item)
    {
        if (DecoOf(item) is not { } deco) return;
        if (DecoOn.Count(on => on == item) >= Items.GetValueOrDefault(item)) { Say($"{deco.Name} — 남은 것이 없다."); Cues.Enqueue("Error"); return; }
        int spot = -1;
        for (int k = 0; k < DecoOn.Length && spot < 0; k++)
            if (DecoOn[k] == 0 && k < deco.Spots.Length && deco.Spots[k] != 0) spot = k;
        if (spot < 0) { Say($"{deco.Name}을(를) 붙일 자리가 비어 있지 않다."); Cues.Enqueue("Error"); return; }
        DecoOn[spot] = item;
        Say($"{deco.Name}을(를) {DecoSpots[spot]}에 붙였다.");
        Cues.Enqueue("Part");
    }

    public void UnfitDeco(int spot)
    {
        if (spot < 0 || spot >= DecoOn.Length || DecoOn[spot] == 0) return;
        Say($"{ItemName(DecoOn[spot])}을(를) 떼었다.");
        DecoOn[spot] = 0;
        Cues.Enqueue("Part");
    }

    /// <summary>선원 장비를 제 갈래의 칸에 쥐여 준다 — 이미 그것이면 거둔다.</summary>
    public void FitCrew(int item)
    {
        if (CrewGearOf(item) is not { } gear || Items.GetValueOrDefault(item) <= 0) return;
        int kind = Math.Clamp(gear.Kind, 0, CrewOn.Length - 1);
        if (CrewOn[kind] == item) { UnfitCrew(kind); return; }
        CrewOn[kind] = item;
        Say($"선원들에게 {gear.Name}을(를) 쥐여 주었다. ({CrewKinds[kind]})");
        Cues.Enqueue("Part");
    }

    public void UnfitCrew(int kind)
    {
        if (kind < 0 || kind >= CrewOn.Length || CrewOn[kind] == 0) return;
        Say($"{ItemName(CrewOn[kind])}을(를) 거두었다.");
        CrewOn[kind] = 0;
        Cues.Enqueue("Part");
    }
}
