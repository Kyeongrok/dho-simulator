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
    /// <summary>배를 대는 바다 자리(세계 좌표). 클라이언트 자료에 없어 직접 찍는다. 0 이면 아직 없음.</summary>
    public int X { get; set; }
    public int Y { get; set; }
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

    public IEnumerable<int> GoodIds() =>
        Goods.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out int id) ? id : 0).Where(id => id != 0);
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

/// <summary>레시피 이름(클라이언트 표 16).</summary>
public sealed class RecipeData
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
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

    public IEnumerable<(int Good, int Count)> InputList() =>
        Inputs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split(':')).Where(p => p.Length == 2 && int.TryParse(p[0], out _) && int.TryParse(p[1], out _))
            .Select(p => (int.Parse(p[0]), int.Parse(p[1])));
}

/// <summary>소비 아이템 — 이름과 번호는 클라이언트 아이템 표(14)의 것이고, 하는 일과 값은 지은 것이다.</summary>
public sealed class ItemData
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Fatigue 피로를 Amount 만큼 풀기 · Repair 내구를 Amount % 고치기 · Cure 재해(Amount = 재해 번호) 풀기 · Lifebuoy 난파를 한 번 막고 내구 Amount % 로.</summary>
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
    /// <summary>가진 레시피 번호들.</summary>
    public List<int> Recipes { get; set; } = [];
    /// <summary>작위(0 부터) · 공적 · 받은 칙명 id · 칙명의 진행(들른 곳 수나 보고한 발견 수).</summary>
    public int[] Court { get; set; } = [0, 0, 0, 0];
    /// <summary>고용한 부관 — [후보 id, 담당, 레벨, 경험].</summary>
    public List<double[]> Aides { get; set; } = [];
    /// <summary>타고 있는 배에 단 부품.</summary>
    public List<int> Parts { get; set; } = [];
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
    /// <summary>단축키 — 하는 일의 이름 → 글쇠(가상 키 번호). 없는 것은 기본값을 쓴다. 게임의 「단축키 등록」에서 바꾼다.</summary>
    public Dictionary<string, int> Keys { get; set; } = new();
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
    public List<SupplyData> Supplies { get; set; } = [];
    public List<SkillRuleData> SkillRules { get; set; } = [];
    public List<SkillData> Skills { get; set; } = [];
    public List<ShipData> Ships { get; set; } = [];
    public OrderBook Orders { get; set; } = new();
    public List<RecipeData> Recipes { get; set; } = [];
    public List<RecipeRule> RecipeRules { get; set; } = [];
    public List<ItemData> Items { get; set; } = [];
    public List<ShipMaterial> ShipMaterials { get; set; } = [];
    public ShipWorkBook ShipWorks { get; set; } = new();
    public List<ShipPart> ShipParts { get; set; } = [];
    /// <summary>부관 후보(표 131) — Group 은 표의 첫 바이트.</summary>
    public List<NamedData> Aides { get; set; } = [];
    /// <summary>부관의 담당 이름(표 36).</summary>
    public List<NamedData> Duties { get; set; } = [];
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
        }
        else
        {
            data.Cities = Read<List<CityData>>(Path.Combine(extracted, "cities.json")) ?? [];
            data.Seas = Read<List<NamedData>>(Path.Combine(extracted, "seas.json")) ?? [];
            data.Landings = Read<List<LandingData>>(Path.Combine(extracted, "landings.json")) ?? [];
            data.Discoveries = Read<List<DiscoveryData>>(Path.Combine(extracted, "discoveries.json")) ?? [];
            data.DiscoveryKinds = Read<List<NamedData>>(Path.Combine(extracted, "discovery-kinds.json")) ?? [];
            data.Skills = Read<List<SkillData>>(Path.Combine(extracted, "skills.json")) ?? [];
            data.Ships = Read<List<ShipData>>(Path.Combine(extracted, "ships.json")) ?? [];
            data.Goods = Read<List<GoodData>>(Path.Combine(extracted, "goods.json")) ?? [];
            data.GoodKinds = Read<List<NamedData>>(Path.Combine(extracted, "good-kinds.json")) ?? [];
            data.Nations = Read<List<NamedData>>(Path.Combine(extracted, "nations.json")) ?? [];
            data.Jobs = Read<List<NamedData>>(Path.Combine(extracted, "jobs.json")) ?? [];
        ShipStats.Facts = (Read<List<ShipFact>>(Path.Combine(extracted, "ship-facts.json")) ?? [])
            .GroupBy(f => f.Name).ToDictionary(g => g.Key, g => g.First());
        data.Places = Read<List<NamedData>>(Path.Combine(extracted, "places.json")) ?? [];
        data.Aides = Read<List<NamedData>>(Path.Combine(extracted, "aides.json")) ?? [];
        data.Duties = Read<List<NamedData>>(Path.Combine(extracted, "duties.json")) ?? [];
        data.ShipParts = Read<List<ShipPart>>(Path.Combine(extracted, "ship-parts.json")) ?? [];
        }

        data.Settings = Read<SettingsData>(Path.Combine(directory, "settings.json")) ?? new SettingsData();
        data.Quests = Read<List<QuestData>>(Path.Combine(directory, "quests.json")) ?? [];
        data.RecipeRules = Read<List<RecipeRule>>(Path.Combine(directory, "recipes.json")) ?? [];
        data.Recipes = Read<List<RecipeData>>(Path.Combine(extracted, "recipes.json")) ?? [];
        data.ShipWorks = Read<ShipWorkBook>(Path.Combine(directory, "ship-works.json")) ?? new ShipWorkBook();
        data.ShipMaterials = Read<List<ShipMaterial>>(Path.Combine(directory, "ship-materials.json")) ?? [];
        data.Items = Read<List<ItemData>>(Path.Combine(directory, "items.json")) ?? [];
        data.Orders = Read<OrderBook>(Path.Combine(directory, "orders.json")) ?? new OrderBook();
        data.Disasters = Read<List<DisasterData>>(Path.Combine(directory, "disasters.json")) ?? [];
        data.Supplies = Read<List<SupplyData>>(Path.Combine(directory, "supplies.json")) ?? [];
        data.SkillRules = Read<List<SkillRuleData>>(Path.Combine(directory, "skill-rules.json")) ?? [];
        data.Markets = Read<List<MarketData>>(Path.Combine(directory, "markets.json")) ?? [];
        data.Start = Read<StartData>(Path.Combine(directory, "start.json")) ?? new StartData();
        data.FillMarkets();
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
        Write(Path.Combine(Directory, "items.json"), Items);
        Write(Path.Combine(Directory, "ship-materials.json"), ShipMaterials);
        Write(Path.Combine(Directory, "ship-works.json"), ShipWorks);
        Write(Path.Combine(Directory, "recipes.json"), RecipeRules);
        Write(Path.Combine(Directory, "disasters.json"), Disasters);
        Write(Path.Combine(Directory, "supplies.json"), Supplies);
        Write(Path.Combine(Directory, "skill-rules.json"), SkillRules);
        Write(Path.Combine(Directory, "start.json"), Start);
        // 도시 이름은 클라이언트 것이라 번호와 품목 번호만 적는다
        Write(Path.Combine(Directory, "markets.json"), Markets.Select(m => new { m.CityId, m.Goods, m.Shipyard }).ToList());
        // 상륙지는 찍어 둔 자리만 따로 적는다 — 이름은 클라이언트 것이라 저장소에 두지 않는다
        Write(Path.Combine(Directory, "landing-points.json"),
              Landings.Where(l => l.X != 0 || l.Y != 0).Select(l => new { l.Id, l.X, l.Y }).ToList());
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
        Write(Path.Combine(extracted, "ships.json"), Ships);
        Write(Path.Combine(extracted, "goods.json"), Goods);
        Write(Path.Combine(extracted, "good-kinds.json"), GoodKinds);
        Write(Path.Combine(extracted, "nations.json"), Nations);
        Write(Path.Combine(extracted, "jobs.json"), Jobs);
        Write(Path.Combine(extracted, "places.json"), Places);
        Write(Path.Combine(extracted, "ship-parts.json"), ShipParts);
        Write(Path.Combine(extracted, "aides.json"), Aides);
        Write(Path.Combine(extracted, "recipes.json"), Recipes);
        Write(Path.Combine(extracted, "duties.json"), Duties);
        File.WriteAllText(Path.Combine(extracted, ExtractVersion), "");
    }

    /// <summary>게임 클라이언트의 표에서 도시·해역·상륙지·발견물을 다시 뽑는다. 찍어 둔 상륙지 자리는 지킨다.</summary>
    public void ExtractFromClient()
    {
        var sceneRows = GvoFiles.ReadMwc(@"0000\local\dt000000.bin", GvoFiles.Korean);
        var tables = new DataTables();
        var map = new WorldMap();
        var scenes = GvoFiles.ReadMwc(@"0000\local\dt000000.bin", GvoFiles.Korean);
        var points = Landings.Where(l => l.X != 0 || l.Y != 0).ToDictionary(l => l.Id, l => (l.X, l.Y));

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
        Landings = tables.Landings.Values.OrderBy(l => l.Id).Select(l =>
        {
            points.TryGetValue(l.Id, out var point);
            return new LandingData { Id = l.Id, Name = l.Name, City = l.City, Region = l.Region, X = point.X, Y = point.Y };
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
        Goods = tables.Goods.Select(g => new GoodData { Id = g.Id, Name = g.Name, Description = g.Description, Kind = g.Kind }).ToList();
        Nations = tables.Nations.Select(n => new NamedData { Id = n.Id, Name = n.Name }).ToList();
        Jobs = tables.Jobs.Select(j => new NamedData { Id = j.Id, Name = j.Name, Group = j.Line }).ToList();
        Recipes = tables.Recipes.Where(r => r.Name.Length > 0 && !r.Name.StartsWith('※')).Select(r => new RecipeData { Id = r.Id, Name = r.Name, Description = r.Description }).ToList();
        Aides = tables.Aides.Select(a => new NamedData { Id = a.Id, Name = a.Name, Group = a.A }).ToList();
        Duties = tables.Duties.OrderBy(d => d.Key).Select(d => new NamedData { Id = d.Key, Name = d.Value }).ToList();
        ShipParts = tables.ShipParts.Where(p => p.Name.Length > 0 && !p.Name.StartsWith('※')).ToList();
        Places = tables.Places.OrderBy(p => p.Key).Select(p => new NamedData { Id = p.Key, Name = p.Value }).ToList();
        GoodKinds = tables.GoodKinds.OrderBy(k => k.Key).Select(k => new NamedData { Id = k.Key, Name = k.Value }).ToList();
        // 빈 줄(※)과 개조·명품·기념·체험 판은 뺀다
        string[] variants = ["개조", "명품", "기념", "체험", "개량"];
        Ships = tables.Ships.Where(s => !s.Name.StartsWith('※') && s.Length > 0 && !variants.Any(s.Name.Contains))
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
    private const string ExtractVersion = "extracted-9";

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
