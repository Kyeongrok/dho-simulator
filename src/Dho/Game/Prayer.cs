namespace Dho.Game;

/// <summary>
/// 기도 효과 — 교회 · 모스크 · 사원에서 헌금하고 기도하면 한동안 항해에 가호가 붙는다.
/// 원본 화면 글에 「기도 효과」(636)와, 그 바로 앞에 나란히 「재해 수호」(607) · 「피로 경감」(608) · 「선원들 장악」(609) · 「포탄 회피」(610)가 있다 —
/// 이 넷이 기도 효과의 갈래라는 것은 번호가 붙어 있는 데서 나온 **짐작**이다.
/// 지은 것: 헌금 1,000 두캇, 넷 가운데 하나가 아무렇게나 붙어 30일 간다, 효과의 크기(재해 −30%, 피로 −30%, 잃는 선원 −30%, 받는 포격 −10%).
/// </summary>
internal sealed partial class Voyage
{
    public const int PrayerFee = 1000, PrayerDays = 30;
    private static readonly (uint Text, string Name, string Note)[] Prayers =
    [
        (607, "재해 수호", "재해가 덜 일어난다"), (608, "피로 경감", "피로가 덜 쌓인다"), (609, "선원들 장악", "잃는 선원이 준다"), (610, "포탄 회피", "받는 포격이 준다"),
    ];

    /// <summary>붙어 있는 기도 효과(0 ~ 3) — 없으면 −1. 끝나는 날.</summary>
    public int Prayer { get; private set; } = -1;
    public int PrayerUntil { get; private set; }

    public bool PrayerOn(int kind) => Prayer == kind && (int)Today < PrayerUntil;
    public string PrayerName => Prayer >= 0 && (int)Today < PrayerUntil ? Text(Prayers[Prayer].Text, Prayers[Prayer].Name) : "";
    public string PrayerNote => PrayerName == "" ? "" : $"{Text(636, "기도 효과")}: {PrayerName} — {Prayers[Prayer].Note} ({PrayerUntil - (int)Today}일 남음)";

    /// <summary>헌금하고 기도한다 — 피로가 풀리고 기도 효과 하나가 붙는다.</summary>
    public void Pray(string host)
    {
        Fatigue = 0;
        if (Money < PrayerFee) { Say($"{host}와(과) 함께 기도를 올렸다. 피로가 풀렸다. (헌금 {PrayerFee:N0} 두캇이 있으면 기도 효과를 받는다)"); return; }
        Money -= PrayerFee;
        (Prayer, PrayerUntil) = (_random.Next(Prayers.Length), (int)Today + PrayerDays);
        Cues.Enqueue("Done");
        Say($"{host}와(과) 함께 기도를 올렸다. (헌금 {PrayerFee:N0} 두캇) {PrayerNote}");
    }

    /// <summary>대본용: 기도한다(갈래를 정할 수 있다).</summary>
    public void PrayForTest(int kind)
    {
        Pray("신부");
        if (kind >= 0 && Prayer >= 0) Prayer = Math.Min(kind, Prayers.Length - 1);
    }
}
