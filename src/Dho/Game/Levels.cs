namespace Dho.Game;

//// <summary>
//// 모험 · 교역 · 전투의 경험치와 레벨, 레벨업.
//// 레벨 표(레벨 n 까지 50 × n²)와 얻는 양은 지은 것이다. 전투가 없어서 전투 경험은 재해를 이겨 낼 때 조금 들어온다(이것도 지은 것).
//// 레벨이 오르면 알림 · 소리가 나고 행동력이 가득 찬다(행동력 회복은 지은 것).
//// </summary>
internal sealed partial class Voyage
{
    public static readonly string[] ExpNames = ["모험", "교역", "전투"];
    public int BattleExp { get; private set; }
    public int TradeFame { get; private set; }
    public int BattleFame { get; private set; }
    // 화면 가운데에 잠깐 뜨는 레벨업 알림(글, 몇 번째 알림인가 — 화면이 새 알림을 알아채는 데 쓴다)
    public (string Text, int Count) LevelNotice { get; private set; } = ("", 0);

    public int ExpOf(int kind) => kind switch { 0 => AdventureExp, 1 => TradeExp, _ => BattleExp };
    public int FameOf(int kind) => kind switch { 0 => AdventureFame, 1 => TradeFame, _ => BattleFame };

    // 경험과 명성을 얻는다(kind: 0 모험 · 1 교역 · 2 전투). 경험은 모드의 배수를 받는다. 레벨이 오르면 알린다
    public void GainExp(int kind, int exp, int fame = 0)
    {
        if (exp <= 0 && fame <= 0) return;
        int before = LevelOf(ExpOf(kind)).Level;
        exp = ExpShown(exp);      // 바다짐승 시리즈를 쓴 동안 경험치 +10/30%
        fame = Math.Max(0, fame);
        if (kind == 0) (AdventureExp, AdventureFame) = (AdventureExp + exp, AdventureFame + fame);
        else if (kind == 1) (TradeExp, TradeFame) = (TradeExp + exp, TradeFame + fame);
        else (BattleExp, BattleFame) = (BattleExp + exp, BattleFame + fame);
        // 부관도 선장이 얻은 경험의 한 몫을 그 갈래로 얻는다 — 몫은 모드 창에서 정한다(기본 10% — 지은 값)
        if (exp > 0) foreach (var aide in Aides) AideGain(aide, Math.Clamp(kind, 0, 2), exp * Math.Clamp(Data.Settings.ModAideShare, 0, 100) / 100.0);
        int after = LevelOf(ExpOf(kind)).Level;
        if (after <= before) return;
        Say($"레벨 업! {ExpNames[kind]} 레벨 {after}");
        LevelNotice = ($"{ExpNames[kind]} 레벨 {after}", LevelNotice.Count + 1);
        Cues.Enqueue("SkillUp");
        GainVigour(MaxVigour);
    }
}