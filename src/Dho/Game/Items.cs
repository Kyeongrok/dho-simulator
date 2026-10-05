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
        item > MaterialItem && item < MaterialItem + 1000 && MaterialOf(item - MaterialItem) is { } wood ? wood.Name :
        item >= JobPaper && Data.Jobs.Find(j => j.Id == item - JobPaper) is { } job ? $"{job.Name} 전직증" : $"아이템 {item}";

    public string ItemNote(int item)
    {
        if (ItemOf(item) is { } known)
            return known.Effect switch
            {
                "Fatigue" => $"선원의 피로를 {known.Amount:0} 푼다.",
                "Repair" => $"내구를 {known.Amount:0}% 고친다.",
                "Cure" => $"{Data.Disasters.Find(d => d.Id == (int)known.Amount)?.Name ?? "재해"}을(를) 가라앉힌다.",
                "Lifebuoy" => "난파할 때 저절로 쓰여 한 번 버틴다.",
                "SailPaint" => "쓰면 돛의 무늬와 색을 고르는 창이 뜬다. 대장간 · 도구점에서 판다.",
                _ => "",
            };
        if (item > MaterialItem && item < MaterialItem + 1000 && MaterialOf(item - MaterialItem) is { } wood)
            return $"선박 재질 — 건조할 때 고른다. 내구 {wood.Durability * 100:0}% · 돛 {wood.Sail * 100:0}%";
        if (item < JobPaper || Data.Jobs.Find(j => j.Id == item - JobPaper) is not { } job) return "";
        string line = job.Group switch { 0 => "모험", 1 => "교역", 2 => "전투", _ => "" };
        return $"쓰면 {job.Name}(으)로 전직한다." + (line == "" ? "" : $" ({line} 계열)");
    }

    public void AddItem(int item, int count = 1)
    {
        Items[item] = Items.GetValueOrDefault(item) + count;
        Say($"{ItemName(item)}을(를) 얻었다.");
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
                    return;
            }
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
