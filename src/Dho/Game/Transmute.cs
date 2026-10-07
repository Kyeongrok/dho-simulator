using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 변성연금 — 달아 둔 선박 장갑(삼나무판 따위)의 장갑을 연금술로 올린다.
/// 클라이언트에 있는 것: 창의 글(화면 글 43601 ~ 43628 — 「변성시킬 아이템을 선택」 · 「변성방침」 · 「변성소재」 ·
/// 「실패하면 변성소재가 소실될 수 있습니다」 · 「변성실행: %d/%d」 · 성공 확률 / 소재 손실 / 보너스를 돕는 비술 셋),
/// 아이템 「우로보로스의 책」(「변성연금 중에 사용하면， 변성이 반드시 성공한다」) · 「유니콘의 책」(「성공시 반드시 보너스가 생긴다. 책 자체는 변성 성공여부에 무관히 소비된다」),
/// 「특별발주증서」 · 「수표(1000만 두캇)」 · 교역품 「무색 승화약」.
/// 클라이언트에 없는 것(수는 모두 사용자가 2026-10-07 에 옮겨 준 글의 값이거나 지은 값):
/// 한 번에 특별발주증서 5 · 무색 승화약 20 · 수표(1000만) 5(글의 값), 아홉 번까지(글: 「총 9번 강화하면 +20」),
/// 성공하면 장갑 +1 이고 보너스가 붙으면 +2(글은 +2 라고 한다 — 유니콘의 책을 쓴 값으로 보았다. 책 없이 보너스가 붙을 확률 2할은 지은 값),
/// 성공 확률 5할 + 연금술 랭크마다 2%(9할까지, 지은 값), 실패하면 소재를 잃고 횟수는 안 준다(지은 것). 연금술 스킬이 있어야 한다.
/// 변성은 부품의 갈래(번호)에 붙는다 — 같은 장갑을 여럿 달아도 하나로 친다(대장간의 단련과 같다).
/// </summary>
internal sealed partial class Voyage
{
    public const int OrderPaper = 1510042, Check10M = 1500504, OuroborosBook = 1510498, UnicornBook = 1510500, ClearElixir = 1600290;
    public const int TransmutePapers = 5, TransmuteElixirs = 20, TransmuteChecks = 5, TransmuteTimes = 9;

    /// <summary>그 장갑에 변성을 성공한 횟수.</summary>
    public int TransmutedTimes(int partId) => ForgedOf(partId, 1);
    /// <summary>변성으로 더해진 장갑.</summary>
    public int TransmutedArmor(int partId) => ForgedOf(partId, 0);
    /// <summary>달아 둔 장갑의 장갑 값 — 변성으로 오른 것까지.</summary>
    public int ArmorOf(ShipPart part) => part.A + (part.Slot == 1 ? TransmutedArmor(part.Id) : 0);

    public double TransmuteChance => Math.Min(0.9, 0.5 + RankByName("연금술") * 0.02);
    public int ElixirsHeld => Cargo.TryGetValue(ClearElixir, out var held) ? held.Count : 0;

    public string? TransmuteBlocker(ShipPart armor) =>
        Mode != Mode.Port ? "항구에서만 한다"
        : RankByName("연금술") <= 0 ? "연금술 스킬이 없다"
        : TransmutedTimes(armor.Id) >= TransmuteTimes ? $"변성실행 {TransmuteTimes}/{TransmuteTimes} — 더는 못 한다"
        : Items.GetValueOrDefault(OrderPaper) < TransmutePapers ? $"특별발주증서 {TransmutePapers}장이 든다"
        : ElixirsHeld < TransmuteElixirs ? $"무색 승화약 {TransmuteElixirs}개가 든다(교역품)"
        : Items.GetValueOrDefault(Check10M) < TransmuteChecks ? $"수표(1000만 두캇) {TransmuteChecks}장이 든다"
        : null;

    /// <summary>변성을 한 번 한다 — 책은 갖고 있을 때만 쓴다.</summary>
    public void Transmute(ShipPart armor, bool ouroboros, bool unicorn)
    {
        if (TransmuteBlocker(armor) != null) { Cues.Enqueue("Error"); return; }
        ouroboros &= Items.GetValueOrDefault(OuroborosBook) > 0;
        unicorn &= Items.GetValueOrDefault(UnicornBook) > 0;
        SpendItem(OrderPaper, TransmutePapers);
        SpendItem(Check10M, TransmuteChecks);
        Cargo[ClearElixir].Count -= TransmuteElixirs;
        if (Cargo[ClearElixir].Count <= 0) Cargo.Remove(ClearElixir);
        if (ouroboros) SpendItem(OuroborosBook, 1);
        if (unicorn) SpendItem(UnicornBook, 1);          // 성공하든 못 하든 책은 없어진다(아이템 설명 그대로)
        Train(Data.Skills.Find(s => s.Name == "연금술")!.Id, 60);
        if (!ouroboros && _random.NextDouble() >= TransmuteChance)
        {
            Cues.Enqueue("Error");
            Say($"{armor.Name} — 변성에 실패했다. 변성소재를 잃었다.");
            return;
        }
        bool bonus = unicorn || _random.NextDouble() < 0.2;
        if (!Forged.TryGetValue(armor.Id, out var added)) Forged[armor.Id] = added = [0, 0];
        added[0] += bonus ? 2 : 1;
        added[1]++;
        Cues.Enqueue("Done");
        Say($"{armor.Name} — 변성 성공{(bonus ? "(보너스)" : "")}. 장갑 {armor.A + added[0]} (변성실행 {added[1]}/{TransmuteTimes})");
    }

    private void SpendItem(int item, int count)
    {
        int left = Items.GetValueOrDefault(item) - count;
        if (left > 0) Items[item] = left; else Items.Remove(item);
    }

    /// <summary>대본용: 변성 소재와 책을 넉넉히 받는다.</summary>
    public void TransmuteKitForTest()
    {
        AddItem(OrderPaper, 50); AddItem(Check10M, 50); AddItem(OuroborosBook, 9); AddItem(UnicornBook, 9);
        if (Data.Goods.Find(g => g.Id == ClearElixir) is { } elixir) GiveGood(elixir, 200);
    }
}
