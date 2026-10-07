using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 구입 발주서 — 교역소에서 쓰면 그 갈래 교역품의 구입 가능량이 원래대로 돌아온다(아이템 설명 그대로: 「식료품의 구입 가능량을 원래대로 되돌리는 것이 가능하다」).
/// 갈래별 발주서 스무 가지(아이템 1500205 ~ 1500224 — 이름이 「○○구입 발주서」)와 카테고리 발주서 넷(1500228 ~ 1500231 — 설명 글에 갈래 이름이 적혀 있다)이 클라이언트에 있다.
/// 어느 발주서가 어느 갈래인지는 이름 · 설명 글의 갈래 이름으로 맞춘다. 얻는 길은 아직 없다 — 「아이템 추가」로 넣는다.
/// </summary>
internal sealed partial class Voyage
{
    private static readonly int[] OrderSheets = [.. Enumerable.Range(1500205, 20), .. Enumerable.Range(1500228, 4)];

    private string ItemText(int id) =>
        Data.Papers.Find(p => p.Id == id) is { } paper ? paper.Name + " " + paper.Description : ItemName(id);

    /// <summary>그 교역품의 갈래에 듣는 발주서 가운데 갖고 있는 것 — 갈래 것부터, 없으면 카테고리 것. 없으면 0.</summary>
    public int OrderSheetFor(GoodData good)
    {
        string kind = Data.GoodKinds.Find(k => k.Id == good.Kind)?.Name ?? "";
        if (kind == "") return 0;
        // 「광물」 갈래의 발주서 이름은 「광석구입 발주서」다
        string[] words = kind == "광물" ? ["광물", "광석"] : [kind];
        return OrderSheets.Where(id => Items.GetValueOrDefault(id) > 0).FirstOrDefault(id => words.Any(w => ItemText(id).Replace(" ", "").Contains(w.Replace(" ", ""))));
    }

    /// <summary>발주서를 한 장 써서 이 도시에서 그 갈래 교역품의 구입 가능량을 되돌린다.</summary>
    public void UseOrderSheet(GoodData good)
    {
        int sheet = OrderSheetFor(good);
        if (Mode != Mode.Port || sheet <= 0) { Cues.Enqueue("Error"); return; }
        var sameKind = _bought.Keys.Where(k => k.Item1 == City.Id && Good(k.Item2)?.Kind == good.Kind).ToList();
        if (sameKind.Count == 0) { Say("구입 가능량이 줄지 않았다 — 발주서를 쓰지 않았다."); return; }
        foreach (var key in sameKind) _bought.Remove(key);
        SpendItem(sheet, 1);
        Cues.Enqueue("Buy");
        Say($"{ItemName(sheet)}을(를) 썼다 — {Data.GoodKinds.Find(k => k.Id == good.Kind)?.Name}의 구입 가능량이 돌아왔다.");
    }
}
