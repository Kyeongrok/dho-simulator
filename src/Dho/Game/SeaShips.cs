using Dho.Data;

namespace Dho.Game;

/// <summary>바다를 다니는 다른 배 한 척.</summary>
internal sealed class SeaShip
{
    public ShipData Ship = null!;
    public string Name = "";
    /// <summary>0 상선 · 1 해적 · 2 군함.</summary>
    public int Kind;
    public int NationId;
    public double X, Y, Heading, Knots, Cruise;
    public double Durability, Crew;
    public int MaxDurability, MaxCrew, Guns, Armor;
    /// <summary>해적이 이쪽을 노리고 쫓아온다.</summary>
    public bool Hunting;
    public double TurnIn;
}

/// <summary>벌어진 해전 — 차례마다 포격 · 백병전 · 도주를 고른다.</summary>
internal sealed class SeaBattle
{
    public SeaShip Foe = null!;
    public int Round = 1;
    public readonly List<string> Log = [];
    /// <summary>끝났으면 그 까닭 글(이겼다 · 달아났다).</summary>
    public string? Result;
}

/// <summary>
/// 바다의 다른 배와 해전. 원본은 여럿이 실시간으로 싸우지만 여기서는 혼자 하는 게임에 맞춰
/// 차례를 주고받는 간단한 싸움으로 줄였다 — 배가 나타나는 잦기, 피해 · 전리품 · 경험의 수는 모두 지은 것이다.
/// 이쪽은 단 대포(부품)의 문 수와 관통력으로 쏘고, 다른 배는 포문 수만큼 팰콘포를 실은 것으로 친다.
/// </summary>
internal sealed partial class Voyage
{
    public List<SeaShip> SeaShips { get; } = [];
    public SeaBattle? Battle { get; private set; }

    public static readonly string[] SeaShipKinds = ["상선", "해적", "군함"];
    private const double SeaShipSight = 22, SeaShipReach = 3.5;      // 배 한 척의 길이가 세계 좌표로 1 쯤이다
    private double _seaShipIn = 4;

    private static readonly string[] PirateNames = ["붉은 수염", "검은 갈매기", "바다 늑대", "피의 닻", "외눈 상어", "밤의 조류", "녹슨 갈고리", "떠도는 해골"];
    private static readonly string[] MerchantNames = ["산타 마리아", "성 니콜라스", "황금 사슴", "북방의 별", "행운의 여신", "흰 돌고래", "성 안토니오", "바다의 딸"];

    private void UpdateSeaShips(double dt)
    {
        SeaShips.RemoveAll(s => Distance(s) > SeaShipSight || s.Durability <= 0);
        _seaShipIn -= dt;
        if (_seaShipIn <= 0)
        {
            _seaShipIn = 15 + _random.NextDouble() * 25;
            if (SeaShips.Count < 3) SpawnSeaShip();
        }
        foreach (var ship in SeaShips)
        {
            double dx = WorldMap.DeltaX(ship.X, ShipX), dy = ShipY - ship.Y, far = Math.Sqrt(dx * dx + dy * dy);
            if (ship.Kind == 1 && !ship.Hunting && !Data.Settings.ModNoPirates && far < 9 && Strength(ship) >= MyStrength * 0.6)
            {
                ship.Hunting = true;
                Say($"해적선 「{ship.Name}」이(가) 다가온다!");
                Cues.Enqueue("Alarm");
            }
            if (ship.Hunting) ship.Heading = Turned(ship.Heading, Normalize(Math.Atan2(dx, -dy)), 0.5 * dt);
            else if ((ship.TurnIn -= dt) <= 0)
            {
                ship.TurnIn = 8 + _random.NextDouble() * 14;
                ship.Heading = Normalize(ship.Heading + (_random.NextDouble() - 0.5) * 0.9);
            }
            ship.Knots += ((ship.Hunting ? ship.Cruise * 1.25 : ship.Cruise) - ship.Knots) * Math.Min(1, dt * 0.5);
            double step = ship.Knots * Settings.UnitsPerKnotSecond * dt;
            double nx = ship.X + Math.Sin(ship.Heading) * step, ny = ship.Y - Math.Cos(ship.Heading) * step;
            // 뭍에 닿으면 돌아선다
            if (Map.IsLand(nx, ny) || Map.IsLand(nx + Math.Sin(ship.Heading) * 2, ny - Math.Cos(ship.Heading) * 2))
            {
                ship.Heading = Normalize(ship.Heading + 1.1);
                ship.Knots *= 0.5;
            }
            else (ship.X, ship.Y) = (WorldMap.WrapX(nx), ny);
            if (ship.Hunting && far < 1.6 && Battle == null && Dialog == Dialog.None) StartBattle(ship, false);
        }
    }

    private static double Turned(double from, double to, double most)
    {
        double turn = Normalize(to - from + Math.PI) - Math.PI;
        return Normalize(from + Math.Clamp(turn, -most, most));
    }

    private double Distance(SeaShip ship)
    {
        double dx = WorldMap.DeltaX(ShipX, ship.X), dy = ship.Y - ShipY;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double Strength(SeaShip ship) => ship.MaxDurability + ship.Guns * 12 + ship.MaxCrew * 2;
    private double MyStrength => Stats.Durability + GunsFitted * 12 + Stats.MaxCrew * 2;

    /// <summary>대본용: 가까이에 배 한 척을 띄운다(kind 가 0 이상이면 그 갈래로).</summary>
    public void SpawnForTest(int kind) => SpawnSeaShip(kind, 3);

    private void SpawnSeaShip(int wanted = -1, double near = 0)
    {
        for (int tries = 0; tries < 8; tries++)
        {
            double angle = _random.NextDouble() * Math.Tau, far = near > 0 ? near : 10 + _random.NextDouble() * 5;
            double x = WorldMap.WrapX(ShipX + Math.Sin(angle) * far), y = ShipY - Math.Cos(angle) * far;
            if (Map.IsLand(x, y) || Map.IsLand(x + 2, y) || Map.IsLand(x - 2, y) || Map.IsLand(x, y + 2) || Map.IsLand(x, y - 2)) continue;
            double draw = _random.NextDouble();
            int kind = wanted >= 0 ? wanted : draw < 0.62 ? 0 : draw < 0.82 ? 1 : 2;
            // 상점에서 파는 배 가운데 이쪽 급까지 — 상선은 교역 레벨을, 해적 · 군함은 전투 레벨을 가장 많이 요구하는 배(그 배의 쓰임으로 본다)
            var hulls = Data.Ships.Where(s => s.Model > 0 && s.SizeClass <= Ship.SizeClass && ShipStats.Facts.TryGetValue(s.Name, out var fact) && fact.Price > 0
                                              && (kind == 0 ? fact.Trade >= fact.Battle && fact.Trade >= fact.Adventure : fact.Battle > fact.Trade && fact.Battle > fact.Adventure)
                                              && ShipStats.Of(s, Settings.Ships).Durability <= Stats.Durability * 1.3).ToList();
            if (hulls.Count == 0) return;
            var hull = hulls[_random.Next(hulls.Count)];
            var stats = ShipStats.Of(hull, Settings.Ships);
            var nations = Data.Nations.Where(n => n.Id is >= 1 and <= 7).ToList();
            int nation = kind == 1 || nations.Count == 0 ? 0 : nations[_random.Next(nations.Count)].Id;
            SeaShips.Add(new SeaShip
            {
                Ship = hull, Kind = kind, NationId = nation,
                Name = SeaShipName(kind, nation),
                X = x, Y = y, Heading = Normalize(angle + Math.PI + (_random.NextDouble() - 0.5) * 1.6),
                Cruise = stats.Knots * (0.45 + _random.NextDouble() * 0.2),
                Durability = stats.Durability, MaxDurability = stats.Durability,
                Crew = stats.MaxCrew * (kind == 0 ? 0.5 : 0.85), MaxCrew = stats.MaxCrew,
                Guns = kind == 0 ? stats.Guns / 3 : stats.Guns, Armor = stats.Armor,
                TurnIn = 6 + _random.NextDouble() * 10,
            });
            return;
        }
    }

    // 이름은 클라이언트의 NPC 이름 표로 짓는다: 상선 = 배 이름(표 41), 해적 = 꾸밈말 + 이름씨(표 95 · 96 — 「난폭한 해적」), 가끔 이름난 선장(표 72).
    // 표가 없으면 지어 둔 이름을 쓴다. 꾸밈말 + 이름씨가 원본에서 바로 이 쓰임인지는 짐작이다
    private string SeaShipName(int kind, int nation)
    {
        var names = Data.Npcs;
        string Pick(List<string> list) => list[_random.Next(list.Count)];
        if (kind == 0) return names.ShipNames.Count > 0 ? Pick(names.ShipNames) : MerchantNames[_random.Next(MerchantNames.Length)];
        if (kind == 2) return $"{Data.Nations.Find(n => n.Id == nation)?.Name} 순찰함";
        if (names.Captains.Count > 0 && _random.NextDouble() < 0.1) return Pick(names.Captains);
        return names.Adjectives.Count > 0 && names.Nouns.Count > 0 ? $"{Pick(names.Adjectives)} {Pick(names.Nouns)}" : PirateNames[_random.Next(PirateNames.Length)];
    }

    /// <summary>싸움을 걸 수 있을 만큼 가까운 배 — 제 나라의 배는 치지 않는다.</summary>
    public SeaShip? ShipInReach() =>
        Mode != Mode.Sea ? null : SeaShips.Where(s => (s.Kind == 1 || s.NationId != NationId) && Distance(s) < SeaShipReach).MinBy(Distance);

    public void Attack()
    {
        if (ShipInReach() is { } ship && Battle == null) StartBattle(ship, true);
    }

    private void StartBattle(SeaShip foe, bool mine)
    {
        Battle = new SeaBattle { Foe = foe };
        Battle.Log.Add(mine ? $"{SeaShipKinds[foe.Kind]} 「{foe.Name}」({foe.Ship.Name})에 싸움을 걸었다." : $"해적선 「{foe.Name}」({foe.Ship.Name})이(가) 덤벼들었다!");
        Say(Battle.Log[0]);
        Cues.Enqueue("Alarm");
        Dialog = Dialog.Battle;
    }

    // 한 번의 포격이 주는 피해 — 포문 수에 비례하고 장갑이 깎는다(지은 식)
    private double Salvo(int guns, int armor) => guns * (6 + _random.NextDouble() * 5) / (1 + armor / 25.0);

    private void FoeFires(SeaBattle battle)
    {
        var foe = battle.Foe;
        if (foe.Guns <= 0 || foe.Durability <= 0 || foe.Crew < 1) return;
        double hit = Salvo((int)(foe.Guns * Math.Clamp(foe.Crew / Math.Max(1, foe.MaxCrew * 0.4), 0.3, 1)), Stats.Armor), dead = hit * 0.04;
        Durability -= hit;
        Crew -= dead;
        battle.Log.Add($"적의 포격 — 내구 −{hit:0}, 선원 −{dead:0}");
    }

    /// <summary>kind: 0 포격 · 1 백병전 · 2 도주.</summary>
    public void BattleAct(int kind)
    {
        if (Battle is not { Result: null } battle) return;
        var foe = battle.Foe;
        if (kind == 0)
        {
            // 단 대포의 문 수만큼 쏜다 — 관통력 30 이 기준(팰콘포)
            double hit = Salvo((int)(GunsFitted * Math.Clamp(Crew / Math.Max(1, Stats.MinCrew), 0.3, 1)), foe.Armor) * Math.Max(0.3, GunPierce / 30), dead = hit * 0.04;
            foe.Durability -= hit;
            foe.Crew -= dead;
            battle.Log.Add($"{battle.Round}합: 포격 — 적의 내구 −{hit:0}, 선원 −{dead:0}");
            FoeFires(battle);
        }
        else if (kind == 1)
        {
            // 서로 선원 수만큼 벤다 — 전투 레벨이 조금 거든다
            double edge = 1 + LevelOf(BattleExp).Level * 0.01;
            double theirs = Crew * (0.10 + _random.NextDouble() * 0.10) * edge, mine = foe.Crew * (0.10 + _random.NextDouble() * 0.10);
            foe.Crew -= theirs;
            Crew -= mine;
            battle.Log.Add($"{battle.Round}합: 백병전 — 적의 선원 −{theirs:0}, 우리 선원 −{mine:0}");
        }
        else
        {
            double chance = Math.Clamp(0.35 + (Stats.Knots - foe.Cruise * 1.8) * 0.06, 0.1, 0.9);
            if (_random.NextDouble() < chance)
            {
                battle.Result = "적을 따돌리고 달아났다.";
                foe.Hunting = false;
                foe.Heading = Normalize(Heading + Math.PI);
                (foe.X, foe.Y) = (WorldMap.WrapX(foe.X + Math.Sin(foe.Heading) * 3), foe.Y - Math.Cos(foe.Heading) * 3);
                Say(battle.Result);
                return;
            }
            battle.Log.Add($"{battle.Round}합: 달아나지 못했다.");
            FoeFires(battle);
        }
        battle.Round++;
        if (battle.Log.Count > 9) battle.Log.RemoveRange(0, battle.Log.Count - 9);

        if (Durability <= 0 || Crew < 1)
        {
            // 졌다 — 난파와 같게 다룬다
            Battle = null;
            SeaShips.Clear();
            if (!UseLifebuoy()) Wreck($"{SeaShipKinds[foe.Kind]} 「{foe.Name}」");
            else Dialog = Dialog.None;
            return;
        }
        if (foe.Durability > 0 && foe.Crew >= 1) return;

        // 이겼다 — 돈과(상선이면) 짐, 전투 경험과 명성
        int exp = 10 + foe.MaxDurability / 20 + foe.Guns, fame = Math.Max(1, exp / 4);
        int money = 500 + foe.MaxDurability * (foe.Kind == 0 ? 20 : 8);
        Money += money;
        string loot = "";
        if (foe.Kind == 0 && HoldFree > 0 && Data.Goods.Count > 0)
        {
            var good = Data.Goods[_random.Next(Data.Goods.Count)];
            int count = Math.Min(HoldFree, 5 + _random.Next(16));
            GiveGood(good, count);
            loot = $", {good.Name} {count}개";
        }
        battle.Result = $"{(foe.Durability <= 0 ? "적선을 가라앉혔다" : "적선을 빼앗았다")}! {money:N0} 두캇{loot}을(를) 얻었다. (전투 경험 +{exp * GainFactor}, 명성 +{fame})";
        Say(battle.Result);
        foe.Durability = 0;
        GainExp(2, exp, fame);
        Cues.Enqueue("Done");
    }

    public void EndBattle()
    {
        Battle = null;
        if (Dialog == Dialog.Battle) Dialog = Dialog.None;
    }
}
