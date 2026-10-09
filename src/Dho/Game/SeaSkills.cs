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
            double water = Math.Min(MaxWaterNow - Water, (rain ? 6 : 1) + rank * rule.PerRank * (rain ? 1 : 0.3));
            if (water <= 0) return;
            Water += water;
            Say(rain ? $"빗물을 받았다. (물 {water:0.#})" : $"해수를 걸러 물을 얻었다. (물 {water:0.#})");
            Train(rule.SkillId, rain ? 25 : 8);
        }
    }
    // ── 낚시 ──
    // 규칙은 사용자가 가리킨 글(인벤 「낚시 만랭 해보자」, 2008 — 낚시 15랭 이용자의 정리)에서:
    //  · 어종마다 랭크가 있다: 1 정어리 · 청어 · 고등어 · 꽁치 / 2 전갱이 · 참돔 / 3 대구 / 4 연어 / 5 가다랑어 / 8 다랑어 / 12 백상어 · 청상아리 · 청새치.
    //  · 랭크가 3 오를 때마다 한 번에 낚이는 마릿수가 하나씩 는다(1랭 어종: 3랭 2마리 · 6랭 3 · 9랭 4 · 12랭 5 · 15랭 6; 다랑어: 12랭 2 · 15랭 3; 상어: 15랭 2).
    //    부스터를 넣은 17랭에서도 다랑어 3 · 상어 2 그대로라 마릿수는 부스터 뺀 랭크로 센다.
    //  · 제 랭크에 딱 맞는 어종은 떼가 감당 못 할 만큼 걸려 「놓쳤다」가 자주 나온다.
    //  · 어장 하나에서 올라오는 어종은 셋이고, 랭크가 높을수록 그 가운데 높은 어종이 더 많이 올라온다(베르겐: 7랭 연어 6 : 청어 2 : 황어 2, 15랭 9 : 1).
    //  · 1랭 어종은 어디서나, 전갱이는 지중해, 참돔은 서아프리카, 대구는 코펜하겐 ~ 오슬로, 연어는 베르겐 · 에딘버러,
    //    가다랑어는 리스본 ~ 마데이라 · 타마타브, 다랑어는 마데이라 · 시라쿠사 · 세우타 · 소팔라 · 라스팔마스 · 산후안.
    // 글의 「포인트」(해역 안의 한 자리)는 이 게임에 없어 해역 하나를 통째로 어장으로 본다(줄인 것).
    private static readonly Dictionary<string, int> FishRanks = new()
    {
        ["정어리"] = 1, ["청어"] = 1, ["고등어"] = 1, ["꽁치"] = 1, ["황어"] = 1,      // 황어의 랭크는 글에 없다(짐작)
        ["전갱이"] = 2, ["참돔"] = 2, ["대구"] = 3, ["연어"] = 4, ["가다랑어"] = 5, ["다랑어"] = 8,
        ["백상어"] = 12, ["청상아리"] = 12, ["청새치"] = 12,
    };
    // 해역 → 올라오는 어종 셋(높은 것부터). 글에 셋이 다 적힌 곳은 베르겐 · 라스팔마스 · 산후안뿐이고, 나머지의 둘째 · 셋째는 짐작이다
    private static readonly Dictionary<string, string[]> FishGrounds = new()
    {
        ["노르웨이 해"] = ["연어", "청어", "황어"], ["브리튼 섬 북부"] = ["연어", "청어", "꽁치"],
        ["유틀란드 반도 앞바다"] = ["대구", "청어", "꽁치"],
        ["카나리아 앞바다"] = ["청상아리", "다랑어", "고등어"], ["산후안 앞바다"] = ["백상어", "청상아리", "다랑어"],
        ["마데이라 앞바다"] = ["다랑어", "가다랑어", "고등어"], ["리스본 앞바다"] = ["가다랑어", "정어리", "고등어"],
        ["지브롤터 해협"] = ["다랑어", "전갱이", "고등어"], ["티레니아 해"] = ["다랑어", "전갱이", "정어리"], ["이오니아 해"] = ["다랑어", "전갱이", "정어리"],
        ["발레아레스제도 앞바다"] = ["전갱이", "정어리", "고등어"], ["리그리아 해"] = ["전갱이", "정어리", "고등어"], ["아드리아 해"] = ["전갱이", "정어리", "고등어"],
        ["동 지중해"] = ["전갱이", "정어리", "고등어"], ["흑해"] = ["전갱이", "정어리", "고등어"],
        ["곡물해안 앞바다"] = ["참돔", "고등어", "정어리"], ["황금해안 앞바다"] = ["참돔", "고등어", "정어리"], ["기니 만"] = ["참돔", "고등어", "정어리"],
        ["마다가스카르 앞바다"] = ["가다랑어", "고등어", "정어리"], ["모잠비크해협"] = ["다랑어", "청상아리", "고등어"],
    };
    private static readonly string[] CommonFish = ["정어리", "청어", "고등어", "꽁치"];

    /// <summary>지금 있는 해역에서 올라오는 어종(높은 것부터) — 어장이 아니면 1랭 어종 넷.</summary>
    public string[] FishHere => FishGrounds.TryGetValue(SeaName, out var ground) ? ground : CommonFish;

    /// <summary>그 어종을 한 번에 몇 마리까지 낚는가 — 1 + (부스터 뺀 랭크 − 어종 랭크 + 1) ÷ 3. 글의 마릿수들에 맞춘 식이다(「12랭에 연어 5마리」만 4 로 어긋난다).</summary>
    public int FishMost(int skillId, string fish) =>
        FishRanks.TryGetValue(fish, out int need) && Math.Max(Rank(skillId), _baitRank) >= need ? 1 + Math.Max(0, Math.Max(Skills.TryGetValue(skillId, out var state) ? state.Rank : 0, _baitRank) - need + 1) / 3 : 0;

    // 낚시밥으로 낚는 동안의 랭크(스킬이 없거나 낮아도 이 랭크로 낚는다) — 낚시밥 1, 고급 낚시밥은 「숙달된 낚시꾼에 필적」이라는 글뿐이라 10 으로 지었다
    private int _baitRank;

    /// <summary>낚시밥을 써서 한 번 낚는다 — 바다나 항구에서. 못 쓰면 false(밥이 안 준다).</summary>
    public bool FishWithBait(int rank)
    {
        if (Data.SkillRules.Find(r => r.Effect == "Fish") is not { } rule) return false;
        if (HoldFree <= 0) { Say("선창이 가득 차 낚은 것을 실을 수 없다."); Cues.Enqueue("Error"); return false; }
        _baitRank = rank;
        try { CatchFish(rule, "낚시밥을 던졌다. "); }
        finally { _baitRank = 0; }
        return true;
    }

    /// <summary>낚시 한 번 — 낚은 물고기는 식량이 아니라 **선창의 교역품**(물고기 갈래, 번호 1601000 ~)으로 실린다(사용자, 2026-10-07 — 원본도 그렇다).</summary>
    private void CatchFish(SkillRuleData rule, string lead)
    {
        if (HoldFree <= 0) { Say($"{lead}선창이 가득 차 낚은 것을 실을 수 없다."); return; }
        int rank = Math.Max(Rank(rule.SkillId), _baitRank);
        if (TryFishFind(rank, lead)) { Train(rule.SkillId, 20 * rank); return; }      // 그 자리의 낚시 발견물 — 숙련도는 지은 값
        // 내 랭크로 낚을 수 있는 어종만 — 가장 높은 것이 올라오는 몫은 랭크가 높을수록 크다(7랭 60% → 15랭 90% 를 곧게 이은 것, 그 밖의 랭크는 짐작)
        var able = FishHere.Where(f => FishMost(rule.SkillId, f) > 0).ToList();
        if (able.Count == 0) { Say($"{lead}아무것도 낚지 못했다."); return; }
        double top = Math.Clamp(0.6 + 0.0375 * (rank - FishRanks[able[0]] - 3), 0.34, 0.9);
        string fish = able.Count == 1 || _random.NextDouble() < top ? able[0] : able[1 + _random.Next(able.Count - 1)];
        if (fish == "청상아리" && _random.Next(2) == 0) fish = "청새치";      // 청상아리와 청새치는 한 무리로 붙어 다닌다(글)
        int most = FishMost(rule.SkillId, fish);
        // 떼의 크기 — 1 ~ 감당할 수 있는 수 + 1 마리가 고르게 걸린다고 본다(지은 값). 감당 못 하면 줄이 끊긴다
        int school = 1 + _random.Next(most + 1);
        if (school > most) { Say($"{lead}{fish}을(를) 놓쳤다. 낚싯줄이 끊어졌다."); Train(rule.SkillId, 2); return; }
        int count = Math.Min(school, HoldFree);
        if (Data.Goods.Find(g => g.Name == fish && g.Id is >= 1_601_000 and < 1_602_000) is not { } good) { Say($"{lead}아무것도 낚지 못했다."); return; }
        GiveGood(good, count);              // 「○○ N개를 실었다」는 글은 그쪽이 낸다
        Studied("Outdoor");                 // 연구 과제 「야외 활동」 — 조달，낚시，채집으로 교역품을 입수
        // 숙련도 — 글에는 차례만 있다(다랑어 2 > 연어 5, 다랑어 2 > 상어 1, 여러 마리 > 한 마리). 그 차례가 나오게 지은 값: 5 × 어종 랭크^1.7 × 마릿수
        Train(rule.SkillId, 5 * Math.Pow(FishRanks[fish], 1.7) * count);
    }
    /// <summary>대본용 — 낚시를 여러 번 던진다.</summary>
    public void FishForTest(int times)
    {
        if (Data.SkillRules.Find(r => r.Effect == "Fish") is not { } rule) return;
        Say($"(시험) {SeaName} — {string.Join(" · ", FishHere.Select(f => $"{f} 최대 {FishMost(rule.SkillId, f)}"))}");
        for (int i = 0; i < times; i++) CatchFish(rule, "");
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
        double room = to.Kind switch { 0 => MaxWaterNow - Water, 1 => MaxFoodNow - Food, _ => 9999 };
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
            "Procure" when Water >= MaxWaterNow => "물통이 가득하다",
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
                double water = Math.Min(MaxWaterNow - Water, (rain ? 6 : 1) + rank * rule.PerRank * (rain ? 1 : 0.3));
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
                // 「주연」의 설명: 「주류 교역품을 선원에게 대접하여 피로도를 회복시킨다. 선원의 욕구 불만도 해소 가능」 — 욕구불만(재해 18)을 푼다
                if (Disasters.Find(d => d.Data.Id == 18) is { } craving) End(craving);
                break;
        }
    }
}
