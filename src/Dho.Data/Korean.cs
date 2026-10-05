using System.Text;

namespace Dho.Data;

/// <summary>
/// 조사 붙이기 — 글 안의 「을(를)」「이(가)」「은(는)」「와(과)」「(으)로」「(이)라」 꼴을 앞 낱말의 받침에 맞는 것으로 바꾼다.
/// 이름이 끼어드는 문장을 그대로 쓰고 보여 줄 때 한 번 거르면 된다.
/// </summary>
public static class Korean
{
    private static readonly (string Pattern, string WithFinal, string Without)[] Pairs =
    [
        ("을(를)", "을", "를"), ("를(을)", "을", "를"),
        ("이(가)", "이", "가"), ("가(이)", "이", "가"),
        ("은(는)", "은", "는"), ("는(은)", "은", "는"),
        ("과(와)", "과", "와"), ("와(과)", "과", "와"),
        ("(으)로", "으로", "로"), ("(이)라", "이라", "라"), ("(이)", "이", ""),
    ];

    /// <summary>
    /// 그 글자로 끝나는 말의 받침: 0 없음, 8 ㄹ, 그 밖의 값은 다른 받침. 한글이 아니면 숫자는 읽는 소리로, 나머지는 받침 없음으로 본다.
    /// </summary>
    private static int Final(char c)
    {
        if (c is >= '가' and <= '힣') return (c - 0xAC00) % 28;
        return c switch
        {
            '0' or '3' or '6' => 1,      // 영 · 삼 · 육
            '1' or '7' or '8' => 8,      // 일 · 칠 · 팔
            _ => 0,
        };
    }

    public static string Particles(string text)
    {
        if (!text.Contains('(')) return text;
        var result = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; )
        {
            bool replaced = false;
            foreach (var (pattern, withFinal, without) in Pairs)
            {
                if (string.CompareOrdinal(text, i, pattern, 0, pattern.Length) != 0) continue;
                // 앞 낱말의 마지막 글자 — 닫는 괄호 · 따옴표 · 빈칸은 건너뛴다
                int back = result.Length - 1;
                while (back >= 0 && result[back] is '」' or '』' or ')' or '"' or '\'' or ' ' or '♂' or '♀') back--;
                if (back < 0) break;
                int final = Final(result[back]);
                // 「(으)로」는 ㄹ 받침 뒤에서도 「로」다
                bool takesFinal = pattern == "(으)로" ? final != 0 && final != 8 : final != 0;
                result.Append(takesFinal ? withFinal : without);
                i += pattern.Length;
                replaced = true;
                break;
            }
            if (!replaced) result.Append(text[i++]);
        }
        return result.ToString();
    }
}
