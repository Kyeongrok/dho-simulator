namespace Dho.Game;

/// <summary>상륙지에서 만난 상대와의 싸움 — 차례마다 공격 · 테크닉 · 방어 · 도망을 고른다.</summary>
internal sealed class LandBattle
{
    public string Name = "";
    public int Level;
    public double Life, MaxLife;
    public int Attack, Defense;
    /// <summary>사람(도적 · 산적)이면 돈을 떨군다.</summary>
    public bool Human;
    public int Round = 1;
    public bool Guarding;
    public readonly List<string> Log = [];
    public string? Result;
    public bool Won;
}

/// <summary>
/// 상륙지의 몬스터(도적 · 맹수)와 육상전. 원본의 몬스터 표(이름 · 능력치 · 나오는 곳)는 클라이언트에서 못 찾았다 —
/// 상대의 갈래 · 능력치 · 나오는 잦기 · 피해 · 보상은 모두 지은 것이다. 테크닉의 이름만 클라이언트 표(51)의 것이다.
/// 이쪽의 공격력 · 방어력은 입은 장비의 수치(장비 표 15)를 더한 것이다.
/// </summary>
internal sealed partial class Voyage
{
    public LandBattle? LandFight { get; private set; }
    /// <summary>생명력 — 싸움에서 깎이고 항구에 들면 가득 찬다(지은 것).</summary>
    public double Life { get; private set; } = 100;
    public int MaxLife => 100 + LevelOf(BattleExp).Level * 8;

    // 입은 장비의 공격력(칸 0) · 방어력(칸 1)
    private int WornStat(int stat) =>
        Equipped.Where(id => id > 0 && Items.GetValueOrDefault(id) > 0).Sum(id => GearOf(id) is { } gear && gear.Stats.Count > stat ? gear.Stats[stat] : 0);
    public int LandAttack => 12 + WornStat(0) + LevelOf(BattleExp).Level;
    public int LandDefense => WornStat(1);

    private static readonly (string Name, bool Human, double Tough)[] LandFoeKinds =
    [
        ("들개", false, 0.6), ("늑대", false, 0.9), ("멧돼지", false, 1.0), ("도적", true, 1.0),
        ("산적", true, 1.2), ("표범", false, 1.4), ("악어", false, 1.6), ("곰", false, 1.8),
    ];
    private bool _siteCleared;

    /// <summary>탐색하려는데 상대가 막아선다 — 한 상륙에 한 번, 절반쯤의 확률로(지은 값).</summary>
    private bool LandFoeAppears()
    {
        if (_siteCleared || _random.NextDouble() >= 0.5) { _siteCleared = true; return false; }
        StartLandBattle(-1);
        return true;
    }

    /// <summary>kind 가 0 이상이면 그 갈래로(대본용).</summary>
    public void StartLandBattle(int kind)
    {
        int level = Math.Max(1, LevelOf(BattleExp).Level + _random.Next(-1, 3));
        var (name, human, tough) = LandFoeKinds[kind >= 0 ? Math.Min(kind, LandFoeKinds.Length - 1) : _random.Next(LandFoeKinds.Length)];
        // 사람은 클라이언트의 꾸밈말을 얹는다(「난폭한 도적」)
        string title = human && Data.Npcs.Adjectives.Count > 0 ? $"{Data.Npcs.Adjectives[_random.Next(Data.Npcs.Adjectives.Count)]} {name}" : name;
        double life = (50 + level * 12) * tough;
        LandFight = new LandBattle
        {
            Name = title, Level = level, Human = human, Life = life, MaxLife = life,
            Attack = (int)((6 + level * 2) * tough), Defense = (int)(level * 1.5 * tough),
        };
        Life = Math.Clamp(Life, 1, MaxLife);
        LandFight.Log.Add($"{title}(Lv {level})이(가) 앞을 막아섰다!");
        Say(LandFight.Log[0]);
        Cues.Enqueue("Alarm");
        Dialog = Dialog.LandBattle;
    }

    private static double Hit(double attack, double defense, double roll) => Math.Max(1, attack * (0.8 + roll * 0.4) - defense * 0.5);

    /// <summary>kind: 0 공격 · 1 테크닉(행동력 10) · 2 방어 · 3 도망.</summary>
    public void LandAct(int kind)
    {
        if (LandFight is not { Result: null } fight) return;
        fight.Guarding = false;
        if (kind == 0)
        {
            double hit = Hit(LandAttack, fight.Defense, _random.NextDouble());
            fight.Life -= hit;
            fight.Log.Add($"{fight.Round}합: 공격 — {fight.Name}에게 {hit:0}");
        }
        else if (kind == 1)
        {
            if (!SpendVigour(10)) { fight.Log.Add("행동력이 모자라다."); return; }
            double hit = Hit(LandAttack * 1.8, fight.Defense, _random.NextDouble());
            fight.Life -= hit;
            string technique = Data.Npcs.Techniques.Count > 0 ? Data.Npcs.Techniques[_random.Next(Math.Min(12, Data.Npcs.Techniques.Count))] : "테크닉";
            fight.Log.Add($"{fight.Round}합: 「{technique}」 — {fight.Name}에게 {hit:0}");
        }
        else if (kind == 2)
        {
            fight.Guarding = true;
            fight.Log.Add($"{fight.Round}합: 방어 자세를 잡았다.");
        }
        else if (_random.NextDouble() < 0.6)
        {
            fight.Result = "배로 달아났다.";
            Say(fight.Result);
            return;
        }
        else fight.Log.Add($"{fight.Round}합: 달아나지 못했다.");

        if (fight.Life > 0)
        {
            double hurt = Hit(fight.Attack, LandDefense, _random.NextDouble()) * (fight.Guarding ? 0.4 : 1);
            Life -= hurt;
            fight.Log.Add($"{fight.Name}의 공격 — 생명력 −{hurt:0}");
        }
        fight.Round++;
        if (fight.Log.Count > 9) fight.Log.RemoveRange(0, fight.Log.Count - 9);

        if (fight.Life <= 0)
        {
            int exp = 8 + fight.Level * 4, money = fight.Human ? 200 + fight.Level * 150 + _random.Next(300) : 0;
            Money += money;
            fight.Won = true;
            fight.Result = $"{fight.Name}을(를) 물리쳤다!{(money > 0 ? $" {money:N0} 두캇을 얻었다." : "")} (전투 경험 +{exp * GainFactor})";
            Say(fight.Result);
            GainExp(2, exp, Math.Max(1, exp / 5));
            Cues.Enqueue("Done");
            _siteCleared = true;
        }
        else if (Life <= 0)
        {
            // 졌다 — 배로 실려 온다. 가진 돈을 조금 털리고 피로가 쌓인다
            int lost = fight.Human ? (int)Math.Min(Money * 0.02, 20_000) : 0;      // 돈은 사람(도적 · 산적)만 털어 간다
            Money -= lost;
            Life = 1;
            Fatigue = Math.Min(100, Fatigue + 20);
            fight.Result = $"{fight.Name}에게 쓰러져 배로 실려 왔다." + (lost > 0 ? $" {lost:N0} 두캇을 잃었다." : "");
            Say(fight.Result);
        }
    }

    public void EndLandBattle()
    {
        bool won = LandFight?.Won == true;
        LandFight = null;
        // 이겼으면 상륙지에 그대로 — 다시 탐색할 수 있다. 졌거나 달아났으면 배로
        if (Dialog == Dialog.LandBattle) Dialog = won && SiteInReach() ? Dialog.Landing : Dialog.None;
    }
}
