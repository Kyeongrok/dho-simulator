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

/// <summary>지어 낸 배 능력치.</summary>
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

    public static ShipStats Of(ShipData ship, ShipRules rules)
    {
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
