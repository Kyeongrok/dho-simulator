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
    /// <summary>선박 형식(자료 표 89 의 번호 1 ~ 7) — 0 이면 그 배의 본디 형식. 조합으로 바뀐다.</summary>
    public int Form { get; set; }
    /// <summary>조합으로 스며든 형식의 횟수 — 형식 번호 → 성공한 조합 수.</summary>
    public Dictionary<int, int> FormPoints { get; } = new();
    /// <summary>그레이드 보너스로 들어온 옵션 스킬(스킬 계승 · 개조) — Skills 에도 들어 있다. 성능초기화에서 안 지워진다.</summary>
    public List<int> BonusSkills { get; } = [];
    /// <summary>전용함 스킬(옵션 스킬 번호) — 배 한 척에 하나만. 없으면 0.</summary>
    public int Dedicated { get; set; }
    // 선체 특수효과 도료(1 ~ 15)로 입힌 효과 — 배 주위에 일렁이는 빛. 없으면 0
    public int HullEffect { get; set; }

    // 스킬 번호 뒤에 그레이드 쪽 값을 큰 수로 덧붙여 적는다(옛 저장과 맞게): 1e6 + 그레이드, 2e6 + 경험치, 3e6 + 숙련도, 5e6 + 전용함 스킬, 6e6 + 보너스(표 90 의 번호), 7e6 + 선박 형식, 8e6 + 형식 × 100 + 스며든 횟수. 4e6 + n 은 옛 저장의 보너스(지어낸 목록의 차례)라 읽을 때 옮긴다
    public double[] ToArray() => [Times, Durability, Sail, Turn, Wave, Hold, .. Skills.Select(s => (double)s),
                                  1_000_000 + Grade, 2_000_000 + GradeExp, 3_000_000 + Math.Round(Mastery), .. Bonuses.Select(b => 6_000_000.0 + b),
                                  .. Dedicated > 0 ? [5_000_000.0 + Dedicated] : Array.Empty<double>(),
                                  .. Form > 0 ? [7_000_000.0 + Form] : Array.Empty<double>(), .. BonusSkills.Select(s => 9_000_000.0 + s),
                                  .. HullEffect > 0 ? [10_000_000.0 + HullEffect] : Array.Empty<double>()];

    // 옛 저장의 보너스 차례(스킬추가 · 가속강화 · 스킬계승 · 내구력 · 세로돛 · 가로돛 · 선회 · 내파 · 장갑 · 선실 · 포실 · 창고) → 표 90 의 번호
    private static readonly int[] OldBonuses = [14, 29, 16, 1, 2, 3, 5, 6, 7, 8, 9, 10];

    /// <summary>옛 번호로 저장된 선박 스킬을 지금 번호로 옮긴다.</summary>
    public ShipWork Renumbered(Func<int, int> map)
    {
        for (int i = 0; i < Skills.Count; i++) Skills[i] = map(Skills[i]);
        if (Dedicated > 0) Dedicated = map(Dedicated);
        return this;
    }

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
            else if (kind == 4) work.Bonuses.Add(rest >= 0 && rest < OldBonuses.Length ? OldBonuses[rest] : 1);
            else if (kind == 6) work.Bonuses.Add(rest);
            else if (kind == 7) work.Form = rest;
            else if (kind == 9) work.BonusSkills.Add(rest);
            else if (kind == 10) work.HullEffect = rest;
            else if (kind == 8) work.FormPoints[rest / 100] = rest % 100;
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

    /// <summary>
    /// 그 배의 강화 상한 열 — 내구력 · 세로돛 · 가로돛 · 조력 · 선회 · 내파 · 장갑 · 선실 · 포실 · 창고(선박 정보의 초록 막대가 이것에 대한 비율이다).
    /// 배 상세(ssjoy 의 「강화 상한」)가 있는 배는 그 값, 없는 배는 게임의 강화 규칙에서 나온 값과 어림(조력 · 장갑 · 선실 · 포실).
    /// </summary>
    public int[] StrengthCaps(ShipData ship, ShipStats plain)
    {
        if (Data.ShipDetail(ship.Name) is { Caps.Count: >= 10 } detail) return [.. detail.Caps.Take(10)];
        return
        [
            (int)WorkCap("Durability", plain), (int)WorkCap("Sail", plain), (int)WorkCap("Sail", plain), plain.Rowing > 0 ? 30 : 0, (int)WorkCap("Turn", plain), (int)WorkCap("Wave", plain),
            10, Math.Max(4, plain.MaxCrew / 5), Math.Max(2, plain.Guns / 5), (int)WorkCap("Hold", plain),
        ];
    }

    /// <summary>강화와 옵션 스킬을 입힌 능력치.</summary>
    public ShipStats Worked(ShipStats stats, ShipWork work, ShipData ship)
    {
        if (work.Times == 0 && work.Skills.Count == 0 && work.Bonuses.Count == 0 && work.Dedicated == 0 && work.Grade == 0) return stats;
        double sails = Math.Max(1, stats.VerticalSail + stats.HorizontalSail);
        double holdBonus = OptionAmount(work, "Hold");
        // 강화분은 조타 숙련도가 찬 만큼 듣는다(절반은 늘 듣는다). 그레이드 보너스의 강화는 그대로 더한다
        double applied = WorkShare(ship, work);
        // 보너스는 표 90 의 번호다: 1 내구력 · 2 세로돛 · 3 가로돛 · 4 조력 · 5 선회 · 6 내파 · 7 장갑 · 8 선실 · 9 포실 · 10 창고, 29 ~ 31 가속 강화
        bool Has(int bonus) => work.Bonuses.Contains(bonus);
        double sail = work.Sail * applied, vertical = sail + (Has(2) ? 12 : 0), horizontal = sail + (Has(3) ? 12 : 0);
        double turn = work.Turn * applied + (Has(5) ? 2 : 0);
        double haste = 1 + 0.03 * work.Bonuses.Count(b => b is 29 or 30 or 31);
        // 형식에 따라 그레이드마다 오르는 성능이 다르다(이용자 풀이 글의 틀 — 크기는 지은 것):
        // 탐험선 돛 +2 · 내파 +½ / 쾌속운송선 돛 +1 · 창고 +1% / 운송선 창고 +2% / 무장상선 창고 +1% · 내구 +1% /
        // 전투함 내구 +2% · 장갑 +½ / 고속전투함 내구 +1% · 돛 +1 / 범용함 돛 +1 · 창고 +0.5%
        int form = FormOf(ship, work), g = work.Grade;
        double formSail = g * (form switch { 1 => 2, 2 or 6 or 7 => 1, _ => 0 });
        double formHull = g * (form switch { 5 => 0.02, 4 or 6 => 0.01, _ => 0 });
        double formHold = g * (form switch { 3 => 0.02, 2 or 4 => 0.01, 7 => 0.005, _ => 0 });
        (vertical, horizontal) = (vertical + formSail, horizontal + formSail);
        return stats with
        {
            Durability = (int)Math.Round(stats.Durability + work.Durability * applied + (Has(1) ? stats.Durability * 0.08 : 0) + stats.Durability * formHull),
            VerticalSail = (int)Math.Round(stats.VerticalSail + vertical),
            HorizontalSail = (int)Math.Round(stats.HorizontalSail + horizontal),
            Knots = stats.Knots * (1 + (vertical + horizontal) / sails * 0.5) * haste,
            Rowing = stats.Rowing + (Has(4) && stats.Rowing > 0 ? Math.Max(4, (int)Math.Round(stats.Rowing * 0.1)) : 0),
            Turn = (int)Math.Round(stats.Turn + turn),
            TurnFactor = stats.TurnFactor * (1 + turn / Math.Max(1.0, stats.Turn)),
            WaveResist = (int)Math.Round(stats.WaveResist + work.Wave * applied + (Has(6) ? 2 : 0) + (form == 1 ? g / 2 : 0)),
            Armor = stats.Armor + (Has(7) ? 3 : 0) + (form == 5 ? g / 2 : 0),
            // 선실 · 포실 적재량 강화 — 한 번에 10%(적어도 선실 4 · 포실 2, 지은 값)
            MaxCrew = stats.MaxCrew + (Has(8) ? Math.Max(4, (int)Math.Round(stats.MaxCrew * 0.1)) : 0),
            Guns = stats.Guns + (Has(9) ? Math.Max(2, (int)Math.Round(stats.Guns * 0.1)) : 0),
            Hold = (int)Math.Round((stats.Hold + work.Hold * applied + (Has(10) ? stats.Hold * 0.08 : 0) + stats.Hold * formHold) * (1 + holdBonus)),
        };
    }

    // ── 그레이드 · 조타 숙련도 · 선박 조합 ────────────────────────────────────
    // 틀은 원본의 것이다(클라이언트 글 40501 ~ 과 이용자 풀이 글): 그레이드 0 ~ 8, 본배에 제물배를 먹여 올리고 제물배는 사라진다,
    // 실패하면 그레이드 경험치가 쌓여 다음 성공률이 오른다, 4 부터는 대실패(한 단계 강등), 1 · 3 · 6 에서 그레이드 보너스가 하나 붙는다,
    // 그레이드가 오르면 강화는 초기화된다, 그레이드마다 조타 숙련도 상한 +5, 숙련도가 차야 강화가 다 듣는다.
    // 성공률은 풀이 글의 세 점(0+0 38% · 0+1 49% · 0+2 59%, 같은 배면 +10%)에 맞춘 식이고, 값 · 경험치 · 보너스의 크기는 지은 것이다.

    public const int MaxGrade = 8;
    /// <summary>
    /// 그레이드 보너스 — 클라이언트 자료 표 90 의 번호 · 이름 · 설명 그대로(22 ~ 28 · 32 는 이름 없는 줄).
    /// 조합할 때 고른다(화면 글 40506 「선택 보너스」). 크기(강화 +8% · 돛 +12 …)는 지은 것이다.
    /// </summary>
    public static readonly (int Id, string Name, string Note)[] GradeBonuses =
    [
        (1, "내구력 강화", "선박 내구력 강화치를 증가시킨다."), (2, "세로돛성능 강화", "선박 세로돛성능 강화치를 증가시킨다."),
        (3, "가로돛성능 강화", "선박 가로돛성능 강화치를 증가시킨다."), (4, "조력 강화", "선박 조력 강화치를 증가시킨다."),
        (5, "선회 성능 강화", "선박 선회성 강화치를 증가시킨다."), (6, "내파성 강화", "선박 내파성 강화치를 증가시킨다."),
        (7, "장갑 강화", "선박의 장갑 강화치를 증가시킨다."), (8, "선실적재량 강화", "선박 선실적재량 강화치를 증가시킨다."),
        (9, "포실적재량 강화", "선박 포실적재량 강화치를 증가시킨다."), (10, "창고 용량 강화", "선박 창고 용량 강화치를 증가시킨다."),
        (11, "선측포 추가", "선측포 슬롯을 추가한다. 최대 추가 가능 수는 선박 사이즈에 따라 달라진다."),
        (12, "선수포 추가", "선수포 슬롯을 추가한다. +1까지만 추가 가능."), (13, "선미포 추가", "선미포 슬롯을 추가한다. +1까지만 추가 가능."),
        (14, "스킬칸 추가1", "옵션 스킬 부여 가능 수가 +1 된다."), (15, "스킬칸 추가2", "옵션 스킬 부여 가능 수에 +1 추가된다."),
        (16, "스킬 계승", "조합에 사용한 배에 부여된 옵션 스킬을 적용시킨다."),
        (17, "포함 개조", "옵션 스킬 「포함 개조」를 적용시킨다."), (18, "장갑함 개조", "옵션 스킬 「장갑함 개조」를 적용시킨다."),
        (19, "백병함 개조", "옵션 스킬 「백병함 개조」를 적용시킨다."), (20, "특수 화물선 개조", "옵션 스킬 「특수화물선 개조」를 적용시킨다."),
        (21, "탐사선 개조", "옵션 스킬 「탐사선 개조」를 적용시킨다."),
        (29, "가속 강화1", "선박의 가속도를 강화시킨다."), (30, "가속 강화2", "선박의 가속도를 더 강화시킨다."), (31, "가속 강화3", "선박의 가속도를 보다 더 강화시킨다."),
    ];

    public static string BonusName(int bonus) => Array.Find(GradeBonuses, b => b.Id == bonus).Name ?? $"보너스 {bonus}";

    /// <summary>개조 보너스(17 ~ 21)가 붙이는 옵션 스킬 — 스킬 표의 2900 ~ 2904.</summary>
    public static int RefitSkill(int bonus) => bonus is >= 17 and <= 21 ? 2900 + bonus - 17 : 0;

    /// <summary>그 배가 이번 조합에서 고를 수 있는 보너스들 — 받은 것은 빼고, 「2 · 3」은 앞의 것이 있어야 하고, 스킬 계승은 재료 선박에 옵션 스킬이 있어야 한다.</summary>
    public List<int> BonusChoices(DockedShip main, DockedShip material)
    {
        var have = main.Work.Bonuses;
        bool refitted = have.Exists(b => b is >= 17 and <= 21);
        return GradeBonuses.Select(b => b.Id).Where(id =>
            (id == 16 || !have.Contains(id))
            && (id != 15 || have.Contains(14)) && (id != 30 || have.Contains(29)) && (id != 31 || have.Contains(30))
            && (id != 16 || InheritChoices(main, material).Count > 0)
            && (id != 4 || main.Ship.Kind == 2)                      // 조력은 노가 있는 배만
            && !(id is >= 17 and <= 21 && refitted))                 // 개조는 한 가지만(짐작)
            .OrderBy(id => id == 16 ? 0 : 1).ToList();                // 스킬 계승을 맨 위에 — 재료 선박에 스킬이 있으면 그것이 먼저 골라져 있다
    }

    /// <summary>스킬 계승으로 옮길 수 있는 재료 선박의 옵션 스킬 — 본배에 아직 없는 것.</summary>
    public List<int> InheritChoices(DockedShip main, DockedShip material) =>
        material.Work.Skills.Where(s => !main.Work.Skills.Contains(s) && s != main.Work.Dedicated).Distinct().ToList();

    /// <summary>이번 조합에서 보너스를 고르는가 — 그레이드가 1 · 3 · 6 이 되는 조합.</summary>
    public static bool GivesBonus(DockedShip main) => main.Work.Grade + 1 is 1 or 3 or 6;

    /// <summary>
    /// 조타 숙련도의 상한 — 이용자 가이드(인벤 「조타 숙련도 습득 가이드」)대로 배 크기에 따라 소형 80 · 중형 120 · 대형 200(소형 40 · 대형 160 인 배도 있다는데 어느 배인지 몰라 뺐다),
    /// 그레이드마다 +5.
    /// </summary>
    // 배 표의 크기 등급은 0 소형1 · 1 소형2 · 2 중형 · 3 대형1 · 4 대형2 다(ssjoy 의 분류와 60척을 견줘 맞았다). 「초대형」은 없다 — 전에 4 를 그렇게 부른 것은 틀렸다.
    // 조타 숙련도 가이드의 값과 그대로 맞는다: 소형 40 / 80 · 중형 120 · 대형 160 / 200
    public static int MasteryCap(ShipData ship, ShipWork work) => (ship.SizeClass switch { <= 0 => 40, 1 => 80, 2 => 120, 3 => 160, _ => 200 }) + 5 * work.Grade;

    /// <summary>
    /// 선박 형식 — 이름은 클라이언트 자료 표 89 의 일곱(1 탐험선 · 2 쾌속운송선 · 3 운송선 · 4 무장상선 · 5 전투함 · 6 고속전투함 · 7 범용함).
    /// 배마다 어느 형식인지는 배 표에서 못 찾았다 — 필요 레벨이 가장 높은 갈래로 짓는다(모험 → 탐험선, 교역 → 운송선, 전투 → 전투함, 비기면 범용함).
    /// 쾌속운송선 · 무장상선 · 고속전투함은 그래서 안 나온다.
    /// </summary>
    public static readonly string[] FormNames = ["", "탐험선", "쾌속운송선", "운송선", "무장상선", "전투함", "고속전투함", "범용함"];

    /// <summary>그 배의 본디 형식(표 89 의 번호) — 필요 레벨이 가장 높은 갈래로 짓는다.</summary>
    public int BaseForm(ShipData ship)
    {
        var levels = ShipStats.Of(ship, Data.Settings.Ships).Levels;
        int top = Math.Max(levels.Adventure, Math.Max(levels.Trade, levels.Battle));
        int tops = (levels.Adventure == top ? 1 : 0) + (levels.Trade == top ? 1 : 0) + (levels.Battle == top ? 1 : 0);
        return top == 0 || tops > 1 ? 7 : levels.Adventure == top ? 1 : levels.Trade == top ? 3 : 5;
    }

    /// <summary>지금 형식 — 조합으로 바뀌었으면 그것, 아니면 본디 형식.</summary>
    public int FormOf(ShipData ship, ShipWork? work) => work is { Form: > 0 } ? work.Form : BaseForm(ship);
    public string FormName(ShipData ship, ShipWork? work = null) => FormNames[Math.Clamp(FormOf(ship, work), 1, 7)];

    /// <summary>
    /// 조합이 성공했을 때의 새 형식 — 이용자 가이드의 그림(「선박 형식은 제물선박의 선박형식에 의해 대상선박의 선박형식이 변경됩니다」)대로.
    /// 형식을 세 바탕(탐험 · 운송 · 전투)의 묶음으로 보면 그림의 화살표가 한 규칙으로 풀린다:
    /// 탐험선 {탐} · 운송선 {운} · 전투함 {전} · 쾌속운송선 {탐, 운} · 고속전투함 {탐, 전} · 무장상선 {운, 전} · 범용함 {탐, 운, 전}.
    /// 제물이 바탕 형식 X 일 때 — 본배에 X 가 없으면 X 가 더해지고(탐험선 + 운송선 → 쾌속운송선, 쾌속운송선 + 전투함 → 범용함),
    /// 있으면 X 만 남는다(쾌속운송선 + 탐험선 → 탐험선, 범용함 + 전투함 → 전투함).
    /// 제물이 쾌속운송선 · 무장상선 · 고속전투함 · 범용함일 때는 그림에 없다(「생략」) — 안 바뀌는 것으로 둔다.
    /// </summary>
    public static int FormAfter(int main, int fed)
    {
        static int Bits(int form) => form switch { 1 => 1, 3 => 2, 5 => 4, 2 => 3, 6 => 5, 4 => 6, _ => 7 };
        int add = fed switch { 1 => 1, 3 => 2, 5 => 4, _ => 0 };
        if (add == 0) return main;
        int now = Bits(main), next = (now & add) != 0 ? add : now | add;
        return next switch { 1 => 1, 2 => 3, 4 => 5, 3 => 2, 5 => 6, 6 => 4, _ => 7 };
    }

    /// <summary>크기의 큰 갈래 — 0 소형(소형1 · 2) · 1 중형 · 2 대형(대형1 · 2). 선박 조합은 같은 갈래끼리 한다.</summary>
    public static int SizeGroup(ShipData ship) => ship.SizeClass switch { <= 1 => 0, 2 => 1, _ => 2 };
    /// <summary>강화가 듣는 몫(0.5 ~ 1) — 조타 숙련도가 찬 만큼.</summary>
    public static double WorkShare(ShipData ship, ShipWork work) => 0.5 + 0.5 * Math.Clamp(work.Mastery / MasteryCap(ship, work), 0, 1);
    // 스킬칸 추가 1 · 2, 그리고 스킬 계승 · 개조로 들어온 스킬은 칸을 따로 차지하지 않게 그만큼 늘린다
    public int SkillSlotsOf(ShipWork work) => Data.ShipWorks.SkillSlots + work.Bonuses.Count(b => b is 14 or 15 or 16 or (>= 17 and <= 21));

    private bool _masteryHalf;

    /// <summary>
    /// 타고 있는 배의 조타 숙련도가 오른다 — 가이드대로 입항할 때 1(거리와 상관없다), 생산할 때 1. 100 부터는 두 번에 한 번만 오른다.
    /// 전투 · 장비 강화로 오르는 것은 없다.
    /// </summary>
    private void GainMastery()
    {
        if (Work.Mastery >= 100 && (_masteryHalf = !_masteryHalf)) return;
        AddMastery(GainFactor, quiet: true);
    }

    /// <summary>조타 숙련도를 올린다(도시 메뉴의 단추도 이것을 쓴다).</summary>
    public void AddMastery(int amount, bool quiet = false)
    {
        int cap = MasteryCap(Ship, Work);
        if (Work.Mastery >= cap) { if (!quiet) Say("조타 숙련도가 이미 가득 찼다."); return; }
        Work.Mastery = Math.Min(cap, Work.Mastery + amount);
        Stats = Worked(StatsOf(Ship, ShipMaterialId, ShipLoad), Work, Ship);
        if (Work.Mastery >= cap) { Say($"{Ship.Name}의 조타 숙련도가 가득 찼다. 강화가 모두 듣는다."); Cues.Enqueue("Mastery"); }
        else if (!quiet) Say($"조타 숙련도 +{amount} ({Work.Mastery:0}/{cap})");
    }

    /// <summary>
    /// 선박 조합에 쓰는 책들(아이템 표 14) — 소지품에서 쓰면 다음 조합 한 번에 듣는다.
    /// 함선 개장 기법서 +25% · 지도서 +30% · 지침서 +50%(설명 글의 값). 개장 특별대우 지시서는 「선박 조합을 실행하면 효과가 사라진다」뿐이라
    /// 무엇을 하는지 글에 없다 — 실패했을 때 대실패(강등)를 막는 것으로 지었다.
    /// </summary>
    public static readonly (int Item, string Name, int Bonus)[] RefitBooks = [(1510029, "함선 개장 기법서", 25), (1510061, "함선 개장 지도서", 30), (1510062, "함선 개장 지침서", 50)];
    public const int RefitGuardItem = 1510030;

    /// <summary>써 둔 개장 책(다음 조합에 듣는다)과 특별대우.</summary>
    public (int Item, string Name, int Bonus)? RefitBook { get; private set; }
    public bool RefitGuard { get; private set; }

    /// <summary>개장 책 · 지시서를 쓴다. 쓴 것이면 true.</summary>
    private bool UseRefit(int item)
    {
        if (item == RefitGuardItem)
        {
            if (RefitGuard) { Say("개장 특별대우 지시서는 이미 써 두었다."); return false; }
            RefitGuard = true;
            Say("개장 특별대우 지시서를 썼다. 다음 선박 조합에서 대실패(강등)가 나지 않는다.");
            return true;
        }
        if (Array.Find(RefitBooks, b => b.Item == item) is not { Item: > 0 } book) return false;
        if (RefitBook is { } now && now.Bonus >= book.Bonus) { Say($"이미 {now.Name}을(를) 써 두었다."); return false; }
        RefitBook = book;
        Say($"{book.Name}을(를) 썼다. 다음 선박 조합의 성공률이 {book.Bonus}% 오른다.");
        return true;
    }

    /// <summary>함선 재설계 기술서 — 「선박 개조를 모두 원래대로 되돌리는 방법이 적힌 문서.」(아이템 표 14)</summary>
    public const int RedesignBook = 1510031;

    public string? RedesignBlocker(DockedShip ship) =>
        ship.Work.Grade == 0 && ship.Work.GradeExp == 0 && ship.Work.Bonuses.Count == 0 ? "그레이드가 0 이다"
        : Items.GetValueOrDefault(RedesignBook) <= 0 ? "함선 재설계 기술서가 있어야 한다" : null;

    /// <summary>
    /// 그레이드 초기화(화면 글 40524 「선박의 그레이드와 그레이드 경험치，성능을 초기화합니다」) — 함선 재설계 기술서 한 권.
    /// 그레이드 · 경험치 · 그레이드 보너스 · 강화치가 0 이 되고, 개조 보너스로 붙은 「○○ 개조」도 빠진다.
    /// 그 밖의 옵션 스킬(계승으로 받은 것 포함)과 전용함 스킬 · 재질은 남긴다 — 이 가름은 내 판단이다.
    /// </summary>
    /// <summary>그레이드 초기화를 한 뒤의 강화 상태 — 배는 건드리지 않는다(미리 보기와 실제 초기화가 같이 쓴다).</summary>
    public ShipWork Redesigned(DockedShip ship)
    {
        var after = new ShipWork { Dedicated = ship.Work.Dedicated };
        // 그레이드 보너스로 들어온 스킬(스킬 계승 · 개조)은 보너스와 함께 사라진다. 조선으로 붙인 옵션 스킬은 남는다
        after.Skills.AddRange(ship.Work.Skills.Where(s => s is not (>= 2900 and <= 2904) && !ship.Work.BonusSkills.Contains(s)));
        after.Mastery = Math.Min(ship.Work.Mastery, MasteryCap(ship.Ship, after));
        return after;
    }

    /// <summary>그 강화 상태였을 때의 부두 배의 능력치.</summary>
    public ShipStats StatsWith(DockedShip ship, ShipWork work) => Worked(StatsOf(ship.Ship, ship.Material, ship.Load), work, ship.Ship);

    public void Redesign(DockedShip ship)
    {
        if (Mode != Mode.Port || !Held(ship) || RedesignBlocker(ship) != null) return;
        if (--Items[RedesignBook] <= 0) Items.Remove(RedesignBook);
        var work = ship.Work;
        var reset = Redesigned(ship);
        (work.Grade, work.GradeExp) = (0, 0);
        work.Bonuses.Clear();
        (work.Times, work.Durability, work.Sail, work.Turn, work.Wave, work.Hold) = (0, 0, 0, 0, 0, 0);
        work.Skills.Clear();
        work.Skills.AddRange(reset.Skills);
        work.BonusSkills.Clear();
        work.Form = 0;
        work.Mastery = reset.Mastery;
        ship.Durability = Math.Min(ship.Durability, StatsOf(ship).Durability);
        if (work == Work) Stats = Worked(StatsOf(Ship, ShipMaterialId, ShipLoad), Work, Ship);
        Say($"함선 재설계 기술서를 써서 {ship.Ship.Name}의 개조를 모두 되돌렸다. (그레이드 0)");
    }

    // 타고 있는 배를 부두의 배처럼 다루는 껍데기(모드: 타고 있는 배도 조합) — 강화 기록은 타고 있는 배의 것을 그대로 가리킨다
    private DockedShip? _aboard;
    public DockedShip Aboard => _aboard is { } a && a.Ship == Ship && a.Work == Work && a.Material == ShipMaterialId && a.Load == ShipLoad && a.Parts.Count == Parts.Count ? a
        : _aboard = new DockedShip { Ship = Ship, Material = ShipMaterialId, Load = ShipLoad, Work = Work, Durability = Stats.Durability, Parts = [.. Parts] };
    private bool Held(DockedShip ship) => Dock.Contains(ship) || Data.Settings.ModCombineOnBoard && ship == _aboard && ship.Work == Work;

    public int CombineChance(DockedShip main, DockedShip material) =>
        Math.Clamp(Math.Clamp(38 - 9 * main.Work.Grade + 10 * material.Work.Grade + (main.Ship.Id == material.Ship.Id ? 10 : 0) + main.Work.GradeExp / 4, 3, 100) + Math.Clamp(Data.Settings.ModCombineBonus, 0, 50) + (RefitBook?.Bonus ?? 0), 0, 100);

    // 조합에 성공한 뒤의 강화 상태 — 배는 건드리지 않는다(조합 창의 미리 보기). Combine 의 성공 쪽과 같은 차례다
    public ShipWork Combined(DockedShip main, DockedShip material, int bonus, int inherit)
    {
        var after = ShipWork.From(main.Work.ToArray());
        after.Mastery = main.Work.Mastery;
        (after.Grade, after.GradeExp) = (main.Work.Grade + 1, 0);
        (after.Times, after.Durability, after.Sail, after.Turn, after.Wave, after.Hold) = (0, 0, 0, 0, 0, 0);
        int had = FormOf(main.Ship, main.Work), turned = FormAfter(had, FormOf(material.Ship, material.Work));
        if (turned != had) after.Form = turned;
        if (!GivesBonus(main) || bonus == 0) return after;
        after.Bonuses.Add(bonus);
        if (bonus == 16 && inherit > 0) after.Skills.Add(inherit);
        else if (RefitSkill(bonus) is > 0 and var refit && !after.Skills.Contains(refit)) after.Skills.Add(refit);
        return after;
    }

    public int CombineCost(DockedShip main) => 50_000 * (main.Work.Grade + 1) * Math.Max(1, main.Ship.SizeClass);

    public string? CombineBlocker(DockedShip main, DockedShip material)
    {
        if (main == material) return "같은 배다";
        if (!Dock.Contains(material)) return "타고 있는 배는 재료가 못 된다";
        if (main.Work.Grade >= MaxGrade) return "그레이드가 최대치다";
        // 크기가 같은 배만 재료가 된다 — 소형끼리 · 중형끼리 · 대형끼리. 대형1 과 대형2 는 그냥 대형이고 서로 먹일 수 있다(사용자 확인, 2026-10-06)
        if (SizeGroup(main.Ship) != SizeGroup(material.Ship)) return "크기가 같은 배만 재료가 된다 (소형 · 중형 · 대형)";
        if (Money < CombineCost(main)) return "돈이 모자라다";
        return null;
    }

    /// <summary>선박 조합 — 재료 선박은 사라진다.</summary>
    /// <param name="bonus">고른 그레이드 보너스(표 90 의 번호) — 그레이드가 1 · 3 · 6 이 될 때만 쓰인다.</param>
    /// <param name="inherit">스킬 계승을 골랐을 때 옮길 옵션 스킬.</param>
    public void Combine(DockedShip main, DockedShip material, int bonus = 0, int inherit = 0)
    {
        CombineInto(main, material, bonus, inherit);
        // 타고 있는 배를 조합했으면 능력치를 다시 셈한다
        if (main.Work == Work) Stats = Worked(StatsOf(Ship, ShipMaterialId, ShipLoad), Work, Ship);
    }

    private void CombineInto(DockedShip main, DockedShip material, int bonus, int inherit)
    {
        if (Mode != Mode.Port || !Held(main) || !Dock.Contains(material) || CombineBlocker(main, material) != null) return;
        int chance = CombineChance(main, material);
        bool guarded = RefitGuard;
        (RefitBook, RefitGuard) = (null, false);       // 책과 지시서는 조합 한 번에 듣고 사라진다
        Money -= CombineCost(main);
        Dock.Remove(material);
        var work = main.Work;
        if (_random.Next(100) >= chance)
        {
            if (work.Grade >= 4 && !guarded && _random.Next(100) < 15)
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
        // 제물 선박의 형식에 따라 본배의 형식이 바뀐다
        int had = FormOf(main.Ship, work), turned = FormAfter(had, FormOf(material.Ship, material.Work));
        if (turned != had)
        {
            work.Form = turned;
            Say($"{main.Ship.Name}의 선박 형식이 {FormNames[had]}에서 {FormNames[turned]}(으)로 바뀌었다!");
        }
        if (work.Grade is 1 or 3 or 6)
        {
            // 고른 보너스가 붙는다(못 고르는 것이었으면 고를 수 있는 것 가운데 첫째)
            work.Grade--;
            var choices = BonusChoices(main, material);
            work.Grade++;
            if (!choices.Contains(bonus)) bonus = choices.Count > 0 ? choices[0] : 0;
            if (bonus == 0) return;
            work.Bonuses.Add(bonus);
            Say($"그레이드 보너스 「{BonusName(bonus)}」(이)가 부여됐습니다!");
            if (bonus == 16)
            {
                var skills = InheritChoices(main, material);
                if (!skills.Contains(inherit)) inherit = skills.Count > 0 ? skills[0] : 0;
                if (inherit > 0)
                {
                    work.Skills.Add(inherit);
                    work.BonusSkills.Add(inherit);
                    Say($"재료 선박의 옵션 스킬 「{OptionName(inherit)}」을(를) 계승했다.");
                }
            }
            else if (RefitSkill(bonus) is > 0 and var refit && !work.Skills.Contains(refit)) { work.Skills.Add(refit); work.BonusSkills.Add(refit); }
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

    public string OptionName(int skillId) => skillId is >= 2900 and <= 2904 ? BonusName(17 + skillId - 2900) : Data.OptionSkills.Find(s => s.SkillId == skillId)?.Name ?? SkillName(skillId);

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

    // ── 진짜 재료 조합으로 옵션 스킬 붙이기 ──
    // 배 상세에 그 스킬의 재료 조합이 적혀 있으면(위키의 「スキル付加例」) 그 조빌 아이템들을 실제로 가지고 있어야 하고, 붙이면 든다.
    // 조합을 모르는 스킬은 전처럼 지어 준 두 가지 재료로 강화 창에서 붙인다.

    // 그 스킬의 재료 조합을 조빌 아이템 번호로 — 조합이 없거나 아이템을 못 찾으면 null
    public List<int>? RealCombo(OptionSkill skill)
    {
        if (Data.ShipDetail(Ship.Name)?.Skills.Find(s => s.Name == skill.Name) is not { Parts.Count: > 0 } real) return null;
        var items = real.Parts.Select(name => Data.Papers.Find(p => p.Name == name && IsShipItem(p.Id))?.Id ?? 0).ToList();
        return items.Contains(0) ? null : items;
    }

    public string? ComboBlocker(OptionSkill skill)
    {
        if (ShipbuildingRank <= 0) return "조선 스킬이 없다";
        if (Work.Skills.Contains(skill.SkillId) || skill.SkillId == Work.Dedicated) return "이미 붙어 있다";
        if (Work.Skills.Count >= SkillSlotsOf(Work)) return "옵션 스킬 칸이 없다";
        if (RealCombo(skill) is not { } items) return "재료 조합을 모른다";
        var lacking = items.GroupBy(i => i).Where(g => Items.GetValueOrDefault(g.Key) < g.Count()).Select(g => ItemName(g.Key)).ToList();
        return lacking.Count > 0 ? "재료가 없다: " + string.Join(", ", lacking) : null;
    }

    public void GrantByCombo(OptionSkill skill)
    {
        if (Mode != Mode.Port || ComboBlocker(skill) != null || RealCombo(skill) is not { } items) return;
        foreach (int item in items)
            if (--Items[item] <= 0) Items.Remove(item);
        Work.Skills.Add(skill.SkillId);
        Stats = Worked(StatsOf(Ship, ShipMaterialId, ShipLoad), Work, Ship);
        TrainEffect("Shipbuilding", 60);
        Cues.Enqueue("Done");
        Say($"{Ship.Name}에 옵션 스킬 「{skill.Name}」을(를) 붙였다. (재료: {string.Join(" · ", items.Select(ItemName))})");
    }

    public OptionSkill? OptionFrom(IReadOnlyCollection<int> parts)
    {
        if (Work.Skills.Count >= SkillSlotsOf(Work)) return null;
        // 전용함 스킬로 이미 받은 스킬은 옵션 스킬로 또 붙지 않는다(클라이언트 글 6822)
        return Data.OptionSkills.Find(s => parts.Contains(s.PartA) && parts.Contains(s.PartB) && !Work.Skills.Contains(s.SkillId) && s.SkillId != Work.Dedicated && ShipAllows(s));
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
        // 아이템 추가의 「재질」 쪽지로 넣은 재질 아이템(지어 넣은 번호 — 재질 번호가 그대로 들어 있다)도 선박재료로 친다
        if (item > MaterialItem && item < MaterialItem + 1000) return MaterialOf(item - MaterialItem);
        // 짝지어 둔 표(material-items.json)에 있으면 그것 — 「○○ 재료」「○○ 강철 비늘」처럼 이름이 「선박재료」로 안 끝나는 것도 잡힌다
        if (Data.MaterialItems.FirstOrDefault(pair => pair.Value == item) is { Value: > 0 } known) return MaterialOf(known.Key);
        if (Data.Papers.Find(p => p.Id == item) is not { } paper || !paper.Name.EndsWith("선박재료")) return null;
        string name = paper.Name[..^4].Trim();
        name = name switch { "삼나무" => "삼나무판", "느릅나무" => "엘름(느릅나무)", "동판" => "동", "철판" => "철", "로즈우드" => "자단", _ => name };
        return Data.ShipMaterials.Find(m => m.Name == name);
    }

    /// <summary>가진 선박재료 아이템들 — 강화에 재료로 넣으면 배의 재질이 그것으로 바뀐다.</summary>
    public List<(int Item, ShipMaterial Material)> WoodsOwned() =>
        Items.Keys.Where(id => id is >= ShipItems and < ShipItems + 100_000 || (id > MaterialItem && id < MaterialItem + 1000)).OrderBy(id => id).Select(id => (Item: id, Material: WoodOf(id))).Where(w => w.Material != null).Select(w => (w.Item, w.Material!)).ToList();

    /// <summary>
    /// 그 배의 강화 횟수 — 배마다 다르다(ssjoy 배 쪽의 「기본 강화 n」). 자료를 못 모은 배는 ship-works.json 의 값.
    /// 「재강화 m」은 아직 안 쓴다.
    /// </summary>
    public int MaxTimesOf(ShipData ship) => Data.ShipDetail(ship.Name) is { Times: > 0 } detail ? detail.Times : Data.ShipWorks.MaxTimes;

    public string? WorkBlocker(IReadOnlyCollection<int> parts, int wood = 0, bool skillOnly = false)
    {
        if (ShipbuildingRank <= 0) return "조선 스킬이 없다";
        // 옵션 스킬 부여(특수 강화)는 강화 성능이 변하지 않는다 — 강화 횟수도 안 쓴다(횟수는 내 짐작)
        if (skillOnly)
        {
            if (wood > 0) return "옵션 스킬 부여에는 선박재료를 넣지 않는다";
            if (Work.Skills.Count >= SkillSlotsOf(Work)) return "옵션 스킬 칸이 없다";
            if (OptionFrom(parts) == null) return "이 재료 조합으로 붙는 옵션 스킬이 없다";
            return Money < WorkCost(parts) ? "돈이 모자라다" : null;
        }
        if (Work.Times >= MaxTimesOf(Ship)) return "더는 강화할 수 없다";
        if (parts.Count + (wood > 0 ? 1 : 0) < 2) return "재료를 둘 이상 고른다";
        if (parts.Count + (wood > 0 ? 1 : 0) > 4) return "재료는 넷까지";
        if (wood > 0 && (WoodOf(wood) == null || Items.GetValueOrDefault(wood) <= 0)) return "그 선박재료가 없다";
        if (Money < WorkCost(parts)) return "돈이 모자라다";
        return null;
    }

    /// <summary>강화한다 — 부품마다 정해진 능력치가 오르고(상한까지), 조합이 맞으면 옵션 스킬이 붙는다.</summary>
    // 성능초기화를 못 하는 까닭(되면 null). 그레이드가 올라 강화 횟수가 0 이 된 배도 조선으로 붙인 옵션 스킬이 남아 있으면 지울 것이 있다
    public string? ResetBlocker(ShipWork work)
    {
        bool skills = work.Skills.Exists(s => !work.BonusSkills.Contains(s) && s is not (>= 2900 and <= 2904));
        if (work.Times == 0 && !skills)
            return work.Skills.Count > 0 ? "지울 것이 없다 — 붙은 스킬은 그레이드 보너스(스킬 계승 · 개조)라 선박 조합의 「그레이드 초기화」로 지운다" : "지울 강화가 없다";
        return Items.GetValueOrDefault(DismantleBook) <= 0 ? "특수조선 해체 기법서가 없다" : null;
    }

    /// <summary>성능초기화 — 타고 있는 배의 강화치를 모두 0 으로(재질은 남는다). 되돌릴 수 없다.</summary>
    public void ResetWork()
    {
        if (Mode != Mode.Port || ResetBlocker(Work) != null) return;
        if (--Items[DismantleBook] <= 0) Items.Remove(DismantleBook);
        // 지워지는 것은 강화치와 (조선으로 붙인) 옵션 스킬뿐 — 재질 · 그레이드와 그 보너스 · 선박 형식 · 전용함 스킬 · 조타 숙련도는 남는다(원본의 안내 글 6843).
        // 그레이드 보너스로 들어온 스킬(스킬 계승 · 개조)도 보너스의 일부라 남는다
        var kept = Work;
        Work = new ShipWork { Dedicated = kept.Dedicated, Grade = kept.Grade, GradeExp = kept.GradeExp, Mastery = kept.Mastery, Form = kept.Form };
        Work.Bonuses.AddRange(kept.Bonuses);
        var held = kept.Skills.Where(s => kept.BonusSkills.Contains(s) || s is >= 2900 and <= 2904).ToList();
        Work.Skills.AddRange(held);
        Work.BonusSkills.AddRange(held);
        Stats = Worked(StatsOf(Ship, ShipMaterialId, ShipLoad), Work, Ship);
        Durability = Math.Min(Durability, Stats.Durability);
        Crew = Math.Min(Crew, Stats.MaxCrew);
        Say($"특수조선 해체 기법서를 써서 {Ship.Name}의 성능을 초기화했다. 강화치와 옵션 스킬이 지워졌다(재질 · 그레이드와 그 보너스 · 전용함 스킬은 그대로).");
    }

    /// <summary>
    /// 강화한 뒤의 능력치를 미리 본다 — 고른 부품과 선박재료를 넣었을 때의 배. 아무것도 바꾸지 않는다.
    /// 옵션 스킬 부여는 성능이 변하지 않으므로 붙을 스킬의 효과만 든다.
    /// </summary>
    public ShipStats StrengthenPreview(IReadOnlyCollection<int> parts, int wood = 0, bool skillOnly = false)
    {
        int material = wood > 0 && WoodOf(wood) is { } timber ? timber.Id : ShipMaterialId;
        var plain = StatsOf(Ship, material, ShipLoad);
        var after = new ShipWork { Times = Work.Times, Durability = Work.Durability, Sail = Work.Sail, Turn = Work.Turn, Wave = Work.Wave, Hold = Work.Hold,
                                   Grade = Work.Grade, GradeExp = Work.GradeExp, Mastery = Work.Mastery, Dedicated = Work.Dedicated, Form = Work.Form };
        after.Skills.AddRange(Work.Skills);
        after.Bonuses.AddRange(Work.Bonuses);
        if (!skillOnly)
            foreach (int id in parts)
            {
                if (Data.ShipWorks.Parts.Find(p => p.Id == id) is not { } part) continue;
                double cap = WorkCap(part.Stat, plain);
                double Add(double now) => Math.Min(cap, now + part.Amount);
                switch (part.Stat)
                {
                    case "Durability": after.Durability = Add(after.Durability); break;
                    case "Sail": after.Sail = Add(after.Sail); break;
                    case "Turn": after.Turn = Add(after.Turn); break;
                    case "Wave": after.Wave = Add(after.Wave); break;
                    case "Hold": after.Hold = Add(after.Hold); break;
                }
            }
        if (OptionFrom(parts) is { } option) after.Skills.Add(option.SkillId);
        // 지금 값(Stats)에는 단 부품(보조돛 · 장갑)이 들어 있다 — 미리 보기에도 얹어야 돛이 떨어져 보이지 않는다
        return WithParts(Worked(plain, after, Ship), Parts);
    }

    public void Strengthen(IReadOnlyCollection<int> parts, int wood = 0, bool skillOnly = false)
    {
        if (Mode != Mode.Port || WorkBlocker(parts, wood, skillOnly) != null) return;
        if (skillOnly && OptionFrom(parts) is { } given)
        {
            // 옵션 스킬 부여 — 스킬만 붙고 강화 성능은 변하지 않는다(클라이언트 글 49203)
            Money -= WorkCost(parts);
            foreach (int owned in parts.Where(OwnsPart).ToList())
                if (--Items[PartItem(owned)] <= 0) Items.Remove(PartItem(owned));
            Work.Skills.Add(given.SkillId);
            Stats = Worked(StatsOf(Ship, ShipMaterialId, ShipLoad), Work, Ship);
            TrainEffect("Shipbuilding", 60);
            Say($"{Ship.Name}에 옵션 스킬 「{given.Name}」을(를) 부여했다." + (OptionValid(given) ? "" : $" 옵션 스킬의 유효 조건을 충족하지 않습니다({OptionNeedLine(given)})."));
            return;
        }
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
        Say($"{Ship.Name}을(를) 강화했다. ({Work.Times}/{MaxTimesOf(Ship)}) " + string.Join(" ", gained));
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
