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
    /// <summary>지난 하루에 줄어든 물 · 식량의 양과 그것을 알린 때(Clock) — 화면이 몇 초 동안 보인다.</summary>
    public (double Used, double At) RationNote { get; private set; } = (0, -100);
    /// <summary>대본용: 하루치 소모 알림을 지금 띄운다.</summary>
    public void RationNoteForTest() => RationNote = (7, Clock);
    private double _rationToday;
    private int _rationDay = -1;
    public double Food { get; private set; }
    public double Fatigue { get; private set; }
    /// <summary>대본용 — 피로를 정한다.</summary>
    public void SetFatigueForTest(double value) => Fatigue = Math.Clamp(value, 0, 100);
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
    /// <summary>실은 자재(2)를 버린다 — 물 · 식량처럼 적재화물 창에서.</summary>
    public void DumpSupply(int id, int amount) { int drop = Math.Min(SupplyCount(id), amount); if (drop <= 0) return; Supplies[id] = SupplyCount(id) - drop; Say($"자재 {drop}을(를) 버렸다."); }

    /// <summary>보급 스킬로 깎인 물·식량 값.</summary>
    public int WaterPrice => (int)Math.Ceiling(Rules.WaterPrice * (1 - Math.Min(0.5, Bonus("Discount"))));
    public int FoodPrice => (int)Math.Ceiling(Rules.FoodPrice * (1 - Math.Min(0.5, Bonus("Discount"))));

    public void BuyWater(int amount) => Water += Buy(amount, WaterPrice, MaxWaterNow - Water);
    public void BuyFood(int amount) => Food += Buy(amount, FoodPrice, MaxFoodNow - Food);

    /// <summary>실은 물 · 식량을 내린다(버린다) — 물자가 창고를 차지하니 교역품 자리를 내려면 덜어야 한다. 값은 돌려받지 않는다.</summary>
    public void DumpWater(int amount) { if (Water <= 0) return; double drop = Math.Min(Water, amount); Water -= drop; Say($"물 {drop:0}을(를) 버렸다."); }
    public void DumpFood(int amount) { if (Food <= 0) return; double drop = Math.Min(Food, amount); Food -= drop; Say($"식량 {drop:0}을(를) 버렸다."); }

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
        Life = MaxLife;
        if (Fatigue > 0) Say(Text(TextFatigueCured, "선원들이 기운을 되찾았다."));
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
        Studied("Cure", 1, 0, disaster.Data.Id);
        Say(Text((uint)disaster.Data.EndText, $"{disaster.Data.Name} — 풀렸다."));
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
        Studied("Storm");
        Say(Text(TextStorm, "폭풍이다! 돛을 편 채로 있으면 배가 뒤집힌다!"));
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
        if (data.Id == 1) BurnCargo();    }

    /// <summary>부관의 재해 방지 스킬이 들을 때 재해 확률에 곱하는 값 — 「크게 낮춰 주지만 100% 막지는 못한다」(사용자가 준 글). 크기 0.2 는 지은 값.</summary>
    public const double AideGuardFactor = 0.2;

    /// <summary>
    /// 화재가 나면 타는 교역품(섬유 · 직물 · 식료품 · 향신료 갈래)의 약 20%가 소실된다(사용자가 준 글, 2026-10-08).
    /// 갈래는 클라이언트 교역품 갈래의 이름으로 가른다. 「장비품 내구도 감소 · 돛 손상」은 크기가 글에 없어 안 넣었다.
    /// </summary>
    private void BurnCargo()
    {
        int burnt = 0;
        foreach (var (id, item) in Cargo.ToList())
        {
            if (Good(id) is not { } good || Data.GoodKinds.Find(k => k.Id == good.Kind)?.Name is not { } kind) continue;
            if (!(kind.Contains("섬유") || kind.Contains("직물") || kind.Contains("식료") || kind.Contains("향신료"))) continue;
            int lost = (int)Math.Round(item.Count * 0.2);
            if (lost <= 0) continue;
            item.Cost -= item.Cost * lost / item.Count;
            item.Count -= lost;
            burnt += lost;
            if (item.Count == 0) Cargo.Remove(id);
        }
        if (burnt > 0) Say($"화재로 교역품 {burnt}개가 불탔다.");
    }
    /// <summary>대본용(바다에서) — 항해 9일째와 10일째에 하루치 재해 굴림을 600번씩 돌려 화재가 난 횟수를 적는다(화재는 10일째부터 — MinDays).</summary>
    public void FireDaysForTest()
    {
        var keep = (Durability, Crew, Food, Water, Fatigue, SecondsAtSea, Weather);
        int[] fires = new int[2];
        for (int k = 0; k < 2; k++)
            for (int n = 0; n < 600; n++)
            {
                (Durability, Crew, Food, Water, Fatigue, Weather) = (keep.Durability, keep.Crew, Math.Max(keep.Food, 50), Math.Max(keep.Water, 50), 0, Weather.Clear);
                SecondsAtSea = (9 + k) * Settings.SecondsPerDay + 1;
                Disasters.Clear();
                UpdateHazards(Settings.SecondsPerDay);
                if (Disasters.Exists(d => d.Data.Id == 1)) fires[k]++;
            }
        Disasters.Clear();
        (Durability, Crew, Food, Water, Fatigue, SecondsAtSea, Weather) = keep;
        Say($"(시험) 하루치 굴림 600번에 화재 — 항해 9일째 {fires[0]}번 · 10일째 {fires[1]}번");
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
        double loss = (1 - Math.Min(0.75, Bonus("CrewLoss"))) * AideCrewLoss * (1 - Math.Min(0.6, Option("CrewLoss") + Study("CrewLoss"))) * (PrayerOn(2) ? 0.7 : 1);
        UpdateOptions(days);
        UpdateAides(days);
        PayCrew(days);
        UpdateBuild(days);
        // 재해 표의 피해는 선원 80명·내구 400짜리 배가 기준이다. 배 크기에 맞춰 늘리고 줄인다.
        double crewScale = Stats.MaxCrew / 80.0, hullScale = Stats.Durability / 400.0;

        // 물과 식량
        // 운용 스킬이 물과 식량을 아낀다
        double ration = Crew * Rules.RationPerCrewDay * days * (1 - Math.Min(0.5, Bonus("Ration"))) * (1 - Math.Min(0.3, FormBonus("FormKeep"))) * AideRation * (1 - Math.Min(0.3, GearEffect("SupplySave") * 0.03));
        Water = Math.Max(0, Water - ration);
        Food = Math.Max(0, Food - ration);
        // 날이 바뀔 때마다 그날 먹고 마신 양을 화면에 잠깐 띄운다(상태 줄의 물병 · 빵 곁)
        _rationToday += ration;
        if ((int)Today != _rationDay)
        {
            if (_rationDay >= 0 && _rationToday >= 0.5) RationNote = (_rationToday, Clock);
            (_rationDay, _rationToday) = ((int)Today, 0);
        }
        bool starving = Water <= 0 || Food <= 0;
        if (starving && !_starvingSaid)
        {
            Say(Water <= 0 ? Text(TextNoWater, "물이 바닥났습니다.") : Text(TextNoFood, "식량이 바닥났습니다."));
            _starvingSaid = true;
        }
        if (!starving) _starvingSaid = false;

        double fatigueBefore = Fatigue;
        Fatigue = Math.Min(100, Fatigue + (Rules.FatiguePerDay + (starving ? Rules.FatigueWhenStarving : 0)) * days * AideFatigue * PartFatigue * (PrayerOn(1) ? 0.7 : 1));
        if (starving)
        {
            Crew -= Crew * 0.04 * days * loss;
            TrainEffect("CrewLoss", 40 * days);
        }
        if (Fatigue >= 100)
        {
            if (fatigueBefore < 100) Say(Text(TextCollapse, "선원들이 지쳐서 쓰러지기 시작했다!"));
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
                Durability -= Rules.StormDurabilityPerDay * hullScale * days * Sail / SailSteps * PartDamage * (1 - Math.Min(0.8, Option("Storm") + Study("Storm"))) * rough;
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
            // 「내화벽」(원본 글: 화재로 인한 피해를 크게 줄일 수 있다) — 화재(1)의 피해만
            double fire = d.Id == 1 ? 1 - Math.Min(0.9, Option("FireGuard")) : 1;
            Durability -= d.DurabilityPerDay * hullScale * days * PartDamage * fire;
            Crew -= d.CrewPerDay * days * loss * crewScale * fire;
            if (d.CrewPerDay > 0) TrainEffect("CrewLoss", 40 * days);
            Food = Math.Max(0, Food - d.FoodPerDay * days);
            Water = Math.Max(0, Water - d.WaterPerDay * days);
            Fatigue = Math.Min(100, Fatigue + d.FatiguePerDay * days);
            if (d.DurationDays > 0 && disaster.Days >= d.DurationDays) End(disaster);
        }

        SeaEvents(days, hullScale);

        // 새 재해
        bool nearLand = IsNearLand();
        foreach (var data in Data.Disasters)
        {
            if (DaysAtSea < data.MinDays || Fatigue < data.MinFatigue || (data.NearLand && !nearLand)) continue;
            if (data.NearLand && Knots < 3) continue;        // 서 있는 배는 암초에 걸리지 않는다
            double aideGuard = AidePrevents(data.Id) ? AideGuardFactor : 1;      // 부관스킬(방화 …) — 그 담당을 맡은 부관이 확률을 크게 낮춘다(다 막지는 못한다 — 사용자가 준 글, 2026-10-08)
            // 「양호실」(원본 글: 쥐，비위생 발생을 높은 확률로 미연에 방지한다) — 쥐(4) · 비위생(14)만
            double clean = data.Id is 4 or 14 ? 1 - Math.Min(1, Option("Hygiene")) : data.Id == 2 ? 1 - Math.Min(1, Option("FloodGuard")) : 1;      // 「수밀격벽」 · 「배수펌프」: 침수(2)만
            // 「생존」의 설명: 「… 괴혈병，역병의 발생률 저하」 — 괴혈병(3) · 전염병(10)의 확률을 랭크마다 3% 낮춘다(60%까지 — 크기는 지은 값)
            if (data.Id is 3 or 10 && Data.Skills.Find(s => s.Name == "생존") is { } survival) clean *= 1 - Math.Min(0.6, Rank(survival.Id) * 0.03);
            if (Roll(data.ChancePerDay * aideGuard * clean * PartLuck * AideLuck * (1 - Math.Min(0.6, Option("Luck") + Study("Luck") + GearEffect("Luck") * 0.03)) * (PrayerOn(0) ? 0.7 : 1), days)) Begin(data);
        }

        if ((Durability <= 0 || Crew < 1) && !UseLifebuoy()) Wreck();
    }

    /// <summary>대본용: 하루치 확률을 스무 날치로 굴려 바다의 일들을 일으킨다.</summary>
    public void SeaEventsForTest() { Knots = Math.Max(Knots, 5); SeaEvents(20, Stats.Durability / 400.0); }

    private bool HasOption(string name) =>
        Data.OptionSkills.Find(o => o.Name == name) is { } option && (Work.Skills.Contains(option.SkillId) || Work.Dedicated == option.SkillId) && OptionValid(option);

    /// <summary>
    /// 한 번 치고 가는 바다의 일 — 높은 파도 · 측면의 파도 · 돌풍. 알림 글은 원본 화면 글(3045 ~ 3047)이고,
    /// 옵션 스킬 「내파장갑」이 파도를, 「내풍마스트」가 돌풍을 막는다는 것도 원본 글(3472 ~ 3481)에서 읽은 것이다. 잦기와 피해는 지은 값이다.
    /// 배가 달리고 있을 때만 일어난다.
    /// </summary>
    private void SeaEvents(double days, double hullScale)
    {
        if (Knots < 2) return;
        if (Clock < _veilUntil) return;        // 천사의 베일 — 폭풍 · 눈보라 말고의 자연재해(높은 파도 · 횡파 · 돌풍)를 막는다
        // 날씨가 궂을수록 잦다
        double rough = Weather switch { Weather.Storm => 4, Weather.Rain => 2, Weather.Cloudy => 1.3, _ => 1 };
        if (Roll(0.05 * rough, days))
        {
            if (SteamOn) Say("증기선 — 높은 파도를 헤치고 나아간다.");      // 증기선 효과: 높은 파도 · 횡파 · 돌풍을 막는다(사용자가 준 글)
            else if (HasOption("내파장갑")) Say(Text(3475, "내파장갑으로 높은 파도를 회피했습니다"));
            else
            {
                // 내파가 높을수록 덜 다친다
                double hit = 25 * hullScale * Math.Clamp(1.2 - Stats.WaveResist / 30.0, 0.2, 1.2);
                Durability -= hit;
                Say($"{Text(3045, "갑자기 높은 파도가 덮쳤습니다.")} (내구 −{hit:0})");
                Cues.Enqueue("Warn");
            }
        }
        if (Roll(0.05 * rough, days))
        {
            if (SteamOn) Say("증기선 — 측면의 파도에도 진로가 흔들리지 않는다.");
            else if (HasOption("내파장갑")) Say(Text(3481, "내파장갑으로 측면 파도를 회피하였습니다"));
            else
            {
                double shove = (_random.NextDouble() < 0.5 ? -1 : 1) * (0.25 + _random.NextDouble() * 0.35);
                Heading = TargetHeading = Normalize(Heading + shove);
                Say(Text(3046, "옆에서 친 파도에 뱃머리가 돌아갔다!"));
                Cues.Enqueue("Warn");
            }
        }
        if (Sail > 0 && Roll(0.04 * rough, days))
        {
            if (SteamOn) Say("증기선 — 돌풍에도 속도가 죽지 않는다.");
            else if (HasOption("내풍마스트")) Say(Text(3473, "내풍마스트로 돌풍을 회피했습니다"));
            else
            {
                // 돛을 편 만큼 다친다 — 돛대가 흔들려 속도가 죽는다
                double hit = 10 * hullScale * Sail / SailSteps;
                Durability -= hit;
                Knots *= 0.4;
                Say($"{Text(3047, "돌풍을 맞았습니다.")} (내구 −{hit:0})");
                Cues.Enqueue("Warn");
            }
        }
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
    private void Wreck(string? beatenBy = null, bool monster = false)
    {
        var nearest = Data.Cities.Where(c => c.SeaX != 0 || c.SeaY != 0)
            .MinBy(c => Math.Pow(WorldMap.DeltaX(ShipX, c.SeaX), 2) + Math.Pow(c.SeaY - ShipY, 2)) ?? City;
        // 해전에서 지면 가진 돈의 5%(5만 두캇까지)를 털린다 — 난파보다 가볍다(지은 값)
        int lost = monster ? 0 : (int)((beatenBy != null ? Math.Min(Money * 0.05, 50_000) : Money * Rules.WreckMoneyLoss) * (1 - Math.Min(1, Option("Lifeboat"))));      // 구명정이 잃는 돈을 줄인다
        Money -= lost;
        string insured = PayInsurance(lost);
        WreckText = (Durability <= 0 ? Text(3038, "배가 더는 나아가지 못한다!") : Text(3037, "선원이 전멸했습니다!")) +
                    (monster ? $"\n\n{beatenBy}에게 당해 {nearest.Name}(으)로 떠밀려 왔다."
                     : beatenBy != null ? $"\n\n{beatenBy}에게 져서 {nearest.Name}(으)로 끌려 왔다.\n{lost:N0} 두캇을 빼앗겼다."
                                      : $"\n\n난파하여 {nearest.Name}(으)로 떠밀려 왔다.\n수습하는 데 {lost:N0} 두캇이 들었다.") + insured;

        Durability = Math.Max(Durability, Stats.Durability * 0.3);
        Crew = Math.Max(Crew, Stats.MinCrew);
        Water = Math.Max(Water, 10);
        Food = Math.Max(Food, 10);
        MoorAt(nearest);
        RestInPort();
        Say(beatenBy != null ? $"{beatenBy}에게 져서 {nearest.Name}에 끌려 왔다." : $"난파하여 {nearest.Name}에 떠밀려 왔다.");
        Dialog = Dialog.Wreck;
    }
}
