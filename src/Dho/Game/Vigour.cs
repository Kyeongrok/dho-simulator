using Dho.Data;

namespace Dho.Game;

// 행동력 — 원본에서 스킬을 쓸 때마다 드는 힘. 음식(설명의 「행동력+n」)을 먹거나 주점에서 한턱내면 차고, 날이 지나면 조금씩 돌아온다.
// 상한과 스킬마다 드는 양, 돌아오는 빠르기는 지은 값이다(스킬 규칙의 Vigour 칸에서 고친다, 0 이면 갈래에 따른 기본값).
internal sealed partial class Voyage
{
    private double _vigour = -1;

    // 상한: 100 에 모험 · 교역 레벨마다 5 씩(500 까지)
    public int MaxVigour => Math.Min(500, 100 + (LevelOf(AdventureExp).Level + LevelOf(TradeExp).Level) * 5);

    public double Vigour
    {
        get => _vigour < 0 ? MaxVigour : Math.Min(_vigour, MaxVigour);
        private set => _vigour = Math.Clamp(value, 0, MaxVigour);
    }

    // 그 스킬을 한 번 쓰는 데 드는 행동력
    public int VigourCost(SkillRuleData rule) => rule.Vigour > 0 ? rule.Vigour : rule.Effect switch
    {
        "Speed" or "Turn" => 8,
        "Survey" => 5,
        "Procure" or "Fish" => 15,
        "Repair" => 20,
        "Rest" => 25,
        "Haggle" => 10,
        _ => 10,
    };

    // 행동력을 쓴다 — 모자라면 false(아무것도 안 깎는다)
    public bool SpendVigour(int amount)
    {
        if (Vigour < amount) return false;
        Vigour -= amount;
        return true;
    }

    public void GainVigour(double amount) => Vigour += amount;

    // 하루에 돌아오는 양 — 바다에서 6, 항구에서 20
    private void RestoreVigour(double days) => Vigour += days * (Mode == Mode.Port ? 20 : 6);
}
