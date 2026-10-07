using Dho.Data;

namespace Dho.Game;

/// <summary>
/// 서고 — 학자에게 열람료를 내고 서적을 읽는다. 읽으면 그 학문 스킬(발견물을 감정하는 스킬들)의 숙련도가 오른다.
/// 원본의 글: 화면 글 7407 「「%s」열람」, 907 「열람료」, 7409 「열람」, 35352 「서적열람에서 학문 스킬의 획득숙련도가 … 증가」,
/// 표 50 의 「서고에서 서적 열람 가능 횟수」. 언어 스킬의 설명이 「회화나 서적열람이 가능」, 라틴어 · 켈트어 · 헤브라이어 · 고대 이집트어는 「서적열람이 가능」이다.
/// 클라이언트에 없어 지은 것: 서적의 차림(학문 스킬마다 한 권), 열람료(500 두캇 × 도시 발전과 무관), 하루 다섯 권, 숙련도 40,
/// 읽다가 스무 번에 한 번 침몰선의 조각지도를 찾는 것, 서적 언어가 통하는 문화권(라틴어 = 유럽 1 ~ 8, 켈트어 = 브리튼 섬, 헤브라이어 = 서아시아, 고대 이집트어 = 북아프리카 — 스킬 설명 글에서 맞춘 짐작).
/// 바디 랭귀지로는 책을 못 읽는다.
/// </summary>
internal sealed partial class Voyage
{
    public const int BookFee = 500, BooksPerDay = 5;
    private int _booksDay = -1, _booksRead;

    /// <summary>오늘 이 도시에서 더 읽을 수 있는 권수.</summary>
    public int BooksToday => BooksPerDay * (Data.Settings.ModBooksTimes5 ? 5 : 1);
    public int BooksLeft => (int)Today != _booksDay ? BooksToday : BooksToday - _booksRead;

    /// <summary>서고의 서적들 — 학문 스킬마다 한 권.</summary>
    public List<SkillData> Books() =>
        Data.SkillRules.Where(r => r.Effect == "Appraise").Select(r => Data.Skills.Find(s => s.Id == r.SkillId)).OfType<SkillData>().Distinct().ToList();

    /// <summary>이 도시의 책을 읽을 수 있는 언어(익힌 것 가운데) — 없으면 null. 제 나라 도시에서는 모국어로 읽는다.</summary>
    public string? ReadsWith()
    {
        if (NationId != 0 && City.Nation == NationId) return "모국어";
        var known = Data.SkillRules.Where(r => r.Effect is "Language" or "BookLanguage" && r.Targets.Contains(City.Culture) && Rank(r.SkillId) > 0).ToList();
        return known.Count == 0 ? null : Data.Skills.Find(s => s.Id == known[0].SkillId)?.Name;
    }

    /// <summary>이 도시의 책이 쓰인 언어들 — 못 읽을 때 알려 준다.</summary>
    public string BookLanguages() =>
        string.Join(" · ", Data.SkillRules.Where(r => r.Effect is "Language" or "BookLanguage" && r.Targets.Contains(City.Culture)).Select(r => r.Name));

    public string? ReadBlocker(SkillData book) =>
        Mode != Mode.Port ? "항구에서만 읽는다" : ReadsWith() == null ? $"읽을 줄 아는 언어가 없다 ({BookLanguages()})"
        : Rank(book.Id) <= 0 ? $"{book.Name} 스킬이 없다" : BooksLeft <= 0 ? "오늘은 더 읽을 수 없다" : Money < BookFee ? $"열람료 {BookFee:N0} 두캇이 모자라다" : null;

    /// <summary>연속 열람에 필요한 소지금(원본 글 7408 「※소지금 %d두캇 이상일 때 연속 열람이 가능」 — 액수는 지은 값: 열 권 값).</summary>
    public const int ReadOnMoney = BookFee * 10;

    /// <summary>연속 열람(원본 글 7410) — 그 책을 오늘 남은 횟수만큼(돈이 닿는 데까지) 이어 읽는다.</summary>
    public void ReadOn(SkillData book)
    {
        if (ReadBlocker(book) != null || Money < ReadOnMoney) { Cues.Enqueue("Error"); return; }
        int read = 0;
        while (ReadBlocker(book) == null) { ReadBook(book, quiet: true); read++; }
        Say($"{Fill(Text(7407, "「%s」열람"), book.Name + " 서적")} — {Text(7410, "연속 열람")} {read}권. ({Text(907, "열람료")} {read * BookFee:N0} 두캇)");
    }

    public void ReadBook(SkillData book) => ReadBook(book, false);

    private void ReadBook(SkillData book, bool quiet)
    {
        if (ReadBlocker(book) != null) { Cues.Enqueue("Error"); return; }
        if ((int)Today != _booksDay) (_booksDay, _booksRead) = ((int)Today, 0);
        _booksRead++;
        Money -= BookFee;
        Train(book.Id, 40);
        foreach (var rule in Data.SkillRules.Where(r => r.Effect is "Language" or "BookLanguage" && r.Targets.Contains(City.Culture) && Rank(r.SkillId) > 0).Take(1)) Train(rule.SkillId, 10);
        if (!quiet) Say($"{Fill(Text(7407, "「%s」열람"), book.Name + " 서적")} — {ReadsWith()}(으)로 읽었다. ({Text(907, "열람료")} {BookFee:N0} 두캇, 남은 열람 {BooksLeft}권)");
        if (_random.NextDouble() < 0.05) FindWreckPiece("서적 사이에서");
    }
}
