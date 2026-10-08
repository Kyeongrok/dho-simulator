using Dho.Data;

namespace Dho.Game;

/// <summary>바다를 다니는 다른 배 한 척.</summary>
internal sealed class SeaShip
{
    public ShipData Ship = null!;
    public string Name = "";
    /// <summary>0 상선 · 1 해적 · 2 군함.</summary>
    public int Kind;
    /// <summary>바다 괴물이면 그 갈래(1 상어 · 2 크라켄) — Kind 는 3 이다. 배가 아니라 제 모습으로 그린다.</summary>
    public int Monster;
    public int NationId;
    public double X, Y, Heading, Knots, Cruise;
    public double Durability, Crew;
    public int MaxDurability, MaxCrew, Guns, Armor;
    /// <summary>해적이 이쪽을 노리고 쫓아온다.</summary>
    public bool Hunting;
    /// <summary>군함 위장에 속았다 — 덤비지 않는다. Seen 은 이미 이쪽을 알아봤다(다시 속지 않는다).</summary>
    public bool Fooled, Seen;
    /// <summary>가라앉기 시작한 뒤 흐른 초 — 0 보다 작으면 떠 있다. 가라앉는 모습을 그리는 데 쓴다.</summary>
    public double Sinking = -1;
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
    public double MeleeIn, RamIn;
    /// <summary>백병전의 전술 — 0 돌격, 1 방어, 2 총격. 돌격은 총격을, 방어는 돌격을, 총격은 방어를 누른다.</summary>
    public int Tactic;
    /// <summary>적이 다시 백병전을 걸어 올 때까지 · 내가 다시 퇴각을 꾀할 수 있을 때까지 남은 초.</summary>
    public double BoardIn, RetreatIn;
    /// <summary>싸움을 시작할 때의 선원 수와, 외과의술이 다음에 듣기까지 남은 초.</summary>
    public double CrewAtStart, SurgeryIn = 5;
    /// <summary>바다에 깔아 둔 기뢰(자리와, 깔고 지난 초)와 다음 것을 깔 수 있을 때까지 남은 초.</summary>
    public readonly List<(double X, double Y, double Age)> Mines = [];
    public double MineIn;
    /// <summary>부른 원군이 앞으로 쏠 횟수와 다음 포격까지 남은 초 — 한 싸움에 한 번만 부른다.</summary>
    public int AidLeft;
    public double AidIn;
    public bool AidCalled;
    /// <summary>적이 깔아 둔 기뢰와, 적이 다음 것을 깔 때까지 남은 초.</summary>
    public readonly List<(double X, double Y, double Age)> FoeMines = [];
    public double FoeMineIn = 6;
    public readonly List<SeaShot> Shots = [];
    public readonly List<SeaHit> Hits = [];
    /// <summary>이 싸움에서 아이템(돌격의 군기 · 강철포탄 · 원군요청서)으로 빌린 스킬 — 랭크 1 로 친다.</summary>
    public readonly HashSet<int> Lent = [];
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

    public static readonly string[] SeaShipKinds = ["상선", "해적", "군함", "바다 괴물"];
    private const double SeaShipSight = 22, SeaShipReach = 3.5;      // 배 한 척의 길이가 세계 좌표로 1 쯤이다
    private double _seaShipIn = 4;

    private static readonly string[] PirateNames = ["붉은 수염", "검은 갈매기", "바다 늑대", "피의 닻", "외눈 상어", "밤의 조류", "녹슨 갈고리", "떠도는 해골"];
    private static readonly string[] MerchantNames = ["산타 마리아", "성 니콜라스", "황금 사슴", "북방의 별", "행운의 여신", "흰 돌고래", "성 안토니오", "바다의 딸"];

    private double _seaQuestIn;

    private void UpdateSeaShips(double dt)
    {
        UpdateInfamy();
        // 진 배는 여섯 초에 걸쳐 가라앉은 뒤 사라진다
        foreach (var sunk in SeaShips.Where(s => s.Sinking >= 0)) sunk.Sinking += dt;
        SeaShips.RemoveAll(s => Distance(s) > SeaShipSight || s.Sinking > 6);
        _seaShipIn -= dt;
        if (_seaShipIn <= 0)
        {
            _seaShipIn = 15 + _random.NextDouble() * 25;
            if (SeaShips.Count < 3) SpawnSeaShip();
        }
        // 해사 의뢰의 토벌 대상 — 네 초마다 살핀다(앞의 대상을 가라앉힌 뒤 다음 것이 나오기까지의 틈, 지은 값)
        if ((_seaQuestIn -= dt) <= 0) { _seaQuestIn = 4; SeaQuestTick(); }
        foreach (var ship in SeaShips)
        {
            if (ship.Sinking >= 0) continue;
            double dx = WorldMap.DeltaX(ship.X, ShipX), dy = ShipY - ship.Y, far = Math.Sqrt(dx * dx + dy * dy);
            // 군함 위장: 해적이 이쪽을 처음 알아볼 때 한 번 속는다 — 속으면 그 배는 끝내 덤비지 않는다
            if (ship.Kind == 1 && !ship.Hunting && !ship.Fooled && !ship.Seen && far < 9 && Option("Disguise") > 0 && _random.NextDouble() < Option("Disguise"))
            {
                ship.Fooled = true;
                Say($"해적선 「{ship.Name}」이(가) 군함인 줄 알고 비켜 간다. (군함 위장)");
            }
            else if (ship.Kind == 1 && !ship.Hunting && !ship.Fooled && !ship.Seen && far < 9)
            {
                // 해적이 이쪽을 처음 알아볼 때 한 번 정한다 — 열에 넷만 덤빈다(나머지는 제 길을 간다). 지은 값: 다 덤비면 포 없는 배는 바다를 못 다닌다
                ship.Seen = true;
                if (_random.NextDouble() >= 0.4 * (1 - Math.Min(0.8, Bonus("Watch"))) * (1 - Math.Min(0.9, Option("Ambush")))) ship.Fooled = true;      // 「고층 감시대」(원본 글: 높은 확률로 바다에서의 기습을 막는다)
                else if (PetWards(ship)) ship.Fooled = true;      // 「경계」 스킬이 기습당할 확률을 낮춘다(스킬 설명 그대로, 랭크마다 4%는 지은 값)
            }
            if (ship.Monster > 0 && !ship.Hunting && !NoRaids && far < 7)
            {
                ship.Hunting = true;
                // 원본의 알림 글(화면 글 3065 · 3067)
                Say(ship.Monster == 2 ? Text(3065, "크라켄이 나타났습니다!") : Text(3067, "식인상어가 나타났습니다."));
                Cues.Enqueue("Alarm");
            }
            if (ship.Kind == 1 && !ship.Hunting && !ship.Fooled && !NoRaids && far < 9 && Strength(ship) >= MyStrength * 0.6)
            {
                ship.Hunting = true;
                // 원본의 알림 글(화면 글 20013) — 뱃머리를 12시로 본 시계 방향
                int clock = (int)Math.Round(((Math.Atan2(WorldMap.DeltaX(ShipX, ship.X), -(ship.Y - ShipY)) - Heading) / (Math.PI * 2) % 1 + 1) % 1 * 12);
                Say($"{Fill(Text(20013, "%d시 방향에 적의 그림자!"), $"{(clock == 0 ? 12 : clock)}")} — 해적선 「{ship.Name}」");
                Cues.Enqueue("Alarm");
            }
            // 적대도가 높은 나라의 군함은 이쪽을 보면 덤벼든다(원본 글 20014 는 싸움이 붙을 때 나온다)
            if (ship.Kind == 2 && !ship.Hunting && !(DelegateSpecial && DelegateTo != null) && far < 9 && Hostile(ship.NationId))
            {
                ship.Hunting = true;
                Say($"{Data.Nations.Find(n => n.Id == ship.NationId)?.Name} 군함 「{ship.Name}」이(가) 이쪽을 알아보고 쫓아온다! (적대도 {Hostility.GetValueOrDefault(ship.NationId)})");
                Cues.Enqueue("Alarm");
            }
            bool fighting = Battle is { Result: null } fight && fight.Foe == ship;
            if (fighting)
            {
                // 싸우는 배: 멀면 다가오고, 사정 안에 들면 옆구리를 이쪽으로 돌린다. 백병전 중에는 선다
                double toMe = Normalize(Math.Atan2(dx, -dy));
                double side = Normalize(toMe + (Normalize(ship.Heading - toMe) < Math.PI ? Math.PI / 2 : -Math.PI / 2));
                // 상선은 싸우지 않고 달아난다(쫓기면서 꼬리 쪽 포만 가끔 맞는다). 해적 · 군함도 내구가 3할 아래로 떨어지면 달아난다
                bool flees = ship.Kind == 0 || (ship.Monster == 0 && ship.Durability < ship.MaxDurability * 0.3);
                ship.Heading = Turned(ship.Heading, flees ? Normalize(toMe + Math.PI) : ship.Monster > 0 || far > FoeRange * 0.8 ? toMe : side, (ship.Monster > 0 ? 1.2 : 0.45) * dt);
            }
            else if (ship.Hunting) ship.Heading = Turned(ship.Heading, Normalize(Math.Atan2(dx, -dy)), 0.5 * dt);
            else if ((ship.TurnIn -= dt) <= 0)
            {
                ship.TurnIn = 8 + _random.NextDouble() * 14;
                ship.Heading = Normalize(ship.Heading + (_random.NextDouble() - 0.5) * 0.9);
            }
            // 괴물은 배 곁(한 척 길이쯤)에서 멈춰 맴돈다 — 더 붙으면 배에 가려 안 보인다
            double wanted = fighting && Battle!.Boarding || (ship.Monster > 0 && far < 1.0) ? 0 : ship.Hunting || fighting ? ship.Cruise * 1.25 : ship.Cruise;
            if (ship.Monster > 0 && far < 1.0) ship.Knots = 0;
            ship.Knots += (wanted - ship.Knots) * Math.Min(1, dt * 0.5);
            double step = ship.Knots * Settings.UnitsPerKnotSecond * dt;
            double nx = ship.X + Math.Sin(ship.Heading) * step, ny = ship.Y - Math.Cos(ship.Heading) * step;
            // 뭍에 닿으면 돌아선다
            // 배의 둘레(반 척 길이)까지 본다 — 가운데만 보면 선체가 뭍에 걸친다. 막히면 트인 쪽으로 돈다
            bool Blocked(double heading) => NearLand(nx, ny) || Map.IsLand(nx + Math.Sin(heading) * 2.5, ny - Math.Cos(heading) * 2.5)
                                            || PortNear(nx + Math.Sin(heading) * 1.5, ny - Math.Cos(heading) * 1.5) < Math.Min(PortKeepOff, PortNear(ship.X, ship.Y));      // 항구 쪽으로는 더 들어가지 않는다
            if (Blocked(ship.Heading))
            {
                ship.Heading = Normalize(ship.Heading + (Blocked(ship.Heading + 1.1) && !Blocked(ship.Heading - 1.1) ? -1.1 : 1.1));
                ship.Knots *= 0.5;
            }
            else (ship.X, ship.Y) = (WorldMap.WrapX(nx), ny);
            // 이쪽 배와 겹치지 않게 한 척 폭은 띄운다(괴물은 따로 곁에서 선다)
            double gap = Distance(ship);
            if (ship.Monster == 0 && gap < ShipGap && gap > 0.01)
            {
                double px = ShipX + WorldMap.DeltaX(ShipX, ship.X) / gap * ShipGap, py = ShipY + (ship.Y - ShipY) / gap * ShipGap;
                if (!NearLand(px, py)) (ship.X, ship.Y) = (WorldMap.WrapX(px), py);
            }
            if (ship.Hunting && far < 6 && Battle == null && Dialog == Dialog.None) StartBattle(ship, false);
        }
        UpdateBattle(dt);
    }

    /// <summary>
    /// 바다의 다른 배에 붙는 이름표 — 가까워야(6) 배 이름이 보이고 멀면 갈래만 보인다.
    /// 「감시」 스킬(스킬 설명: 「멀리 있는 배의 선장이름을 알게 된다」)이 랭크마다 그 거리를 1.5 늘린다. 거리는 지은 값.
    /// </summary>
    public string ShipLabel(SeaShip ship) =>
        ship.Monster > 0 || Distance(ship) <= 6 + Bonus("Lookout") || Battle?.Foe == ship ? $"{SeaShipKinds[ship.Kind]} {ship.Name}" : SeaShipKinds[ship.Kind];

    // 항구 모형은 지도 칸으로는 바다인 자리까지 덮는다 — 다른 배는 항구에서 이만큼 떨어져 다닌다(이미 안에 있으면 나가는 쪽으로만 간다)
    private const double PortKeepOff = 4;

    private double PortNear(double x, double y)
    {
        double best = double.MaxValue;
        foreach (var city in Data.Cities)
        {
            if (city.SeaX == 0 && city.SeaY == 0) continue;
            double dx = WorldMap.DeltaX(x, city.SeaX), dy = city.SeaY - y;
            if (Math.Abs(dx) > PortKeepOff || Math.Abs(dy) > PortKeepOff) continue;
            best = Math.Min(best, Math.Sqrt(dx * dx + dy * dy));
        }
        return best;
    }

    private const double ShipGap = 1.2;

    private bool NearLand(double x, double y) => Map.IsLand(x, y) || Map.IsLand(x + 0.8, y) || Map.IsLand(x - 0.8, y) || Map.IsLand(x, y + 0.8) || Map.IsLand(x, y - 0.8);

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
    public void SpawnForTest(int kind, double far = 3) => SpawnSeaShip(kind, far);

    /// <summary>대본용: 뱃머리를 싸우는 적에게 돌리고 8노트로 민다 — 충각을 시험한다.</summary>
    public void RamForTest()
    {
        if (Battle is not { } battle) return;
        Heading = TargetHeading = Normalize(Math.Atan2(WorldMap.DeltaX(ShipX, battle.Foe.X), -(battle.Foe.Y - ShipY)));
        (Knots, Sail) = (8, SailSteps);
    }

    private void SpawnSeaShip(int wanted = -1, double near = 0)
    {
        for (int tries = 0; tries < 8; tries++)
        {
            double angle = _random.NextDouble() * Math.Tau, far = near > 0 ? near : 10 + _random.NextDouble() * 5;
            double x = WorldMap.WrapX(ShipX + Math.Sin(angle) * far), y = ShipY - Math.Cos(angle) * far;
            if (Map.IsLand(x, y) || Map.IsLand(x + 2, y) || Map.IsLand(x - 2, y) || Map.IsLand(x, y + 2) || Map.IsLand(x, y - 2)) continue;
            double draw = _random.NextDouble();
            // 해적섬(나소 · 홀로 · 포트 로얄) 가까이에서는 나타나는 배의 열에 일곱이 해적이다(지은 값)
            bool den = Data.Cities.Exists(c => c.Kind == 3 && Math.Abs(WorldMap.DeltaX(ShipX, c.SeaX)) < 120 && Math.Abs(c.SeaY - ShipY) < 120);
            int kind = wanted >= 0 ? wanted : den ? (draw < 0.2 ? 0 : draw < 0.997 ? 1 : 3) : draw < 0.6 ? 0 : draw < 0.8 ? 1 : draw < 0.997 ? 2 : 3;      // 크라켄은 삼백여 척에 하나(지은 값 — 원본의 비율은 못 찾았다. 스물다섯에 하나는 너무 잦았다)
            if (kind >= 3)
            {
                // 바다 괴물: 상어 떼(선원을 물어 간다)와 크라켄(배를 조른다). 원본에 「상어 격퇴」 · 「크라켄 격퇴」 스킬이 있어 이 둘이 바다에 나온다는 것은 안다 —
                // 어디에 얼마나 나오고 얼마나 센지는 모른다(지은 값)
                // 상어는 원본에서도 해상재해다(재해 표의 「상어」 — 「식인상어가 나타났습니다.」) — 저절로는 크라켄만 나온다. 상어 떼는 대본(foe:3)으로만
                int monster = wanted == 3 ? 1 : 2;
                int life = monster == 2 ? 600 + (int)Stats.Durability / 2 : 250;
                SeaShips.Add(new SeaShip
                {
                    Ship = Ship, Kind = 3, Monster = monster, Name = monster == 2 ? "크라켄" : "식인상어",
                    X = x, Y = y, Heading = Normalize(angle + Math.PI), Cruise = monster == 2 ? 5 : 9,
                    Durability = life, MaxDurability = life, Crew = 1, MaxCrew = 1, TurnIn = 5,
                });
                return;
            }
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
        Mode != Mode.Sea ? null : SeaShips.Where(s => s.Sinking < 0 && (s.Kind == 1 || s.NationId != NationId) && Distance(s) < SeaShipReach).MinBy(Distance);

    public void Attack()
    {
        if (ShipInReach() is { } ship && Battle == null) { Offend(ship); StartBattle(ship, true); }
    }

    private void StartBattle(SeaShip foe, bool mine)
    {
        Battle = new SeaBattle { Foe = foe, MyReload = 1.5, FoeReload = 3, CrewAtStart = Crew };
        Battle.Log.Add(foe.Monster > 0 ? $"{foe.Name}이(가) 배를 덮쳤다!" : Fill(mine ? Text(20012, "%s와 교전에 들어갑니다!") : Text(20014, "%s의 강습입니다!"), $"{SeaShipKinds[foe.Kind]} 「{foe.Name}」({foe.Ship.Name})"));      // 원본의 글(화면 글 20012 · 20014)
        Say(Battle.Log[0]);
        Say("해전 — 옆구리를 적에게 돌리고 스페이스로 포를 쏜다. 붙으면 백병전을 건다.");
        Cues.Enqueue("Alarm");
    }

    // 원본 글의 %s · %d 자리를 차례로 채운다
    private static string Fill(string text, params string[] values)
    {
        foreach (string value in values)
        {
            int at = text.IndexOf('%');
            if (at < 0 || at + 1 >= text.Length) break;
            text = text[..at] + value + text[(at + 2)..];
        }
        // 글에 섞인 빛깔 표시(0 · 1 · 2 바이트)는 뺀다
        return string.Concat(text.Where(c => c >= ' '));
    }

    private const double ReloadSeconds = 6, FoeReloadSeconds = 7, FoeRange = 5, BoardReach = 1.6, EscapeReach = 20;      // 화면에 아직 보이는 배가 「도망쳤다」가 되지 않게 시야(22) 바로 안쪽

    /// <summary>이쪽 포의 사정(세계 좌표) — 단 대포의 사정거리 평균(360 이 5쯤).</summary>
    public double GunRange => Parts.Where(p => p.Slot == 4).Sum(p => p.A) is > 0 and var guns ? Parts.Where(p => p.Slot == 4).Sum(p => p.A * p.C) / (double)guns / 72 * (1 + Bonus("Range")) : 0;      // 탄도학이 사정을 늘린다

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
        // 배에 들러붙은 괴물은 어느 쪽에서든 쏜다 — 옆구리를 맞추라고 하면 닻을 내린 배는 손을 못 쓴다
        return foe.Monster > 0 || Broadside(Heading, bearing) ? null : "옆구리를 적에게 돌려야 한다";
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
        bool rake = foe.Monster == 0 && Raking(foe.Heading, bearing);      // 괴물에게는 관통이 없다
        // 탄의 갈래(가장 많이 실은 대포의 것): 포도탄은 선원을, 사슬탄은 돛을(느려진다), 철갑탄 · 철갑 유탄은 장갑을 뚫고, 중량포탄은 더 아프다.
        // 탄 이름은 클라이언트 표 21 의 것이고 효과의 크기는 지은 것이다
        int ammo = Parts.Where(p => p.Slot == 4).GroupBy(p => p.D / 10).OrderByDescending(g => g.Sum(p => p.A)).First().Key;
        double hit = Salvo((int)(GunsFitted * Math.Clamp(Crew / Math.Max(1, Stats.MinCrew), 0.3, 1)), ammo is 8 or 17 ? 0 : foe.Armor) * Math.Max(0.3, GunPierce / 30) * (rake ? 1.5 : 1);
        double dead = hit * 0.04;
        hit *= (1 + Option("Shot")) * (1 + Bonus("Shot")) * (rake ? 1 + Bonus("Rake") : 1);      // 포술 · 수평사격, 관통 스킬
        // 「상어 격퇴」 · 「크라켄 격퇴」 스킬(클라이언트 스킬 표의 이름) — 랭크마다 그 괴물에게 주는 피해 +10%(크기는 지은 값)
        if (foe.Monster > 0 && Data.Skills.Find(s => s.Name == (foe.Monster == 1 ? "상어 격퇴" : "크라켄 격퇴")) is { } repel) hit *= 1 + Rank(repel.Id) * 0.1;
        dead = hit * 0.04;
        if (ammo == 2) (hit, dead) = (hit * 0.4, hit * 0.15);
        else if (ammo == 3) { hit *= 0.6; foe.Cruise *= 0.85; }
        else if (ammo == 6) hit *= 1.2;
        // 「화약학」(설명: 「화약류를 잘 다루게 된다. 유탄포의 효과가 상승한다」) — 화염탄 · 작렬탄 · 유탄에 랭크마다 +5%(지은 값)
        if (ammo is 4 or 10 or 17 or 18) { hit *= 1 + Bonus("Powder"); TrainEffect("Powder", 10); }
        // 가까이 붙어 쏘면 기관포가 갑판을 쓸고(선원), 화염방사기가 불을 붙인다(내구) — 특수장비. 크기는 지은 것
        if (Distance(foe) < GunRange / 2)
        {
            if (Parts.Exists(p => p.Slot == 5 && p.A == 5)) dead += 3 + foe.Crew * 0.05;
            if (Parts.Exists(p => p.Slot == 5 && p.A == 6)) hit *= 1.2;
        }
        if (foe.Monster > 0) dead = 0;      // 괴물은 선원이 없다 — 내구가 다해야 잡힌다(전에는 「선원 1」이 깎여 한 방에 잡혔다)
        foe.Durability -= hit;
        foe.Crew -= dead;
        battle.MyReload = ReloadSeconds * CannonReload * (1 - Math.Min(0.6, Option("Reload") + Bonus("Reload") + (Sail == 0 ? Option("FurledReload") : 0)));      // 「집중장전」: 돛을 접고 있는 동안 장전속도 50% 상승(원본 글의 수 그대로)      // 속사 스킬
        battle.Shots.Add(new SeaShot { FromX = ShipX, FromY = ShipY, ToX = foe.X, ToY = foe.Y, Life = 1.1 });
        battle.Hits.Add(new SeaHit { X = foe.X, Y = foe.Y, Text = $"{(rake ? "관통! " : "")}−{hit:0}" });
        battle.Log.Add($"{Fill(Text(20006, "%s에게 포격 명중!"), foe.Name)}{(rake ? " (관통)" : "")} [{AmmoName(ammo)}] {Fill(Text(20007, "선체에 %d의 피해를 주었습니다!"), $"{hit:0}")}{(dead >= 1 ? " " + Fill(Text(20008, "선원들에게 %d의 피해를 주었습니다!"), $"{dead:0}") : "")}");
        Cues.Enqueue("Cannon");
        TrainEffect("Shot", 15); TrainEffect("Reload", 8); TrainEffect("Range", 8);
        if (rake) TrainEffect("Rake", 20);
        Studied("Shot");
        if (rake) Studied("RakeShot");
        Studied(Distance(foe) > GunRange * 0.6 ? "FarShot" : Distance(foe) < GunRange * 0.35 ? "NearShot" : "");
        CheckBattleEnd(battle);
    }

    /// <summary>백병전을 건다 — 적선에 바짝 붙어 있어야 한다.</summary>
    public string? BoardBlocker() =>
        Battle is not { Result: null } battle ? "싸움이 없다" : battle.Foe.Monster > 0 ? "괴물에게는 못 건다" : battle.Boarding ? "이미 백병전 중이다" : Distance(battle.Foe) > BoardReach * (1 + Math.Min(1, Bonus("BoardReach"))) ? "더 가까이 붙어야 한다" : null;      // 「접현」 스킬: 백병전을 걸기 쉬워진다 — 랭크마다 거리 +10%(지은 값)

    public static readonly string[] Tactics = ["돌격", "방어", "총격"];

    /// <summary>백병전의 전술을 고른다 — 다음 합부터 쓴다.</summary>
    public void SetTactic(int tactic)
    {
        if (Battle is { Boarding: true } battle) battle.Tactic = Math.Clamp(tactic, 0, 2);
    }

    /// <summary>백병전에서 빠져나오려 한다 — 절반은 실패한다(지은 값). 실패하면 세 초 동안 다시 못 꾀한다.</summary>
    public void Retreat()
    {
        if (Battle is not { Boarding: true, Result: null } battle || battle.RetreatIn > 0) return;
        if (_random.NextDouble() < 0.5)
        {
            battle.RetreatIn = 3;
            battle.Log.Add(Text(20052, "퇴각에 실패했습니다!"));
            Cues.Enqueue("Error");
            return;
        }
        (battle.Boarding, battle.BoardIn) = (false, 10);
        battle.Log.Add(Text(20549, "전투에서 퇴각하겠습니다!"));
        Say(Text(20549, "전투에서 퇴각하겠습니다!"));
    }

    /// <summary>적의 기뢰가 보이는가 — 「기뢰발견」 스킬(설명: 「전투 중에 적이 설치한 기뢰를 발견할 수 있다」)이 있어야 보인다.</summary>
    public bool SeesMines => Has("MineSight");

    /// <summary>대본용: 싸우는 적이 이쪽 앞에 기뢰를 하나 깐다.</summary>
    public void FoeMineForTest()
    {
        if (Battle is { } battle) battle.FoeMines.Add((ShipX + Math.Sin(Heading) * 2.2, ShipY - Math.Cos(Heading) * 2.2, 2));
    }

    /// <summary>
    /// 해전 아이템을 쓴다(gvdb 「消耗品（海戦・白兵戦）」의 使用時効果). 썼으면 true(하나 준다).
    /// Truce 정전 협정서 「艦隊戦強制終了 — 奇襲してきた船との戦闘を終わらせる(対NPC)」 · Bell 철수의 종 「白兵戦強制終了」 ·
    /// BattleSkill(Amount = 스킬 번호) 원군요청서 「援軍要請」 · 돌격의 군기 「突撃」 · 강철포탄 「貫通」 — 그 스킬을 이 싸움 동안 랭크 1 로 빌린다(랭크는 짐작).
    /// </summary>
    public bool UseBattleItem(ItemData item)
    {
        if (Battle is not { Result: null } battle) { Say($"{item.Name} — 해전 중에 쓴다."); Cues.Enqueue("Error"); return false; }
        switch (item.Effect)
        {
            case "Truce":
                if (battle.Foe.Monster > 0) { Say($"{item.Name} — 괴물에게는 통하지 않는다."); Cues.Enqueue("Error"); return false; }
                (battle.Boarding, battle.Foe.Hunting, battle.Foe.Fooled) = (false, false, true);
                battle.Result = $"{item.Name}을(를) 건넸다. 싸움이 끝났다.";
                Say(battle.Result);
                Dialog = Dialog.Battle;
                return true;
            case "Bell":
                if (!battle.Boarding) { Say($"{item.Name} — 백병전 중에 쓴다."); Cues.Enqueue("Error"); return false; }
                (battle.Boarding, battle.BoardIn) = (false, 10);
                battle.Log.Add($"{item.Name}을(를) 울렸다. " + Text(20549, "전투에서 퇴각하겠습니다!"));
                Say(battle.Log[^1]);
                return true;
            case "BattleSkill":
                int skill = (int)item.Amount;
                if (battle.Lent.Contains(skill) || Skills.ContainsKey(skill) && Data.SkillRules.Find(r => r.SkillId == skill)?.Effect != "Aid") { Say($"{item.Name} — 이미 {SkillName(skill)}이(가) 듣고 있다."); Cues.Enqueue("Error"); return false; }
                if (Data.SkillRules.Find(r => r.SkillId == skill)?.Effect == "Aid")
                {
                    if (battle.AidCalled || battle.Foe.Monster > 0) { Say($"{item.Name} — 지금은 원군을 부를 수 없다."); Cues.Enqueue("Error"); return false; }
                    battle.Lent.Add(skill);
                    CallAid();
                    return battle.AidCalled;
                }
                battle.Lent.Add(skill);
                battle.Log.Add($"{item.Name} — 이 싸움 동안 {SkillName(skill)}이(가) 듣는다.");
                Say(battle.Log[^1]);
                return true;
        }
        return false;
    }

    /// <summary>「원군요청」 · 「해군 호위 요청」 스킬이 있는가 — 해전 막대에 단추가 선다.</summary>
    public bool CanAid => Has("Aid");

    /// <summary>
    /// 원군을 부른다(스킬 설명: 「해상 전투 중，다른 함대에 원군을 요청할 수 있다」 · 「자국 해군을 원군으로 부른다」).
    /// 원본은 다른 배가 싸움에 끼어든다 — 여기서는 배를 띄우지 않고, 다섯 초 뒤부터 여섯 초마다 세 번 적선에 포격이 떨어진다.
    /// 한 싸움에 한 번. 한 번의 피해는 30 + 랭크마다 5(두 스킬은 더한다). 모두 지은 값. 괴물에게는 못 부른다.
    /// </summary>
    public void CallAid()
    {
        if (Battle is not { Result: null, AidCalled: false } battle || !CanAid || battle.Foe.Monster > 0) return;
        (battle.AidCalled, battle.AidLeft, battle.AidIn) = (true, 3, 5);
        battle.Log.Add("원군을 요청했다 — 곧 포격이 온다.");
        Say(battle.Log[^1]);
        TrainEffect("Aid", 20);
    }

    /// <summary>「기뢰 설치」 스킬이 있는가 — 해전 막대에 단추가 선다.</summary>
    public bool CanMine => Has("Mine");

    /// <summary>
    /// 기뢰를 지금 자리에 깐다(스킬 설명: 「해상 전투 중， 기뢰를 설치할 수 있다」). 적선이 닿으면 터진다.
    /// 지은 값: 한 번에 셋까지, 열두 초에 하나, 깔고 두 초 뒤부터 듣고, 피해는 60 + 랭크마다 15. 원본은 기뢰 아이템을 쓰지만 여기서는 그냥 깐다.
    /// </summary>
    public void LayMine()
    {
        if (Battle is not { Boarding: false, Result: null } battle || !CanMine || battle.MineIn > 0 || battle.Mines.Count >= 3) return;
        battle.Mines.Add((ShipX, ShipY, 0));
        battle.MineIn = 12;
        battle.Log.Add("기뢰를 설치했다.");
        TrainEffect("Mine", 10);
    }

    /// <summary>「도주」 스킬이 있는가 — 해전 막대에 단추가 선다.</summary>
    public bool CanFlee => Has("Flee");

    /// <summary>
    /// 「도주」 스킬(스킬 설명: 「공격을 포기하고 쏜살같이 도망친다」) — 싸움을 그 자리에서 끝낸다.
    /// 5할 + 랭크마다 5%로 성공하고(지은 값), 실패하면 다섯 초 뒤에 다시 꾀한다. 백병전 중에는 먼저 퇴각해야 한다.
    /// </summary>
    public void Flee()
    {
        if (Battle is not { Boarding: false, Result: null } battle || !CanFlee || battle.RetreatIn > 0) return;
        TrainEffect("Flee", 12);
        if (_random.NextDouble() >= 0.5 + Bonus("Flee"))
        {
            battle.RetreatIn = 5;
            battle.Log.Add(Text(20052, "퇴각에 실패했습니다!"));
            Cues.Enqueue("Error");
            return;
        }
        (battle.Foe.Hunting, battle.Foe.Fooled) = (false, true);      // 그 배는 다시 덤비지 않는다
        battle.Result = Text(20549, "전투에서 퇴각하겠습니다!");
        Say(battle.Result);
        Dialog = Dialog.Battle;
    }

    public void Board()
    {
        if (Battle is not { } battle || BoardBlocker() != null) return;
        (battle.Boarding, battle.MeleeIn) = (true, 0.5);
        battle.Log.Add(Text(20058, "적선에 돌격!"));
        Say(Text(20058, "적선에 돌격!"));
        Sail = 0;
    }

    private void UpdateBattle(double dt)
    {
        // 결과 창이 닫혔으면 싸움도 끝낸다(창을 Esc 로 닫은 때)
        if (Battle is { Result: not null } && Dialog != Dialog.Battle) Battle = null;
        if (Battle is not { Result: null } battle) return;
        var foe = battle.Foe;
        foreach (var shot in battle.Shots) shot.Age += dt;
        battle.Shots.RemoveAll(s => s.Age > s.Life);
        foreach (var hit in battle.Hits) hit.Age += dt;
        battle.Hits.RemoveAll(h => h.Age > 1.6);
        battle.MyReload = Math.Max(0, battle.MyReload - dt);
        battle.FoeReload = Math.Max(0, battle.FoeReload - dt);
        double far = Distance(foe);

        AideShipsFire(battle, dt);
        if (Battle != battle || battle.Result != null) return;
        // 원군의 포격
        if (battle.AidLeft > 0 && (battle.AidIn -= dt) <= 0)
        {
            (battle.AidIn, battle.AidLeft) = (6, battle.AidLeft - 1);
            double shell = (30 + Bonus("Aid")) * (0.8 + _random.NextDouble() * 0.4);
            foe.Durability -= shell;
            battle.Hits.Add(new SeaHit { X = foe.X, Y = foe.Y, Text = $"원군! −{shell:0}" });
            battle.Log.Add($"원군의 포격 — {Fill(Text(20007, "선체에 %d의 피해를 주었습니다!"), $"{shell:0}")}");
            Cues.Enqueue("Cannon");
            CheckBattleEnd(battle);
            if (Battle != battle || battle.Result != null) return;
        }
        // 기뢰: 적선이 닿으면 터진다(괴물은 안 건드린다)
        battle.MineIn = Math.Max(0, battle.MineIn - dt);
        for (int i = battle.Mines.Count - 1; i >= 0; i--)
        {
            var mine = battle.Mines[i];
            battle.Mines[i] = mine = (mine.X, mine.Y, mine.Age + dt);
            double mdx = WorldMap.DeltaX(mine.X, foe.X), mdy = foe.Y - mine.Y;
            if (mine.Age < 2 || foe.Monster > 0 || mdx * mdx + mdy * mdy > 0.9 * 0.9) continue;
            double blast = 60 + Bonus("Mine");
            foe.Durability -= blast;
            battle.Mines.RemoveAt(i);
            battle.Hits.Add(new SeaHit { X = foe.X, Y = foe.Y, Text = $"기뢰! −{blast:0}" });
            battle.Log.Add(foe.Durability <= 0 ? Fill(Text(20071, "적선 %s가 기뢰에 의해 격침되었습니다!"), foe.Name) : $"{foe.Name}이(가) 기뢰를 밟았다 — 선체에 {blast:0}의 피해");
            Cues.Enqueue("Cannon");
            CheckBattleEnd(battle);
            if (Battle != battle || battle.Result != null) return;
        }
        // 적의 기뢰: 달아나는 해적 · 군함이 여덟 초마다 제 자리에 하나씩 깐다(셋까지). 이쪽이 닿으면 터진다.
        // 「기뢰발견」이 있으면 보이고, 닿았을 때 랭크마다 8%로 피한다. 원본 글 20062 는 그걸로 가라앉을 때의 것. 빈도 · 피해 · 확률은 지은 값
        if ((battle.FoeMineIn -= dt) <= 0)
        {
            battle.FoeMineIn = 8;
            if (foe.Kind is 1 or 2 && foe.Durability < foe.MaxDurability * 0.3 && battle.FoeMines.Count < 3)
            {
                battle.FoeMines.Add((foe.X, foe.Y, 0));
                // 안 보여도 깔았다는 것은 알린다 — 모르고 밟는 일은 없게
                battle.Log.Add($"{foe.Name}이(가) 달아나며 기뢰를 뿌렸다!{(SeesMines ? "" : " (기뢰발견 스킬이 없어 보이지 않는다)")}");
                Say(battle.Log[^1]);
            }
        }
        for (int i = battle.FoeMines.Count - 1; i >= 0; i--)
        {
            var mine = battle.FoeMines[i];
            battle.FoeMines[i] = mine = (mine.X, mine.Y, mine.Age + dt);
            double mdx = WorldMap.DeltaX(mine.X, ShipX), mdy = ShipY - mine.Y;
            if (mine.Age < 2 || mdx * mdx + mdy * mdy > 0.9 * 0.9) continue;
            battle.FoeMines.RemoveAt(i);
            if (_random.NextDouble() < Bonus("MineSight"))
            {
                battle.Log.Add("기뢰를 발견해 비켜 갔다.");
                TrainEffect("MineSight", 15);
                continue;
            }
            // 「조타」의 설명 글: 「기뢰 등으로 인한 피해도 억제할 수 있다」 — 랭크마다 3%, 절반까지(크기는 지은 값)
            double blast = (50 + _random.NextDouble() * 40) * PartDamage * (1 - Math.Min(0.5, RankByName("조타") * 0.03));
            Durability -= blast;
            battle.Hits.Add(new SeaHit { X = ShipX, Y = ShipY, Text = $"기뢰! −{blast:0}", OnMe = true });
            battle.Log.Add(Durability <= 0 ? Text(20062, "기뢰에 의해 배가 침몰했습니다!") : $"기뢰를 밟았다! {Fill(Text(20010, "선체에 %d의 피해!"), $"{blast:0}")}");
            if (Durability <= 0) Say(Text(20062, "기뢰에 의해 배가 침몰했습니다!"));
            Cues.Enqueue("Cannon");
            CheckBattleEnd(battle);
            if (Battle != battle || battle.Result != null) return;
        }
        // 「외과의술」: 싸우는 동안 다섯 초마다 다친 선원을 돌려놓는다 — 물을 1 쓴다(스킬 설명 「물이 필수」). 랭크마다 0.5명은 지은 값
        if ((battle.SurgeryIn -= dt) <= 0)
        {
            battle.SurgeryIn = 5;
            double healed = Math.Min(Bonus("Surgery"), battle.CrewAtStart - Crew);
            if (healed >= 0.5 && Water >= 1)
            {
                Crew += healed;
                Water -= 1;
                battle.Hits.Add(new SeaHit { X = ShipX, Y = ShipY, Text = $"선원 +{healed:0}", OnMe = true });
                TrainEffect("Surgery", 8);
            }
        }
        battle.BoardIn = Math.Max(0, battle.BoardIn - dt);
        battle.RetreatIn = Math.Max(0, battle.RetreatIn - dt);
        // 해적은 선원이 넉넉하면 붙어서 백병전을 걸어 온다 — 열에 셋은 피한다(원본 글 3280 · 20059, 확률과 간격은 지은 값)
        if (!battle.Boarding && foe.Kind == 1 && far < BoardReach && battle.BoardIn <= 0 && foe.Crew > Crew * 0.8)
        {
            battle.BoardIn = 6;
            if (_random.NextDouble() < 0.3) battle.Log.Add(Text(3280, "백병전을 회피했습니다."));
            else
            {
                (battle.Boarding, battle.MeleeIn) = (true, 0.5);
                battle.Log.Add(Text(20059, "적의 선원들이 돌격해 왔습니다!"));
                Say(Text(20059, "적의 선원들이 돌격해 왔습니다!"));
                Sail = 0;
            }
        }
        if (battle.Boarding)
        {
            // 서로 선원 수만큼 벤다 — 전투 레벨이 조금 거든다
            if ((battle.MeleeIn -= dt) > 0) return;
            battle.MeleeIn = 1.5;
            double edge = 1 + LevelOf(BattleExp).Level * 0.01 + (GearPower(4) + GearPower(1)) * 0.05 + Option("Melee") + Study("Melee") + Bonus("Melee");      // 조교(특수장비 갈래 4 「接舷効果」)와 백병전 지원 장비(갈래 1 「白兵戦支援」 — gvdb 글로 확인, 곱 5%는 조교와 같이 둔 지은 값) · 선박 스킬이 거든다
            // 한 합에 맞붙는 수는 적은 쪽의 선원 수다 — 선원이 서너 배 많은 배에 걸려도 두세 합에 전멸하지 않고 퇴각할 틈이 있다(지은 식).
            // 전에는 서로 제 선원 수만큼 베어서, 선원 40명인 배가 150명짜리 해적에게 걸리면 네 초 만에 졌다
            double front = Math.Min(Crew, foe.Crew);
            double theirs = front * (0.08 + _random.NextDouble() * 0.08) * edge, mine = front * (0.08 + _random.NextDouble() * 0.08) * (1 - Math.Min(0.6, Option("MeleeGuard") + Bonus("MeleeGuard") + (Parts.Exists(p => p.Slot == 5 && p.A == 7) ? 0.15 : 0)));
            // 전술의 맞물림 — 원본 화면 글(20080 ~ 20092)이 말하는 대로 돌격 > 총격 > 방어 > 돌격. 적은 그때그때 아무거나 고른다.
            // 이긴 쪽이 1.5배를 주고 절반만 받는다(크기는 지은 값)
            int theirTactic = _random.Next(3), won = (theirTactic - battle.Tactic + 3) % 3;      // 2 = 내가 눌렀다, 1 = 눌렸다
            string tactics = "";
            if (won == 2)
            {
                (theirs, mine) = (theirs * 1.5, mine * 0.5);
                tactics = battle.Tactic switch { 0 => Text(20086, "적선에 돌격! 적의 전술은 무력화되었습니다!"), 1 => Text(20088, "적 선원의 돌격을 막아냈습니다!"), _ => Text(20091, "총격으로 적의 방어를 무력화시켰습니다") } + "\n";
            }
            else if (won == 1)
            {
                (theirs, mine) = (theirs * 0.5, mine * 1.5);
                tactics = battle.Tactic switch { 0 => Text(20087, "적의 방어진때문에 돌격이 실패했습니다!"), 1 => Text(20090, "적 선원의 총격 때문에 방어가 무력화되었습니다!"), _ => Text(20080, "적의 돌격때문에 전술의 효과가 없었습니다!") } + "\n";
            }
            // 전술로 누른 합에는 「수탈」 스킬이 랭크마다 5%로 적선의 짐을 조금 빼앗는다(원본 글 20025, 확률과 양은 지은 값)
            if (won == 2 && HoldFree > 0 && Data.Goods.Count > 0 && _random.NextDouble() < Bonus("Loot"))
            {
                var taken = Data.Goods[_random.Next(Data.Goods.Count)];
                int some = Math.Min(HoldFree, 1 + _random.Next(5));
                GiveGood(taken, some);
                tactics += $"{Text(20025, "적선에서 적재화물을 강탈했습니다!")} ({taken.Name} {some}개)\n";
                TrainEffect("Loot", 10);
            }
            // 눌렸어도 「전술」 스킬이 랭크마다 3%로 적의 전술을 지운다(원본 글 20081, 확률은 지은 값)
            if (won == 1 && _random.NextDouble() < Bonus("Tactics"))
            {
                (theirs, mine) = (theirs * 2, mine / 1.5);
                tactics = Text(20081, "적의 전술은 무력화되었습니다") + "\n";
            }
            // 고른 전술의 스킬(돌격 · 방어 · 총격)이 그 전술을 세게 한다 — 랭크마다 4%(지은 값). 쓴 전술의 스킬이 자란다
            if (battle.Tactic == 1) mine *= 1 - Math.Min(0.5, Bonus("Guard"));
            else theirs *= 1 + Bonus(battle.Tactic == 0 ? "Charge" : "Volley");
            TrainEffect(battle.Tactic switch { 0 => "Charge", 1 => "Guard", _ => "Volley" }, 10);
            TrainEffect("Tactics", 4);
            // 선원이 몇 안 남으면 한 합의 피해가 1 에 못 미쳐 「0의 피해」가 줄줄이 찍히며 헛돌았다 — 한 합에 적어도 한 사람은 쓰러진다(남은 선원까지)
            (theirs, mine) = (Math.Max(theirs, Math.Min(1, foe.Crew)), Math.Max(mine, Math.Min(1, Crew)));
            foe.Crew -= theirs;
            Crew -= mine;
            battle.Hits.Add(new SeaHit { X = foe.X, Y = foe.Y, Text = $"선원 −{theirs:0}" });
            battle.Hits.Add(new SeaHit { X = ShipX, Y = ShipY, Text = $"선원 −{mine:0}", OnMe = true });
            battle.Log.Add($"{tactics}{Fill(Text(20023, "적의 선원들에게 %d의 피해를 주었습니다!"), $"{theirs:0}")} {Fill(Text(20024, "적의 선원에 의해 %d의 피해를 입었습니다!"), $"{mine:0}")}");
            TrainEffect("Melee", 12); TrainEffect("MeleeGuard", 8);
            Studied("Melee");
            CheckBattleEnd(battle);
            return;
        }

        // 바다 괴물: 배에 붙으면 세 초마다 문다 — 상어는 선원을, 크라켄은 배를
        if (foe.Monster > 0 && far < 1.6 && battle.FoeReload <= 0)
        {
            battle.FoeReload = 3;
            if (foe.Monster == 1)
            {
                double bitten = 1 + _random.NextDouble() * 2;
                Crew -= bitten;
                battle.Hits.Add(new SeaHit { X = ShipX, Y = ShipY, Text = $"선원 −{bitten:0}", OnMe = true });
                battle.Log.Add($"{Text(15602, "선원이 상어에게 공격받고 있습니다")} — 선원 −{bitten:0}");
            }
            else
            {
                double crushed = (12 + _random.NextDouble() * 13) * PartDamage;
                Durability -= crushed;
                battle.Hits.Add(new SeaHit { X = ShipX, Y = ShipY, Text = $"−{crushed:0}", OnMe = true });
                battle.Log.Add($"크라켄이 배를 조른다 — 내구 −{crushed:0}");
            }
        }
        // 충각: 뱃머리를 적에게 향한 채 빠르게 들이받으면 한 번 크게 다친다(10초에 한 번)
        battle.RamIn = Math.Max(0, battle.RamIn - dt);
        double toFoe = Math.Atan2(WorldMap.DeltaX(ShipX, foe.X), -(foe.Y - ShipY));
        if (GearPower(0) > 0 && battle.RamIn <= 0 && far < 1.3 && Knots > 4 && Math.Cos(toFoe - Heading) > 0.8)
        {
            double crash = GearPower(0) * 40 * Math.Min(1.5, Knots / 8) * (1 + Option("Ram"));
            foe.Durability -= crash;
            battle.RamIn = 10;
            Knots *= 0.3;
            battle.Hits.Add(new SeaHit { X = foe.X, Y = foe.Y, Text = $"충각! −{crash:0}" });
            battle.Log.Add($"충각으로 들이받았다 — 적의 내구 −{crash:0}");
            Cues.Enqueue("Cannon");
            CheckBattleEnd(battle);
            if (Battle != battle || battle.Result != null) return;
        }
        // 적의 포격 — 옆구리가 이쪽을 보고 사정 안이면 쏜다
        double bearing = Math.Atan2(WorldMap.DeltaX(foe.X, ShipX), -(ShipY - foe.Y));
        if (battle.FoeReload <= 0 && foe.Guns > 0 && far <= FoeRange && Broadside(foe.Heading, bearing))
        {
            // 선수상의 「포탄 회피」 — 그 번의 포격이 통째로 빗나간다
            if (_random.NextDouble() < PartDodge) { _dodged++; battle.FoeReload = FoeReloadSeconds; battle.Log.Add("적의 포탄이 빗나갔다 — 선수상의 가호."); return; }
            bool rake = Raking(Heading, bearing);
            // 적은 한쪽 옆구리의 포만 쏜다 — 포문 수의 절반
            double hit = Salvo((int)(foe.Guns / 2 * Math.Clamp(foe.Crew / Math.Max(1, foe.MaxCrew * 0.4), 0.3, 1)), Stats.Armor) * (rake ? 1.5 : 1) * (1 - Math.Min(0.6, Option("ShotArmor") + Bonus("ShotArmor") + HonorEffect("ShotArmor") + (PrayerOn(3) ? 0.1 : 0))) * 0.5, dead = hit * 0.04;      // 적의 포격은 절반으로 친다(지은 값 — 그대로면 서너 번에 가라앉는다)
            Durability -= hit;
            Crew -= dead;
            battle.FoeReload = FoeReloadSeconds;
            battle.Shots.Add(new SeaShot { FromX = foe.X, FromY = foe.Y, ToX = ShipX, ToY = ShipY, Life = 0.7 });
            battle.Hits.Add(new SeaHit { X = ShipX, Y = ShipY, Text = $"{(rake ? "관통! " : "")}−{hit:0}", OnMe = true });
            battle.Log.Add($"{Fill(Text(20009, "%s에게 공격을 받았습니다!"), foe.Name)}{(rake ? " (관통)" : "")} {Fill(Text(20010, "선체에 %d의 피해!"), $"{hit:0}")}{(dead >= 1 ? " " + Fill(Text(20011, "선원들에게 %d의 피해!"), $"{dead:0}") : "")}");
            Cues.Enqueue("Cannon");
        }
        if (far > EscapeReach)
        {
            battle.Result = foe.Kind == 0 || foe.Durability < foe.MaxDurability * 0.3 ? Text(20550, "적의 함대가 도망쳤습니다!") : Text(20549, "전투에서 퇴각하겠습니다!");
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
            if (foe.Monster == 0) Say(Fill(Durability <= 0 ? Text(20064, "적선 %s의 공격에 의해，격침되었습니다!") : Text(20065, "적선 %s에 나포되어 버렸습니다!"), foe.Name));
            if (!UseLifebuoy()) Wreck(foe.Monster > 0 ? foe.Name : $"{SeaShipKinds[foe.Kind]} 「{foe.Name}」", foe.Monster > 0);
            return;
        }
        if (foe.Durability > 0 && foe.Crew >= 1) return;

        SeaQuestSunk(foe);
        // 이겼다 — 돈과(상선이면) 짐, 전투 경험과 명성
        int exp = 10 + foe.MaxDurability / 20 + foe.Guns, fame = Math.Max(1, exp / 4);
        int money = foe.Monster > 0 ? 0 : 500 + foe.MaxDurability * (foe.Kind == 0 ? 20 : 8);
        money = (int)(money * (1 + Bonus("Loot")));      // 수탈 스킬
        Money += money;
        string loot = "";
        if (foe.Kind == 0 && HoldFree > 0 && Data.Goods.Count > 0)
        {
            var good = Data.Goods[_random.Next(Data.Goods.Count)];
            int count = Math.Min(HoldFree, 5 + _random.Next(16));
            GiveGood(good, count);
            loot = $", {good.Name} {count}개";
        }
        // 「구조」: 이긴 뒤 물에 빠진 선원을 건진다 — 이 싸움에서 잃은 선원의 랭크마다 5%(지은 값, 8할까지)
        string rescued = "";
        if (Bonus("Rescue") > 0 && battle.CrewAtStart - Crew >= 1)
        {
            double back = Math.Floor((battle.CrewAtStart - Crew) * Math.Min(0.8, Bonus("Rescue")));
            if (back >= 1) { Crew += back; rescued = $" 물에 빠진 선원 {back:0}명을 구조했다."; TrainEffect("Rescue", 15); }
        }
        battle.Result = foe.Monster > 0 ? $"{(foe.Monster == 2 ? Text(3094, "크라켄이 사라졌습니다.") : Text(3096, "식인 상어는 사라져 갔습니다."))} (전투 경험 +{exp * GainFactor}, 명성 +{fame})"
            : $"{Fill(foe.Durability <= 0 ? Text(20072, "적선 %s를 격침했습니다!") : Text(20073, "적선 %s를 나포했습니다!"), foe.Name)} {money:N0} 두캇{loot}을(를) 얻었다.{rescued} (전투 경험 +{exp * GainFactor}, 명성 +{fame})";
        Say(battle.Result);
        Studied("SeaWin");
        if (foe.Durability > 0) { Studied("WipeWin"); if (battle.Boarding) Studied("MeleeWin"); }
        if (foe.Kind == 2) { Studied("NavyWin"); NavyWins++; }
        else if (foe.Kind is 1 or 3) { PirateWins++; if (foe.Kind == 1) { Atone(3); if (_random.NextDouble() < 0.3) FindWreckPiece("해적선에서"); } }
        (foe.Durability, foe.Sinking, foe.Knots, foe.Hunting) = (0, 0, 0, false);
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
