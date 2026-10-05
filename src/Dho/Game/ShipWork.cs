using Dho.Data;

namespace Dho.Game;

/// <summary>배 한 척에 쌓인 강화와 옵션 스킬.</summary>
internal sealed class ShipWork
{
    public int Times { get; set; }
    public double Durability { get; set; }
    public double Sail { get; set; }
    public double Turn { get; set; }
    public double Wave { get; set; }
    public double Hold { get; set; }
    public List<int> Skills { get; } = [];

    public double[] ToArray() => [Times, Durability, Sail, Turn, Wave, Hold, .. Skills.Select(s => (double)s)];

    public static ShipWork From(double[] saved)
    {
        var work = new ShipWork();
        if (saved.Length < 6) return work;
        (work.Times, work.Durability, work.Sail, work.Turn, work.Wave, work.Hold) = ((int)saved[0], saved[1], saved[2], saved[3], saved[4], saved[5]);
        work.Skills.AddRange(saved.Skip(6).Select(s => (int)s));
        return work;
    }
}

/// <summary>
/// 강화와 옵션 스킬 — 조선소에서 부품을 둘 이상 넣어 능력치를 올린다(원본의 「보통 강화」). 넣은 부품 가운데 둘이
/// 옵션 스킬의 조합과 맞고 칸이 남아 있으면 그 스킬이 붙는다. 틀(둘 이상의 재료 · 조합으로 스킬)과 이름은 원본의 것이고,
/// 오르는 양 · 상한 · 횟수 · 값 · 효과의 크기는 지은 것이다(<c>ship-works.json</c>).
/// </summary>
internal sealed partial class Voyage
{
    public ShipWork Work { get; private set; } = new();

    /// <summary>능력치마다 강화로 올릴 수 있는 상한 — 그 배의 원래 값에 견준 것.</summary>
    private static double WorkCap(string stat, ShipStats plain) => stat switch
    {
        "Durability" => plain.Durability * 0.3,
        "Sail" => 40,
        "Turn" => 5,
        "Wave" => 5,
        "Hold" => plain.Hold * 0.1,
        _ => 0,
    };

    /// <summary>강화와 옵션 스킬을 입힌 능력치.</summary>
    public ShipStats Worked(ShipStats stats, ShipWork work)
    {
        if (work.Times == 0 && work.Skills.Count == 0) return stats;
        double sails = Math.Max(1, stats.VerticalSail + stats.HorizontalSail);
        double holdBonus = OptionAmount(work, "Hold");
        return stats with
        {
            Durability = (int)Math.Round(stats.Durability + work.Durability),
            VerticalSail = (int)Math.Round(stats.VerticalSail + work.Sail),
            HorizontalSail = (int)Math.Round(stats.HorizontalSail + work.Sail),
            Knots = stats.Knots * (1 + work.Sail * 2 / sails * 0.5),
            Turn = (int)Math.Round(stats.Turn + work.Turn),
            TurnFactor = stats.TurnFactor * (1 + work.Turn / Math.Max(1.0, stats.Turn)),
            WaveResist = (int)Math.Round(stats.WaveResist + work.Wave),
            Hold = (int)Math.Round((stats.Hold + work.Hold) * (1 + holdBonus)),
        };
    }

    private double OptionAmount(ShipWork work, string effect) =>
        Data.ShipWorks.Skills.Where(s => s.Effect == effect && work.Skills.Contains(s.SkillId)).Sum(s => s.Amount);

    /// <summary>타고 있는 배의 옵션 스킬 효과의 합.</summary>
    public double Option(string effect) => OptionAmount(Work, effect);

    public string OptionName(int skillId) => Data.ShipWorks.Skills.Find(s => s.SkillId == skillId)?.Name ?? SkillName(skillId);

    public static string OptionNote(OptionSkill skill) => skill.Effect switch
    {
        "Speed" => $"속도 +{skill.Amount * 100:0}%",
        "Storm" => $"폭풍 피해 −{skill.Amount * 100:0}%",
        "Turn" => $"선회 +{skill.Amount * 100:0}%",
        "Survey" => $"주변 지도 +{skill.Amount * 100:0}%",
        "CrewLoss" => $"선원 피해 −{skill.Amount * 100:0}%",
        "Luck" => $"재해 −{skill.Amount * 100:0}%",
        "Hold" => $"창고 +{skill.Amount * 100:0}%",
        "Flotsam" => $"하루에 한 번쯤 표류물({skill.Amount:0} 두캇 안팎)",
        _ => "",
    };

    /// <summary>이 부품들을 넣으면 붙을 옵션 스킬 — 조합이 맞고, 아직 없고, 칸이 남았을 때.</summary>
    public OptionSkill? OptionFrom(IReadOnlyCollection<int> parts)
    {
        if (Work.Skills.Count >= Data.ShipWorks.SkillSlots) return null;
        return Data.ShipWorks.Skills.Find(s => parts.Contains(s.PartA) && parts.Contains(s.PartB) && !Work.Skills.Contains(s.SkillId));
    }

    public int WorkCost(IEnumerable<int> parts) => parts.Sum(id => Data.ShipWorks.Parts.Find(p => p.Id == id)?.Price ?? 0);

    public string? WorkBlocker(IReadOnlyCollection<int> parts)
    {
        if (ShipbuildingRank <= 0) return "조선 스킬이 없다";
        if (Work.Times >= Data.ShipWorks.MaxTimes) return "더는 강화할 수 없다";
        if (parts.Count < 2) return "부품을 둘 이상 고른다";
        if (parts.Count > 4) return "부품은 넷까지";
        if (Money < WorkCost(parts)) return "돈이 모자라다";
        return null;
    }

    /// <summary>강화한다 — 부품마다 정해진 능력치가 오르고(상한까지), 조합이 맞으면 옵션 스킬이 붙는다.</summary>
    public void Strengthen(IReadOnlyCollection<int> parts)
    {
        if (Mode != Mode.Port || WorkBlocker(parts) != null) return;
        var plain = StatsOf(Ship, ShipMaterialId, ShipLoad);
        Money -= WorkCost(parts);
        var gained = new List<string>();
        foreach (int id in parts)
        {
            if (Data.ShipWorks.Parts.Find(p => p.Id == id) is not { } part) continue;
            double cap = WorkCap(part.Stat, plain);
            double Add(double now) => Math.Min(cap, now + part.Amount);
            switch (part.Stat)
            {
                case "Durability": Work.Durability = Add(Work.Durability); break;
                case "Sail": Work.Sail = Add(Work.Sail); break;
                case "Turn": Work.Turn = Add(Work.Turn); break;
                case "Wave": Work.Wave = Add(Work.Wave); break;
                case "Hold": Work.Hold = Add(Work.Hold); break;
            }
        }
        if (OptionFrom(parts) is { } option)
        {
            Work.Skills.Add(option.SkillId);
            gained.Add($"옵션 스킬 「{option.Name}」이(가) 붙었다!");
        }
        Work.Times++;
        double worn = Stats.Durability - Durability;
        Stats = Worked(plain, Work);
        Durability = Math.Max(1, Stats.Durability - worn);
        TrainEffect("Shipbuilding", 60);
        Say($"{Ship.Name}을(를) 강화했다. ({Work.Times}/{Data.ShipWorks.MaxTimes}) " + string.Join(" ", gained));
    }

    private double _flotsam;

    /// <summary>표류물 탐색 — 하루에 한 번쯤 떠다니는 것을 건진다.</summary>
    private void UpdateOptions(double days)
    {
        double find = Option("Flotsam");
        if (find <= 0) return;
        _flotsam += days;
        if (_flotsam < 1) return;
        _flotsam -= 1;
        if (_random.NextDouble() < 0.6)
        {
            int worth = (int)(find * (0.5 + _random.NextDouble()));
            Money += worth;
            Say($"표류물을 건졌다. ({worth:N0} 두캇)");
        }
    }
}
