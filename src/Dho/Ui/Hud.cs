using Dho.Data;
using Dho.Game;
using Vortice.Mathematics;

namespace Dho.Ui;

/// <summary>화면 위의 글과 창 — 왼쪽 위 상태, 왼쪽 아래 기록, 오른쪽 아래 항구 단추·주변 지도, 가운데 대화 창.</summary>
internal sealed class Hud(Canvas canvas, Voyage voyage)
{
    private static readonly string[] SailNames = ["닻 내림", "돛 1/4", "돛 2/4", "돛 3/4", "돛 전개"];

    public void Draw()
    {
        Status();
        LogPanel();
        if (voyage.Mode == Mode.Port) PortPanel();
        else
        {
            Compass();
            SeaMap();
            Prompt();
        }
        Dialogs();
    }

    // ── 왼쪽 위 ──────────────────────────────────────────────────────────────

    private void Status()
    {
        if (voyage.Mode == Mode.Port)
        {
            canvas.Text($"{voyage.City.Name} 항구", 10, 6, 400, 28, 19, Canvas.White);
            canvas.Text($"{voyage.Money:N0} Ð", 30, 34, 300, 24, 17, Canvas.White);
            canvas.Text(voyage.PlayerName, 44, 60, 300, 24, 17, Canvas.White);
            Bar(44, 90, 1.00f, new Color4(0.90f, 0.35f, 0.40f, 1));
            Bar(44, 110, 0.62f, new Color4(0.45f, 0.80f, 0.35f, 1));
            Bar(44, 130, 0.92f, new Color4(0.80f, 0.40f, 0.85f, 1));
        }
        else
        {
            canvas.Text($"{voyage.Money:N0} Ð", 84, 34, 300, 24, 17, Canvas.White);
            canvas.Fill(4, 66, 150, 82, new Color4(0.03f, 0.05f, 0.20f, 0.75f));
            canvas.Frame(4, 66, 150, 82, new Color4(0.4f, 0.45f, 0.7f, 1), 1);
            canvas.Text(voyage.SeaName, 10, 68, 140, 24, 17, Canvas.White);
            canvas.Text($"항해일수:{voyage.DaysAtSea}일", 10, 94, 140, 24, 16, Canvas.White);
            canvas.Text("☀맑음", 10, 120, 140, 24, 16, Canvas.Gold);
        }
    }

    private void Bar(float x, float y, float fill, Color4 color)
    {
        canvas.Fill(x, y, 122, 6, new Color4(0.1f, 0.1f, 0.15f, 0.8f));
        canvas.Fill(x, y, 122 * fill, 6, color);
    }

    /// <summary>나침반 — 뱃머리 방위와 속도, 바람이 불어 가는 쪽.</summary>
    private void Compass()
    {
        const float cx = 42, cy = 30, r = 26;
        canvas.Circle(cx, cy, r, new Color4(0.05f, 0.07f, 0.2f, 0.85f));
        canvas.Circle(cx, cy, r, Canvas.PanelEdge, false, 2);
        float hx = MathF.Sin((float)voyage.Heading), hy = -MathF.Cos((float)voyage.Heading);
        canvas.Line(cx, cy, cx + hx * r, cy + hy * r, Canvas.Gold, 2.5f);
        float wx = MathF.Sin((float)voyage.WindDirection), wy = -MathF.Cos((float)voyage.WindDirection);
        canvas.Line(cx - wx * r * 0.9f, cy - wy * r * 0.9f, cx + wx * r * 0.9f, cy + wy * r * 0.9f,
                    new Color4(0.6f, 0.85f, 1f, 0.9f), 1.2f);
        canvas.Circle(cx + wx * r * 0.9f, cy + wy * r * 0.9f, 3, new Color4(0.6f, 0.85f, 1f, 1));
        canvas.Text($"{voyage.Knots:0}", cx - r, cy - 13, r * 2, 26, 18, Canvas.White, 1, true);
        canvas.Text(SailNames[voyage.Sail], 160, 8, 200, 24, 15, Canvas.Dim);
    }

    // ── 왼쪽 아래: 기록 ──────────────────────────────────────────────────────

    private void LogPanel()
    {
        const float w = 620, h = 150;
        float x = 4, y = canvas.Height - h - 4;
        canvas.Fill(x, y - 26, 104, 26, Canvas.PanelFill);
        canvas.Frame(x, y - 26, 104, 26, Canvas.PanelEdge, 1);
        canvas.Text("전체", x + 44, y - 25, 60, 24, 16, Canvas.White, 0, true);
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y - 26, w, h + 26);

        const int lines = 5;
        int first = Math.Max(0, voyage.Log.Count - lines);
        for (int i = first; i < voyage.Log.Count; i++)
            canvas.Text(voyage.Log[i], x + 10, y + 8 + (i - first) * 24, w - 20, 24, 16, Canvas.White);

        canvas.Line(x, y + h - 26, x + w, y + h - 26, Canvas.PanelEdge, 1);
        string keys = voyage.Mode == Mode.Port
            ? "마우스 오른쪽 끌기: 시점   휠: 거리"
            : "W/S: 돛   A/D: 키   바다 클릭: 그쪽으로   F: 입항·상륙";
        canvas.Text(keys, x + 10, y + h - 24, w - 20, 22, 13, Canvas.Dim);
    }

    // ── 오른쪽 아래 ──────────────────────────────────────────────────────────

    private void PortPanel()
    {
        const float w = 210, h = 196;
        float x = canvas.Width - w - 6, y = canvas.Height - h - 6;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text("항구", x + 10, y + 6, 100, 24, 17, Canvas.White);
        if (voyage.Dialog != Dialog.None) return;

        if (canvas.Button("출항", x + 12, y + 38, w - 24, 34)) voyage.Depart();
        if (canvas.Button("모험가 조합", x + 12, y + 78, w - 24, 34)) voyage.Dialog = Dialog.Guild;
        if (canvas.Button("의뢰 내용", x + 12, y + 118, w - 24, 34, voyage.Quest != null))
            voyage.Dialog = Dialog.QuestDetail;
        canvas.Text($"모험 명성 {voyage.AdventureFame}   경험 {voyage.AdventureExp}", x + 12, y + 162, w - 24, 22, 13, Canvas.Dim);
    }

    /// <summary>주변 지도 — 통행 판정 지도를 그대로 찍는다.</summary>
    private void SeaMap()
    {
        const float size = 170;
        const int cells = 34;
        const double reach = 136;                     // 세계 좌표 반지름
        float x = canvas.Width - size - 8, y = canvas.Height - size - 8;
        canvas.Fill(x, y, size, size, new Color4(0.06f, 0.14f, 0.36f, 0.9f));

        float cell = size / cells;
        double step = reach * 2 / cells;
        for (int j = 0; j < cells; j++)
        for (int i = 0; i < cells; i++)
        {
            double wx = voyage.ShipX - reach + (i + 0.5) * step, wy = voyage.ShipY - reach + (j + 0.5) * step;
            if (voyage.Map.IsLand(wx, wy))
                canvas.Fill(x + i * cell, y + j * cell, cell + 0.5f, cell + 0.5f, new Color4(0.55f, 0.60f, 0.40f, 1));
        }

        void Mark(double wx, double wy, Color4 color, float radius)
        {
            double dx = WorldMap.DeltaX(voyage.ShipX, wx), dy = wy - voyage.ShipY;
            if (Math.Abs(dx) > reach || Math.Abs(dy) > reach) return;
            canvas.Circle(x + size / 2 + (float)(dx / reach) * size / 2, y + size / 2 + (float)(dy / reach) * size / 2, radius, color);
        }

        foreach (var city in voyage.Data.Cities) Mark(city.SeaX, city.SeaY, new Color4(1f, 0.35f, 0.3f, 1), 3);
        if (voyage.QuestStage == QuestStage.Accepted && voyage.QuestLanding is { X: not 0 } site)
            Mark(site.X, site.Y, Canvas.Gold, 4);

        float hx = MathF.Sin((float)voyage.Heading), hy = -MathF.Cos((float)voyage.Heading);
        canvas.Line(x + size / 2, y + size / 2, x + size / 2 + hx * 12, y + size / 2 + hy * 12, Canvas.White, 2);
        canvas.Circle(x + size / 2, y + size / 2, 3, Canvas.White);
        canvas.Frame(x, y, size, size, Canvas.PanelEdge);
        canvas.Text($"{voyage.ShipX:0}, {voyage.ShipY:0}", x, y - 22, size, 20, 13, Canvas.Dim, 2);
        canvas.Block(x, y, size, size);
    }

    /// <summary>지금 할 수 있는 일 — 입항, 상륙.</summary>
    private void Prompt()
    {
        if (voyage.Dialog != Dialog.None) return;
        string? text = null;
        if (voyage.SiteInReach()) text = $"F : {voyage.QuestLanding!.Name}에 상륙한다";
        else if (voyage.PortInReach() is { } port) text = $"F : {port.Name}에 입항한다";
        if (text == null) return;

        float w = 360, x = (canvas.Width - w) / 2, y = canvas.Height * 0.72f;
        canvas.Panel(x, y, w, 40);
        canvas.Text(text, x, y + 8, w, 26, 17, Canvas.Gold, 1);
    }

    // ── 가운데 창 ────────────────────────────────────────────────────────────

    private void Dialogs()
    {
        if (voyage.Dialog == Dialog.None) return;
        const float w = 560, h = 340;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);

        void Title(string text) => canvas.Text(text, x + 20, y + 14, w - 40, 30, 20, Canvas.Gold, 0, true);
        void Body(string text, float top = 56, float height = 200) =>
            canvas.Text(text, x + 20, y + top, w - 40, height, 16, Canvas.White);
        bool Close(string label = "닫기") => canvas.Button(label, x + w - 140, y + h - 50, 120, 34);

        switch (voyage.Dialog)
        {
            case Dialog.Guild:
                Guild(x, y, w, h, Title, Body);
                if (voyage.Dialog == Dialog.Guild && Close())
                {
                    voyage.Offered = null;
                    voyage.Dialog = Dialog.None;
                }
                break;

            case Dialog.QuestDetail when voyage.Quest is { } quest:
                Title($"의뢰 내용 — {quest.Title}");
                Body($"의뢰인: {quest.Client}\n\n{quest.Request}\n\n목표: {quest.Hint}\n" +
                     $"보고: {voyage.CityName(quest.CityId)}   보수: {quest.Reward:N0} 두캇\n\n" +
                     (voyage.QuestStage == QuestStage.Discovered ? "※ 발견을 마쳤다. 조합에 보고하자." : "※ 아직 발견하지 못했다."));
                if (Close()) voyage.Dialog = Dialog.None;
                break;

            case Dialog.Landing when voyage.Quest is { } quest:
                Title(voyage.QuestLanding?.Name ?? "상륙");
                Body(quest.LandingText + "\n\n어떻게 할까?");
                if (canvas.Button("주변을 탐색한다", x + 20, y + h - 50, 180, 34)) voyage.Search();
                if (voyage.Dialog == Dialog.Landing && Close("배로 돌아간다")) voyage.Dialog = Dialog.None;
                break;

            case Dialog.Discovery when voyage.QuestDiscovery is { } found:
                Title($"발견!  {found.Name}");
                canvas.Text($"{voyage.DiscoveryKind(found.Kind)}   {new string('★', found.Stars)}   모험 경험 +{found.Exp}   명성 +{found.Fame}",
                            x + 20, y + 50, w - 40, 24, 15, Canvas.Gold);
                Body(found.Description, 84, 190);
                if (Close("확인")) voyage.Dialog = Dialog.None;
                break;

            case Dialog.Report when voyage.Reported is { } reported:
                Title("의뢰 달성");
                Body($"조합 마스터\n{voyage.ReportedDiscovery?.Name ?? "그것"}이라… 학자들이 기뻐하겠군. 수고했네.\n\n" +
                     $"「{reported.Title}」 보수 {reported.Reward:N0} 두캇을 받았다.\n소지금 {voyage.Money:N0} 두캇");
                if (Close("확인")) voyage.Dialog = Dialog.None;
                break;

            default:
                voyage.Dialog = Dialog.None;
                break;
        }
    }

    private void Guild(float x, float y, float w, float h, Action<string> title, Action<string, float, float> body)
    {
        title("모험가 조합");
        if (voyage.CanReportHere)
        {
            body("조합 마스터\n오, 무언가 찾아낸 얼굴이군. 이야기를 들려주게.", 56, 200);
            if (canvas.Button("발견을 보고한다", x + 20, y + h - 50, 180, 34)) voyage.Report();
            return;
        }
        if (voyage.Quest is { } taken)
        {
            body($"조합 마스터\n「{taken.Title}」은(는) 아직인가. {taken.Hint}", 56, 200);
            return;
        }
        if (voyage.Offered is { } offered)
        {
            body($"조합 마스터\n{offered.Request}", 56, 140);
            canvas.Text($"의뢰 「{offered.Title}」   선금 {offered.Advance:N0}   보수 {offered.Reward:N0}",
                        x + 20, y + 200, w - 40, 24, 15, Canvas.Gold);
            if (canvas.Button("의뢰를 받는다", x + 20, y + h - 50, 160, 34)) voyage.AcceptQuest();
            else if (canvas.Button("다른 의뢰", x + 190, y + h - 50, 120, 34)) voyage.Offered = null;
            return;
        }

        var quests = voyage.QuestsHere().Take(5).ToList();
        if (quests.Count == 0)
        {
            body("조합 마스터\n지금은 소개해 줄 의뢰가 없네. 다른 도시의 조합도 둘러보게.", 56, 200);
            return;
        }
        body("조합 마스터\n이런 의뢰가 들어와 있네. 어느 것을 맡겠나?", 56, 60);
        for (int i = 0; i < quests.Count; i++)
            if (canvas.Button($"{quests[i].Title}   (보수 {quests[i].Reward:N0})", x + 20, y + 120 + i * 38, w - 40, 32))
                voyage.Offered = quests[i];
    }
}
