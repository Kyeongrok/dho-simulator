using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 호칭 — 이룬 일에 따라 얻고, 하나를 골라 내건다. 이름과 설명은 클라이언트 표 35(92줄)의 것이다.
/// 무엇을 얼마나 해야 얻는가는 표에 없다 — 설명 글이 말하는 일을 이 게임에서 셀 수 있는 것만 골라 문턱을 지었다(지은 값).
/// 내건 호칭의 효과도 설명 글에 적힌 것 가운데 이 게임에 있는 것만 옮겼다.
/// </summary>
internal sealed partial class Voyage
{
    /// <summary>내건 호칭(표의 번호) — 0 이면 없음.</summary>
    public int Honor { get; private set; }
    /// <summary>물리친 해적 · 군함 · 바다 괴물의 수.</summary>
    public int PirateWins { get; private set; }
    public int NavyWins { get; private set; }

    // (호칭 번호, 조건 글, 채웠는가) — 장인급을 랭크 15 로 본 것, 수들은 모두 지은 값
    private (int Id, string Need, Func<bool> Met)[] HonorRules =>
    [
        (1, "조리 랭크 15", () => RankByName("조리") >= 15), (2, "주조 랭크 15", () => RankByName("주조") >= 15), (3, "봉제 랭크 15", () => RankByName("봉제") >= 15),
        (4, "공예 랭크 15", () => RankByName("공예") >= 15), (5, "보관 랭크 15", () => RankByName("보관") >= 15), (6, "연금술 랭크 15", () => RankByName("연금술") >= 15),
        (11, "해적 30척을 물리친다", () => PirateWins >= 30),
        (27, "대학 연구 20가지를 마친다", () => StudyDone.Count >= 20),
        (33, "발견 50가지를 보고한다", () => _done.Count >= 50),
        (34, "은행 예금 1억 두캇", () => Savings >= 100_000_000),
        (35, "군함 30척을 물리친다", () => NavyWins >= 30),
        (76, "해적 300척을 물리친다", () => PirateWins >= 300),
        (62, "한 도시에 5천만 두캇을 투자한다", () => Invested.Values.Any(v => v >= 50_000_000)),
        (63, "세 도시에 5천만 두캇씩 투자한다", () => Invested.Values.Count(v => v >= 50_000_000) >= 3),
    ];

    private int RankByName(string skill) => Data.Skills.Find(s => s.Name == skill) is { } found ? Rank(found.Id) : 0;

    public NamedData? HonorData(int id) => Data.Honors.Find(h => h.Id == id);
    public string HonorName => Honor == 0 ? "" : HonorData(Honor)?.Name ?? "";

    /// <summary>얻을 수 있는 호칭들 — (호칭, 조건 글, 얻었는가).</summary>
    public List<(NamedData Honor, string Need, bool Earned)> Honors() =>
        HonorRules.Select(r => (HonorData(r.Id), r.Need, r.Met())).Where(r => r.Item1 != null).Select(r => (r.Item1!, r.Need, r.Item3)).ToList();

    public void WearHonor(int id)
    {
        if (id != 0 && !HonorRules.Any(r => r.Id == id && r.Met())) return;
        Honor = id;
        Say(id == 0 ? "호칭을 내렸다." : $"호칭 「{HonorName}」을(를) 내걸었다.");
    }

    /// <summary>내건 호칭의 효과 — 희대의 자산가: 흥정 폭 +2%p, 역전의 명제독: 받는 포격 −10%, 명예 시장: 투자 공적 +20% · +50%(크기는 지은 값).</summary>
    public double HonorEffect(string effect) => (Honor, effect) switch
    {
        (34, "Haggle") => 0.02, (35, "ShotArmor") => 0.10, (62, "Invest") => 0.2, (63, "Invest") => 0.5, _ => 0,
    };

    public static string HonorNote(int id) => id switch { 34 => "흥정 폭 +2%p", 35 => "받는 포격 −10%", 62 => "투자 공적 +20%", 63 => "투자 공적 +50%", _ => "" };
}
