using Dho.Data;

namespace Dho.Game;

internal enum Mode { Port, Sea }

/// <summary>어떤 창이 떠 있는가.</summary>
internal enum Dialog { None, Guild, QuestDetail, Landing, Discovery, Report, Supply, Wreck, Skills, Shipyard, Trade, ShipSwap, Items, ShipParts, Aides, Court, CustomBuild, Outfit, UseSkills, QuickSetup, Strengthen, Bank, Vault, Tavern, ShipInfo, University, Character, ShipyardMenu, SpecialBuild, Jobs, Cargo, Sail, Learn, WorkMethod, Combine, Fitting, Recruit }

internal enum QuestStage { None, Accepted, Discovered }

/// <summary>
/// 게임의 상태와 규칙 — 항구에 머물기, 바다를 달리기, 의뢰를 받아 끝내기.
/// 자료는 <see cref="GameData"/>(JSON)에서 오고, 그리기는 모른다.
/// </summary>
internal sealed partial class Voyage
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
    /// <summary>틀어야 할 효과음의 이름들 — 창이 프레임마다 꺼내 튼다.</summary>
    public Queue<string> Cues { get; } = new();

    private Dialog _dialog = Dialog.None;
    /// <summary>떠 있는 창. 스킬 창을 그냥 열면 가르치는 사람이 없는 것이다(<see cref="LearnFrom"/> 로 열어야 배운다).</summary>
    public Dialog Dialog
    {
        get => _dialog;
        set { _dialog = value; Teacher = -1; }
    }

    /// <summary>스킬을 가르치는 조합 마스터의 갈래 — 0 모험, 1 교역, 2 전투. 없으면 -1.</summary>
    public int Teacher { get; private set; } = -1;

    /// <summary>조합 마스터에게 스킬을 배우러 스킬 창을 연다.</summary>
    public void LearnFrom(int group) => (_dialog, Teacher) = (Dialog.Learn, group);

    public static string TeacherName(int group) => group switch { 0 => "모험가조합", 1 => "상인조합", 2 => "해양조합", _ => "조합" };
    /// <summary>항구에서 시내를 내려다보고 있는가.</summary>
    private bool _townView;

    /// <summary>시내에 있는가. 항구로 나오면 건물 안에서도 나온 것이 된다.</summary>
    public bool TownView
    {
        get => _townView;
        set
        {
            _townView = value;
            if (!value) (Interior, InteriorName) = (0, "");
        }
    }

    /// <summary>들어가 있는 건물 안의 장면 번호. 0 이면 바깥(시내).</summary>
    public int Interior { get; private set; }
    public string InteriorName { get; private set; } = "";

    /// <summary>지금 걷고 있는 장면 — 건물 안이면 그 방, 아니면 시내.</summary>
    public int WalkScene => TownView && Interior != 0 ? Interior : City.TownScene;

    /// <summary>안에 있는 사람의 이름과, 말을 걸면 열리는 창(없으면 None).</summary>
    public string InteriorHost { get; private set; } = "";
    public Dialog InteriorDialog { get; private set; }

    /// <summary>이름에 그 말이 든 건물의 방 장면 번호와 건물 이름.</summary>
    private (string Name, int Scene)? RoomOf(params string[] words)
    {
        foreach (string room in City.Rooms.Split(", ", StringSplitOptions.RemoveEmptyEntries))
        {
            int at = room.LastIndexOf('=');
            if (at > 0 && words.Any(room[..at].Contains) && int.TryParse(room[(at + 1)..], out int scene)) return (room[..at], scene);
        }
        return null;
    }

    /// <summary>시내 지도의 장소 번호에 맞는 건물 안으로 들어간다. 들어갈 방이 없으면 false.</summary>
    public bool EnterPlace(int place)
    {
        (string[] Words, string Host, Dialog Opens)? kind = place switch
        {
            1 => (["모험가조합"], "조합 마스터", Dialog.Guild),
            2 => (["상인조합"], "상인조합 마스터", Dialog.None),
            3 => (["해양조합"], "해양조합 마스터", Dialog.None),
            13 => (["주점"], "주점 주인", Dialog.Tavern),
            29 => (["양성학교", "학교"], "교수", Dialog.University),
            16 => (["서고"], "학자", Dialog.None),
            201 => (["교회", "성당"], "신부", Dialog.None),
            202 => (["모스크", "교회", "성당"], "이맘", Dialog.None),
            203 => (["사원", "교회", "성당"], "승려", Dialog.None),
            // 300번대에는 조련사 같은 것도 섞여 있다 — 이름으로 가린다
            >= 301 and < 400 when new[] { "왕궁", "궁전", "집무실", "관저", "저택" }.Any(PlaceName(place).Contains)
                => (["왕궁", "궁전", "집무실", "관저", "원수공저택"], "시종장", Dialog.Court),
            >= 1001 and < 2000 when PlaceName(place).Contains("저택") => ([PlaceName(place)], PlaceName(place).Replace(" 대표 저택", " 대표").Replace(" 제독 저택", " 제독").Replace(" 공작저택", " 공작").Replace(" 저택", "").Replace("저택", ""), Dialog.None),
            _ => null,
        };
        if (kind is not { } k || RoomOf(k.Words) is not { } room) return false;
        (Interior, InteriorName, InteriorHost, InteriorDialog, _interiorPlace) = (room.Scene, room.Name, k.Host, k.Opens, place);
        Say($"{room.Name}에 들어섰다.");
        return true;
    }

    private int _interiorPlace;

    /// <summary>창이 없는 방의 사람에게 말을 걸었을 때 — 한마디와, 교회에서는 기도.</summary>
    private void TalkInside()
    {
        switch (_interiorPlace)
        {
            case 2: Say($"{InteriorHost}: 「교역 의뢰는 아직 없네. 장사에 쓸 기술이라면 가르쳐 주지.」"); LearnFrom(1); break;
            case 3: Say($"{InteriorHost}: 「토벌 의뢰는 아직 없다. 싸우는 기술이라면 가르쳐 주마.」"); LearnFrom(2); break;
            case 29: Say($"{InteriorHost}: 「항해자 양성학교다. 수업은 아직 열지 않았다 — 조합에서 의뢰를 받으며 익히게.」"); break;
            case 16: Say($"{InteriorHost}: 「지도와 기록은 여기 다 있소. 찾는 곳이 있으면 모험가조합의 의뢰부터 받아 오시오.」"); break;
            case 201 or 202 or 203:
                if (Fatigue > 0) { Fatigue = 0; Say($"{InteriorHost}와(과) 함께 기도를 올렸다. 피로가 풀렸다."); }
                else Say($"{InteriorHost}: 「항해가 무사하기를.」");
                break;
            default: Say($"{InteriorHost}: 「먼 길 오셨소. 바다 이야기나 들려주시오.」"); break;
        }
    }

    public void EnterGuild()
    {
        if (!EnterPlace(1)) Dialog = Dialog.Guild;
    }

    public void LeaveInterior()
    {
        if (Interior == 0) return;
        Say($"{InteriorName}에서 나왔다.");
        (Interior, InteriorName) = (0, "");
    }

    public string PlayerName { get; private set; } = "";
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

    /// <param name="developer">true 면 캐릭터 만들기와 이어 하기를 건너뛰고 설정의 값으로 바로 시작한다(확인용 대본).</param>
    /// <param name="scratch">이어 하기를 읽지도 적지도 않는다 — 확인용 대본으로 돌릴 때. 사람이 하던 것을 건드리지 않게.</param>
    public Voyage(GameData data, bool developer = false, bool scratch = false)
    {
        _scratch = scratch;
        Data = data;
        Map = new WorldMap();
        Zones = new SeaZones();
        _cities = data.Cities.ToDictionary(c => c.Id);
        _landings = data.Landings.ToDictionary(l => l.Id);
        _discoveries = data.Discoveries.ToDictionary(d => d.Id);
        _seas = data.Seas.ToDictionary(s => s.Id, s => s.Name);

        SkyPhase = Settings.StartSkyPhase;
        City = _cities.TryGetValue(Settings.StartCity, out var start) ? start : data.Cities[0];
        Begin(developer);
    }

    /// <summary>지금 있는 곳의 배경음 번호 — 항구·시내는 도시의 것, 바다는 해역의 것.</summary>
    public int MusicNumber => Mode == Mode.Sea
        ? Data.Seas.Find(s => s.Id == Zones.ZoneAt(ShipX, ShipY))?.Music ?? 0
        : City.Music;

    public string SeaName => _seas.TryGetValue(Zones.ZoneAt(ShipX, ShipY), out var name) ? name : "먼 바다";

    public DiscoveryData? QuestDiscovery => Quest != null && _discoveries.TryGetValue(Quest.DiscoveryId, out var d) ? d : null;
    public LandingData? QuestLanding => Quest != null && _landings.TryGetValue(Quest.LandingId, out var l) ? l : null;
    public string DiscoveryKind(int kind) => Data.DiscoveryKinds.Find(k => k.Id == kind)?.Name ?? "발견물";
    /// <summary>도시가 어떤 곳인가 — 나라와 소속 갈래, 교역소가 파는 것.</summary>
    public string CityFacts(CityData city)
    {
        string nation = Data.Nations.Find(n => n.Id == city.Nation)?.Name ?? "";
        string kind = city.Kind switch { 0 => "본거지", 1 => "영지", 2 => "동맹항", _ => "" };
        string head = string.Join(" ", new[] { nation, kind }.Where(t => t != ""));
        string goods = string.Join(" · ", (Data.Markets.Find(m => m.CityId == city.Id)?.GoodIds() ?? []).Select(Good).OfType<GoodData>().Select(g => g.Name));
        return (head == "" ? "" : head + "\n") + (goods == "" ? "" : "특산: " + goods);
    }

    /// <summary>이 도시에 조선소가 있는가(교역 탭의 Shipyard).</summary>
    public bool HasShipyard => TownMap is { Marks.Count: > 0 } map
        ? map.Marks.Any(m => m.Place is 9 or 30)
        : Data.Markets.Find(m => m.CityId == City.Id)?.Shipyard ?? true;

    private (int City, TownMap? Map) _townMap;

    /// <summary>이 도시의 원본 시내 지도(그림과 시설 자리). 없으면 null.</summary>
    public TownMap? TownMap
    {
        get
        {
            if (_townMap.City != City.Id) _townMap = (City.Id, Dho.Data.TownMap.Load(City.Id));
            return _townMap.Map;
        }
    }

    /// <summary>소지품 창을 도구점 쪽지로 열라는 표시(창이 보고 끈다).</summary>
    public bool ItemShopOpen { get; set; }

    /// <summary>시설 앞에 서 있는 사람의 이름(없는 시설이면 null) — 표 98 의 NPC 갈래 이름을 따른다.</summary>
    public static string? KeeperName(int place) => place switch
    {
        9 or 30 => "조선소 주인",
        14 or 21 or 22 or 25 => "은행원",
        12 => "대장장이",
        10 or 32 => "교역소 주인",
        11 or 31 => "도구점 주인",
        13 => "주점 주인",
        _ => null,
    };

    /// <summary>개발 메뉴: 돈을 늘리거나 줄인다.</summary>
    public void AddMoney(int amount)
    {
        Money = Math.Max(0, Money + amount);
        Say($"(개발) 소지금 {(amount >= 0 ? "+" : "")}{amount:N0} → {Money:N0} 두캇");
    }

    /// <summary>건물 안의 사람과 출구에 붙이는 가짜 장소 번호.</summary>
    public const int InsideMaster = 9001, InsideExit = 9002, InsideMaid = 9003, InsideSailor = 9004;
    /// <summary>주점에 여급이 있는 도시 — 원본은 도시마다 다른 여급이 있고 없는 곳도 있다. 어느 도시인지 자료가 없어 본거지에만 둔다.</summary>
    public bool HasMaid => City.Kind == 0;

    public string PlaceName(int place) => place == InsideMaster ? InteriorHost : place == InsideMaid ? "여급" : place == InsideSailor ? "뱃사람" : place == InsideExit ? "출구" : Data.Places.Find(p => p.Id == place)?.Name ?? (place > 1000 ? "저택" : $"장소 {place}");

    /// <summary>시내에서 그 시설에 닿았을 때 — 하는 일이 있는 곳이면 창을 연다.</summary>
    public void Visit(TownMark mark)
    {
        string name = PlaceName(mark.Place);
        if (mark.Place is not (InsideMaster or InsideMaid or InsideSailor)) Say($"{name}에 왔다.");
        if (mark.Place == InsideMaster)
        {
            if (InteriorDialog == Dialog.None) TalkInside();
            else Dialog = InteriorDialog;
            return;
        }
        if (mark.Place == InsideExit) { LeaveInterior(); return; }
        if (mark.Place == InsideSailor) { Dialog = Dialog.Recruit; return; }
        if (mark.Place == InsideMaid) { Say("여급: 「어서 오세요! 오늘은 무엇을 드릴까요?」"); return; }
        if (mark.Place is 4 or 5) TownView = false;                          // 항구 · 항구(항구 앞) → 부두로
        else if (mark.Place is 9 or 30) Dialog = Dialog.ShipyardMenu;
        else if (mark.Place is 10 or 19 or 26 or 27 or 32) Dialog = Dialog.Trade;
        else if (mark.Place is 14 or 21 or 22 or 25) Dialog = Dialog.Bank;
        else if (mark.Place == 1) EnterGuild();
        else if (mark.Place is 11 or 31 or 12) { ItemShopOpen = true; Dialog = Dialog.Items; }       // 12 대장간 — 돛 도료를 판다
        else if (mark.Place == 13) { if (!EnterPlace(13)) Dialog = Dialog.Tavern; }
        else EnterPlace(mark.Place);
    }

    /// <summary>은행에 맡긴 돈 — 난파해도 남는다. 이자는 없다(원본도 없다).</summary>
    public long Savings { get; private set; }

    /// <summary>은행에 맡긴다(양수) · 찾는다(음수). 가진 만큼만.</summary>
    public void Bank(long amount)
    {
        if (amount > 0)
        {
            int put = (int)Math.Min(amount, Money);
            if (put <= 0) return;
            (Money, Savings) = (Money - put, Savings + put);
            Say($"은행에 {put:N0} 두캇을 맡겼다. (예금 {Savings:N0})");
        }
        else
        {
            int take = (int)Math.Min(Math.Min(-amount, Savings), int.MaxValue - Money);
            if (take <= 0) return;
            (Money, Savings) = (Money + take, Savings - take);
            Say($"은행에서 {take:N0} 두캇을 찾았다. (예금 {Savings:N0})");
        }
    }

    /// <summary>은행 보관함 — 아이템을 맡겨 둔다. 난파해도 남는다.</summary>
    public Dictionary<int, int> Vault { get; } = new();
    public const int VaultSlots = 50;

    /// <summary>보관함에 넣는다(양수) · 꺼낸다(음수).</summary>
    public void Stash(int item, int count)
    {
        var (from, to) = count > 0 ? (Items, Vault) : (Vault, Items);
        int move = Math.Min(Math.Abs(count), from.GetValueOrDefault(item));
        if (move <= 0) return;
        if (count > 0 && !Vault.ContainsKey(item) && Vault.Count >= VaultSlots) { Say("보관함이 가득 찼다."); return; }
        if ((from[item] -= move) <= 0) from.Remove(item);
        to[item] = to.GetValueOrDefault(item) + move;
        Say(count > 0 ? $"{ItemName(item)} {move}개를 보관함에 맡겼다." : $"{ItemName(item)} {move}개를 보관함에서 꺼냈다.");
    }

    /// <summary>모집할 수 있는 선원의 갈래와 한 사람 값 — 이름과 값은 원본 화면의 것이다. 갈래에 따른 차이(숙련도)는 이 게임에 없다.</summary>
    public static readonly (string Name, int Price)[] Recruits = [("신참선원", 50), ("중견선원", 200), ("숙련선원", 1000)];

    /// <summary>주점의 뱃사람에게서 선원을 모집한다 — 갈래마다의 수.</summary>
    public void Recruit(int[] counts)
    {
        int room = (int)Math.Floor(Stats.MaxCrew - Crew), hired = 0, cost = 0;
        for (int kind = 0; kind < Recruits.Length && kind < counts.Length; kind++)
        {
            int take = Math.Min(counts[kind], room - hired);
            (hired, cost) = (hired + take, cost + take * Recruits[kind].Price);
        }
        if (Mode != Mode.Port || hired <= 0 || Money < cost) return;
        Money -= cost;
        Crew += hired;
        Say($"선원 {hired}명을 모집했다. ({cost:N0} 두캇, 선원 {Crew:0}명)");
    }

    /// <summary>선원을 내보낸다 — 필요 선원 밑으로는 못 줄인다. 돈은 돌려받지 않는다.</summary>
    public void DismissCrew(int count)
    {
        int gone = (int)Math.Min(count, Math.Floor(Crew - Stats.MinCrew));
        if (Mode != Mode.Port || gone <= 0) return;
        Crew -= gone;
        Say($"선원 {gone}명을 해고했다. (선원 {Crew:0}명)");
    }

    /// <summary>주점에서 한턱낸다 — 선원 수만큼 술값을 내고 피로를 푼다.</summary>
    public int TreatCost => 200 + (int)Crew * 20;
    public void Treat()
    {
        if (Mode != Mode.Port || Money < TreatCost || Fatigue <= 0) return;
        Money -= TreatCost;
        Fatigue = Math.Max(0, Fatigue - 40);
        Say($"선원들에게 한턱냈다. 피로가 풀렸다. ({TreatCost:N0} 두캇)");
    }

    /// <summary>경험치로 셈한 레벨과, 다음 레벨까지 남은 경험치. 원본의 레벨 표를 몰라 지은 것이다(레벨 n 까지 50 × n²).</summary>
    public static (int Level, int Next) LevelOf(int exp)
    {
        int level = (int)Math.Sqrt(Math.Max(0, exp) / 50.0);
        return (level, 50 * (level + 1) * (level + 1) - Math.Max(0, exp));
    }

    /// <summary>의뢰를 주는 곳의 이름 — 조합 건물이 있는 도시는 열세 곳뿐이고 나머지는 의뢰 중개인이 준다.</summary>
    public string GuildName => City.Buildings.Contains("모험가조합") ? "모험가 조합" : "의뢰 중개인";

    public string CityName(int id) => _cities.TryGetValue(id, out var city) ? city.Name : $"도시 {id}";

    public void Say(string line)
    {
        Log.Add(Korean.Particles(line));
        if (Log.Count > 60) Log.RemoveAt(0);
    }

    // ── 항구 ─────────────────────────────────────────────────────────────────

    private void MoorAt(CityData city)
    {
        _skillOn.Clear();                        // 켜 둔 스킬은 뭍에 닿으면 꺼진다
        City = city;
        TownView = false;
        (Interior, InteriorName) = (0, "");
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
        TownView = false;
        (Interior, InteriorName) = (0, "");
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
        Studied("Voyage");
        if (days >= 1) GainMastery();
        if (days >= 15) Studied("LongVoyage");
        RestInPort();
        OrderOnArrive();
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
        if (SearchBlocker() is { } lacking)
        {
            Say($"아무것도 찾지 못했다. ({lacking} 필요)");
            return;
        }
        QuestStage = QuestStage.Discovered;
        Dialog = Dialog.Discovery;
        if (QuestDiscovery is { } found)
        {
            AdventureExp += found.Exp;
            AdventureFame += found.Fame;
            Say($"{found.Name}을(를) 발견했다!");
            Say($"모험 경험 {found.Exp}, 모험 명성 {found.Fame}을(를) 얻었다.");
            TrainDiscovery(found);
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
        OrderOnReport();
        Studied("Discover");
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
    public void GoTo(int cityId) { if (_cities.TryGetValue(cityId, out var city)) { MoorAt(city); OrderOnArrive(); } }
    /// <summary>내구를 0 으로 — 다음 틱에 난파한다.</summary>
    public void Sink() => Durability = 0;

    /// <summary>일시정지 — 원본에는 없지만 혼자 하는 게임이라 넣었다. 시간 · 항해 · 재해가 모두 멎는다.</summary>
    public bool Paused { get; set; }

    public void Update(double dt, double steer)
    {
        if (!Created || Paused) return;
        dt *= TimeScale;
        Clock += dt;
        SkyPhase = (SkyPhase + dt / Settings.SecondsPerSkyCycle) % 1;

        // 바람은 클라이언트 자료에 없다. 천천히 도는 것으로 지어 낸다.
        WindDirection = Normalize(2.2 + Math.Sin(Clock / 97) * 1.1 + Math.Sin(Clock / 41) * 0.35);
        WindKnots = 9 + Math.Sin(Clock / 63) * 4;

        AutoSave(dt);
        // 바다에서는 창을 열어도 배가 멈추지 않는다. 상륙 · 발견처럼 배를 세우고 하는 일만 멈춘다
        if (Mode != Mode.Sea || Dialog is Dialog.Landing or Dialog.Discovery or Dialog.Wreck)
        {
            Knots += (0 - Knots) * Math.Min(1, dt * 2);
            return;
        }

        int daysBefore = DaysAtSea;
        SecondsAtSea += dt;
        if (DaysAtSea != daysBefore) Say($"항해 {DaysAtSea}일째.");
        UpdateHazards(dt);
        if (Mode != Mode.Sea) return;            // 난파해서 항구로 떠밀려 갔다
        TickSkills();

        if (steer != 0) TargetHeading = Normalize(Heading + steer * 0.6);
        double turn = Normalize(TargetHeading - Heading + Math.PI) - Math.PI;
        double turnRate = Settings.TurnRate * Stats.TurnFactor * (1 + Bonus("Turn")) * (1 + Option("Turn"));
        Heading = Normalize(Heading + Math.Clamp(turn, -turnRate * dt, turnRate * dt));
        if (Sail > 0)
        {
            double days = dt / Settings.SecondsPerDay;
            TrainEffect("Speed", 12 * days);
            TrainEffect("Survey", 8 * days);
            if (Math.Abs(turn) > 0.05) TrainEffect("Turn", 30 * days);
        }

        // 돛이 받는 바람: 뒤바람·옆바람에서 빠르고 맞바람에서 느리다
        double off = Math.Cos(Heading - WindDirection);             // 1 = 순풍
        double windFactor = 0.35 + 0.65 * Math.Clamp(0.55 + 0.6 * off - 0.15 * off * off, 0, 1);
        // 선원이 모자라거나 재해가 있으면 느려진다
        double hands = Math.Clamp(Crew / Stats.MinCrew, 0.3, 1);
        double target = Stats.Knots * Sail / SailSteps * windFactor * (0.6 + WindKnots / 22) * hands * DisasterSpeedFactor()
                        * (1 + Bonus("Speed")) * PartSpeed * AideSpeed * (1 + Option("Speed"));
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
