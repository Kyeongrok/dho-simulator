using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 발견물 — 찾아낸 발견물의 기록과, 의뢰 없이 얻는 발견.
/// 클라이언트의 발견물 표(표 32, 3,925개)에는 이름 · 설명 · 갈래 · 별 · 경험 · 명성이 있고 **어디서 찾는지는 없다**.
/// 다만 갈래 15 「항구-마을」 208개는 이름이 도시 이름과 같다(207개가 맞는다) — 그 도시에 처음 들어가면 발견한다(원본도 그렇다).
/// 나머지는 조합의 의뢰로 찾는다(<see cref="QuestsHere"/>).
/// </summary>
internal sealed partial class Voyage
{
    /// <summary>찾아낸 발견물(발견물 표의 번호).</summary>
    public HashSet<int> Found { get; } = [];

    public int FoundOfKind(int kind) => Found.Count(id => Data.Discoveries.Find(d => d.Id == id)?.Kind == kind);

    private Dictionary<string, DiscoveryData>? _portDiscoveries;

    // 항구에 들어올 때 — 그 도시가 「항구-마을」 발견물이고 처음이면 발견한다
    private void DiscoverPort(CityData city)
    {
        _portDiscoveries ??= Data.Discoveries.Where(d => d.Kind == 15).GroupBy(d => d.Name).ToDictionary(g => g.Key, g => g.First());
        if (!_portDiscoveries.TryGetValue(city.Name, out var port) || !Found.Add(port.Id)) return;
        Cues.Enqueue("Discover");
        Say($"{port.Name}을(를) 발견했다! (항구-마을 ★{port.Stars}, 모험 경험 {port.Exp} · 명성 {port.Fame})");
        GainExp(0, port.Exp, port.Fame);
        (Discovered, DiscoveredAt) = (port, Clock);
    }

    /// <summary>발견 카드가 뜬 때(<see cref="Clock"/>).</summary>
    public double DiscoveredAt { get; private set; }

    /// <summary>대본용: 지금 도시를 방금 발견한 것으로 한다(카드와 소리를 본다).</summary>
    public void DiscoverPortForTest()
    {
        _portDiscoveries ??= Data.Discoveries.Where(d => d.Kind == 15).GroupBy(d => d.Name).ToDictionary(g => g.Key, g => g.First());
        if (_portDiscoveries.TryGetValue(City.Name, out var port)) { Found.Remove(port.Id); DiscoverPort(City); }
    }

    /// <summary>대본용: 발견물 몇 개를 찾은 것으로 한다.</summary>
    public void FoundForTest(int count) { foreach (var d in Data.Discoveries.Take(count)) Found.Add(d.Id); }

    /// <summary>방금 의뢰 없이 발견한 것 — 화면이 한 번 보여 주고 지운다.</summary>
    public DiscoveryData? Discovered { get; set; }
}
