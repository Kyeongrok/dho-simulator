using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 조선 재료(조빌 아이템)로 하는 보통 강화 — 원본의 강화 창(사용자의 원본 화면, 2026-10-08)대로:
/// 왼쪽 「소유 재료」에서 가진 재료를 골라 오른쪽 네 칸에 넣는다. 칸은 주요 돛 하나 · 포문 하나 · 장비 둘
/// (화면에서 대 라틴세일이 첫 칸, 정박용망 둘이 셋째 · 넷째 칸에 들어갔다 — 칸의 갈래는 그것으로 읽은 것).
/// 재료마다 능력치가 「낮은 값 ~ 높은 값」으로 오르고(세로돛 +40 ~ +50, 가로돛 −10 ~ 0), 넣은 것을 더한 범위가 「강화 성능」에 뜬다.
/// 실제로 얼마가 붙는지는 그 범위 안에서 정해진다(고르게 뽑는다 — 원본의 치우침은 모른다). 재료의 값은 gvdb 의 것이다.
/// 아직 없는 것: 건조일수 · 비용 · 「특수조선 강화 허가증」(원본 화면에는 45일 · 5,989,200 Ð · 3장) — 식을 몰라 받지 않는다.
/// </summary>
internal sealed partial class Voyage
{
    public static readonly string[] BuildKinds = ["주요 돛", "포문", "선체", "장비"];
    // 강화 창의 칸 넷 — 칸마다 들어가는 재료의 갈래(BuildPart.Kind)
    public static readonly int[] BuildSlots = [0, 1, 3, 3];

    /// <summary>능력치 이름 → 화면 글(원본 강화 창의 것) · 아이콘(화면 부품 묶음 0 의 번호).</summary>
    public static readonly (string Stat, string Label, string Short, int Icon)[] BuildStats =
    [
        ("Durability", "내구력 강화", "내구력", 310), ("Vertical", "세로돛성능 강화", "세로돛성능", 306), ("Horizontal", "가로돛성능 강화", "가로돛성능", 307),
        ("Rowing", "조력 강화", "조력", 314), ("Turn", "선회속도 강화", "선회 성능", 308), ("Wave", "내파성 강화", "내파성", 309),
        ("Armor", "장갑 강화", "장갑치", 333), ("Cabin", "선실 강화", "선실적재량", 330), ("Guns", "포실 강화", "포실적재량", 305), ("Hold", "창고 강화", "창고 용량", 323),
    ];

    /// <summary>
    /// 최대적재량 — 선실 · 포실 · 창고의 합(원본 화면: 67 + 48 + 885 = 「최대적재량 1000」). 적재 변경 load(%)를 입힌 값.
    /// 원본의 「가능 범위 750 ~ 1250」은 본디 값의 ±25%, 「적정 범위 800 ~ 1209」는 ±20% 쯤 — 이 게임의 적재 변경(±25%, 20% 를 넘으면 돛 · 내파가 깎인다)과 같은 틀이다.
    /// </summary>
    public int LoadTotal(int load)
    {
        var stats = StatsOf(Ship, ShipMaterialId, load);
        return stats.MaxCrew + stats.Guns + stats.Hold;
    }

    /// <summary>조선 재료의 한 줄 풀이 — 「[주요 돛 · 대형] 세로돛성능 강화 +40~+50 · 가로돛성능 강화 -10~0」. 재료가 아니면 빈 글.</summary>
    public string BuildPartLine(int item)
    {
        if (BuildPartOf(item) is not { } part) return "";
        string[] sizes = ["소형", "중형", "대형"];
        var stats = BuildStats.Where(s => part.Stats.TryGetValue(s.Stat, out var r) && r.Length >= 2).Select(s => $"{s.Label} {part.Stats[s.Stat][0]:+0;-0;0}~{part.Stats[s.Stat][1]:+0;-0;0}");
        return $"[{BuildKinds[Math.Clamp(part.Kind, 0, 3)]} · {(part.Sizes.Count == 0 ? "전부" : string.Join("/", part.Sizes.Select(s => sizes[Math.Clamp(s, 0, 2)])))}{(part.Cash ? " · 캐시" : "")}] " + string.Join(" · ", stats);
    }

    public BuildPart? BuildPartOf(int item) => Data.BuildParts.Find(p => p.Id == item);

    /// <summary>이 배에 쓸 수 있는 재료인가 — 배의 크기(소형 · 중형 · 대형)가 맞아야 한다. 선체는 신규 건조의 것이라 강화에는 안 넣는다.</summary>
    public bool BuildPartFits(BuildPart part) => part.Kind != 2 && (part.Sizes.Count == 0 || part.Sizes.Contains(SizeGroup(Ship)));

    /// <summary>가진 조선 재료들(강화에 쓸 수 있는 것) — (아이템, 재료, 가진 수).</summary>
    public List<(int Item, BuildPart Part, int Count)> BuildPartsOwned() =>
        Items.Where(i => i.Value > 0).Select(i => (Item: i.Key, Part: BuildPartOf(i.Key), Count: i.Value)).Where(i => i.Part != null && i.Part.Kind != 2 && i.Part.Stats.Count > 0)
             .OrderBy(i => i.Part!.Kind).ThenBy(i => i.Item).Select(i => (i.Item, i.Part!, i.Count)).ToList();

    /// <summary>넣은 재료들이 올릴 범위 — 능력치마다 (낮은 값, 높은 값)의 합.</summary>
    public Dictionary<string, (int Min, int Max)> BuildRanges(IEnumerable<int> items)
    {
        var sum = new Dictionary<string, (int Min, int Max)>();
        foreach (int item in items)
            if (BuildPartOf(item) is { } part)
                foreach (var (stat, range) in part.Stats)
                    if (range.Length >= 2) sum[stat] = (sum.GetValueOrDefault(stat).Min + range[0], sum.GetValueOrDefault(stat).Max + range[1]);
        return sum;
    }

    /// <summary>재료를 칸에 넣을 수 있는가 — 그 갈래의 빈 칸이 있어야 한다(넣을 칸의 차례, 없으면 -1).</summary>
    public int BuildSlotFor(IReadOnlyList<int> picked, int item)
    {
        if (BuildPartOf(item) is not { } part || !BuildPartFits(part)) return -1;
        int used = picked.Count(p => BuildPartOf(p)?.Kind == part.Kind), room = BuildSlots.Count(k => k == part.Kind);
        return used < room && Items.GetValueOrDefault(item) > picked.Count(p => p == item) ? used : -1;
    }

    // 재료 아이템 → 옵션 스킬 조합에 쓰는 옛 부품 번호(이름이 같은 것)
    private List<int> BuildPartsAsOld(IEnumerable<int> items) =>
        items.Select(i => Data.ShipWorks.Parts.Find(p => p.Name == BuildPartOf(i)?.Name)?.Id ?? 0).Where(id => id > 0).ToList();

    /// <summary>이 재료들로 붙을 옵션 스킬(조합이 맞고 칸이 있을 때).</summary>
    public OptionSkill? BuildOption(IEnumerable<int> items) => OptionFrom(BuildPartsAsOld(items));

    /// <summary>
    /// 초과 강화 — 강화 횟수를 다 쓴 뒤에도 끝없이 더 강화할 수 있고, 할수록 성공률만 낮아진다. 캐시 재료를 넣으면 꼭 된다(사용자, 2026-10-08).
    /// 성공률은 자료가 없어 지은 값: 60% 에서 초과 한 번마다 −15%p, 조선 랭크마다 +1%p, 모드의 「초과 강화 성공률」을 더한다(5% 아래로는 안 내려간다).
    /// 실패하면 넣은 재료만 사라지고 횟수는 안 준다(이것도 지은 것).
    /// </summary>
    public bool OverWork => Work.Times >= MaxTimesOf(Ship);
    /// <summary>초과 강화의 성공률(%) — 캐시 재료가 하나라도 들어가면 100.</summary>
    public int OverWorkChance(IEnumerable<int>? items = null) =>
        items != null && items.Any(i => BuildPartOf(i)?.Cash == true) ? 100
        : Math.Clamp(60 - 15 * Math.Max(0, Work.Times - MaxTimesOf(Ship)) + ShipbuildingRank + Data.Settings.ModOverWorkBonus, 5, 95);

    public string? BuildBlocker(IReadOnlyList<int> items, int wood = 0)
    {
        if (ShipbuildingRank <= 0) return "조선 스킬이 없다";
        if (items.Count + (wood > 0 ? 1 : 0) < 2) return "재료를 둘 이상 넣는다";      // 원본 글 49202 「강화에는 최소 2종류의 재료」
        if (items.Any(i => BuildPartOf(i) is not { } part || !BuildPartFits(part))) return "이 배에 못 쓰는 재료가 있다";
        if (items.GroupBy(i => i).Any(g => Items.GetValueOrDefault(g.Key) < g.Count())) return "재료가 모자라다";
        if (wood > 0 && (WoodOf(wood) == null || Items.GetValueOrDefault(wood) <= 0)) return "그 선박재료가 없다";
        return null;
    }

    /// <summary>재료를 넣어 강화한다 — 능력치마다 범위 안에서 붙고(조타 숙련도의 한계까지), 조합이 맞으면 옵션 스킬이 붙는다.</summary>
    public void StrengthenWith(IReadOnlyList<int> items, int wood = 0, int? load = null)
    {
        if (Mode != Mode.Port || BuildBlocker(items, wood) != null) return;
        var gained = new List<string>();
        if (OverWork && _random.Next(100) >= OverWorkChance(items))
        {
            // 초과 강화 실패 — 재료만 사라진다
            foreach (int item in items)
                if (--Items[item] <= 0) Items.Remove(item);
            Cues.Enqueue("Error");
            Say($"{Ship.Name}의 초과 강화에 실패했다. 넣은 재료를 잃었다. (강화 {Work.Times}회)");
            return;
        }
        // 강화의 둘째 화면(원본: 「최대적재량 변경」)에서 고친 적재 — 조선 랭크가 차야 바뀐다
        if (load is { } newLoad && newLoad != ShipLoad && ShipbuildingRank >= LoadRank)
        {
            ShipLoad = Math.Clamp(newLoad, -25, 25);
            gained.Add($"최대적재량 {LoadTotal(ShipLoad)}.");
        }
        if (wood > 0 && WoodOf(wood) is { } timber)
        {
            if (--Items[wood] <= 0) Items.Remove(wood);
            ShipMaterialId = timber.Id;
            gained.Add($"재질이 {timber.Name}(으)로 바뀌었다.");
        }
        var plain = StatsOf(Ship, ShipMaterialId, ShipLoad);
        var option = BuildOption(items);
        foreach (int item in items)
            if (--Items[item] <= 0) Items.Remove(item);
        Studied("Build");
        foreach (var (stat, range) in BuildRanges(items))
        {
            int roll = range.Min + _random.Next(range.Max - range.Min + 1);
            double limit = WorkLimit(stat, plain, Ship, Work);
            // 오르는 것은 지금 한계까지만(이미 넘겨 올려 둔 것은 안 깎는다), 내리는 것은 그대로 내린다
            double Add(double now) => roll >= 0 ? Math.Max(now, Math.Min(limit, now + roll)) : now + roll;
            double before = stat switch { "Durability" => Work.Durability, "Vertical" => Work.Vertical, "Horizontal" => Work.Horizontal, "Rowing" => Work.Rowing, "Turn" => Work.Turn,
                                          "Wave" => Work.Wave, "Armor" => Work.Armor, "Cabin" => Work.Cabin, "Guns" => Work.Guns, _ => Work.Hold };
            double after = Add(before);
            switch (stat)
            {
                case "Durability": Work.Durability = after; break;
                case "Vertical": Work.Vertical = after; break;
                case "Horizontal": Work.Horizontal = after; break;
                case "Rowing": Work.Rowing = after; break;
                case "Turn": Work.Turn = after; break;
                case "Wave": Work.Wave = after; break;
                case "Armor": Work.Armor = after; break;
                case "Cabin": Work.Cabin = after; break;
                case "Guns": Work.Guns = after; break;
                default: Work.Hold = after; break;
            }
            string label = Array.Find(BuildStats, s => s.Stat == stat).Short ?? stat;
            gained.Add($"{label} {after - before:+0;-0;0}" + (roll > 0 && after - before < roll ? "(한계)" : ""));
        }
        if (option != null)
        {
            Work.Skills.Add(option.SkillId);
            gained.Add($"옵션 스킬 「{option.Name}」이(가) 붙었다!" + (OptionValid(option) ? "" : $" 유효조건을 만족하지 않아 효과는 듣지 않는다({OptionNeedLine(option)})."));
        }
        Work.Times++;
        double worn = Stats.Durability - Durability;
        Stats = Worked(plain, Work, Ship);
        Durability = Math.Max(1, Stats.Durability - worn);
        TrainEffect("Shipbuilding", 60);
        Cues.Enqueue("Part");
        Say($"{Ship.Name}을(를) 강화했다. ({Work.Times}/{MaxTimesOf(Ship)}) " + string.Join(" · ", gained));
    }
}
