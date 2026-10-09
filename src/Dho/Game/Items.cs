using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 소지품 — 지금은 전직증뿐이다. 전직증은 직업마다 하나씩 있는 지어낸 아이템이고(클라이언트 아이템 표에는
/// 「전직 신청서」「초급 전직 추천장」만 있다), 쓰면 그 직업이 된다. 「아이템 추가」에서 마음대로 넣는다.
/// </summary>
internal sealed partial class Voyage
{
    /// <summary>전직증의 아이템 번호 = 이 값 + 직업 번호.</summary>
    public const int JobPaper = 9_000_000;

    /// <summary>돛의 빛깔(0xRRGGBB). 배를 갈아타도 따라온다(배마다 따로 적지는 않는다).</summary>
    public int SailColor { get; private set; } = 0xFFFFFF;

    /// <summary>돛의 무늬(0 ~ 17)와 색(0 ~ 9) — 돛 도료를 쓰면 고르는 창이 뜬다.</summary>
    public int SailPattern { get; private set; }
    public int SailTint { get; private set; }

    /// <summary>고르는 동안 미리 보기(되돌릴 때도 쓴다).</summary>
    /// <summary>
    /// 선박용 도료(船塗料 — gvdb: 「使うと船体の色を変えられる」, 공방 장인 60곳 500)로 고를 수 있는 선체 빛깔 — 재질 줄의 번호들.
    /// 원본이 어떤 빛깔을 고르게 하는지는 모른다 — 클라이언트 재질 표에서 「칠한 빛깔」로 적힌 열다섯 줄(특별 주문 도료들의 빛깔)을 쓴다(짐작).
    /// </summary>
    public List<int> HullPaints => _hullPaints ??= Data.MaterialColors.Where(c => c.Value >> 24 == 2).GroupBy(c => c.Value).Select(g => g.Min(c => c.Key)).OrderBy(id => id).ToList();
    private List<int>? _hullPaints;

    /// <summary>고르는 동안 미리 보는 빛깔(재질 줄 번호) — 0 이면 없음.</summary>
    public int HullPaintTry { get; set; }

    /// <summary>타고 있는 배의 선체 빛깔 — 도료로 칠했으면 그 빛깔, 아니면 재질의 것.</summary>
    public int ShipHullColor => HullPaintTry > 0 ? HullColor(HullPaintTry) : Work.HullPaint > 0 ? HullColor(Work.HullPaint) : HullColorOf(Ship, ShipMaterialId);

    public bool PaintHull(int paint)
    {
        if (Data.Items.Find(i => i.Effect == "HullPaint" && Items.GetValueOrDefault(i.Id) > 0) is not { } can || !HullPaints.Contains(paint) || paint == Work.HullPaint) return false;
        if (--Items[can.Id] <= 0) Items.Remove(can.Id);
        (Work.HullPaint, HullPaintTry) = (paint, 0);
        Cues.Enqueue("Buy");
        Say($"{can.Name}(으)로 선체를 칠했다.");
        return true;
    }

    public void ShowSail(int pattern, int tint) => (SailPattern, SailTint) = (pattern, tint);

    /// <summary>고른 대로 칠한다 — 돛 도료 하나가 든다.</summary>
    /// <summary>쓰고 있는 돛 도료의 번호 — 1 ~ 23, 특수는 101 ~ 111.</summary>
    public int SailDye { get; private set; } = 1;
    public int SailDyeItem => Data.Items.Find(i => i.Effect == "SailPaint" && (int)i.Amount == SailDye)?.Id ?? 0;

    /// <summary>
    /// 도료 번호마다 고를 수 있는 무늬(돛 그림의 번호 TEX_BASE nnn). 이용자가 준 원본 도료 표의 그림과 클라이언트의 무늬 104장을 눈으로 맞춘 것이다.
    /// </summary>
    public static int[] DyePatterns(int dye) => dye switch
    {
        1 => [0, 1, 2, 3, 4],
        2 => [5, 6, 7, 8, 34, 35, 37, 38],
        3 => [9, 10, 11, 12, 13, 39, 40],
        4 => [14, 15, 16, 17, 36],
        5 => [18, 19, 20, 21, 22, 41],
        6 => [23, 24, 25, 26, 42, 43],
        7 => [27, 28, 29, 30, 44],
        8 => [45, 46, 47, 48, 49],
        9 => [31, 32, 33, 50, 51, 52],
        10 => [61], 11 => [62],
        12 => [64, 65, 66, 67], 13 => [68, 69, 70, 71], 14 => [72, 73, 74, 75], 15 => [76, 77, 78, 79], 16 => [80, 81, 82, 83],
        17 => [89, 90, 91, 92, 93],
        18 => [84, 85, 86, 87],
        19 => [95], 20 => [96], 21 => [97],
        22 => [98, 99],
        23 => [100, 101, 102, 103],
        > 100 and <= 111 => [new[] { 53, 54, 55, 56, 57, 58, 59, 60, 63, 88, 94 }[dye - 101]],
        _ => [0],
    };

    public bool PaintSail()
    {
        if (Data.Items.Find(i => i.Id == SailDyeItem && Items.GetValueOrDefault(i.Id) > 0) is not { } dye) return false;
        if (--Items[dye.Id] <= 0) Items.Remove(dye.Id);
        Say("돛 도료로 돛을 칠했다.");
        return true;
    }

    /// <summary>아이템 번호 → 가진 수.</summary>
    public Dictionary<int, int> Items { get; } = new();

    /// <summary>전직증으로 될 수 있는 직업들(빈 줄은 뺀다).</summary>
    public IEnumerable<NamedData> JobsToTake() => Data.Jobs.Where(j => j.Name.Length > 0 && !j.Name.StartsWith('※'));

    public ItemData? ItemOf(int item) => Data.Items.Find(i => i.Id == item);

    /// <summary>도구점이 있는가(시내 지도의 표식). 지도가 없는 도시는 있다고 본다.</summary>
    public bool HasItemShop => TownMap is not { Marks.Count: > 0 } map || map.Marks.Any(m => m.Place is 11 or 19 or 26 or 27 or 31);

    public void BuyItem(ItemData item)
    {
        if (Mode != Mode.Port || Money < item.Price) return;
        Money -= item.Price;
        Cues.Enqueue("Buy");
        Items[item.Id] = Items.GetValueOrDefault(item.Id) + 1;
        Say($"{item.Name}을(를) 샀다. ({item.Price:N0} 두캇)");
    }

    /// <summary>난파할 참에 구명도구가 있으면 하나 쓰고 버틴다.</summary>
    private bool UseLifebuoy()
    {
        var buoy = Data.Items.Find(i => i.Effect == "Lifebuoy" && Items.GetValueOrDefault(i.Id) > 0);
        if (buoy == null) return false;
        if (--Items[buoy.Id] <= 0) Items.Remove(buoy.Id);
        Durability = Math.Max(Durability, Stats.Durability * buoy.Amount / 100);
        Crew = Math.Max(Crew, Stats.MinCrew);
        Say($"{buoy.Name}을(를) 써서 가까스로 버텼다!");
        return true;
    }

    // 선체 특수효과 도료 1 ~ 15(아이템 표의 원본 번호). 원본의 효과 자료(ef 묶음)는 못 풀어서 빛깔은 지은 것이다 — 15 번은 무지개처럼 빛깔이 돈다
    public const int HullEffectPaint = 1510699;
    public static readonly (string Name, int Color)[] HullEffects =
    [
        ("푸른", 0x3C8CFF), ("붉은", 0xFF4030), ("초록", 0x40E060), ("금", 0xFFC830), ("보라", 0xA050FF),
        ("흰", 0xF0F4FF), ("청록", 0x30E0D8), ("분홍", 0xFF70B8), ("주황", 0xFF8A28), ("짙은 보라", 0x6030C0),
        ("연두", 0xA8F040), ("얼음", 0x90D8FF), ("진홍", 0xE01848), ("옥", 0x20B890), ("무지개", 0xFFFFFF),
    ];

    private Dictionary<int, GearModel>? _gearModels;

    // 지금 입은 장비가 그 몸 틀에 입히는 것들 — (부위, [모형 묶음, 항목, 텍스처 묶음, 항목], 색). 그 몸 틀(성별)이 못 입는 것은 빠진다.
    // 신발은 입은 옷에 따라 긴 것 · 짧은 것, 바지 조각이 있는 것 · 없는 것이 갈린다.
    public List<(string Part, int[] Entry, List<int> Colors)> WornModels(int frame)
    {
        _gearModels ??= Data.GearModels.ToDictionary(g => g.Id);
        var found = new List<(string, int[], List<int>)>();
        GearModel? Of(int slot) => Equipped[slot] > 0 && Items.GetValueOrDefault(Equipped[slot]) > 0 ? _gearModels.GetValueOrDefault(Equipped[slot]) : null;
        var body = Of(0);
        foreach (int slot in (int[])[0, 1, 2, 3])
        {
            if (Of(slot) is not { } gear || !gear.Frames.TryGetValue($"{frame}", out var spots) || spots.Count == 0) continue;
            int pick = 0;
            if (gear.Part == "leg" && body != null)
            {
                bool shorter = (body.Flags & 0x80) != 0, bare = (body.Flags & 0x40) != 0;
                pick = spots.Count >= 4 ? (shorter ? 2 : 0) + (bare ? 1 : 0) : shorter ? 1 : 0;
            }
            var entry = (pick < spots.Count ? spots[pick] : null) ?? spots[0];
            if (entry is { Length: 4 }) found.Add((gear.Part, entry, gear.Colors));
        }
        return found;
    }

    // 입은 장비를 한 줄로 — 겉모습이 바뀌었는지 가리는 데 쓴다
    public string WornKey => string.Join(",", Equipped.Select(e => e > 0 && Items.GetValueOrDefault(e) > 0 ? e : 0));

    private Dictionary<int, PaperItem>? _foods;
    /// <summary>행동력 음식(아이템 표의 원본) — 없으면 null.</summary>
    /// <summary>음식을 먹으면 얻는 것 한 줄 — 「행동력 +40 · 피로 −8 · 괴혈병 회복」. 음식이 아니면 빈 글. (먹을 때의 셈과 같다)</summary>
    public string FoodEffectText(int item)
    {
        if (FoodOf(item) is not { } food) return "";
        int vigour = FoodVigour(food);
        string text = food.Description.Replace(" ", "");
        double relief = Data.FoodEffects.TryGetValue(item, out var effect) && effect.Length > 1 && effect[1] > 0 ? effect[1] : text.Contains("피로회복") ? vigour * 0.2 : 0;
        return $"행동력 +{vigour}" + (relief > 0 ? $" · 피로 −{relief:0}" : "") + (text.Contains("괴혈병") ? " · 괴혈병 회복" : "");
    }

    public PaperItem? FoodOf(int item) => (_foods ??= Data.Foods.ToDictionary(f => f.Id)).GetValueOrDefault(item);

    /// <summary>음식의 설명 글에 적힌 「행동력+n」의 n.</summary>
    public static int FoodVigour(PaperItem food) =>
        System.Text.RegularExpressions.Regex.Match(food.Description, @"행동력\s*[+＋]\s*(\d+)") is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0;

    public string ItemName(int item) =>
        ItemOf(item) is { } known ? known.Name :
        FoodOf(item) is { } food ? food.Name :
        BoosterOf(item) is { } booster ? booster.Name :
        DecoOf(item) is { } deco ? deco.Name :
        CrewGearOf(item) is { } crewGear ? crewGear.Name :
        Data.Papers.Find(p => p.Id == item) is { } paper ? paper.Name :
        item < 1_000_000 && Data.Gear.Find(g => g.Id == item) is { } gear ? gear.Name :
        item > MaterialItem && item < MaterialItem + 1000 && MaterialOf(item - MaterialItem) is { } wood ? wood.Name :
        item >= JobPaper && Data.Jobs.Find(j => j.Id == item - JobPaper) is { } job ? $"{job.Name} 전직증" :
        Data.ItemNames.TryGetValue(item, out string? plain) && plain.Length > 0 ? plain :
        Data.ShipParts.Find(p => p.Id == item) is { } part ? part.Name : $"아이템 {item}";      // 그 밖의 것은 클라이언트 아이템 표의 이름

    /// <summary>입거나 찬 장비 — 갈래(0 옷 · 1 모자 · 2 신발 · 3 장갑 · 4 무기 · 5 장신구)마다 아이템 번호. 장비한 것은 소지품에 그대로 있다.</summary>
    public int[] Equipped { get; } = new int[6];
    public static readonly string[] GearSlots = ["옷", "모자", "신발", "장갑", "무기", "장신구"];

    public GearItem? GearOf(int item) => item is > 0 and < 600_000 ? Data.Gear.Find(g => g.Id == item) : null;

    /// <summary>가진 장비 물품(소지품 가운데 장비 표의 것).</summary>
    public List<GearItem> GearOwned() => Items.Keys.OrderBy(id => id).Select(GearOf).OfType<GearItem>().ToList();

    /// <summary>장비한다 — 같은 갈래에 있던 것은 벗겨진다. 이미 장비한 것을 고르면 벗는다.</summary>
    public void Equip(int item)
    {
        if (GearOf(item) is not { } gear || Items.GetValueOrDefault(item) <= 0 || gear.Slot >= Equipped.Length) return;
        if (Equipped[gear.Slot] == item) { Equipped[gear.Slot] = 0; Say($"{gear.Name}을(를) 벗었다."); return; }
        Equipped[gear.Slot] = item;
        Say($"{gear.Name}을(를) 장비했다.");
    }

    public void Unequip(int slot)
    {
        if (slot < 0 || slot >= Equipped.Length || Equipped[slot] == 0) return;
        Say($"{ItemName(Equipped[slot])}을(를) 벗었다.");
        Equipped[slot] = 0;
    }

    /// <summary>장비한 것들의 수치 합 — 장비 표 줄 꼬리의 칸 1 · 3 · 5 · 7 차례(뜻은 못 밝혔다).</summary>
    public int[] GearTotals()
    {
        var totals = new int[4];
        foreach (int item in Equipped)
            if (GearOf(item) is { Stats.Count: >= 4 } gear && Items.GetValueOrDefault(item) > 0)
                for (int k = 0; k < 4; k++) totals[k] += gear.Stats[k];
        return totals;
    }

    /// <summary>선박 교환권들(아이템 표의 원본 번호 그대로).</summary>
    // 이름에 「선박」이 없는 것도 있다(「컴벌랜드 교환권」「플로레 교환권」) — 배를 찾을 수 있으면 선박 교환권으로 친다
    public IEnumerable<PaperItem> ShipTickets() => Data.Papers.Where(p => p.Name.Contains("교환권") && (p.Name.Contains("선박") || TicketShip(p) != null));

    private readonly Dictionary<int, ShipData?> _ticketShips = new();

    /// <summary>
    /// 교환권이 바꿔 주는 배 — 설명 글의 「」 안 이름(없으면 교환권 이름의 앞머리)으로 배 표에서 찾는다.
    /// 「명품 · 개량 · 개량형 …」이 붙은 배는 배 표에 따로 없어서 그 말을 뗀 바탕 배로 준다(올려 준 성능은 못 살린다). 못 찾으면 null.
    /// </summary>
    public ShipData? TicketShip(PaperItem ticket)
    {
        if (_ticketShips.TryGetValue(ticket.Id, out var cached)) return cached;
        string text = ticket.Description.Replace("\n", "");
        var names = new List<string>();
        foreach (var (open, close) in new[] { ('「', '」'), ('“', '”') })
            if (text.IndexOf(open) is >= 0 and var from && text.IndexOf(close, from + 1) is > 0 and var to) names.Add(text[(from + 1)..to]);
        if (ticket.Name.IndexOf("선박 교환권", StringComparison.Ordinal) is > 0 and var cut) names.Add(ticket.Name[..cut].Trim());
        else if (ticket.Name.IndexOf("교환권", StringComparison.Ordinal) is > 0 and var end) names.Add(ticket.Name[..end].Trim());
        static string Tight(string name) => name.Replace(" ", "");
        ShipData? Named(string name) => Data.Ships.Find(s => Tight(s.Name) == Tight(name));
        ShipData? found = null;
        foreach (string name in names) if ((found ??= Named(name)) != null) break;
        if (found == null)
            foreach (string name in names)
            {
                string bare = Tight(name);
                for (bool cutAny = true; cutAny && found == null;)
                {
                    cutAny = false;
                    foreach (string lead in (string[])["명품", "개량형", "개량", "특급", "월광", "특별판", "기념판"])
                        if (bare.StartsWith(lead)) { bare = bare[lead.Length..]; cutAny = true; break; }
                    if (cutAny) found = Named(bare);
                }
                if (found != null) break;
            }
        // 「」 없이 「…월광 상업용 대형 클리퍼와 교환할 수 있는 티켓」처럼 적힌 교환권 — 설명 글에 이름이 통째로 든 배 가운데 가장 긴 것
        if (found == null && Tight(text) is { Length: > 0 } packed)
            found = Data.Ships.Where(s => Tight(s.Name).Length >= 4 && packed.Contains(Tight(s.Name))).MaxBy(s => Tight(s.Name).Length);
        return _ticketShips[ticket.Id] = found;
    }

    // 입은 장비가 그 스킬에 올려 주는 랭크의 합(익힌 스킬에만 듣는다 — Rank 가 익힌 것만 센다)
    public int GearRank(int skillId)
    {
        int sum = 0;
        foreach (int item in Equipped)
            if (item > 0 && Items.GetValueOrDefault(item) > 0 && Data.GearBoosts.TryGetValue(item, out var boosts)) sum += boosts.GetValueOrDefault(skillId);
        return sum;
    }

    // 장비의 수치와 올려 주는 스킬 — 장비 표 줄 꼬리의 다섯 칸은 공격력 · 방어력 · 정장도 · 변장도 · 내구도(무기 · 옷의 값과 위키의 적는 차례로 맞춤)
    public static readonly string[] GearStatNames = ["공격력", "방어력", "정장도", "변장도", "내구도"];
    public string GearLine(GearItem gear)
    {
        var parts = new List<string>();
        for (int k = 0; k < Math.Min(gear.Stats.Count, GearStatNames.Length); k++)
            if (gear.Stats[k] != 0) parts.Add($"{GearStatNames[k]} {gear.Stats[k]}" + (k < 2 && ForgedOf(gear.Id, k) is not 0 and var forged ? $"({forged:+0;-0})" : ""));
        if (Data.GearBoosts.TryGetValue(gear.Id, out var boosts))
            parts.AddRange(boosts.Select(b => $"{SkillName(b.Key)} +{b.Value}"));
        if (Data.GearEffects.GetValueOrDefault(gear.Id) is { } worn)
            foreach (var (key, label) in new[] { ("VigourSave", "행동력 감소 억제"), ("Speed", "항해속도 상승"), ("SupplySave", "물자 감소 억제"), ("Luck", "재해 발생률 감소"), ("Ambush", "기습 · 강습률 감소"), ("Melee", "백병전 전투력 상승"), ("Loot", "수탈률 상승"), ("AideGrow", "부관 성장 촉진") })
                if (worn.GetValueOrDefault(key) is > 0 and var rank) parts.Add($"{label} {rank}");
        return parts.Count == 0 ? "" : "\n" + string.Join(" · ", parts);
    }

    public string ItemNote(int item)
    {
        if (FoodOf(item) is { } food) return food.Description.Replace("\n", " ");
        if (BoosterOf(item) is { } booster) return booster.Description.Replace("\n", " ");
        if (DecoOf(item) is { } deco) return deco.Description.Replace("\n", " ");
        if (CrewGearOf(item) is { } crewGear) return crewGear.Description.Replace("\n", " ") + (crewGear.Skill > 0 && Data.ShipSkillNames.GetValueOrDefault(crewGear.Skill) is { Length: > 0 } gearSkill ? $" 〔선박 스킬: {gearSkill}〕" : "");
        if (item < 1_000_000 && Data.Gear.Find(g => g.Id == item) is { } gear) return gear.Description.Replace("\n", " ") + GearLine(gear);
        if (Data.Papers.Find(p => p.Id == item) is { } paper)
            return paper.Description.Replace("\n", " ") + (BuildPartLine(item) is { Length: > 0 } build ? "  " + build : "") + (paper.Name.Contains("교환권") && (paper.Name.Contains("선박") || TicketShip(paper) != null) ? (TicketShip(paper) is { } gives ? $"  → {gives.Name}" : "  (바꿀 배를 못 찾았다)") : "");
        if (ItemOf(item) is { } known)
            return known.Effect switch
            {
                "Fatigue" => $"선원의 피로를 {known.Amount:0} 푼다.",
                "LandThrow" => "육상전에서 적에게 던져 피해를 준다.",
                "LandBerserk" => "육상전에서 쓰면 공격력이 2배, 방어력이 1/4 이 된다.",
                "LandHeal" => $"육상전에서 입은 피해를 고친다(생명력의 {known.Amount:0}%).",
                "Repair" => $"내구를 {known.Amount:0}% 고친다.",
                "RepairFlat" => $"내구를 {known.Amount:0} 고친다(싸우는 중에는 못 쓴다).",
                "Cure" => $"{Data.Disasters.Find(d => d.Id == (int)known.Amount)?.Name ?? "재해"}을(를) 가라앉힌다.",
                "Lifebuoy" => "난파할 때 저절로 쓰여 한 번 버틴다.",
                "Stock" => $"쓰면 자재 {known.Amount:0}개로 바뀐다(창고에 실린다).",
                "Truce" => "해전 중에 쓰면 싸움이 끝난다(괴물에게는 안 통한다).",
                "Bell" => "백병전 중에 쓰면 반드시 빠져나온다.",
                "BattleSkill" => $"해전 중에 쓰면 그 싸움 동안 {SkillName((int)known.Amount)}이(가) 듣는다(랭크 1).",
                "Memo" => TranslationMemos.TryGetValue(known.Id, out var tongues) ? $"쓰면 그 도시에 머무는 동안 말이 통한다: {string.Join(" · ", tongues.Select(SkillName))}." : "",
                "HullPaint" => "쓰면 선체의 빛깔을 고르는 창이 뜬다.",
                "Bait" => "쓰면 낚시 스킬이 없어도 한 번 낚는다(바다 · 항구).",
                "SkillTool" => $"쓰면 {SkillName((int)known.Amount)} 스킬이 없어도 한 번 한다(랭크 1).",
                "RecipeBook" when RecipeBookOf(known.Id) is { } book => $"레시피 책 — {book.Recipes.Count}가지: " + string.Join(" · ", book.Recipes.Take(6).Select(id => Data.Recipes.Find(r => r.Id == id)?.Name ?? "")) + (book.Recipes.Count > 6 ? " …" : ""),
                "SailPaint" => "쓰면 돛의 무늬와 색을 고르는 창이 뜬다. 대장간 · 도구점에서 판다.",
                "Paper" when known.Id == MedalPaper => $"{PermitCost}장을 본거지 왕궁의 서기관에게 가져가면 전용함 건조 허가증으로 바꿔 준다.",
                "Paper" when known.Id == ShipPermit => $"국가공헌 훈장증서 {PermitCost}장과 바꾼 증서.",
                _ => "",
            };
        if (item > MaterialItem && item < MaterialItem + 1000 && MaterialOf(item - MaterialItem) is { } wood)
            return $"선박 재질 — 건조할 때 고른다. 내구 {wood.Durability * 100:0}% · 돛 {wood.Sail * 100:0}%";
        if (item < JobPaper || Data.Jobs.Find(j => j.Id == item - JobPaper) is not { } job) return "";
        string line = job.Group switch { 0 => "모험", 1 => "교역", 2 => "전투", _ => "" };
        return $"쓰면 {job.Name}(으)로 전직한다." + (line == "" ? "" : $" ({line} 계열)");
    }

    /// <summary>특수조선 해체 기법서 — 아이템 표(14)의 원본 번호. 성능초기화에 한 권 든다(「특수 조선에서 강화한 성능을 초기화하는 방법이 적혀 있는 책」).</summary>
    public const int DismantleBook = 1_510_017;

    /// <summary>국가공헌 훈장증서 · 전용함 건조 허가증(items.json), 허가증 한 장에 드는 훈장증서.</summary>
    public const int MedalPaper = 9_300_001, ShipPermit = 9_300_002, PermitCost = 200;

    /// <summary>본거지 왕궁의 서기관 — 국가공헌 훈장증서 200장을 전용함 건조 허가증 한 장으로 바꾼다.</summary>
    public void ExchangePermit()
    {
        if (!AtCourt || Items.GetValueOrDefault(MedalPaper) < PermitCost) return;
        if ((Items[MedalPaper] -= PermitCost) <= 0) Items.Remove(MedalPaper);
        Items[ShipPermit] = Items.GetValueOrDefault(ShipPermit) + 1;
        Say($"서기관에게 국가공헌 훈장증서 {PermitCost}장을 건네고 전용함 건조 허가증을 받았다.");
    }

    // 소지품은 백 가지까지(가짓수 — 같은 것은 겹쳐 든다)
    public const int ItemKinds = 100;

    public void AddItem(int item, int count = 1)
    {
        if (!Items.ContainsKey(item) && Items.Count >= ItemKinds) { Say($"소지품이 가득 찼다({ItemKinds}가지). {ItemName(item)}을(를) 받지 못했다."); Cues.Enqueue("Error"); return; }
        if (item == CoalItem && Items.GetValueOrDefault(item) + count > CoalLimit)
        {
            // 석탄 연료는 한 사람이 200개까지(사용자가 준 증기선 글)
            count = CoalLimit - Items.GetValueOrDefault(item);
            if (count <= 0) { Say($"석탄 연료는 {CoalLimit}개까지만 가질 수 있다."); Cues.Enqueue("Error"); return; }
        }
        Items[item] = Items.GetValueOrDefault(item) + count;
        Say(count > 1 ? $"{ItemName(item)} {count}개를 얻었다." : $"{ItemName(item)}을(를) 얻었다.");
    }

    public void UseItem(int item)
    {
        if (Items.GetValueOrDefault(item) <= 0) return;
        if (ItemOf(item) is { } known)
        {
            switch (known.Effect)
            {
                case "Fatigue" when Fatigue > 0:
                    Fatigue = Math.Max(0, Fatigue - known.Amount);
                    Say($"{known.Name}을(를) 썼다. 선원들이 기운을 차렸다.");
                    Cues.Enqueue("Eat");               // 음식을 먹는 소리
                    break;
                case "LandHeal":
                    // 치료약 갈래 — gvdb: 「陸戦治療（陸戦で、ターゲットのダメージを回復する）」 効果・小/中. 싸우는 중이면 한 합을 쓴다
                    if (Life >= MaxLife) { Say($"{known.Name} — 다친 데가 없다."); Cues.Enqueue("Error"); return; }
                    if (LandFight is { Result: null }) { _healWith = known; LandAct(4); return; }
                    HealLife(known);
                    Say($"{known.Name}을(를) 썼다. 생명력 {Life:0} / {MaxLife}.");
                    break;
                case "RepairFlat" when Durability < Stats.Durability && Battle is not { Result: null }:
                    // 목공도구 갈래 — gvdb: 명인 목수의 목공도구 「耐久力を100回復」 · 거장의 목공도구 「500回復」 · 「戦闘中は使用不可」
                    Durability = Math.Min(Stats.Durability, Durability + known.Amount);
                    Say($"{known.Name}(으)로 배를 고쳤다. 내구 {Durability:0} / {Stats.Durability}.");
                    break;
                case "LandThrow" or "LandBerserk":
                    if (LandFight is not { Result: null }) { Say($"{known.Name} — 육상전에서 쓴다."); Cues.Enqueue("Error"); return; }
                    UseLandItem(known);
                    return;
                case "Repair" when Durability < Stats.Durability:
                    Durability = Math.Min(Stats.Durability, Durability + Stats.Durability * known.Amount / 100);
                    Say($"{known.Name}(으)로 배를 고쳤다.");
                    break;
                case "Bait":
                    // 낚시밥 — 낚시 스킬이 없어도 한 번 낚는다(클라이언트 설명: 「도시나 해상에서 낚시를 할 수 있는 미끼」 · gvdb: 쓰면 걸리는 효과 「釣り」)
                    if (!FishWithBait((int)known.Amount)) return;
                    break;
                case "Truce" or "Bell" or "BattleSkill":
                    if (!UseBattleItem(known)) return;
                    break;
                case "Stock":
                    // 비축물자:자재 — 클라이언트 설명: 「대해전 중 도시에서 후원해준 비축물자. 자재 100개로 교환할 수 있다」. 창고가 허락하는 만큼만 받는다
                    int stock = Math.Min((int)known.Amount, HoldFree);
                    if (stock <= 0) { Say($"{known.Name} — 창고가 가득 찼다."); Cues.Enqueue("Error"); return; }
                    Supplies[RepairSupply] = SupplyCount(RepairSupply) + stock;
                    Say($"{known.Name}을(를) 풀었다. 자재 {stock}개를 실었다.");
                    break;
                case "LevelCharm":
                    UseLevelCharm(known);
                    break;
                case "Veil":
                    UseVeil(known);
                    break;
                case "ExpCharm":
                    UseExpCharm(known);
                    break;
                case "Digest":
                    if (!Stuffed) { Say($"{known.Name} — 만복 상태가 아니다."); Cues.Enqueue("Error"); return; }
                    _stuffedUntil = 0;
                    Say($"{known.Name}을(를) 먹었다. 만복이 풀렸다.");
                    break;
                case "AideMeal":
                    if (!UseAideMeal(known)) return;
                    break;
                case "Memo":
                    if (!UseTranslationMemo(known.Id)) return;
                    break;
                case "SkillTool":
                    if (!UseSkillTool((int)known.Amount, known.Name)) return;
                    break;
                case "HullPaint":
                    Dialog = Dialog.HullPaint;         // 빛깔을 고르는 창 — 「확인」을 눌러야 도료가 든다
                    return;
                case "SailPaint":
                    SailDye = (int)known.Amount;
                    Dialog = Dialog.Sail;              // 무늬와 색을 고르는 창 — 「확인」을 눌러야 도료가 든다
                    return;
                case "RecipeBook":
                    Dialog = Dialog.Items;             // 책은 닳지 않는다 — 레시피 쪽에서 쓴다
                    Say($"{known.Name} — 「레시피」 쪽에 든 레시피가 열려 있다.");
                    return;
                case "Cure" when Disasters.Find(d => d.Data.Id == (int)known.Amount) is { } disaster:
                    End(disaster);
                    break;
                default:
                    Say($"{known.Name} — 지금은 쓸 데가 없다.");
                    Cues.Enqueue("Error");
                    return;
            }
        }
        else if (GearOf(item) != null) { Equip(item); return; }
        else if (item == RefitGuardItem || Array.Exists(RefitBooks, b => b.Item == item))
        {
            if (!UseRefit(item)) return;
        }
        else if (item >= HullEffectPaint && item < HullEffectPaint + HullEffects.Length)
        {
            // 선체 특수효과 도료 — 「배 주위에 특수 효과를 발생시키는 도료. 효과는 영속된다」. 타고 있는 배에 입힌다(다른 도료를 쓰면 바뀐다)
            int effect = item - HullEffectPaint + 1;
            if (Work.HullEffect == effect) { Say($"{ItemName(item)} — 이미 이 효과가 입혀져 있다."); Cues.Enqueue("Error"); return; }
            Work.HullEffect = effect;
            Say($"{ItemName(item)}을(를) {Ship.Name}에 칠했다. 배 주위에 {HullEffects[effect - 1].Name} 빛이 일렁인다.");
            Cues.Enqueue("Part");
        }
        else if (Data.Papers.Find(p => p.Id == item) is { } ticket)
        {
            // 선박 교환권 — 항구에서 쓰면 그 배가 부두에 들어온다
            if (!ticket.Name.Contains("교환권") || TicketShip(ticket) is not { } given) { Say($"{ticket.Name} — 지금은 쓸 데가 없다."); return; }
            if (Mode != Mode.Port) { Say("선박 교환권은 항구에서 쓴다."); return; }
            if (Dock.Count >= DockSlots) { Say(Text(6831, "부두가 가득 찼다.").TrimStart('※')); return; }
            // 교환권으로 받은 배에는 그 배에 붙일 수 있는 옵션 스킬 가운데 하나가 무작위로 붙어 있다
            // (배 상세를 모은 배는 거기 적힌 스킬 가운데, 못 모은 배는 옵션 스킬 전체 가운데)
            // 원본처럼 강화를 한 번 한 상태(1/6)로 나온다(사용자가 준 글, 2026-10-09) — 그 한 번으로 무엇이 올랐는지는 몰라 횟수만 쓴 것으로 한다(짐작). 성능 초기화로 되돌린다
            var fitted = new ShipWork { Times = 1 };
            var allowed = Data.ShipDetail(given.Name) is { Skills.Count: > 0 } detail
                ? Data.OptionSkills.Where(o => detail.Skills.Exists(s => s.Name == o.Name)).ToList() : Data.OptionSkills.Where(o => o.SkillId is not (>= 2900 and <= 2904)).ToList();      // 「개조」는 그레이드 보너스로만 붙는 스킬이라 뺀다
            var bonus = allowed.Count > 0 ? allowed[_random.Next(allowed.Count)] : null;
            if (bonus != null) fitted.Skills.Add(bonus.SkillId);
            Dock.Add(new DockedShip { Ship = given, Durability = ShipStats.Of(given, Data.Settings.Ships).Durability, Work = fitted, Material = NativeMaterial(given) });
            Say($"{ticket.Name}을(를) {given.Name}(으)로 바꿔 부두에 매어 두었다. 선박교환에서 갈아탄다. 강화가 한 번 된 상태다." + (bonus == null ? "" : $" 옵션 스킬 「{bonus.Name}」이(가) 붙어 있다."));
        }
        else if (item >= JobPaper && Data.Jobs.Find(j => j.Id == item - JobPaper) is { } job)
        {
            if (job.Id == JobId) { Say($"이미 {job.Name}이다."); return; }
            string before = JobName;
            JobId = job.Id; Studied("Job");
            Say($"{ItemName(item)}을(를) 썼다. {before}에서 {job.Name}(으)로 전직했다!");
            Cues.Enqueue("JobChange");          // 효과음 0:9(사용자, 2026-10-07)
        }
        else if (BoosterOf(item) is { } booster)
        {
            if (!UseBooster(booster)) { Say($"{booster.Name} — 지금은 쓸 데가 없다."); return; }
        }
        else if (FoodOf(item) is { } food)
        {
            // 행동력 음식 — 설명의 「행동력+n」만큼 행동력이 찬다. 「피로 회복」이 붙은 것은 선원의 피로도 푼다 — 푸는 양은 gvdb 의 아이템 설명(「疲労度：-12」)의 값,
            // 거기 없는 음식만 행동력의 1/5(gvdb 의 73가지에서 가장 흔한 비 — 지은 값. 전에는 절반이었다).
            // 「괴혈병 회복」이 붙은 것은 괴혈병도 가라앉힌다.
            int vigour = FoodVigour(food);
            string text = food.Description.Replace(" ", "");
            bool rests = text.Contains("피로회복");
            var scurvy = text.Contains("괴혈병") ? Disasters.Find(d => d.Data.Name.Contains("괴혈병")) : null;
            // 영양부족은 행동력을 채우는 음식을 먹으면 낫는다(사용자, 2026-10-09 — 원본에서 그렇다)
            var hungry = Disasters.Find(d => d.Data.Name.Contains("영양부족"));
            if (Vigour >= MaxVigour && (!rests || Fatigue <= 0) && scurvy == null && hungry == null) { Say($"{food.Name} — 지금은 먹을 까닭이 없다."); Cues.Enqueue("Error"); return; }
            GainVigour(vigour * (1 + Study("FoodGain")));
            double relief = Data.FoodEffects.TryGetValue(item, out var effect) && effect.Length > 1 && effect[1] > 0 ? effect[1] : rests ? vigour * 0.2 : 0;
            rests |= relief > 0;
            if (rests) Fatigue = Math.Max(0, Fatigue - relief);
            if (scurvy != null) End(scurvy);
            if (hungry != null) End(hungry);
            Say($"{food.Name}을(를) 먹었다. 행동력 +{vigour} ({Vigour:0}/{MaxVigour})" + (rests ? $" · 피로 −{relief:0}" : "") + (scurvy != null ? " · 괴혈병이 가라앉았다" : "") + (hungry != null ? " · 영양부족이 나았다" : "") + ".");
            Cues.Enqueue("Eat");
        }
        else return;
        if (--Items[item] <= 0) Items.Remove(item);
    }
}
