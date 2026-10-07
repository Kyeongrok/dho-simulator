namespace Dho.Data;

/// <summary>
/// 조선 재료 한 가지 — 강화에 넣으면 능력치가 「낮은 값 ~ 높은 값」 사이에서 오른다(원본 강화 창의 「세로돛성능 강화 +40~ +50」).
/// 값은 gvdb 의 아이템 목록에서 뽑는다(<c>tools\gvo\gvdb_shipparts.py</c> → <c>data\extracted\ship-parts-gvdb.json</c>).
/// Kind: 0 주요 돛 · 1 포문 · 2 선체 · 3 장비. Sizes: 0 소형 · 1 중형 · 2 대형(비면 전부).
/// Stats 의 이름: Durability · Vertical · Horizontal · Rowing · Turn · Wave · Armor · Cabin · Guns · Hold.
/// </summary>
public sealed class BuildPart
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Kind { get; set; }
    public string Types { get; set; } = "";
    public List<int> Sizes { get; set; } = [];
    public Dictionary<string, int[]> Stats { get; set; } = [];
    /// <summary>캐시(아이템 샵) 재료 — 넣으면 초과 강화가 꼭 된다. 목록은 짐작(도구의 주석).</summary>
    public bool Cash { get; set; }
}
