using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 스킬 연성 — 끝까지 올린 스킬을 처음으로 되돌리고 그 대신 랭크 +2 를 늘 얻는다.
/// 원본의 글(화면 글): 342 「연성 스킬」, 6624 「연성할 스킬을 선택하십시오. 연성한 스킬은 +2됩니다. 연성 시 랭크는 1이 됩니다.」,
/// 6625 「스킬을 연성하면 랭크와 숙련도가 초기 수치로 돌아갑니다. 이대로 진행하시겠습니까?」, 3011 「스킬「%s」를 연성했습니다.」.
/// 어디서 · 언제 하는가와 덤은 이용자 글(인벤 dho/498/18903)의 것: 상트 페테르부르크에서, 스킬이 끝 랭크일 때,
/// 다음 랭크까지의 숙련도가 8할이 되고 생산 재료를 가끔 아낀다(아끼는 확률 10%는 지은 값).
/// 원본은 상인조합 마스터에게 말을 건다 — 여기서는 그 도시에 있을 때 스킬 창의 단추로 한다. 한 스킬은 한 번만.
/// </summary>
internal sealed partial class Voyage
{
    public const int RefineBoost = 2, RefineCity = 195;

    public bool Refined(int skillId) => Skills.TryGetValue(skillId, out var state) && state.Refined;

    /// <summary>다음 랭크까지의 숙련도 — 연성한 스킬은 8할.</summary>
    public int ExpNeed(SkillState state) => (int)(ExpToNext(state.Rank) * (state.Refined ? 0.8 : 1));

    /// <summary>스킬 창에 연성 단추를 보일 것인가 — 아직 연성하지 않았고 끝 랭크인 스킬.</summary>
    public bool CanRefineSoon(int skillId) => Skills.TryGetValue(skillId, out var state) && !state.Refined && state.Rank >= Settings.MaxSkillRank;

    public string? RefineBlocker(int skillId) =>
        !Skills.TryGetValue(skillId, out var state) ? "익히지 않았다"
        : state.Refined ? "이미 연성한 스킬이다"
        : state.Rank < Settings.MaxSkillRank ? $"랭크 {Settings.MaxSkillRank} 까지 올려야 한다"
        : Mode != Mode.Port || City.Id != RefineCity ? $"{Data.Cities.Find(c => c.Id == RefineCity)?.Name ?? "상트 페테르부르크"}에서 한다"
        : null;

    public void Refine(int skillId)
    {
        if (RefineBlocker(skillId) is { } why) { Say(why); Cues.Enqueue("Error"); return; }
        var state = Skills[skillId];
        (state.Rank, state.Exp, state.Refined) = (1, 0, true);
        Cues.Enqueue("SkillUp");
        Say($"스킬 「{SkillName(skillId)}」을(를) 연성했습니다. 랭크 1(+{RefineBoost})부터 다시 올린다.");      // 원본 글 3011 은 괄호 글자가 깨져 있어 다시 적었다
    }
}
