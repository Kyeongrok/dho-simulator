using Dho.Data;

namespace Dho.Game;

internal enum Mode { Port, Sea }

/// <summary>어떤 창이 떠 있는가.</summary>
internal enum Dialog { None, Guild, QuestDetail, Landing, Discovery, Report }

internal enum QuestStage { None, Accepted, Discovered }

/// <summary>
/// 게임의 상태와 규칙 — 항구에 머물기, 바다를 달리기, 의뢰를 받아 끝내기.
/// 자료는 <see cref="GameData"/>(JSON)에서 오고, 그리기는 모른다.
/// </summary>
internal sealed class Voyage
{
    public const int SailSteps = 4;

    public GameData Data { get; }
    public WorldMap Map { get; }
    public SeaZones Zones { get; }
    private SettingsData Settings => Data.Settings;

    private readonly Dictionary<int, CityData> _cities;
    private readonly Dictionary<int, LandingData> _landings;
    private readonly Dictionary<int, DiscoveryData> _discoveries;
    private readonly Dictionary<int, string> _seas;

    public Mode Mode { get; private set; } = Mode.Port;
    public Dialog Dialog { get; set; } = Dialog.None;

    public string PlayerName => Settings.PlayerName;
    public int Money { get; private set; }
    public int AdventureExp { get; private set; }
    public int AdventureFame { get; private set; }

    public CityData City { get; private set; }
    public double ShipX { get; private set; }
    public double ShipY { get; private set; }
    /// <summary>뱃머리 방위. 0 이 북쪽, 시계 방향(라디안).</summary>
    public double Heading { get; private set; }
    public double TargetHeading { get; private set; }
    public int Sail { get; private set; }
    public double Knots { get; private set; }

    public double WindDirection { get; private set; } = 2.2;   // 바람이 불어 가는 쪽
    public double WindKnots { get; private set; } = 9;

    public double SecondsAtSea { get; private set; }
    public int DaysAtSea => (int)(SecondsAtSea / Settings.SecondsPerDay);
    /// <summary>하늘의 때. 0 = 자정, 0.5 = 한낮.</summary>
    public double SkyPhase { get; private set; }
    public double Clock { get; private set; }

    /// <summary>받아 둔 의뢰. 한 번에 하나.</summary>
    public QuestData? Quest { get; private set; }
    public QuestStage QuestStage { get; private set; }
    /// <summary>조합 창에서 고른 의뢰(아직 받기 전).</summary>
    public QuestData? Offered { get; set; }
    private readonly HashSet<int> _done = [];

    public List<string> Log { get; } = [];

    public Voyage(GameData data)
    {
        Data = data;
        Map = new WorldMap();
        Zones = new SeaZones();
        _cities = data.Cities.ToDictionary(c => c.Id);
        _landings = data.Landings.ToDictionary(l => l.Id);
        _discoveries = data.Discoveries.ToDictionary(d => d.Id);
        _seas = data.Seas.ToDictionary(s => s.Id, s => s.Name);

        Money = Settings.Money;
        SkyPhase = Settings.StartSkyPhase;
        City = _cities.TryGetValue(Settings.StartCity, out var start) ? start : data.Cities[0];
        MoorAt(City);
        Say($"{City.Name} 항구에 정박해 있다.");
        if (QuestsHere().Any()) Say("모험가 조합에서 의뢰를 받을 수 있다.");
    }

    public string SeaName => _seas.TryGetValue(Zones.ZoneAt(ShipX, ShipY), out var name) ? name : "먼 바다";

    public DiscoveryData? QuestDiscovery => Quest != null && _discoveries.TryGetValue(Quest.DiscoveryId, out var d) ? d : null;
    public LandingData? QuestLanding => Quest != null && _landings.TryGetValue(Quest.LandingId, out var l) ? l : null;
    public string DiscoveryKind(int kind) => Data.DiscoveryKinds.Find(k => k.Id == kind)?.Name ?? "발견물";
    public string CityName(int id) => _cities.TryGetValue(id, out var city) ? city.Name : $"도시 {id}";

    public void Say(string line)
    {
        Log.Add(line);
        if (Log.Count > 60) Log.RemoveAt(0);
    }

    // ── 항구 ─────────────────────────────────────────────────────────────────

    private void MoorAt(CityData city)
    {
        City = city;
        ShipX = city.SeaX;
        ShipY = city.SeaY;
        Sail = 0;
        Knots = 0;
        // 뭍을 등지고 선다
        Heading = TargetHeading = Normalize(Math.Atan2(WorldMap.DeltaX(city.X, city.SeaX), -(city.SeaY - city.Y)));
        Mode = Mode.Port;
    }

    public void Depart()
    {
        if (Mode != Mode.Port) return;
        Mode = Mode.Sea;
        Dialog = Dialog.None;
        SecondsAtSea = 0;
        Sail = 1;
        Say($"{City.Name}을(를) 출항했다.");
    }

    /// <summary>항구 앞바다에 있으면 그 도시.</summary>
    public CityData? PortInReach()
    {
        if (Mode != Mode.Sea) return null;
        foreach (var city in Data.Cities)
        {
            if (city.SeaX == 0 && city.SeaY == 0) continue;
            double dx = WorldMap.DeltaX(ShipX, city.SeaX), dy = city.SeaY - ShipY;
            if (dx * dx + dy * dy < Settings.PortRange * Settings.PortRange) return city;
        }
        return null;
    }

    public void EnterPort()
    {
        if (PortInReach() is not { } city) return;
        int days = DaysAtSea;
        MoorAt(city);
        Say($"{city.Name}에 입항했다. (항해 {days}일)");
    }

    // ── 의뢰 ─────────────────────────────────────────────────────────────────

    /// <summary>이 도시의 조합이 내놓는 의뢰 — 아직 끝내지 않았고 지금 받아 둔 것도 아닌 것.</summary>
    public IEnumerable<QuestData> QuestsHere() =>
        Data.Quests.Where(q => q.CityId == City.Id && !_done.Contains(q.Id) && q != Quest);

    public bool CanReportHere =>
        Mode == Mode.Port && Quest != null && QuestStage == QuestStage.Discovered && Quest.CityId == City.Id;

    public void AcceptQuest()
    {
        if (Quest != null || Offered == null || Offered.CityId != City.Id) return;
        Quest = Offered;
        Offered = null;
        QuestStage = QuestStage.Accepted;
        Money += Quest.Advance;
        Dialog = Dialog.None;
        Say($"의뢰 「{Quest.Title}」을(를) 받았다. 선금 {Quest.Advance:N0} 두캇.");
        Say(Quest.Hint);
        if (QuestLanding is not { X: not 0 }) Say("※ 이 의뢰의 상륙지 자리가 아직 없다. 개발도구에서 찍어야 한다.");
    }

    public bool SiteInReach()
    {
        if (Mode != Mode.Sea || QuestStage != QuestStage.Accepted || QuestLanding is not { X: not 0 } site) return false;
        double dx = WorldMap.DeltaX(ShipX, site.X), dy = site.Y - ShipY;
        return dx * dx + dy * dy < Settings.LandingRange * Settings.LandingRange;
    }

    public void Land()
    {
        if (!SiteInReach()) return;
        Sail = 0;
        Knots = 0;
        Dialog = Dialog.Landing;
        Say($"{QuestLanding!.Name}에 상륙했다.");
    }

    public void Search()
    {
        if (Dialog != Dialog.Landing || Quest == null) return;
        QuestStage = QuestStage.Discovered;
        Dialog = Dialog.Discovery;
        if (QuestDiscovery is { } found)
        {
            AdventureExp += found.Exp;
            AdventureFame += found.Fame;
            Say($"{found.Name}을(를) 발견했다!");
            Say($"모험 경험 {found.Exp}, 모험 명성 {found.Fame}을(를) 얻었다.");
        }
    }

    /// <summary>방금 보고한 의뢰 — 보고 창이 쓴다.</summary>
    public QuestData? Reported { get; private set; }
    public DiscoveryData? ReportedDiscovery { get; private set; }

    public void Report()
    {
        if (!CanReportHere) return;
        Reported = Quest;
        ReportedDiscovery = QuestDiscovery;
        Money += Quest!.Reward;
        _done.Add(Quest.Id);
        Say($"의뢰 「{Quest.Title}」을(를) 보고했다. 보수 {Quest.Reward:N0} 두캇.");
        Quest = null;
        QuestStage = QuestStage.None;
        Dialog = Dialog.Report;
    }

    // ── 바다 ─────────────────────────────────────────────────────────────────

    public void ChangeSail(int delta)
    {
        if (Mode != Mode.Sea || Dialog != Dialog.None) return;
        Sail = Math.Clamp(Sail + delta, 0, SailSteps);
    }

    public void SteerTo(double heading) => TargetHeading = Normalize(heading);

    // ── 확인용 대본이 쓰는 것 ────────────────────────────────────────────────

    public double TimeScale { get; set; } = 1;
    public void SetSkyPhase(double phase) => SkyPhase = phase;

    public void Update(double dt, double steer)
    {
        dt *= TimeScale;
        Clock += dt;
        SkyPhase = (SkyPhase + dt / Settings.SecondsPerSkyCycle) % 1;

        // 바람은 클라이언트 자료에 없다. 천천히 도는 것으로 지어 낸다.
        WindDirection = Normalize(2.2 + Math.Sin(Clock / 97) * 1.1 + Math.Sin(Clock / 41) * 0.35);
        WindKnots = 9 + Math.Sin(Clock / 63) * 4;

        if (Mode != Mode.Sea || Dialog != Dialog.None)
        {
            Knots += (0 - Knots) * Math.Min(1, dt * 2);
            return;
        }

        int daysBefore = DaysAtSea;
        SecondsAtSea += dt;
        if (DaysAtSea != daysBefore) Say($"항해 {DaysAtSea}일째.");

        if (steer != 0) TargetHeading = Normalize(Heading + steer * 0.6);
        double turn = Normalize(TargetHeading - Heading + Math.PI) - Math.PI;
        Heading = Normalize(Heading + Math.Clamp(turn, -Settings.TurnRate * dt, Settings.TurnRate * dt));

        // 돛이 받는 바람: 뒤바람·옆바람에서 빠르고 맞바람에서 느리다
        double off = Math.Cos(Heading - WindDirection);             // 1 = 순풍
        double windFactor = 0.35 + 0.65 * Math.Clamp(0.55 + 0.6 * off - 0.15 * off * off, 0, 1);
        double target = Settings.MaxKnots * Sail / SailSteps * windFactor * (0.6 + WindKnots / 22);
        Knots += (target - Knots) * Math.Min(1, dt * 0.8);

        double distance = Knots * Settings.UnitsPerKnotSecond * dt;
        double nextX = ShipX + Math.Sin(Heading) * distance;
        double nextY = ShipY - Math.Cos(Heading) * distance;
        if (Blocked(nextX, nextY))
        {
            if (Knots > 1) Say("육지에 막혔다. 뱃머리를 돌려야 한다.");
            Knots = 0;
            Sail = Math.Min(Sail, 1);
        }
        else
        {
            ShipX = WorldMap.WrapX(nextX);
            ShipY = nextY;
        }
    }

    private bool Blocked(double x, double y)
    {
        // 항구 앞 자리는 판정 지도에서 뭍에 걸치기도 한다 — 이미 뭍 칸이면 빠져나가게 둔다
        if (Map.IsLand(ShipX, ShipY)) return false;
        // 뱃머리 조금 앞까지 본다
        double ahead = 3;
        return Map.IsLand(x, y) || Map.IsLand(x + Math.Sin(Heading) * ahead, y - Math.Cos(Heading) * ahead);
    }

    private static double Normalize(double angle) => (angle % Math.Tau + Math.Tau) % Math.Tau;
}
