using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 퀵슬롯 — 자주 쓰는 스킬과 소비 아이템을 올려 두고 숫자 글쇠(1 ~ 8)로 쓴다. 원본처럼 여덟 칸짜리가 세 쪽이다.
/// 칸의 값: 양수 = 스킬 번호, 음수 = −아이템 번호, 0 = 빈 칸.
/// </summary>
internal sealed partial class Voyage
{
    public const int QuickPageSize = 8, QuickPages = 3, QuickSlotCount = QuickPageSize * QuickPages;

    public int[] QuickSlots { get; private set; } = new int[QuickSlotCount];
    /// <summary>지금 펼친 쪽(0 ~ 2) — 숫자 글쇠는 이 쪽의 칸을 쓴다.</summary>
    public int QuickPage { get; private set; }

    public void TurnQuickPage(int delta) => QuickPage = ((QuickPage + delta) % QuickPages + QuickPages) % QuickPages;

    /// <summary>그 칸에 올린다(0 이면 비운다).</summary>
    public void SetQuickSlot(int slot, int value)
    {
        if (slot >= 0 && slot < QuickSlotCount) QuickSlots[slot] = value;
    }

    /// <summary>지금 쪽의 n 번째 칸(0 ~ 7)을 쓴다.</summary>
    public void UsePageSlot(int n) => UseQuickSlot(QuickPage * QuickPageSize + n);

    /// <summary>눌러 쓰는 스킬 전부(익힌 것) — F2 창에 늘어놓는다.</summary>
    public List<SkillRuleData> UsableSkills() => SeaSkills().ToList();

    /// <summary>쓸 수 있는 소비 아이템(가진 것).</summary>
    public List<ItemData> UsableItems() => Data.Items.Where(i => i.Effect != "Lifebuoy" && Items.GetValueOrDefault(i.Id) > 0).ToList();

    // 퀵슬롯에 올릴 수 있는 가진 아이템의 번호 — 도구점의 소비 아이템에 더해 음식(행동력) · 부스트 아이템
    public List<int> UsableItemIds() =>
        Items.Where(i => i.Value > 0 && (ItemOf(i.Key) is { Effect: not "Lifebuoy" } || FoodOf(i.Key) != null || BoosterOf(i.Key) != null)).Select(i => i.Key).ToList();

    public bool InQuickSlot(int value) => Array.IndexOf(QuickSlots, value) >= 0;

    /// <summary>첫 빈 칸에 올린다. 이미 있거나 빈 칸이 없으면 그대로.</summary>
    public void AddQuickSlot(int value)
    {
        if (value == 0 || InQuickSlot(value)) return;
        int free = Array.IndexOf(QuickSlots, 0);
        if (free < 0) { Say("퀵슬롯에 빈 칸이 없다."); return; }
        QuickSlots[free] = value;
    }

    public void ClearQuickSlot(int slot)
    {
        if (slot >= 0 && slot < QuickSlotCount) QuickSlots[slot] = 0;
    }

    /// <summary>그 칸의 것을 쓴다.</summary>
    public void UseQuickSlot(int slot)
    {
        if (slot < 0 || slot >= QuickSlotCount) return;
        int value = QuickSlots[slot];
        if (value > 0 && Data.SkillRules.Find(r => r.SkillId == value) is { } rule) UseSkill(rule);
        else if (value < 0) UseItem(-value);
    }

    /// <summary>칸에 든 것의 이름. 빈 칸이면 빈 글.</summary>
    public string QuickSlotName(int slot)
    {
        int value = QuickSlots[slot];
        return value > 0 ? SkillName(value) : value < 0 ? ItemName(-value) : "";
    }

    /// <summary>눌러 쓰는 스킬을 새로 익히면 퀵슬롯에 저절로 올린다.</summary>
    private void QuickSlotLearned(int skillId)
    {
        if (SeaSkills().Any(r => r.SkillId == skillId)) AddQuickSlot(skillId);
    }
}
