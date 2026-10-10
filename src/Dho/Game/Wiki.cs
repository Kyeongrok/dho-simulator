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

    // 별자리 번호(1 ~ 45) → 이름. 원본 표에는 이름이 없어 발견물(천문)의 이름을 열쇠로 적는다 — 이야기 글의 첫머리와 별 발견물의 설명으로 하나씩 맞춘 것(w-890).
    // 발견물 표의 띄어쓰기 그대로다(「작은곰 자리」 따위). 발견물에 그 이름이 없으면 그냥 글로만 보인다
    private static readonly string[] ConstellationNames =
    [
        "작은곰 자리", "안드로메다 자리", "이리자리", "큰곰자리", "카시오페이아자리", "황새치자리", "왕관자리", "마차부 자리", "공작자리", "고래 자리",
        "케페우스 자리", "켄타우로스자리", "두루미자리", "고물 자리", "돛자리", "남십자자리", "나침반 자리", "용골자리", "용자리", "육분의자리",
        "사자자리", "게자리", "작은사자자리", "살쾡이자리", "기린자리", "쌍둥이자리", "황소자리", "작은개자리", "오리온자리", "페르세우스 자리",
        "백조자리", "작은여우자리", "거문고자리", "화살자리", "독수리자리", "처녀자리", "천칭자리", "목동 자리", "머리털 자리", "사냥개자리",
        "삼각형자리", "산양자리", "물고기자리", "에리다누스 자리", "페가수스자리",
    ];
    private static string ConstellationName(int group) => group >= 1 && group <= ConstellationNames.Length ? ConstellationNames[group - 1] : $"별자리 {group}";
    /// <summary>낱말이 이름에 든 것들 — 도시 · 교역품 · 아이템 · 배 차례. 낱말이 비면 도시 전부.</summary>
    /// <summary>위키의 갈래들 — 탭의 차례.</summary>
    public static readonly string[] WikiKinds = ["도시", "교역품", "아이템", "배", "스킬", "레시피", "발견물", "의뢰", "직업", "부품", "상륙지", "재질", "해역", "부관", "역사", "연구"];

    /// <summary>낱말이 이름에 든 것들. <paramref name="kind"/> 를 주면 그 갈래만(낱말이 비면 그 갈래 전부), 안 주고 낱말도 비면 도시 전부.</summary>
    public List<(string Kind, int Id, string Name)> WikiFind(string word, string building, string kind)
    {
        var all = WikiFind(word, kind == "" || kind == "도시" ? building : "");
        if (kind == "도시" || (kind == "" && (word == "" || building != ""))) return all.Where(f => f.Kind == "도시").ToList();
        bool Hit(string name) => word == "" || name.Contains(word, StringComparison.OrdinalIgnoreCase);
        if (kind == "") word = word == "" ? "\0" : word;      // 「전체」에서 낱말이 없으면 도시만(위에서 돌려보냈다)
        if (kind is "" or "교역품") { }                       // 교역품 · 아이템 · 배는 아래의 옛 함수가 낱말로 찾는다 — 낱말이 비면 여기서 전부를 댄다
        if (kind == "교역품" && word == "") return Data.Goods.OrderBy(g => g.Name).Select(g => ("교역품", g.Id, g.Name)).ToList();
        if (kind == "아이템" && word == "") return WikiItems().Select(i => ("아이템", i.Id, i.Name)).ToList();
        if (kind == "배" && word == "") return Data.Ships.Where(s => s.Kind == 0).GroupBy(s => s.Name).Select(g => g.First()).OrderBy(s => s.Name).Select(s => ("배", s.Id, s.Name)).ToList();
        if (kind is "" or "스킬")
            all.AddRange(Data.Skills.Where(s => s.Name != "" && !s.Name.StartsWith('※') && Hit(s.Name)).GroupBy(s => s.Name).Select(g => g.First()).OrderBy(s => s.Name).Select(s => ("스킬", s.Id, s.Name))
                .Concat(Data.OptionSkills.Where(o => Hit(o.Name) && !Data.Skills.Exists(s => s.Name == o.Name)).OrderBy(o => o.Name).Select(o => ("스킬", o.SkillId, o.Name))));
        if (kind is "" or "레시피") all.AddRange(Data.RecipeRules.Where(r => r.Name != "" && Hit(r.Name)).OrderBy(r => r.Name).Select(r => ("레시피", r.RecipeId, r.Name)));
        if (kind is "" or "발견물") all.AddRange(Data.Discoveries.Where(d => d.Name != "" && Hit(d.Name)).OrderBy(d => d.Name).Select(d => ("발견물", d.Id, d.Name)));
        if (kind is "" or "의뢰") all.AddRange(Data.Quests.Where(q => q.Title != "" && Hit(q.Title)).OrderBy(q => q.Title).Select(q => ("의뢰", q.Id, q.Title)));
        if (kind is "" or "부관") all.AddRange(Data.AideFacts.Where(a => a.Name != "" && (Hit(a.Name) || Hit(a.Job))).OrderBy(a => a.Name).Select(a => ("부관", a.Doc, a.Job is "" or "-" ? a.Name : $"{a.Name} — {a.Job}")));      // 이름 옆에 직업 — 직업 이름으로도 찾아진다
        if (kind is "" or "역사") all.AddRange(Data.HistoryEvents.Where(e => Hit(e.Value[0])).OrderBy(e => e.Key).Select(e => ("역사", e.Key + 3000, e.Value[0] + " (사건)")));      // 역사적 사건(표 85) — 3000 을 더했다(지은 번호)
        if (kind is "" or "역사") all.AddRange(Data.Themes.Where(e => Hit(e.Value[0])).OrderBy(e => e.Key).Select(e => ("역사", e.Key + 4000, e.Value[0] + " (테마)")));      // 테마(표 83) — 4000 을 더했다(지은 번호)
        if (kind is "" or "역사") all.AddRange(Data.StarTales.Keys.Select(k => k / 100).Distinct().OrderBy(g => g).Select(g => (Group: g, Name: ConstellationName(g))).Where(c => Hit(c.Name)).Select(c => ("역사", c.Group + 6000, c.Name + " (별자리)")));      // 별자리(표 142 · 144) — 6000 을 더했다(지은 번호)
        if (kind is "" or "역사") all.AddRange(Data.Greats.Where(e => Hit(e.Value[0])).OrderBy(e => e.Key).Select(e => ("역사", e.Key + 7000, e.Value[0] + " (위인)")));      // 위인(표 149) — 7000 을 더했다(지은 번호)
        if (kind is "" or "역사") all.AddRange(Data.DigTopics.Where(e => Hit(e.Value[0])).OrderBy(e => e.Key).Select(e => ("역사", e.Key + 8000, e.Value[0] + " (조사 주제)")));      // 조사 주제(표 134) — 8000 을 더했다(지은 번호)
        if (kind is "" or "역사") all.AddRange(Data.GreatPirates.Where(e => Hit(e.Value[0])).OrderBy(e => e.Key).Select(e => ("역사", e.Key + 5000, e.Value[0] + " (대해적)")));      // 대해적(표 94) — 5000 을 더했다(지은 번호)
        if (kind is "" or "역사") all.AddRange(Data.HistoryEnds.Where(e => Hit(e.Value[0])).OrderBy(e => e.Key).Select(e => ("역사", e.Key, e.Value[0])));      // 역사적 사건의 결말(표 87)
        if (kind is "" or "역사") all.AddRange(Data.Legends.Where(e => Hit(e.Value[0])).OrderBy(e => e.Key).Select(e => ("역사", e.Key + 1000, e.Value[0])));      // 전승(표 124) — 결말과 번호가 겹쳐 1000 을 더했다(지은 번호)
        if (kind is "" or "역사") all.AddRange(Data.Tales.Where(e => Hit(e.Value[0])).OrderBy(e => e.Key).Select(e => ("역사", e.Key + 2000, e.Value[0])));      // 단계가 있는 이야기(표 81) — 2000 을 더했다(지은 번호)
        if (kind is "" or "연구") all.AddRange(Data.Research.Where(r => r.Name != "" && (Hit(r.Name) || Hit(r.Major) || Hit(r.Skill))).OrderBy(r => r.Name).Select(r => ("연구", r.No, r.Name)));      // 대학 연구 — 전공 · 얻는 스킬 이름으로도 찾아진다
        if (kind is "" or "직업") all.AddRange(Data.JobFacts.Where(j => j.Name != "" && Hit(j.Name)).OrderBy(j => j.Name).Select(j => ("직업", j.No, j.Name)));
        if (kind is "" or "부품") all.AddRange(Data.ShipParts.Where(p => p.Name != "" && Hit(p.Name)).GroupBy(p => p.Id).Select(g => g.First()).OrderBy(p => p.Slot).ThenBy(p => p.Name).Select(p => ("부품", p.Id, p.Name)));
        if (kind is "" or "상륙지") all.AddRange(Data.Landings.Where(l => l.Name != "" && Hit(l.Name)).GroupBy(l => l.Id).Select(g => g.First()).OrderBy(l => l.Name).Select(l => ("상륙지", l.Id, l.Name)));
        if (kind is "" or "해역") all.AddRange(Data.Seas.Where(sea => sea.Name != "" && Hit(sea.Name)).GroupBy(sea => sea.Id).Select(g => g.First()).OrderBy(sea => sea.Name).Select(sea => ("해역", sea.Id, sea.Name)));
        if (kind is "" or "재질") all.AddRange(Data.ShipMaterials.Where(m => m.Name != "" && Hit(m.Name)).OrderBy(m => m.Name).Select(m => ("재질", m.Id, m.Name)));
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

    private List<(int Id, string Name)>? _wikiItems;
    /// <summary>위키의 아이템 목록 — 도구점에서 파는 것뿐 아니라 장비 · 증서 · 교환권 · 쓰는 아이템까지(이름이 있는 것, 이름 차례).</summary>
    private List<(int Id, string Name)> WikiItems() => _wikiItems ??= Data.ItemTowns.Keys
        .Concat(Data.Gear.Select(g => g.Id)).Concat(Data.Papers.Select(p => p.Id)).Concat(Data.Items.Select(i => i.Id)).Distinct()
        .Select(id => (Id: id, Name: ItemName(id))).Where(i => i.Name != "" && !i.Name.StartsWith('※') && !i.Name.StartsWith('?'))
        // 이름이 같은 것(빛깔만 다른 옷 따위)은 한 줄로 — 도구점에서 파는 것이 있으면 그것, 없으면 번호가 가장 작은 것을 남긴다
        .GroupBy(i => i.Name).Select(g => g.OrderBy(i => Data.ItemTowns.ContainsKey(i.Id) ? 0 : 1).ThenBy(i => i.Id).First())
        .OrderBy(i => i.Name, StringComparer.Ordinal).ToList();

    public List<(string Kind, int Id, string Name)> WikiFind(string word, string building = "")
    {
        bool Hit(string name) => name.Contains(word, StringComparison.OrdinalIgnoreCase);
        // 건물로 거르면 그 건물이 있는 도시만 나온다
        if (building != "")
            return Data.Cities.Where(c => !c.Name.StartsWith('※') && (word == "" || Hit(c.Name)) && WikiBuildingsOf(c).Contains(building)).OrderBy(c => c.Name).Select(c => ("도시", c.Id, c.Name)).ToList();
        var found = Data.Cities.Where(c => !c.Name.StartsWith('※') && (word == "" || Hit(c.Name))).OrderBy(c => c.Name).Select(c => ("도시", c.Id, c.Name)).ToList();
        if (word == "") return found;
        found.AddRange(Data.Goods.Where(g => Hit(g.Name)).OrderBy(g => g.Name).Select(g => ("교역품", g.Id, g.Name)));
        found.AddRange(WikiItems().Where(i => Hit(i.Name)).Select(i => ("아이템", i.Id, i.Name)));
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
                // 말 · 자리 — 그 도시에서 통하는 언어(누르면 스킬 쪽), 항구 앞바다의 해역과 좌표
                var tongues = LanguagesOf(city);
                if (tongues.Count > 0) { Head("언어"); foreach (var tongue in tongues) lines.Add(new(tongue.Name, "스킬", tongue.Id)); }
                if (city.SeaX != 0 || city.SeaY != 0) lines.Add(new($"{SeaNameAt(city.SeaX, city.SeaY)}  ({city.SeaX}, {city.SeaY})"));
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
                    // 원본의 도시 정보 창처럼 갈래(대포 · 보조돛 · 특수장비 · 장갑 · 장식)마다 머리를 따로 단다 — 갈래 이름은 게임의 부품 갈래 이름
                    var soldParts = partShop.Select(s => (Part: Data.ShipParts.Find(p => p.Id == s.Key), Price: s.Value)).Where(s => s.Part != null).ToList();
                    foreach (var slotGroup in soldParts.GroupBy(s => Math.Clamp(s.Part!.Slot, 0, SlotName.Length - 1)).OrderBy(g => g.Key))
                    {
                        Head($"파는 {SlotName[slotGroup.Key]} {slotGroup.Count()}가지 (선박부품)");      // 머리 단추가 갈래 이름으로 뜨게 갈래를 앞에
                        foreach (var sold in slotGroup.OrderBy(s => s.Price))
                            lines.Add(new(sold.Part!.Name + (sold.Price > 0 ? $"  {sold.Price:N0}" : ""), "부품", sold.Part.Id));
                    }
                }
                var kinds = WikiBuildingsOf(city);
                if (kinds.Count > 0) Head($"건물 {kinds.Count}가지");
                foreach (string built in kinds) lines.Add(new(built));
                // 이 도시에 딸린 상륙지와, 여기서 받는 의뢰
                var shores = Data.Landings.Where(l => l.City == id && l.Name != "").GroupBy(l => l.Id).Select(g => g.First()).OrderBy(l => l.Name).ToList();
                if (shores.Count > 0) Head($"가까운 상륙지 {shores.Count}곳");
                foreach (var shore in shores) lines.Add(new(shore.Name, "상륙지", shore.Id));
                var asked = Data.Quests.Where(q => q.CityId == id && q.Title != "").OrderBy(q => q.Title).ToList();
                if (asked.Count > 0) Head($"여기서 받는 의뢰 {asked.Count}건" + (asked.Count > 30 ? " (30건만)" : ""));
                foreach (var quest in asked.Take(30)) lines.Add(new(quest.Title, "의뢰", quest.Id));
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
                // 선창에서 물 · 식량 · 자재로 돌리는 전용(「전용」 스킬)
                if (ConvertOf(good) is { } turns) { Head("전용"); lines.Add(new($"{ConvertNames[turns.Kind]} {turns.Each} (하나에)")); }
                if (good.Description != "") { Head("설명"); WikiProse(lines, good.Description); }
                break;
            }
            case "아이템":
            {
                // 설명과 효과(게임이 쓰는 그대로 — 음식 · 부스터 · 장비 · 교환권 · 쓰는 아이템)
                if (ItemNote(id).Trim() is { Length: > 0 } itemNote) WikiProse(lines, itemNote);
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
                // 부관의 스킬(스킬 표의 1000번대)은 찾는 목록에 없어 이름이 안 보이니 쪽 맨 위에 적는다
                if (skill == null && Data.AideSkillLabels.TryGetValue(id, out string? aideSkill)) lines.Add(new(aideSkill + " — 부관의 스킬"));
                if (about != "") WikiProse(lines, about);
                // 이 게임에서의 효과와 한 번 쓰는 데 드는 행동력(바다에서 눌러 쓰는 스킬)
                if (SkillEffect(id) is { Length: > 0 } effect) { Head("이 게임에서"); WikiProse(lines, effect); }
                if (Data.SkillRules.Find(r => r.SkillId == id && ActiveEffects.Contains(r.Effect)) is { } used) lines.Add(new($"행동력 {VigourCost(used)}" + (Sustained.Contains(used.Effect) ? " · 켜 두는 스킬(끝나면 다시 켜진다)" : "")));
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
                var studies = Data.Research.Where(r => r.Skill != "" && r.Skill.Replace(" ", "") == name.Replace(" ", "")).OrderBy(r => r.Level).ThenBy(r => r.Name).ToList();
                if (studies.Count > 0) { Head($"이 스킬의 대학 연구 {studies.Count}건"); foreach (var paper in studies) lines.Add(new(paper.Name, "연구", paper.No)); }
                break;
            }
            case "직업" when Data.JobFacts.Find(j => j.No == id) is { } job:
            {

                if (job.Cost > 0) lines.Add(new($"전직 비용 {job.Cost:N0}"));
                if (job.Expert != "") { Head("전문 스킬"); lines.Add(new(job.Expert, "스킬", Data.Skills.Find(s => s.Name == job.Expert)?.Id ?? 0)); }
                Head($"우대 스킬 {job.Skills.Count}가지");
                foreach (string liked in job.Skills) lines.Add(new(liked, "스킬", Data.Skills.Find(s => s.Name == liked)?.Id ?? 0));
                var jobStudies = Data.Research.Where(r => r.Job == job.Name).OrderBy(r => r.Level).ThenBy(r => r.Name).ToList();
                if (jobStudies.Count > 0) { Head($"이 직업의 대학 연구 {jobStudies.Count}건"); foreach (var paper in jobStudies) lines.Add(new(paper.Name, "연구", paper.No)); }
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
                if (Data.Landings.Find(l => l.Id == quest.LandingId) is { } landing) lines.Add(new(landing.Name + "  (상륙지)", "상륙지", landing.Id));
                if (Data.Discoveries.Find(d => d.Id == quest.DiscoveryId) is { } target) lines.Add(new(target.Name + "  (발견물)", "발견물", target.Id));
                if (quest.Request != "") { Head("의뢰 내용"); WikiProse(lines, quest.Request); }
                if (quest.Hint != "") { Head("실마리"); WikiProse(lines, quest.Hint); }
                break;
            }
            case "연구" when Data.Research.Find(r => r.No == id) is { } study:
            {
                // 대학 연구 — 연구 자료(위키)와 클라이언트의 연구 값 표(67)에서 온 것. 행동의 설명은 연구 과제 표(64), 스킬의 설명은 스킬 표의 글이다
                lines.Add(new(string.Join(" · ", new[] { study.Hall != "" ? study.Hall : $"연구동 Lv {study.Level}", study.Job }.Where(t => t != ""))));
                lines.Add(new($"전공: {study.Major}"));
                lines.Add(new($"필요 페이지 {study.Pages:N0} · 학점 {study.Credit:N0}"));
                if (Data.MajorSkills.TryGetValue(study.Major, out int[]? needed))
                {
                    // 전공에 붙은 스킬과 랭크(표 65) — 전공에 드는 조건으로 보이나 모두인지 하나라도인지는 자료에 없다(게임은 막지 않는다)
                    Head($"전공의 스킬 (Rank {needed[1]})");
                    for (int n = 0; n + 1 < needed.Length; n += 2) lines.Add(new(Data.Skills.Find(s => s.Id == needed[n])?.Name ?? $"스킬 {needed[n]}", "스킬", needed[n]));
                }
                Head("연구 행동 (한 번에 써지는 쪽)");
                foreach (var act in study.Actions)
                {
                    lines.Add(new($"{act.Name} +{act.Count:N0}쪽" + (EventOf(act.Name) == null ? "  (게임에 없다)" : "")));
                    if (StudyTaskNote(act.Name) is { Length: > 0 } told) WikiProse(lines, told);
                    if (StudyWayNote(act.Name) is { Length: > 0 } counted) WikiProse(lines, "게임에서는: " + counted);
                }
                if (study.Skill != "")
                {
                    Head("얻는 스킬");
                    if (Data.Skills.Find(s => s.Name == study.Skill) is { } plain) lines.Add(new(study.Skill, "스킬", plain.Id)); else lines.Add(new(study.Skill));
                    if (StudySkillNote(study.Skill) is { Length: > 0 } note) WikiProse(lines, note);
                    if (StudyNote(study.Skill) is { } effect) lines.Add(new($"게임의 효과: {effect}"));
                }
                lines.Add(new(StudyDone.Contains(study.No) ? "마친 연구다." : study.Actions.Exists(a => EventOf(a.Name) != null && a.Count > 0) ? "이 게임에서 끝낼 수 있다." : "이 게임에 없는 행동뿐이라 끝낼 수 없다."));
                var mates = Data.Research.Where(r => r.Major == study.Major && r.No != study.No).OrderBy(r => r.Level).ToList();
                if (mates.Count > 0) { Head("같은 전공의 연구"); foreach (var mate in mates) lines.Add(new(mate.Name, "연구", mate.No)); }
                // 강의(표 66) — 같은 갈래의 전공 둘이나 셋을 묶은 것. 이름 · 설명과 묶인 전공만 보인다(강의의 수치 넷은 뜻을 모른다)
                if (Data.Lectures.Values.FirstOrDefault(l => l.Skip(2).Contains(study.Major)) is { } lecture)
                {
                    Head("강의");
                    lines.Add(new(lecture[0]));
                    if (lecture[1] != "") WikiProse(lines, lecture[1]);
                    lines.Add(new("전공: " + string.Join(" · ", lecture.Skip(2))));
                }
                break;
            }
            case "역사" when id > 8000 && Data.DigTopics.TryGetValue(id - 8000, out string[]? topic):
            {
                // 조사 주제 — 원본의 표 134(제목 · 글)와 그 주제에서 모으는 정보 조각(표 133 — 번호 ÷ 100 이 주제). 조사하는 기능은 게임에 아직 없다
                lines.Add(new("조사 주제(글만 보인다)"));
                if (topic.Length > 1 && topic[1] != "") WikiProse(lines, topic[1]);
                var scraps = Data.DigClues.Where(c => c.Key / 100 == id - 8000).OrderBy(c => c.Key).Select(c => c.Value).ToList();
                if (scraps.Count > 0) Head($"정보 조각 {scraps.Count}개");
                foreach (string[] scrap in scraps) { lines.Add(new("· " + scrap[0])); if (scrap.Length > 1 && scrap[1] != "") WikiProse(lines, scrap[1]); }
                break;
            }
            case "역사" when id > 7000 && Data.Greats.TryGetValue(id - 7000, out string[]? great):
            {
                // 위인 — 원본의 표 149(이름 · 소개 글)와 일감 이름(표 148 — 위인마다 넷씩 차례로). 일감을 하는 기능은 게임에 아직 없다
                lines.Add(new("위인(글만 보인다)"));
                if (great.Length > 1 && great[1] != "") WikiProse(lines, great[1]);
                var chores = Enumerable.Range((id - 7001) * 4 + 1, 4).Where(Data.GreatTasks.ContainsKey).Select(n => Data.GreatTasks[n]).ToList();
                if (chores.Count > 0) Head("일감");
                for (int n = 0; n < chores.Count; n++) lines.Add(new($"{n + 1}. {chores[n].Trim()}"));
                break;
            }
            case "역사" when id > 6000 && Data.StarTales.Keys.Any(k => k / 100 == id - 6000):
            {
                // 별자리 — 원본의 표 144(이야기 글들)와 표 142(그 별자리의 별 이름). 이름은 표에 없어 천문 발견물의 이름에서 찾는다(ConstellationName)
                lines.Add(new("별자리(글만 보인다)"));
                foreach (var tale in Data.StarTales.Where(t => t.Key / 100 == id - 6000).OrderBy(t => t.Key)) WikiProse(lines, tale.Value);
                var lights = Data.StarNames.Where(s => s.Key / 10 == id - 6000 && s.Value != "" && s.Value != "없음").OrderBy(s => s.Key).Select(s => s.Value).ToList();
                if (lights.Count > 0) Head($"별 {lights.Count}개");
                foreach (string light in lights)
                    lines.Add(Data.Discoveries.Find(d => d.Name == light) is { } seenStar ? new(light, "발견물", seenStar.Id) : new(light));
                if (Data.Discoveries.Find(d => d.Name == ConstellationName(id - 6000)) is { } seenSign) { Head("발견물"); lines.Add(new(seenSign.Name, "발견물", seenSign.Id)); }
                break;
            }
            case "역사" when id > 5000 && Data.GreatPirates.TryGetValue(id - 5000, out string[]? corsair):
            {
                // 대해적 — 원본의 표 94(이름 · 설명). 바다에 나타나는 규칙은 게임에 아직 없다
                lines.Add(new("대해적(글만 보인다)"));
                if (corsair.Length > 1 && corsair[1] != "") WikiProse(lines, corsair[1]);
                break;
            }
            case "역사" when id > 4000 && Data.Themes.TryGetValue(id - 4000, out string[]? motif):
            {
                // 테마 — 원본의 표 83(이름 · 설명). 단서(표 84)는 설명 글에 든 테마 이름으로 잇고, 이야기(표 81)는 그 단서 글에 이름이 나오는 것만 잇는다(숫자 칸이 없다)
                lines.Add(new("테마(글만 보인다)"));
                if (motif.Length > 1 && motif[1] != "") WikiProse(lines, motif[1]);
                var leads = Data.ThemeClues.Where(c => c.Value.Length > 1 && c.Value[1].Replace("\n", "").Contains($"「{motif[0]}」")).OrderBy(c => c.Key).Select(c => c.Value).ToList();
                var storied = Data.Tales.Where(t => leads.Exists(c => c[0].Contains(t.Value[0]) || c[1].Contains(t.Value[0]))).OrderBy(t => t.Key).ToList();
                if (storied.Count > 0) { Head("이야기"); foreach (var t in storied) lines.Add(new(t.Value[0], "역사", t.Key + 2000)); }
                Head("단서");
                lines.Add(new(leads.Count == 0 ? "단서 없음" : $"단서 {leads.Count}개"));
                foreach (string[] c in leads.Take(10)) lines.Add(new("· " + c[0]));
                if (leads.Count > 10) lines.Add(new($"… 그 밖 {leads.Count - 10}개"));
                break;
            }
            case "역사" when id > 3000 && Data.HistoryEvents.TryGetValue(id - 3000, out string[]? happening):
            {
                // 역사적 사건 — 원본의 표 85(이름 · 본문). 결말 셋은 표 87 의 3n−2 · 3n−1 · 3n. 사건을 겪는 기능은 게임에 아직 없다
                lines.Add(new("역사적 사건(글만 보인다)"));
                if (happening.Length > 1 && happening[1] != "") WikiProse(lines, happening[1]);
                Head("결말");
                for (int n = 3 * (id - 3000) - 2; n <= 3 * (id - 3000); n++)
                    if (Data.HistoryEnds.TryGetValue(n, out string[]? outcome) && outcome[0] != "" && !outcome[0].StartsWith('※')) lines.Add(new(outcome[0], "역사", n));
                break;
            }
            case "역사" when id > 2000 && Data.Tales.TryGetValue(id - 2000, out string[]? tale):
            {
                // 단계가 있는 이야기 — 원본의 표 81(이름 · 설명 · 단계 칸)과 표 82(단계 글). 이야기를 푸는 기능은 게임에 아직 없다
                lines.Add(new("이야기(글만 보인다)"));
                if (tale.Length > 1 && tale[1] != "") WikiProse(lines, tale[1]);
                int stage = 0;
                foreach (string cell in tale.Skip(2))
                {
                    string[] halves = cell.Split('|');
                    if (halves.Length < 2 || !int.TryParse(halves[1], out int stepId) || !Data.TaleSteps.TryGetValue(stepId, out string? stepNote)) continue;
                    Head($"{++stage}단계 — 조건 값 {halves[0]}");
                    WikiProse(lines, stepNote);
                }
                break;
            }
            case "역사" when id > 1000 && Data.Legends.TryGetValue(id - 1000, out string[]? legend):
            {
                // 전승 — 원본의 표 124(이름 · 본문). 전승을 쓰는 기능은 게임에 아직 없다
                lines.Add(new("전승(글만 보인다)"));
                if (legend.Length > 1 && legend[1] != "") WikiProse(lines, legend[1]);
                if (legend.Length > 2 && int.TryParse(legend[2], out int found) && Data.Discoveries.Find(d => d.Id == found) is { } told) { Head("발견물"); lines.Add(new(told.Name + "  (발견물)", "발견물", told.Id)); }
                break;
            }
            case "역사" when Data.HistoryEnds.TryGetValue(id, out string[]? end):
            {
                // 역사적 사건의 결말 — 원본의 표 87(이름 · 본문 · 결과 줄 셋). 사건 자체는 게임에 아직 없다
                lines.Add(new("역사적 사건의 결말(글만 보인다)"));
                if (end.Length > 1 && end[1] != "") WikiProse(lines, end[1]);
                var after = end.Skip(2).Where(t => t != "").ToList();
                if (after.Count > 0) { Head("결과"); foreach (string t in after) WikiProse(lines, t); }
                if (Data.HistoryEvents.TryGetValue((id + 2) / 3, out string[]? whence)) { Head("사건"); lines.Add(new(whence[0], "역사", (id + 2) / 3 + 3000)); }
                break;
            }
            case "부관" when Data.AideFacts.Find(a => a.Doc == id) is { } mate:
            {
                // 부관 — dhoguide.kr 의 부관 목록에서 받은 것(분류 · 직업 · 성별 · 국적 · 고용하는 도시 · 구조)
                lines.Add(new(string.Join(" · ", new[] { mate.Kind, mate.Job, mate.Sex }.Where(t => t is not ("" or "-")))));
                if (mate.Nation is not ("" or "-")) lines.Add(new("국적  " + mate.Nation));
                if (Data.Aides.Exists(a => a.Name == mate.Name)) lines.Add(new("이 게임의 부관 목록에 있는 이름이다."));
                var towns = mate.City.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Where(t => t != "-").ToList();
                if (towns.Count > 0) Head("고용하는 도시");
                foreach (string town in towns)
                    lines.Add(_cities.Values.FirstOrDefault(c => c.Name == town) is { } home ? new(town, "도시", home.Id) : new(town));
                if (mate.Rescue is not ("" or "-")) { Head("구조"); lines.Add(new(mate.Rescue)); }
                if (Data.Jobs.Find(j => j.Name == mate.Job) is { } trade && Data.JobFacts.Exists(j => j.No == trade.Id)) { Head("직업"); lines.Add(new(mate.Job, "직업", trade.Id)); }
                // 스킬 — 낱낱의 쪽을 받은 부관만. 숫자는 그 스킬을 익히는 데 드는 부관의 레벨(모험 · 교역 · 전투)과 특성 값
                if (mate.Skills.Count == 0) lines.Add(new("스킬 자료는 아직 받지 않았다."));
                if (mate.NeedLevel != "") { Head("최대 필요 레벨"); lines.Add(new(mate.NeedLevel)); }
                if (mate.NeedTrait != "") { Head("최대 필요 특성"); WikiProse(lines, mate.NeedTrait); }
                foreach (var group in mate.Skills.GroupBy(s => s.Kind))
                {
                    Head(group.Key + " 스킬");
                    foreach (var learned in group)
                    {
                        var needs = new[] { ("모험", learned.Adventure), ("교역", learned.Trade), ("전투", learned.Battle) }.Where(n => n.Item2 > 0).Select(n => $"{n.Item1} {n.Item2}").ToList();
                        if (learned.Trait != "") needs.Add(learned.Trait);
                        string row = learned.Name + (needs.Count > 0 ? "  —  " + string.Join(" · ", needs) : "");
                        // 선장의 스킬이면 그 번호, 부관 · 부관선장 갈래면 스킬 표의 1000번대 번호 — 그림이 붙고 눌러서 설명 쪽으로 간다
                        int linked = Data.Skills.Find(s => s.Name == learned.Name)?.Id ?? (learned.Kind is "부관" or "부관선장" ? AideSkillIdOf(learned.Name) : 0);
                        lines.Add(linked > 0 ? new(row, "스킬", linked) : new(row));
                    }
                }
                lines.Add(new($"번호 {mate.No} · 출처 dhoguide.kr"));
                break;
            }
            case "해역" when Data.Seas.Find(sea => sea.Id == id) is { } waters:
            {
                // 해역 — 낚이는 어종(낚시 표), 이 해역에 항구 앞바다가 든 도시, 배 대는 자리가 든 상륙지
                // 큰 바다(표 9) — 이 해역이 든 큰 바다의 이름과, 같은 큰 바다의 다른 해역들(누르면 그 해역 쪽)
                if (Data.OceanNames.TryGetValue(waters.Group, out string? ocean) && ocean != "")
                {
                    lines.Add(new($"큰 바다 — {ocean}"));
                    var sisters = Data.Seas.Where(s => s.Group == waters.Group && s.Id != waters.Id && s.Name != "").OrderBy(s => s.Name).ToList();
                    if (sisters.Count > 0) { Head($"같은 큰 바다의 해역 {sisters.Count}곳"); foreach (var sister in sisters) lines.Add(new(sister.Name, "해역", sister.Id)); }
                }
                var catches = FishGrounds.TryGetValue(waters.Name, out var ground) ? ground : CommonFish;
                Head("낚이는 어종");
                foreach (string fish in catches)
                {
                    var fishGood = Data.Goods.Find(g => g.Name == fish && g.Id is >= 1_601_000 and < 1_602_000);
                    lines.Add(new(fish + (FishRanks.TryGetValue(fish, out int fishRank) ? $"  (낚시 {fishRank}랭크부터)" : ""), fishGood != null ? "교역품" : "", fishGood?.Id ?? 0));
                }
                var ports = Data.Cities.Where(c => !c.Name.StartsWith('※') && (c.SeaX != 0 || c.SeaY != 0) && Zones.ZoneAt(c.SeaX, c.SeaY) == id).OrderBy(c => c.Name).ToList();
                if (ports.Count > 0) Head($"이 해역의 도시 {ports.Count}곳");
                foreach (var port in ports) lines.Add(new(port.Name, "도시", port.Id));
                var beaches = Data.Landings.Where(l => l.Name != "" && (l.X != 0 || l.Y != 0) && Zones.ZoneAt(l.X, l.Y) == id).GroupBy(l => l.Id).Select(g => g.First()).OrderBy(l => l.Name).ToList();
                if (beaches.Count > 0) Head($"이 해역의 상륙지 {beaches.Count}곳");
                foreach (var beach in beaches) lines.Add(new(beach.Name, "상륙지", beach.Id));
                break;
            }
            case "재질" when MaterialOf(id) is { } timber:
            {
                // 선박 재질 — 내구도 · 돛의 배율은 이용자들이 모은 값(너도밤나무가 100%)
                lines.Add(new($"내구도 {timber.Durability * 100:0.#}% · 돛 성능 {timber.Sail * 100:0.#}%"));
                WikiProse(lines, "너도밤나무를 100% 로 본 배율이다. 강화 때 재료 칸 하나에 넣거나 특수 조선에서 고른다.");
                if (Data.MaterialItems.TryGetValue(id, out int timberItem) && timberItem > 0) { Head("재질 아이템"); lines.Add(new(ItemName(timberItem), "아이템", timberItem)); }
                var same = Data.ShipMaterials.Where(m => m.Id != id && m.Name != "" && Math.Abs(m.Sail - timber.Sail) < 1e-6 && Math.Abs(m.Durability - timber.Durability) < 1e-6).OrderBy(m => m.Name).Take(12).ToList();
                if (same.Count > 0) Head("배율이 같은 재질");
                foreach (var twin in same) lines.Add(new(twin.Name, "재질", twin.Id));
                break;
            }
            case "상륙지" when Data.Landings.Find(l => l.Id == id) is { } site:
            {
                bool placed = site.X != 0 || site.Y != 0;
                lines.Add(new(placed ? $"{SeaNameAt(site.X, site.Y)}  ({site.X}, {site.Y})" : "배를 대는 자리를 아직 모른다"));
                if (_cities.ContainsKey(site.City)) { Head("가까운 도시"); lines.Add(new(CityName(site.City), "도시", site.City)); }
                if (site.ObservePoints > 0 || site.GatherKinds.Count > 0)
                {
                    Head("뭍의 탐색 지점");
                    if (site.ObservePoints > 0) lines.Add(new($"관찰 지점 {site.ObservePoints}곳"));
                    if (site.GatherKinds.Count > 0) lines.Add(new($"채집 지점 {site.GatherKinds.Count}곳"));
                }
                var here = Data.Quests.Where(q => q.LandingId == id && q.Title != "").OrderBy(q => q.Title).ToList();
                if (here.Count > 0) Head($"이곳으로 가는 의뢰 {here.Count}건");
                foreach (var quest in here) lines.Add(new(quest.Title, "의뢰", quest.Id));
                break;
            }
            case "배" when Data.Ships.Find(s => s.Id == id) is { } ship:
            {
                var stats = ShipStats.Of(ship, Settings.Ships);
                // 성능은 줄글이 아니라 선박 정보 창 같은 카드로 — 화면 쪽이 이 여덟 줄 자리에 그린다(사용자, 2026-10-09)
                for (int row = 0; row < 8; row++) lines.Add(new("", "배카드", ship.Id));
                // 배 상세(강화 횟수 · 건조 일수 · 강화 상한 · 부품 칸) — 자료가 있는 배만
                if (Data.ShipDetail(ship.Name) is { } detail)
                {
                    Head("강화");
                    if (detail.Times > 0) lines.Add(new($"강화 {detail.Times}번" + (detail.Retimes > 0 ? $" · 재강화 {detail.Retimes}번" : "") + (detail.Days > 0 ? $" · 건조 {detail.Days}일" : "")));
                    if (detail.Caps.Count >= 10)
                    {
                        lines.Add(new($"상한: 내구 {detail.Caps[0]} · 세로돛 {detail.Caps[1]} · 가로돛 {detail.Caps[2]}"));
                        lines.Add(new($"  조력 {detail.Caps[3]} · 선회 {detail.Caps[4]} · 내파 {detail.Caps[5]} · 장갑 {detail.Caps[6]}"));
                        lines.Add(new($"  선실 {detail.Caps[7]} · 포실 {detail.Caps[8]} · 창고 {detail.Caps[9]}"));
                    }

                    if (detail.Borrowed != "") lines.Add(new($"(이 배의 자료가 없어 「{detail.Borrowed}」 것을 빌렸다)"));
                }
                var yards = Data.Shipyards.Where(y => y.Value.ContainsKey(ship.Name) && _cities.ContainsKey(y.Key)).OrderBy(y => CityName(y.Key)).ToList();
                Head($"파는 조선소 {yards.Count}곳");
                foreach (var yard in yards) lines.Add(new(CityName(yard.Key) + (yard.Value[ship.Name] > 0 ? $"  {yard.Value[ship.Name]:N0}" : ""), "도시", yard.Key));
                // 특수 조선으로 짓는 배 — 그 선체를 그 도시의 조선소 주인에게 가져간다(배 상세의 「특수 건조 도시」; 사용자, 2026-10-10: 「배 기영 얻는법이 없네」)
                if (Data.ShipDetails.Find(d => d.Name == ship.Name) is { Special.Count: > 0 } built)
                {
                    Head($"특수 조선 {built.Special.Count}곳 — 선체를 가져가 짓는다");
                    foreach (var way in built.Special)
                    {
                        int town = Data.Cities.Find(c => c.Name == way.City)?.Id ?? 0;
                        string told = $"{way.City} — {way.Hull}" + (way.Rank > 0 ? $" · 조선 랭크 {way.Rank}" : "") + (way.Material != "" ? $" · {way.Material}" : "");
                        if (town != 0) lines.Add(new(told, "도시", town)); else lines.Add(new(told));
                    }
                }
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
