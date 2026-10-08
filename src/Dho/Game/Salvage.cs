using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 침몰선 인양 — 조각지도를 모아 침몰선의 자리를 알아내고, 그 바다에 가서 끌어올린다.
/// 원본의 글(화면 글): 6208 「침몰선의 조각지도를 분석하여 침몰선의 장소를 알아냅니다」, 1325 「분석조각지도수」, 619 「누적인양수」,
/// 3175 「인양조건이 갖추어지지 않았기 때문에 …」, 3176 「인양작업에 실패하여 배가 손상되었습니다…」, 3177 「…침몰선이 가라앉아버렸습니다…. 인양율은 %d％입니다」,
/// 3346 「…현재 %d％까지 올라와 있습니다」, 3348 「%s가 침몰선의 인양에 성공했습니다!」.
/// 클라이언트에는 침몰선의 표(이름 · 자리 · 나오는 것)가 없다 — 아래는 모두 지은 것이다:
/// 조각지도는 해적을 물리치거나(3할) 잠긴 궤를 열면 한 장, 네 장이면 자리가 나온다(지금 자리에서 15 ~ 40 떨어진 바다).
/// 한 번 끌 때마다 행동력 20, 15 + 랭크 × 3 %씩 오르고, 15%(랭크마다 −1%p)로 실패해 배가 상한다. 세 번 실패하면 가라앉는다.
/// 다 올리면 2만 + 랭크 × 1만 두캇쯤과 모험 경험 · 명성. 「인양」 스킬(43)이 있어야 끈다.
/// 침몰선 안을 걸어 조사하는 것, 도시까지 끌고 가는 예항은 넣지 않았다.
/// </summary>
internal sealed partial class Voyage
{
    public const int WreckPiecesNeeded = 4;

    /// <summary>모은 조각지도의 수(원본의 「분석조각지도수」).</summary>
    public int WreckPieces { get; private set; }
    /// <summary>알아낸 침몰선의 자리 — 없으면 null.</summary>
    public (double X, double Y)? WreckAt { get; private set; }
    /// <summary>끌어올린 정도(%)와 실패한 횟수, 지금까지 올린 수(원본의 「누적인양수」).</summary>
    public int WreckRaised { get; private set; }
    public int WreckFails { get; private set; }
    public int WrecksSalvaged { get; private set; }

    private void FindWreckPiece(string from)
    {
        if (WreckAt != null || TowValue > 0) return;
        WreckPieces++;
        Say($"{from} 침몰선의 조각지도를 얻었다. ({Text(1325, "분석조각지도수")} {WreckPieces} / {WreckPiecesNeeded})");
        if (WreckPieces < WreckPiecesNeeded) return;
        // 원본의 침몰선 자리(gvdb 「沈没船（発見物）」 35건 — 발견물과 좌표)가 남아 있으면 아직 못 찾은 것 가운데 가장 가까운 것을 가리킨다.
        // 다 찾았거나 자료가 없으면 아래의 지은 자리(지금 자리에서 15 ~ 40 떨어진 바다)
        if (Data.WreckFinds.Where(w => !Found.Contains(w.DiscoveryId)).MinBy(w => Math.Pow(WorldMap.DeltaX(ShipX, w.X), 2) + Math.Pow(w.Y - ShipY, 2)) is { } real)
        {
            (WreckAt, WreckPieces, WreckRaised, WreckFails) = ((real.X, real.Y), 0, 0, 0);
            Cues.Enqueue("Done");
            Say($"조각지도를 맞춰 침몰선의 자리를 알아냈다 — {WreckNote()}");
            return;
        }
        // 지금 자리에서 15 ~ 40 떨어진 바다 한 곳
        for (int tries = 0; tries < 60; tries++)
        {
            double angle = _random.NextDouble() * Math.Tau, far = 15 + _random.NextDouble() * 25;
            double x = WorldMap.WrapX(ShipX + Math.Sin(angle) * far), y = ShipY - Math.Cos(angle) * far;
            if (y < 2 || NearLand(x, y) || PortNear(x, y) < PortKeepOff) continue;
            (WreckAt, WreckPieces, WreckRaised, WreckFails) = ((x, y), 0, 0, 0);
            Cues.Enqueue("Done");
            Say($"조각지도를 맞춰 침몰선의 자리를 알아냈다 — {WreckNote()}");
            return;
        }
        WreckPieces = WreckPiecesNeeded - 1;      // 마땅한 바다를 못 찾았다 — 다음 조각에서 다시 맞춘다
    }

    /// <summary>대본용: 조각지도를 한 장 얻는다 / 침몰선을 바로 곁에 둔다.</summary>
    public void WreckPieceForTest() => FindWreckPiece("(개발)");
    public void WreckHereForTest() => (WreckAt, WreckRaised, WreckFails) = ((ShipX, ShipY), 0, 0);
    /// <summary>대본용: 알아낸 침몰선의 자리로 배를 옮긴다.</summary>
    public void WreckGoForTest() { if (WreckAt is { } at) { Teleport(at.X, at.Y); Say($"(시험) 침몰선 자리 ({at.X:0}, {at.Y:0}) — 원본 침몰선 {Data.WreckFinds.Count}건"); } }

    public double WreckFar => WreckAt is { } at ? Math.Sqrt(Math.Pow(WorldMap.DeltaX(ShipX, at.X), 2) + Math.Pow(at.Y - ShipY, 2)) : double.MaxValue;

    /// <summary>침몰선까지의 방향(여덟 방위)과 거리 — 화면 왼쪽 위에 보인다.</summary>
    public string WreckNote()
    {
        if (TowValue > 0) return $"침몰선 예항 중{(TowFrayed ? " — 로프가 끊어질 듯하다" : "")} — 항구로";
        if (WreckAt is not { } at) return "";
        string[] winds = ["북", "북동", "동", "남동", "남", "남서", "서", "북서"];
        double bearing = Math.Atan2(WorldMap.DeltaX(ShipX, at.X), -(at.Y - ShipY));
        int index = (int)Math.Round((bearing / Math.Tau * 8 + 8) % 8) % 8;
        return WreckFar < 2 ? $"침몰선 — 바로 아래 ({WreckRaised}%)" : $"침몰선 — {winds[index]}쪽 {WreckFar:0}";
    }

    public bool WreckInReach => Mode == Mode.Sea && WreckFar < 2;

    /// <summary>침몰선을 한 번 끌어올린다(F).</summary>
    public void Salvage()
    {
        if (!WreckInReach || Battle is { Result: null }) return;
        if (!Has("Salvage")) { Say(Text(3175, "인양조건이 갖추어지지 않았기 때문에 이대로 계속해도 끌어올릴 수 없습니다") + " (인양 스킬이 없다)"); Cues.Enqueue("Error"); return; }
        if (!SpendVigour(20)) { Say("행동력이 모자라다."); Cues.Enqueue("Error"); return; }
        (Sail, Knots) = (0, 0);
        int rank = RankByName("인양");
        TrainEffect("Salvage", 25);
        if (_random.NextDouble() < Math.Max(0.03, 0.15 - rank * 0.01))
        {
            WreckFails++;
            double harm = 20 + _random.NextDouble() * 30;
            Durability = Math.Max(1, Durability - harm);
            Cues.Enqueue("Error");
            if (WreckFails >= 3)
            {
                Say(Fill(Text(3177, "인양에 실패하여 침몰선이 가라앉아버렸습니다…. 인양율은%d％입니다"), $"{WreckRaised}"));
                WreckAt = null;
            }
            else Say($"{Text(3176, "인양작업에 실패하여 배가 손상되었습니다…")} (내구 −{harm:0}, 실패 {WreckFails} / 3)");
            return;
        }
        WreckRaised = Math.Min(100, WreckRaised + 15 + rank * 3);
        if (WreckRaised < 100) { Say(Fill(Text(3346, "%s의 침몰선이 올라왔습니다. 현재 %d％까지 올라와 있습니다！"), PlayerName, $"{WreckRaised}")); return; }
        // 다 올렸다 — 이제 항구까지 끌고 가야 값을 받는다(예항)
        TowValue = 20_000 + rank * 10_000 + _random.Next(20_000);
        // 원본의 침몰선 자리였으면 그 침몰선을 발견한다(발견물 표의 침몰선 — 인양에 성공하면 발견)
        if (WreckAt is { } raisedAt && Data.WreckFinds.Find(w => Math.Abs(w.X - raisedAt.X) < 1 && Math.Abs(w.Y - raisedAt.Y) < 1) is { } wreck
            && Data.Discoveries.Find(d => d.Id == wreck.DiscoveryId) is { } ship && Found.Add(ship.Id))
        {
            _towWreck = ship.Id;
            Cues.Enqueue("Discover");
            Say($"{ship.Name}을(를) 발견했다! ({Data.DiscoveryKinds.Find(k => k.Id == ship.Kind)?.Name} {DiscoveryStars(ship)}, 모험 경험 {ship.Exp} · 명성 {ship.Fame})");
            GainExp(0, ship.Exp, ship.Fame);
            (Discovered, DiscoveredAt) = (ship, Clock);
        }
        WreckAt = null;
        Cues.Enqueue("Done");
        Say($"{Fill(Text(3348, "%s가 침몰선의 인양에 성공했습니다!"), PlayerName)} 가까운 항구까지 끌고 가자(예항 — 배가 느려진다).");
        GainExp(0, 100, 20);
    }

    // ── 예항 ──
    // 원본 글: 3351 「%s의 예항로프가 끊어질 듯 합니다！」, 3428 「선장님！ 예항로프가 끊어져 버렸습니다！」, 3182 「예항 중인 침몰선이 다시 바다속으로 가라앉아 버렸습니다…」.
    // 지은 값: 끄는 동안 속도 7할, 하루마다 10%(「예항」 랭크마다 −1%p, 2%까지)로 로프가 상한다 — 한 번은 「끊어질 듯」, 두 번째에 끊어져 잃는다.
    // 어느 항구든 들어가면 값을 받는다.

    /// <summary>끌고 가는 침몰선의 값 — 0 이면 끄는 것이 없다.</summary>
    public int TowValue { get; private set; }
    // 끌고 있는 원본 침몰선(발견물 번호) — 항구에 넘길 때 그 침몰선의 인양품 하나를 받는다
    private int _towWreck;
    public bool TowFrayed { get; private set; }
    public double TowSpeed => TowValue > 0 ? Math.Min(1, 0.7 + Option("Tow")) : 1;      // 「예항 보조」: 안정된 예항

    private void UpdateTow(int days)
    {
        for (int day = 0; day < days && TowValue > 0 && Mode == Mode.Sea; day++)
        {
            TrainEffect("Tow", 20);
            if (_random.NextDouble() >= Math.Max(0.02, 0.10 - Bonus("Tow") / 2)) continue;
            if (!TowFrayed)
            {
                TowFrayed = true;
                Say(Fill(Text(3351, "%s의 예항로프가 끊어질 듯 합니다！"), PlayerName));
                Cues.Enqueue("Alarm");
                continue;
            }
            (TowValue, TowFrayed, _towWreck) = (0, false, 0);
            Say(Text(3428, "선장님！ 예항로프가 끊어져 버렸습니다！"));
            Say(Text(3182, "예항 중인 침몰선이 다시 바다속으로 가라앉아 버렸습니다…"));
            Cues.Enqueue("Error");
        }
    }

    // 항구에 들어올 때 — 끌고 온 침몰선의 값을 받는다
    private void DeliverTow()
    {
        if (TowValue <= 0) return;
        Money += TowValue;
        WrecksSalvaged++;
        Say($"끌고 온 침몰선을 넘겼다 — {TowValue:N0} 두캇. ({Text(619, "누적인양수").Split('%')[0].Trim()} {WrecksSalvaged})");
        GainExp(0, 50, 10);
        // 원본 침몰선이면 그 침몰선의 인양품(gvdb 의 보상 칸에 적힌 것들) 가운데 하나를 받는다 — 무엇이 몇 개 나오는지는 자료가 없어 「아무것 하나」로 지었다
        if (_towWreck > 0 && Data.WreckFinds.Find(w => w.DiscoveryId == _towWreck) is { Gifts.Count: > 0 } hauled)
            GiveGifts([hauled.Gifts[_random.Next(hauled.Gifts.Count)]]);
        (TowValue, TowFrayed, _towWreck) = (0, false, 0);
        Cues.Enqueue("Done");
    }

    /// <summary>대본용: 침몰선을 끌고 있는 것으로 한다.</summary>
    public void TowForTest() => (TowValue, TowFrayed) = (50_000, false);
}
