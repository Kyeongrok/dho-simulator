using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 레시피와 생산 — 가진 레시피를 열어 싣고 있는 교역품으로 다른 교역품을 만든다.
/// 레시피의 이름과 설명은 클라이언트 표 16 의 것이다. 재료와 생산물은 클라이언트에 없어서
/// <c>recipes.json</c> 에 지어 적었다(이름에서 생산물을 알 수 있는 교역품 레시피만).
/// </summary>
internal sealed partial class Voyage
{
    public HashSet<int> Recipes { get; } = [];

    public RecipeRule? RuleOf(int recipe) => Data.RecipeRules.Find(r => r.RecipeId == recipe);

    public void AddRecipe(RecipeData recipe)
    {
        if (Recipes.Add(recipe.Id)) Say($"레시피 「{recipe.Name}」을(를) 얻었다.");
    }

    /// <summary>싣고 있는 재료로 몇 번 만들 수 있는가.</summary>
    public int CanProduce(RecipeRule rule)
    {
        int times = int.MaxValue;
        foreach (var (good, count) in rule.InputList())
            times = Math.Min(times, (Cargo.TryGetValue(good, out var item) ? item.Count : 0) / Math.Max(1, count));
        foreach (int tool in rule.ConsumeList()) times = Math.Min(times, Items.GetValueOrDefault(tool));
        return times == int.MaxValue ? 0 : times;
    }

    /// <summary>레시피가 요구하는 스킬(번호와 랭크). 없으면 null.</summary>
    public (int SkillId, int Rank)? RecipeSkill(RecipeRule rule)
    {
        var parts = rule.Skill.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[^1], out int rank)) return null;
        string name = string.Join(' ', parts[..^1]);
        return Data.Skills.Find(s => s.Name == name) is { } skill ? (skill.Id, rank) : null;
    }

    /// <summary>
    /// 가진 실험 설비 가운데 가장 좋은 것의 급(1 간이 · 2 보통 · 3 개량) — 없으면 0. 연금술 복합시설은 화로와 실험대를 겸한다(급 2 로 친다).
    /// 설비는 items.json 의 Effect "Lab" 아이템이고 Amount 의 십 자리가 갈래(1 화로 · 2 실험대 · 3 복합), 일 자리가 급이다.
    /// </summary>
    public int LabTier(string facility)
    {
        int kind = facility == "Furnace" ? 1 : 2, best = 0;
        foreach (var lab in Data.Items.Where(i => i.Effect == "Lab" && Items.GetValueOrDefault(i.Id) > 0))
        {
            int family = (int)lab.Amount / 10, tier = (int)lab.Amount % 10;
            if (family == kind) best = Math.Max(best, tier);
            else if (family == 3) best = Math.Max(best, 2);
        }
        return best;
    }

    public static string LabName(string facility) => facility == "Furnace" ? "실험 화로" : "실험대";

    /// <summary>실험이 실패할 확률(%) — 설비의 급에 따라 20 · 10 · 3(지은 값).</summary>
    public int LabFailChance(RecipeRule rule) => rule.Facility == "" ? 0 : LabTier(rule.Facility) switch { 1 => 20, 2 => 10, 3 => 3, _ => 100 };

    /// <summary>못 만드는 까닭.</summary>
    public string? ProduceBlocker(RecipeRule rule, int times)
    {
        if (!Recipes.Contains(rule.RecipeId)) return "레시피가 없다";
        if (rule.Facility != "" && LabTier(rule.Facility) == 0) return $"{LabName(rule.Facility)}가 있어야 한다 — {(rule.Facility == "Furnace" ? "화로를 사용한 연금술" : "실험대에서 진행하는 연금술")}";
        if (rule.ToolList().FirstOrDefault(tool => Items.GetValueOrDefault(tool) <= 0) is > 0 and var missing) return $"도구 「{ItemName(missing)}」이(가) 있어야 한다";
        if (RecipeSkill(rule) is { } need && Rank(need.SkillId) < need.Rank) return $"{SkillName(need.SkillId)} 랭크 {need.Rank} 이 있어야 한다";
        if (rule.ConsumeList().FirstOrDefault(tool => Items.GetValueOrDefault(tool) < times) is > 0 and var worn) return $"「{ItemName(worn)}」이(가) 모자라다 — 한 번에 하나씩 닳는다";
        if (CanProduce(rule) < times) return "재료가 모자라다";
        int used = rule.InputList().Sum(i => i.Count) * times, made = rule.OutputCount * times;
        if (rule.OutputItem == 0 && HoldFree + used < made) return "창고가 모자라다";
        return null;
    }

    /// <summary>재료를 쓰고 생산물을 창고에 넣는다. 든 값은 쓴 재료의 산 값을 그대로 물려받는다.</summary>
    public void Produce(RecipeRule rule, int times = 1)
    {
        times = Math.Min(times, CanProduce(rule));
        if (times <= 0 || ProduceBlocker(rule, times) != null) return;
        long cost = 0;
        foreach (int tool in rule.ConsumeList()) SpendItem(tool, times);
        // 연성한 생산 스킬은 재료를 아껴 준다 — 한 번마다 10%로 그 번의 재료가 안 든다(지은 값)
        int spared = RecipeSkill(rule) is { } craft && Refined(craft.SkillId) ? Enumerable.Range(0, times).Count(_ => _random.Next(100) < 10) : 0;
        if (spared > 0) Say($"연성한 솜씨로 재료를 {spared}번 아꼈다.");
        foreach (var (good, count) in rule.InputList())
        {
            var item = Cargo[good];
            long share = item.Cost * (count * (times - spared)) / Math.Max(1, item.Count);
            cost += share;
            item.Cost -= share;
            item.Count -= count * (times - spared);
            if (item.Count <= 0) Cargo.Remove(good);
        }
        // 연금술 실험은 설비의 급에 따라 한 번씩 실패할 수 있다 — 재료는 들고 나오는 것이 없다
        int done = times;
        if (rule.Facility != "")
            for (int k = 0; k < times; k++)
                if (_random.Next(100) < LabFailChance(rule)) done--;
        if (done < times) Say(done == 0 ? "생산에 실패했습니다." : $"실험 {times}번 가운데 {times - done}번은 실패했다.");
        if (rule.OutputItem > 0)
        {
            if (done > 0) Items[rule.OutputItem] = Items.GetValueOrDefault(rule.OutputItem) + rule.OutputCount * done;
        }
        else if (done > 0)
        {
            if (!Cargo.TryGetValue(rule.Output, out var made)) Cargo[rule.Output] = made = new CargoItem();
            made.Count += rule.OutputCount * done;
            made.Cost += cost;
        }
        Fatigue = Math.Min(100, Fatigue + 0.5 * times);
        if (RecipeSkill(rule) is { } used) Train(used.SkillId, 25 * times);
        Studied("Produce", times);
        GainMastery();
        if (done > 0) Say($"{(rule.OutputItem > 0 ? ItemName(rule.OutputItem) : Good(rule.Output)?.Name ?? "물건")} {rule.OutputCount * done}개를 만들었다.");
    }
}
