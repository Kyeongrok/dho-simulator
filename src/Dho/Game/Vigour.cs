using Dho.Data;

namespace Dho.Game;

// 행동력 — 원본에서 스킬을 쓸 때마다 드는 힘. 음식(설명의 「행동력+n」)을 먹거나 주점에서 한턱내면 차고, 날이 지나면 조금씩 돌아온다.
// 상한은 원본의 식이고, 스킬마다 드는 양과 돌아오는 빠르기는 지은 값이다(스킬 규칙의 Vigour 칸에서 고친다, 0 이면 갈래에 따른 기본값).
internal sealed partial class Voyage
{
    private double _vigour = -1;

    // 상한 — 사용자가 준 원본의 식(2026-10-07): 200 + (모험 + 상인 + 군인 레벨) × 5
    public int MaxVigour => 200 + (LevelOf(AdventureExp).Level + LevelOf(TradeExp).Level + LevelOf(BattleExp).Level) * 5;

    public double Vigour
    {
        get => _vigour < 0 ? MaxVigour : Math.Min(_vigour, MaxVigour);
        private set => _vigour = Math.Clamp(value, 0, MaxVigour);
    }

    /// <summary>
    /// 입은 장비의 「행동력 감소 억제」 랭크 — 스킬을 쓸 때 드는 행동력이 그 랭크만큼 덜 든다(1 까지).
    /// 사용자가 준 글(2026-10-07): 「장비에 붙은 랭크만큼 행동력 소모 수치가 직접 차감됩니다(최소 1까지)」. 어느 장비에 몇 랭크인지는 gvdb 의 것.
    /// 여러 벌을 입었을 때는 가장 높은 것 하나만 친다(더하는지는 모른다).
    /// </summary>
    public int VigourSave => Equipped.Where(e => e > 0 && Items.GetValueOrDefault(e) > 0).Select(e => Data.GearEffects.GetValueOrDefault(e)?.GetValueOrDefault("VigourSave") ?? 0).DefaultIfEmpty(0).Max();

    // 그 스킬을 한 번 쓰는 데 드는 행동력(장비의 행동력 감소 억제를 뺀 값)
    public int VigourCost(SkillRuleData rule) => Math.Max(1, VigourCostBase(rule) - VigourSave);

    private static int VigourCostBase(SkillRuleData rule) => rule.Vigour > 0 ? rule.Vigour : rule.Effect switch
    {
        "Speed" or "Turn" => 8,
        "Survey" => 5,
        "Procure" or "Fish" or "Gather" => 15,
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
