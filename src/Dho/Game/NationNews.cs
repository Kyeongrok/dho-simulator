using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 나라의 정세 — 때때로 어느 나라에 일이 생겨 그 나라 항구의 관세나 시세가 한동안 달라진다.
/// 원본의 글: 화면 글 9542 · 9543 「오른쪽에 속한 도시에서 %s의 항해자에게 대상 교역품 관세 증가 / 감소」, 자료 표 86 의 효과 이름(「관세 증가」 · 「시장 고등」 …).
/// 원본에서는 나라끼리의 일(대투자전 · 칙명 따위)의 결과로 붙는다 — 그 얼개는 클라이언트에 없다.
/// 지은 것: 스무 날에 한 번쯤 아무 나라에 셋 가운데 하나(관세 증가 +5%p · 관세 감소 −5%p · 시장 고등 값 +15%)가 20일 동안 붙는다.
/// </summary>
internal sealed partial class Voyage
{
    private static readonly string[] NewsNames = ["관세 증가", "관세 감소", "시장 고등"];

    /// <summary>지금의 정세 — (나라, 갈래 0 ~ 2, 끝나는 날). 없으면 나라 0.</summary>
    public (int Nation, int Kind, int Until) News { get; private set; }
    private int _newsCheckedDay = -1;

    public bool NewsOn => News.Nation != 0 && (int)Today < News.Until;
    public string NewsNote => !NewsOn ? "" : $"{Data.Nations.Find(n => n.Id == News.Nation)?.Name} — {NewsNames[News.Kind]} ({News.Until - (int)Today}일)";

    private double NewsTax(CityData city) => NewsOn && city.Nation == News.Nation ? News.Kind switch { 0 => 0.05, 1 => -0.05, _ => 0 } : 0;
    private double NewsPrice(CityData city) => NewsOn && city.Nation == News.Nation && News.Kind == 2 ? 1.15 : 1;

    // 날이 바뀔 때마다 — 정세가 없으면 하루 5%로 새 일이 생긴다
    private void UpdateNews(int days)
    {
        if (NewsOn) return;
        for (int day = 0; day < days; day++)
        {
            if (_random.NextDouble() >= 0.05) continue;
            var nations = Data.Start.Nations.Select(n => n.NationId).ToList();
            if (nations.Count == 0) return;
            News = (nations[_random.Next(nations.Count)], _random.Next(NewsNames.Length), (int)Today + 20);
            Say($"소문: {NewsNote} — 그 나라의 항구에서 {(News.Kind == 2 ? "교역품 값이 오른다" : "관세가 달라진다")}.");
            return;
        }
    }

    /// <summary>대본용: 정세를 정한다.</summary>
    public void NewsForTest(int nation, int kind) => News = (nation, Math.Clamp(kind, 0, 2), (int)Today + 20);
}
