using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 애완동물 — 한 마리를 데리고 다닌다. 「조교」 스킬(설명: 「애완동물이 특수한 효과를 발휘한다. 랭크에 따라 발휘하는 효과가 많아진다」)이 효과를 끌어낸다.
/// 이름은 클라이언트 표 47(57마리)의 것이다. 원본 화면 글에서 읽은 것: 「친밀도」(8120) · 「필요 조련 스킬랭크」 · 「필요 친밀도」(8121 · 8122),
/// 「%s가 %s %d개를 발견했습니다！」(8129), 「%s가 주위의 적을 경계하여 전투를 회피하였습니다！」(8132), 「%s가 배가 고픈 것 같습니다.」(8123), 「%s가 기뻐하고 있습니다！」(8125).
/// 그래서 효과는 둘 — 물건을 찾아 오는 것과 적을 경계해 싸움을 피하는 것. 어느 동물이 무슨 스킬을 갖는지 · 수치는 클라이언트에 없다.
/// 지은 것: 주점에서 5,000 두캇에 아무 한 마리를 들인다(원본은 권리서 · 조련사), 바다에서 하루에 친밀도 +1(100까지) · 식량 0.2,
/// 식량이 떨어지면 친밀도 −5, 하루에 (조교 랭크 × 1 + 친밀도 ÷ 20)% 로 교역품 1 ~ 3개를 찾고, 해적이 덤비려 할 때 (조교 랭크 × 2 + 친밀도 ÷ 10)% 로 피한다.
/// </summary>
internal sealed partial class Voyage
{
    public const int PetPrice = 5000;
    public int PetId { get; private set; }
    public int PetLove { get; private set; }

    public string PetName => Data.Pets.Find(p => p.Id == PetId)?.Name ?? "";
    public bool HasPet => PetId != 0 && PetName != "";

    /// <summary>주점에서 애완동물을 들인다 — 이미 있으면 바꾼다(친밀도는 처음부터).</summary>
    public void BuyPet()
    {
        if (Mode != Mode.Port || Money < PetPrice || Data.Pets.Count == 0) { Cues.Enqueue("Error"); return; }
        Money -= PetPrice;
        string old = PetName;
        (PetId, PetLove) = (Data.Pets[_random.Next(Data.Pets.Count)].Id, 10);
        Cues.Enqueue("Buy");
        Say((old == "" ? "" : $"{old}을(를) 떠나보내고 ") + $"{PetName}을(를) 들였다. ({PetPrice:N0} 두캇, {Text(8120, "친밀도")} {PetLove})");
    }

    // 날이 바뀔 때마다(바다에서)
    private void UpdatePet(int days)
    {
        if (!HasPet || Mode != Mode.Sea) return;
        int rank = RankByName("조교");
        for (int day = 0; day < days; day++)
        {
            bool grew = false;
            if (Food >= 0.2) { Food -= 0.2; if (PetLove < 100) { PetLove++; grew = true; } }
            else
            {
                PetLove = Math.Max(0, PetLove - 5);
                Say(Fill(Text(8123, "%s가 배가 고픈 것 같습니다."), PetName));
                continue;
            }
            if (grew && PetLove is 50 or 100 && grew) Say(Fill(Text(8125, "%s가 기뻐하고 있습니다！"), PetName));
            if (rank <= 0) continue;
            Train(Data.Skills.Find(s => s.Name == "조교")!.Id, 6);
            if (HoldFree > 0 && Data.Goods.Count > 0 && _random.NextDouble() < (rank + PetLove / 20.0) / 100)
            {
                var good = Data.Goods[_random.Next(Data.Goods.Count)];
                int count = Math.Min(HoldFree, 1 + _random.Next(3));
                GiveGood(good, count);
                Say(Fill(Text(8129, "%s가 %s %d개를 발견했습니다！"), PetName, good.Name, $"{count}"));
            }
        }
    }

    // 해적이 덤비려는 참에 — 애완동물이 알아채 피하는가
    private bool PetWards(SeaShip ship)
    {
        int rank = RankByName("조교");
        if (!HasPet || rank <= 0 || _random.NextDouble() >= (rank * 2 + PetLove / 10.0) / 100) return false;
        Say(Fill(Text(8132, "%s가 주위의 적을 경계하여 전투를 회피하였습니다！"), PetName) + $" (해적선 「{ship.Name}」)");
        return true;
    }

    /// <summary>대본용: 애완동물과 친밀도를 정한다.</summary>
    public void PetForTest(int love) => (PetId, PetLove) = (Data.Pets.Count > 0 ? Data.Pets[0].Id : 0, love);
}
