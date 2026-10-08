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
        Say($"{port.Name}을(를) 발견했다! (항구-마을 {DiscoveryStars(port)}, 모험 경험 {port.Exp} · 명성 {port.Fame})");
        GainExp(0, port.Exp, port.Fame);
        (Discovered, DiscoveredAt) = (port, Clock);
    }

    /// <summary>낚시 발견물이 낚이는 둘레(세계 좌표) · 한 번 던질 때 낚일 확률 — 둘 다 지은 값(gvdb 의 글은 「○○付近」 · 「一投目で釣れました」 쯤이다).</summary>
    public const double FishFindReach = 30, FishFindChance = 0.3;

    /// <summary>지금 자리에서 낚을 수 있는 낚시 발견물(아직 못 찾은 것 · 둘레 안) — 필요 랭크 낮은 것부터.</summary>
    public List<FishFind> FishFindsNear() =>
        Mode != Mode.Sea ? [] : Data.FishFinds.Where(f => !Found.Contains(f.DiscoveryId) && Math.Pow(WorldMap.DeltaX(ShipX, f.X), 2) + Math.Pow(f.Y - ShipY, 2) <= FishFindReach * FishFindReach).OrderBy(f => f.Rank).ToList();

    // 낚시를 한 번 던질 때 — 그 자리의 낚시 발견물(gvdb 「釣り（発見物）」 69건)이 랭크가 되면 확률로 낚인다. 낚였으면 true(그 던짐은 발견으로 끝난다)
    private bool TryFishFind(int rank, string lead, bool sure = false)
    {
        if (FishFindsNear().Find(f => f.Rank <= rank) is not { } find || Data.Discoveries.Find(d => d.Id == find.DiscoveryId) is not { } fish) return false;
        if (!sure && _random.NextDouble() >= FishFindChance) return false;
        Found.Add(fish.Id);
        Cues.Enqueue("Discover");
        Say($"{lead}{fish.Name}을(를) 낚았다 — 발견! ({Data.DiscoveryKinds.Find(k => k.Id == fish.Kind)?.Name} {DiscoveryStars(fish)}, 모험 경험 {fish.Exp} · 명성 {fish.Fame})");
        GainExp(0, fish.Exp, fish.Fame);
        (Discovered, DiscoveredAt) = (fish, Clock);
        return true;
    }

    /// <summary>대본용 — 낚시 발견물 자료를 세고, 번호가 0 이상이면 그 차례의 자리로 옮겨 낚아 본다.</summary>
    public void FishFindForTest(int index)
    {
        Say($"(시험) 낚시 발견물 {Data.FishFinds.Count}건 · 발견물 표에 있는 것 {Data.FishFinds.Count(f => Data.Discoveries.Exists(d => d.Id == f.DiscoveryId))}건 · 육지에 걸린 자리 {Data.FishFinds.Count(f => Map.IsLand(f.X, f.Y))}건");
        if (index < 0 || Data.FishFinds.ElementAtOrDefault(index % 1000) is not { } find) return;
        Teleport(find.X, find.Y);
        Say($"(시험) ({find.X}, {find.Y}) — 필요 낚시 랭크 {find.Rank} · 둘레의 낚시 발견물 {FishFindsNear().Count}건");
        if (index >= 1000) return;      // 1000 + N: 자리로만 옮긴다(낚시는 스킬로 따로 던진다)
        if (!TryFishFind(99, "(시험) ", true)) Say("(시험) 낚이지 않았다.");
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
