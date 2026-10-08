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
        canvas.PanelLabel = WindowLabel;
        if (!voyage.Created)
        {
            Creation();
            Display();
            return;
        }
        // 사람 이름표는 맨 먼저 — 그 뒤에 그리는 창(설정 · 효과음 고르기 · 기록)이 모두 그 위를 덮는다
        if (voyage.Mode == Mode.Port && voyage.TownView) TownLabels();
        if (voyage.Mode == Mode.Sea) SeaLabels();
        DisasterEffects();                        // 재해 · 날씨의 모습도 창들 밑에
        HelmMarks();
        AideSpeechBox();
        Status();
        LogPanel();
        Display();
        if (voyage.Mode == Mode.Port && voyage.TownView)
        {
            // 도시 메뉴는 T 로 여닫는다. 닫혀 있을 때는 지도만 둔다
            if (TownMenuOpen && voyage.Dialog != Dialog.None) TownMenuOpen = false;      // 다른 창이 뜨면 도시 메뉴는 닫는다
            if (TownMenuOpen) TownPanel();
            else
            {
                TownMapPanel();
                canvas.Text($"{KeyName(KeyOf(voyage.Data.Settings.Keys, "TownMenu"))} 도시 메뉴", canvas.Width - 170, canvas.Height - 26, 160, 20, 13, Canvas.Dim, 2);
            }
        }
        else if (voyage.Mode == Mode.Port) PortPanel();
        else
        {
            SeaMap();
            QuickBar();
            ActiveSkills();
            Prompt();
            BattleBar();
        }
        if (voyage.Mode == Mode.Port) QuickBar();
        // 걸려 있는 부스트 — 위쪽 가운데에 한 줄씩(이름 · 효과 · 남은 시간)
        for (int i = 0; i < voyage.Boosts.Count; i++)
        {
            string line = $"▲ {voyage.Boosts[i].Name} — {voyage.BoostNote(voyage.Boosts[i])}";
            float bw = line.Length * 11.5f + 16, bx = (canvas.Width - bw) / 2;
            canvas.Fill(bx, 4 + i * 22, bw, 20, new Color4(0.03f, 0.10f, 0.20f, 0.75f));
            canvas.Text(line, bx, 5 + i * 22, bw, 18, 12, new Color4(0.6f, 1f, 0.85f, 1), 1);
        }
        canvas.PanelId = null;
        canvas.LastPanel = default;
        Dialogs();
        _dialogRect = voyage.Dialog == Dialog.None ? default : canvas.LastPanel;      // 기록 칸을 굴릴 때 — 창이 덮은 자리가 아니면 굴린다
        canvas.PanelId = null;
        if (voyage.Dialog is not (Dialog.Guild or Dialog.TradeGuild or Dialog.SeaGuild or Dialog.Broker)) _viaBroker = false;      // 중개인을 거쳐 연 창이 닫혔다
        DiscoveryCard();
        SkillUpEffect();                          // 스킬 랭크 업의 모습은 창들 위에(레시피 창에서 생산하다 올라도 보이게)
        // 레벨업 알림 — 화면 위쪽 가운데에 잠깐
        if (voyage.LevelNotice.Count != _levelSeen) (_levelSeen, _levelShown) = (voyage.LevelNotice.Count, Environment.TickCount64);
        if (voyage.LevelNotice.Text is { Length: > 0 } levelUp && Environment.TickCount64 - _levelShown < 4000)
        {
            float nw = 360, nx = (canvas.Width - nw) / 2, ny = canvas.Height * 0.22f;
            canvas.Fill(nx, ny, nw, 64, new Color4(0.03f, 0.06f, 0.20f, 0.85f));
            canvas.Frame(nx, ny, nw, 64, Canvas.Gold, 2);
            canvas.Text(levelUp.StartsWith("작위") ? "작위 수여" : levelUp.StartsWith("입항 허가") ? "허가" : "LEVEL UP", nx, ny + 4, nw, 24, 16, Canvas.Gold, 1, true);
            canvas.Text(levelUp, nx, ny + 28, nw, 30, 22, Canvas.White, 1, true);
        }
        TopBar();
        Tip();
    }

    private int _topMenu = -1;
    /// <summary>대본용: 오른쪽 위 단추의 차림을 연다(0 캐릭터, 1 선박, 2 시스템).</summary>
    public void OpenTopMenu(int which) => _topMenu = which;

    /// <summary>
    /// 오른쪽 위 단추 줄 — 원본의 「캐릭터」 · 「선박」(명령 단추 묶음의 7 · 10 부터 세 장씩, 50 × 24) · 「시스템」(19 부터, 40 × 24). 누르면 원본처럼 차림이 내려온다.
    /// 아직 없는 것(테크닉 · 부관 선박 정보 · 함대에 참가 · 게임 등급 · 캐릭터 변경)은 꺼 두었고, 부관정보와 부관 담당 변경은 둘 다 부관 창으로 간다. 로그아웃은 끝내기다.
    /// </summary>
    private void TopBar()
    {
        const float y = 4, mw = 200, line = 28;
        float scale = 1.1f * IconScale, h = 24 * scale;      // 항구 단추와 같은 크기
        (int Icon, float Width, string Label, (string Label, bool Enabled, Action Run)[] Menu)[] buttons =
        [
            (7, 50, "캐릭터",
            [
                ("캐릭터정보", true, () => voyage.Dialog = Dialog.Character),
                ("스킬", true, () => voyage.Dialog = Dialog.Skills),
                ("테크닉", false, () => { }),
                ("장비물품", true, () => (voyage.Dialog, _equipTop) = (Dialog.Equip, 0)),
                ("소유물품 목록", true, () => voyage.Dialog = Dialog.Items),
                ("부관정보", true, () => voyage.Dialog = Dialog.Aides),
                ("부관 담당 변경", true, () => voyage.Dialog = Dialog.Aides),
            ]),
            (10, 50, "선박",
            [
                ("선박정보", true, () => voyage.Dialog = Dialog.ShipInfo),
                ("선박부품", true, () => voyage.Dialog = Dialog.Fitting),
                ("적재화물", true, () => voyage.Dialog = Dialog.Cargo),
                ("부관 선박 정보", false, () => { }),
                ("대원 모집", voyage.Mode == Mode.Port && voyage.HasTavern, () => voyage.Dialog = Dialog.Recruit),
                ("함대에 참가", false, () => { }),
                ("내비게이션", true, () => voyage.Dialog = Dialog.Nav),
                (voyage.DelegateTo == null ? voyage.Text(25343, "위임 항해") : voyage.Text(5830, "위임 항해 취소"), voyage.Mode == Mode.Sea,
                 () => { if (voyage.DelegateTo == null) (voyage.Dialog, _delegateTop) = (Dialog.Delegate, 0); else voyage.CancelDelegate(); }),
            ]),
            (19, 40, "시스템",
            [
                ("환경설정", true, () => { voyage.Dialog = Dialog.None; _displayOpen = true; }),
                ("퀵슬롯 등록", true, () => voyage.Dialog = Dialog.QuickSetup),
                ("단축키 등록", true, () => _keysOpen = true),
                ("게임 등급", false, () => { }),
                ("캐릭터 변경", false, () => { }),
                ("로그아웃", true, () => Quit?.Invoke()),
            ]),
        ];
        bool overAny = false;
        float x = canvas.Width - 4;
        foreach (var button in buttons) x -= button.Width * scale + 4;
        for (int k = 0; k < buttons.Length; x += buttons[k].Width * scale + 4, k++)
        {
            float w = buttons[k].Width * scale;
            bool over = canvas.Hover(x, y, w, h);
            overAny |= over;
            if (Command(buttons[k].Icon, x, y, true, buttons[k].Label, scale, buttons[k].Width)) _topMenu = _topMenu == k ? -1 : k;
            if (over) { canvas.Pointer.Consumed = true; _tip = _topMenu == k ? null : (buttons[k].Label, x + w / 2, y + h + 34); }
            if (_topMenu != k) continue;

            var menu = buttons[k].Menu;
            float mx = Math.Min(x, canvas.Width - mw - 8), my = y + h + 4, mh = menu.Length * line + 12;
            canvas.Fill(mx, my, mw, mh, new Color4(0.06f, 0.09f, 0.26f, 0.96f));
            canvas.Frame(mx, my, mw, mh, new Color4(0.35f, 0.45f, 0.75f, 1), 1.2f);
            for (int i = 0; i < menu.Length; i++)
            {
                float row = my + 6 + i * line;
                bool hover = menu[i].Enabled && canvas.Hover(mx + 6, row, mw - 12, line);
                if (hover) canvas.Fill(mx + 6, row, mw - 12, line - 2, new Color4(0.12f, 0.55f, 0.52f, 0.95f));
                canvas.Text(menu[i].Label, mx + 14, row + 2, mw - 24, 24, 17, menu[i].Enabled ? Canvas.White : Canvas.Dim);
                if (!hover || !canvas.Pointer.Clicked) continue;
                (canvas.Pointer.Consumed, canvas.Pointer.Clicked, canvas.Pressed, _topMenu) = (true, false, true, -1);
                menu[i].Run();
                return;
            }
            overAny |= canvas.Hover(mx, my, mw, mh);
            canvas.Block(mx, my, mw, mh);
        }
        // 차림 밖을 누르면 닫힌다
        if (canvas.Pointer.Clicked && !overAny) _topMenu = -1;
    }

    // ── 화면(해상도) ─────────────────────────────────────────────────────────

    /// <summary>해상도를 바꾸는 일 — 창이 넣어 준다(너비, 높이, 전체 화면).</summary>
    public Action<int, int, bool>? SetDisplay;
    private bool _displayOpen;
    private static readonly (int Width, int Height)[] Sizes = [(1024, 768), (1280, 720), (1280, 800), (1366, 768), (1600, 900), (1920, 1080), (2560, 1440), (3200, 1800), (3840, 2160)];

    public const int TitleHeight = 30, TitleMenuWidth = 44, TitleButtonsWidth = 132;
    public Action? Minimize, Quit;
    private bool _menuOpen;
    /// <summary>모니터 크기 — 이보다 큰 해상도는 고를 수 없다.</summary>
    public (int Width, int Height) Screen = (int.MaxValue, int.MaxValue);
    /// <summary>지금 그리는 크기(픽셀).</summary>
    public (int Width, int Height) Pixels;
    /// <summary>대본용: 햄버거 차림(0) 또는 설정 창(1)을 연다.</summary>
    public void OpenMenu(int which) => (_menuOpen, _displayOpen, _keysOpen, _devOpen, _modOpen) = (which == 0, which == 1, which == 2, which == 3, which == 4);

    private bool _devOpen, _modOpen, _bentoOpen;

    // 창의 보조 아이디(번호) — 가운데 창은 창 갈래(Dialog)의 차례, 그 밖의 창은 201 부터. 번호가 바뀌지 않게 늘 끝에만 더한다
    private static readonly string[] ExtraWindows = ["WndItemsRecipe", "WndItemsShop", "WndItemsAdd", "WndItemsShip", "WndPort", "WndMod", "WndWarp", "WndDev", "WndKeys", "WndSound", "WndDisplay", "WndLog", "WndBattleBar", "WndShipCompare"];

    public static int WindowNumber(string id)
    {
        int extra = Array.IndexOf(ExtraWindows, id);
        if (extra >= 0) return 201 + extra;
        return Enum.TryParse<Dialog>(id.StartsWith("Wnd") ? id[3..] : id, out var dialog) ? (int)dialog : 0;
    }

    private string? WindowLabel(string id) => voyage.Data.Settings.ModWindowIds switch { 1 => id, 2 => $"Wnd{WindowNumber(id):000}", _ => null };

    /// <summary>개발도구(Dho.Tools.exe)를 띄운다 — 게임 실행 파일 곁이나, 저장소의 src\Dho.Tools\bin 아래에서 찾는다.</summary>
    private void OpenTools()
    {
        string? found = null;
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder != null && found == null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "Dho.Tools.exe"))) found = Path.Combine(folder.FullName, "Dho.Tools.exe");
            else if (Directory.Exists(Path.Combine(folder.FullName, "Dho.Tools", "bin")))
                found = Directory.EnumerateFiles(Path.Combine(folder.FullName, "Dho.Tools", "bin"), "Dho.Tools.exe", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        }
        if (found == null) { voyage.Say("개발도구를 찾지 못했다 — 먼저 빌드한다: dotnet build src\\Dho.Tools"); return; }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(found) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(found)! });
            voyage.Say("개발도구를 열었다. 고친 자료는 게임을 다시 켜면 듣는다.");
        }
        catch (Exception e) { voyage.Say($"개발도구를 열지 못했다: {e.Message}"); }
    }

    /// <summary>모드 — 원본과 다르게 굴리는 것들을 켜고 끈다. 설정에 남는다.</summary>
    private void ModWindow()
    {
        canvas.PanelId = "WndMod";
        const float w = 520, h = 630;
        float x = (canvas.Width - w) / 2, y = 34;
        var settings = voyage.Data.Settings;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text("모드", x + 16, y + 10, w - 32, 26, 18, Canvas.Gold, 0, true);
        canvas.Text("원본과 다르게 굴리는 것들. 켜 두면 계속 남는다.", x + 80, y + 14, w - 96, 20, 12, Canvas.Dim);
        // 켜고 끄는 줄 하나 — 네모 칸과 글
        bool Toggle(string label, string note, float ty, bool on)
        {
            bool over = canvas.Hover(x + 16, ty, w - 32, 44);
            if (over) canvas.Fill(x + 16, ty, w - 32, 44, new Color4(0.2f, 0.3f, 0.6f, 0.5f));
            canvas.Fill(x + 22, ty + 11, 22, 22, new Color4(0.02f, 0.04f, 0.14f, 0.95f));
            canvas.Frame(x + 22, ty + 11, 22, 22, Canvas.PanelEdge, 1.2f);
            if (on) canvas.Text("✔", x + 22, ty + 9, 22, 22, 16, new Color4(0.5f, 1f, 0.6f, 1), 1, true);
            canvas.Text(label, x + 54, ty + 2, w - 80, 22, 15, Canvas.White);
            canvas.Text(note, x + 54, ty + 24, w - 70, 18, note.Length > 44 ? 10 : 12, Canvas.Dim);      // 긴 글은 작게 — 두 줄로 넘어가 아랫줄과 겹치지 않게
            return over && canvas.Pointer.Clicked;
        }
        if (Toggle("타고 있는 배도 커스텀설정 조선", "원본은 탑승 중인 배를 건드릴 수 없다 — 켜면 타고 있는 배도 강화 · 성능초기화를 한다.", y + 46, settings.ModWorkOnBoard))
        {
            settings.ModWorkOnBoard = !settings.ModWorkOnBoard;
            voyage.Data.SaveSettings();
        }
        // 선박 조합의 성공률 — 0 ~ 50 을 정하면 셈한 성공률이 그만큼 높아진다
        settings.ModCombineBonus = Math.Clamp(settings.ModCombineBonus, 0, 50);
        canvas.Text($"선박 조합 성공률  +{settings.ModCombineBonus}%", x + 54, y + 100, 220, 22, 15, Canvas.White);
        canvas.Text("셈한 성공률에 이만큼 더한다(0 ~ 50). 0 이면 그대로.", x + 54, y + 122, 250, 18, 10, Canvas.Dim);
        (string Label, int Step)[] steps = [("−10", -10), ("−1", -1), ("+1", 1), ("+10", 10)];
        for (int i = 0; i < steps.Length; i++)
        {
            int next = Math.Clamp(settings.ModCombineBonus + steps[i].Step, 0, 50);
            if (canvas.Button(steps[i].Label, x + w - 216 + i * 50, y + 102, 46, 28, next != settings.ModCombineBonus, 13)) { settings.ModCombineBonus = next; voyage.Data.SaveSettings(); }
        }
        if (Toggle("타고 있는 배도 선박 조합", "켜면 타고 있는 배를 강화 선박으로 고를 수 있다. 재료(제물)로는 못 쓴다.", y + 198, settings.ModCombineOnBoard))
        {
            settings.ModCombineOnBoard = !settings.ModCombineOnBoard;
            voyage.Data.SaveSettings();
        }
        // 경험치 · 숙련도 배율 — 1 · 2 · 3배 가운데 하나
        canvas.Text($"경험치 · 숙련도  {settings.Gain}배", x + 54, y + 154, 220, 22, 15, Canvas.White);
        canvas.Text("경험치와 스킬 · 조타 숙련도. 명성은 그대로.", x + 54, y + 176, 250, 18, 10, Canvas.Dim);
        for (int i = 1; i <= 3; i++)
        {
            float bx = x + w - 216 + (i - 1) * 67;
            if (canvas.Button($"{i}배", bx, y + 156, 62, 28, settings.Gain != i, 13)) { (settings.ModGain, settings.ModTripleGain) = (i, i == 3); voyage.Data.SaveSettings(); }
            if (settings.Gain == i) canvas.Frame(bx, y + 156, 62, 28, Canvas.Gold, 1.6f);
        }
        if (Toggle("해적이 덤비지 않는다", "켜면 해적선이 쫓아오지 않는다. 이쪽에서 거는 싸움(G)은 그대로 된다.", y + 244, settings.ModNoPirates))
        {
            settings.ModNoPirates = !settings.ModNoPirates;
            voyage.Data.SaveSettings();
        }
        if (Toggle("입항 허가 없이 다닌다", "끄면 먼 바다(중남미 · 동남아시아 · 동아시아)의 항구는 허가를 얻어야 들어간다(원본의 규칙).", y + 336, settings.ModNoPermits))
        {
            settings.ModNoPermits = !settings.ModNoPermits;
            voyage.Data.SaveSettings();
        }
        if (Toggle("관세 없이 산다", "끄면 사고팔 때 관세가 붙는다(남의 나라 10%, 제 나라 5% − 작위 — 임시 값).", y + 382, settings.ModNoTax))
        {
            settings.ModNoTax = !settings.ModNoTax;
            voyage.Data.SaveSettings();
        }
        if (Toggle("어디서나 말이 통한다", "끄면 그 도시의 언어나 바디 랭귀지가 없는 곳에서는 흥정을 못 한다.", y + 428, settings.ModAllLanguages))
        {
            settings.ModAllLanguages = !settings.ModAllLanguages;
            voyage.Data.SaveSettings();
        }
        if (Toggle("서적 열람 횟수 5배", "서고에서 하루에 읽는 권수가 5권에서 25권이 된다.", y + 474, settings.ModBooksTimes5))
        {
            settings.ModBooksTimes5 = !settings.ModBooksTimes5;
            voyage.Data.SaveSettings();
        }
        // 창 ID — 셋 가운데 하나를 고른다: 없음 · 아이디(WndShipSwap) · 보조 아이디(Wnd012)
        canvas.Text("창 ID", x + 54, y + 292, 80, 22, 15, Canvas.White);
        canvas.Text("창의 오른쪽 위 구석에 작게 보인다 — 고칠 창을 가리켜 말할 때 쓴다.", x + 54, y + 314, w - 70, 18, 12, Canvas.Dim);
        string[] idModes = ["없음", "아이디", "보조 아이디"];
        float radioX = x + 130;
        for (int i = 0; i < idModes.Length; i++)
        {
            float radioW = 46 + idModes[i].Length * 14;
            bool on = Math.Clamp(settings.ModWindowIds, 0, 2) == i, over = canvas.Hover(radioX, y + 290, radioW, 24);
            canvas.Circle(radioX + 10, y + 302, 8, new Color4(0.02f, 0.04f, 0.14f, 0.95f));
            canvas.Circle(radioX + 10, y + 302, 8, over ? Canvas.Gold : Canvas.PanelEdge, false, 1.4f);
            if (on) canvas.Circle(radioX + 10, y + 302, 4.5f, new Color4(0.5f, 1f, 0.6f, 1));
            canvas.Text(idModes[i], radioX + 24, y + 292, radioW - 24, 22, 14, on ? Canvas.White : Canvas.Dim);
            if (over && canvas.Pointer.Clicked && !on) { settings.ModWindowIds = i; canvas.Pressed = true; voyage.Data.SaveSettings(); }
            radioX += radioW;
        }
        // 초과 강화의 성공률 — 0 ~ 50 을 정하면 셈한 성공률이 그만큼 높아진다
        settings.ModOverWorkBonus = Math.Clamp(settings.ModOverWorkBonus, 0, 50);
        canvas.Text($"초과 강화 성공률  +{settings.ModOverWorkBonus}%", x + 54, y + 522, 220, 22, 15, Canvas.White);
        canvas.Text("강화 횟수를 다 쓴 뒤의 강화. 0 이면 그대로.", x + 54, y + 544, 250, 18, 10, Canvas.Dim);
        (string Label, int Step)[] overSteps = [("−10", -10), ("−1", -1), ("+1", 1), ("+10", 10)];
        for (int i = 0; i < overSteps.Length; i++)
        {
            int next = Math.Clamp(settings.ModOverWorkBonus + overSteps[i].Step, 0, 50);
            if (canvas.Button(overSteps[i].Label, x + w - 216 + i * 50, y + 524, 46, 28, next != settings.ModOverWorkBonus, 13)) { settings.ModOverWorkBonus = next; voyage.Data.SaveSettings(); }
        }
        if (canvas.Button("닫기", x + w - 116, y + h - 40, 100, 30)) _modOpen = false;
    }

    /// <summary>대본용: 모드 창을 연다.</summary>
    public void OpenMod() => _modOpen = true;

    private bool _warpOpen, _warpSearching;
    private string _warpSearch = "";
    private bool WarpTyping => _warpOpen && _warpSearching;
    public void WarpSearchForTest(string word) => _warpSearch = word;
    private int _warpPage;
    /// <summary>대본용: 해역 워프 창을 연다.</summary>
    public void OpenWarp(bool cities = false) => (_devOpen, _warpOpen, _warpCities, _warpPage) = (true, true, cities, 0);

    private bool _warpCities;

    /// <summary>
    /// 워프(개발 메뉴) — 「해역」: 이름을 누르면 그 바다 한가운데로, 「도시」: 그 도시의 항구로 옮겨 간다. 지금 있는 곳에는 ● 가 붙는다.
    /// </summary>
    private void WarpWindow()
    {
        canvas.PanelId = "WndWarp";
        const int columns = 4, lines = 12;
        const float w = 760, h = 96 + lines * 30 + 50;
        float x = (canvas.Width - w) / 2, y = Math.Max(34, (canvas.Height - h) / 2);
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text("워프", x + 16, y + 10, 60, 26, 18, Canvas.Gold, 0, true);
        string[] tabs = ["해역", "도시"];
        for (int k = 0; k < tabs.Length; k++)
        {
            bool on = _warpCities == (k == 1);
            canvas.Fill(x + 76 + k * 74, y + 10, 70, 26, on ? new Color4(0.16f, 0.62f, 0.72f, 0.98f) : new Color4(0.06f, 0.10f, 0.28f, 0.9f));
            canvas.Text(tabs[k], x + 76 + k * 74, y + 12, 70, 22, 14, Canvas.White, 1, on);
            if (canvas.Hover(x + 76 + k * 74, y + 10, 70, 26) && canvas.Pointer.Clicked) (_warpCities, _warpPage, canvas.Pressed) = (k == 1, 0, true);
        }
        canvas.Text(voyage.Mode == Mode.Port ? $"지금: {voyage.City.Name} 항구" : $"지금: {voyage.SeaName}   {voyage.WindName}노트" + (voyage.CurrentName == "" ? "" : $" · {voyage.CurrentName}노트"), x + 450, y + 14, w - 466, 20, 12, Canvas.White);
        // 찾기 칸 — 누르고 이름의 일부를 친다
        bool overWarpSearch = canvas.Hover(x + 236, y + 10, 200, 26);
        if (canvas.Pointer.Clicked) _warpSearching = overWarpSearch;
        canvas.Fill(x + 236, y + 10, 200, 26, new Color4(0.02f, 0.04f, 0.14f, 0.95f));
        canvas.Frame(x + 236, y + 10, 200, 26, _warpSearching ? Canvas.Gold : Canvas.PanelEdge, 1.2f);
        canvas.Text(_warpSearch == "" && !_warpSearching ? "🔍 찾기" : _warpSearch + (_warpSearching && (int)(voyage.Clock * 2) % 2 == 0 ? "|" : ""), x + 242, y + 13, 170, 20, 14, _warpSearch == "" && !_warpSearching ? Canvas.Dim : Canvas.White);
        if (_warpSearch != "")
        {
            canvas.Text("✕", x + 414, y + 13, 20, 20, 13, Canvas.Dim, 1);
            if (canvas.Hover(x + 412, y + 10, 24, 26) && canvas.Pointer.Clicked) (_warpSearch, _warpPage) = ("", 0);
        }
        canvas.Text(_warpCities ? "누르면 그 도시의 항구로 옮겨 간다." : "누르면 그 해역의 한가운데 바다로 옮겨 간다(항구에 있으면 출항한다).", x + 16, y + 42, w - 32, 20, 12, Canvas.Dim);
        var places = _warpCities ? voyage.Data.Cities.Select(c => (c.Id, c.Name)).ToList() : voyage.SeasToWarp();
        if (_warpSearch != "") places = places.Where(p => p.Name.Contains(_warpSearch, StringComparison.OrdinalIgnoreCase)).ToList();
        if (places.Count == 0) canvas.Text("맞는 곳이 없다.", x + 16, y + 80, w - 32, 22, 14, Canvas.Dim, 1);
        int pages = Math.Max(1, (places.Count + columns * lines - 1) / (columns * lines));
        _warpPage = Math.Clamp(_warpPage, 0, pages - 1);
        float cw = (w - 32) / columns;
        for (int k = 0; k < columns * lines; k++)
        {
            int index = _warpPage * columns * lines + k;
            if (index >= places.Count) break;
            float bx = x + 16 + k / lines * cw, by = y + 68 + k % lines * 30;
            bool here = _warpCities ? voyage.Mode == Mode.Port && voyage.City.Id == places[index].Id : voyage.Mode == Mode.Sea && places[index].Name == voyage.SeaName;
            if (!canvas.Button((here ? "● " : "") + places[index].Name, bx, by, cw - 6, 27, true, places[index].Name.Length > 11 ? 11 : 13)) continue;
            if (_warpCities) { voyage.Dialog = Dialog.None; voyage.TownView = false; voyage.GoTo(places[index].Id); }
            else voyage.WarpToSea(places[index].Id);
            break;
        }
        if (canvas.Button("◀", x + 16, y + h - 42, 40, 30, _warpPage > 0)) _warpPage--;
        canvas.Text($"{_warpPage + 1} / {pages}", x + 60, y + h - 37, 60, 22, 14, Canvas.White, 1);
        if (canvas.Button("▶", x + 124, y + h - 42, 40, 30, _warpPage < pages - 1)) _warpPage++;
        if (canvas.Button("닫기", x + w - 116, y + h - 42, 100, 30)) (_warpOpen, _devOpen) = (false, false);
    }

    /// <summary>개발 메뉴 — 시험하기 쉽게 돈을 100만 단위로 늘리고 줄인다. 해역 워프도 여기서 연다.</summary>
    private void DevWindow()
    {
        canvas.PanelId = "WndDev";
        if (_warpOpen) { WarpWindow(); return; }
        const float w = 340, h = 190;
        float x = (canvas.Width - w) / 2, y = 34;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text("개발 메뉴", x + 16, y + 10, w - 32, 26, 18, Canvas.Gold, 0, true);
        canvas.Text($"소지금  {voyage.Money:N0} Ð", x + 16, y + 48, w - 32, 26, 17, Canvas.White);
        if (canvas.Button("− 1,000,000", x + 16, y + 86, 150, 32, voyage.Money > 0, 14)) voyage.AddMoney(-1_000_000);
        if (canvas.Button("+ 1,000,000", x + 174, y + 86, 150, 32, voyage.Money < 2_000_000_000, 14)) voyage.AddMoney(1_000_000);
        canvas.Text("전직증 · 레시피는 소지품 창(I)의 「아이템 추가」에서 넣는다.", x + 16, y + 124, w - 32, 20, 12, Canvas.Dim);
        if (canvas.Button("워프 (해역 · 도시)", x + 16, y + h - 40, 150, 30, true, 13)) _warpOpen = true;
        if (canvas.Button("닫기", x + w - 116, y + h - 40, 100, 30)) _devOpen = false;
    }

    /// <summary>시내 사람들의 이름표(화면 자리와 이름)와, 말을 걸 수 있는 사람의 이름 — 창이 프레임마다 넣는다.</summary>
    public readonly List<(float X, float Y, string Text)> Labels = [];
    /// <summary>조타 표시 — 배 둘레의 방위 글자(북 · 동 · 남 · 서)와 타륜("*")의 화면 자리. 키를 잡는 동안만 찬다.</summary>
    public readonly List<(float X, float Y, string Text)> Helm = [];
    /// <summary>시내에서 눌러 고른 사람의 이름 — 머리 위에 초록 역삼각형이 선다. 없으면 null.</summary>
    public string? Picked;
    public string? TalkTo;

    /// <summary>바다의 다른 배 이름표 — 해적은 붉게, 제 나라 배는 푸르게.</summary>
    public readonly List<(float X, float Y, string Text, int Tone)> ShipLabels = [];

    private void SeaLabels()
    {
        // 이름표끼리 겹치면 뒤의 것을 위로 한 줄씩 올린다(피해 글은 그대로 둔다)
        var placed = new List<(float X, float Y, float Half)>();
        foreach (var (lx, at, text, tone) in ShipLabels)
        {
            float ly = at, half = text.Length * 7f + 6;
            if (tone < 3)
            {
                for (int tries = 0; tries < 6 && placed.Exists(p => MathF.Abs(p.Y - ly) < 16 && MathF.Abs(p.X - lx) < p.Half + half); tries++) ly -= 17;
                placed.Add((lx, ly, half));
            }
            if (tone >= 3) canvas.Text(text, lx - 120, ly - 22, 240, 24, 18, tone == 4 ? new Color4(1f, 0.35f, 0.3f, 1) : new Color4(1f, 0.9f, 0.3f, 1), 1, true);      // 해전의 피해 글 — 3 적이 입은 것 · 4 이쪽이 입은 것
            else canvas.Text(text, lx - 120, ly - 20, 240, 20, 13, tone == 1 ? new Color4(1f, 0.45f, 0.4f, 1) : tone == 2 ? new Color4(0.6f, 0.8f, 1f, 1) : new Color4(0.95f, 0.95f, 0.8f, 1), 1, true);
        }
    }

    private bool _landItems;

    /// <summary>육상전 — 이쪽의 생명력 · 공격력 · 방어력과 상대, 싸움의 기록, 공격 · 테크닉 · 방어 · 도망.</summary>
    private void LandBattleWindow()
    {
        if (voyage.LandFight is not { } fight) { voyage.Dialog = Dialog.None; return; }
        const float w = 640, h = 380;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text($"육상전 — {fight.Round}합", x + 20, y + 12, w - 40, 28, 19, Canvas.Gold, 0, true);
        void Side(float sx, string title, double life, double most, int attack, int defense, Color4 tone)
        {
            canvas.Text(title, sx, y + 50, 290, 22, 15, tone, 0, true);
            canvas.Text($"생명력 {Math.Max(0, life):0} / {most:0}", sx, y + 76, 290, 20, 14, Canvas.White);
            canvas.Fill(sx, y + 98, 280, 5, new Color4(0.03f, 0.05f, 0.16f, 0.9f));
            canvas.Fill(sx, y + 98, 280 * (float)Math.Clamp(life / Math.Max(1, most), 0, 1), 5, new Color4(0.90f, 0.35f, 0.40f, 1));
            canvas.Text($"공격력 {attack}   방어력 {defense}", sx, y + 108, 290, 20, 13, Canvas.Dim);
        }
        Side(x + 20, voyage.PlayerName, voyage.Life, voyage.MaxLife, voyage.LandAttack, voyage.LandDefense, new Color4(0.6f, 0.8f, 1f, 1));
        Side(x + 340, $"{fight.Name}  Lv {fight.Level}", fight.Life, fight.MaxLife, fight.Attack, fight.Defense, new Color4(1f, 0.5f, 0.45f, 1));
        canvas.Line(x + 320, y + 50, x + 320, y + 130, new Color4(0.5f, 0.55f, 0.75f, 0.8f), 1);
        canvas.Fill(x + 20, y + 140, w - 40, 170, new Color4(0.02f, 0.04f, 0.14f, 0.9f));
        float line = y + 144;
        foreach (string text in fight.Log.TakeLast(8))
        {
            canvas.Text(text, x + 28, line, w - 56, 20, 13, Canvas.White);
            line += 20;
        }
        if (fight.Result is { } result)
        {
            canvas.Text(result, x + 20, y + h - 52, w - 170, 40, 13, Canvas.Gold);
            if (canvas.Button("확인", x + w - 140, y + h - 50, 120, 34)) voyage.EndLandBattle();
            return;
        }
        if (canvas.Button("공격", x + 20, y + h - 50, 120, 34)) voyage.LandAct(0);
        if (canvas.Button("테크닉 (행동력 10)", x + 148, y + h - 50, 170, 34, voyage.Vigour >= 10, 13)) voyage.LandAct(1);
        if (canvas.Button("방어", x + 326, y + h - 50, 84, 34)) voyage.LandAct(2);
        var usable = voyage.LandItems();
        if (usable.Count > 0 && canvas.Button(_landItems ? "닫기" : "아이템", x + 416, y + h - 50, 78, 34, true, 13)) _landItems = !_landItems;
        if (_landItems && usable.Count > 0)
        {
            // 가진 육상전 아이템 — 기록 칸 위에 덮어 늘어놓는다(두 줄 × 넷)
            canvas.Fill(x + 20, y + 140, w - 40, 170, new Color4(0.02f, 0.04f, 0.14f, 1f));
            for (int i = 0; i < Math.Min(8, usable.Count); i++)
                if (canvas.Button($"{usable[i].Name} ({voyage.Items.GetValueOrDefault(usable[i].Id)})", x + 28 + i % 2 * 294, y + 148 + i / 2 * 40, 286, 34, true, 13)) { voyage.UseLandItem(usable[i]); _landItems = false; }
        }
        else _landItems = false;
        if (canvas.Button("도망", x + w - 140, y + h - 50, 120, 34)) voyage.LandAct(3);
    }

    /// <summary>해전 — 두 배의 내구 · 선원 · 포문과 싸움의 기록, 포격 · 백병전 · 도주.</summary>
    private void BattleWindow()
    {
        if (voyage.Battle is not { } battle) { voyage.Dialog = Dialog.None; return; }
        const float w = 640, h = 400;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        var foe = battle.Foe;
        var mine = voyage.Stats;
        canvas.Text("해전", x + 20, y + 12, w - 40, 28, 19, Canvas.Gold, 0, true);
        void Side(float sx, string title, string hull, double durability, int most, double crew, int crewMost, int guns, int armor, Color4 tone)
        {
            canvas.Text(title, sx, y + 50, 290, 22, 15, tone, 0, true);
            canvas.Text(hull, sx, y + 72, 290, 20, 12, Canvas.Dim);
            canvas.Text($"내구 {Math.Max(0, durability):0} / {most}", sx, y + 96, 290, 20, 14, Canvas.White);
            canvas.Fill(sx, y + 118, 280, 5, new Color4(0.03f, 0.05f, 0.16f, 0.9f));
            canvas.Fill(sx, y + 118, 280 * (float)Math.Clamp(durability / Math.Max(1, most), 0, 1), 5, new Color4(0.90f, 0.35f, 0.40f, 1));
            canvas.Text($"선원 {Math.Max(0, crew):0} / {crewMost}", sx, y + 128, 290, 20, 14, Canvas.White);
            canvas.Fill(sx, y + 150, 280, 5, new Color4(0.03f, 0.05f, 0.16f, 0.9f));
            canvas.Fill(sx, y + 150, 280 * (float)Math.Clamp(crew / Math.Max(1, crewMost), 0, 1), 5, new Color4(0.35f, 0.85f, 0.45f, 1));
            canvas.Text($"포문 {guns}   장갑 {armor}", sx, y + 160, 290, 20, 13, Canvas.Dim);
        }
        Side(x + 20, voyage.PlayerName, voyage.Ship.Name, voyage.Durability, mine.Durability, voyage.Crew, mine.MaxCrew, voyage.GunsFitted, mine.Armor, new Color4(0.6f, 0.8f, 1f, 1));
        Side(x + 340, $"{Voyage.SeaShipKinds[foe.Kind]} 「{foe.Name}」", foe.Monster > 0 ? "" : foe.Ship.Name, foe.Durability, foe.MaxDurability, foe.Crew, foe.MaxCrew, foe.Guns, foe.Armor, new Color4(1f, 0.5f, 0.45f, 1));
        canvas.Line(x + 320, y + 50, x + 320, y + 180, new Color4(0.5f, 0.55f, 0.75f, 0.8f), 1);
        canvas.Fill(x + 20, y + 190, w - 40, 150, new Color4(0.02f, 0.04f, 0.14f, 0.9f));
        float line = y + 194;
        // 한 항목이 여러 줄일 수 있다(전술 글 + 피해 글, 긴 포격 글) — 줄로 나눠 끝의 일곱 줄만
        var rows = battle.Log.SelectMany(entry => entry.Split('\n')).SelectMany(row => row.Length > 50 && row.IndexOf("! ", 20) is > 0 and var cut ? new[] { row[..(cut + 1)], row[(cut + 2)..] } : [row]).ToList();
        foreach (string text in rows.TakeLast(7))
        {
            canvas.Text(text, x + 28, line, w - 56, 20, 12, Canvas.White);
            line += 20;
        }
        if (battle.Result is { } result)
        {
            canvas.Text(result, x + 20, y + h - 52, w - 170, 40, 13, Canvas.Gold);
            if (canvas.Button("확인", x + w - 140, y + h - 50, 120, 34)) voyage.EndBattle();
            return;
        }
        // 싸움은 바다 위에서 한다 — 이 창은 끝난 뒤의 결과만 보인다
        voyage.Dialog = Dialog.None;
    }

    /// <summary>해전 표시줄 — 싸우는 동안 화면 위쪽에: 두 배의 내구 · 선원, 거리와 사정, 장전, 포격 · 백병전.</summary>
    private void BattleBar()
    {
        if (voyage.Battle is not { Result: null } battle || voyage.Dialog != Dialog.None) return;
        const float w = 640, h = 104;
        float x = (canvas.Width - w) / 2, y = 16;
        canvas.PanelId = "WndBattleBar";
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        var foe = battle.Foe;
        var mine = voyage.Stats;
        void Side(float sx, string title, double durability, int most, double crew, int crewMost, Color4 tone, int align)
        {
            canvas.Text(title, sx, y + 6, 220, 20, title.Length > 26 ? 9 : title.Length > 20 ? 10 : title.Length > 16 ? 12 : 14, tone, align, true);
            canvas.Fill(sx, y + 30, 220, 6, new Color4(0.03f, 0.05f, 0.16f, 0.9f));
            canvas.Fill(sx, y + 30, 220 * (float)Math.Clamp(durability / Math.Max(1, most), 0, 1), 6, new Color4(0.90f, 0.35f, 0.40f, 1));
            canvas.Text($"내구 {Math.Max(0, durability):0} / {most}", sx, y + 38, 220, 18, 12, Canvas.White, align);
            canvas.Fill(sx, y + 58, 220, 5, new Color4(0.03f, 0.05f, 0.16f, 0.9f));
            canvas.Fill(sx, y + 58, 220 * (float)Math.Clamp(crew / Math.Max(1, crewMost), 0, 1), 5, new Color4(0.35f, 0.85f, 0.45f, 1));
            canvas.Text($"선원 {Math.Max(0, crew):0} / {crewMost}", sx, y + 65, 220, 18, 12, Canvas.White, align);
        }
        Side(x + 12, $"{voyage.PlayerName} — 포 {voyage.GunsFitted}문", voyage.Durability, mine.Durability, voyage.Crew, mine.MaxCrew, new Color4(0.6f, 0.8f, 1f, 1), 0);
        Side(x + w - 232, foe.Monster > 0 ? foe.Name : $"{Voyage.SeaShipKinds[foe.Kind]} 「{foe.Name}」 — 포 {foe.Guns}문", foe.Durability, foe.MaxDurability, foe.Crew, foe.MaxCrew, new Color4(1f, 0.5f, 0.45f, 1), 2);
        // 가운데: 쏠 수 있는가와 장전
        float mx = x + 240, mw = w - 480;
        string? why = voyage.FireBlocker();
        canvas.Text(battle.Boarding ? "백병전!" : why ?? "쏠 수 있다", mx, y + 4, mw, 18, 12, why == null ? new Color4(0.5f, 1f, 0.6f, 1) : new Color4(1f, 0.75f, 0.45f, 1), 1);
        canvas.Fill(mx, y + 24, mw, 5, new Color4(0.03f, 0.05f, 0.16f, 0.9f));
        canvas.Fill(mx, y + 24, mw * (float)Math.Clamp(1 - battle.MyReload / 6, 0, 1), 5, new Color4(0.95f, 0.8f, 0.3f, 1));
        if (battle.Boarding)
        {
            // 백병전: 전술 셋 가운데 하나 — 고른 것 아래에 금빛 줄
            for (int i = 0; i < 3; i++)
            {
                if (canvas.Button(Voyage.Tactics[i], mx + i * (mw / 3), y + 32, mw / 3 - 2, 24, true, 12)) voyage.SetTactic(i);
                if (battle.Tactic == i) canvas.Fill(mx + i * (mw / 3), y + 57, mw / 3 - 2, 3, new Color4(0.95f, 0.8f, 0.3f, 1));
            }
            canvas.Text("돌격 > 총격 > 방어 > 돌격", mx, y + 60, mw, 14, 9, new Color4(0.8f, 0.85f, 0.95f, 1), 1);
            if (canvas.Button(voyage.Text(1291, "퇴각"), mx, y + 76, mw, 22, battle.RetreatIn <= 0, 12)) voyage.Retreat();
        }
        else
        {
            if (canvas.Button("포격 (Space)", mx, y + 34, mw, 28, why == null, 12)) voyage.Fire();
            // 「기뢰 설치」 · 「도주」 스킬이 있으면 백병전 단추 줄을 나눠 곁에 단추가 선다
            int cells = 1 + (voyage.CanMine ? 1 : 0) + (voyage.CanFlee ? 1 : 0) + (voyage.CanAid ? 1 : 0), cell = 0;
            float each = mw / cells;
            if (canvas.Button("백병전", mx, y + 66, each - 2, 28, voyage.BoardBlocker() == null, 12)) voyage.Board();
            if (voyage.CanMine && canvas.Button(cells > 3 ? $"기뢰{battle.Mines.Count}" : $"기뢰 {battle.Mines.Count}/3", mx + ++cell * each, y + 66, each - 2, 28, battle.MineIn <= 0 && battle.Mines.Count < 3, cells > 2 ? 10 : 12)) voyage.LayMine();
            if (voyage.CanAid && canvas.Button("원군", mx + ++cell * each, y + 66, each - 2, 28, !battle.AidCalled && battle.Foe.Monster == 0, cells > 2 ? 10 : 12)) voyage.CallAid();
            if (voyage.CanFlee && canvas.Button("도주", mx + ++cell * each, y + 66, each - 2, 28, battle.RetreatIn <= 0, 12)) voyage.Flee();
        }
        if (battle.Log.Count > 0) canvas.Text(battle.Log[^1], x, y + h + 2, w, 18, 12, Canvas.White, 1);
    }

    private void TownLabels()
    {
        // 사람을 누르면(이름표나 몸 둘레) 그 사람이 골라진다 — 원본처럼 이름 밑 · 머리 위에 초록 역삼각형이 선다. 빈 데를 누르면 풀린다
        if (canvas.Pointer.Clicked && voyage.Dialog == Dialog.None)
        {
            Picked = null;
            foreach (var (lx, ly, text) in Labels)
                if (canvas.Hover(lx - 45, ly - 24, 90, 150)) Picked = text;
        }
        if (Picked != null && !Labels.Exists(l => l.Text == Picked)) Picked = null;
        foreach (var (lx, ly, text) in Labels)
        {
            bool picked = text == Picked;
            canvas.Text(text, lx - 100, ly - (picked ? 34 : 20), 200, 20, 14, new Color4(0.55f, 1f, 0.6f, 1), 1);
            if (!picked) continue;
            // 역삼각형(너비 14, 높이 11) — 가로줄을 좁혀 가며 쌓는다
            for (int k = 0; k < 11; k++)
            {
                float half = 7 * (1 - k / 11f);
                canvas.Line(lx - half - 1, ly - 13 + k, lx + half + 1, ly - 13 + k, new Color4(0.05f, 0.25f, 0.08f, 0.9f), 1.2f);
                canvas.Line(lx - half, ly - 13 + k, lx + half, ly - 13 + k, new Color4(0.35f, 0.95f, 0.4f, 1), 1.2f);
            }
        }
        // 눌러 가는 곳의 표식
        // 원본 커서와 같은 금빛의 화살촉(화면 부품 묶음 0 의 맨 끝 장, 24 × 40)이 그 자리를 내려 찍는다 — 살짝 오르내린다.
        // 이 조각이 원본에서 바로 이 쓰임인지는 짐작이다(원본 화면 구석에 같은 빛깔의 촉이 보였다).
        if (Target is { } target)
        {
            float bob = MathF.Abs(MathF.Sin((float)voyage.Clock * 4)) * 6;
            if (!canvas.Image("gm0:1258", () => (_markParts ??= new UiParts(0)).Pixels(1258), target.X - 9, target.Y - 30 - bob, 18, 30, MathF.PI))
            {
                canvas.Circle(target.X, target.Y, 9, new Color4(0.1f, 0.1f, 0.1f, 0.55f));
                canvas.Circle(target.X, target.Y, 7, Canvas.Gold);
            }
        }
        if (TalkTo != null && voyage.Dialog == Dialog.None)
        {
            // 화면 가운데를 가리지 않게 맨 아래, 기록 창(너비 620)의 오른쪽에 둔다. 자리가 좁으면 전처럼 기록 창 바로 위
            float free = canvas.Width - 632 - 8, w = Math.Min(360, free);
            float x = 632 + (free - w) / 2, y = canvas.Height - 78;
            if (free < 280) (w, x, y) = (360, (canvas.Width - 360) / 2, canvas.Height - 216);
            canvas.Panel(x, y, w, 40);
            canvas.Text(TalkTo == "출구" ? "F : 밖으로 나간다" : $"F : {TalkTo}에게 말을 건다", x, y + 8, w, 26, 17, Canvas.Gold, 1);
        }
    }

    /// <summary>단축키로 하는 일들 — (이름, 화면에 보이는 말, 기본 글쇠).</summary>
    public static readonly (string Action, string Label, int Default)[] KeyActions =
    [
        ("Map", "지도 (미니맵)", 'M'), ("TownMenu", "도시 메뉴", 'T'), ("Skills", "스킬 창", 'X'), ("ShipInfo", "선박 정보", 'V'), ("Cargo", "적재화물", 'N'), ("Pause", "일시정지", 'P'), ("Fitting", "선박부품 탈착", 'B'), ("UseSkills", "스킬 사용 창", 0x71),
        ("Quick", "퀵슬롯 여닫기", 'Q'), ("Items", "소지품", 'I'), ("Outfit", "캐릭터 정보", 'C'), ("Fullscreen", "전체 화면", 0x7A),
        ("SailUp", "돛 올리기", 'W'), ("SailDown", "돛 내리기", 'S'), ("Delegate", "위임 항해", 'H'),
        ("Settings", "환경설정", 'O'), ("Menu", "차림 (☰)", 'K'),
        // 차림의 나머지 — 기본은 글쇠 없음(겹치지 않게), 여기서 매어 쓴다
        ("Nav", "내비게이션", 'J'), ("KeysWin", "단축키 등록", 0), ("Mod", "모드", 0), ("Dev", "개발 메뉴", 0), ("Warp", "워프", 0), ("Jobs", "직업 일람", 0),
        ("Equip", "장비물품", 0), ("Aides", "부관정보", 0), ("QuickSetup", "퀵슬롯 등록", 0), ("Found", "발견물 목록", 0), ("Logout", "로그아웃", 0),
    ];

    /// <summary>그 일에 매인 글쇠(설정에 없으면 기본값).</summary>
    public static int KeyOf(Dictionary<string, int> keys, string action) =>
        keys.TryGetValue(action, out int key) ? key : Array.Find(KeyActions, a => a.Action == action).Default;

    /// <summary>단축키 조합 — 글쇠 번호의 윗자리에 Ctrl · Alt · Shift 를 얹는다.</summary>
    public const int KeyCtrl = 0x100, KeyAlt = 0x200, KeyShift = 0x400;
    public static bool IsModifier(int key) => key is 0x10 or 0x11 or 0x12 or (>= 0xA0 and <= 0xA5);

    public static string KeyName(int key) => key > 0xFF
        ? ((key & KeyCtrl) != 0 ? "Ctrl+" : "") + ((key & KeyAlt) != 0 ? "Alt+" : "") + ((key & KeyShift) != 0 ? "Shift+" : "") + KeyName(key & 0xFF)
        : key switch
    {
        0 => "없음",
        >= 0x70 and <= 0x7B => $"F{key - 0x6F}",
        >= '0' and <= '9' or >= 'A' and <= 'Z' => ((char)key).ToString(),
        0x20 => "Space", 0x09 => "Tab", 0x0D => "Enter",
        0x21 => "PgUp", 0x22 => "PgDn", 0x23 => "End", 0x24 => "Home", 0x25 => "←", 0x26 => "↑", 0x27 => "→", 0x28 => "↓", 0x2D => "Insert", 0x2E => "Delete", 0x10 => "Shift", 0x12 => "Alt", 0x08 => "Backspace",
        >= 0x60 and <= 0x69 => $"Num{key - 0x60}", 0xC0 => "`", 0xBD => "-", 0xBB => "=", 0xDB => "[", 0xDD => "]", 0xBA => ";", 0xDE => "'", 0xBC => ",", 0xBE => ".", 0xBF => "/", 0xDC => "\\",
        _ => $"키 {key}",
    };

    /// <summary>단축키 등록 창이 다음 글쇠를 기다리는 일(없으면 null) — 창이 글쇠를 받아 맨다.</summary>
    public string? KeyWaiting;
    private bool _keysOpen;

    /// <summary>단축키 등록 — 줄을 누르고 새 글쇠를 누른다. 고른 것은 설정에 남는다.</summary>
    private void KeyWindow()
    {
        canvas.PanelId = "WndKeys";
        // 두 줄로 세운다 — 일이 열다섯이라 한 줄로는 화면 배율 2에서 창이 넘친다
        const float w = 1110, half = 370;
        int perColumn = (KeyActions.Length + 2) / 3;      // 세 줄
        float h = 96 + perColumn * 32 + 46;
        float x = (canvas.Width - w) / 2, y = 34;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text("단축키 등록", x + 16, y + 10, w - 32, 26, 18, Canvas.Gold, 0, true);
        canvas.Text(KeyWaiting == null ? "바꿀 줄을 누르고 새 글쇠를 누른다." : "새 글쇠를 누른다. (Esc 그만두기)", x + 16, y + 40, w - 32, 20, 13, Canvas.Dim);
        var keys = voyage.Data.Settings.Keys;
        float row = y + 68;
        for (int k = 0; k < KeyActions.Length; k++)
        {
            var (action, label, _) = KeyActions[k];
            float cx = x + (k / perColumn) * half, cy = y + 68 + (k % perColumn) * 32;
            bool waiting = KeyWaiting == action;
            canvas.Text(label, cx + 16, cy + 4, 200, 22, 15, Canvas.White);
            if (canvas.Button(waiting ? "…" : KeyName(KeyOf(keys, action)), cx + half - 136, cy, 120, 28, true, 14)) KeyWaiting = waiting ? null : action;
        }
        row += perColumn * 32;
        canvas.Text("걷기 W A S D · 돛 ↑ ↓ · 입항 F · 퀵슬롯 1 ~ 8 은 고정이다.", x + 16, row + 4, w - 32, 20, 12, Canvas.Dim);
        if (canvas.Button("기본값", x + 16, y + h - 40, 100, 30, true, 13)) { keys.Clear(); voyage.Data.SaveSettings(); }
        if (canvas.Button("닫기", x + w - 116, y + h - 40, 100, 30)) (_keysOpen, KeyWaiting) = (false, null);
    }

    /// <summary>
    /// 제목 줄 — 창의 제목 줄을 스스로 그린다. 왼쪽 햄버거(☰) 단추를 누르면 차림이 뜨고, 「설정」이 해상도·소리 창을 연다.
    /// 가운데를 잡고 끌면 창이 움직인다(창 쪽에서 처리). 오른쪽은 내리기와 닫기.
    /// </summary>
    private void Display()
    {
        const float bar = TitleHeight;
        canvas.Fill(0, -bar, canvas.Width, bar, new Color4(0.03f, 0.05f, 0.16f, 1));
        canvas.Line(0, -0.5f, canvas.Width, -0.5f, Canvas.PanelEdge, 1);
        canvas.Text("대항해시대 온라인 — 항해 (개인용)", TitleMenuWidth + 8, -bar + 5, 500, 22, 14, Canvas.Dim, 0, false, false);

        bool TitleButton(string label, float x, float w, float size = 16)
        {
            bool hover = canvas.Hover(x, -bar, w, bar);
            if (hover) canvas.Fill(x, -bar, w, bar - 1, new Color4(0.22f, 0.32f, 0.66f, 1));
            canvas.Text(label, x, -bar + (bar - size * 1.4f) / 2, w, bar, size, Canvas.White, 1, false, false);
            if (!hover || !canvas.Pointer.Clicked) return false;
            canvas.Pointer.Consumed = true;
            return true;
        }
        if (TitleButton("☰", 0, TitleMenuWidth, 18)) _menuOpen = !_menuOpen;
        // 도시락 차림(⋮⋮⋮) — 게임 밖의 도구들
        if (TitleButton("⋮⋮⋮", canvas.Width - 132, 44, 13)) (_bentoOpen, _menuOpen) = (!_bentoOpen, false);
        if (TitleButton("—", canvas.Width - 88, 44, 13)) Minimize?.Invoke();
        if (TitleButton("✕", canvas.Width - 44, 44, 14)) Quit?.Invoke();
        if (canvas.Hover(0, -bar, canvas.Width, bar)) canvas.Pointer.Consumed = true;

        if (_bentoOpen)
        {
            (string Label, Action Run)[] tools =
            [
                ("개발도구 열기", OpenTools),
                ("개발 메뉴", () => { if (voyage.Created) _devOpen = true; }),
            ];
            const float bw = 180;
            float bx = canvas.Width - 180 - bw;      // 오른쪽 위의 둥근 단추들을 비켜서
            canvas.Panel(bx, 0, bw, tools.Length * 32 + 8);
            canvas.Block(bx, 0, bw, tools.Length * 32 + 8);
            for (int i = 0; i < tools.Length; i++)
                if (canvas.Button(tools[i].Label, bx + 4, 4 + i * 32, bw - 8, 28, true, 14)) { _bentoOpen = false; tools[i].Run(); }
            if (canvas.Pointer.Clicked && !canvas.Pointer.Consumed) _bentoOpen = false;
        }
        if (_menuOpen)
        {
            (string Label, Action Run)[] menu =
            [
                ("환경설정 (해상도 · 소리)", () => { if (voyage.Created) voyage.Dialog = Dialog.None; _displayOpen = true; }),
                ("단축키 등록", () => _keysOpen = true),
                ("모드", () => { if (voyage.Created) voyage.Dialog = Dialog.None; _modOpen = true; }),
                ("개발 메뉴", () => { if (voyage.Created) _devOpen = true; }),
                ("워프 (해역 · 도시)", () => { if (voyage.Created) { voyage.Dialog = Dialog.None; OpenWarp(_warpCities); } }),
                ("직업 일람", () => { if (voyage.Created) voyage.Dialog = Dialog.Jobs; }),
                ("선박부품 (B)", () => { if (voyage.Created) voyage.Dialog = Dialog.Fitting; }),
                (voyage.Paused ? "● 일시정지 풀기 (P)" : "일시정지 (P)", () => voyage.Paused = !voyage.Paused),
                ("전체 화면 (F11)", () => SetDisplay?.Invoke(voyage.Data.Settings.WindowWidth, voyage.Data.Settings.WindowHeight, !voyage.Data.Settings.Fullscreen)),
                ("끝내기", () => Quit?.Invoke()),
            ];
            const float mw = 210;
            canvas.Panel(0, 0, mw, menu.Length * 32 + 8);
            canvas.Block(0, 0, mw, menu.Length * 32 + 8);
            for (int i = 0; i < menu.Length; i++)
                if (canvas.Button(menu[i].Label, 4, 4 + i * 32, mw - 8, 28, true, 14)) { _menuOpen = false; menu[i].Run(); }
            if (canvas.Pointer.Clicked && !canvas.Pointer.Consumed) _menuOpen = false;
        }
        if (_keysOpen) KeyWindow();
        if (_devOpen) DevWindow();
        if (_modOpen && voyage.Created && voyage.Dialog != Dialog.None) _modOpen = false;      // 다른 창이 뜨면 닫는다
        if (_modOpen) ModWindow();
        if (!_displayOpen) return;
        if (voyage.Created && voyage.Dialog != Dialog.None) { _displayOpen = false; return; }
        if (_soundOpen) { SoundWindow(); return; }
        canvas.PanelId = "WndDisplay";

        const float w = 300;
        float h = 74 + (1 + 3) * 30 + 34 + 62 + 68 + 68 + 34;      // + 퀵슬롯 배율 줄
        float x = (canvas.Width - w) / 2, y = 34;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text("설정 — 화면과 소리", x + 16, y + 10, w - 32, 26, 18, Canvas.Gold, 0, true);
        canvas.Text($"지금 {Pixels.Width} × {Pixels.Height}   (F11 전체 화면)", x + 16, y + 40, w - 32, 20, 13, Canvas.Dim);
        var settings = voyage.Data.Settings;
        float row = y + 68;
        // 해상도는 고르는 칸 하나 — 누르면 아래로 목록이 펼쳐진다(목록은 맨 나중에, 다른 것들 위에 그린다)
        float listY = row + 28, listH = Sizes.Length * 26 + 4;
        bool overList = _sizesOpen && canvas.Hover(x + 16, listY, w - 32, listH), listClick = overList && canvas.Pointer.Clicked;
        if (canvas.Button($"{Pixels.Width} × {Pixels.Height}   {(_sizesOpen ? "▲" : "▼")}", x + 16, row, w - 32, 26, true, 14)) _sizesOpen = !_sizesOpen;
        else if (_sizesOpen && canvas.Pointer.Clicked && !overList) _sizesOpen = false;
        if (overList) canvas.Pointer.Clicked = false;      // 목록 밑에 깔린 단추가 눌리지 않게
        row += 30;
        if (canvas.Button((settings.Fullscreen ? "● " : "") + "전체 화면", x + 16, row, w - 32, 26, true, 14))
            SetDisplay?.Invoke(settings.WindowWidth, settings.WindowHeight, !settings.Fullscreen);
        // 글과 창의 배율
        row += 34;
        canvas.Text($"UI 크기 {canvas.Scale * 100:0}%" + (settings.UiScale > 0 ? "" : " (자동)"), x + 16, row + 3, 130, 22, 14, Canvas.White);
        if (canvas.Button("−", x + 150, row, 36, 26, canvas.Scale > 0.75f, 16)) { settings.UiScale = Math.Max(0.75, Math.Round(canvas.Scale * 4 - 1) / 4); voyage.Data.SaveSettings(); }
        if (canvas.Button("+", x + 190, row, 36, 26, canvas.Scale < 3, 16)) { settings.UiScale = Math.Min(3, Math.Round(canvas.Scale * 4 + 1) / 4); voyage.Data.SaveSettings(); }
        if (canvas.Button("자동", x + 230, row, 54, 26, settings.UiScale > 0, 13)) { settings.UiScale = 0; voyage.Data.SaveSettings(); }
        // 그림 단추의 배율 — 글과 창의 배율과 따로
        row += 34;
        canvas.Text($"아이콘 배율 {settings.IconScale * 100:0}%", x + 16, row + 3, 130, 22, 14, Canvas.White);
        if (canvas.Button("−", x + 150, row, 36, 26, settings.IconScale > 0.5, 16)) { settings.IconScale = Math.Max(0.5, Math.Round(settings.IconScale * 10 - 1) / 10); voyage.Data.SaveSettings(); }
        if (canvas.Button("+", x + 190, row, 36, 26, settings.IconScale < 2.5, 16)) { settings.IconScale = Math.Min(2.5, Math.Round(settings.IconScale * 10 + 1) / 10); voyage.Data.SaveSettings(); }
        if (canvas.Button("100%", x + 230, row, 54, 26, settings.IconScale != 1, 13)) { settings.IconScale = 1; voyage.Data.SaveSettings(); }
        // 퀵슬롯 칸의 배율 — 10% 단위
        row += 34;
        canvas.Text($"퀵슬롯 배율 {settings.QuickScale * 100:0}%", x + 16, row + 3, 130, 22, 14, Canvas.White);
        if (canvas.Button("−", x + 150, row, 36, 26, settings.QuickScale > 0.5, 16)) { settings.QuickScale = Math.Max(0.5, Math.Round(settings.QuickScale * 10 - 1) / 10); voyage.Data.SaveSettings(); }
        if (canvas.Button("+", x + 190, row, 36, 26, settings.QuickScale < 2.5, 16)) { settings.QuickScale = Math.Min(2.5, Math.Round(settings.QuickScale * 10 + 1) / 10); voyage.Data.SaveSettings(); }
        if (canvas.Button("100%", x + 230, row, 54, 26, settings.QuickScale != 1, 13)) { settings.QuickScale = 1; voyage.Data.SaveSettings(); }
        // 배경음 크기
        row += 34;
        canvas.Text($"배경음 {settings.MusicVolume * 100:0}%", x + 16, row + 3, 120, 22, 14, Canvas.White);
        if (canvas.Button("−", x + 140, row, 40, 26, settings.MusicVolume > 0, 16)) { settings.MusicVolume = Math.Max(0, Math.Round(settings.MusicVolume - 0.1, 1)); voyage.Data.SaveSettings(); }
        if (canvas.Button("+", x + 186, row, 40, 26, settings.MusicVolume < 1, 16)) { settings.MusicVolume = Math.Min(1, Math.Round(settings.MusicVolume + 0.1, 1)); voyage.Data.SaveSettings(); }
        // 바다의 둥근 지도 크기
        row += 34;
        canvas.Text($"항해 지도 {settings.SeaMapScale * 100:0}%", x + 16, row + 3, 130, 22, 14, Canvas.White);
        if (canvas.Button("−", x + 150, row, 36, 26, settings.SeaMapScale > 0.5, 16)) { settings.SeaMapScale = Math.Max(0.5, Math.Round(settings.SeaMapScale - 0.1, 1)); voyage.Data.SaveSettings(); }
        if (canvas.Button("+", x + 190, row, 36, 26, settings.SeaMapScale < 1.5, 16)) { settings.SeaMapScale = Math.Min(1.5, Math.Round(settings.SeaMapScale + 0.1, 1)); voyage.Data.SaveSettings(); }
        // 항해 지도의 모양 — 동그라미(원본) · 네모
        if (canvas.Button(settings.SeaMapSquare ? "■" : "●", x + 230, row, 54, 26, true, 14)) { settings.SeaMapSquare = !settings.SeaMapSquare; voyage.Data.SaveSettings(); }
        if (canvas.Hover(x + 230, row, 54, 26)) _tip = (settings.SeaMapSquare ? "네모 — 누르면 동그라미" : "동그라미 — 누르면 네모", x + 257, row);
        // 항해 지도의 배율 — 크면 가까운 곳이 크게, 작으면 먼 곳까지(지도 위에서 휠로도 바꾼다)
        row += 34;
        canvas.Text($"지도 배율 {settings.SeaMapZoom * 100:0}%", x + 16, row + 3, 130, 22, 14, Canvas.White);
        if (canvas.Button("−", x + 150, row, 36, 26, settings.SeaMapZoom > 0.25, 16)) SetSeaMapZoom(settings.SeaMapZoom * 0.8);
        if (canvas.Button("+", x + 190, row, 36, 26, settings.SeaMapZoom < 4, 16)) SetSeaMapZoom(settings.SeaMapZoom * 1.25);
        if (canvas.Button("100%", x + 230, row, 54, 26, settings.SeaMapZoom != 1, 13)) SetSeaMapZoom(1);
        // 소지품 창의 격자 줄 수(다섯 칸 × n 줄)
        row += 34;
        canvas.Text($"소지품 5 × {settings.ItemRows}", x + 16, row + 3, 130, 22, 14, Canvas.White);
        if (canvas.Button("−", x + 150, row, 36, 26, settings.ItemRows > 4, 16)) { settings.ItemRows--; voyage.Data.SaveSettings(); }
        if (canvas.Button("+", x + 190, row, 36, 26, settings.ItemRows < 8, 16)) { settings.ItemRows++; voyage.Data.SaveSettings(); }
        row += 34;
        if (canvas.Button("효과음 고르기", x + 16, row, 170, 26, true, 13)) _soundOpen = true;
        if (canvas.Button("닫기", x + w - 116, y + h - 40, 100, 30)) (_displayOpen, _soundOpen, _sizesOpen) = (false, false, false);
        if (!_sizesOpen) return;
        canvas.Pointer.Clicked = listClick;
        canvas.Fill(x + 16, listY, w - 32, listH, new Color4(0.03f, 0.05f, 0.18f, 1));
        canvas.Frame(x + 16, listY, w - 32, listH, Canvas.PanelEdge, 1.2f);
        for (int i = 0; i < Sizes.Length; i++)
        {
            var (width, height) = Sizes[i];
            float ly = listY + 2 + i * 26;
            bool current = !settings.Fullscreen && Pixels.Width == width && Pixels.Height == height;
            bool fits = width <= Screen.Width && height <= Screen.Height;
            bool over = fits && canvas.Hover(x + 18, ly, w - 36, 26);
            if (over) canvas.Fill(x + 18, ly, w - 36, 26, new Color4(0.2f, 0.3f, 0.6f, 0.8f));
            canvas.Text((current ? "● " : "") + $"{width} × {height}" + (fits ? "" : "  (화면보다 큼)"), x + 18, ly + 3, w - 36, 22, 14, fits ? Canvas.White : Canvas.Dim, 1);
            if (over && canvas.Pointer.Clicked) { _sizesOpen = false; SetDisplay?.Invoke(width, height, false); }
        }
    }
    private bool _sizesOpen;

    /// <summary>
    /// 발견 카드 — 도시에 처음 들어가면(「항구-마을」 발견물) 화면 가운데 위에 카드가 뜬다: 그림 · 이름 · 갈래와 별 · 설명 · 얻은 경험과 명성.
    /// 여덟 초 뒤나 카드를 누르면 사라진다. 원본의 카드 모습은 못 맞춰 봤다(짜임은 의뢰의 발견 창을 따랐다).
    /// </summary>
    private void DiscoveryCard()
    {
        if (voyage.Discovered is not { } found) return;
        float age = (float)(voyage.Clock - voyage.DiscoveredAt);
        if (age > 8 || age < 0) { voyage.Discovered = null; return; }
        const float w = 520, h = 190;
        float fade = Math.Clamp(Math.Min(age * 3, (8 - age) * 2), 0, 1), x = (canvas.Width - w) / 2, y = 70 - (1 - Math.Min(1, age * 3)) * 20;
        canvas.Fill(x, y, w, h, new Color4(0.03f, 0.06f, 0.20f, 0.93f * fade));
        canvas.Frame(x, y, w, h, new Color4(Canvas.Gold.R, Canvas.Gold.G, Canvas.Gold.B, fade), 2);
        canvas.Text($"발견!  {found.Name}", x + 16, y + 8, w - 32, 28, 20, new Color4(Canvas.Gold.R, Canvas.Gold.G, Canvas.Gold.B, fade), 0, true);
        canvas.Text($"{voyage.DiscoveryKind(found.Kind)}   {voyage.DiscoveryStars(found)}   모험 경험 +{found.Exp}   명성 +{found.Fame}", x + 16, y + 38, w - 32, 22, 13, new Color4(1, 1, 1, fade));
        bool pictured = canvas.Image($"sd{found.Id}", () => (_discoveryImages ??= new ImageSet(@"0010\0001\sd")).Pixels(0, found.Id), x + 16, y + 66, 110, 110, 0, false, fade);
        if (pictured) canvas.Frame(x + 16, y + 66, 110, 110, Canvas.PanelEdge, 1);
        canvas.Text(found.Description, x + (pictured ? 140 : 16), y + 66, w - (pictured ? 156 : 32), 116, 13, new Color4(1, 1, 1, fade));
        if (canvas.Hover(x, y, w, h) && canvas.Pointer.Clicked) { canvas.Pointer.Consumed = true; voyage.Discovered = null; }
    }
    private (float X, float Y, float Size) _seaMapRect;
    private void SetSeaMapZoom(double zoom)
    {
        voyage.Data.Settings.SeaMapZoom = Math.Round(Math.Clamp(zoom, 0.25, 4), 2);
        voyage.Data.SaveSettings();
    }
    private bool _autoProduce;
    private object? _autoFor;
    private double _autoNext, _autoSeen;

    /// <summary>
    /// 효과음 고르기 — 원본 소리 1,397개에 이름표가 없어서, 들어 보고 메모를 달고 일(선회 · 돛 조종 · 스킬)에 매는 창.
    /// 왼쪽에서 묶음을 고르면 오른쪽에 그 묶음의 소리가 늘어선다. 줄을 누르면 들린다.
    /// </summary>
    private void SoundWindow()
    {
        canvas.PanelId = "WndSound";
        const float sw = 700, sh = 500;
        float sx = (canvas.Width - sw) / 2, y = Math.Max(34, (canvas.Height - sh) / 2);
        var settings = voyage.Data.Settings;
        canvas.Panel(sx, y, sw, sh);
        canvas.Block(sx, y, sw, sh);
        canvas.Text("효과음 고르기", sx + 16, y + 10, 200, 26, 18, Canvas.Gold, 0, true);
        canvas.Text("묶음 (휠)", sx + 16, y + 42, 100, 20, 13, Canvas.Dim);
        const int banksShown = 15;
        _bankTop = Math.Clamp(_bankTop, 0, Audio.SoundEffects.Banks - banksShown);
        for (int bank = _bankTop; bank < _bankTop + banksShown; bank++)
        {
            float bx = sx + 16, by = y + 64 + (bank - _bankTop) * 26;
            bool on = bank == _soundBank, over = canvas.Hover(bx, by, 190, 25);
            if (on) canvas.Fill(bx, by, 190, 25, new Color4(0.16f, 0.62f, 0.72f, 0.98f));
            else if (over) canvas.Fill(bx, by, 190, 25, new Color4(0.2f, 0.3f, 0.6f, 0.6f));
            canvas.Text($"{bank}", bx + 4, by + 3, 26, 20, 13, Canvas.Dim, 2);
            canvas.Text(settings.SoundMemos.GetValueOrDefault($"{bank}") ?? "", bx + 38, by + 3, 150, 20, 13, Canvas.White);
            if (over && canvas.Pointer.Clicked) (_soundBank, _soundIndex, _soundTop, _memoEditing) = (bank, 0, 0, false);
        }
        _soundLeft = canvas.Pointer.X < sx + 214;
        canvas.Line(sx + 214, y + 42, sx + 214, y + sh - 16, new Color4(0.5f, 0.55f, 0.75f, 0.8f), 1);

        float lx = sx + 226, lw = sw - 242;
        int count = SoundCount?.Invoke(_soundBank) ?? 0;
        const int shown = 8;
        _soundIndex = Math.Clamp(_soundIndex, 0, Math.Max(0, count - 1));
        _soundTop = Math.Clamp(_soundTop, 0, Math.Max(0, count - shown));
        // 묶음 제목 칸 — 누르고 적는다
        canvas.Text($"묶음 {_soundBank} 제목", lx, y + 45, 90, 20, 13, Canvas.Dim);
        bool overTitle = canvas.Hover(lx + 92, y + 42, lw - 212, 26), wasTitle = _memoEditing && _memoBank;
        if (canvas.Pointer.Clicked)
        {
            if (wasTitle && !overTitle) { _memoEditing = false; voyage.Data.SaveSettings(); }
            if (overTitle) (_memoEditing, _memoBank) = (true, true);
        }
        bool titling = _memoEditing && _memoBank;
        canvas.Fill(lx + 92, y + 42, lw - 212, 26, new Color4(0.02f, 0.04f, 0.14f, 0.95f));
        canvas.Frame(lx + 92, y + 42, lw - 212, 26, titling ? Canvas.Gold : Canvas.PanelEdge, 1.2f);
        string title = settings.SoundMemos.GetValueOrDefault($"{_soundBank}") ?? "";
        canvas.Text(title == "" && !titling ? "누르고 적는다 (예: 바다)" : title + (titling && (int)(voyage.Clock * 2) % 2 == 0 ? "|" : ""), lx + 98, y + 45, lw - 224, 20, 13, title == "" && !titling ? Canvas.Dim : Canvas.White);
        canvas.Text(count == 0 ? "소리 없음" : $"소리 {count}개 (줄을 누르면 들린다 · 휠)", lx, y + 74, lw, 20, 13, Canvas.Dim);
        for (int i = _soundTop; i < Math.Min(count, _soundTop + shown); i++)
        {
            float ry = y + 96 + (i - _soundTop) * 26;
            string key = $"{_soundBank}:{i}";
            bool chosen = i == _soundIndex, hover = canvas.Hover(lx, ry, lw, 25);
            if (chosen) canvas.Fill(lx, ry, lw, 25, new Color4(0.12f, 0.62f, 0.55f, 0.95f));
            else if (hover) canvas.Fill(lx, ry, lw, 25, new Color4(0.2f, 0.3f, 0.6f, 0.6f));
            canvas.Text(key, lx + 6, ry + 3, 60, 20, 13, Canvas.White);
            string uses = string.Join(" ", new[] { ("Turn", "선회"), ("Sail", "돛"), ("Skill", "스킬"), ("Eat", "음식"), ("Door", "입구"), ("Open", "문"), ("Click", "누름"), ("Quest", "퀘스트"), ("Error", "오류"), ("Mastery", "숙련도"), ("Warn", "경고"), ("SkillUp", "레벨업"), ("Done", "완료"), ("StudyDone", "연구 완료"), ("Bank", "저금"), ("Part", "부품"), ("Buy", "구매"), ("Drunk", "술"), ("University", "대학") }.Where(c => settings.Sounds.GetValueOrDefault(c.Item1) == key).Select(c => $"[{c.Item2}]"));
            canvas.Text(uses, lx + 60, ry + 3, 110, 20, 12, Canvas.Gold);
            canvas.Text(settings.SoundMemos.GetValueOrDefault(key) ?? "", lx + 170, ry + 3, lw - 176, 20, 13, Canvas.White);
            if (hover && canvas.Pointer.Clicked)
            {
                (_soundIndex, _memoEditing) = (i, false);
                PlaySound?.Invoke(key);
            }
        }
        float my = y + 96 + shown * 26 + 8;
        string picked = $"{_soundBank}:{_soundIndex}";
        // 메모 칸 — 누르고 글자를 친다(Enter 로 마친다)
        canvas.Text("메모", lx, my + 3, 44, 20, 13, Canvas.Dim);
        bool overMemo = canvas.Hover(lx + 44, my, lw - 44, 26);
        if (canvas.Pointer.Clicked)
        {
            if (_memoEditing && !_memoBank && !overMemo) { _memoEditing = false; voyage.Data.SaveSettings(); }
            if (overMemo && count > 0) (_memoEditing, _memoBank) = (true, false);
        }
        bool noting = _memoEditing && !_memoBank;
        canvas.Fill(lx + 44, my, lw - 44, 26, new Color4(0.02f, 0.04f, 0.14f, 0.95f));
        canvas.Frame(lx + 44, my, lw - 44, 26, noting ? Canvas.Gold : Canvas.PanelEdge, 1.2f);
        string memo = settings.SoundMemos.GetValueOrDefault(picked) ?? "";
        canvas.Text(memo == "" && !noting ? "누르고 적는다 (예: 돛 펴는 소리)" : memo + (noting && (int)(voyage.Clock * 2) % 2 == 0 ? "|" : ""), lx + 50, my + 3, lw - 56, 20, 13, memo == "" && !noting ? Canvas.Dim : Canvas.White);
        // 넷째 줄은 ◀ ▶ 로 고르는 그 밖의 소리 — 재해마다의 소리도 여기서 맨다(안 매면 재해는 경고 소리로 난다)
        var disasterCues = voyage.Data.Disasters.Select(d => ($"Disaster{d.Id}", d.Name));
        (string Cue, string Label)[] more = [.. disasterCues, ("Storm", "폭풍"), ("Warn", "경고"), ("Click", "누름"), ("Quest", "퀘스트"), ("Error", "오류"), ("Done", "완료"), ("SkillUp", "레벨업"), ("Mastery", "숙련도"),
            ("Eat", "음식"), ("Door", "입구"), ("Open", "문 열기"), ("Buy", "구매"), ("Bank", "저금"), ("Part", "부품"), ("Drunk", "술"), ("StudyDone", "연구 완료"), ("University", "대학")];
        _moreCue = (_moreCue % more.Length + more.Length) % more.Length;
        (string Cue, string Label)[] cues = [("Turn", "선회"), ("Sail", "돛 조종"), ("Skill", "그 밖의 스킬"), more[_moreCue]];
        if (canvas.Button("◀", lx - 2, my + 36 + 3 * 30, 22, 25, true, 11)) _moreCue--;
        if (canvas.Button("▶", lx + 22, my + 36 + 3 * 30, 22, 25, true, 11)) _moreCue++;
        for (int i = 0; i < cues.Length; i++)
        {
            float cy = my + 36 + i * 30;
            canvas.Text($"{cues[i].Label}: {(settings.Sounds.GetValueOrDefault(cues[i].Cue) is { Length: > 0 } now ? now : "없음")}", lx + (i == 3 ? 50 : 0), cy + 3, 170 - (i == 3 ? 50 : 0), 22, 13, Canvas.White);
            if (canvas.Button($"{picked} 로", lx + 176, cy, 96, 25, count > 0, 12)) { settings.Sounds[cues[i].Cue] = picked; voyage.Data.SaveSettings(); }
            if (canvas.Button("듣기", lx + 276, cy, 50, 25, true, 12) && settings.Sounds.GetValueOrDefault(cues[i].Cue) is { Length: > 0 } set) PlaySound?.Invoke(set);
            if (canvas.Button("끔", lx + 330, cy, 40, 25, true, 12)) { settings.Sounds[cues[i].Cue] = ""; voyage.Data.SaveSettings(); }
        }
        if (canvas.Button("뒤로", sx + sw - 116, y + 8, 100, 28)) { (_soundOpen, _memoEditing) = (false, false); voyage.Data.SaveSettings(); }
    }
    private int _soundTop, _bankTop;
    private bool _memoEditing, _memoBank, _soundLeft;       // _memoBank = 적는 것이 묶음 제목인가, _soundLeft = 포인터가 묶음 목록 위인가
    private bool MemoTyping => _displayOpen && _soundOpen && _memoEditing;
    private bool _soundOpen;
    private int _soundBank, _soundIndex;
    /// <summary>효과음을 틀고 묶음의 소리 수를 묻는 것 — 창이 넣어 준다.</summary>
    public Action<string>? PlaySound;
    public Func<int, int>? SoundCount;

    // ── 왼쪽 위 ──────────────────────────────────────────────────────────────

    private void Status()
    {
        if (voyage.Paused)
        {
            canvas.Fill(canvas.Width / 2 - 110, 60, 220, 40, new Color4(0.02f, 0.04f, 0.14f, 0.85f));
            canvas.Text("일시정지 (P)", canvas.Width / 2 - 110, 66, 220, 30, 20, Canvas.Gold, 1, true);
        }
        if (voyage.Mode == Mode.Port)
        {
            canvas.Text(voyage.TownView && voyage.Interior != 0 ? (voyage.InteriorName.StartsWith(voyage.City.Name) ? voyage.InteriorName : $"{voyage.City.Name} {voyage.InteriorName}") : voyage.TownView ? $"{voyage.City.Name} 시내" : $"{voyage.City.Name} 항구", 10, 6, 400, 28, 19, Canvas.White);
            // 도시의 갈래와 소속 — 이름 오른쪽에 작게
            if (!(voyage.TownView && voyage.Interior != 0))
                canvas.Text(Voyage.CityKindName(voyage.City) + (voyage.Data.Nations.Find(n => n.Id == voyage.City.Nation) is { } owner ? $" · {owner.Name}" : "") + (voyage.CultureName(voyage.City) is { Length: > 0 } culture ? $" · {culture}" : ""), 14 + (voyage.City.Name.Length + 3) * 19.5f, 13, 260, 20, 12, Canvas.Gold);
            canvas.Text($"{voyage.Money:N0} Ð", 30, 34, 300, 24, 17, Canvas.White);
            canvas.Text(voyage.PlayerName, 44, 60, 300, 24, 17, Canvas.White);
            Bars(44, 90);
        }
        else
        {
            // 원본처럼: 타륜 안에 항해일수, 옆에 소지금, 아래에 이름과 막대 셋(배 · 선원 · 식량 그림)
            // 타륜은 작게(전의 0.62 배) — 화면을 덜 가린다
            const float wx = 26, wy = 28, wr = 17;
            canvas.Circle(wx, wy, wr, new Color4(0.08f, 0.10f, 0.24f, 0.95f));
            for (int k = 0; k < 8; k++)
            {
                float a = k * MathF.PI / 4;
                canvas.Line(wx + MathF.Cos(a) * 10, wy + MathF.Sin(a) * 10, wx + MathF.Cos(a) * 21, wy + MathF.Sin(a) * 21, new Color4(0.62f, 0.45f, 0.22f, 1), 2.5f);
                canvas.Circle(wx + MathF.Cos(a) * 21, wy + MathF.Sin(a) * 21, 2, new Color4(0.72f, 0.55f, 0.28f, 1));
            }
            canvas.Circle(wx, wy, wr, new Color4(0.72f, 0.55f, 0.28f, 1), false, 2.5f);
            canvas.Circle(wx, wy, 11, new Color4(0.04f, 0.06f, 0.18f, 1));
            canvas.Circle(wx, wy, 11, new Color4(0.85f, 0.85f, 0.9f, 1), false, 1);
            canvas.Text($"{voyage.DaysAtSea}", wx - 20, wy - 9, 40, 20, 13, Canvas.White, 1, true);
            canvas.Image("gm0:311", () => (_markParts ??= new UiParts(0)).Pixels(311), 56, 18, 20, 20);
            canvas.Text($"{voyage.Money:N0} Ð", 80, 15, 300, 26, 18, Canvas.White, 0, true);
            // 보급품 아이콘 — 원본처럼 항해일수 오른쪽, 돈 위에 아이콘만 선다(사용자, 2026-10-07): 실은 것이 있으면 물병(265) · 빵(266).
            // 열흘 치 아래로 떨어지면 깜박인다(깜박임은 지은 것)
            // 아이콘의 수 = 남은 날수(하루 먹는 양으로 나눈 것, 열 개까지) — 날이 갈수록 하나씩 준다. 마우스를 올리면 남은 양이 뜬다.
            // 「아이콘 하나 = 하루분」은 원본 화면 몇 장(물 넷 · 빵 하나 따위)을 보고 한 짐작이다. 이틀분 아래면 깜박인다(깜박임은 지은 것)
            // 열흘분이 모이면 큰 아이콘 하나로 합쳐진다(사용자, 2026-10-07) — 큰 것은 같은 물병 · 빵을 크게 그린 것(사용자: 「큰물 큰빵이 되는거」). 「열 개가 하나로」와 큰 것의 크기(22)는 짐작
            (int Icon, int Big, double Left, string Name)[] stores = [(265, 265, voyage.Water, "물"), (266, 266, voyage.Food, "식량")];
            float storeX = 58;
            foreach (var (storeIcon, bigIcon, left, storeName) in stores)
            {
                double days = left / voyage.RationPerDay;
                int whole = left <= 0 ? 0 : Math.Max(1, (int)Math.Ceiling(days)), bigs = Math.Min(9, whole / 10), pips = bigs + (whole >= 100 ? 0 : whole % 10);
                bool blink = days < 2 && (int)(voyage.Clock * 2) % 2 == 0;
                float from = storeX;
                for (int k = 0; k < pips; storeX += k < bigs ? 19 : 13, k++)
                    {
                    int shown = k < bigs ? bigIcon : storeIcon;
                    float side = k < bigs ? 22 : 16;
                    if (!blink) canvas.Image($"gm0:{shown}", () => (_markParts ??= new UiParts(0)).Pixels(shown), storeX, k < bigs ? -3 : 1, side, side);
                }
                if (pips > 0 && canvas.Hover(from, 0, storeX - from + 4, 18)) _tip = ($"{storeName} {left:0} — {days:0.#}일분", 330, 44);      // 아이콘 · 돈 · 이름을 가리지 않게 돈 오른쪽에
                storeX += 8;
            }
            canvas.Text(voyage.PlayerName, 44, 54, 260, 24, 17, Canvas.White);
            bool storm = voyage.Weather == Weather.Storm;
            canvas.Text($"{voyage.SeaName}  " + (voyage.Weather == Weather.Clear ? "☀" : storm ? "⚡" : "☁") + voyage.WeatherName, 44, 144, 300, 22, 14,
                        storm ? new Color4(1f, 0.45f, 0.4f, 1) : Canvas.Gold);
            // 그 바다의 바람과 해류(해역마다 다르다)
            canvas.Text(voyage.WindName + "노트" + (voyage.CurrentName == "" ? "" : "   " + voyage.CurrentName + "노트") + $"   돛 {voyage.WindShare}%" + (voyage.DelegateTo is { } bound ? $"   {(voyage.DelegateSpecial ? "특별 위임" : "위임")}: {bound.Name}" : ""), 44, 164, 460, 20, 13, Canvas.White);      // 돛 %: 지금 뱃머리에서 돛이 받는 바람
            if (voyage.QuestSeaNote() is { Length: > 0 } questNote) canvas.Text(questNote, 44, voyage.WreckAt != null || voyage.TowValue > 0 || voyage.WreckPieces > 0 ? 204 : 184, 400, 20, 13, Canvas.Gold);
            // 알아낸 침몰선의 방향과 거리, 모으는 중이면 조각지도 수
            if (voyage.WreckAt != null || voyage.TowValue > 0) canvas.Text(voyage.WreckNote(), 44, 184, 360, 20, 13, new Color4(0.6f, 0.9f, 1f, 1));
            if (voyage.SeaJob != null) canvas.Text(voyage.SeaJobNote(), 44, voyage.WreckAt != null || voyage.TowValue > 0 ? 244 : 224, 400, 20, 13, new Color4(1f, 0.75f, 0.6f, 1));
            else if (voyage.WreckPieces > 0) canvas.Text($"침몰선 조각지도 {voyage.WreckPieces} / {Voyage.WreckPiecesNeeded}", 44, 184, 360, 20, 12, Canvas.Dim);
            int[] icons = [301, 330, 232];
            for (int k = 0; k < 3; k++)
            {
                int icon = icons[k];
                canvas.Image($"gm0:{icon}", () => (_markParts ??= new UiParts(0)).Pixels(icon), 44, 78 + k * 20, 18, 18);
            }
            Bars(68, 84);
            Disasters();
        }
    }

    // 행동력 — 이름 옆의 하늘빛 막대와 숫자
    private void VigourBar(float x, float y)
    {
        canvas.Fill(x, y + 4, 90, 6, new Color4(0.03f, 0.05f, 0.16f, 0.9f));
        canvas.Fill(x, y + 4, 90 * (float)Math.Clamp(voyage.Vigour / Math.Max(1, voyage.MaxVigour), 0, 1), 6, new Color4(0.35f, 0.8f, 1f, 1));
        canvas.Text($"행동력 {voyage.Vigour:0}/{voyage.MaxVigour}", x + 96, y - 4, 150, 20, 13, Canvas.White);
    }

    /// <summary>막대 셋 — 내구(빨강), 선원과 피로(초록 위에 노랑), 행동력(분홍 — 원본의 맨 아래 막대). 물 · 식량은 그 옆에 숫자로.</summary>
    private void Bars(float x, float y)
    {
        var rules = voyage.Data.Settings.Voyage;
        Bar(x, y, (float)(voyage.Durability / voyage.Stats.Durability), new Color4(0.90f, 0.35f, 0.40f, 1));
        Bar(x, y + 20, (float)(voyage.Crew / voyage.Stats.MaxCrew), new Color4(0.45f, 0.80f, 0.35f, 1));
        canvas.Fill(x, y + 24, 122 * (float)(voyage.Fatigue / 100), 2, new Color4(1f, 0.85f, 0.2f, 1));
        Bar(x, y + 40, (float)(voyage.Vigour / Math.Max(1, voyage.MaxVigour)), new Color4(0.80f, 0.40f, 0.85f, 1));
        canvas.Text($"내구 {voyage.Durability:0}", x + 128, y - 7, 200, 18, 12, Canvas.Dim);
        canvas.Text($"선원 {voyage.Crew:0}  피로 {voyage.Fatigue:0}", x + 128, y + 13, 200, 18, 12, Canvas.Dim);
        canvas.Text($"행동력 {voyage.Vigour:0}/{voyage.MaxVigour}   물 {voyage.Water:0}  식량 {voyage.Food:0}", x + 128, y + 33, 260, 18, 12, Canvas.Dim);
    }

    private void Bar(float x, float y, float fill, Color4 color)
    {
        canvas.Fill(x, y, 122, 6, new Color4(0.1f, 0.1f, 0.15f, 0.8f));
        canvas.Fill(x, y, 122 * Math.Clamp(fill, 0, 1), 6, color);
    }

    /// <summary>벌어진 재해와, 실어 둔 보급품으로 대처하는 단추.</summary>
    private void Disasters()
    {
        float y = voyage.WreckAt != null || voyage.TowValue > 0 || voyage.WreckPieces > 0 ? 208 : 188;      // 바람 · 해류 줄(과 침몰선 줄) 밑
        foreach (var disaster in voyage.Disasters.ToList())
        {
            canvas.Fill(4, y, 150, 26, new Color4(0.45f, 0.08f, 0.08f, 0.85f));
            canvas.Text(voyage.DisasterName(disaster.Data), 10, y + 2, 140, 22, 15, Canvas.White);
            int supplyId = disaster.Data.CureSupply;
            // 그 재해를 푸는 소지품(소화모래 · 라임주스 · 쥐약)을 갖고 있으면 그것이 먼저다 — 보급품(모래주머니 따위)이 없어도 쓴다
            var owned = voyage.Data.Items.Find(i => i.Effect == "Cure" && (int)i.Amount == disaster.Data.Id && voyage.Items.GetValueOrDefault(i.Id) > 0);
            if (owned != null && (supplyId == 0 || voyage.SupplyCount(supplyId) <= 0))
            {
                if (canvas.Button($"{owned.Name} 쓰기 ({voyage.Items.GetValueOrDefault(owned.Id)})", 160, y, 190, 26, voyage.Dialog == Dialog.None, 13)) voyage.UseItem(owned.Id);
            }
            else if (supplyId != 0 && voyage.Data.Supplies.Find(s => s.Id == supplyId) is { } supply)
            {
                int count = voyage.SupplyCount(supplyId);
                if (canvas.Button($"{supply.Name} 쓰기 ({count})", 160, y, 190, 26, count > 0 && voyage.Dialog == Dialog.None, 13))
                    voyage.Cure(disaster);
            }
            else if (voyage.CureSkill(disaster.Data) is { } rule &&
                     canvas.Button($"{voyage.SkillName(rule.SkillId)} 쓰기 (R{voyage.Rank(rule.SkillId)})", 160, y, 190, 26, voyage.Dialog == Dialog.None, 13))
                voyage.CureWithSkill(disaster);
            // 그 재해를 푸는 소지품(items.json 의 Cure — 해초의 예비키 따위)
            else if (voyage.Data.Items.Find(i => i.Effect == "Cure" && (int)i.Amount == disaster.Data.Id) is { } remedy && voyage.CureSkill(disaster.Data) == null)
            {
                int have = voyage.Items.GetValueOrDefault(remedy.Id);
                if (canvas.Button($"{remedy.Name} 쓰기 ({have})", 160, y, 190, 26, have > 0 && voyage.Dialog == Dialog.None, 13)) voyage.UseItem(remedy.Id);
            }
            y += 30;
        }
        if (voyage.RestSkill is { } rest && voyage.Fatigue >= 20 &&
            canvas.Button($"{voyage.SkillName(rest.SkillId)} (식량 5)", 4, y, 150, 26, voyage.Food >= 5 && voyage.Dialog == Dialog.None, 13))
            voyage.Feast();
    }
    // ── 왼쪽 아래: 기록 ──────────────────────────────────────────────────────

    private (float X, float Y, float W, float H) _logArea;
    private int _logBack, _logCount, _swapTop;
    private bool _logHover;

    private readonly List<string> _logLines = [];

    /// <summary>
    /// 글을 너비에 맞춰 줄로 나눈다 — 글자 너비는 어림이다(한글 · 한자 · 전각 기호는 글자 크기만큼, 그 밖은 0.56배).
    /// 띄어쓰기에서 끊고, 끊을 데가 없으면 글자에서 끊는다.
    /// </summary>
    private static List<string> Wrapped(string text, float width, float size)
    {
        var lines = new List<string>();
        static float Wide(char c, float size) => c >= 0x1100 ? size * 0.97f : c == ' ' ? size * 0.3f : size * 0.56f;
        foreach (string part in text.Split('\n'))
        {
            int start = 0, space = -1;
            float used = 0;
            for (int i = 0; i < part.Length; i++)
            {
                if (part[i] == ' ') space = i;
                used += Wide(part[i], size);
                if (used <= width) continue;
                int cut = space > start ? space : i;
                lines.Add(part[start..cut]);
                start = space > start ? space + 1 : i;
                (i, space, used) = (start - 1, -1, 0);
            }
            lines.Add(part[start..]);
        }
        return lines;
    }

    private int _logTab;
    private (float X, float Y, float W, float H) _dialogRect;
    private static readonly string[] ShipWords = ["건조", "조선", "강화", "선박", "부두", "옵션 스킬", "조합", "그레이드", "선체", "재질", "갈아", "달았다", "떼었다", "매어", "지어졌", "조타", "도료", "대포", "장갑", "보조돛"];
    private static bool AboutShips(string entry) => Array.Exists(ShipWords, entry.Contains) && !(entry.Contains("의뢰") || entry.Contains("조합 마스터"));      // 「모험가 조합」의 줄은 뺀다

    private void LogPanel()
    {
        const float w = 620, h = 150;
        float x = 4, y = canvas.Height - h - 4;
        // 쪽지: 전체 · 선박(건조 · 강화 · 조합 · 부품처럼 배에 얽힌 줄만)
        string[] logTabs = ["전체", "선박"];
        for (int t = 0; t < logTabs.Length; t++)
        {
            bool on = _logTab == t;
            canvas.Fill(x + t * 106, y - 26, 104, 26, on ? Canvas.PanelFill : new Color4(0.05f, 0.08f, 0.22f, 0.75f));
            canvas.Frame(x + t * 106, y - 26, 104, 26, Canvas.PanelEdge, 1);
            canvas.Text(logTabs[t], x + t * 106, y - 25, 104, 24, 16, on ? Canvas.White : Canvas.Dim, 1, on);
            if (!on && canvas.Hover(x + t * 106, y - 26, 104, 26) && canvas.Pointer.Clicked) { (_logTab, _logCount, canvas.Pointer.Consumed, canvas.Pressed) = (t, -1, true, true); _logLines.Clear(); }
        }
        canvas.PanelId = "WndLog";
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y - 26, w, h + 26);

        const int lines = 5;
        _logArea = (x, y - 26, w, h + 26);
        _logHover = canvas.Hover(x, y - 26, w, h + 26);
        // 긴 글은 창 너비에 맞춰 여러 줄로 나눠 둔다(아래 도움말 줄로 넘치지 않게) — 기록이 늘 때만 다시 나눈다
        if (voyage.LogSerial != _logCount)
        {
            (_logCount, _logBack) = (voyage.LogSerial, 0);          // 새 줄이 오면 맨 아래로
            _logLines.Clear();
            foreach (string entry in voyage.Log.Skip(Math.Max(0, voyage.Log.Count - 400)).Where(e => _logTab == 0 || AboutShips(e))) _logLines.AddRange(Wrapped(entry, w - 34, 16));
        }
        if (_logLines.Count == 0 && _logTab == 1) canvas.Text("배에 얽힌 기록이 아직 없다.", x + 10, y + 8, w - 14, 24, 14, Canvas.Dim);
        _logBack = Math.Clamp(_logBack, 0, Math.Max(0, _logLines.Count - lines));
        int first = Math.Max(0, _logLines.Count - lines - _logBack);
        for (int i = first; i < Math.Min(_logLines.Count, first + lines); i++)
            canvas.Text(_logLines[i], x + 10, y + 8 + (i - first) * 24, w - 14, 24, 16, Canvas.White);
        if (_logLines.Count > lines)
        {
            // 굴림 막대 — 휠로 지난 기록을 본다
            float barH = lines * 24 - 10, thumb = Math.Max(14, barH * lines / _logLines.Count);
            float at = (barH - thumb) * first / Math.Max(1, _logLines.Count - lines);
            canvas.Fill(x + w - 9, y + 6, 5, barH, new Color4(0.03f, 0.05f, 0.16f, 0.9f));
            canvas.Fill(x + w - 9, y + 6 + at, 5, thumb, new Color4(0.72f, 0.76f, 0.84f, 0.95f));
        }

        canvas.Line(x, y + h - 26, x + w, y + h - 26, Canvas.PanelEdge, 1);
        string keys = voyage.Mode == Mode.Port
            ? "마우스 오른쪽 끌기: 시점   휠: 거리"
            : "W/S: 돛   A/D: 키   바다 클릭: 그쪽으로   F: 입항·상륙";
        canvas.Text(keys, x + 10, y + h - 24, w - 20, 22, 13, Canvas.Dim);
    }

    // ── 오른쪽 아래 ──────────────────────────────────────────────────────────

    private ImageSet? _commandIcons;

    /// <summary>
    /// 원본 명령 단추 — <c>0010\0001\sg</c>(40 × 24, 오른쪽 위 줄의 캐릭터 · 선박만 50 × 24). 세 장이 한 벌이다: 평소(잿빛 유리) · 가리킴(청록) · 눌림.
    /// <paramref name="icon"/> 은 평소 장의 id. 못 누르는 단추는 평소 장을 흐리게 그린다.
    /// </summary>
    private bool Command(int icon, float x, float y, bool enabled, string label, float scale = 1, float width = 40)
    {
        float w = width * scale, h = 24 * scale;
        bool hover = enabled && canvas.Hover(x, y, w, h);
        int id = icon + (hover ? 1 : 0);
        if (!canvas.Image($"sg{id}", () => (_commandIcons ??= new ImageSet(@"0010\0001\sg")).Pixels(0, id), x, y, w, h, 0, false, enabled ? 1 : 0.4f))
            return canvas.Button(label, x, y, w, h, enabled, 11);
        if (canvas.Hover(x, y, w, h)) _tip = (label, x + w / 2, y);
        if (!hover || !canvas.Pointer.Clicked) return false;
        (canvas.Pointer.Consumed, canvas.Pressed) = (true, true);
        return true;
    }

    /// <summary>그림 단추의 배율 — 환경설정의 「아이콘 배율」.</summary>
    private float IconScale => (float)Math.Clamp(voyage.Data.Settings.IconScale, 0.5, 2.5);

    private int _moreCue;
    private (string Text, float X, float Y)? _tip;

    /// <summary>가리킨 단추의 이름 — 다 그린 뒤 맨 위에 띄운다.</summary>
    private (string Title, string Note, float X, float Y, float W, float H)? _itemTip;

    private void Tip()
    {
        if (_itemTip is { } card)
        {
            _itemTip = null;
            canvas.Fill(card.X, card.Y, card.W, card.H, new Color4(0.02f, 0.04f, 0.14f, 0.96f));
            canvas.Frame(card.X, card.Y, card.W, card.H, Canvas.Gold, 1.2f);
            canvas.Text(card.Title, card.X + 10, card.Y + 6, card.W - 20, 22, 15, Canvas.Gold, 0, true);
            canvas.Text(card.Note, card.X + 10, card.Y + 32, card.W - 20, card.H - 36, 13, Canvas.White);
        }
        if (_tip is not { } tip) return;
        _tip = null;
        // 글에 맞춘 너비(한글은 넓게, 영문 · 숫자는 좁게)와 줄 수 — 여러 줄이면 첫 줄은 이름, 나머지는 작게
        var tipLines = tip.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        float Wide(string s, float size) => s.Sum(c => c >= 0x1100 ? size : c == ' ' ? size * 0.32f : size * 0.58f);
        float w = Math.Max(Wide(tipLines[0], 15), tipLines.Skip(1).Select(l => Wide(l, 12)).DefaultIfEmpty(0).Max()) + 18;
        float tall = 24 + (tipLines.Length - 1) * 17, x = Math.Clamp(tip.X - w / 2, 4, canvas.Width - w - 4), top = Math.Max(2, tip.Y - tall - 4);
        canvas.Fill(x, top, w, tall, new Color4(0.02f, 0.04f, 0.14f, 0.94f));
        canvas.Frame(x, top, w, tall, new Color4(0.45f, 0.5f, 0.7f, 0.9f), 1);
        canvas.Text(tipLines[0], x, top + 1, w, 22, 15, Canvas.White, 1);
        for (int k = 1; k < tipLines.Length; k++) canvas.Text(tipLines[k], x, top + 6 + k * 17, w, 16, 12, Canvas.Gold, 1);
    }

    private void PortPanel()
    {
        canvas.PanelId = "WndPort";
        // 원본 항구 창처럼 그림 단추를 넉 줄씩 — (꺼짐 장의 id, 이름, 켜짐, 하는 일)
        (int Icon, string Label, bool Enabled, Action Run)[] buttons =
        [
            (244, "출항", true, voyage.Depart),
            (247, "보급", true, () => voyage.Dialog = Dialog.Supply),
            (184, voyage.GuildName, true, () => voyage.Dialog = Dialog.Guild),
            (253, "선박교환", true, () => voyage.Dialog = Dialog.ShipSwap),
            (256, "시내", voyage.City.TownScene != 0, () => voyage.TownView = true),
            (259, "교역소", true, () => voyage.Dialog = Dialog.Trade),
            (265, "조선소", voyage.HasShipyard, () =>
            {
                // 시내가 있고 지도에 조선소가 있으면 시내로 들어가 조선소 앞에 선다(거기서 조선소 주인 차림이 열린다). 없으면 창만 연다
                if (voyage.City.TownScene != 0 && voyage.TownMap?.Marks.Exists(m => m.Place == 9) == true) (voyage.TownView, PendingPlace) = (true, 9);
                else voyage.Dialog = Dialog.ShipyardMenu;
            }),
            (13, "의뢰 내용", voyage.Quest != null || voyage.TradeJob != null || voyage.SeaJob != null, () => voyage.Dialog = Dialog.QuestDetail),
            (154, "스킬 (X)", true, () => voyage.Dialog = Dialog.Skills),
            (43, "주점 (부관)", true, () => voyage.Dialog = voyage.HasTavern ? Dialog.Tavern : Dialog.Aides),
            (208, "왕궁 (칙명)", voyage.AtCourt || voyage.Order != null, () => voyage.Dialog = Dialog.Court),
            (262, "투자", voyage.City.Kind == 2, () => voyage.Dialog = Dialog.Invest),
            (250, "개인농장", voyage.AtFarm, () => voyage.Dialog = Dialog.Farm),
            (187, "대장간", true, () => voyage.Dialog = Dialog.Forge),
            (208, voyage.PermitOffered() is { } permit ? $"집정관 — {permit.Name} 입항 허가 (명성 {voyage.TotalFame:N0} / {permit.Fame:N0})" : "집정관 (입항 허가)", voyage.PermitOffered() is { CityId: not 0 } offered && voyage.TotalFame >= offered.Fame, voyage.TakePermit),
        ];
        // 망명 단추는 망명할 수 있는 곳(다른 나라의 본거지)에서만 선다 — 그림은 명령 단추 묶음의 「달려 나가는 사람」(241), 뇌물은 「선물 상자」(193). 원본에서 이 쓰임인지는 모른다
        // 도시 안에서 찾는 의뢰를 받았고 그 도시에 와 있으면 탐색 단추가 선다
        if (voyage.CitySiteHere) buttons = [.. buttons, (220, $"의뢰 탐색 — {voyage.Quest!.Title}", true, voyage.SearchInCity)];
        // 뇌물 단추는 적대도가 있는 나라의 본거지에서만 선다(원본 글 49134 — 값은 임시)
        if (voyage.BribeHere() is { } bribe) buttons = [.. buttons, (193, $"뇌물 — {bribe.Name} 적대도 −{bribe.Drop} ({bribe.Cost:N0} 두캇, 악명 +5)", voyage.Money >= bribe.Cost, voyage.Bribe)];
        if (voyage.ExileTo is { } host) buttons = [.. buttons, (241, $"망명 — {host.Name}(으)로", true, () => voyage.Dialog = Dialog.Exile)];
        float icon = 1.1f * IconScale;             // 그림 단추는 본디 비율(40 × 24)대로
        float w = Math.Max(212, 18 + 4 * 46 * icon), h = 30 + (buttons.Length + 3) / 4 * 28 * icon + 40;
        float x = canvas.Width - w - 6, y = canvas.Height - h - 6;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text("항구", x + 8, y + 4, 100, 20, 14, Canvas.White);
        canvas.Text(voyage.Ship.Name, x + 8, y + h - 34, w - 12, 18, 10, Canvas.Dim);
        canvas.Text($"Lv 모험 {Voyage.LevelOf(voyage.AdventureExp).Level} · 교역 {Voyage.LevelOf(voyage.TradeExp).Level} · 전투 {Voyage.LevelOf(voyage.BattleExp).Level}", x + 8, y + h - 20, w - 12, 18, 10, Canvas.Dim);
        if (voyage.Dialog != Dialog.None) return;

        for (int i = 0; i < buttons.Length; i++)
            if (Command(buttons[i].Icon, x + 9 + i % 4 * (w - 18) / 4, y + 28 + i / 4 * 28 * icon, buttons[i].Enabled, buttons[i].Label, icon))
                buttons[i].Run();
    }

    /// <summary>시내의 걷는 면과 지금 선 자리 — 창이 프레임마다 넣는다. 없으면 내려다보기만 한다.</summary>
    public TownGrid? TownGrid;
    public System.Numerics.Vector2 TownSpot;

    /// <summary>시내 — 도시가 어떤 곳인지, 들를 수 있는 곳, 걷는 면으로 그린 작은 지도.</summary>
    /// <summary>도시 메뉴를 열었는가(T).</summary>
    public bool TownMenuOpen;

    private void TownPanel()
    {
        var city = voyage.City;
        var buildings = city.Buildings.Split(", ", StringSplitOptions.RemoveEmptyEntries);
        // 아래 단추도 그 시설 앞으로 바로 옮겨 가서 연다. 지도에 그 시설이 없는 도시(또는 방 안)에서는 창만 연다
        void Go(Dialog opens, params int[] kinds)
        {
            var mark = voyage.Interior == 0 && TownGrid != null ? voyage.TownMap?.Marks.Find(m => kinds.Contains(m.Place)) : null;
            if (mark != null && JumpTo != null) JumpTo(mark);
            else voyage.Dialog = opens;
        }
        (string Label, bool Enabled, Action Run)[] buttons =
        [
            (voyage.GuildName, true, () => Go(Dialog.Guild, 1)),
            ("교역소", true, () => Go(Dialog.Trade, 10, 19, 26, 27, 32)),
            ("조선소", voyage.HasShipyard, () => Go(Dialog.ShipyardMenu, 9, 30)),
            ("항구로", true, () => voyage.TownView = false),
        ];
        // 원본 시내 지도가 있는 도시는 그 표식들을 두 줄로 늘어놓는다 — 누르면 그 앞으로 바로 옮겨 가서 일을 연다
        var places = new List<TownMark>();
        if (voyage.Interior == 0 && TownGrid != null)
            foreach (var mark in voyage.TownMap?.Marks ?? [])
                if (!places.Exists(m => voyage.PlaceName(m.Place) == voyage.PlaceName(mark.Place))) places.Add(mark);
        int rows = places.Count > 0 ? (places.Count + 1) / 2 : Math.Max(1, buildings.Length);
        const float w = 300, bw = 136, bh = 30;
        float h = 82 + rows * 22 + 10 + 2 * (bh + 6) + 6;
        float x = canvas.Width - w - 6, y = canvas.Height - h - 6;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text(city.Name, x + 10, y + 6, w - 20, 26, 18, Canvas.Gold, 0, true);
        if (voyage.Dialog == Dialog.None && canvas.Button("날짜 +1", x + w - 76, y + 6, 66, 26, true, 12)) voyage.PassDay();
        if (voyage.Dialog == Dialog.None && canvas.Button("조타숙련 +10", x + w - 176, y + 6, 96, 26, true, 12)) voyage.AddMastery(10);
        canvas.Text(voyage.CityFacts(city).Split('\n').FirstOrDefault(line => !line.StartsWith("특산")) ?? "", x + 10, y + 34, w - 20, 22, 13, Canvas.Dim);      // 교역품은 안 보인다
        canvas.Text("건물", x + 10, y + 58, 100, 20, 13, Canvas.Gold);
        float row = y + 80;
        for (int i = 0; i < places.Count; i++)
        {
            float px = x + 10 + i % 2 * 142, py = row + i / 2 * 22;
            bool hover = voyage.Dialog == Dialog.None && canvas.Hover(px, py, 138, 20);
            if (hover) canvas.Fill(px - 2, py, 138, 20, new Color4(0.22f, 0.32f, 0.66f, 0.9f));
            // 지도의 표식 그림을 이름 앞에
            int mark = MarkIcon(places[i].Place);
            canvas.Image($"gm0:{mark}", () => (_markParts ??= new UiParts(0)).Pixels(mark), px, py + 1, 18, 18);
            canvas.Text(voyage.PlaceName(places[i].Place), px + 22, py, 114, 20, 13, hover ? Canvas.Gold : Canvas.White);
            if (hover && canvas.Pointer.Clicked)
            {
                canvas.Pointer.Consumed = true;
                (JumpTo ?? WalkTo)?.Invoke(places[i]);
                TownMenuOpen = false;
            }
        }
        if (places.Count == 0 && buildings.Length == 0) canvas.Text("들어갈 수 있는 건물이 없다.", x + 10, row, w - 20, 20, 13, Canvas.Dim);
        foreach (string building in places.Count > 0 ? [] : buildings)
        {
            canvas.Text(building, x + 10, row, w - 20, 20, 13, Canvas.White);
            row += 20;
        }
        row = y + h - 2 * (bh + 6) - 6;
        if (voyage.Dialog == Dialog.None)
            for (int i = 0; i < buttons.Length; i++)
                if (canvas.Button(buttons[i].Label, x + 10 + i % 2 * (bw + 8), row + i / 2 * (bh + 6), bw, bh, buttons[i].Enabled, 14))
                    buttons[i].Run();

        TownMapPanel();
    }

    /// <summary>자동 이동 — 창이 넣어 준다.</summary>
    public Action<TownMark>? WalkTo;
    /// <summary>바로 옮겨 가기(도시 메뉴의 건물 목록) — 창이 넣어 준다.</summary>
    public Action<TownMark>? JumpTo;
    /// <summary>시내에 들어서는 대로 그 앞으로 옮겨 갈 시설(장소 번호) — 항구 차림에서 시설을 눌렀을 때. 0 이면 없다.</summary>
    public int PendingPlace;
    /// <summary>시내 지도를 펼쳤는가(Ctrl+5).</summary>
    public bool TownMapOpen;
    public float TownFacing;
    private UiParts? _markParts;

    /// <summary>장소 번호 → 지도 표식 그림(<c>gm000000</c> 의 조각). 번호와 그림의 짝은 눈으로 맞춘 것이다.</summary>
    private static int MarkIcon(int place) => place switch
    {
        4 or 5 or 6 or 7 or 8 or 20 => 484,          // 닻 — 항구 구역
        9 or 30 => 362,                              // 타륜 — 조선소
        10 or 19 or 26 or 27 or 32 => 485,           // 저울 — 교역소
        13 => 486,                                   // 잔 — 주점
        14 or 21 or 22 or 25 => 496,                 // 기둥 집 — 은행
        17 or 28 or 29 => 487,                       // 성문 — 문지기
        201 or 204 => 476, 202 => 478, 203 => 479,   // 교회 · 모스크 · 사원
        >= 301 and < 400 => 483,                     // 왕궁
        _ => 477,                                    // 붉은 지붕 집
    };

    /// <summary>
    /// 원본 시내 지도(오른쪽 위, Ctrl+5 로 여닫는다) — 시설 표식에 마우스를 올리면 이름이 뜨고, 누르면 그리로 걸어간다.
    /// 원본 지도가 없는 도시는 걷는 면으로 그린 지도를 대신 보여 준다.
    /// </summary>
    private void TownMapPanel()
    {
        if (TownGrid is not { } grid || voyage.Interior != 0) return;
        var city = voyage.City;
        if (voyage.TownMap is not { } map)
        {
            // 걷는 면 그림 위에 선 자리
            float mapWidth = 250, mapHeight = mapWidth * grid.Height / grid.Width;
            float gx = canvas.Width - mapWidth - 10, gy = 44;      // 오른쪽 위 단추 줄 아래
            canvas.Fill(gx - 3, gy - 3, mapWidth + 6, mapHeight + 6, Canvas.PanelFill);
            canvas.Image($"town{city.Id}", () =>
            {
                var (pw, ph, rows) = grid.Picture();
                var bgra = new byte[pw * ph * 4];
                for (int py = 0; py < ph; py++)
                for (int px = 0; px < pw; px++)
                {
                    int from = py * (1 + pw * 3) + 1 + px * 3, to = (py * pw + px) * 4;
                    (bgra[to], bgra[to + 1], bgra[to + 2], bgra[to + 3]) = (rows[from + 2], rows[from + 1], rows[from], 255);
                }
                return (pw, ph, bgra);
            }, gx, gy, mapWidth, mapHeight);
            canvas.Frame(gx - 3, gy - 3, mapWidth + 6, mapHeight + 6, Canvas.PanelEdge);
            float dotX = gx + TownSpot.X / (grid.Width * grid.Cell) * mapWidth, dotY = gy + TownSpot.Y / (grid.Height * grid.Cell) * mapHeight;
            canvas.Circle(dotX, dotY, 4.5f, Canvas.White);
            canvas.Circle(dotX, dotY, 3f, new Color4(0.9f, 0.15f, 0.1f, 1f));
            return;
        }

        float scale = TownMapOpen ? 2.5f : 1f;
        float w = TownMap.Width * scale, h = map.Height * scale;
        float mx = canvas.Width - w - (QuickOpen ? 136 : 48), my = 44;      // 오른쪽 위 단추 줄 아래
        canvas.Image($"tm{city.Id}", () => (TownMap.Width, map.Height, (byte[])map.Bgra.Clone()), mx, my, w, h, 0, false, TownMapOpen ? 0.92f : 0.85f);
        canvas.Frame(mx, my, w, h, Canvas.PanelEdge, 1.2f);
        canvas.Block(mx, my, w, h);

        TownMark? pointed = null;
        if (TownMapOpen)
        {
            foreach (var mark in map.Marks)
            {
                float ix = mx + mark.MapX * scale, iy = my + mark.MapY * scale;
                int icon = MarkIcon(mark.Place);
                if (!canvas.Image($"gm0:{icon}", () => (_markParts ??= new UiParts(0)).Pixels(icon), ix - 9, iy - 9, 18, 18))
                    canvas.Circle(ix, iy, 4, Canvas.Gold);
                if (canvas.Hover(ix - 10, iy - 10, 20, 20)) pointed = mark;
            }
        }
        // 내 자리: 초록 세모가 보는 쪽을 가리킨다
        var me = map.ToMap(TownSpot);
        var facing = map.ToMapDirection(new System.Numerics.Vector2(MathF.Sin(TownFacing), MathF.Cos(TownFacing)));
        float px0 = mx + me.X * scale, py0 = my + me.Y * scale;
        if (px0 >= mx && px0 <= mx + w && py0 >= my && py0 <= my + h)
        {
            if (Part(163) != null) PartImage(163, px0 - 8, py0 - 8, 16, 16, MathF.Atan2(facing.X, -facing.Y));
            else canvas.Circle(px0, py0, 4, new Color4(0.2f, 1f, 0.6f, 1));
        }
        if (pointed is { } target)
        {
            _tip = (voyage.PlaceName(target.Place), mx + target.MapX * scale, my + target.MapY * scale - 8);
            if (canvas.Pointer.Clicked && voyage.Dialog == Dialog.None) WalkTo?.Invoke(target);
        }
        if (TownMapOpen) canvas.Text($"{KeyName(KeyOf(voyage.Data.Settings.Keys, "Map"))} 지도 접기 · 표식을 누르면 그리로 간다", mx, my + h + 4, w, 18, 12, Canvas.Dim, 2);
        else canvas.Text($"{KeyName(KeyOf(voyage.Data.Settings.Keys, "Map"))} 지도 펴기", mx - 126, my + 2, 120, 18, 12, Canvas.Dim, 2);      // 접혀 있을 때는 지도 왼쪽에
    }

    private UiParts? _parts;
    private ImageSet? _skillIcons, _goodIcons, _itemIcons;

    /// <summary>
    /// 목록 옆의 스크롤바 — 그리고, 누르거나 끌면 그 자리로 굴린다. <paramref name="top"/> 이 맨 윗줄의 차례.
    /// 줄이 한 화면에 다 들어오면 아무것도 안 한다.
    /// </summary>
    private void ScrollBar(float x, float y, float height, int visible, int count, ref int top)
    {
        int last = count - visible;
        if (last <= 0) { top = 0; return; }
        top = Math.Clamp(top, 0, last);
        float thumb = Math.Max(24, height * visible / count);
        if (canvas.Pointer.Down && canvas.Hover(x - 8, y, 26, height))
            top = Math.Clamp((int)MathF.Round((canvas.Pointer.Y - y - thumb / 2) / Math.Max(1, height - thumb) * last), 0, last);
        canvas.Fill(x, y, 10, height, new Color4(0.03f, 0.05f, 0.16f, 0.9f));
        canvas.Fill(x + 1, y + (height - thumb) * top / last, 8, thumb, new Color4(0.72f, 0.76f, 0.84f, 0.95f));
    }

    /// <summary>배의 작은 그림 — <c>0010\0001\sc</c> 의 무리 1, id 가 배 번호(48 × 48).</summary>
    private bool ShipIcon(int ship, float x, float y, float size) =>
        canvas.Image($"sc1:{ship}", () => (_goodIcons ??= new ImageSet(@"0010\0001\sc")).Pixels(1, ship), x, y, size, size);

    /// <summary>
    /// 재질의 작은 그림 — 같은 묶음의 무리 2(88장). id 는 클라이언트의 재질 번호다(1 삼나무판 · 2 붉은 소나무 · 3 너도밤나무 · 4 엘름 · 6 떡갈나무 · 8 동 · 10 철).
    /// ssjoy 에서 모은 재질 표는 번호를 새로 매겼기 때문에 이름으로 맞추고, 나머지는 차례대로 11 번부터 댄다(짐작이다).
    /// </summary>
    private bool MaterialIcon(Dho.Data.ShipMaterial material, float x, float y, float size)
    {
        // 그 재질의 선박재료 아이템을 알면 그 아이템의 그림(아이템 그림 묶음 무리 22)을 쓴다 — 빛깔이 재질과 맞는다
        if (voyage.Data.MaterialItems.TryGetValue(material.Id, out int item)
            && canvas.Image($"sb22:{item}", () => (_itemIcons ??= new ImageSet(@"0010\0001\sb")).Pixels(22, item), x, y, size, size)) return true;
        int picture = material.Name switch
        {
            "삼나무판" => 1, "붉은 소나무" => 2, "너도밤나무" => 3, "엘름(느릅나무)" => 4, "티크" => 5, "떡갈나무" => 6, "마호가니" => 7, "동" => 8, "자단" => 9, "철" => 10,
            _ => voyage.Data.ShipMaterials.Count > 10 ? Math.Clamp(11 + voyage.Data.ShipMaterials.FindIndex(m => m.Id == material.Id) - 7, 11, 88) : material.Id,
        };
        return canvas.Image($"sc2:{picture}", () => (_goodIcons ??= new ImageSet(@"0010\0001\sc")).Pixels(2, picture), x, y, size, size);
    }
    private readonly Dictionary<int, (int Width, int Height, byte[] Bgra)?> _partPixels = new();
    private readonly byte[] _seaMapPixels = new byte[MapPixels * MapPixels * 4], _surveyPixels = new byte[MapPixels * MapPixels * 4];
    private const int MapPixels = 128;

    /// <summary>화면 부품(<c>gm000002</c>)의 조각 — 한 번 꺼내 두고 다시 쓴다.</summary>
    private (int Width, int Height, byte[] Bgra)? Part(int index)
    {
        if (_partPixels.TryGetValue(index, out var cached)) return cached;
        try { cached = (_parts ??= new UiParts(2)).Pixels(index); }
        catch (Exception) { cached = null; }         // 게임 폴더에 부품이 없어도 지도는 뜬다
        return _partPixels[index] = cached;
    }

    private void PartImage(int index, float x, float y, float w, float h, float angle = 0, float opacity = 1) =>
        canvas.Image($"gm2:{index}", () => Part(index) is { } part ? (part.Width, part.Height, (byte[])part.Bgra.Clone()) : null, x, y, w, h, angle, false, opacity);

    /// <summary>
    /// 주변 지도 — 원본의 둥근 지도. 부품은 원본 것을 쓴다: 흐르는 바다 무늬, 구름, 둥근 가림판,
    /// 초록 세모(내 배). 북이 위다.
    /// </summary>
    private void SeaMap()
    {
        float size = 208 * (float)Math.Clamp(voyage.Data.Settings.SeaMapScale, 0.5, 1.5), radius = size / 2;
        const int seaTile = 154, cloud = 158, mask = 159, softMask = 160, needle = 162, ship = 163;
        // 둥근 지도가 보이는 반지름(세계 좌표) — 측량과 상관없이 늘 뜬다. 배율(환경설정 · 지도 위에서 휠)이 크면 좁게 크게 보인다
        double roundReach = 180 / Math.Clamp(voyage.Data.Settings.SeaMapZoom, 0.25, 4);
        float cx = canvas.Width - radius - 26, cy = canvas.Height - radius - 50;
        bool square = voyage.Data.Settings.SeaMapSquare;      // 환경설정: 네모 지도(원본은 둥글다)
        _seaMapRect = (cx - radius, cy - radius, size);

        // 그림 한 장으로 짓는다: 가림판의 알파 안쪽에 바다·뭍(둥근 지도에는 흐르는 바다 무늬와 구름까지)
        void Paint(byte[] pixels, (int Width, int Height, byte[] Bgra)? shape, double reach, bool lively)
        {
            var waves = lively ? Part(seaTile + (int)(voyage.Clock * 2.5) % 4) : null;
            var clouds = lively ? Part(cloud) : null;
            double step = reach * 2 / MapPixels;
            int drift = (int)(voyage.Clock * 3), slideX = (int)(voyage.ShipX / step), slideY = (int)(voyage.ShipY / step);
            for (int j = 0; j < MapPixels; j++)
            for (int i = 0; i < MapPixels; i++)
            {
                int at = (j * MapPixels + i) * 4;
                float dx = (i + 0.5f - MapPixels / 2f) / (MapPixels / 2f), dy = (j + 0.5f - MapPixels / 2f) / (MapPixels / 2f);
                byte alpha = shape is { } m ? m.Bgra[(j * m.Height / MapPixels * m.Width + i * m.Width / MapPixels) * 4 + 3]
                           : (byte)(dx * dx + dy * dy <= 1 ? 255 : 0);
                if (alpha == 0) { pixels[at + 3] = 0; continue; }

                double wx = voyage.ShipX - reach + (i + 0.5) * step, wy = voyage.ShipY - reach + (j + 0.5) * step;
                float b, g, r;
                if (voyage.Map.IsLand(wx, wy))
                {
                    // 바다와 맞닿은 칸은 모래빛 해안선
                    bool shore = !voyage.Map.IsLand(wx + step, wy) || !voyage.Map.IsLand(wx - step, wy) || !voyage.Map.IsLand(wx, wy + step) || !voyage.Map.IsLand(wx, wy - step);
                    (b, g, r) = shore ? (120f, 190f, 205f) : (78f, 128f, 112f);
                }
                else if (waves is { } w)
                {
                    int p = (((j + slideY) % w.Height + w.Height) % w.Height * w.Width + ((i + slideX) % w.Width + w.Width) % w.Width) * 4;
                    (b, g, r) = (w.Bgra[p], w.Bgra[p + 1], w.Bgra[p + 2]);
                }
                else (b, g, r) = lively ? (150f, 90f, 40f) : (118f, 86f, 52f);      // 측량 지도의 바다는 무늬 없는 잿빛 파랑
                if (clouds is { } c)
                {
                    // 구름은 날씨가 궂을수록 짙다
                    float cover = voyage.Weather switch { Weather.Clear => 0.25f, Weather.Cloudy => 0.6f, _ => 0.85f };
                    float veil = c.Bgra[(((j + slideY + drift / 2) % c.Height + c.Height) % c.Height * c.Width + ((i + slideX + drift) % c.Width + c.Width) % c.Width) * 4 + 3] / 255f * cover;
                    (b, g, r) = (b + (235 - b) * veil, g + (235 - g) * veil, r + (235 - r) * veil);
                }
                (pixels[at], pixels[at + 1], pixels[at + 2], pixels[at + 3]) = ((byte)b, (byte)g, (byte)r, alpha);
            }
        }
        // 도시(빨강)와 의뢰의 상륙지(금빛) 점, 그리고 내 배 — 초록 세모가 뱃머리 쪽을 가리킨다(조각은 위를 본다)
        void Marks(float mx, float my, float half, double reach, bool round)
        {
            void Mark(double wx, double wy, Color4 color, float dot)
            {
                double dx = WorldMap.DeltaX(voyage.ShipX, wx) / reach, dy = (wy - voyage.ShipY) / reach;
                if (round && !square ? dx * dx + dy * dy > 0.94 : Math.Max(Math.Abs(dx), Math.Abs(dy)) > (round ? 0.96 : 0.9)) return;
                canvas.Circle(mx + (float)dx * half, my + (float)dy * half, dot + 1, new Color4(0, 0, 0, 0.7f));
                canvas.Circle(mx + (float)dx * half, my + (float)dy * half, dot, color);
            }
            foreach (var city in voyage.Data.Cities) Mark(city.SeaX, city.SeaY, round ? new Color4(1f, 0.35f, 0.3f, 1) : Canvas.White, round ? 3 : 2);
            if (voyage.QuestStage == QuestStage.Accepted && voyage.QuestLanding is { X: not 0 } site)
                Mark(site.X, site.Y, Canvas.Gold, 4);
            if (voyage.QuestStage == QuestStage.Accepted && voyage.Quest is { SeaX: > 0 } seaSpot)
                Mark(seaSpot.SeaX, seaSpot.SeaY, Canvas.Gold, 4);
            // 위임 항해의 남은 길 — 작은 흰 점을 띄엄띄엄
            if (voyage.DelegateTo != null)
                for (int k = voyage.DelegateRouteAt; k < voyage.DelegateRoute.Count; k += 2)
                    Mark(voyage.DelegateRoute[k].X, voyage.DelegateRoute[k].Y, new Color4(1f, 1f, 1f, 0.85f), 1);
            // 알아낸 침몰선 — 하늘빛 점. 지도 밖이면 그쪽 가장자리에 붙는다(어느 쪽인지 보이게)
            if (voyage.WreckAt is { } wreck)
            {
                double wdx = WorldMap.DeltaX(voyage.ShipX, wreck.X) / reach, wdy = (wreck.Y - voyage.ShipY) / reach, far = Math.Sqrt(wdx * wdx + wdy * wdy);
                double keep = round ? 0.9 : 0.85, shrink = far > keep ? keep / far : 1;
                if (round && square) shrink = Math.Min(1, 0.92 / Math.Max(0.001, Math.Max(Math.Abs(wdx), Math.Abs(wdy))));
                canvas.Circle(mx + (float)(wdx * shrink) * half, my + (float)(wdy * shrink) * half, 4.5f, new Color4(0, 0, 0, 0.7f));
                canvas.Circle(mx + (float)(wdx * shrink) * half, my + (float)(wdy * shrink) * half, 3.5f, new Color4(0.4f, 0.9f, 1f, 1));
            }
            // 자리를 아는 상륙지 — 작은 초록 점(둥근 지도에만)
            if (round)
                foreach (var shore in voyage.Data.Landings)
                    if (shore.X != 0 || shore.Y != 0) Mark(shore.X, shore.Y, new Color4(0.55f, 0.9f, 0.5f, 1), 1.5f);
            // 다른 배 — 해적은 붉은 점, 제 나라 배는 푸른 점, 그 밖은 흰 점(둥근 지도에만)
            if (round)
                foreach (var other in voyage.SeaShips)
                    Mark(other.X, other.Y, other.Kind is 1 or 3 ? new Color4(1f, 0.2f, 0.2f, 1) : other.NationId == voyage.NationId ? new Color4(0.5f, 0.75f, 1f, 1) : Canvas.White, 2);
            float heading = (float)voyage.Heading;
            if (Part(ship) != null) PartImage(ship, mx - 8, my - 8, 16, 16, heading);
            else canvas.Line(mx, my, mx + MathF.Sin(heading) * 12, my - MathF.Cos(heading) * 12, Canvas.White, 2);
        }

        // 둥근 지도 — 늘 뜬다
        Paint(_seaMapPixels, Part(square ? softMask : mask), roundReach, true);      // 네모는 원본 부품의 네모 가림판(가장자리가 부드럽다)
        if (square) canvas.Fill(cx - radius - 5, cy - radius - 5, size + 10, size + 10, new Color4(0.04f, 0.06f, 0.18f, 0.9f));
        else canvas.Circle(cx, cy, radius + 5, new Color4(0.04f, 0.06f, 0.18f, 0.9f));
        canvas.Image("seamap", () => (MapPixels, MapPixels, (byte[])_seaMapPixels.Clone()), cx - radius, cy - radius, size, size, 0, true);
        if (square)
        {
            canvas.Frame(cx - radius - 1, cy - radius - 1, size + 2, size + 2, Canvas.PanelEdge, 2.5f);
            canvas.Frame(cx - radius - 5, cy - radius - 5, size + 10, size + 10, new Color4(0.45f, 0.38f, 0.2f, 1), 1);
        }
        else
        {
            canvas.Circle(cx, cy, radius + 1, Canvas.PanelEdge, false, 2.5f);
            canvas.Circle(cx, cy, radius + 5, new Color4(0.45f, 0.38f, 0.2f, 1), false, 1);
        }
        Marks(cx, cy, radius, roundReach, true);
        // 둘레: 북쪽의 「N」, 바람이 불어 가는 쪽의 흰 화살(세기만큼 길다), 해류가 흘러가는 쪽의 청록 화살 — 지도는 북이 위다
        canvas.Text("N", cx - 10, cy - radius - 3, 20, 16, 12, Canvas.Gold, 1, true);
        void Arrow(double toward, float length, Color4 color, float rim)
        {
            float dx = MathF.Sin((float)toward), dy = -MathF.Cos((float)toward);
            // 불어오는 쪽 가장자리에서 가운데 쪽으로 — 화살촉이 가는 쪽을 가리킨다
            float x0 = cx - dx * (radius * rim), y0 = cy - dy * (radius * rim), x1 = x0 + dx * length, y1 = y0 + dy * length;
            canvas.Line(x0, y0, x1, y1, new Color4(0, 0, 0, 0.5f), 4);
            canvas.Line(x0, y0, x1, y1, color, 2);
            canvas.Line(x1, y1, x1 - dx * 7 - dy * 4, y1 - dy * 7 + dx * 4, color, 2);
            canvas.Line(x1, y1, x1 - dx * 7 + dy * 4, y1 - dy * 7 - dx * 4, color, 2);
        }
        Arrow(voyage.WindDirection, 10 + (float)Math.Clamp(voyage.WindKnots, 0, 25) * 1.1f, new Color4(0.95f, 0.97f, 1f, 0.95f), 0.96f);
        if (voyage.CurrentKnots >= 0.15) Arrow(voyage.CurrentDirection, 10 + (float)Math.Clamp(voyage.CurrentKnots, 0, 2) * 9, new Color4(0.3f, 0.95f, 0.85f, 0.95f), 0.70f);

        // 돛을 펴고 달리는 동안은 「Auto Sailing !!」 — 원본의 자동 돛 조정 표시(화면 부품 묶음 2 의 303, 128 × 24). 우리는 돛 방향을 손으로 맞추지 않으니 늘 자동이다
        if (voyage.Sail > 0 && Part(303) != null) PartImage(303, cx + radius - 150, cy - radius - 36, 154, 29);

        // 측량 지도 — 측량을 켜 두면 둥근 지도 위에 따로 뜬다(원본): 가장자리가 흐린 네모, 왼쪽 위에 나침반 바늘, 오른쪽 위에 좌표
        if (voyage.CanSurvey)
        {
            float side = size * 0.92f, half = side / 2, sx = canvas.Width - side - 22, sy = cy - radius - 44 - side;
            Paint(_surveyPixels, Part(softMask), voyage.SurveyReach, false);
            canvas.Image("surveymap", () => (MapPixels, MapPixels, (byte[])_surveyPixels.Clone()), sx, sy, side, side, 0, true);
            Marks(sx + half, sy + half, half, voyage.SurveyReach, false);
            if (Part(needle) != null) PartImage(needle, sx + 6, sy + 8, 32, 32);
            string spot = $"{voyage.ShipX:0},{voyage.ShipY:0}";
            canvas.Fill(sx + side - spot.Length * 8.2f - 14, sy + 12, spot.Length * 8.2f + 6, 18, new Color4(0, 0, 0, 0.45f));
            canvas.Text(spot, sx, sy + 11, side - 10, 20, 13, Canvas.White, 2);
            canvas.Block(sx, sy, side, side);
        }

        // 지도 밑: 속도와 돛
        canvas.Text($"{voyage.Knots:0.0} 노트", cx - radius, cy + radius + 10, size * 0.5f, 22, 16, Canvas.White, 0, true);
        canvas.Text(SailNames[voyage.Sail], cx, cy + radius + 12, radius, 20, 14, Canvas.Dim, 2);
        // 선원이 필요 선원보다 적으면 그만큼 느려진다 — 까닭을 알려 준다
        if (voyage.Crew < voyage.Stats.MinCrew)
            canvas.Text($"선원 부족 {voyage.Crew:0}/{voyage.Stats.MinCrew} — 속도 {Math.Clamp(voyage.Crew / voyage.Stats.MinCrew, 0.3, 1) * 100:0}%", cx - radius - 60, cy + radius + 32, size + 60, 20, 13, new Color4(1f, 0.5f, 0.45f, 1), 2);
        canvas.Block(cx - radius - 6, cy - radius - 6, size + 12, size + 12);
    }

    /// <summary>퀵슬롯 패널을 펼쳤는가.</summary>
    /// <summary>퀵슬롯으로 스킬 · 아이템을 썼다 — 「고정」을 안 켰으면 닫는다.</summary>
    public void QuickUsed() { if (!voyage.Data.Settings.QuickPin) QuickOpen = false; }
    public bool QuickOpen;                     // 기본은 접힘 — 오른쪽 끝의 단추로 편다
    private int _quickPicked;

    /// <summary>칸 하나 — 스킬은 아이콘(<c>0010\\0001\\sa</c>), 아이템은 이름 두 자와 가진 수. 가리키고 있으면 true.</summary>
    private bool SlotCell(int value, float x, float y, float size, string corner, bool chosen = false)
    {
        bool hover = canvas.Hover(x, y, size, size);
        canvas.Fill(x, y, size, size, new Color4(0.02f, 0.04f, 0.14f, 0.92f));
        canvas.Frame(x, y, size, size, chosen ? Canvas.Gold : hover && value != 0 ? Canvas.White : new Color4(0.35f, 0.45f, 0.75f, 1), chosen ? 2.5f : 1.2f);
        if (value > 0)
        {
            var rule = voyage.Data.SkillRules.Find(r => r.SkillId == value);
            bool ready = rule == null || voyage.SkillBlocker(rule) == null || voyage.Mode != Mode.Sea;
            int id = value;
            float iw = size < 50 ? size * 0.74f : size * 0.6f, ih = iw * 28 / 24;
            canvas.Image($"sa{id}", () => (_skillIcons ??= new ImageSet(@"0010\0001\sa")).Pixels(0, id), x + (size - iw) / 2, y + (size - ih) / 2, iw, ih, 0, false, ready ? 1 : 0.5f);
            double share = rule == null ? 0 : voyage.SkillWaitShare(rule);
            if (share > 0) canvas.Fill(x + 1, y + 1 + (size - 2) * (float)(1 - share), size - 2, (size - 2) * (float)share, new Color4(0, 0, 0, 0.55f));
        }
        else if (value < 0)
        {
            // 아이템 그림 — 0010\0001\sb 의 무리 15, id 가 아이템 번호(48 × 48). 없으면 이름 두 자
            int item = -value;
            float side = size * 0.8f;
            if (!canvas.Image($"sb{item}", () => (_itemIcons ??= new ImageSet(@"0010\0001\sb")).Pixels(15, item), x + (size - side) / 2, y + (size - side) / 2, side, side)
                && !canvas.Image($"sb{item / 100000}:{item}", () => (_itemIcons ??= new ImageSet(@"0010\0001\sb")).Pixels(item / 100000, item), x + (size - side) / 2, y + (size - side) / 2, side, side))
            {
                string name = voyage.ItemName(item);
                canvas.Text(name[..Math.Min(2, name.Length)], x, y + size * 0.22f, size, 24, size * 0.3f, Canvas.White, 1, true);
            }
            canvas.Text($"{voyage.Items.GetValueOrDefault(-value)}", x, y + size - 18, size - 4, 16, 12, Canvas.White, 2, true);
        }
        if (corner != "") canvas.Text(corner, x + 3, y + 1, 16, 16, 12, Canvas.White, 0, true);
        return hover;
    }

    /// <summary>
    /// 퀵슬롯 — 원본처럼 화면 오른쪽 가장자리에 단추 둘(노랑 = 퀵슬롯 여닫기, 하늘 = 스킬 사용 창)이 있고,
    /// 펼치면 그 옆에 두 줄 × 네 칸(왼쪽 1 ~ 4, 오른쪽 5 ~ 8)과 쪽 넘기기(1/3)가 뜬다. 칸을 누르거나 숫자 글쇠로 쓴다.
    /// </summary>
    /// <summary>켜져 있는 스킬 — 원본처럼 화면 오른쪽 가운데 아래에 그림이 늘어서고, 남은 시간만큼 밑줄이 줄어든다. 누르면 끈다.</summary>
    private void ActiveSkills()
    {
        var on = voyage.SkillsOn();
        // 원본의 본디 크기(24 × 28)로 작게, 테두리는 가는 초록 줄 하나. 지도들과 겹치지 않게 그 위에 둔다(측량 지도가 떠 있으면 그것보다 위)
        const float iw = 24, ih = 28, step = 29;
        float map = 208 * (float)Math.Clamp(voyage.Data.Settings.SeaMapScale, 0.5, 1.5);
        float top = canvas.Height - map - 50 - 44 - (voyage.CanSurvey ? map * 0.92f : 0);
        float y = Math.Max(120, top - ih - 14);
        // 급가속(눌러 쓰는 선박 스킬) — 켜진 스킬 그림들 왼쪽에 제 칸. 누르면 쓰고, 켜져 있는 동안 금빛 밑줄, 쉬는 동안은 흐리게
        if (voyage.HasDash)
        {
            float dx = canvas.Width - 30 - iw - on.Count * step - 8;
            double state = voyage.DashState;
            canvas.Fill(dx - 1, y - 1, iw + 2, ih + 6, new Color4(0.02f, 0.05f, 0.12f, 0.55f));
            SkillIcon(2001, dx, y, state < 0 ? 0.4f : 1, iw / 28);
            canvas.Frame(dx - 1, y - 1, iw + 2, ih + 6, state > 0 ? Canvas.Gold : new Color4(0.5f, 0.6f, 0.9f, 0.9f), 1);
            if (state != 0) canvas.Fill(dx, y + ih + 1, iw * (float)Math.Abs(state), 2, state > 0 ? Canvas.Gold : new Color4(0.5f, 0.55f, 0.7f, 1));
            if (canvas.Hover(dx - 1, y - 1, iw + 2, ih + 6))
            {
                _tip = (state > 0 ? "급가속 — 켜져 있다" : state < 0 ? "급가속 — 쉬는 중" : "급가속 — 누르면 쓴다", dx + iw / 2, y - 4);
                if (canvas.Pointer.Clicked && voyage.Dialog == Dialog.None) { canvas.Pointer.Consumed = true; voyage.UseDash(); }
            }
        }
        for (int i = 0; i < on.Count; i++)
        {
            float x = canvas.Width - 30 - iw - (on.Count - 1 - i) * step;
            canvas.Fill(x - 1, y - 1, iw + 2, ih + 6, new Color4(0.02f, 0.05f, 0.12f, 0.55f));
            SkillIcon(on[i].Rule.SkillId, x, y, 1, iw / 28);
            canvas.Frame(x - 1, y - 1, iw + 2, ih + 6, new Color4(0.3f, 0.85f, 0.5f, 0.95f), 1);
            canvas.Fill(x, y + ih + 1, iw * (float)on[i].Left, 2, Canvas.Gold);
            if (canvas.Hover(x - 1, y - 1, iw + 2, ih + 6))
            {
                _tip = (voyage.SkillName(on[i].Rule.SkillId), x + iw / 2, y - 4);
                _tip = (voyage.SkillName(on[i].Rule.SkillId) + " — Ctrl+클릭: 끄기", x + iw / 2, y - 4);
                // 그냥 누르면 다시 쓰고(시간이 처음부터), Ctrl 을 누른 채 누르면 끈다(원본: 사용자, 2026-10-07)
                if (canvas.Pointer.Clicked && voyage.Dialog == Dialog.None) { canvas.Pointer.Consumed = true; if (canvas.Pointer.Ctrl) voyage.StopSkill(on[i].Rule.SkillId); else voyage.UseSkill(on[i].Rule); }
            }
        }
    }

    private void QuickBar()
    {
        float quick = (float)Math.Clamp(voyage.Data.Settings.QuickScale, 0.5, 2.5);      // 환경설정의 퀵슬롯 배율
        float cell = 36 * quick, gap = 3 * quick;   // 글자 없는 칸이라 작게 — 아이콘 본디 크기(24 × 28)에 가깝게
        float round = 32 * IconScale, edge = canvas.Width - round - 8, top = 10 + 24 * 1.1f * IconScale;      // 오른쪽 위 단추 줄 밑
        // 가장자리 단추 — 화면 부품(gm000002)의 둥근 단추 15(노란 화살) · 18(하늘 화살)
        bool Edge(int part, string label, float y)
        {
            bool hover = canvas.Hover(edge, y, round, round);
            if (Part(part) != null) PartImage(part, edge, y, round, round, 0, hover ? 1 : 0.85f);
            else canvas.Button(label[..1], edge, y, round, round, true, 13);
            canvas.Block(edge, y, round, round);
            if (hover) _tip = (label, edge - 30, y + 30);
            if (!hover || !canvas.Pointer.Clicked) return false;
            canvas.Pointer.Consumed = true;
            return true;
        }
        if (Edge(15, "퀵슬롯", top)) QuickOpen = !QuickOpen;
        if (Edge(18, "스킬 (F2)", top + round + 6) && voyage.Dialog is Dialog.None or Dialog.UseSkills)
            voyage.Dialog = voyage.Dialog == Dialog.UseSkills ? Dialog.None : Dialog.UseSkills;
        if (!QuickOpen) return;

        float foot = 24 * Math.Max(1, quick * 0.8f);      // 쪽 넘김 줄
        float pinRow = 22 * Math.Max(1, quick * 0.8f);      // 「고정」 줄
        float w = cell * 2 + gap * 3, h = cell * 4 + gap * 5 + foot + pinRow;
        float x = edge - w - 6, y = top;
        canvas.Fill(x, y, w, h, new Color4(0.04f, 0.07f, 0.22f, 0.9f));
        canvas.Frame(x, y, w, h, new Color4(0.35f, 0.45f, 0.75f, 1), 1.2f);
        canvas.Block(x, y, w, h);
        for (int n = 0; n < Voyage.QuickPageSize; n++)
        {
            int slot = voyage.QuickPage * Voyage.QuickPageSize + n, value = voyage.QuickSlots[slot];
            float cx = x + gap + n / 4 * (cell + gap), cy = y + gap + n % 4 * (cell + gap);
            if (!SlotCell(value, cx, cy, cell, $"{n + 1}") || value == 0) continue;
            _tip = (voyage.QuickSlotName(slot), cx + cell / 2, cy);
            if (canvas.Pointer.Clicked && voyage.Dialog is Dialog.None or Dialog.Items) { canvas.Pointer.Consumed = true; if (canvas.Pointer.Ctrl && value > 0 && voyage.SkillOn(value)) voyage.StopSkill(value); else { voyage.UseQuickSlot(slot); QuickUsed(); } }
        }
        // 「고정」 — 켜 두어야 스킬을 쓴 뒤에도 퀵슬롯이 안 닫힌다(원본의 고정 칸: 사용자, 2026-10-07)
        var quickSettings = voyage.Data.Settings;
        float pinY = y + h - pinRow, box = pinRow - 8;
        canvas.Fill(x + gap, pinY + 3, box, box, new Color4(0.02f, 0.04f, 0.14f, 0.95f));
        canvas.Frame(x + gap, pinY + 3, box, box, Canvas.PanelEdge, 1.2f);
        if (quickSettings.QuickPin) canvas.Text("✔", x + gap, pinY + 1, box, box, box * 0.8f, new Color4(0.5f, 1f, 0.6f, 1), 1, true);
        canvas.Text("고정", x + gap + box + 4, pinY + 2, w - box - gap * 2, box + 2, Math.Min(13, box * 0.85f), Canvas.White);
        if (canvas.Hover(x, pinY, w, pinRow) && canvas.Pointer.Clicked) { canvas.Pointer.Consumed = true; quickSettings.QuickPin = !quickSettings.QuickPin; voyage.Data.SaveSettings(); }
        float py = y + h - pinRow - foot + 1, arrow = foot - 2;
        if (canvas.Button("◀", x + gap, py, arrow, arrow - 2, true, 10)) voyage.TurnQuickPage(-1);
        canvas.Text($"{voyage.QuickPage + 1}/{Voyage.QuickPages}", x, py + (arrow - 18) / 2, w, 18, 12, Canvas.White, 1);
        if (canvas.Button("▶", x + w - gap - arrow, py, arrow, arrow - 2, true, 10)) voyage.TurnQuickPage(1);
    }

    /// <summary>
    /// 퀵슬롯 등록 — 원본의 짜임: 왼쪽에 스킬과 아이템이 늘어서고 오른쪽에 세 쪽의 칸. 왼쪽에서 하나를 고르고 칸 밑의 단추를 누르면 올라간다.
    /// 칸 자체를 누르면 비워진다.
    /// </summary>
    private void QuickSetupWindow()
    {
        const float w = 1010, h = 430, cell = 60;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Fill(x + 20, y + 14, 370, 26, new Color4(0.72f, 0.76f, 0.84f, 0.92f));
        canvas.Text("스킬-아이템", x + 20, y + 16, 370, 22, 15, new Color4(0.05f, 0.08f, 0.2f, 1), 1, true, false);

        var choices = voyage.UsableSkills().Select(r => r.SkillId).Concat(voyage.UsableItemIds().Select(i => -i)).ToList();
        for (int i = 0; i < Math.Min(choices.Count, 25); i++)
        {
            float cx = x + 24 + i % 5 * (cell + 12), cy = y + 54 + i / 5 * (cell + 8);
            if (!SlotCell(choices[i], cx, cy, cell, "", _quickPicked == choices[i])) continue;
            _tip = (choices[i] > 0 ? voyage.SkillName(choices[i]) : voyage.ItemName(-choices[i]), cx + cell / 2, cy);
            if (canvas.Pointer.Clicked) _quickPicked = choices[i];
        }
        if (choices.Count == 0) canvas.Text("올릴 스킬이나 아이템이 없다.\n눌러 쓰는 스킬(측량 · 조달 · 낚시 · 수리 · 주연)을 익히거나 도구점에서 약을 산다.", x + 24, y + 60, 360, 80, 14, Canvas.Dim);
        canvas.Line(x + 404, y + 14, x + 404, y + h - 14, new Color4(0.5f, 0.55f, 0.75f, 0.8f), 1);

        for (int page = 0; page < Voyage.QuickPages; page++)
        {
            float px = x + 420 + page * 196;
            canvas.Fill(px, y + 14, 184, h - 78, new Color4(0.03f, 0.05f, 0.16f, 0.7f));
            for (int n = 0; n < Voyage.QuickPageSize; n++)
            {
                int slot = page * Voyage.QuickPageSize + n;
                float cx = px + 14 + n / 4 * (cell + 36), cy = y + 24 + n % 4 * (cell + 26);
                if (SlotCell(voyage.QuickSlots[slot], cx, cy, cell, $"{n + 1}") && voyage.QuickSlots[slot] != 0)
                {
                    _tip = (voyage.QuickSlotName(slot) + " — 누르면 뺀다", cx + cell / 2, cy);
                    if (canvas.Pointer.Clicked) { canvas.Pointer.Consumed = true; voyage.SetQuickSlot(slot, 0); }
                }
                if (canvas.Button(_quickPicked != 0 ? "▲" : "", cx + 4, cy + cell + 3, cell - 8, 18, _quickPicked != 0, 11)) voyage.SetQuickSlot(slot, _quickPicked);
            }
        }
        canvas.Text(_quickPicked == 0 ? "왼쪽에서 하나를 고른 뒤 칸 밑의 단추를 누른다." : $"고른 것: {(_quickPicked > 0 ? voyage.SkillName(_quickPicked) : voyage.ItemName(-_quickPicked))} — 올릴 칸 밑의 ▲ 를 누른다.",
                    x + 24, y + h - 44, 600, 22, 13, Canvas.Dim);
        if (canvas.Button("확인", x + w - 130, y + h - 50, 110, 34)) (voyage.Dialog, _quickPicked) = (Dialog.None, 0);
    }

    /// <summary>
    /// 스킬 사용 창(F2) — 눌러 쓰는 스킬의 목록. 여기서 바로 쓰거나 퀵슬롯에 올린다.
    /// 창이 열려 있는 동안 퀵슬롯의 칸을 누르면 그 칸이 비워진다.
    /// </summary>
    private void UseSkillWindow()
    {
        const float w = 560;
        var skills = voyage.UsableSkills();
        List<int> items = [];          // 이 창에는 스킬만 선다(사용자, 2026-10-07) — 소비 아이템은 소지품 창과 「퀵슬롯 등록」에서
        float h = 110 + Math.Max(1, skills.Count + items.Count) * 34 + 50;
        float x = (canvas.Width - w) / 2, y = Math.Max(40, (canvas.Height - h) / 2 - 40);
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text("스킬 사용", x + 20, y + 12, w - 40, 26, 18, Canvas.Gold, 0, true);
        canvas.Text("「슬롯」으로 퀵슬롯의 빈 칸에 올린다. 칸을 골라 올리려면 ☰ → 퀵슬롯 등록.", x + 20, y + 42, w - 40, 20, 12, Canvas.Dim);
        float row = y + 72;
        foreach (var rule in skills)
        {
            int id = rule.SkillId;
            string? blocker = voyage.SkillBlocker(rule);
            SkillIcon(id, x + 20, row - 2, 1);
            canvas.Text($"{voyage.SkillName(id)}  R{voyage.Rank(id)}", x + 56, row + 4, 170, 22, 15, Canvas.White);
            canvas.Text(blocker ?? $"행동력 {voyage.VigourCost(rule)}", x + 226, row + 6, 150, 20, 12, blocker == null ? new Color4(0.85f, 0.6f, 0.9f, 1) : Canvas.Dim);
            if (canvas.Button("사용", x + w - 180, row, 70, 28, blocker == null, 13)) voyage.UseSkill(rule);
            if (canvas.Button(voyage.InQuickSlot(id) ? "올림" : "슬롯", x + w - 104, row, 84, 28, !voyage.InQuickSlot(id), 13)) voyage.AddQuickSlot(id);
            row += 34;
        }
        foreach (var item in items)
        {
            canvas.Text($"{voyage.ItemName(item)} × {voyage.Items.GetValueOrDefault(item)}", x + 56, row + 4, 200, 22, 15, Canvas.White);
            canvas.Text(voyage.ItemNote(item).Replace("\n", " "), x + 226, row + 6, 150, 20, 11, Canvas.Dim);
            if (canvas.Button("사용", x + w - 180, row, 70, 28, true, 13)) voyage.UseItem(item);
            if (canvas.Button(voyage.InQuickSlot(-item) ? "올림" : "슬롯", x + w - 104, row, 84, 28, !voyage.InQuickSlot(-item), 13)) voyage.AddQuickSlot(-item);
            row += 34;
        }
        if (skills.Count + items.Count == 0)
            canvas.Text("눌러 쓰는 스킬이 없다. 측량 · 조달 · 낚시 · 수리 · 주연을 익히면 여기에 뜬다(스킬 창 X).", x + 20, row, w - 40, 40, 14, Canvas.Dim);
        if (canvas.Button("닫기", x + w - 130, y + h - 44, 110, 32)) voyage.Dialog = Dialog.None;
    }

    /// <summary>지금 할 수 있는 일 — 입항, 상륙.</summary>
    private void Prompt()
    {
        if (voyage.Dialog != Dialog.None) return;
        string? text = null;
        // 싸우는 동안에는 입항 · 상륙을 못 한다 — 벗어나거나 이겨야 한다
        if (voyage.Battle is { Result: null }) return;
        if (voyage.SiteInReach()) text = $"F : {voyage.QuestLanding!.Name}에 상륙한다";
        else if (voyage.SeaSiteInReach() && voyage.PortInReach() == null) text = $"F : 둘레를 살핀다 — {voyage.Quest!.Title} (행동력 10)";
        else if (voyage.WreckInReach && voyage.PortInReach() == null) text = $"F : 침몰선을 끌어올린다 ({voyage.WreckRaised}%, 행동력 20)";
        else if (voyage.PortInReach() == null && voyage.LandingInReach() is { } shore) text = $"F : {shore.Name}에 상륙한다";
        else if (voyage.PortInReach() is { } port) text = voyage.PermitMissing(port) is { } lacking ? $"{port.Name} — 입항 허가가 없다" : $"F : {port.Name}에 입항한다";
        else if (voyage.ShipInReach() is { } foe) text = $"G : {Voyage.SeaShipKinds[foe.Kind]} 「{foe.Name}」을(를) 공격한다";
        if (text == null) return;

        float w = 360, x = (canvas.Width - w) / 2, y = canvas.Height * 0.72f;
        canvas.Panel(x, y, w, 40);
        canvas.Text(text, x, y + 8, w, 26, 17, Canvas.Gold, 1);
    }

    // ── 가운데 창 ────────────────────────────────────────────────────────────

    private ImageSet? _discoveryImages;

    private void Dialogs()
    {
        // 부두의 배를 강화하던 창이 닫혔으면 타던 배로 돌아간다
        if (voyage.Working && voyage.Dialog is not (Dialog.Strengthen or Dialog.WorkMethod) && !(_infoDocked && voyage.Dialog == Dialog.ShipInfo)) { voyage.EndWork(); _infoDocked = false; }
        // 돛 도료 창이 「확인」 없이 닫혔으면 미리 보던 돛을 되돌린다
        if (voyage.Dialog != Dialog.HullPaint) voyage.HullPaintTry = 0;
        if (voyage.Dialog != Dialog.Sail && _sailWas is { } was) { voyage.ShowSail(was.Pattern, was.Tint); _sailWas = null; }
        if (voyage.Dialog == Dialog.None) return;
        // 창마다의 이름 — 소지품 창은 쪽지마다 다르다
        string[] itemTabs = ["", "Recipe", "Shop", "Add", "Ship"];
        canvas.PanelId = $"Wnd{voyage.Dialog}" + (voyage.Dialog == Dialog.Items ? itemTabs[Math.Clamp(_itemTab, 0, itemTabs.Length - 1)] : "");
        if (voyage.Dialog == Dialog.Skills) { SkillWindow(); return; }
        if (voyage.Dialog == Dialog.Shipyard) { ShipyardWindow(); return; }
        if (voyage.Dialog == Dialog.Items) { ItemWindow(); return; }
        if (voyage.Dialog == Dialog.ShipParts) { ShipPartWindow(); return; }
        if (voyage.Dialog == Dialog.Aides) { AideWindow(); return; }
        if (voyage.Dialog == Dialog.Court) { CourtWindow(); return; }
        if (voyage.Dialog == Dialog.CustomBuild) { CustomBuildWindow(); return; }
        if (voyage.Dialog == Dialog.Outfit) { OutfitWindow(); return; }
        if (voyage.Dialog == Dialog.UseSkills) { UseSkillWindow(); return; }
        if (voyage.Dialog == Dialog.QuickSetup) { QuickSetupWindow(); return; }
        if (voyage.Dialog == Dialog.Strengthen) { StrengthenWindow(); return; }
        if (voyage.Dialog == Dialog.Trade) { TradeWindow(); return; }
        if (voyage.Dialog == Dialog.ShipSwap) { ShipSwapWindow(); return; }
        if (voyage.Dialog == Dialog.Vault) { VaultWindow(); return; }
        if (voyage.Dialog == Dialog.Tavern) { TavernWindow(); return; }
        if (voyage.Dialog == Dialog.ShipInfo) { ShipInfoWindow(); return; }
        if (voyage.Dialog == Dialog.University) { UniversityWindow(); return; }
        if (voyage.Dialog == Dialog.Character) { CharacterWindow(); return; }
        if (voyage.Dialog == Dialog.Jobs) { JobWindow(); return; }
        if (voyage.Dialog == Dialog.Learn) { LearnWindow(); return; }
        if (voyage.Dialog == Dialog.WorkMethod) { WorkMethodWindow(); return; }
        if (voyage.Dialog == Dialog.Combine) { CombineWindow(); return; }
        if (voyage.Dialog == Dialog.Fitting) { FittingWindow(); return; }
        if (voyage.Dialog == Dialog.Recruit) { RecruitWindow(); return; }
        if (voyage.Dialog == Dialog.Cargo) { CargoWindow(); return; }
        if (voyage.Dialog == Dialog.Sail) { SailWindow(); return; }
        if (voyage.Dialog == Dialog.HullPaint) { HullPaintWindow(); return; }
        if (voyage.Dialog == Dialog.ShipyardMenu) { ShipyardMenu(); return; }
        if (voyage.Dialog == Dialog.SpecialBuild) { SpecialBuildWindow(); return; }
        if (voyage.Dialog == Dialog.HullBuild) { HullBuildWindow(); return; }
        if (voyage.Dialog == Dialog.Equip) { EquipWindow(); return; }
        if (voyage.Dialog == Dialog.Battle) { BattleWindow(); return; }
        if (voyage.Dialog == Dialog.LandBattle) { LandBattleWindow(); return; }
        if (voyage.Dialog == Dialog.Invest) { InvestWindow(); return; }
        if (voyage.Dialog == Dialog.Farm) { FarmWindow(); return; }
        if (voyage.Dialog == Dialog.TavernMenu) { TavernMenuWindow(); return; }
        if (voyage.Dialog == Dialog.FleetReport) { FleetReportWindow(); return; }
        if (voyage.Dialog == Dialog.Exile) { ExileWindow(); return; }
        if (voyage.Dialog == Dialog.Library) { LibraryWindow(); return; }
        if (voyage.Dialog == Dialog.FoundList) { FoundWindow(); return; }
        if (voyage.Dialog != Dialog.Trade && voyage.SheetsMarkedAll > 0) voyage.ClearMarkedSheets();      // 교역 창이 닫혔으면(Esc 따위) 걸어 둔 발주서를 푼다
        if (voyage.Dialog == Dialog.Delegate) { DelegateWindow(); return; }
        if (voyage.Dialog == Dialog.Chart) { ChartWindow(); return; }
        if (voyage.Dialog == Dialog.Nav) { NavWindow(); return; }
        if (voyage.Dialog == Dialog.Forge) { ForgeWindow(); return; }
        if (voyage.Dialog == Dialog.Honors) { HonorWindow(); return; }
        const float w = 560, h = 340;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);

        void Title(string text) => canvas.Text(text, x + 20, y + 14, w - 40, 30, 20, Canvas.Gold, 0, true);
        void Body(string text, float top = 56, float height = 200) =>
            canvas.Text(text, x + 20, y + top, w - 40, height, 16, Canvas.White);
        bool Close(string label = "닫기") => canvas.Button(label, x + w - 140, y + h - 50, 120, 34);

        // 의뢰 중개인을 거쳐 연 조합 창은 말하는 이가 중개인이다 — 창이 닫히면 잊는다
        if (voyage.Dialog is not (Dialog.Guild or Dialog.TradeGuild or Dialog.SeaGuild)) _viaBroker &= voyage.Dialog == Dialog.Broker;
        void BrokerBody(string text, float top, float tall) => Body(_viaBroker ? text.Replace("상인조합 마스터\n", "의뢰 중개인\n").Replace("조합 마스터\n", "의뢰 중개인\n") : text, top, tall);
        switch (voyage.Dialog)
        {
            case Dialog.Broker:
                Title("의뢰 중개인");
                Body("의뢰 중개인\n조합에 들어온 일을 이어 주고 있소. 어느 쪽 일을 찾으시오?", 56, 60);
                if (canvas.Button($"모험 의뢰 — {voyage.GuildName}", x + 20, y + 130, w - 40, 34)) { (voyage.Dialog, _viaBroker) = (Dialog.Guild, true); break; }
                if (canvas.Button($"교역 의뢰 — 상인조합 (납품 {voyage.TradeQuestsHere().Count}건)", x + 20, y + 172, w - 40, 34)) { (voyage.Dialog, _viaBroker) = (Dialog.TradeGuild, true); break; }
                if (canvas.Button($"해사 의뢰 — 해양조합 (토벌 {voyage.SeaQuestsHere().Count}건)", x + 20, y + 214, w - 40, 34)) { (voyage.Dialog, _viaBroker) = (Dialog.SeaGuild, true); break; }
                if (voyage.ScholarHere && canvas.Button("서적 열람 — 학자 (서고 건물이 없는 도시)", x + 20, y + 256, w - 190, 34, true, 14)) { voyage.Dialog = Dialog.Library; break; }
                if (voyage.Dialog == Dialog.Broker && Close()) voyage.Dialog = Dialog.None;
                break;
            case Dialog.SeaGuild:
                canvas.PanelId = "Wnd057";
                SeaGuild(x, y, w, h, Title, (text, top, tall) => BrokerBody(text.Replace("해양조합 마스터\n", _viaBroker ? "의뢰 중개인\n" : "해양조합 마스터\n"), top, tall));
                if (voyage.Dialog == Dialog.SeaGuild && !_viaBroker && canvas.Button("스킬을 배운다", x + w - 170, y + 12, 150, 30, true, 14)) { voyage.LearnFrom(2); break; }
                if (voyage.Dialog == Dialog.SeaGuild && Close()) voyage.Dialog = Dialog.None;
                break;

            case Dialog.TradeGuild:
                TradeGuild(x, y, w, h, Title, BrokerBody);
                if (voyage.Dialog == Dialog.TradeGuild && canvas.Button("스킬을 배운다", x + w - 170, y + 12, 150, 30, true, 14)) { voyage.LearnFrom(1); break; }
                if (voyage.Dialog == Dialog.TradeGuild && Close()) voyage.Dialog = Dialog.None;
                break;
            case Dialog.Guild:
                Guild(x, y, w, h, Title, BrokerBody);
                if (voyage.Dialog == Dialog.Guild && canvas.Button("스킬을 배운다", x + w - 170, y + 12, 150, 30, true, 14)) { voyage.LearnFrom(0); break; }
                if (voyage.Dialog == Dialog.Guild && Close())
                {
                    voyage.Offered = null;
                    voyage.Dialog = Dialog.None;
                }
                break;

            case Dialog.QuestDetail when voyage.Quest is { MapSkill: > 0 } map:
                Title($"지도 — {map.Title}");
                Body($"{map.Client}에서 얻은 지도\n\n{map.Request}\n\n목표: {map.Hint}\n필요: {voyage.SkillName(map.MapSkill)} 랭크 {map.Rank}{(map.FindRank > 0 ? $" · 찾는 스킬 랭크 {Math.Max(1, map.FindRank - 2)}" : "")}\n\n※ 찾으면 그 자리에서 끝난다(보고하지 않는다)." +
                     (voyage.TradeJob != null ? $"\n\n교역 의뢰: {voyage.TradeLine}" : "") + (voyage.SeaJob != null ? $"\n\n해사 의뢰: {voyage.SeaLine}" : ""));
                if (Close()) voyage.Dialog = Dialog.None;
                break;

            case Dialog.QuestDetail when voyage.Quest is { } quest:
                Title($"의뢰 내용 — {quest.Title}");
                Body($"의뢰인: {quest.Client}\n\n{quest.Request}\n\n목표: {quest.Hint}\n" +
                     $"보고: {voyage.CityName(quest.CityId)}   보수: {quest.Reward:N0} 두캇{(quest.RewardItem > 0 ? $" · {voyage.ItemName(quest.RewardItem)} {quest.RewardItemCount}개" : "")}\n\n" +
                     (voyage.QuestStage == QuestStage.Discovered ? "※ 발견을 마쳤다. 조합에 보고하자." : "※ 아직 발견하지 못했다.") +
                     (voyage.TradeJob != null ? $"\n\n교역 의뢰: {voyage.TradeLine}" : "") + (voyage.SeaJob != null ? $"\n\n해사 의뢰: {voyage.SeaLine}" : ""));
                if (Close()) voyage.Dialog = Dialog.None;
                break;

            case Dialog.QuestDetail when voyage.TradeJob is { } delivery:
                Title($"의뢰 내용 — {voyage.TradeTitle(delivery)}");
                Body($"의뢰인: 상인조합\n\n{voyage.TradeLine}\n\n보고: {voyage.CityName(voyage.TradeGiver)}   보수: {voyage.TradePay(delivery):N0} 두캇{(delivery.Gifts.Count > 0 ? " · " + voyage.GiftText(delivery.Gifts) : "")}" + (voyage.SeaJob != null ? $"\n\n해사 의뢰: {voyage.SeaLine}" : ""));
                if (Close()) voyage.Dialog = Dialog.None;
                break;

            case Dialog.QuestDetail when voyage.SeaJob is { } hunt:
                Title($"의뢰 내용 — {voyage.SeaTitle(hunt)}");
                Body($"의뢰인: 해양조합\n\n{voyage.SeaLine}\n\n보고: {voyage.CityName(voyage.SeaGiver)}   보수: {voyage.SeaPay(hunt):N0} 두캇{(hunt.Gifts.Count > 0 ? " · " + voyage.GiftText(hunt.Gifts) : "")}");
                if (Close()) voyage.Dialog = Dialog.None;
                break;

            case Dialog.Ashore when voyage.Ashore is { } shoreNow:
                Title(shoreNow.Name);
                Body($"배를 대고 뭍에 올랐다.\n\n물 {voyage.Water:0} / {voyage.MaxWaterNow}   피로 {voyage.Fatigue:0}   생명력 {voyage.Life:0} / {voyage.MaxLife}");
                if (canvas.Button("물을 긷는다", x + 20, y + h - 50, 130, 34)) voyage.DrawWater();
                if (canvas.Button($"둘러본다 ({voyage.LooksLeft})", x + 160, y + h - 50, 130, 34, voyage.LooksLeft > 0)) voyage.LookAround();
                if (voyage.CanGather && canvas.Button($"채집 ({voyage.GatherLeft})", x + 20, y + h - 90, 130, 34, voyage.GatherLeft > 0)) voyage.Gather();
                if (voyage.Dialog == Dialog.Ashore && Close("배로 돌아간다")) voyage.Dialog = Dialog.None;
                break;

            case Dialog.Landing when voyage.Quest is { } quest:
                Title(voyage.QuestLanding?.Name ?? "상륙");
                Body(quest.LandingText + "\n\n어떻게 할까?");
                if (canvas.Button("주변을 탐색한다", x + 20, y + h - 50, 180, 34)) voyage.Search();
                if (voyage.Dialog == Dialog.Landing && Close("배로 돌아간다")) voyage.Dialog = Dialog.None;
                break;

            case Dialog.Discovery when voyage.QuestDiscovery is { } found:
                Title($"발견!  {found.Name}");
                canvas.Text($"{voyage.DiscoveryKind(found.Kind)}   {voyage.DiscoveryStars(found)}   모험 경험 +{found.Exp}   명성 +{found.Fame}",
                            x + 20, y + 50, w - 40, 24, 15, Canvas.Gold);
                // 발견물 그림(0010\\0001\\sd, 무리 0)이 있으면 왼쪽에 띄운다
                if (canvas.Image($"sd{found.Id}", () => (_discoveryImages ??= new ImageSet(@"0010\0001\sd")).Pixels(0, found.Id), x + 20, y + 86, 128, 128))
                {
                    canvas.Frame(x + 20, y + 86, 128, 128, Canvas.PanelEdge, 1f);
                    canvas.Text(found.Description, x + 164, y + 84, w - 184, 190, 16, Canvas.White);
                }
                else Body(found.Description, 84, 190);
                if (Close("확인")) voyage.Dialog = Dialog.None;
                break;

            case Dialog.Report when voyage.Reported is { } reported:
                Title("의뢰 달성");
                Body($"조합 마스터\n{voyage.ReportedDiscovery?.Name ?? "그것"}이라… 학자들이 기뻐하겠군. 수고했네.\n\n" +
                     $"「{reported.Title}」 보수 {reported.Reward:N0} 두캇을 받았다.\n소지금 {voyage.Money:N0} 두캇");
                if (Close("확인")) voyage.Dialog = Dialog.None;
                break;

            case Dialog.Supply:
                Supply(x, y, w, h, Title);
                if (Close()) voyage.Dialog = Dialog.None;
                break;

            case Dialog.ShipSwap:
                ShipSwapWindow();
                if (Close()) voyage.Dialog = Dialog.None;
                break;

            case Dialog.Bank:
                Title("은행");
                Body($"은행원\n어서 오십시오. 맡기신 돈은 배가 가라앉아도 그대로입니다.\n\n예금   {voyage.Savings:N0} 두캇\n소지금 {voyage.Money:N0} 두캇", 56, 150);
                canvas.Text("맡긴다", x + 20, y + 208, 70, 24, 15, Canvas.Gold);
                canvas.Text("찾는다", x + 20, y + 246, 70, 24, 15, Canvas.Gold);
                (string Label, long Amount)[] sums = [("10만", 100_000), ("100만", 1_000_000), ("1000만", 10_000_000), ("전부", long.MaxValue)];
                for (int i = 0; i < sums.Length; i++)
                {
                    if (canvas.Button(sums[i].Label, x + 92 + i * 96, y + 204, 90, 30, voyage.Money > 0, 14)) voyage.Bank(sums[i].Amount);
                    if (canvas.Button(sums[i].Label, x + 92 + i * 96, y + 242, 90, 30, voyage.Savings > 0, 14)) voyage.Bank(-sums[i].Amount);
                }
                if (canvas.Button($"보관함 ({voyage.Vault.Count})", x + 20, y + h - 50, 150, 34)) { voyage.Dialog = Dialog.Vault; break; }
                // 보험 — 누를 때마다 다음 등급으로(끝 다음은 해약). 등급과 보험료는 임시 값
                int nextInsurance = (voyage.Insurance + 1) % Voyage.Insurances.Length;
                if (canvas.Button($"{voyage.Text(1225, "보험")}: {(voyage.Insurance == 0 ? "미 계약" : $"{Voyage.Insurances[voyage.Insurance].Most / 10000}만")} ▶ {(nextInsurance == 0 ? "해약" : $"{Voyage.Insurances[nextInsurance].Most / 10000}만 ({Voyage.Insurances[nextInsurance].Fee:N0})")}", x + 178, y + h - 50, 234, 34, true, 11)) voyage.NextInsurance();
                if (Close()) voyage.Dialog = Dialog.None;
                break;

            case Dialog.Wreck:
                Title(voyage.WreckText.Contains("빼앗겼다") || voyage.WreckText.Contains("당해") ? "패배" : "난파");
                Body(voyage.WreckText);
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
            canvas.Text($"의뢰 「{offered.Title}」   선금 {offered.Advance:N0}   보수 {offered.Reward:N0}{(offered.RewardItem > 0 ? $" · {voyage.ItemName(offered.RewardItem)} {offered.RewardItemCount}개" : "")}",
                        x + 20, y + 200, w - 40, 24, 15, Canvas.Gold);
            string? tongue = voyage.AcceptBlocker(offered);
            if (offered.Languages.Count > 0)
                canvas.Text(tongue ?? $"필요 언어: {string.Join(" · ", offered.Languages.Select(voyage.SkillName))}", x + 20, y + 228, w - 40, 20, 13, tongue != null ? new Color4(1f, 0.6f, 0.5f, 1) : Canvas.Dim);
            if (canvas.Button("의뢰를 받는다", x + 20, y + h - 50, 160, 34, tongue == null)) voyage.AcceptQuest();
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
        if (voyage.QuestPermits > 0 && canvas.Button($"의뢰 알선서 사용 ({voyage.QuestPermits})", x + w - 400, y + h - 46, 190, 30, true, 13)) voyage.UseQuestPermit();
        // 한 쪽에 넷 — 더 있으면 ◀ ▶ 로 넘긴다(창 밖으로 넘쳐 가려졌었다)
        const int shown = 4;
        int pages = (quests.Count + shown - 1) / shown;
        _guildPage = Math.Clamp(_guildPage, 0, pages - 1);
        for (int i = _guildPage * shown; i < Math.Min(quests.Count, (_guildPage + 1) * shown); i++)
            if (canvas.Button($"{quests[i].Title}   (보수 {quests[i].Reward:N0})", x + 20, y + 120 + (i - _guildPage * shown) * 38, w - 40, 32))
                voyage.Offered = quests[i];
        if (pages > 1)
        {
            if (canvas.Button("◀", x + 20, y + h - 44, 36, 28, _guildPage > 0, 12)) _guildPage--;
            canvas.Text($"{_guildPage + 1} / {pages}", x + 60, y + h - 40, 50, 20, 13, Canvas.White, 1);
            if (canvas.Button("▶", x + 114, y + h - 44, 36, 28, _guildPage < pages - 1, 12)) _guildPage++;
        }
    }
    private int _guildPage;
    private bool _viaBroker;

    /// <summary>해양조합 — 해사 의뢰(토벌)를 받고 · 보고한다.</summary>
    private void SeaGuild(float x, float y, float w, float h, Action<string> title, Action<string, float, float> body)
    {
        title("해양조합");
        body($"해양조합 마스터\n{voyage.SeaGuildLine}", 56, 60);
        if (voyage.CanReportSea)
        {
            if (canvas.Button("토벌을 보고한다", x + 20, y + h - 50, 180, 34)) voyage.ReportSea();
            return;
        }
        if (voyage.SeaJob != null)
        {
            if (canvas.Button("의뢰를 그만둔다", x + 20, y + h - 50, 160, 34)) voyage.DropSea();
            return;
        }
        var quests = voyage.SeaQuestsHere();
        const int shown = 4;
        int pages = Math.Max(1, (quests.Count + shown - 1) / shown);
        _seaGuildPage = Math.Clamp(_seaGuildPage, 0, pages - 1);
        for (int i = _seaGuildPage * shown; i < Math.Min(quests.Count, (_seaGuildPage + 1) * shown); i++)
            if (canvas.Button($"{new string('★', Math.Clamp(quests[i].Difficulty, 1, 10))} {voyage.SeaTitle(quests[i])}   ({voyage.SeaShort(quests[i])} · 보수 {voyage.SeaPay(quests[i]):N0}{(quests[i].Advance > 0 ? $" · 선금 {quests[i].Advance:N0}" : "")}{(quests[i].Gifts.Count > 0 ? " · " + voyage.GiftText(quests[i].Gifts) : "")})", x + 20, y + 120 + (i - _seaGuildPage * shown) * 38, w - 40, 32, true, 12))
                voyage.AcceptSea(quests[i]);
        if (pages > 1)
        {
            if (canvas.Button("◀", x + 20, y + h - 44, 36, 28, _seaGuildPage > 0, 12)) _seaGuildPage--;
            canvas.Text($"{_seaGuildPage + 1} / {pages}", x + 60, y + h - 40, 50, 20, 13, Canvas.White, 1);
            if (canvas.Button("▶", x + 114, y + h - 44, 36, 28, _seaGuildPage < pages - 1, 12)) _seaGuildPage++;
        }
    }
    private int _seaGuildPage;

    /// <summary>상인조합 — 교역 의뢰(납품)를 받고 · 건네고 · 보고한다.</summary>
    private void TradeGuild(float x, float y, float w, float h, Action<string> title, Action<string, float, float> body)
    {
        title("상인조합");
        body($"상인조합 마스터\n{voyage.TradeGuildLine}", 56, 60);
        if (voyage.CanReportTrade)
        {
            if (canvas.Button("납품을 보고한다", x + 20, y + h - 50, 180, 34)) voyage.ReportTrade();
            return;
        }
        if (voyage.TradeJob != null)
        {
            if (voyage.CanDeliverTrade && canvas.Button("물건을 건넨다", x + 20, y + h - 50, 180, 34)) voyage.DeliverTrade();
            else if (canvas.Button("의뢰를 그만둔다", x + 210, y + h - 50, 160, 34)) voyage.DropTrade();
            return;
        }
        var quests = voyage.TradeQuestsHere();
        if (voyage.QuestPermits > 0 && canvas.Button($"의뢰 알선서 사용 ({voyage.QuestPermits})", x + w - 400, y + h - 46, 190, 30, true, 13)) voyage.UseQuestPermit();
        const int shown = 4;
        int pages = Math.Max(1, (quests.Count + shown - 1) / shown);
        _tradeGuildPage = Math.Clamp(_tradeGuildPage, 0, pages - 1);
        for (int i = _tradeGuildPage * shown; i < Math.Min(quests.Count, (_tradeGuildPage + 1) * shown); i++)
            if (canvas.Button($"{new string('★', Math.Clamp(quests[i].Difficulty, 1, 10))} {voyage.TradeTitle(quests[i])}   ({voyage.TradeNote(quests[i])} · 보수 {voyage.TradePay(quests[i]):N0}{(quests[i].Advance > 0 ? $" · 선금 {quests[i].Advance:N0}" : "")}{(quests[i].Gifts.Count > 0 ? " · " + voyage.GiftText(quests[i].Gifts) : "")})", x + 20, y + 120 + (i - _tradeGuildPage * shown) * 38, w - 40, 32, true, 12))
                voyage.AcceptTrade(quests[i]);
        if (pages > 1)
        {
            if (canvas.Button("◀", x + 20, y + h - 44, 36, 28, _tradeGuildPage > 0, 12)) _tradeGuildPage--;
            canvas.Text($"{_tradeGuildPage + 1} / {pages}", x + 60, y + h - 40, 50, 20, 13, Canvas.White, 1);
            if (canvas.Button("▶", x + 114, y + h - 44, 36, 28, _tradeGuildPage < pages - 1, 12)) _tradeGuildPage++;
        }
    }
    private int _tradeGuildPage;

    /// <summary>항구의 보급 — 물·식량·대처 물품을 사고, 배를 고치고, 선원을 채운다.</summary>
    private void Supply(float x, float y, float w, float h, Action<string> title)
    {
        var rules = voyage.Data.Settings.Voyage;
        title($"보급     소지금 {voyage.Money:N0} Ð");

        float row = y + 50;
        void Row(string label, string state, string button, bool enabled, Action buy)
        {
            canvas.Text(label, x + 20, row + 4, 150, 24, 15, Canvas.White);
            canvas.Text(state, x + 130, row + 4, 230, 24, state.Length > 22 ? 11 : 14, Canvas.Dim);
            if (canvas.Button(button, x + w - 190, row, 170, 23, enabled, 13)) buy();
            row += 26;
        }

        // 물 · 식량은 창고가 허락하는 만큼 싣는다 — 열 통 단추 왼쪽에 「100통」과 「내리기」(물자가 창고를 차지하니 덜어 낼 길도 둔다)
        if (canvas.Button("−10", x + w - 310, row, 54, 23, voyage.Water > 0, 12)) voyage.DumpWater(10);
        if (canvas.Button("100통", x + w - 252, row, 58, 23, voyage.Water < voyage.MaxWaterNow, 12)) voyage.BuyWater(100);
        Row("물", $"{voyage.Water:0} / {voyage.MaxWaterNow}", $"10통 싣기 ({voyage.WaterPrice * 10:N0})", voyage.Water < voyage.MaxWaterNow, () => voyage.BuyWater(10));
        if (canvas.Button("−10", x + w - 310, row, 54, 23, voyage.Food > 0, 12)) voyage.DumpFood(10);
        if (canvas.Button("100통", x + w - 252, row, 58, 23, voyage.Food < voyage.MaxFoodNow, 12)) voyage.BuyFood(100);
        Row("식량", $"{voyage.Food:0} / {voyage.MaxFoodNow}", $"10통 싣기 ({voyage.FoodPrice * 10:N0})", voyage.Food < voyage.MaxFoodNow, () => voyage.BuyFood(10));
        // 자재(수리용 목재) — 바다에서 수리 스킬로 배를 고칠 때 든다. 물 · 식량처럼 열 개씩 싣는다
        if (voyage.Data.Supplies.Find(s => s.Id == 2) is { } timber)
            Row("자재 (수리용)", $"{voyage.SupplyCount(2)}개", $"10개 싣기 ({timber.Price * 10:N0})", voyage.Money >= timber.Price * 10, () => { for (int i = 0; i < 10; i++) voyage.BuySupply(timber); });
        foreach (var supply in voyage.Data.Supplies.Take(4))
            Row(supply.Name, $"{voyage.SupplyCount(supply.Id)}개 · {supply.Description}", $"사기 ({supply.Price:N0})", voyage.Money >= supply.Price, () => voyage.BuySupply(supply));
        Row("내구", $"{voyage.Durability:0} / {voyage.Stats.Durability}", $"수리 ({voyage.RepairCost:N0})", voyage.RepairCost > 0 && voyage.Money >= voyage.RepairCost, voyage.Repair);
        Row("선원", $"{voyage.Crew:0} / {voyage.Stats.MaxCrew}", $"모집 ({voyage.HireCost:N0})", voyage.HireCost > 0 && voyage.Money >= voyage.HireCost, voyage.Hire);
    }

    private int _skillPage;

    /// <summary>스킬 창 — 익힌 스킬의 랭크와 숙련도, 항구에서 배울 수 있는 스킬.</summary>
    /// <summary>스킬 창의 쪽지: 0 습득, 1 ~ 4 는 스킬 표의 갈래(모험 · 교역 · 전투 · 언어).</summary>
    public int SkillTab;
    private int _skillChosen;
    private (int Count, int Visible) _skillRows;

    /// <summary>마우스 휠 — 목록이 떠 있으면 목록을 굴리고 true(그러면 카메라는 안 움직인다).</summary>
    public bool Wheel(int notches)
    {
        // 바다의 주변 지도 위에서 굴리면 지도의 배율이 바뀐다(위로: 크게)
        if (voyage.Created && voyage.Mode == Mode.Sea && voyage.Dialog == Dialog.None && _seaMapRect.Size > 0
            && canvas.Pointer.X >= _seaMapRect.X && canvas.Pointer.X < _seaMapRect.X + _seaMapRect.Size && canvas.Pointer.Y >= _seaMapRect.Y && canvas.Pointer.Y < _seaMapRect.Y + _seaMapRect.Size)
        {
            SetSeaMapZoom(voyage.Data.Settings.SeaMapZoom * (notches > 0 ? 1.25 : 0.8));
            return true;
        }
        if (_displayOpen && _soundOpen)
        {
            if (_soundLeft) _bankTop = Math.Max(0, _bankTop - notches * 2);
            else _soundTop = Math.Max(0, _soundTop - notches * 2);
            return true;
        }
        if (voyage.Dialog == Dialog.FoundList) { _foundTop = Math.Max(0, _foundTop - notches * 3); return true; }
        // 내비게이션: 위로 굴리면 좁게(확대), 아래로 굴리면 넓게(축소) — 1 · 2 · 4배
        if (voyage.Dialog == Dialog.Nav) { _navZoom = notches > 0 ? Math.Max(1, _navZoom / 2) : Math.Min(4, _navZoom * 2); return true; }
        if (voyage.Dialog == Dialog.Delegate) { _delegateTop = Math.Max(0, _delegateTop - notches * 3); return true; }
        // 기록 칸 — 창이 떠 있어도 마우스가 그 창 밖의 기록 칸 위에 있으면 굴린다
        bool underDialog = _dialogRect.W > 0 && canvas.Pointer.X >= _dialogRect.X && canvas.Pointer.X < _dialogRect.X + _dialogRect.W && canvas.Pointer.Y >= _dialogRect.Y && canvas.Pointer.Y < _dialogRect.Y + _dialogRect.H;
        if (_logHover && !underDialog)
        {
            _logBack = Math.Max(0, _logBack + notches);
            return true;
        }
        if (voyage.Dialog == Dialog.Items)
        {
            _itemPage = Math.Max(0, _itemPage - notches);
            return true;
        }
        if (voyage.Dialog == Dialog.Learn)
        {
            _learnTop = Math.Clamp(_learnTop - notches, 0, Math.Max(0, _learnRows.Count - _learnRows.Visible));
            return true;
        }
        if (voyage.Dialog == Dialog.Jobs)
        {
            _jobTop = Math.Clamp(_jobTop - notches * 2, 0, Math.Max(0, _jobRows.Count - _jobRows.Visible));
            return true;
        }
        if (voyage.Dialog == Dialog.Trade)
        {
            _tradePage = Math.Clamp(_tradePage - notches, 0, Math.Max(0, (_tradeRows.Count - 1) / Math.Max(1, _tradeRows.Visible)));
            return true;
        }
        if (voyage.Dialog == Dialog.University)
        {
            _majorTop = Math.Clamp(_majorTop - notches * 2, 0, Math.Max(0, _majorRows.Count - _majorRows.Visible));
            return true;
        }
        if (voyage.Dialog == Dialog.WorkMethod)
        {
            _contentTop = Math.Clamp(_contentTop - notches, 0, Math.Max(0, _contentRows.Count - _contentRows.Visible));
            return true;
        }
        if (voyage.Dialog == Dialog.Combine)
        {
            // 포인터가 보너스 칸 위에 있으면 보너스 목록을, 아니면 배 목록을 굴린다
            if (_overBonus) _bonusTop = Math.Max(0, _bonusTop - notches * 2);
            else _combineTop = Math.Max(0, _combineTop - notches);
            return true;
        }
        if (voyage.Dialog == Dialog.ShipSwap)
        {
            _swapTop = Math.Max(0, _swapTop - notches);
            return true;
        }
        if (voyage.Dialog == Dialog.Shipyard)
        {
            _shipPage = Math.Clamp(_shipPage - notches * 2, 0, Math.Max(0, _shipRows.Count - _shipRows.Visible));
            return true;
        }
        if (voyage.Dialog == Dialog.SpecialBuild)
        {
            _specialTop = Math.Max(0, _specialTop - notches);
            return true;
        }
        if (voyage.Dialog == Dialog.HullBuild)
        {
            _hullTop = Math.Max(0, _hullTop - notches);
            return true;
        }
        if (voyage.Dialog == Dialog.CustomBuild)
        {
            _buildTop = Math.Clamp(_buildTop - notches * 2, 0, Math.Max(0, _buildRows.Count - _buildRows.Visible));
            return true;
        }
        if (voyage.Dialog == Dialog.ShipParts)
        {
            _partPage = Math.Max(0, Math.Min(_partPage - notches, (_partRows.Count - 1) / Math.Max(1, _partRows.Visible)));
            return true;
        }
        if (voyage.Dialog != Dialog.Skills) return voyage.Dialog != Dialog.None;
        _skillPage = Math.Clamp(_skillPage - notches * 2, 0, Math.Max(0, _skillRows.Count - _skillRows.Visible));
        return true;
    }
    private int _shipPage, _tradePage, _shipSize, _shipUse, _teacherShown = -1, _partSlot;
    private (int Count, int Visible) _partRows;

    /// <summary>선박 구입 창에서 위아래 글쇠 — 고른 배를 옮기고 목록이 따라 굴러간다.</summary>
    /// <summary>단축키: 환경설정 창을 여닫는다 / 왼쪽 위 차림(☰)을 여닫는다.</summary>
    public void ToggleSettings() => (_displayOpen, _soundOpen, _menuOpen) = (!_displayOpen, false, false);
    public void ToggleMenu() => (_menuOpen, _bentoOpen) = (!_menuOpen, false);
    public void ToggleKeys() => (_keysOpen, KeyWaiting, _menuOpen) = (!_keysOpen, null, false);
    public void ToggleMod() => (_modOpen, _menuOpen) = (!_modOpen, false);
    public void ToggleDev() => (_devOpen, _menuOpen) = (!_devOpen, false);
    public void ToggleWarp() { if (_warpOpen) _warpOpen = false; else OpenWarp(_warpCities); }
    public void LogOut() => Quit?.Invoke();

    /// <summary>Esc — 따로 뜨는 창과 차림 가운데 맨 위의 것 하나를 닫는다. 닫은 것이 있으면 true.</summary>
    public bool CloseTop()
    {
        if (_soundOpen) { (_soundOpen, _memoEditing) = (false, false); voyage.Data.SaveSettings(); return true; }
        if (_keysOpen) { (_keysOpen, KeyWaiting) = (false, null); return true; }
        if (_displayOpen) { _displayOpen = false; return true; }
        if (_modOpen) { _modOpen = false; return true; }
        if (_warpOpen) { _warpOpen = false; return true; }
        if (_devOpen) { _devOpen = false; return true; }
        if (_menuOpen || _bentoOpen) { (_menuOpen, _bentoOpen) = (false, false); return true; }
        if (_cmpOpen) { _cmpOpen = false; return true; }
        if (_nearbyOpen) { _nearbyOpen = false; return true; }
        if (TownMenuOpen) { TownMenuOpen = false; return true; }
        if (TownMapOpen) { TownMapOpen = false; return true; }
        return false;
    }

    public bool ListKey(int step)
    {
        // 고른 줄을 옮기고, 화면 밖으로 나가면 목록이 따라 굴러간다
        static void Move(ref int chosen, ref int top, (int Count, int Visible) rows, int step)
        {
            if (rows.Count == 0) return;
            chosen = Math.Clamp(chosen + step, 0, rows.Count - 1);
            if (chosen < top) top = chosen;
            if (chosen >= top + rows.Visible) top = chosen - rows.Visible + 1;
        }
        if (_displayOpen && _soundOpen && !_memoEditing)
        {
            int count = SoundCount?.Invoke(_soundBank) ?? 0;
            Move(ref _soundIndex, ref _soundTop, (count, 8), step);
            if (count > 0) PlaySound?.Invoke($"{_soundBank}:{_soundIndex}");
            return true;
        }
        switch (voyage.Dialog)
        {
            case Dialog.Shipyard: Move(ref _shipChosen, ref _shipPage, _shipRows, step); return true;
            case Dialog.CustomBuild: Move(ref _buildMaterial, ref _buildTop, _buildRows, step); return true;
            case Dialog.Skills: Move(ref _skillChosen, ref _skillPage, _skillRows, step); return true;
            case Dialog.Jobs: Move(ref _jobChosen, ref _jobTop, _jobRows, step); return true;
            case Dialog.WorkMethod: Move(ref _contentChosen, ref _contentTop, _contentRows, step); return true;
            case Dialog.Learn: Move(ref _learnChosen, ref _learnTop, _learnRows, step); return true;
            case Dialog.University: Move(ref _majorChosen, ref _majorTop, _majorRows, step); return true;
            case Dialog.Trade:
                // 교역소는 쪽으로 넘긴다 — 고른 줄이 쪽을 넘어가면 쪽이 바뀐다
                if (_tradeRows.Count == 0) return true;
                _tradeChosen = Math.Clamp(_tradeChosen + step, 0, _tradeRows.Count - 1);
                _tradePage = _tradeChosen / Math.Max(1, _tradeRows.Visible);
                return true;
            case Dialog.SpecialBuild: (_specialChosen, _specialKeyed) = (Math.Max(0, _specialChosen + step), true); return true;
            case Dialog.ShipSwap:
            {
                // 고른 배를 옮긴다(−1 이 타고 있는 배, 0 부터 부두의 배) — 목록은 고른 줄이 보이는 끝을 넘을 때만 굴러간다
                if (_cmpOpen) return true;
                const int visible = 4;
                int now = _swapPicked == null ? -1 : voyage.Dock.IndexOf(_swapPicked);
                now = Math.Clamp(now + step, -1, voyage.Dock.Count - 1);
                _swapPicked = now < 0 ? null : voyage.Dock[now];
                if (now >= 0 && now < _swapTop) _swapTop = now;
                if (now >= _swapTop + visible) _swapTop = now - visible + 1;
                if (now < 0) _swapTop = 0;
                return true;
            }
            case Dialog.Items: _itemPage = Math.Max(0, _itemPage + step); return true;
            case Dialog.ShipParts: _partPage = Math.Max(0, _partPage + step); return true;
            case Dialog.None: return false;
            default: return true;              // 다른 창이 떠 있을 때 위아래 글쇠가 돛을 건드리지 않게
        }
    }

    /// <summary>시내에서 눌러 가는 곳의 화면 자리(없으면 null) — 창이 프레임마다 넣는다.</summary>
    public (float X, float Y)? Target;
    private (int Count, int Visible) _shipRows;
    private bool _selling;

    /// <summary>
    /// 스킬 창(X) — 원본의 짜임을 따른다: 위에 쪽지(습득 · 모험 · 교역 · 전투 · 언어), 왼쪽에 아이콘·이름·랭크·숙련도,
    /// 오른쪽에 고른 스킬의 설명과 상태. 아이콘은 <c>0010\0001\sa</c>(id = 스킬 번호).
    /// </summary>
    private void SkillWindow()
    {
        string[] tabs = ["습득", "모험", "교역", "전투", "언어"];
        // 조합 마스터에게 배우러 왔으면 그 갈래의 쪽지부터 보여 준다
        if (voyage.Teacher != _teacherShown)
        {
            _teacherShown = voyage.Teacher;
            if (voyage.Teacher >= 0) (SkillTab, _skillPage, _skillChosen) = (voyage.Teacher + 1, 0, 0);
        }
        const float w = 900, h = 450, listWidth = 520;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
        canvas.Block(x, y - 30, w, h + 30);
        for (int i = 0; i < tabs.Length; i++)
        {
            bool on = SkillTab == i;
            canvas.Fill(x + i * 122, y - 30, 118, 30, on ? new Color4(0.16f, 0.62f, 0.72f, 0.98f) : new Color4(0.35f, 0.55f, 0.65f, 0.9f));
            canvas.Frame(x + i * 122, y - 30, 118, 30, Canvas.PanelEdge, 1);
            canvas.Text(tabs[i], x + i * 122, y - 27, 118, 24, 16, on ? Canvas.White : new Color4(0.05f, 0.1f, 0.2f, 1), 1, on, on);
            if (canvas.Hover(x + i * 122, y - 30, 118, 30) && canvas.Pointer.Clicked) (SkillTab, _skillPage, _skillChosen) = (i, 0, 0);
        }
        canvas.Panel(x, y, w, h);

        // 쪽지에 든 스킬들: 습득은 익힌 것, 나머지는 그 갈래의 것 전부(익힌 것이 앞)
        var list = (SkillTab == 0
            ? voyage.Skills.Keys.OrderBy(id => id).Select(id => voyage.Data.Skills.Find(s => s.Id == id)).OfType<SkillData>()
            : voyage.Data.Skills.Where(s => s.Group == SkillTab - 1 && !s.Name.StartsWith('※')).OrderBy(s => voyage.Rank(s.Id) > 0 ? 0 : 1).ThenBy(s => s.Id)).ToList();
        // 목록은 휠로 굴린다 — _skillPage 가 맨 윗줄의 차례다
        const int perPage = 10;
        int last = Math.Max(0, list.Count - perPage);
        _skillPage = Math.Clamp(_skillPage, 0, last);
        _skillChosen = Math.Clamp(_skillChosen, 0, Math.Max(0, list.Count - 1));
        _skillRows = (list.Count, perPage);

        float row = y + 16;
        for (int i = _skillPage; i < Math.Min(list.Count, _skillPage + perPage); i++, row += 38)
        {
            var skill = list[i];
            int rank = voyage.Rank(skill.Id);
            bool chosen = i == _skillChosen;
            if (chosen) canvas.Fill(x + 14, row, listWidth - 8, 36, new Color4(0.12f, 0.55f, 0.52f, 0.95f));
            else if (canvas.Hover(x + 14, row, listWidth - 8, 36)) canvas.Fill(x + 14, row, listWidth - 8, 36, new Color4(0.2f, 0.3f, 0.6f, 0.6f));
            if (canvas.Hover(x + 14, row, listWidth - 8, 36) && canvas.Pointer.Clicked) _skillChosen = i;
            SkillIcon(skill.Id, x + 18, row + 2, rank > 0 ? 1 : 0.4f);
            // 전문 스킬은 빨간 별, 우대 스킬은 노란 별
            if (voyage.IsExpert(skill.Id)) canvas.Text("✦", x + 40, row - 2, 16, 16, 13, new Color4(1f, 0.3f, 0.45f, 1), 0, true);
            else if (voyage.IsFavored(skill.Id)) canvas.Text("✦", x + 40, row - 2, 16, 16, 13, Canvas.Gold, 0, true);
            canvas.Text(skill.Name, x + 56, row + 7, 190, 24, 16, rank > 0 ? Canvas.White : Canvas.Dim);
            if (rank > 0)
            {
                int boost = voyage.ExpertBoost(skill.Id);
                canvas.Text($"Rank {rank - boost,2}" + (boost > 0 ? $"(+{boost})" : ""), x + 250, row + 7, 110, 24, 16, Canvas.White);
                bool top = rank >= voyage.Data.Settings.MaxSkillRank;
                canvas.Text(top ? "※최대 랭크" : $"{voyage.Skills[skill.Id].Exp:0}/{voyage.ExpNeed(voyage.Skills[skill.Id])}", x + 340, row + 7, 170, 24, 16, Canvas.White, 2);
            }
            else canvas.Text($"{skill.Cost:N0} Ð", x + 340, row + 7, 170, 24, 15, Canvas.Dim, 2);
        }
        if (list.Count == 0) canvas.Text("익힌 스킬이 없다.", x + 24, y + 24, 300, 24, 16, Canvas.Dim);
        canvas.Text("✦", x + 20, y + h - 40, 16, 20, 14, new Color4(1f, 0.3f, 0.45f, 1), 0, true);
        canvas.Text("전문스킬", x + 38, y + h - 40, 70, 22, 14, Canvas.White);
        canvas.Text("✦", x + 112, y + h - 40, 16, 20, 14, Canvas.Gold, 0, true);
        canvas.Text("우대스킬", x + 130, y + h - 40, 70, 22, 14, Canvas.White);
        canvas.Text($"습득수 {voyage.Skills.Count}   (이 쪽지 {list.Count}개)", x + 220, y + h - 40, 290, 22, 14, Canvas.White, 2);
        // 굴림 막대 — 누르면 그 자리로
        if (last > 0)
        {
            float barX = x + listWidth - 2, barY = y + 16, barH = perPage * 38 - 2;
            canvas.Fill(barX, barY, 10, barH, new Color4(0.03f, 0.05f, 0.16f, 0.9f));
            float thumb = Math.Max(24, barH * perPage / list.Count), at = (barH - thumb) * _skillPage / last;
            canvas.Fill(barX + 1, barY + at, 8, thumb, new Color4(0.72f, 0.76f, 0.84f, 0.95f));
            if (canvas.Hover(barX - 4, barY, 18, barH) && canvas.Pointer.Clicked)
                _skillPage = Math.Clamp((int)MathF.Round((canvas.Pointer.Y - barY - thumb / 2) / Math.Max(1, barH - thumb) * last), 0, last);
        }
        canvas.Line(x + listWidth + 14, y + 14, x + listWidth + 14, y + h - 14, new Color4(0.5f, 0.55f, 0.75f, 0.8f), 1);

        // 오른쪽: 고른 스킬
        float rx = x + listWidth + 30, rw = w - listWidth - 50;
        if (list.Count > 0)
        {
            if (SkillPick != "" && list.FindIndex(s => s.Name == SkillPick) is >= 0 and var picked) (_skillChosen, SkillPick) = (picked, "");
            var skill = list[_skillChosen];
            int rank = voyage.Rank(skill.Id);
            var rule = voyage.Data.SkillRules.Find(r => r.SkillId == skill.Id);
            SkillIcon(skill.Id, rx, y + 16, 1);
            canvas.Text(skill.Name, rx + 40, y + 22, rw - 40, 26, 18, Canvas.White, 0, true);
            canvas.Fill(rx, y + 60, rw, 96, new Color4(0.06f, 0.10f, 0.26f, 0.9f));
            canvas.Text(skill.Description, rx + 8, y + 66, rw - 16, 88, 15, Canvas.White);
            // 이 게임에서 하는 일 — 스킬 규칙에서 읽어 한 줄로(크기는 대부분 임시 값)
            if (rule != null) canvas.Text(voyage.SkillEffect(skill.Id), rx, y + 160, rw, 34, 12, new Color4(0.55f, 1f, 0.65f, 1));
            bool active = rule != null && voyage.SeaSkills().Contains(rule);
            canvas.Text(rule == null ? "(이 게임에서는 아직 하는 일이 없다)" : rule.Effect is "Aid" or "Mine" or "Flee" ? "사용 스킬 — 해전 중에 눌러 쓴다" : rule.Effect == "Gather" ? "사용 스킬 — 상륙지 · 바다에서 쓴다" : active || rule.Effect is "Procure" or "Fish" or "Repair" or "Rest" ? "사용 스킬 — 바다에서 눌러 쓴다" : "자동효과",
                        rx, rule == null ? y + 162 : y + 26, rw, 22, rule == null ? 14 : 12, rule == null ? Canvas.Dim : Canvas.Gold, 2);      // 효과 글이 그 줄을 쓰니 이름 줄 오른쪽으로 올렸다
            canvas.Text("상태", rx, y + 196, 100, 22, 15, Canvas.Gold, 0, true);
            canvas.Text(rank > 0 ? $"Rank {rank}" + (voyage.Refined(skill.Id) ? "(연성 +2)" : "") + (voyage.Skills[skill.Id].Rank >= voyage.Data.Settings.MaxSkillRank ? "   ※최대 랭크" : $"   {voyage.Skills[skill.Id].Exp:0}/{voyage.ExpNeed(voyage.Skills[skill.Id])}") : "익히지 않았다",
                        rx + 10, y + 222, rw - 10, 22, 15, Canvas.White);
            canvas.Text("습득", rx, y + 258, 100, 22, 15, Canvas.Gold, 0, true);
            canvas.Text($"{skill.Cost:N0} 두캇 — {Voyage.TeacherName(skill.Group == 3 ? 0 : skill.Group)} 마스터에게 배운다", rx + 10, y + 284, rw - 10, 22, 15, Canvas.White);
            // 배우기 단추는 그 갈래의 조합 마스터 앞에서만 나온다
            if (rank == 0 && rule != null && voyage.CanLearn(skill) &&
                canvas.Button(voyage.LearnNeed(skill) is { } lacks ? $"배우기 — {lacks}" : $"배우기 ({skill.Cost:N0})", rx, y + 320, voyage.LearnNeed(skill) != null ? 360 : 200, 32, voyage.Mode == Mode.Port && voyage.Money >= skill.Cost && voyage.LearnNeed(skill) == null, 14))
                voyage.Learn(skill);
            // 연성 — 끝까지 올린 스킬을 상트 페테르부르크에서
            if (rank > 0 && voyage.CanRefineSoon(skill.Id))
            {
                string? why = voyage.RefineBlocker(skill.Id);
                if (canvas.Button(voyage.Text(342, "연성 스킬").Replace(" 스킬", "") + " (+2)", rx, y + 320, 200, 32, why == null, 14)) voyage.Refine(skill.Id);
                canvas.Text(why ?? voyage.Text(6625, "스킬을 연성하면 랭크와 숙련도가 초기 수치로 돌아갑니다."), rx + 210, y + 322, rw - 210, 34, 12, why != null ? new Color4(1f, 0.6f, 0.5f, 1) : Canvas.Dim);
            }
        }
        canvas.Text($"소지금 {voyage.Money:N0} Ð", rx, y + h - 78, rw, 22, 14, Canvas.Dim, 2);
        if (canvas.Button("이전", x + w - 130, y + h - 50, 110, 34)) voyage.Dialog = Dialog.None;
    }

    private void SkillIcon(int id, float x, float y, float opacity, float scale = 1) =>
        canvas.Image($"sa{id}", () => (_skillIcons ??= new ImageSet(@"0010\0001\sa")).Pixels(0, id), x, y, 28 * scale, 33 * scale, 0, false, opacity);

    /// <summary>쪽 넘김 단추. 지금 쪽을 돌려준다.</summary>
    private int Pager(float x, float y, float h, int page, int pages)
    {
        page = Math.Clamp(page, 0, pages - 1);
        if (pages <= 1) return page;
        if (canvas.Button("◀", x + 20, y + h - 50, 40, 34, page > 0)) page--;
        canvas.Text($"{page + 1} / {pages}", x + 64, y + h - 43, 60, 24, 15, Canvas.White, 1);
        if (canvas.Button("▶", x + 128, y + h - 50, 40, 34, page < pages - 1)) page++;
        return page;
    }

    private int _shipChosen;

    /// <summary>레시피 쪽지 — 가진 레시피의 목록, 하나를 열면 재료와 생산 단추.</summary>
    private void RecipeTab(float x, float y, float w, float h, float row, Action<int> pages)
    {
        const int perPage = 10;
        if (_recipeOpen is { } open)
        {
            var recipe = voyage.Data.Recipes.Find(r => r.Id == open.RecipeId);
            canvas.Text(recipe?.Name ?? open.Name, x + 20, row, w - 40, 26, 17, Canvas.White, 0, true);
            canvas.Text((recipe?.Description ?? "").Replace('\n', ' '), x + 20, row + 28, w - 40, 22, 13, Canvas.Dim);
            row += 62;
            canvas.Text("재료", x + 20, row, 100, 22, 15, Canvas.Gold, 0, true);
            row += 26;
            foreach (var (good, count) in open.InputList())
            {
                int have = voyage.HaveInput(good);
                if (Voyage.IsGoodId(good)) GoodIcon(good, x + 20, row);
                canvas.Text($"{voyage.Good(good)?.Name ?? voyage.ItemName(good)} × {count}", x + 52, row + 3, 260, 22, 15, Canvas.White);
                canvas.Text($"{(Voyage.IsGoodId(good) ? "실은 수" : "가진 수")} {have}", x + 320, row + 4, 200, 22, 13, have >= count ? Canvas.Dim : new Color4(1f, 0.5f, 0.45f, 1));
                row += 30;
            }
            row += 8;
            canvas.Text("만들어지는 것", x + 20, row, 160, 22, 15, Canvas.Gold, 0, true);
            row += 26;
            if (open.OutputItem > 0)
            {
                int made = open.OutputItem;
                canvas.Image($"sb{made / 100000}:{made}", () => (_itemIcons ??= new ImageSet(@"0010\0001\sb")).Pixels(made / 100000, made), x + 20, row, 28, 28);
                canvas.Text($"{voyage.ItemName(made)} × {open.OutputCount}  ({(voyage.IsPartId(made) ? "선박 부품" : "아이템")})", x + 52, row + 3, 300, 22, 15, Canvas.White);
            }
            else
            {
                GoodIcon(open.Output, x + 20, row);
                canvas.Text($"{voyage.Good(open.Output)?.Name} × {open.OutputCount}", x + 52, row + 3, 300, 22, 15, Canvas.White);
            }
            if (open.Facility != "")
            {
                // 연금술 실험 — 설비와 도구(도구는 들지 않는다)
                int tier = voyage.LabTier(open.Facility);
                float lx = x + 420, ly = y + 108;
                canvas.Text("연금술 실험", lx, ly, 200, 22, 15, Canvas.Gold, 0, true);
                canvas.Text($"{Voyage.LabName(open.Facility)}  " + (tier == 0 ? "없음" : new[] { "", "간이", "보통", "개량" }[tier] + $" · 실패 {voyage.LabFailChance(open)}%"), lx, ly + 26, 260, 22, 14, tier > 0 ? Canvas.White : new Color4(1f, 0.5f, 0.45f, 1));
                float ty2 = ly + 52;
                foreach (int tool in open.ToolList())
                {
                    bool has = voyage.Items.GetValueOrDefault(tool) > 0;
                    canvas.Image($"sb{tool / 100000}:{tool}", () => (_itemIcons ??= new ImageSet(@"0010\0001\sb")).Pixels(tool / 100000, tool), lx, ty2, 28, 28);
                    canvas.Text(voyage.ItemName(tool) + (has ? "" : "  (없음)"), lx + 34, ty2 + 3, 220, 22, 14, has ? Canvas.White : new Color4(1f, 0.5f, 0.45f, 1));
                    ty2 += 30;
                }
            }
            int can = voyage.CanProduce(open);
            string needs = voyage.RecipeSkill(open) is { } need ? $"필요 스킬: {voyage.SkillName(need.SkillId)} 랭크 {(open.RankGuessed ? "모름(1 로 둠)" : need.Rank)} (지금 {voyage.Rank(need.SkillId)})   " : "";
            canvas.Text(needs.TrimEnd(), x + 20, row + 36, w - 40, 22, 13, Canvas.Dim);
            canvas.Text($"지금 {can}번 만들 수 있다 · 창고 {voyage.HoldUsed}/{voyage.TotalHold} · 행동력 {voyage.Vigour:0}(한 번에 {voyage.ProduceVigourOf(open)}) · 숙련도 +{voyage.ProduceExp(open) * voyage.Data.Settings.Gain}(대성공 +{voyage.ProduceExp(open, true) * voyage.Data.Settings.Gain})", x + 20, row + 56, w - 40, 22, 13, Canvas.Dim);
            if (voyage.ProduceBlocker(open, 1) is { } why) canvas.Text(why, x + 20, row + 78, w - 40, 22, 13, new Color4(1f, 0.5f, 0.45f, 1));
            // 자동 생산 — 켜 두면 0.5초에 한 번씩 만든다. 못 만들게 되거나(재료 · 창고 · 행동력) 다른 레시피로 가면 꺼진다
            bool canOne = voyage.ProduceBlocker(open, 1) == null;
            if (!ReferenceEquals(_autoFor, open) || !canOne || voyage.Clock - _autoSeen > 0.4) (_autoProduce, _autoFor) = (false, open);      // 창을 닫았다 열어도 꺼져 있다
            _autoSeen = voyage.Clock;
            if (canvas.Button(_autoProduce ? "■ 생산 멈춤" : "▶ 자동 생산", x + 20, y + h - 50, 100, 34, canOne, 13)) (_autoProduce, _autoNext) = (!_autoProduce, voyage.Clock);
            if (_autoProduce) canvas.Frame(x + 20, y + h - 50, 100, 34, Canvas.Gold, 1.6f);
            if (_autoProduce && voyage.Clock >= _autoNext) { _autoNext = voyage.Clock + 0.5; voyage.Produce(open, 1); }
            if (canvas.Button("10번", x + 126, y + h - 50, 70, 34, can >= 10 && voyage.ProduceBlocker(open, 10) == null, 14)) voyage.Produce(open, 10);
            // 「전부」는 행동력이 닿는 데까지
            int all = Math.Min(can, (int)(voyage.Vigour / (voyage.ProduceVigourOf(open, 100) / 100.0)));
            if (canvas.Button("전부", x + 202, y + h - 50, 70, 34, all > 0 && voyage.ProduceBlocker(open, all) == null, 14)) voyage.Produce(open, all);
            if (canvas.Button("목록으로", x + 290, y + h - 50, 100, 34, true, 14)) _recipeOpen = null;
            return;
        }

        // 원본의 짜임: 왼쪽에 책 그림, 이름, 그 밑에 필요 스킬(그림과 랭크), 오른쪽에 필요 재료(그림과 수). 줄을 누르면 연다
        // 찾기 칸 — 레시피 이름, 만들어지는 것, 재료의 이름에서 찾는다
        float sx = x + w - 250, sw = 230;
        bool overSearch = canvas.Hover(sx, y + 12, sw, 24);
        if (canvas.Pointer.Clicked) _recipeSearching = overSearch;
        canvas.Fill(sx, y + 12, sw, 24, new Color4(0.02f, 0.04f, 0.14f, 0.95f));
        canvas.Frame(sx, y + 12, sw, 24, _recipeSearching ? Canvas.Gold : Canvas.PanelEdge, 1.2f);
        canvas.Text(_recipeSearch == "" && !_recipeSearching ? "🔍 레시피 · 생산물 · 재료 찾기" : _recipeSearch + (_recipeSearching && (int)(voyage.Clock * 2) % 2 == 0 ? "|" : ""), sx + 6, y + 14, sw - 30, 20, 13, _recipeSearch == "" && !_recipeSearching ? Canvas.Dim : Canvas.White);
        if (_recipeSearch != "")
        {
            canvas.Text("✕", sx + sw - 22, y + 14, 20, 20, 13, Canvas.Dim, 1);
            if (canvas.Hover(sx + sw - 22, y + 12, 22, 24) && canvas.Pointer.Clicked) (_recipeSearch, _itemPage) = ("", 0);
        }
        bool Matches(int id)
        {
            if (_recipeSearch == "") return true;
            bool Has(string? text) => text != null && text.Contains(_recipeSearch, StringComparison.OrdinalIgnoreCase);
            if (Has(voyage.Data.Recipes.Find(r => r.Id == id)?.Name)) return true;
            if (voyage.RuleOf(id) is not { } made) return false;
            return Has(made.OutputItem > 0 ? voyage.ItemName(made.OutputItem) : voyage.Good(made.Output)?.Name) || made.InputList().Any(i => Has(voyage.Good(i.Item1)?.Name));
        }
        var owned = voyage.KnownRecipes().Where(Matches).ToList();
        if (_recipeSearch != "") canvas.Text($"{owned.Count}가지", sx - 70, y + 15, 64, 20, 12, Canvas.Dim, 2);
        const int shown = 6;
        int count2 = Math.Max(1, (owned.Count + shown - 1) / shown);
        _itemPage = Math.Clamp(_itemPage, 0, count2 - 1);
        foreach (int id in owned.Skip(_itemPage * shown).Take(shown))
        {
            var recipe = voyage.Data.Recipes.Find(r => r.Id == id);
            var rule = voyage.RuleOf(id);
            bool hover = rule != null && canvas.Hover(x + 16, row, w - 32, 52);
            canvas.Fill(x + 16, row, w - 32, 52, hover ? new Color4(0.12f, 0.62f, 0.55f, 0.95f) : new Color4(0.04f, 0.07f, 0.2f, 0.85f));
            canvas.Image("sb17:book", () => (_itemIcons ??= new ImageSet(@"0010\0001\sb")).Pixels(17, 1700000), x + 20, row + 4, 44, 44);
            canvas.Text(recipe?.Name ?? $"레시피 {id}", x + 72, row + 2, 300, 22, 15, Canvas.White);
            if (rule == null) canvas.Text("만들 것이 정해지지 않았다", x + 72, row + 26, 300, 20, 12, Canvas.Dim);
            else
            {
                if (voyage.RecipeSkill(rule) is { } need)
                {
                    SkillIcon(need.SkillId, x + 72, row + 23, voyage.Rank(need.SkillId) >= need.Rank ? 1 : 0.45f, 0.84f);      // 28 × 33 을 그대로 두면 줄(52) 아래로 삐져나온다
                    canvas.Text($"{need.Rank}", x + 104, row + 27, 40, 20, 14, voyage.Rank(need.SkillId) >= need.Rank ? Canvas.White : new Color4(1f, 0.5f, 0.45f, 1));
                }
                float mx = x + 380;
                foreach (var (good, count) in rule.InputList().Take(5))
                {
                    int have = voyage.Cargo.TryGetValue(good, out var item) ? item.Count : 0;
                    canvas.Fill(mx, row + 4, 44, 44, new Color4(0.02f, 0.04f, 0.12f, 0.95f));
                    GoodIcon(good, mx + 2, row + 7, 40);
                    canvas.Text($"{count}", mx, row + 28, 42, 20, 14, have >= count ? Canvas.White : new Color4(1f, 0.5f, 0.45f, 1), 2, true);
                    if (canvas.Hover(mx, row + 4, 44, 44)) _tip = ($"{voyage.Good(good)?.Name} (실은 수 {have})", mx + 22, row + 2);
                    mx += 48;
                }
                if (hover && canvas.Pointer.Clicked) { canvas.Pointer.Consumed = true; _recipeOpen = rule; }
            }
            row += 56;
        }
        if (owned.Count == 0) canvas.Text(_recipeSearch != "" ? "찾는 말에 맞는 레시피가 없다." : "가진 레시피가 없다. 「아이템 추가」의 「레시피 보기」에서 넣는다.", x + 20, row, w - 40, 24, 15, Canvas.Dim);
        pages(count2);
    }
    private int _itemTab, _itemPage;
    /// <summary>대본용: 소지품 창의 쪽지.</summary>
    public int ItemTab { set => (_itemTab, _itemPage) = (value, 0); }
    private bool _addRecipes, _addMaterials;
    private string? _addRecipeFor;
    private List<RecipeData> _addRecipeFound = [];

    /// <summary>아이템 추가에 늘어놓는 레시피 — 만들 것이 정해진 것이 먼저, 그 뒤로 번호 차례.</summary>
    private IEnumerable<RecipeData> RecipesToAdd() =>
        voyage.Data.Recipes.Where(r => r.Name != "" && voyage.BooksOf(r.Id).Count == 0).OrderBy(r => voyage.RuleOf(r.Id) == null).ThenBy(r => r.Id);      // 책에 든 것은 책으로 넣는다

    private string RecipeNote(RecipeData recipe)
    {
        if (voyage.RuleOf(recipe.Id) is { } rule)
            return $"→ {(rule.OutputItem > 0 ? voyage.ItemName(rule.OutputItem) : voyage.Good(rule.Output)?.Name)} × {rule.OutputCount}";
        if (voyage.Data.RecipeNotes.TryGetValue(recipe.Id, out var note)) return $"→ {note.Makes}{(note.Skill == "" ? "" : $" ({note.Skill})")} — 재료 모름";
        return recipe.Description.Replace("\n", " ") + " — 재료 모름";
    }
    private RecipeRule? _recipeOpen;
    /// <summary>대본용 — 소지품 창의 레시피 쪽에서 그 레시피를 연 것으로.</summary>
    public RecipeRule? RecipeOpen { set => (_itemTab, _itemPage, _recipeOpen) = (1, 0, value); }

    /// <summary>소지품 창(I) — 가진 것을 쓰는 쪽지와, 전직증을 마음대로 넣는 「아이템 추가」 쪽지.</summary>
    private void ItemWindow()
    {
        string[] tabs = ["소지품", "레시피", "도구점", "아이템 추가", "선박"];
        if (voyage.ItemShopOpen) (voyage.ItemShopOpen, _itemTab, _itemPage) = (false, 2, 0);
        // 「아이템 추가」는 쪽지가 아니라 따로 뜨는 넓은 창이다(목록이 길고 설명이 붙는다)
        bool adding = _itemTab == 3;
        float w = adding ? Math.Min(980, canvas.Width - 16) : 640;
        int gridRows = Math.Clamp(voyage.Data.Settings.ItemRows, 4, 8);
        float h = _itemTab == 0 ? Math.Max(440, 136 + gridRows * 68) : adding ? Math.Min(568, canvas.Height - 44) : 440;
        float x = (canvas.Width - w) / 2, y = Math.Max(34, (canvas.Height - h) / 2);
        canvas.Block(x, y - 30, w, h + 30);
        for (int i = 0; i < tabs.Length && !adding; i++)
        {
            bool on = _itemTab == i;
            canvas.Fill(x + i * 128, y - 30, 124, 30, on ? new Color4(0.16f, 0.62f, 0.72f, 0.98f) : new Color4(0.35f, 0.55f, 0.65f, 0.9f));
            canvas.Frame(x + i * 128, y - 30, 124, 30, Canvas.PanelEdge, 1);
            canvas.Text(tabs[i], x + i * 128, y - 27, 124, 24, 15, on ? Canvas.White : new Color4(0.05f, 0.1f, 0.2f, 1), 1, on, on);
            if (canvas.Hover(x + i * 128, y - 30, 124, 30) && canvas.Pointer.Clicked) (_itemTab, _itemPage, _addSearching) = (i, 0, false);
        }
        canvas.Panel(x, y, w, h);
        canvas.Text(_itemTab == 3 ? "아이템 추가" : _itemTab == 4 ? "선박" : $"직업: {voyage.JobName}", x + 20, y + 12, 130, 24, 16, Canvas.Gold, 0, true);
        if (_itemTab == 1)
        {
            // 선창이 얼마나 찼는가 — 막대와 수(생산물이 들어갈 자리를 보면서 만든다)
            float fill = voyage.TotalHold > 0 ? Math.Clamp(voyage.HoldUsed / (float)voyage.TotalHold, 0, 1) : 0;
            canvas.Text("선창", x + 158, y + 15, 36, 20, 13, Canvas.Dim);
            canvas.Fill(x + 194, y + 18, 110, 12, new Color4(0.02f, 0.04f, 0.14f, 0.95f));
            canvas.Fill(x + 194, y + 18, 110 * fill, 12, fill >= 0.98f ? new Color4(1f, 0.45f, 0.4f, 1) : new Color4(0.35f, 0.75f, 0.95f, 1));
            canvas.Frame(x + 194, y + 18, 110, 12, Canvas.PanelEdge, 1);
            canvas.Text($"{voyage.HoldUsed}/{voyage.TotalHold}", x + 310, y + 15, 80, 20, 13, Canvas.White);
        }

        const int perPage = 10;
        float row = y + 46;
        if (_itemTab == 0)
        {
            // 원본처럼 아이콘 격자(다섯 칸 × 다섯 줄). 아이템 뒤에 선박 권리서(부두의 배)가 온다. 올리면 설명, 누르면 고른다
            var items = voyage.Items.OrderBy(i => i.Key).ToList();
            const int columns = 5, tile = 64;
            int lines = gridRows;                  // 줄 수는 환경설정에서 고른다
            int total = items.Count + voyage.Dock.Count;
            int pages = Math.Max(1, (total + columns * lines - 1) / (columns * lines));
            _itemPage = Math.Clamp(_itemPage, 0, pages - 1);
            canvas.Fill(x + 20, y + 44, 150, 24, new Color4(0.72f, 0.76f, 0.84f, 0.92f));
            canvas.Text("소유물품", x + 20, y + 45, 150, 22, 15, new Color4(0.05f, 0.08f, 0.2f, 1), 1, true, false);
            canvas.Text($"{total} / 100", x + 180, y + 46, 100, 22, 14, total > 100 ? new Color4(1f, 0.5f, 0.45f, 1) : Canvas.Dim);
            int pointed = -1;
            for (int k = 0; k < columns * lines; k++)
            {
                int index = _itemPage * columns * lines + k;
                float tx = x + 20 + k % columns * (tile + 4), ty = y + 76 + k / columns * (tile + 4);
                canvas.Fill(tx, ty, tile, tile, new Color4(0.03f, 0.10f, 0.08f, 0.9f));
                if (index >= total) continue;
                if (index == _itemChosen) canvas.Frame(tx, ty, tile, tile, new Color4(0.3f, 0.95f, 0.9f, 1), 2);
                if (index < items.Count)
                {
                    int id = items[index].Key;
                    // 전직증은 증서 그림(무리 24 — 어느 것이 전직증인지는 짐작), 돛 도료는 돛 그림(무리 6)
                    bool paper = id >= Voyage.JobPaper && id < Voyage.MaterialItem - 100000 && voyage.ItemOf(id) == null, paint = voyage.ItemOf(id)?.Effect == "SailPaint";
                    // 클라이언트의 아이템 번호는 십만 자리가 그림 무리다(15 소비품 · 22 조선 부품 · 0 ~ 5 의상과 장비 …)
                    var (group, picture) = paper ? (24, 2400000) : paint ? (14, 1400100) : id < Voyage.JobPaper ? (id / 100000, id)
                        : id == Voyage.MedalPaper ? (15, 1500263) : id == Voyage.ShipPermit ? (15, 1500264) : (15, id);
                    if (id > Voyage.MaterialItem && id < Voyage.MaterialItem + 1000 && voyage.MaterialOf(id - Voyage.MaterialItem) is { } wood && MaterialIcon(wood, tx + 4, ty + 4, tile - 8)) { }
                    else if (paint && SailThumb(Voyage.DyePatterns((int)voyage.ItemOf(id)!.Amount)[0], 5, tx + 4, ty + 4, tile - 8))
                        canvas.Text(voyage.ItemName(id).Replace("특수 돛 도료", "특수").Replace("돛 도료", "").Trim(), tx + 2, ty + 2, tile - 6, 16, 11, Canvas.Gold, 2, true);
                    else if (!canvas.Image($"sb{group}:{picture}", () => (_itemIcons ??= new ImageSet(@"0010\0001\sb")).Pixels(group, picture), tx + 4, ty + 4, tile - 8, tile - 8))
                        canvas.Text(voyage.ItemName(id), tx + 2, ty + 14, tile - 4, 40, 11, Canvas.White, 1);
                    else if (paper || paint) canvas.Text(voyage.ItemName(id).Replace(" 전직증", "").Replace("돛 도료", "돛 도료"), tx + 2, ty + 2, tile - 4, 16, 10, Canvas.Gold, 1);
                    if (items[index].Value > 1) canvas.Text($"{items[index].Value}", tx, ty + tile - 22, tile - 4, 20, 15, Canvas.White, 2, true);
                }
                else
                {
                    // 선박 권리서의 원본 그림 — 아이템 그림 묶음의 무리 12, 번호 1200001
                    if (!canvas.Image("sb:deed", () => (_itemIcons ??= new ImageSet(@"0010\0001\sb")).Pixels(12, 1200001), tx + 4, ty + 4, tile - 8, tile - 8))
                        canvas.Text("권리서", tx, ty + tile - 20, tile - 4, 18, 11, Canvas.Gold, 2);
                }
                if (canvas.Hover(tx, ty, tile, tile))
                {
                    pointed = index;
                    if (canvas.Pointer.Clicked)
                    {
                        // 같은 칸을 잇달아 두 번 누르면 쓴다
                        long now = Environment.TickCount64;
                        if (_itemChosen == index && now - _itemClicked < 450 && index < items.Count) { voyage.UseItem(items[index].Key); _itemClicked = 0; }
                        else (_itemChosen, _itemClicked) = (index, now);
                    }
                }
            }
            // 올려 둔 칸 곁에 뜨는 설명 쪽지 — 창을 다 그린 뒤에 맨 위에 그린다
            if (pointed >= 0 && pointed < items.Count)
            {
                int hid = items[pointed].Key, hk = pointed - _itemPage * columns * lines;
                string hname = voyage.ItemName(hid), hnote = voyage.ItemNote(hid);
                const float tw = 330;
                float th = 40 + hnote.Split('\n').Sum(line => MathF.Ceiling(Math.Max(1, line.Length) / 25f)) * 19;
                float hx = Math.Min(x + 20 + hk % columns * (tile + 4) + tile + 6, canvas.Width - tw - 6);
                float hy = Math.Min(y + 76 + hk / columns * (tile + 4), canvas.Height - th - 6);
                _itemTip = (hname + (items[pointed].Value > 1 ? $"  × {items[pointed].Value}" : ""), hnote, hx, hy, tw, th);
            }
            // 오른쪽: 고른 것의 이름 · 설명 · 사용시 효과
            float px = x + 380, pw = w - 400;
            int shown = pointed >= 0 ? pointed : _itemChosen;
            if (shown >= 0 && shown < total)
            {
                bool isShip = shown >= items.Count;
                string title = isShip ? voyage.Dock[shown - items.Count].Ship.Name : voyage.ItemName(items[shown].Key);
                canvas.Text(title, px, y + 46, pw, 26, 17, Canvas.White, 0, true);
                canvas.Text(isShip ? "선박 권리서" : $"가진 수 {items[shown].Value}", px, y + 72, pw, 20, 13, Canvas.Dim);
                canvas.Fill(px, y + 100, 110, 22, new Color4(0.72f, 0.76f, 0.84f, 0.92f));
                canvas.Text("설명", px, y + 101, 110, 20, 14, new Color4(0.05f, 0.08f, 0.2f, 1), 1, true, false);
                if (isShip)
                {
                    var docked = voyage.Dock[shown - items.Count];
                    var s = voyage.StatsOf(docked);
                    canvas.Text("배의 소유권을 증명하기 위한 권리서.", px, y + 128, pw, 22, 14, Canvas.White);
                    string[] sizes = ["소형", "소형", "중형", "대형", "대형"];
                    canvas.Text($"선박종류   {docked.Ship.Name}({sizes[Math.Clamp(docked.Ship.SizeClass, 0, 4)]})\n내구력   {docked.Durability:0} / {s.Durability}\n적재량분배   선원 {s.MaxCrew} · 대포 {s.Guns} · 창고 {s.Hold}\n필요 레벨   {s.Levels.Adventure} / {s.Levels.Trade} / {s.Levels.Battle}\n강화 횟수   {docked.Work?.Times ?? 0} / {voyage.MaxTimesOf(docked.Ship)}\n성능   세로 {s.VerticalSail} · 가로 {s.HorizontalSail} · 선회 {s.Turn} · 내파 {s.WaveResist}\n재질   {voyage.MaterialOf(docked.Material)?.Name ?? "기본"}",
                                px, y + 156, pw, 170, 13, Canvas.White);
                }
                else
                {
                    int id = items[shown].Key;
                    canvas.Fill(px, y + 128, pw, 70, new Color4(0.04f, 0.07f, 0.2f, 0.85f));
                    // 장비는 다른 표(장비 표)의 설명 글 — 원본처럼 「설명」 칸에, 능력치 · 스킬 보정은 「사용시 효과」 칸에
                    string? worn = voyage.GearOf(id) is { Description.Length: > 0 } piece ? piece.Description.Replace("\n", " ") : null;
                    canvas.Text(worn ?? (voyage.Data.ItemNotes.TryGetValue(voyage.ItemOf(id) is { SoldAs: > 0 } alias ? alias.SoldAs : id, out string? told) ? told.Replace("\n", " ") : voyage.ItemName(id)), px + 8, y + 132, pw - 16, 62, 13, Canvas.White);
                    canvas.Fill(px, y + 208, 130, 22, new Color4(0.72f, 0.76f, 0.84f, 0.92f));
                    canvas.Text("사용시 효과", px, y + 209, 130, 20, 14, new Color4(0.05f, 0.08f, 0.2f, 1), 1, true, false);
                    string note = voyage.ItemNote(id);
                    // 음식은 「설명」 칸에 같은 글이 이미 있다 — 여기에는 먹으면 얻는 것을 수로. 의뢰 알선서는 조합 창에서 쓴다
                    if (worn != null && note.StartsWith(worn)) note = note[worn.Length..].Trim();
                    if (voyage.FoodEffectText(id) is { Length: > 0 } eaten) note = eaten;
                    else if (id == Voyage.QuestPermit) note = "조합의 의뢰 창에서 「의뢰 알선서 사용」을 누르면 의뢰가 새로 나온다.";
                    else if (voyage.Data.ItemNotes.TryGetValue(id, out string? same) && same.Replace("\n", " ") == note) note = "";
                    canvas.Text(note == "" ? "없음" : note, px + 4, y + 236, pw - 8, 84, 13, Canvas.White);
                    // 장비: 스킬 보정을 모르면 요청 딱지
                    if (voyage.GearOf(id) != null && !voyage.Data.GearBoosts.ContainsKey(id)) Invented("gear", id, voyage.ItemName(id), px + 138, y + 208);
                    if (shown == _itemChosen && canvas.Button("사용", px, y + 326, 110, 30, true, 14)) voyage.UseItem(id);
                }
            }
            else canvas.Text("물품 위에 마우스를 올리면 설명이 나온다.\n누르면 고르고 「사용」한다.", px, y + 60, pw, 60, 14, Canvas.Dim);
            if (total == 0) canvas.Text("가진 것이 없다. 「도구점」에서 사거나 「아이템 추가」에서 전직증을 넣는다.", x + 20, row, w - 40, 24, 15, Canvas.Dim);
            Pages(pages);
        }
        else if (_itemTab == 1) RecipeTab(x, y, w, h, row, Pages);
        else if (_itemTab == 2)
        {
            bool open = voyage.Mode == Mode.Port && voyage.HasItemShop;
            if (!open) { canvas.Text(voyage.Mode == Mode.Port ? "이 도시에는 도구점이 없다." : "도구점은 항구에서 연다.", x + 20, row, w - 40, 24, 15, Canvas.Dim); row += 30; }
            // 한 쪽에 열 줄 — 휠이나 아래 단추로 넘긴다
            int shopPages = Math.Max(1, (voyage.Data.Items.Count(voyage.ShopSells) + perPage - 1) / perPage);
            _itemPage = Math.Clamp(_itemPage, 0, shopPages - 1);
            Pages(shopPages);
            foreach (var item in voyage.Data.Items.Where(voyage.ShopSells).Skip(_itemPage * perPage).Take(open ? perPage : perPage - 1))
            {
                canvas.Text(item.Name, x + 20, row + 3, 200, 24, 15, Canvas.White);
                canvas.Text($"{voyage.ItemNote(item.Id)}   가진 수 {voyage.Items.GetValueOrDefault(item.Id)}", x + 200, row + 5, 300, 22, 12, Canvas.Dim);
                if (canvas.Button($"사기 ({item.Price:N0})", x + w - 140, row, 120, 26, open && voyage.Money >= item.Price, 13)) voyage.BuyItem(item);
                row += 32;
            }
            canvas.Text($"소지금 {voyage.Money:N0} Ð", x + 200, y + h - 43, 250, 22, 14, Canvas.Dim);
        }
        if (_itemTab == 4)
        {
            // 「선박 추가」: 배 표의 배를 값 없이 부두에 받는다 — 이름으로 찾고, 크기로 거른다
            float sw = 240, sx = x + w - 20 - sw;
            sw = 190; sx = x + w - 20 - sw;
            bool overSearch = canvas.Hover(sx, y + 12, sw, 24);
            if (canvas.Pointer.Clicked) _addSearching = overSearch;
            canvas.Fill(sx, y + 12, sw, 24, new Color4(0.02f, 0.04f, 0.14f, 0.95f));
            canvas.Frame(sx, y + 12, sw, 24, _addSearching ? Canvas.Gold : Canvas.PanelEdge, 1.2f);
            canvas.Text(_addSearch == "" && !_addSearching ? "🔍 배 이름으로 찾기" : _addSearch + (_addSearching && (int)(voyage.Clock * 2) % 2 == 0 ? "|" : ""), sx + 6, y + 14, sw - 30, 20, 14, _addSearch == "" && !_addSearching ? Canvas.Dim : Canvas.White);
            if (_addSearch != "")
            {
                canvas.Text("✕", sx + sw - 22, y + 14, 20, 20, 13, Canvas.Dim, 1);
                if (canvas.Hover(sx + sw - 22, y + 12, 22, 24) && canvas.Pointer.Clicked) (_addSearch, _itemPage) = ("", 0);
            }
            string[] sizes = ["전체", "소형", "중형", "대형", "보유"];
            for (int i = 0; i < sizes.Length; i++)
            {
                bool on = _shipAddSize == i, over = canvas.Hover(x + 110 + i * 56, y + 13, 52, 22);
                canvas.Fill(x + 110 + i * 56, y + 13, 52, 22, on ? new Color4(0.16f, 0.62f, 0.72f, 0.98f) : over ? new Color4(0.22f, 0.32f, 0.66f, 0.95f) : new Color4(0.06f, 0.10f, 0.28f, 0.9f));
                canvas.Text(sizes[i], x + 110 + i * 56, y + 14, 52, 20, 13, Canvas.White, 1);
                if (over && canvas.Pointer.Clicked) (_shipAddSize, _itemPage) = (i, 0);
            }
            static int SizeOf(Dho.Data.ShipData s) => s.SizeClass <= 1 ? 1 : s.SizeClass == 2 ? 2 : 3;
            // 「보유」: 타고 있는 배와 부두의 배 — 다른 거르기는 받을 수 있는 배 표
            var ships = _shipAddSize == 4 ? voyage.Dock.Select(d => d.Ship).Prepend(voyage.Ship).Where(s => s.Name.Contains(_addSearch, StringComparison.OrdinalIgnoreCase)).ToList()
                : voyage.Data.Ships.Where(s => s.Model > 0 && s.Name.Contains(_addSearch, StringComparison.OrdinalIgnoreCase) && (_shipAddSize == 0 || SizeOf(s) == _shipAddSize)).ToList();
            int shipPages = Math.Max(1, (ships.Count + perPage - 1) / perPage);
            _itemPage = Math.Clamp(_itemPage, 0, shipPages - 1);
            Pages(shipPages);
            foreach (var ship in ships.Skip(_itemPage * perPage).Take(perPage))
            {
                var s = voyage.StatsOf(ship, 0, 0);
                canvas.Text(ship.Name, x + 20, row + 3, 220, 24, ship.Name.Length > 13 ? 12 : 15, Canvas.White);
                canvas.Text($"{sizes[SizeOf(ship)]} · 내구 {s.Durability} · 돛 {s.VerticalSail}/{s.HorizontalSail} · 창고 {s.Hold}", x + 240, row + 5, 270, 22, 12, Canvas.Dim);
                if (_shipAddSize == 4) canvas.Text(row == y + 46 && _itemPage == 0 && _addSearch == "" ? "타고 있다" : "부두", x + w - 110, row + 4, 90, 22, 13, Canvas.Gold, 1);
                else if (canvas.Button("추가", x + w - 110, row, 90, 26, voyage.Dock.Count < Voyage.DockSlots, 13)) voyage.GiveShip(ship);
                row += 32;
            }
            if (ships.Count == 0) canvas.Text("맞는 배가 없다.", x + 20, row, w - 40, 24, 14, Canvas.Dim);
            canvas.Text($"{ships.Count:N0}척 · 부두 {voyage.Dock.Count}/{Voyage.DockSlots} — 받은 배는 선박교환에서 탄다", x + 180, y + h - 43, 300, 22, 12, Canvas.Dim);
        }
        if (_itemTab == 3)
        {
            // 「아이템 추가」: 갈래(전체 · 전직증 · 레시피 · 재질 …)와 그 안의 거르기, 이름으로 찾기. 「전체」는 모든 갈래에서 찾는다
            string[] kinds = ["전체", "전직", "레시피", "재질", "도료", "증서", "교환", "조빌", "의상", "연금"];
            int kind = _addAll ? 0 : _addLab ? 9 : _addGear ? 8 : _addShipItems ? 7 : _addTickets ? 6 : _addPapers ? 5 : _addDyes ? 4 : _addMaterials ? 3 : _addRecipes ? 2 : 1;
            bool Chip(string text, float cx, float cy, float cw, bool on)
            {
                bool over = canvas.Hover(cx, cy, cw, 22);
                canvas.Fill(cx, cy, cw, 22, on ? new Color4(0.16f, 0.62f, 0.72f, 0.98f) : over ? new Color4(0.22f, 0.32f, 0.66f, 0.95f) : new Color4(0.06f, 0.10f, 0.28f, 0.9f));
                canvas.Text(text, cx, cy + 1, cw, 20, 13, Canvas.White, 1);
                return over && canvas.Pointer.Clicked;
            }
            float chipX = x + 150;
            for (int i = 0; i < kinds.Length; i++)
            {
                float chipW = kinds[i].Length > 2 ? 54 : 44;
                if (Chip(kinds[i], chipX, y + 13, chipW, kind == i)) (_addAll, _addRecipes, _addMaterials, _addDyes, _addPapers, _addTickets, _addShipItems, _addGear, _addLab, _itemPage, _addGroup) = (i == 0, i == 2, i == 3, i == 4, i == 5, i == 6, i == 7, i == 8, i == 9, 0, 0);
                chipX += chipW + 3;
            }
            float sw = Math.Min(260, x + w - 20 - chipX - 12), sx = x + w - 20 - sw;
            bool overSearch = canvas.Hover(sx, y + 12, sw, 24);
            if (canvas.Pointer.Clicked) _addSearching = overSearch;
            canvas.Fill(sx, y + 12, sw, 24, new Color4(0.02f, 0.04f, 0.14f, 0.95f));
            canvas.Frame(sx, y + 12, sw, 24, _addSearching ? Canvas.Gold : Canvas.PanelEdge, 1.2f);
            canvas.Text(_addSearch == "" && !_addSearching ? "🔍 찾기" : _addSearch + (_addSearching && (int)(voyage.Clock * 2) % 2 == 0 ? "|" : ""), sx + 6, y + 14, sw - 30, 20, 14, _addSearch == "" && !_addSearching ? Canvas.Dim : Canvas.White);
            if (_addSearch != "")
            {
                canvas.Text("✕", sx + sw - 22, y + 14, 20, 20, 13, Canvas.Dim, 1);
                if (canvas.Hover(sx + sw - 22, y + 12, 22, 24) && canvas.Pointer.Clicked) (_addSearch, _itemPage) = ("", 0);
            }
            string[] groups = kind == 1 ? ["전체", "모험", "교역", "전투"] : kind == 3 ? ["전체", "나라", "의식용", "특수 도장", "제독", "그 밖"] : kind == 4 ? ["전체", "돛 도료", "특수"] : kind == 6 ? ["전체", "배를 찾은 것"] : kind == 7 ? ["전체", "선체", "돛", "선박재료", "그 밖"] : kind == 8 ? ["전체", "옷", "모자", "신발", "장갑", "무기", "장신구", "스킬 보정"] : ["전체"];
            _addGroup = Math.Clamp(_addGroup, 0, groups.Length - 1);
            for (int i = 0; i < groups.Length && groups.Length > 1; i++)
                if (Chip(groups[i], x + 20 + i * 84, y + 42, 80, _addGroup == i)) (_addGroup, _itemPage) = (i, 0);
            row = y + 72;
        }
        bool Found(string name) => _addSearch == "" || name.Contains(_addSearch, StringComparison.OrdinalIgnoreCase);
        int addPage = Math.Max(4, (int)((h - 72 - 58) / 32));      // 창 높이에 드는 만큼
        // 아이템 추가의 목록은 줄마다 바탕빛을 번갈아 — 긴 줄을 눈으로 따라가기 쉽게
        if (_itemTab == 3)
            for (int k = 0; k < addPage; k++)
                canvas.Fill(x + 14, row - 3 + k * 32, w - 28, 32, k % 2 == 0 ? new Color4(0.10f, 0.14f, 0.34f, 0.55f) : new Color4(0.03f, 0.05f, 0.16f, 0.35f));
        if (_itemTab != 3) { }
        else if (_addAll)
        {
            // 모든 갈래를 한 목록으로 — 찾는 말이 바뀔 때만 다시 모은다
            if (_addAllFor != _addSearch)
            {
                _addAllFor = _addSearch;
                _addAllFound.Clear();
                void Put(string tag, int id, string name, string note, int recipe = 0)
                {
                    if (Found(name) || Found(note) || Found(tag)) _addAllFound.Add((tag, id, name, note, recipe));      // 설명 글과 갈래 이름에서도 찾는다
                }
                foreach (var job in voyage.JobsToTake()) Put("전직", Voyage.JobPaper + job.Id, voyage.ItemName(Voyage.JobPaper + job.Id), job.Group switch { 0 => "모험 계열", 1 => "교역 계열", 2 => "전투 계열", _ => "" });
                foreach (var recipe in RecipesToAdd()) Put("레시피", 0, recipe.Name, RecipeNote(recipe), recipe.Id);
                foreach (var book in voyage.Data.RecipeBooks) Put("레시피", book.ItemId, book.Name, voyage.ItemNote(book.ItemId));
                foreach (var wood in voyage.SpecialMaterials()) Put("재질", Voyage.MaterialItem + wood.Id, wood.Name, $"내구 {wood.Durability * 100:0}% · 돛 {wood.Sail * 100:0}%");
                foreach (var dye in voyage.Data.Items.Where(i => i.Effect == "SailPaint")) Put("도료", dye.Id, dye.Name, "돛 도료");
                int[] books = [Voyage.DismantleBook, 1510029, 1510061, 1510062, 1510030, Voyage.RedesignBook, 1510035, 1510016, .. Voyage.ExtraPapers];
                foreach (var paper in voyage.Data.Items.Where(i => i.Effect == "Paper")) Put("증서", paper.Id, paper.Name, voyage.ItemNote(paper.Id));
                foreach (int book in books) if (voyage.Data.Papers.Find(p => p.Id == book) is { } found) Put("증서", found.Id, found.Name, voyage.ItemNote(found.Id));
                // 도구점의 소비 아이템(소화모래 · 쥐약 · 라임주스 · 음식 따위)도 여기서 찾아 넣는다
                foreach (var tool in voyage.Data.Items.Where(i => i.Effect is not ("SailPaint" or "Paper"))) Put("도구", tool.Id, tool.Name, voyage.ItemNote(tool.Id));
                foreach (var ticket in voyage.ShipTickets()) Put("교환", ticket.Id, ticket.Name + (voyage.TicketShip(ticket) is { } gives ? $" → {gives.Name}" : ""), ticket.Description.Replace("\n", " "));
                foreach (var part in voyage.Data.Papers.Where(p => p.Id is >= Voyage.ShipItems and < Voyage.ShipItems + 100_000)) Put("조빌", part.Id, part.Name, voyage.ItemNote(part.Id));      // 설명 + 강화 범위(「세로돛성능 강화 +40~+50」) — 찾기에도 걸린다
                foreach (var worn in voyage.Data.Gear) Put("의상", worn.Id, worn.Name, worn.Description.Replace("\n", " ") + voyage.GearLine(worn).Replace("\n", "  "));      // 스킬 보정도 찾는 말에 걸린다
                foreach (var part in voyage.Data.ShipParts) Put("부품", part.Id, part.Name, $"[{Voyage.SlotName[Math.Clamp(part.Slot, 0, Voyage.SlotName.Length - 1)]}] {voyage.PartNote(part)} — " + part.Description.Replace("\n", " "));
                foreach (var paint in voyage.Data.Papers.Where(p => p.Id is >= Voyage.HullEffectPaint and < Voyage.HullEffectPaint + 15)) Put("도료", paint.Id, paint.Name, $"[{Voyage.HullEffects[paint.Id - Voyage.HullEffectPaint].Name} 빛] " + paint.Description.Replace("\n", " "));
                foreach (var good in voyage.Data.Goods)
                {
                    var from = voyage.SourcesOf(good.Id);
                    string kind = voyage.Data.GoodKinds.Find(k => k.Id == good.Kind)?.Name ?? "";
                    Put("교역품", good.Id, good.Name, $"[{kind}] " + (from.Count == 0 ? "파는 도시 없음" : "산지: " + string.Join(" · ", from.Take(4).Select(c => c.Name)) + (from.Count > 4 ? $" 외 {from.Count - 4}곳" : "")));
                }
                foreach (var booster in voyage.Data.Boosters) Put("부스트", booster.Id, booster.Name, booster.Description.Replace("\n", " "));
                foreach (var food in voyage.Data.Foods) Put("음식", food.Id, food.Name, food.Description.Replace("\n", " "));
                foreach (var deco in voyage.Data.Decos) Put("데코", deco.Id, deco.Name, deco.Description.Replace("\n", " "));
                foreach (var crew in voyage.Data.CrewGears) Put("선원", crew.Id, crew.Name, $"[{Voyage.CrewKinds[Math.Clamp(crew.Kind, 0, 2)]}] " + crew.Description.Replace("\n", " "));
                foreach (var lab in voyage.Data.Items.Where(i => i.Effect == "Lab")) Put("연금", lab.Id, lab.Name, "연금술 설비");
                foreach (var lab in voyage.Data.Papers.Where(p => p.Id is >= 1500339 and <= 1500356 or 1504031)) Put("연금", lab.Id, lab.Name, lab.Description.Replace("\n", " "));
            }
            int pages = Math.Max(1, (_addAllFound.Count + addPage - 1) / addPage);
            _itemPage = Math.Clamp(_itemPage, 0, pages - 1);
            foreach (var entry in _addAllFound.Skip(_itemPage * addPage).Take(addPage))
            {
                int id = entry.Id;
                if (entry.Tag == "교역품") GoodIcon(id, x + 20, row, 28);
                if (entry.Tag is "교역품" or "부품" or "레시피" && canvas.Hover(x + 20, row - 1, 28, 28)) _itemTip = (entry.Name, entry.Note, x + 54, Math.Min(row + 29, canvas.Height - 104), 380, 40 + MathF.Ceiling(Math.Max(1, entry.Note.Length) / 26f) * 19);
                else if (entry.Recipe != 0) RecipeIcon(entry.Recipe, x + 20, row - 1, 28);
                else if (id > 0) ItemImage(id, x + 20, row - 1, 28);
                canvas.Text(entry.Tag, x + 54, row + 5, 50, 22, 12, Canvas.Gold);
                canvas.Text(entry.Name, x + 106, row + 3, 300, 24, entry.Name.Length > 18 ? 12 : 15, Canvas.White);
                canvas.Text(entry.Note, x + 410, row + 5, w - 410 - 200, 22, 11, Canvas.Dim);
                if (entry.Recipe != 0)
                {
                    var recipe = voyage.Data.Recipes.Find(r => r.Id == entry.Recipe);
                    bool have = voyage.KnowsRecipe(entry.Recipe);
                    if (canvas.Button(have ? "있음" : "추가", x + w - 110, row, 90, 26, !have && recipe != null, 13)) voyage.AddRecipe(recipe!);
                }
                else if (entry.Tag == "교역품")
                {
                    // 교역품은 소지품이 아니라 창고에 실린다 — 한 번에 열 개
                    // 어느 도시에서 파는가는 클라이언트에 없어 지은 것이다 — 딱지를 누르면 그 교역품의 산지를 찾아 달라는 요청이 쌓인다
                    Invented("good", id, entry.Name, x + w - 292, row + 2);
                    canvas.Text($"실은 수 {(voyage.Cargo.TryGetValue(id, out var held) ? held.Count : 0)}", x + w - 190, row + 5, 74, 22, 12, Canvas.Dim);
                    if (canvas.Button("+10", x + w - 110, row, 90, 26, voyage.HoldFree > 0, 13) && voyage.Good(id) is { } loaded) voyage.GiveGood(loaded, 10);
                }
                else if (entry.Tag == "부품")
                {
                    // 선박부품 · 문장은 소지품이 아니라 「소유 선박부품」으로 들어간다
                    canvas.Text($"가진 수 {voyage.PartStock.Count(p => p.Id == id) + voyage.Parts.Count(p => p.Id == id)}", x + w - 190, row + 5, 74, 22, 12, Canvas.Dim);
                    if (canvas.Button("추가", x + w - 110, row, 90, 26, voyage.PartStock.Count < Voyage.PartStockLimit, 13) && voyage.Data.ShipParts.Find(p => p.Id == id) is { } given) voyage.GivePart(given);
                }
                else
                {
                    canvas.Text($"가진 수 {voyage.Items.GetValueOrDefault(id)}", x + w - 190, row + 5, 74, 22, 12, Canvas.Dim);
                    if (AddWithCount(x + w - 110, row)) voyage.AddItem(id, _addCount);
                }
                row += 32;
            }
            if (_addAllFound.Count == 0) canvas.Text("맞는 것이 없다.", x + 20, row, w - 40, 24, 14, Canvas.Dim);
            canvas.Text($"{_addAllFound.Count:N0}가지", x + 180, y + h - 43, 120, 22, 13, Canvas.Dim);
            Pages(pages);
        }
        else if (_addLab)
        {
            // 연금술 — 실험 설비(화로 · 실험대, 지어 넣은 아이템), 도구와 원액(아이템 표의 원본), 그리고 연금술 레시피
            var things = voyage.Data.Items.Where(i => i.Effect == "Lab").Select(i => (i.Id, i.Name, Note: (int)i.Amount / 10 == 1 ? "화로를 사용한 연금술" : (int)i.Amount / 10 == 2 ? "실험대에서 진행하는 연금술" : "화로와 실험대를 겸한다"))
                .Concat(voyage.Data.Papers.Where(p => p.Id is >= 1500339 and <= 1500356 or 1504031).Select(p => (p.Id, p.Name, Note: p.Description.Replace("\n", " "))))
                .Where(i => Found(i.Name)).ToList();
            int pages = Math.Max(1, (things.Count + addPage - 1) / addPage);
            _itemPage = Math.Clamp(_itemPage, 0, pages - 1);
            foreach (var thing in things.Skip(_itemPage * addPage).Take(addPage))
            {
                int id = thing.Id;
                ItemImage(id, x + 20, row - 1, 28);
                canvas.Text(thing.Name, x + 54, row + 3, 190, 24, thing.Name.Length > 12 ? 12 : 15, Canvas.White);
                canvas.Text(thing.Note, x + 246, row + 5, w - 246 - 200, 22, 11, Canvas.Dim);
                canvas.Text($"가진 수 {voyage.Items.GetValueOrDefault(id)}", x + w - 190, row + 5, 74, 22, 12, Canvas.Dim);
                if (AddWithCount(x + w - 110, row)) voyage.AddItem(id, _addCount);
                row += 32;
            }
            // 연금술 레시피를 한꺼번에 — 만들 것이 정해진 것만
            var labRecipes = voyage.Data.RecipeRules.Where(r => r.Facility != "").ToList();
            if (canvas.Button($"연금술 레시피 {labRecipes.Count}가지 넣기", x + 180, y + h - 50, 220, 34, labRecipes.Exists(r => !voyage.KnowsRecipe(r.RecipeId)), 13))
                foreach (var rule in labRecipes)
                    if (voyage.Data.Recipes.Find(r => r.Id == rule.RecipeId) is { } recipe) voyage.AddRecipe(recipe);
            Pages(pages);
        }
        else if (_addGear)
        {
            // 의상 · 장비 — 장비 표(15). 번호의 십만 자리가 갈래이자 그림 무리(0 옷 · 1 모자 · 2 신발 · 3 장갑 · 4 무기 · 5 장신구)
            // 찾는 말은 이름뿐 아니라 올려 주는 스킬의 이름에서도 찾는다(「봉제」를 치면 봉제를 올려 주는 장비)
            var sought = _addSearch == "" ? null : voyage.Data.Skills.Where(k => Found(k.Name)).Select(k => k.Id).ToHashSet();
            var gear = voyage.Data.Gear.Where(g => (Found(g.Name) || (sought is { Count: > 0 } && voyage.Data.GearBoosts.TryGetValue(g.Id, out var lifts) && lifts.Keys.Any(sought.Contains))) && (_addGroup == 0 || (_addGroup == 7 ? voyage.Data.GearBoosts.ContainsKey(g.Id) : g.Id / 100000 == _addGroup - 1))).ToList();      // 7 = 스킬을 올려 주는 것만
            int pages = Math.Max(1, (gear.Count + addPage - 1) / addPage);
            _itemPage = Math.Clamp(_itemPage, 0, pages - 1);
            foreach (var worn in gear.Skip(_itemPage * addPage).Take(addPage))
            {
                int id = worn.Id;
                ItemImage(id, x + 20, row - 1, 28);
                canvas.Text(worn.Name, x + 54, row + 3, 190, 24, worn.Name.Length > 12 ? 12 : 15, Canvas.White);
                // 스킬 보정이 있으면 설명 대신 그것을 보인다(수치 · 올려 주는 스킬) — 설명은 마우스를 올리면 나온다
                bool lifted = voyage.Data.GearBoosts.ContainsKey(id);
                canvas.Text(lifted ? voyage.GearLine(worn).Trim() : worn.Description.Replace("\n", " "), x + 246, row + 5, w - 246 - 300, 22, 11, lifted ? new Color4(0.55f, 1f, 0.65f, 1) : Canvas.Dim);
                // 장비가 올려 주는 스킬은 클라이언트에 없다 — 모르는 장비에는 딱지를 달아, 누르면 위키에서 찾아 달라는 요청이 쌓인다
                if (!voyage.Data.GearBoosts.ContainsKey(id)) Invented("gear", id, worn.Name, x + w - 292, row + 2);
                canvas.Text($"가진 수 {voyage.Items.GetValueOrDefault(id)}", x + w - 190, row + 5, 74, 22, 12, Canvas.Dim);
                if (AddWithCount(x + w - 110, row)) voyage.AddItem(id, _addCount);
                row += 32;
            }
            if (gear.Count == 0) canvas.Text(voyage.Data.Gear.Count == 0 ? "의상 목록(data\\extracted\\gear-items.json)이 없다." : "여기에 맞는 것이 없다.", x + 20, row, w - 40, 24, 14, Canvas.Dim);
            Pages(pages);
        }
        else if (_addShipItems)
        {
            // 조빌 아이템 — 아이템 표(14)의 조선 부품(2200000 ~): 선체 · 돛 · 망 · 선실 · 선박재료. 강화 부품과 이름이 같은 것은 강화 때 값 대신 든다
            static int Family(string name) => name.Contains("선체") || name.Contains("도선") ? 1 : name.Contains("세일") || name.Contains("돛") || name.Contains("마스트") ? 2 : name.Contains("선박재료") ? 3 : 4;
            var parts = voyage.Data.Papers.Where(p => p.Id is >= Voyage.ShipItems and < Voyage.ShipItems + 100_000 && Found(p.Name) && (_addGroup == 0 || Family(p.Name) == _addGroup)).ToList();
            int pages = Math.Max(1, (parts.Count + addPage - 1) / addPage);
            _itemPage = Math.Clamp(_itemPage, 0, pages - 1);
            foreach (var part in parts.Skip(_itemPage * addPage).Take(addPage))
            {
                int id = part.Id;
                canvas.Image($"sb22:{id}", () => (_itemIcons ??= new ImageSet(@"0010\0001\sb")).Pixels(22, id), x + 20, row - 1, 28, 28); if (canvas.Hover(x + 20, row - 1, 28, 28)) ItemCard(id, x + 54, row + 29);
                canvas.Text(part.Name, x + 54, row + 3, 190, 24, part.Name.Length > 12 ? 12 : 15, Canvas.White);
                // 강화에 넣으면 오르는 범위를 아는 재료는 그것을(금빛), 모르는 것은 설명 글을 — 올리면 카드에 둘 다 뜬다
                string build = voyage.BuildPartLine(id);
                canvas.Text(build != "" ? build : part.Description.Replace("\n", " "), x + 246, row + 5, w - 246 - 200, 22, 11, build != "" ? Canvas.Gold : Canvas.Dim);
                if (canvas.Hover(x + 54, row, w - 260, 30)) ItemCard(id, x + 54, row + 29);
                canvas.Text($"가진 수 {voyage.Items.GetValueOrDefault(id)}", x + w - 190, row + 5, 74, 22, 12, Canvas.Dim);
                if (AddWithCount(x + w - 110, row)) voyage.AddItem(id, _addCount);
                row += 32;
            }
            if (parts.Count == 0) canvas.Text(voyage.Data.Papers.Count == 0 ? "조빌 아이템 목록(data\\extracted\\paper-items.json)이 없다." : "여기에 맞는 것이 없다.", x + 20, row, w - 40, 24, 14, Canvas.Dim);
            Pages(pages);
        }
        else if (_addTickets)
        {
            // 선박 교환권 — 아이템 표(14)의 원본 번호와 이름. 쓰면 그 배가 부두에 들어온다
            var tickets = voyage.ShipTickets().Where(t => Found(t.Name + voyage.TicketShip(t)?.Name + t.Description) && (_addGroup == 0 || voyage.TicketShip(t) != null)).ToList();
            int pages = Math.Max(1, (tickets.Count + addPage - 1) / addPage);
            _itemPage = Math.Clamp(_itemPage, 0, pages - 1);
            foreach (var ticket in tickets.Skip(_itemPage * addPage).Take(addPage))
            {
                int id = ticket.Id;
                canvas.Image($"sb{id}", () => (_itemIcons ??= new ImageSet(@"0010\0001\sb")).Pixels(15, id), x + 20, row - 1, 28, 28); if (canvas.Hover(x + 20, row - 1, 28, 28)) ItemCard(id, x + 54, row + 29);
                canvas.Text(ticket.Name, x + 54, row + 3, 230, 24, ticket.Name.Length > 14 ? 12 : 15, Canvas.White);
                var gives = voyage.TicketShip(ticket);
                canvas.Text(gives != null ? $"→ {gives.Name}" : "바꿀 배를 못 찾았다", x + 290, row + 5, 180, 22, 12, gives != null ? Canvas.Gold : Canvas.Dim);
                canvas.Text($"가진 수 {voyage.Items.GetValueOrDefault(id)}", x + 470, row + 5, 80, 22, 12, Canvas.Dim);
                if (AddWithCount(x + w - 110, row)) voyage.AddItem(id, _addCount);
                row += 32;
            }
            if (tickets.Count == 0) canvas.Text(voyage.Data.Papers.Count == 0 ? "교환권 목록(data\\extracted\\paper-items.json)이 없다." : "여기에 맞는 교환권이 없다.", x + 20, row, w - 40, 24, 14, Canvas.Dim);
            Pages(pages);
        }
        else if (_addPapers)
        {
            // 증서 — 국가공헌 훈장증서 · 전용함 건조 허가증, 그리고 아이템 표의 조선 쪽 책(해체 기법서 …)
            int[] books = [Voyage.DismantleBook, 1510029, 1510061, 1510062, 1510030, Voyage.RedesignBook, 1510035, 1510016, .. Voyage.ExtraPapers];
            var listed = voyage.Data.Items.Where(i => i.Effect == "Paper").Select(i => (i.Id, i.Name))
                .Concat(books.Select(id => voyage.Data.Papers.Find(p => p.Id == id)).Where(p => p != null).Select(p => (p!.Id, p.Name)))
                .Where(p => Found(p.Name)).ToList();
            // 한 쪽에 다섯 줄 — 휠이나 아래 단추로 넘긴다
            const int papersShown = 5;
            int paperPages = Math.Max(1, (listed.Count + papersShown - 1) / papersShown);
            _itemPage = Math.Clamp(_itemPage, 0, paperPages - 1);
            Pages(paperPages);
            foreach (var paper in listed.Skip(_itemPage * papersShown).Take(papersShown))
            {
                canvas.Text(paper.Name, x + 20, row + 3, 190, 24, 15, Canvas.White);
                canvas.Text($"가진 수 {voyage.Items.GetValueOrDefault(paper.Id)}", x + 215, row + 5, 100, 22, 12, Canvas.Dim);
                if (canvas.Button("+1", x + w - 250, row, 60, 26, true, 13)) voyage.AddItem(paper.Id);
                if (canvas.Button("+10", x + w - 184, row, 60, 26, true, 13)) voyage.AddItem(paper.Id, 10);
                if (canvas.Button("+200", x + w - 118, row, 98, 26, true, 13)) voyage.AddItem(paper.Id, 200);
                canvas.Text(voyage.ItemNote(paper.Id), x + 20, row + 28, w - 40, 20, 12, Canvas.Dim);
                row += 56;
            }
        }
        else if (_addDyes)
        {
            // 돛 도료 1 ~ 23 과 특수 돛 도료 1 ~ 11 — 줄마다 그 도료로 칠할 수 있는 무늬가 보인다
            var dyes = voyage.Data.Items.Where(i => i.Effect == "SailPaint" && Found(i.Name) && (_addGroup == 0 || (i.Amount > 100) == (_addGroup == 2))).ToList();
            int pages = Math.Max(1, (dyes.Count + addPage - 1) / addPage);
            _itemPage = Math.Clamp(_itemPage, 0, pages - 1);
            foreach (var dye in dyes.Skip(_itemPage * addPage).Take(addPage))
            {
                canvas.Text(dye.Name, x + 20, row + 3, 150, 24, 15, Canvas.White);
                if (canvas.Hover(x + 20, row, 150, 28)) ItemCard(dye.Id, x + 54, row + 29);
                var shapes = Voyage.DyePatterns((int)dye.Amount);
                for (int k = 0; k < shapes.Length; k++) SailThumb(shapes[k], 5, x + 170 + k * 30, row - 1, 28);
                canvas.Text($"가진 수 {voyage.Items.GetValueOrDefault(dye.Id)}", x + 420, row + 5, 100, 22, 12, Canvas.Dim);
                if (AddWithCount(x + w - 110, row)) voyage.AddItem(dye.Id, _addCount);
                row += 32;
            }
            Pages(pages);
        }
        else if (_addMaterials)
        {
            // 나라별 · 제독 재료 같은 재질 — 가지고 있어야 건조할 때 고를 수 있다
            static int Family(string name) =>
                name.Contains("제독") ? 4 : name.Contains("특수 도장") || name.Contains("특별 주문") ? 3
                : new[] { "제례", "의전", "귀빈", "축전", "야전" }.Any(name.Contains) ? 2
                : name.Contains("군 ") || name.Contains("제국군") ? 1 : 5;
            var woods = voyage.SpecialMaterials().Where(m => Found(m.Name) && (_addGroup == 0 || Family(m.Name) == _addGroup)).ToList();
            int pages = Math.Max(1, (woods.Count + addPage - 1) / addPage);
            _itemPage = Math.Clamp(_itemPage, 0, pages - 1);
            foreach (var wood in woods.Skip(_itemPage * addPage).Take(addPage))
            {
                int id = Voyage.MaterialItem + wood.Id;
                canvas.Text(wood.Name, x + 20, row + 3, 280, 24, 15, Canvas.White);
                if (canvas.Hover(x + 20, row, 280, 28)) ItemCard(id, x + 54, row + 29);
                canvas.Text($"내구 {wood.Durability * 100:0}% · 돛 {wood.Sail * 100:0}%   가진 수 {voyage.Items.GetValueOrDefault(id)}", x + 300, row + 5, 220, 22, 12, Canvas.Dim);
                if (AddWithCount(x + w - 110, row)) voyage.AddItem(id, _addCount);
                row += 32;
            }
            if (woods.Count == 0) canvas.Text("맞는 재질이 없다.", x + 20, row, w - 40, 24, 14, Canvas.Dim);
            Pages(pages);
        }
        else if (_addRecipes)
        {
            // 클라이언트의 레시피를 다 늘어놓는다 — 만들 것이 정해진 것(재료를 아는 것)이 먼저, 나머지는 얻을 수만 있다
            if (_addRecipeFor != _addSearch)
            {
                _addRecipeFor = _addSearch;
                _addRecipeFound = RecipesToAdd().Where(r => Found(r.Name) || Found(RecipeNote(r))).ToList();
            }
            int pages = Math.Max(1, (_addRecipeFound.Count + addPage - 1) / addPage);
            _itemPage = Math.Clamp(_itemPage, 0, pages - 1);
            foreach (var recipe in _addRecipeFound.Skip(_itemPage * addPage).Take(addPage))
            {
                bool have = voyage.KnowsRecipe(recipe.Id), known = voyage.RuleOf(recipe.Id) != null;
                RecipeIcon(recipe.Id, x + 20, row - 1, 28);
                canvas.Text(recipe.Name, x + 54, row + 3, 250, 24, recipe.Name.Length > 16 ? 12 : 15, have ? Canvas.Dim : Canvas.White);
                canvas.Text(RecipeNote(recipe), x + 304, row + 5, w - 304 - (known ? 120 : 220), 22, 12, Canvas.Dim);
                // 재료는 클라이언트에 없다 — 딱지를 누르면 그 레시피의 재료를 찾아 달라는 요청이 쌓인다
                if (!known) Invented("recipe", recipe.Id, recipe.Name, x + w - 210, row + 2);
                if (canvas.Button(have ? "있음" : "추가", x + w - 110, row, 90, 26, !have, 13)) voyage.AddRecipe(recipe);
                row += 32;
            }
            canvas.Text($"{_addRecipeFound.Count:N0}가지", x + 180, y + h - 43, 120, 22, 12, Canvas.Dim);
            Pages(pages);
        }
        else
        {
            var jobs = voyage.JobsToTake().Where(j => Found(j.Name) && (_addGroup == 0 || j.Group == _addGroup - 1)).ToList();
            int pages = Math.Max(1, (jobs.Count + addPage - 1) / addPage);
            _itemPage = Math.Clamp(_itemPage, 0, pages - 1);
            foreach (var job in jobs.Skip(_itemPage * addPage).Take(addPage))
            {
                int id = Voyage.JobPaper + job.Id;
                canvas.Text(voyage.ItemName(id), x + 20, row + 3, 250, 24, 15, Canvas.White);
                if (canvas.Hover(x + 20, row, 250, 28)) ItemCard(id, x + 54, row + 29);
                canvas.Text(job.Group switch { 0 => "모험 계열", 1 => "교역 계열", 2 => "전투 계열", _ => "" } + $"   가진 수 {voyage.Items.GetValueOrDefault(id)}", x + 250, row + 5, 250, 22, 12, Canvas.Dim);
                if (AddWithCount(x + w - 110, row)) voyage.AddItem(id, _addCount);
                row += 32;
            }
            Pages(pages);
        }
        // 「아이템 추가」 창에서는 소지품으로 돌아간다
        if (canvas.Button(adding ? "소지품으로" : "이전", x + w - 130, y + h - 50, 110, 34)) { if (adding) (_itemTab, _itemPage, _addSearching) = (0, 0, false); else voyage.Dialog = Dialog.None; }

        void Pages(int pages)
        {
            if (canvas.Button("◀", x + 20, y + h - 50, 40, 34, _itemPage > 0)) _itemPage--;
            canvas.Text($"{_itemPage + 1} / {pages}", x + 64, y + h - 43, 60, 24, 15, Canvas.White, 1);
            if (canvas.Button("▶", x + 128, y + h - 50, 40, 34, _itemPage < pages - 1)) _itemPage++;
        }
    }

    /// <summary>
    /// 조선소 — 원본의 선박 구입 창을 따른다: 왼쪽에 판매선박(이름과 값), 오른쪽에 고른 배의
    /// 등급 · 기본성능(세로돛 · 가로돛 · 조력 · 선회 · 내파 · 장갑) · 내구력 · 선창 정보(선원 · 대포 · 창고) · 구입 가격.
    /// </summary>
    // ── 선박 비교 ────────────────────────────────────────────────────────────

    private sealed record CompareSide(string Name, int ShipId, Dho.Data.ShipStats Stats, double Durability, ShipWork? Work, string Form, int Model, int Color);
    // 비교 창의 두 배 모습 — 창이 프레임마다 넣는다(칸과 모형 번호, 선체 빛깔). 둘은 함께 돈다
    public readonly List<(float X, float Y, float W, float H, int Model, int Color)> ComparePreviews = [];
    private bool _cmpOpen;
    private int _cmpLeft, _cmpRight;
    private ShipData? _cmpSale;

    // 내 배들 — 타고 있는 배가 0 번, 그 뒤로 부두의 배
    private List<CompareSide> OwnedSides()
    {
        var sides = new List<CompareSide> { new("타고 있는 배 — " + voyage.Ship.Name, voyage.Ship.Id, voyage.Stats, voyage.Durability, voyage.Work, voyage.FormName(voyage.Ship, voyage.Work), voyage.Ship.Model, voyage.ShipHullColor) };
        foreach (var docked in voyage.Dock)
            sides.Add(new(docked.Ship.Name + (docked.Material != 0 ? $" ({voyage.MaterialOf(docked.Material)?.Name})" : ""), docked.Ship.Id, voyage.StatsOf(docked), docked.Durability, docked.Work, voyage.FormName(docked.Ship, docked.Work), docked.Ship.Model, voyage.HullColorOf(docked)));
        return sides;
    }

    // 두 배를 좌우로 나란히 놓고 견준다 — 줄마다 나은 쪽이 초록, 못한 쪽이 주황. ◀ ▶ 로 내 배들 가운데서 바꾼다(조선소에서는 오른쪽이 고른 판매선박)
    private void CompareWindow()
    {
        // 배의 모습 칸 — 화면이 낮으면 줄어든다
        const float w = 880;
        float shape = Math.Clamp(canvas.Height - 560, 0, 190), h = 540 + shape;
        float x = (canvas.Width - w) / 2, y = Math.Max(14, (canvas.Height - h) / 2);
        canvas.PanelId = "WndShipCompare";
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text("선박 비교", x + 20, y + 10, 200, 28, 20, Canvas.Gold, 0, true);
        if (shape > 60) canvas.Text("배의 모습은 끌어서 돌리고 휠로 다가선다(둘이 함께 돈다)", x + 150, y + 16, 400, 20, 12, Canvas.Dim);
        var owned = OwnedSides();
        _cmpLeft = Math.Clamp(_cmpLeft, 0, owned.Count - 1);
        _cmpRight = Math.Clamp(_cmpRight, 0, owned.Count - 1);
        var left = owned[_cmpLeft];
        var right = _cmpSale is { } sale ? new CompareSide(sale.Name + " (판매선박)", sale.Id, Dho.Data.ShipStats.Of(sale, voyage.Data.Settings.Ships), Dho.Data.ShipStats.Of(sale, voyage.Data.Settings.Ships).Durability, null, voyage.FormName(sale, new ShipWork()), sale.Model, voyage.HullColorOf(sale, 0))
                                         : owned[_cmpRight];
        float half = (w - 40) / 2, lx = x + 20, rx = x + 20 + half, mid = x + w / 2;
        void Head(CompareSide side, float hx, bool leftSide)
        {
            canvas.Fill(hx + 4, y + 48, half - 8, 64, new Color4(0.04f, 0.07f, 0.2f, 0.85f));
            canvas.Fill(hx + 10, y + 54, 52, 52, new Color4(0.02f, 0.03f, 0.08f, 0.95f));
            ShipIcon(side.ShipId, hx + 10, y + 54, 52);
            canvas.Text(side.Name, hx + 70, y + 52, half - 150, 24, side.Name.Length > 18 ? 13 : 16, Canvas.White, 0, true);
            canvas.Text(side.Form + (side.Work is { Grade: > 0 } work ? $"   G{work.Grade}" : ""), hx + 70, y + 80, half - 150, 20, 13, Canvas.Gold);
            if (!side.Stats.Real) Invented("ship", side.ShipId, voyage.Data.Ships.Find(s => s.Id == side.ShipId)?.Name ?? side.Name, hx + 180, y + 80);
            bool fixedSide = !leftSide && _cmpSale != null;
            if (!fixedSide && owned.Count > 1)
            {
                // 누르면 가진 배의 목록이 뜬다 — 거기서 고른다
                int which = leftSide ? 1 : 2;
                if (canvas.Button(_cmpList == which ? "닫기 ▲" : "고르기 ▼", hx + half - 96, y + 64, 88, 30, true, 13)) (_cmpList, _cmpListTop) = (_cmpList == which ? 0 : which, 0);
            }
        }
        Head(left, lx, true);
        Head(right, rx, false);
        canvas.Line(mid, y + 48, mid, y + h - 60, new Color4(0.5f, 0.55f, 0.75f, 0.8f), 1);
        if (shape > 60)
        {
            canvas.Fill(lx + 4, y + 116, half - 8, shape - 4, new Color4(0.03f, 0.05f, 0.16f, 0.6f));
            canvas.Fill(rx + 4, y + 116, half - 8, shape - 4, new Color4(0.03f, 0.05f, 0.16f, 0.6f));
            // 목록이 떠 있는 쪽은 배의 모습을 그리지 않는다 — 3D 모습이 목록 위를 덮는다
            if (_cmpList != 1) ComparePreviews.Add((lx + 6, y + 118, half - 12, shape - 8, left.Model, left.Color));
            if (_cmpList != 2) ComparePreviews.Add((rx + 6, y + 118, half - 12, shape - 8, right.Model, right.Color));
        }

        // (그림, 이름, 값, 표시 글, 클수록 좋은가)
        (int Icon, string Label, Func<CompareSide, double> Value, Func<CompareSide, string>? Text)[] rows =
        [
            (310, "내구력", s => s.Stats.Durability, s => $"{s.Durability:0} / {s.Stats.Durability}"),
            (306, "세로돛", s => s.Stats.VerticalSail, null), (307, "가로돛", s => s.Stats.HorizontalSail, null), (314, "조력", s => s.Stats.Rowing, null),
            (308, "선회", s => s.Stats.Turn, null), (309, "내파", s => s.Stats.WaveResist, null), (333, "장갑", s => s.Stats.Armor, null),
            (330, "선원", s => s.Stats.MaxCrew, s => $"{s.Stats.MinCrew} ~ {s.Stats.MaxCrew}"), (305, "대포", s => s.Stats.Guns, null), (323, "창고", s => s.Stats.Hold, null),
            (-1, "속도", s => s.Stats.Knots, s => $"{s.Stats.Knots:0.0} 노트"),
            (-1, "강화 횟수", s => s.Work?.Times ?? 0, null), (364, "조타 숙련도", s => s.Work?.Mastery ?? 0, s => $"{s.Work?.Mastery ?? 0:0}"),
        ];
        var better = new Color4(0.55f, 1f, 0.6f, 1);
        var worse = new Color4(1f, 0.62f, 0.45f, 1);
        float row = y + 122 + (shape > 60 ? shape : 0);
        foreach (var (icon, label, value, text) in rows)
        {
            double a = value(left), b = value(right);
            canvas.Fill(lx + 4, row, w - 48, 24, new Color4(0.04f, 0.07f, 0.2f, 0.6f));
            if (icon >= 0) canvas.Image($"gm0:{icon}", () => (_markParts ??= new UiParts(0)).Pixels(icon), mid - 60, row + 2, 20, 20);
            canvas.Text(label, mid - 60, row + 2, 120, 20, 13, Canvas.Dim, 1);
            string Show(CompareSide side, double v) => text != null ? text(side) : $"{v:0.#}";
            canvas.Text(Show(left, a), lx + 10, row + 1, half - 90, 22, 15, a > b ? better : a < b ? worse : Canvas.White, 2, a > b);
            canvas.Text(Show(right, b), rx + 80, row + 1, half - 90, 22, 15, b > a ? better : b < a ? worse : Canvas.White, 0, b > a);
            // 차이 — 오른쪽이 왼쪽보다 얼마나 큰가
            if (Math.Abs(b - a) > 0.049) canvas.Text($"{(b > a ? "+" : "−")}{Math.Abs(b - a):0.#}", rx + half - 70, row + 3, 60, 20, 12, b > a ? better : worse, 2);
            row += 26;
        }
        // 붙은 선박 스킬
        void SkillRow(CompareSide side, float sx)
        {
            var fitted = side.Work == null ? [] : side.Work.Dedicated > 0 ? side.Work.Skills.Append(side.Work.Dedicated).ToList() : side.Work.Skills;
            for (int k = 0; k < Math.Min(fitted.Count, 11); k++)
            {
                SkillIcon(fitted[k], sx + k * 32, row + 2, 1);
                if (canvas.Hover(sx + k * 32, row + 2, 30, 33)) _tip = (voyage.OptionName(fitted[k]), sx + k * 32 + 15, row);
            }
            if (fitted.Count == 0) canvas.Text("선박 스킬 없음", sx, row + 8, 200, 20, 12, Canvas.Dim);
        }
        SkillRow(left, lx + 10);
        SkillRow(right, rx + 14);
        canvas.Text("나은 쪽이 초록 · 오른쪽 끝의 숫자는 오른쪽 배가 왼쪽 배보다 얼마나 큰가", x + 20, y + h - 42, w - 180, 20, 12, Canvas.Dim);
        if (canvas.Button("이전", x + w - 130, y + h - 46, 110, 32)) (_cmpOpen, _cmpSale, _cmpList) = (false, null, 0);
        // 가진 배의 목록 — 맨 위에 그린다. 줄을 누르면 그 배로 바뀌고 닫힌다
        if (_cmpList == 0 || owned.Count < 2) return;
        const int listRows = 12;
        float px = (_cmpList == 1 ? lx : rx) + 4, py = y + 114, pw = half - 8, ph = Math.Min(owned.Count, listRows) * 30 + (owned.Count > listRows ? 40 : 8);
        canvas.Fill(px, py, pw, ph, new Color4(0.02f, 0.04f, 0.14f, 0.98f));
        canvas.Frame(px, py, pw, ph, Canvas.Gold, 1.2f);
        _cmpListTop = Math.Clamp(_cmpListTop, 0, Math.Max(0, owned.Count - listRows));
        int now = _cmpList == 1 ? _cmpLeft : _cmpRight;
        for (int i = _cmpListTop; i < Math.Min(owned.Count, _cmpListTop + listRows); i++)
        {
            float ly = py + 4 + (i - _cmpListTop) * 30;
            bool over = canvas.Hover(px + 2, ly, pw - 4, 29);
            if (i == now) canvas.Fill(px + 2, ly, pw - 4, 29, new Color4(0.12f, 0.55f, 0.52f, 0.95f));
            else if (over) canvas.Fill(px + 2, ly, pw - 4, 29, new Color4(0.2f, 0.3f, 0.6f, 0.8f));
            ShipIcon(owned[i].ShipId, px + 6, ly + 1, 27);
            canvas.Text(owned[i].Name, px + 40, ly + 4, pw - 150, 22, owned[i].Name.Length > 20 ? 11 : 14, Canvas.White);
            canvas.Text($"{owned[i].Form}{(owned[i].Work is { Grade: > 0 } graded ? $"  G{graded.Grade}" : "")}", px + pw - 110, ly + 6, 104, 20, 11, Canvas.Gold, 2);
            if (!over || !canvas.Pointer.Clicked) continue;
            if (_cmpList == 1) _cmpLeft = i; else _cmpRight = i;
            (_cmpList, canvas.Pointer.Consumed, canvas.Pressed) = (0, true, true);
            return;
        }
        if (owned.Count > listRows)
        {
            if (canvas.Button("▲", px + 6, py + ph - 34, 40, 28, _cmpListTop > 0, 12)) _cmpListTop -= listRows / 2;
            if (canvas.Button("▼", px + 50, py + ph - 34, 40, 28, _cmpListTop + listRows < owned.Count, 12)) _cmpListTop += listRows / 2;
            canvas.Text($"{_cmpListTop + 1} ~ {Math.Min(owned.Count, _cmpListTop + listRows)} / {owned.Count}척", px + 100, py + ph - 29, 200, 20, 12, Canvas.Dim);
        }
    }
    private int _cmpList, _cmpListTop;
    // 「지은 값」 표시 — 진짜 자료가 없어 지어 넣은 값 옆에 붙는다. 누르면 위키에서 찾아 채워 달라는 요청이 data\wiki-requests.json 에 쌓인다
    /// <summary>대본용: 스킬 창에서 이름으로 스킬을 고른다(그 쪽이 열려 있어야 한다).</summary>
    public string SkillPick = "";

    private void Invented(string kind, int id, string name, float x, float y)
    {
        bool asked = voyage.Data.WikiRequested(kind, id);
        const float bw = 92, bh = 22;
        bool over = canvas.Hover(x, y, bw, bh);
        canvas.Fill(x, y, bw, bh, asked ? new Color4(0.16f, 0.36f, 0.30f, 0.95f) : over ? new Color4(0.85f, 0.5f, 0.15f, 1) : new Color4(0.55f, 0.32f, 0.10f, 0.95f));
        canvas.Frame(x, y, bw, bh, asked ? new Color4(0.4f, 0.9f, 0.7f, 1) : Canvas.Gold, 1);
        canvas.Text(asked ? "✔ 요청함" : "위키값 요청", x, y + 2, bw, 18, 12, Canvas.White, 1);
        if (over) _tip = (asked ? "요청해 두었다 — 채워지면 이 표시가 사라진다" : "원본의 값을 몰라 임시로 넣은 값이다. 누르면 위키에서 찾아 채워 달라고 요청한다", x + bw / 2, y - 2);
        if (!over || asked || !canvas.Pointer.Clicked) return;
        (canvas.Pointer.Consumed, canvas.Pointer.Clicked, canvas.Pressed) = (true, false, true);
        voyage.Data.RequestWiki(kind, id, name);
        voyage.Say($"「{name}」의 진짜 값을 찾아 달라고 요청해 두었다.");
    }

    // ── 해상 재해의 모습 ─────────────────────────────────────────────────────

    // 배가 화면의 어디에 얼마만 하게 보이는가 — 가운데, 뱃머리 쪽으로 반 길이, 돛대 꼭대기 쪽. 창이 프레임마다 넣는다(없으면 안 그린다)
    public (float X, float Y, float AlongX, float AlongY, float UpX, float UpY)? ShipOnScreen;

    // 재해마다의 모습 — 원본의 효과 자료(ef 묶음)를 못 풀어서 모두 지어 그린 것이다. 배 둘레의 알갱이와 화면 가장자리의 빛깔로 무슨 재해인지 알아보게 한다.
    // 1 화재(불꽃과 연기) · 2 침수(물보라, 배가 가라앉는다) · 3 괴혈병(누런 기운) · 4 쥐(갑판을 뛰는 점들) · 5 해초(선체에 감긴 풀) ·
    // 6 암초(바위와 부딪는 물보라) · 7 상어(둘레를 도는 지느러미) · 8 향수병(푸른 기운과 한숨) · 9 반란(붉은 번쩍임과 칼빛). 비와 폭풍은 빗줄기와 번개.
    // 조타 표시 — 원본은 물 위에 누운 흰 글자(동 · 서 · 남)와 금빛 북쪽 표, 나무 타륜이다. 여기서는 글자와 손으로 그린 타륜으로 흉내 냈다
    // 부관의 한마디 — 원본처럼 화면 가운데 위에 얼굴(48 × 48)과 말이 든 상자
    private void AideSpeechBox()
    {
        if (voyage.AideSpeech is not { } said || voyage.Clock > said.Until || voyage.Dialog != Dialog.None) return;
        const float w = 330, h = 76;
        float x = (canvas.Width - w) / 2, y = canvas.Height * 0.22f;
        canvas.Panel(x, y, w, h);
        int face = said.Who.Id;
        canvas.Image($"sd30:{face}", () => (_discoveryImages ??= new ImageSet(@"0010\0001\sd")).Pixels(30, face), x + 14, y + 14, 48, 48);
        canvas.Text(said.Line, x + 74, y + 24, w - 86, 28, 16, Canvas.White);
    }

    // 스킬 랭크 업의 모습 — 원본에는 이펙트가 있다(사용자) — 그 자료를 못 풀어 지어 그린 것: 화면 가운데(바다에서는 배 자리)에서 금빛 알갱이가 솟고, 스킬 아이콘과 「RANK UP」이 세 초 떠오른다
    private void SkillUpEffect()
    {
        var (skillId, rank, at) = voyage.SkillUpNotice;
        float age = (float)(voyage.Clock - at);
        if (skillId == 0 || age < 0 || age > 3) return;
        float cx = ShipOnScreen is { } ship && voyage.Mode == Mode.Sea ? ship.X : canvas.Width / 2, cy = ShipOnScreen is { } s2 && voyage.Mode == Mode.Sea ? s2.Y : canvas.Height * 0.5f;
        float fade = age < 2.2f ? 1 : 1 - (age - 2.2f) / 0.8f;
        static float Rand(int i, int k) { float v = MathF.Sin(i * 12.9898f + k * 78.233f) * 43758.547f; return v - MathF.Floor(v); }
        for (int i = 0; i < 46; i++)
        {
            float life = (age * (0.5f + Rand(i, 1) * 0.6f) + Rand(i, 2)) % 1f;
            float angle = Rand(i, 3) * MathF.Tau + age * 1.5f, radius = 26 + Rand(i, 4) * 46;
            float px = cx + MathF.Cos(angle) * radius * (1 - life * 0.4f), py = cy + 40 - life * 170 + MathF.Sin(angle) * 10;
            canvas.Circle(px, py, 1.5f + Rand(i, 5) * 2.5f, new Color4(1f, 0.85f + Rand(i, 6) * 0.15f, 0.35f, (1 - life) * 0.9f * fade));
        }
        float rise = MathF.Min(1, age / 0.5f) * 60;
        SkillIcon(skillId, cx - 21, cy - 60 - rise, fade, 1.5f);
        canvas.Text("RANK UP", cx - 100, cy - 6 - rise, 200, 24, 17, new Color4(1f, 0.85f, 0.3f, fade), 1, true);
        canvas.Text($"{voyage.SkillName(skillId)}  Rank {rank}", cx - 150, cy + 16 - rise, 300, 24, 16, new Color4(1f, 1f, 1f, fade), 1, true);
    }

    private void HelmMarks()
    {
        if (voyage.Mode != Mode.Sea) return;
        foreach (var (hx, hy, text) in Helm)
        {
            if (text == "*")
            {
                var wood = new Color4(0.70f, 0.47f, 0.22f, 1);
                float spin = (float)voyage.TurnVelocity * 3 + (float)voyage.Heading;
                canvas.Circle(hx, hy, 15, new Color4(0.25f, 0.15f, 0.06f, 0.9f), false, 5);
                canvas.Circle(hx, hy, 15, wood, false, 3);
                for (int k = 0; k < 8; k++)
                {
                    float a = spin + k * MathF.PI / 4;
                    canvas.Line(hx, hy, hx + MathF.Cos(a) * 21, hy + MathF.Sin(a) * 21, wood, 2.2f);
                    canvas.Circle(hx + MathF.Cos(a) * 21, hy + MathF.Sin(a) * 21, 2, wood);
                }
                canvas.Circle(hx, hy, 4, wood);
                continue;
            }
            if (text == "^")
            {
                // 뱃머리 쪽의 노란 표 — 원본은 날개 편 꼴의 작은 금빛 그림이다(그 그림은 못 찾아 손으로 그렸다): 배에서 바깥을 향한 촉과 두 날개
                var gold = new Color4(1f, 0.82f, 0.2f, 1);
                var from = ShipOnScreen is { } s ? new System.Numerics.Vector2(hx - s.X, hy - s.Y) : new System.Numerics.Vector2(0, -1);
                var d = from.Length() > 1 ? from / from.Length() : new System.Numerics.Vector2(0, -1);
                var p = new System.Numerics.Vector2(-d.Y, d.X);
                var at = new System.Numerics.Vector2(hx, hy);
                void Stroke(System.Numerics.Vector2 a, System.Numerics.Vector2 b, float wide) { canvas.Line(a.X, a.Y, b.X, b.Y, new Color4(0.3f, 0.2f, 0.02f, 0.9f), wide + 2); canvas.Line(a.X, a.Y, b.X, b.Y, gold, wide); }
                Stroke(at - d * 9, at + d * 12, 4);
                Stroke(at + d * 4, at - d * 4 + p * 14, 3.5f);
                Stroke(at + d * 4, at - d * 4 - p * 14, 3.5f);
                continue;
            }
            canvas.Text(text, hx - 20, hy - 15, 40, 30, 22, Canvas.White, 1, true);
        }
    }

    private void DisasterEffects()
    {
        if (voyage.Mode != Mode.Sea) return;
        float t = (float)voyage.Clock, W = canvas.Width, H = canvas.Height;
        static float Rand(int i, int k) { float v = MathF.Sin(i * 12.9898f + k * 78.233f) * 43758.547f; return v - MathF.Floor(v); }
        static float Frac(float v) => v - MathF.Floor(v);
        // 날씨: 빗줄기(폭풍이면 굵고 많다)와 번개
        // 원본의 비: 가늘고 긴 줄이 화면 가득 비스듬히(오른쪽 위 → 왼쪽 아래) 내린다
        int streaks = voyage.Weather == Weather.Storm ? 260 : voyage.Weather == Weather.Rain ? 170 : 0;
        for (int i = 0; i < streaks; i++)
        {
            float fall = Frac(t * (1.6f + Rand(i, 1) * 0.8f) + Rand(i, 2)), rx = Rand(i, 3) * (W + 200) - 100 - fall * 120, ry = fall * (H + 80) - 40;
            float len = 46 + Rand(i, 4) * 40;
            canvas.Line(rx, ry, rx - len * 0.42f, ry + len, new Color4(0.78f, 0.84f, 0.95f, (voyage.Weather == Weather.Storm ? 0.30f : 0.20f) * (0.5f + Rand(i, 5) * 0.5f)), voyage.Weather == Weather.Storm ? 1.3f : 0.9f);
        }
        if (voyage.Weather == Weather.Storm)
        {
            float beat = Frac(t / 5.3f + 0.2f * MathF.Sin(t * 0.37f));          // 다섯 해 남짓에 한 번 번쩍
            if (beat < 0.035f || (beat > 0.06f && beat < 0.075f)) canvas.Fill(0, -TitleHeight, W, H + TitleHeight, new Color4(1f, 1f, 1f, 0.32f));
        }
        if (ShipOnScreen is not { } ship || voyage.Disasters.Count == 0) return;
        var c = new System.Numerics.Vector2(ship.X, ship.Y);
        var along = new System.Numerics.Vector2(ship.AlongX, ship.AlongY);
        var up = new System.Numerics.Vector2(ship.UpX, ship.UpY);
        float size = Math.Clamp(MathF.Max(along.Length(), up.Length() * 0.5f), 30, 150);
        var side = along.Length() > 1 ? new System.Numerics.Vector2(-along.Y, along.X) / along.Length() * size * 0.35f : new System.Numerics.Vector2(size * 0.35f, 0);
        void Dot(System.Numerics.Vector2 p, float r, Color4 color) => canvas.Circle(p.X, p.Y, MathF.Max(1, r), color);
        void Edge(Color4 color)
        {
            // 화면 가장자리에 띠 — 가운데는 가리지 않는다
            const float band = 46;
            canvas.Fill(0, 0, W, band, color); canvas.Fill(0, H - band, W, band, color);
            canvas.Fill(0, band, band, H - band * 2, color); canvas.Fill(W - band, band, band, H - band * 2, color);
        }
        foreach (var disaster in voyage.Disasters)
        {
            switch (disaster.Data.Id)
            {
                case 1:                                 // 화재
                    for (int i = 0; i < 10; i++)
                    {
                        float p = Frac(t * 0.33f + Rand(i, 11));
                        Dot(c + along * (Rand(i, 12) - 0.5f) * 1.2f + up * (0.35f + p * 1.3f) + side * (Rand(i, 13) - 0.5f + MathF.Sin(t + i) * 0.2f), size * (0.08f + 0.12f * p), new Color4(0.16f, 0.15f, 0.15f, 0.28f * (1 - p)));
                    }
                    for (int i = 0; i < 30; i++)
                    {
                        float p = Frac(t * (0.8f + Rand(i, 21) * 0.5f) + Rand(i, 22));
                        var at = c + along * (Rand(i, 23) - 0.5f) * 1.3f + up * (0.08f + p * 0.55f) + side * (Rand(i, 24) - 0.5f) * 0.5f;
                        Dot(at, size * 0.085f * (1 - p) + 2, new Color4(1f, 0.30f + 0.45f * (1 - p), 0.06f, 0.6f * (1 - p)));
                        Dot(at, size * 0.04f * (1 - p) + 1, new Color4(1f, 0.92f, 0.5f, 0.65f * (1 - p)));
                    }
                    Edge(new Color4(1f, 0.35f, 0.05f, 0.07f + 0.04f * MathF.Sin(t * 7)));
                    break;
                case 2:                                 // 침수
                    for (int i = 0; i < 16; i++)
                    {
                        float p = Frac(t * 0.9f + Rand(i, 31)), arc = MathF.Sin(p * MathF.PI);
                        Dot(c + along * (Rand(i, 32) - 0.5f) * 1.7f + side * (Rand(i, 33) < 0.5f ? -1 : 1) * (0.8f + p * 0.5f) + up * (arc * 0.12f - 0.02f), size * 0.03f + 1.5f, new Color4(0.75f, 0.9f, 1f, 0.7f * (1 - p)));
                    }
                    Edge(new Color4(0.1f, 0.35f, 0.9f, 0.08f));
                    break;
                case 3:                                 // 괴혈병
                    for (int i = 0; i < 9; i++)
                    {
                        float p = Frac(t * 0.22f + Rand(i, 41));
                        Dot(c + along * (Rand(i, 42) - 0.5f) + up * (0.25f + p * 0.9f) + side * MathF.Sin(t * 1.3f + i) * 0.4f, size * 0.04f + 2, new Color4(0.75f, 0.85f, 0.2f, 0.45f * MathF.Sin(p * MathF.PI)));
                    }
                    Edge(new Color4(0.7f, 0.8f, 0.1f, 0.08f + 0.03f * MathF.Sin(t * 2)));
                    break;
                case 4:                                 // 쥐
                    for (int i = 0; i < 12; i++)
                    {
                        float run = Frac(t * (0.25f + Rand(i, 51) * 0.3f) * (i % 2 == 0 ? 1 : -1) + Rand(i, 52));
                        var at = c + along * (run - 0.5f) * 1.5f + up * 0.10f + side * (Rand(i, 53) - 0.5f) * 0.45f;
                        Dot(at, size * 0.016f + 1.6f, new Color4(0.18f, 0.12f, 0.08f, 0.95f));
                        canvas.Line(at.X, at.Y, at.X - (i % 2 == 0 ? 1 : -1) * along.X / MathF.Max(1, along.Length()) * 5, at.Y - (i % 2 == 0 ? 1 : -1) * along.Y / MathF.Max(1, along.Length()) * 5, new Color4(0.3f, 0.2f, 0.15f, 0.9f), 1);
                    }
                    break;
                case 5:                                 // 해초 — 원본 글: 「해초가 선박 밑 키에 얽혔습니다!」. 배 밑과 고물(키) 쪽에 엉긴 풀이 물결을 따라 뒤로 끌린다
                    for (int i = 0; i < 26; i++)
                    {
                        // 열에 여섯은 고물 뒤로 길게 끌리고, 나머지는 선체 옆 물금에 붙어 있다
                        bool trailing = i % 10 < 6;
                        float sideways = (Rand(i, 62) - 0.5f) * (trailing ? 0.9f : 2.0f);
                        var root = c - along * (trailing ? 0.85f + Rand(i, 61) * 0.25f : (Rand(i, 61) - 0.5f) * 1.5f) + side * sideways - up * 0.04f;
                        float reach = (trailing ? 0.45f + Rand(i, 63) * 0.5f : 0.16f + Rand(i, 63) * 0.14f);
                        var dark = new Color4(0.05f, 0.20f + Rand(i, 64) * 0.12f, 0.09f, trailing ? 0.62f : 0.5f);
                        var from = root;
                        for (int k = 1; k <= 4; k++)
                        {
                            float wave = MathF.Sin(t * 1.1f + i * 1.7f + k * 0.9f) * 0.10f * k / 4;
                            var to = root - along * reach * k / 4 + side * (sideways * 0.15f * k / 4 + wave);
                            canvas.Line(from.X, from.Y, to.X, to.Y, dark, trailing ? 3.4f - k * 0.5f : 2.4f);
                            from = to;
                        }
                        if (i % 4 == 0) Dot(root, size * 0.035f + 1.5f, new Color4(0.06f, 0.24f, 0.10f, 0.55f));
                    }
                    break;
                case 6:                                 // 암초
                    for (int i = 0; i < 6; i++)
                    {
                        var rock = c + along * (0.75f + Rand(i, 71) * 0.5f) + side * (Rand(i, 72) - 0.5f) * 2.2f;
                        Dot(rock, size * (0.07f + Rand(i, 73) * 0.06f), new Color4(0.22f, 0.22f, 0.24f, 0.92f));
                        Dot(rock + new System.Numerics.Vector2(-2, -3), size * 0.035f, new Color4(0.42f, 0.42f, 0.44f, 0.9f));
                    }
                    for (int i = 0; i < 12; i++)
                    {
                        float p = Frac(t * 1.3f + Rand(i, 74));
                        Dot(c + along * (0.85f + p * 0.2f) + side * (Rand(i, 75) - 0.5f) * (0.6f + p * 1.6f) + up * MathF.Sin(p * MathF.PI) * 0.2f, size * 0.03f + 1.5f, new Color4(1f, 1f, 1f, 0.75f * (1 - p)));
                    }
                    break;
                case 7:                                 // 상어
                    for (int i = 0; i < 3; i++)
                    {
                        float a = t * 0.7f + i * 2.094f;
                        var at = c + along * MathF.Cos(a) * 1.45f + side * MathF.Sin(a) * 3.0f;
                        var ahead = (along * -MathF.Sin(a) * 1.45f + side * MathF.Cos(a) * 3.0f);
                        ahead = ahead.Length() > 0.01f ? ahead / ahead.Length() : new System.Numerics.Vector2(1, 0);
                        float fin = size * 0.10f + 4;
                        var tipTop = at - new System.Numerics.Vector2(0, fin);
                        canvas.Line(at.X - ahead.X * fin * 0.6f, at.Y - ahead.Y * fin * 0.6f, at.X - ahead.X * fin * 2.4f, at.Y - ahead.Y * fin * 2.4f, new Color4(1f, 1f, 1f, 0.5f), 1.5f);
                        for (float k = 0; k <= 1; k += 0.125f)
                            canvas.Line(at.X + ahead.X * fin * (0.5f - k), at.Y + ahead.Y * fin * (0.5f - k), tipTop.X - ahead.X * fin * 0.3f, tipTop.Y - ahead.Y * fin * 0.3f, new Color4(0.25f, 0.28f, 0.32f, 0.95f), 2);
                    }
                    break;
                case 8:                                 // 향수병
                    for (int i = 0; i < 5; i++)
                    {
                        float p = Frac(t * 0.18f + Rand(i, 81));
                        var at = c + along * (Rand(i, 82) - 0.5f) + up * (0.4f + p * 0.8f);
                        canvas.Text("…", at.X - 10, at.Y - 10, 24, 20, 15, new Color4(0.75f, 0.85f, 1f, 0.8f * MathF.Sin(p * MathF.PI)), 1, true, false);
                    }
                    Edge(new Color4(0.25f, 0.4f, 0.75f, 0.10f));
                    break;
                case 9:                                 // 반란
                    for (int i = 0; i < 7; i++)
                    {
                        float p = Frac(t * 2.2f + Rand(i, 91));
                        if (p > 0.35f) continue;
                        var at = c + along * (Rand(i + (int)(t * 2.2f), 92) - 0.5f) * 1.3f + up * (0.12f + Rand(i, 93) * 0.2f);
                        float len = size * 0.14f + 6, tilt = Rand(i + (int)(t * 2.2f), 94) * 3.14f;
                        canvas.Line(at.X - MathF.Cos(tilt) * len, at.Y - MathF.Sin(tilt) * len, at.X + MathF.Cos(tilt) * len, at.Y + MathF.Sin(tilt) * len, new Color4(1f, 1f, 0.9f, 0.9f * (1 - p / 0.35f)), 2);
                    }
                    Edge(new Color4(0.95f, 0.1f, 0.08f, 0.06f + 0.07f * MathF.Max(0, MathF.Sin(t * 6))));
                    break;
            }
        }
    }
    private int _buyGrade;

    private void ShipyardWindow()
    {
        if (_cmpOpen) { CompareWindow(); return; }
        const float w = 900, h = 470, listWidth = 400;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        void Header(string text, float hx, float hy, float hw = 150)
        {
            canvas.Fill(hx, hy, hw, 24, new Color4(0.72f, 0.76f, 0.84f, 0.92f));
            canvas.Text(text, hx, hy + 1, hw, 22, 15, new Color4(0.05f, 0.08f, 0.2f, 1), 1, true, false);
        }
        // icon: 화면 부품 묶음 0 의 작은 그림(306 세로돛, 307 가로돛, 314 노, 308 선회, 309 물결, 333 철판, 330 선원, 305 대포, 323 상자, 310 판자)
        void Cell(string label, string value, float cx, float cy, float cw = 122, int icon = -1)
        {
            canvas.Fill(cx, cy, cw, 26, new Color4(0.04f, 0.07f, 0.2f, 0.85f));
            bool drawn = icon >= 0 && canvas.Image($"gm0:{icon}", () => (_markParts ??= new UiParts(0)).Pixels(icon), cx + 3, cy + 3, 20, 20);
            canvas.Text(label, cx + (drawn ? 26 : 6), cy + 3, cw, 20, 13, Canvas.Dim);
            canvas.Text(value, cx, cy + 2, cw - 6, 22, 15, Canvas.White, 2);
        }

        Header("판매선박", x + 20, y + 16, 130);
        // 찾기 칸 — 누르고 글자를 치면 이름에 그 글자가 든 배만 남는다(Esc 나 다른 데를 누르면 입력을 마친다)
        float sx = x + 156, sw = listWidth - 156;
        bool overSearch = canvas.Hover(sx, y + 16, sw, 24);
        if (canvas.Pointer.Clicked) _shipSearching = overSearch;
        canvas.Fill(sx, y + 16, sw, 24, new Color4(0.02f, 0.04f, 0.14f, 0.95f));
        canvas.Frame(sx, y + 16, sw, 24, _shipSearching ? Canvas.Gold : Canvas.PanelEdge, 1.2f);
        canvas.Text(_shipSearch == "" && !_shipSearching ? "🔍 이름으로 찾기" : _shipSearch + (_shipSearching && (int)(voyage.Clock * 2) % 2 == 0 ? "|" : ""), sx + 6, y + 18, sw - 30, 20, 14, _shipSearch == "" && !_shipSearching ? Canvas.Dim : Canvas.White);
        if (_shipSearch != "" && canvas.Hover(sx + sw - 22, y + 16, 22, 24) && canvas.Pointer.Clicked) (_shipSearch, _shipPage, _shipChosen) = ("", 0, 0);
        if (_shipSearch != "") canvas.Text("✕", sx + sw - 22, y + 18, 20, 20, 13, Canvas.Dim, 1);
        canvas.Text(voyage.ShipbuildingLine, x + listWidth + 30, y - 2, w - listWidth - 50, 18, 12, Canvas.Gold, 2);
        // 거르기: 선체 크기와 쓰임새(요구 레벨이 가장 높은 갈래)
        bool Chip(string text, float cx, float cy, float cw, bool on)
        {
            bool over = canvas.Hover(cx, cy, cw, 22);
            canvas.Fill(cx, cy, cw, 22, on ? new Color4(0.16f, 0.62f, 0.72f, 0.98f) : over ? new Color4(0.22f, 0.32f, 0.66f, 0.95f) : new Color4(0.06f, 0.10f, 0.28f, 0.9f));
            canvas.Text(text, cx, cy + 1, cw, 20, 13, Canvas.White, 1);
            return over && canvas.Pointer.Clicked;
        }
        string[] sizeNames = ["전체", "소형", "중형", "대형"], useNames = ["전체", "모험", "교역", "전투"];
        // 걸어 둔 거르기는 설정에 남는다 — 창을 처음 열 때 되살린다
        if (!_shipFilterRead) (_shipFilterRead, _shipSize, _shipUse) = (true, Math.Clamp(voyage.Data.Settings.ShipFilterSize, 0, 3), Math.Clamp(voyage.Data.Settings.ShipFilterUse, 0, 3));
        canvas.Text("크기", x + 22, y + 47, 40, 20, 13, Canvas.Dim);
        canvas.Text("용도", x + 22, y + 73, 40, 20, 13, Canvas.Dim);
        for (int i = 0; i < 4; i++)
        {
            if (Chip(sizeNames[i], x + 62 + i * 80, y + 46, 76, _shipSize == i)) { (_shipSize, _shipPage, _shipChosen) = (i, 0, 0); KeepShipFilter(); }
            if (Chip(useNames[i], x + 62 + i * 80, y + 72, 76, _shipUse == i)) { (_shipUse, _shipPage, _shipChosen) = (i, 0, 0); KeepShipFilter(); }
        }
        var ships = voyage.ShipsForSale().Where(ship =>
        {
            if (_shipSearch != "" && !ship.Name.Contains(_shipSearch, StringComparison.OrdinalIgnoreCase)) return false;
            if (_shipSize != 0 && (_shipSize == 1 ? ship.SizeClass > 1 : _shipSize == 2 ? ship.SizeClass != 2 : ship.SizeClass < 3)) return false;
            if (_shipUse == 0) return true;
            var levels = Dho.Data.ShipStats.Of(ship, voyage.Data.Settings.Ships).Levels;
            int top = Math.Max(levels.Adventure, Math.Max(levels.Trade, levels.Battle));
            return (_shipUse == 1 ? levels.Adventure : _shipUse == 2 ? levels.Trade : levels.Battle) == top;
        }).ToList();
        // 목록은 휠로 굴린다 — _shipPage 가 맨 윗줄의 차례다
        const int perPage = 7;
        const float line = 46;
        int last = Math.Max(0, ships.Count - perPage);
        _shipPage = Math.Clamp(_shipPage, 0, last);
        _shipChosen = Math.Clamp(_shipChosen, 0, Math.Max(0, ships.Count - 1));
        _shipRows = (ships.Count, perPage);
        float row = y + 100;
        for (int i = _shipPage; i < Math.Min(ships.Count, _shipPage + perPage); i++, row += line)
        {
            bool chosen = i == _shipChosen, hover = canvas.Hover(x + 20, row, listWidth - 34, line - 1);
            if (chosen) canvas.Fill(x + 20, row, listWidth - 34, line - 1, new Color4(0.12f, 0.55f, 0.52f, 0.95f));
            else if (hover) canvas.Fill(x + 20, row, listWidth - 34, line - 1, new Color4(0.2f, 0.3f, 0.6f, 0.6f));
            if (hover && canvas.Pointer.Clicked) _shipChosen = i;
            // 원본의 짜임: 왼쪽에 배 그림, 이름, 오른쪽 아래에 값
            canvas.Fill(x + 23, row + 2, 41, 41, new Color4(0.02f, 0.03f, 0.08f, 0.95f));
            ShipIcon(ships[i].Id, x + 23, row + 2, 41);
            canvas.Frame(x + 23, row + 2, 41, 41, new Color4(0.75f, 0.75f, 0.8f, 0.9f), 1);
            canvas.Text(ships[i].Name, x + 72, row + 2, listWidth - 110, 22, 15, Canvas.White);
            canvas.Text($"{voyage.ShipCost(ships[i]):N0} Ð", x + 30, row + 22, listWidth - 54, 22, 15, Canvas.White, 2);
            // 값 왼쪽에 세로돛 · 가로돛 · 선회
            var listed = Dho.Data.ShipStats.Of(ships[i], voyage.Data.Settings.Ships);
            (int Icon, int Value)[] shown = [(306, listed.VerticalSail), (307, listed.HorizontalSail), (308, listed.Turn)];
            for (int k = 0; k < shown.Length; k++)
            {
                int icon = shown[k].Icon;
                float statX = x + 72 + k * 46;
                canvas.Image($"gm0:{icon}", () => (_markParts ??= new UiParts(0)).Pixels(icon), statX, row + 26, 14, 14);
                canvas.Text($"{shown[k].Value}", statX + 15, row + 25, 30, 18, 11, Canvas.Dim);
            }
        }
        if (ships.Count == 0) canvas.Text("여기에 맞는 배가 없다.", x + 30, y + 110, 300, 24, 15, Canvas.Dim);
        if (canvas.Button($"선박 매각 · 교환 (부두 {voyage.Dock.Count}척)", x + 20, y + h - 46, listWidth - 34, 32, true, 14)) (voyage.Dialog, _swapFrom) = (Dialog.ShipSwap, Dialog.Shipyard);
        ScrollBar(x + listWidth - 10, y + 100, perPage * line - 1, perPage, ships.Count, ref _shipPage);
        canvas.Line(x + listWidth + 14, y + 14, x + listWidth + 14, y + h - 14, new Color4(0.5f, 0.55f, 0.75f, 0.8f), 1);

        float rx = x + listWidth + 30, rw = w - listWidth - 50;
        if (ships.Count > 0)
        {
            var ship = ships[_shipChosen];
            var s = Dho.Data.ShipStats.Of(ship, voyage.Data.Settings.Ships);
            string[] sizes = ["소형", "소형", "중형", "대형", "대형"];
            canvas.Text($"{ship.Name}({sizes[Math.Clamp(ship.SizeClass, 0, 4)]})", rx, y + 14, rw, 26, 18, Canvas.White, 0, true);
            Cell("Grade", $"0({voyage.FormName(ship)})", rx, y + 46, 300);

            Header("기본성능", rx, y + 84);
            Header("내구력", rx + 310, y + 84, 140);
            Cell("세로돛", $"{s.VerticalSail}", rx, y + 114, 96, 306);
            Cell("가로돛", $"{s.HorizontalSail}", rx + 100, y + 114, 96, 307);
            Cell("조력", $"{s.Rowing}", rx + 200, y + 114, 96, 314);
            Cell("선회", $"{s.Turn}", rx, y + 144, 96, 308);
            Cell("내파", $"{s.WaveResist}", rx + 100, y + 144, 96, 309);
            Cell("장갑", $"{s.Armor}", rx + 200, y + 144, 96, 333);
            Cell("", $"{s.Durability} / {s.Durability}", rx + 310, y + 114, 140, 310);
            canvas.Fill(rx + 310, y + 142, 140, 4, new Color4(0.90f, 0.35f, 0.40f, 1));

            Header("선창 정보", rx, y + 186);
            Cell("선원", $"{s.MinCrew} / {s.MaxCrew}", rx, y + 216, 146, 330);
            Cell("대포", $"0 / {s.Guns}", rx + 152, y + 216, 146, 305);
            Cell("창고", $"0 / {s.Hold}", rx + 304, y + 216, 146, 323);

            Header("운항", rx, y + 258);
            canvas.Text($"Lv 모험 {s.Levels.Adventure} · 교역 {s.Levels.Trade} · 전투 {s.Levels.Battle}     속도 {s.Knots:0.0}노트", rx + 4, y + 288, rw, 22, 14, Canvas.White);
            if (!s.Real) Invented("ship", ship.Id, ship.Name, rx + rw - 96, y + 288);
            string? blocker = voyage.ShipBlocker(ship);
            if (blocker != null) canvas.Text(blocker, rx + 4, y + 312, rw, 22, 14, new Color4(1f, 0.5f, 0.45f, 1));

            // 그레이드를 정해서 산다(원본에는 없다) — 그레이드 하나에 값이 절반씩 더 든다
            _buyGrade = Math.Clamp(_buyGrade, 0, Voyage.MaxGrade);
            canvas.Text($"Grade {_buyGrade}", rx + 40, y + h - 114, 70, 22, 15, _buyGrade > 0 ? Canvas.Gold : Canvas.White, 1, true);
            if (canvas.Button("−", rx, y + h - 118, 34, 26, _buyGrade > 0, 15)) _buyGrade--;
            if (canvas.Button("+", rx + 112, y + h - 118, 34, 26, _buyGrade < Voyage.MaxGrade, 15)) _buyGrade++;
            int cost = voyage.ShipCostAt(ship, _buyGrade);
            if (blocker == null && voyage.Money < cost) blocker = "돈이 모자라다";
            Cell("구입 가격", $"{cost:N0} Ð", rx + 150, y + h - 118, 300);
            Cell("소지금", $"{voyage.Money:N0} Ð", rx + 150, y + h - 88, 300);
            if (canvas.Button("확인", rx + 300, y + h - 50, 72, 34, blocker == null)) voyage.BuyShip(ship, _buyGrade);
            // 고른 판매선박을 내 배와 나란히 견준다
            if (canvas.Button("내 배와 비교", rx + 150, y + h - 150, 110, 26, true, 12)) (_cmpOpen, _cmpSale, _cmpLeft) = (true, ship, 0);
        }
        if (canvas.Button("선박부품", rx, y + h - 50, 88, 34, true, 14)) (voyage.Dialog, _partPage) = (Dialog.ShipParts, 0);
        if (canvas.Button("강화", rx + 236, y + h - 50, 60, 34, true, 14)) { (voyage.Dialog, _workSkillOnly, _workBack) = (Dialog.Strengthen, false, Dialog.Shipyard); _workPicked.Clear(); }
        if (canvas.Button("커스텀설정 조선", rx + 92, y + h - 50, 140, 34, ships.Count > 0, 14))
        {
            _buildShip = ships.Count > 0 ? ships[_shipChosen] : null;
            (voyage.Dialog, _buildMaterial, _buildLoad) = (Dialog.CustomBuild, 0, 0);
        }
        if (canvas.Button("이전", rx + 376, y + h - 50, 74, 34)) voyage.Dialog = Dialog.None;
    }

    private readonly List<int> _workPicked = [];
    private Dialog _workBack = Dialog.Shipyard;    // 강화 창의 「이전」이 돌아갈 곳
    private bool _workSkillOnly;               // 옵션 스킬 부여로 들어왔는가 — 스킬만 붙고 성능은 그대로
    private int _woodSeen;
    private int _workWood;                     // 강화에 넣기로 고른 선박재료 아이템(없으면 0)

    /// <summary>
    /// 강화 — 타고 있는 배에 조선 부품을 둘 이상(넷까지) 넣는다. 왼쪽에서 부품을 고르면 오른쪽에 오를 능력치와,
    /// 조합이 맞을 때 붙을 옵션 스킬이 보인다.
    /// </summary>
    private readonly List<int> _workItems = [];      // 보통 강화에 넣은 조선 재료(아이템 번호 — 같은 것을 여럿 넣을 수 있다)
    private int _workItemTop, _workPage;       // _workPage: 0 재료, 1 적재(「다음」)
    private int? _workLoad;                    // 강화하면서 바꿀 적재(%) — 안 고쳤으면 null

    /// <summary>
    /// 보통 강화 — 원본의 짜임(사용자의 원본 화면): 왼쪽 「소유 재료」 격자(다섯 칸), 오른쪽 위에 넣는 칸 넷(주요 돛 · 포문 · 장비 둘),
    /// 그 아래 「강화 성능」(지금 값( +낮은 값~ +높은 값)). 재료에 마우스를 올리면 이름 · 선박 사이즈 · 올리는 범위가 뜬다.
    /// 재료를 누르면 제 갈래의 빈 칸에 들어가고, 칸을 누르면 빠진다.
    /// </summary>
    private void MaterialStrengthenWindow()
    {
        const float w = 880, h = 500, tile = 60;
        const int columns = 5, lines = 4;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
        canvas.PanelId = "WndStrengthen";
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        if (_workPage == 1 && !_workSkillOnly) { StrengthenLoadPage(x, y, w, h); return; }
        float lw = columns * (tile + 6) - 6;
        canvas.Fill(x + 20, y + 14, lw, 24, new Color4(0.72f, 0.76f, 0.84f, 0.92f));
        canvas.Text("소유 재료", x + 20, y + 15, lw, 22, 15, new Color4(0.05f, 0.08f, 0.2f, 1), 1, true, false);
        _workItems.RemoveAll(i => voyage.Items.GetValueOrDefault(i) <= 0);
        var owned = voyage.BuildPartsOwned();
        int rows = (owned.Count + columns - 1) / columns;
        ScrollBar(x + 24 + lw, y + 46, lines * (tile + 6) - 6, lines, rows, ref _workItemTop);
        _workItemTop = Math.Clamp(_workItemTop, 0, Math.Max(0, rows - lines));
        bool Icon(int item, float px, float py, float size) => canvas.Image($"sb22:{item}", () => (_itemIcons ??= new ImageSet(@"0010\0001\sb")).Pixels(22, item), px, py, size, size);
        string[] sizeNames = ["소형", "중형", "대형"];
        string Range(int low, int high) => $"{low:+0;-0;0}~ {high:+0;-0;0}";
        for (int k = 0; k < columns * lines; k++)
        {
            int index = _workItemTop * columns + k;
            float tx = x + 20 + k % columns * (tile + 6), ty = y + 46 + k / columns * (tile + 6);
            canvas.Fill(tx, ty, tile, tile, new Color4(0.03f, 0.10f, 0.08f, 0.9f));
            if (index >= owned.Count) continue;
            var (item, part, count) = owned[index];
            int left = count - _workItems.Count(i => i == item);
            bool fits = voyage.BuildPartFits(part), room = left > 0 && voyage.BuildSlotFor(_workItems, item) >= 0;
            if (!Icon(item, tx + 3, ty + 3, tile - 6)) canvas.Text(part.Name, tx + 2, ty + 12, tile - 4, 40, 10, Canvas.White, 1);
            if (!fits || left <= 0) canvas.Fill(tx, ty, tile, tile, new Color4(0.02f, 0.03f, 0.10f, 0.72f));
            canvas.Frame(tx, ty, tile, tile, new Color4(0.75f, 0.75f, 0.8f, 0.6f), 1);
            if (left > 1 || count > 1) canvas.Text($"{left}", tx, ty + tile - 20, tile - 4, 18, 12, Canvas.White, 2, true);
            if (!canvas.Hover(tx, ty, tile, tile)) continue;
            canvas.Frame(tx, ty, tile, tile, Canvas.Gold, 1.6f);
            var tip = new List<string> { part.Name, $"{Voyage.BuildKinds[Math.Clamp(part.Kind, 0, 3)]} · 선박 사이즈 {(part.Sizes.Count == 0 ? "전부" : string.Join("/", part.Sizes.Select(s => sizeNames[Math.Clamp(s, 0, 2)])))}" };
            foreach (var (stat, label, _, _) in Voyage.BuildStats)
                if (part.Stats.TryGetValue(stat, out var range) && range.Length >= 2) tip.Add($"{label}  {Range(range[0], range[1])}");
            if (part.Cash) tip.Add("캐시 재료 — 초과 강화가 꼭 된다");
            if (!fits) tip.Add("이 배의 크기에는 못 쓴다");
            _tip = (string.Join("\n", tip), tx + tile / 2, ty + 2);
            if (canvas.Pointer.Clicked && room) { _workItems.Add(item); voyage.Cues.Enqueue("Click"); }
        }
        if (owned.Count == 0) canvas.Text("가진 조선 재료가 없다.\n(소지품 → 아이템 추가 → 「조빌」)", x + 30, y + 60, lw - 20, 44, 13, Canvas.Dim);
        // 선박재료(재질 바꾸기) — 가진 것이 있으면 아래 한 줄에
        var woods = voyage.WoodsOwned();
        if (!woods.Exists(o => o.Item == _workWood)) _workWood = 0;
        float wy = y + 46 + lines * (tile + 6) + 4;
        canvas.Text(woods.Count == 0 ? "" : $"선박재료 — 넣으면 재질이 바뀐다(지금 {voyage.MaterialOf(voyage.ShipMaterialId)?.Name ?? "기본"})", x + 20, wy, lw + 20, 18, 11, Canvas.Dim);
        for (int i = 0; i < Math.Min(woods.Count, 9); i++)
        {
            float px = x + 20 + i * 37, py = wy + 20;
            bool picked = woods[i].Item == _workWood, over = canvas.Hover(px, py, 35, 35);
            canvas.Fill(px, py, 35, 35, new Color4(0.03f, 0.10f, 0.08f, 0.9f));
            MaterialIcon(woods[i].Material, px + 2, py + 2, 31);
            canvas.Frame(px, py, 35, 35, picked ? new Color4(0.3f, 0.95f, 0.9f, 1) : over ? Canvas.Gold : new Color4(0.75f, 0.75f, 0.8f, 0.6f), picked ? 2.5f : 1);
            if (over) _tip = ($"{woods[i].Material.Name} — 내구 {woods[i].Material.Durability * 100:0}% · 돛 {woods[i].Material.Sail * 100:0}%", px + 18, py - 2);
            if (over && canvas.Pointer.Clicked) _workWood = picked ? 0 : woods[i].Item;
        }
        canvas.Line(x + lw + 50, y + 14, x + lw + 50, y + h - 60, new Color4(0.5f, 0.55f, 0.75f, 0.8f), 1);

        // 오른쪽: 넣는 칸 넷 — 주요 돛 · 포문 · 장비 · 장비
        float rx = x + lw + 66, rw = x + w - 20 - rx;
        int[] slotIcons = [306, 305, 323, 323];
        var slotted = new int[Voyage.BuildSlots.Length];
        foreach (int item in _workItems)
            if (voyage.BuildPartOf(item) is { } part)
                for (int k = 0; k < slotted.Length; k++)
                    if (Voyage.BuildSlots[k] == part.Kind && slotted[k] == 0) { slotted[k] = item; break; }
        for (int k = 0; k < slotted.Length; k++)
        {
            float sx = rx + k * (tile + 34), sy = y + 14;
            canvas.Image($"gm0:{slotIcons[k]}", () => (_markParts ??= new UiParts(0)).Pixels(slotIcons[k]), sx, sy + tile / 2 - 9, 18, 18);
            canvas.Fill(sx + 22, sy, tile, tile, new Color4(0.05f, 0.08f, 0.26f, 0.95f));
            canvas.Frame(sx + 22, sy, tile, tile, new Color4(0.75f, 0.75f, 0.8f, 0.5f), 1);
            bool over = canvas.Hover(sx + 22, sy, tile, tile);
            if (slotted[k] == 0) { if (over) _tip = (Voyage.BuildKinds[Voyage.BuildSlots[k]] + " 칸", sx + 22 + tile / 2, sy + 2); continue; }
            if (!Icon(slotted[k], sx + 25, sy + 3, tile - 6)) canvas.Text(voyage.ItemName(slotted[k]), sx + 24, sy + 12, tile - 4, 40, 10, Canvas.White, 1);
            if (!over) continue;
            _tip = (voyage.ItemName(slotted[k]) + " — 누르면 뺀다", sx + 22 + tile / 2, sy + 2);
            if (canvas.Pointer.Clicked) { _workItems.Remove(slotted[k]); voyage.Cues.Enqueue("Click"); }
        }
        // 강화 성능 — 줄마다 「지금 값( +낮은 값~ +높은 값)」
        float py0 = y + 14 + tile + 14;
        canvas.Fill(rx, py0, rw, 24, new Color4(0.72f, 0.76f, 0.84f, 0.92f));
        canvas.Text("강화 성능", rx, py0 + 1, rw, 22, 15, new Color4(0.05f, 0.08f, 0.2f, 1), 1, true, false);
        canvas.Fill(rx, py0 + 28, rw, 216, new Color4(0.03f, 0.05f, 0.16f, 0.8f));
        var now = voyage.Stats;
        var plainNow = voyage.StatsOf(voyage.Ship, voyage.ShipMaterialId, voyage.ShipLoad);
        var ranges = voyage.BuildRanges(_workItems);
        float ly = py0 + 34;
        foreach (var (stat, _, label, icon) in Voyage.BuildStats)
        {
            if (!ranges.TryGetValue(stat, out var range)) continue;
            int value = stat switch { "Durability" => now.Durability, "Vertical" => now.VerticalSail, "Horizontal" => now.HorizontalSail, "Rowing" => now.Rowing, "Turn" => now.Turn,
                                      "Wave" => now.WaveResist, "Armor" => now.Armor, "Cabin" => now.MaxCrew, "Guns" => now.Guns, _ => now.Hold };
            int limit = voyage.WorkLimit(stat, plainNow, voyage.Ship, voyage.Work);
            canvas.Image($"gm0:{icon}", () => (_markParts ??= new UiParts(0)).Pixels(icon), rx + 8, ly + 1, 18, 18);
            canvas.Text(label, rx + 32, ly, 120, 20, 14, Canvas.White);
            canvas.Text($"{value}( {Range(range.Min, range.Max)})", rx + 150, ly, rw - 160, 20, 14, range.Max > 0 ? new Color4(0.55f, 1f, 0.6f, 1) : new Color4(1f, 0.65f, 0.5f, 1), 2);
            if (canvas.Hover(rx, ly, rw, 22)) _tip = ($"지금 강화 한계 {limit} (조타 숙련도가 찬 만큼)", rx + rw / 2, ly);
            ly += 24;
        }
        if (_workSkillOnly)
        {
            // 옵션 스킬 부여 — 스킬만 붙고 강화 성능은 변하지 않는다(위의 범위는 참고로만 보인다)
            canvas.Text("옵션 스킬 부여 — 강화 성능은 변하지 않는다.", rx + 8, ly, rw - 16, 20, 13, Canvas.Gold);
            ly += 24;
        }
        if (ranges.Count == 0 && _workWood == 0) canvas.Text("왼쪽에서 재료를 눌러 칸에 넣는다(둘 이상).", rx + 8, ly, rw - 16, 20, 13, Canvas.Dim);
        if (_workWood > 0 && voyage.WoodOf(_workWood) is { } wood) { canvas.Text($"재질 → {wood.Name}", rx + 8, ly, rw - 16, 20, 13, new Color4(0.5f, 1f, 0.6f, 1)); ly += 24; }
        if (voyage.BuildOption(_workItems) is { } gain)
        {
            SkillIcon(gain.SkillId, rx + 6, ly + 2, 1);
            canvas.Text($"옵션 스킬 「{gain.Name}」 — {Voyage.OptionNote(gain)}", rx + 44, ly + 4, rw - 50, 36, 13, new Color4(0.5f, 1f, 0.6f, 1));
        }
        canvas.Text($"{voyage.Ship.Name}     강화 횟수 {voyage.Work.Times}/{voyage.MaxTimesOf(voyage.Ship)}" + (voyage.OverWork ? $"  초과 강화 — 성공률 {voyage.OverWorkChance(_workItems)}%" + (voyage.OverWorkChance(_workItems) == 100 ? "(캐시 재료)" : "") : ""), x + 20, y + h - 44, 520, 22, 14, voyage.OverWork ? new Color4(1f, 0.7f, 0.4f, 1) : Canvas.Gold);
        // 조타 숙련도가 덜 찼으면 강화가 한계에 막혀 헛돈다(숙련도 0 이면 하나도 안 붙는다) — 눈에 띄게 알린다
        double masteryShare = Voyage.MasteryShare(voyage.Ship, voyage.Work);
        if (masteryShare < 1)
            canvas.Text($"조타 숙련도 {voyage.Work.Mastery:0}/{Voyage.MasteryCap(voyage.Ship, voyage.Work)} — 지금은 강화 상한의 {masteryShare * 100:0}%까지만 오른다", rx, y + h - 128, rw, 18, 12, new Color4(1f, 0.7f, 0.4f, 1), 2);
        // 원본 창의 「필요한 건조일수 · 비용 · 특수조선 강화 허가증」은 식을 몰라 아직 받지 않는다
        canvas.Text("건조일수 · 비용 · 강화 허가증은 아직 받지 않는다", rx, y + h - 84, rw, 18, 11, Canvas.Dim, 2);
        string? blocker = _workSkillOnly ? voyage.GrantBlocker(_workItems) : voyage.BuildBlocker(_workItems, _workWood);
        if (blocker != null) canvas.Text(blocker, rx, y + h - 106, rw, 20, 13, new Color4(1f, 0.5f, 0.45f, 1), 2);
        if (_workSkillOnly)
        {
            if (canvas.Button("부여한다", x + w - 250, y + h - 50, 110, 34, blocker == null)) { voyage.GrantWith(_workItems.ToList()); _workItems.Clear(); }
        }
        // 원본의 차례: 재료 → 「다음」 → 적재 → 강화
        else if (canvas.Button("다음 ▶", x + w - 250, y + h - 50, 110, 34, blocker == null)) _workPage = 1;
        if (canvas.Button("이전", x + w - 130, y + h - 50, 110, 34)) { voyage.Dialog = _workBack; _workItems.Clear(); (_workPage, _workLoad) = (0, null); }
    }

    /// <summary>
    /// 강화의 둘째 화면 — 원본의 「최대적재량 변경」(사용자의 원본 화면): 최대적재량 · 가능 범위 · 적정 범위 · 현재 용량, 설명 글, 오른쪽에 선창 정보(선실 · 포실 · 창고)와 선회.
    /// 원본은 숫자판으로 값을 넣는다 — 여기서는 1% · 5% 씩 올리고 내린다(적재는 %로 둔다). 조선 랭크가 차야 바꾼다.
    /// </summary>
    private void StrengthenLoadPage(float x, float y, float w, float h)
    {
        bool can = voyage.ShipbuildingRank >= Voyage.LoadRank;
        int load = can ? _workLoad ?? voyage.ShipLoad : voyage.ShipLoad;
        int baseTotal = voyage.LoadTotal(0), total = voyage.LoadTotal(load);
        int reach = voyage.LoadReach, proper = voyage.LoadProper;      // 조선 랭크 + 5 % 까지, 랭크 % 까지가 적정(나무위키 조선 문서)
        canvas.Text($"최대적재량  {total}", x + 24, y + 18, 240, 26, 18, Canvas.White, 0, true);
        (string Label, int Step)[] steps = [("−5%", -5), ("−1%", -1), ("+1%", 1), ("+5%", 5)];
        for (int i = 0; i < steps.Length; i++)
        {
            int next = Math.Clamp(load + steps[i].Step, -reach, reach);
            if (canvas.Button(steps[i].Label, x + 24 + i * 62, y + 132, 58, 28, can && next != load, 13)) _workLoad = next;
        }
        if (canvas.Button("Max", x + 24 + 4 * 62, y + 132, 58, 28, can && load != reach, 13)) _workLoad = reach;
        if (canvas.Button("원래", x + 24 + 5 * 62, y + 132, 58, 28, can && load != 0, 13)) _workLoad = 0;
        // 범위는 그 %로 실제 나오는 최대적재량 — 창고와 선실 · 포실을 맞바꾸는 모형이라 본디 값의 ±25% 보다 좁다
        canvas.Text($"가능 범위   {voyage.LoadTotal(-reach)} ~ {voyage.LoadTotal(reach)}  (±{reach}%)", x + 44, y + 52, 320, 22, 15, Canvas.White);
        canvas.Text($"적정 범위   {voyage.LoadTotal(-proper)} ~ {voyage.LoadTotal(proper)}  (±{proper}%)", x + 44, y + 76, 320, 22, 15, Canvas.White);
        canvas.Text($"현재 용량   {voyage.LoadTotal(voyage.ShipLoad)}" + (load != voyage.ShipLoad ? $"  →  {total} (창고 {load:+0;-0;0}%)" : ""), x + 44, y + 102, 380, 22, 15, load != voyage.ShipLoad ? new Color4(0.55f, 1f, 0.6f, 1) : Canvas.White);
        // 오버 — 적정 범위(조선 랭크 %)를 넘기면 돛 · 내파 · 속도가 깎이고, 거듭하면 쌓인다
        int overNow = voyage.Work.Over + Math.Max(0, Math.Abs(voyage.ShipLoad) - 20), overThen = load == voyage.ShipLoad ? overNow : voyage.OverAfter(voyage.Work.Over, voyage.ShipLoad, load) + Math.Max(0, Math.Abs(load) - 20);
        if (overThen > 0 || overNow > 0)
            canvas.Text($"오버 {overNow}%p" + (overThen != overNow ? $" → {overThen}%p" : "") + $" — 돛 · 내파 · 속도 −{Math.Min(80, overThen * 2)}%", x + 44, y + 160, 400, 18, 12, new Color4(1f, 0.6f, 0.5f, 1));
        canvas.Fill(x + 24, y + 176, 420, 150, new Color4(0.03f, 0.05f, 0.16f, 0.8f));
        // 원본의 설명 글(화면 그대로)
        canvas.Text("적재량을 늘리면 적재화물을 많이 실을 수 있으나 선회 속도가 떨어집니다.\n또, 물의 저항이 커져서 선박의 속도가 떨어집니다" + (can ? "" : $"\n\n※ 적재 변경은 조선 랭크 {Voyage.LoadRank} 부터"),
                    x + 34, y + 184, 400, 136, 14, Canvas.White);
        canvas.Line(x + 470, y + 14, x + 470, y + h - 60, new Color4(0.5f, 0.55f, 0.75f, 0.8f), 1);
        float rx = x + 490, rw = x + w - 20 - rx;
        var now = voyage.StatsOf(voyage.Ship, voyage.ShipMaterialId, voyage.ShipLoad);
        var then = voyage.StatsOf(voyage.Ship, voyage.ShipMaterialId, load);
        if (load != voyage.ShipLoad && voyage.OverAfter(voyage.Work.Over, voyage.ShipLoad, load) is > 0 and var overNext)
            then = voyage.Worked(then, new ShipWork { Over = overNext }, voyage.Ship);      // 오버로 깎이는 돛 · 내파를 미리 본다
        if (voyage.Work.Over > 0) now = voyage.Worked(now, new ShipWork { Over = voyage.Work.Over }, voyage.Ship);
        canvas.Fill(rx, y + 14, 150, 24, new Color4(0.72f, 0.76f, 0.84f, 0.92f));
        canvas.Text("선창 정보", rx, y + 15, 150, 22, 15, new Color4(0.05f, 0.08f, 0.2f, 1), 1, true, false);
        (int Icon, string Label, int Was, int Will)[] rows =
        [
            (330, "선실", now.MaxCrew, then.MaxCrew), (305, "포실", now.Guns, then.Guns), (323, "창고", now.Hold, then.Hold),
            (308, "선회", now.Turn, then.Turn), (306, "세로돛", now.VerticalSail, then.VerticalSail), (307, "가로돛", now.HorizontalSail, then.HorizontalSail), (309, "내파", now.WaveResist, then.WaveResist),
        ];
        for (int k = 0; k < rows.Length; k++)
        {
            float ly = y + 48 + k * 26 + (k >= 3 ? 34 : 0);
            if (k == 3) { canvas.Fill(rx, ly - 30, 150, 24, new Color4(0.72f, 0.76f, 0.84f, 0.92f)); canvas.Text("성능", rx, ly - 29, 150, 22, 15, new Color4(0.05f, 0.08f, 0.2f, 1), 1, true, false); }
            int icon = rows[k].Icon;
            canvas.Image($"gm0:{icon}", () => (_markParts ??= new UiParts(0)).Pixels(icon), rx + 6, ly + 1, 18, 18);
            canvas.Text(rows[k].Label, rx + 30, ly, 80, 20, 14, Canvas.White);
            var color = rows[k].Will > rows[k].Was ? new Color4(0.55f, 1f, 0.6f, 1) : rows[k].Will < rows[k].Was ? new Color4(1f, 0.65f, 0.5f, 1) : Canvas.White;
            canvas.Text(rows[k].Will == rows[k].Was ? $"{rows[k].Was}" : $"{rows[k].Was} → {rows[k].Will}", rx + 110, ly, rw - 120, 20, 14, color, 2);
        }
        canvas.Text($"{voyage.Ship.Name}     강화 횟수 {voyage.Work.Times}/{voyage.MaxTimesOf(voyage.Ship)}" + (voyage.OverWork ? $"  초과 강화 — 성공률 {voyage.OverWorkChance(_workItems)}%" + (voyage.OverWorkChance(_workItems) == 100 ? "(캐시 재료)" : "") : ""), x + 20, y + h - 44, 520, 22, 14, voyage.OverWork ? new Color4(1f, 0.7f, 0.4f, 1) : Canvas.Gold);
        string? blocker = voyage.BuildBlocker(_workItems, _workWood);
        if (canvas.Button("◀ 돌아가기", x + w - 370, y + h - 50, 110, 34, true, 14)) _workPage = 0;
        if (canvas.Button("강화한다", x + w - 250, y + h - 50, 110, 34, blocker == null))
        {
            voyage.StrengthenWith(_workItems.ToList(), _workWood, _workLoad);
            _workItems.Clear();
            (_workWood, _workPage, _workLoad) = (0, 0, null);
        }
        if (canvas.Button("취소", x + w - 130, y + h - 50, 110, 34)) { voyage.Dialog = _workBack; _workItems.Clear(); (_workPage, _workLoad) = (0, null); }
    }

    private void StrengthenWindow()
    {
        // 보통 강화도 옵션 스킬 부여도 원본의 짜임(소유 재료 · 넣는 칸)으로 한다 — 아래 옛 창은 이제 안 쓴다
        MaterialStrengthenWindow();
        if (_workItems != null) return;
        const float w = 860, h = 530;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        var book = voyage.Data.ShipWorks;
        canvas.Text($"강화     {voyage.Ship.Name}     {(_workSkillOnly ? "옵션 스킬 부여 — 강화 성능은 변하지 않는다" : $"강화 횟수 {voyage.Work.Times}/{voyage.MaxTimesOf(voyage.Ship)}")}     소지금 {voyage.Money:N0} Ð", x + 20, y + 12, w - 40, 26, 18, Canvas.Gold, 0, true);
        string[] statNames = ["Durability", "Sail", "Turn", "Wave", "Hold"], statLabels = ["내구", "돛", "선회", "내파", "창고"];
        float row = y + 50;
        for (int i = 0; i < book.Parts.Count; i++)
        {
            var part = book.Parts[i];
            float px = x + 20 + i % 2 * 232, py = row + i / 2 * 32;
            bool picked = _workPicked.Contains(part.Id);
            string label = statLabels[Math.Max(0, Array.IndexOf(statNames, part.Stat))];
            if (canvas.Button((picked ? "● " : "") + $"{part.Name}  {label}+{part.Amount:0}", px, py, 226, 28, picked || _workPicked.Count < 4, 12))
            {
                if (picked) _workPicked.Remove(part.Id);
                else _workPicked.Add(part.Id);
            }
        }
        // 가진 선박재료(조빌 아이템) — 하나를 재료로 넣으면 배의 재질이 그것으로 바뀐다
        var woods = voyage.WoodsOwned();
        if (!woods.Exists(o => o.Item == _workWood)) _workWood = 0;
        float wy = row + (book.Parts.Count + 1) / 2 * 32 + 6;
        canvas.Text(woods.Count == 0 ? "선박재료(국재질): 가진 것이 없다 — 소지품의 아이템 추가 「재질」 · 「조빌」" : $"선박재료 — 넣으면 재질이 바뀐다 (지금 {voyage.MaterialOf(voyage.ShipMaterialId)?.Name ?? "기본"})", x + 20, wy, 470, 20, 12, Canvas.Dim);
        // 소지품에 있는 선박재료를 소지품처럼 그림 칸으로 늘어놓는다(가진 수와 함께, 올리면 이름) — 한 줄에 열둘, 두 줄까지
        for (int i = 0; i < Math.Min(woods.Count, 24); i++)
        {
            float px = x + 20 + i % 12 * 39, py = wy + 22 + i / 12 * 39;
            bool picked = woods[i].Item == _workWood, over = canvas.Hover(px, py, 37, 37);
            canvas.Fill(px, py, 37, 37, new Color4(0.03f, 0.10f, 0.08f, 0.9f));
            MaterialIcon(woods[i].Material, px + 2, py + 2, 33);
            canvas.Frame(px, py, 37, 37, picked ? new Color4(0.3f, 0.95f, 0.9f, 1) : over ? Canvas.Gold : new Color4(0.75f, 0.75f, 0.8f, 0.6f), picked ? 2.5f : 1);
            int have = voyage.Items.GetValueOrDefault(woods[i].Item);
            if (have > 1) canvas.Text($"{have}", px, py + 19, 35, 18, 12, Canvas.White, 2, true);
            if (over) _tip = ($"{woods[i].Material.Name} — 내구 {woods[i].Material.Durability * 100:0}% · 돛 {woods[i].Material.Sail * 100:0}% (가진 수 {have})", px + 18, py - 2);
            if (over && canvas.Pointer.Clicked && (picked || _workPicked.Count < 4)) _workWood = picked ? 0 : woods[i].Item;
        }
        // 재질 미리보기 — 고른(없으면 포인터를 올린) 선박재료의 빛깔을 입힌 이 배의 모습. 창 오른쪽(자리가 없으면 왼쪽)에 뜬다. 끌면 돌고 휠로 다가선다
        int shownWood = _workWood;
        for (int i = 0; i < Math.Min(woods.Count, 24) && shownWood == 0; i++)
            if (canvas.Hover(x + 20 + i % 12 * 39, wy + 22 + i / 12 * 39, 37, 37)) shownWood = woods[i].Item;
        if (shownWood == 0 && _woodSeen != 0 && woods.Exists(o => o.Item == _woodSeen)) shownWood = _woodSeen;      // 마지막에 본 것을 그대로 둔다
        if (shownWood != 0 && voyage.WoodOf(shownWood) is { } seen)
        {
            _woodSeen = shownWood;
            float pw = Math.Min(340, canvas.Width - (x + w) - 12), px = x + w + 6;
            if (pw < 190) (pw, px) = (Math.Min(340, x - 12), x - Math.Min(340, x - 12) - 6);
            if (pw < 190) (pw, px) = (300, x + w - 306);
            float ph = pw * 0.9f + 34, py = y + 40;
            canvas.Panel(px, py, pw, ph);
            canvas.Block(px, py, pw, ph);
            MaterialIcon(seen, px + 8, py + 5, 24);
            canvas.Text($"재질 미리보기 — {seen.Name}", px + 38, py + 7, pw - 44, 20, 13, Canvas.Gold);
            ShipPreview = (px + 6, py + 32, pw - 12, ph - 38);
            PreviewShip = (voyage.Ship.Model, voyage.HullColor(seen.Id));
        }
        if (_workWood > 0 && voyage.WoodOf(_workWood) is { } chosenWood) canvas.Text($"고른 것: {chosenWood.Name}", x + 300, wy, 190, 20, 12, new Color4(0.5f, 1f, 0.6f, 1), 2);
        canvas.Line(x + 496, y + 50, x + 496, y + h - 60, new Color4(0.5f, 0.55f, 0.75f, 0.8f), 1);

        float rx = x + 512, rw = w - 532;
        canvas.Text("지금 붙은 옵션 스킬", rx, y + 50, rw, 22, 15, Canvas.Gold, 0, true);
        float ry = y + 76;
        // 붙은 스킬마다 왼쪽에 그림 — 전용함 스킬도 함께
        foreach (int id in voyage.Work.Dedicated > 0 ? voyage.Work.Skills.Append(voyage.Work.Dedicated) : voyage.Work.Skills)
        {
            var skill = voyage.Data.OptionSkills.Find(s => s.SkillId == id);
            SkillIcon(id, rx + 2, ry - 2, 1);
            canvas.Text(voyage.OptionName(id) + (id == voyage.Work.Dedicated ? " (전용함)" : ""), rx + 40, ry - 1, rw - 40, 20, 14, Canvas.White);
            canvas.Text(skill == null ? "" : Voyage.OptionNote(skill), rx + 40, ry + 16, rw - 40, 18, 11, Canvas.Dim);
            ry += 38;
        }
        if (voyage.Work.Skills.Count == 0) { canvas.Text($"없음 (칸 {voyage.SkillSlotsOf(voyage.Work)})", rx + 6, ry, rw, 20, 13, Canvas.Dim); ry += 22; }
        ry += 10;
        canvas.Text("이번 강화", rx, ry, rw, 22, 15, Canvas.Gold, 0, true);
        ry += 26;
        foreach (int id in _workPicked)
            if (book.Parts.Find(p => p.Id == id) is { } part)
            {
                canvas.Text($"{part.Name}  ({part.Price:N0})", rx + 6, ry, rw, 20, 13, Canvas.White);
                ry += 20;
            }
        if (_workWood > 0 && voyage.WoodOf(_workWood) is { } wood)
        {
            canvas.Text($"{voyage.ItemName(_workWood)}  → 재질 {wood.Name}", rx + 6, ry, rw, 20, 13, new Color4(0.5f, 1f, 0.6f, 1));
            ry += 20;
        }
        if (voyage.OptionFrom(_workPicked) is { } gain)
        {
            SkillIcon(gain.SkillId, rx + 2, ry + 4, 1);
            canvas.Text($"→ 옵션 스킬 「{gain.Name}」 {Voyage.OptionNote(gain)}", rx + 40, ry + 4, rw - 40, 40, 13, new Color4(0.5f, 1f, 0.6f, 1));
            ry += 44;
        }
        canvas.Text($"값 {voyage.WorkCost(_workPicked):N0} 두캇", rx + 6, ry + 4, rw, 20, 14, Canvas.White);
        // 한 줄에 이으면 넘쳐서 아래 「강화 결과」와 겹친다 — 아랫줄에 작게
        if (_workPicked.Count(voyage.OwnsPart) is > 0 and var owned)
        {
            canvas.Text($"가진 조빌 아이템 {owned}가지는 값 대신 든다", rx + 6, ry + 24, rw, 18, 11, Canvas.Dim);
            ry += 16;
        }
        // 강화한 결과를 미리 — 지금 값 → 강화 뒤 값(오르면 초록, 내리면 주황)
        if (_workPicked.Count + (_workWood > 0 ? 1 : 0) > 0)
        {
            var now = voyage.Stats;
            var then = voyage.StrengthenPreview(_workPicked, _workWood, _workSkillOnly);
            canvas.Text("강화 결과", rx, ry + 34, rw, 22, 15, Canvas.Gold, 0, true);
            (string Label, int Now, int Then)[] rows =
            [
                ("내구", now.Durability, then.Durability), ("세로돛", now.VerticalSail, then.VerticalSail), ("가로돛", now.HorizontalSail, then.HorizontalSail),
                ("선회", now.Turn, then.Turn), ("내파", now.WaveResist, then.WaveResist), ("창고", now.Hold, then.Hold),
            ];
            for (int k = 0; k < rows.Length; k++)
            {
                float cx = rx + 6 + k % 2 * (rw / 2), cy = ry + 60 + k / 2 * 22;
                var color = rows[k].Then > rows[k].Now ? new Color4(0.5f, 1f, 0.6f, 1) : rows[k].Then < rows[k].Now ? new Color4(1f, 0.6f, 0.5f, 1) : Canvas.White;
                canvas.Text(rows[k].Label, cx, cy, 60, 20, 13, Canvas.Dim);
                canvas.Text(rows[k].Then == rows[k].Now ? $"{rows[k].Now}" : $"{rows[k].Now} → {rows[k].Then}", cx + 54, cy, rw / 2 - 66, 20, 14, color);
            }
            canvas.Text($"속도 {now.Knots:0.0} → {then.Knots:0.0}노트", rx + 6, ry + 60 + 3 * 22 + 2, rw, 20, 13, Canvas.Dim);
        }
        var stats = voyage.Stats;
        canvas.Text($"지금: 내구 {stats.Durability} · 세로돛 {stats.VerticalSail} · 가로돛 {stats.HorizontalSail} · 선회 {stats.Turn} · 내파 {stats.WaveResist} · 창고 {stats.Hold}",
                    x + 20, y + h - 84, w - 40, 20, 13, Canvas.Dim);
        canvas.Text("옵션 스킬의 조합: " + string.Join(" · ", book.Skills.Take(5).Select(s => $"{s.Name} = {book.Parts.Find(p => p.Id == s.PartA)?.Name} + {book.Parts.Find(p => p.Id == s.PartB)?.Name}")),
                    x + 20, y + h - 62, w - 280, 40, 11, Canvas.Dim);
        string? blocker = voyage.WorkBlocker(_workPicked, _workWood, _workSkillOnly);
        if (blocker != null) canvas.Text(blocker, rx + 6, y + h - 110, rw, 20, 13, new Color4(1f, 0.5f, 0.45f, 1));
        if (canvas.Button(_workSkillOnly ? "부여한다" : "강화한다", x + w - 250, y + h - 50, 110, 34, blocker == null)) { voyage.Strengthen(_workPicked.ToList(), _workWood, _workSkillOnly); _workPicked.Clear(); _workWood = 0; }
        // 들어온 데로 돌아간다 — 커스텀설정 조선의 강화 방법에서 왔으면 거기로, 선박 구입 창에서 왔으면 거기로
        if (canvas.Button("이전", x + w - 130, y + h - 50, 110, 34)) voyage.Dialog = _workBack;
    }

    private int _partPage;

    /// <summary>몸 틀의 그 부위가 몇 가지인가 — 창이 넣어 준다(몸 묶음을 훑은 결과).</summary>
    public Func<int, string, int>? PartCount;
    private static readonly string[] LookParts = ["", "face", "hair", "body", "leg", "hand", "cap"];

    /// <summary>
    /// 의상(C) — 체형과 얼굴 · 머리 · 옷 · 신발 · 손 · 모자를 바꾼다. 화면 왼쪽에 붙여, 시내에서는 사람이 바뀌는 것이 옆에 보인다.
    /// </summary>
    private void OutfitWindow()
    {
        const float w = 400, h = 380;
        float x = 16, y = (canvas.Height - h) / 2;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text("의상", x + 20, y + 12, w - 40, 26, 18, Canvas.Gold, 0, true);
        var looks = voyage.Looks;
        var frames = Voyage.FramesOf(voyage.Male);
        float row = y + 52;
        for (int part = 0; part < 7; part++, row += 38)
        {
            canvas.Text(Voyage.LookNames[part], x + 20, row + 4, 80, 24, 16, Canvas.White);
            int count, at;
            if (part == 0) (count, at) = (frames.Length, Math.Max(0, Array.IndexOf(frames, looks[0])));
            else (count, at) = (PartCount?.Invoke(looks[0], LookParts[part]) ?? 0, looks[part]);
            int lowest = part == 6 ? -1 : 0;                      // 모자는 「없음」이 있다
            void Set(int value) => voyage.SetLook(part, part == 0 ? frames[value] : value);
            if (canvas.Button("◀◀", x + 104, row, 44, 30, count > 0 && at - 10 >= lowest, 12)) Set(at - 10);
            if (canvas.Button("◀", x + 152, row, 36, 30, count > 0 && at > lowest)) Set(at - 1);
            canvas.Text(count == 0 ? "없음" : at < 0 ? "안 씀" : $"{at + 1} / {count}", x + 190, row + 4, 96, 24, 15, count == 0 ? Canvas.Dim : Canvas.White, 1);
            if (canvas.Button("▶", x + 288, row, 36, 30, at < count - 1)) Set(at + 1);
            if (canvas.Button("▶▶", x + 328, row, 44, 30, at + 10 <= count - 1, 12)) Set(at + 10);
        }
        canvas.Text(voyage.TownView ? "오른쪽 끌기로 돌려 본다." : "사람은 시내에서 보인다 — 항구의 「시내」로 들어가서 고르면 바로 보인다.", x + 20, row + 2, w - 40, 40, 12, Canvas.Dim);
        if (canvas.Button("이전", x + w - 130, y + h - 46, 110, 32)) voyage.Dialog = Dialog.None;
    }
    private ShipData? _buildShip;

    /// <summary>대본용: 그 배로 커스텀설정 조선 창을 연다.</summary>
    public void OpenCustomBuild(int shipId)
    {
        _buildShip = voyage.Data.Ships.Find(s => s.Id == shipId);
        (voyage.Dialog, _buildMaterial, _buildLoad) = (Dialog.CustomBuild, 99, 15);
    }
    private int _buildMaterial, _buildLoad, _buildTop;
    private (int Count, int Visible) _buildRows;

    /// <summary>
    /// 커스텀설정 조선 — 원본의 차례를 따른다: 배(조선소 창에서 고른 것) → 재질 → 적재 변경 → 맡기기.
    /// 날이 차면 어느 조선소에서나 받아 부두에 둔다.
    /// </summary>
    private void CustomBuildWindow()
    {
        // 왼쪽에 「재질」 목록(그림 · 이름 · 값), 오른쪽에 나머지 — x 는 오른쪽 칸의 왼쪽 끝이다
        const float w = 760, h = 660, side = 270;
        float left = (canvas.Width - w - side) / 2, x = left + side, y = (canvas.Height - h) / 2;
        canvas.Panel(left, y, w + side, h);
        canvas.Block(left, y, w + side, h);
        canvas.Text($"커스텀설정 조선     조선 랭크 {voyage.ShipbuildingRank}     소지금 {voyage.Money:N0} Ð", x + 20, y + 12, w - 40, 26, 18, Canvas.Gold, 0, true);

        // 맡겨 둔 배
        if (voyage.Ordered is { } order)
        {
            canvas.Fill(x + 16, y + 46, w - 32, 34, new Color4(0.04f, 0.07f, 0.2f, 0.85f));
            canvas.Text($"맡겨 둔 배: {order.Ship.Name} · {voyage.MaterialOf(order.Material)?.Name} · 적재 {order.Load:+0;-0;0}%   " +
                        (order.DaysLeft > 0 ? $"앞으로 {Math.Ceiling(order.DaysLeft):0}일(바다에서 보낸 날로 센다)" : "다 지어졌다"), x + 26, y + 52, w - 190, 22, 14, Canvas.White);
            if (canvas.Button("받기", x + w - 136, y + 49, 110, 28, voyage.ReceiveBlocker == null, 14)) voyage.ReceiveShip();
        }

        var materials = voyage.MaterialsToUse();
        if (_buildShip is not { } ship || voyage.ShipbuildingRank <= 0 || materials.Count == 0)
        {
            canvas.Text(voyage.ShipbuildingRank <= 0 ? "조선 스킬이 있어야 배를 지을 수 있다. 스킬 창(X)의 전투 쪽지에서 배운다." : "조선소 창에서 지을 배를 고르고 들어온다.",
                        x + 20, y + 100, w - 40, 24, 15, Canvas.Dim);
            if (canvas.Button("이전", x + w - 140, y + h - 50, 110, 34)) voyage.Dialog = Dialog.Shipyard;
            return;
        }
        _buildMaterial = Math.Clamp(_buildMaterial, 0, materials.Count - 1);
        var material = materials[_buildMaterial];
        var plain = Dho.Data.ShipStats.Of(ship, voyage.Data.Settings.Ships);
        var built = voyage.Worked(voyage.StatsOf(ship, material.Id, _buildLoad), new ShipWork { Over = voyage.OverAfter(0, 0, _buildLoad) }, ship);      // 오버로 깎이는 것까지 미리 본다

        float row = y + 96;
        canvas.Text("건조할 배", x + 20, row + 3, 110, 22, 15, Canvas.Gold, 0, true);
        canvas.Text(ship.Name, x + 140, row + 2, 300, 24, 16, Canvas.White);
        row += 36;
        canvas.Text("재질", x + 20, row + 3, 110, 22, 15, Canvas.Gold, 0, true);
        MaterialIcon(material, x + 140, row - 4, 34);
        canvas.Text(material.Name, x + 180, row + 3, 210, 24, 16, Canvas.White);
        // 왼쪽 목록 — 줄을 누르거나 휠 · ↑↓
        canvas.Fill(left + 16, y + 14, side - 16, 24, new Color4(0.72f, 0.76f, 0.84f, 0.92f));
        canvas.Text("재질", left + 16, y + 15, side - 16, 22, 15, new Color4(0.05f, 0.08f, 0.2f, 1), 1, true, false);
        const int woods = 11;
        _buildRows = (materials.Count, woods);
        _buildTop = Math.Clamp(_buildTop, 0, Math.Max(0, materials.Count - woods));
        for (int i = _buildTop; i < Math.Min(materials.Count, _buildTop + woods); i++)
        {
            float wy = y + 44 + (i - _buildTop) * 52;
            bool chosen = i == _buildMaterial, hover = canvas.Hover(left + 16, wy, side - 30, 50);
            if (chosen) canvas.Fill(left + 16, wy, side - 30, 50, new Color4(0.12f, 0.55f, 0.52f, 0.95f));
            else if (hover) canvas.Fill(left + 16, wy, side - 30, 50, new Color4(0.2f, 0.3f, 0.6f, 0.6f));
            canvas.Fill(left + 20, wy + 4, 42, 42, new Color4(0.02f, 0.03f, 0.08f, 0.95f));
            MaterialIcon(materials[i], left + 20, wy + 4, 42);
            canvas.Frame(left + 20, wy + 4, 42, 42, new Color4(0.75f, 0.75f, 0.8f, 0.9f), 1);
            canvas.Text(materials[i].Name, left + 68, wy + 3, side - 100, 22, materials[i].Name.Length > 10 ? 12 : 15, Canvas.White);
            canvas.Text($"{voyage.BuildCost(ship, materials[i].Id):N0} Ð", left + 68, wy + 25, side - 88, 22, 14, Canvas.White, 2);
            if (hover) _tip = ($"{materials[i].Name} — 내구도 {materials[i].Durability * 100:0}% · 돛 {materials[i].Sail * 100:0}%" + (Voyage.IsSpecial(materials[i].Id) && voyage.WoodItemName(materials[i].Id) is { } needs ? $" · 필요 장비 재료: {needs}" : ""), left + side / 2, wy);
            if (hover && canvas.Pointer.Clicked) _buildMaterial = i;
        }
        ScrollBar(left + side - 12, y + 44, woods * 52 - 2, woods, materials.Count, ref _buildTop);
        canvas.Line(left + side + 4, y + 44, left + side + 4, y + h - 14, new Color4(0.5f, 0.55f, 0.75f, 0.8f), 1);
        canvas.Text($"내구도 {material.Durability * 100:0}% · 돛 {material.Sail * 100:0}%   (고를 수 있는 재질 {materials.Count}/{voyage.Data.ShipMaterials.Count} — 조선 랭크가 오르면 는다)", x + 400, row + 5, w - 420, 22, 12, Canvas.Dim);
        row += 36;
        canvas.Text("적재 변경", x + 20, row + 3, 110, 22, 15, Canvas.Gold, 0, true);
        bool canLoad = voyage.ShipbuildingRank >= Voyage.LoadRank;
        _buildLoad = Math.Clamp(_buildLoad, -voyage.LoadReach, voyage.LoadReach);
        if (canvas.Button("−5", x + 140, row, 44, 28, canLoad && _buildLoad > -voyage.LoadReach, 14)) _buildLoad = Math.Max(-voyage.LoadReach, _buildLoad - 5);
        canvas.Text($"{_buildLoad:+0;-0;0}%", x + 190, row + 3, 150, 24, 16, Math.Abs(_buildLoad) > voyage.LoadProper ? new Color4(1f, 0.6f, 0.4f, 1) : Canvas.White, 1);
        if (canvas.Button("+5", x + 346, row, 44, 28, canLoad && _buildLoad < voyage.LoadReach, 14)) _buildLoad = Math.Min(voyage.LoadReach, _buildLoad + 5);
        canvas.Text(!canLoad ? $"조선 랭크 {Voyage.LoadRank} 부터 바꿀 수 있다" : Math.Abs(_buildLoad) > voyage.LoadProper ? $"조선 랭크 %(±{voyage.LoadProper}%)를 넘겨 돛 · 내파 · 속도가 {Math.Min(80, (voyage.OverAfter(0, 0, _buildLoad) + Math.Max(0, Math.Abs(_buildLoad) - 20)) * 2)}% 깎인다" : $"+ 는 창고를 늘리고 선실 · 포실을 줄인다. ±{voyage.LoadProper}% 까지는 손해가 없다(가능 ±{voyage.LoadReach}%)",
                    x + 400, row + 5, w - 420, 22, 12, Canvas.Dim);
        row += 44;

        // 능력치: 그대로 / 지은 것
        (string Label, int Plain, int Built)[] cells =
        [
            ("내구도", plain.Durability, built.Durability), ("세로돛", plain.VerticalSail, built.VerticalSail), ("가로돛", plain.HorizontalSail, built.HorizontalSail),
            ("선회", plain.Turn, built.Turn), ("내파", plain.WaveResist, built.WaveResist), ("장갑", plain.Armor, built.Armor),
            ("선실", plain.MaxCrew, built.MaxCrew), ("대포", plain.Guns, built.Guns), ("창고", plain.Hold, built.Hold),
        ];
        for (int i = 0; i < cells.Length; i++)
        {
            float cx = x + 20 + i % 3 * 240, cy = row + i / 3 * 30;
            canvas.Fill(cx, cy, 232, 26, new Color4(0.04f, 0.07f, 0.2f, 0.85f));
            canvas.Text(cells[i].Label, cx + 8, cy + 3, 80, 20, 13, Canvas.Dim);
            var color = cells[i].Built > cells[i].Plain ? new Color4(0.5f, 1f, 0.6f, 1) : cells[i].Built < cells[i].Plain ? new Color4(1f, 0.6f, 0.5f, 1) : Canvas.White;
            canvas.Text($"{cells[i].Plain} → {cells[i].Built}", cx, cy + 2, 224, 22, 15, color, 2);
        }
        row += 100;
        canvas.Text($"건조일수 {Voyage.BuildDays(ship)}일 · 값 {voyage.BuildCost(ship, material.Id):N0} 두캇 · 속도 {plain.Knots:0.0} → {built.Knots:0.0}노트" + (plain.Real ? "" : "   (이 배의 능력치는 지어낸 값)"),
                    x + 20, row, w - 40, 22, 14, Canvas.White);
        string? blocker = voyage.BuildBlocker(ship, material.Id, _buildLoad);
        if (blocker != null) canvas.Text(blocker, x + 20, row + 24, w - 40, 22, 14, new Color4(1f, 0.5f, 0.45f, 1));

        // 원본처럼 고른 재질의 빛깔을 입힌 배의 모습 — 끌면 돌고 휠로 다가선다
        canvas.Fill(x + 20, row + 52, w - 40, y + h - 62 - (row + 52), new Color4(0.02f, 0.04f, 0.12f, 0.55f));
        ShipPreview = (x + 20, row + 52, w - 40, y + h - 62 - (row + 52));
        PreviewShip = (ship.Model, voyage.HullColor(material.Id));
        if (canvas.Button("건조를 맡긴다", x + w - 180, y + h - 50, 160, 34, blocker == null))
        {
            // 맡겼으면 창을 닫고 조선소 주인 차림으로 돌아간다
            voyage.OrderShip(ship, material.Id, _buildLoad);
            if (voyage.Ordered != null) voyage.Dialog = Dialog.ShipyardMenu;
        }
        if (canvas.Button("이전", x + 20, y + h - 50, 110, 34)) voyage.Dialog = Dialog.Shipyard;
    }

    /// <summary>왕궁 — 작위와 공적, 받은 칙명의 진행, 새로 받을 칙명.</summary>
    private int _farmChosen;

    /// <summary>개인농장 — 네 칸의 시설과 쌓인 것, 짓기 · 랭크 올리기 · 거두기.</summary>
    private void FarmWindow()
    {
        const float w = 760, h = 430;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text("개인농장", x + 20, y + 12, 200, 28, 19, Canvas.Gold, 0, true);
        canvas.Text($"소지금 {voyage.Money:N0} Ð     창고 빈 칸 {voyage.HoldFree}", x + 20, y + 16, w - 40, 22, 13, Canvas.Dim, 2);
        if (!voyage.FarmOwned)
        {
            canvas.Text($"농장 관리인\n개인농장의 권리를 {Voyage.FarmPrice:N0} 두캇에 넘겨 드리지요. 밭이든 광산이든 네 칸까지 지을 수 있고,\n지어 두면 항해하는 동안에도 날마다 물건이 쌓입니다. 본거지에 들를 때 거둬 가십시오.", x + 20, y + 60, w - 40, 120, 15, Canvas.White);
            if (canvas.Button($"권리를 산다 ({Voyage.FarmPrice:N0})", x + 20, y + h - 50, 240, 34, voyage.Money >= Voyage.FarmPrice)) voyage.BuyFarm();
            if (canvas.Button("이전", x + w - 130, y + h - 50, 110, 34)) voyage.Dialog = Dialog.None;
            return;
        }
        // 왼쪽: 네 칸
        _farmChosen = Math.Clamp(_farmChosen, 0, voyage.Farm.Length - 1);
        for (int i = 0; i < voyage.Farm.Length; i++)
        {
            var plot = voyage.Farm[i];
            float ry = y + 52 + i * 62;
            bool chosen = _farmChosen == i, hover = canvas.Hover(x + 20, ry, 340, 58);
            canvas.Fill(x + 20, ry, 340, 58, chosen ? new Color4(0.12f, 0.55f, 0.52f, 0.95f) : hover ? new Color4(0.2f, 0.3f, 0.6f, 0.6f) : new Color4(0.04f, 0.07f, 0.2f, 0.85f));
            if (hover && canvas.Pointer.Clicked) _farmChosen = i;
            if (plot.Kind < 0) { canvas.Text($"{i + 1}. 빈 땅", x + 32, ry + 18, 300, 22, 15, Canvas.Dim); continue; }
            var good = voyage.FarmProduct(plot);
            if (good != null) GoodIcon(good.Id, x + 28, ry + 9, 40);
            canvas.Text($"{i + 1}. {Voyage.FarmKinds[plot.Kind]}  랭크 {plot.Rank}", x + 78, ry + 6, 270, 22, 15, Canvas.White);
            int ready = voyage.FarmReady(plot);
            canvas.Text($"{good?.Name}  {ready} / {Voyage.FarmStore(plot)}   (하루 {Voyage.FarmRate(plot)})", x + 78, ry + 30, 270, 20, 13, ready >= Voyage.FarmStore(plot) ? Canvas.Gold : Canvas.Dim);
        }
        canvas.Line(x + 374, y + 52, x + 374, y + h - 60, new Color4(0.5f, 0.55f, 0.75f, 0.8f), 1);
        // 오른쪽: 고른 칸에 지을 시설과 기를 것
        float rx = x + 390;
        var picked = voyage.Farm[_farmChosen];
        canvas.Text($"{_farmChosen + 1}번 칸 — 지을 시설 ({Voyage.FarmBuildPrice:N0} 두캇, 바꾸면 쌓인 것은 사라진다)", rx, y + 52, w - 410, 20, 12, Canvas.Dim);
        float line = y + 76;
        for (int kind = 0; kind < Voyage.FarmKinds.Length; kind++)
        {
            var products = voyage.FarmProducts(kind);
            if (products.Count == 0) continue;
            canvas.Text(Voyage.FarmKinds[kind], rx, line + 4, 80, 22, 14, Canvas.White);
            for (int p = 0; p < products.Count; p++)
            {
                bool now = picked.Kind == kind && picked.Product == p;
                if (canvas.Button(products[p].Name, rx + 84 + p * 92, line, 88, 26, !now && voyage.Money >= Voyage.FarmBuildPrice, 12)) voyage.BuildFarm(_farmChosen, kind, p);
            }
            line += 31;
        }
        if (picked.Kind >= 0 && picked.Rank < Voyage.FarmMaxRank
            && canvas.Button($"랭크 올리기 ({voyage.FarmUpgradePrice(picked):N0})", x + 190, y + h - 50, 220, 34, voyage.Money >= voyage.FarmUpgradePrice(picked), 14)) voyage.UpgradeFarm(_farmChosen);
        if (canvas.Button("모두 거둔다", x + 20, y + h - 50, 160, 34, voyage.Farm.Any(p => voyage.FarmReady(p) > 0))) voyage.Harvest();
        if (canvas.Button("이전", x + w - 130, y + h - 50, 110, 34)) voyage.Dialog = Dialog.None;
    }

    /// <summary>투자 — 이 도시에 돈을 넣는다. 제 나라의 점유 · 발전도 · 넣은 돈과, 얻는 공적.</summary>
    private void InvestWindow()
    {
        const float w = 620, h = 360;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        var city = voyage.City;
        string owner = voyage.Data.Nations.Find(n => n.Id == city.Nation)?.Name ?? "소속 없음";
        canvas.Text($"투자 — {city.Name}", x + 20, y + 12, w - 40, 28, 19, Canvas.Gold, 0, true);
        canvas.Text(city.Nation == 0 ? owner : $"소속 {owner}", x + 20, y + 14, w - 40, 24, 14, Canvas.White, 2);
        int share = voyage.ShareIn(city);
        canvas.Text($"{voyage.NationName}의 영향도 {share}%", x + 20, y + 56, 300, 22, 15, Canvas.White);
        canvas.Text(voyage.Text(7001, "이 도시에 투자합니다. 투자하면 자국의 영향도가 높아집니다"), x + 20, y + 36, w - 40, 18, 11, Canvas.Dim);
        canvas.Fill(x + 20, y + 82, w - 40, 10, new Color4(0.03f, 0.05f, 0.16f, 0.9f));
        canvas.Fill(x + 20, y + 82, (w - 40) * share / 100f, 10, new Color4(0.35f, 0.75f, 0.95f, 1));
        canvas.Line(x + 20 + (w - 40) / 2, y + 78, x + 20 + (w - 40) / 2, y + 96, Canvas.Gold, 1);
        canvas.Text(city.Nation == voyage.NationId ? "우리 나라의 항구다." : "영향도가 절반을 넘으면 우리 나라의 동맹항이 된다.", x + 20, y + 98, w - 40, 20, 12, Canvas.Dim);
        canvas.Text($"발전도 {voyage.GrowthOf(city)} / 10      당신의 투자금액 {voyage.InvestedIn(city):N0} 두캇", x + 20, y + 128, w - 40, 22, 14, Canvas.White);
        canvas.Text($"작위 {voyage.TitleName}      공적 {voyage.Merit} / {voyage.MeritToNext}" + (voyage.TitleDue ? "   — 본국의 왕궁에서 작위를 받는다" : ""), x + 20, y + 154, w - 40, 22, 14, voyage.TitleDue ? Canvas.Gold : Canvas.White);
        canvas.Text("투자금액", x + 20, y + 196, 200, 22, 15, Canvas.Gold, 0, true);
        string[] labels = ["5만", "50만", "500만", "5000만"];
        for (int i = 0; i < Voyage.InvestSteps.Length; i++)
        {
            int amount = Voyage.InvestSteps[i];
            if (canvas.Button(labels[i], x + 20 + i * 146, y + 224, 138, 34, voyage.InvestBlocker(amount) == null)) voyage.Invest(amount);
            canvas.Text($"공적 +{voyage.InvestMerit(amount)}", x + 20 + i * 146, y + 260, 138, 18, 12, Canvas.Dim, 1);
        }
        if (voyage.InvestBlocker(Voyage.InvestSteps[0]) is { } why) canvas.Text(why, x + 20, y + h - 43, 300, 22, 13, new Color4(1f, 0.5f, 0.45f, 1));
        else canvas.Text($"소지금 {voyage.Money:N0} Ð", x + 20, y + h - 43, 300, 22, 14, Canvas.Dim);
        if (canvas.Button("이전", x + w - 130, y + h - 50, 110, 34)) voyage.Dialog = Dialog.None;
    }

    private void CourtWindow()
    {
        const float w = 720, h = 420;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text($"왕궁     작위: {voyage.TitleName}     공적 {voyage.Merit} / {voyage.MeritToNext}", x + 20, y + 12, w - 40, 26, 18, Canvas.Gold, 0, true);
        float row = y + 54;
        if (voyage.Order is { } order)
        {
            canvas.Text($"받든 칙명: {order.Title}", x + 20, row, w - 40, 24, 16, Canvas.White, 0, true);
            canvas.Text(order.Text, x + 20, row + 28, w - 40, 44, 15, Canvas.White);
            canvas.Text($"{voyage.OrderGoal(order)}   ({voyage.OrderState})", x + 20, row + 76, w - 40, 22, 14, Canvas.Gold);
            canvas.Text($"하사금 {order.Reward:N0} 두캇 · 공적 {order.Merit}", x + 20, row + 100, w - 40, 22, 14, Canvas.Dim);
            if (canvas.Button("아뢴다", x + 20, y + h - 50, 130, 34, voyage.AtCourt && voyage.OrderDone)) voyage.CompleteOrder();
            if (canvas.Button("반납한다", x + 160, y + h - 50, 130, 34)) voyage.AbandonOrder();
            if (!voyage.AtCourt) canvas.Text("본국의 왕궁에서 아뢴다.", x + 300, y + h - 43, 250, 22, 13, Canvas.Dim);
        }
        else if (!voyage.AtCourt) canvas.Text("칙명은 제 나라 본거지의 왕궁에서 받는다.", x + 20, row, w - 40, 24, 15, Canvas.Dim);
        else
            foreach (var offered in voyage.OrdersOffered())
            {
                canvas.Text(offered.Title, x + 20, row + 3, 160, 24, 16, Canvas.White);
                canvas.Text($"{voyage.OrderGoal(offered)} — {offered.Reward:N0} 두캇 · 공적 {offered.Merit}", x + 180, row + 5, 380, 22, 12, Canvas.Dim);
                if (canvas.Button("받든다", x + w - 120, row, 100, 26, true, 13)) voyage.AcceptOrder(offered);
                row += 32;
            }
        // 서기관: 국가공헌 훈장증서 200장 → 전용함 건조 허가증
        if (voyage.AtCourt)
        {
            int medals = voyage.Items.GetValueOrDefault(Voyage.MedalPaper);
            canvas.Line(x + 20, y + h - 100, x + w - 20, y + h - 100, Canvas.PanelEdge, 1);
            canvas.Text($"서기관 — 국가공헌 훈장증서 {Voyage.PermitCost}장을 전용함 건조 허가증으로 바꿔 준다.", x + 20, y + h - 96, w - 170, 20, 13, Canvas.White);
            canvas.Text($"가진 훈장증서 {medals} · 허가증 {voyage.Items.GetValueOrDefault(Voyage.ShipPermit)}", x + 20, y + h - 77, w - 170, 20, 12, Canvas.Dim);
            if (canvas.Button("교환한다", x + w - 140, y + h - 96, 120, 28, medals >= Voyage.PermitCost, 13)) voyage.ExchangePermit();
        }
        // 입항 허가 — 자국 본거지에서는 중남미 동쪽 해안의 것을 내준다
        if (voyage.PermitOffered() is { CityId: 0 } homePermit)
        {
            canvas.Text($"{homePermit.Name} 입항 허가 — 명성 {voyage.TotalFame:N0} / {homePermit.Fame:N0}", x + 20, y + h - 124, w - 200, 20, 13, Canvas.White);
            if (canvas.Button("허가를 받는다", x + w - 170, y + h - 128, 150, 26, voyage.TotalFame >= homePermit.Fame, 13)) voyage.TakePermit();
        }
        // 공적이 찼으면 작위를 받는다
        if (voyage.AtCourt && canvas.Button(voyage.TitleDue ? "작위를 받는다" : "공적이 모자라다", x + w - 300, y + h - 50, 160, 34, voyage.TitleDue, 14)) voyage.ReceiveTitle();
        if (canvas.Button("이전", x + w - 130, y + h - 50, 110, 34)) voyage.Dialog = Dialog.None;
    }

    /// <summary>부관 — 고용한 사람들의 담당 · 레벨과, 이 도시의 주점에서 만날 수 있는 후보.</summary>
    private int _aideChosen;
    private readonly int[] _recruitBasket = new int[3];
    private int _recruitKind;

    /// <summary>사람 모형을 그릴 네모와 겉모습 — 부관고용 창이 떠 있는 프레임에만 값이 있다.</summary>
    public (float X, float Y, float W, float H, int Frame, int Face, int Hair, int Body, int Leg)? FigurePreview;

    /// <summary>
    /// 부관고용 — 원본의 짜임: 왼쪽 「부관 후보」(이름과 담당), 오른쪽에 고른 후보의 모습 · 고용 비용 · 소지금, 확인 · 이전.
    /// 위에는 이미 고용한 부관(담당 바꾸기 · 해고). 얼굴은 원본 그림이다 — <c>0010\0001\sd</c> 의 무리 30(작은 얼굴 48 × 48) · 31(반기는 모습 128 × 128) · 32(풀 죽은 모습),
    /// id 가 부관 번호(1 ~ 32). 나라 깃발은 못 찾아 없다.
    /// </summary>
    private Aide? _captainFor;
    /// <summary>대본용: 아이템 추가의 의상 갈래를 그 찾는 말로 연다.</summary>
    public void GearSearchForTest(string word) => (_itemTab, _addAll, _addRecipes, _addMaterials, _addDyes, _addPapers, _addTickets, _addShipItems, _addGear, _addLab, _itemPage, _addGroup, _addSearch) = (3, false, false, false, false, false, false, false, true, false, 0, 0, word);
    /// <summary>대본용: 적재화물 창에서 그 차례의 교역품을 고른다.</summary>
    public void CargoPickForTest(int index) => _cargoChosen = index;
    /// <summary>대본용: 아이템 추가의 「전체」 갈래를 그 찾는 말로 연다.</summary>
    public void AllSearchForTest(string word) => (_itemTab, _addAll, _addRecipes, _addMaterials, _addDyes, _addPapers, _addTickets, _addShipItems, _addGear, _addLab, _itemPage, _addGroup, _addSearch) = (3, true, false, false, false, false, false, false, false, false, 0, 0, word);
    // 「아이템 추가」의 수량 — 1 · 10 · 100(기본 1). 「추가」 왼쪽의 작은 단추를 누를 때마다 바뀌고, 모든 줄이 같은 수량을 쓴다
    private int _addCount = 1;

    private bool AddWithCount(float bx, float by)
    {
        if (canvas.Button($"×{_addCount}", bx, by, 40, 26, true, 11)) _addCount = _addCount switch { 1 => 10, 10 => 100, _ => 1 };
        return canvas.Button("추가", bx + 42, by, 48, 26, true, 13);
    }
    /// <summary>대본용: 교역 창을 매각 쪽으로.</summary>
    public void SellSideForTest() => (_selling, _tradePage, _tradeChosen) = (true, 0, 0);
    /// <summary>대본용: 첫 부관의 배 고르는 창을 띄운다.</summary>
    public void CaptainPickForTest() => _captainFor = voyage.Aides.FirstOrDefault();

    private void AideWindow()
    {
        const float w = 800, h = 520, listWidth = 400;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
        // 부관 선장에게 줄 배 고르기 — 부두의 배들
        if (_captainFor is { } captain && voyage.Aides.Contains(captain))
        {
            canvas.Panel(x + 150, y + 60, 500, 340);
            canvas.Block(x + 150, y + 60, 500, 340);
            canvas.Text(voyage.Text(25332, "부관 선장 임명") + $" — {captain.Who.Name}", x + 170, y + 72, 460, 24, 16, Canvas.Gold, 0, true);
            canvas.Text(voyage.Text(16221, "부관 선장에게 부여할 선박을 선택해 주십시오."), x + 170, y + 100, 460, 20, 13, Canvas.White);
            float pick = y + 130;
            foreach (var docked in voyage.Dock.Take(7).ToList())
            {
                var stats = voyage.StatsOf(docked);
                if (canvas.Button($"{docked.Ship.Name} — 창고 {stats.Hold} · 대포 {stats.Guns}", x + 170, pick, 460, 26, true, 13)) { voyage.AppointCaptain(captain, docked); _captainFor = null; }
                pick += 30;
            }
            if (voyage.Dock.Count == 0) canvas.Text("부두에 맡길 배가 없다 — 조선소에서 배를 한 척 더 산다.", x + 170, pick, 460, 20, 13, new Color4(1f, 0.6f, 0.5f, 1));
            if (canvas.Button("이전", x + 540, y + 360, 90, 28, true, 14)) _captainFor = null;
            return;
        }
        _captainFor = null;
        canvas.Fill(0, 0, canvas.Width, 30, new Color4(0.55f, 0.70f, 0.90f, 0.9f));
        canvas.Text("부관고용", 20, 3, 140, 24, 17, new Color4(0.05f, 0.08f, 0.2f, 1), 0, true, false);
        canvas.Text("부관을 고용합니다. 부관에게는 매일 급여를 지불합니다.", 170, 5, 500, 22, 14, new Color4(0.05f, 0.08f, 0.2f, 1), 0, false, false);
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        float row = y + 14;
        canvas.Text($"고용한 부관 {voyage.Aides.Count}/{Voyage.AideSlots}", x + 20, row, 200, 22, 14, Canvas.Gold, 0, true);
        row += 24;
        foreach (var aide in voyage.Aides.ToList())
        {
            int hired = aide.Who.Id;
            canvas.Image($"sd30:{hired}", () => (_discoveryImages ??= new ImageSet(@"0010\0001\sd")).Pixels(30, hired), x + 20, row, 24, 24);
            canvas.Text($"{aide.Who.Name}  Lv {aide.Level}", x + 48, row + 3, 122, 22, 14, Canvas.White);
            if (canvas.Button(voyage.DutyName(aide.Duty), x + 170, row, 100, 24, true, 12)) voyage.NextDuty(aide);
            canvas.Text($"급여 {voyage.AidePay(aide)} · {voyage.Text(16208, "신뢰도")} {aide.Trust:0}", x + 272, row + 4, 70, 20, 10, Canvas.Dim);
            if (canvas.Button("해고", x + 340, row, 50, 24, voyage.Mode == Mode.Port, 12)) voyage.DismissAide(aide);
            row += 26;
            // 부관 선장 — 부두의 배를 맡긴다(항구에서만)
            canvas.Text(aide.Ship is { } own ? $"{voyage.Text(16217, "부관 선장")}: {own.Ship.Name} (창고 +{voyage.StatsOf(own).Hold})" : "", x + 48, row + 3, 190, 18, 12, Canvas.Gold);
            if (aide.Ship == null) { if (canvas.Button(voyage.Text(25332, "부관 선장 임명"), x + 48, row, 130, 22, voyage.Mode == Mode.Port, 12)) _captainFor = aide; }
            else if (canvas.Button(voyage.Text(25333, "부관 선장 해임"), x + 260, row, 130, 22, voyage.Mode == Mode.Port, 12)) voyage.RelieveCaptain(aide);
            row += 28;
        }
        if (voyage.Aides.Count == 0) { canvas.Text("없음", x + 20, row + 2, 100, 20, 13, Canvas.Dim); row += 26; }
        // 지방함대 — 고용한 부관을 이 지역의 일에 내보낸다(오른쪽 아래 칸)
        if (voyage.Data.FleetMissions.Count > 0)
        {
            float fx = x + 20, fy = y + h - 176, fw = listWidth - 20;      // 왼쪽 아래 — 오른쪽은 후보의 모습이 차지한다
            canvas.Fill(fx, fy, fw, 24, new Color4(0.72f, 0.76f, 0.84f, 0.92f));
            canvas.Text("지방함대", fx, fy + 1, fw, 22, 15, new Color4(0.05f, 0.08f, 0.2f, 1), 1, true, false);
            var sent = voyage.Aides.OrderByDescending(a => a.Level).FirstOrDefault();
            string? why = voyage.FleetBlocker(sent);
            canvas.Text(why ?? $"{sent!.Who.Name}(Lv {sent.Level})을(를) 내보낸다 — 잘될 가망 {Voyage.FleetChance(sent)}%", fx, fy + 28, fw, 18, 12, why != null ? new Color4(1f, 0.6f, 0.5f, 1) : Canvas.Dim);
            var offers = voyage.FleetOffers();
            for (int k = 0; k < offers.Count; k++)
                if (canvas.Button(offers[k].Name, fx, fy + 48 + k * 28, fw, 25, why == null, 13)) voyage.SendFleet(sent!, offers[k]);
        }
        row += 6;
        canvas.Fill(x + 20, row, listWidth - 20, 24, new Color4(0.72f, 0.76f, 0.84f, 0.92f));
        canvas.Text("부관 후보", x + 20, row + 1, listWidth - 20, 22, 15, new Color4(0.05f, 0.08f, 0.2f, 1), 1, true, false);
        row += 30;
        bool open = voyage.Mode == Mode.Port && voyage.HasTavern;
        var candidates = open ? voyage.AidesToHire() : [];
        _aideChosen = Math.Clamp(_aideChosen, 0, Math.Max(0, candidates.Count - 1));
        for (int i = 0; i < candidates.Count && row + 50 < y + h - 180; i++, row += 52)
        {
            bool chosen = i == _aideChosen, hover = canvas.Hover(x + 20, row, listWidth - 20, 50);
            if (chosen) canvas.Fill(x + 20, row, listWidth - 20, 50, new Color4(0.12f, 0.62f, 0.55f, 0.95f));
            else if (hover) canvas.Fill(x + 20, row, listWidth - 20, 50, new Color4(0.2f, 0.3f, 0.6f, 0.6f));
            if (hover && canvas.Pointer.Clicked) _aideChosen = i;
            canvas.Fill(x + 24, row + 3, 44, 44, new Color4(0.02f, 0.04f, 0.12f, 0.95f));
            int face = candidates[i].Id;
            if (!canvas.Image($"sd30:{face}", () => (_discoveryImages ??= new ImageSet(@"0010\0001\sd")).Pixels(30, face), x + 24, row + 3, 44, 44))
                canvas.Image("gm0:330", () => (_markParts ??= new UiParts(0)).Pixels(330), x + 30, row + 9, 32, 32);
            canvas.Text(candidates[i].Name, x + 78, row + 3, 200, 22, 16, Canvas.White);
            canvas.Text(voyage.DutyName(candidates[i].Id % 6), x + 78, row + 26, listWidth - 108, 20, 14, Canvas.White, 2);
        }
        if (!open) canvas.Text(voyage.Mode != Mode.Port ? "항구에서만 고용한다." : "이 도시에는 주점이 없다.", x + 24, row, listWidth, 22, 14, Canvas.Dim);
        else if (candidates.Count == 0) canvas.Text("지금은 후보가 없다.", x + 24, row, listWidth, 22, 14, Canvas.Dim);
        canvas.Line(x + listWidth + 14, y + 14, x + listWidth + 14, y + h - 14, new Color4(0.5f, 0.55f, 0.75f, 0.8f), 1);

        float rx = x + listWidth + 30, rw = w - listWidth - 50;
        if (candidates.Count > 0)
        {
            var who = candidates[_aideChosen];
            // 모습은 이름 번호에서 지어 고른다(온전히 그려지는 옷 몇 벌 가운데)
            (int Body, int Leg)[] wear = [(4, 2), (19, 7), (10, 4), (28, 10), (7, 3), (31, 11)];
            bool woman = who.Id % 3 == 0;
            var (body, leg) = woman ? (16 + who.Id % 4, 0) : wear[who.Id % wear.Length];
            // 원본의 초상화가 있으면 그것을, 없으면 지어 고른 3D 모습을 보인다
            float side = Math.Min(rw - 40, h - 160);
            int portrait = who.Id;
            if (canvas.Image($"sd31:{portrait}", () => (_discoveryImages ??= new ImageSet(@"0010\0001\sd")).Pixels(31, portrait), rx + (rw - side) / 2, y + 16, side, side))
                canvas.Frame(rx + (rw - side) / 2, y + 16, side, side, Canvas.PanelEdge, 1.5f);
            else FigurePreview = (rx, y + 14, rw, h - 140, woman ? 1 : 0, who.Id % 9, who.Id % 8, body, leg);
            string? blocker = voyage.AideBlocker(who);
            canvas.Text("고용 비용", rx + 40, y + h - 118, 100, 22, 15, Canvas.White);
            canvas.Text($"{voyage.AideCost(who):N0} Ð", rx, y + h - 118, rw - 8, 22, 16, voyage.Money >= voyage.AideCost(who) ? Canvas.White : new Color4(1f, 0.5f, 0.45f, 1), 2);
            canvas.Image("gm0:311", () => (_markParts ??= new UiParts(0)).Pixels(311), rx + 40, y + h - 92, 20, 20);
            canvas.Text($"{voyage.Money:N0} Ð", rx, y + h - 92, rw - 8, 22, 16, Canvas.White, 2);
            if (blocker != null) canvas.Text(blocker, rx, y + h - 66, rw - 230, 20, 12, new Color4(1f, 0.5f, 0.45f, 1));
            if (canvas.Button("확인", x + w - 224, y + h - 46, 100, 32, blocker == null)) voyage.HireAide(who);
        }
        if (canvas.Button("이전", x + w - 118, y + h - 46, 98, 32)) voyage.Dialog = voyage.Mode == Mode.Port && voyage.HasTavern ? Dialog.Tavern : Dialog.None;
    }

    /// <summary>
    /// 선원 모집 — 원본의 짜임: 왼쪽 「모집선원」(신참 · 중견 · 숙련과 값), 가운데 「모집인원 수」(1 · 5 · 10 — 누르면 담긴다),
    /// 오른쪽 「모집 예정」과 「모집 후의 선원 수」, 고용 비용 · 소지금, 확인 · 이전.
    /// </summary>
    private void RecruitWindow()
    {
        const float w = 760, h = 400;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        void Header(string text, float hx, float hy, float hw)
        {
            canvas.Fill(hx, hy, hw, 24, new Color4(0.72f, 0.76f, 0.84f, 0.92f));
            canvas.Text(text, hx, hy + 1, hw, 22, 15, new Color4(0.05f, 0.08f, 0.2f, 1), 1, true, false);
        }
        void Face(float fx, float fy, float size) =>
            canvas.Image("gm0:330", () => (_markParts ??= new UiParts(0)).Pixels(330), fx, fy, size, size);
        Header("모집선원", x + 20, y + 16, 270);
        for (int i = 0; i < Voyage.Recruits.Length; i++)
        {
            float ry = y + 48 + i * 70;
            bool chosen = i == _recruitKind, hover = canvas.Hover(x + 20, ry, 270, 66);
            if (chosen) canvas.Fill(x + 20, ry, 270, 66, new Color4(0.12f, 0.62f, 0.55f, 0.95f));
            else if (hover) canvas.Fill(x + 20, ry, 270, 66, new Color4(0.2f, 0.3f, 0.6f, 0.6f));
            if (hover && canvas.Pointer.Clicked) _recruitKind = i;
            canvas.Fill(x + 26, ry + 6, 54, 54, new Color4(0.02f, 0.04f, 0.12f, 0.95f));
            Face(x + 33, ry + 13, 40);
            canvas.Text(Voyage.Recruits[i].Name, x + 90, ry + 6, 190, 24, 17, Canvas.White);
            canvas.Text($"{Voyage.Recruits[i].Price:N0} Ð", x + 90, ry + 36, 190, 24, 16, Canvas.White, 2);
        }
        Header("모집인원 수", x + 300, y + 16, 110);
        int planned = _recruitBasket.Sum(), room = (int)Math.Floor(voyage.Stats.MaxCrew - voyage.Crew) - planned;
        int[] amounts = [1, 5, 10];
        for (int i = 0; i < amounts.Length; i++)
        {
            float ry = y + 52 + i * 70;
            bool can = room >= amounts[i], over = can && canvas.Hover(x + 326, ry, 58, 58);
            canvas.Fill(x + 326, ry, 58, 58, over ? new Color4(0.22f, 0.32f, 0.66f, 0.95f) : new Color4(0.04f, 0.07f, 0.2f, 0.85f));
            canvas.Frame(x + 326, ry, 58, 58, over ? Canvas.Gold : Canvas.PanelEdge, 1.2f);
            Face(x + 335, ry + 6, 40);
            canvas.Text($"{amounts[i]}", x + 326, ry + 36, 54, 20, 15, can ? Canvas.White : Canvas.Dim, 2, true);
            if (over && canvas.Pointer.Clicked) { canvas.Pointer.Consumed = true; _recruitBasket[_recruitKind] += amounts[i]; }
        }
        canvas.Line(x + 424, y + 14, x + 424, y + h - 60, new Color4(0.5f, 0.55f, 0.75f, 0.8f), 1);
        float rx = x + 440, rw = w - 460;
        Header("모집 예정", rx, y + 16, rw);
        canvas.Fill(rx, y + 46, rw, 86, new Color4(0.04f, 0.07f, 0.2f, 0.85f));
        float line = y + 50;
        for (int i = 0; i < 3; i++)
        {
            if (_recruitBasket[i] == 0) continue;
            bool hover = canvas.Hover(rx, line, rw, 24);
            canvas.Text($"{Voyage.Recruits[i].Name} × {_recruitBasket[i]}", rx + 8, line + 2, rw - 120, 22, 14, hover ? Canvas.Gold : Canvas.White);
            canvas.Text($"{_recruitBasket[i] * Voyage.Recruits[i].Price:N0} Ð", rx, line + 2, rw - 8, 22, 14, Canvas.White, 2);
            if (hover && canvas.Pointer.Clicked) { canvas.Pointer.Consumed = true; _recruitBasket[i] = 0; }
            line += 26;
        }
        if (planned == 0) canvas.Text("수를 누르면 여기에 담긴다. 담은 줄을 누르면 뺀다.", rx + 8, y + 54, rw - 16, 40, 12, Canvas.Dim);
        Header("모집 후의 선원 수", rx, y + 144, 190);
        Face(rx + 2, y + 176, 24);
        canvas.Text($"{voyage.Crew + planned:0} / {voyage.Stats.MaxCrew}   (필요 {voyage.Stats.MinCrew})", rx + 32, y + 178, rw - 40, 22, 15, Canvas.White);
        canvas.Fill(rx, y + 204, 160, 4, new Color4(0.03f, 0.05f, 0.16f, 0.9f));
        canvas.Fill(rx, y + 204, 160 * (float)Math.Clamp((voyage.Crew + planned) / Math.Max(1, voyage.Stats.MaxCrew), 0, 1), 4, new Color4(0.35f, 0.85f, 0.4f, 1));
        int cost = Enumerable.Range(0, 3).Sum(i => _recruitBasket[i] * Voyage.Recruits[i].Price);
        canvas.Text("고용 비용", rx + 20, y + h - 118, 100, 22, 15, Canvas.White);
        canvas.Text($"{cost:N0} Ð", rx, y + h - 118, rw - 8, 22, 16, voyage.Money >= cost ? Canvas.White : new Color4(1f, 0.5f, 0.45f, 1), 2);
        canvas.Image("gm0:311", () => (_markParts ??= new UiParts(0)).Pixels(311), rx + 20, y + h - 92, 20, 20);
        canvas.Text($"{voyage.Money:N0} Ð", rx, y + h - 92, rw - 8, 22, 16, Canvas.White, 2);
        if (canvas.Button("확인", x + w - 224, y + h - 46, 100, 32, planned > 0 && voyage.Money >= cost))
        {
            voyage.Recruit(_recruitBasket);
            Array.Clear(_recruitBasket);
        }
        if (canvas.Button("이전", x + w - 118, y + h - 46, 98, 32)) { Array.Clear(_recruitBasket); voyage.Dialog = Dialog.None; }
    }

    /// <summary>선박부품 — 타고 있는 배에 단 것(떼어 팔기)과 이 조선소가 파는 것.</summary>
    private void ShipPartWindow()
    {
        const float w = 760, h = 470;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text($"선박부품     {voyage.Ship.Name}     소지금 {voyage.Money:N0} Ð", x + 20, y + 12, w - 40, 26, 18, Canvas.Gold, 0, true);
        canvas.Text($"속도 ×{voyage.PartSpeed:0.00} · 내구 피해 ×{voyage.PartDamage:0.00} · 재해 ×{voyage.PartLuck:0.00}", x + 20, y + 42, w - 40, 20, 13, Canvas.Dim);
        float row = y + 68;
        foreach (var part in voyage.PartStock.TakeLast(4).ToList())
        {
            canvas.Text($"[{Voyage.SlotName[part.Slot]}] {part.Name}", x + 20, row + 3, 300, 22, 14, Canvas.White);
            canvas.Text(voyage.PartNote(part), x + 330, row + 4, 220, 20, 12, Canvas.Dim);
            if (canvas.Button($"팔기 ({voyage.PartPrice(part) / 2:N0})", x + w - 190, row, 170, 24, true, 12)) voyage.SellPart(part);
            row += 27;
        }
        if (voyage.PartStock.Count == 0) { canvas.Text("가진 부품이 없다. 사서 B(선박부품 탈착)로 배에 단다.", x + 20, row + 3, w - 40, 22, 14, Canvas.Dim); row += 27; }
        canvas.Line(x + 16, row + 4, x + w - 16, row + 4, new Color4(0.5f, 0.55f, 0.75f, 0.8f), 1);
        row += 12;

        // 거르기: 부품이 다는 자리
        string[] slots = ["전체", .. Voyage.SlotName];
        for (int i = 0; i < slots.Length; i++)
        {
            bool on = _partSlot == i, over = canvas.Hover(x + 20 + i * 96, row, 90, 24);
            canvas.Fill(x + 20 + i * 96, row, 90, 24, on ? new Color4(0.16f, 0.62f, 0.72f, 0.98f) : over ? new Color4(0.22f, 0.32f, 0.66f, 0.95f) : new Color4(0.06f, 0.10f, 0.28f, 0.9f));
            canvas.Text(slots[i], x + 20 + i * 96, row + 2, 90, 20, 13, Canvas.White, 1);
            if (over && canvas.Pointer.Clicked) (_partSlot, _partPage) = (i, 0);
        }
        row += 32;
        var sale = voyage.PartsForSale().Where(p => _partSlot == 0 || p.Slot == _partSlot - 1).ToList();
        int perPage = Math.Max(1, (int)((y + h - 60 - row) / 27));
        _partRows = (sale.Count, perPage);
        int pages = Math.Max(1, (sale.Count + perPage - 1) / perPage);
        _partPage = Math.Clamp(_partPage, 0, pages - 1);
        foreach (var part in sale.Skip(_partPage * perPage).Take(perPage))
        {
            canvas.Text($"[{Voyage.SlotName[part.Slot]}] {part.Name}", x + 20, row + 3, 300, 22, 14, Canvas.White);
            canvas.Text(voyage.PartNote(part), x + 330, row + 4, 220, 20, 12, Canvas.Dim);
            if (canvas.Button($"구입 ({voyage.PartPrice(part):N0})", x + w - 190, row, 170, 24, voyage.PartBlocker(part) == null, 12)) voyage.BuyPart(part);
            row += 27;
        }
        if (canvas.Button("◀", x + 20, y + h - 50, 40, 34, _partPage > 0)) _partPage--;
        canvas.Text($"{_partPage + 1} / {pages}", x + 64, y + h - 43, 60, 24, 15, Canvas.White, 1);
        if (canvas.Button("▶", x + 128, y + h - 50, 40, 34, _partPage < pages - 1)) _partPage++;
        if (canvas.Button("이전", x + w - 140, y + h - 50, 110, 34)) voyage.Dialog = Dialog.Shipyard;
    }

    /// <summary>선박교환 — 부두에 매어 둔 배로 갈아타거나 판다.</summary>
    private void ShipSwapWindow()
    {
        // 붙은 선박 스킬까지 보이게 넓게 — 화면이 좁으면 화면에 맞춘다. 줄을 눌러 배를 고르면 아래에 그 배의 모습이 뜬다
        if (_cmpOpen) { CompareWindow(); return; }
        if (_swapPicked != null && !voyage.Dock.Contains(_swapPicked)) _swapPicked = null;
        const bool previewing = true;              // 아무것도 안 골랐으면 타고 있는 배를 보인다
        const float previewHeight = 230;
        float h = Math.Min(420 + (previewing ? previewHeight : 0), canvas.Height - 8);
        float w = Math.Min(1200, canvas.Width - 16);
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text($"선박교환     부두 {voyage.Dock.Count}/{Voyage.DockSlots}", x + 20, y + 12, w - 40, 30, 20, Canvas.Gold, 0, true);
        // 한 줄에 배 하나: 이름, 그림 붙은 능력치, 단추
        bool Line(int ship, string name, Dho.Data.ShipStats s, double durability, float row, bool mine, ShipWork work, string form, bool picked)
        {
            canvas.Fill(x + 16, row, w - 32, 50, mine ? new Color4(0.12f, 0.45f, 0.45f, 0.7f) : new Color4(0.04f, 0.07f, 0.2f, 0.85f));
            if (picked) canvas.Frame(x + 16, row, w - 32, 50, Canvas.Gold, 2);
            // 단추들(오른쪽 290)을 뺀 데를 누르면 이 배를 고른다
            bool chosen = canvas.Hover(x + 16, row, w - 32 - (mine ? 0 : 350), 50) && canvas.Pointer.Clicked;
            // 왼쪽에 배의 작은 그림(커스텀설정 조선의 목록과 같은 것)
            canvas.Fill(x + 20, row + 3, 44, 44, new Color4(0.02f, 0.03f, 0.08f, 0.95f));
            ShipIcon(ship, x + 20, row + 3, 44);
            canvas.Frame(x + 20, row + 3, 44, 44, new Color4(0.75f, 0.75f, 0.8f, 0.9f), 1);
            // 이름 뒤의 재질(「 (…)」)은 작은 글씨로 — 길면 아랫줄의 수치와 겹친다
            int split = name.IndexOf(" (", StringComparison.Ordinal);
            string hullName = split > 0 ? name[..split] : name;
            canvas.Text(hullName, x + 74, row + 4, 300, 24, 17, Canvas.White, 0, true);
            if (split > 0)
            {
                float nameWide = hullName.Sum(c => c >= 0x1100 ? 17.2f : c == ' ' ? 5.5f : 10f) + 8;
                canvas.Text(name[(split + 2)..].TrimEnd(')'), x + 74 + nameWide, row + 10, 300 - nameWide, 16, 11, Canvas.Dim);
            }
            (int Icon, string Text)[] cells =
            [
                (310, $"{durability:0}/{s.Durability}"), (323, $"{s.Hold}"), (330, $"{s.MinCrew}~{s.MaxCrew}"), (305, $"{s.Guns}"),
                (306, $"{s.VerticalSail}"), (307, $"{s.HorizontalSail}"), (308, $"{s.Turn}"), (309, $"{s.WaveResist}"),
            ];
            float cx = x + 74;
            foreach (var (icon, text) in cells)
            {
                canvas.Image($"gm0:{icon}", () => (_markParts ??= new UiParts(0)).Pixels(icon), cx, row + 27, 18, 18);
                canvas.Text(text, cx + 21, row + 27, 70, 20, 13, Canvas.White);
                cx += icon is 310 or 330 ? 92 : 62;
            }
            canvas.Text($"{s.Knots:0.0}노트", x + 380, row + 6, 80, 20, 13, Canvas.Dim);
            canvas.Text(form, x + 462, row + 6, 90, 20, 13, form switch { "탐험선" => new Color4(0.65f, 1f, 0.7f, 1), "운송선" => new Color4(1f, 0.86f, 0.45f, 1), "전투함" => new Color4(1f, 0.6f, 0.5f, 1), _ => Canvas.Dim });
            // 붙은 선박 스킬(옵션 스킬과 전용함 스킬)의 그림 — 올리면 이름. 그레이드가 있으면 앞에
            float kx = x + 648;
            if (work.Grade > 0) { canvas.Text($"G{work.Grade}", kx - 4, row + 14, 34, 22, 15, Canvas.Gold, 0, true); kx += 34; }
            var fitted = work.Dedicated > 0 ? work.Skills.Append(work.Dedicated).ToList() : work.Skills;
            float room = x + w - 350 - kx;
            for (int k = 0; k < fitted.Count && (k + 1) * 32 <= room; k++)
            {
                SkillIcon(fitted[k], kx + k * 32, row + 8, 1);
                if (canvas.Hover(kx + k * 32, row + 8, 30, 33)) _tip = (voyage.OptionName(fitted[k]) + (fitted[k] == work.Dedicated ? " (전용함 스킬)" : ""), kx + k * 32 + 15, row + 6);
            }
            return chosen;
        }
        if (Line(voyage.Ship.Id, "타고 있는 배 — " + voyage.Ship.Name, voyage.Stats, voyage.Durability, y + 52, true, voyage.Work, voyage.FormName(voyage.Ship, voyage.Work), _swapPicked == null))
            _swapPicked = null;
        float row = y + 110;
        const int shown = 4;
        _swapTop = Math.Clamp(_swapTop, 0, Math.Max(0, voyage.Dock.Count - shown));
        ScrollBar(x + w - 13, y + 110, shown * 54 - 4, shown, voyage.Dock.Count, ref _swapTop);        // 누르거나 끌어도 굴러간다
        if (voyage.Dock.Count > shown)
            canvas.Text($"{_swapTop + 1} ~ {Math.Min(voyage.Dock.Count, _swapTop + shown)} / {voyage.Dock.Count}척 · 휠로 굴린다", x + 20, y + h - 44, 300, 22, 14, Canvas.Dim);
        foreach (var docked in voyage.Dock.Skip(_swapTop).Take(shown).ToList())
        {
            if (Line(docked.Ship.Id, docked.Ship.Name + (docked.Material != 0 ? $" ({voyage.MaterialOf(docked.Material)?.Name})" : ""), voyage.StatsOf(docked), docked.Durability, row, false, docked.Work, voyage.FormName(docked.Ship, docked.Work), _swapPicked == docked))
                _swapPicked = _swapPicked == docked ? null : docked;
            string? blocker = voyage.SwapBlocker(docked);
            if (blocker != null) canvas.Text(blocker, x + w - 550, row + 30, 200, 20, 12, new Color4(1f, 0.5f, 0.45f, 1), 2);
            // 비교 — 타고 있는 배와 나란히 놓고 견준다(창 안에서 ◀ ▶ 로 다른 배로 바꾼다)
            if (canvas.Button("비교", x + w - 340, row + 9, 54, 32, true, 12)) { (_cmpOpen, _cmpSale, _cmpLeft, _cmpRight) = (true, null, 0, voyage.Dock.IndexOf(docked) + 1); break; }
            // 선박정보 — 그 배를 선박 정보 창으로 본다(보는 동안만 그 배로 바꿔 탔다가 닫으면 돌아온다)
            if (canvas.Button("선박정보", x + w - 282, row + 9, 72, 32, voyage.Mode == Mode.Port, 12))
            {
                voyage.BeginWork(docked);
                if (voyage.Working) (voyage.Dialog, _infoDocked, _workView) = (Dialog.ShipInfo, true, false);
                break;
            }
            if (canvas.Button("갈아타기", x + w - 206, row + 9, 72, 32, blocker == null, 12)) voyage.SwapShip(docked);
            if (canvas.Button($"팔기 ({voyage.DockedPrice(docked):N0})", x + w - 130, row + 9, 110, 32, true, 12)) voyage.SellDocked(docked);
            row += 54;
        }
        if (voyage.Dock.Count == 0)
            canvas.Text("부두에 매어 둔 배가 없다.\n조선소에서 배를 사거나 지으면 타던 배가 여기에 남고, 여기서 갈아탄다.", x + 26, row + 6, w - 52, 60, 15, Canvas.Dim);
        // 고른 배의 모습 — 목록 아래 칸에서 끌어 돌리고 휠로 다가선다
        if (previewing && h > 420 + 60)
        {
            float py = y + 110 + shown * 54 + 2, ph = y + h - 58 - py;
            canvas.Fill(x + 16, py, w - 32, ph, new Color4(0.03f, 0.05f, 0.16f, 0.6f));
            canvas.Text(_swapPicked?.Ship.Name ?? voyage.Ship.Name, x + 26, py + 6, 400, 24, 16, Canvas.Gold, 0, true);
            ShipPreview = (x + 20, py + 4, w - 40, ph - 8);
            if (_swapPicked is { } seen) PreviewShip = (seen.Ship.Model, voyage.HullColorOf(seen));
        }
        canvas.Text("줄을 누르면 아래에 그 배의 모습이 뜬다.", x + 340, y + h - 44, 400, 22, 13, Canvas.Dim);
        if (canvas.Button(_swapFrom == Dialog.None ? "닫기" : "이전", x + w - 140, y + h - 50, 120, 34)) (voyage.Dialog, _swapFrom, _swapPicked) = (_swapFrom, Dialog.None, null);
    }
    private DockedShip? _swapPicked;
    /// <summary>선박교환 창을 연 곳 — 닫으면 그리로 돌아간다(조선소 · 선박 정보).</summary>
    private Dialog _swapFrom;
    private bool _workView, _infoDocked;
    private int _combineMain = -1, _combineMaterial = -1, _combineTop;
    private int _levelSeen;
    private long _levelShown = -100_000;
    // 대본용: 조합 창에서 강화 선박과 재료 선박을 고른다
    public void PickCombine(int main, int material) => (_combineMain, _combineMaterial) = (main, material);

    /// <summary>선박부품의 그림 — 아이템 그림 묶음의 무리 6(돛) · 8(장갑) · 9(선수상). 그 번호의 그림이 없으면 무리의 첫 그림.</summary>
    private void PartIcon(ShipPart part, float x, float y, float size)
    {
        var (group, first) = part.Slot switch { 0 => (6, 600100), 1 => (8, 800100), 3 => (11, 1100001), 4 => (7, 700100), 2 => (10, 1000100), _ => (9, 900001) };
        if (!canvas.Image($"sb{group}:{part.Id}", () => (_itemIcons ??= new ImageSet(@"0010\0001\sb")).Pixels(group, part.Id), x, y, size, size))
            canvas.Image($"sb{group}:{first}", () => (_itemIcons ??= new ImageSet(@"0010\0001\sb")).Pixels(group, first), x, y, size, size);
    }

    /// <summary>
    /// 선박부품 탈착(B) — 원본의 짜임: 왼쪽 「소유 선박부품」 격자, 오른쪽에 그 배의 칸들(보조돛 · 장갑 · 선수상).
    /// 왼쪽을 누르면 빈 칸에 달리고, 단 것(초록 E)을 누르면 떼어진다. 오른쪽 끝에 내 배가 돈다(원본은 칸 뒤에 비친다). 대포 칸은 없다(전투가 없다).
    /// 위의 탭은 원본의 셋 — 선박 데코 · 선원 장비는 아직 없어 꺼 두었다.
    /// </summary>
    private int _fitTab;

    /// <summary>
    /// 선박부품 창의 「선박 데코」 · 「선원 장비」 쪽 — 왼쪽에 가진 것(단 것에 초록 E), 오른쪽에 붙이는 자리.
    /// 왼쪽을 누르면 붙고(다시 누르면 떼고), 오른쪽 칸을 눌러도 떼어진다. 그림은 아이템 그림 묶음에서 번호로 찾고 없으면 이름을 적는다.
    /// </summary>
    private void DecorTab(float x, float y, float w, float h, float tile, bool crew)
    {
        canvas.Fill(x + 16, y + 14, 290, 24, new Color4(0.72f, 0.76f, 0.84f, 0.92f));
        canvas.Text(crew ? "소유 선원 장비" : "소유 선박 데코", x + 16, y + 15, 290, 22, 15, new Color4(0.05f, 0.08f, 0.2f, 1), 1, true, false);
        int[] worn = crew ? voyage.CrewOn : voyage.DecoOn;
        bool Mine(int id) => crew ? voyage.CrewGearOf(id) != null : voyage.DecoOf(id) != null;
        void Picture(int id, float px, float py, float size)
        {
            if (!canvas.Image($"sb{id / 100000}:{id}", () => (_itemIcons ??= new ImageSet(@"0010\0001\sb")).Pixels(id / 100000, id), px, py, size, size))
                canvas.Text(voyage.ItemName(id), px, py + 4, size, size - 4, 10, Canvas.White, 1);
        }
        var owned = voyage.Items.Where(i => i.Value > 0 && Mine(i.Key)).Select(i => i.Key).OrderBy(id => id).ToList();
        int pointed = 0;
        for (int k = 0; k < 25; k++)
        {
            float tx = x + 16 + k % 5 * (tile + 6), ty = y + 46 + k / 5 * (tile + 6);
            canvas.Fill(tx, ty, tile, tile, new Color4(0.03f, 0.10f, 0.08f, 0.9f));
            if (k >= owned.Count) continue;
            int id = owned[k];
            Picture(id, tx + 3, ty + 3, tile - 6);
            int on = worn.Count(v => v == id), have = voyage.Items.GetValueOrDefault(id);
            if (on > 0) canvas.Text("E", tx + 3, ty, 16, 20, 15, new Color4(0.3f, 1f, 0.75f, 1), 0, true);
            if (have > 1) canvas.Text($"{have}", tx, ty + tile - 18, tile - 4, 16, 12, Canvas.White, 2, true);
            if (!canvas.Hover(tx, ty, tile, tile)) continue;
            pointed = id;
            canvas.Frame(tx, ty, tile, tile, Canvas.Gold, 2);
            if (!canvas.Pointer.Clicked) continue;
            canvas.Pointer.Consumed = true;
            if (crew) voyage.FitCrew(id);
            else if (on >= have) voyage.UnfitDeco(Array.IndexOf(worn, id));
            else voyage.FitDeco(id);
            break;
        }
        if (owned.Count == 0)
            canvas.Text(crew ? "가진 선원 장비가 없다.\n(소지품 → 아이템 추가 → 「전체」에서 「선원」으로 찾는다)" : "가진 선박 데코가 없다.\n(소지품 → 아이템 추가 → 「전체」에서 「데코」로 찾는다)", x + 24, y + 56, 280, 60, 13, Canvas.Dim);
        canvas.Line(x + 320, y + 14, x + 320, y + h - 60, new Color4(0.5f, 0.55f, 0.75f, 0.8f), 1);
        canvas.Text("⇄", x + 308, y + 150, 24, 30, 20, Canvas.Gold, 1);

        float rx = x + 340;
        canvas.Text(crew ? "선원 장비" : voyage.Ship.Name, rx, y + 14, 280, 24, 16, Canvas.White, 0, true);
        // 자리 — 데코: 마스트 톱 하나, 전방 측면 둘, 뒤쪽 측면 둘 / 선원 장비: 갈래 셋
        (string Label, int[] Slots)[] rows = crew
            ? [(Voyage.CrewKinds[0], [0]), (Voyage.CrewKinds[1], [1]), (Voyage.CrewKinds[2], [2])]
            : [(Voyage.DecoSpots[0], [0]), (Voyage.DecoSpots[1], [1, 2]), (Voyage.DecoSpots[3], [3, 4])];
        for (int r = 0; r < rows.Length; r++)
        {
            float ry = y + 50 + r * (tile + 14);
            canvas.Text(rows[r].Label, rx - 4, ry + 16, 76, 20, 13, Canvas.Dim);
            for (int k = 0; k < rows[r].Slots.Length; k++)
            {
                int slot = rows[r].Slots[k];
                float tx = rx + 80 + k * (tile + 6);
                canvas.Fill(tx, ry, tile, tile, new Color4(0.02f, 0.04f, 0.12f, 0.95f));
                canvas.Frame(tx, ry, tile, tile, new Color4(0.75f, 0.75f, 0.8f, 0.9f), 1);
                if (worn[slot] == 0) continue;
                Picture(worn[slot], tx + 3, ry + 3, tile - 6);
                if (!canvas.Hover(tx, ry, tile, tile)) continue;
                pointed = worn[slot];
                canvas.Frame(tx, ry, tile, tile, Canvas.Gold, 2);
                if (!canvas.Pointer.Clicked) continue;
                canvas.Pointer.Consumed = true;
                if (crew) voyage.UnfitCrew(slot); else voyage.UnfitDeco(slot);
            }
        }
        string note = "왼쪽의 것을 누르면 붙고, 붙인 것(E)이나 오른쪽 칸을 누르면 떼어진다.";
        if (pointed != 0)
        {
            note = $"{voyage.ItemName(pointed)} — {voyage.ItemNote(pointed)}";
            if (voyage.CrewGearOf(pointed) is { } gear) note += $"   (수치 {string.Join(" · ", gear.Values)}, 내구 {gear.Durability})";
            else if (voyage.DecoOf(pointed) is { } deco) note += $"   ({string.Join(" · ", Voyage.DecoSpots.Where((_, k) => k < deco.Spots.Length && deco.Spots[k] != 0).Distinct())})";
        }
        canvas.Text(note, x + 16, y + h - 82, w - 32, 36, 12, pointed != 0 ? Canvas.White : Canvas.Dim);
        canvas.Text("달아도 성능은 바뀌지 않는다(수치의 뜻을 못 밝혔다).", x + 16, y + h - 44, w - 150, 20, 12, Canvas.Dim);
        if (canvas.Button("이전", x + w - 118, y + h - 46, 98, 32)) voyage.Dialog = Dialog.None;
    }

    private void FittingWindow()
    {
        const float w = 900, h = 564, tile = 52;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2 - 10;
        string[] tabs = ["선박부품", "선박 데코", "선원 장비"];
        for (int k = 0; k < tabs.Length; k++)
        {
            bool on = _fitTab == k;
            canvas.Fill(x + 4 + k * 132, y - 28, 128, 28, on ? new Color4(0.25f, 0.72f, 0.85f, 0.95f) : new Color4(0.16f, 0.30f, 0.48f, 0.9f));
            canvas.Text(tabs[k], x + 4 + k * 132, y - 25, 128, 22, 15, on ? new Color4(0.03f, 0.06f, 0.18f, 1) : Canvas.White, 1, on, false);
            if (canvas.Hover(x + 4 + k * 132, y - 28, 128, 28) && canvas.Pointer.Clicked) (_fitTab, canvas.Pressed) = (k, true);
        }
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y - 28, w, h + 28);
        ShipPreview = (x + 680, y + 14, w - 696, h - 110);
        if (_fitTab != 0) { DecorTab(x, y, w, h, tile, _fitTab == 2); return; }
        canvas.Fill(x + 16, y + 14, 290, 24, new Color4(0.72f, 0.76f, 0.84f, 0.92f));
        canvas.Text("소유 선박부품", x + 16, y + 15, 290, 22, 15, new Color4(0.05f, 0.08f, 0.2f, 1), 1, true, false);
        ShipPart? pointed = null;
        // 원본처럼 단 것도 이 칸에 함께 보인다 — 초록 E 가 붙는다
        var owned = voyage.Parts.Select(p => (Part: p, On: true)).Concat(voyage.PartStock.Select(p => (Part: p, On: false))).ToList();
        for (int k = 0; k < 25; k++)
        {
            float tx = x + 16 + k % 5 * (tile + 6), ty = y + 46 + k / 5 * (tile + 6);
            canvas.Fill(tx, ty, tile, tile, new Color4(0.03f, 0.10f, 0.08f, 0.9f));
            if (k >= owned.Count) continue;
            var (part, on) = owned[k];
            PartIcon(part, tx + 3, ty + 3, tile - 6);
            if (on) canvas.Text("E", tx + 3, ty, 16, 20, 15, new Color4(0.3f, 1f, 0.75f, 1), 0, true);
            if (!canvas.Hover(tx, ty, tile, tile)) continue;
            pointed = part;
            canvas.Frame(tx, ty, tile, tile, on || voyage.FitBlocker(part) == null ? Canvas.Gold : new Color4(1f, 0.5f, 0.45f, 1), 2);
            if (!canvas.Pointer.Clicked) continue;
            canvas.Pointer.Consumed = true;
            if (on) voyage.Unfit(part); else voyage.Fit(part);
            break;
        }
        canvas.Line(x + 320, y + 14, x + 320, y + h - 60, new Color4(0.5f, 0.55f, 0.75f, 0.8f), 1);
        canvas.Text("⇄", x + 308, y + 150, 24, 30, 20, Canvas.Gold, 1);
        float rx = x + 340;
        canvas.Text(voyage.Ship.Name, rx, y + 14, 280, 24, 16, Canvas.White, 0, true);
        int[] icons = [306, 333, 245, 301, 305, 364];
        for (int slot = 0; slot < 6; slot++)
        {
            float ry = y + 50 + slot * (tile + 14);
            int icon = icons[slot];
            canvas.Image($"gm0:{icon}", () => (_markParts ??= new UiParts(0)).Pixels(icon), rx, ry + 14, 22, 22);
            canvas.Text(Voyage.SlotName[slot], rx - 4, ry + tile - 14, 60, 16, 10, Canvas.Dim);
            var fitted = voyage.Parts.Where(p => p.Slot == slot).ToList();
            // 칸이 다섯을 넘으면(대포) 작게 그려 배 그림을 가리지 않게 한다
            float box = voyage.SlotsOf(slot) > 4 ? 40 : tile;
            for (int k = 0; k < voyage.SlotsOf(slot); k++)
            {
                float tx = rx + 60 + k * (box + 6);
                canvas.Fill(tx, ry, box, box, new Color4(0.02f, 0.04f, 0.12f, 0.95f));
                canvas.Frame(tx, ry, box, box, new Color4(0.75f, 0.75f, 0.8f, 0.9f), 1);
                if (k >= fitted.Count) continue;
                PartIcon(fitted[k], tx + 3, ry + 3, box - 6);
                if (!canvas.Hover(tx, ry, box, box)) continue;
                pointed = fitted[k];
                canvas.Frame(tx, ry, box, box, Canvas.Gold, 2);
                if (canvas.Pointer.Clicked) { canvas.Pointer.Consumed = true; voyage.Unfit(fitted[k]); }
            }
        }
        canvas.Text(pointed != null ? $"[{Voyage.SlotName[pointed.Slot]}] {pointed.Name} — {voyage.PartNote(pointed)}" + (voyage.PartStock.Contains(pointed) && voyage.FitBlocker(pointed) is { } why ? $"   ({why})" : "")
                                    : "왼쪽의 부품을 누르면 빈 칸에 달리고, 단 부품(E)을 누르면 떼어진다.", x + 16, y + h - 82, w - 32, 20, 13, pointed != null ? Canvas.White : Canvas.Dim);
        canvas.Text($"속도 ×{voyage.PartSpeed:0.00} · 내구 피해 ×{voyage.PartDamage:0.00} · 재해 ×{voyage.PartLuck:0.00}     소지금 {voyage.Money:N0} Ð", x + 16, y + h - 44, w - 150, 20, 13, Canvas.Dim);
        if (canvas.Button("이전", x + w - 118, y + h - 46, 98, 32)) voyage.Dialog = Dialog.None;
    }

    /// <summary>
    /// 선박 조합 — 원본의 차례: 조합할 선박(강화 선박) → 재료가 될 선박 → 성공률을 보고 결정. 재료 선박은 사라진다.
    /// 탑승 중인 선박은 고를 수 없어서 부두의 배만 나온다.
    /// </summary>
    private int _combineBonus, _combineInherit, _bonusTop;
    private bool _redesignAsk;
    private bool _overBonus;

    /// <summary>
    /// 그레이드 보너스의 원본 그림 — 스킬 그림 묶음(<c>0010\0001\sa</c>)의 무리 2, id 가 자료 표 90 의 번호(1 ~ 31, 24 × 28).
    /// 16 스킬 계승 · 17 ~ 21 개조 다섯 · 29 ~ 31 가속 강화(↑UP Ⅰ · Ⅱ · Ⅲ). 같은 묶음의 무리 3 은 변성연금의 비술 셋이다.
    /// </summary>
    private void BonusPicture(int bonus, float x, float y, float width) =>
        canvas.Image($"sa2:{bonus}", () => (_skillIcons ??= new ImageSet(@"0010\0001\sa")).Pixels(2, bonus), x, y, width, width * 28 / 24);

    private void CombineWindow()
    {
        const float w = 1140, h = 470, listWidth = 270;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text("선박 조합", x + 20, y + 10, 200, 28, 20, Canvas.Gold, 0, true);
        bool aboard = voyage.Data.Settings.ModCombineOnBoard;      // 모드: 타고 있는 배가 맨 위에 — 강화 선박으로만
        canvas.Text(aboard ? "조합할 선박과 재료가 될 선박을 고른다. 타고 있는 배는 강화 선박으로만 고를 수 있고(모드), 재료로 고른 선박은 사라진다."
                           : "조합할 선박과 재료가 될 선박을 고른다. 탑승 중인 선박은 고를 수 없고, 재료로 고른 선박은 사라진다.", x + 150, y + 15, w - 170, 22, 13, Canvas.White);
        void Header(string text, float hx, float hw)
        {
            canvas.Fill(hx, y + 46, hw, 24, new Color4(0.72f, 0.76f, 0.84f, 0.92f));
            canvas.Text(text, hx, y + 47, hw, 22, 15, new Color4(0.05f, 0.08f, 0.2f, 1), 1, true, false);
        }
        List<DockedShip> dock = aboard ? [voyage.Aboard, .. voyage.Dock] : [.. voyage.Dock];
        const int shown = 7;
        if (aboard && _combineMaterial == 0) _combineMaterial = -1;
        _combineTop = Math.Clamp(_combineTop, 0, Math.Max(0, dock.Count - shown));
        if (_combineMain >= dock.Count) _combineMain = -1;
        if (_combineMaterial >= dock.Count) _combineMaterial = -1;
        void List(string title, float lx, bool material)
        {
            Header(title, lx, listWidth);
            float row = y + 78;
            for (int i = _combineTop; i < Math.Min(dock.Count, _combineTop + shown); i++, row += 46)
            {
                bool mine = aboard && i == 0;
                bool chosen = (material ? _combineMaterial : _combineMain) == i, other = (material ? _combineMain : _combineMaterial) == i || material && mine;
                bool hover = !other && canvas.Hover(lx, row, listWidth, 44);
                if (chosen) canvas.Fill(lx, row, listWidth, 44, new Color4(0.12f, 0.62f, 0.55f, 0.95f));
                else if (hover) canvas.Fill(lx, row, listWidth, 44, new Color4(0.2f, 0.3f, 0.6f, 0.6f));
                // 이름 뒤에 크기 — 크기가 같은 배만 재료가 된다. 재료 쪽에서는 강화 선박과 크기가 다른 배를 흐리게
                string[] sizeNames = ["소형", "소형", "중형", "대형", "대형"];
                string size = sizeNames[Math.Clamp(dock[i].Ship.SizeClass, 0, 4)];
                bool fits = !material || _combineMain < 0 || _combineMain >= dock.Count || Voyage.SizeGroup(dock[_combineMain].Ship) == Voyage.SizeGroup(dock[i].Ship);
                var sizeColor = size switch { "대형" => new Color4(1f, 0.72f, 0.45f, 1), "중형" => new Color4(0.65f, 1f, 0.7f, 1), _ => new Color4(0.6f, 0.85f, 1f, 1) };
                canvas.Text(dock[i].Ship.Name + (mine ? " (탑승 중)" : ""), lx + 8, row + 2, listWidth - 70, 22, 15, other || !fits ? Canvas.Dim : Canvas.White);
                canvas.Text(size + " · " + voyage.FormName(dock[i].Ship, dock[i].Work), lx + 140, row + 24, 110, 18, 12, fits ? sizeColor : Canvas.Dim);
                canvas.Text($"그레이드 {dock[i].Work.Grade}   경험치 {dock[i].Work.GradeExp}", lx + 8, row + 23, 130, 20, 12, Canvas.Dim);
                // 붙은 선박 스킬의 그림(올리면 이름) — 옵션 스킬과 전용함 스킬
                var skills = dock[i].Work.Dedicated > 0 ? dock[i].Work.Skills.Append(dock[i].Work.Dedicated).ToList() : dock[i].Work.Skills;
                for (int k = 0; k < skills.Count && k < 5; k++)
                {
                    float kx = lx + listWidth - 22 - (Math.Min(skills.Count, 5) - 1 - k) * 21;
                    SkillIcon(skills[k], kx, row + 1, other ? 0.45f : 1, 0.66f);
                    if (canvas.Hover(kx, row + 1, 20, 22)) _tip = (voyage.OptionName(skills[k]) + (skills[k] == dock[i].Work.Dedicated ? " (전용함 스킬)" : ""), kx + 10, row - 1);
                }
                if (hover && canvas.Pointer.Clicked) { if (material) _combineMaterial = i; else _combineMain = i; }
            }
            if (dock.Count == 0) canvas.Text("부두에 배가 없다.", lx + 8, row + 4, listWidth, 22, 14, Canvas.Dim);
        }
        List("강화 선박", x + 20, false);
        List("재료 선박", x + 20 + listWidth + 12, true);
        // 선택 보너스 — 그레이드가 1 · 3 · 6 이 되는 조합에서 하나를 고른다. 스킬 계승이면 옮길 스킬도 고른다
        float bx = x + 20 + (listWidth + 12) * 2, bw = 290;
        Header("선택 보너스", bx, bw);
        _overBonus = canvas.Hover(bx, y + 46, bw, h - 110);
        int picked = 0, inherit = 0;
        if (_combineMain >= 0 && _combineMaterial >= 0 && _combineMain < dock.Count && _combineMaterial < dock.Count)
        {
            var (bonusMain, bonusMaterial) = (dock[_combineMain], dock[_combineMaterial]);
            if (!Voyage.GivesBonus(bonusMain)) canvas.Text($"그레이드 {bonusMain.Work.Grade + 1} 에서는 보너스가 없다.\n(1 · 3 · 6 에서 하나씩 고른다)", bx + 6, y + 82, bw - 12, 44, 13, Canvas.Dim);
            else
            {
                var choices = voyage.BonusChoices(bonusMain, bonusMaterial);
                if (!choices.Contains(_combineBonus)) _combineBonus = choices.Count > 0 ? choices[0] : 0;
                picked = _combineBonus;
                var skills = voyage.InheritChoices(bonusMain, bonusMaterial);
                const int bonusShown = 9;
                int listed = picked == 16 ? Math.Max(3, bonusShown - skills.Count - 1) : bonusShown;
                ScrollBar(bx + bw - 10, y + 78, listed * 28 - 2, listed, choices.Count, ref _bonusTop);
                for (int i = _bonusTop; i < Math.Min(choices.Count, _bonusTop + listed); i++)
                {
                    float by = y + 78 + (i - _bonusTop) * 28;
                    int id = choices[i];
                    bool on = id == picked, over = canvas.Hover(bx, by, bw - 14, 27);
                    if (on) canvas.Fill(bx, by, bw - 14, 27, new Color4(0.12f, 0.62f, 0.55f, 0.95f));
                    else if (over) canvas.Fill(bx, by, bw - 14, 27, new Color4(0.2f, 0.3f, 0.6f, 0.6f));
                    BonusPicture(id, bx + 4, by + 1, 21);
                    canvas.Text(Voyage.BonusName(id), bx + 30, by + 3, bw - 50, 20, 14, Canvas.White);
                    if (over) _tip = (Array.Find(Voyage.GradeBonuses, b => b.Id == id).Note, bx + bw / 2, by - 2);
                    if (over && canvas.Pointer.Clicked) _combineBonus = id;
                }
                if (picked == 16)
                {
                    // 계승할 옵션 스킬 — 재료 선박의 스킬 가운데 본배에 없는 것
                    if (!skills.Contains(_combineInherit)) _combineInherit = skills.Count > 0 ? skills[0] : 0;
                    inherit = _combineInherit;
                    float sy = y + 78 + listed * 28 + 6;
                    canvas.Text("계승할 옵션 스킬", bx + 4, sy, bw, 20, 13, Canvas.Gold);
                    for (int k = 0; k < skills.Count && k < 5; k++)
                    {
                        float ky = sy + 22 + k * 34;
                        bool on = skills[k] == inherit, over = canvas.Hover(bx, ky, bw - 14, 33);
                        if (on) canvas.Fill(bx, ky, bw - 14, 33, new Color4(0.12f, 0.62f, 0.55f, 0.95f));
                        else if (over) canvas.Fill(bx, ky, bw - 14, 33, new Color4(0.2f, 0.3f, 0.6f, 0.6f));
                        SkillIcon(skills[k], bx + 2, ky, 1);
                        canvas.Text(voyage.OptionName(skills[k]), bx + 40, ky + 6, bw - 60, 20, 14, Canvas.White);
                        if (over && canvas.Pointer.Clicked) _combineInherit = skills[k];
                    }
                }
            }
        }
        else canvas.Text("그레이드 1 · 3 · 6 이 되는 조합에서\n보너스를 하나 고른다.", bx + 6, y + 82, bw - 12, 44, 13, Canvas.Dim);
        float rx = bx + bw + 12, rw = x + w - 20 - rx;
        Header("성공률", rx, rw);
        if (_combineMain >= 0 && _combineMaterial >= 0)
        {
            var (main, material) = (dock[_combineMain], dock[_combineMaterial]);
            string? blocker = voyage.CombineBlocker(main, material);
            canvas.Text($"{voyage.CombineChance(main, material)} ％", rx, y + 84, rw, 40, 30, Canvas.White, 1, true);
            if (voyage.RefitBook is { } aid) canvas.Text($"{aid.Name} +{aid.Bonus}%" + (voyage.RefitGuard ? " · 특별대우" : ""), rx, y + 118, rw, 18, 11, new Color4(0.5f, 1f, 0.6f, 1), 1);
            else if (voyage.RefitGuard) canvas.Text("개장 특별대우", rx, y + 118, rw, 18, 11, new Color4(0.5f, 1f, 0.6f, 1), 1);
            canvas.Text($"그레이드 {main.Work.Grade} → {main.Work.Grade + 1}", rx, y + 132, rw, 22, 15, Canvas.Gold, 1);
            // 선박 형식 — 제물의 형식이 다르면 스며들고, 횟수가 차면 바뀐다
            int formNow = voyage.FormOf(main.Ship, main.Work), formNext = Voyage.FormAfter(formNow, voyage.FormOf(material.Ship, material.Work));
            canvas.Text(formNow == formNext ? $"선박 형식 {Voyage.FormNames[formNow]} (그대로)" : $"선박 형식 {Voyage.FormNames[formNow]} → {Voyage.FormNames[formNext]}",
                        rx, y + 150, rw, 18, 12, formNow == formNext ? Canvas.Dim : new Color4(0.5f, 1f, 0.6f, 1), 1);
            canvas.Text($"비용 {voyage.CombineCost(main):N0} Ð\n소지금 {voyage.Money:N0} Ð", rx + 4, y + 164, rw - 8, 44, 14, Canvas.White);
            // 성공하면 달라지는 것 — 지금과 성공 뒤의 능력치를 견줘 바뀌는 줄만(강화가 초기화돼 내려가는 것은 붉게)
            var after = voyage.Combined(main, material, picked, inherit);
            var (was, will) = (voyage.StatsWith(main, main.Work), voyage.StatsWith(main, after));
            int capWas = Voyage.MasteryCap(main.Ship, main.Work), capWill = Voyage.MasteryCap(main.Ship, after);
            (string Label, double Now, double Then, string Unit)[] gains =
            [
                ("내구력", was.Durability, will.Durability, ""), ("세로돛", was.VerticalSail, will.VerticalSail, ""), ("가로돛", was.HorizontalSail, will.HorizontalSail, ""),
                ("조력", was.Rowing, will.Rowing, ""), ("선회", was.Turn, will.Turn, ""), ("내파", was.WaveResist, will.WaveResist, ""), ("장갑", was.Armor, will.Armor, ""),
                ("선실", was.MaxCrew, will.MaxCrew, ""), ("포실", was.Guns, will.Guns, ""), ("창고", was.Hold, will.Hold, ""),
                ("속도", Math.Round(was.Knots, 1), Math.Round(will.Knots, 1), "노트"), ("숙련도 상한", capWas, capWill, ""),
            ];
            canvas.Text("성공하면", rx + 4, y + 212, rw - 8, 18, 13, Canvas.Gold, 0, true);
            float gy = y + 230;
            foreach (var (label, now, then, unit) in gains)
            {
                if (now == then || gy > y + 342) continue;
                bool up = then > now;
                canvas.Text(label, rx + 4, gy, 70, 16, 12, Canvas.White);
                canvas.Text($"{now:0.#} → {then:0.#}{unit}  ({(up ? "+" : "")}{then - now:0.#})", rx + 74, gy, rw - 78, 16, 12, up ? new Color4(0.5f, 1f, 0.6f, 1) : new Color4(1f, 0.55f, 0.5f, 1));
                gy += 16;
            }
            string extra = (picked > 0 ? $"보너스: {Voyage.BonusName(picked)}" + (inherit > 0 ? $" — {voyage.OptionName(inherit)}" : "") + "\n" : "")
                           + (main.Work.Times > 0 ? "강화는 초기화된다." : "") + (main.Work.Grade >= 3 ? " 4 부터는 대실패(강등)가 있다." : "");
            canvas.Text(extra, rx + 4, gy + 2, rw - 8, 48, 12, Canvas.Dim);
            if (blocker != null) canvas.Text(blocker, rx + 4, y + 372, rw - 8, 40, 12, new Color4(1f, 0.5f, 0.45f, 1));
            if (canvas.Button("확인", x + w - 232, y + h - 50, 100, 34, blocker == null))
            {
                voyage.Combine(main, material, picked, inherit);
                (_combineMain, _combineMaterial) = (aboard && main == voyage.Aboard ? 0 : voyage.Dock.IndexOf(main) + (aboard ? 1 : 0), -1);
            }
        }
        else canvas.Text("두 배를 고르면 성공률이 나온다.", rx + 4, y + 84, rw - 8, 44, 13, Canvas.Dim);
        // 그레이드 초기화 — 강화 선박으로 고른 배의 개조를 모두 되돌린다(함선 재설계 기술서 한 권)
        if (_combineMain >= 0 && _combineMain < dock.Count)
        {
            var target = dock[_combineMain];
            string? why = voyage.RedesignBlocker(target);
            if (canvas.Button("그레이드 초기화", x + 20, y + h - 50, 150, 34, why == null, 14)) _redesignAsk = true;
            canvas.Text(why ?? $"함선 재설계 기술서 1권 (가진 수 {voyage.Items.GetValueOrDefault(Voyage.RedesignBook)})", x + 178, y + h - 43, 330, 20, 12, Canvas.Dim);
            if (_redesignAsk && why == null)
            {
                // 초기화 전 · 후를 나란히 — 달라지는 줄은 빛깔로
                const float pw = 640, ph = 392;
                float px = x + (w - pw) / 2, py = y + (h - ph) / 2;
                canvas.Fill(px, py, pw, ph, new Color4(0.02f, 0.04f, 0.14f, 0.985f));
                canvas.Frame(px, py, pw, ph, Canvas.PanelEdge, 1.5f);
                canvas.Block(px, py, pw, ph);
                canvas.Text($"그레이드 초기화 — {target.Ship.Name}", px + 16, py + 10, pw - 32, 26, 18, Canvas.Gold, 0, true);
                var (was, will) = (target.Work, voyage.Redesigned(target));
                var (before, after) = (voyage.StatsWith(target, was), voyage.StatsWith(target, will));
                canvas.Fill(px + 16, py + 44, pw - 32, 22, new Color4(0.72f, 0.76f, 0.84f, 0.92f));
                canvas.Text("지금", px + 150, py + 45, 110, 20, 14, new Color4(0.05f, 0.08f, 0.2f, 1), 2, true, false);
                canvas.Text("초기화 뒤", px + 270, py + 45, 110, 20, 14, new Color4(0.05f, 0.08f, 0.2f, 1), 2, true, false);
                (string Label, string Now, string Then, int Dir)[] rows =
                [
                    ("선박 형식", voyage.FormName(target.Ship, was), voyage.FormName(target.Ship, will), voyage.FormOf(target.Ship, was) == voyage.FormOf(target.Ship, will) ? 0 : -1),
                    ("그레이드", $"{was.Grade}", $"{will.Grade}", will.Grade - was.Grade), ("그레이드 경험치", $"{was.GradeExp}", $"{will.GradeExp}", will.GradeExp - was.GradeExp),
                    ("강화 횟수", $"{was.Times} / {voyage.MaxTimesOf(target.Ship)}", $"0 / {voyage.MaxTimesOf(target.Ship)}", -was.Times),
                    ("내구력", $"{before.Durability}", $"{after.Durability}", after.Durability - before.Durability),
                    ("세로돛성능", $"{before.VerticalSail}", $"{after.VerticalSail}", after.VerticalSail - before.VerticalSail),
                    ("가로돛성능", $"{before.HorizontalSail}", $"{after.HorizontalSail}", after.HorizontalSail - before.HorizontalSail),
                    ("선회 성능", $"{before.Turn}", $"{after.Turn}", after.Turn - before.Turn), ("내파성", $"{before.WaveResist}", $"{after.WaveResist}", after.WaveResist - before.WaveResist),
                    ("장갑치", $"{before.Armor}", $"{after.Armor}", after.Armor - before.Armor), ("선실적재량", $"{before.MaxCrew}", $"{after.MaxCrew}", after.MaxCrew - before.MaxCrew),
                    ("포실적재량", $"{before.Guns}", $"{after.Guns}", after.Guns - before.Guns), ("창고 용량", $"{before.Hold}", $"{after.Hold}", after.Hold - before.Hold),
                    ("속도", $"{before.Knots:0.0}노트", $"{after.Knots:0.0}노트", Math.Sign(Math.Round(after.Knots - before.Knots, 1))),
                ];
                for (int k = 0; k < rows.Length; k++)
                {
                    float ry = py + 70 + k * 19;
                    var color = rows[k].Dir < 0 ? new Color4(1f, 0.6f, 0.5f, 1) : rows[k].Dir > 0 ? new Color4(0.5f, 1f, 0.6f, 1) : Canvas.White;
                    canvas.Text(rows[k].Label, px + 20, ry, 130, 20, 13, Canvas.Dim);
                    canvas.Text(rows[k].Now, px + 150, ry, 110, 20, 14, Canvas.White, 2);
                    canvas.Text(rows[k].Then, px + 270, ry, 110, 20, 14, color, 2);
                }
                // 오른쪽: 사라지는 그레이드 보너스, 스킬의 전 · 후
                float sx = px + 404, sw = pw - 420;
                canvas.Text("사라지는 그레이드 보너스", sx, py + 70, sw, 20, 13, Canvas.Gold);
                canvas.Text(was.Bonuses.Count == 0 ? "없음" : string.Join("\n", was.Bonuses.Select(Voyage.BonusName)), sx + 4, py + 92, sw, 70, 13, was.Bonuses.Count == 0 ? Canvas.Dim : new Color4(1f, 0.6f, 0.5f, 1));
                void Skills(string title, ShipWork work, float ty)
                {
                    canvas.Text(title, sx, ty, sw, 20, 13, Canvas.Gold);
                    var list = work.Dedicated > 0 ? work.Skills.Append(work.Dedicated).ToList() : work.Skills;
                    if (list.Count == 0) canvas.Text("없음", sx + 4, ty + 24, sw, 20, 13, Canvas.Dim);
                    for (int k = 0; k < list.Count && k < 7; k++)
                    {
                        SkillIcon(list[k], sx + k * 31, ty + 22, 1);
                        if (canvas.Hover(sx + k * 31, ty + 22, 29, 33)) _tip = (voyage.OptionName(list[k]), sx + k * 31 + 14, ty + 20);
                    }
                }
                Skills("선박 스킬 — 지금", was, py + 170);
                Skills("선박 스킬 — 초기화 뒤", will, py + 234);
                canvas.Text("함선 재설계 기술서 1권이 든다. 초기화한 뒤에는 되돌릴 수 없다.", px + 20, py + ph - 40, pw - 240, 20, 12, Canvas.Dim);
                if (canvas.Button("초기화한다", px + pw - 216, py + ph - 46, 110, 32)) { voyage.Redesign(target); _redesignAsk = false; }
                else if (canvas.Button("그만둔다", px + pw - 100, py + ph - 46, 84, 32)) _redesignAsk = false;
            }
        }
        else _redesignAsk = false;
        if (canvas.Button("이전", x + w - 124, y + h - 50, 100, 34)) voyage.Dialog = Dialog.ShipyardMenu;
    }

    /// <summary>배 모형을 그릴 네모(화면 글 좌표) — 선박 정보 창이 떠 있는 프레임에만 값이 있다.</summary>
    public (float X, float Y, float W, float H)? ShipPreview;
    /// <summary>그 네모에 타고 있는 배 말고 다른 배를 보일 때 — 모형 번호와 선체 빛깔(0xRRGGBB).</summary>
    public (int Model, int Color)? PreviewShip;

    /// <summary>선박 정보(V) — 원본의 짜임: 이름 · 등급, 기본성능, 내구력, 강화, 선창 정보, 선원 상황. 배는 왼쪽에 보이게 창을 오른쪽에 둔다.</summary>
    private void ShipInfoWindow()
    {
        const float w = 470, h = 430, model = 430;
        float x = (canvas.Width - w - model) / 2 + model, y = (canvas.Height - h) / 2 - 20;      // 모형 칸까지 합쳐 화면 가운데
        // 왼쪽 칸에 배 모형이 돈다(창이 그 자리를 알려 주면 화면 글 위에 3D 로 그린다)
        canvas.Panel(x - model, y, w + model, h);
        canvas.Block(x - model, y, w + model, h);
        canvas.Line(x - 4, y + 14, x - 4, y + h - 14, new Color4(0.5f, 0.55f, 0.75f, 0.8f), 1);
        ShipPreview = (x - model + 8, y + 8, model - 20, h - 56);
        var s = voyage.Stats;
        var ship = voyage.Ship;
        // 왼쪽 아래: 조타 숙련도(이 배에 대한 숙련도 — 차야 강화가 다 듣는다)
        int cap = Voyage.MasteryCap(voyage.Ship, voyage.Work);
        // 조타 숙련도의 그림은 키(타륜)다 — 원본의 강화성능 화면 오른쪽 아래에 있는 그것
        canvas.Image("gm0:364", () => (_markParts ??= new UiParts(0)).Pixels(364), x - model + 16, y + h - 44, 20, 20);
        canvas.Text($"{voyage.Work.Mastery:0} / {cap}", x - model + 40, y + h - 45, 140, 22, 15, Canvas.White);
        canvas.Fill(x - model + 16, y + h - 20, 150, 3, new Color4(0.03f, 0.05f, 0.16f, 0.9f));
        canvas.Fill(x - model + 16, y + h - 20, 150 * (float)Math.Clamp(voyage.Work.Mastery / cap, 0, 1), 3, new Color4(0.95f, 0.7f, 0.3f, 1));
        if (canvas.Hover(x - model + 12, y + h - 48, 170, 34)) _tip = ($"조타 숙련도 — 지금 강화 한계는 상한의 {Voyage.MasteryShare(voyage.Ship, voyage.Work) * 100:0}%\n다 채우고 강화해야 상한까지 오른다", x - model + 150, y + h - 50);
        void Header(string text, float hx, float hy, float hw = 150)
        {
            canvas.Fill(hx, hy, hw, 24, new Color4(0.72f, 0.76f, 0.84f, 0.92f));
            canvas.Text(text, hx, hy + 1, hw, 22, 15, new Color4(0.05f, 0.08f, 0.2f, 1), 1, true, false);
        }
        void Cell(int icon, string value, float cx, float cy, float cw)
        {
            canvas.Fill(cx, cy, cw, 26, new Color4(0.04f, 0.07f, 0.2f, 0.85f));
            canvas.Image($"gm0:{icon}", () => (_markParts ??= new UiParts(0)).Pixels(icon), cx + 3, cy + 3, 20, 20);
            canvas.Text(value, cx, cy + 2, cw - 6, 22, 15, Canvas.White, 2);
        }
        void Bar(float bx, float by, float bw, double part, Color4 color)
        {
            canvas.Fill(bx, by, bw, 4, new Color4(0.03f, 0.05f, 0.16f, 0.9f));
            canvas.Fill(bx, by, bw * (float)Math.Clamp(part, 0, 1), 4, color);
        }
        string[] sizes = ["소형", "소형", "중형", "대형", "대형"];
        // 강화 횟수 — 한 번에 별 하나(원본 선박 카드의 ★)
        int timesDone = voyage.Work.Times, timesMost = voyage.MaxTimesOf(voyage.Ship);
        canvas.Text("강화 횟수", x + 20, y + 12, 80, 22, 13, Canvas.Gold);
        canvas.Text(new string('★', Math.Min(timesDone, 10)) + new string('☆', Math.Clamp(timesMost - timesDone, 0, 10)) + $"   {timesDone} / {timesMost}", x + 92, y + 10, w - 120, 24, 15, Canvas.White);
        canvas.Text($"{ship.Name}({sizes[Math.Clamp(ship.SizeClass, 0, 4)]})" + (voyage.MaterialOf(voyage.ShipMaterialId) is { } wood ? $"  {wood.Name}" : ""), x + 20, y + 36, w - 40, 26, 18, Canvas.White, 0, true);
        canvas.Fill(x + 20, y + 66, 240, 24, new Color4(0.04f, 0.07f, 0.2f, 0.85f));
        canvas.Text("Grade", x + 26, y + 68, 80, 20, 13, Canvas.Gold);
        canvas.Text($"{voyage.Work.Grade}({voyage.FormName(ship, voyage.Work)})", x + 20, y + 67, 234, 22, 15, Canvas.White, 2);
        // 그레이드 경험치 — 찰수록 선박 조합이 잘 붙는다
        canvas.Fill(x + 20, y + 90, 240, 3, new Color4(0.03f, 0.05f, 0.16f, 0.9f));
        canvas.Fill(x + 20, y + 90, 240 * voyage.Work.GradeExp / 100f, 3, new Color4(0.6f, 0.6f, 0.75f, 1));

        Header("기본성능", x + 20, y + 100);
        Header("내구력", x + 300, y + 100, 150);
        // 값 옆에 강화 · 그레이드 · 재질로 붙은 몫을 작게(+n) — 본디 배(재질 없이 · 강화 없이)와 견준 것
        var bare = Dho.Data.ShipStats.Of(ship, voyage.Data.Settings.Ships);
        void Stat(int icon, int now, int was, float cx, float cy)
        {
            Cell(icon, $"{now}", cx, cy, 86);
            if (now != was) canvas.Text($"{now - was:+0;-0}", cx + 24, cy + 6, 34, 16, 10, now > was ? new Color4(0.55f, 1f, 0.6f, 1) : new Color4(1f, 0.6f, 0.5f, 1));
        }
        Stat(306, s.VerticalSail, bare.VerticalSail, x + 20, y + 130);
        Stat(307, s.HorizontalSail, bare.HorizontalSail, x + 110, y + 130);
        Stat(314, s.Rowing, bare.Rowing, x + 200, y + 130);
        Stat(308, s.Turn, bare.Turn, x + 20, y + 160);
        Stat(309, s.WaveResist, bare.WaveResist, x + 110, y + 160);
        Stat(333, s.Armor, bare.Armor, x + 200, y + 160);
        Cell(310, $"{voyage.Durability:0} / {s.Durability}", x + 300, y + 130, 150);
        if (s.Durability != bare.Durability) canvas.Text($"{s.Durability - bare.Durability:+0;-0}", x + 326, y + 136, 40, 16, 10, s.Durability > bare.Durability ? new Color4(0.55f, 1f, 0.6f, 1) : new Color4(1f, 0.6f, 0.5f, 1));
        Bar(x + 300, y + 158, 150, voyage.Durability / Math.Max(1, s.Durability), new Color4(0.90f, 0.35f, 0.40f, 1));
        canvas.Text($"부품 {voyage.Parts.Count}   {s.Knots:0.0}노트", x + 22, y + 192, 220, 20, 13, Canvas.Dim);
        // 붙은 옵션 스킬의 그림(올리면 이름)
        var fitted = voyage.Work.Dedicated > 0 ? voyage.Work.Skills.Append(voyage.Work.Dedicated).ToList() : voyage.Work.Skills;
        for (int k = 0; k < fitted.Count && k < 6; k++)
        {
            int skillId = fitted[k];
            float sx = x + 250 + k * 32;
            SkillIcon(skillId, sx, y + 184, 1);
            if (canvas.Hover(sx, y + 184, 28, 33)) _tip = (voyage.OptionName(skillId) + (skillId == voyage.Work.Dedicated ? " (전용함 스킬)" : "") + (voyage.Data.OptionSkills.Find(o => o.SkillId == skillId) is { } fitted1 && !voyage.OptionValid(fitted1) ? " — 유효조건 미달" : ""), sx + 14, y + 182);
        }

        Header("선창 정보", x + 20, y + 220);
        Cell(330, $"{voyage.Crew:0} / {s.MaxCrew}", x + 20, y + 250, 140);
        Bar(x + 20, y + 278, 140, voyage.Crew / Math.Max(1, s.MaxCrew), new Color4(0.35f, 0.85f, 0.4f, 1));
        Cell(305, $"{voyage.GunsFitted} / {s.Guns}", x + 165, y + 250, 140);
        Cell(323, $"{voyage.HoldUsed} / {s.Hold}", x + 310, y + 250, 140);
        Bar(x + 310, y + 278, 140, voyage.HoldUsed / (double)Math.Max(1, s.Hold), new Color4(0.85f, 0.7f, 0.3f, 1));

        Header("상세 선원 상황", x + 20, y + 296, 170);
        canvas.Text($"필요 선원 {s.MinCrew}     피로 {voyage.Fatigue:0}     물 {voyage.Water:0} · 식량 {voyage.Food:0}", x + 22, y + 326, w - 44, 22, 14, Canvas.White);
        // 이 배에 붙일 수 있는 옵션 스킬의 그림 — 원본 선박 카드처럼 한 줄로(올리면 이름). 목록이 있는 배만
        if (voyage.Data.ShipDetail(voyage.Ship.Name) is { Skills.Count: > 0 } attachable)
        {
            float ix = x - model + 150;
            foreach (var skill in attachable.Skills.Take(13))
            {
                if (voyage.Data.OptionSkills.Find(o => o.Name == skill.Name) is not { } option) continue;
                SkillIcon(option.SkillId, ix, y + h - 48, voyage.Work.Skills.Contains(option.SkillId) ? 1 : 0.55f, 0.8f);
                if (canvas.Hover(ix, y + h - 48, 24, 27)) _tip = (skill.Name + (voyage.Work.Skills.Contains(option.SkillId) ? " (붙어 있다)" : ""), ix + 12, y + h - 50);
                ix += 27;
            }
        }        // 부품 칸의 수 — 원본 선박 카드의 차례(보조돛 · 특수장비 · 추가장갑 / 선측포 · 선수포 · 선미포). 자료가 있는 배만
        if (Dho.Data.ShipStats.Facts.TryGetValue(voyage.Ship.Name, out var slotFact) && slotFact.Slots is { Length: >= 6 } slots)
        {
            string N(int n) => n > 0 ? $"{n}" : "-";
            canvas.Text($"보조돛 {N(slots[0])} · 특수장비 {N(slots[1])} · 추가장갑 {N(slots[2])}     선측포 {N(slots[3])} · 선수포 {N(slots[4])} · 선미포 {N(slots[5])}", x + 22, y + 348, w - 44, 20, 12, Canvas.Dim);
        }

        if (_workView)
        {
            // 강화성능 — 능력치마다 지금 값(+강화와 그레이드로 붙은 몫), 선실 · 포실 · 창고, 그레이드 보너스
            var plain = voyage.StatsOf(ship, voyage.ShipMaterialId, voyage.ShipLoad);
            canvas.Fill(x + 4, y + 96, w - 12, h - 150, new Color4(0.03f, 0.06f, 0.20f, 0.98f));
            Header("기본성능", x + 20, y + 100, 200);
            Header("선실 강화", x + 250, y + 100, 200);
            // 원본의 짜임: 줄마다 「값( +붙은 몫)」, 그 밑에 초록 막대 — 붙은 몫이 그 배의 강화 상한에 얼마나 찼는가
            var caps = voyage.StrengthCaps(ship, plain);
            double workShare = ship == voyage.Ship ? Voyage.MasteryShare(voyage.Ship, voyage.Work) : 1;      // 지금 한계 = 상한 × 조타 숙련도의 찬 비율
            void Line(int icon, string label, int now, int was, float lx, float ly, int cap)
            {
                canvas.Image($"gm0:{icon}", () => (_markParts ??= new UiParts(0)).Pixels(icon), lx, ly + 1, 18, 18);
                canvas.Text(label, lx + 22, ly, 90, 20, 13, Canvas.White);
                canvas.Text($"{now}( {(now - was >= 0 ? "+" : "")}{now - was})", lx, ly, 196, 20, 13, now > was ? new Color4(0.55f, 1f, 0.6f, 1) : Canvas.White, 2);
                float part = cap > 0 ? Math.Clamp((now - was) / (float)cap, 0, 1) : 0;
                canvas.Fill(lx, ly + 20, 196, 3, new Color4(0.30f, 0.32f, 0.40f, 0.9f));
                if (part > 0) canvas.Fill(lx, ly + 20, 196 * part, 3, new Color4(0.55f, 0.85f, 0.35f, 1));
                if (canvas.Hover(lx, ly, 196, 23)) _tip = (cap > 0 ? $"강화 수치: {Math.Max(0, now - was)}/{(int)Math.Floor(cap * workShare + 1e-9)}(상한: {cap})" : $"{label} — 강화할 수 없다", lx + 98, ly - 2);      // 원본의 글 그대로: 지금 값 / 지금 한계(상한)
            }
            Line(310, "내구력", s.Durability, plain.Durability, x + 22, y + 130, caps[0]);
            Line(306, "세로돛성능", s.VerticalSail, plain.VerticalSail, x + 22, y + 154, caps[1]);
            Line(307, "가로돛성능", s.HorizontalSail, plain.HorizontalSail, x + 22, y + 178, caps[2]);
            Line(314, "조력", s.Rowing, plain.Rowing, x + 22, y + 202, caps[3]);
            Line(308, "선회 성능", s.Turn, plain.Turn, x + 22, y + 226, caps[4]);
            Line(309, "내파성", s.WaveResist, plain.WaveResist, x + 22, y + 250, caps[5]);
            Line(333, "장갑치", s.Armor, plain.Armor, x + 22, y + 274, caps[6]);
            Line(330, "선실적재량", s.MaxCrew, plain.MaxCrew, x + 252, y + 130, caps[7]);
            Line(305, "포실적재량", s.Guns, plain.Guns, x + 252, y + 154, caps[8]);
            Line(323, "창고 용량", s.Hold, plain.Hold, x + 252, y + 178, caps[9]);
            Header("특수 강화", x + 250, y + 212, 200);
            // 원본의 짜임: 재질(그림과 이름), 그 밑에 그레이드 보너스의 그림들을 테두리로 묶고 「그레이드 보너스」
            if (voyage.MaterialOf(voyage.ShipMaterialId) is { } timber)
            {
                MaterialIcon(timber, x + 254, y + 240, 24);
                canvas.Text(timber.Name, x + 284, y + 242, 166, 20, 14, Canvas.White);
            }
            var bonuses = voyage.Work.Bonuses;
            if (bonuses.Count == 0) canvas.Text("그레이드 보너스 없음", x + 254, y + 272, 196, 20, 13, Canvas.Dim);
            else
            {
                canvas.Frame(x + 252, y + 268, bonuses.Count * 36 + 4, 40, Canvas.Gold, 1.5f);
                for (int k = 0; k < bonuses.Count; k++)
                {
                    string name = Voyage.BonusName(bonuses[k]);

                    float bx = x + 256 + k * 36, by = y + 272;
                    BonusPicture(bonuses[k], bx + 2, by, 28);
                    if (canvas.Hover(bx, by, 32, 32)) _tip = (name, bx + 16, by - 2);
                }
                canvas.Text("그레이드 보너스", x + 254, y + 310, 196, 20, 13, Canvas.White);
            }
        }
        if (canvas.Button(_workView ? "선박 정보" : "강화성능", x + 176, y + h - 50, 110, 34, true, 14)) _workView = !_workView;
        if (canvas.Button("보유 선박정보", x + 20, y + h - 50, 150, 34, true, 14)) (voyage.Dialog, _swapFrom) = (Dialog.ShipSwap, Dialog.ShipInfo);
        if (canvas.Button("이전", x + w - 130, y + h - 50, 110, 34)) voyage.Dialog = _infoDocked ? Dialog.ShipSwap : Dialog.None;
    }

    /// <summary>
    /// 조선소 주인의 차림 — 원본처럼 오른쪽 아래에 작은 창으로 단추 일곱과 닫기.
    /// 원본 단추 그림과 이름의 짝은 못 맞춰서(「특수조선」만 확인) 글 단추이고, 나머지 이름과 차례는 이 게임에 있는 일에 맞춘 것이다.
    /// </summary>
    private void ShipyardMenu()
    {
        const float w = 396, h = 164;
        float x = canvas.Width - w - 8, y = canvas.Height - h - 8;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text("조선소주인", x + 12, y + 6, 200, 24, 17, Canvas.White);
        // 맡긴 배의 건조일수를 기다리지 않고 넘긴다(도시 메뉴의 「날짜 +1」과 같은 일)
        if (canvas.Button("날짜 +1", x + 104, y + 6, 66, 26, true, 12)) voyage.PassDay();
        if (voyage.Ordered == null) canvas.Text(voyage.ShipbuildingLine, x + 176, y + 9, w - 188, 20, 13, Canvas.Gold, 2);
        string? receive = voyage.ReceiveBlocker;
        (string Label, bool Enabled, Action Run)[] buttons =
        [
            ("선박 구입", true, () => voyage.Dialog = Dialog.Shipyard),
            ("선박 매각", true, () => (voyage.Dialog, _swapFrom) = (Dialog.ShipSwap, Dialog.ShipyardMenu)),
            ("선박교환", true, () => (voyage.Dialog, _swapFrom) = (Dialog.ShipSwap, Dialog.ShipyardMenu)),
            (voyage.RepairCost > 0 ? $"수리 ({voyage.RepairCost:N0})" : "수리", voyage.RepairCost > 0 && voyage.Money >= voyage.RepairCost, voyage.Repair),
            ("커스텀설정 조선 (C)", true, OpenCustomSetup),
            ("선박 받기", receive == null, voyage.ReceiveShip),
            ("선박부품", true, () => (voyage.Dialog, _partPage) = (Dialog.ShipParts, 0)),
            ("선박 조합", true, () => (voyage.Dialog, _combineMain, _combineMaterial) = (Dialog.Combine, -1, -1)),
            ("닫기 ✕", true, () => voyage.Dialog = Dialog.None),
            ("특수 조선", true, () => (voyage.Dialog, _specialChosen, _specialTitle) = (Dialog.SpecialBuild, 0, "특수 조선")),
        ];
        for (int i = 0; i < buttons.Length; i++)
        {
            // 원본처럼 첫 줄 넷, 둘째 · 셋째 줄 둘씩(넓게), 닫기는 오른쪽 아래
            var (bx, by, bw) = i < 4 ? (x + 10 + i * 95, y + 38, 91f) : i < 8 ? (x + 10 + (i - 4) % 2 * 142, y + 78 + (i - 4) / 2 * 40, 138f) : (x + 300, i == 8 ? y + 118 : y + 78, 86f);
            if (canvas.Button(buttons[i].Label, bx, by, bw, 36, buttons[i].Enabled, 13)) { buttons[i].Run(); break; }
        }
        if (voyage.Ordered != null) canvas.Text(receive == null ? "맡긴 배가 다 됐다" : $"맡긴 배: {receive}", x + 176, y + 9, w - 188, 20, 12, Canvas.Gold, 2);
    }

    /// <summary>조선소 주인 차림의 「커스텀설정 조선」을 연다 — 차림에서 C 를 눌러도 온다.</summary>
    public void OpenCustomSetup() => (voyage.Dialog, _specialChosen, _specialTitle) = (Dialog.SpecialBuild, 0, "커스텀설정 조선");

    private int _specialChosen, _hullChosen;
    private string _specialTitle = "커스텀설정 조선";

    /// <summary>특수 조선의 신규건조를 처음부터 연다(대본에서도 쓴다).</summary>
    public void OpenHullBuild()
    {
        (voyage.Dialog, _specialTitle, _hullStage, _hullChosen, _hullTop) = (Dialog.HullBuild, "특수 조선", 0, 0, 0);
        _hullPicked.Clear();
    }

    private int _equipTop;

    /// <summary>
    /// 장비 물품 — 원본의 짜임: 왼쪽 「소유장비물품」 격자(다섯 칸), 오른쪽에 갈래마다의 칸(모자 · 옷 · 장갑 · 신발, 무기 · 장신구)과 내 모습, 아래에 수치.
    /// 격자의 물품을 누르면 제 갈래 칸에 들어가고(다시 누르거나 칸을 누르면 빠진다), 장비한 것에는 테두리가 뜬다.
    /// 장비해도 3D 모습은 안 바뀐다 — 어느 아이템이 어느 옷 모형인지를 못 이었다.
    /// </summary>
    private void EquipWindow()
    {
        const float w = 880, h = 480, tile = 64;
        const int columns = 5, lines = 5;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Fill(x + 20, y + 16, columns * (tile + 4) - 4, 24, new Color4(0.72f, 0.76f, 0.84f, 0.92f));
        canvas.Text("소유장비물품", x + 20, y + 17, columns * (tile + 4) - 4, 22, 15, new Color4(0.05f, 0.08f, 0.2f, 1), 1, true, false);
        var owned = voyage.GearOwned();
        int rows = (owned.Count + columns - 1) / columns;
        ScrollBar(x + 24 + columns * (tile + 4), y + 48, lines * (tile + 4) - 4, lines, rows, ref _equipTop);
        _equipTop = Math.Clamp(_equipTop, 0, Math.Max(0, rows - lines));
        bool Picture(int id, float px, float py, float size) => canvas.Image($"sb{id / 100000}:{id}", () => (_itemIcons ??= new ImageSet(@"0010\0001\sb")).Pixels(id / 100000, id), px, py, size, size);
        for (int k = 0; k < columns * lines; k++)
        {
            int index = _equipTop * columns + k;
            float tx = x + 20 + k % columns * (tile + 4), ty = y + 48 + k / columns * (tile + 4);
            canvas.Fill(tx, ty, tile, tile, new Color4(0.03f, 0.10f, 0.08f, 0.9f));
            if (index >= owned.Count) continue;
            var gear = owned[index];
            if (!Picture(gear.Id, tx + 4, ty + 4, tile - 8)) canvas.Text(gear.Name, tx + 2, ty + 14, tile - 4, 40, 11, Canvas.White, 1);
            bool worn = voyage.Equipped[Math.Min(gear.Slot, 5)] == gear.Id;
            if (worn) canvas.Frame(tx, ty, tile, tile, new Color4(0.3f, 0.95f, 0.9f, 1), 2.5f);
            if (!canvas.Hover(tx, ty, tile, tile)) continue;
            _tip = ($"{gear.Name} ({Voyage.GearSlots[Math.Min(gear.Slot, 5)]}){(worn ? " — 장비 중" : "")}{string.Concat(voyage.GearLine(gear).Trim().Split(" · ", StringSplitOptions.RemoveEmptyEntries).Chunk(4).Select(c => "\n" + string.Join(" · ", c)))}", tx + tile / 2, ty + 2);      // 수치 · 스킬 보정은 네 개씩 끊어 아랫줄에
            if (canvas.Pointer.Clicked) voyage.Equip(gear.Id);
        }
        if (owned.Count == 0) canvas.Text("가진 장비 물품이 없다.\n(소지품 → 아이템 추가 → 「의상」)", x + 30, y + 60, 300, 44, 14, Canvas.Dim);
        canvas.Line(x + 388, y + 16, x + 388, y + h - 60, new Color4(0.5f, 0.55f, 0.75f, 0.8f), 1);

        // 갈래의 칸 — 원본처럼 왼쪽 줄에 모자 · 옷 · 장갑 · 신발, 오른쪽 줄에 무기 · 장신구
        (int Slot, float X, float Y)[] spots = [(1, 410, 40), (0, 410, 124), (3, 410, 208), (2, 410, 292), (4, 780, 124), (5, 780, 208)];
        foreach (var (slot, sx, sy) in spots)
        {
            float px = x + sx, py = y + sy;
            canvas.Fill(px, py, 80, 76, new Color4(0.04f, 0.06f, 0.2f, 0.9f));
            canvas.Frame(px, py, 80, 76, new Color4(0.75f, 0.75f, 0.8f, 0.7f), 1);
            int item = voyage.Equipped[slot];
            if (item > 0 && voyage.Items.GetValueOrDefault(item) > 0) Picture(item, px + 8, py + 6, 64);
            else canvas.Text(Voyage.GearSlots[slot], px, py + 26, 80, 22, 14, Canvas.Dim, 1);
            if (!canvas.Hover(px, py, 80, 76)) continue;
            _tip = (item > 0 ? $"{voyage.ItemName(item)} — 누르면 벗는다" : $"{Voyage.GearSlots[slot]} 칸", px + 40, py - 2);
            if (canvas.Pointer.Clicked) voyage.Unequip(slot);
        }
        // 가운데에 내 모습
        var looks = voyage.Looks;
        FigurePreview = (x + 500, y + 16, 270, h - 130, looks[0], looks[1], looks[2], looks[3], looks[4]);
        (FigureView, FigureMine) = ((0.35f, false, looks[5], looks[6]), true);
        // 수치 — 장비 표의 칸들을 더한 것(뜻은 못 밝혀 차례대로 적는다)
        var totals = voyage.GearTotals();
        string[] names = ["공격력", "방어력", "정장도", "변장도"];
        for (int k = 0; k < 4; k++)
        {
            canvas.Text(names[k], x + w - 190, y + h - 150 + k * 22, 80, 20, 12, Canvas.Dim, 2);
            canvas.Text($"{totals[k]}", x + w - 100, y + h - 152 + k * 22, 70, 22, 15, Canvas.White, 2);
        }
        canvas.Text("물품을 누르면 장비한다 · 다시 누르면 벗는다", x + 20, y + h - 42, 360, 20, 12, Canvas.Dim);
        if (canvas.Button("이전", x + w - 130, y + h - 46, 110, 32)) voyage.Dialog = Dialog.Character;
    }

    private int _hullStage, _hullItem, _hullTop;
    private readonly List<int> _hullPicked = [];

    /// <summary>
    /// 특수 조선의 신규건조 — 원본의 차례(화면 글 6820 ~ 6823): 사용할 선체 → 건조할 선박의 종류 → 사용할 재료(「주요 돛」「포문」이 있어야 한다,
    /// 조합에 따라 옵션 스킬) → 성능을 보고 맡긴다. 어느 선체로 어느 도시에서 무엇을 짓는지는 배 상세(ssjoy)를 모은 배만 안다.
    /// </summary>
    private void HullBuildWindow()
    {
        const float w = 860, h = 460, listWidth = 360;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        string[] asks = ["사용할 선체를 선택해 주십시오.", "건조할 선박의 종류를 선택해 주십시오.", "사용할 재료를 선택해 주십시오. 신규건조는 「주요 돛」「포문」이 필요합니다."];
        string[] heads = ["선체", "선박종류", "재료"];
        canvas.Text("특수 조선", x + 20, y + 10, 200, 28, 20, Canvas.Gold, 0, true);
        canvas.Text(asks[_hullStage], x + 150, y + 15, w - 330, 22, 14, Canvas.White);
        canvas.Text(voyage.ShipbuildingLine, x + 20, y + 15, w - 40, 22, 14, Canvas.Gold, 2);
        canvas.Fill(x + 20, y + 46, listWidth - 20, 24, new Color4(0.72f, 0.76f, 0.84f, 0.92f));
        canvas.Text(heads[_hullStage], x + 20, y + 47, listWidth - 20, 22, 15, new Color4(0.05f, 0.08f, 0.2f, 1), 1, true, false);
        canvas.Line(x + listWidth + 14, y + 46, x + listWidth + 14, y + h - 60, new Color4(0.5f, 0.55f, 0.75f, 0.8f), 1);
        float rx = x + listWidth + 30, rw = w - listWidth - 50;
        const int shown = 6;
        // 목록의 한 줄 — 그림, 이름, 아랫글. 눌렸으면 true
        bool Row(int i, bool chosen, bool enabled, Func<float, float, float, bool> picture, string name, string under)
        {
            float row = y + 78 + (i - _hullTop) * 52;
            bool hover = canvas.Hover(x + 20, row, listWidth - 34, 50);
            if (chosen) canvas.Fill(x + 20, row, listWidth - 34, 50, new Color4(0.12f, 0.55f, 0.52f, 0.95f));
            else if (hover) canvas.Fill(x + 20, row, listWidth - 34, 50, new Color4(0.2f, 0.3f, 0.6f, 0.6f));
            canvas.Fill(x + 25, row + 4, 42, 42, new Color4(0.02f, 0.03f, 0.08f, 0.95f));
            picture(x + 25, row + 4, 42);
            canvas.Text(name, x + 74, row + 3, listWidth - 112, 22, name.Length > 13 ? 12 : 15, enabled ? Canvas.White : Canvas.Dim);
            canvas.Text(under, x + 74, row + 26, listWidth - 112, 20, 12, Canvas.Dim);
            return hover && canvas.Pointer.Clicked;
        }
        bool ItemPicture(int id, float px, float py, float size) => canvas.Image($"sb22:{id}", () => (_itemIcons ??= new ImageSet(@"0010\0001\sb")).Pixels(22, id), px, py, size, size);

        var hulls = voyage.HullsOwned();
        var plans = _hullStage > 0 ? voyage.HullPlans(_hullItem) : [];
        string? blocker = null;
        Action? next = null;
        if (_hullStage == 0)
        {
            _hullChosen = Math.Clamp(_hullChosen, 0, Math.Max(0, hulls.Count - 1));
            ScrollBar(x + listWidth - 10, y + 78, shown * 52 - 2, shown, hulls.Count, ref _hullTop);
            for (int i = _hullTop; i < Math.Min(hulls.Count, _hullTop + shown); i++)
            {
                int id = hulls[i].Id;
                if (Row(i, i == _hullChosen, true, (px, py, size) => ItemPicture(id, px, py, size), hulls[i].Name, $"가진 수 {voyage.Items.GetValueOrDefault(id)}")) _hullChosen = i;
            }
            if (hulls.Count == 0)
            {
                canvas.Text("가진 선체가 없다.", x + 30, y + 86, listWidth - 40, 22, 14, Canvas.Dim);
                canvas.Text("특수 조선의 신규건조에는 선체가 있어야 한다.\n(소지품 → 아이템 추가 → 「조빌」의 「선체」)", rx, y + 60, rw, 60, 14, Canvas.Dim);
                blocker = "선체가 없다";
            }
            else
            {
                var hull = hulls[_hullChosen];
                var builds = voyage.HullPlans(hull.Id);
                canvas.Text(hull.Name, rx, y + 50, rw, 26, 18, Canvas.White, 0, true);
                canvas.Text(hull.Description.Replace("\n", " "), rx, y + 82, rw, 40, 13, Canvas.Dim);
                canvas.Text(builds.Count == 0 ? "이 선체로 짓는 배의 자료가 없다." : "이 선체로 짓는 배: " + string.Join(", ", builds.Select(b => b.Ship.Name).Distinct()), rx, y + 130, rw, 80, 14, builds.Count == 0 ? new Color4(1f, 0.6f, 0.5f, 1) : Canvas.White);
                if (builds.Count == 0) blocker = "이 선체로 짓는 배의 자료가 없다";
                next = () => (_hullItem, _hullStage, _hullChosen, _hullTop) = (hull.Id, 1, 0, 0);
            }
        }
        else if (_hullStage == 1)
        {
            _hullChosen = Math.Clamp(_hullChosen, 0, Math.Max(0, plans.Count - 1));
            ScrollBar(x + listWidth - 10, y + 78, shown * 52 - 2, shown, plans.Count, ref _hullTop);
            for (int i = _hullTop; i < Math.Min(plans.Count, _hullTop + shown); i++)
            {
                var listed = plans[i];
                bool here = listed.City == voyage.City.Name;
                if (Row(i, i == _hullChosen, here, (px, py, size) => ShipIcon(listed.Ship.Id, px, py, size), listed.Ship.Name + (listed.MaterialName == "너도밤나무" ? "" : $" ({listed.MaterialName})"),
                        here ? $"{voyage.BuildCost(listed.Ship, listed.Material):N0} Ð" : $"{listed.City}의 조선소에서만")) _hullChosen = i;
            }
            if (plans.Count > 0)
            {
                var plan = plans[_hullChosen];
                var s = voyage.StatsOf(plan.Ship, plan.Material, 0);
                canvas.Text(plan.Ship.Name, rx, y + 50, rw, 26, 18, Canvas.White, 0, true);
                canvas.Text($"필요 선체: {plan.Hull}   ·   조선 랭크 {plan.Rank}   ·   재질 {plan.MaterialName}", rx, y + 84, rw, 20, 13, Canvas.Dim);
                canvas.Text($"내구 {s.Durability}   세로돛 {s.VerticalSail}   가로돛 {s.HorizontalSail}   선회 {s.Turn}   내파 {s.WaveResist}   장갑 {s.Armor}", rx, y + 116, rw, 20, 14, Canvas.White);
                canvas.Text($"선원 {s.MinCrew} / {s.MaxCrew}   대포 {s.Guns}   창고 {s.Hold}", rx, y + 140, rw, 20, 14, Canvas.White);
                if (voyage.Data.ShipDetail(plan.Ship.Name) is { Skills.Count: > 0 } detail)
                    canvas.Text("재료의 조합으로 붙는 스킬: " + string.Join(" · ", detail.Skills.Select(k => $"{k.Name}({string.Join(" + ", k.Parts)})")), rx, y + 172, rw, 120, 12, Canvas.Dim);
                if (plan.City != voyage.City.Name) blocker = $"{plan.City}의 조선소에서만 짓는다";
                else if (voyage.ShipbuildingRank < plan.Rank) blocker = $"조선 랭크 {plan.Rank} 이 있어야 한다";
                next = () => { (_hullStage, _hullTop) = (2, 0); _hullPicked.Clear(); };
            }
        }
        else
        {
            var plan = plans[Math.Clamp(_hullChosen, 0, Math.Max(0, plans.Count - 1))];
            // 가진 조빌 아이템 가운데 재료가 될 것 — 돛과 포문을 앞에
            var owned = voyage.Data.Papers.Where(p => Voyage.IsShipItem(p.Id) && !Voyage.IsHullName(p.Name) && !p.Name.EndsWith("선박재료") && voyage.Items.GetValueOrDefault(p.Id) > 0)
                .OrderBy(p => Voyage.IsSailName(p.Name) ? 0 : Voyage.IsGunportName(p.Name) ? 1 : 2).ToList();
            _hullPicked.RemoveAll(id => !owned.Exists(p => p.Id == id));
            ScrollBar(x + listWidth - 10, y + 78, shown * 52 - 2, shown, owned.Count, ref _hullTop);
            for (int i = _hullTop; i < Math.Min(owned.Count, _hullTop + shown); i++)
            {
                int id = owned[i].Id;
                bool picked = _hullPicked.Contains(id);
                string kind = Voyage.IsSailName(owned[i].Name) ? "주요 돛" : Voyage.IsGunportName(owned[i].Name) ? "포문" : "선박 재료";
                if (Row(i, picked, true, (px, py, size) => ItemPicture(id, px, py, size), (picked ? "● " : "") + owned[i].Name, $"{kind} · 가진 수 {voyage.Items.GetValueOrDefault(id)}"))
                {
                    if (picked) _hullPicked.Remove(id);
                    else if (_hullPicked.Count < 4) _hullPicked.Add(id);
                }
            }
            if (owned.Count == 0) canvas.Text("재료로 쓸 조빌 아이템이 없다.\n(소지품 → 아이템 추가 → 「조빌」)", x + 30, y + 86, listWidth - 40, 44, 14, Canvas.Dim);
            var s = voyage.StatsOf(plan.Ship, plan.Material, 0);
            canvas.Text($"{plan.Ship.Name}  —  {plan.Hull}", rx, y + 50, rw, 26, 17, Canvas.White, 0, true);
            canvas.Text("특수조선으로 작성하는 배의 성능입니다.", rx, y + 80, rw, 20, 12, Canvas.Dim);
            canvas.Text($"내구 {s.Durability}   세로돛 {s.VerticalSail}   가로돛 {s.HorizontalSail}   선회 {s.Turn}   내파 {s.WaveResist}   장갑 {s.Armor}", rx, y + 102, rw, 20, 14, Canvas.White);
            canvas.Text($"선원 {s.MinCrew} / {s.MaxCrew}   대포 {s.Guns}   창고 {s.Hold}   재질 {plan.MaterialName}", rx, y + 126, rw, 20, 14, Canvas.White);
            canvas.Text("고른 재료: " + (_hullPicked.Count == 0 ? "없음" : string.Join(" · ", _hullPicked.Select(voyage.ItemName))), rx, y + 160, rw, 40, 13, Canvas.White);
            if (voyage.HullSkill(plan, _hullPicked) is { } gain) canvas.Text($"→ 옵션 스킬 「{gain.Name}」이(가) 붙는다", rx, y + 204, rw, 20, 13, new Color4(0.5f, 1f, 0.6f, 1));
            canvas.Text($"필요한 건조일수   {Voyage.BuildDays(plan.Ship)}일", rx, y + 240, rw, 20, 14, Canvas.White, 2);
            canvas.Text($"비용   {voyage.BuildCost(plan.Ship, plan.Material):N0} Ð", rx, y + 264, rw, 20, 14, Canvas.White, 2);
            canvas.Text($"소지금   {voyage.Money:N0} Ð", rx, y + 288, rw, 20, 14, Canvas.White, 2);
            blocker = voyage.HullBlocker(plan, _hullPicked);
            next = () =>
            {
                voyage.OrderHull(plan, _hullPicked.ToList());
                if (voyage.Ordered != null) { voyage.Dialog = Dialog.ShipyardMenu; _hullPicked.Clear(); }
            };
        }
        if (blocker != null) canvas.Text(blocker, rx, y + h - 86, rw, 20, 14, new Color4(1f, 0.5f, 0.45f, 1));
        if (canvas.Button("돌아가기", x + w - 330, y + h - 50, 100, 34, true, 14))
        {
            if (_hullStage == 0) voyage.Dialog = Dialog.SpecialBuild;
            else (_hullStage, _hullChosen, _hullTop) = (_hullStage - 1, 0, 0);
        }
        else if (canvas.Button(_hullStage == 2 ? "건조" : "다음 ▶", x + w - 224, y + h - 50, 100, 34, blocker == null && next != null, 14)) next!();
        if (canvas.Button("취소", x + w - 118, y + h - 50, 98, 34, true, 14)) voyage.Dialog = Dialog.None;
    }

    /// <summary>
    /// 특수조선 — 원본의 첫 화면: 왼쪽 「신규건조 강화상대」(신규건조와 가진 배들), 오른쪽 고른 배의 성능, 아래 선박부품 · 성능초기화 · 다음.
    /// 「다음」은 신규건조면 배 고르기(→ 커스텀설정 조선의 재질 · 적재량), 타고 있는 배면 강화로 간다. 부두의 배는 갈아탄 뒤에 강화한다.
    /// </summary>
    private void SpecialBuildWindow()
    {
        const float w = 860, h = 460, listWidth = 360;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text(_specialTitle, x + 20, y + 10, 220, 28, 20, Canvas.Gold, 0, true);
        bool hullMode = _specialTitle == "특수 조선";
        bool onBoard = voyage.Data.Settings.ModWorkOnBoard;        // 모드: 타고 있는 배도 고를 수 있다
        canvas.Text("신규건조 또는 강화를 선택해 주십시오.", x + 240, y + 15, w - 260, 22, 14, Canvas.White);
        canvas.Text(voyage.ShipbuildingLine, x + 20, y + 15, w - 40, 22, 14, Canvas.Gold, 2);
        canvas.Fill(x + 20, y + 46, listWidth - 20, 24, new Color4(0.72f, 0.76f, 0.84f, 0.92f));
        canvas.Text("신규건조 강화상대", x + 20, y + 47, listWidth - 20, 22, 15, new Color4(0.05f, 0.08f, 0.2f, 1), 1, true, false);
        // 0 = 신규건조, 1 = 타고 있는 배, 2 ~ = 부두의 배
        // 한 화면에 여섯 줄 — 넘치면 휠 · ↑↓ 로 굴린다
        const int specialShown = 6;
        int count = 2 + voyage.Dock.Count;
        _specialChosen = Math.Clamp(_specialChosen, 0, count - 1);
        // 고른 줄을 따라가는 것은 ↑↓ 로 옮겼을 때만 — 휠이나 스크롤바로 굴릴 때는 고른 줄이 화면 밖에 있어도 된다
        if (_specialKeyed && _specialChosen < _specialTop) _specialTop = _specialChosen;
        if (_specialKeyed && _specialChosen >= _specialTop + specialShown) _specialTop = _specialChosen - specialShown + 1;
        _specialKeyed = false;
        _specialTop = Math.Clamp(_specialTop, 0, Math.Max(0, count - specialShown));
        ScrollBar(x + listWidth + 1, y + 78, specialShown * 52 - 2, specialShown, count, ref _specialTop);
        float row = y + 78;
        for (int i = _specialTop; i < Math.Min(count, _specialTop + specialShown); i++, row += 52)
        {
            bool chosen = i == _specialChosen, hover = canvas.Hover(x + 20, row, listWidth - 20, 50);
            if (chosen) canvas.Fill(x + 20, row, listWidth - 20, 50, new Color4(0.12f, 0.55f, 0.52f, 0.95f));
            else if (hover) canvas.Fill(x + 20, row, listWidth - 20, 50, new Color4(0.2f, 0.3f, 0.6f, 0.6f));
            if (hover && canvas.Pointer.Clicked) _specialChosen = i;
            int icon = i == 0 ? 355 : 301;
            if (i == 0 || !ShipIcon(i == 1 ? voyage.Ship.Id : voyage.Dock[i - 2].Ship.Id, x + 25, row + 4, 42))
                canvas.Image($"gm0:{icon}", () => (_markParts ??= new UiParts(0)).Pixels(icon), x + 28, row + 7, 36, 36);
            if (i == 0) { canvas.Text("신규건조", x + 74, row + 13, 250, 24, 17, Canvas.White); continue; }
            var (name, times, durability, most) = i == 1
                ? (voyage.Ship.Name + "  (타고 있는 배)", voyage.Work.Times, voyage.Durability, voyage.Stats.Durability)
                : (voyage.Dock[i - 2].Ship.Name, voyage.Dock[i - 2].Work?.Times ?? 0, voyage.Dock[i - 2].Durability, voyage.StatsOf(voyage.Dock[i - 2]).Durability);
            int limit = voyage.MaxTimesOf(i == 1 ? voyage.Ship : voyage.Dock[i - 2].Ship);
            canvas.Text(name, x + 74, row + 3, listWidth - 100, 22, 15, i == 1 && !onBoard ? Canvas.Dim : Canvas.White);       // 타고 있는 배는 흐리게 — 건드릴 수 없다(모드를 켜면 된다)
            // 이름 옆에 선체의 갈래(특수 조선에 드는 선체) — 자료가 있는 배만
            var hullOf = voyage.Data.ShipDetail((i == 1 ? voyage.Ship : voyage.Dock[i - 2].Ship).Name);
            string hullType = hullOf == null ? "" : hullOf.Hull != "" ? hullOf.Hull : hullOf.Special.FirstOrDefault()?.Hull ?? "";
            if (hullType != "") canvas.Text(hullType, x + 74, row + 6, listWidth - 100, 18, 12, Canvas.Gold, 2);
            canvas.Text($"강화 {times}/{limit}     내구 {durability:0}/{most}", x + 74, row + 26, listWidth - 100, 20, 13, Canvas.Dim);
        }
        canvas.Line(x + listWidth + 14, y + 46, x + listWidth + 14, y + h - 60, new Color4(0.5f, 0.55f, 0.75f, 0.8f), 1);

        float rx = x + listWidth + 30, rw = w - listWidth - 50;
        void Cell(int icon, string value, float cx, float cy, float cw)
        {
            canvas.Fill(cx, cy, cw, 26, new Color4(0.04f, 0.07f, 0.2f, 0.85f));
            canvas.Image($"gm0:{icon}", () => (_markParts ??= new UiParts(0)).Pixels(icon), cx + 3, cy + 3, 20, 20);
            canvas.Text(value, cx, cy + 2, cw - 6, 22, 15, Canvas.White, 2);
        }
        if (_specialChosen == 0)
        {
            canvas.Text("신규건조", rx, y + 50, rw, 26, 18, Canvas.White, 0, true);
            canvas.Text(hullMode ? "특수한 선체로 특수 선박을 짓는다.\n선체 → 선박의 종류 → 재료(「주요 돛」「포문」)의 차례로 고른다.\n건조일수가 지나면 「선박 받기」로 받는다."
                                 : "배를 고른 뒤 재질과 최대적재량을 정해 건조를 맡긴다.\n적재량을 늘리면 화물을 많이 싣지만 선회와 속도가 떨어진다.\n건조일수가 지나면 「선박 받기」로 받는다.", rx, y + 86, rw, 100, 15, Canvas.White);
            if (voyage.Ordered is { } ordered) canvas.Text($"맡겨 둔 배: {ordered.Ship.Name} — {(voyage.ReceiveBlocker ?? "다 됐다")}", rx, y + 196, rw, 22, 14, Canvas.Gold);
        }
        else
        {
            var s = _specialChosen == 1 ? voyage.Stats : voyage.StatsOf(voyage.Dock[_specialChosen - 2]);
            canvas.Text(_specialChosen == 1 ? voyage.Ship.Name : voyage.Dock[_specialChosen - 2].Ship.Name, rx, y + 50, rw, 26, 18, Canvas.White, 0, true);
            Cell(306, $"{s.VerticalSail}", rx, y + 88, 96); Cell(307, $"{s.HorizontalSail}", rx + 100, y + 88, 96); Cell(314, $"{s.Rowing}", rx + 200, y + 88, 96);
            Cell(308, $"{s.Turn}", rx, y + 118, 96); Cell(309, $"{s.WaveResist}", rx + 100, y + 118, 96); Cell(333, $"{s.Armor}", rx + 200, y + 118, 96);
            Cell(310, $"{s.Durability}", rx + 310, y + 88, 130);
            Cell(330, $"{s.MinCrew} / {s.MaxCrew}", rx, y + 158, 146); Cell(305, $"{s.Guns}", rx + 150, y + 158, 146); Cell(323, $"{s.Hold}", rx + 300, y + 158, 140);
            // 그 배에 붙은 옵션 스킬(과 전용함 스킬) — 그림과 이름, 그레이드
            var shownWork = _specialChosen == 1 ? voyage.Work : voyage.Dock[_specialChosen - 2].Work;
            var fittedSkills = shownWork.Dedicated > 0 ? shownWork.Skills.Append(shownWork.Dedicated).ToList() : shownWork.Skills;
            float skillY = (_specialChosen == 1 && !onBoard) || _resetAsk ? y + 300 : y + 200;
            canvas.Text($"옵션 스킬 {shownWork.Skills.Count}/{voyage.SkillSlotsOf(shownWork)}" + (shownWork.Grade > 0 ? $"     그레이드 {shownWork.Grade}" : ""), rx, skillY, rw, 20, 13, Canvas.Gold);
            if (fittedSkills.Count == 0) canvas.Text("없음", rx, skillY + 24, 100, 20, 13, Canvas.Dim);
            for (int k = 0; k < fittedSkills.Count && k < 6; k++)
            {
                int skillId = fittedSkills[k];
                float kx = rx + k % 2 * (rw / 2), ky = skillY + 24 + k / 2 * 36;
                SkillIcon(skillId, kx, ky, 1);
                canvas.Text(voyage.OptionName(skillId) + (skillId == shownWork.Dedicated ? " (전용함)" : ""), kx + 38, ky + 6, rw / 2 - 42, 20, 14, Canvas.White);
            }
            if (_specialChosen == 1 && !onBoard) canvas.Text("탑승하고 있는 선박은 건드릴 수 없다. 강화하려면 다른 배로 갈아탄다. (☰ → 모드에서 풀 수 있다)", rx, y + 198, rw, 44, 14, Canvas.Dim);
            else if (_resetAsk)
            {
                canvas.Fill(rx, y + 196, rw, 96, new Color4(0.02f, 0.04f, 0.14f, 0.95f));
                canvas.Text("선박의 성능을 초기화합니다. 재질 이외의 모든 강화치가 0 이 됩니다. (특수조선 해체 기법서 1권)\n초기화한 후에는 다시 되돌릴 수 없습니다. 이대로 진행하시겠습니까?", rx + 10, y + 202, rw - 20, 50, 14, Canvas.White);
                if (canvas.Button("예", rx + rw - 180, y + 256, 80, 30))
                {
                    if (_specialChosen == 1) voyage.ResetWork();
                    else
                    {
                        voyage.BeginWork(voyage.Dock[_specialChosen - 2]);
                        voyage.ResetWork();
                        voyage.EndWork();
                    }
                    (_resetAsk, _specialChosen) = (false, 0);
                }
                else if (canvas.Button("아니오", rx + rw - 94, y + 256, 84, 30)) _resetAsk = false;
            }
        }
        int books = voyage.Items.GetValueOrDefault(Voyage.DismantleBook);
        string? resetWhy = _specialChosen > 1 ? voyage.ResetBlocker(voyage.Dock[_specialChosen - 2].Work) : _specialChosen != 1 ? "초기화할 배를 고른다"
                         : onBoard ? voyage.ResetBlocker(voyage.Work) : "타고 있는 배는 못 한다 (모드에서 켤 수 있다)";
        if (canvas.Button("성능초기화", x + 20, y + h - 50, 120, 34, resetWhy == null, 14)) _resetAsk = true;
        if (canvas.Hover(x + 20, y + h - 50, 120, 34)) _tip = (resetWhy ?? $"특수조선 해체 기법서가 한 권 든다 (가진 수 {books})", x + 20 + 200, y + h - 52);
        // 스킬만 초기화 — 책 없이 옵션 스킬만 뗀다(원본의 「선박 스킬만 초기화」)
        var skillWork = _specialChosen > 1 ? voyage.Dock[_specialChosen - 2].Work : _specialChosen == 1 && onBoard ? voyage.Work : null;
        int clearable = skillWork == null ? 0 : voyage.ClearableSkills(skillWork).Count;
        if (canvas.Button("스킬 초기화", x + 148, y + h - 50, 120, 34, clearable > 0, 14))
        {
            if (_specialChosen == 1) voyage.ClearOptionSkills();
            else { voyage.BeginWork(voyage.Dock[_specialChosen - 2]); voyage.ClearOptionSkills(); voyage.EndWork(); }
        }
        if (canvas.Hover(x + 148, y + h - 50, 120, 34)) _tip = (skillWork == null ? "배를 고른다" : clearable > 0 ? $"붙인 옵션 스킬 {clearable}개를 지운다 — 책이 들지 않고 강화치 · 횟수는 그대로다" : "지울 옵션 스킬이 없다", x + 148 + 200, y + h - 52);
        if (canvas.Button("돌아가기", x + w - 330, y + h - 50, 100, 34, true, 14)) (voyage.Dialog, _resetAsk) = (Dialog.ShipyardMenu, false);
        if (canvas.Button("다음 ▶", x + w - 224, y + h - 50, 100, 34, _specialChosen != 1 || onBoard, 14))
        {
            _resetAsk = false;
            if (_specialChosen == 0 && hullMode) { (voyage.Dialog, _hullStage, _hullChosen, _hullTop) = (Dialog.HullBuild, 0, 0, 0); _hullPicked.Clear(); }
            else if (_specialChosen == 0) voyage.Dialog = Dialog.Shipyard;
            else if (_specialChosen == 1)
            {
                // 모드: 타고 있는 배를 그대로 강화한다
                (voyage.Dialog, _methodChosen, _contentChosen) = (Dialog.WorkMethod, 0, 0);
                _specialChosen = 0;
            }
            else
            {
                // 부두의 배를 강화한다 — 강화 창이 떠 있는 동안만 그 배로 바꿔 탄다
                voyage.BeginWork(voyage.Dock[_specialChosen - 2]);
                (voyage.Dialog, _methodChosen, _contentChosen) = (Dialog.WorkMethod, 0, 0);
                _specialChosen = 0;
            }
        }
        if (canvas.Button("취소", x + w - 118, y + h - 50, 98, 34, true, 14)) (voyage.Dialog, _resetAsk) = (Dialog.None, false);
    }
    private bool _resetAsk, _specialKeyed;
    private int _specialTop;

    private (int Pattern, int Tint)? _sailWas;

    /// <summary>돛 무늬의 작은 그림 — 무늬 번호 ÷ 18 이 묶음(sa000N), 나머지가 그 안의 차례.</summary>
    private bool SailThumb(int pattern, int tint, float x, float y, float size) =>
        canvas.Image($"sail{pattern}:{tint}", () => Render.GameTexture.Pixels(new Pack($@"0001\sa{pattern / 18:D4}.bin").Entry(pattern % 18), tint), x, y, size, size);
    private int _sailTop;

    /// <summary>
    /// 돛 도료 — 원본의 짜임: 「모양」 격자(무늬 열여덟)와 아래 색 단추 1 ~ 10. 고르면 배의 돛이 바로 바뀌어 보이고,
    /// 「확인」을 눌러야 도료가 들며 「이전」은 되돌린다. 배가 보이게 창을 왼쪽에 둔다.
    /// 원본은 도료 번호마다 고를 수 있는 무늬가 여섯 가지쯤으로 정해져 있는데, 그 짝을 몰라 어느 도료든 열여덟 가지를 다 고른다.
    /// </summary>
    private void SailWindow()
    {
        _sailWas ??= (voyage.SailPattern, voyage.SailTint);
        const float w = 430, h = 400, tile = 62;
        float x = 30, y = (canvas.Height - h) / 2 - 30;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text(voyage.ItemName(voyage.SailDyeItem), x + 20, y + 10, 300, 26, 18, Canvas.Gold, 0, true);
        canvas.Fill(x + 20, y + 44, w - 40, 24, new Color4(0.72f, 0.76f, 0.84f, 0.92f));
        canvas.Text("모양", x + 20, y + 45, w - 40, 22, 15, new Color4(0.05f, 0.08f, 0.2f, 1), 1, true, false);
        const int columns = 6, lines = 3;
        // 도료 번호마다 고를 수 있는 무늬가 정해져 있다. 특수 돛 도료는 무늬 하나에 색이 없다
        var patterns = Voyage.DyePatterns(voyage.SailDye);
        bool special = voyage.SailDye > 100;
        for (int k = 0; k < columns * lines && k < patterns.Length; k++)
        {
            int pattern = patterns[k], tint = special ? 0 : voyage.SailTint;
            float tx = x + 22 + k % columns * (tile + 3), ty = y + 76 + k / columns * (tile + 3);
            SailThumb(pattern, tint, tx, ty, tile);
            bool chosen = pattern == voyage.SailPattern;
            canvas.Frame(tx, ty, tile, tile, chosen ? new Color4(0.3f, 0.95f, 0.9f, 1) : new Color4(0.75f, 0.75f, 0.8f, 0.9f), chosen ? 3 : 1);
            if (canvas.Hover(tx, ty, tile, tile) && canvas.Pointer.Clicked) voyage.ShowSail(pattern, tint);
        }
        canvas.Text("색", x + 22, y + 284, 40, 22, 15, Canvas.White);
        for (int c = 0; c < Render.ShipModel.SailColors && !special; c++)
        {
            float cx = x + 66 + c * 35, cy = y + 296;
            bool chosen = c == voyage.SailTint, hover = canvas.Hover(cx - 15, cy - 15, 30, 30);
            canvas.Circle(cx, cy, 14, chosen ? new Color4(0.16f, 0.62f, 0.72f, 1) : hover ? new Color4(0.3f, 0.4f, 0.7f, 1) : new Color4(0.55f, 0.58f, 0.66f, 1));
            if (chosen) canvas.Circle(cx, cy, 14, new Color4(0.3f, 0.95f, 0.9f, 1), false, 2.5f);
            canvas.Text($"{c + 1}", cx - 14, cy - 11, 28, 22, 14, Canvas.White, 1, true);
            if (hover && canvas.Pointer.Clicked) voyage.ShowSail(voyage.SailPattern, c);
        }
        int dyes = voyage.Items.GetValueOrDefault(voyage.SailDyeItem);
        canvas.Text($"가진 돛 도료 {dyes}개 — 확인하면 하나가 든다.", x + 22, y + 322, w - 44, 20, 13, Canvas.Dim);
        bool changed = _sailWas != (voyage.SailPattern, voyage.SailTint);
        if (canvas.Button("확인", x + w - 224, y + h - 46, 100, 32, dyes > 0 && changed))
        {
            if (voyage.PaintSail()) (_sailWas, voyage.Dialog) = (null, Dialog.None);
        }
        else if (canvas.Button("이전", x + w - 118, y + h - 46, 98, 32))
        {
            voyage.ShowSail(_sailWas.Value.Pattern, _sailWas.Value.Tint);
            (_sailWas, voyage.Dialog) = (null, Dialog.None);
        }
    }

    /// <summary>선박용 도료 — 선체 빛깔을 고른다. 고르면 배가 바로 바뀌어 보이고 「확인」을 눌러야 도료가 든다. 돛 도료 창처럼 왼쪽에 둔다.</summary>
    private void HullPaintWindow()
    {
        const float w = 430, h = 250;
        float x = 30, y = (canvas.Height - h) / 2 - 30;
        canvas.PanelId = "Wnd056";
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text("선박용 도료", x + 20, y + 10, 300, 26, 18, Canvas.Gold, 0, true);
        canvas.Fill(x + 20, y + 44, w - 40, 24, new Color4(0.72f, 0.76f, 0.84f, 0.92f));
        canvas.Text("선체 색", x + 20, y + 45, w - 40, 22, 15, new Color4(0.05f, 0.08f, 0.2f, 1), 1, true, false);
        var paints = voyage.HullPaints;
        int now = voyage.HullPaintTry > 0 ? voyage.HullPaintTry : voyage.Work.HullPaint;
        for (int k = 0; k < paints.Count && k < 16; k++)
        {
            float tx = x + 24 + k % 8 * 48, ty = y + 80 + k / 8 * 48;
            int rgb = voyage.HullColor(paints[k]);
            canvas.Fill(tx, ty, 42, 42, new Color4((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f, 1));
            bool chosen = paints[k] == now;
            canvas.Frame(tx, ty, 42, 42, chosen ? new Color4(0.3f, 0.95f, 0.9f, 1) : new Color4(0.75f, 0.75f, 0.8f, 0.9f), chosen ? 3 : 1);
            if (canvas.Hover(tx, ty, 42, 42) && canvas.Pointer.Clicked) voyage.HullPaintTry = paints[k];
        }
        int cans = voyage.Data.Items.Where(i => i.Effect == "HullPaint").Sum(i => voyage.Items.GetValueOrDefault(i.Id));
        canvas.Text($"가진 선박용 도료 {cans}개 — 확인하면 하나가 든다.", x + 22, y + 178, w - 44, 20, 13, Canvas.Dim);
        if (canvas.Button("확인", x + w - 224, y + h - 46, 100, 32, cans > 0 && voyage.HullPaintTry > 0 && voyage.HullPaintTry != voyage.Work.HullPaint))
        {
            if (voyage.PaintHull(voyage.HullPaintTry)) voyage.Dialog = Dialog.None;
        }
        else if (canvas.Button("이전", x + w - 118, y + h - 46, 98, 32)) (voyage.HullPaintTry, voyage.Dialog) = (0, Dialog.None);
    }

    private int _cargoChosen = -1;

    /// <summary>
    /// 적재화물(N) — 원본의 짜임: 위에 「물자」(물 · 식량 · 자재 · 포탄), 아래에 「교역 물품」 격자, 오른쪽 아래에 창고.
    /// 배가 보이게 창을 왼쪽에 둔다. 물자 그림은 아이템 그림 묶음의 무리 14(1400100 물 · 200 식량 · 300 자재 · 400 포탄).
    /// </summary>
    private void CargoWindow()
    {
        const float w = 440, h = 420, tile = 62;
        float x = 30, y = (canvas.Height - h) / 2 - 30;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        void Header(string text, float hy, float hw)
        {
            canvas.Fill(x + 20, hy, hw, 24, new Color4(0.72f, 0.76f, 0.84f, 0.92f));
            canvas.Text(text, x + 20, hy + 1, hw, 22, 15, new Color4(0.05f, 0.08f, 0.2f, 1), 1, true, false);
        }
        void Tile(float tx, float ty, string key, Func<(int, int, byte[])?> picture, string count, bool chosen = false)
        {
            canvas.Fill(tx, ty, tile, tile, new Color4(0.02f, 0.04f, 0.12f, 0.95f));
            canvas.Image(key, picture, tx + 3, ty + 3, tile - 6, tile - 6);
            canvas.Frame(tx, ty, tile, tile, chosen ? new Color4(0.3f, 0.95f, 0.9f, 1) : new Color4(0.75f, 0.75f, 0.8f, 0.9f), chosen ? 2 : 1);
            canvas.Text(count, tx, ty + tile - 24, tile - 4, 22, 16, Canvas.White, 2, true);
        }
        Header("물자", y + 16, 260);
        (int Picture, double Amount, string Name)[] supplies =
            [(1400100, voyage.Water, "물"), (1400200, voyage.Food, "식량"), (1400300, voyage.SupplyCount(2), "자재"), (1400400, 0, "포탄")];
        for (int i = 0; i < supplies.Length; i++)
        {
            int picture = supplies[i].Picture;
            float tx = x + 24 + i * (tile + 8);
            Tile(tx, y + 48, $"sb14:{picture}", () => (_itemIcons ??= new ImageSet(@"0010\0001\sb")).Pixels(14, picture), $"{supplies[i].Amount:0}");
            if (canvas.Hover(tx, y + 48, tile, tile)) _tip = (supplies[i].Name, tx + tile / 2, y + 46);
        }
        Header("교역 물품", y + 126, 150);
        var cargo = voyage.Cargo.OrderBy(c => c.Key).ToList();
        _cargoChosen = Math.Clamp(_cargoChosen, -1, cargo.Count - 1);
        for (int i = 0; i < Math.Min(cargo.Count, 15); i++)
        {
            int id = cargo[i].Key;
            float tx = x + 24 + i % 5 * (tile + 8), ty = y + 158 + i / 5 * (tile + 8);
            Tile(tx, ty, $"sc{id}", () => (_goodIcons ??= new ImageSet(@"0010\0001\sc")).Pixels(0, id), $"{cargo[i].Value.Count}", i == _cargoChosen);
            if (!canvas.Hover(tx, ty, tile, tile)) continue;
            _tip = (voyage.Good(id)?.Name ?? "", tx + tile / 2, ty - 2);
            if (canvas.Pointer.Clicked) _cargoChosen = i;
        }
        if (cargo.Count == 0) canvas.Text("실은 교역품이 없다.", x + 26, y + 166, w - 52, 22, 14, Canvas.Dim);
        if (canvas.Button("적재화물파기", x + 20, y + h - 46, 130, 32, _cargoChosen >= 0, 14) && voyage.Good(cargo[_cargoChosen].Key) is { } dumped)
        {
            voyage.DumpGood(dumped);
            _cargoChosen = -1;
        }
        // 전용 — 고른 교역품을 물 · 식량 · 자재로 돌린다(돌릴 수 있는 것만 단추가 선다)
        if (_cargoChosen >= 0 && voyage.Good(cargo[_cargoChosen].Key) is { } turned && voyage.ConvertOf(turned) is { } into &&
            canvas.Button($"{Voyage.ConvertNames[into.Kind]}(으)로 전용 (×{into.Each})", x + 20, y + h - 82, 190, 30, true, 13))
        {
            voyage.ConvertGood(turned);
            _cargoChosen = -1;
        }
        canvas.Image("gm0:323", () => (_markParts ??= new UiParts(0)).Pixels(323), x + w - 250, y + h - 42, 20, 20);
        // 창고가 얼마나 찼는가 — 물자와 교역품을 더한 것(원본의 글 그대로: 「221/1634」, 올리면 「창고 (물자:182 교역품:39)」)
        canvas.Text($"{voyage.HoldUsed}/{voyage.TotalHold}", x + w - 226, y + h - 43, 100, 22, 15, voyage.HoldUsed > voyage.TotalHold ? new Color4(1f, 0.55f, 0.45f, 1) : Canvas.White, 2);
        canvas.Fill(x + w - 250, y + h - 18, 124, 4, new Color4(0.03f, 0.05f, 0.16f, 0.9f));
        canvas.Fill(x + w - 250, y + h - 18, 124 * Math.Clamp(voyage.HoldUsed / (float)Math.Max(1, voyage.TotalHold), 0, 1), 4, new Color4(0.3f, 0.85f, 0.9f, 1));
        if (canvas.Hover(x + w - 250, y + h - 46, 130, 34)) _tip = ($"창고 (물자:{voyage.StoresCount} 교역품:{voyage.CargoCount})", x + w - 186, y + h - 46);
        if (canvas.Button("이전", x + w - 110, y + h - 46, 90, 32)) voyage.Dialog = Dialog.None;
    }

    private long _itemClicked;
    private int _methodChosen, _contentChosen, _contentTop;
    private string _contentSearch = "";
    private bool _contentSearching;
    private (int Count, int Visible) _contentRows;

    /// <summary>
    /// 커스텀설정 조선의 둘째 화면 — 원본의 짜임: 왼쪽 「강화 방법」(보통 강화 · 옵션 스킬 부여 · 전용함 스킬 부여), 오른쪽 「강화 내용」.
    /// 「다음」은 재료를 고르는 창으로 간다(옵션 스킬을 골랐으면 그 스킬의 재료 둘이 미리 골라져 있다).
    /// 옵션 스킬은 배마다 붙일 수 있는 것이 정해져 있다 — 배 상세(ssjoy)를 모은 배는 그 스킬만 나오고 원본 재료가 함께 보인다.
    /// 전용함 스킬 부여: 어느 배에나 하나 — 전용함 건조 허가증이 든다(재료 조합은 자료가 없어 안 따진다). 이미 있으면 바꿔 단다.
    /// </summary>
    private void WorkMethodWindow()
    {
        const float w = 860, h = 460, listWidth = 360;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        void Header(string text, float hx, float hw)
        {
            canvas.Fill(hx, y + 16, hw, 24, new Color4(0.72f, 0.76f, 0.84f, 0.92f));
            canvas.Text(text, hx, y + 17, hw, 22, 15, new Color4(0.05f, 0.08f, 0.2f, 1), 1, true, false);
        }
        bool Row(float rx, float ry, float rw, bool chosen, bool enabled, int skillIcon, string title, string under)
        {
            bool hover = enabled && canvas.Hover(rx, ry, rw, 50);
            if (chosen) canvas.Fill(rx, ry, rw, 50, new Color4(0.12f, 0.62f, 0.55f, 0.95f));
            else if (hover) canvas.Fill(rx, ry, rw, 50, new Color4(0.2f, 0.3f, 0.6f, 0.6f));
            if (skillIcon > 0) SkillIcon(skillIcon, rx + 10, ry + 8, enabled ? 1 : 0.4f);
            else canvas.Image("gm0:301", () => (_markParts ??= new UiParts(0)).Pixels(301), rx + 8, ry + 9, 32, 32);
            canvas.Text(title, rx + 52, ry + 3, rw - 60, 22, 16, enabled ? Canvas.White : Canvas.Dim);
            if (under.Length > 58) under = under[..57] + "…";      // 더 길면 줄이 넘어가 아랫줄과 겹친다
            canvas.Text(under, rx + 52, ry + 26, rw - 60, 20, under.Length > 44 ? 10 : under.Length > 36 ? 11.5f : 13, Canvas.Dim);      // 긴 줄은 작게 — 두 줄로 넘어가 아랫줄과 겹치지 않게
            return hover && canvas.Pointer.Clicked;
        }
        canvas.Text($"{voyage.Ship.Name}     강화 {voyage.Work.Times}/{voyage.MaxTimesOf(voyage.Ship)}     {voyage.ShipbuildingLine}", x + 20, y + h - 44, 520, 22, 14, Canvas.Gold);
        Header("강화 방법", x + 20, listWidth - 20);
        int craft = voyage.Data.SkillRules.Find(r => r.Effect == "Shipbuilding")?.SkillId ?? 0;
        string[] methods = ["보통 강화", "옵션 스킬 부여", "전용함 스킬 부여"];
        for (int i = 0; i < methods.Length; i++)
            if (Row(x + 20, y + 48 + i * 54, listWidth - 20, _methodChosen == i, true, i < 2 ? craft : 0, methods[i], i == 0 ? "조선 1" : i == 1 ? "재료의 조합으로 붙는다"
                    : voyage.Work.Dedicated > 0 ? $"지금 {voyage.OptionName(voyage.Work.Dedicated)} — 바꿔 단다(허가증 {Voyage.PermitsFor(voyage.Ship)}장)" : $"전용함 건조 허가증 {Voyage.PermitsFor(voyage.Ship)}장 · 한 척에 하나"))
                (_methodChosen, _contentChosen) = (i, 0);
        canvas.Line(x + listWidth + 14, y + 14, x + listWidth + 14, y + h - 60, new Color4(0.5f, 0.55f, 0.75f, 0.8f), 1);

        float rx0 = x + listWidth + 30, rw0 = w - listWidth - 50;
        Header("강화 내용", rx0, _methodChosen == 0 ? rw0 : rw0 - 216);
        // 스킬 이름으로 찾기 — 옵션 스킬 · 전용함 스킬 목록에서
        if (_methodChosen != 0)
        {
            float fx = rx0 + rw0 - 210, fw = 210;
            bool overFind = canvas.Hover(fx, y + 16, fw, 24);
            if (canvas.Pointer.Clicked) _contentSearching = overFind;
            canvas.Fill(fx, y + 16, fw, 24, new Color4(0.02f, 0.04f, 0.14f, 0.95f));
            canvas.Frame(fx, y + 16, fw, 24, _contentSearching ? Canvas.Gold : Canvas.PanelEdge, 1.2f);
            canvas.Text(_contentSearch == "" && !_contentSearching ? "🔍 스킬 이름으로 찾기" : _contentSearch + (_contentSearching && (int)(voyage.Clock * 2) % 2 == 0 ? "|" : ""), fx + 6, y + 18, fw - 30, 20, 14, _contentSearch == "" && !_contentSearching ? Canvas.Dim : Canvas.White);
            if (_contentSearch != "")
            {
                canvas.Text("✕", fx + fw - 22, y + 18, 20, 20, 13, Canvas.Dim, 1);
                if (canvas.Hover(fx + fw - 22, y + 16, 22, 24) && canvas.Pointer.Clicked) (_contentSearch, _contentTop, _contentChosen, _contentSearching) = ("", 0, 0, false);
            }
        }
        else _contentSearching = false;
        var book = voyage.Data.ShipWorks;
        (string Stat, string Name)[] plain = [("Sail", "돛 강화"), ("Turn", "조타강화"), ("Wave", "내파 강화"), ("Durability", "선체 강화"), ("Hold", "선창 강화")];
        // 이 배에 붙일 수 있는 스킬만 — 배 상세가 없는 배는 전부 보인다
        var detail = voyage.Data.ShipDetail(voyage.Ship.Name);
        var options = _methodChosen == 2 ? voyage.DedicatedSkills() : voyage.Data.OptionSkills.Where(voyage.ShipAllows).ToList();
        if (_methodChosen != 0 && _contentSearch != "") options = options.Where(o => o.Name.Contains(_contentSearch, StringComparison.OrdinalIgnoreCase)).ToList();
        string? Original(string name) => detail?.Skills.Find(s => s.Name == name) is { Parts.Count: > 0 } real ? string.Join(" + ", real.Parts)
            : voyage.Data.ShipCombos.Where(c => c.Ship == voyage.Ship.Name && c.Skill == name).OrderBy(c => c.Parts.Count).FirstOrDefault() is { } told ? string.Join(" + ", told.Parts) : null;      // 배 상세에 없으면 gvdb 의 보고 가운데 가장 짧은 조합
        // 이 배에 붙는 옵션 스킬의 목록이 없으면 전부 보인다 — 그 사실을 알리고 요청 딱지를 단다
        if (_methodChosen == 1 && (detail is not { Skills.Count: > 0 } || detail.Borrowed != ""))
        {
            // 아랫줄(배 이름 줄)의 오른쪽에 — 목록 위에 두면 마지막 줄과 겹친다
            canvas.Text(detail is { Skills.Count: > 0 } ? $"{detail.Borrowed}의 목록" : "목록 없음 · 전부 보인다", x + 318, y + h - 62, 200, 16, 11, new Color4(1f, 0.75f, 0.45f, 1));
            Invented("shipskill", voyage.Ship.Id, voyage.Ship.Name, x + 318, y + h - 44);
        }
        int count = _methodChosen == 0 ? plain.Length : options.Count;
        _contentRows = (count, 7);
        _contentTop = Math.Clamp(_contentTop, 0, Math.Max(0, count - 7));
        if (count > 7) canvas.Text($"{_contentTop + 1} ~ {Math.Min(count, _contentTop + 7)} / {count}", rx0 + 6, y + 20, 120, 18, 11, new Color4(0.05f, 0.08f, 0.2f, 1), 0, false, false);      // 머리 줄 왼쪽에 — 아래에 두면 마지막 줄의 글과 겹친다
        _contentChosen = Math.Clamp(_contentChosen, 0, Math.Max(0, count - 1));
        for (int i = _methodChosen == 0 ? 0 : _contentTop; i < Math.Min(count, _contentTop + 7); i++)
        {
            bool click = _methodChosen == 0
                ? Row(rx0, y + 48 + (i - (_methodChosen == 0 ? 0 : _contentTop)) * 52, rw0, _contentChosen == i, true, craft, plain[i].Name, "조선 1 · 재료 " + string.Join(", ", book.Parts.Where(p => p.Stat == plain[i].Stat).Take(3).Select(p => p.Name)))
                : Row(rx0, y + 48 + (i - (_methodChosen == 0 ? 0 : _contentTop)) * 52, rw0, _contentChosen == i, true, options[i].SkillId, options[i].Name,
                      _methodChosen == 2 ? voyage.OptionNeedLine(options[i]) + (options[i].Effect == "" ? " · 효과 없음" : "")
                      : (Original(options[i].Name) ?? string.Join(" + ", new[] { options[i].PartA, options[i].PartB }.Select(id => book.Parts.Find(p => p.Id == id)?.Name ?? "?")))
                        + (voyage.OptionNeedLine(options[i]) is { Length: > 0 } needs ? " · " + needs : "") + (options[i].Effect == "" ? " · 효과 없음" : ""));
            if (click) _contentChosen = i;
            // 선박 스킬 줄에 마우스를 올리면 설명이 뜬다 — 원본의 스킬 설명 글, 이 게임에서의 효과, 재료 · 유효조건
            float tipY = y + 48 + (i - _contentTop) * 52;
            if (_methodChosen != 0 && canvas.Hover(rx0, tipY, rw0, 50))
            {
                var option = options[i];
                string about = (voyage.Data.Skills.Find(s => s.Id == option.SkillId)?.Description ?? voyage.Data.SkillNotes.GetValueOrDefault(option.SkillId) ?? "").Replace("\r", "").Replace("\n", " ").Trim();
                var lines = new List<string> { option.Name };
                for (int from = 0; from < about.Length; from += 34) lines.Add(about.Substring(from, Math.Min(34, about.Length - from)));
                lines.Add(option.Effect == "" ? "이 게임에서는 아직 효과가 없다" : "이 게임에서: " + Voyage.OptionNote(option));
                string parts = string.Join(" + ", new[] { option.PartA, option.PartB }.Where(id => id > 0).Select(id => book.Parts.Find(p => p.Id == id)?.Name ?? "?"));
                if (parts != "") lines.Add("재료: " + parts);
                if (voyage.OptionNeedLine(option) is { Length: > 0 } needLine) lines.Add("유효조건: " + needLine);
                _tip = (string.Join("\n", lines), rx0 + rw0 / 2, tipY + 2);
            }
        }
        if (canvas.Button("돌아가기", x + w - 330, y + h - 50, 100, 34, true, 14)) voyage.Dialog = Dialog.SpecialBuild;
        if (_methodChosen == 2)
        {
            // 전용함 스킬은 재료 고르기 없이 여기서 바로 붙인다
            string? blocker = count > 0 ? voyage.DedicatedBlocker(options[_contentChosen]) : "붙일 수 있는 전용함 스킬이 없다";
            canvas.Text(blocker ?? $"가진 전용함 건조 허가증 {voyage.Items.GetValueOrDefault(Voyage.ShipPermit)}장", x + 20, y + h - 70, listWidth - 20, 20, 13, blocker != null ? new Color4(1f, 0.5f, 0.45f, 1) : Canvas.Dim);
            if (canvas.Button("부여", x + w - 224, y + h - 50, 100, 34, blocker == null, 14)) voyage.GiveDedicated(options[_contentChosen]);
        }
        else if (_methodChosen == 1 && count > 0 && voyage.RealCombo(options[_contentChosen]) is { } combo)
        {
            // 진짜 재료 조합을 아는 스킬 — 그 조빌 아이템들을 가지고 있으면 여기서 바로 붙인다(재료가 든다)
            string? blocker = voyage.ComboBlocker(options[_contentChosen]);
            canvas.Text(blocker ?? "재료: " + string.Join(" · ", combo.Select(voyage.ItemName)), x + 20, y + h - 70, listWidth + 180, 20, 12, blocker != null ? new Color4(1f, 0.5f, 0.45f, 1) : Canvas.Dim);
            if (canvas.Button("부여", x + w - 224, y + h - 50, 100, 34, blocker == null, 14)) voyage.GrantByCombo(options[_contentChosen]);
        }
        else if (canvas.Button("다음 ▶", x + w - 224, y + h - 50, 100, 34, count > 0, 14))
        {
            _workPicked.Clear();
            // 고른 내용의 재료를 미리 골라 둔다 — 보통 강화는 그 능력치의 재료 둘, 옵션 스킬은 그 조합
            if (_methodChosen == 0) _workPicked.AddRange(book.Parts.Where(p => p.Stat == plain[_contentChosen].Stat).Take(2).Select(p => p.Id));
            else _workPicked.AddRange([options[_contentChosen].PartA, options[_contentChosen].PartB]);
            // 새 강화 창은 가진 조선 재료로 한다 — 고른 옵션 스킬의 재료를 가지고 있으면 칸에 미리 넣어 둔다
            _workItems.Clear();
            (_workPage, _workLoad) = (0, null);
            if (_methodChosen == 1)
                foreach (int item in voyage.BuildComboOf(options[_contentChosen]))      // 그 배의 진짜 조합(배 상세)의 재료를, 가진 것만
                    if (voyage.BuildSlotFor(_workItems, item) >= 0) _workItems.Add(item);
            (voyage.Dialog, _workSkillOnly, _workWood, _workBack) = (Dialog.Strengthen, _methodChosen == 1, 0, Dialog.WorkMethod);
        }
        if (canvas.Button("취소", x + w - 118, y + h - 50, 98, 34, true, 14)) voyage.Dialog = Dialog.None;
    }

    private int _learnChosen, _learnTop;
    private (int Count, int Visible) _learnRows;

    /// <summary>
    /// 스킬습득 — 원본의 짜임: 왼쪽 「스킬」(그림 · 이름 · 값), 오른쪽 「효과」와 「습득조건」, 비용과 소지금, 확인 · 이전.
    /// 목록에는 내 직업의 우대 스킬만 나온다. 이미 익힌 것은 흐리게.
    /// </summary>
    private void LearnWindow()
    {
        const float w = 760, h = 440, listWidth = 300;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
        canvas.Fill(0, 0, canvas.Width, 30, new Color4(0.55f, 0.70f, 0.90f, 0.9f));
        canvas.Text("스킬습득", 20, 3, 140, 24, 17, new Color4(0.05f, 0.08f, 0.2f, 1), 0, true, false);
        canvas.Text("습득할 스킬을 선택해 주십시오", 170, 5, 400, 22, 14, new Color4(0.05f, 0.08f, 0.2f, 1), 0, false, false);
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        void Header(string text, float hx, float hy, float hw)
        {
            canvas.Fill(hx, hy, hw, 24, new Color4(0.72f, 0.76f, 0.84f, 0.92f));
            canvas.Text(text, hx, hy + 1, hw, 22, 15, new Color4(0.05f, 0.08f, 0.2f, 1), 1, true, false);
        }
        Header("스킬", x + 20, y + 16, listWidth - 20);
        var skills = voyage.SkillsTaught();
        const int perPage = 7;
        _learnRows = (skills.Count, perPage);
        _learnTop = Math.Clamp(_learnTop, 0, Math.Max(0, skills.Count - perPage));
        _learnChosen = Math.Clamp(_learnChosen, 0, Math.Max(0, skills.Count - 1));
        float row = y + 48;
        for (int i = _learnTop; i < Math.Min(skills.Count, _learnTop + perPage); i++, row += 46)
        {
            bool known = voyage.Rank(skills[i].Id) > 0, chosen = i == _learnChosen, hover = canvas.Hover(x + 20, row, listWidth - 34, 44);
            if (chosen) canvas.Fill(x + 20, row, listWidth - 34, 44, new Color4(0.12f, 0.62f, 0.55f, 0.95f));
            else if (hover) canvas.Fill(x + 20, row, listWidth - 34, 44, new Color4(0.2f, 0.3f, 0.6f, 0.6f));
            if (hover && canvas.Pointer.Clicked) _learnChosen = i;
            SkillIcon(skills[i].Id, x + 26, row + 5, known ? 0.45f : 1);
            if (voyage.IsExpert(skills[i].Id) || voyage.IsFavored(skills[i].Id) || voyage.JobName == "")      // 별은 우대 · 전문 스킬에만(직업이 없으면 전처럼)
                canvas.Text("✦", x + 50, row + 1, 16, 16, 12, voyage.IsExpert(skills[i].Id) ? new Color4(1f, 0.3f, 0.45f, 1) : Canvas.Gold, 0, true);
            canvas.Text(skills[i].Name, x + 66, row + 2, listWidth - 110, 22, 16, known ? Canvas.Dim : Canvas.White);
            canvas.Text(known ? $"Rank {voyage.Rank(skills[i].Id)}" : $"{skills[i].Cost:N0} Ð", x + 66, row + 22, listWidth - 108, 20, 15, known ? Canvas.Dim : Canvas.White, 2);
        }
        if (skills.Count > perPage)
        {
            float barH = perPage * 46 - 2, thumb = Math.Max(24, barH * perPage / skills.Count), at = (barH - thumb) * _learnTop / Math.Max(1, skills.Count - perPage);
            canvas.Fill(x + listWidth - 10, y + 48, 8, barH, new Color4(0.03f, 0.05f, 0.16f, 0.9f));
            canvas.Fill(x + listWidth - 9, y + 48 + at, 6, thumb, new Color4(0.72f, 0.76f, 0.84f, 0.95f));
        }
        if (skills.Count == 0) canvas.Text("가르쳐 줄 스킬이 없다.", x + 30, y + 60, listWidth, 24, 15, Canvas.Dim);
        canvas.Text($"습득수 {voyage.Skills.Count}", x + 20, y + h - 40, listWidth - 34, 22, 14, Canvas.White, 2);
        canvas.Line(x + listWidth + 8, y + 14, x + listWidth + 8, y + h - 14, new Color4(0.5f, 0.55f, 0.75f, 0.8f), 1);

        float rx = x + listWidth + 24, rw = w - listWidth - 44;
        Header("효과", rx, y + 16, 110);
        canvas.Fill(rx, y + 48, rw, 110, new Color4(0.04f, 0.07f, 0.2f, 0.85f));
        Header("습득조건", rx, y + 176, 130);
        canvas.Text($"직업: {(voyage.JobName == "" ? "없음" : voyage.JobName)}", rx + 150, y + 178, rw - 150, 22, 14, Canvas.Dim, 2);
        if (skills.Count > 0)
        {
            var skill = skills[_learnChosen];
            bool known = voyage.Rank(skill.Id) > 0;
            canvas.Text(skill.Description, rx + 8, y + 54, rw - 16, 100, 15, Canvas.White);
            if (voyage.IsFavored(skill.Id)) canvas.Text("✦ 우대 스킬 : 습득조건면제", rx + 10, y + 208, rw - 20, 22, 15, Canvas.White);
            // 습득조건(레벨) — gvdb 의 값. 못 채운 것은 붉게, 우대 스킬이면 면제
            string needText = voyage.LearnNeedText(skill), needWhy = voyage.LearnNeed(skill) ?? "";
            if (needText != "")
                canvas.Text($"레벨: {needText}" + (voyage.IsFavored(skill.Id) ? "  (우대 스킬 — 면제)" : needWhy != "" ? $"  — {needWhy}" : ""), rx + 10, y + 256, rw - 20, 20, 13, needWhy != "" ? new Color4(1f, 0.55f, 0.5f, 1) : Canvas.White);
            if (voyage.Data.SkillRules.Find(r => r.SkillId == skill.Id) == null)
                canvas.Text("(이 게임에서는 아직 하는 일이 없다)", rx + 10, y + 234, rw - 20, 20, 13, Canvas.Dim);
            canvas.Text("비용", rx + rw - 230, y + h - 122, 80, 22, 15, Canvas.White);
            canvas.Text(known ? "익혔다" : $"{skill.Cost:N0} Ð", rx, y + h - 122, rw - 8, 22, 16, Canvas.White, 2);
            canvas.Image("gm0:311", () => (_markParts ??= new UiParts(0)).Pixels(311), rx + rw - 230, y + h - 96, 20, 20);
            canvas.Text($"{voyage.Money:N0} Ð", rx, y + h - 96, rw - 8, 22, 16, Canvas.White, 2);
            if (canvas.Button("확인", x + w - 224, y + h - 46, 100, 32, !known && voyage.Money >= skill.Cost && voyage.LearnNeed(skill) == null)) voyage.Learn(skill);
        }
        if (canvas.Button("이전", x + w - 118, y + h - 46, 98, 32)) voyage.Dialog = Dialog.None;
    }

    private int _jobTab, _jobTop, _jobChosen;
    private string _jobSkill = "";             // 스킬 이름으로 거르기 — 그 스킬이 우대 스킬인 직업만
    private bool _jobSearching;
    private (int Count, int Visible) _jobRows;

    /// <summary>
    /// 직업 일람 — 갈래(모험 · 교역 · 전투)마다 직업들, 고르면 그 직업의 우대 스킬(전문 스킬은 ★)과 전직 비용.
    /// 오른쪽 위 칸에 스킬 이름을 치거나 우대 스킬 칸을 누르면 그 스킬을 우대하는 직업만(갈래를 가리지 않고) 남는다.
    /// </summary>
    private void JobWindow()
    {
        string[] tabs = ["모험", "교역", "전투"];
        const float w = 860, h = 500, listWidth = 290;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2 + 10;
        canvas.Block(x, y - 30, w, h + 30);
        for (int i = 0; i < tabs.Length; i++)
        {
            bool on = _jobTab == i;
            canvas.Fill(x + i * 122, y - 30, 118, 30, on ? new Color4(0.16f, 0.62f, 0.72f, 0.98f) : new Color4(0.35f, 0.55f, 0.65f, 0.9f));
            canvas.Frame(x + i * 122, y - 30, 118, 30, Canvas.PanelEdge, 1);
            canvas.Text(tabs[i], x + i * 122, y - 27, 118, 24, 16, on ? Canvas.White : new Color4(0.05f, 0.1f, 0.2f, 1), 1, on, on);
            if (canvas.Hover(x + i * 122, y - 30, 118, 30) && canvas.Pointer.Clicked) (_jobTab, _jobTop, _jobChosen) = (i, 0, 0);
        }
        canvas.Panel(x, y, w, h);
        canvas.Text($"직업 일람     지금 직업: {(voyage.JobName == "" ? "없음" : voyage.JobName)}", x + 20, y + 10, 420, 26, 17, Canvas.Gold, 0, true);
        // 스킬로 거르기 — 치고 있는 글이 우대 스킬 이름에 든 직업만. 거르는 동안에는 세 갈래를 한꺼번에 본다
        float fx = x + w - 330, fw = 310;
        canvas.Text("스킬로 찾기", fx - 84, y + 13, 80, 20, 13, Canvas.Dim, 2);
        bool overFilter = canvas.Hover(fx, y + 10, fw, 24);
        if (canvas.Pointer.Clicked) _jobSearching = overFilter;
        canvas.Fill(fx, y + 10, fw, 24, new Color4(0.02f, 0.04f, 0.14f, 0.95f));
        canvas.Frame(fx, y + 10, fw, 24, _jobSearching ? Canvas.Gold : Canvas.PanelEdge, 1.2f);
        canvas.Text(_jobSkill == "" && !_jobSearching ? "🔍 스킬 이름 (우대 스킬 칸을 눌러도 된다)" : _jobSkill + (_jobSearching && (int)(voyage.Clock * 2) % 2 == 0 ? "|" : ""), fx + 6, y + 12, fw - 30, 20, 14, _jobSkill == "" && !_jobSearching ? Canvas.Dim : Canvas.White);
        if (_jobSkill != "")
        {
            canvas.Text("✕", fx + fw - 22, y + 12, 20, 20, 13, Canvas.Dim, 1);
            if (canvas.Hover(fx + fw - 22, y + 10, 22, 24) && canvas.Pointer.Clicked) (_jobSkill, _jobTop, _jobChosen, _jobSearching) = ("", 0, 0, false);
        }
        bool filtering = _jobSkill != "";
        bool Favours(Dho.Data.NamedData j) => voyage.Data.JobFacts.Find(f => f.Name == j.Name) is { } known && known.Skills.Exists(s => s.Contains(_jobSkill, StringComparison.OrdinalIgnoreCase));
        var jobs = voyage.Data.Jobs.Where(j => j.Name.Length > 0 && !j.Name.StartsWith('※') && (filtering ? Favours(j) : j.Group == _jobTab)).ToList();
        const int perPage = 16;
        _jobRows = (jobs.Count, perPage);
        _jobTop = Math.Clamp(_jobTop, 0, Math.Max(0, jobs.Count - perPage));
        _jobChosen = Math.Clamp(_jobChosen, 0, Math.Max(0, jobs.Count - 1));
        float row = y + 46;
        for (int i = _jobTop; i < Math.Min(jobs.Count, _jobTop + perPage); i++, row += 25)
        {
            bool chosen = i == _jobChosen, hover = canvas.Hover(x + 16, row, listWidth, 24);
            if (chosen) canvas.Fill(x + 16, row, listWidth, 24, new Color4(0.12f, 0.55f, 0.52f, 0.95f));
            else if (hover) canvas.Fill(x + 16, row, listWidth, 24, new Color4(0.2f, 0.3f, 0.6f, 0.6f));
            if (hover && canvas.Pointer.Clicked) _jobChosen = i;
            bool mine = jobs[i].Name == voyage.JobName;
            canvas.Text((mine ? "● " : "") + jobs[i].Name, x + 24, row + 2, listWidth - 12, 22, 14, mine ? Canvas.Gold : Canvas.White);
            if (filtering) canvas.Text(tabs[Math.Clamp(jobs[i].Group, 0, 2)], x + 24, row + 3, listWidth - 20, 20, 12, Canvas.Dim, 2);
        }
        if (jobs.Count > perPage) canvas.Text("휠로 굴린다", x + 20, y + h - 40, 200, 20, 12, Canvas.Dim);
        if (filtering && jobs.Count == 0) canvas.Text($"「{_jobSkill}」을(를) 우대하는 직업이 없다.", x + 24, y + 50, listWidth, 22, 14, Canvas.Dim);
        canvas.Line(x + listWidth + 26, y + 46, x + listWidth + 26, y + h - 60, new Color4(0.5f, 0.55f, 0.75f, 0.8f), 1);
        if (jobs.Count > 0)
        {
            var job = jobs[_jobChosen];
            float rx = x + listWidth + 40, rw = w - listWidth - 60;
            canvas.Text(job.Name, rx, y + 46, rw, 28, 20, Canvas.White, 0, true);
            var fact = voyage.Data.JobFacts.Find(f => f.Name == job.Name);
            if (fact == null) canvas.Text("이 직업의 우대 스킬 자료가 없다.", rx, y + 84, rw, 24, 15, Canvas.Dim);
            else
            {
                canvas.Text($"전직 비용  {fact.Cost:N0} 두캇", rx, y + 78, rw, 22, 15, Canvas.Gold);
                canvas.Fill(rx, y + 108, 130, 24, new Color4(0.72f, 0.76f, 0.84f, 0.92f));
                canvas.Text("우대 스킬", rx, y + 109, 130, 22, 15, new Color4(0.05f, 0.08f, 0.2f, 1), 1, true, false);
                canvas.Text("★ 전문 스킬 · 초록은 익힌 것", rx + 140, y + 111, rw - 140, 20, 12, Canvas.Dim);
                for (int k = 0; k < fact.Skills.Count; k++)
                {
                    string name = fact.Skills[k];
                    var skill = voyage.Data.Skills.Find(s => s.Name == name);
                    float sx = rx + k % 2 * (rw / 2), sy = y + 142 + k / 2 * 38;
                    bool overSkill = canvas.Hover(sx, sy, rw / 2 - 6, 34);
                    canvas.Fill(sx, sy, rw / 2 - 6, 34, overSkill ? new Color4(0.2f, 0.3f, 0.6f, 0.85f) : new Color4(0.04f, 0.07f, 0.2f, 0.85f));
                    if (overSkill && canvas.Pointer.Clicked) (_jobSkill, _jobTop, _jobChosen, _jobSearching) = (name, 0, 0, false);     // 이 스킬을 우대하는 직업들로 거른다
                    if (skill != null) SkillIcon(skill.Id, sx + 4, sy, 1);
                    bool known = skill != null && voyage.Rank(skill.Id) > 0;
                    canvas.Text((name == fact.Expert ? "★ " : "") + name + (known ? $"  R{voyage.Rank(skill!.Id)}" : ""), sx + 40, sy + 6, rw / 2 - 50, 22, 15,
                                known ? new Color4(0.55f, 1f, 0.6f, 1) : name == fact.Expert ? Canvas.Gold : Canvas.White);
                }
            }
            canvas.Text("전직은 그 직업의 전직증을 써서 한다(소지품 → 아이템 추가).", rx, y + h - 40, rw - 130, 20, 12, Canvas.Dim);
        }
        if (canvas.Button("이전", x + w - 130, y + h - 46, 110, 32)) voyage.Dialog = Dialog.None;
    }

    /// <summary>캐릭터 정보 창이 내 모습을 어떻게 보일지 — 도는 각, 얼굴 확대, 그리고 미리보기 줄에 없는 손 · 모자. 창이 프레임마다 넣는다.</summary>
    public (float Yaw, bool Face, int Hand, int Cap)? FigureView;
    // 미리보기가 내 모습인가 — 그러면 입은 장비도 입혀 보인다(장비 물품 · 캐릭터 정보 창)
    public bool FigureMine;
    private float _figureYaw = 0.35f;
    private bool _figureFace;

    /// <summary>
    /// 캐릭터 정보(C) — 원본의 짜임: 왼쪽에 내 모습(얼굴 확대 · 회전), 오른쪽에 이름 · 직업 · 작위,
    /// 상태(모험 · 교역 · 전투 레벨과 다음 레벨까지, 명성), 피로, 자산.
    /// </summary>
    private void CharacterWindow()
    {
        const float w = 480, h = 430, side = 300;
        float x = (canvas.Width - w - side) / 2 + side, y = (canvas.Height - h) / 2 - 20;
        canvas.Panel(x - side, y, w + side, h);
        canvas.Block(x - side, y, w + side, h);
        canvas.Line(x, y + 16, x, y + h - 16, new Color4(0.5f, 0.55f, 0.75f, 0.8f), 1);
        // 왼쪽 칸: 내 모습. 단추는 원본처럼 모습의 오른쪽 위에 둔다
        var looks = voyage.Looks;
        FigurePreview = (x - side + 10, y + 14, 180, h - 74, looks[0], looks[1], looks[2], looks[3], looks[4]);
        FigureView = (_figureYaw, _figureFace, looks[5], looks[6]);
        FigureMine = true;
        if (canvas.Button((_figureFace ? "☑" : "☐") + " 얼굴 확대", x - 104, y + 16, 94, 28, true, 13)) _figureFace = !_figureFace;
        if (canvas.Button("회전 ▶", x - 104, y + 50, 94, 28, true, 13)) _figureYaw += MathF.PI / 4;
        if (canvas.Button("의상 · 외관", x - side + 20, y + h - 46, 130, 32, true, 14)) voyage.Dialog = Dialog.Outfit;
        void Header(string text, float hy)
        {
            canvas.Fill(x + 20, hy, 110, 24, new Color4(0.72f, 0.76f, 0.84f, 0.92f));
            canvas.Text(text, x + 20, hy + 1, 110, 22, 15, new Color4(0.05f, 0.08f, 0.2f, 1), 1, true, false);
        }
        void Cell(int icon, string label, string value, float cx, float cy, float cw)
        {
            canvas.Fill(cx, cy, cw, 26, new Color4(0.04f, 0.07f, 0.2f, 0.85f));
            bool drawn = icon >= 0 && canvas.Image($"gm0:{icon}", () => (_markParts ??= new UiParts(0)).Pixels(icon), cx + 3, cy + 3, 20, 20);
            canvas.Text(label, cx + (drawn ? 27 : 8), cy + 3, cw, 20, 14, Canvas.Dim);
            canvas.Text(value, cx, cy + 2, cw - 8, 22, 15, Canvas.White, 2);
        }
        canvas.Text(voyage.PlayerName, x + 20, y + 12, w - 160, 28, 20, Canvas.White, 0, true);
        // 내건 호칭 — 이름 옆에. 누르면 호칭 창
        if (canvas.Button(voyage.HonorName == "" ? "호칭" : voyage.HonorName, x + 30 + voyage.PlayerName.Length * 20, y + 13, voyage.HonorName == "" ? 60 : 150, 24, true, 12)) voyage.Dialog = Dialog.Honors;
        canvas.Text(voyage.NationName, x + 20, y + 14, w - 40, 24, 15, Canvas.Gold, 2);
        canvas.Text(voyage.JobName == "" ? "직업 없음" : voyage.JobName, x + 22, y + 44, w - 44, 22, 15, Canvas.White);
        canvas.Text($"작위  {voyage.TitleName}     공적 {voyage.Merit} / {voyage.MeritToNext}", x + 22, y + 68, w - 44, 22, 14, Canvas.White);
        canvas.Text(voyage.Major == "" ? "전공 없음" : $"전공  {voyage.Major}     학점 {voyage.Credits:N0}", x + 22, y + 90, w - 44, 22, 14, Canvas.Dim);

        Header("상태", y + 122);
        (string Name, int Icon, int Exp, int Fame)[] kinds = [("모험", 268, voyage.AdventureExp, voyage.AdventureFame), ("교역", 269, voyage.TradeExp, voyage.TradeFame), ("전투", 270, voyage.BattleExp, voyage.BattleFame)];
        for (int i = 0; i < kinds.Length; i++)
        {
            var (level, next) = Voyage.LevelOf(kinds[i].Exp);
            float cy = y + 152 + i * 30;
            Cell(kinds[i].Icon, $"Lv {kinds[i].Name}", $"{level}", x + 20, cy, 130);
            Cell(-1, "Next", $"{next:N0}", x + 154, cy, 140);
            Cell(245, "", $"{kinds[i].Fame:N0}", x + 298, cy, 162);
        }
        Cell(232, "피로", $"{voyage.Fatigue:0} / 100", x + 20, y + 246, 200);
        Cell(270, "생명력", $"{voyage.Life:0} / {voyage.MaxLife}", x + 226, y + 246, 234);
        canvas.Text($"공격력 {voyage.LandAttack} · 방어력 {voyage.LandDefense}", x + 226, y + 272, 234, 16, 11, Canvas.Dim, 2);
        if (voyage.PrayerNote != "") canvas.Text(voyage.PrayerNote, x + 22, y + 109, w - 44, 12, 10, new Color4(0.7f, 0.9f, 1f, 1));
        // 악명과 타국 적대도 — 있을 때만(낱말은 원본의 것, 값의 오르내림은 지은 것)
        if (voyage.Infamy > 0 || voyage.Hostility.Count > 0)
            canvas.Text($"악명 {voyage.Infamy}" + string.Concat(voyage.HostilityList().Take(2).Select(h => $"   {h.Name} 적대도 {h.Value}{(h.Hunts ? "!" : "")}")), x + 150, y + 92, w - 172, 18, 12, new Color4(1f, 0.7f, 0.5f, 1), 2);
        canvas.Fill(x + 20, y + 274, 200, 4, new Color4(0.03f, 0.05f, 0.16f, 0.9f));
        canvas.Fill(x + 20, y + 274, 200 * (float)Math.Clamp(voyage.Fatigue / 100, 0, 1), 4, new Color4(0.85f, 0.4f, 0.9f, 1));

        Header("자산", y + 290);
        Cell(311, "소지금", $"{voyage.Money:N0} Ð", x + 20, y + 320, 250);
        Cell(272, "은행", $"{voyage.Savings:N0} Ð", x + 20, y + 350, 250);
        Cell(301, "", $"배 {voyage.Dock.Count + 1}척", x + 276, y + 320, 184);
        Cell(323, "", $"보관함 {voyage.Vault.Count}", x + 276, y + 350, 184);

        if (canvas.Button("장비 물품", x + 20, y + h - 46, 110, 32, true, 14)) (voyage.Dialog, _equipTop) = (Dialog.Equip, 0);
        if (canvas.Button("스킬", x + 136, y + h - 46, 80, 32, true, 14)) voyage.Dialog = Dialog.Skills;
        if (canvas.Button("직업 일람", x + 222, y + h - 46, 96, 32, true, 14)) voyage.Dialog = Dialog.Jobs;
        if (canvas.Button($"발견물 {voyage.Found.Count}", x - 280, y + h - 46, 130, 32, true, 13)) voyage.Dialog = Dialog.FoundList;
        if (canvas.Button("이전", x + w - 130, y + h - 46, 110, 32)) voyage.Dialog = Dialog.None;
    }

    private int _majorTop, _majorChosen = -1, _itemChosen = -1;
    private (int Count, int Visible) _majorRows;

    /// <summary>대학 — 왼쪽에서 전공을 고르고, 오른쪽에서 그 전공의 연구를 시작한다.</summary>
    private void UniversityWindow()
    {
        const float w = 980, h = 520, listWidth = 250;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text($"대학     전공: {(voyage.Major == "" ? "없음" : voyage.Major)}     학점 {voyage.Credits:N0}     마친 연구 {voyage.StudyDone.Count}", x + 20, y + 12, w - 40, 30, 19, Canvas.Gold, 0, true);
        var majors = voyage.Majors();
        if (majors.Count == 0)
        {
            canvas.Text("연구 목록이 없다(data\\extracted\\research-facts.json).", x + 20, y + 60, w - 40, 24, 15, Canvas.Dim);
            if (canvas.Button("이전", x + w - 140, y + h - 50, 120, 34)) voyage.Dialog = Dialog.None;
            return;
        }
        if (_majorChosen < 0) _majorChosen = Math.Max(0, majors.IndexOf(voyage.Major));
        const int perPage = 15;
        _majorRows = (majors.Count, perPage);
        _majorTop = Math.Clamp(_majorTop, 0, Math.Max(0, majors.Count - perPage));
        _majorChosen = Math.Clamp(_majorChosen, 0, majors.Count - 1);
        canvas.Text("전공 (휠로 굴린다)", x + 20, y + 50, listWidth, 20, 13, Canvas.Dim);
        float row = y + 74;
        for (int i = _majorTop; i < Math.Min(majors.Count, _majorTop + perPage); i++, row += 26)
        {
            bool chosen = i == _majorChosen, hover = canvas.Hover(x + 16, row, listWidth, 25);
            if (chosen) canvas.Fill(x + 16, row, listWidth, 25, new Color4(0.12f, 0.55f, 0.52f, 0.95f));
            else if (hover) canvas.Fill(x + 16, row, listWidth, 25, new Color4(0.2f, 0.3f, 0.6f, 0.6f));
            if (hover && canvas.Pointer.Clicked) _majorChosen = i;
            canvas.Text((majors[i] == voyage.Major ? "● " : "") + majors[i], x + 24, row + 2, listWidth - 12, 22, 14, majors[i] == voyage.Major ? Canvas.Gold : Canvas.White);
        }
        canvas.Line(x + listWidth + 26, y + 50, x + listWidth + 26, y + h - 60, new Color4(0.5f, 0.55f, 0.75f, 0.8f), 1);

        float rx = x + listWidth + 40, rw = w - listWidth - 60;
        string major = majors[_majorChosen];
        canvas.Text(major, rx, y + 50, 300, 26, 18, Canvas.White, 0, true);
        if (major != voyage.Major && canvas.Button("이 전공으로 정한다", rx + rw - 180, y + 48, 180, 30, true, 14)) voyage.ChooseMajor(major);
        row = y + 88;
        foreach (var research in voyage.ResearchOf(major).Take(4))
        {
            bool done = voyage.StudyDone.Contains(research.No), now = voyage.Studying == research, can = voyage.CanStudy(research);
            canvas.Fill(rx, row, rw, 86, new Color4(0.04f, 0.07f, 0.2f, 0.85f));
            canvas.Text($"{research.Name}", rx + 10, row + 4, rw - 200, 24, 16, can ? Canvas.White : Canvas.Dim, 0, true);
            canvas.Text($"연구동 Lv {research.Level} · 학점 {research.Credit:N0}" + (research.Job != "" ? $" · {research.Job}" : ""), rx + 10, row + 4, rw - 20, 22, 13, Canvas.Dim, 2);
            string acts = string.Join("   ", research.Actions.Select(a => $"{a.Name} {(now ? voyage.StudyProgress.GetValueOrDefault(a.Name) + "/" : "")}{a.Count}"));
            canvas.Text(acts, rx + 10, row + 30, rw - 20, 22, 14, now ? Canvas.Gold : Canvas.White);
            canvas.Text($"얻는 스킬: {research.Skill}" + (Voyage.StudyNote(research.Skill) is { } effect ? $"  —  {effect}" : research.Skill == "" ? "" : "  (효과 없음)"), rx + 10, row + 56, rw - 190, 22, 13, Voyage.StudyNote(research.Skill) != null ? new Color4(0.5f, 1f, 0.6f, 1) : Canvas.Dim);
            if (done) canvas.Text("마쳤다", rx + rw - 170, row + 56, 160, 22, 14, new Color4(0.55f, 1f, 0.6f, 1), 2);
            else if (now) canvas.Text("연구 중", rx + rw - 170, row + 56, 160, 22, 14, Canvas.Gold, 2);
            else if (!can) canvas.Text("이 게임에 없는 행동이 든다", rx + rw - 230, row + 56, 220, 22, 13, Canvas.Dim, 2);
            else if (canvas.Button("연구 시작", rx + rw - 130, row + 52, 120, 28, major == voyage.Major, 13)) voyage.StartResearch(research);
            row += 92;
        }
        canvas.Text("연구가 바라는 행동을 하면 진행된다. 얻은 스킬 가운데 효과가 정해진 것은 초록 글로 보인다(크기는 임시 값).", x + 20, y + h - 44, w - 180, 22, 13, Canvas.Dim);
        if (canvas.Button("이전", x + w - 140, y + h - 50, 120, 34)) voyage.Dialog = Dialog.None;
    }

    /// <summary>은행 보관함 — 왼쪽이 가진 것, 오른쪽이 맡긴 것. 하나씩 또는 전부 옮긴다.</summary>
    private void VaultWindow()
    {
        const float w = 820, h = 470;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text($"은행 보관함     {voyage.Vault.Count}/{Voyage.VaultSlots}칸", x + 20, y + 12, w - 40, 30, 20, Canvas.Gold, 0, true);
        void Side(string title, Dictionary<int, int> items, float sx, int sign, ref int page)
        {
            const int perPage = 9;
            canvas.Text(title, sx, y + 50, 300, 22, 15, Canvas.Gold, 0, true);
            var list = items.OrderBy(i => i.Key).ToList();
            int pages = Math.Max(1, (list.Count + perPage - 1) / perPage);
            page = Math.Clamp(page, 0, pages - 1);
            float row = y + 78;
            foreach (var (id, count) in list.Skip(page * perPage).Take(perPage))
            {
                canvas.Image($"sb{id}", () => (_itemIcons ??= new ImageSet(@"0010\0001\sb")).Pixels(15, id), sx, row, 28, 28);
                canvas.Text($"{voyage.ItemName(id)} × {count}", sx + 34, row + 4, 190, 24, 15, Canvas.White);
                if (canvas.Button(sign > 0 ? "1개 ▶" : "◀ 1개", sx + 228, row + 1, 70, 26, true, 12)) voyage.Stash(id, sign);
                if (canvas.Button("전부", sx + 302, row + 1, 60, 26, true, 12)) voyage.Stash(id, sign * count);
                row += 34;
            }
            if (list.Count == 0) canvas.Text(sign > 0 ? "가진 아이템이 없다." : "맡긴 아이템이 없다.", sx, row + 4, 300, 24, 15, Canvas.Dim);
            if (pages > 1)
            {
                if (canvas.Button("◀", sx, y + h - 50, 36, 30, page > 0)) page--;
                canvas.Text($"{page + 1}/{pages}", sx + 40, y + h - 45, 50, 22, 14, Canvas.White, 1);
                if (canvas.Button("▶", sx + 94, y + h - 50, 36, 30, page < pages - 1)) page++;
            }
        }
        Side("소지품", voyage.Items, x + 24, +1, ref _vaultPage.Mine);
        canvas.Line(x + w / 2, y + 48, x + w / 2, y + h - 60, new Color4(0.5f, 0.55f, 0.75f, 0.8f), 1);
        Side("보관함", voyage.Vault, x + w / 2 + 20, -1, ref _vaultPage.Kept);
        if (canvas.Button("이전", x + w - 140, y + h - 50, 120, 34)) voyage.Dialog = Dialog.Bank;
    }
    private (int Mine, int Kept) _vaultPage;

    /// <summary>주점 — 한턱내기, 선원 모집, 부관.</summary>
    private void TavernWindow()
    {
        const float w = 620, h = 460;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text($"주점     소지금 {voyage.Money:N0} Ð", x + 20, y + 12, w - 40, 30, 20, Canvas.Gold, 0, true);
        canvas.Text("주점 주인\n어서 오게. 한잔하겠나, 사람을 찾나?", x + 20, y + 52, w - 40, 50, 16, Canvas.White);
        void Row(float row, string label, string state, string button, bool enabled, Action run)
        {
            canvas.Fill(x + 16, row, w - 32, 44, new Color4(0.04f, 0.07f, 0.2f, 0.85f));
            canvas.Text(label, x + 28, row + 10, 150, 24, 17, Canvas.White, 0, true);
            canvas.Text(state, x + 170, row + 12, 230, 22, 14, Canvas.Dim);
            if (canvas.Button(button, x + w - 216, row + 6, 190, 32, enabled, 14)) run();
        }
        Row(y + 112, "한턱내기", $"피로 {voyage.Fatigue:0}", $"한턱낸다 ({voyage.TreatCost:N0})", voyage.Fatigue > 0 && voyage.Money >= voyage.TreatCost, voyage.Treat);
        Row(y + 162, "선원 모집", $"선원 {voyage.Crew:0} / {voyage.Stats.MaxCrew}", "선원을 모집한다", voyage.Crew < voyage.Stats.MaxCrew, () => voyage.Dialog = Dialog.Recruit);
        if (canvas.Button("5명 해고", x + w - 316, y + 168, 94, 32, voyage.Crew > voyage.Stats.MinCrew, 13)) voyage.DismissCrew(5);
        Row(y + 212, "부관", $"고용 {voyage.Aides.Count}/{Voyage.AideSlots} · 후보 {voyage.AidesToHire().Count}명", "부관을 만난다", true, () => voyage.Dialog = Dialog.Aides);
        Row(y + 262, "주문", $"행동력 {voyage.Vigour:0} / {voyage.MaxVigour}", "차림을 본다", voyage.Data.TavernMenu.Count > 0, () => voyage.Dialog = Dialog.TavernMenu);
        // 애완동물 — 이름은 원본의 것, 들이는 곳과 값은 지은 것(요청 딱지)
        Row(y + 312, "애완동물", voyage.HasPet ? $"{voyage.PetName} · {voyage.Text(8120, "친밀도")} {voyage.PetLove}" : "없음", $"{(voyage.HasPet ? "바꾼다" : "들인다")} ({Voyage.PetPrice:N0})", voyage.Money >= Voyage.PetPrice, voyage.BuyPet);
        Invented("pet", 1, "애완동물을 얻는 법과 효과", x + 300, y + 322);
        if (canvas.Button("닫기", x + w - 140, y + h - 50, 120, 34)) voyage.Dialog = Dialog.None;
    }

    /// <summary>호칭 — 얻을 수 있는 호칭의 조건과 설명, 얻은 것 가운데 하나를 내건다.</summary>
    private void HonorWindow()
    {
        const float w = 820, h = 560;
        float x = (canvas.Width - w) / 2, y = Math.Max(14, (canvas.Height - h) / 2);
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text("호칭", x + 20, y + 12, 200, 28, 19, Canvas.Gold, 0, true);
        canvas.Text($"해적 {voyage.PirateWins}척 · 군함 {voyage.NavyWins}척을 물리쳤다     이름만 아는 호칭이 {voyage.Data.Honors.Count}가지 — 여기에는 이 게임에서 얻을 수 있는 것만", x + 80, y + 17, w - 100, 20, 11, Canvas.Dim);
        float row = y + 48;
        foreach (var (honor, need, earned) in voyage.Honors())
        {
            bool on = voyage.Honor == honor.Id;
            canvas.Fill(x + 16, row, w - 32, 30, on ? new Color4(0.12f, 0.45f, 0.45f, 0.8f) : new Color4(0.04f, 0.07f, 0.2f, 0.85f));
            canvas.Text(honor.Name, x + 24, row + 5, 150, 20, 14, earned ? Canvas.White : Canvas.Dim, 0, earned);
            canvas.Text(need + (Voyage.HonorNote(honor.Id) is { Length: > 0 } effect ? $"  →  {effect}" : ""), x + 176, row + 2, w - 176 - 120, 16, 11, earned ? new Color4(0.5f, 1f, 0.6f, 1) : Canvas.Gold);
            canvas.Text(honor.Extra.Length > 66 ? honor.Extra[..65] + "…" : honor.Extra, x + 176, row + 15, w - 176 - 120, 14, 9.5f, Canvas.Dim);
            if (canvas.Hover(x + 176, row, w - 176 - 120, 30)) _itemTip = (honor.Name, honor.Extra, x + 176, Math.Min(row + 31, canvas.Height - 110), 420, 40 + MathF.Ceiling(honor.Extra.Length / 28f) * 19);
            if (canvas.Button(on ? "내린다" : "내건다", x + w - 110, row + 3, 86, 24, earned || on, 12)) voyage.WearHonor(on ? 0 : honor.Id);
            row += 32;
        }
        if (canvas.Button("이전", x + w - 130, y + h - 46, 110, 32)) voyage.Dialog = Dialog.Character;
    }

    /// <summary>대장간 — 입은 장비의 공격력 · 방어력 단련과 단 대포의 관통력 강화.</summary>
    private void ForgeWindow()
    {
        const float w = 860, h = 560;
        float x = (canvas.Width - w) / 2, y = Math.Max(14, (canvas.Height - h) / 2);
        canvas.PanelId = "WndForge";
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text("대장간", x + 20, y + 12, 200, 28, 19, Canvas.Gold, 0, true);
        canvas.Text($"소지금 {voyage.Money:N0} Ð", x + 20, y + 16, w - 40, 22, 13, Canvas.Dim, 2);
        canvas.Text("대장장이\n입고 있는 장비와 달아 둔 대포를 단련해 드리지요. 기본 값이 0 인 것과 제한을 넘는 것은 못 합니다.", x + 20, y + 46, w - 40, 40, 13, Canvas.White);
        float row = y + 96;
        string[] shorts = ["공격 +1", "방어 +1", "공격 → 방어", "방어 → 공격"];
        var worn = voyage.Equipped.Where(id => id > 0 && voyage.Items.GetValueOrDefault(id) > 0).Select(voyage.GearOf).OfType<Dho.Data.GearItem>().ToList();
        foreach (var gear in worn)
        {
            canvas.Fill(x + 16, row, w - 32, 36, new Color4(0.04f, 0.07f, 0.2f, 0.85f));
            ItemImage(gear.Id, x + 20, row + 4, 28);
            canvas.Text(gear.Name, x + 54, row + 2, 200, 20, gear.Name.Length > 13 ? 12 : 14, Canvas.White);
            int attack = gear.Stats.ElementAtOrDefault(0), defense = gear.Stats.ElementAtOrDefault(1);
            canvas.Text($"공격력 {attack + voyage.ForgedOf(gear.Id, 0)} / {attack + Voyage.ForgeLimit(attack)}   방어력 {defense + voyage.ForgedOf(gear.Id, 1)} / {defense + Voyage.ForgeLimit(defense)}", x + 54, row + 19, 260, 16, 11, Canvas.Dim);
            for (int kind = 0; kind < 4; kind++)
            {
                string? why = voyage.ForgeBlocker(gear, kind);
                float bx = x + 318 + kind * 104;
                if (canvas.Button(shorts[kind], bx, row + 4, 100, 28, why == null, 12)) voyage.Forge(gear, kind);
                if (why != null && canvas.Hover(bx, row + 4, 100, 28)) _tip = ($"{Voyage.ForgeKinds[kind]} — {why}", bx + 50, row + 2);
            }
            canvas.Text($"{voyage.ForgePrice(gear.Id):N0} Ð", x + w - 116, row + 9, 96, 20, 12, Canvas.Gold, 2);
            row += 39;
        }
        if (worn.Count == 0) { canvas.Text("입고 있는 장비가 없다 — 소지품에서 장비를 「사용」하면 입는다.", x + 20, row + 4, w - 40, 22, 13, Canvas.Dim); row += 30; }
        foreach (var cannon in voyage.Parts.Where(p => p.Slot == 4).Take(Math.Max(0, (int)((y + h - 60 - row) / 39))))
        {
            canvas.Fill(x + 16, row, w - 32, 36, new Color4(0.04f, 0.07f, 0.2f, 0.85f));
            PartIcon(cannon, x + 20, row + 4, 28);
            canvas.Text(cannon.Name, x + 54, row + 2, 240, 20, 14, Canvas.White);
            canvas.Text($"관통 {cannon.B + voyage.ForgedOf(cannon.Id, 0)} / {cannon.B + Math.Max(2, cannon.B / 10)}", x + 54, row + 19, 260, 16, 11, Canvas.Dim);
            string? why = voyage.CannonForgeBlocker(cannon);
            if (canvas.Button("대포 관통력 강화", x + 318, row + 4, 204, 28, why == null, 12)) voyage.ForgeCannon(cannon);
            if (why != null && canvas.Hover(x + 318, row + 4, 204, 28)) _tip = (why, x + 420, row + 2);
            canvas.Text($"{voyage.ForgePrice(cannon.Id):N0} Ð", x + w - 116, row + 9, 96, 20, 12, Canvas.Gold, 2);
            row += 39;
        }
        // 변성연금 — 달아 둔 선박 장갑의 장갑을 올린다. 창의 낱말(변성소재 · 변성실행)은 원본의 것, 소재의 수는 사용자가 옮겨 준 글의 값, 확률은 임시 값(요청 딱지)
        var plates = voyage.Parts.Where(p => p.Slot == 1).GroupBy(p => p.Id).Select(g => g.First()).ToList();
        if (plates.Count > 0 && row < y + h - 150)
        {
            row += 6;
            canvas.Text("변성연금", x + 20, row, 100, 22, 15, Canvas.Gold, 0, true);
            canvas.Text($"변성소재: 특별발주증서 {voyage.Items.GetValueOrDefault(Voyage.OrderPaper)}/{Voyage.TransmutePapers} · 무색 승화약 {voyage.ElixirsHeld}/{Voyage.TransmuteElixirs} · 수표(1000만) {voyage.Items.GetValueOrDefault(Voyage.Check10M)}/{Voyage.TransmuteChecks}     성공 {voyage.TransmuteChance * 100:0}%",
                        x + 110, row + 3, w - 240, 18, 12, Canvas.White);
            Invented("transmute", 1, "변성연금의 소재 · 확률 · 오르는 양", x + w - 116, row);
            row += 26;
            (string Label, int Item)[] books = [("우로보로스의 책", Voyage.OuroborosBook), ("유니콘의 책", Voyage.UnicornBook)];
            for (int b = 0; b < books.Length; b++)
            {
                int have = voyage.Items.GetValueOrDefault(books[b].Item);
                bool on = (b == 0 ? _useOuroboros : _useUnicorn) && have > 0;
                float bx = x + 20 + b * 300;
                bool over = canvas.Hover(bx, row, 290, 22);
                canvas.Frame(bx, row + 2, 18, 18, over ? Canvas.Gold : Canvas.PanelEdge, 1.2f);
                if (on) canvas.Text("✔", bx, row, 18, 20, 14, new Color4(0.5f, 1f, 0.6f, 1), 1, true);
                canvas.Text($"{books[b].Label} 사용 ({have}권) — {(b == 0 ? "반드시 성공" : "성공 시 보너스(+2)")}", bx + 24, row + 3, 290, 18, 11, have > 0 ? Canvas.White : Canvas.Dim);
                if (over && canvas.Pointer.Clicked && have > 0) { if (b == 0) _useOuroboros = !_useOuroboros; else _useUnicorn = !_useUnicorn; canvas.Pressed = true; }
            }
            row += 28;
            foreach (var plate in plates.Take(Math.Max(1, (int)((y + h - 60 - row) / 39))))
            {
                canvas.Fill(x + 16, row, w - 32, 36, new Color4(0.04f, 0.07f, 0.2f, 0.85f));
                PartIcon(plate, x + 20, row + 4, 28);
                canvas.Text(plate.Name, x + 54, row + 2, 240, 20, 14, Canvas.White);
                canvas.Text($"장갑 {voyage.ArmorOf(plate)} (기본 {plate.A})   변성실행 {voyage.TransmutedTimes(plate.Id)}/{Voyage.TransmuteTimes}", x + 54, row + 19, 260, 16, 11, Canvas.Dim);
                string? why = voyage.TransmuteBlocker(plate);
                if (canvas.Button("변성 실행 (장갑 강화)", x + 318, row + 4, 204, 28, why == null, 12)) voyage.Transmute(plate, _useOuroboros, _useUnicorn);
                if (why != null) canvas.Text(why, x + 530, row + 10, w - 550, 18, 11, new Color4(1f, 0.75f, 0.45f, 1));
                row += 39;
            }
        }
        if (canvas.Button("이전", x + w - 130, y + h - 50, 110, 34)) voyage.Dialog = Dialog.None;
    }
    private bool _useOuroboros, _useUnicorn;

    private int _foundKind = 1, _foundTop, _foundChosen;

    /// <summary>
    /// 발견물 목록 — 원본의 글: 「발견물의 리스트입니다.」(6201) · 「보고 싶은 발견물의 분류를 선택해 주십시오」(6202).
    /// 왼쪽에 갈래(찾은 수 / 모두), 가운데에 그 갈래에서 찾은 것, 오른쪽에 고른 것의 그림과 설명.
    /// </summary>
    private int _delegateTop;
    private bool _delegateSpecial;

    // 지도 창이 보이는 쪽 — 그림을 가로 384 · 세로 192 픽셀씩 나눈 칸의 번호(가로 8 · 세로 8, 가로는 감긴다). −1 이면 열 때 배가 있는 쪽으로 잡는다
    private int _chartX = -1, _chartY;
    private const int ChartStepX = 384, ChartStepY = 192, ChartW = 768, ChartH = 384;

    // 내비게이션 — 이용자가 만든 세계 지도(DHOMAP 지도 모음) 위에 내 자리를 찍는다. 지도는 배를 따라간다
    private float _navPanX, _navPanY;
    private bool _navDrag, _navHeld;
    private (float X, float Y) _navLast;
    private int _navMap, _navZoom = 1, _navLeft = int.MinValue, _navTop, _navShown = -1;
    private const int NavW = 960, NavH = 480;

    private void NavWindow()
    {
        const float w = NavW + 40, h = NavH + 110;
        float x = (canvas.Width - w) / 2, y = Math.Max(6, (canvas.Height - h) / 2 - 10);
        canvas.PanelId = "WndNav";
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text("내비게이션", x + 20, y + 8, 200, 24, 17, Canvas.Gold, 0, true);
        var files = NavMap.Files();
        float mx = x + 20, my = y + 40;
        if (files.Count == 0)
        {
            canvas.Text("내비게이션 지도 그림이 없다.\n바탕 화면의 「…DHOMAP…」 폴더나 게임의 data\\navmaps 폴더에 지도 PNG(4096 × 2048)를 둔다.", mx, my + 180, NavW, 60, 14, Canvas.Dim, 1);
            if (canvas.Button("이전", x + w - 130, y + h - 44, 110, 34)) voyage.Dialog = Dialog.None;
            return;
        }
        _navMap = Math.Clamp(_navMap, 0, files.Count - 1);
        // 보이는 넓이: 배율 1 이면 그림의 960 × 480 픽셀(세계 좌표 3,840 × 1,920), 2 면 그 갑절, 4 면 온 세계
        int srcW = NavW * _navZoom, srcH = NavH * _navZoom;
        double px = voyage.ShipX * NavMap.Scale, py = voyage.ShipY * NavMap.Scale;
        // 지도를 잡고 끌면 옮겨진다(가로는 감긴다) — 「내 자리」로 배가 가운데 오게 되돌린다
        bool overMap = canvas.Hover(mx, my, NavW, NavH);
        if (!canvas.Pointer.Down) _navDrag = false;
        else if (_navDrag)
        {
            _navPanX -= (canvas.Pointer.X - _navLast.X) * _navZoom;
            _navPanY -= (canvas.Pointer.Y - _navLast.Y) * _navZoom;
        }
        else if (overMap && !_navHeld) _navDrag = true;
        _navHeld = canvas.Pointer.Down;
        _navLast = (canvas.Pointer.X, canvas.Pointer.Y);
        _navPanY = (float)Math.Clamp(_navPanY, -(py - srcH / 2.0), Math.Max(0, 2048 - srcH) - (py - srcH / 2.0));
        int left = (int)Math.Round(px - srcW / 2.0 + _navPanX), top = Math.Clamp((int)Math.Round(py - srcH / 2.0 + _navPanY), 0, Math.Max(0, 2048 - srcH));
        // 배가 그림에서 여섯 픽셀 넘게 움직였거나 지도 · 배율을 바꿨을 때만 다시 잘라 낸다
        bool again = _navShown != _navMap * 10 + _navZoom || Math.Abs(left - _navLeft) > 6 * _navZoom || Math.Abs(top - _navTop) > 6 * _navZoom;
        if (again) (_navLeft, _navTop, _navShown) = (left, top, _navMap * 10 + _navZoom);
        int map = _navMap, zoom = _navZoom, cutLeft = _navLeft, cutTop = _navTop;
        (int, int, byte[])? Cut()
        {
            if (NavMap.Crop(map, cutLeft, cutTop, NavW * zoom, NavH * zoom) is not { } big) return null;
            if (zoom == 1) return big;
            // 줄여서(가까운 점 뽑기) 창 크기에 맞춘다
            var small = new byte[NavW * NavH * 4];
            for (int j = 0; j < NavH; j++)
                for (int i = 0; i < NavW; i++)
                    Buffer.BlockCopy(big.Bgra, (j * zoom * big.Width + i * zoom) * 4, small, (j * NavW + i) * 4, 4);
            return (NavW, NavH, small);
        }
        if (!canvas.Image("navmap", Cut, mx, my, NavW, NavH, 0, again))
            canvas.Text("지도 그림을 읽지 못했다.", mx, my + 200, NavW, 24, 14, Canvas.Dim, 1);
        canvas.Frame(mx, my, NavW, NavH, Canvas.PanelEdge, 1.5f);
        (float X, float Y)? Spot(double wx, double wy)
        {
            double sx = (((wx * NavMap.Scale - _navLeft) % 4096 + 4096) % 4096) / _navZoom, sy = (wy * NavMap.Scale - _navTop) / _navZoom;
            return sx < 0 || sx >= NavW || sy < 0 || sy >= NavH ? null : (mx + (float)sx, my + (float)sy);
        }
        if (voyage.DelegateTo != null)
            for (int k = voyage.DelegateRouteAt; k < voyage.DelegateRoute.Count; k += 2)
                if (Spot(voyage.DelegateRoute[k].X, voyage.DelegateRoute[k].Y) is { } dot) canvas.Circle(dot.X, dot.Y, 1.5f, new Color4(1f, 1f, 0.6f, 1));
        if (voyage.QuestStage == QuestStage.Accepted && voyage.Quest is { SeaX: > 0 } sought && Spot(sought.SeaX, sought.SeaY) is { } goal)
            canvas.Circle(goal.X, goal.Y, 7, Canvas.Gold, false, 2);
        if (voyage.WreckAt is { } wreck && Spot(wreck.X, wreck.Y) is { } sunk) canvas.Circle(sunk.X, sunk.Y, 5, new Color4(0.4f, 0.9f, 1f, 1), false, 2);
        // 내 자리 — 붉은 점과 뱃머리 줄, 둘레에 깜박이는 고리
        if (Spot(voyage.Mode == Mode.Port ? voyage.City.SeaX : voyage.ShipX, voyage.Mode == Mode.Port ? voyage.City.SeaY : voyage.ShipY) is { } me)
        {
            float heading = (float)voyage.Heading, pulse = 9 + 4 * MathF.Abs(MathF.Sin((float)voyage.Clock * 3));
            canvas.Circle(me.X, me.Y, pulse, new Color4(1f, 0.25f, 0.2f, 0.9f), false, 2);
            if (voyage.Mode == Mode.Sea)
            {
                canvas.Line(me.X, me.Y, me.X + MathF.Sin(heading) * 18, me.Y - MathF.Cos(heading) * 18, new Color4(0, 0, 0, 1), 4);
                canvas.Line(me.X, me.Y, me.X + MathF.Sin(heading) * 18, me.Y - MathF.Cos(heading) * 18, new Color4(1f, 0.9f, 0.2f, 1), 2);
            }
            canvas.Circle(me.X, me.Y, 5, new Color4(0, 0, 0, 1));
            canvas.Circle(me.X, me.Y, 3.8f, new Color4(1f, 0.25f, 0.2f, 1));
        }
        float by = y + h - 44;
        if (canvas.Button("◀", x + 20, by, 34, 34, files.Count > 1, 13)) _navMap = (_navMap + files.Count - 1) % files.Count;
        canvas.Text(NavMap.NameOf(_navMap), x + 58, by + 7, 330, 22, 13, Canvas.White, 1);
        if (canvas.Button("▶", x + 392, by, 34, 34, files.Count > 1, 13)) _navMap = (_navMap + 1) % files.Count;
        if (canvas.Button("넓게", x + 440, by, 70, 34, _navZoom < 4, 13)) _navZoom *= 2;
        if (canvas.Button("좁게", x + 514, by, 70, 34, _navZoom > 1, 13)) _navZoom /= 2;
        if (canvas.Button("내 자리", x + 588, by, 76, 34, _navPanX != 0 || _navPanY != 0, 13)) (_navPanX, _navPanY) = (0, 0);
        canvas.Text($"({voyage.ShipX:0}, {voyage.ShipY:0})  {voyage.SeaNameAt(voyage.ShipX, voyage.ShipY)}", x + 672, by + 8, 190, 22, 12, Canvas.Dim);
        if (canvas.Button("이전", x + w - 130, by, 110, 34)) voyage.Dialog = Dialog.None;
    }

    /// <summary>지도 창을 연다(바다 · 항구에서 M) — 배가 있는 쪽이 가운데 오게.</summary>
    public void OpenChart()
    {
        double px = voyage.ShipX * WorldChart.Scale, py = voyage.ShipY * WorldChart.Scale;
        _chartX = (int)Math.Round((px - ChartW / 2.0) / ChartStepX);
        _chartY = Math.Clamp((int)Math.Round((py - ChartH / 2.0) / ChartStepY), 0, (WorldChart.Height - ChartH) / ChartStepY);
        voyage.Dialog = Dialog.Chart;
    }

    /// <summary>
    /// 게임 지도 — 원본은 지방마다의 양피지 지도(도시는 붉은 지붕 집, 상륙지는 나무, 「변경」 · 「자세히」 · 「조사」 단추)다.
    /// 양피지 그림은 못 찾았고, 클라이언트의 세계지도 그림(뭍만 그린 3,072 × 1,536 — <see cref="WorldChart"/>)을 768 × 384 씩 잘라 보인다.
    /// 화살표로 옆 쪽으로 넘긴다. 도시(들어갈 수 있는 항구)는 붉은 집 꼴, 자리를 아는 상륙지는 초록 점, 내 배는 금빛 점과 뱃머리 줄, 위임 항해의 길은 흰 점.
    /// </summary>
    private void ChartWindow()
    {
        const float w = ChartW + 60, h = ChartH + 150;
        float x = (canvas.Width - w) / 2, y = Math.Max(10, (canvas.Height - h) / 2 - 10);
        canvas.PanelId = "WndChart";
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        if (_chartX == -1) OpenChart();
        int pagesX = WorldChart.Width / ChartStepX, pagesY = (WorldChart.Height - ChartH) / ChartStepY;
        int cx = (_chartX % pagesX + pagesX) % pagesX, cy = Math.Clamp(_chartY, 0, pagesY);
        int left = cx * ChartStepX, top = cy * ChartStepY;
        float mx = x + 30, my = y + 70;
        // 이 쪽 가운데의 해역 이름
        string seaName = voyage.SeaNameAt((left + ChartW / 2.0) / WorldChart.Scale, (top + ChartH / 2.0) / WorldChart.Scale);
        canvas.Fill(x + 30, y + 34, 300, 26, new Color4(0.02f, 0.04f, 0.12f, 0.95f));
        canvas.Text(seaName == "" ? "먼 바다" : seaName, x + 38, y + 37, 290, 22, 15, Canvas.White);
        canvas.Text("세계 지도", x + 30, y + 8, 200, 24, 17, Canvas.Gold, 0, true);
        if (!canvas.Image($"chart:{cx}:{cy}", () => WorldChart.Crop(left, top, ChartW, ChartH), mx, my, ChartW, ChartH))
            canvas.Text("세계지도 그림을 읽지 못했다(게임 폴더의 0010\\0000\\kp000000.bin).", mx, my + 150, ChartW, 24, 14, Canvas.Dim, 1);
        canvas.Frame(mx, my, ChartW, ChartH, Canvas.PanelEdge, 1.5f);
        // 세계 좌표 → 이 쪽의 화면 자리(가로는 감긴다). 쪽 밖이면 null
        (float X, float Y)? Spot(double wx, double wy)
        {
            double px = ((wx * WorldChart.Scale - left) % WorldChart.Width + WorldChart.Width) % WorldChart.Width, py = wy * WorldChart.Scale - top;
            return px < 0 || px >= ChartW || py < 0 || py >= ChartH ? null : (mx + (float)px, my + (float)py);
        }
        foreach (var shore in voyage.Data.Landings)
            if ((shore.X != 0 || shore.Y != 0) && Spot(shore.X, shore.Y) is { } tree)
            {
                canvas.Circle(tree.X, tree.Y, 4, new Color4(0.05f, 0.2f, 0.05f, 0.9f));
                canvas.Circle(tree.X, tree.Y, 3, new Color4(0.25f, 0.65f, 0.2f, 1));
                if (canvas.Hover(tree.X - 6, tree.Y - 6, 12, 12)) _tip = (shore.Name, tree.X, tree.Y - 8);
            }
        foreach (var city in voyage.Data.Cities)
        {
            if ((city.SeaX == 0 && city.SeaY == 0) || Spot(city.SeaX, city.SeaY) is not { } house) continue;
            // 붉은 지붕의 작은 집
            canvas.Fill(house.X - 4, house.Y - 2, 8, 6, new Color4(0.97f, 0.95f, 0.88f, 1));
            canvas.Frame(house.X - 4, house.Y - 2, 8, 6, new Color4(0.25f, 0.12f, 0.08f, 1), 1);
            for (int k = 0; k < 5; k++) canvas.Line(house.X - 5 + k, house.Y - 2 - k, house.X + 5 - k, house.Y - 2 - k, new Color4(0.85f, 0.18f, 0.12f, 1), 1.2f);
            if (city == voyage.City && voyage.Mode == Mode.Port) canvas.Circle(house.X, house.Y, 9, Canvas.Gold, false, 1.5f);
            if (canvas.Hover(house.X - 7, house.Y - 8, 14, 14)) _tip = ($"{city.Name} ({voyage.CultureName(city)})", house.X, house.Y - 10);
        }
        if (voyage.DelegateTo != null)
            for (int k = voyage.DelegateRouteAt; k < voyage.DelegateRoute.Count; k += 3)
                if (Spot(voyage.DelegateRoute[k].X, voyage.DelegateRoute[k].Y) is { } dot) canvas.Circle(dot.X, dot.Y, 1.3f, Canvas.White);
        if (voyage.QuestStage == QuestStage.Accepted && voyage.Quest is { SeaX: > 0 } sought && Spot(sought.SeaX, sought.SeaY) is { } goal)
            canvas.Circle(goal.X, goal.Y, 5, Canvas.Gold, false, 2);
        if (Spot(voyage.ShipX, voyage.ShipY) is { } me)
        {
            float heading = (float)voyage.Heading;
            canvas.Line(me.X, me.Y, me.X + MathF.Sin(heading) * 12, me.Y - MathF.Cos(heading) * 12, new Color4(0.1f, 0.1f, 0.1f, 1), 3);
            canvas.Line(me.X, me.Y, me.X + MathF.Sin(heading) * 12, me.Y - MathF.Cos(heading) * 12, Canvas.Gold, 1.6f);
            canvas.Circle(me.X, me.Y, 5, new Color4(0.1f, 0.1f, 0.1f, 1));
            canvas.Circle(me.X, me.Y, 3.6f, Canvas.Gold);
        }
        // 옆 쪽으로 넘기기 — 원본처럼 네 변의 세모 단추
        if (canvas.Button("◀", x + 4, my + ChartH / 2 - 16, 24, 32, true, 12)) _chartX--;
        if (canvas.Button("▶", x + w - 28, my + ChartH / 2 - 16, 24, 32, true, 12)) _chartX++;
        if (canvas.Button("▲", mx + ChartW / 2 - 16, my - 26, 32, 22, cy > 0, 12)) _chartY = cy - 1;
        if (canvas.Button("▼", mx + ChartW / 2 - 16, my + ChartH + 4, 32, 22, cy < pagesY, 12)) _chartY = cy + 1;
        canvas.Text($"내 배 ({voyage.ShipX:0}, {voyage.ShipY:0}) · 붉은 집: 도시 · 초록 점: 상륙지 — 표 위에 마우스를 올리면 이름이 뜬다", x + 30, y + h - 62, w - 60, 20, 12, Canvas.Dim);
        if (canvas.Button("내 배로", x + 30, y + h - 40, 100, 30, true, 13)) OpenChart();
        if (canvas.Button("이전", x + w - 140, y + h - 42, 110, 34)) voyage.Dialog = Dialog.None;
    }

    /// <summary>위임 항해의 목적지 고르기 — 들어가 본 항구를 가까운 차례로. 원본의 글 5812.</summary>
    private void DelegateWindow()
    {
        const float w = 560, h = 520;
        float x = (canvas.Width - w) / 2, y = Math.Max(14, (canvas.Height - h) / 2);
        canvas.PanelId = "WndDelegate";
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text(voyage.Text(25343, "위임 항해"), x + 20, y + 10, 200, 28, 19, Canvas.Gold, 0, true);
        canvas.Text(voyage.Text(5812, "위임 항해를 할 목적지를 선택하십시오. 중간에「교전」상태가 되면 위임이 중지됩니다."), x + 20, y + 42, w - 40, 36, 12, Canvas.Dim);
        var choices = voyage.DelegateChoices();
        string? why = voyage.DelegateBlocker();
        // 특별 위임 항해 — 허가증이 있으면 켜고 끈다(원본의 글 25449 · 22019)
        int permits = voyage.Items.GetValueOrDefault(Voyage.SpecialPermit);
        if (canvas.Button($"{voyage.Text(25449, "특별 위임 항해")}: {(_delegateSpecial && permits > 0 ? "켬" : "끔")} (허가증 {permits})", x + w - 290, y + 10, 270, 28, permits > 0, 13)) _delegateSpecial = !_delegateSpecial;
        const int shown = 12;
        _delegateTop = Math.Clamp(_delegateTop, 0, Math.Max(0, choices.Count - shown));
        float row = y + 84;
        foreach (var (city, far) in choices.Skip(_delegateTop).Take(shown))
        {
            if (canvas.Button($"{city.Name}   ({voyage.CultureName(city)} · 거리 {far:0})", x + 20, row, w - 40, 28, why == null, 14)) voyage.StartDelegate(city, _delegateSpecial && permits > 0);
            row += 31;
        }
        if (choices.Count == 0) canvas.Text("들어가 본 항구가 없다 — 한 번 입항한 항구만 맡길 수 있다.", x + 20, row, w - 40, 22, 14, Canvas.Dim);
        canvas.Text(why ?? $"{_delegateTop + 1} ~ {Math.Min(choices.Count, _delegateTop + shown)} / {choices.Count} · 휠", x + 20, y + h - 42, 300, 22, 13, why != null ? new Color4(1f, 0.6f, 0.5f, 1) : Canvas.Dim);
        if (canvas.Button("이전", x + w - 130, y + h - 50, 110, 34)) voyage.Dialog = Dialog.None;
    }

    private void FoundWindow()
    {
        const float w = 980, h = 560;
        float x = (canvas.Width - w) / 2, y = Math.Max(14, (canvas.Height - h) / 2);
        canvas.PanelId = "WndFoundList";
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        var all = voyage.Data.Discoveries;
        canvas.Text($"발견물     {voyage.Found.Count} / {all.Count}", x + 20, y + 10, 400, 28, 19, Canvas.Gold, 0, true);
        canvas.Text(voyage.Text(6202, "보고 싶은 발견물의 분류를 선택해 주십시오"), x + 300, y + 16, w - 320, 20, 13, Canvas.Dim, 2);
        // 갈래
        var kinds = voyage.Data.DiscoveryKinds.Where(k => all.Exists(d => d.Kind == k.Id)).ToList();
        for (int i = 0; i < kinds.Count; i++)
        {
            float row = y + 50 + i * 24;
            bool on = kinds[i].Id == _foundKind, over = canvas.Hover(x + 16, row, 200, 23);
            if (on || over) canvas.Fill(x + 16, row, 200, 23, on ? new Color4(0.12f, 0.55f, 0.5f, 0.9f) : new Color4(0.2f, 0.3f, 0.6f, 0.5f));
            canvas.Text(kinds[i].Name, x + 24, row + 2, 120, 20, 14, Canvas.White);
            canvas.Text($"{voyage.FoundOfKind(kinds[i].Id)} / {all.Count(d => d.Kind == kinds[i].Id)}", x + 120, row + 3, 90, 18, 12, Canvas.Dim, 2);
            if (over && canvas.Pointer.Clicked) (_foundKind, _foundTop, _foundChosen, canvas.Pressed) = (kinds[i].Id, 0, 0, true);
        }
        // 그 갈래에서 찾은 것
        var found = all.Where(d => d.Kind == _foundKind && voyage.Found.Contains(d.Id)).OrderBy(d => d.Stars).ThenBy(d => d.Id).ToList();
        const int rows = 18;
        _foundTop = Math.Clamp(_foundTop, 0, Math.Max(0, found.Count - rows));
        _foundChosen = Math.Clamp(_foundChosen, 0, Math.Max(0, found.Count - 1));
        if (found.Count == 0) canvas.Text("이 갈래에서는 아직 찾은 것이 없다.", x + 240, y + 60, 320, 22, 14, Canvas.Dim);
        for (int i = _foundTop; i < Math.Min(found.Count, _foundTop + rows); i++)
        {
            float row = y + 50 + (i - _foundTop) * 24;
            bool on = i == _foundChosen, over = canvas.Hover(x + 230, row, 330, 23);
            if (on || over) canvas.Fill(x + 230, row, 330, 23, on ? new Color4(0.12f, 0.55f, 0.5f, 0.9f) : new Color4(0.2f, 0.3f, 0.6f, 0.5f));
            canvas.Text(found[i].Name, x + 238, row + 2, 205, 20, 14, Canvas.White);
            canvas.Text(voyage.DiscoveryStars(found[i]), x + 440, row + 3, 114, 18, 12, Canvas.Gold, 2);
            if (over && canvas.Pointer.Clicked) (_foundChosen, canvas.Pressed) = (i, true);
        }
        if (found.Count > rows) canvas.Text($"{_foundTop + 1} ~ {Math.Min(found.Count, _foundTop + rows)} / {found.Count} · 휠", x + 230, y + h - 44, 330, 18, 12, Canvas.Dim);
        // 고른 것
        if (found.Count > 0)
        {
            var one = found[_foundChosen];
            float rx = x + 580, rw = w - 600;
            canvas.Image($"sd{one.Id}", () => (_discoveryImages ??= new ImageSet(@"0010\0001\sd")).Pixels(0, one.Id), rx, y + 50, 128, 128);
            canvas.Text(one.Name, rx + 140, y + 54, rw - 140, 50, 17, Canvas.Gold, 0, true);
            canvas.Text($"{kinds.Find(k => k.Id == one.Kind)?.Name}  {voyage.DiscoveryStars(one)}\n모험 경험 {one.Exp} · 명성 {one.Fame}", rx + 140, y + 106, rw - 140, 44, 13, Canvas.White);
            canvas.Text(one.Description, rx, y + 190, rw, h - 260, 14, Canvas.White);
        }
        if (canvas.Button("이전", x + w - 130, y + h - 50, 110, 34)) voyage.Dialog = Dialog.Character;
    }

    /// <summary>서고 — 학문 서적을 골라 읽는다. 글(「열람」 · 「열람료」)은 원본의 것이고 차림과 값은 지은 것(요청 딱지).</summary>
    private void LibraryWindow()
    {
        var books = voyage.Books();
        const float w = 660;
        float h = 170 + books.Count * 34;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
        canvas.PanelId = "WndLibrary";
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text($"{voyage.City.Name} 서고", x + 20, y + 12, w - 40, 28, 19, Canvas.Gold, 0, true);
        string? with = voyage.ReadsWith();
        canvas.Text(with != null ? $"{with}(으)로 읽는다 · 오늘 남은 열람 {voyage.BooksLeft}권" : $"읽을 줄 아는 언어가 없다 — {voyage.BookLanguages()}",
                    x + 20, y + 46, w - 150, 20, 13, with != null ? Canvas.White : new Color4(1f, 0.7f, 0.5f, 1));
        Invented("library", 1, "서고의 서적과 열람료", x + w - 116, y + 44);
        for (int i = 0; i < books.Count; i++)
        {
            float row = y + 78 + i * 34;
            var book = books[i];
            int rank = voyage.Rank(book.Id);
            canvas.Text($"{book.Name} 서적", x + 24, row + 4, 200, 22, 15, rank > 0 ? Canvas.White : Canvas.Dim);
            int shelved = voyage.MapsShelved(book.Id), readable = voyage.MapsHere(book.Id).Count;
            canvas.Text((rank > 0 ? $"Rank {rank}   숙련도 +{40 * voyage.Data.Settings.Gain}" : "스킬 없음") + (shelved > 0 ? $"   지도 {readable}/{shelved}" : ""), x + 200, row + 6, 216, 20, 12, readable > 0 ? Canvas.Gold : Canvas.Dim);
            string? why = voyage.ReadBlocker(book);
            if (canvas.Button($"열람 ({Voyage.BookFee:N0})", x + w - 250, row, 110, 28, why == null, 13)) voyage.ReadBook(book);
            if (canvas.Button(voyage.Text(7410, "연속 열람"), x + w - 134, row, 114, 28, why == null && voyage.Money >= Voyage.ReadOnMoney, 13)) voyage.ReadOn(book);
        }
        canvas.Text(voyage.HoldsMap ? $"가진 지도: 「{voyage.Quest!.Title}」 — {voyage.Quest.Hint}" : voyage.Quest != null ? "※의뢰를 맡은 동안에는 지도가 나오지 않는다" : $"※소지금 {Voyage.ReadOnMoney:N0}두캇 이상일 때 연속 열람이 가능 · 「지도 a/b」 = 지금 랭크로 나올 수 있는 것 / 이 서고의 것", x + 20, y + h - 42, w - 290, 18, 12, voyage.HoldsMap ? Canvas.Gold : Canvas.Dim);
        if (voyage.HoldsMap && canvas.Button("지도를 버린다", x + w - 260, y + h - 50, 122, 34, true, 13)) voyage.DropMap();
        if (canvas.Button("이전", x + w - 130, y + h - 50, 110, 34)) voyage.Dialog = Dialog.None;
    }

    /// <summary>
    /// 망명 확인 창 — 원본의 확인 글(화면 글 22062)에서 이 게임에 있는 줄만 보인다. 값과 기다리는 날은 지은 값(요청 딱지).
    /// </summary>
    private void ExileWindow()
    {
        if (voyage.ExileTo is not { } nation) { voyage.Dialog = Dialog.None; return; }
        const float w = 560, h = 330;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text("망명", x + 20, y + 12, w - 40, 28, 19, Canvas.Gold, 0, true);
        canvas.Text("※극비 망명을 실시합니다", x + 20, y + 54, w - 40, 22, 15, Canvas.White);
        canvas.Text($"{(voyage.NationName == "" ? "무소속" : voyage.NationName)} → {nation.Name}", x + 20, y + 82, w - 40, 26, 18, Canvas.Gold, 1, true);
        canvas.Text($"※지금까지 획득한 공로치가 반감됩니다  ({voyage.Merit} → {voyage.MeritAfterExile})", x + 20, y + 124, w - 40, 22, 14, Canvas.White);
        canvas.Text(voyage.Order is { } order ? $"※받들던 칙명 「{order.Title}」이(가) 취소됩니다" : "※받들던 칙명이 있으면 취소됩니다", x + 20, y + 150, w - 40, 22, 14, Canvas.White);
        canvas.Text($"작위({voyage.TitleName})와 투자한 돈은 그대로 남는다. 왕궁 · 개인농장은 새 나라의 본거지에서 쓴다.", x + 20, y + 176, w - 40, 40, 12, Canvas.Dim);
        canvas.Text($"비용 {voyage.ExileCost:N0} 두캇", x + 20, y + 226, 300, 22, 15, voyage.Money >= voyage.ExileCost ? Canvas.White : new Color4(1f, 0.6f, 0.5f, 1));
        Invented("exile", 1, "망명의 비용 · 조건", x + 250, y + 228);
        string? why = voyage.ExileBlocker();
        if (why != null) canvas.Text(why, x + 20, y + h - 44, w - 300, 22, 13, new Color4(1f, 0.75f, 0.45f, 1));
        if (canvas.Button("망명한다", x + w - 270, y + h - 50, 130, 34, why == null, 14)) voyage.Exile();
        if (canvas.Button("그만둔다", x + w - 130, y + h - 50, 110, 34)) voyage.Dialog = Dialog.None;
    }

    /// <summary>지방함대의 결과 — 그 일의 이름과 원본의 글(잘됐을 때 · 안됐을 때), 받은 것.</summary>
    private void FleetReportWindow()
    {
        if (voyage.FleetResult is not { } result) { voyage.Dialog = Dialog.None; return; }
        const float w = 620, h = 330;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text($"지방함대 — {result.Mission.Name}", x + 20, y + 12, w - 40, 28, 19, Canvas.Gold, 0, true);
        canvas.Text(result.Success ? "성공" : "실패", x + 20, y + 16, w - 40, 22, 15, result.Success ? new Color4(0.5f, 1f, 0.6f, 1) : new Color4(1f, 0.6f, 0.5f, 1), 2, true);
        canvas.Text($"내보낸 부관: {result.Who}", x + 20, y + 50, w - 40, 20, 13, Canvas.Dim);
        canvas.Text(result.Success ? result.Mission.Success : result.Mission.Fail, x + 20, y + 78, w - 40, 170, 15, Canvas.White);
        canvas.Text(result.Reward, x + 20, y + h - 44, w - 170, 22, 13, Canvas.Gold);
        if (canvas.Button("확인", x + w - 140, y + h - 50, 120, 34)) voyage.Dialog = Dialog.Aides;
    }

    /// <summary>주점의 차림 — 이 도시가 내는 술 · 요리 · 음료 여덟 가지. 이름과 설명은 클라이언트 표 37 의 것.</summary>
    private void TavernMenuWindow()
    {
        const float w = 760, h = 430;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text($"{voyage.City.Name} 주점의 차림", x + 20, y + 12, 400, 28, 19, Canvas.Gold, 0, true);
        canvas.Text($"행동력 {voyage.Vigour:0} / {voyage.MaxVigour}   피로 {voyage.Fatigue:0}   소지금 {voyage.Money:N0} Ð", x + 20, y + 16, w - 40, 22, 13, Canvas.Dim, 2);
        float row = y + 52;
        foreach (var dish in voyage.TavernMenuHere())
        {
            canvas.Fill(x + 16, row, w - 32, 38, new Color4(0.04f, 0.07f, 0.2f, 0.85f));
            canvas.Text(Voyage.DishKinds[Math.Clamp(dish.Kind, 0, 3)], x + 24, row + 10, 40, 20, 12, Canvas.Gold);
            canvas.Text(dish.Name, x + 66, row + 2, 230, 20, dish.Name.Length > 14 ? 12 : 15, Canvas.White);
            canvas.Text(Voyage.DishNote(dish), x + 66, row + 21, 230, 16, 10, new Color4(0.5f, 1f, 0.6f, 1));
            canvas.Text(dish.Description.Replace("\n", " "), x + 300, row + 3, w - 300 - 160, 34, 11, Canvas.Dim);
            if (canvas.Button($"주문 ({Voyage.DishPrice(dish):N0})", x + w - 150, row + 5, 130, 28, voyage.Money >= Voyage.DishPrice(dish), 13)) voyage.OrderDish(dish);
            row += 41;
        }
        Invented("tavern", voyage.City.Id, voyage.City.Name + " 주점", x + 20, y + h - 44);
        canvas.Text("어느 도시가 무엇을 내는지는 지어 넣은 것이다", x + 120, y + h - 42, 300, 20, 11, Canvas.Dim);
        if (canvas.Button("이전", x + w - 130, y + h - 50, 110, 34)) voyage.Dialog = Dialog.Tavern;
    }

    /// <summary>교역소 — 사기와 팔기.</summary>
    private readonly Dictionary<int, int> _basket = new();
    private int _tradeChosen;

    /// <summary>
    /// 교역소 — 원본의 짜임: 왼쪽에 품목(그림 · 이름 · 값), 가운데에 수량(누르면 오른쪽으로 담긴다), 오른쪽에 「구입 후」(담은 것 · 창고 · 구입 가격 · 소지금).
    /// 「확인」을 눌러야 산다. 팔기도 같은 짜임이다(오른쪽이 「매각 후」).
    /// </summary>
    private void TradeWindow()
    {
        const float w = 900, h = 520, listWidth = 330, midWidth = 110;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        void Header(string text, float hx, float hw)
        {
            canvas.Fill(hx, y + 52, hw, 24, new Color4(0.72f, 0.76f, 0.84f, 0.92f));
            canvas.Text(text, hx, y + 53, hw, 22, 15, new Color4(0.05f, 0.08f, 0.2f, 1), 1, true, false);
        }
        canvas.Text(_selling ? "교역품매각" : "교역품구입", x + 20, y + 10, 200, 30, 21, Canvas.Gold, 0, true);
        canvas.Text(_selling ? "매각할 교역품을 골라주십시오." : "구입할 교역품을 골라주십시오.", x + 170, y + 16, 400, 24, 15, Canvas.White);
        {
            // 값에 든 관세(살 때 붙고 팔 때 떼인다) — 세율은 지은 값(요청 딱지)
            if (voyage.LanguageNote != "") canvas.Text(voyage.LanguageNote, x + 20, y + 34, 300, 18, 12, new Color4(1f, 0.7f, 0.5f, 1));
            if (voyage.NewsNote != "") canvas.Text($"소문: {voyage.NewsNote}", x + 330, y + 36, 350, 16, 11, new Color4(0.7f, 0.9f, 1f, 1));
            canvas.Text($"관세 {voyage.TaxRate * 100:0}%", x + 440, y + 18, 90, 20, 13, voyage.TaxRate > 0.05 ? new Color4(1f, 0.75f, 0.45f, 1) : Canvas.Dim);
            Invented("tax", 1, "교역 관세의 세율", x + 520, y + 16);
        }
        if (canvas.Button("구입", x + w - 200, y + 10, 86, 30, _selling, 15)) { (_selling, _tradePage, _tradeChosen) = (false, 0, 0); _basket.Clear(); }
        if (canvas.Button("매각", x + w - 108, y + 10, 86, 30, !_selling, 15)) { (_selling, _tradePage, _tradeChosen) = (true, 0, 0); _basket.Clear(); }

        // 왼쪽: 품목
        var goods = _selling ? voyage.Cargo.Where(c => c.Value.Count - _basket.GetValueOrDefault(c.Key) > 0).Select(c => c.Key).OrderBy(k => k).Select(voyage.Good).OfType<GoodData>().ToList() : voyage.GoodsHere();      // 팔려고 다 담은 것은 왼쪽에서 빠진다
        Header(_selling ? "실은 교역품" : "진열품", x + 20, listWidth);
        // 구입 발주서 — 구입 쪽에는 늘 단추가 선다. 고른 교역품의 갈래에 듣는 발주서를 갖고 있을 때만 눌린다(없으면 「발주서 사용 (0)」으로 꺼져 있다)
        if (!_selling && goods.Count > 0)
        {
            var wanted = goods[Math.Clamp(_tradeChosen, 0, goods.Count - 1)];
            int sheet = voyage.OrderSheetFor(wanted);
            // 걸어 둔 장수 / 가진 장수 — 「확인」으로 살 때 걸어 둔 만큼 쓰인다
            string label = sheet > 0 ? $"{voyage.ItemName(sheet)} 사용 ({voyage.SheetsMarked(wanted.Kind)}/{voyage.Items.GetValueOrDefault(sheet)})" : "발주서 사용 (0)";
            if (canvas.Button(label, x + w - 440, y + h - 46, 200, 34, sheet > 0, 12)) voyage.UseOrderSheet(wanted);      // 「확인」 · 「이전」과 같은 줄 · 같은 높이
        }
        // 투자해야 나오는 품목 — 무엇이 얼마에 풀리는지 한 줄로
        if (!_selling && voyage.LockedGoodsHere() is { Count: > 0 } lockedGoods)
            canvas.Text("투자하면 나온다: " + string.Join(" · ", lockedGoods.Select(g => $"{g.Good.Name}({g.Need / 10000:N0}만)")), x + 20, y + 446, 330, 34, 11, Canvas.Gold);      // 진열품 목록 밑(쪽 번호 줄 위)
        const int perPage = 6;
        int pages = Math.Max(1, (goods.Count + perPage - 1) / perPage);
        _tradePage = Math.Clamp(_tradePage, 0, pages - 1);
        _tradeChosen = Math.Clamp(_tradeChosen, 0, Math.Max(0, goods.Count - 1));
        _tradeRows = (goods.Count, perPage);
        int Price(GoodData good) => _selling ? voyage.SellPrice(good) : voyage.BuyPrice(good);
        float row = y + 84;
        for (int i = _tradePage * perPage; i < Math.Min(goods.Count, (_tradePage + 1) * perPage); i++, row += 58)
        {
            bool chosen = i == _tradeChosen, hover = canvas.Hover(x + 20, row, listWidth, 56);
            if (chosen) canvas.Fill(x + 20, row, listWidth, 56, new Color4(0.12f, 0.62f, 0.55f, 0.95f));
            else if (hover) canvas.Fill(x + 20, row, listWidth, 56, new Color4(0.2f, 0.3f, 0.6f, 0.6f));
            if (hover && canvas.Pointer.Clicked) _tradeChosen = i;
            GoodIcon(goods[i].Id, x + 26, row + 5, 46);
            int have = voyage.Cargo.TryGetValue(goods[i].Id, out var held) ? held.Count : 0;
            canvas.Text(goods[i].Name + (_selling ? $" × {have - _basket.GetValueOrDefault(goods[i].Id)}" : ""), x + 84, row + 4, listWidth - 90, 24, 17, Canvas.White);
            canvas.Text($"{Price(goods[i]):N0} Ð", x + 84, row + 28, listWidth - 96, 24, 17, Canvas.White, 2);
            if (voyage.SpecialtyOf(goods[i]) is { Length: > 0 } famed) canvas.Text($"명산품({famed})", x + 150, row + 31, 170, 20, 12, Canvas.Gold);
            if (!_selling && voyage.CanSeeMarket) canvas.Text($"{voyage.MarketPercent(goods[i])}%", x + 84, row + 31, 80, 20, 13, Canvas.Dim);
        }
        if (goods.Count == 0) canvas.Text(_selling ? "실은 교역품이 없다." : "이 도시의 교역소는 팔 물건이 없다.", x + 30, y + 96, listWidth, 24, 15, Canvas.Dim);
        if (pages > 1) canvas.Text($"{_tradePage + 1} / {pages} · 휠", x + 20, y + h - 44, 120, 22, 13, Canvas.Dim);

        // 가운데: 수량 — 누르면 담긴다
        float mx = x + 20 + listWidth + 14;
        Header(_selling ? "매각량" : "구입량", mx, midWidth);
        int used = voyage.HoldUsed + (_selling ? -1 : 1) * _basket.Values.Sum();
        long sum = _basket.Sum(b => voyage.Good(b.Key) is { } g ? (long)Price(g) * b.Value : 0);
        if (goods.Count > 0)
        {
            var good = goods[_tradeChosen];
            int inBasket = _basket.GetValueOrDefault(good.Id), price = Price(good);
            int room = _selling
                ? (voyage.Cargo.TryGetValue(good.Id, out var owned) ? owned.Count : 0) - inBasket
                : (int)Math.Max(0, Math.Min(Math.Min(voyage.Stock(good) - inBasket, voyage.TotalHold - used), (voyage.Money - sum) / Math.Max(1, price)));
            canvas.Text(_selling ? "매각 가능량" : "구입 가능량", mx, y + 82, midWidth, 20, 13, Canvas.White, 1);
            canvas.Text($"{Math.Max(0, room)}", mx, y + 100, midWidth, 22, 16, Canvas.White, 1, true);
            int[] amounts = _selling || goods.Count == 0 ? [1, 10, 50, int.MaxValue] : [.. voyage.BuySteps(goods[Math.Clamp(_tradeChosen, 0, goods.Count - 1)]), int.MaxValue];      // 많이 살 수 있는 품목은 단위가 크다
            for (int k = 0; k < amounts.Length; k++)
            {
                float ty = y + 130 + k * 78;
                int take = Math.Min(amounts[k], room);
                bool can = take > 0 && (amounts[k] == int.MaxValue || room >= amounts[k]);
                bool over = can && canvas.Hover(mx + 27, ty, 56, 56);
                canvas.Fill(mx + 27, ty, 56, 56, over ? new Color4(0.22f, 0.32f, 0.66f, 0.95f) : new Color4(0.04f, 0.07f, 0.2f, 0.85f));
                canvas.Frame(mx + 27, ty, 56, 56, over ? Canvas.Gold : Canvas.PanelEdge, 1.2f);
                GoodIcon(good.Id, mx + 32, ty + 6, 46);
                canvas.Text(amounts[k] == int.MaxValue ? "전체" : $"{amounts[k]}", mx + 27, ty + 36, 52, 20, 14, can ? Canvas.White : Canvas.Dim, 2, true);
                canvas.Text(can ? $"{(long)price * take:N0}" : "-", mx, ty + 58, midWidth, 18, 13, Canvas.Dim, 1);
                if (over && canvas.Pointer.Clicked)
                {
                    canvas.Pointer.Consumed = true;
                    _basket[good.Id] = inBasket + take;
                }
            }
        }

        // 오른쪽: 담은 것
        float rx = mx + midWidth + 14, rw = x + w - 20 - rx;
        Header(_selling ? "매각 후" : "구입 후", rx, rw - 110);
        if (canvas.Button("전부취소", rx + rw - 104, y + 50, 104, 28, _basket.Count > 0 || voyage.SheetsMarkedAll > 0, 14)) { _basket.Clear(); voyage.ClearMarkedSheets(); }
        row = y + 84;
        foreach (var (id, count) in _basket.ToList().Take(7))
        {
            if (voyage.Good(id) is not { } good) continue;
            bool hover = canvas.Hover(rx, row, rw, 38);
            canvas.Fill(rx, row, rw, 38, hover ? new Color4(0.3f, 0.2f, 0.3f, 0.7f) : new Color4(0.04f, 0.07f, 0.2f, 0.85f));
            GoodIcon(good.Id, rx + 4, row + 4, 32);
            canvas.Text($"{good.Name} × {count}", rx + 44, row + 8, rw - 150, 24, 16, Canvas.White);
            canvas.Text($"{(long)Price(good) * count:N0} Ð", rx, row + 8, rw - 8, 24, 16, Canvas.White, 2);
            if (hover && canvas.Pointer.Clicked) { canvas.Pointer.Consumed = true; _basket.Remove(id); }
            row += 42;
        }
        if (_basket.Count == 0) canvas.Text("가운데의 수량을 누르면 여기에 담긴다.\n담은 줄을 누르면 뺀다.", rx + 8, y + 92, rw - 16, 50, 14, Canvas.Dim);
        canvas.Image("gm0:323", () => (_markParts ??= new UiParts(0)).Pixels(323), rx + rw - 150, y + h - 142, 20, 20);
        canvas.Text($"{used} / {voyage.TotalHold}", rx + rw - 126, y + h - 142, 120, 22, 15, Canvas.White, 2);
        canvas.Fill(rx, y + h - 118, rw, 4, new Color4(0.03f, 0.05f, 0.16f, 0.9f));
        canvas.Fill(rx, y + h - 118, rw * Math.Clamp(used / (float)Math.Max(1, voyage.TotalHold), 0, 1), 4, new Color4(0.85f, 0.4f, 0.9f, 1));
        canvas.Text(_selling ? "매각 가격" : "구입 가격", rx + 60, y + h - 108, 120, 24, 16, Canvas.White);
        canvas.Text($"{sum:N0} Ð", rx, y + h - 108, rw - 8, 24, 16, Canvas.White, 2);
        canvas.Text("소지금", rx + 60, y + h - 82, 120, 24, 16, Canvas.Dim);
        canvas.Text($"{voyage.Money:N0} Ð", rx, y + h - 82, rw - 8, 24, 16, Canvas.White, 2);
        if (canvas.Button("확인", x + w - 232, y + h - 46, 100, 34, _basket.Count > 0))
        {
            foreach (var (id, count) in _basket.ToList())
                if (voyage.Good(id) is { } good) { if (_selling) voyage.SellGood(good, count); else voyage.BuyGood(good, count); }
            if (!_selling) voyage.SpendMarkedSheets();      // 걸어 둔 발주서는 거래할 때 한꺼번에 쓰인다
            _basket.Clear();
        }
        if (canvas.Button("이전", x + w - 124, y + h - 46, 100, 34)) { voyage.Dialog = Dialog.None; _basket.Clear(); voyage.ClearMarkedSheets(); _nearbyOpen = false; }

        // 회계 — 흥정과 인근 도시의 시세
        if (voyage.CanSeeMarket)
        {
            string? shut = voyage.HaggleBlocker;
            if (canvas.Button("흥정", x + 150, y + h - 46, 84, 34, shut == null, 15)) voyage.TryHaggle();
            if (canvas.Hover(x + 150, y + h - 46, 84, 34)) _tip = (shut ?? $"회계 — 먹힐 확률 {voyage.HaggleChance * 100:0}% · 상한 {voyage.HaggleCap * 100:0.#}%", x + 192, y + h - 48);
            if (canvas.Button(_nearbyOpen ? "시세 닫기" : "인근 시세", x + 240, y + h - 46, 104, 34, goods.Count > 0, 14)) _nearbyOpen = !_nearbyOpen;
            if (voyage.Haggled > 0) canvas.Text($"흥정 {(_selling ? "+" : "−")}{voyage.Haggled * 100:0.#}%", x + 580, y + 16, 110, 22, 15, new Color4(0.55f, 1f, 0.6f, 1), 2, true);
        }
        else canvas.Text("회계 스킬을 익히면 흥정과 인근 시세 확인을 할 수 있다.", x + 150, y + h - 38, 400, 20, 12, Canvas.Dim);
        if (_nearbyOpen && voyage.CanSeeMarket && goods.Count > 0)
        {
            // 고른 품목이 가까운 도시들에서 얼마인가 — 오른쪽 칸을 덮는다
            var good = goods[_tradeChosen];
            var near = voyage.NearbyMarkets(good);
            float px = rx - 4, py = y + 48, pw = rw + 8, ph = h - 104;
            canvas.Fill(px, py, pw, ph, new Color4(0.03f, 0.05f, 0.17f, 0.98f));
            canvas.Frame(px, py, pw, ph, Canvas.PanelEdge, 1.2f);
            canvas.Block(px, py, pw, ph);
            GoodIcon(good.Id, px + 8, py + 6, 30);
            canvas.Text($"{good.Name} — 인근 도시의 시세", px + 44, py + 9, pw - 52, 22, 15, Canvas.Gold, 0, true);
            canvas.Text("도시", px + 12, py + 40, 120, 18, 12, Canvas.Dim);
            canvas.Text("거리", px + 150, py + 40, 60, 18, 12, Canvas.Dim, 2);
            canvas.Text("시세", px + 216, py + 40, 60, 18, 12, Canvas.Dim, 2);
            canvas.Text("팔 때의 값", px, py + 40, pw - 12, 18, 12, Canvas.Dim, 2);
            int here = voyage.SellPrice(good), shown = (int)((ph - 70) / 24);
            for (int i = 0; i < Math.Min(near.Count, shown); i++)
            {
                var (city, far, percent, price, sells) = near[i];
                float ny = py + 60 + i * 24;
                canvas.Text(city.Name + (sells ? " (산지)" : ""), px + 12, ny, 150, 22, 14, Canvas.White);
                canvas.Text($"{far:0}", px + 150, ny, 60, 22, 13, Canvas.Dim, 2);
                canvas.Text($"{percent}%", px + 216, ny, 60, 22, 14, percent >= 110 ? new Color4(0.55f, 1f, 0.6f, 1) : percent <= 90 ? new Color4(1f, 0.6f, 0.5f, 1) : Canvas.White, 2);
                canvas.Text($"{price:N0} Ð", px, ny, pw - 12, 22, 14, price > here ? Canvas.Gold : Canvas.White, 2);
            }
            if (near.Count == 0) canvas.Text("시세를 알 만큼 가까운 도시가 없다.", px + 12, py + 64, pw - 24, 22, 14, Canvas.Dim);
            else if (near.Count > shown) canvas.Text($"… 그 밖에 {near.Count - shown}곳", px + 12, py + ph - 24, pw - 24, 18, 12, Canvas.Dim);
        }
    }
    private bool _nearbyOpen;
    private (int Count, int Visible) _tradeRows;

    /// <summary>교역품 아이콘 — <c>0010\0001\sc</c> 의 무리 0, id 가 교역품 번호다(48 × 48).</summary>
    // 레시피 책의 그림 — 그 책의 제 그림(아이템 그림 묶음에서 번호로), 없으면 여느 책 그림
    /// <summary>아이템 그림 한 장 — 마우스를 올리면 이름과 설명이 쪽지로 뜬다(아이템 추가 창의 줄들).</summary>
    private void ItemImage(int id, float x, float y, float size)
    {
        canvas.Image($"sb{id / 100000}:{id}", () => (_itemIcons ??= new ImageSet(@"0010\0001\sb")).Pixels(id / 100000, id), x, y, size, size);
        if (!canvas.Hover(x, y, size, size)) return;
        ItemCard(id, x + size + 6, y + size + 2);
    }

    // 아이템의 이름과 설명을 카드 쪽지로 — 다 그린 뒤 맨 위에 뜬다
    private void ItemCard(int id, float x, float y)
    {
        string note = voyage.ItemNote(id).Replace("\n", " ");
        float height = 40 + MathF.Ceiling(Math.Max(1, note.Length) / 26f) * 19;
        _itemTip = (voyage.ItemName(id), note, Math.Min(x, canvas.Width - 384), Math.Min(y, canvas.Height - height - 4), 380, height);
    }

    private void RecipeIcon(int recipeId, float x, float y, float size)
    {
        // 만들어지는 것의 그림이 있으면 그것(원본의 레시피 목록처럼), 없으면 책 그림
        if (voyage.RuleOf(recipeId) is { Output: > 0 } made && voyage.Good(made.Output) != null) { GoodIcon(made.Output, x, y, size); return; }
        if (!canvas.Image($"sb{recipeId / 100000}:{recipeId}", () => (_itemIcons ??= new ImageSet(@"0010\0001\sb")).Pixels(recipeId / 100000, recipeId), x, y, size, size))
            canvas.Image("sb17:book", () => (_itemIcons ??= new ImageSet(@"0010\0001\sb")).Pixels(17, 1700000), x, y, size, size);
    }

    private void GoodIcon(int goodId, float x, float y, float size = 26) =>
        canvas.Image($"sc{goodId}", () => (_goodIcons ??= new ImageSet(@"0010\0001\sc")).Pixels(0, goodId), x, y - 1, size, size);

    // ── 캐릭터 만들기 ────────────────────────────────────────────────────────

    private int _step, _nation;
    private string _name = "";
    private StartLineData? _line;
    private bool _male = true;
    private int _frame;

    private string _shipSearch = "";
    private bool _shipFilterRead;

    private void KeepShipFilter()
    {
        (voyage.Data.Settings.ShipFilterSize, voyage.Data.Settings.ShipFilterUse) = (_shipSize, _shipUse);
        voyage.Data.SaveSettings();
    }
    private bool _shipSearching;

    /// <summary>글자를 받는 칸에 입력하는 중인가 — 그동안은 단축키가 듣지 않는다.</summary>
    private string _addSearch = "";
    private bool _addSearching, _addDyes, _addPapers, _addTickets, _addShipItems, _addGear, _addLab, _addAll = true;
    private string? _addAllFor;
    private readonly List<(string Tag, int Id, string Name, string Note, int Recipe)> _addAllFound = [];
    private int _addGroup;
    private string _recipeSearch = "";
    private bool _recipeSearching;
    private bool RecipeTyping => voyage.Dialog == Dialog.Items && _itemTab == 1 && _recipeOpen == null && _recipeSearching;
    private bool AddTyping => voyage.Dialog == Dialog.Items && _itemTab is 3 or 4 && _addSearching;
    private int _shipAddSize;
    private bool JobTyping => voyage.Dialog == Dialog.Jobs && _jobSearching;
    private bool ContentTyping => voyage.Dialog == Dialog.WorkMethod && _contentSearching;
    public bool Typing => MemoTyping || WarpTyping || (voyage.Created && ((voyage.Dialog == Dialog.Shipyard && _shipSearching) || AddTyping || JobTyping || ContentTyping || RecipeTyping));
    public void StopTyping() => (_shipSearching, _addSearching, _memoEditing, _jobSearching, _contentSearching, _recipeSearching, _warpSearching) = (false, false, false, false, false, false, false);

    /// <summary>이름 칸에 글자를 넣는다(백스페이스는 지운다).</summary>
    public void Type(char c)
    {
        if (MemoTyping)
        {
            var memos = voyage.Data.Settings.SoundMemos;
            string key = _memoBank ? $"{_soundBank}" : $"{_soundBank}:{_soundIndex}", now = memos.GetValueOrDefault(key) ?? "";
            if (c == '\b') { if (now.Length > 0) now = now[..^1]; }
            else if (c is '\r' or (char)27) { _memoEditing = false; voyage.Data.SaveSettings(); return; }
            else if (!char.IsControl(c) && now.Length < (_memoBank ? 12 : 30)) now += c;
            if (now == "") memos.Remove(key); else memos[key] = now;
            return;
        }
        if (WarpTyping)
        {
            if (c == '\b') { if (_warpSearch.Length > 0) _warpSearch = _warpSearch[..^1]; }
            else if (c is '\r' or (char)27) _warpSearching = false;
            else if (!char.IsControl(c) && _warpSearch.Length < 16) _warpSearch += c;
            _warpPage = 0;
            return;
        }
        if (ContentTyping)
        {
            if (c == '\b') { if (_contentSearch.Length > 0) _contentSearch = _contentSearch[..^1]; }
            else if (c is '\r' or (char)27) _contentSearching = false;
            else if (!char.IsControl(c) && _contentSearch.Length < 12) _contentSearch += c;
            (_contentTop, _contentChosen) = (0, 0);
            return;
        }
        if (JobTyping)
        {
            if (c == '\b') { if (_jobSkill.Length > 0) _jobSkill = _jobSkill[..^1]; }
            else if (c is '\r' or (char)27) _jobSearching = false;
            else if (!char.IsControl(c) && _jobSkill.Length < 12) _jobSkill += c;
            (_jobTop, _jobChosen) = (0, 0);
            return;
        }
        if (RecipeTyping)
        {
            if (c == '\b') { if (_recipeSearch.Length > 0) _recipeSearch = _recipeSearch[..^1]; }
            else if (c is '\r' or (char)27) _recipeSearching = false;
            else if (!char.IsControl(c) && _recipeSearch.Length < 16) _recipeSearch += c;
            _itemPage = 0;
            return;
        }
        if (AddTyping)
        {
            if (c == '\b') { if (_addSearch.Length > 0) _addSearch = _addSearch[..^1]; }
            else if (c is '\r' or (char)27) _addSearching = false;
            else if (!char.IsControl(c) && _addSearch.Length < 16) _addSearch += c;
            _itemPage = 0;
            return;
        }
        if (Typing)
        {
            if (c == '\b') { if (_shipSearch.Length > 0) _shipSearch = _shipSearch[..^1]; }
            else if (c is '\r' or (char)27) _shipSearching = false;
            else if (!char.IsControl(c) && _shipSearch.Length < 16) _shipSearch += c;
            (_shipPage, _shipChosen) = (0, 0);
            return;
        }
        if (voyage.Created || _step != 1) return;
        if (c == '\b') { if (_name.Length > 0) _name = _name[..^1]; }
        else if (!char.IsControl(c) && _name.Length < 12) _name += c;
    }

    /// <summary>대본용: "나라,이름,계열,쪽" 으로 만들기 화면을 채운다.</summary>
    public void Fill(string values)
    {
        var parts = values.Split(',');
        _nation = int.Parse(parts[0]);
        _name = parts[1];
        _line = voyage.Data.Start.Lines.Find(l => l.Line == int.Parse(parts[2]));
        _step = int.Parse(parts[3]);
    }

    public void Finish()
    {
        if (!voyage.Created && _line != null) voyage.Create(_name, _nation, _line, _male);
    }

    /// <summary>만들기: 나라 → 이름 → 직업 계열 → 성별 → 확인. 안내 글은 클라이언트 화면 글(5103~5107)이다.</summary>
    private void Creation()
    {
        const float w = 640, h = 420;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
        canvas.Fill(0, 0, canvas.Width, canvas.Height, new Color4(0, 0.02f, 0.10f, 0.55f));
        canvas.Panel(x, y, w, h);
        canvas.Text(voyage.Text(5005, "캐릭터 신규 작성"), x + 20, y + 14, w - 40, 30, 20, Canvas.Gold, 0, true);
        string[] steps = [voyage.Text(310, "소속국가"), voyage.Text(302, "이름"), "직업", "용모", "확인"];
        for (int i = 0; i < steps.Length; i++)
            canvas.Text(steps[i], x + 20 + i * 120, y + 50, 116, 22, 14, i == _step ? Canvas.Gold : Canvas.Dim);

        void Guide(uint id, string fallback) => canvas.Text(voyage.Text(id, fallback).Replace("\n", " "), x + 20, y + 80, w - 40, 60, 14, Canvas.White);
        bool next = false;
        switch (_step)
        {
            case 0:
                Guide(5103, "소속 국가를 선택해 주십시오.");
                var nations = voyage.NationChoices();
                for (int i = 0; i < nations.Count; i++)
                {
                    bool chosen = nations[i].Id == _nation;
                    if (canvas.Button((chosen ? "● " : "") + nations[i].Name, x + 20 + i % 2 * 300, y + 150 + i / 2 * 42, 290, 36)) _nation = nations[i].Id;
                }
                if (_nation != 0)
                    canvas.Text($"시작 도시: {voyage.StartCityOf(_nation).Name}", x + 20, y + 290, w - 40, 24, 15, Canvas.Gold);
                next = _nation != 0;
                break;

            case 1:
                Guide(5105, "이름을 입력해 주십시오.");
                canvas.Fill(x + 20, y + 160, 300, 36, new Color4(0.02f, 0.03f, 0.12f, 1));
                canvas.Frame(x + 20, y + 160, 300, 36, Canvas.Gold);
                canvas.Text(_name + "_", x + 30, y + 166, 280, 26, 17, Canvas.White);
                canvas.Text("한글·영문·숫자, 12자까지. 비워 두면 설정의 이름을 쓴다.", x + 20, y + 206, w - 40, 22, 13, Canvas.Dim);
                next = true;
                break;

            case 2:
                Guide(5106, "직업을 선택해 주십시오.");
                var lines = voyage.Data.Start.Lines;
                for (int i = 0; i < lines.Count; i++)
                {
                    string job = voyage.Data.Jobs.Find(j => j.Id == lines[i].JobId)?.Name ?? lines[i].Name;
                    if (canvas.Button((_line == lines[i] ? "● " : "") + $"{lines[i].Name} — {job}", x + 20, y + 150 + i * 42, 290, 36)) _line = lines[i];
                }
                if (_line != null)
                {
                    canvas.Text(voyage.Text((uint)(2008 + _line.Line), "").Replace("\n", " "), x + 330, y + 150, w - 350, 90, 13, Canvas.White);
                    string skills = string.Join(", ", _line.Skills.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => int.TryParse(s, out int id) ? voyage.SkillName(id) : s));
                    string ship = voyage.Data.Ships.Find(s => s.Id == _line.ShipId)?.Name ?? "";
                    canvas.Text($"소지금 {_line.Money:N0}\n배 {ship}\n스킬 {skills}", x + 330, y + 250, w - 350, 90, 14, Canvas.Gold);
                }
                next = _line != null;
                break;

            case 3:
                Guide(5107, "용모를 선택해 주십시오.");
                if (canvas.Button((_male ? "● " : "") + voyage.Text(303, "남자"), x + 20, y + 150, 200, 36)) _male = true;
                if (canvas.Button((!_male ? "● " : "") + voyage.Text(304, "여자"), x + 230, y + 150, 200, 36)) _male = false;
                var frames = Voyage.FramesOf(_male);
                if (!frames.Contains(_frame)) _frame = frames[0];
                canvas.Text("체형", x + 20, y + 204, 100, 24, 16, Canvas.Gold, 0, true);
                for (int i = 0; i < frames.Length; i++)
                    if (canvas.Button((_frame == frames[i] ? "● " : "") + $"체형 {i + 1}", x + 20 + i * 140, y + 234, 132, 36)) _frame = frames[i];
                canvas.Text("체형 1 이 옷과 머리 모양이 가장 많다. 얼굴 · 머리 · 옷은 시작한 뒤 「의상」(C)에서 고른다.", x + 20, y + 284, w - 40, 22, 13, Canvas.Dim);
                next = true;
                break;

            case 4:
                var nation = voyage.Data.Nations.Find(n => n.Id == _nation);
                canvas.Text($"이름: {(_name.Trim().Length > 0 ? _name.Trim() : voyage.Data.Settings.PlayerName)}\n나라: {nation?.Name}\n" +
                            $"직업: {voyage.Data.Jobs.Find(j => j.Id == _line!.JobId)?.Name}\n성별: {(_male ? "남자" : "여자")}\n" +
                            $"시작 도시: {voyage.StartCityOf(_nation).Name}",
                            x + 20, y + 90, w - 40, 160, 17, Canvas.White);
                if (canvas.Button("이대로 시작한다", x + w - 200, y + h - 54, 180, 38)) voyage.Create(_name, _nation, _line!, _male, _frame);
                break;
        }

        if (_step > 0 && canvas.Button("이전", x + 20, y + h - 54, 100, 38)) _step--;
        if (_step < 4 && canvas.Button("다음", x + w - 120, y + h - 54, 100, 38, next)) _step++;
    }
}
