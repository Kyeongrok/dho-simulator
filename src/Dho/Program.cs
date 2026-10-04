namespace Dho;

internal static class Program
{
    // DPI 인식(PerMonitorV2)은 app.manifest 가 켠다.
    [STAThread]
    private static void Main(string[] args)
    {
        // --script "depart;wait:3;shot:a.png;quit" — 사람 손 없이 돌려 보고 화면을 찍는다(확인용).
        string? script = null;
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == "--script") script = args[i + 1];

        // --new : 이어 하기를 지우고 캐릭터 만들기부터
        using var window = new GameWindow(script, args.Contains("--new"));
        window.Run();
    }
}
