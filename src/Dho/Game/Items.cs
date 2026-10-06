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

    public string ItemName(int item) =>
        ItemOf(item) is { } known ? known.Name :
        Data.Papers.Find(p => p.Id == item) is { } paper ? paper.Name :
        item < 1_000_000 && Data.Gear.Find(g => g.Id == item) is { } gear ? gear.Name :
        item > MaterialItem && item < MaterialItem + 1000 && MaterialOf(item - MaterialItem) is { } wood ? wood.Name :
        item >= JobPaper && Data.Jobs.Find(j => j.Id == item - JobPaper) is { } job ? $"{job.Name} 전직증" : $"아이템 {item}";

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
        return _ticketShips[ticket.Id] = found;
    }

    public string ItemNote(int item)
    {
        if (item < 1_000_000 && Data.Gear.Find(g => g.Id == item) is { } gear) return gear.Description.Replace("\n", " ");
        if (Data.Papers.Find(p => p.Id == item) is { } paper)
            return paper.Description.Replace("\n", " ") + (paper.Name.Contains("교환권") && (paper.Name.Contains("선박") || TicketShip(paper) != null) ? (TicketShip(paper) is { } gives ? $"  → {gives.Name}" : "  (바꿀 배를 못 찾았다)") : "");
        if (ItemOf(item) is { } known)
            return known.Effect switch
            {
                "Fatigue" => $"선원의 피로를 {known.Amount:0} 푼다.",
                "Repair" => $"내구를 {known.Amount:0}% 고친다.",
                "Cure" => $"{Data.Disasters.Find(d => d.Id == (int)known.Amount)?.Name ?? "재해"}을(를) 가라앉힌다.",
                "Lifebuoy" => "난파할 때 저절로 쓰여 한 번 버틴다.",
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

    public void AddItem(int item, int count = 1)
    {
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
                case "Repair" when Durability < Stats.Durability:
                    Durability = Math.Min(Stats.Durability, Durability + Stats.Durability * known.Amount / 100);
                    Say($"{known.Name}(으)로 배를 고쳤다.");
                    break;
                case "SailPaint":
                    SailDye = (int)known.Amount;
                    Dialog = Dialog.Sail;              // 무늬와 색을 고르는 창 — 「확인」을 눌러야 도료가 든다
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
        else if (Data.Papers.Find(p => p.Id == item) is { } ticket)
        {
            // 선박 교환권 — 항구에서 쓰면 그 배가 부두에 들어온다
            if (!ticket.Name.Contains("교환권") || TicketShip(ticket) is not { } given) { Say($"{ticket.Name} — 지금은 쓸 데가 없다."); return; }
            if (Mode != Mode.Port) { Say("선박 교환권은 항구에서 쓴다."); return; }
            if (Dock.Count >= DockSlots) { Say("부두가 가득 찼다."); return; }
            // 교환권으로 받은 배에는 그 배에 붙일 수 있는 옵션 스킬 가운데 하나가 무작위로 붙어 있다
            // (배 상세를 모은 배는 거기 적힌 스킬 가운데, 못 모은 배는 옵션 스킬 전체 가운데)
            var fitted = new ShipWork();
            var allowed = Data.ShipDetail(given.Name) is { Skills.Count: > 0 } detail
                ? Data.OptionSkills.Where(o => detail.Skills.Exists(s => s.Name == o.Name)).ToList() : Data.OptionSkills;
            var bonus = allowed.Count > 0 ? allowed[_random.Next(allowed.Count)] : null;
            if (bonus != null) fitted.Skills.Add(bonus.SkillId);
            Dock.Add(new DockedShip { Ship = given, Durability = ShipStats.Of(given, Data.Settings.Ships).Durability, Work = fitted });
            Say($"{ticket.Name}을(를) {given.Name}(으)로 바꿔 부두에 매어 두었다. 선박교환에서 갈아탄다." + (bonus == null ? "" : $" 옵션 스킬 「{bonus.Name}」이(가) 붙어 있다."));
        }
        else if (item >= JobPaper && Data.Jobs.Find(j => j.Id == item - JobPaper) is { } job)
        {
            if (job.Id == JobId) { Say($"이미 {job.Name}이다."); return; }
            string before = JobName;
            JobId = job.Id;
            Say($"{ItemName(item)}을(를) 썼다. {before}에서 {job.Name}(으)로 전직했다!");
        }
        else return;
        if (--Items[item] <= 0) Items.Remove(item);
    }
}
