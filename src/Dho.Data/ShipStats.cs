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

/// <summary>배의 크기에서 능력치를 지어 내는 계수. 원본 능력치는 클라이언트에 없다.</summary>
public sealed class ShipRules
{
    /// <summary>내구 = 길이 × 폭 ÷ 이 값.</summary>
    public double DurabilityDivisor { get; set; } = 4;
    /// <summary>창고 = 길이 × 폭 ÷ 이 값.</summary>
    public double HoldDivisor { get; set; } = 3;
    /// <summary>선원 정원 = 길이 × 폭 ÷ 이 값.</summary>
    public double CrewDivisor { get; set; } = 12;
    /// <summary>필요 선원 = 정원 × 이 값.</summary>
    public double MinCrewRate { get; set; } = 0.4;
    /// <summary>값 = 길이 × 폭 × 높이 × 이 값.</summary>
    public double PriceFactor { get; set; } = 6;
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
            SellPrice: (int)(price * rules.SellRate));
    }
}
