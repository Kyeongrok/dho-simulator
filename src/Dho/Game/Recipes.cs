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

    /// <summary>못 만드는 까닭.</summary>
    public string? ProduceBlocker(RecipeRule rule, int times)
    {
        if (!Recipes.Contains(rule.RecipeId)) return "레시피가 없다";
        if (RecipeSkill(rule) is { } need && Rank(need.SkillId) < need.Rank) return $"{SkillName(need.SkillId)} 랭크 {need.Rank} 이 있어야 한다";
        if (CanProduce(rule) < times) return "재료가 모자라다";
        int used = rule.InputList().Sum(i => i.Count) * times, made = rule.OutputCount * times;
        if (HoldFree + used < made) return "창고가 모자라다";
        return null;
    }

    /// <summary>재료를 쓰고 생산물을 창고에 넣는다. 든 값은 쓴 재료의 산 값을 그대로 물려받는다.</summary>
    public void Produce(RecipeRule rule, int times = 1)
    {
        times = Math.Min(times, CanProduce(rule));
        if (times <= 0 || ProduceBlocker(rule, times) != null) return;
        long cost = 0;
        foreach (var (good, count) in rule.InputList())
        {
            var item = Cargo[good];
            long share = item.Cost * (count * times) / Math.Max(1, item.Count);
            cost += share;
            item.Cost -= share;
            item.Count -= count * times;
            if (item.Count <= 0) Cargo.Remove(good);
        }
        if (!Cargo.TryGetValue(rule.Output, out var made)) Cargo[rule.Output] = made = new CargoItem();
        made.Count += rule.OutputCount * times;
        made.Cost += cost;
        Fatigue = Math.Min(100, Fatigue + 0.5 * times);
        if (RecipeSkill(rule) is { } used) Train(used.SkillId, 25 * times);
        Say($"{Good(rule.Output)?.Name ?? "물건"} {rule.OutputCount * times}개를 만들었다.");
    }
}
