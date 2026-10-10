using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 위임 항해 — 목적지 항구를 고르면 배가 뭍을 피해 알아서 간다.
/// 원본의 글(화면 글): 25343 「위임 항해」, 5812 「위임 항해를 할 목적지를 선택하십시오. 중간에「교전」상태가 되면 위임이 중지됩니다.」,
/// 5815 「%s까지 항해를 위임합니다.」, 5816 「위임 항해를 중지합니다.」, 5830 「위임 항해 취소」, 3492 「목적지에 도착했습니다」.
/// 어떻게 길을 잡는가는 클라이언트에 없다 — 여기서는 바다 지도(뭍 판정, 칸 4)를 8 단위 칸으로 줄여 가장 짧은 바닷길을 찾고(A*),
/// 그 길을 따라 뱃머리를 돌린다. 뭍에 붙은 칸은 값을 더 쳐서 되도록 떨어져 간다. 돛은 맡길 때 다 편다(그 뒤로는 손으로 바꿀 수 있다).
/// 목적지는 들어가 본 항구(「항구-마을」을 발견한 도시)만 고른다 — 원본도 가 본 곳만 맡긴다(짐작).
/// 키를 잡거나 바다를 눌러 뱃머리를 돌리면, 싸움이 붙으면 위임이 풀린다. 재해 · 폭풍은 손으로 돌본다. 저장에는 남지 않는다.
/// </summary>
internal sealed partial class Voyage
{
    private const int RouteCell = 8, RouteW = WorldMap.Width / RouteCell, RouteH = WorldMap.Height / RouteCell;

    /// <summary>맡긴 목적지 — 없으면 null.</summary>
    public CityData? DelegateTo { get; private set; }

    private List<(double X, double Y)> _route = [];
    private int _routeAt;
    private double _routeStuck;
    private bool _reefed, _supplyWarned;
    private int _delegateSaved;         // 불러온 저장의 목적지 — 바다의 첫 틱에 길을 다시 찾는다
    private byte[]? _routeSea;          // 0 뭍 · 1 바다 · 2 뭍에 붙은 바다

    /// <summary>위임할 수 있는 목적지 — 들어가 본 항구(지금 자리에서 가까운 차례). 모드 「워프」처럼 다 열어 주지는 않는다.</summary>
    public List<(CityData City, double Far)> DelegateChoices()
    {
        _portDiscoveries ??= Data.Discoveries.Where(d => d.Kind == 15).GroupBy(d => d.Name).ToDictionary(g => g.Key, g => g.First());
        return Data.Cities.Where(c => (c.SeaX != 0 || c.SeaY != 0) && _portDiscoveries.TryGetValue(c.Name, out var port) && Found.Contains(port.Id))
            .Select(c => (c, Math.Sqrt(Math.Pow(WorldMap.DeltaX(ShipX, c.SeaX), 2) + Math.Pow(c.SeaY - ShipY, 2)))).Where(c => c.Item2 > Settings.PortRange).OrderBy(c => c.Item2).ToList();      // 바로 앞의 항구는 뺀다
    }

    /// <summary>그 자리의 해역 이름(지도 창이 쓴다) — 없으면 빈 글.</summary>
    public string SeaNameAt(double x, double y) => Data.Seas.Find(s => s.Id == Zones.ZoneAt(WorldMap.WrapX(x), Math.Clamp(y, 0, WorldMap.Height - 1)))?.Name ?? "";

    /// <summary>선원이 하루에 먹고 마시는 양(스킬 · 부관의 절약은 빼고) — 물 · 식량이 며칠분인지 어림하는 데 쓴다.</summary>
    public double RationPerDay => Math.Max(0.01, Crew * Rules.RationPerCrewDay);

    public string? DelegateBlocker() =>
        Mode != Mode.Sea ? "바다에 나가서 맡긴다" : Battle is { Result: null } ? "싸우는 중에는 맡길 수 없다" : null;

    /// <summary>
    /// 특별 위임 항해 — 원본의 글 22019: 「특별 위임 항해 허가증을 사용하여 보통보다 빠른 특별 위임 항해를 실시합니다. 또한 특별 위임 항해 중에는 모든 공격을 피할 수 있습니다.」
    /// 허가증(아이템 1510808) 한 장이 든다. 얼마나 빠른가는 클라이언트에 없다 — 속도 1.5배는 지은 값. 도착하거나 풀리면 끝난다.
    /// </summary>
    public const int SpecialPermit = 1510808;
    public bool DelegateSpecial { get; private set; }
    public double DelegateBoost => DelegateSpecial && DelegateTo != null ? 1.5 : 1;
    /// <summary>습격을 받지 않는가 — 모드 「해적이 덤비지 않는다」이거나 특별 위임 항해 중.</summary>
    public bool NoRaids => Data.Settings.ModNoPirates || (DelegateSpecial && DelegateTo != null);

    public void StartDelegate(CityData city, bool special = false)
    {
        if (DelegateBlocker() is { } why) { Say(why); Cues.Enqueue("Error"); return; }
        if (special && Items.GetValueOrDefault(SpecialPermit) <= 0) { Say($"{ItemName(SpecialPermit)}이(가) 없다."); Cues.Enqueue("Error"); return; }
        var route = FindRoute(ShipX, ShipY, city.SeaX, city.SeaY);
        if (route == null) { Say($"{city.Name}까지 가는 바닷길을 찾지 못했다."); Cues.Enqueue("Error"); return; }
        (DelegateTo, _route, _routeAt, _routeStuck, _reefed, _supplyWarned, DelegateSpecial) = (city, route, 0, 0, false, false, special);
        if (special) { SpendItem(SpecialPermit, 1); Say($"{ItemName(SpecialPermit)}을(를) 썼다 — 보통보다 빠르고, 가는 동안 습격을 받지 않는다."); }
        Sail = SailSteps;
        Dialog = Dialog.None;
        Cues.Enqueue("Done");
        Say(Fill(Text(5815, "%s까지 항해를 위임합니다."), city.Name).Split("합니다")[0] + "합니다.");
        // 걸릴 날과 버틸 날의 어림 — 길의 길이를 배의 빠르기 일곱 할로 간다고 보고, 물 · 식량은 지금 선원이 먹는 양으로
        double length = 0;
        for (int k = 1; k < route.Count; k++) length += Math.Sqrt(Math.Pow(WorldMap.DeltaX(route[k - 1].X, route[k].X), 2) + Math.Pow(route[k].Y - route[k - 1].Y, 2));
        double perDay = Stats.Knots * 0.7 * DelegateBoost * Settings.UnitsPerKnotSecond * Settings.SecondsPerDay;
        double need = perDay > 0 ? length / perDay : 0, ration = Math.Max(0.01, Crew * Rules.RationPerCrewDay), last = Math.Min(Water, Food) / ration;
        Say($"길 {length:0} — {need:0}일쯤 걸린다. 물 · 식량은 {last:0}일분." + (last < need ? " ※ 모자라다 — 가는 길에 보급해야 한다." : ""));
        if (last < need) Cues.Enqueue("Alarm");
    }

    public void CancelDelegate(string why = "")
    {
        if (DelegateTo == null) return;
        (DelegateTo, _route) = (null, []);
        Say(Text(5830, "위임 항해 취소") + (why == "" ? "" : $" — {why}"));
    }

    /// <summary>남은 길(지도에 그릴 때 쓴다) — 맡기지 않았으면 빈 목록.</summary>
    public IReadOnlyList<(double X, double Y)> DelegateRoute => _route;
    public int DelegateRouteAt => _routeAt;

    // 틱마다 — 길을 따라 뱃머리를 잡는다. 키 입력(steer)이 있으면 위임을 푼다
    private void UpdateDelegate(double dt, double steer)
    {
        if (_delegateSaved != 0)
        {
            int saved = _delegateSaved;
            _delegateSaved = 0;
            if (Data.Cities.Find(c => c.Id == saved) is { } again0 && FindRoute(ShipX, ShipY, again0.SeaX, again0.SeaY) is { } kept)
                (DelegateTo, _route, _routeAt, _routeStuck) = (again0, kept, 0, 0);
        }
        if (DelegateTo is not { } goal) return;
        if (steer != 0) { CancelDelegate("키를 잡았다"); return; }
        if (Battle is { Result: null }) { CancelDelegate("교전"); return; }
        double gx = WorldMap.DeltaX(ShipX, goal.SeaX), gy = goal.SeaY - ShipY;
        if (gx * gx + gy * gy < Math.Pow(Settings.PortRange * 0.7, 2))
        {
            (DelegateTo, _route, Sail) = (null, [], 0);
            Cues.Enqueue("Done");
            // 닿으면 바로 입항한다(사용자, 2026-10-10) — 허가가 없어 못 들어가면 앞바다에 선다(까닭은 EnterPort 가 알린다)
            EnterPort();
            if (Mode == Mode.Sea) Say($"{Text(3492, "목적지에 도착했습니다")} — {goal.Name} 앞바다. (F: 입항)");
            return;
        }
        // 폭풍이면 돛을 한 단만 남기고, 걷히면 다시 편다(손으로 내린 돛은 건드리지 않는다)
        if (Weather == Weather.Storm && Sail > 1) { Sail = 1; _reefed = true; Say("폭풍 — 위임 항해: 돛을 줄였다."); }
        else if (Weather != Weather.Storm && _reefed) { _reefed = false; if (Sail > 0) Sail = SailSteps; }
        // 물 · 식량이 사흘분 아래로 떨어지면 한 번 알린다
        if (!_supplyWarned && Math.Min(Water, Food) < Crew * Rules.RationPerCrewDay * 3)
        {
            _supplyWarned = true;
            Cues.Enqueue("Alarm");
            Say("※ 물 · 식량이 사흘분도 안 남았다 — 가까운 항구에 들러야 한다.");
        }
        // 길에서 지금 가장 가까운 자리로 차례를 당기고, 거기서 뭍에 안 걸리고 곧게 갈 수 있는 가장 먼 자리를 겨눈다
        double Far(int k) => Math.Pow(WorldMap.DeltaX(ShipX, _route[k].X), 2) + Math.Pow(_route[k].Y - ShipY, 2);
        for (int k = _routeAt + 1; k < Math.Min(_route.Count, _routeAt + 24); k++)
            if (Far(k) <= Far(_routeAt)) _routeAt = k;
        int aim = Math.Min(_route.Count - 1, _routeAt + 1);
        for (int k = Math.Min(_route.Count - 1, _routeAt + 14); k > _routeAt + 1; k--)
            if (ClearWay(ShipX, ShipY, _route[k].X, _route[k].Y)) { aim = k; break; }
        TargetHeading = Normalize(Math.Atan2(WorldMap.DeltaX(ShipX, _route[aim].X), -(_route[aim].Y - ShipY)));
        // 뭍에 막혀 서 버렸으면 돛을 다시 펴고, 오래 못 움직이면 길을 다시 찾는다
        if (Knots < 0.3) _routeStuck += dt; else _routeStuck = 0;
        if (_routeStuck > 6)
        {
            _routeStuck = 0;
            if (Sail == 0) return;      // 손으로 돛을 내린 것이면 기다린다
            if (FindRoute(ShipX, ShipY, goal.SeaX, goal.SeaY) is { } again) (_route, _routeAt, Sail) = (again, 0, SailSteps);
            else CancelDelegate("길이 막혔다");
        }
    }

    // 두 자리 사이를 곧게 갈 때 뭍(과 뭍에 붙은 칸)에 걸리지 않는가
    private bool ClearWay(double x0, double y0, double x1, double y1)
    {
        double dx = WorldMap.DeltaX(x0, x1), dy = y1 - y0, far = Math.Sqrt(dx * dx + dy * dy);
        int steps = Math.Max(1, (int)(far / 4));
        for (int k = 1; k <= steps; k++)
            if (RouteSea((int)Math.Floor(WorldMap.WrapX(x0 + dx * k / steps) / RouteCell), (int)Math.Floor((y0 + dy * k / steps) / RouteCell)) != 1) return false;
        return true;
    }

    private byte RouteSea(int cx, int cy)
    {
        if (cy < 0 || cy >= RouteH) return 0;
        if (_routeSea == null)
        {
            // 한 번만 만든다: 8 단위 칸이 바다인가(안의 뭍 판정 칸 넷이 모두 바다), 둘레 여덟 칸에 뭍이 있는가
            var sea = new byte[RouteW * RouteH];
            for (int y = 0; y < RouteH; y++)
                for (int x = 0; x < RouteW; x++)
                    sea[y * RouteW + x] = (byte)(Map.IsLandCell(x * 2, y * 2) || Map.IsLandCell(x * 2 + 1, y * 2) || Map.IsLandCell(x * 2, y * 2 + 1) || Map.IsLandCell(x * 2 + 1, y * 2 + 1) ? 0 : 1);
            var near = (byte[])sea.Clone();
            for (int y = 0; y < RouteH; y++)
                for (int x = 0; x < RouteW; x++)
                {
                    if (sea[y * RouteW + x] == 0) continue;
                    for (int k = 0; k < 8; k++)
                    {
                        int nx = (x + RouteDx[k] + RouteW) % RouteW, ny = y + RouteDy[k];
                        if (ny < 0 || ny >= RouteH || sea[ny * RouteW + nx] == 0) { near[y * RouteW + x] = 2; break; }
                    }
                }
            _routeSea = near;
        }
        return _routeSea[cy * RouteW + (cx % RouteW + RouteW) % RouteW];
    }

    private static readonly int[] RouteDx = [1, -1, 0, 0, 1, 1, -1, -1], RouteDy = [0, 0, 1, -1, 1, -1, 1, -1];

    // 가까운 바다 칸(항구 앞 자리는 뭍 칸에 걸치기도 한다)
    private (int X, int Y)? NearSea(double x, double y)
    {
        int cx = (int)Math.Floor(x / RouteCell), cy = (int)Math.Floor(y / RouteCell);
        for (int r = 0; r < 12; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == r && RouteSea(cx + dx, cy + dy) != 0) return (((cx + dx) % RouteW + RouteW) % RouteW, cy + dy);
        return null;
    }

    /// <summary>
    /// 두 도시 사이의 항로를 위임 항해의 길찾기로 만들어 저장한다(사용자, 2026-10-10: 「천주에서 세비야 가는 항로 니가 입력 해줄 수 있나」).
    /// 찾은 바닷길에서 곧게 갈 수 있는 구간은 한 줄로 줄여(뭍에 붙은 칸을 안 지나는 가장 먼 점까지) 꺾는 자리만 조타 기록으로 남긴다.
    /// </summary>
    public SavedRoute? MakeRouteBetween(int fromCity, int toCity)
    {
        if (Data.Cities.Find(c => c.Id == fromCity) is not { } from || Data.Cities.Find(c => c.Id == toCity) is not { } to) return null;
        if (FindRoute(from.SeaX, from.SeaY, to.SeaX, to.SeaY) is not { Count: > 1 } path) { Say($"{from.Name}에서 {to.Name}까지 가는 바닷길을 찾지 못했다."); return null; }
        var turns = new List<(double X, double Y)> { path[0] };
        for (int at = 0; at < path.Count - 1;)
        {
            int next = at + 1;
            for (int k = path.Count - 1; k > at + 1; k--)
                if (ClearWay(path[at].X, path[at].Y, path[k].X, path[k].Y)) { next = k; break; }
            turns.Add(path[next]);
            at = next;
        }
        var points = new List<int>();
        for (int k = 0; k < turns.Count; k++)
        {
            var (a, b) = k + 1 < turns.Count ? (turns[k], turns[k + 1]) : (turns[k - 1], turns[k]);
            double heading = Normalize(Math.Atan2(WorldMap.DeltaX(a.X, b.X), -(b.Y - a.Y)));
            points.AddRange([(int)WorldMap.WrapX(turns[k].X), (int)turns[k].Y, (int)Math.Round(heading * 180 / Math.PI)]);
        }
        string name = $"{from.Name} → {to.Name}";
        for (int n = 2; Data.Routes.Exists(r => r.Name == name); n++) name = $"{from.Name} → {to.Name} ({n})";
        var route = new SavedRoute { Name = name, Saved = $"{DateTime.Now:yyyy-MM-dd HH:mm}", Points = points, CityId = to.Id };
        Data.Routes.Add(route);
        Data.SaveRoutes();
        Say($"항로 「{name}」을(를) 만들어 저장했다. (조타 기록 {turns.Count}개 · 길찾기 점 {path.Count}개)");
        return route;
    }

    /// <summary>
    /// 네비게이션의 자동 항로(사용자, 2026-10-10: 「도시안에서 from, to 도시 입력해서 따라가기 누르면 출항부터 입항까지」) —
    /// 두 도시 사이의 항로가 없으면 길찾기로 만들고(있으면 그것을 쓴다), 항구에 있으면 출항해서, 그 항로를 끝까지 따라가 입항한다.
    /// 항구에서는 떠나는 도시에 있어야 한다. 바다에서 걸면 가까운 구간부터 따라간다.
    /// </summary>
    public void AutoRoute(CityData from, CityData to)
    {
        if (from.Id == to.Id) { Say("떠나는 도시와 닿는 도시가 같다."); Cues.Enqueue("Error"); return; }
        if (Mode == Mode.Port && City.Id != from.Id) { Say($"{from.Name}에 있어야 떠날 수 있다 — 지금은 {City.Name}이다."); Cues.Enqueue("Error"); return; }
        if (Mode != Mode.Port && Mode != Mode.Sea) return;
        var route = Data.Routes.FindLast(r => r.CityId == to.Id && r.Name.StartsWith($"{from.Name} → {to.Name}")) ?? MakeRouteBetween(from.Id, to.Id);
        if (route == null) { Cues.Enqueue("Error"); return; }
        if (Mode == Mode.Port) Depart();
        if (Mode == Mode.Sea) FollowRoute(route);
    }

    /// <summary>바닷길 — 8 단위 칸 위의 A*. 가로는 감긴다. 뭍에 붙은 칸은 세 배로 친다. 못 찾으면 null.</summary>
    private List<(double X, double Y)>? FindRoute(double fromX, double fromY, double toX, double toY)
    {
        if (NearSea(fromX, fromY) is not { } start || NearSea(toX, toY) is not { } goal) return null;
        int Index(int x, int y) => y * RouteW + x;
        var cost = new float[RouteW * RouteH];
        Array.Fill(cost, float.MaxValue);
        var from = new int[RouteW * RouteH];
        var open = new PriorityQueue<int, float>();
        float Guess(int x, int y)
        {
            int dx = Math.Abs(x - goal.X); dx = Math.Min(dx, RouteW - dx);
            int dy = Math.Abs(y - goal.Y);
            return Math.Max(dx, dy) + 0.4142f * Math.Min(dx, dy);
        }
        int first = Index(start.X, start.Y), last = Index(goal.X, goal.Y);
        (cost[first], from[first]) = (0, -1);
        open.Enqueue(first, Guess(start.X, start.Y));
        bool found = false;
        while (open.TryDequeue(out int at, out float rank))
        {
            if (at == last) { found = true; break; }
            int x = at % RouteW, y = at / RouteW;
            if (rank > cost[at] + Guess(x, y) + 0.001f) continue;      // 낡은 줄
            for (int k = 0; k < 8; k++)
            {
                int nx = (x + RouteDx[k] + RouteW) % RouteW, ny = y + RouteDy[k];
                byte sea = RouteSea(nx, ny);
                if (sea == 0) continue;
                // 모서리를 비껴 지날 때는 양옆이 모두 바다여야 한다
                if (k >= 4 && (RouteSea(nx, y) == 0 || RouteSea(x, ny) == 0)) continue;
                float step = (k < 4 ? 1f : 1.4142f) * (sea == 2 ? 3f : 1f);
                int next = Index(nx, ny);
                if (cost[at] + step >= cost[next]) continue;
                (cost[next], from[next]) = (cost[at] + step, at);
                open.Enqueue(next, cost[next] + Guess(nx, ny));
            }
        }
        if (!found) return null;
        var route = new List<(double, double)>();
        for (int at = last; at >= 0; at = from[at]) route.Add(((at % RouteW + 0.5) * RouteCell, (at / RouteW + 0.5) * RouteCell));
        route.Reverse();
        route.Add((toX, toY));
        return route;
    }
}
