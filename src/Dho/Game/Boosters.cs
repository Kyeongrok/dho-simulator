using System.Text.RegularExpressions;
using Dho.Data;

namespace Dho.Game;

// 부스트 아이템 — 아이템 표(14)의 설명 글대로: 「n 동안 선박의 속도가 p% 상승」, 「n 동안 (모험 · 교역 · 전투) 스킬이 +k 된다. 상한치 m랭크」,
// 「사용하면 스킬 효과가 연장된다」. 지속 시간은 설명(또는 이름 끝 괄호)의 「30초 · 60초 · 30분 · 1시간 · 1일 · 7일 …」을 그대로 플레이 시간으로 친다.
// 시간이 안 적힌 것은 한 시간, 「스킬 효과가 연장」은 켜 두는 스킬의 지속을 두 배로 한다(이 둘은 지은 값). 같은 갈래는 겹치지 않고 새것으로 바뀐다.
internal sealed class Boost
{
    public string Kind = "";            // Speed · Skill · Extend
    public string Name = "";
    public double Amount;               // Speed: 비율(0.3), Skill: 랭크 수
    public int Group = -1;              // Skill: 0 모험 · 1 교역 · 2 전투, −1 모든 스킬
    public int SkillId;                 // Skill: 한 스킬만(0 이면 갈래 전체)
    public int Cap = 20;
    public double Until;
}

internal sealed partial class Voyage
{
    public List<Boost> Boosts { get; } = [];
    private Dictionary<int, PaperItem>? _boosters;

    public PaperItem? BoosterOf(int item) => (_boosters ??= Data.Boosters.ToDictionary(b => b.Id)).GetValueOrDefault(item);

    private void ExpireBoosts()
    {
        for (int i = Boosts.Count - 1; i >= 0; i--)
            if (Boosts[i].Until <= Clock) { Say($"{Boosts[i].Name}의 효과가 끝났다."); Boosts.RemoveAt(i); }
    }

    public double BoostSpeed => Boosts.Where(b => b.Kind == "Speed").Select(b => b.Amount).DefaultIfEmpty(0).Max();
    public double BoostExtend => Boosts.Exists(b => b.Kind == "Extend") ? 2 : 1;

    // 그 스킬에 붙는 랭크 — 익힌 스킬에만, 상한치까지
    public int BoostRank(int skillId, int rank)
    {
        if (rank <= 0 || Boosts.Count == 0) return 0;
        int group = -2, best = 0;
        foreach (var boost in Boosts)
        {
            if (boost.Kind != "Skill") continue;
            if (boost.SkillId != 0 ? boost.SkillId != skillId : boost.Group >= 0 && boost.Group != (group == -2 ? group = Data.Skills.Find(s => s.Id == skillId)?.Group ?? -1 : group)) continue;
            best = Math.Max(best, Math.Clamp(boost.Cap - rank, 0, (int)boost.Amount));
        }
        return best;
    }

    private static double BoostSeconds(string text)
    {
        var m = Regex.Match(text, @"(\d+)\s*(초|분|시간|일)");
        if (!m.Success) return 3600;
        double n = double.Parse(m.Groups[1].Value);
        return m.Groups[2].Value switch { "초" => n, "분" => n * 60, "시간" => n * 3600, _ => n * 86400 };
    }

    // 부스트 아이템을 쓴다 — 썼으면 true
    private bool UseBooster(PaperItem item)
    {
        string text = item.Description.Replace("\n", " ");
        var boost = new Boost { Name = item.Name, Until = Clock + BoostSeconds(Regex.IsMatch(text, @"\d+\s*(초|분|시간|일)\s*동안|\d+\s*시간，") ? text : item.Name) };
        if (Regex.Match(text, @"속도가 (\d+)% 상승") is { Success: true } speed) (boost.Kind, boost.Amount) = ("Speed", double.Parse(speed.Groups[1].Value) / 100);
        else if (Regex.Match(text, @"스킬이 ?\+(\d+)") is { Success: true } skill)
        {
            (boost.Kind, boost.Amount) = ("Skill", double.Parse(skill.Groups[1].Value));
            if (Regex.Match(text, @"상한치 ?(\d+)") is { Success: true } cap) boost.Cap = int.Parse(cap.Groups[1].Value);
            boost.Group = text.Contains("모험") ? 0 : text.Contains("교역") ? 1 : text.Contains("전투") || text.Contains("해사") ? 2 : -1;
            // 갈래가 안 적힌 것(「조선 지침 메모」)은 이름에 든 스킬 하나
            if (boost.Group < 0 && Data.Skills.Where(s => s.Name.Length >= 2 && item.Name.Contains(s.Name)).MaxBy(s => s.Name.Length) is { } one) boost.SkillId = one.Id;
        }
        else if (text.Contains("스킬 효과가 연장")) boost.Kind = "Extend";
        else return false;
        Boosts.RemoveAll(b => b.Kind == boost.Kind && b.Group == boost.Group && b.SkillId == boost.SkillId);
        Boosts.Add(boost);
        Say($"{item.Name}을(를) 썼다. " + BoostNote(boost) + ".");
        Cues.Enqueue("Skill");
        return true;
    }

    public string BoostNote(Boost boost)
    {
        string what = boost.Kind switch
        {
            "Speed" => $"선박 속도 +{boost.Amount * 100:0}%",
            "Skill" => (boost.SkillId != 0 ? SkillName(boost.SkillId) : boost.Group switch { 0 => "모험 스킬", 1 => "교역 스킬", 2 => "전투 스킬", _ => "모든 스킬" }) + $" +{boost.Amount:0} (상한 {boost.Cap})",
            _ => "스킬 지속 2배",
        };
        double left = Math.Max(0, boost.Until - Clock);
        return what + " · " + (left >= 86400 ? $"{left / 86400:0.#}일" : left >= 3600 ? $"{left / 3600:0.#}시간" : left >= 60 ? $"{left / 60:0}분" : $"{left:0}초");
    }
}
