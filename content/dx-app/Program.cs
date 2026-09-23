namespace MyApp;

internal static class Program
{
    // DPI 인식(PerMonitorV2)은 app.manifest 가 켠다 — 안 그러면 고배율 모니터에서 OS 가
    // 창을 통째로 다시 늘려, 이미 2배로 그린 그림이 또 늘어나 뭉갠다.
    [STAThread]
    private static void Main()
    {
        using var window = new GameWindow();
        window.Run();
    }
}
