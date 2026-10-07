using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 바다에서 눌러 쓰는 스킬 — 조달(물)·낚시(식량)·수리(내구)·측량(위치)·주연(피로).
/// 이름과 설명은 클라이언트 스킬 표의 것이고, 얻는 양과 다시 쓸 때까지의 시간은 지은 값이다.
/// </summary>
internal sealed partial class Voyage
{
    /// <summary>수리에 쓰는 자재(보급품 「수리용 통」).</summary>
    private const int RepairSupply = 2;

    private static readonly string[] ActiveEffects = ["Survey", "Procure", "Fish", "Repair", "Rest", "Speed", "Turn"];

    /// <summary>
    /// 켜 두는 스킬 — 돛 조종 · 조타 · 낚시 · 조달. 원본처럼 켜면 한동안 켜져 있다가 꺼지고(화면 오른쪽 가운데에 그림이 뜬다),
    /// 켜져 있는 동안 돛 조종 · 조타는 효과가 걸리고 낚시 · 조달은 일정한 사이를 두고 저절로 된다.
    /// 켜져 있는 시간 · 사이 · 한꺼번에 켜는 수는 지은 값이다.
    /// </summary>
    private static readonly string[] Sustained = ["Speed", "Turn", "Fish", "Procure", "Survey"];
    public const int MaxSkillsOn = 3;
    private const double OnSeconds = 180, TickSeconds = 15;
    private readonly Dictionary<int, (double Until, double Next)> _skillOn = new();
    private static readonly string[] Fishes = ["고등어", "정어리", "전갱이", "청어", "대구", "도미", "청상아리", "가다랑어"];

    public bool SkillOn(int skillId) => _skillOn.ContainsKey(skillId);

    /// <summary>켜져 있는 스킬과 남은 시간의 몫(0 ~ 1) — 켠 차례대로.</summary>
    public List<(SkillRuleData Rule, double Left)> SkillsOn() =>
        _skillOn.Select(on => (Rule: Data.SkillRules.Find(r => r.SkillId == on.Key), Left: Math.Clamp((on.Value.Until - Clock) / OnSeconds, 0, 1)))
                .Where(on => on.Rule != null).Select(on => (on.Rule!, on.Left)).ToList();

    /// <summary>바다에서 프레임마다 — 시간이 다 된 스킬을 끄고, 낚시 · 조달은 때가 되면 한 번 한다.</summary>
    private void TickSkills()
    {
        foreach (var (id, on) in _skillOn.ToList())
        {
            var rule = Data.SkillRules.Find(r => r.SkillId == id);
            if (rule == null || Clock >= on.Until)
            {
                _skillOn.Remove(id);
                Say($"{SkillName(id)} 스킬의 효과가 끝났다.");
                continue;
            }
            if (Clock < on.Next) continue;
            _skillOn[id] = (on.Until, Clock + TickSeconds);
            if (rule.Effect is "Fish" or "Procure") Gather(rule);
        }
    }

    /// <summary>낚시 · 조달 한 번.</summary>
    private void Gather(SkillRuleData rule)
    {
        int rank = Rank(rule.SkillId);
        if (rule.Effect == "Fish")
        {
            // 배가 빠르면 낚싯줄을 드리우기 어렵다
            double food = Math.Min(Rules.MaxFood - Food, Math.Round((1 + _random.NextDouble() * 2 + rank * rule.PerRank) * (Knots > 8 ? 0.5 : 1)));
            if (food < 1) { Say("아무것도 낚지 못했다."); return; }
            Food += food;
            Say($"{Fishes[_random.Next(Fishes.Length)]}을(를) 낚아 올렸다. (식량 {food:0})");
            Train(rule.SkillId, 20);
        }
        else
        {
            // 원본 설명대로 비가 올 때 제대로 모인다
            bool rain = Weather is Weather.Rain or Weather.Storm;
            double water = Math.Min(Rules.MaxWater - Water, (rain ? 6 : 1) + rank * rule.PerRank * (rain ? 1 : 0.3));
            if (water <= 0) return;
            Water += water;
            Say(rain ? $"빗물을 받았다. (물 {water:0.#})" : $"해수를 걸러 물을 얻었다. (물 {water:0.#})");
            Train(rule.SkillId, rain ? 25 : 8);
        }
    }
    private readonly Dictionary<int, double> _skillReady = new();

    /// <summary>익힌 스킬 가운데 바다에서 눌러 쓰는 것.</summary>
    public IEnumerable<SkillRuleData> SeaSkills() =>
        Data.SkillRules.Where(r => ActiveEffects.Contains(r.Effect) && Rank(r.SkillId) > 0);

    /// <summary>다시 쓸 수 있을 때까지 남은 초.</summary>
    public double SkillWait(SkillRuleData rule) => Math.Max(0, _skillReady.GetValueOrDefault(rule.SkillId) - Clock);

    /// <summary>다시 쓸 때까지의 시간 가운데 남은 몫(0 ~ 1) — 단추를 덮는 데 쓴다.</summary>
    public double SkillWaitShare(SkillRuleData rule) => SkillWait(rule) / Pause(rule);

    private static double Pause(SkillRuleData rule) => rule.Effect switch { "Survey" => 5, "Repair" => 1, "Rest" => 10, _ => 20 };      // 수리는 기다림이 없다(사용자 확인, 2026-10-07) — 나눗셈 때문에 1초만 둔다

    /// <summary>지금 못 쓰는 까닭. 쓸 수 있으면 null.</summary>
    public string? SkillBlocker(SkillRuleData rule)
    {
        if (Mode != Mode.Sea) return "바다에서만 쓴다";
        if (Sustained.Contains(rule.Effect))
            return SkillOn(rule.SkillId) ? null : _skillOn.Count >= MaxSkillsOn ? $"스킬은 {MaxSkillsOn}개까지 켠다"
                : Vigour < VigourCost(rule) ? $"행동력이 모자란다 ({Vigour:0}/{VigourCost(rule)})" : null;
        if (SkillWait(rule) > 0) return $"{SkillWait(rule):0}초 뒤";
        if (Vigour < VigourCost(rule)) return $"행동력이 모자란다 ({Vigour:0}/{VigourCost(rule)})";
        return rule.Effect switch
        {
            "Procure" when Water >= Rules.MaxWater => "물통이 가득하다",
            "Fish" when Food >= Rules.MaxFood => "식량 창고가 가득하다",
            "Repair" when Durability >= Stats.Durability => "고칠 데가 없다",
            "Repair" when SupplyCount(RepairSupply) <= 0 => "수리용 통이 없다",
            "Rest" when Food < 5 => "식량이 모자란다",
            "Rest" when Fatigue <= 0 => "선원들이 지치지 않았다",
            _ => null,
        };
    }

    public void UseSkill(SkillRuleData rule)
    {
        if (SkillBlocker(rule) is { } blocker)
        {
            if (Rank(rule.SkillId) > 0) { Say($"{SkillName(rule.SkillId)} — {blocker}."); Cues.Enqueue("Error"); }
            return;
        }
        if (Rank(rule.SkillId) <= 0) return;
        int rank = Rank(rule.SkillId);
        string name = SkillName(rule.SkillId);
        if (Sustained.Contains(rule.Effect))
        {
            // 켜 두는 스킬: 다시 누르면 끈다
            if (_skillOn.Remove(rule.SkillId)) { Say($"{name} 스킬을 껐다."); return; }
            _skillOn[rule.SkillId] = (Clock + OnSeconds * BoostExtend, Clock + TickSeconds);
            SpendVigour(VigourCost(rule));
            Fatigue = Math.Min(100, Fatigue + 1);
            Say($"{name} 스킬을 사용했다.");
            Cues.Enqueue(rule.Effect == "Speed" ? "Sail" : "Skill");
            return;
        }
        _skillReady[rule.SkillId] = Clock + Pause(rule);
        SpendVigour(VigourCost(rule));
        Cues.Enqueue("Skill");
        switch (rule.Effect)
        {
            case "Survey":
                var near = Data.Cities.Where(c => c.SeaX != 0 || c.SeaY != 0)
                    .MinBy(c => Math.Pow(WorldMap.DeltaX(ShipX, c.SeaX), 2) + Math.Pow(c.SeaY - ShipY, 2));
                string where = "";
                if (near != null)
                {
                    double dx = WorldMap.DeltaX(ShipX, near.SeaX), dy = near.SeaY - ShipY;
                    string[] points = ["북", "북동", "동", "남동", "남", "남서", "서", "북서"];
                    int point = (int)Math.Round(Normalize(Math.Atan2(dx, -dy)) / (Math.PI / 4)) % 8;
                    where = $" 가장 가까운 항구는 {near.Name} — {points[point]}쪽 {Math.Sqrt(dx * dx + dy * dy):0}.";
                }
                Say($"{name}: {SeaName} ({ShipX:0}, {ShipY:0}).{where}");
                Train(rule.SkillId, 8);
                break;
            case "Procure":
                // 원본 설명대로 비가 올 때 제대로 모인다
                bool rain = Weather is Weather.Rain or Weather.Storm;
                double water = Math.Min(Rules.MaxWater - Water, (rain ? 6 : 1) + rank * rule.PerRank * (rain ? 1 : 0.3));
                Water += water;
                Fatigue = Math.Min(100, Fatigue + 2);
                Say(rain ? $"{name}: 빗물을 받아 물 {water:0.#} 을 얻었다." : $"{name}: 비가 오지 않아 이슬만 모았다. (물 {water:0.#})");
                Train(rule.SkillId, rain ? 25 : 8);
                break;
            case "Fish":
                // 배가 빠르면 낚싯줄을 드리우기 어렵다
                double catchRate = Knots > 8 ? 0.5 : 1;
                double food = Math.Min(Rules.MaxFood - Food, Math.Round((1 + _random.NextDouble() * 2 + rank * rule.PerRank) * catchRate));
                Food += food;
                Fatigue = Math.Min(100, Fatigue + 2);
                Say(food >= 1 ? $"{name}: 물고기를 낚았다. (식량 {food:0})" : $"{name}: 아무것도 낚지 못했다.");
                Train(rule.SkillId, 20);
                break;
            case "Repair":
                Supplies[RepairSupply] = SupplyCount(RepairSupply) - 1;
                double mend = Math.Min(Stats.Durability - Durability, Stats.Durability * (0.06 + rank * rule.PerRank) * (1 + Study("Repair")));
                Durability += mend;
                Fatigue = Math.Min(100, Fatigue + 4);
                Say($"{name}: 자재를 써서 배를 고쳤다. (내구 +{mend:0})");
                Train(rule.SkillId, 30);
                break;
            case "Rest":
                Feast();
                break;
        }
    }
}
