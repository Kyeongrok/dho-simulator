using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 해역마다 다른 바다 — 바람(방향 · 세기 · 흔들림, 철 따라 뒤집히는 몬순), 해류, 폭풍과 비의 잦기, 물결의 높이.
/// 클라이언트에는 이 자료가 없어서 <c>data\sea-climates.json</c> 의 값은 모두 지은 것이다(실제 지구의 바람띠와 해류를 본떴다).
/// 해역을 넘어가면 바람과 해류가 천천히 그 바다의 것으로 바뀐다.
/// </summary>
internal sealed partial class Voyage
{
    private static readonly SeaClimate OpenSea = new();
    private Dictionary<int, SeaClimate>? _climates;
    private double _currentX, _currentY, _wave = 1;

    /// <summary>지금 있는 해역의 기후(자료에 없는 해역은 기본값).</summary>
    public SeaClimate Climate =>
        (_climates ??= Data.SeaClimates.ToDictionary(c => c.Id)).GetValueOrDefault(Zones.ZoneAt(ShipX, ShipY)) ?? OpenSea;

    /// <summary>해류가 흘러가는 쪽(라디안, 0 북)과 세기(노트).</summary>
    public double CurrentDirection => Math.Atan2(_currentX, _currentY);
    public double CurrentKnots => Math.Sqrt(_currentX * _currentX + _currentY * _currentY);
    /// <summary>물결의 높이 배율 — 해역의 값에 날씨를 곱한 것(폭풍이면 높다).</summary>
    public double WaveScale => _wave;

    /// <summary>한 해는 360일 — 앞의 절반이 여름이다. 몬순 해역은 겨울에 바람이 뒤집힌다.</summary>
    public bool Summer => Clock / Settings.SecondsPerDay % 360 < 180;

    /// <summary>바람과 해류를 지금 해역의 것으로 천천히 옮긴다.</summary>
    private void UpdateClimate(double dt)
    {
        var climate = Climate;
        double toward = climate.WindDirection * Math.PI / 180 + (climate.Seasonal && !Summer ? Math.PI : 0);
        double swing = climate.WindSwing * Math.PI / 180;
        // 흔들림: 느린 것과 빠른 것 둘을 겹친다
        double want = toward + Math.Sin(Clock / 97) * swing * 0.75 + Math.Sin(Clock / 41) * swing * 0.25;
        double turn = Normalize(want - WindDirection + Math.PI) - Math.PI, blend = Math.Min(1, dt * 0.25);
        WindDirection = Normalize(WindDirection + turn * blend);
        double storm = Weather == Weather.Storm ? 1.6 : Weather == Weather.Rain ? 1.15 : 1;
        WindKnots += (climate.WindKnots * (1 + Math.Sin(Clock / 63) * 0.3) * storm - WindKnots) * blend;

        double flow = climate.CurrentDirection * Math.PI / 180;
        _currentX += (Math.Sin(flow) * climate.CurrentKnots - _currentX) * blend;
        _currentY += (Math.Cos(flow) * climate.CurrentKnots - _currentY) * blend;
        _wave += (climate.Wave * (Weather == Weather.Storm ? 2.2 : Weather == Weather.Rain ? 1.3 : 1) - _wave) * blend;
    }

    /// <summary>나침반 여덟 방위의 이름 — 바람은 「불어오는 쪽」으로 부른다(북동풍 = 북동에서 불어온다).</summary>
    public static string Compass(double radians)
    {
        string[] names = ["북", "북동", "동", "남동", "남", "남서", "서", "북서"];
        return names[(int)Math.Round(Normalize(radians) / (Math.PI / 4)) % 8];
    }

    /// <summary>워프할 수 있는 해역들(격자에 있는 것) — 번호와 이름.</summary>
    public List<(int Id, string Name)> SeasToWarp() =>
        Data.Seas.Where(s => Zones.Bounds(s.Id) != null).Select(s => (s.Id, s.Name)).ToList();

    /// <summary>
    /// 그 해역으로 바로 옮겨 간다(개발 메뉴) — 해역 네모의 가운데에서 가장 가까운, 둘레까지 바다인 자리. 항구에 있으면 출항부터 한다.
    /// </summary>
    public void WarpToSea(int sea)
    {
        if (Zones.Bounds(sea) is not { } box) return;
        double cx = (box.X0 + box.X1) / 2.0, cy = (box.Y0 + box.Y1) / 2.0, best = double.MaxValue;
        (double X, double Y)? spot = null;
        for (double y = box.Y0 + 48; y < box.Y1 - 32; y += 48)
        for (double x = box.X0 + 48; x < box.X1 - 32; x += 48)
        {
            double far = (x - cx) * (x - cx) + (y - cy) * (y - cy);
            if (far >= best || Zones.ZoneAt(x, y) != sea) continue;
            if (Map.IsLand(x, y) || Map.IsLand(x + 24, y) || Map.IsLand(x - 24, y) || Map.IsLand(x, y + 24) || Map.IsLand(x, y - 24)) continue;
            (best, spot) = (far, (x, y));
        }
        string name = _seas.GetValueOrDefault(sea) ?? $"해역 {sea}";
        if (spot is not { } at) { Say($"{name}에는 배를 띄울 바다가 없다."); return; }
        if (Mode == Mode.Port) Depart();
        if (Mode != Mode.Sea) return;
        Teleport(at.X, at.Y);
        (Knots, Dialog) = (0, Dialog.None);
        Say($"(개발) {name}(으)로 옮겨 왔다.");
    }

    /// <summary>확인용: 바다 위의 자리로 옮긴다.</summary>
    public void Teleport(double x, double y) => (ShipX, ShipY) = (WorldMap.WrapX(x), y);

    // 확인용: 타고 있는 배의 재질을 바꾼다
    public void SetMaterialForTest(int material) => ShipMaterialId = material;

    public void SetNationForTest(int nation) => NationId = nation;

    public string WindName => $"{Compass(WindDirection + Math.PI)}풍 {WindKnots:0}";
    public string CurrentName => CurrentKnots < 0.15 ? "" : $"해류 {Compass(CurrentDirection)}쪽 {CurrentKnots:0.0}";
}
