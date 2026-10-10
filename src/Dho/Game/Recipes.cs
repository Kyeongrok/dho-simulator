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

    // 선박 부품(대포 · 돛 · 장갑 · 선수상 · 특수장비)도 레시피의 생산물 · 재료가 된다 — 부품은 소지품이 아니라 「소유 선박부품」에 하나씩 든다
    /// <summary>장비 연성 레시피인가 — 재료의 장비가 생산물과 같고 설명에 「실패하면 … 소실」이 있는 것. 올리는 칸(0 공격력 · 1 방어력), 아니면 −1.</summary>
    public int TemperStat(RecipeRule rule) =>
        rule.OutputItem > 0 && rule.InputList().Any(i => i.Good == rule.OutputItem) && Data.Recipes.Find(r => r.Id == rule.RecipeId)?.Description is { } text && (text.Contains("소실") || text.Contains("사라진다"))
            ? (text.Contains("방어력") ? 1 : 0) : -1;

    /// <summary>대본용 — 장비 연성이 늘 실패하게.</summary>
    public bool TemperFailForTest { get; set; }

    private HashSet<int>? _partIds;
    public bool IsPartId(int id) => (_partIds ??= Data.ShipParts.Select(p => p.Id).ToHashSet()).Contains(id);
    public int HaveInput(int id) => IsGoodId(id) ? (Cargo.TryGetValue(id, out var item) ? item.Count : 0) : IsPartId(id) ? PartStock.Count(p => p.Id == id) : Items.GetValueOrDefault(id);

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
    public bool KnowsRecipe(int recipe) => RuleOf(recipe) is { Cities.Length: > 0 } local ? MakesHere(local)
        : BooksOf(recipe) is { Count: > 0 } books ? books.Exists(OwnsBook) : Recipes.Contains(recipe);

    /// <summary>
    /// 고정 레시피 — 레시피 책이 아니라 그 도시의 조선소 주인 · 조선공에게 가서 만든다(사용자, 2026-10-10: 「레시피 아이템이 있는게 아니야 도시에 가서 하는거야」).
    /// 레시피 목록에는 안 뜬다 — 그 도시의 조선소 주인에게 말을 걸면(조선소주인 차림의 「제조」) 뜬다(사용자: 「조선소나 거기 가면 npc한테 말걸면 떠야지」).
    /// 조선공의 것(선실 설계법 · 강화판 제조법)도 조선소 주인 차림에 함께 둔다(조선공에게 말을 거는 자리가 없다 — 줄인 것).
    /// </summary>
    public bool MakesHere(RecipeRule rule) => Mode == Mode.Port && rule.CityList().Contains(City.Name);
    public List<int> LocalRecipes() => Mode != Mode.Port ? [] : [.. Data.RecipeRules.Where(r => r.Cities.Length > 0 && MakesHere(r)).Select(r => r.RecipeId)];

    /// <summary>지금 쓸 수 있는 레시피 전부 — 가진 책의 것과 낱개로 얻은 것.</summary>
    public List<int> KnownRecipes() =>
        Data.RecipeBooks.Where(OwnsBook).SelectMany(b => b.Recipes).Concat(Recipes.Where(id => BooksOf(id).Count == 0)).Distinct().OrderBy(id => id).ToList();

    public RecipeBook? RecipeBookOf(int item) => Data.RecipeBooks.Find(b => b.ItemId == item);

    /// <summary>
    /// 이 도시의 도구점이 그 아이템을 파는가 — gvdb 에 파는 도시가 적힌 아이템은 그 도시에서만(레시피 책은 적힌 도시가 없으면 안 판다).
    /// 원본은 도구점 주인 · 행상인 · 거래 상인 · 공방 장인이 따로 팔지만 여기서는 모두 도구점에 모았다(줄인 것).
    /// 파는 도시가 하나도 안 적힌 아이템(원본에서는 생산 · 행사로 얻는 돛 도료 2 ~ 19 따위)은 어디서나 판다(지은 것).
    /// </summary>
    /// <summary>대본용 — 이 도시의 도구점이 파는 것을 한 줄로.</summary>
    public void ShopListForTest() =>
        Say($"(시험) {City.Name} 도구점 {Data.Items.Count(ShopSells)}가지: " + string.Join(" · ", Data.Items.Where(ShopSells).Where(i => i.Effect != "SailPaint" || i.SoldAs > 0).Select(i => i.Effect == "RecipeBook" ? "[책]" + i.Name : i.Name)));

    /// <summary>대본용 — 내구 · 생명력을 그 값으로(음수면 그대로).</summary>
    public void HurtForTest(double hull, double life)
    {
        if (hull >= 0) Durability = hull;
        if (life >= 0) Life = life;
    }

    public bool ShopSells(ItemData item) =>
        item.Price > 0 && (Data.ItemTowns.TryGetValue(item.SoldAs > 0 ? item.SoldAs : item.Id, out var towns) ? towns.Contains(City.Id) : item.Effect != "RecipeBook" || Data.MarketFacts.Count == 0);

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
            times = Math.Min(times, HaveInput(good) / Math.Max(1, count));
        foreach (int tool in rule.ConsumeList()) times = Math.Min(times, Items.GetValueOrDefault(tool));
        return times == int.MaxValue ? 0 : times;
    }

    /// <summary>레시피가 요구하는 스킬(번호와 랭크). 없으면 null.</summary>
    public (int SkillId, int Rank)? RecipeSkill(RecipeRule rule) => SkillOf(rule.Skill);
    public (int SkillId, int Rank)? SkillOf(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
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
    /// <summary>생산 한 번에 드는 행동력 — 원본의 기본값(사용자 확인, 2026-10-07).</summary>
    public const int ProduceVigour = 5;

    /// <summary>그 레시피를 times 번 만드는 데 드는 행동력 — 기본 5(사용자 확인), 그 스킬의 마이스터 호칭을 내걸었으면 20% 덜 든다(한 번에 4).</summary>
    public int ProduceVigourOf(RecipeRule rule, int times = 1) =>
        (int)Math.Ceiling(Math.Max(1, ProduceVigour - VigourSave - (RecipeSkill(rule) is { } aided ? AideCraftSave(aided.SkillId) : 0)) * times * (RecipeSkill(rule) is { } craft && MeisterOf(craft.SkillId) ? 0.8 : 1));      // 장비의 행동력 감소 억제(랭크만큼, 1 까지)

    /// <summary>
    /// 생산 한 번에 오르는 숙련도 — 사용자가 준 원본의 식(2026-10-07):
    /// 성공 = (레시피 요구 랭크 + 1 − 내 스킬 랭크) × 2, 0 이하면 1. 대성공 = (레시피 요구 랭크 + 2) × 2(내 랭크와 상관없다). 내 랭크가 요구 랭크보다 열 이상 높으면 0.
    /// 내 랭크는 부스트를 뺀 제 랭크로 본다(부스트로 제 랭크보다 높은 레시피를 만들면 많이 오른다).
    /// 여기에 모드의 「생산 · 조선 숙련도 곱」(기본 6.25 — 지은 값, 처음에는 12.5)을 곱한다. 필요 숙련도 표는 이제 원본 것(우대 랭크² × 100)이라 ×1 이 원본 그대로다.
    /// </summary>
    public double ProduceExpScale => Math.Clamp(Data.Settings.ModCraftMastery, 1, 100) / 4.0;      // 모드의 「생산 · 조선 숙련도 곱」(기본 6.25)

    public int ProduceExp(RecipeRule rule, bool great = false)
    {
        if (RecipeSkill(rule) is not { } need) return 0;
        int mine = Skills.TryGetValue(need.SkillId, out var state) ? state.Rank : 0;
        // 내 랭크가 요구 랭크보다 열 이상 높으면 숙련도가 안 오른다(사용자, 2026-10-08 — 인벤 글: 「조리 13랭부터는 돼지→돼지고기(요구 3랭)를 통한 숙련도를 얻을 수 없다」). 대성공도 같이 0 으로 본다(글에 가른 말이 없다 — 짐작)
        if (mine - need.Rank >= 10) return 0;
        int raw = great ? (need.Rank + 2) * 2 : Math.Max(1, (need.Rank + 1 - mine) * 2);
        return (int)Math.Round(raw * ProduceExpScale);
    }

    public string? ProduceBlocker(RecipeRule rule, int times)
    {
        if (rule.Cities.Length > 0 && !MakesHere(rule)) return $"{string.Join(" · ", rule.CityList())}의 조선소에서만 만든다";
        if (SkillOf(rule.Skill2) is { } also && Rank(also.SkillId) < also.Rank) return $"{SkillName(also.SkillId)} 랭크 {also.Rank} 이 있어야 한다";
        if (!KnowsRecipe(rule.RecipeId)) return BooksOf(rule.RecipeId) is { Count: > 0 } books ? $"레시피 책 「{books[0].Name}」이(가) 있어야 한다" : "레시피가 없다";
        if (rule.Facility != "" && LabTier(rule.Facility) == 0) return $"{LabName(rule.Facility)}가 있어야 한다 — {(rule.Facility == "Furnace" ? "화로를 사용한 연금술" : "실험대에서 진행하는 연금술")}";
        if (rule.ToolList().FirstOrDefault(tool => Items.GetValueOrDefault(tool) <= 0) is > 0 and var missing) return $"도구 「{ItemName(missing)}」이(가) 있어야 한다";
        if (RecipeSkill(rule) is { } need && Rank(need.SkillId) < need.Rank) return $"{SkillName(need.SkillId)} 랭크 {need.Rank} 이 있어야 한다";
        if (rule.ConsumeList().FirstOrDefault(tool => Items.GetValueOrDefault(tool) < times) is > 0 and var worn) return $"「{ItemName(worn)}」이(가) 모자라다 — 한 번에 하나씩 닳는다";
        if (CanProduce(rule) < times) return "재료가 모자라다";
        int used = rule.InputList().Where(i => IsGoodId(i.Good)).Sum(i => i.Count) * times, made = rule.OutputCount * times;
        if (rule.OutputItem == 0 && HoldFree + used < made) return "창고가 모자라다";
        if (IsPartId(rule.OutputItem) && PartStock.Count - rule.InputList().Where(i => IsPartId(i.Good)).Sum(i => i.Count) * times + made > PartStockLimit) return "부품을 더 가질 수 없다";
        if (Vigour < ProduceVigourOf(rule, times)) return $"행동력이 모자라다 — 한 번에 {ProduceVigourOf(rule)}";
        return null;
    }

    /// <summary>재료를 쓰고 생산물을 창고에 넣는다. 든 값은 쓴 재료의 산 값을 그대로 물려받는다.</summary>
    public void Produce(RecipeRule rule, int times = 1)
    {
        times = Math.Min(times, CanProduce(rule));
        if (times <= 0 || ProduceBlocker(rule, times) != null) return;
        long cost = 0;
        // 헤파이스토스의 가호 — 랭크에 따른 확률로 그 번의 행동력이 안 든다
        int blessed = VigourFreeTimes(times);
        SpendVigour(ProduceVigourOf(rule, times) * (times - blessed) / times);
        if (blessed > 0) Say($"헤파이스토스의 가호 — 행동력을 {blessed}번 아꼈다.");
        foreach (int tool in rule.ConsumeList()) SpendItem(tool, times);
        // 연성한 생산 스킬은 재료를 아껴 준다 — 한 번마다 10%로 그 번의 재료가 안 든다(지은 값)
        int spared = RecipeSkill(rule) is { } craft && Refined(craft.SkillId) ? Enumerable.Range(0, times).Count(_ => _random.Next(100) < 10) : 0;
        if (spared > 0) Say($"연성한 솜씨로 재료를 {spared}번 아꼈다.");
        // 대학 스킬 「○○의 기술 1」 — 재료 10% 감소(열 번에 한 번치가 안 든다)
        if (RecipeSkill(rule) is { } learned && StudySpared(learned.SkillId, times) is > 0 and var studied) { spared = Math.Min(times - 1, spared + studied); Say($"대학에서 익힌 기술로 재료를 {studied}번치 아꼈다."); }
        foreach (var (good, count) in rule.InputList())
        {
            if (IsPartId(good)) { for (int k = 0; k < count * (times - spared); k++) PartStock.RemoveAt(PartStock.FindIndex(p => p.Id == good)); continue; }
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
        int great = 0, refinedMore = 0;
        if (rule.Facility == "")
        {
            double chance = Math.Min(0.30, 0.10 + (RecipeSkill(rule) is { } skilled ? Math.Max(0, Rank(skilled.SkillId) - skilled.Rank) * 0.02 : 0));
            for (int k = 0; k < done; k++) if (_random.NextDouble() < chance) great++;
            if (GreatForTest) (great, GreatForTest) = (done, false);
            // 연성한 생산 스킬은 대성공 때 수량이 더 나온다(사용자가 준 글: 「연성하실 경우 대성공시 수량이 추가되며 재료가 일부 환급됩니다」) — 얼마인지는 자료가 없다.
            // 지은 값: 대성공한 번마다 한 번 몫을 더(곱절 → 세 곱). 대성공품이 따로 나오는 레시피에는 안 붙는다
            if (great > 0 && rule.GreatOutput == 0 && RecipeSkill(rule) is { } refined && Refined(refined.SkillId)) refinedMore = great;
        }
        if (rule.OutputItem > 0 && Data.ShipParts.Find(p => p.Id == rule.OutputItem) is { } carved)
        {
            // 생산물이 선박 부품(선수상 — 조각상 레시피)이면 부품 창고로 간다. 대성공의 곱절은 없다(부품은 하나씩)
            for (int k = 0; k < rule.OutputCount * done; k++) GivePart(carved);
        }
        else if (TemperStat(rule) is >= 0 and var stat)
        {
            // 장비 연성(「○○ 연성법」 — 재료의 장비가 그대로 생산물이다. 클라이언트 설명: 「공격력을 높이는 기법. 실패하면 장비물품은 소실된다」):
            // 성공하면 그 장비가 돌아오고 대장장이 단련과 같은 칸(Forged)이 하나 오른다, 실패하면 장비가 사라진다. 실패 확률 20% · 한 번에 +1 · 단련 한도까지는 지은 값(자료에 수가 없다)
            int kept = 0, raised = 0;
            for (int k = 0; k < done; k++)
            {
                if (TemperFailForTest || _random.Next(100) < 20) continue;
                kept++;
                if (GearOf(rule.OutputItem) is not { } gear || ForgedOf(gear.Id, stat) >= ForgeLimit(gear.Stats.ElementAtOrDefault(stat))) continue;
                if (!Forged.TryGetValue(gear.Id, out var added)) Forged[gear.Id] = added = [0, 0];
                added[stat]++;
                raised++;
            }
            if (kept > 0) Items[rule.OutputItem] = Items.GetValueOrDefault(rule.OutputItem) + kept;
            if (done > kept) Say($"연성에 실패해 {ItemName(rule.OutputItem)} {done - kept}개가 소실되었다.");
            if (raised > 0) Say($"{ItemName(rule.OutputItem)}의 {(stat == 0 ? "공격력" : "방어력")}이 {raised} 올랐다(+{ForgedOf(rule.OutputItem, stat)}).");
            else if (kept > 0) Say($"{ItemName(rule.OutputItem)} — 더는 오르지 않는다(단련 한도).");
            (done, great) = (kept, 0);
        }
        else if (rule.OutputItem > 0)
        {
            if (done > 0) Items[rule.OutputItem] = Items.GetValueOrDefault(rule.OutputItem) + rule.OutputCount * (done + great + refinedMore);
        }
        else if (done > 0)
        {
            if (!Cargo.TryGetValue(rule.Output, out var made)) Cargo[rule.Output] = made = new CargoItem();
            // 대성공품이 따로 있는 레시피(gvdb 의 둘째 생산물 — 석류석 → 루비)는 대성공한 번에 곱절 대신 그것이 나온다(짐작). 선창에 들어가는 만큼만
            bool other = rule.GreatOutput > 0 && great > 0 && Good(rule.GreatOutput) != null;
            made.Count += other ? rule.OutputCount * done : Math.Min(rule.OutputCount * (done + great + refinedMore), rule.OutputCount * done + Math.Max(0, HoldFree));      // 대성공의 덤은 선창에 들어가는 만큼만
            made.Cost += cost;
            if (other && Math.Min(rule.GreatCount * great, Math.Max(0, HoldFree)) is > 0 and var bonus)
            {
                GiveGood(Good(rule.GreatOutput)!, bonus);
                Say($"대성공 — {Good(rule.GreatOutput)!.Name} {bonus}개가 나왔다.");
            }
        }
        if (RecipeSkill(rule) is { } used) Train(used.SkillId, ProduceExp(rule) * (times - great) + ProduceExp(rule, true) * great);      // 실패한 번도 성공만큼 오른다(실패 때의 양은 모른다)
        Studied("Produce", times, RecipeSkill(rule)?.Rank ?? 0, RecipeSkill(rule)?.SkillId ?? 0);
        if (great > 0) Studied("Great", great, RecipeSkill(rule)?.Rank ?? 0, RecipeSkill(rule)?.SkillId ?? 0);
        GainMastery();
        if (great > 0) { Say(Text(16503, "생산 대성공!!") + (times > 1 ? $" ({great}번)" : "")); Cues.Enqueue("Done"); }
        // 관리기술의 숙련도 — 「바다 위에서 · 스킬 달린 선박으로 움직이면서 · 대성공 생산 시」 오른다(사용자가 준 인벤 글 「실전 관기 랭작 빨리하기」, 2026-10-09).
        // 한 번에 얼마인지는 글에 없다 — 대성공 한 번에 10(지은 값)
        if (great > 0 && Mode == Mode.Sea && Knots > 0.5 && (Work.Skills.Count > 0 || Work.Dedicated > 0) && Data.Skills.Find(s => s.Name == "관리기술") is { } keeping && Rank(keeping.Id) > 0)
            Train(keeping.Id, 10 * great);
        string madeName = Data.ShipParts.Find(p => p.Id == rule.OutputItem)?.Name ?? (rule.OutputItem > 0 ? ItemName(rule.OutputItem) : Good(rule.Output)?.Name ?? "물건");
        if (done > 0) Say($"{madeName} {rule.OutputCount * (done + (rule.GreatOutput > 0 && rule.OutputItem == 0 ? 0 : IsPartId(rule.OutputItem) ? 0 : great + refinedMore))}개를 만들었다.");
    }
}
