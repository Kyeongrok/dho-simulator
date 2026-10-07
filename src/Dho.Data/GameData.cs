using System.Buffers.Binary;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Dho.Data;

/// <summary>모험 의뢰 한 건. 조합 의뢰 목록은 클라이언트 자료에 없어서(서버가 내려 주던 것) 직접 짓는다.</summary>
public sealed class QuestData
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Client { get; set; } = "모험가 조합";
    /// <summary>조합 마스터가 의뢰를 내밀며 하는 말.</summary>
    public string Request { get; set; } = "";
    /// <summary>무엇을 하면 되는지 한 줄.</summary>
    public string Hint { get; set; } = "";
    /// <summary>의뢰를 받고 보고하는 도시(도시 표 id).</summary>
    public int CityId { get; set; }
    /// <summary>찾아낼 발견물(발견물 표 id).</summary>
    public int DiscoveryId { get; set; }
    /// <summary>상륙할 곳(상륙지 표 id). 자리는 상륙지의 X·Y.</summary>
    public int LandingId { get; set; }
    /// <summary>바다에서 찾는 의뢰면 그 해역(해역 표 id) — 0 이면 상륙지에서 찾는다.</summary>
    public int SeaZone { get; set; }
    /// <summary>바다의 한 자리에서 찾는 의뢰면 그 세계 좌표(大航海時代DB 의 의뢰 차례 글에서) — 0 이면 없다.</summary>
    public int SeaX { get; set; }
    public int SeaY { get; set; }
    /// <summary>찾는 스킬(인식 · 탐색 · 생태 조사)에 적힌 랭크 — 0 이면 Rank 를 쓴다.</summary>
    public int FindRank { get; set; }
    /// <summary>도시 안에서 찾는 의뢰면 그 도시(도시 표 id) — 그 도시의 항구에서 「의뢰 탐색」을 누른다. 0 이면 아니다.</summary>
    public int SearchCity { get; set; }
    /// <summary>의뢰를 받는 데 필요한 언어(스킬 번호) — 진짜 의뢰의 값.</summary>
    public List<int> Languages { get; set; } = [];
    /// <summary>밤에만, 폭풍이 아닐 때 찾는다(별 따위) — 진짜 의뢰의 값.</summary>
    public bool NightOnly { get; set; }
    /// <summary>상륙했을 때 나오는 글.</summary>
    public string LandingText { get; set; } = "";
    public int Advance { get; set; }
    public int Reward { get; set; }
    /// <summary>찾는 스킬과 감정 스킬에 필요한 랭크. 0 이면 스킬 없이도 된다.</summary>
    public int Rank { get; set; } = 1;
}

public sealed class CityData
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>0 본거지, 1 영지, 2 동맹항 …</summary>
    public int Kind { get; set; }
    public int Nation { get; set; }
    public int Culture { get; set; }
    /// <summary>뭍 위의 도시 자리(세계 좌표).</summary>
    public int X { get; set; }
    public int Y { get; set; }
    /// <summary>항구 앞바다 — 입항·출항하는 자리.</summary>
    public int SeaX { get; set; }
    public int SeaY { get; set; }
    /// <summary>항구 장면 번호(<c>0002</c> 파일 이름이 된다). 0 이면 없음.</summary>
    public int PortScene { get; set; }
    /// <summary>시내 장면 번호(2000 + 도시 id 꼴). 0 이면 없음.</summary>
    public int TownScene { get; set; }
    /// <summary>들어갈 수 있는 건물 이름들(장면 표의 건물 줄)을 쉼표로. 길드 사무소·빈집은 뺀다.</summary>
    public string Buildings { get; set; } = "";
    /// <summary>들어갈 수 있는 건물과 그 방 장면 번호 — "이름=번호" 를 쉼표로(장면 표의 건물 줄. 길드 사무소 · 빈집은 뺀다).</summary>
    public string Rooms { get; set; } = "";
    /// <summary>모험가조합 건물 안의 장면 번호(장면 표의 건물 줄). 0 이면 조합 건물이 없다.</summary>
    public int GuildScene { get; set; }
    /// <summary>배경음 번호(<c>0006\0000NN.bin</c>) — 장면 표의 시내 줄에 있다. 0 이면 없음.</summary>
    public int Music { get; set; }
}

public sealed class LandingData
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int City { get; set; }
    public int Region { get; set; }
    /// <summary>배를 대는 바다 자리(세계 좌표). 직접 찍은 것이 먼저고, 없으면 세계지도 표식(MapX · MapY)에서 가까운 바다를 찾아 쓴다. 0 이면 아직 없음.</summary>
    public int X { get; set; }
    public int Y { get; set; }
    /// <summary>클라이언트 세계지도 표식의 자리(표 103, 세계 좌표 — 뭍 위일 수 있다). 0 이면 표식이 없다.</summary>
    public int MapX { get; set; }
    public int MapY { get; set; }
    /// <summary>클라이언트의 뭍 탐색 지점(표 106): 관찰 지점의 수와 채집 지점의 갈래(1 ~ 5 — 갈래의 뜻은 모른다).</summary>
    public int ObservePoints { get; set; }
    public List<int> GatherKinds { get; set; } = [];
    /// <summary>X · Y 가 표식에서 셈한 것이다(직접 찍은 것이 아니다) — 찍은 자리 파일에는 안 적는다.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public bool FromMap { get; set; }
}

/// <summary>大航海時代DB 의 모험 의뢰 한 건 — 이름 · 글은 일본어 그대로다.</summary>
public sealed class QuestFact
{
    public string Title { get; set; } = "";
    public int DiscoveryId { get; set; }
    /// <summary>의뢰를 내는 도시들(도시 표 id).</summary>
    public List<int> Cities { get; set; } = [];
    public List<QuestSkill> Skills { get; set; } = [];
    public int Reward { get; set; }
    public int Advance { get; set; }
    /// <summary>바다에서 찾는 자리(세계 좌표) — 0 이면 차례 글에 좌표가 없다(뭍에서 찾거나 못 읽었다).</summary>
    public int X { get; set; }
    public int Y { get; set; }
    public string Steps { get; set; } = "";
    /// <summary>찾는 자리의 갈래 — 1 바다의 좌표, 2 상륙지, 3 도시 안, 0 못 읽음.</summary>
    public int Place { get; set; }
    public int LandingId { get; set; }
    public int TownId { get; set; }
    /// <summary>자리 4: 그 해역 안 어디서나 찾는다(해역 번호).</summary>
    public int SeaZone { get; set; }
    /// <summary>밤에만(거친 날씨가 아닐 때) 찾는다 — 차례 글의 「荒天以外の夜」.</summary>
    public bool Night { get; set; }
    public int Difficulty { get; set; }
}

public sealed class QuestSkill
{
    public string Name { get; set; } = "";
    public int Rank { get; set; }
}

/// <summary>위키(wikiwiki.jp/gvo 의 発見物 쪽)에서 채운 것.</summary>
public sealed class DiscoveryFact
{
    public int Id { get; set; }
    public string Japanese { get; set; } = "";
    /// <summary>難度 — 찾는 데 필요한 스킬 랭크.</summary>
    public int Difficulty { get; set; }
    /// <summary>発見方法 — 의뢰의 이름(일본어), 또는 「…の地図」.</summary>
    public string Method { get; set; } = "";
    /// <summary>서고의 지도로 찾는 것.</summary>
    public bool Map { get; set; }
}

public sealed class DiscoveryData
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int Kind { get; set; }
    public int Stars { get; set; }
    public int Exp { get; set; }
    public int Fame { get; set; }
}

public sealed class NamedData
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>해역이면 큰 바다 id.</summary>
    public int Group { get; set; }
    /// <summary>해역이면 배경음 번호.</summary>
    public int Music { get; set; }
    /// <summary>덧붙는 글(호칭의 설명 따위).</summary>
    public string Extra { get; set; } = "";
}

/// <summary>
/// 해상재해 한 가지. 이름과 알림 글은 클라이언트 화면 글 표(<c>dt000002</c>)의 id 로 가리키고,
/// 일어날 확률과 피해는 클라이언트에 없어(서버 몫) 직접 지은 값이다.
/// </summary>
public sealed class DisasterData
{
    public int Id { get; set; }
    /// <summary>개발도구에서 알아보기 위한 이름. 게임 화면에는 <see cref="StateText"/> 의 글이 나온다.</summary>
    public string Name { get; set; } = "";
    /// <summary>상태 이름 글 id(2037 괴혈병 …).</summary>
    public int StateText { get; set; }
    /// <summary>일어났을 때 알림 글 id(3055 …).</summary>
    public int StartText { get; set; }
    /// <summary>풀렸을 때 알림 글 id(3083 …).</summary>
    public int EndText { get; set; }
    /// <summary>바다에서 하루에 일어날 확률(0~1).</summary>
    public double ChancePerDay { get; set; }
    /// <summary>항해일수가 이만큼 지나야 일어난다.</summary>
    public int MinDays { get; set; }
    /// <summary>피로도가 이만큼 넘어야 일어난다.</summary>
    public int MinFatigue { get; set; }
    /// <summary>뭍 가까이(세계 좌표 12 안)에서만 일어난다.</summary>
    public bool NearLand { get; set; }
    /// <summary>저절로 풀리기까지의 날 수. 0 이면 대처해야 풀린다.</summary>
    public double DurationDays { get; set; }
    public double DurabilityPerDay { get; set; }
    public double CrewPerDay { get; set; }
    public double FoodPerDay { get; set; }
    public double WaterPerDay { get; set; }
    public double FatiguePerDay { get; set; }
    /// <summary>속도에 곱하는 값. 1 이면 그대로.</summary>
    public double SpeedFactor { get; set; } = 1;
    /// <summary>이 보급품을 쓰면 풀린다(보급품 id). 0 이면 없음.</summary>
    public int CureSupply { get; set; }
}

/// <summary>항구에서 사서 싣는 대처 물품.</summary>
public sealed class SupplyData
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int Price { get; set; }
}

/// <summary>배와 선원 살림 — 물·식량·피로·내구. 지은 값이다.</summary>
public sealed class VoyageRules
{
    public int StartWater { get; set; } = 60;
    public int StartFood { get; set; } = 60;
    public int MaxWater { get; set; } = 120;
    public int MaxFood { get; set; } = 120;
    /// <summary>선원 한 명이 하루에 먹는 물·식량.</summary>
    public double RationPerCrewDay { get; set; } = 0.04;
    public double FatiguePerDay { get; set; } = 3;
    /// <summary>물이나 식량이 떨어졌을 때 하루에 더 쌓이는 피로.</summary>
    public double FatigueWhenStarving { get; set; } = 18;
    public int WaterPrice { get; set; } = 20;
    public int FoodPrice { get; set; } = 35;
    public int RepairPricePerPoint { get; set; } = 15;
    public int CrewPrice { get; set; } = 120;
    /// <summary>하루에 폭풍이 올 확률.</summary>
    public double StormChancePerDay { get; set; } = 0.06;
    public double StormDays { get; set; } = 0.6;
    /// <summary>폭풍 속에서 돛을 편 채로 하루에 잃는 내구.</summary>
    public double StormDurabilityPerDay { get; set; } = 260;
    // 배의 내파가 이 값 이상이면 폭풍 속에서도 돛을 편 채 항해할 수 있다(피해가 없다). 원본에 이런 문턱이 있다는 것은 사용자의 기억이고 15 라는 수는 지은 것이다
    public int StormWaveResist { get; set; } = 15;
    /// <summary>난파했을 때 잃는 소지금 비율.</summary>
    public double WreckMoneyLoss { get; set; } = 0.1;
}
/// <summary>스킬 한 가지(클라이언트 표 6). 모험·교역·전투·언어 갈래만 뽑는다.</summary>
public sealed class SkillData
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>0 모험 · 1 교역 · 2 전투 · 3 언어.</summary>
    public int Group { get; set; }
    /// <summary>표에 든 습득 비용.</summary>
    public int Cost { get; set; }
}

/// <summary>
/// 스킬이 게임에서 하는 일. 랭크별 효과 수치는 클라이언트에 없어 지은 값이다.
/// </summary>
public sealed class SkillRuleData
{
    public int SkillId { get; set; }
    /// <summary>개발도구에서 알아보기 위한 이름.</summary>
    public string Name { get; set; } = "";
    /// <summary>
    /// Speed 속도 · Turn 선회 · Survey 좌표와 주변 지도 · Find 발견물 찾기 · Appraise 발견물 감정 ·
    /// CrewLoss 선원 피해 줄이기 · Cure 재해 풀기 · Rest 피로 풀기 · Discount 보급 값 깎기 ·
    /// Craft 생산 스킬(레시피가 이 이름과 랭크를 요구한다) · Shipbuilding 커스텀설정 조선.
    /// TradeKind 그 갈래 교역품의 진열량 늘리기 · Haggle 흥정과 시세 보기 · Ration 물·식량 아끼기.
    /// 바다에서 눌러 쓰는 것: Survey(위치 알기) · Procure 물 모으기 · Fish 낚시 · Repair 자재로 수리 · Rest.
    /// </summary>
    public string Effect { get; set; } = "";
    /// <summary>한 번 쓰는 데 드는 행동력. 0 이면 갈래에 따른 기본값(돛 조종 · 조타 8, 측량 5, 조달 · 낚시 15, 수리 20, 주연 25, 회계 10).</summary>
    public int Vigour { get; set; }
    /// <summary>랭크 하나에 붙는 효과(비율).</summary>
    public double PerRank { get; set; }
    /// <summary>Find·Appraise 면 발견물 갈래 번호들, Cure 면 재해 번호들, TradeKind 면 교역품 갈래 번호들.</summary>
    public List<int> Targets { get; set; } = [];

    /// <summary>개발도구의 표에서 고치기 위한 글 꼴("1, 3"). 파일에는 적지 않는다.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string TargetsText
    {
        get => string.Join(", ", Targets);
        set => Targets = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out int n) ? n : -1).Where(n => n >= 0).ToList();
    }
}

public sealed class StartSkillData
{
    public int SkillId { get; set; }
    public int Rank { get; set; } = 1;
}
/// <summary>교역품 한 가지(클라이언트 표 19). 값은 표에 없다.</summary>
public sealed class GoodData
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>갈래(표 18): 0 식료품 · 1 조미료 · 2 주류 … 19 잡화.</summary>
    public int Kind { get; set; }
}

/// <summary>도시 교역소가 파는 품목. 클라이언트에 없어 지어 넣는다.</summary>
public sealed class MarketData
{
    public int CityId { get; set; }
    /// <summary>개발도구에서 알아보기 위한 도시 이름.</summary>
    public string City { get; set; } = "";
    /// <summary>파는 교역품 id 들을 쉼표로.</summary>
    public string Goods { get; set; } = "";
    /// <summary>
    /// 조선소가 있는가. 비워 두면 규칙으로 채운다 — 본거지·영지이거나 장면 표에 건물 줄이 있는 도시.
    /// (원본의 조선소는 시내에 선 사람이고 그 배치는 클라이언트에 없다.)
    /// </summary>
    public bool? Shipyard { get; set; }

    /// <summary>이용자 사이트(gvdb)에서 본 그 도시의 실제 판매 품목 — 있으면 손으로 적은 것(지은 것) 대신 쓴다. 저장소에는 안 적힌다.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public List<int>? RealGoods { get; set; }
    /// <summary>투자해야 교역소에 나오는 품목 — 교역품 → 필요 투자액(모르면 −1). gvdb 의 값.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public Dictionary<int, int> RealInvest { get; } = [];

    public IEnumerable<int> GoodIds() => RealGoods != null ? RealGoods :
        Goods.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out int id) ? id : 0).Where(id => id != 0);
}

/// <summary>이용자 사이트(gvdb tradeinfo)에서 본 한 도시의 판매 — 교역품과 아이템, [번호, 값].</summary>
public sealed class MarketFact
{
    public int CityId { get; set; }
    public List<int[]> Goods { get; set; } = [];
    public List<int[]> Items { get; set; } = [];
    /// <summary>그 도시에 팔았을 때의 값(이용자들의 보고) — [교역품, 값].</summary>
    public List<int[]> Buys { get; set; } = [];
}

/// <summary>교역 값 셈의 계수. 전부 지은 값이다.</summary>
public sealed class TradeRules
{
    /// <summary>갈래(0~19)별 기준값.</summary>
    public List<int> KindPrices { get; set; } =
        [60, 90, 120, 150, 420, 300, 110, 260, 280, 900, 180, 520, 1200, 200, 380, 520, 1500, 460, 340, 130];
    /// <summary>그 도시가 파는 품목을 그 도시에 되팔 때의 배율.</summary>
    public double HomeSellRate { get; set; } = 0.5;
    /// <summary>가장 가까운 산지에서 세계 좌표 100 멀어질 때마다 붙는 값.</summary>
    public double DistanceBonusPer100 { get; set; } = 0.07;
    public double MaxDistanceBonus { get; set; } = 1.5;
    /// <summary>지방이 다르면 붙는 값.</summary>
    public double RegionBonus { get; set; } = 0.15;
    /// <summary>시세가 오르내리는 폭.</summary>
    public double Swing { get; set; } = 0.25;
    /// <summary>시세가 한 바퀴 도는 날 수.</summary>
    public double SwingDays { get; set; } = 40;
    /// <summary>자동으로 채울 때 도시마다 파는 품목 수.</summary>
    public int GoodsPerCity { get; set; } = 5;
    /// <summary>품목 하나의 진열량(한 번에 살 수 있는 수).</summary>
    public int Stock { get; set; } = 40;
    /// <summary>산 만큼 줄어든 진열량이 다시 차는 날 수.</summary>
    public double RestockDays { get; set; } = 3;
    /// <summary>회계 스킬로 깎거나 올려 받을 수 있는 가장 큰 비율.</summary>
    public double MaxHaggle { get; set; } = 0.15;
    /// <summary>회계 스킬로 시세를 알아볼 수 있는 거리(세계 좌표).</summary>
    public double NearbyReach { get; set; } = 250;
}
/// <summary>캐릭터를 만들 때 나라가 정하는 것.</summary>
public sealed class StartNationData
{
    public int NationId { get; set; }
    /// <summary>시작 도시 — 그 나라의 본거지.</summary>
    public int CityId { get; set; }
}

/// <summary>캐릭터를 만들 때 직업 계열이 정하는 것. 클라이언트에 없어 지은 값이다.</summary>
public sealed class StartLineData
{
    /// <summary>0 모험 · 1 교역 · 2 전투.</summary>
    public int Line { get; set; }
    public string Name { get; set; } = "";
    /// <summary>시작 직업(직업 표 id): 1 수습모험가 · 2 수습상인 · 3 수습군인.</summary>
    public int JobId { get; set; }
    public int Money { get; set; }
    public int ShipId { get; set; }
    /// <summary>시작 스킬 id 들을 쉼표로.</summary>
    public string Skills { get; set; } = "";
}

public sealed class StartData
{
    /// <summary>0 이 아니면 나라와 상관없이 이 도시에서 시작한다(의뢰가 있는 곳에서 시작하려고).</summary>
    public int CityOverride { get; set; }
    public List<StartNationData> Nations { get; set; } = [];
    public List<StartLineData> Lines { get; set; } = [];
}

/// <summary>NPC 의 이름들 — 배 이름, 이름난 선장, 꾸밈말 + 이름씨(이어서 이름을 짓는다), 육상전 테크닉.</summary>
public sealed class NpcNames
{
    public List<string> ShipNames { get; set; } = [];
    public List<string> Captains { get; set; } = [];
    public List<string> Adjectives { get; set; } = [];
    public List<string> Nouns { get; set; } = [];
    public List<string> Techniques { get; set; } = [];
}

/// <summary>지방함대의 활동 한 가지.</summary>
public sealed class FleetMission
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Success { get; set; } = "";
    public string Fail { get; set; } = "";
}

/// <summary>레시피 이름(클라이언트 표 16).</summary>
public sealed class RecipeData
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>레시피 책 한 권 — 아이템 번호 · 이름 · 도구점 값(모르면 0) · 든 레시피들.</summary>
public sealed class RecipeBook
{
    public int ItemId { get; set; }
    public string Name { get; set; } = "";
    public int Price { get; set; }
    public List<int> Recipes { get; set; } = [];
}

/// <summary>레시피로 만드는 것 — 재료와 생산물. 클라이언트에 없어서 지은 것이다(<c>recipes.json</c>).</summary>
public sealed class RecipeRule
{
    public int RecipeId { get; set; }
    /// <summary>개발도구에서 알아보기 위한 이름.</summary>
    public string Name { get; set; } = "";
    /// <summary>생산물(교역품 번호)과 한 번에 나오는 수.</summary>
    public int Output { get; set; }
    public int OutputCount { get; set; } = 1;
    /// <summary>재료 — "교역품 번호:수" 를 쉼표로.</summary>
    public string Inputs { get; set; } = "";
    /// <summary>필요한 생산 스킬과 랭크 — "조리 3" 꼴. 비면 없다. 이용자들이 모은 자료의 값.</summary>
    public string Skill { get; set; } = "";
    /// <summary>연금술 실험의 설비 — "Furnace"(화로) · "Bench"(실험대). 비면 설비가 필요 없는 보통 생산.</summary>
    public string Facility { get; set; } = "";
    /// <summary>실험에 갖춰야 하는 도구(아이템 번호를 쉼표로) — 들지는 않는다.</summary>
    public string Tools { get; set; } = "";
    /// <summary>생산물이 교역품이 아니라 아이템일 때 그 번호(이그니스의 원액 …). 0 이면 Output 의 교역품.</summary>
    public int OutputItem { get; set; }
    /// <summary>한 번 만들 때마다 하나씩 닳는 아이템(번호를 쉼표로) — 재봉도구 따위.</summary>
    public string Consumes { get; set; } = "";
    public IEnumerable<int> ConsumeList() =>
        Consumes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(t => int.TryParse(t, out _)).Select(int.Parse);
    /// <summary>이용자 사이트(gvdb)의 값으로 채운 것 — <c>data\extracted\recipe-inputs.json</c>. 저장소의 recipes.json 에는 적지 않는다.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public bool FromSite { get; set; }

    public IEnumerable<int> ToolList() =>
        Tools.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(t => int.TryParse(t, out _)).Select(int.Parse);

    public IEnumerable<(int Good, int Count)> InputList() =>
        Inputs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split(':')).Where(p => p.Length == 2 && int.TryParse(p[0], out _) && int.TryParse(p[1], out _))
            .Select(p => (int.Parse(p[0]), int.Parse(p[1])));
}

/// <summary>소비 아이템 — 이름과 번호는 클라이언트 아이템 표(14)의 것이고, 하는 일과 값은 지은 것이다.</summary>
/// <summary>연구 하나 — 연구동 레벨, 전공, (직업), 해야 하는 행동들과 횟수, 필요 페이지, 얻는 스킬, 학점.</summary>
public sealed class ResearchFact
{
    public int No { get; set; }
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public string Major { get; set; } = "";
    public string Job { get; set; } = "";
    public List<ResearchAction> Actions { get; set; } = [];
    public int Pages { get; set; }
    public string Skill { get; set; } = "";
    public int Credit { get; set; }
}

/// <summary>직업 하나 — 우대 스킬들과 전문 스킬(하나), 전직 비용.</summary>
public sealed class JobFact
{
    public int No { get; set; }
    public string Name { get; set; } = "";
    public List<string> Skills { get; set; } = [];
    public string Expert { get; set; } = "";
    public long Cost { get; set; }
}

/// <summary>
/// 한 해역의 바다 — 방향은 나침반 각도(0 북 · 90 동)이고 「불어 가는 쪽 / 흘러가는 쪽」이다. 모두 지은 값.
/// </summary>
public sealed class SeaClimate
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>바람이 불어 가는 쪽(도)과 세기(노트), 좌우로 흔들리는 너비(도).</summary>
    public double WindDirection { get; set; } = 125;
    public double WindKnots { get; set; } = 9;
    public double WindSwing { get; set; } = 60;
    /// <summary>철 따라 바람이 뒤집힌다(몬순) — 한 해의 뒤 절반에 반대로 분다.</summary>
    public bool Seasonal { get; set; }
    /// <summary>해류가 흘러가는 쪽(도)과 세기(노트). 0 이면 해류가 없다.</summary>
    public double CurrentDirection { get; set; }
    public double CurrentKnots { get; set; }
    /// <summary>폭풍이 이는 잦기의 배수(1 이 보통), 비가 올 몫(0 ~ 1), 물결 높이의 배수.</summary>
    public double Storm { get; set; } = 1;
    public double Rain { get; set; } = 0.12;
    public double Wave { get; set; } = 1;
}

// 장비 아이템 하나가 몸에 입히는 것 — 겉모습 줄 표(0001\0000.bin)와 몸 틀별 자리 표(0001\0001.bin), 자원 색인(0000.tbl)을 이어 뽑아 둔 것.
// 아이템 번호 → 겉모습 줄은 클라이언트에 표가 없어 「번호 순 = 줄 차례」로 맞춘 것이다(추정).
public sealed class GearModel
{
    public int Id { get; set; }
    // body · cap · leg · hand
    public string Part { get; set; } = "";
    // 줄의 플래그 — 옷의 0x80 은 신발을 짧은 것으로, 0x40 은 바지 조각 없는 것으로 바꾼다
    public int Flags { get; set; }
    // 색 다섯 쌍(0xRRGGBB × 10) — 모형의 조각마다 어느 쌍으로 물들일지가 정해져 있다
    public List<int> Colors { get; set; } = [];
    // 몸 틀(0 ~ 7) → 자리마다 [모형 묶음, 모형 항목, 텍스처 묶음, 텍스처 항목](묶음은 md 번호). 신발은 자리가 넷(긴 · 긴 맨다리 · 짧은 · 짧은 맨다리) 또는 둘
    public Dictionary<string, List<int[]?>> Frames { get; set; } = [];
}

// 지은 값을 진짜 값으로 채워 달라는 요청 하나 — 게임에서 「지은 값」 표시를 누르면 data\wiki-requests.json 에 쌓인다.
// 한꺼번에 긁으면 위키가 막으니, 사용자가 누른 것만 한 쪽씩 찾아다 채운다. Done 은 채운 날(비어 있으면 아직).
public sealed class WikiRequest
{
    // ship(배의 능력치) …
    public string Kind { get; set; } = "";
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Asked { get; set; } = "";
    public string Done { get; set; } = "";
    public string Note { get; set; } = "";
}

/// <summary>아이템 표(14)의 증서 · 허가증 · 교환권 한 줄 — 번호 · 이름 · 설명.</summary>
public sealed class PaperItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>
/// 장비 표(15)의 한 줄 — 번호 · 이름 · 설명과 줄 꼬리의 수치 다섯(u16 칸 1 · 3 · 5 · 7 · 9).
/// 칸 9 는 내구로 보인다(옷 30 · 무기 100). 칸 1 은 무기에서, 칸 3 · 5 는 옷에서 값이 있다 — 공격 · 방어 따위로 보이지만 어느 것인지는 못 밝혔다.
/// </summary>
public sealed class GearItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<int> Stats { get; set; } = [];
    /// <summary>갈래 — 번호의 십만 자리: 0 옷 · 1 모자 · 2 신발 · 3 장갑 · 4 무기 · 5 장신구.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public int Slot => Id / 100000;
}

/// <summary>배 하나의 상세(ssjoy 의 배 쪽) — 강화 횟수, 강화 상한, 부품 칸 수, 붙일 수 있는 선박 스킬과 그 재료, 특수 건조의 선체.</summary>
public sealed class ShipDetailFact
{
    public int No { get; set; }
    public string Name { get; set; } = "";
    public int Times { get; set; }
    public int Retimes { get; set; }
    // 건조 일수(위키의 日数) — 0 이면 모른다
    public int Days { get; set; }
    public List<int> Caps { get; set; } = [];
    public List<int> Slots { get; set; } = [];
    public List<ShipDetailSkill> Skills { get; set; } = [];
    // 제 자료가 없어 다른 배의 것을 빌려 왔으면 그 배의 이름(화면에 「지은 값」 딱지가 남는다)
    public string Borrowed { get; set; } = "";
    public string Hull { get; set; } = "";
    /// <summary>특수 건조 — 어느 도시에서 어느 선체로 짓는가(조선 랭크 · 기본 재질).</summary>
    public List<ShipSpecialBuild> Special { get; set; } = [];
}

public sealed class ShipSpecialBuild
{
    public string Hull { get; set; } = "";
    public int Rank { get; set; }
    public string Material { get; set; } = "";
    public string City { get; set; } = "";
}

public sealed class ShipDetailSkill
{
    public string Name { get; set; } = "";
    public List<string> Parts { get; set; } = [];
}

public sealed class ShipSkillFact
{
    public int No { get; set; }
    public string Name { get; set; } = "";
    public string Vigour { get; set; } = "";
    public string Scope { get; set; } = "";
    public string Needs { get; set; } = "";
}

public sealed class RoomSpot
{
    public int Scene { get; set; }
    public float HostX { get; set; }
    public float HostZ { get; set; }
    public float FaceX { get; set; }
    public float FaceZ { get; set; }
    public float MaidX { get; set; }
    public float MaidZ { get; set; }
}

public sealed class ResearchAction
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
}

public sealed class ItemData
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>SailPaint 돛을 Amount(0xRRGGBB) 빛깔로 칠하기 ·
    /// Fatigue 피로를 Amount 만큼 풀기 · Repair 내구를 Amount % 고치기 · Cure 재해(Amount = 재해 번호) 풀기 · Lifebuoy 난파를 한 번 막고 내구 Amount % 로.</summary>
    public string Effect { get; set; } = "";
    public double Amount { get; set; }
    public int Price { get; set; }
}

/// <summary>칙명 하나. 전부 지은 것이다(클라이언트에는 「칙명청부」라는 차림 이름과 임명장 아이템뿐이다).</summary>
public sealed class OrderData
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    /// <summary>Visit 다른 나라 본거지에 들르기 · Far 먼 바다(다른 큰 바다)의 항구에 들르기 · Bring 그 갈래 교역품을 Count 개 바치기 · Discover 발견물 Count 개 보고하기.</summary>
    public string Kind { get; set; } = "";
    /// <summary>Bring 이면 교역품 갈래 번호.</summary>
    public int Target { get; set; }
    public int Count { get; set; } = 1;
    /// <summary>이 작위(0 부터) 이상이어야 내린다.</summary>
    public int MinTitle { get; set; }
    public int Reward { get; set; }
    /// <summary>공적 — 쌓이면 작위가 오른다.</summary>
    public int Merit { get; set; }
}

public sealed class OrderBook
{
    /// <summary>작위 이름들(낮은 것부터).</summary>
    public List<string> Titles { get; set; } = [];
    public List<OrderData> Orders { get; set; } = [];
    /// <summary>다음 작위까지의 공적 = 이 값 × (지금 작위 + 1).</summary>
    public int MeritPerTitle { get; set; } = 20;
}

/// <summary>이어 하기 — 항구에 들어올 때 적는다.</summary>
public sealed class SaveData
{
    public string Name { get; set; } = "";
    public bool Male { get; set; } = true;
    public int NationId { get; set; }
    public int JobId { get; set; }
    public int Money { get; set; }
    public int CityId { get; set; }
    public int ShipId { get; set; }
    public double Durability { get; set; }
    public double Crew { get; set; }
    public double Water { get; set; }
    public double Food { get; set; }
    public double Clock { get; set; }
    public double SkyPhase { get; set; }
    public int AdventureExp { get; set; }
    public int AdventureFame { get; set; }
    public int TradeExp { get; set; }
    public int BattleExp { get; set; }
    public int TradeFame { get; set; }
    public int BattleFame { get; set; }
    public Dictionary<int, double[]> Skills { get; set; } = new();     // id → [랭크, 숙련도]
    public Dictionary<int, int> Supplies { get; set; } = new();
    public Dictionary<int, long[]> Cargo { get; set; } = new();       // id → [수, 산 값의 합]
    public List<int> DoneQuests { get; set; } = [];
    /// <summary>소지품 — 아이템 번호 → 수.</summary>
    public Dictionary<int, int> Items { get; set; } = new();
    /// <summary>타고 있는 배의 [재질 번호, 적재 변경 %].</summary>
    public int[] Build { get; set; } = [0, 0];
    /// <summary>조선소에 맡긴 배 — [배 id, 재질, 적재 변경, 남은 날]. 없으면 빈 것.</summary>
    public double[] Ordered { get; set; } = [];
    /// <summary>겉모습 — [몸 틀, 얼굴, 머리, 몸, 다리, 손, 모자(-1 없음)].</summary>
    public int[] Looks { get; set; } = [];
    /// <summary>퀵슬롯 여덟 칸 — 양수 스킬 번호, 음수 −아이템 번호, 0 빈 칸.</summary>
    public int[] QuickSlots { get; set; } = [];
    /// <summary>타고 있는 배의 강화 — [횟수, 내구, 돛, 선회, 내파, 창고, 옵션 스킬 …]. 부두의 배는 DockWork 에 같은 차례로.</summary>
    public double[] Work { get; set; } = [];
    public List<double[]> DockWork { get; set; } = [];
    /// <summary>부두의 배들의 돛 [무늬, 색] — Dock 과 같은 차례.</summary>
    public List<int[]> DockSail { get; set; } = [];
    /// <summary>가진 레시피 번호들.</summary>
    public List<int> Recipes { get; set; } = [];
    /// <summary>작위(0 부터) · 공적 · 받은 칙명 id · 칙명의 진행(들른 곳 수나 보고한 발견 수).</summary>
    public int[] Court { get; set; } = [0, 0, 0, 0];
    // 도시마다 투자한 돈과, 투자하기 전부터 제 나라 것이던 도시
    public Dictionary<int, long> Invested { get; set; } = [];
    public List<int> InvestedHome { get; set; } = [];
    // 개인농장: 가졌는가, 칸마다 (갈래, 기르는 것, 랭크, 거둔 때)
    public double[] Farm { get; set; } = [];
    // 지방함대를 마지막으로 내보낸 날(게임의 날 수) — 하루에 한 번
    public int FleetDay { get; set; } = -1;
    public int ExileDay { get; set; } = -1;
    public int Infamy { get; set; }
    /// <summary>침몰선: 조각지도 수, 알아낸 자리(없으면 0, 0), 올린 정도 · 실패 · 누적.</summary>
    public int WreckPieces { get; set; }
    public double WreckX { get; set; }
    public double WreckY { get; set; }
    public List<int> WreckState { get; set; } = [];
    public int TowValue { get; set; }
    /// <summary>기도 효과: 갈래(−1 없음)와 끝나는 날.</summary>
    public List<int> Prayer { get; set; } = [];
    /// <summary>나라의 정세: 나라, 갈래, 끝나는 날.</summary>
    public List<int> News { get; set; } = [];
    /// <summary>애완동물: 번호(0 없음)와 친밀도.</summary>
    public List<int> Pet { get; set; } = [];
    public int Insurance { get; set; }
    /// <summary>찾아낸 발견물의 번호들.</summary>
    public List<int> Found { get; set; } = [];
    /// <summary>위임 항해의 목적지(도시 번호) — 없으면 0.</summary>
    public int DelegateCity { get; set; }
    /// <summary>바다 위에서 적은 저장 — [x, y, 뱃머리, 바다에서 보낸 초]. 항구에서 적었으면 빈 채.</summary>
    public double[] AtSea { get; set; } = [];
    public Dictionary<int, int> Hostility { get; set; } = [];
    // 대장간의 단련: 장비 · 대포 번호 → 더해진 (공격력 또는 관통력, 방어력)
    public Dictionary<int, int[]> Forged { get; set; } = [];
    // 얻은 입항 허가(큰 바다의 번호)
    public List<int> Permits { get; set; } = [];
    // 내건 호칭, 물리친 해적 · 군함의 수
    public int[] Honor { get; set; } = [0, 0, 0];
    /// <summary>은행에 맡긴 돈.</summary>
    public long Bank { get; set; }
    /// <summary>돛의 빛깔(0xRRGGBB) — 돛 도료를 써서 바꾼다.</summary>
    public int SailColor { get; set; } = 0xFFFFFF;
    /// <summary>돛의 [무늬 0 ~ 17, 색 0 ~ 9].</summary>
    public int[] SailLook { get; set; } = [0, 0];
    /// <summary>대학 — 전공, 하고 있는 연구 번호, 그 연구의 행동별 진행, 학점, 마친 연구 번호.</summary>
    public string Major { get; set; } = "";
    public int Research { get; set; }
    public Dictionary<string, int> ResearchProgress { get; set; } = new();
    public int Credits { get; set; }
    public List<int> ResearchDone { get; set; } = [];
    /// <summary>은행 보관함에 맡긴 아이템 — 번호와 개수.</summary>
    public Dictionary<int, int> Vault { get; set; } = new();
    /// <summary>고용한 부관 — [후보 id, 담당, 레벨, 경험].</summary>
    public List<double[]> Aides { get; set; } = [];
    /// <summary>타고 있는 배에 단 부품.</summary>
    public List<int> Parts { get; set; } = [];
    /// <summary>가지고만 있는(배에 안 단) 선박부품의 번호들.</summary>
    public List<int> PartStock { get; set; } = [];
    /// <summary>입거나 찬 장비 — 갈래(0 옷 … 5 장신구)마다 아이템 번호, 없으면 0.</summary>
    public List<int> Equipped { get; set; } = [];
    /// <summary>남은 행동력. 음수면 가득(옛 저장).</summary>
    public double Vigour { get; set; } = -1;
    // 걸려 있는 부스트 — [갈래(0 속도 · 1 스킬 · 2 연장), 값, 스킬 갈래, 스킬 번호, 상한, 남은 초, 아이템 번호]
    public List<double[]> Boosts { get; set; } = [];
    /// <summary>붙인 선박 데코(자리 다섯)와 쥐여 준 선원 장비(갈래 셋) — 아이템 번호, 없으면 0.</summary>
    public List<int> Decos { get; set; } = [];
    public List<int> CrewGear { get; set; } = [];
    /// <summary>부두의 배들 — [배 id, 내구, 단 부품 …].</summary>
    public List<double[]> Dock { get; set; } = [];
    public int QuestId { get; set; }
    public int QuestStage { get; set; }
}
/// <summary>항구 장면 안에서 배가 뜨는 자리.</summary>
public sealed class BerthData
{
    public int Scene { get; set; }
    public float X { get; set; }
    public float Z { get; set; }
    /// <summary>배 모형을 y 축으로 돌리는 각(라디안).</summary>
    public float Yaw { get; set; }
}

public sealed class SettingsData
{
    public string PlayerName { get; set; } = "김선민";
    public int Money { get; set; } = 1_093_057;
    public int StartCity { get; set; } = 28;
    /// <summary>처음 타는 배(배 표 id). 16 = 중 갤리온.</summary>
    public int StartShip { get; set; } = 16;
    public ShipRules Ships { get; set; } = new();
    public TradeRules Trade { get; set; } = new();
    /// <summary>시작할 때 하늘의 때. 0 = 자정, 0.5 = 한낮.</summary>
    public double StartSkyPhase { get; set; } = 0.40;
    public double SecondsPerDay { get; set; } = 60;
    public double SecondsPerSkyCycle { get; set; } = 600;
    /// <summary>창 안쪽 크기(해상도). 게임의 「화면」 창에서 바꾸면 여기에 적힌다.</summary>
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 800;
    /// <summary>테두리 없는 전체 화면.</summary>
    public bool Fullscreen { get; set; }
    /// <summary>화면 글과 창의 배율. 0 이면 윈도의 배율(175% 면 1.75)을 따른다.</summary>
    public double UiScale { get; set; }
    /// <summary>배경음 크기(0 ~ 1). 0 이면 끈다.</summary>
    public double MusicVolume { get; set; } = 0.5;
    /// <summary>그림 단추(오른쪽 위 단추 줄 · 항구 단추 · 가장자리 둥근 단추)의 배율(0.5 ~ 2.5) — 글과 창의 배율과 따로 논다.</summary>
    public double IconScale { get; set; } = 1;
    /// <summary>퀵슬롯 칸의 배율(0.5 ~ 2.5, 10% 단위) — 아이콘 배율과 따로.</summary>
    public double QuickScale { get; set; } = 1;
    /// <summary>퀵슬롯의 「고정」 — 켜 두면 스킬을 써도 퀵슬롯이 안 닫힌다.</summary>
    public bool QuickPin { get; set; }
    /// <summary>바다의 둥근 지도 크기 배율(0.5 ~ 1.5).</summary>
    public double SeaMapScale { get; set; } = 1;
    /// <summary>바다의 주변 지도의 배율 — 1 이면 반지름 180(세계 좌표)이 보이고, 크면 좁게 크게 보인다.</summary>
    public double SeaMapZoom { get; set; } = 1;
    /// <summary>바다의 주변 지도를 네모로 그린다(원본은 둥글다).</summary>
    public bool SeaMapSquare { get; set; }
    /// <summary>판매선박 목록에 걸어 둔 거르기 — 크기(0 전체 · 1 소형 · 2 중형 · 3 대형)와 용도(0 전체 · 1 모험 · 2 교역 · 3 전투). 게임을 껐다 켜도 남는다.</summary>
    public int ShipFilterSize { get; set; }
    public int ShipFilterUse { get; set; }
    /// <summary>소지품 창의 격자 줄 수(다섯 칸 × 이 줄 수) — 4 ~ 8.</summary>
    public int ItemRows { get; set; } = 5;
    /// <summary>모드: 타고 있는 배도 커스텀설정 조선 · 특수 조선에서 강화할 수 있다(원본은 못 한다).</summary>
    public bool ModWorkOnBoard { get; set; }
    // 모드: 타고 있는 배도 선박 조합의 강화 선박으로 고를 수 있다(재료로는 못 쓴다)
    public bool ModCombineOnBoard { get; set; }
    // 모드: 해적선이 쫓아와 싸움을 걸지 않는다
    public bool ModNoPirates { get; set; }
    // 모드: 창마다 오른쪽 위에 적는 창 이름 — 0 없음 · 1 아이디(WndShipSwap) · 2 보조 아이디(Wnd012)
    public int ModWindowIds { get; set; }
    // 모드: 입항 허가 없이 어느 바다의 항구에나 들어간다(원본은 먼 바다에 허가가 든다) — 기본 켜짐
    public bool ModNoPermits { get; set; } = true;
    // 모드: 교역 관세 없이 산다(관세의 세율은 지은 값이다) — 기본 꺼짐
    public bool ModNoTax { get; set; }
    // 모드: 어느 도시에서나 말이 통한다(언어를 몰라도 흥정한다) — 기본 꺼짐
    public bool ModAllLanguages { get; set; }
    // 모드: 서고에서 하루에 읽는 권수 5배(5 → 25) — 기본 꺼짐
    public bool ModBooksTimes5 { get; set; }
    /// <summary>모드: 선박 조합의 성공률에 더하는 값(%) — 0 ~ 50. 0 이면 그대로.</summary>
    public int ModCombineBonus { get; set; }
    /// <summary>모드: 초과 강화(강화 횟수를 다 쓴 뒤의 강화)의 성공률에 더하는 값(0 ~ 50, %p).</summary>
    public int ModOverWorkBonus { get; set; }
    /// <summary>모드: 경험치(모험 · 교역 · 부관)와 숙련도(스킬 · 조타)가 세 배로 오른다.</summary>
    public bool ModTripleGain { get; set; }
    /// <summary>모드: 경험치 · 숙련도 배율(1 ~ 3). 0 이면 아직 안 고른 것 — 예전 설정(ModTripleGain)을 따른다.</summary>
    public int ModGain { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public int Gain => ModGain is >= 1 and <= 3 ? ModGain : ModTripleGain ? 3 : 1;
    /// <summary>단축키 — 하는 일의 이름 → 글쇠(가상 키 번호). 없는 것은 기본값을 쓴다. 게임의 「단축키 등록」에서 바꾼다.</summary>
    public Dictionary<string, int> Keys { get; set; } = new();
    /// <summary>효과음 — 일 이름 → "묶음:차례"(<c>data\extracted\se-all</c> 의 파일 이름 앞 두 수). 빈 글이면 소리 없음.</summary>
    /// <summary>효과음마다 적어 둔 메모 — "묶음:차례" → 글. 묶음의 제목은 "묶음" → 글.</summary>
    public Dictionary<string, string> SoundMemos { get; set; } = new();
    public Dictionary<string, string> Sounds { get; set; } = new() { ["Skill"] = "0:6", ["Turn"] = "0:12", ["Eat"] = "0:11", ["Door"] = "0:15" };
    public double MaxKnots { get; set; } = 14;
    /// <summary>1노트로 1초에 가는 세계 좌표.</summary>
    public double UnitsPerKnotSecond { get; set; } = 0.16;
    /// <summary>키를 돌리는 빠르기(초당 라디안).</summary>
    public double TurnRate { get; set; } = 0.9;
    public double PortRange { get; set; } = 14;
    public double LandingRange { get; set; } = 16;
    public VoyageRules Voyage { get; set; } = new();
    public List<StartSkillData> StartSkills { get; set; } = [];
    public int MaxSkillRank { get; set; } = 15;
    /// <summary>랭크 r 에서 r+1 로 오르는 데 드는 숙련도 = 이 값 × r².</summary>
    public int SkillExpBase { get; set; } = 100;
    /// <summary>
    /// 방 안 사람이 서는 자리 — 클라이언트에 없어서(서버가 주는 값) 방 장면마다 손으로 적는다. 없는 방은 짐작으로 세운다.
    /// 주인의 자리, 손님 쪽(말을 거는 쪽)으로의 방향, 여급의 자리(주점).
    /// </summary>
    public List<RoomSpot> RoomSpots { get; set; } = [];
    /// <summary>손으로 재어 둔 자리 — 설정에 그 방이 없으면 이것을 쓴다. 3022 는 3021 과 같은 방이 z 로 300 밀린 꼴이다.</summary>
    public static readonly RoomSpot[] KnownRoomSpots =
    [
        new RoomSpot { Scene = 3021, HostX = 1550, HostZ = 3750, FaceX = 1, FaceZ = 0, MaidX = 1550, MaidZ = 4150 },
        new RoomSpot { Scene = 3022, HostX = 1550, HostZ = 4050, FaceX = 1, FaceZ = 0, MaidX = 1550, MaidZ = 4450 },
    ];
    public List<BerthData> Berths { get; set; } = [new BerthData { Scene = 7004, X = 39500, Z = 28500, Yaw = 1.15f }];
}

/// <summary>
/// 게임이 돌아가는 자료 한 벌. <c>data</c> 폴더의 JSON 으로 읽고 쓴다.
/// </summary>
/// <remarks>
/// 직접 지은 것(저장소에 둔다): <c>settings.json</c>, <c>quests.json</c>, <c>landing-points.json</c>,
/// <c>disasters.json</c>, <c>supplies.json</c>.
/// 게임 클라이언트에서 뽑은 것(저장소에 두지 않는다): <c>extracted\cities.json</c>, <c>seas.json</c>,
/// <c>landings.json</c>, <c>discoveries.json</c>, <c>discovery-kinds.json</c>. 없으면 클라이언트에서 다시 뽑는다.
/// </remarks>
public sealed class GameData
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string Directory { get; private set; } = "";
    public SettingsData Settings { get; set; } = new();
    public List<QuestData> Quests { get; set; } = [];
    public List<DisasterData> Disasters { get; set; } = [];
    public List<WikiRequest> WikiRequests { get; set; } = [];

    // 그 값을 채워 달라고 요청한다(이미 있으면 그대로) — 바로 파일에 적는다
    public void RequestWiki(string kind, int id, string name)
    {
        if (WikiRequests.Exists(r => r.Kind == kind && r.Id == id)) return;
        WikiRequests.Add(new WikiRequest { Kind = kind, Id = id, Name = name, Asked = DateTime.Now.ToString("yyyy-MM-dd HH:mm") });
        try { Write(Path.Combine(Directory, "wiki-requests.json"), WikiRequests); } catch (Exception) { }
    }

    public bool WikiRequested(string kind, int id) => WikiRequests.Exists(r => r.Kind == kind && r.Id == id && r.Done == "");
    /// <summary>해역마다의 바람 · 해류 · 날씨 — <c>sea-climates.json</c>(지은 값, <c>tools\gvo\climate.py</c> 가 기본값을 만든다).</summary>
    public List<SeaClimate> SeaClimates { get; set; } = [];
    public List<SupplyData> Supplies { get; set; } = [];
    public List<SkillRuleData> SkillRules { get; set; } = [];
    public List<SkillData> Skills { get; set; } = [];
    /// <summary>조선소에서 팔지 않는 배의 이름 표시 — 교환권 · 특수 조선으로만 얻는 변형들.</summary>
    public static readonly string[] ShipVariants = ["개조", "명품", "기념", "체험", "개량", "개장", "신형", "특제", "특수", "월광", "신장"];

    public List<ShipData> Ships { get; set; } = [];
    public OrderBook Orders { get; set; } = new();
    public List<RecipeData> Recipes { get; set; } = [];
    public List<RecipeRule> RecipeRules { get; set; } = [];
    /// <summary>레시피 책 — 어느 책(아이템)에 어느 레시피가 있는가. 이용자 사이트(gvdb)의 값(<c>data\extracted\recipe-books.json</c>).</summary>
    public List<RecipeBook> RecipeBooks { get; set; } = [];
    /// <summary>도시마다 실제로 파는 것(gvdb) — <c>data\extracted\market-facts.json</c>.</summary>
    public List<MarketFact> MarketFacts { get; set; } = [];
    /// <summary>교역품의 실제 판매 값(파는 도시들의 가운데 값) — 있으면 갈래 기준값 대신 쓴다.</summary>
    public Dictionary<int, int> GoodPrices { get; } = [];
    /// <summary>(도시, 교역품) → 그 도시에 팔았을 때의 실제 값(gvdb 의 보고).</summary>
    public Dictionary<(int City, int Good), int> BuyPrices { get; } = [];
    /// <summary>명산품 — 교역품 → 그것이 나는 문화권(gvdb 아이템 설명의 「○○の名産品」, <c>data\extracted\specialties.json</c>).</summary>
    public Dictionary<int, int> Specialties { get; set; } = [];
    /// <summary>전용 — 교역품 → [물자(0 물 · 1 식량 · 2 자재 · 3 탄약), 하나에 얻는 양]. gvdb 아이템 설명의 「…への転用量」(<c>data\extracted\conversions.json</c>).</summary>
    public Dictionary<int, int[]> Conversions { get; set; } = [];
    /// <summary>NPC 의 이름 표(클라이언트 표 41 · 72 · 95 · 96 · 51) — tools\gvo\npcs.py 가 뽑는다.</summary>
    public NpcNames Npcs { get; set; } = new();
    /// <summary>지방함대의 활동(클라이언트 표 113) — 이름과 잘됐을 때 · 안됐을 때의 글. tools\gvo\npcs.py 가 뽑는다.</summary>
    public List<FleetMission> FleetMissions { get; set; } = [];
    /// <summary>레시피 번호 → (필요 스킬, 만드는 것) — 재료를 모르는 레시피의 쪽지에 쓴다.</summary>
    public Dictionary<int, (string Skill, string Makes)> RecipeNotes { get; } = [];
    public List<ItemData> Items { get; set; } = [];
    private bool _materialsFromFacts;
    /// <summary>선박 스킬 109가지의 이름 · 행동력 · 필요 스킬 — <c>data\extracted\shipskill-facts.json</c>(ssjoy 에서 모은 것, 저장소에는 안 둔다).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public List<ShipSkillFact> ShipSkillFacts { get; set; } = [];

    /// <summary>배마다의 상세 — <c>data\extracted\shipdetail-facts.json</c>(ssjoy 에서 모은 것, 일부 배만 있다. 저장소에는 안 둔다).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public List<ShipDetailFact> ShipDetails { get; set; } = [];

    /// <summary>클라이언트 아이템 표에서 뽑은 증서 · 허가증 · 교환권 — <c>data\extracted\paper-items.json</c>(저장소에는 안 둔다).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public List<PaperItem> Papers { get; set; } = [];

    /// <summary>
    /// 클라이언트 장비 표(15)에서 뽑은 의상 · 장비 — <c>data\extracted\gear-items.json</c>(저장소에는 안 둔다).
    /// 번호의 십만 자리가 갈래다: 0 옷 · 1 모자 · 2 신발 · 3 장갑 · 4 무기 · 5 장신구. 아이템 그림 묶음의 무리 번호와 같다.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore] public List<GearItem> Gear { get; set; } = [];

    /// <summary>
    /// 재질 번호 → 클라이언트의 선박재료 아이템(아이템 표 14 의 2200000 대) — <c>data\extracted\material-items.json</c>.
    /// 재질 표(ssjoy)와 아이템 이름을 다듬어 짝지은 것이다(98 가운데 98). 재질의 그림은 이 아이템의 그림이다.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore] public Dictionary<int, int> MaterialItems { get; set; } = [];

    /// <summary>이름으로 찾는 배 상세 — 모으지 못한 배는 null.</summary>
    private readonly Dictionary<string, ShipDetailFact?> _detailByName = [];
    private static readonly string[] VariantPrefixes = ["특주 ", "특급 ", "월광 ", "명품 ", "개량 ", "개조 ", "신형 ", "강화 ", "기념 "];

    /// <summary>
    /// 배 상세. 제 자료가 없는 변형 배(「특주 …」 · 「월광 …」 · 「… 2」)는 바탕 배의 옵션 스킬 목록과 강화 횟수를 빌린다 —
    /// 빌린 것은 <c>Borrowed</c> 에 바탕 배의 이름이 남아 화면에 「지은 값」 딱지가 뜬다. 특수 건조 자료 · 건조 일수는 빌리지 않는다.
    /// </summary>
    public ShipDetailFact? ShipDetail(string name)
    {
        if (_detailByName.TryGetValue(name, out var known)) return known;
        var own = ShipDetails.Find(d => d.Name == name);
        if (own is not { Skills.Count: > 0 })
        {
            string plain = name;
            foreach (string prefix in VariantPrefixes) if (plain.StartsWith(prefix)) plain = plain[prefix.Length..];
            plain = System.Text.RegularExpressions.Regex.Replace(plain, @"\s*\d+$", "");
            if (plain != name && ShipDetails.Find(d => d.Name == plain) is { Skills.Count: > 0 } source)
                own = new ShipDetailFact { No = own?.No ?? 0, Name = name, Times = own is { Times: > 0 } ? own.Times : source.Times, Retimes = own?.Retimes ?? source.Retimes, Days = own?.Days ?? 0,
                                           Caps = own?.Caps ?? [], Slots = own?.Slots ?? [], Skills = source.Skills, Hull = own?.Hull ?? "", Special = own?.Special ?? [], Borrowed = plain };
        }
        return _detailByName[name] = own;
    }
    private List<OptionSkill>? _optionSkills;

    // 이름만 아는 선박 스킬의 효과 — 갈래: 속도 · 선회 · 창고 · 폭풍 · 선원 피해 · 재해, 해전의 포격(Shot) · 장전(Reload) · 받는 포격(ShotArmor) · 충각(Ram) · 백병(Melee) · 백병 방어(MeleeGuard), 군함 위장(Disguise), 구명정(Lifeboat)
    private static readonly Dictionary<string, (string Effect, double Amount)> NamedEffects = new()
    {
        ["개량갑판"] = ("Speed", 0.05), ["추진력 강화"] = ("Speed", 0.05), ["고속범주"] = ("Speed", 0.06), ["증기기관"] = ("Speed", 0.08), ["고속수송"] = ("Speed", 0.04), ["노 젓기보조"] = ("Speed", 0.03),
        ["조타강화"] = ("Turn", 0.10), ["반동타"] = ("Turn", 0.08), ["충돌 회피"] = ("Turn", 0.05),
        ["강화창고"] = ("Hold", 0.05), ["식량 비축 창고"] = ("Hold", 0.03), ["내진창고"] = ("Hold", 0.03),
        ["내파장갑"] = ("Storm", 0.30), ["수밀격벽"] = ("Luck", 0.10), ["배수펌프"] = ("Luck", 0.10), ["의료지원"] = ("CrewLoss", 0.15), ["원양 선실"] = ("CrewLoss", 0.10),
        ["군함 위장"] = ("Disguise", 0.8), ["구명정"] = ("Lifeboat", 0.5),
        ["일발필중"] = ("Shot", 0.10), ["견제 포격"] = ("Shot", 0.08), ["중량포격"] = ("Shot", 0.12), ["대 대형 포격"] = ("Shot", 0.20), ["철갑탄"] = ("Shot", 0.10), ["작렬탄"] = ("Shot", 0.10),
        ["강화포문"] = ("Reload", 0.15), ["집중장전"] = ("Reload", 0.20),
        ["내포격장갑"] = ("ShotArmor", 0.15), ["직격저지"] = ("ShotArmor", 0.10), ["직격대책"] = ("ShotArmor", 0.10), ["내염현측"] = ("ShotArmor", 0.05),
        ["특수충각"] = ("Ram", 0.5), ["강화충각"] = ("Ram", 0.5), ["충각전술"] = ("Ram", 0.3),
        ["강습 갑판전"] = ("Melee", 0.15), ["인해전술"] = ("Melee", 0.15), ["소진백병"] = ("Melee", 0.10), ["백병전 요격"] = ("Melee", 0.10),
        ["백병전 회피"] = ("MeleeGuard", 0.20), ["갑판 장벽"] = ("MeleeGuard", 0.15), ["침투방지망"] = ("MeleeGuard", 0.15),
    };

    /// <summary>
    /// 옛 저장의 선박 스킬 번호(3000 + ssjoy 표의 차례)를 지금 번호로 — 배에 붙은 스킬에만 쓴다(3000 대의 진짜 스킬과 헷갈리지 않게).
    /// </summary>
    public int ShipSkillId(int saved) =>
        saved is > 3000 and < 3200 && ShipSkillFacts.Find(f => f.No == saved - 3000) is { } fact
        && OptionSkills.Find(o => o.Name == fact.Name) is { } now ? now.SkillId : saved;

    /// <summary>
    /// 붙일 수 있는 옵션 스킬 전부 — 효과를 정해 둔 것(<c>ship-works.json</c>) 뒤에, 이름만 아는 선박 스킬들이 온다.
    /// 뒤의 것들은 효과가 없고(이름만 붙는다) 재료 조합은 겹치지 않게 차례로 지어 준 것이다.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<OptionSkill> OptionSkills
    {
        get
        {
            if (_optionSkills != null) return _optionSkills;
            var all = new List<OptionSkill>(ShipWorks.Skills);
            var used = all.Select(s => (Math.Min(s.PartA, s.PartB), Math.Max(s.PartA, s.PartB))).ToHashSet();
            var pairs = new Queue<(int, int)>();
            for (int gap = 1; gap < ShipWorks.Parts.Count; gap++)
                for (int i = 0; i + gap < ShipWorks.Parts.Count; i++)
                {
                    var pair = (Math.Min(ShipWorks.Parts[i].Id, ShipWorks.Parts[i + gap].Id), Math.Max(ShipWorks.Parts[i].Id, ShipWorks.Parts[i + gap].Id));
                    if (!used.Contains(pair)) pairs.Enqueue(pair);
                }
            // 번호는 클라이언트 스킬 표(6)의 진짜 번호를 쓴다(선박 스킬 2000 ~ 2162) — 그래야 스킬 그림이 맞는다.
            // 표에서 이름을 못 찾은 것만 3000 + 차례(옛 방식, 그림은 엉뚱하다)
            Dictionary<string, int> real = [];
            try
            {
                foreach (var skill in new DataTables().Skills.Where(s => s.Id is >= 2000 and < 3000 && s.Name.Length > 0))
                    real.TryAdd(skill.Name.Replace(" ", ""), skill.Id);
            }
            catch (Exception) { }
            foreach (var fact in ShipSkillFacts)
            {
                if (all.Exists(s => s.Name.Replace(" ", "") == fact.Name.Replace(" ", "")) || pairs.Count == 0) continue;
                var (a, b) = pairs.Dequeue();
                // 개량갑판: 「항해속도가 상승하고 …」 — 속도 +5%(크기는 지은 것). 화재 · 연막 억제는 전투가 없어 뜻이 없다
                // 이름에서 하는 일을 알 수 있는 스킬에는 효과를 준다(NamedEffects) — 어느 쪽에 듣는가는 이름 그대로이고 크기는 모두 지은 것이다
                var (effect, amount) = NamedEffects.GetValueOrDefault(fact.Name, ("", 0));
                all.Add(new OptionSkill { SkillId = real.GetValueOrDefault(fact.Name.Replace(" ", ""), 3000 + fact.No), Name = fact.Name, PartA = a, PartB = b, Effect = effect, Amount = amount });
            }
            return _optionSkills = all;
        }
    }
    /// <summary>대학의 연구 목록 — <c>data\extracted\research-facts.json</c>(ssjoy 에서 모은 것, 저장소에는 안 둔다). 없으면 대학은 빈다.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public List<ResearchFact> Research { get; set; } = [];
    /// <summary>직업마다 우대 스킬 · 전문 스킬 · 전직 비용 — <c>data\extracted\job-facts.json</c>(ssjoy 에서 모은 것, 저장소에는 안 둔다).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public List<JobFact> JobFacts { get; set; } = [];
    public List<ShipMaterial> ShipMaterials { get; set; } = [];
    public ShipWorkBook ShipWorks { get; set; } = new();
    public List<ShipPart> ShipParts { get; set; } = [];
    /// <summary>장비 아이템이 입히는 모형 — <c>data\extracted\gear-models.json</c>(저장소에는 안 둔다).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public List<GearModel> GearModels { get; set; } = [];
    // 부스트 아이템 — 아이템 표(14)에서 설명에 「속도가 n% 상승」 · 「스킬이 +n」 · 「스킬 효과가 연장」이 든 것 — data\extracted\booster-items.json(저장소에는 안 둔다)
    [System.Text.Json.Serialization.JsonIgnore] public List<PaperItem> Boosters { get; set; } = [];
    // 장비가 올려 주는 스킬 — 장비 번호 → (스킬 번호 → 랭크). 클라이언트에는 없어서 위키 사본에서 뽑는다(<c>data\extracted\gear-boosts.json</c>, 저장소에는 안 둔다)
    [System.Text.Json.Serialization.JsonIgnore] public Dictionary<int, Dictionary<int, int>> GearBoosts { get; set; } = [];
    // 재질의 실제 빛깔(0xRRGGBB) — 배 모형 표(0001\0002.bin) 뒤의 재질 줄(40바이트 × 99, 줄 k = 재질 번호 k + 1)에서 뽑은 것: 칠한 재질은 칠 빛깔, 나무는 나무 빛깔
    [System.Text.Json.Serialization.JsonIgnore] public Dictionary<int, int> MaterialColors { get; set; } = [];
    // 칠한 재질의 띠 빛(재질 줄의 어두운 쪽 칠 빛) — 재질 번호 → 0xRRGGBB. 선체의 띠 조각(빛깔 번호 1 · 2)에 쓴다
    [System.Text.Json.Serialization.JsonIgnore] public Dictionary<int, int> MaterialTrims { get; set; } = [];
    /// <summary>행동력 음식 — 아이템 표(14)에서 설명에 「행동력+n」이 든 것(해물 피자 · 마늘닭 통구이 …) — <c>data\extracted\food-items.json</c>(저장소에는 안 둔다).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public List<PaperItem> Foods { get; set; } = [];
    /// <summary>선박 데코(표 138) · 선원 장비(표 139) — <c>data\extracted\ship-decos.json</c> · <c>crew-gear.json</c>(저장소에는 안 둔다).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public List<ShipDeco> Decos { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore] public List<CrewGear> CrewGears { get; set; } = [];
    /// <summary>부관 후보(표 131) — Group 은 표의 첫 바이트.</summary>
    public List<NamedData> Aides { get; set; } = [];
    /// <summary>부관의 담당 이름(표 36).</summary>
    public List<NamedData> Duties { get; set; } = [];
    public List<NamedData> Ammo { get; set; } = [];
    public List<TavernDish> TavernMenu { get; set; } = [];
    /// <summary>호칭(표 35) — 이름과 설명(Description 은 NamedData 에 없어 Extra 에 둔다).</summary>
    public List<NamedData> Honors { get; set; } = [];
    /// <summary>문화권 이름(클라이언트 표 1) — 도시의 Culture.</summary>
    public List<NamedData> Cultures { get; set; } = [];
    /// <summary>애완동물 이름(클라이언트 표 47).</summary>
    public List<NamedData> Pets { get; set; } = [];
    /// <summary>위키에서 채운 발견물의 난도(필요 스킬 랭크)와 찾는 법 — <c>data\extracted\discovery-facts.json</c>(<c>tools\gvo\wiki_discovery.py</c>). 없는 발견물은 별 수를 랭크로 쓴다.</summary>
    public List<DiscoveryFact> DiscoveryFacts { get; set; } = [];
    /// <summary>大航海時代DB(gvdb.mydns.jp)에서 채운 진짜 의뢰 — <c>data\extracted\quest-facts.json</c>(<c>tools\gvo\gvdb_quests.py</c>).</summary>
    public List<QuestFact> QuestFacts { get; set; } = [];
    /// <summary>시내 장소 이름(표 40).</summary>
    public List<NamedData> Places { get; set; } = [];
    public List<GoodData> Goods { get; set; } = [];
    public List<NamedData> GoodKinds { get; set; } = [];
    public List<MarketData> Markets { get; set; } = [];
    public StartData Start { get; set; } = new();
    public List<NamedData> Nations { get; set; } = [];
    public List<NamedData> Jobs { get; set; } = [];
    public List<CityData> Cities { get; set; } = [];
    public List<NamedData> Seas { get; set; } = [];
    /// <summary>장비 번호 → 장비 효과(이름 → 랭크) — gvdb 의 「装備効果」. 지금은 VigourSave(행동력 감소 억제)만.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Dictionary<int, Dictionary<string, int>> GearEffects { get; set; } = [];
    /// <summary>조선 재료(조빌 아이템)의 강화 수치 — gvdb 의 아이템 목록에서(tools\gvo\gvdb_shipparts.py).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<BuildPart> BuildParts { get; set; } = [];
    /// <summary>스킬 번호 → 원본 설명 글(선박 스킬 따위, 스킬 창에 안 나오는 것들).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Dictionary<int, string> SkillNotes { get; set; } = [];
    public List<LandingData> Landings { get; set; } = [];
    public List<DiscoveryData> Discoveries { get; set; } = [];
    public List<NamedData> DiscoveryKinds { get; set; } = [];

    /// <summary>
    /// 자료 폴더 — 환경 변수 <c>DHO_DATA</c>, 없으면 실행 파일에서 위로 올라가며 <c>data\settings.json</c> 을 찾고,
    /// 그래도 없으면 실행 파일 옆 <c>data</c>.
    /// </summary>
    public static string FindDirectory()
    {
        string? fromEnvironment = Environment.GetEnvironmentVariable("DHO_DATA");
        if (!string.IsNullOrEmpty(fromEnvironment)) return fromEnvironment;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "data", "settings.json")))
                return Path.Combine(dir.FullName, "data");
        return Path.Combine(AppContext.BaseDirectory, "data");
    }

    public static GameData Load(string? directory = null)
    {
        directory ??= FindDirectory();
        string extracted = Path.Combine(directory, "extracted");
        var data = new GameData { Directory = directory };

        if (!File.Exists(Path.Combine(extracted, "cities.json")) || !File.Exists(Path.Combine(extracted, "skills.json"))
            || !File.Exists(Path.Combine(extracted, "ships.json")) || !File.Exists(Path.Combine(extracted, "goods.json"))
            || !File.Exists(Path.Combine(extracted, "nations.json")) || !File.Exists(Path.Combine(extracted, "places.json"))
            || !File.Exists(Path.Combine(extracted, ExtractVersion)))
        {
            data.ExtractFromClient();
            data.SaveExtracted();
            // 뽑은 것을 적었으니 처음부터 다시 읽는다 — 그대로 가면 아래 갈래의 자료(배 성능 · 장비 …)를 안 읽은 채로 돈다
            if (File.Exists(Path.Combine(extracted, ExtractVersion)) && File.Exists(Path.Combine(extracted, "cities.json"))) return Load(directory);
        }
        else
        {
            data.Cities = Read<List<CityData>>(Path.Combine(extracted, "cities.json")) ?? [];
            data.Seas = Read<List<NamedData>>(Path.Combine(extracted, "seas.json")) ?? [];
            data.Landings = Read<List<LandingData>>(Path.Combine(extracted, "landings.json")) ?? [];
            data.Discoveries = Read<List<DiscoveryData>>(Path.Combine(extracted, "discoveries.json")) ?? [];
            data.DiscoveryKinds = Read<List<NamedData>>(Path.Combine(extracted, "discovery-kinds.json")) ?? [];
            data.Skills = Read<List<SkillData>>(Path.Combine(extracted, "skills.json")) ?? [];
            data.BuildParts = Read<List<BuildPart>>(Path.Combine(extracted, "ship-parts-gvdb.json")) ?? [];
            // 익히는 스킬이 아닌 것들(선박 스킬 · 부관 스킬 · 효과)의 원본 설명 글 — tools\gvo\skills.py 의 표 6 에서 뽑아 둔다(없으면 빈 채)
            data.SkillNotes = (Read<Dictionary<string, string>>(Path.Combine(extracted, "skill-notes.json")) ?? []).Where(n => int.TryParse(n.Key, out _)).ToDictionary(n => int.Parse(n.Key), n => n.Value);
            data.Ships = Read<List<ShipData>>(Path.Combine(extracted, "ships.json")) ?? [];
            data.Goods = Read<List<GoodData>>(Path.Combine(extracted, "goods.json")) ?? [];
            data.GoodKinds = Read<List<NamedData>>(Path.Combine(extracted, "good-kinds.json")) ?? [];
            data.Nations = Read<List<NamedData>>(Path.Combine(extracted, "nations.json")) ?? [];
            data.Jobs = Read<List<NamedData>>(Path.Combine(extracted, "jobs.json")) ?? [];
        ShipStats.Facts = (Read<List<ShipFact>>(Path.Combine(extracted, "ship-facts.json")) ?? [])
            .GroupBy(f => f.Name).ToDictionary(g => g.Key, g => g.First());
        data.Places = Read<List<NamedData>>(Path.Combine(extracted, "places.json")) ?? [];
        // 기본 재질 98종(ssjoy 에서 모은 배율) — 있으면 저장소의 몇 가지 대신 쓴다
        if (Read<List<ShipMaterial>>(Path.Combine(extracted, "material-facts.json")) is { Count: > 0 } materials) (data.ShipMaterials, data._materialsFromFacts) = (materials, true);
        data.ShipSkillFacts = Read<List<ShipSkillFact>>(Path.Combine(extracted, "shipskill-facts.json")) ?? [];
        data.ShipDetails = Read<List<ShipDetailFact>>(Path.Combine(extracted, "shipdetail-facts.json")) ?? [];
        data.Papers = Read<List<PaperItem>>(Path.Combine(extracted, "paper-items.json")) ?? [];
        // 아이템 표(items14.json)에서 이름으로 더 고른 것 — 구입 발주서 · 수표 · 변성연금의 책 · 재봉도구. 소지품 이름과 「아이템 추가」 목록에 선다
        foreach (var extra in (Read<List<PaperItem>>(Path.Combine(extracted, "items14.json")) ?? [])
                 .Where(i => (i.Name.Contains("구입 발주서") || i.Name.Contains("구입 발주서") || i.Name.EndsWith("구입 발주서") || i.Name.Contains("발주서(카테고리") || i.Name.StartsWith("수표(") || i.Name is "우로보로스의 책" or "유니콘의 책" or "재봉도구" or "특별 위임 항해 허가증" or "특별발주증서")
                             && i.Id is < 1500793 or > 1500820))
            if (!data.Papers.Exists(p => p.Id == extra.Id)) data.Papers.Add(extra);
        data.Foods = Read<List<PaperItem>>(Path.Combine(extracted, "food-items.json")) ?? [];
        data.MaterialColors = Read<Dictionary<int, int>>(Path.Combine(extracted, "material-colors.json")) ?? [];
        data.MaterialTrims = Read<Dictionary<int, int>>(Path.Combine(extracted, "material-trims.json")) ?? [];
        data.Boosters = Read<List<PaperItem>>(Path.Combine(extracted, "booster-items.json")) ?? [];
        // 이용자 사이트(gvdb items.csv)의 보정이 바탕, 위키에서 뽑은 것이 그것을 덮는다
        data.GearBoosts = Read<Dictionary<int, Dictionary<int, int>>>(Path.Combine(extracted, "gear-boosts-gvdb.json")) ?? [];
        data.GearEffects = Read<Dictionary<int, Dictionary<string, int>>>(Path.Combine(extracted, "gear-effects-gvdb.json")) ?? [];
        foreach (var (gearId, boosts) in Read<Dictionary<int, Dictionary<int, int>>>(Path.Combine(extracted, "gear-boosts.json")) ?? []) data.GearBoosts[gearId] = boosts;
        // 손으로 적어 넣은 것(위키에서 못 뽑은 장비 — 사용자의 기억 따위)이 뽑은 것을 덮는다. 도구가 extracted 의 파일을 새로 써도 남는다
        foreach (var (gearId, boosts) in Read<Dictionary<int, Dictionary<int, int>>>(Path.Combine(directory, "gear-boosts.json")) ?? []) data.GearBoosts[gearId] = boosts;
        data.GearModels = Read<List<GearModel>>(Path.Combine(extracted, "gear-models.json")) ?? [];
        data.Gear = Read<List<GearItem>>(Path.Combine(extracted, "gear-items.json")) ?? [];
        data.MaterialItems = Read<Dictionary<int, int>>(Path.Combine(extracted, "material-items.json")) ?? [];
        data.JobFacts = Read<List<JobFact>>(Path.Combine(extracted, "job-facts.json")) ?? [];
        data.Research = Read<List<ResearchFact>>(Path.Combine(extracted, "research-facts.json")) ?? [];
        data.Aides = Read<List<NamedData>>(Path.Combine(extracted, "aides.json")) ?? [];
        data.Duties = Read<List<NamedData>>(Path.Combine(extracted, "duties.json")) ?? [];
        data.Ammo = Read<List<NamedData>>(Path.Combine(extracted, "ammo.json")) ?? [];
        data.TavernMenu = Read<List<TavernDish>>(Path.Combine(extracted, "tavern-menu.json")) ?? [];
        data.Honors = Read<List<NamedData>>(Path.Combine(extracted, "honors.json")) ?? [];
        data.Cultures = Read<List<NamedData>>(Path.Combine(extracted, "cultures.json")) ?? [];
        data.Pets = Read<List<NamedData>>(Path.Combine(extracted, "pets.json")) ?? [];
        data.DiscoveryFacts = Read<List<DiscoveryFact>>(Path.Combine(extracted, "discovery-facts.json")) ?? [];
        data.QuestFacts = Read<List<QuestFact>>(Path.Combine(extracted, "quest-facts.json")) ?? [];
        data.ShipParts = Read<List<ShipPart>>(Path.Combine(extracted, "ship-parts.json")) ?? [];
        }
        data.Decos = Read<List<ShipDeco>>(Path.Combine(extracted, "ship-decos.json")) ?? [];
        data.CrewGears = Read<List<CrewGear>>(Path.Combine(extracted, "crew-gear.json")) ?? [];

        data.Settings = Read<SettingsData>(Path.Combine(directory, "settings.json")) ?? new SettingsData();
        data.Quests = Read<List<QuestData>>(Path.Combine(directory, "quests.json")) ?? [];
        data.RecipeRules = Read<List<RecipeRule>>(Path.Combine(directory, "recipes.json")) ?? [];
        data.Recipes = Read<List<RecipeData>>(Path.Combine(extracted, "recipes.json")) ?? [];
        // 이용자 사이트(gvdb)에서 뽑은 재료 · 생산물 · 수량 — 손으로 적은 것(지은 값)을 덮는다. 설비 · 도구는 손으로 적은 것을 둔다
        // 실험 도구(시험관 · 증류기 …)는 사이트에 재료로 적혀 있지만 닳지 않는다 — 손으로 적은 레시피의 도구 목록에 있는 번호는 재료에서 도구로 옮긴다
        var labTools = data.RecipeRules.SelectMany(r => r.ToolList()).ToHashSet();
        foreach (var real in Read<List<RecipeRule>>(Path.Combine(extracted, "recipe-inputs.json")) ?? [])
        {
            real.FromSite = true;
            var held = real.InputList().Where(i => labTools.Contains(i.Good)).Select(i => i.Good).ToList();
            if (held.Count > 0)
            {
                real.Inputs = string.Join(",", real.InputList().Where(i => !labTools.Contains(i.Good)).Select(i => $"{i.Good}:{i.Count}"));
                real.Tools = string.Join(",", held);
            }
            if (data.RecipeRules.Find(r => r.RecipeId == real.RecipeId) is { } mine)
            {
                (mine.Output, mine.OutputCount, mine.Inputs, mine.Skill, mine.OutputItem) = (real.Output, real.OutputCount, real.Inputs, real.Skill == "" ? mine.Skill : real.Skill, real.OutputItem);
                if (real.Tools != "") mine.Tools = real.Tools;
                mine.Consumes = "";      // 닳는 도구는 실제 재료 줄에 들어 있다
            }
            else data.RecipeRules.Add(real);
        }
        data.Npcs = Read<NpcNames>(Path.Combine(extracted, "npc-names.json")) ?? new NpcNames();
        data.FleetMissions = Read<List<FleetMission>>(Path.Combine(extracted, "fleet-missions.json")) ?? [];
        // 이용자들이 모은 레시피 자료(번호, 이름, …, 필요 스킬, 만드는 것) — 재료는 없다
        foreach (var row in Read<List<List<System.Text.Json.JsonElement>>>(Path.Combine(extracted, "recipe-facts.json")) ?? [])
            if (row.Count >= 5 && row[0].ValueKind == System.Text.Json.JsonValueKind.Number)
                data.RecipeNotes[row[0].GetInt32()] = (row[3].ToString(), row[4].ToString().Replace(", …", " 외"));
        data.ShipWorks = Read<ShipWorkBook>(Path.Combine(directory, "ship-works.json")) ?? new ShipWorkBook();
        if (!data._materialsFromFacts) data.ShipMaterials = Read<List<ShipMaterial>>(Path.Combine(directory, "ship-materials.json")) ?? [];
        data.Items = Read<List<ItemData>>(Path.Combine(directory, "items.json")) ?? [];
        // 레시피 책은 아이템으로도 선다(도구점 · 소지품) — 값을 아는 책만 도구점에 나온다. items.json 에는 적지 않는다
        data.RecipeBooks = Read<List<RecipeBook>>(Path.Combine(extracted, "recipe-books.json")) ?? [];
        foreach (var book in data.RecipeBooks)
            if (!data.Items.Exists(i => i.Id == book.ItemId)) data.Items.Add(new ItemData { Id = book.ItemId, Name = book.Name, Effect = "RecipeBook", Price = book.Price });
        data.Orders = Read<OrderBook>(Path.Combine(directory, "orders.json")) ?? new OrderBook();
        data.Disasters = Read<List<DisasterData>>(Path.Combine(directory, "disasters.json")) ?? [];
        data.WikiRequests = Read<List<WikiRequest>>(Path.Combine(directory, "wiki-requests.json")) ?? [];
        data.SeaClimates = Read<List<SeaClimate>>(Path.Combine(directory, "sea-climates.json")) ?? [];
        data.Supplies = Read<List<SupplyData>>(Path.Combine(directory, "supplies.json")) ?? [];
        data.SkillRules = Read<List<SkillRuleData>>(Path.Combine(directory, "skill-rules.json")) ?? [];
        data.Markets = Read<List<MarketData>>(Path.Combine(directory, "markets.json")) ?? [];
        data.Start = Read<StartData>(Path.Combine(directory, "start.json")) ?? new StartData();
        data.FillMarkets();
        // 실제 판매 목록(gvdb)이 있는 도시는 그것으로 — 손으로 적은 markets.json 의 글은 그대로 두고 읽을 때만 바꾼다
        data.MarketFacts = Read<List<MarketFact>>(Path.Combine(extracted, "market-facts.json")) ?? [];
        foreach (var fact in data.MarketFacts)
            if (fact.Goods.Count > 0 && data.Markets.Find(m => m.CityId == fact.CityId) is { } real)
            {
                real.RealGoods = fact.Goods.Where(g => g.Length >= 2 && data.Goods.Exists(x => x.Id == g[0])).Select(g => g[0]).ToList();
                foreach (var locked in fact.Goods.Where(g => g.Length >= 3 && g[2] != 0)) real.RealInvest[locked[0]] = locked[2];
            }
        data.Specialties = Read<Dictionary<int, int>>(Path.Combine(extracted, "specialties.json")) ?? [];
        data.Conversions = Read<Dictionary<int, int[]>>(Path.Combine(extracted, "conversions.json")) ?? [];
        foreach (var fact in data.MarketFacts)
            foreach (var buy in fact.Buys.Where(b => b.Length >= 2 && b[1] > 0)) data.BuyPrices[(fact.CityId, buy[0])] = buy[1];
        foreach (var prices in data.MarketFacts.SelectMany(f => f.Goods).Where(g => g.Length >= 2 && g[1] > 0).GroupBy(g => g[0]))
            data.GoodPrices[prices.Key] = prices.Select(g => g[1]).OrderBy(p => p).ElementAt(prices.Count() / 2);
        var points = Read<List<LandingData>>(Path.Combine(directory, "landing-points.json")) ?? [];
        foreach (var point in points)
            if (data.Landings.Find(l => l.Id == point.Id) is { } landing) (landing.X, landing.Y) = (point.X, point.Y);
        return data;
    }

    /// <summary>직접 지은 것과 뽑은 것을 모두 적는다.</summary>
    /// <summary>설정만 적는다(게임에서 해상도를 바꿨을 때).</summary>
    public void SaveSettings() => Write(Path.Combine(Directory, "settings.json"), Settings);

    public void Save()
    {
        System.IO.Directory.CreateDirectory(Directory);
        Write(Path.Combine(Directory, "settings.json"), Settings);
        Write(Path.Combine(Directory, "quests.json"), Quests);
        Write(Path.Combine(Directory, "orders.json"), Orders);
        Write(Path.Combine(Directory, "items.json"), Items.Where(i => i.Effect != "RecipeBook").ToList());
        if (!_materialsFromFacts) Write(Path.Combine(Directory, "ship-materials.json"), ShipMaterials);      // 모아 온 표는 저장소 쪽 파일에 적지 않는다
        Write(Path.Combine(Directory, "ship-works.json"), ShipWorks);
        Write(Path.Combine(Directory, "recipes.json"), RecipeRules.Where(r => !r.FromSite).ToList());
        Write(Path.Combine(Directory, "disasters.json"), Disasters);
        Write(Path.Combine(Directory, "sea-climates.json"), SeaClimates);
        Write(Path.Combine(Directory, "supplies.json"), Supplies);
        Write(Path.Combine(Directory, "skill-rules.json"), SkillRules);
        Write(Path.Combine(Directory, "start.json"), Start);
        // 도시 이름은 클라이언트 것이라 번호와 품목 번호만 적는다
        Write(Path.Combine(Directory, "markets.json"), Markets.Select(m => new { m.CityId, m.Goods, m.Shipyard }).ToList());
        // 상륙지는 찍어 둔 자리만 따로 적는다 — 이름은 클라이언트 것이라 저장소에 두지 않는다
        Write(Path.Combine(Directory, "landing-points.json"),
              Landings.Where(l => (l.X != 0 || l.Y != 0) && !l.FromMap).Select(l => new { l.Id, l.X, l.Y }).ToList());
        SaveExtracted();
    }

    private void SaveExtracted()
    {
        string extracted = Path.Combine(Directory, "extracted");
        System.IO.Directory.CreateDirectory(extracted);
        Write(Path.Combine(extracted, "cities.json"), Cities);
        Write(Path.Combine(extracted, "seas.json"), Seas);
        Write(Path.Combine(extracted, "landings.json"), Landings);
        Write(Path.Combine(extracted, "discoveries.json"), Discoveries);
        Write(Path.Combine(extracted, "discovery-kinds.json"), DiscoveryKinds);
        Write(Path.Combine(extracted, "skills.json"), Skills);
        Write(Path.Combine(extracted, "skill-notes.json"), SkillNotes.ToDictionary(n => n.Key.ToString(), n => n.Value));
        Write(Path.Combine(extracted, "ships.json"), Ships);
        Write(Path.Combine(extracted, "goods.json"), Goods);
        Write(Path.Combine(extracted, "good-kinds.json"), GoodKinds);
        Write(Path.Combine(extracted, "nations.json"), Nations);
        Write(Path.Combine(extracted, "jobs.json"), Jobs);
        Write(Path.Combine(extracted, "places.json"), Places);
        Write(Path.Combine(extracted, "ship-parts.json"), ShipParts);
        Write(Path.Combine(extracted, "ship-decos.json"), Decos);
        Write(Path.Combine(extracted, "crew-gear.json"), CrewGears);
        Write(Path.Combine(extracted, "aides.json"), Aides);
        Write(Path.Combine(extracted, "recipes.json"), Recipes);
        Write(Path.Combine(extracted, "duties.json"), Duties);
        Write(Path.Combine(extracted, "ammo.json"), Ammo);
        Write(Path.Combine(extracted, "tavern-menu.json"), TavernMenu);
        Write(Path.Combine(extracted, "honors.json"), Honors);
        Write(Path.Combine(extracted, "cultures.json"), Cultures);
        Write(Path.Combine(extracted, "pets.json"), Pets);
        File.WriteAllText(Path.Combine(extracted, ExtractVersion), "");
    }

    /// <summary>게임 클라이언트의 표에서 도시·해역·상륙지·발견물을 다시 뽑는다. 찍어 둔 상륙지 자리는 지킨다.</summary>
    public void ExtractFromClient()
    {
        var sceneRows = GvoFiles.ReadMwc(@"0000\local\dt000000.bin", GvoFiles.Korean);
        var tables = new DataTables();
        var map = new WorldMap();
        var scenes = GvoFiles.ReadMwc(@"0000\local\dt000000.bin", GvoFiles.Korean);
        var points = Landings.Where(l => (l.X != 0 || l.Y != 0) && !l.FromMap).ToDictionary(l => l.Id, l => (l.X, l.Y));

        Cities = tables.Cities.Values.OrderBy(c => c.Id).Select(c =>
        {
            map.CityOnLand.TryGetValue(c.Id, out var land);
            map.CityAtSea.TryGetValue(c.Id, out var sea);
            return new CityData
            {
                Id = c.Id, Name = c.Name, Kind = c.Kind, Nation = c.Nation, Culture = c.Culture,
                X = land.X, Y = land.Y, SeaX = sea.X, SeaY = sea.Y, PortScene = PortSceneNumber(scenes, c.Id),
                TownScene = FindScene(scenes, (uint)(0x0800 + c.Id) << 16, c.Id)?.Number ?? 0, Buildings = BuildingsOf(scenes, c.Id),
                Music = SceneMusic(scenes, (uint)(0x0800 + c.Id) << 16),
                GuildScene = BuildingScene(scenes, c.Id, "모험가조합"), Rooms = RoomsOf(scenes, c.Id),
            };
        }).ToList();
        Seas = tables.Seas.Values.OrderBy(s => s.Id).Select(s => new NamedData { Id = s.Id, Name = s.Name, Group = s.Ocean, Music = SceneMusic(sceneRows, (uint)(0x0400 + s.Id) << 16) }).ToList();
        // 해역 지도의 표식으로 상륙지 자리를 더 촘촘히: 해역마다 그 해역의 도시들(지도 자리 ↔ 세계 자리)로 가로 · 세로의 배율과 밀림을 맞춘 뒤 상륙지 표식을 옮긴다.
        // 도시가 둘 넘고 서로 충분히 떨어진 해역만(나머지는 세계지도 표식을 쓴다)
        var fine = new Dictionary<int, (int X, int Y)>();
        foreach (var zone in tables.ZoneMarks.Where(m => m.X < 60000 && m.Y < 60000).GroupBy(m => m.Zone))
        {
            var towns = zone.Where(m => m.Kind == 1).Select(m => (m.X, m.Y, City: Cities.Find(c => c.Id == m.Target))).Where(m => m.City is { } c && (c.X != 0 || c.Y != 0)).ToList();
            if (towns.Count < 2) continue;
            double baseX = towns[0].City!.X;
            (double Scale, double Offset)? Fit(Func<(int X, int Y, CityData? City), double> local, Func<(int X, int Y, CityData? City), double> world)
            {
                double meanLocal = towns.Average(local), meanWorld = towns.Average(world), spread = towns.Sum(t => Math.Pow(local(t) - meanLocal, 2));
                if (spread < 200) return null;
                double scale = towns.Sum(t => (local(t) - meanLocal) * (world(t) - meanWorld)) / spread;
                return (scale, meanWorld - scale * meanLocal);
            }
            var fx = Fit(t => t.X, t => baseX + WorldMap.DeltaX(baseX, t.City!.X));
            var fy = Fit(t => t.Y, t => t.City!.Y);
            if (fx is not { Scale: > 1 and < 12 } ax || fy is not { Scale: > 1 and < 12 } ay) continue;
            foreach (var mark in zone.Where(m => m.Kind == 3))
                fine[mark.Target] = ((int)WorldMap.WrapX(ax.Scale * mark.X + ax.Offset), (int)(ay.Scale * mark.Y + ay.Offset));
        }
        // 세계지도 표식만 있는 상륙지는 표식이 200 쯤 어긋나기도 한다(지도 그림이 고르지 않다) — 가까운 표식 셋 가운데 촘촘한 자리를 아는 것들이
        // 얼마나 어긋났는지를 보고 그만큼 옮긴다
        var known = fine.Where(f => tables.LandingSpots.ContainsKey(f.Key))
            .Select(f => (Rough: tables.LandingSpots[f.Key], Dx: WorldMap.DeltaX(tables.LandingSpots[f.Key].X, f.Value.X), Dy: (double)(f.Value.Y - tables.LandingSpots[f.Key].Y))).ToList();
        var mended = new Dictionary<int, (int X, int Y)>();
        foreach (var (id, rough) in tables.LandingSpots)
        {
            if (fine.ContainsKey(id) || known.Count == 0) continue;
            var near = known.OrderBy(k => Math.Pow(WorldMap.DeltaX(rough.X, k.Rough.X), 2) + Math.Pow(k.Rough.Y - rough.Y, 2)).Take(3)
                .Where(k => Math.Abs(WorldMap.DeltaX(rough.X, k.Rough.X)) < 1500 && Math.Abs(k.Rough.Y - rough.Y) < 1500).ToList();
            if (near.Count > 0) mended[id] = ((int)WorldMap.WrapX(rough.X + near.Average(k => k.Dx)), (int)(rough.Y + near.Average(k => k.Dy)));
        }
        Landings = tables.Landings.Values.OrderBy(l => l.Id).Select(l =>
        {
            points.TryGetValue(l.Id, out var point);
            tables.LandingSpots.TryGetValue(l.Id, out var spot);
            if (fine.TryGetValue(l.Id, out var better)) spot = better;
            else if (mended.TryGetValue(l.Id, out var moved)) spot = moved;
            return new LandingData { Id = l.Id, Name = l.Name, City = l.City, Region = l.Region, X = point.X, Y = point.Y, MapX = spot.X, MapY = spot.Y,
                                    ObservePoints = tables.LandPoints.GetValueOrDefault(l.Id).Observe, GatherKinds = [.. tables.LandPoints.GetValueOrDefault(l.Id).Gather ?? []] };
        }).ToList();
        Discoveries = tables.Discoveries.Values.OrderBy(d => d.Id).Select(d => new DiscoveryData
        {
            Id = d.Id, Name = d.Name, Description = d.Description, Kind = d.Kind, Stars = d.Stars, Exp = d.Exp, Fame = d.Fame,
        }).ToList();
        DiscoveryKinds = tables.DiscoveryKinds.OrderBy(k => k.Key).Select(k => new NamedData { Id = k.Key, Name = k.Value }).ToList();
        Skills = tables.Skills.Where(s => s.Group <= 3).Select(s => new SkillData
        {
            Id = s.Id, Name = s.Name, Description = s.Description, Group = s.Group, Cost = s.Cost,
        }).ToList();
        SkillNotes = tables.Skills.Where(s => s.Group > 3 && s.Description.Length > 0).GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First().Description);
        Goods = tables.Goods.Select(g => new GoodData { Id = g.Id, Name = g.Name, Description = g.Description, Kind = g.Kind }).ToList();
        Nations = tables.Nations.Select(n => new NamedData { Id = n.Id, Name = n.Name }).ToList();
        Jobs = tables.Jobs.Select(j => new NamedData { Id = j.Id, Name = j.Name, Group = j.Line }).ToList();
        Recipes = tables.Recipes.Where(r => r.Name.Length > 0 && !r.Name.StartsWith('※')).Select(r => new RecipeData { Id = r.Id, Name = r.Name, Description = r.Description }).ToList();
        Aides = tables.Aides.Select(a => new NamedData { Id = a.Id, Name = a.Name, Group = a.A }).ToList();
        Duties = tables.Duties.OrderBy(d => d.Key).Select(d => new NamedData { Id = d.Key, Name = d.Value }).ToList();
        Ammo = tables.Ammo.OrderBy(d => d.Key).Select(d => new NamedData { Id = d.Key, Name = d.Value }).ToList();
        Pets = tables.Pets.Where(p => p.Name.Length > 0 && !p.Name.StartsWith('※')).Select(p => new NamedData { Id = p.Id, Name = p.Name }).ToList();
        Cultures = tables.Cultures.Select(c => new NamedData { Id = c.Id, Name = c.Name }).ToList();
        Honors = tables.Honors.Where(h => h.Name.Length > 0 && !h.Name.StartsWith('※')).Select(h => new NamedData { Id = h.Id, Name = h.Name, Extra = h.Description.Replace("\n", " ") }).ToList();
        TavernMenu = tables.TavernMenu.Where(m => m.Name.Length > 0 && !m.Name.StartsWith('※')).ToList();
        ShipParts = tables.ShipParts.Where(p => p.Name.Length > 0 && !p.Name.StartsWith('※')).ToList();
        Decos = tables.Decos.Where(d => d.Name.Length > 0 && !d.Name.StartsWith('※')).ToList();
        CrewGears = tables.CrewGears.Where(g => g.Name.Length > 0 && !g.Name.StartsWith('※')).ToList();
        Places = tables.Places.OrderBy(p => p.Key).Select(p => new NamedData { Id = p.Key, Name = p.Value }).ToList();
        GoodKinds = tables.GoodKinds.OrderBy(k => k.Key).Select(k => new NamedData { Id = k.Key, Name = k.Value }).ToList();
        // 빈 줄(※)과 개조·명품·기념·체험 판은 뺀다
        // 「명품 · 개량 · 개조 …」이 붙은 배도 실제 배다(선박 교환권으로 받는다) — 다 넣고, 조선소 판매 목록에서만 뺀다
        Ships = tables.Ships.Where(s => !s.Name.StartsWith('※') && s.Length > 0)
            .Select(s => new ShipData
            {
                Id = s.Id, Name = s.Name, Description = s.Description, Model = s.Model, Height = s.Height, Width = s.Width,
                Length = s.Length, SizeClass = s.SizeClass, Kind = s.Kind, Masts = s.Masts,
            }).ToList();
    }

    public string SavePath => Path.Combine(Directory, "save.json");

    public SaveData? LoadSave() => Read<SaveData>(SavePath);

    public void WriteSave(SaveData save)
    {
        System.IO.Directory.CreateDirectory(Directory);
        Write(SavePath, save);
    }

    public void DeleteSave()
    {
        if (File.Exists(SavePath)) File.Delete(SavePath);
    }
    /// <summary>
    /// 판매 목록이 없는 도시를 채운다 — 같은 지방의 도시는 같은 갈래 여섯에서, 도시마다 다른 품목을 고른다(늘 같은 결과).
    /// 물고기(1601001~)는 팔지 않는다.
    /// </summary>
    public void FillMarkets()
    {
        var sellable = Goods.Where(g => g.Id < 1601001 && !g.Name.StartsWith('※')).ToList();
        if (sellable.Count == 0) return;
        foreach (var city in Cities)
        {
            if (city.SeaX == 0 && city.SeaY == 0) continue;
            var market = Markets.Find(m => m.CityId == city.Id);
            if (market == null) Markets.Add(market = new MarketData { CityId = city.Id });
            market.City = city.Name;
            market.Shipyard ??= city.Kind <= 1 || city.Buildings.Length > 0;
            if (market.Goods.Length > 0) continue;

            var kinds = new Random(1000 + city.Culture).GetItems(Enumerable.Range(0, 20).ToArray(), 6);
            var pool = sellable.Where(g => kinds.Contains(g.Kind)).ToList();
            if (pool.Count == 0) pool = sellable;
            var random = new Random(city.Id);
            market.Goods = string.Join(",", Enumerable.Range(0, Settings.Trade.GoodsPerCity)
                .Select(_ => pool[random.Next(pool.Count)].Id).Distinct());
        }
        Markets.Sort((a, b) => a.CityId.CompareTo(b.CityId));
    }
    /// <summary>
    /// 장면 표(<c>dt000000</c>)에서 도시의 항구 장면 번호를 찾는다.
    /// 항구 줄: u32 장면 id(<c>(0x1C00 + 도시 id) &lt;&lt; 16</c>), u32 어미 장면, u32 도시 id, 이름,
    /// NUL 로 끝나는 글 셋(자원 이름 <c>TOWN_TUNIS_P_000</c>, 환경 <c>PORT004</c> 두 번), u32 장면 번호(7004).
    /// </summary>
    public static int PortSceneNumber(byte[] sceneTable, int cityId) =>
        FindScene(sceneTable, (uint)(0x1C00 + cityId) << 16, cityId)?.Number ?? 0;

    /// <summary>
    /// 도시의 건물 줄(장면 id <c>(0x0C00 + 도시 id) &lt;&lt; 16 | 차례 &lt;&lt; 8</c>)의 이름들.
    /// 길드 사무소와 빈집은 수만 많고 쓸 데가 없어 뺀다.
    /// </summary>
    public static string BuildingsOf(byte[] sceneTable, int cityId)
    {
        var names = new List<string>();
        for (int n = 0; n < 64; n++)
        {
            if (FindScene(sceneTable, (uint)(0x0C00 + cityId) << 16 | (uint)n << 8, cityId) is not { } row) break;
            if (!row.Name.Contains("길드 사무소") && row.Name != "빈집") names.Add(row.Name);
        }
        return string.Join(", ", names);
    }

    /// <summary>뽑은 것의 판 — 뽑는 칸이 늘면 이름을 바꿔 다시 뽑게 한다.</summary>
    private const string ExtractVersion = "extracted-20";

    public static string RoomsOf(byte[] sceneTable, int cityId)
    {
        var rooms = new List<string>();
        for (int n = 0; n < 64; n++)
        {
            if (FindScene(sceneTable, (uint)(0x0C00 + cityId) << 16 | (uint)n << 8, cityId) is not { } row) break;
            if (!row.Name.Contains("길드 사무소") && row.Name != "빈집" && row.Number > 0) rooms.Add($"{row.Name}={row.Number}");
        }
        return string.Join(", ", rooms);
    }

    /// <summary>이름에 그 말이 든 건물 줄의 방 장면 번호. 없으면 0.</summary>
    public static int BuildingScene(byte[] sceneTable, int cityId, string word)
    {
        for (int n = 0; n < 64; n++)
        {
            if (FindScene(sceneTable, (uint)(0x0C00 + cityId) << 16 | (uint)n << 8, cityId) is not { } row) break;
            if (row.Name.Contains(word)) return row.Number;
        }
        return 0;
    }

    /// <summary>
    /// 장면 줄의 배경음 번호 — 꼬리(글 셋 뒤)의 여덟째 u32. 아홉째는 소리 크기(100)다.
    /// 리스본 3 · 세비야 4 · 마르세이유 5 · 베네치아 6 · 런던 7 · 암스테르담 8, 북유럽 9 · 이베리아 10 · 아프리카 11 · 이슬람 12 · 인도 13 …, 해역은 18 · 20 · 21 …
    /// </summary>
    public static int SceneMusic(byte[] sceneTable, uint sceneId)
    {
        var key = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(key, sceneId);
        for (int at = 0; ; at += 4)
        {
            int found = sceneTable.AsSpan(at).IndexOf(key);
            if (found < 0) return 0;
            at += found;
            if (at + 14 > sceneTable.Length) return 0;
            int cursor = at + 12, length = BinaryPrimitives.ReadUInt16LittleEndian(sceneTable.AsSpan(cursor));
            if (length > 400 || length % 4 != 0) continue;
            cursor += 2 + length;
            bool named = true;
            for (int i = 0; i < 3 && named; i++)
            {
                int end = cursor < sceneTable.Length ? Array.IndexOf(sceneTable, (byte)0, cursor) : -1;
                if (end < 0 || end - cursor > 60 || (i == 0 && end - cursor < 6)) named = false;
                else cursor = end + 1;
            }
            if (!named || cursor + 36 > sceneTable.Length) continue;
            int music = BinaryPrimitives.ReadInt32LittleEndian(sceneTable.AsSpan(cursor + 28));
            int volume = BinaryPrimitives.ReadInt32LittleEndian(sceneTable.AsSpan(cursor + 32));
            if (music is > 0 and < 1000 && volume is > 0 and <= 100) return music;
        }
    }

    /// <summary>장면 표에서 줄 하나 — 이름과 장면 번호.</summary>
    public static (string Name, int Number)? FindScene(byte[] sceneTable, uint sceneId, int cityId)
    {
        var key = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(key, sceneId);
        for (int at = 0; ; at += 4)
        {
            int found = sceneTable.AsSpan(at).IndexOf(key);
            if (found < 0) return null;
            at += found;
            if (at + 14 > sceneTable.Length || BinaryPrimitives.ReadInt32LittleEndian(sceneTable.AsSpan(at + 8)) != cityId) continue;

            int cursor = at + 12;
            int nameAt = cursor;
            cursor += 2 + BinaryPrimitives.ReadUInt16LittleEndian(sceneTable.AsSpan(cursor));
            if (cursor >= sceneTable.Length) continue;
            string resource = "";
            for (int i = 0; i < 3; i++)
            {
                int end = Array.IndexOf(sceneTable, (byte)0, cursor);
                if (end < 0) return null;
                if (i == 0) resource = Encoding.ASCII.GetString(sceneTable, cursor, end - cursor);
                cursor = end + 1;
            }
            if (!resource.StartsWith("TOWN_") || cursor + 4 > sceneTable.Length) continue;
            return (DataTables.TextAt(sceneTable, nameAt, (int)sceneId), BinaryPrimitives.ReadInt32LittleEndian(sceneTable.AsSpan(cursor)));
        }
    }

    private static T? Read<T>(string path) where T : class =>
        File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) : null;

    private static void Write<T>(string path, T value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
}
