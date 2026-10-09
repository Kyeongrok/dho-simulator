using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 위키(게임 안의 찾아보기 — 처음에는 「도시 정보 검색」이었다) — 낱말로 도시 · 교역품 · 아이템 · 배 · 스킬 · 레시피 · 발견물 · 의뢰를 찾고, 고른 것의 쪽을 보인다:
/// 도시는 그곳이 파는 것(교역품 · 아이템 · 배), 교역품 · 아이템 · 배는 그것을 파는 도시들, 레시피는 재료 · 생산물 · 든 책, 발견물은 그것을 찾는 의뢰 …
/// 쪽끼리 서로 이어진다(줄을 누르면 넘어간다). 설명 글은 실행 때 클라이언트에서 읽은 것을 그대로 보인다(코드에는 없다).
/// 자료는 게임이 이미 쓰는 것 그대로다(gvdb 의 이용자 보고 — 받은 도시만 있다. 없는 도시는 지은 목록이라고 적는다). 원본에는 없는 창이다.
/// </summary>
internal sealed partial class Voyage
{
    /// <summary>쪽의 한 줄 — Kind 가 있으면 눌러서 그 쪽으로 간다. Head 는 묶음의 머리 줄.</summary>
    internal readonly record struct WikiLine(string Text, string Kind = "", int Id = 0, bool Head = false);

    /// <summary>낱말이 이름에 든 것들 — 도시 · 교역품 · 아이템 · 배 차례. 낱말이 비면 도시 전부.</summary>
    /// <summary>위키의 갈래들 — 탭의 차례.</summary>
    public static readonly string[] WikiKinds = ["도시", "교역품", "아이템", "배", "스킬", "레시피", "발견물", "의뢰", "직업", "부품"];

    /// <summary>낱말이 이름에 든 것들. <paramref name="kind"/> 를 주면 그 갈래만(낱말이 비면 그 갈래 전부), 안 주고 낱말도 비면 도시 전부.</summary>
    public List<(string Kind, int Id, string Name)> WikiFind(string word, string building, string kind)
    {
        var all = WikiFind(word, kind == "" || kind == "도시" ? building : "");
        if (kind == "도시" || (kind == "" && (word == "" || building != ""))) return all.Where(f => f.Kind == "도시").ToList();
        bool Hit(string name) => word == "" || name.Contains(word, StringComparison.OrdinalIgnoreCase);
        if (kind == "") word = word == "" ? "\0" : word;      // 「전체」에서 낱말이 없으면 도시만(위에서 돌려보냈다)
        if (kind is "" or "교역품") { }                       // 교역품 · 아이템 · 배는 아래의 옛 함수가 낱말로 찾는다 — 낱말이 비면 여기서 전부를 댄다
        if (kind == "교역품" && word == "") return Data.Goods.OrderBy(g => g.Name).Select(g => ("교역품", g.Id, g.Name)).ToList();
        if (kind == "아이템" && word == "") return Data.ItemTowns.Keys.Select(id => ("아이템", id, ItemName(id))).OrderBy(i => i.Item3).ToList();
        if (kind == "배" && word == "") return Data.Ships.Where(s => s.Kind == 0).GroupBy(s => s.Name).Select(g => g.First()).OrderBy(s => s.Name).Select(s => ("배", s.Id, s.Name)).ToList();
        if (kind is "" or "스킬")
            all.AddRange(Data.Skills.Where(s => s.Name != "" && !s.Name.StartsWith('※') && Hit(s.Name)).GroupBy(s => s.Name).Select(g => g.First()).OrderBy(s => s.Name).Select(s => ("스킬", s.Id, s.Name))
                .Concat(Data.OptionSkills.Where(o => Hit(o.Name) && !Data.Skills.Exists(s => s.Name == o.Name)).OrderBy(o => o.Name).Select(o => ("스킬", o.SkillId, o.Name))));
        if (kind is "" or "레시피") all.AddRange(Data.RecipeRules.Where(r => r.Name != "" && Hit(r.Name)).OrderBy(r => r.Name).Select(r => ("레시피", r.RecipeId, r.Name)));
        if (kind is "" or "발견물") all.AddRange(Data.Discoveries.Where(d => d.Name != "" && Hit(d.Name)).OrderBy(d => d.Name).Select(d => ("발견물", d.Id, d.Name)));
        if (kind is "" or "의뢰") all.AddRange(Data.Quests.Where(q => q.Title != "" && Hit(q.Title)).OrderBy(q => q.Title).Select(q => ("의뢰", q.Id, q.Title)));
        if (kind is "" or "직업") all.AddRange(Data.JobFacts.Where(j => j.Name != "" && Hit(j.Name)).OrderBy(j => j.Name).Select(j => ("직업", j.No, j.Name)));
        if (kind is "" or "부품") all.AddRange(Data.ShipParts.Where(p => p.Name != "" && Hit(p.Name)).GroupBy(p => p.Id).Select(g => g.First()).OrderBy(p => p.Slot).ThenBy(p => p.Name).Select(p => ("부품", p.Id, p.Name)));
        return kind == "" ? all : all.Where(f => f.Kind == kind).ToList();
    }

    /// <summary>긴 설명 글을 쪽의 줄 너비(22자)로 끊어 넣는다.</summary>
    private static void WikiProse(List<WikiLine> lines, string text)
    {
        foreach (string para in text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
{
            // 빈칸에서 끊는다 — 22자를 넘는 낱말만 가운데서 끊긴다
            string rest = para;
            while (rest.Length > 22)
            {
                int cut = rest.LastIndexOf(' ', 22);
                if (cut < 8) cut = 22;
                lines.Add(new(rest[..cut].TrimEnd()));
                rest = rest[cut..].TrimStart();
            }
            if (rest.Length > 0) lines.Add(new(rest));
        }
    }

    public List<(string Kind, int Id, string Name)> WikiFind(string word, string building = "")
    {
        bool Hit(string name) => name.Contains(word, StringComparison.OrdinalIgnoreCase);
        // 건물로 거르면 그 건물이 있는 도시만 나온다
        if (building != "")
            return Data.Cities.Where(c => !c.Name.StartsWith('※') && (word == "" || Hit(c.Name)) && WikiBuildingsOf(c).Contains(building)).OrderBy(c => c.Name).Select(c => ("도시", c.Id, c.Name)).ToList();
        var found = Data.Cities.Where(c => !c.Name.StartsWith('※') && (word == "" || Hit(c.Name))).OrderBy(c => c.Name).Select(c => ("도시", c.Id, c.Name)).ToList();
        if (word == "") return found;
        found.AddRange(Data.Goods.Where(g => Hit(g.Name)).OrderBy(g => g.Name).Select(g => ("교역품", g.Id, g.Name)));
        found.AddRange(Data.ItemTowns.Keys.Select(id => (Id: id, Name: ItemName(id))).Where(i => Hit(i.Name)).OrderBy(i => i.Name).Select(i => ("아이템", i.Id, i.Name)));
        found.AddRange(Data.Ships.Where(s => s.Kind == 0 && Hit(s.Name)).GroupBy(s => s.Name).Select(g => g.First()).OrderBy(s => s.Name).Select(s => ("배", s.Id, s.Name)));
        return found;
    }

    /// <summary>
    /// 그 도시의 건물 갈래들 — 장면 표의 건물 이름에서 앞의 도시 이름을 뗀 것(「리스본 주점」 → 「주점」).
    /// 조선소는 건물이 아니라 사람이라 장면 표에 없다 — 조선소 판매 자료(gvdb)가 있는 도시에 「조선소」를 더한다(자료를 받은 도시만).
    /// </summary>
    public List<string> WikiBuildingsOf(CityData city)
    {
        var kinds = city.Buildings.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(b => b.StartsWith(city.Name) ? b[city.Name.Length..].Trim() : b).Where(b => b != "" && b != "※").Distinct().ToList();
        if (Data.Shipyards.ContainsKey(city.Id)) kinds.Add("조선소");
        return kinds;
    }

    private List<(string Name, int Count)>? _wikiBuildings;
    /// <summary>거를 수 있는 건물 갈래 — 세 도시 넘게 있는 것만(한 곳에만 있는 전시실 따위는 뺀다), 많은 차례로.</summary>
    public List<(string Name, int Count)> WikiBuildings() => _wikiBuildings ??=
        Data.Cities.Where(c => !c.Name.StartsWith('※')).SelectMany(WikiBuildingsOf).GroupBy(b => b).Where(g => g.Count() >= 3).OrderByDescending(g => g.Count()).Select(g => (g.Key, g.Count())).ToList();

    /// <summary>
    /// 그 배에 그 선박 스킬을 붙이는 재료 조합을 한 줄로 — 배 상세(ssjoy · dhoguide)에 적힌 것, 없으면 gvdb 이용자 보고의 가장 짧은 것,
    /// 그것도 없으면(또는 배 이름이 비면) 기본 조합(ship-works.json 의 두 재료). 자료에 없으면 빈 글.
    /// </summary>
    public string WikiCombo(string shipName, OptionSkill skill)
    {
        if (shipName != "")
        {
            if (Data.ShipDetail(shipName)?.Skills.Find(s => s.Name == skill.Name) is { Parts.Count: > 0 } own) return string.Join(" + ", own.Parts);
            if (Data.ShipCombos.Where(c => c.Ship == shipName && c.Skill == skill.Name).OrderBy(c => c.Parts.Count).FirstOrDefault() is { } told) return string.Join(" + ", told.Parts);
        }
        var basic = new[] { skill.PartA, skill.PartB }.Where(id => id > 0).Select(id => Data.ShipWorks.Parts.Find(p => p.Id == id)?.Name ?? "").Where(name => name != "").ToList();
        return basic.Count == 0 ? "" : string.Join(" + ", basic) + (shipName == "" ? "" : " (기본 조합)");
    }

    /// <summary>고른 것의 쪽.</summary>
    public List<WikiLine> WikiPage(string kind, int id)
    {
        var lines = new List<WikiLine>();
        void Head(string text) => lines.Add(new(text, Head: true));
        switch (kind)
        {
            case "도시" when _cities.TryGetValue(id, out var city):
            {
                string nation = Data.Nations.Find(n => n.Id == city.Nation)?.Name ?? "";
                lines.Add(new(string.Join(" · ", new[] { CityKindName(city), nation, CultureName(city) }.Where(t => t != ""))));
                var fact = Data.MarketFacts.Find(f => f.CityId == id);
                var market = Data.Markets.Find(m => m.CityId == id);
                var goods = (market?.GoodIds() ?? []).Select(Good).OfType<GoodData>().ToList();
                Head($"교역소가 파는 것 {goods.Count}가지" + (market?.RealGoods != null ? "" : " — 자료가 없어 지은 목록"));
                foreach (var good in goods)
                {
                    int price = fact?.Goods.Find(g => g[0] == good.Id) is { Length: > 1 } told ? told[1] : 0;
                    long invest = InvestNeed(city, good.Id);
                    lines.Add(new(good.Name + (price > 0 ? $"  {price:N0}" : "") + (invest > 0 ? "  (투자)" : ""), "교역품", good.Id));
                }
                var items = Data.ItemTowns.Where(t => t.Value.Contains(id)).Select(t => t.Key).OrderBy(ItemName).ToList();
                if (items.Count > 0) Head($"파는 아이템 {items.Count}가지");
                foreach (int item in items)
                {
                    int price = fact?.Items.Find(i => i[0] == item) is { Length: > 1 } told ? told[1] : 0;
                    lines.Add(new(ItemName(item) + (price > 0 ? $"  {price:N0}" : ""), "아이템", item));
                }
                if (Data.Shipyards.TryGetValue(id, out var yard))
                {
                    Head($"조선소가 파는 배 {yard.Count}척");
                    foreach (var sold in yard.OrderBy(s => s.Value))
                        lines.Add(new(sold.Key + (sold.Value > 0 ? $"  {sold.Value:N0}" : ""), "배", Data.Ships.Find(s => s.Kind == 0 && s.Name == sold.Key)?.Id ?? 0));
                }
                var pays = Data.BuyPrices.Where(p => p.Key.City == id).OrderByDescending(p => p.Value).Take(12).ToList();
                if (pays.Count > 0) Head("비싸게 사 주는 교역품(이용자 보고)");
                foreach (var pay in pays) lines.Add(new($"{Good(pay.Key.Good)?.Name}  {pay.Value:N0}", "교역품", pay.Key.Good));
                if (Data.PartShops.TryGetValue(id, out var partShop) && partShop.Count > 0)
                {
                    Head($"파는 선박부품 {partShop.Count}가지");
                    foreach (var sold in partShop.OrderBy(s => s.Value))
                        if (Data.ShipParts.Find(p => p.Id == sold.Key) is { } soldPart) lines.Add(new(soldPart.Name + (sold.Value > 0 ? $"  {sold.Value:N0}" : ""), "부품", soldPart.Id));
                }
                var kinds = WikiBuildingsOf(city);
                if (kinds.Count > 0) Head($"건물 {kinds.Count}가지");
                foreach (string built in kinds) lines.Add(new(built));
                break;
            }
            case "교역품" when Good(id) is { } good:
            {
                lines.Add(new(string.Join(" · ", new[] { Data.GoodKinds.Find(k => k.Id == good.Kind)?.Name ?? "", SpecialtyOf(good) is { Length: > 0 } culture ? culture + "의 명산품" : "" }.Where(t => t != ""))));
                var sellers = Data.Markets.Where(m => m.GoodIds().Contains(id)).Select(m => m.CityId).Where(_cities.ContainsKey).OrderBy(CityName).ToList();
                Head($"파는 도시 {sellers.Count}곳");
                foreach (int town in sellers)
                {
                    int price = Data.MarketFacts.Find(f => f.CityId == town)?.Goods.Find(g => g[0] == id) is { Length: > 1 } told ? told[1] : 0;
                    lines.Add(new(CityName(town) + (price > 0 ? $"  {price:N0}" : "") + (InvestNeed(_cities[town], id) > 0 ? "  (투자)" : ""), "도시", town));
                }
                var pays = Data.BuyPrices.Where(p => p.Key.Good == id && _cities.ContainsKey(p.Key.City)).OrderByDescending(p => p.Value).Take(16).ToList();
                if (pays.Count > 0) Head("비싸게 사 주는 도시(이용자 보고)");
                foreach (var pay in pays) lines.Add(new($"{CityName(pay.Key.City)}  {pay.Value:N0}", "도시", pay.Key.City));
                var makes = Data.RecipeRules.Where(r => r.Output == id && r.OutputItem == 0 && r.Name != "").Take(12).ToList();
                if (makes.Count > 0) Head("이것을 만드는 레시피");
                foreach (var rule in makes) lines.Add(new(rule.Name, "레시피", rule.RecipeId));
                var uses = Data.RecipeRules.Where(r => r.Name != "" && r.InputList().Any(i => i.Good == id)).ToList();
                if (uses.Count > 0) Head($"재료로 쓰는 레시피 {uses.Count}건" + (uses.Count > 20 ? " (20건만)" : ""));
                foreach (var rule in uses.Take(20)) lines.Add(new(rule.Name, "레시피", rule.RecipeId));
                if (good.Description != "") { Head("설명"); WikiProse(lines, good.Description); }
                break;
            }
            case "아이템":
            {
                var towns = (Data.ItemTowns.GetValueOrDefault(id) ?? []).Where(_cities.ContainsKey).OrderBy(CityName).ToList();
                Head($"파는 도시 {towns.Count}곳");
                foreach (int town in towns)
                {
                    int price = Data.MarketFacts.Find(f => f.CityId == town)?.Items.Find(i => i[0] == id) is { Length: > 1 } told ? told[1] : 0;
                    lines.Add(new(CityName(town) + (price > 0 ? $"  {price:N0}" : ""), "도시", town));
                }
                if (Data.RecipeBooks.Find(b => b.ItemId == id) is { } book)
                {
                    Head($"이 책에 든 레시피 {book.Recipes.Count}건");
                    foreach (int recipe in book.Recipes) if (RuleOf(recipe) is { Name.Length: > 0 } rule) lines.Add(new(rule.Name, "레시피", recipe));
                }
                var made = Data.RecipeRules.Where(r => r.OutputItem == id && r.Name != "").Take(12).ToList();
                if (made.Count > 0) Head("이것을 만드는 레시피");
                foreach (var rule in made) lines.Add(new(rule.Name, "레시피", rule.RecipeId));
                break;
            }
            case "스킬":
            {
                var skill = Data.Skills.Find(s => s.Id == id);
                string name = skill?.Name ?? OptionName(id);
                string about = skill?.Description is { Length: > 0 } told ? told : Data.SkillNotes.GetValueOrDefault(id) ?? "";
                if (about != "") WikiProse(lines, about);
                if (Data.OptionSkills.Find(o => o.SkillId == id || o.Name == name) is { } option)
                {
                    Head("선박 스킬(옵션 스킬)");
                    if (WikiCombo("", option) is { Length: > 0 } basic) WikiProse(lines, "기본 조합: " + basic);
                    WikiProse(lines, option.Effect == "" ? "이 게임에서는 아직 효과가 없다" : "이 게임에서: " + OptionNote(option));
                    if (OptionNeedLine(option) is { Length: > 0 } needs) WikiProse(lines, "유효조건: " + needs);
                    var fits = Data.ShipDetails.Where(d => d.Skills.Exists(s => s.Name == option.Name)).Select(d => d.Name)
                        .Concat(Data.ShipCombos.Where(c => c.Skill == option.Name).Select(c => c.Ship)).Distinct().OrderBy(s => s).ToList();
                    if (fits.Count > 0) Head($"붙일 수 있는 배 {fits.Count}척" + (fits.Count > 24 ? " (24척만)" : ""));
                    foreach (string ship in fits.Take(24)) lines.Add(new(ship, "배", Data.Ships.Find(s => s.Kind == 0 && s.Name == ship)?.Id ?? 0));
                }
                var jobs = Data.JobFacts.Where(j => j.Skills.Contains(name) || j.Expert == name).OrderBy(j => j.Name).ToList();
                if (jobs.Count > 0) Head($"우대하는 직업 {jobs.Count}가지" + (jobs.Count > 24 ? " (24가지만)" : ""));
                foreach (var liker in jobs.Take(24)) lines.Add(new(liker.Name + (liker.Expert == name ? "  (전문)" : ""), "직업", liker.No));
                var needing = Data.RecipeRules.Where(r => r.Name != "" && r.Skill.StartsWith(name + " ")).OrderBy(r => r.Skill.Length).ThenBy(r => r.Skill).ToList();
                if (needing.Count > 0) Head($"이 스킬로 만드는 레시피 {needing.Count}건" + (needing.Count > 30 ? " (30건만)" : ""));
                foreach (var rule in needing.Take(30)) lines.Add(new($"{rule.Name}  {rule.Skill[(name.Length + 1)..]}", "레시피", rule.RecipeId));
                break;
            }
            case "직업" when Data.JobFacts.Find(j => j.No == id) is { } job:
            {
                if (job.Cost > 0) lines.Add(new($"전직 비용 {job.Cost:N0}"));
                if (job.Expert != "") { Head("전문 스킬"); lines.Add(new(job.Expert, "스킬", Data.Skills.Find(s => s.Name == job.Expert)?.Id ?? 0)); }
                Head($"우대 스킬 {job.Skills.Count}가지");
                foreach (string liked in job.Skills) lines.Add(new(liked, "스킬", Data.Skills.Find(s => s.Name == liked)?.Id ?? 0));
                break;
            }
            case "부품" when Data.ShipParts.Find(p => p.Id == id) is { } part:
            {
                // 선박부품 — 갈래(보조돛 · 장갑 · 선수상 · 문장 · 대포 · 특수장비)와 수치, 파는 도시(gvdb 의 부품 가게 자료)
                WikiProse(lines, $"[{SlotName[Math.Clamp(part.Slot, 0, SlotName.Length - 1)]}] {PartNote(part)}");
                var shops = Data.PartShops.Where(s => s.Value.ContainsKey(id) && _cities.ContainsKey(s.Key)).OrderBy(s => CityName(s.Key)).ToList();
                Head($"파는 도시 {shops.Count}곳");
                foreach (var shop in shops) lines.Add(new(CityName(shop.Key) + (shop.Value[id] > 0 ? $"  {shop.Value[id]:N0}" : ""), "도시", shop.Key));
                if (part.Description is { Length: > 0 } partNote) { Head("설명"); WikiProse(lines, partNote); }
                break;
            }
            case "레시피" when RuleOf(id) is { } rule:
            {
                lines.Add(new((rule.Skill == "" ? "필요 스킬 없음" : "필요 스킬: " + rule.Skill + (rule.RankGuessed ? " (랭크 모름)" : "")) + (rule.Facility == "" ? "" : rule.Facility == "Furnace" ? " · 화로" : " · 실험대")));
                if (Data.Skills.Find(s => rule.Skill.StartsWith(s.Name + " ")) is { } needs) lines.Add(new(needs.Name, "스킬", needs.Id));
                Head("재료");
                foreach (var (good, count) in rule.InputList()) lines.Add(new($"{Good(good)?.Name}  ×{count}", "교역품", good));
                foreach (int tool in rule.ToolList()) lines.Add(new($"{ItemName(tool)}  (도구)", "아이템", tool));
                foreach (int worn in rule.ConsumeList()) lines.Add(new($"{ItemName(worn)}  (닳는다)", "아이템", worn));
                Head("만들어지는 것");
                if (rule.OutputItem > 0) lines.Add(new($"{ItemName(rule.OutputItem)}  ×{rule.OutputCount}", "아이템", rule.OutputItem));
                else if (Good(rule.Output) is { } output) lines.Add(new($"{output.Name}  ×{rule.OutputCount}", "교역품", output.Id));
                if (rule.GreatOutput > 0 && Good(rule.GreatOutput) is { } great) lines.Add(new($"{great.Name}  ×{rule.GreatCount} (대성공)", "교역품", great.Id));
                var books = Data.RecipeBooks.Where(b => b.Recipes.Contains(id)).ToList();
                if (books.Count > 0) Head("이 레시피가 든 책");
                foreach (var book in books) lines.Add(new(book.Name + (book.Price > 0 ? $"  {book.Price:N0}" : ""), "아이템", book.ItemId));
                if (Data.Recipes.Find(r => r.Id == id)?.Description is { Length: > 0 } note) { Head("설명"); WikiProse(lines, note); }
                break;
            }
            case "발견물" when Data.Discoveries.Find(d => d.Id == id) is { } find:
            {
                lines.Add(new(string.Join(" · ", new[] { Data.DiscoveryKinds.Find(k => k.Id == find.Kind)?.Name ?? "", new string('★', Math.Clamp(find.Stars, 0, 10)), find.Exp > 0 ? $"경험 {find.Exp}" : "", find.Fame > 0 ? $"명성 {find.Fame}" : "" }.Where(t => t != ""))));
                var quests = Data.Quests.Where(q => q.DiscoveryId == id).ToList();
                if (quests.Count > 0) Head("이것을 찾는 의뢰");
                foreach (var quest in quests) lines.Add(new(quest.Title, "의뢰", quest.Id));
                if (find.Description != "") { Head("설명"); WikiProse(lines, find.Description); }
                break;
            }
            case "의뢰" when Data.Quests.Find(q => q.Id == id) is { } quest:
            {
                if (quest.Client != "") lines.Add(new("의뢰인: " + quest.Client));
                Head("이어지는 곳");
                if (_cities.ContainsKey(quest.CityId)) lines.Add(new(CityName(quest.CityId) + "  (받는 도시)", "도시", quest.CityId));
                if (quest.SearchCity > 0 && _cities.ContainsKey(quest.SearchCity)) lines.Add(new(CityName(quest.SearchCity) + "  (찾는 도시)", "도시", quest.SearchCity));
                if (Data.Landings.Find(l => l.Id == quest.LandingId) is { } landing) lines.Add(new(landing.Name + "  (상륙지)"));
                if (Data.Discoveries.Find(d => d.Id == quest.DiscoveryId) is { } target) lines.Add(new(target.Name + "  (발견물)", "발견물", target.Id));
                if (quest.Request != "") { Head("의뢰 내용"); WikiProse(lines, quest.Request); }
                if (quest.Hint != "") { Head("실마리"); WikiProse(lines, quest.Hint); }
                break;
            }
            case "배" when Data.Ships.Find(s => s.Id == id) is { } ship:
            {
                var stats = ShipStats.Of(ship, Settings.Ships);
                lines.Add(new($"선원 {stats.MinCrew}/{stats.MaxCrew} · 대포 {stats.Guns} · 창고 {stats.Hold} · 속도 {stats.Knots:0.0}노트"));
                var yards = Data.Shipyards.Where(y => y.Value.ContainsKey(ship.Name) && _cities.ContainsKey(y.Key)).OrderBy(y => CityName(y.Key)).ToList();
                Head($"파는 조선소 {yards.Count}곳");
                foreach (var yard in yards) lines.Add(new(CityName(yard.Key) + (yard.Value[ship.Name] > 0 ? $"  {yard.Value[ship.Name]:N0}" : ""), "도시", yard.Key));
                // 조선소에서 안 파는 배의 얻는 길 — 이 배로 바뀌는 선박 교환권(아이템 이름 · 설명에서 읽은 것)
                var tickets = ShipTickets().Where(ticket => TicketShip(ticket)?.Name == ship.Name).ToList();
                if (tickets.Count > 0) Head("이 배를 주는 교환권");
                foreach (var ticket in tickets) lines.Add(new(ticket.Name, "아이템", ticket.Id));
                var skills = AttachableSkills(ship.Name);
                if (skills.Count > 0) Head($"붙일 수 있는 스킬 {skills.Count}가지");
                foreach (var skill in skills)
                {
                    // 스킬 이름(누르면 스킬 쪽) 아래에 이 배에서의 조합 — 자료에 적힌 그대로
                    lines.Add(new(skill.Name, "스킬", skill.SkillId));
                    if (WikiCombo(ship.Name, skill) is { Length: > 0 } combo) WikiProse(lines, "  " + combo);
                }
                break;
            }
        }
        return lines;
    }
}
