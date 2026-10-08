using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 해사 의뢰(토벌) — 해양조합에서 받아, 적힌 바다에 가서 토벌 대상을 척수만큼 가라앉히고, 받은 도시의 해양조합에 보고한다.
/// 의뢰(내는 도시 · 난이도 · 싸우는 자리 · 상대의 배 종류 · 척수, 적혀 있으면 보수 · 내구 · 선원)는 gvdb 의 해사 의뢰 차례 글에서 읽은 것이다
/// (<c>tools\gvo\gvdb_seaquests.py</c> — 177건. 글에서 읽어 틀린 것이 섞여 있을 수 있다).
/// 지은 것: 의뢰의 이름(원본 이름은 일본어뿐 — 「○○의 토벌」) · 상대의 이름(원본 「アイトリア海賊」 따위를 옮길 표가 없어 게임의 해적 이름) ·
/// 보수를 모르는 의뢰의 보수 · 토벌 경험과 명성 · 한 번에 내는 수(난이도 낮은 여덟).
/// 줄인 것: 원본은 여러 척이 한 함대로 한꺼번에 나오지만 여기서는 한 척씩 차례로 나온다(해전이 일대일이다). 원본의 앞 차례(도시의 누구와 이야기)는 뺐다.
/// </summary>
internal sealed partial class Voyage
{
    /// <summary>맡은 해사 의뢰 · 받은 도시 · 가라앉힌 수 · 가라앉혀야 하는 수.</summary>
    public SeaQuest? SeaJob { get; private set; }
    public int SeaGiver { get; private set; }
    public int SeaSunk { get; private set; }
    public int SeaNeed { get; private set; }
    private readonly HashSet<int> _seaDone = [];
    private SeaShip? _seaTarget;

    /// <summary>토벌 대상이 나타나는 거리(세계 좌표) — 그 자리에서 이만큼 안에 들면 나온다(지은 값).</summary>
    private const double SeaQuestReach = 14;

    /// <summary>싸우는 자리 — 글에 좌표가 있으면 그것, 없으면 그 바다(클라이언트 바다 표)의 가운데.</summary>
    public (double X, double Y) SeaSpot(SeaQuest quest)
    {
        if (_seaSpots.TryGetValue(quest.Id, out var known)) return known;
        (double X, double Y) spot = quest.X > 0 ? (quest.X, quest.Y) : Zones.Bounds(quest.SeaZone) is { } box ? ((box.X0 + box.X1) / 2.0, (box.Y0 + box.Y1) / 2.0) : (0, 0);
        // 글의 좌표(이용자가 적은 어림)나 바다의 가운데가 육지에 걸리면 가장 가까운 바다로 옮긴다 — 둘레를 넓혀 가며 찾는다
        if (spot.X > 0 && Map.IsLand(spot.X, spot.Y))
            for (int ring = 4; ring <= 200 && Map.IsLand(spot.X, spot.Y); ring += 4)
                for (int step = 0; step < 16; step++)
                {
                    double x = WorldMap.WrapX(spot.X + Math.Sin(step * Math.Tau / 16) * ring), y = spot.Y - Math.Cos(step * Math.Tau / 16) * ring;
                    if (!Map.IsLand(x, y) && !Map.IsLand(x + 2, y) && !Map.IsLand(x - 2, y) && !Map.IsLand(x, y + 2) && !Map.IsLand(x, y - 2)) { spot = (x, y); break; }
                }
        return _seaSpots[quest.Id] = spot;
    }
    private readonly Dictionary<int, (double X, double Y)> _seaSpots = [];

    /// <summary>대본용 — 맡은 해사 의뢰가 저장 꼴을 오가도 남는가.</summary>
    public void SeaRoundTripForTest()
    {
        var saved = SeaSave;
        string before = $"{SeaJob?.Id} · 받은 곳 {SeaGiver} · {SeaSunk}/{SeaNeed} · 끝낸 것 {_seaDone.Count}";
        RestoreSea(saved.Job, saved.Giver, saved.Sunk, saved.Need, saved.Done);
        Say($"(시험) 해사 의뢰 저장 꼴 왕복: {before} → {SeaJob?.Id} · 받은 곳 {SeaGiver} · {SeaSunk}/{SeaNeed} · 끝낸 것 {_seaDone.Count}");
    }

    /// <summary>싸우는 바다의 이름.</summary>
    public string SeaPlace(SeaQuest quest)
    {
        var (x, y) = SeaSpot(quest);
        return Data.Seas.Find(s => s.Id == (quest.SeaZone > 0 ? quest.SeaZone : Zones.ZoneAt(x, y)))?.Name ?? Data.Seas.Find(s => s.Id == Zones.ZoneAt(x, y))?.Name ?? "먼 바다";
    }

    /// <summary>의뢰의 이름 — 「에게 해의 토벌」(교역 의뢰의 「○○의 납품」을 본뜬 것 — 지은 것).</summary>
    public string SeaTitle(SeaQuest quest) => $"{SeaPlace(quest)}의 토벌";

    /// <summary>의뢰의 속 — 「바사 3척 · (475, 3200)」.</summary>
    public string SeaNote(SeaQuest quest)
    {
        var (x, y) = SeaSpot(quest);
        string count = quest.CountMin == quest.CountMax || quest.CountMin >= SeaMost ? $"{Math.Min(quest.CountMin, SeaMost)}척" : $"{quest.CountMin}~{Math.Min(quest.CountMax, SeaMost)}척";
        return $"{quest.Ship} {count} · ({x:0}, {y:0})";
    }

    /// <summary>조합 창의 한 줄에 넣는 짧은 속 — 좌표는 뺀다(「전투용 캐러벨 2~3척」).</summary>
    public string SeaShort(SeaQuest quest) => SeaNote(quest).Split(" · ")[0];

    // 한 의뢰에서 가라앉힐 수의 위 — 원본에는 함대 전체(열 척)가 적힌 것도 있다. 한 척씩 싸우는 게임이라 여섯으로 자른다(지은 값)
    private const int SeaMost = 6;

    /// <summary>보수 — gvdb 에 적힌 것은 그대로. 모르는 것은 지은 값: 난이도² × 3,000(적힌 51건이 난이도 2 에 13,000 · 3 에 24,000 ~ 27,000 · 6 에 109,600 쯤이다).</summary>
    public int SeaPay(SeaQuest quest) => quest.Reward > 0 ? quest.Reward : quest.Difficulty * quest.Difficulty * 3000;

    /// <summary>이 도시의 해양조합이 내는 해사 의뢰 — 아직 안 한 것 가운데 난이도 낮은 것부터 여덟(여덟은 지은 수).</summary>
    public List<SeaQuest> SeaQuestsHere() =>
        Data.SeaQuests.Where(q => q.Cities.Contains(City.Id) && !_seaDone.Contains(q.Id) && q != SeaJob && Data.Ships.Exists(s => s.Name == q.Ship) && SeaSpot(q).X > 0)
            .OrderBy(q => q.Difficulty).ThenBy(q => q.Id).Take(8).ToList();

    public void AcceptSea(SeaQuest quest)
    {
        if (Mode != Mode.Port || SeaJob != null) return;
        (SeaJob, SeaGiver, SeaSunk, _seaTarget) = (quest, City.Id, 0, null);
        SeaNeed = Math.Clamp(quest.CountMin + _random.Next(Math.Max(1, quest.CountMax - quest.CountMin + 1)), 1, SeaMost);
        Money += quest.Advance;
        Cues.Enqueue("Quest");
        var (x, y) = SeaSpot(quest);
        Say($"해사 의뢰 「{SeaTitle(quest)}」을(를) 받았다." + (quest.Advance > 0 ? $" 선금 {quest.Advance:N0} 두캇." : ""));
        Say($"{SeaPlace(quest)} ({x:0}, {y:0}) 둘레에서 {quest.Ship} {SeaNeed}척을 토벌한다.");
    }

    /// <summary>토벌을 마쳤는가.</summary>
    public bool SeaCleared => SeaJob != null && SeaSunk >= SeaNeed;
    public bool CanReportSea => Mode == Mode.Port && SeaCleared && City.Id == SeaGiver;

    public void ReportSea()
    {
        if (!CanReportSea || SeaJob is not { } job) return;
        int pay = SeaPay(job);
        Money += pay;
        _seaDone.Add(job.Id);
        GiveGifts(job.Gifts);
        GainExp(2, 40 + job.Difficulty * 60, 15 + job.Difficulty * 15);      // 전투 경험 · 명성 — 지은 값(원본 값은 의뢰마다 다르고 자료가 없다)
        Cues.Enqueue("Done");
        Say($"해사 의뢰 「{SeaTitle(job)}」을(를) 보고했다. 보수 {pay:N0} 두캇.");
        (SeaJob, SeaSunk, SeaNeed, _seaTarget) = (null, 0, 0, null);
    }

    /// <summary>맡은 해사 의뢰를 그만둔다 — 선금을 돌려준다.</summary>
    public void DropSea()
    {
        if (SeaJob is not { } job) return;
        Money -= Math.Min(Money, job.Advance);
        Say($"해사 의뢰 「{SeaTitle(job)}」을(를) 그만두었다." + (job.Advance > 0 ? " 선금을 돌려주었다." : ""));
        (SeaJob, SeaSunk, SeaNeed, _seaTarget) = (null, 0, 0, null);
    }

    /// <summary>토벌하는 자리까지의 거리 — 맡은 것이 없으면 아주 큰 값.</summary>
    public double SeaJobFar => SeaJob is { } job && SeaSpot(job) is var (x, y) ? Math.Sqrt(Math.Pow(WorldMap.DeltaX(ShipX, x), 2) + Math.Pow(y - ShipY, 2)) : double.MaxValue;

    // 바다에서 틈틈이 — 토벌하는 자리 가까이에 있고 싸우는 중이 아니며 대상이 떠 있지 않으면 다음 대상을 띄운다
    private void SeaQuestTick()
    {
        if (SeaJob is not { } job || SeaCleared || Mode != Mode.Sea || Battle != null || SeaJobFar > SeaQuestReach) return;
        if (_seaTarget != null && SeaShips.Contains(_seaTarget) && _seaTarget.Durability > 0) return;
        if (Data.Ships.Find(s => s.Name == job.Ship) is not { } hull) return;
        var stats = ShipStats.Of(hull, Settings.Ships);
        for (int tries = 0; tries < 12; tries++)
        {
            double angle = _random.NextDouble() * Math.Tau, far = 5 + _random.NextDouble() * 3;
            double x = WorldMap.WrapX(ShipX + Math.Sin(angle) * far), y = ShipY - Math.Cos(angle) * far;
            if (Map.IsLand(x, y) || Map.IsLand(x + 2, y) || Map.IsLand(x - 2, y) || Map.IsLand(x, y + 2) || Map.IsLand(x, y - 2)) continue;
            // 내구 · 선원은 글에 적힌 것(기함의 값)이 있으면 그것, 없으면 그 배의 본디 능력치
            int life = job.Durability > 0 ? job.Durability : stats.Durability, crew = job.Crew > 0 ? job.Crew : (int)(stats.MaxCrew * 0.85);
            _seaTarget = new SeaShip
            {
                Ship = hull, Kind = 1, NationId = 0, Name = SeaShipName(1, 0),
                X = x, Y = y, Heading = Normalize(angle + Math.PI), Cruise = stats.Knots * 0.55,
                Durability = life, MaxDurability = life, Crew = crew, MaxCrew = Math.Max(crew, stats.MaxCrew),
                Guns = stats.Guns, Armor = stats.Armor, TurnIn = 6, Hunting = true,
            };
            SeaShips.Add(_seaTarget);
            Cues.Enqueue("Alarm");
            Say($"토벌 대상 — 해적 「{_seaTarget.Name}」({hull.Name})이(가) 나타났다! ({SeaSunk + 1}/{SeaNeed})");
            return;
        }
    }

    // 해전에서 이겼을 때 — 그 배가 토벌 대상이면 센다
    private void SeaQuestSunk(SeaShip foe)
    {
        if (SeaJob is not { } job || foe != _seaTarget) return;
        _seaTarget = null;
        SeaSunk++;
        Say(SeaCleared ? $"토벌을 마쳤다({SeaSunk}/{SeaNeed}). {CityName(SeaGiver)}의 해양조합에 보고한다." : $"토벌 대상을 가라앉혔다. ({SeaSunk}/{SeaNeed})");
    }

    /// <summary>맡은 해사 의뢰의 지금 할 일 한 줄(의뢰 내용 창 · 바다 화면).</summary>
    public string SeaLine => SeaJob is not { } job ? ""
        : SeaCleared ? $"「{SeaTitle(job)}」 — 토벌을 마쳤다. {CityName(SeaGiver)}의 해양조합에 보고한다."
        : $"「{SeaTitle(job)}」 — {SeaNote(job)} 둘레에서 {job.Ship} {SeaNeed}척을 토벌한다. (지금 {SeaSunk}척)";

    /// <summary>바다 화면 왼쪽 위의 한 줄 — 자리까지의 방향과 거리.</summary>
    public string SeaJobNote()
    {
        if (SeaJob is not { } job || Mode != Mode.Sea) return "";
        if (SeaCleared) return $"토벌 완료 — {CityName(SeaGiver)}에 보고";
        var (x, y) = SeaSpot(job);
        string[] winds = ["북", "북동", "동", "남동", "남", "남서", "서", "북서"];
        double bearing = Math.Atan2(WorldMap.DeltaX(ShipX, x), -(y - ShipY));
        return SeaJobFar <= SeaQuestReach ? $"토벌 — 이 둘레다 ({SeaSunk}/{SeaNeed})" : $"토벌 — ({x:0}, {y:0})  {winds[(int)Math.Round((bearing / Math.Tau * 8 + 8) % 8) % 8]}쪽 {SeaJobFar:0}";
    }

    /// <summary>해양조합 마스터의 말.</summary>
    public string SeaGuildLine =>
        CanReportSea ? "잘 해 주었네. 그 바다가 한동안은 조용하겠군."
        : SeaJob is { } job ? (SeaCleared ? $"「{SeaTitle(job)}」은(는) {CityName(SeaGiver)}의 해양조합에 보고하게." : $"「{SeaTitle(job)}」 — {SeaNote(job)} 둘레의 {job.Ship} {SeaNeed}척일세. (지금 {SeaSunk}척)")
        : SeaQuestsHere().Count == 0 ? "지금은 맡길 토벌이 없네. 다른 도시의 조합도 둘러보게." : "이런 토벌 일이 들어와 있네. 어느 것을 맡겠나?";

    private (int Job, int Giver, int Sunk, int Need, List<int> Done) SeaSave => (SeaJob?.Id ?? 0, SeaGiver, SeaSunk, SeaNeed, _seaDone.ToList());

    private void RestoreSea(int job, int giver, int sunk, int need, IEnumerable<int> done)
    {
        _seaDone.Clear();
        foreach (int id in done) _seaDone.Add(id);
        SeaJob = Data.SeaQuests.Find(q => q.Id == job);
        (SeaGiver, SeaSunk, SeaNeed, _seaTarget) = SeaJob == null ? (0, 0, 0, null) : (giver, sunk, Math.Max(1, need), (SeaShip?)null);
    }

    /// <summary>대본용 — 맡은 해사 의뢰의 자리로 옮긴다 / 토벌 대상 앞에 붙는다.</summary>
    public void SeaToSiteForTest()
    {
        if (SeaJob is { } job && SeaSpot(job) is var (x, y)) { Teleport(x, y); Say($"(시험) 토벌 자리 ({x:0}, {y:0}) — {SeaPlace(job)}"); }
    }

    /// <summary>대본용 — 떠 있는 토벌 대상에 붙어 싸움을 걸고 그 자리에서 가라앉힌다(이긴 뒤의 셈은 진짜 길로 탄다).</summary>
    public void SeaWinForTest()
    {
        if (Battle is { Result: not null }) { EndBattle(); return; }      // 앞 싸움의 결과 창이 떠 있으면 먼저 닫는다(「확인」)
        if (_seaTarget is not { } target || !SeaShips.Contains(target)) { Say($"(시험) 토벌 대상이 떠 있지 않다 — 싸움 {(Battle == null ? "없음" : Battle.Result ?? "중")} · 창 {Dialog} · 거리 {SeaJobFar:0.0} · 다음 살핌 {_seaQuestIn:0.0}초 · 배 {SeaShips.Count}척 · 모드 {Mode}"); return; }
        (target.X, target.Y) = (WorldMap.WrapX(ShipX + 1.1), ShipY);
        Attack();
        if (Battle is not { } battle || battle.Foe != target) { Say("(시험) 토벌 대상과 싸움이 안 걸렸다."); return; }
        target.Durability = 0;
        CheckBattleEnd(battle);
    }

    /// <summary>대본용 — 해사 의뢰를 내는 도시와 건수, 배를 못 이은 것 · 자리가 없는 것을 센다.</summary>
    public void SeaGuildsForTest()
    {
        var usable = Data.SeaQuests.Where(q => Data.Ships.Exists(s => s.Name == q.Ship) && SeaSpot(q).X > 0).ToList();
        var givers = usable.SelectMany(q => q.Cities).GroupBy(c => c).OrderByDescending(g => g.Count()).ToList();
        // 시내 지도에 해양조합 표식(장소 3)이 없는 도시 · 시내 지도가 아예 없는 도시 · gvdb 시설 표에 「海事ギルド」가 없는 도시
        var none = givers.Where(g => Dho.Data.TownMap.Load(g.Key) is { Marks.Count: > 0 } map && !map.Marks.Any(m => m.Place == 3)).ToList();
        var blind = givers.Where(g => Dho.Data.TownMap.Load(g.Key) is not { Marks.Count: > 0 }).ToList();
        var known = givers.Where(g => Data.TownFacts.ContainsKey(g.Key)).ToList();
        // gvdb 의 시설 표는 조합을 「ギルド」 한 칸으로만 적는다(모험 · 상인 · 해양을 안 가른다)
        var unlisted = known.Where(g => !Data.TownFacts[g.Key].Facilities.Exists(f => f.Contains("ギルド"))).ToList();
        Say($"(시험) 해사 의뢰 {Data.SeaQuests.Count}건 · 쓸 수 있는 것 {usable.Count}건 · 내는 도시 {givers.Count}곳 · 육지에 걸린 자리 {usable.Count(q => SeaSpot(q) is var (x, y) && Map.IsLand(x, y))}건");
        Say($"(시험) 해양조합 표식이 없는 곳 {none.Count}({string.Join(" · ", none.Take(6).Select(g => CityName(g.Key)))}) · 시내 지도가 없는 곳 {blind.Count}({string.Join(" · ", blind.Take(6).Select(g => CityName(g.Key)))}) · gvdb 쪽이 있는 {known.Count}곳 가운데 「ギルド」가 없는 곳 {unlisted.Count}({string.Join(" · ", unlisted.Take(6).Select(g => CityName(g.Key)))})");
    }
}
