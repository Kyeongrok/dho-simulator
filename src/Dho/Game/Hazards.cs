using Dho.Data;

namespace Dho.Game;

internal enum Weather { Clear, Cloudy, Rain, Storm }

/// <summary>지금 벌어지고 있는 재해 하나.</summary>
internal sealed class ActiveDisaster(DisasterData data)
{
    public DisasterData Data { get; } = data;
    /// <summary>벌어진 뒤 지난 날 수.</summary>
    public double Days { get; set; }
}

/// <summary>
/// 배 살림과 해상재해 — 물·식량·피로·내구, 날씨(폭풍), 재해가 일어나고 풀리는 것, 난파.
/// 이름과 알림 글은 클라이언트 화면 글 표에서 가져오고, 확률과 피해는 <c>disasters.json</c> 의 지은 값이다.
/// </summary>
internal sealed partial class Voyage
{
    private const double NearLandReach = 12;

    // 화면 글 표(dt000002)의 id
    private const uint TextNoWater = 3039, TextNoFood = 3040, TextCollapse = 3042, TextWaveDamage = 3043;
    private const uint TextStorm = 3195, TextStormOver = 3197, TextCrewDied = 3212, TextFatigueCured = 3112;
    private static readonly uint[] WeatherText = [2085, 2086, 2087, 2089];

    private readonly Random _random = new();
    private StringTable? _texts;
    private VoyageRules Rules => Settings.Voyage;

    public double Durability { get; private set; }
    public double Crew { get; private set; }
    public double Water { get; private set; }
    public double Food { get; private set; }
    public double Fatigue { get; private set; }
    public Dictionary<int, int> Supplies { get; } = new();
    public List<ActiveDisaster> Disasters { get; } = [];
    public Weather Weather { get; private set; } = Weather.Clear;
    private double _stormDays;
    private bool _starvingSaid;

    /// <summary>난파 창에 보일 글.</summary>
    public string WreckText { get; private set; } = "";

    public string WeatherName => Text(WeatherText[(int)Weather], Weather switch
    {
        Weather.Cloudy => "흐림", Weather.Rain => "비", Weather.Storm => "폭풍", _ => "맑음",
    });

    /// <summary>클라이언트 화면 글. 표를 못 읽으면 <paramref name="fallback"/>.</summary>
    public string Text(uint id, string fallback)
    {
        if (_texts == null)
        {
            try { _texts = StringTable.Load(@"0000\local\dt000002.bin"); }
            catch (Exception) { return fallback; }
        }
        string text = _texts[id];
        return text.StartsWith('#') ? fallback : text;
    }

    public string DisasterName(DisasterData data) => Text((uint)data.StateText, data.Name);

    private void StartSupplies()
    {
        Durability = Stats.Durability;
        Crew = Stats.MaxCrew;
        Water = Rules.StartWater;
        Food = Rules.StartFood;
    }

    // ── 항구의 보급 ──────────────────────────────────────────────────────────

    public int SupplyCount(int id) => Supplies.GetValueOrDefault(id);

    /// <summary>보급 스킬로 깎인 물·식량 값.</summary>
    public int WaterPrice => (int)Math.Ceiling(Rules.WaterPrice * (1 - Math.Min(0.5, Bonus("Discount"))));
    public int FoodPrice => (int)Math.Ceiling(Rules.FoodPrice * (1 - Math.Min(0.5, Bonus("Discount"))));

    public void BuyWater(int amount) => Water += Buy(amount, WaterPrice, Rules.MaxWater - Water);
    public void BuyFood(int amount) => Food += Buy(amount, FoodPrice, Rules.MaxFood - Food);

    /// <summary>창고와 소지금이 허락하는 만큼 사고, 산 수를 돌려준다.</summary>
    private int Buy(int amount, int price, double room)
    {
        if (Mode != Mode.Port) return 0;
        amount = (int)Math.Min(amount, Math.Floor(room));
        if (price > 0) amount = Math.Min(amount, Money / price);
        if (amount <= 0) return 0;
        Money -= amount * price;
        return amount;
    }

    public void BuySupply(SupplyData supply)
    {
        if (Mode != Mode.Port || Money < supply.Price) return;
        Money -= supply.Price;
        Supplies[supply.Id] = SupplyCount(supply.Id) + 1;
    }

    public int RepairCost => (int)Math.Ceiling(Stats.Durability - Durability) * Rules.RepairPricePerPoint;
    public int HireCost => (int)Math.Ceiling(Stats.MaxCrew - Crew) * Rules.CrewPrice;

    public void Repair()
    {
        if (Mode != Mode.Port || RepairCost == 0 || Money < RepairCost) return;
        Money -= RepairCost;
        Durability = Stats.Durability;
        Say("배를 수리했다.");
        Studied("Repair");
    }

    public void Hire()
    {
        if (Mode != Mode.Port || HireCost == 0 || Money < HireCost) return;
        Money -= HireCost;
        Crew = Stats.MaxCrew;
        Say("선원을 모집했다.");
    }

    private void RestInPort()
    {
        if (Fatigue > 0) Say(Text(TextFatigueCured, "선원들의 피로가 회복되었습니다."));
        Fatigue = 0;
        foreach (var disaster in Disasters) Say(Text((uint)disaster.Data.EndText, $"{disaster.Data.Name} — 풀렸다."));
        Disasters.Clear();
        Weather = Weather.Clear;
        _stormDays = 0;
    }

    // ── 재해 ─────────────────────────────────────────────────────────────────

    /// <summary>보급품을 써서 재해를 푼다.</summary>
    public void Cure(ActiveDisaster disaster)
    {
        int supply = disaster.Data.CureSupply;
        if (supply == 0 || SupplyCount(supply) <= 0 || !Disasters.Contains(disaster)) return;
        Supplies[supply]--;
        End(disaster);
    }

    private void End(ActiveDisaster disaster)
    {
        Disasters.Remove(disaster);
        Studied("Cure");
        Say(Text((uint)disaster.Data.EndText, $"{disaster.Data.Name} — 풀렸다."));
        GainExp(2, 30, 2);                      // 전투가 없어 재해를 이겨 낸 것을 전투 경험으로 친다(지은 값)
    }

    /// <summary>대본·개발 확인용: 재해를 바로 일으킨다.</summary>
    public void StartDisaster(int id)
    {
        if (Data.Disasters.Find(d => d.Id == id) is { } data) Begin(data);
    }

    // 폭풍 속에서도 돛을 편 채 갈 수 있는 배인가 — 내파가 문턱 이상
    public bool StormProof => Stats.WaveResist >= Rules.StormWaveResist;

    public void StartStorm()
    {
        Weather = Weather.Storm;
        _stormDays = Rules.StormDays;
        Say(Text(TextStorm, "폭풍이 몰아칩니다! 돛을 펴놓고 있으면 전복하고 맙니다!"));
        Say(StormProof ? $"이 배의 내파({Stats.WaveResist})라면 폭풍 속에서도 항해할 수 있다. (내파 {Rules.StormWaveResist} 이상)"
                       : $"내파 {Stats.WaveResist} — {Rules.StormWaveResist} 이상이어야 폭풍 속을 항해할 수 있다.");
        Cues.Enqueue("Storm");
    }

    private void Begin(DisasterData data)
    {
        if (Disasters.Exists(d => d.Data.Id == data.Id)) return;
        Disasters.Add(new ActiveDisaster(data));
        Say(Text((uint)data.StartText, $"{data.Name} 발생!"));
        Cues.Enqueue($"Disaster{data.Id}");        // 재해마다 소리를 따로 맬 수 있다(안 매면 경고 소리)
    }

    /// <summary>재해 때문에 속도에 곱해지는 값.</summary>
    private double DisasterSpeedFactor()
    {
        double factor = 1;
        foreach (var disaster in Disasters) factor *= disaster.Data.SpeedFactor;
        return factor;
    }

    /// <summary>바다에서 흐른 시간만큼 살림과 재해를 굴린다.</summary>
    private void UpdateHazards(double dt)
    {
        double days = dt / Settings.SecondsPerDay;
        // 생존 스킬이 선원 피해를 줄인다
        double loss = (1 - Math.Min(0.75, Bonus("CrewLoss"))) * AideCrewLoss * (1 - Math.Min(0.6, Option("CrewLoss")));
        UpdateOptions(days);
        UpdateAides(days);
        UpdateBuild(days);
        // 재해 표의 피해는 선원 80명·내구 400짜리 배가 기준이다. 배 크기에 맞춰 늘리고 줄인다.
        double crewScale = Stats.MaxCrew / 80.0, hullScale = Stats.Durability / 400.0;

        // 물과 식량
        // 운용 스킬이 물과 식량을 아낀다
        double ration = Crew * Rules.RationPerCrewDay * days * (1 - Math.Min(0.5, Bonus("Ration"))) * AideRation;
        Water = Math.Max(0, Water - ration);
        Food = Math.Max(0, Food - ration);
        bool starving = Water <= 0 || Food <= 0;
        if (starving && !_starvingSaid)
        {
            Say(Water <= 0 ? Text(TextNoWater, "물이 바닥났습니다.") : Text(TextNoFood, "식량이 바닥났습니다."));
            _starvingSaid = true;
        }
        if (!starving) _starvingSaid = false;

        double fatigueBefore = Fatigue;
        Fatigue = Math.Min(100, Fatigue + (Rules.FatiguePerDay + (starving ? Rules.FatigueWhenStarving : 0)) * days * AideFatigue);
        if (starving)
        {
            Crew -= Crew * 0.04 * days * loss;
            TrainEffect("CrewLoss", 40 * days);
        }
        if (Fatigue >= 100)
        {
            if (fatigueBefore < 100) Say(Text(TextCollapse, "피로가 극에 달해 선원들이 쓰러지고 있습니다!"));
            Crew -= 3 * days * crewScale;
        }

        // 날씨
        if (Weather == Weather.Storm)
        {
            _stormDays -= days;
            if (Sail > 0 && !StormProof)
            {
                // 내파가 문턱에 가까울수록 덜 다친다(절반까지)
                double rough = 1 - 0.5 * Math.Clamp(Stats.WaveResist / (double)Math.Max(1, Rules.StormWaveResist), 0, 1);
                Durability -= Rules.StormDurabilityPerDay * hullScale * days * Sail / SailSteps * PartDamage * (1 - Math.Min(0.8, Option("Storm"))) * rough;
                Crew -= 6 * days * loss * crewScale * rough;
                TrainEffect("CrewLoss", 40 * days);
            }
            if (_stormDays <= 0)
            {
                Weather = Weather.Clear;
                Say(Text(TextStormOver, "폭풍이 지나간 것 같습니다."));
            }
        }
        else if (Roll(Rules.StormChancePerDay * Climate.Storm, days)) StartStorm();      // 폭풍과 비의 잦기는 해역마다 다르다
        else if (Roll(0.9, days))
        {
            double rain = Math.Clamp(Climate.Rain, 0, 0.7), draw = _random.NextDouble();
            Weather = draw < rain ? Weather.Rain : draw < rain + 0.28 ? Weather.Cloudy : Weather.Clear;
        }

        // 벌어진 재해의 피해와 끝
        foreach (var disaster in Disasters.ToList())
        {
            var d = disaster.Data;
            disaster.Days += days;
            Durability -= d.DurabilityPerDay * hullScale * days * PartDamage;
            Crew -= d.CrewPerDay * days * loss * crewScale;
            if (d.CrewPerDay > 0) TrainEffect("CrewLoss", 40 * days);
            Food = Math.Max(0, Food - d.FoodPerDay * days);
            Water = Math.Max(0, Water - d.WaterPerDay * days);
            Fatigue = Math.Min(100, Fatigue + d.FatiguePerDay * days);
            if (d.DurationDays > 0 && disaster.Days >= d.DurationDays) End(disaster);
        }

        // 새 재해
        bool nearLand = IsNearLand();
        foreach (var data in Data.Disasters)
        {
            if (DaysAtSea < data.MinDays || Fatigue < data.MinFatigue || (data.NearLand && !nearLand)) continue;
            if (data.NearLand && Knots < 3) continue;        // 서 있는 배는 암초에 걸리지 않는다
            if (Roll(data.ChancePerDay * PartLuck * AideLuck * (1 - Math.Min(0.6, Option("Luck"))), days)) Begin(data);
        }

        if ((Durability <= 0 || Crew < 1) && !UseLifebuoy()) Wreck();
    }

    /// <summary>하루 확률 p 인 일이 days 동안에 일어났는가.</summary>
    private bool Roll(double chancePerDay, double days) =>
        chancePerDay > 0 && _random.NextDouble() < 1 - Math.Pow(1 - Math.Min(chancePerDay, 0.999), days);

    private bool IsNearLand()
    {
        for (int i = 0; i < 8; i++)
        {
            double angle = i * Math.PI / 4;
            if (Map.IsLand(ShipX + Math.Sin(angle) * NearLandReach, ShipY - Math.Cos(angle) * NearLandReach)) return true;
        }
        return false;
    }

    /// <summary>배가 못 쓰게 되거나 선원이 다 떠났다 — 가장 가까운 도시로 떠밀려 간다.</summary>
    private void Wreck()
    {
        var nearest = Data.Cities.Where(c => c.SeaX != 0 || c.SeaY != 0)
            .MinBy(c => Math.Pow(WorldMap.DeltaX(ShipX, c.SeaX), 2) + Math.Pow(c.SeaY - ShipY, 2)) ?? City;
        int lost = (int)(Money * Rules.WreckMoneyLoss);
        Money -= lost;
        WreckText = (Durability <= 0 ? Text(3038, "선박이 항해불능상태가 되었습니다!") : Text(3037, "선원이 전멸했습니다!")) +
                    $"\n\n난파하여 {nearest.Name}(으)로 떠밀려 왔다.\n수습하는 데 {lost:N0} 두캇이 들었다.";

        Durability = Math.Max(Durability, Stats.Durability * 0.3);
        Crew = Math.Max(Crew, Stats.MinCrew);
        Water = Math.Max(Water, 10);
        Food = Math.Max(Food, 10);
        MoorAt(nearest);
        RestInPort();
        Say($"난파하여 {nearest.Name}에 떠밀려 왔다.");
        Dialog = Dialog.Wreck;
    }
}
