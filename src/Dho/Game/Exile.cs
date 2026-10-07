using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 망명 — 다른 나라의 본거지에서 그 나라로 국적을 옮긴다.
/// 원본의 확인 글(화면 글 22062)이 말하는 것: 국가통상면허가 취소되고, 지금까지 얻은 공로치가 반으로 줄고,
/// 왕립 함대에서 제대하고, 진행 중인 임무 일부가 되돌아간다.
/// 여기서는 그 가운데 이 게임에 있는 것만 옮겼다 — 공적 반감, 받들던 칙명 취소, 내건 호칭은 그대로.
/// 어디서 누구에게 청하는지 · 무엇이 드는지는 클라이언트에 없다: 「받는 나라의 본거지에서, 돈을 내고」는 지은 것이다.
/// 값(100만 + 작위마다 50만 두캇)도, 한 번 옮기면 30일 동안 다시 못 옮기는 것도 지은 값이다.
/// </summary>
internal sealed partial class Voyage
{
    /// <summary>마지막으로 망명한 날 — 없으면 −1.</summary>
    public int ExileDay { get; private set; } = -1;

    private const int ExileWait = 30;

    /// <summary>여기서 망명할 수 있는 나라 — 다른 나라의 본거지에 있을 때 그 나라.</summary>
    public NamedData? ExileTo =>
        Mode == Mode.Port && City.Kind == 0 && City.Nation != NationId && City.Nation != 0
        && Data.Start.Nations.Any(n => n.NationId == City.Nation) ? Data.Nations.Find(n => n.Id == City.Nation) : null;

    public int ExileCost => 1_000_000 + Title * 500_000;

    /// <summary>망명하면 남는 공적 — 반(원본의 글).</summary>
    public int MeritAfterExile => Merit / 2;

    /// <summary>지금 망명하지 못하는 까닭 — 할 수 있으면 null.</summary>
    public string? ExileBlocker() =>
        ExileTo == null ? "다른 나라의 본거지에서만 망명할 수 있다"
        : ExileDay >= 0 && (int)Today - ExileDay < ExileWait ? $"망명한 지 얼마 되지 않았다 — {ExileWait - ((int)Today - ExileDay)}일 뒤에"
        : Money < ExileCost ? $"{ExileCost:N0} 두캇이 든다"
        : null;

    public void Exile()
    {
        if (ExileBlocker() != null || ExileTo is not { } nation) return;
        string from = NationName == "" ? "무소속" : NationName;
        Money -= ExileCost;
        NationId = nation.Id;
        Merit = MeritAfterExile;
        if (Order != null)
        {
            Say($"받들던 칙명 「{Order.Title}」은(는) 없던 일이 되었다.");
            (Order, OrderProgress) = (null, 0);
        }
        ExileDay = (int)Today;
        Cues.Enqueue("Done");
        Say($"{from}에서 {nation.Name}(으)로 망명했다. 공적은 {Merit}."); 
        Dialog = Dialog.None;
    }
}
