using Dho.Data;

namespace Dho.Game;

/// <summary>고용한 부관 한 사람.</summary>
internal sealed class Aide
{
    public required NamedData Who { get; init; }
    /// <summary>담당 — 표 36 의 번호(0 항해장 … 5 선의).</summary>
    public int Duty { get; set; }
    public int Level { get; set; } = 1;
    public double Exp { get; set; }
}

/// <summary>
/// 부관 — 주점에서 고용해 담당을 맡긴다. 이름 32명(표 131)과 담당 여섯(표 36)은 클라이언트의 것이고,
/// 고용비 · 급여 · 담당의 효과 · 성장은 지은 규칙이다.
/// </summary>
internal sealed partial class Voyage
{
    public const int AideSlots = 2, AideMaxLevel = 20;

    public List<Aide> Aides { get; } = [];

    public string DutyName(int duty) => Data.Duties.Find(d => d.Id == duty)?.Name ?? $"담당 {duty}";

    public static string DutyNote(int duty) => duty switch
    {
        0 => "속도가 오른다",
        1 => "재해가 덜 난다",
        2 => "교역소 값이 좋아진다",
        3 => "물과 식량이 덜 준다",
        4 => "선원이 덜 지친다",
        5 => "선원이 덜 준다",
        _ => "",
    };

    /// <summary>이 도시의 주점에서 만날 수 있는 후보 — 도시마다 셋(늘 같은 사람들), 이미 고용한 사람은 뺀다.</summary>
    public List<NamedData> AidesToHire()
    {
        if (Data.Aides.Count == 0) return [];
        var random = new Random(City.Id * 31 + 7);
        return Data.Aides.OrderBy(_ => random.Next()).Take(3).Where(a => Aides.All(h => h.Who.Id != a.Id)).ToList();
    }

    /// <summary>주점이 있는 도시에서만 고용한다(시내 지도에 주점 표식이 있는가). 지도가 없는 도시는 된다고 본다.</summary>
    public bool HasTavern => TownMap is not { Marks.Count: > 0 } map || map.Marks.Any(m => m.Place == 13);

    public int AideCost(NamedData who) => 8000 + who.Id % 7 * 1500;

    /// <summary>바다에서 하루에 나가는 급여.</summary>
    public int AidePay(Aide aide) => 30 + aide.Level * 12;

    public string? AideBlocker(NamedData who)
    {
        if (!HasTavern) return "이 도시에는 주점이 없다";
        if (Aides.Count >= AideSlots) return "부관 자리가 찼다";
        if (Money < AideCost(who)) return "돈이 모자라다";
        return null;
    }

    public void HireAide(NamedData who)
    {
        if (Mode != Mode.Port || AideBlocker(who) != null) return;
        Money -= AideCost(who);
        // 아직 아무도 안 맡은 담당부터
        int duty = Enumerable.Range(0, 6).FirstOrDefault(d => Aides.All(a => a.Duty != d));
        Aides.Add(new Aide { Who = who, Duty = duty });
        Say($"{who.Name}을(를) 부관으로 고용했다. 담당: {DutyName(duty)}");
    }

    public void DismissAide(Aide aide)
    {
        if (Aides.Remove(aide)) Say($"{aide.Who.Name}을(를) 해고했다.");
    }

    /// <summary>담당을 다음 것으로 바꾼다.</summary>
    public void NextDuty(Aide aide) => aide.Duty = (aide.Duty + 1) % 6;

    /// <summary>그 담당을 맡은 부관들의 효과 합 — 한 사람이 (기본 + 레벨 × 늘어나는 몫).</summary>
    private double AideEffect(int duty, double basis, double perLevel) =>
        Aides.Where(a => a.Duty == duty).Sum(a => basis + a.Level * perLevel);

    public double AideSpeed => 1 + AideEffect(0, 0.03, 0.004);
    public double AideLuck => 1 - Math.Min(0.5, AideEffect(1, 0.10, 0.01));
    public double AideHaggle => AideEffect(2, 0.01, 0.002);
    public double AideRation => 1 - Math.Min(0.4, AideEffect(3, 0.08, 0.008));
    public double AideFatigue => 1 - Math.Min(0.5, AideEffect(4, 0.10, 0.01));
    public double AideCrewLoss => 1 - Math.Min(0.5, AideEffect(5, 0.12, 0.012));

    /// <summary>바다에서 흐른 날만큼 급여가 나가고 경험이 쌓인다.</summary>
    private void UpdateAides(double days)
    {
        foreach (var aide in Aides)
        {
            _aidePay += AidePay(aide) * days;
            if (aide.Level >= AideMaxLevel) continue;
            aide.Exp += days * 10 * GainFactor;
            while (aide.Level < AideMaxLevel && aide.Exp >= aide.Level * 40)
            {
                aide.Exp -= aide.Level * 40;
                aide.Level++;
                Say($"부관 {aide.Who.Name}의 레벨이 {aide.Level}(이)가 되었다.");
            }
        }
        // 급여는 두캇 단위로 모아서 뗀다
        if (_aidePay >= 1) { int pay = (int)_aidePay; _aidePay -= pay; Money = Math.Max(0, Money - pay); }
    }

    private double _aidePay;

    // ── 지방함대 ──
    // 부관을 그 지역의 일(독초 조사 · 밤도둑 토벌 · 해적단 거점 조사 …)에 내보낸다. 일의 이름과 잘됐을 때 · 안됐을 때의 글은 클라이언트 표 113 의 것이다.
    // 원본의 지방함대가 어떻게 도는지(누구를 며칠 보내고 무엇을 받는가)는 모른다 — 하루에 한 번, 부관의 레벨로 성패가 갈리고 돈 · 공적 · 부관 경험을 받는 것은 지은 규칙이다.

    private int _fleetDay = -1;
    public (FleetMission Mission, bool Success, string Reward, string Who)? FleetResult { get; private set; }

    /// <summary>이 도시에서 오늘 맡을 수 있는 일 셋.</summary>
    public List<FleetMission> FleetOffers()
    {
        var pick = new Random(City.Id * 131 + (int)Today);
        return Data.FleetMissions.OrderBy(_ => pick.Next()).Take(3).ToList();
    }

    public string? FleetBlocker(Aide? aide) =>
        Mode != Mode.Port ? "항구에서만 내보낸다" : aide == null ? "부관이 없다" : _fleetDay == (int)Today ? "오늘은 이미 내보냈다 — 내일 다시" : null;

    public static int FleetChance(Aide aide) => Math.Min(95, 50 + aide.Level * 2);

    public void SendFleet(Aide aide, FleetMission mission)
    {
        if (FleetBlocker(aide) != null) return;
        _fleetDay = (int)Today;
        bool success = _random.Next(100) < FleetChance(aide);
        int money = success ? 2000 + aide.Level * 500 + _random.Next(2000) : 0, merit = success ? 1 : 0;
        Money += money;
        Merit += merit;
        aide.Exp += (success ? 30 : 10) * GainFactor;
        while (aide.Level < AideMaxLevel && aide.Exp >= aide.Level * 40) { aide.Exp -= aide.Level * 40; aide.Level++; Say($"부관 {aide.Who.Name}의 레벨이 {aide.Level}(이)가 되었다."); }
        string reward = success ? $"사례금 {money:N0} 두캇 · 공적 +{merit} · 부관 경험 +{30 * GainFactor}" : $"부관 경험 +{10 * GainFactor}";
        FleetResult = (mission, success, reward, aide.Who.Name);
        Say($"지방함대 「{mission.Name}」 — {(success ? "잘 끝났다" : "잘되지 않았다")}. ({reward})");
        Cues.Enqueue(success ? "Done" : "Error");
        Dialog = Dialog.FleetReport;
        if (TitleDue) Say("당신의 올린 공적에 대한 작위가 수여된다고 합니다. 자국 본거지의 투자를 받고있는 인물을 만나러 갑시다.");
    }
}
