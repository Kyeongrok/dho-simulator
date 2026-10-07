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
    /// <summary>대본용: 다음 생산은 모두 대성공.</summary>
    public bool GreatForTest { get; set; }

    public RecipeRule? RuleOf(int recipe) => Data.RecipeRules.Find(r => r.RecipeId == recipe);

    /// <summary>교역품 번호인가(1600001 ~) — 아니면 소지품의 아이템.</summary>
    public static bool IsGoodId(int id) => id is >= 1_600_000 and < 1_700_000;

    // ── 레시피 책 ──
    // 책에 든 레시피는 그 책을 가져야 열린다(사용자, 2026-10-07: 「레시피 책을 사야만 열리게」). 어느 책에도 없는 레시피(아직 책 자료를 못 받은 갈래)만 낱개로 가진다.
    // 책과 든 레시피, 도구점 값은 이용자 사이트(gvdb)의 것. 어느 도시의 도구점이 어느 책을 파는가는 모른다 — 값을 아는 책은 모든 도구점에 선다(지은 것).
    private Dictionary<int, List<RecipeBook>>? _booksOf;

    /// <summary>그 레시피가 든 책들 — 없으면 빈 목록.</summary>
    public List<RecipeBook> BooksOf(int recipe)
    {
        if (_booksOf == null)
        {
            _booksOf = [];
            foreach (var book in Data.RecipeBooks)
                foreach (int id in book.Recipes)
                {
                    if (!_booksOf.TryGetValue(id, out var list)) _booksOf[id] = list = [];
                    list.Add(book);
                }
        }
        return _booksOf.GetValueOrDefault(recipe) ?? [];
    }

    public bool OwnsBook(RecipeBook book) => Items.GetValueOrDefault(book.ItemId) > 0;

    /// <summary>그 레시피를 쓸 수 있는가 — 책에 든 것은 책을 가졌을 때, 아니면 낱개로 얻었을 때.</summary>
    public bool KnowsRecipe(int recipe) => BooksOf(recipe) is { Count: > 0 } books ? books.Exists(OwnsBook) : Recipes.Contains(recipe);

    /// <summary>지금 쓸 수 있는 레시피 전부 — 가진 책의 것과 낱개로 얻은 것.</summary>
    public List<int> KnownRecipes() =>
        Data.RecipeBooks.Where(OwnsBook).SelectMany(b => b.Recipes).Concat(Recipes.Where(id => BooksOf(id).Count == 0)).Distinct().OrderBy(id => id).ToList();

    public RecipeBook? RecipeBookOf(int item) => Data.RecipeBooks.Find(b => b.ItemId == item);

    /// <summary>이 도시의 도구점이 그 아이템을 파는가 — 레시피 책은 실제로 파는 도시(gvdb)에서만. 다른 아이템은 어디서나(지은 것).</summary>
    public bool ShopSells(ItemData item) =>
        item.Price > 0 && (item.Effect != "RecipeBook" || Data.MarketFacts.Count == 0 || (Data.MarketFacts.Find(f => f.CityId == City.Id)?.Items.Exists(i => i[0] == item.Id) ?? false));

    public void AddRecipe(RecipeData recipe)
    {
        // 책에 든 레시피는 낱개로 못 얻는다 — 그 책을 넣어 준다(대본 · 개발용)
        if (BooksOf(recipe.Id) is { Count: > 0 } books) { if (!books.Exists(OwnsBook)) AddItem(books[0].ItemId); return; }
        if (Recipes.Add(recipe.Id)) Say($"레시피 「{recipe.Name}」을(를) 얻었다.");
    }

    /// <summary>싣고 있는 재료로 몇 번 만들 수 있는가.</summary>
    public int CanProduce(RecipeRule rule)
    {
        int times = int.MaxValue;
        // 재료의 번호가 교역품(창고)이 아니면 소지품의 아이템이다(재봉도구 따위)
        foreach (var (good, count) in rule.InputList())
            times = Math.Min(times, (IsGoodId(good) ? (Cargo.TryGetValue(good, out var item) ? item.Count : 0) : Items.GetValueOrDefault(good)) / Math.Max(1, count));
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
    /// <summary>생산 한 번에 드는 행동력 — 원본도 생산에 행동력이 든다(사용자, 2026-10-07). 얼마인지는 못 찾아 지은 값.</summary>
    public const int ProduceVigour = 5;

    /// <summary>
    /// 생산 한 번에 오르는 숙련도 — 사용자가 준 원본의 식(2026-10-07):
    /// 성공 = (레시피 요구 랭크 + 1 − 내 스킬 랭크) × 2, 0 이하면 1. 대성공 = (레시피 요구 랭크 + 2) × 2(내 랭크와 상관없다).
    /// 내 랭크는 부스트를 뺀 제 랭크로 본다(부스트로 제 랭크보다 높은 레시피를 만들면 많이 오른다).
    /// 원본의 「랭크마다 필요한 숙련도」 표를 몰라 이쪽 표(기준값 × 랭크²)에 맞추려고 12.5 를 곱한다 — 같은 랭크 레시피가 전처럼 25 가 되게. 이 곱은 지은 값.
    /// </summary>
    public const double ProduceExpScale = 12.5;

    public int ProduceExp(RecipeRule rule, bool great = false)
    {
        if (RecipeSkill(rule) is not { } need) return 0;
        int mine = Skills.TryGetValue(need.SkillId, out var state) ? state.Rank : 0;
        int raw = great ? (need.Rank + 2) * 2 : Math.Max(1, (need.Rank + 1 - mine) * 2);
        return (int)Math.Round(raw * ProduceExpScale);
    }

    public string? ProduceBlocker(RecipeRule rule, int times)
    {
        if (!KnowsRecipe(rule.RecipeId)) return BooksOf(rule.RecipeId) is { Count: > 0 } books ? $"레시피 책 「{books[0].Name}」이(가) 있어야 한다" : "레시피가 없다";
        if (rule.Facility != "" && LabTier(rule.Facility) == 0) return $"{LabName(rule.Facility)}가 있어야 한다 — {(rule.Facility == "Furnace" ? "화로를 사용한 연금술" : "실험대에서 진행하는 연금술")}";
        if (rule.ToolList().FirstOrDefault(tool => Items.GetValueOrDefault(tool) <= 0) is > 0 and var missing) return $"도구 「{ItemName(missing)}」이(가) 있어야 한다";
        if (RecipeSkill(rule) is { } need && Rank(need.SkillId) < need.Rank) return $"{SkillName(need.SkillId)} 랭크 {need.Rank} 이 있어야 한다";
        if (rule.ConsumeList().FirstOrDefault(tool => Items.GetValueOrDefault(tool) < times) is > 0 and var worn) return $"「{ItemName(worn)}」이(가) 모자라다 — 한 번에 하나씩 닳는다";
        if (CanProduce(rule) < times) return "재료가 모자라다";
        int used = rule.InputList().Where(i => IsGoodId(i.Good)).Sum(i => i.Count) * times, made = rule.OutputCount * times;
        if (rule.OutputItem == 0 && HoldFree + used < made) return "창고가 모자라다";
        if (Vigour < ProduceVigour * times) return $"행동력이 모자라다 — 한 번에 {ProduceVigour}";
        return null;
    }

    /// <summary>재료를 쓰고 생산물을 창고에 넣는다. 든 값은 쓴 재료의 산 값을 그대로 물려받는다.</summary>
    public void Produce(RecipeRule rule, int times = 1)
    {
        times = Math.Min(times, CanProduce(rule));
        if (times <= 0 || ProduceBlocker(rule, times) != null) return;
        long cost = 0;
        SpendVigour(ProduceVigour * times);
        foreach (int tool in rule.ConsumeList()) SpendItem(tool, times);
        // 연성한 생산 스킬은 재료를 아껴 준다 — 한 번마다 10%로 그 번의 재료가 안 든다(지은 값)
        int spared = RecipeSkill(rule) is { } craft && Refined(craft.SkillId) ? Enumerable.Range(0, times).Count(_ => _random.Next(100) < 10) : 0;
        if (spared > 0) Say($"연성한 솜씨로 재료를 {spared}번 아꼈다.");
        foreach (var (good, count) in rule.InputList())
        {
            if (!IsGoodId(good)) { SpendItem(good, count * (times - spared)); continue; }
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
        // 대성공 — 원본의 글 16503 「생산 대성공!!」. 대성공한 번은 생산물이 곱절로 나온다(1개 만들 것이 2개 — 사용자, 2026-10-07; gvdb 의 「成功１　大成功２」).
        // 확률은 클라이언트에 없다 — 지은 값: 10% + (스킬 랭크 − 필요 랭크) × 2%, 30%까지. 연금술 실험(설비)은 뺀다
        int great = 0;
        if (rule.Facility == "")
        {
            double chance = Math.Min(0.30, 0.10 + (RecipeSkill(rule) is { } skilled ? Math.Max(0, Rank(skilled.SkillId) - skilled.Rank) * 0.02 : 0));
            for (int k = 0; k < done; k++) if (_random.NextDouble() < chance) great++;
            if (GreatForTest) (great, GreatForTest) = (done, false);
        }
        if (rule.OutputItem > 0)
        {
            if (done > 0) Items[rule.OutputItem] = Items.GetValueOrDefault(rule.OutputItem) + rule.OutputCount * (done + great);
        }
        else if (done > 0)
        {
            if (!Cargo.TryGetValue(rule.Output, out var made)) Cargo[rule.Output] = made = new CargoItem();
            made.Count += Math.Min(rule.OutputCount * (done + great), rule.OutputCount * done + Math.Max(0, HoldFree));      // 대성공의 덤은 선창에 들어가는 만큼만
            made.Cost += cost;
        }
        Fatigue = Math.Min(100, Fatigue + 0.5 * times);
        if (RecipeSkill(rule) is { } used) Train(used.SkillId, ProduceExp(rule) * (times - great) + ProduceExp(rule, true) * great);      // 실패한 번도 성공만큼 오른다(실패 때의 양은 모른다)
        Studied("Produce", times);
        GainMastery();
        if (great > 0) { Say(Text(16503, "생산 대성공!!") + (times > 1 ? $" ({great}번)" : "")); Cues.Enqueue("Done"); }
        if (done > 0) Say($"{(rule.OutputItem > 0 ? ItemName(rule.OutputItem) : Good(rule.Output)?.Name ?? "물건")} {rule.OutputCount * (done + great)}개를 만들었다.");
    }
}
