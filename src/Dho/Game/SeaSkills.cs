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

    private static readonly string[] ActiveEffects = ["Survey", "Procure", "Fish", "Repair", "Rest"];
    private readonly Dictionary<int, double> _skillReady = new();

    /// <summary>익힌 스킬 가운데 바다에서 눌러 쓰는 것.</summary>
    public IEnumerable<SkillRuleData> SeaSkills() =>
        Data.SkillRules.Where(r => ActiveEffects.Contains(r.Effect) && Rank(r.SkillId) > 0);

    /// <summary>다시 쓸 수 있을 때까지 남은 초.</summary>
    public double SkillWait(SkillRuleData rule) => Math.Max(0, _skillReady.GetValueOrDefault(rule.SkillId) - Clock);

    /// <summary>다시 쓸 때까지의 시간 가운데 남은 몫(0 ~ 1) — 단추를 덮는 데 쓴다.</summary>
    public double SkillWaitShare(SkillRuleData rule) => SkillWait(rule) / Pause(rule);

    private static double Pause(SkillRuleData rule) => rule.Effect switch { "Survey" => 5, "Repair" => 30, "Rest" => 10, _ => 20 };

    /// <summary>지금 못 쓰는 까닭. 쓸 수 있으면 null.</summary>
    public string? SkillBlocker(SkillRuleData rule)
    {
        if (Mode != Mode.Sea) return "바다에서만 쓴다";
        if (SkillWait(rule) > 0) return $"{SkillWait(rule):0}초 뒤";
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
            if (Rank(rule.SkillId) > 0) Say($"{SkillName(rule.SkillId)} — {blocker}.");
            return;
        }
        if (Rank(rule.SkillId) <= 0) return;
        int rank = Rank(rule.SkillId);
        string name = SkillName(rule.SkillId);
        _skillReady[rule.SkillId] = Clock + Pause(rule);
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
                double mend = Math.Min(Stats.Durability - Durability, Stats.Durability * (0.06 + rank * rule.PerRank));
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
