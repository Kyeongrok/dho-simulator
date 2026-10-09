using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 스킬이 이 게임에서 하는 일을 한 줄로 — 스킬 창이 설명 글 아래에 보인다.
/// 스킬의 설명 글은 원본의 것이고, 여기 적는 효과의 크기는 스킬 규칙(<c>data\skill-rules.json</c>)의 값 — 대부분 지어 넣은 것이다.
/// </summary>
internal sealed partial class Voyage
{
    public string SkillEffect(int skillId)
    {
        var notes = Data.SkillRules.Where(r => r.SkillId == skillId).Select(RuleEffect).Where(n => n != "").Distinct().ToList();
        return notes.Count == 0 ? "" : string.Join(" · ", notes);
    }

    private string RuleEffect(SkillRuleData rule)
    {
        string pct = $"{rule.PerRank * 100:0.#}%";
        string Forms() => string.Join(" · ", rule.Targets.Select(f => FormNames.ElementAtOrDefault(f)).Where(n => !string.IsNullOrEmpty(n)));
        string Names(Func<int, string?> name) => string.Join(" · ", rule.Targets.Select(name).Where(n => !string.IsNullOrEmpty(n)));
        return rule.Effect switch
        {
            "Speed" => "항해속도 — 홀수 랭크마다 +1% (15랭 8% · 19랭 10%)",
            "Row" => $"조력이 있는 배의 속도 랭크마다 +{pct}",
            "Turn" => $"선회 랭크마다 +{pct}, 기뢰 피해 랭크마다 −3%",
            "Survey" => "켜 두면 주변 지도와 좌표가 보인다",
            "Procure" => "바다에서 물 · 식량을 조달한다",
            "Fish" => "바다에서 물고기를 낚는다",
            "Repair" => $"바다에서 배를 고친다(랭크마다 +{pct})",
            "Rest" => "주점 밖에서 선원의 피로를 푼다",
            "Ration" => $"물 · 식량이 주는 양 랭크마다 −{pct}",
            "CrewLoss" => $"잃는 선원 랭크마다 −{pct}",
            "Cure" => $"재해를 가라앉힌다: {Names(id => Data.Disasters.Find(d => d.Id == id)?.Name)}",
            "Discount" => $"보급 값 랭크마다 −{pct}",
            "Haggle" => $"흥정 폭 랭크마다 +{pct}",
            "TradeKind" => $"{Names(id => Data.GoodKinds.Find(k => k.Id == id)?.Name)} 구입량 랭크마다 +{pct}",
            "Find" => "발견물을 찾는다",
            "Appraise" => "발견물을 감정한다. 서고에서 책을 읽어 올릴 수 있다",
            "Craft" => "레시피로 물건을 만든다",
            "Shipbuilding" => "배를 짓고 강화한다",
            "Shot" => $"포격 피해 랭크마다 +{pct}",
            "Range" => $"대포 사정 랭크마다 +{pct}",
            "Reload" => $"장전 시간 랭크마다 −{pct}",
            "Rake" => $"관통 포격 피해 랭크마다 +{pct}",
            "ShotArmor" => $"받는 포격 랭크마다 −{pct}",
            "Powder" => $"화염탄 · 작렬탄 · 유탄 피해 랭크마다 +{pct}",
            "Melee" => $"백병전 · 육상전 공격 랭크마다 +{pct}",
            "MeleeGuard" => $"육상전 방어 랭크마다 +{pct}",
            "Charge" => $"백병전 「돌격」 전술의 피해 랭크마다 +{pct}",
            "Guard" => $"백병전 「방어」 전술로 받는 피해 랭크마다 −{pct}",
            "Volley" => $"백병전 「총격」 전술의 피해 랭크마다 +{pct}",
            "Tactics" => $"백병전에서 눌린 전술을 랭크마다 {pct}로 뒤집는다",
            "Loot" => $"해전의 전리금 랭크마다 +{pct}, 백병전에서 짐을 빼앗는다",
            "BoardReach" => $"백병전을 걸 수 있는 거리 랭크마다 +{pct}",
            "Flee" => $"해전에서 달아난다(성공률 50% + 랭크마다 {pct})",
            "Watch" => $"해적이 덤빌 확률 랭크마다 −{pct}",
            "Lookout" => $"먼 배의 이름이 보이는 거리 랭크마다 +{rule.PerRank:0.#}",
            "Surgery" => $"해전 중 다섯 초마다 선원 {rule.PerRank:0.#} × 랭크 명을 돌려놓는다(물 1)",
            "Rescue" => $"해전에서 이기면 잃은 선원의 랭크마다 {pct}를 건진다",
            "Mine" => $"해전에서 기뢰를 깐다(피해 60 + 랭크마다 {rule.PerRank:0})",
            "MineSight" => $"적의 기뢰가 보이고 랭크마다 {pct}로 비켜 간다",
            "Aid" => $"해전에서 원군을 부른다(한 싸움에 한 번, 포격 세 번 — 한 번에 30 + 랭크마다 {rule.PerRank:0})",
            "LandRanged" => $"육상전 공격력 랭크마다 +{pct}",
            "Technique" => $"육상전 테크닉의 피해 랭크마다 +{pct}",
            "GearUse" => $"{Forms()}의 특수장비(충각 · 조교 따위) 세기 랭크마다 +{pct}",
            "FormSail" => $"{Forms()}의 속도 랭크마다 +{pct}",
            "FormKeep" => $"{Forms()}의 물 · 식량 소모 랭크마다 −{pct}",
            "Lockpick" => $"상륙지에서 찾은 잠긴 궤를 연다(30% + 랭크마다 {pct})",
            "Salvage" => "침몰선을 끌어올린다(한 번에 15% + 랭크마다 3%, 실패 15% − 랭크마다 1%p)",
            "Tow" => "침몰선을 끌고 갈 때 로프가 상할 확률 랭크마다 −1%p(하루 10%에서)",
            "Language" => $"말이 통하는 곳: {Names(id => Data.Cultures.Find(c => c.Id == id)?.Name)} (거기서 흥정을 할 수 있다)",
            "BookLanguage" => $"서고의 책을 읽는다: {Names(id => Data.Cultures.Find(c => c.Id == id)?.Name)}",
            "Pet" => "애완동물이 하루에 (랭크 + 친밀도 ÷ 20)%로 교역품을 찾아 오고, 해적이 덤빌 때 (랭크 × 2 + 친밀도 ÷ 10)%로 피한다",
            "BodyTalk" => "어느 도시에서나 말이 통한다(흥정을 할 수 있다)",
            "Gather" => "상륙지에서 채집한다(한 번 오르면 세 번). 바다에서 켜 두면 해수 · 해초 따위를 건진다",
            "Observe" => $"상륙지를 둘러볼 때 랭크마다 {pct}로 묻힌 것을 찾는다",
            "March" => $"뭍에서의 피로와 도적 · 맹수가 나올 확률 랭크마다 −{pct}",
            "Social" => $"칙명의 공적 랭크마다 +{pct}, 뇌물 값 랭크마다 −{pct}",
            "Chat" => $"주점의 한턱 값 랭크마다 −{pct}",
            _ => "",
        };
    }
}
