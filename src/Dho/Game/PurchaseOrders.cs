using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 구입 발주서 — 교역소에서 쓰면 그 갈래 교역품의 구입 가능량이 원래대로 돌아온다(아이템 설명 그대로: 「식료품의 구입 가능량을 원래대로 되돌리는 것이 가능하다」).
/// 갈래별 발주서 스무 가지(아이템 1500205 ~ 1500224 — 이름이 「○○구입 발주서」)와 카테고리 발주서 넷(1500228 ~ 1500231 — 설명 글에 갈래 이름이 적혀 있다)이 클라이언트에 있다.
/// 어느 발주서가 어느 갈래인지는 이름 · 설명 글의 갈래 이름으로 맞춘다. 얻는 길: 교역 의뢰의 보상(gvdb 의 보상 칸 — 카테고리 발주서 1 ~ 4 가 340건쯤에 붙어 있다) · 「아이템 추가」.
/// </summary>
internal sealed partial class Voyage
{
    private static readonly int[] OrderSheets = [.. Enumerable.Range(1500205, 20), .. Enumerable.Range(1500228, 4)];

    /// <summary>「아이템 추가」의 증서 목록에 더 세우는 것 — 구입 발주서(카테고리 1 ~ 4) · 특별발주증서 · 천만 수표 · 변성연금의 책 둘 · 재봉도구 · 특별 위임 항해 허가증.</summary>
    public static readonly int[] ExtraPapers = [.. Enumerable.Range(1500228, 4), OrderPaper, Check10M, OuroborosBook, UnicornBook, 1500052, SpecialPermit, .. Enumerable.Range(1510681, 6), CoalItem];

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

    // 걸어 둔 발주서 — (발주서 아이템, 그 장으로 되돌리는 갈래). 원본의 글(화면 글 6702): 「구입 발주서를 사용한 경우에는 거래 시 한번에 소비됩니다.」
    // 곧 「사용」은 걸어 두는 것이고, 「확인」으로 사야 그때 한꺼번에 쓰인다 — 사지 않고 「이전」 · 「전부취소」면 안 쓰인다.
    private readonly List<(int Sheet, int Kind)> _sheetsMarked = [];

    /// <summary>그 갈래에 걸어 둔 발주서의 수.</summary>
    public int SheetsMarked(int kind) => _sheetsMarked.Count(s => s.Kind == kind);
    public int SheetsMarkedAll => _sheetsMarked.Count;

    /// <summary>발주서를 한 장 걸어 둔다 — 그 갈래 교역품의 구입 가능량이 한 번 더 찬다. 쓰이는 것은 거래할 때.</summary>
    public void UseOrderSheet(GoodData good)
    {
        int sheet = OrderSheetFor(good);
        if (Mode != Mode.Port || sheet <= 0) { Cues.Enqueue("Error"); return; }
        if (_sheetsMarked.Count(s => s.Sheet == sheet) >= Items.GetValueOrDefault(sheet)) { Say($"{ItemName(sheet)}이(가) 더 없다."); Cues.Enqueue("Error"); return; }
        _sheetsMarked.Add((sheet, good.Kind));
        Cues.Enqueue("Click");
    }

    /// <summary>거래가 이루어졌다 — 걸어 둔 발주서가 한꺼번에 쓰인다.</summary>
    public void SpendMarkedSheets()
    {
        if (_sheetsMarked.Count == 0) return;
        foreach (var group in _sheetsMarked.GroupBy(s => s.Sheet)) { SpendItem(group.Key, group.Count()); Say($"{ItemName(group.Key)} {group.Count()}장을 썼다."); }
        _sheetsMarked.Clear();
    }

    /// <summary>거래를 그만두었다 — 걸어 둔 발주서를 푼다(쓰이지 않는다).</summary>
    public void ClearMarkedSheets() => _sheetsMarked.Clear();
}
