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

    // ── 의뢰 없는 상륙 ──
    // 자리를 아는 상륙지에는 의뢰가 없어도 오른다: 물을 긷고(물 +20, 피로 +3), 둘러본다(도적 · 맹수가 나오기도 한다 — 물리치면 모험 경험).
    // 원본은 상륙지에서 걸어 다니며 채집 · 관찰 · 전투를 한다 — 여기서는 창 하나로 줄였고, 얻는 것은 모두 지은 값이다.

    /// <summary>올라 있는 상륙지(의뢰 없이 오른 것).</summary>
    public Dho.Data.LandingData? Ashore { get; private set; }
    private int _ashoreLooked;

    /// <summary>이 뭍에서 둘러볼 수 있는 횟수 — 클라이언트의 관찰 지점 수(표 106, 상륙지마다 넷 · 다섯). 자료가 없는 뭍은 한 번.</summary>
    public int LooksLeft => Ashore is { } site ? Math.Max(1, site.ObservePoints) - _ashoreLooked : 0;
    /// <summary>이 뭍에 채집 지점이 있는가(표 106) — 자료가 없는 뭍(지점이 하나도 안 적힌 곳)은 있는 것으로 친다.</summary>
    public bool GatherHere => Ashore is { } site && (site.GatherKinds.Count > 0 || site.ObservePoints == 0);

    /// <summary>바로 곁의 상륙지 — 의뢰의 상륙지가 닿을 때는 그쪽이 먼저다.</summary>
    public Dho.Data.LandingData? LandingInReach()
    {
        if (Mode != Mode.Sea || SiteInReach()) return null;
        double reach = Settings.LandingRange * Settings.LandingRange;
        return Data.Landings.Where(s => s.X != 0 || s.Y != 0)
            .FirstOrDefault(s => Math.Pow(Dho.Data.WorldMap.DeltaX(ShipX, s.X), 2) + Math.Pow(s.Y - ShipY, 2) < reach);
    }

    public int MaxWaterNow => (int)Rules.MaxWater;

    public void GoAshore()
    {
        if (LandingInReach() is not { } site || Battle is { Result: null }) return;
        (Ashore, _ashoreLooked, _gathered, Sail, Knots) = (site, 0, 0, 0, 0);
        Dialog = Dialog.Ashore;
        Say($"{site.Name}에 상륙했다.");
        Studied("Outdoor");
    }

    public void DrawWater()
    {
        if (Dialog != Dialog.Ashore || Water >= Rules.MaxWater) return;
        Water = Math.Min(Rules.MaxWater, Water + 20);
        Fatigue = Math.Min(100, Fatigue + 3 * (1 - March));
        Say($"물을 길었다. (물 {Water:0})");
    }

    public void LookAround()
    {
        if (Dialog != Dialog.Ashore || LooksLeft <= 0) return;
        _ashoreLooked++;
        TrainEffect("Observe", 10);
        TrainEffect("March", 6);
        if (!_lockForTest && _random.NextDouble() < 0.5 * (1 - March)) { StartLandBattle(-1); return; }
        // 「관찰」(설명: 「뭔가가 있을 것 같은 장소를 알 수 있다」) — 랭크마다 8%로 묻힌 것을 찾아낸다(확률과 얻는 것은 지은 값)
        if (_lockForTest || _random.NextDouble() < Bonus("Observe"))
        {
            int found = 300 * (1 + _random.Next(1, 6));
            // 셋에 하나는 잠긴 궤다(원본 글 3140 · 3142 · 3143 · 3147) — 「자물쇠 따기」가 있어야 열고, 3할 + 랭크마다 7%로 열린다. 열면 다섯 배(확률과 양은 지은 값)
            if (_random.NextDouble() < 1.0 / 3 || _lockForTest)
            {
                _lockForTest = false;
                Say(Text(3140, "무언가를 발견했습니다!자물쇠로 잠겨있는 것 같습니다"));
                if (!Has("Lockpick")) { Say(Text(3147, "자물쇠는 열리지 않았습니다.")); GainExp(0, 10); return; }
                TrainEffect("Lockpick", 20);
                if (_random.NextDouble() >= 0.3 + Bonus("Lockpick")) { Say(Text(3143, "자물쇠 열기에 실패했습니다…….")); GainExp(0, 10); return; }
                found *= 5;
                Say(Text(3142, "자물쇠 여는 방법을 알아냈습니다!"));
                FindWreckPiece("궤 안에서");
            }
            Money += found;
            GainExp(0, 20);
            Say($"수상한 자리를 알아보고 파 보았다 — {found:N0} 두캇을 찾았다. (모험 경험 +20)");
            return;
        }
        GainExp(0, 5);
        Say("둘레를 살폈지만 눈에 띄는 것은 없었다. (모험 경험 +5)");
    }

    // 「행군」(설명: 「육지에서의 피로도 상승률이나 산적 습격률을 억제한다」) — 랭크마다 5%, 6할까지(크기는 지은 값)
    private double March => Math.Min(0.6, Bonus("March"));

    private bool _lockForTest;
    /// <summary>대본용: 다음에 둘러볼 때 아무도 안 나오고 잠긴 궤를 찾는다.</summary>
    public void LockForTest() => _lockForTest = true;

    private int _gathered;
    public const int GatherTimes = 3;

    /// <summary>「채집」 스킬이 있는가 — 상륙 창에 단추가 선다.</summary>
    public bool CanGather => Has("Gather") && GatherHere;
    public int GatherLeft => GatherTimes - _gathered;

    /// <summary>
    /// 채집(스킬 설명: 「여러 가지 재료와 물품을 채집할 수 있다」) — 한 번 오른 뭍에서 세 번까지, 한 번에 피로 +5.
    /// 원본은 상륙지마다 나는 것이 정해져 있다(클라이언트 표 106 · 107 의 채집 지점 — 무엇이 나는지는 못 풀었다).
    /// 여기서는 식료품 · 조미료 · 의약품 · 섬유 갈래의 교역품 가운데 아무것이나 1 + 랭크 ÷ 3 개(지은 값).
    /// </summary>
    public void Gather()
    {
        if (Dialog != Dialog.Ashore || !CanGather || _gathered >= GatherTimes) return;
        if (HoldFree <= 0) { Say("창고가 가득 찼다."); return; }
        _gathered++;
        Fatigue = Math.Min(100, Fatigue + 5 * (1 - March));
        var wild = Data.Goods.Where(g => g.Kind is 0 or 1 or 5 or 6).ToList();
        if (wild.Count == 0) return;
        var good = wild[_random.Next(wild.Count)];
        int count = Math.Min(HoldFree, 1 + (int)Bonus("Gather") / 3 + _random.Next(2));
        GiveGood(good, count);
        GainExp(0, 4);
        TrainEffect("Gather", 12);
        TrainEffect("March", 4);
        Say($"{good.Name} {count}개를 채집했다. (남은 채집 {GatherLeft}번)");
    }
    /// <summary>생명력 — 싸움에서 깎이고 항구에 들면 가득 찬다(지은 것).</summary>
    public double Life { get; private set; } = 100;
    public int MaxLife => 100 + LevelOf(BattleExp).Level * 8;

    // 입은 장비의 공격력(칸 0) · 방어력(칸 1)
    private int WornStat(int stat) =>
        Equipped.Where(id => id > 0 && Items.GetValueOrDefault(id) > 0).Sum(id => GearOf(id) is { } gear && gear.Stats.Count > stat ? gear.Stats[stat] + ForgedOf(id, stat) : 0);
    // 검술 · 응용검술 · 돌격(Melee)과 방어(MeleeGuard) 스킬이 육상전에도 듣는다 — 그 비율만큼
    public int LandAttack => (int)((12 + WornStat(0) + LevelOf(BattleExp).Level + Study("LandAttack") + Study("LandBoth")) * (1 + Bonus("Melee") + Bonus("LandRanged")));      // 저격술 · 던지기 기술 · 활 쏘기 — 육상전에 무기 갈래가 없어 공격력에 그대로 더한다(지은 값)
    public int LandDefense => (int)((WornStat(1) + Study("LandBoth")) * (1 + Bonus("MeleeGuard")));

    private static readonly (string Name, bool Human, double Tough)[] LandFoeKinds =
    [
        ("들개", false, 0.6), ("늑대", false, 0.9), ("멧돼지", false, 1.0), ("도적", true, 1.0),
        ("산적", true, 1.2), ("표범", false, 1.4), ("악어", false, 1.6), ("곰", false, 1.8),
    ];
    private bool _siteCleared;

    /// <summary>탐색하려는데 상대가 막아선다 — 한 상륙에 한 번, 절반쯤의 확률로(지은 값).</summary>
    private bool LandFoeAppears()
    {
        if (_siteCleared || _random.NextDouble() >= 0.5 * (1 - March)) { _siteCleared = true; return false; }
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
            // 「상품지식」(아이템 계통) · 「함정」(함정 계통)의 설명: 「…테크닉 효과를 높인다」 — 테크닉의 갈래가 없어 테크닉 피해에 랭크마다 +4%로 더한다(지은 값)
            double hit = Hit(LandAttack * 1.8 * (1 + Bonus("Technique")), fight.Defense, _random.NextDouble());
            TrainEffect("Technique", 10);
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
        Studied("LandFight");
        TrainEffect("Melee", 10); TrainEffect("MeleeGuard", 6);
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
        if (Dialog == Dialog.LandBattle) Dialog = won && SiteInReach() ? Dialog.Landing : won && Ashore != null ? Dialog.Ashore : Dialog.None;
    }
}
