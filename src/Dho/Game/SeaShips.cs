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
    /// <summary>군함 위장에 속았다 — 덤비지 않는다. Seen 은 이미 이쪽을 알아봤다(다시 속지 않는다).</summary>
    public bool Fooled, Seen;
    public double TurnIn;
}

/// <summary>날아가는 포탄 한 발(세계 좌표) — 그리는 데만 쓴다.</summary>
internal sealed class SeaShot
{
    public double FromX, FromY, ToX, ToY, Age, Life;
}

/// <summary>배 위에 잠깐 뜨는 피해 글.</summary>
internal sealed class SeaHit
{
    public double X, Y, Age;
    public string Text = "";
    public bool OnMe;
}

/// <summary>벌어진 해전 — 바다 위에서 그대로 움직이며 싸운다.</summary>
internal sealed class SeaBattle
{
    public SeaShip Foe = null!;
    public readonly List<string> Log = [];
    /// <summary>끝났으면 그 까닭 글(이겼다 · 달아났다).</summary>
    public string? Result;
    /// <summary>다시 쏠 수 있을 때까지 남은 초.</summary>
    public double MyReload, FoeReload;
    /// <summary>백병전 중 — 두 배가 붙어 선원끼리 싸운다.</summary>
    public bool Boarding;
    public double MeleeIn;
    public readonly List<SeaShot> Shots = [];
    public readonly List<SeaHit> Hits = [];
}

/// <summary>
/// 바다의 다른 배와 해전. 해전은 바다 위에서 그대로 벌어진다 — 배를 몰아 옆구리(현측)를 적에게 돌리고,
/// 사정 안에 들면 포를 쏜다(스페이스). 적의 뱃머리나 꼬리 쪽에서 쏘면 더 아프다(관통). 가까이 붙으면 백병전을 건다.
/// 원본의 짜임(현측포 · 사정 · 장전 · 백병전)을 흉내 낸 것이고, 배가 나타나는 잦기 · 피해 · 장전 시간 · 전리품 · 경험의 수는 모두 지은 것이다.
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
            // 군함 위장: 해적이 이쪽을 처음 알아볼 때 한 번 속는다 — 속으면 그 배는 끝내 덤비지 않는다
            if (ship.Kind == 1 && !ship.Hunting && !ship.Fooled && !ship.Seen && far < 9 && Option("Disguise") > 0 && _random.NextDouble() < Option("Disguise"))
            {
                ship.Fooled = true;
                Say($"해적선 「{ship.Name}」이(가) 군함인 줄 알고 비켜 간다. (군함 위장)");
            }
            else if (ship.Kind == 1 && !ship.Hunting && !ship.Fooled && far < 9) ship.Seen = true;
            if (ship.Kind == 1 && !ship.Hunting && !ship.Fooled && !Data.Settings.ModNoPirates && far < 9 && Strength(ship) >= MyStrength * 0.6)
            {
                ship.Hunting = true;
                Say($"해적선 「{ship.Name}」이(가) 다가온다!");
                Cues.Enqueue("Alarm");
            }
            bool fighting = Battle is { Result: null } fight && fight.Foe == ship;
            if (fighting)
            {
                // 싸우는 배: 멀면 다가오고, 사정 안에 들면 옆구리를 이쪽으로 돌린다. 백병전 중에는 선다
                double toMe = Normalize(Math.Atan2(dx, -dy));
                double side = Normalize(toMe + (Normalize(ship.Heading - toMe) < Math.PI ? Math.PI / 2 : -Math.PI / 2));
                ship.Heading = Turned(ship.Heading, far > FoeRange * 0.8 ? toMe : side, 0.45 * dt);
            }
            else if (ship.Hunting) ship.Heading = Turned(ship.Heading, Normalize(Math.Atan2(dx, -dy)), 0.5 * dt);
            else if ((ship.TurnIn -= dt) <= 0)
            {
                ship.TurnIn = 8 + _random.NextDouble() * 14;
                ship.Heading = Normalize(ship.Heading + (_random.NextDouble() - 0.5) * 0.9);
            }
            double wanted = fighting && Battle!.Boarding ? 0 : ship.Hunting || fighting ? ship.Cruise * 1.25 : ship.Cruise;
            ship.Knots += (wanted - ship.Knots) * Math.Min(1, dt * 0.5);
            double step = ship.Knots * Settings.UnitsPerKnotSecond * dt;
            double nx = ship.X + Math.Sin(ship.Heading) * step, ny = ship.Y - Math.Cos(ship.Heading) * step;
            // 뭍에 닿으면 돌아선다
            if (Map.IsLand(nx, ny) || Map.IsLand(nx + Math.Sin(ship.Heading) * 2, ny - Math.Cos(ship.Heading) * 2))
            {
                ship.Heading = Normalize(ship.Heading + 1.1);
                ship.Knots *= 0.5;
            }
            else (ship.X, ship.Y) = (WorldMap.WrapX(nx), ny);
            if (ship.Hunting && far < 6 && Battle == null && Dialog == Dialog.None) StartBattle(ship, false);
        }
        UpdateBattle(dt);
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
        Battle = new SeaBattle { Foe = foe, MyReload = 1.5, FoeReload = 3 };
        Battle.Log.Add(mine ? $"{SeaShipKinds[foe.Kind]} 「{foe.Name}」({foe.Ship.Name})에 싸움을 걸었다." : $"해적선 「{foe.Name}」({foe.Ship.Name})이(가) 덤벼들었다!");
        Say(Battle.Log[0]);
        Say("해전 — 옆구리를 적에게 돌리고 스페이스로 포를 쏜다. 붙으면 백병전을 건다.");
        Cues.Enqueue("Alarm");
    }

    private const double ReloadSeconds = 6, FoeReloadSeconds = 7, FoeRange = 5, BoardReach = 1.6, EscapeReach = 15;

    /// <summary>이쪽 포의 사정(세계 좌표) — 단 대포의 사정거리 평균(360 이 5쯤).</summary>
    public double GunRange => Parts.Where(p => p.Slot == 4).Sum(p => p.A) is > 0 and var guns ? Parts.Where(p => p.Slot == 4).Sum(p => p.A * p.C) / (double)guns / 72 : 0;

    // 한 번의 포격이 주는 피해 — 포문 수에 비례하고 장갑이 깎는다(지은 식)
    private double Salvo(int guns, int armor) => guns * (6 + _random.NextDouble() * 5) / (1 + armor / 25.0);

    // 쏘는 배의 옆구리가 표적을 보는가 — 뱃머리에서 잰 표적의 방향이 좌우 90° 의 ±55° 안
    private static bool Broadside(double heading, double bearing) => Math.Abs(Math.Sin(bearing - heading)) > 0.57;

    // 포탄이 표적의 뱃머리 · 꼬리 쪽에서 들어오는가(관통) — 배의 길이를 따라 훑는다
    private static bool Raking(double targetHeading, double bearing) => Math.Abs(Math.Cos(bearing - targetHeading)) > 0.85;

    /// <summary>지금 포를 못 쏘는 까닭 — 쏠 수 있으면 null.</summary>
    public string? FireBlocker()
    {
        if (Battle is not { Result: null } battle) return "싸움이 없다";
        if (battle.Boarding) return "백병전 중이다";
        if (GunsFitted <= 0) return "단 대포가 없다";
        if (battle.MyReload > 0) return "장전 중";
        var foe = battle.Foe;
        if (Distance(foe) > GunRange) return "사정 밖";
        double bearing = Math.Atan2(WorldMap.DeltaX(ShipX, foe.X), -(foe.Y - ShipY));
        return Broadside(Heading, bearing) ? null : "옆구리를 적에게 돌려야 한다";
    }

    /// <summary>포를 쏜다(스페이스) — 옆구리가 적을 보고 사정 안이어야 한다.</summary>
    public void Fire()
    {
        if (Battle is not { Result: null } battle) return;
        if (FireBlocker() is { } why)
        {
            if (why != "장전 중") Cues.Enqueue("Error");
            return;
        }
        var foe = battle.Foe;
        double bearing = Math.Atan2(WorldMap.DeltaX(ShipX, foe.X), -(foe.Y - ShipY));
        bool rake = Raking(foe.Heading, bearing);
        double hit = Salvo((int)(GunsFitted * Math.Clamp(Crew / Math.Max(1, Stats.MinCrew), 0.3, 1)), foe.Armor) * Math.Max(0.3, GunPierce / 30) * (rake ? 1.5 : 1);
        double dead = hit * 0.04;
        foe.Durability -= hit;
        foe.Crew -= dead;
        battle.MyReload = ReloadSeconds;
        battle.Shots.Add(new SeaShot { FromX = ShipX, FromY = ShipY, ToX = foe.X, ToY = foe.Y, Life = 0.7 });
        battle.Hits.Add(new SeaHit { X = foe.X, Y = foe.Y, Text = $"{(rake ? "관통! " : "")}−{hit:0}" });
        battle.Log.Add($"포격{(rake ? "(관통)" : "")} — 적의 내구 −{hit:0}, 선원 −{dead:0}");
        Cues.Enqueue("Cannon");
        CheckBattleEnd(battle);
    }

    /// <summary>백병전을 건다 — 적선에 바짝 붙어 있어야 한다.</summary>
    public string? BoardBlocker() =>
        Battle is not { Result: null } battle ? "싸움이 없다" : battle.Boarding ? "이미 백병전 중이다" : Distance(battle.Foe) > BoardReach ? "더 가까이 붙어야 한다" : null;

    public void Board()
    {
        if (Battle is not { } battle || BoardBlocker() != null) return;
        (battle.Boarding, battle.MeleeIn) = (true, 0.5);
        battle.Log.Add("백병전을 걸었다!");
        Say("백병전!");
        Sail = 0;
    }

    private void UpdateBattle(double dt)
    {
        if (Battle is not { Result: null } battle) return;
        var foe = battle.Foe;
        foreach (var shot in battle.Shots) shot.Age += dt;
        battle.Shots.RemoveAll(s => s.Age > s.Life);
        foreach (var hit in battle.Hits) hit.Age += dt;
        battle.Hits.RemoveAll(h => h.Age > 1.6);
        battle.MyReload = Math.Max(0, battle.MyReload - dt);
        battle.FoeReload = Math.Max(0, battle.FoeReload - dt);
        double far = Distance(foe);

        if (battle.Boarding)
        {
            // 서로 선원 수만큼 벤다 — 전투 레벨이 조금 거든다
            if ((battle.MeleeIn -= dt) > 0) return;
            battle.MeleeIn = 1.5;
            double edge = 1 + LevelOf(BattleExp).Level * 0.01;
            double theirs = Crew * (0.08 + _random.NextDouble() * 0.08) * edge, mine = foe.Crew * (0.08 + _random.NextDouble() * 0.08);
            foe.Crew -= theirs;
            Crew -= mine;
            battle.Hits.Add(new SeaHit { X = foe.X, Y = foe.Y, Text = $"선원 −{theirs:0}" });
            battle.Hits.Add(new SeaHit { X = ShipX, Y = ShipY, Text = $"선원 −{mine:0}", OnMe = true });
            battle.Log.Add($"백병전 — 적의 선원 −{theirs:0}, 우리 선원 −{mine:0}");
            CheckBattleEnd(battle);
            return;
        }

        // 적의 포격 — 옆구리가 이쪽을 보고 사정 안이면 쏜다
        double bearing = Math.Atan2(WorldMap.DeltaX(foe.X, ShipX), -(ShipY - foe.Y));
        if (battle.FoeReload <= 0 && foe.Guns > 0 && far <= FoeRange && Broadside(foe.Heading, bearing))
        {
            bool rake = Raking(Heading, bearing);
            // 적은 한쪽 옆구리의 포만 쏜다 — 포문 수의 절반
            double hit = Salvo((int)(foe.Guns / 2 * Math.Clamp(foe.Crew / Math.Max(1, foe.MaxCrew * 0.4), 0.3, 1)), Stats.Armor) * (rake ? 1.5 : 1), dead = hit * 0.04;
            Durability -= hit;
            Crew -= dead;
            battle.FoeReload = FoeReloadSeconds;
            battle.Shots.Add(new SeaShot { FromX = foe.X, FromY = foe.Y, ToX = ShipX, ToY = ShipY, Life = 0.7 });
            battle.Hits.Add(new SeaHit { X = ShipX, Y = ShipY, Text = $"{(rake ? "관통! " : "")}−{hit:0}", OnMe = true });
            battle.Log.Add($"적의 포격{(rake ? "(관통)" : "")} — 내구 −{hit:0}, 선원 −{dead:0}");
            Cues.Enqueue("Cannon");
        }
        if (far > EscapeReach)
        {
            battle.Result = "싸움터를 벗어났다.";
            foe.Hunting = false;
            Say(battle.Result);
            Dialog = Dialog.Battle;
            return;
        }
        CheckBattleEnd(battle);
    }

    private void CheckBattleEnd(SeaBattle battle)
    {
        var foe = battle.Foe;
        if (battle.Log.Count > 9) battle.Log.RemoveRange(0, battle.Log.Count - 9);
        if (Durability <= 0 || Crew < 1)
        {
            // 졌다 — 가까운 도시로 끌려간다
            Battle = null;
            SeaShips.Clear();
            if (!UseLifebuoy()) Wreck($"{SeaShipKinds[foe.Kind]} 「{foe.Name}」");
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
        Dialog = Dialog.Battle;
    }

    public void EndBattle()
    {
        Battle = null;
        if (Dialog == Dialog.Battle) Dialog = Dialog.None;
    }
}
