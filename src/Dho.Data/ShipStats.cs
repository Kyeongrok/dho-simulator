namespace Dho.Data;

/// <summary>배 한 종류(클라이언트 표 28). 표에는 크기·돛대 수·모형 번호만 있다.</summary>
public sealed class ShipData
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>모형 번호 nn — <c>0001\sh000x.bin</c> 에서 이름이 <c>SHIPnn_01</c> 인 항목.</summary>
    public int Model { get; set; }
    public int Height { get; set; }
    public int Width { get; set; }
    public int Length { get; set; }
    /// <summary>0 가장 작은 배 ~ 4 큰 배.</summary>
    public int SizeClass { get; set; }
    /// <summary>0 범선 · 2 갤리 · 4 증기선.</summary>
    public int Kind { get; set; }
    public int Masts { get; set; }
}

/// <summary>
/// 배의 크기에서 능력치를 지어 내는 계수. 원본 능력치는 클라이언트에 없다.
/// 기본값은 원본 조선소 화면의 바사(길이 18 · 폭 8 · 높이 12 — 내구 18, 선원 5, 대포 2, 창고 43, 선회 14, 값 2,000)에 맞췄다.
/// </summary>
public sealed class ShipRules
{
    /// <summary>내구 = 길이 × 폭 ÷ 이 값.</summary>
    public double DurabilityDivisor { get; set; } = 8;
    /// <summary>창고 = 길이 × 폭 ÷ 이 값.</summary>
    public double HoldDivisor { get; set; } = 3.35;
    /// <summary>선원 정원 = 길이 × 폭 ÷ 이 값.</summary>
    public double CrewDivisor { get; set; } = 29;
    /// <summary>필요 선원 = 정원 × 이 값.</summary>
    public double MinCrewRate { get; set; } = 0.4;
    /// <summary>값 = 길이 × 폭 × 높이 × 이 값.</summary>
    public double PriceFactor { get; set; } = 1.16;
    /// <summary>팔 때 받는 비율.</summary>
    public double SellRate { get; set; } = 0.5;
    /// <summary>돛 성능 = 높이 × 돛대 수 × 이 값.</summary>
    public double SailFactor { get; set; } = 3;
    public double BaseKnots { get; set; } = 8;
    /// <summary>본거지 · 영지 · 그 밖의 도시 조선소가 파는 가장 큰 크기 등급.</summary>
    public int CapitalMaxClass { get; set; } = 4;
    public int TerritoryMaxClass { get; set; } = 3;
    public int OtherMaxClass { get; set; } = 2;
}

/// <summary>
/// 배의 실제 능력치 한 줄 — 이용자들이 모은 자료(ssjoy.org 의 선박 표)에서 옮긴 것.
/// <c>data\extracted\ship-facts.json</c> 에 있으면 쓰고(저장소에는 안 둔다), 없으면 크기에서 지어 낸다.
/// </summary>
public sealed class ShipFact
{
    public string Name { get; set; } = "";
    public int Adventure { get; set; }
    public int Trade { get; set; }
    public int Battle { get; set; }
    public int Durability { get; set; }
    public int VerticalSail { get; set; }
    public int HorizontalSail { get; set; }
    public int Turn { get; set; }
    public int WaveResist { get; set; }
    public int Armor { get; set; }
    /// <summary>선실 — 태울 수 있는 선원 수.</summary>
    public int Cabin { get; set; }
    public int Hold { get; set; }
    // 일본 위키(wikiwiki.jp/gvo 의 배 표)에서 채운 것 — 없으면(null) 지어낸 값을 쓴다: 기본 가격, 조력, 필요 선원, 대포 칸
    public int? Price { get; set; }
    public int? Rowing { get; set; }
    public int? MinCrew { get; set; }
    public int? Guns { get; set; }
    // 부품 칸의 수 — 보조돛 · 특수장비 · 추가장갑 · 선측포 · 선수포 · 선미포(위키의 補 特 追 側 首 尾, 원본 선박 카드의 차례). 없으면 지어낸 수를 쓴다
    public int[]? Slots { get; set; }
}

/// <summary>
/// 선박 재질 — 내구도와 돛의 배율은 이용자들이 모은 자료의 값이고(너도밤나무가 기준 100%),
/// 고를 수 있게 되는 조선 랭크와 값 배율은 지은 것이다. Id 는 클라이언트 재료 표(30)의 번호.
/// </summary>
public sealed class ShipMaterial
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public double Durability { get; set; } = 1;
    public double Sail { get; set; } = 1;
    public int MinRank { get; set; }
    public double Price { get; set; } = 1;
    /// <summary>이 재질로 지은 배의 선체 빛깔(0xRRGGBB) — 이름에서 지어낸 것이다(원본의 재질별 텍스처는 못 찾았다).</summary>
    public int Color { get; set; } = 0xFFFFFF;
}

/// <summary>강화에 넣는 조선 부품. 이름은 원본의 것이고 올리는 능력치 · 양 · 값은 지은 것이다.</summary>
public sealed class WorkPart
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Sail 돛 · Turn 선회 · Wave 내파 · Durability 내구 · Hold 창고.</summary>
    public string Stat { get; set; } = "";
    public double Amount { get; set; }
    public int Price { get; set; }
}

/// <summary>
/// 옵션 스킬 — 강화에 넣은 부품 둘의 조합이 맞으면 붙는다. 이름과 조합은 원본의 것(이용자 자료의 배 상세 한 건)이고 효과의 크기는 지은 것이다.
/// SkillId 는 클라이언트 스킬 표의 번호(2000 ~).
/// </summary>
public sealed class OptionSkill
{
    public int SkillId { get; set; }
    public string Name { get; set; } = "";
    public int PartA { get; set; }
    public int PartB { get; set; }
    /// <summary>Speed 속도 · Storm 폭풍 피해 줄임 · Turn 선회 · Survey 주변 지도 넓힘 · CrewLoss 선원 피해 줄임 · Luck 재해 줄임 · Hold 창고 · Flotsam 하루마다 표류물(두캇).</summary>
    public string Effect { get; set; } = "";
    public double Amount { get; set; }
}

public sealed class ShipWorkBook
{
    /// <summary>배 한 척을 강화할 수 있는 횟수.</summary>
    public int MaxTimes { get; set; } = 5;
    /// <summary>옵션 스킬 칸 수.</summary>
    public int SkillSlots { get; set; } = 2;
    public List<WorkPart> Parts { get; set; } = [];
    public List<OptionSkill> Skills { get; set; } = [];
}

/// <summary>배 능력치 — 실제 값이 있으면 그것, 없으면 지어 낸 값.</summary>
public sealed record ShipStats(int Durability, int Hold, int MaxCrew, int MinCrew, double Knots, double TurnFactor, int Price, int SellPrice)
{
    // 조선소 화면에 보이는 값들 — 원본 화면의 칸 그대로(세로돛 · 가로돛 · 조력 · 선회 · 내파 · 장갑 · 대포)
    public int VerticalSail { get; init; }
    public int HorizontalSail { get; init; }
    public int Rowing { get; init; }
    public int Turn { get; init; }
    public int WaveResist { get; init; }
    public int Armor { get; init; }
    public int Guns { get; init; }

    /// <summary>이름 → 실제 능력치. 자료를 읽을 때 채운다.</summary>
    public static Dictionary<string, ShipFact> Facts { get; set; } = new();

    /// <summary>운항에 필요한 레벨(모험 · 교역 · 전투). 실제 값을 모르면 0.</summary>
    public (int Adventure, int Trade, int Battle) Levels { get; init; }
    /// <summary>실제 값에서 온 것인가.</summary>
    public bool Real { get; init; }

    /// <summary>
    /// 커스텀설정 조선으로 지은 배의 능력치 — 재질의 배율과 적재 변경을 입힌다.
    /// 적재 변경 x%(창고 쪽이 +): 창고가 x% 늘고 선실이 그만큼(반은 포실 몫) 준다 — 줄이면 거꾸로 선실 · 포실이 는다.
    /// 원본은 「최대적재량」 값을 정하고 줄이면 선회 · 속도를 얻는다(화면의 글) — 그 크기를 몰라, 한때(2026-10-08 새벽) 최대적재량을 늘리고 줄이게 바꿨다가
    /// 줄인 배가 창고만 잃고 얻는 것이 없어 되돌렸다. 크기를 알면 그때 원본대로 바꾼다.
    /// 20% 까지는 손해가 없고 그 너머는 넘은 1% 마다 돛과 내파가 2% 깎인다(원본 규칙 — 풀이 글).
    /// </summary>
    public ShipStats Built(ShipMaterial? material, int load, ShipRules rules)
    {
        double durability = material?.Durability ?? 1, sail = material?.Sail ?? 1;
        double penalty = 1 - Math.Max(0, Math.Abs(load) - 20) * 0.02;
        int moved = (int)Math.Round(Hold * load / 100.0);
        int vertical = (int)(VerticalSail * sail * penalty), horizontal = (int)(HorizontalSail * sail * penalty);
        int price = (int)(Price * (material?.Price ?? 1));
        return this with
        {
            Durability = Math.Max(1, (int)Math.Round(Durability * durability)),
            Hold = Math.Max(1, Hold + moved),
            MaxCrew = Math.Max(MinCrew, MaxCrew - moved / 2),
            // 최대 적재량이 적을수록 빠르다 — 「25적다와 25적업은 약 30% 의 속도 차」(나무위키 조선 문서)에서: ±25% 에서 ×1.13 / ×0.87
            Knots = Knots * sail * penalty * (1 - load * 0.0052),
            // 적재를 늘리면 선회가 「극소량」 준다(같은 문서) — 크기는 지은 값: ±13% 를 넘으면 1
            Turn = Math.Max(1, Turn - (load >= 13 ? 1 : 0) + (load <= -13 ? 1 : 0)),
            VerticalSail = vertical, HorizontalSail = horizontal,
            WaveResist = Math.Max(0, (int)Math.Round(WaveResist * penalty)),
            Guns = Math.Max(0, Guns - moved / 20),
            Price = price, SellPrice = (int)(price * rules.SellRate),
        };
    }

    public static ShipStats Of(ShipData ship, ShipRules rules)
    {
        if (Facts.TryGetValue(ship.Name, out var fact) && fact.Durability > 0)
        {
            // 값은 표에 없어서 크기에서 짓는다. 속도는 돛 성능에서.
            int cost = fact.Price ?? (int)(ship.Length * ship.Width * ship.Height * rules.PriceFactor);
            return new ShipStats(
                Durability: fact.Durability,
                Hold: fact.Hold,
                MaxCrew: Math.Max(2, fact.Cabin),
                MinCrew: fact.MinCrew ?? Math.Max(1, (int)Math.Round(fact.Cabin * 0.2)),
                Knots: rules.BaseKnots + (fact.VerticalSail + fact.HorizontalSail) / 80.0,
                TurnFactor: Math.Max(4, fact.Turn) / 12.0,
                Price: cost,
                SellPrice: (int)(cost * rules.SellRate))
            {
                VerticalSail = fact.VerticalSail, HorizontalSail = fact.HorizontalSail,
                Rowing = fact.Rowing ?? (ship.Kind == 2 ? ship.Length / 2 : 0),
                Turn = fact.Turn, WaveResist = fact.WaveResist, Armor = fact.Armor,
                Guns = fact.Guns ?? Math.Max(2, ship.Length / (ship.Kind == 2 ? 18 : 9)),
                Levels = (fact.Adventure, fact.Trade, fact.Battle), Real = true,
            };
        }

        double area = ship.Length * ship.Width;
        int durability = Math.Max(20, (int)(area / rules.DurabilityDivisor));
        int maxCrew = Math.Max(5, (int)(area / rules.CrewDivisor));
        double sail = ship.Height * ship.Masts * rules.SailFactor;
        int price = (int)(area * ship.Height * rules.PriceFactor);
        return new ShipStats(
            Durability: durability,
            Hold: Math.Max(10, (int)(area / rules.HoldDivisor)),
            MaxCrew: maxCrew,
            MinCrew: Math.Max(2, (int)(maxCrew * rules.MinCrewRate)),
            Knots: rules.BaseKnots + sail / (durability / 6.0 + 30),
            // 작은 배가 잘 돈다
            TurnFactor: Math.Max(4, 24 - ship.Length / 6.0) / 12,
            Price: price,
            SellPrice: (int)(price * rules.SellRate))
        {
            // 돛대가 적은 작은 배는 세로돛(삼각돛) 배, 돛대가 많을수록 가로돛이 는다. 갤리는 노를 젓는다.
            VerticalSail = (int)(sail * (ship.Masts <= 1 ? 2.6 : ship.Masts == 2 ? 1.4 : 0.6)),
            HorizontalSail = (int)(sail * (ship.Masts <= 1 ? 0.14 : ship.Masts == 2 ? 0.9 : 1.5)),
            Rowing = ship.Kind == 2 ? ship.Length / 2 : 0,
            Turn = Math.Max(3, (int)Math.Round(17 - ship.Length / 6.0)),
            WaveResist = 2 + ship.SizeClass * 2,
            Armor = 2 + ship.SizeClass * 3,
            Guns = Math.Max(2, ship.Length / (ship.Kind == 2 ? 18 : 9)),
        };
    }
}
