namespace Dho.Game;

/// <summary>
/// 보험과 선원 급료.
/// 보험 — 원본 화면 글: 1225 「보험」, 1226 「보험(최고보상액:%d두캇)」, 1227 「계약보험」, 7103 「계약할 보험을 결정하십시오. 미 계약을 선택하면 계약을 해제할 수 있습니다」,
/// 7119 「보험을 해약하시겠습니까?」, 3014 「보험금으로 %d두캇을 받았습니다.」. 은행에서 계약하고, 난파하거나 해전에서 져서 잃은 돈을 최고보상액까지 돌려받는다.
/// 지은 것: 세 등급(최고보상액 5만 · 50만 · 500만, 보험료 2천 · 1만 5천 · 10만 두캇), 보험료는 계약할 때 한 번 내고 한 번 받으면 계약이 끝난다.
/// 선원 급료 — 원본 글 3041 「더 이상 선원들에게 급료를 지불할 수 없습니다.」, 표 50 의 「항해 중에 발생하는 선원 급여，부관 급여 및 보험료」.
/// 바다에서 날마다 선원 한 사람에 1 두캇(지은 값)이 나간다. 못 주면 피로가 하루에 5씩 더 쌓인다(지은 값).
/// </summary>
internal sealed partial class Voyage
{
    public static readonly (int Most, int Fee)[] Insurances = [(0, 0), (50_000, 2_000), (500_000, 15_000), (5_000_000, 100_000)];

    /// <summary>계약한 보험의 등급(0 없음).</summary>
    public int Insurance { get; private set; }
    public string InsuranceName => Insurance == 0 ? "미 계약" : Fill(Text(1226, "보험(최고보상액:%d두캇)"), $"{Insurances[Insurance].Most:N0}");

    /// <summary>다음 등급으로 계약을 바꾼다(끝 등급 다음은 해약) — 보험료는 새로 낸다. 돈이 모자라면 해약된다.</summary>
    public void NextInsurance()
    {
        if (Mode != Mode.Port) return;
        int next = (Insurance + 1) % Insurances.Length;
        if (next != 0 && Money < Insurances[next].Fee) next = 0;
        if (next == Insurance) { Cues.Enqueue("Error"); return; }
        Money -= Insurances[next].Fee;
        Insurance = next;
        Cues.Enqueue("Buy");
        Say(next == 0 ? "보험을 해약했다." : $"{Text(1227, "계약보험")}: {InsuranceName} — 보험료 {Insurances[next].Fee:N0} 두캇.");
    }

    // 잃은 돈을 보험이 메운다 — 받은 글(없으면 빈 글)
    private string PayInsurance(int lost)
    {
        if (Insurance == 0 || lost <= 0) return "";
        int paid = Math.Min(lost, Insurances[Insurance].Most);
        Money += paid;
        Insurance = 0;
        return "\n" + Fill(Text(3014, "보험금으로 %d두캇을 받았습니다."), $"{paid:N0}");
    }

    private double _crewPay;
    private int _unpaidDay = -1;
    public const double CrewWage = 1;

    // 바다에서 흐른 날만큼 선원 급료가 나간다
    private void PayCrew(double days)
    {
        _crewPay += Crew * CrewWage * days;
        if (_crewPay < 1) return;
        int pay = (int)_crewPay;
        _crewPay -= pay;
        if (Money >= pay) { Money -= pay; return; }
        Money = 0;
        Fatigue = Math.Min(100, Fatigue + 5 * days);
        if (_unpaidDay == (int)Today) return;
        _unpaidDay = (int)Today;
        Say(Text(3041, "선원들에게 줄 급료가 떨어졌다."));
    }
}
