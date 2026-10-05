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
                _ => "",
            };
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
