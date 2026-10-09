using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 생산 랭작을 돕는 것들 — 번개 시리즈(숙련도 +100%) · 헤파이스토스의 가호 · 만복과 위장약 · 부관식.
/// 있는 것과 하는 일은 클라이언트의 아이템 · 스킬 설명 글과 gvdb 의 효과 글, 사용자가 준 생산 랭작 글(2026-10-08)의 것이고,
/// 수치(가호의 확률 · 만복이 될 확률과 가는 동안 · 부관식이 주는 경험)는 자료가 없어 지은 값이다.
/// </summary>
internal sealed partial class Voyage
{
    // ── 번개 시리즈: 「일정 시간 스킬 숙련도 +100%」 — 이름의 시간(1시간 … 30일)만큼. 시간은 게임을 켜 둔 시간으로 센다(원본은 실제 시간 — 줄인 것)
    private double _charmUntil;
    public double CharmLeft => Math.Max(0, _charmUntil - Clock);
    // 덤의 크기(%) — 번개 100 · 뇌수 10 · 뇌왕 30 · 뇌신 50(gvdb 글). 크기가 다른 것을 겹쳐 쓰면 새것으로 바뀌고 시간도 새로 센다(원본이 어떻게 겹치는지는 모른다 — 짐작)
    private int _charmPower = 100;
    private double CharmFactor => Clock < _charmUntil ? 1 + _charmPower / 100.0 : 1;

    private void UseExpCharm(ItemData charm)
    {
        int power = charm.Power > 0 ? charm.Power : 100;
        _charmUntil = (power == _charmPower ? Math.Max(Clock, _charmUntil) : Clock) + charm.Amount * 3600;
        _charmPower = power;
        Cues.Enqueue("Skill");
        Say($"{charm.Name}을(를) 썼다. 스킬 숙련도 +{power}% — 남은 시간 {LeftText(CharmLeft)}.");
    }

    // ── 바다짐승 시리즈(해수 +10% · 해왕 +30% — gvdb 「○時間、経験値を10/30％多く獲得できる」): 모험 · 교역 · 전투 경험치가 그만큼 더 붙는다.
    // 시간과 겹쳐 쓰는 법은 숙련도 부적과 같다(켜 둔 시간으로 센다 · 크기가 다르면 새것으로 — 줄인 것 · 짐작)
    private double _levelCharmUntil;
    private int _levelCharmPower;
    public double LevelCharmLeft => Math.Max(0, _levelCharmUntil - Clock);
    private double LevelCharmFactor => Clock < _levelCharmUntil ? 1 + _levelCharmPower / 100.0 : 1;

    private void UseLevelCharm(ItemData charm)
    {
        _levelCharmUntil = (charm.Power == _levelCharmPower ? Math.Max(Clock, _levelCharmUntil) : Clock) + charm.Amount * 3600;
        _levelCharmPower = charm.Power;
        Cues.Enqueue("Skill");
        Say($"{charm.Name}을(를) 썼다. 경험치 +{charm.Power}% — 남은 시간 {LeftText(LevelCharmLeft)}.");
    }

    /// <summary>글에 적는 경험치 — 모드의 배수와 경험치 부적의 덤을 받은 값(GainExp 가 실제로 더하는 값).</summary>
    public int ExpShown(int exp) => (int)Math.Round(Math.Max(0, exp) * GainFactor * LevelCharmFactor);

    /// <summary>지금 듣고 있는 부적 · 베일과 남은 시간 — HUD 의 이름 옆에 보인다(없으면 빈 글).</summary>
    public string BuffText => string.Join("  ", new[]
    {
        CharmLeft > 0 ? $"숙련도 +{_charmPower}% {LeftText(CharmLeft)}" : "",
        LevelCharmLeft > 0 ? $"경험치 +{_levelCharmPower}% {LeftText(LevelCharmLeft)}" : "",
        VeilLeft > 0 ? $"재해 회피 {LeftText(VeilLeft)}" : "",
    }.Where(t => t != ""));

    public static string LeftText(double seconds) => seconds >= 86400 ? $"{seconds / 86400:0.#}일" : seconds >= 3600 ? $"{seconds / 3600:0.#}시간" : $"{Math.Ceiling(seconds / 60):0}분";

    // ── 천사의 베일(역천사 30분 · 주천사 1시간 · 치천사 1일 — gvdb 「自然災害回避 — 嵐や吹雪を除く自然災害から守る」): 그 동안 높은 파도 · 횡파 · 돌풍이 안 든다.
    // 시간은 번개 시리즈처럼 게임을 켜 둔 시간으로 센다(줄인 것)
    private double _veilUntil;
    public double VeilLeft => Math.Max(0, _veilUntil - Clock);

    private void UseVeil(ItemData veil)
    {
        _veilUntil = Math.Max(Clock, _veilUntil) + veil.Amount * 3600;
        Cues.Enqueue("Skill");
        Say($"{veil.Name}을(를) 썼다. 자연재해 회피 — 남은 시간 {LeftText(VeilLeft)}.");
    }

    // ── 헤파이스토스의 가호(스킬 645): 「가호의 랭크에 따라 생산 시 일정 확률로 행동력을 소비하지 않는다」.
    // 확률은 자료가 없다 — 지은 값: 랭크마다 25%(3랭 75%. 글: 「3랭이면 행동력 음식 없이 랭작한다」). 원본은 켜서 일정 시간 가는 스킬인데 여기서는 가지고 있으면 늘 듣는다(줄인 것)
    public const int HephaestusSkill = 645;
    public double VigourFreeChance => Math.Min(0.75, Rank(HephaestusSkill) * 0.25);

    // 생산 times 번 가운데 가호로 행동력이 안 든 번 수
    private int VigourFreeTimes(int times)
    {
        double chance = VigourFreeChance;
        return chance <= 0 ? 0 : Enumerable.Range(0, times).Count(_ => _random.NextDouble() < chance);
    }

    // ── 만복: 주점에서 요리를 먹으면 만복이 될 수 있고(스킬 3088 「주점에서 음식을 먹을 때 만복 또는 만취 상태가 될 확률이 낮아진다」), 그 동안은 주점에서 더 못 먹는다.
    // 위장약(1500261, gvdb 「満腹解消」)이 푼다. 확률 30%(스킬 랭크마다 −5%) · 가는 동안 5분은 지은 값
    private double _stuffedUntil;
    public bool Stuffed => Clock < _stuffedUntil;
    public double StuffedLeft => Math.Max(0, _stuffedUntil - Clock);
    public const int LightEaterSkill = 3088;
    private double StuffedChance => Math.Max(0, 0.30 - Rank(LightEaterSkill) * 0.05);

    private void MaybeStuffed()
    {
        if (_random.NextDouble() >= StuffedChance) return;
        _stuffedUntil = Clock + 300;
        Say("배가 꽉 찼다 — 만복 상태. 한동안 주점에서 더 먹을 수 없다(위장약으로 푼다).");
    }

    /// <summary>대본용 — 만복 상태로.</summary>
    public void StuffedForTest() => _stuffedUntil = Clock + 300;

    // ── 부관식(歓待料理): gvdb 「特性上昇補助 — 副官のレベル上昇時に特性が上昇しやすくなる」. 이 게임의 부관에는 특성이 없어 그 담당 부관의 경험으로 옮긴다(지은 것 — 한 번에 경험 30).
    // Amount: 담당 번호(0 항해장 · 2 회계사 · 3 창고당번 · 5 선의 — 표 36 의 차례) · 9 는 모든 부관 · 8 은 부관의 피로(이 게임에는 부관의 피로가 없어 신뢰도 +2 로 옮긴다 — 지은 것)
    private bool UseAideMeal(ItemData meal)
    {
        int duty = (int)meal.Amount;
        var fed = Aides.Where(a => duty >= 8 || a.Duty == duty).ToList();
        if (fed.Count == 0) { Say(Aides.Count == 0 ? $"{meal.Name} — 대접할 부관이 없다." : $"{meal.Name} — {DutyName(duty)}을(를) 맡은 부관이 없다."); Cues.Enqueue("Error"); return false; }
        foreach (var aide in fed)
        {
            if (duty == 8) { aide.Trust = Math.Min(100, aide.Trust + 2); continue; }
            AideGain(aide, AideMainKind(aide), 30);
        }
        Cues.Enqueue("Eat");
        Say($"{meal.Name}을(를) {string.Join(" · ", fed.Select(a => a.Who.Name))}에게 대접했다.");
        return true;
    }
}
