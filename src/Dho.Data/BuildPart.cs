namespace Dho.Data;

/// <summary>
/// 조선 재료 한 가지 — 강화에 넣으면 능력치가 「낮은 값 ~ 높은 값」 사이에서 오른다(원본 강화 창의 「세로돛성능 강화 +40~ +50」).
/// 값은 gvdb 의 아이템 목록에서 뽑는다(<c>tools\gvo\gvdb_shipparts.py</c> → <c>data\extracted\ship-parts-gvdb.json</c>).
/// Kind: 0 주요 돛 · 1 포문 · 2 선체 · 3 장비. Sizes: 0 소형 · 1 중형 · 2 대형(비면 전부).
/// Stats 의 이름: Durability · Vertical · Horizontal · Rowing · Turn · Wave · Armor · Cabin · Guns · Hold.
/// </summary>
/// <summary>배 한 척에 옵션 스킬 하나를 붙인 재료 조합 — gvdb 의 이용자 보고(<c>tools\gvo\gvdb_fsbuild.py</c>).</summary>
public sealed class ShipCombo
{
    public string Ship { get; set; } = "";
    public List<string> Parts { get; set; } = [];
    public string Skill { get; set; } = "";
}

/// <summary>한 도시의 조선소가 파는 배와 값 — gvdb 의 이용자 보고(<c>tools\gvo\gvdb_shipyard.py</c>). 값 0 은 값을 모르는 것.</summary>
public sealed class ShipyardStock
{
    public int CityId { get; set; }
    public List<System.Text.Json.JsonElement[]> Ships { get; set; } = [];
}

/// <summary>한 도시의 장인들이 파는 선박 부품과 값 — gvdb 의 이용자 보고(<c>tools\gvo\gvdb_partshops.py</c>). [부품 번호, 값] — 값 0 은 모르는 것.</summary>
public sealed class PartShopStock
{
    public int CityId { get; set; }
    public List<int[]> Parts { get; set; } = [];
    /// <summary>그 도시의 장인이 다루는 조선 재료(아이템 번호) — gvdb 에 값이 하나도 없어(197줄 모두 빈칸) 아직 팔지 않는다.</summary>
    public List<int[]> Materials { get; set; } = [];
}

/// <summary>gvdb 의 도시 쪽에서 뽑은 것(<c>tools\gvo\gvdb_towns.py</c>) — 필요 언어(언어 스킬 번호) · 시설(일본어 이름) · 문화권. 쪽을 받아 둔 도시만 있다.</summary>
public sealed class TownFact
{
    public int CityId { get; set; }
    public string Kind { get; set; } = "";
    public List<int> Language { get; set; } = [];
    public string LanguageName { get; set; } = "";
    public List<string> Facilities { get; set; } = [];
    public string Culture { get; set; } = "";
    /// <summary>투자 보수 — 이 도시에 InvestNeed 두캇을 투자하면 받는 아이템(번호). 없으면 0.</summary>
    public int InvestReward { get; set; }
    public int InvestNeed { get; set; }
}

/// <summary>교역 의뢰 하나 — gvdb 의 교역 의뢰 차례 글에서 읽은 것(<c>tools\gvo\gvdb_tradequests.py</c>). 보수 0 은 모르는 것.</summary>
public sealed class TradeQuest
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int Difficulty { get; set; }
    public List<int> Cities { get; set; } = [];
    public int GoodId { get; set; }
    public int Count { get; set; }
    public int ToCity { get; set; }
    public int Reward { get; set; }
    public int Advance { get; set; }
    /// <summary>보고하면 받는 것(gvdb 의 보상 칸) — [갈래(0 아이템 · 1 교역품), 번호, 수].</summary>
    public List<int[]> Gifts { get; set; } = [];
}

/// <summary>해사 의뢰 하나 — gvdb 의 해사 의뢰 차례 글에서 읽은 것(<c>tools\gvo\gvdb_seaquests.py</c>). X · Y 가 0 이면 SeaZone(바다 표 번호)만 안다. 보수 · 내구 · 선원 0 은 모르는 것.</summary>
public sealed class SeaQuest
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int Difficulty { get; set; }
    public List<int> Cities { get; set; } = [];
    public string Ship { get; set; } = "";
    public int CountMin { get; set; }
    public int CountMax { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int SeaZone { get; set; }
    public string Target { get; set; } = "";
    public int Durability { get; set; }
    public int Crew { get; set; }
    public int Reward { get; set; }
    public int Advance { get; set; }
    /// <summary>보고하면 받는 것(gvdb 의 보상 칸) — [갈래(0 아이템 · 1 교역품), 번호, 수].</summary>
    public List<int[]> Gifts { get; set; } = [];
}

/// <summary>낚시로 발견하는 해양생물 하나 — gvdb 의뢰 표의 「釣り（発見物）」 줄(<c>tools\gvo\gvdb_fishfinds.py</c>): 발견물 · 필요 낚시 랭크 · 낚이는 자리(세계 좌표).</summary>
public sealed class FishFind
{
    public int DiscoveryId { get; set; }
    public int Rank { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    /// <summary>침몰선일 때 인양품(gvdb 의 보상 칸에서 아이템 · 교역품으로 이어진 것) — [갈래, 번호, 수].</summary>
    public List<int[]> Gifts { get; set; } = [];
}

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
