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

    // 눌러 쓰는 스킬 — 뒤의 셋(재해 풀기 · 찾기 · 구조)은 원본에서도 눌러 쓰는 스킬이라 스킬 사용 창(F2)과 퀵슬롯에 선다(사용자, 2026-10-07)
    private static readonly string[] ActiveEffects = ["Survey", "Procure", "Fish", "Repair", "Rest", "Speed", "Turn", "Gather", "Cure", "Find", "Rescue"];

    /// <summary>
    /// 켜 두는 스킬 — 돛 조종 · 조타 · 낚시 · 조달. 원본처럼 켜면 한동안 켜져 있다가 꺼지고(화면 오른쪽 가운데에 그림이 뜬다),
    /// 켜져 있는 동안 돛 조종 · 조타는 효과가 걸리고 낚시 · 조달은 일정한 사이를 두고 저절로 된다.
    /// 켜져 있는 시간 · 사이 · 한꺼번에 켜는 수는 지은 값이다.
    /// </summary>
    private static readonly string[] Sustained = ["Speed", "Turn", "Fish", "Procure", "Survey", "Gather"];
    public const int MaxSkillsOn = 3;
    private const double OnSeconds = 180, TickSeconds = 15;
    private readonly Dictionary<int, (double Until, double Next)> _skillOn = new();
    private static readonly string[] Fishes = ["고등어", "정어리", "전갱이", "청어", "대구", "도미", "청상아리", "가다랑어"];

    public bool SkillOn(int skillId) => _skillOn.ContainsKey(skillId);

    /// <summary>켜 둔 스킬을 끈다(Ctrl+클릭).</summary>
    public void StopSkill(int skillId)
    {
        if (_skillOn.Remove(skillId)) Say($"{SkillName(skillId)} 스킬을 껐다.");
    }

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
            else if (rule.Effect == "Gather") SeaGather(rule);
        }
    }

    // 바다에서의 채집 — 원본에 「본 해역 해상에서 낚시，채집 실행」(화면 글 42084)이라는 말이 있어 바다에서도 된다는 것만 안다.
    // 무엇이 건져지는지는 지은 것: 해수 · 해초 · 참다시마, 랭크 5부터 열에 하나는 굴조개 · 진주조개. 한 번에 1 + 랭크 ÷ 4 개
    private static readonly string[] SeaFinds = ["해수", "해초", "참다시마"], SeaRareFinds = ["굴조개", "진주조개"];

    private void SeaGather(SkillRuleData rule)
    {
        int rank = Rank(rule.SkillId);
        if (HoldFree <= 0) { Say("창고가 가득 차 채집한 것을 실을 수 없다."); return; }
        string name = rank >= 5 && _random.NextDouble() < 0.1 ? SeaRareFinds[_random.Next(SeaRareFinds.Length)] : SeaFinds[_random.Next(SeaFinds.Length)];
        if (Data.Goods.Find(g => g.Name == name) is not { } good) return;
        int count = Math.Min(HoldFree, 1 + rank / 4);
        GiveGood(good, count);
        Say($"{good.Name} {count}개를 건져 올렸다.");
        Train(rule.SkillId, 15);
    }

    /// <summary>낚시 · 조달 한 번.</summary>
    private void Gather(SkillRuleData rule)
    {
        int rank = Rank(rule.SkillId);
        if (rule.Effect == "Fish")
        {
            CatchFish(rule, "");
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
    /// <summary>
    /// 낚시 한 번 — 낚은 물고기는 식량이 아니라 **선창의 교역품**(물고기 갈래, 번호 1601000 ~)으로 실린다(사용자, 2026-10-07 — 원본도 그렇다).
    /// 무엇이 낚이는가(이름 여덟 가지 가운데 아무거나)와 마릿수(1 ~ 3 + 랭크 몫, 배가 빠르면 반)는 지은 값이다.
    /// </summary>
    private void CatchFish(SkillRuleData rule, string lead)
    {
        // 배가 빠르면 낚싯줄을 드리우기 어렵다
        int count = (int)Math.Round((1 + _random.NextDouble() * 2 + Rank(rule.SkillId) * rule.PerRank) * (Knots > 8 ? 0.5 : 1));
        count = Math.Min(count, HoldFree);
        string fish = Fishes[_random.Next(Fishes.Length)];
        var good = Data.Goods.Find(g => g.Name == fish && g.Id is >= 1_601_000 and < 1_602_000) ?? Data.Goods.Find(g => g.Id is >= 1_601_000 and < 1_602_000);
        if (HoldFree <= 0) { Say($"{lead}선창이 가득 차 낚은 것을 실을 수 없다."); return; }
        if (count < 1 || good == null) { Say($"{lead}아무것도 낚지 못했다."); return; }
        GiveGood(good, count);              // 「○○ N개를 실었다」는 글은 그쪽이 낸다
        Train(rule.SkillId, 20);
    }

    // ── 전용: 선창의 교역품을 물 · 식량 · 자재로 돌린다 ──
    // 무엇이 무엇으로 몇이 되는가는 이용자 사이트(gvdb 아이템 설명의 「食料への転用量：3」 · 「水への転用量：1」)의 값(data\extracted\conversions.json).
    // 거기 없는 물고기(낚은 것)는 한 마리가 식량 1 — 지은 값. 탄약 · 포탄으로의 전용은 이 게임에 그 물자가 없어 뺐다.
    public static readonly string[] ConvertNames = ["물", "식량", "자재"];

    /// <summary>그 교역품이 돌아가는 물자(0 물 · 1 식량 · 2 자재)와 하나에 얻는 양 — 못 돌리면 null.</summary>
    public (int Kind, int Each)? ConvertOf(GoodData good) =>
        Data.Conversions.TryGetValue(good.Id, out var to) && to.Length >= 2 && to[0] is >= 0 and <= 2 ? (to[0], to[1])
        : good.Id is >= 1_601_000 and < 1_602_000 ? (1, 1) : null;

    /// <summary>실은 것을 모두(들어가는 만큼) 물자로 돌린다.</summary>
    public void ConvertGood(GoodData good)
    {
        if (ConvertOf(good) is not { } to || !Cargo.TryGetValue(good.Id, out var item) || item.Count <= 0) return;
        double room = to.Kind switch { 0 => Rules.MaxWater - Water, 1 => Rules.MaxFood - Food, _ => 9999 };
        int count = (int)Math.Min(item.Count, Math.Ceiling(room / to.Each));
        if (count <= 0) { Say($"{ConvertNames[to.Kind]}이(가) 가득 차 있다."); Cues.Enqueue("Error"); return; }
        double gain = Math.Min(room, count * to.Each);
        item.Cost -= item.Cost * count / item.Count;
        if ((item.Count -= count) <= 0) Cargo.Remove(good.Id);
        if (to.Kind == 0) Water += gain; else if (to.Kind == 1) Food += gain; else Supplies[RepairSupply] = SupplyCount(RepairSupply) + (int)gain;
        Cues.Enqueue("Buy");
        Say($"{good.Name} {count}개를 {ConvertNames[to.Kind]} {gain:0}(으)로 돌렸다.");
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
            return !SkillOn(rule.SkillId) && _skillOn.Count >= MaxSkillsOn ? $"스킬은 {MaxSkillsOn}개까지 켠다"
                : Vigour < VigourCost(rule) ? $"행동력이 모자란다 ({Vigour:0}/{VigourCost(rule)})" : null;
        if (SkillWait(rule) > 0) return $"{SkillWait(rule):0}초 뒤";
        if (Vigour < VigourCost(rule)) return $"행동력이 모자란다 ({Vigour:0}/{VigourCost(rule)})";
        return rule.Effect switch
        {
            "Procure" when Water >= Rules.MaxWater => "물통이 가득하다",
            "Fish" when HoldFree <= 0 => "선창이 가득하다",
            "Repair" when Durability >= Stats.Durability => "고칠 데가 없다",
            "Repair" when SupplyCount(RepairSupply) <= 0 => "수리용 통이 없다",
            "Rest" when Food < 5 => "식량이 모자란다",
            "Rest" when Fatigue <= 0 => "선원들이 지치지 않았다",
            "Cure" when !Disasters.Exists(d => rule.Targets.Contains(d.Data.Id)) => "풀 재해가 없다",
            "Find" when !SeaSiteInReach() => "찾을 것이 가까이 없다",
            "Rescue" => "해전에서 이기면 저절로 듣는다",
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
            // 켜 두는 스킬: 켜져 있을 때 다시 쓰면 끄는 것이 아니라 새로 쓴다 — 남은 시간이 처음부터 다시 흐른다(사용자, 2026-10-07 — 원본이 그렇다)
            bool again = _skillOn.TryGetValue(rule.SkillId, out var running);
            _skillOn[rule.SkillId] = (Clock + OnSeconds * BoostExtend, again ? running.Next : Clock + TickSeconds);
            SpendVigour(VigourCost(rule));
            Fatigue = Math.Min(100, Fatigue + 1);
            Say($"{name} 스킬을 사용했다.");
            Cues.Enqueue(rule.Effect == "Speed" ? "Sail" : "Skill");
            return;
        }
        // 재해 풀기(구제 따위)와 찾기(인식 · 탐색 · 생태 조사)는 제 길이 따로 있다 — 그쪽이 행동력과 숙련을 셈한다
        if (rule.Effect == "Cure")
        {
            if (Disasters.Find(d => rule.Targets.Contains(d.Data.Id)) is not { } trouble) return;
            _skillReady[rule.SkillId] = Clock + 5;
            SpendVigour(VigourCost(rule));
            Cues.Enqueue("Skill");
            Say($"{name} 스킬을 사용했다.");
            CureWithSkill(trouble);
            return;
        }
        if (rule.Effect == "Find") { Cues.Enqueue("Skill"); SearchAtSea(); return; }
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
                Fatigue = Math.Min(100, Fatigue + 2);
                CatchFish(rule, $"{name}: ");
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
