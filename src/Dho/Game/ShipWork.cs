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
    /// <summary>그레이드(0 ~ 8) · 그레이드 경험치(0 ~ 100) · 조타 숙련도 · 그레이드 보너스(<see cref="Voyage.GradeBonuses"/> 의 차례).</summary>
    public int Grade { get; set; }
    public int GradeExp { get; set; }
    public double Mastery { get; set; }
    public List<int> Bonuses { get; } = [];
    /// <summary>전용함 스킬(옵션 스킬 번호) — 배 한 척에 하나만. 없으면 0.</summary>
    public int Dedicated { get; set; }

    // 스킬 번호 뒤에 그레이드 쪽 값을 큰 수로 덧붙여 적는다(옛 저장과 맞게): 1e6 + 그레이드, 2e6 + 경험치, 3e6 + 숙련도, 4e6 + 보너스, 5e6 + 전용함 스킬
    public double[] ToArray() => [Times, Durability, Sail, Turn, Wave, Hold, .. Skills.Select(s => (double)s),
                                  1_000_000 + Grade, 2_000_000 + GradeExp, 3_000_000 + Math.Round(Mastery), .. Bonuses.Select(b => 4_000_000.0 + b),
                                  .. Dedicated > 0 ? [5_000_000.0 + Dedicated] : Array.Empty<double>()];

    public static ShipWork From(double[] saved)
    {
        var work = new ShipWork();
        if (saved.Length < 6) return work;
        (work.Times, work.Durability, work.Sail, work.Turn, work.Wave, work.Hold) = ((int)saved[0], saved[1], saved[2], saved[3], saved[4], saved[5]);
        foreach (double value in saved.Skip(6))
        {
            int kind = (int)(value / 1_000_000), rest = (int)(value % 1_000_000);
            if (kind == 0) work.Skills.Add(rest);
            else if (kind == 1) work.Grade = rest;
            else if (kind == 2) work.GradeExp = rest;
            else if (kind == 3) work.Mastery = rest;
            else if (kind == 4) work.Bonuses.Add(rest);
            else if (kind == 5) work.Dedicated = rest;
        }
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
    public ShipStats Worked(ShipStats stats, ShipWork work, ShipData ship)
    {
        if (work.Times == 0 && work.Skills.Count == 0 && work.Bonuses.Count == 0 && work.Dedicated == 0) return stats;
        double sails = Math.Max(1, stats.VerticalSail + stats.HorizontalSail);
        double holdBonus = OptionAmount(work, "Hold");
        // 강화분은 조타 숙련도가 찬 만큼 듣는다(절반은 늘 듣는다). 그레이드 보너스의 강화는 그대로 더한다
        double applied = WorkShare(ship, work);
        bool Has(string bonus) => work.Bonuses.Contains(Array.IndexOf(GradeBonuses, bonus));
        double sail = work.Sail * applied, vertical = sail + (Has("세로돛강화") ? 12 : 0), horizontal = sail + (Has("가로돛강화") ? 12 : 0);
        double turn = work.Turn * applied + (Has("선회성능강화") ? 2 : 0);
        return stats with
        {
            Durability = (int)Math.Round(stats.Durability + work.Durability * applied + (Has("내구력강화") ? stats.Durability * 0.08 : 0)),
            VerticalSail = (int)Math.Round(stats.VerticalSail + vertical),
            HorizontalSail = (int)Math.Round(stats.HorizontalSail + horizontal),
            Knots = stats.Knots * (1 + (vertical + horizontal) / sails * 0.5) * (Has("가속강화") ? 1.05 : 1),
            Turn = (int)Math.Round(stats.Turn + turn),
            TurnFactor = stats.TurnFactor * (1 + turn / Math.Max(1.0, stats.Turn)),
            WaveResist = (int)Math.Round(stats.WaveResist + work.Wave * applied + (Has("내파성강화") ? 2 : 0)),
            Armor = stats.Armor + (Has("장갑강화") ? 3 : 0),
            Hold = (int)Math.Round((stats.Hold + work.Hold * applied + (Has("창고용량강화") ? stats.Hold * 0.08 : 0)) * (1 + holdBonus)),
        };
    }

    // ── 그레이드 · 조타 숙련도 · 선박 조합 ────────────────────────────────────
    // 틀은 원본의 것이다(클라이언트 글 40501 ~ 과 이용자 풀이 글): 그레이드 0 ~ 8, 본배에 제물배를 먹여 올리고 제물배는 사라진다,
    // 실패하면 그레이드 경험치가 쌓여 다음 성공률이 오른다, 4 부터는 대실패(한 단계 강등), 1 · 3 · 6 에서 그레이드 보너스가 하나 붙는다,
    // 그레이드가 오르면 강화는 초기화된다, 그레이드마다 조타 숙련도 상한 +5, 숙련도가 차야 강화가 다 듣는다.
    // 성공률은 풀이 글의 세 점(0+0 38% · 0+1 49% · 0+2 59%, 같은 배면 +10%)에 맞춘 식이고, 값 · 경험치 · 보너스의 크기는 지은 것이다.

    public const int MaxGrade = 8;
    public static readonly string[] GradeBonuses =
        ["스킬추가", "가속강화", "스킬계승", "내구력강화", "세로돛강화", "가로돛강화", "선회성능강화", "내파성강화", "장갑강화", "선실적재량강화", "포실적재량강화", "창고용량강화"];

    /// <summary>
    /// 조타 숙련도의 상한 — 이용자 가이드(인벤 「조타 숙련도 습득 가이드」)대로 배 크기에 따라 소형 80 · 중형 120 · 대형 200(소형 40 · 대형 160 인 배도 있다는데 어느 배인지 몰라 뺐다),
    /// 그레이드마다 +5.
    /// </summary>
    public static int MasteryCap(ShipData ship, ShipWork work) => (ship.SizeClass switch { <= 1 => 80, 2 => 120, _ => 200 }) + 5 * work.Grade;
    /// <summary>강화가 듣는 몫(0.5 ~ 1) — 조타 숙련도가 찬 만큼.</summary>
    public static double WorkShare(ShipData ship, ShipWork work) => 0.5 + 0.5 * Math.Clamp(work.Mastery / MasteryCap(ship, work), 0, 1);
    public int SkillSlotsOf(ShipWork work) => Data.ShipWorks.SkillSlots + work.Bonuses.Count(b => b is 0 or 2);

    private bool _masteryHalf;

    /// <summary>
    /// 타고 있는 배의 조타 숙련도가 오른다 — 가이드대로 입항할 때 1(거리와 상관없다), 생산할 때 1. 100 부터는 두 번에 한 번만 오른다.
    /// 전투 · 장비 강화로 오르는 것은 없다.
    /// </summary>
    private void GainMastery()
    {
        if (Work.Mastery >= 100 && (_masteryHalf = !_masteryHalf)) return;
        AddMastery(1, quiet: true);
    }

    /// <summary>조타 숙련도를 올린다(도시 메뉴의 단추도 이것을 쓴다).</summary>
    public void AddMastery(int amount, bool quiet = false)
    {
        int cap = MasteryCap(Ship, Work);
        if (Work.Mastery >= cap) { if (!quiet) Say("조타 숙련도가 이미 가득 찼다."); return; }
        Work.Mastery = Math.Min(cap, Work.Mastery + amount);
        Stats = Worked(StatsOf(Ship, ShipMaterialId, ShipLoad), Work, Ship);
        if (Work.Mastery >= cap) Say($"{Ship.Name}의 조타 숙련도가 가득 찼다. 강화가 모두 듣는다.");
        else if (!quiet) Say($"조타 숙련도 +{amount} ({Work.Mastery:0}/{cap})");
    }

    public int CombineChance(DockedShip main, DockedShip material) =>
        Math.Clamp(38 - 9 * main.Work.Grade + 10 * material.Work.Grade + (main.Ship.Id == material.Ship.Id ? 10 : 0) + main.Work.GradeExp / 4, 3, 100);

    public int CombineCost(DockedShip main) => 50_000 * (main.Work.Grade + 1) * Math.Max(1, main.Ship.SizeClass);

    public string? CombineBlocker(DockedShip main, DockedShip material)
    {
        if (main == material) return "같은 배다";
        if (main.Work.Grade >= MaxGrade) return "그레이드가 최대치다";
        if (Math.Max(1, main.Ship.SizeClass) != Math.Max(1, material.Ship.SizeClass)) return "크기가 같은 배만 재료가 된다";
        if (Money < CombineCost(main)) return "돈이 모자라다";
        return null;
    }

    /// <summary>선박 조합 — 재료 선박은 사라진다.</summary>
    public void Combine(DockedShip main, DockedShip material)
    {
        if (Mode != Mode.Port || !Dock.Contains(main) || !Dock.Contains(material) || CombineBlocker(main, material) != null) return;
        int chance = CombineChance(main, material);
        Money -= CombineCost(main);
        Dock.Remove(material);
        var work = main.Work;
        if (_random.Next(100) >= chance)
        {
            if (work.Grade >= 4 && _random.Next(100) < 15)
            {
                work.Grade--;
                Say($"선박 조합 대실패! {main.Ship.Name}의 그레이드가 {work.Grade}(으)로 내려갔다.");
                return;
            }
            work.GradeExp = Math.Min(100, work.GradeExp + 12 + 6 * material.Work.Grade);
            Say($"선박 조합에 실패했다. {main.Ship.Name}의 그레이드 경험치가 올랐다. ({work.GradeExp}/100)");
            return;
        }
        work.Grade++;
        work.GradeExp = 0;
        // 그레이드가 오르면 특수 조선의 강화는 초기화된다(옵션 스킬은 남는다)
        (work.Times, work.Durability, work.Sail, work.Turn, work.Wave, work.Hold) = (0, 0, 0, 0, 0, 0);
        Say($"{main.Ship.Name}의 그레이드가 상승했습니다! (그레이드 {work.Grade})");
        if (work.Grade is 1 or 3 or 6)
        {
            int bonus = _random.Next(GradeBonuses.Length);
            if (GradeBonuses[bonus] == "스킬계승" && material.Work.Skills.Count == 0) bonus = 0;
            work.Bonuses.Add(bonus);
            Say($"그레이드 보너스 「{GradeBonuses[bonus]}」(이)가 부여됐습니다!");
            if (GradeBonuses[bonus] == "스킬계승")
            {
                int inherited = material.Work.Skills[_random.Next(material.Work.Skills.Count)];
                if (!work.Skills.Contains(inherited)) work.Skills.Add(inherited);
                Say($"재료 선박의 옵션 스킬 「{OptionName(inherited)}」을(를) 이어받았다.");
            }
        }
    }

    private double OptionAmount(ShipWork work, string effect) =>
        Data.OptionSkills.Where(s => s.Effect == effect && (work.Skills.Contains(s.SkillId) || work.Dedicated == s.SkillId) && OptionValid(s)).Sum(s => s.Amount);

    /// <summary>타고 있는 배의 옵션 스킬 효과의 합.</summary>
    public double Option(string effect) => OptionAmount(Work, effect);

    /// <summary>
    /// 전용함 스킬로 붙일 수 있는 것들 — 선박 스킬 가운데 관리기술을 요구하는 것(자료에 「전용함 스킬」 표시가 없어 이렇게 가른다).
    /// 배의 크기에 따른 제약은 자료가 없어 못 따진다.
    /// </summary>
    public List<OptionSkill> DedicatedSkills() =>
        Data.OptionSkills.Where(s => Data.ShipSkillFacts.Find(f => f.Name == s.Name) is { } fact && fact.Needs.Contains("관리기술")).ToList();

    /// <summary>
    /// 선박 스킬의 유효조건 — 「관리기술 1, 병기기술 3」 같은 필요 스킬과 랭크(ssjoy 의 선박 스킬 표). 자료가 없는 스킬은 조건이 없다.
    /// 조건에 못 미쳐도 배에 붙일 수는 있지만 효과가 듣지 않는다.
    /// </summary>
    public List<(string Skill, int Rank)> OptionNeeds(OptionSkill skill)
    {
        var needs = new List<(string, int)>();
        foreach (string part in (Data.ShipSkillFacts.Find(f => f.Name == skill.Name)?.Needs ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (part.LastIndexOf(' ') is > 0 and var cut && int.TryParse(part[(cut + 1)..], out int rank)) needs.Add((part[..cut], rank));
        return needs;
    }

    /// <summary>내 스킬이 그 선박 스킬의 유효조건을 채우는가.</summary>
    public bool OptionValid(OptionSkill skill) =>
        OptionNeeds(skill).All(need => Data.Skills.Find(s => s.Name == need.Skill) is not { } mine || Rank(mine.Id) >= need.Rank);

    /// <summary>유효조건을 한 줄로 — 「관리기술 1 · 병기기술 3 (미달)」. 조건이 없으면 빈 글.</summary>
    public string OptionNeedLine(OptionSkill skill) =>
        OptionNeeds(skill) is { Count: > 0 } needs ? string.Join(" · ", needs.Select(n => $"{n.Skill} {n.Rank}")) + (OptionValid(skill) ? "" : " (미달)") : "";

    /// <summary>전용함 스킬의 유효조건 — 요구하는 관리기술 랭크(없으면 0).</summary>
    public int DedicatedNeed(OptionSkill skill)
    {
        string needs = Data.ShipSkillFacts.Find(f => f.Name == skill.Name)?.Needs ?? "";
        foreach (string part in needs.Split(','))
            if (part.Contains("관리기술") && int.TryParse(part.Trim().Split(' ')[^1], out int rank)) return rank;
        return 0;
    }

    /// <summary>내 관리기술 랭크.</summary>
    public int ManagementRank => Data.Skills.Find(s => s.Name == "관리기술") is { } skill ? Rank(skill.Id) : 0;

    /// <summary>전용함 스킬 하나에 드는 전용함 건조 허가증 — 중형 2장(소형 1 · 대형 3 은 짐작).</summary>
    public static int PermitsFor(ShipData ship) => ship.SizeClass switch { <= 1 => 1, 2 => 2, _ => 3 };

    public string? DedicatedBlocker(OptionSkill skill)
    {
        if (Work.Dedicated == skill.SkillId) return "이미 이 전용함 스킬이 붙어 있다";
        int have = Items.GetValueOrDefault(ShipPermit), need = PermitsFor(Ship);
        return have < need ? $"전용함 건조 허가증 {need}장이 있어야 한다 (가진 수 {have})" : null;
    }

    /// <summary>전용함 스킬을 붙인다 — 한 척에 하나라서 이미 있으면 바꿔 단다.</summary>
    public void GiveDedicated(OptionSkill skill)
    {
        if (DedicatedBlocker(skill) != null) return;
        int need = PermitsFor(Ship);
        if ((Items[ShipPermit] -= need) <= 0) Items.Remove(ShipPermit);
        string was = Work.Dedicated > 0 ? OptionName(Work.Dedicated) : "";
        Work.Dedicated = skill.SkillId;
        Say(was == "" ? $"{Ship.Name}에 전용함 스킬 「{skill.Name}」을(를) 붙였다. (허가증 {need}장)" : $"{Ship.Name}의 전용함 스킬을 「{was}」에서 「{skill.Name}」(으)로 바꿨다. (허가증 {need}장)");
        if (!OptionValid(skill)) Say($"전용함 스킬의 유효조건을 만족하지 않습니다. ({OptionNeedLine(skill)})");
    }

    public string OptionName(int skillId) => Data.OptionSkills.Find(s => s.SkillId == skillId)?.Name ?? SkillName(skillId);

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
    /// <summary>이 배에 붙일 수 있는 옵션 스킬인가 — 배 상세(ssjoy)를 모은 배는 거기 적힌 스킬만, 못 모은 배는 무엇이든.</summary>
    public bool ShipAllows(OptionSkill skill) => Data.ShipDetail(Ship.Name) is not { Skills.Count: > 0 } detail || detail.Skills.Exists(s => s.Name == skill.Name);

    public OptionSkill? OptionFrom(IReadOnlyCollection<int> parts)
    {
        if (Work.Skills.Count >= SkillSlotsOf(Work)) return null;
        return Data.OptionSkills.Find(s => parts.Contains(s.PartA) && parts.Contains(s.PartB) && !Work.Skills.Contains(s.SkillId) && ShipAllows(s));
    }

    /// <summary>강화 부품과 이름이 같은 조빌 아이템(아이템 표의 조선 부품)의 번호 — 없으면 0.</summary>
    public int PartItem(int part) =>
        Data.ShipWorks.Parts.Find(p => p.Id == part) is { } known ? Data.Papers.Find(p => p.Name == known.Name && p.Id is >= ShipItems and < ShipItems + 100_000)?.Id ?? 0 : 0;

    /// <summary>그 부품을 조빌 아이템으로 가지고 있는가 — 가진 것은 값을 안 치르고 그 아이템이 든다.</summary>
    public bool OwnsPart(int part) => PartItem(part) is > 0 and var item && Items.GetValueOrDefault(item) > 0;

    public int WorkCost(IEnumerable<int> parts) => parts.Where(id => !OwnsPart(id)).Sum(id => Data.ShipWorks.Parts.Find(p => p.Id == id)?.Price ?? 0);

    /// <summary>
    /// 조빌 아이템 「○○ 선박재료」가 가리키는 재질 — 이름에서 「선박재료」를 떼고 재질 표에서 찾는다
    /// (삼나무 → 삼나무판, 느릅나무 → 엘름, 동판 → 동, 철판 → 철, 로즈우드 → 자단). 못 찾으면 null.
    /// </summary>
    public ShipMaterial? WoodOf(int item)
    {
        if (Data.Papers.Find(p => p.Id == item) is not { } paper || !paper.Name.EndsWith("선박재료")) return null;
        string name = paper.Name[..^4].Trim();
        name = name switch { "삼나무" => "삼나무판", "느릅나무" => "엘름(느릅나무)", "동판" => "동", "철판" => "철", "로즈우드" => "자단", _ => name };
        return Data.ShipMaterials.Find(m => m.Name == name);
    }

    /// <summary>가진 선박재료 아이템들 — 강화에 재료로 넣으면 배의 재질이 그것으로 바뀐다.</summary>
    public List<(int Item, ShipMaterial Material)> WoodsOwned() =>
        Items.Keys.Where(id => id is >= ShipItems and < ShipItems + 100_000).Select(id => (Item: id, Material: WoodOf(id))).Where(w => w.Material != null).Select(w => (w.Item, w.Material!)).ToList();

    public string? WorkBlocker(IReadOnlyCollection<int> parts, int wood = 0)
    {
        if (ShipbuildingRank <= 0) return "조선 스킬이 없다";
        if (Work.Times >= Data.ShipWorks.MaxTimes) return "더는 강화할 수 없다";
        if (parts.Count + (wood > 0 ? 1 : 0) < 2) return "재료를 둘 이상 고른다";
        if (parts.Count + (wood > 0 ? 1 : 0) > 4) return "재료는 넷까지";
        if (wood > 0 && (WoodOf(wood) == null || Items.GetValueOrDefault(wood) <= 0)) return "그 선박재료가 없다";
        if (Money < WorkCost(parts)) return "돈이 모자라다";
        return null;
    }

    /// <summary>강화한다 — 부품마다 정해진 능력치가 오르고(상한까지), 조합이 맞으면 옵션 스킬이 붙는다.</summary>
    /// <summary>성능초기화 — 타고 있는 배의 강화치를 모두 0 으로(재질은 남는다). 되돌릴 수 없다.</summary>
    public void ResetWork()
    {
        if (Mode != Mode.Port || Work.Times == 0 || Items.GetValueOrDefault(DismantleBook) <= 0) return;
        if (--Items[DismantleBook] <= 0) Items.Remove(DismantleBook);
        // 지워지는 것은 강화치와 옵션 스킬뿐 — 재질 · 그레이드와 그 보너스 · 전용함 스킬 · 조타 숙련도는 남는다(원본의 안내 글 6843)
        var kept = Work;
        Work = new ShipWork { Dedicated = kept.Dedicated, Grade = kept.Grade, GradeExp = kept.GradeExp, Mastery = kept.Mastery };
        Work.Bonuses.AddRange(kept.Bonuses);
        Stats = Worked(StatsOf(Ship, ShipMaterialId, ShipLoad), Work, Ship);
        Durability = Math.Min(Durability, Stats.Durability);
        Crew = Math.Min(Crew, Stats.MaxCrew);
        Say($"특수조선 해체 기법서를 써서 {Ship.Name}의 성능을 초기화했다. 강화치와 옵션 스킬이 지워졌다(재질 · 그레이드는 그대로).");
    }

    public void Strengthen(IReadOnlyCollection<int> parts, int wood = 0)
    {
        if (Mode != Mode.Port || WorkBlocker(parts, wood) != null) return;
        var gained = new List<string>();
        // 선박재료를 넣었으면 배의 재질이 그것으로 바뀐다(국재질 넣기)
        if (wood > 0 && WoodOf(wood) is { } timber)
        {
            if (--Items[wood] <= 0) Items.Remove(wood);
            ShipMaterialId = timber.Id;
            gained.Add($"재질이 {timber.Name}(으)로 바뀌었다.");
        }
        var plain = StatsOf(Ship, ShipMaterialId, ShipLoad);
        Money -= WorkCost(parts);
        foreach (int owned in parts.Where(OwnsPart).ToList())
            if (--Items[PartItem(owned)] <= 0) Items.Remove(PartItem(owned));
        Studied("Build");
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
            gained.Add($"옵션 스킬 「{option.Name}」이(가) 붙었다!" + (OptionValid(option) ? "" : $" 유효조건을 만족하지 않아 효과는 듣지 않는다({OptionNeedLine(option)})."));
        }
        Work.Times++;
        double worn = Stats.Durability - Durability;
        Stats = Worked(plain, Work, Ship);
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
