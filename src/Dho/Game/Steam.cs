namespace Dho.Game;

/// <summary>
/// 증기선 — 사용자가 준 글(인벤 2597/2557 「선박 증기선」, 2026-10-08)의 규칙.
/// 증기선은 다섯 척(클레르몽 · 신형/개량/개조/개장 사바나)이고, 석탄 연료(아이템 1504012 — 클라이언트 설명 「증기선박에 연료로 사용되는 석탄」,
/// 도구점 10만 두캇 · gvdb 판매 표)를 하루치 넘게 가지고 있어야 증기선 효과를 본다. 모자라면 패널티.
/// 글에 수가 있는 것: 하루 소모 20개(항해기술 · 관리기술이 10 이상이면 18, 관리기술 15랭부터 −1) · 한 사람이 200개까지 · 출항한 날(0일째)은 안 쓴다 ·
/// 연료가 있으면 가속이 최대이고 높은 파도 · 횡파 · 돌풍을 막는다.
/// 짐작 · 지은 값: 연료가 없을 때의 속도 ×0.3(글: 「기본 돛 성능의 1/3 ~ 1/4 수준? — 정확한 값은 실험 측정 필요」) · 「가속 최대」를 붙는 빠르기 네 곱으로 옮긴 것.
/// 안 넣은 것: 마스터 오브 스팀 호칭(1/3 절약) · 자국 산업혁명(5 · 3 · 2개) · 증기기사의 기술 · 증기선 이벤트(건조 · 승선 조건) · 「피로 상승이 준다」(크기 없음) — 이 게임에 그 바탕이 없다.
/// </summary>
internal sealed partial class Voyage
{
    public const int CoalItem = 1504012, CoalLimit = 200;
    private static readonly string[] Steamships = ["클레르몽", "신형 사바나", "개량 사바나", "개조 사바나", "개장 사바나"];

    public bool OnSteamship => Array.IndexOf(Steamships, Ship.Name) >= 0;
    public int Coal => Items.GetValueOrDefault(CoalItem);

    /// <summary>바다에서 하루에 드는 석탄 연료 — 글의 표(자국 산업혁명 X 줄): 20 · 항해기술과 관리기술이 10 이상이면 18 · 관리기술 15랭부터 −1.</summary>
    public int CoalPerDay
    {
        get
        {
            int sailing = Data.Skills.Find(s => s.Name == "항해기술") is { } a ? Rank(a.Id) : 0, managing = Data.Skills.Find(s => s.Name == "관리기술") is { } b ? Rank(b.Id) : 0;
            return (sailing >= 10 && managing >= 10 ? 18 : 20) - (managing >= 15 ? 1 : 0);
        }
    }

    /// <summary>증기선 효과가 듣는가 — 증기선에 타고 석탄 연료가 하루치 이상.</summary>
    public bool SteamOn => OnSteamship && Coal >= CoalPerDay;
    /// <summary>증기선의 속도 배율 — 연료가 모자라면 0.3(짐작).</summary>
    public double SteamSpeed => OnSteamship && !SteamOn ? 0.3 : 1;
    /// <summary>속도가 붙는 빠르기의 배율 — 증기선 효과가 들으면 네 곱(「가속도 최대」를 옮긴 지은 값).</summary>
    public double SteamHaste => SteamOn ? 4 : 1;

    // 항해 일수가 바뀔 때(서 있어도) 하루치를 태운다. 모자라면 그대로 두고 알린다
    /// <summary>대본용 — 하루가 바뀐 것으로 치고 석탄을 태운다.</summary>
    public void BurnCoalForTest() => BurnCoal();

    private void BurnCoal()
    {
        if (!OnSteamship) return;
        int need = CoalPerDay;
        if (Coal >= need)
        {
            SpendItem(CoalItem, need);
            Say($"석탄 연료 {need}개를 태웠다. (남은 것 {Coal}개" + (Coal < need ? " — 하루치가 안 된다" : "") + ")");
        }
        else { Say($"석탄 연료가 모자라다({Coal}/{need}) — 증기선의 속도가 크게 떨어진다."); Cues.Enqueue("Warn"); }
    }
}
