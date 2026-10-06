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
        if (!voyage.Created)
        {
            Creation();
            Display();
            return;
        }
        Status();
        LogPanel();
        Display();
        if (voyage.Mode == Mode.Port && voyage.TownView)
        {
            TownLabels();
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
        }
        if (voyage.Mode == Mode.Port) QuickBar();
        Dialogs();
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

    public const int TitleHeight = 30, TitleMenuWidth = 44, TitleButtonsWidth = 88;
    public Action? Minimize, Quit;
    private bool _menuOpen;
    /// <summary>모니터 크기 — 이보다 큰 해상도는 고를 수 없다.</summary>
    public (int Width, int Height) Screen = (int.MaxValue, int.MaxValue);
    /// <summary>지금 그리는 크기(픽셀).</summary>
    public (int Width, int Height) Pixels;
    /// <summary>대본용: 햄버거 차림(0) 또는 설정 창(1)을 연다.</summary>
    public void OpenMenu(int which) => (_menuOpen, _displayOpen, _keysOpen, _devOpen, _modOpen) = (which == 0, which == 1, which == 2, which == 3, which == 4);

    private bool _devOpen, _modOpen;

    /// <summary>모드 — 원본과 다르게 굴리는 것들을 켜고 끈다. 설정에 남는다.</summary>
    private void ModWindow()
    {
        const float w = 520, h = 258;
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
            canvas.Text(note, x + 54, ty + 24, w - 80, 18, 12, Canvas.Dim);
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
        canvas.Text("셈한 성공률에 이만큼 더한다(0 ~ 50). 0 이면 그대로.", x + 54, y + 122, 250, 18, 12, Canvas.Dim);
        (string Label, int Step)[] steps = [("−10", -10), ("−1", -1), ("+1", 1), ("+10", 10)];
        for (int i = 0; i < steps.Length; i++)
        {
            int next = Math.Clamp(settings.ModCombineBonus + steps[i].Step, 0, 50);
            if (canvas.Button(steps[i].Label, x + w - 216 + i * 50, y + 102, 46, 28, next != settings.ModCombineBonus, 13)) { settings.ModCombineBonus = next; voyage.Data.SaveSettings(); }
        }
        if (Toggle("경험치 · 숙련도 3배", "모험 · 교역 · 부관 경험치와 스킬 숙련도 · 조타 숙련도가 세 배로 오른다. 명성은 그대로.", y + 152, settings.ModTripleGain))
        {
            settings.ModTripleGain = !settings.ModTripleGain;
            voyage.Data.SaveSettings();
        }
        if (canvas.Button("닫기", x + w - 116, y + h - 40, 100, 30)) _modOpen = false;
    }

    /// <summary>개발 메뉴 — 시험하기 쉽게 돈을 100만 단위로 늘리고 줄인다.</summary>
    private void DevWindow()
    {
        const float w = 340, h = 190;
        float x = (canvas.Width - w) / 2, y = 34;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text("개발 메뉴", x + 16, y + 10, w - 32, 26, 18, Canvas.Gold, 0, true);
        canvas.Text($"소지금  {voyage.Money:N0} Ð", x + 16, y + 48, w - 32, 26, 17, Canvas.White);
        if (canvas.Button("− 1,000,000", x + 16, y + 86, 150, 32, voyage.Money > 0, 14)) voyage.AddMoney(-1_000_000);
        if (canvas.Button("+ 1,000,000", x + 174, y + 86, 150, 32, voyage.Money < 2_000_000_000, 14)) voyage.AddMoney(1_000_000);
        canvas.Text("전직증 · 레시피는 소지품 창(I)의 「아이템 추가」에서 넣는다.", x + 16, y + 124, w - 32, 20, 12, Canvas.Dim);
        if (canvas.Button("닫기", x + w - 116, y + h - 40, 100, 30)) _devOpen = false;
    }

    /// <summary>시내 사람들의 이름표(화면 자리와 이름)와, 말을 걸 수 있는 사람의 이름 — 창이 프레임마다 넣는다.</summary>
    public readonly List<(float X, float Y, string Text)> Labels = [];
    public string? TalkTo;

    private void TownLabels()
    {
        foreach (var (lx, ly, text) in Labels)
            canvas.Text(text, lx - 100, ly - 20, 200, 20, 14, new Color4(0.55f, 1f, 0.6f, 1), 1);
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
    ];

    /// <summary>그 일에 매인 글쇠(설정에 없으면 기본값).</summary>
    public static int KeyOf(Dictionary<string, int> keys, string action) =>
        keys.TryGetValue(action, out int key) ? key : Array.Find(KeyActions, a => a.Action == action).Default;

    public static string KeyName(int key) => key switch
    {
        0 => "없음",
        >= 0x70 and <= 0x7B => $"F{key - 0x6F}",
        >= '0' and <= '9' or >= 'A' and <= 'Z' => ((char)key).ToString(),
        0x20 => "Space", 0x09 => "Tab", 0x0D => "Enter",
        _ => $"키 {key}",
    };

    /// <summary>단축키 등록 창이 다음 글쇠를 기다리는 일(없으면 null) — 창이 글쇠를 받아 맨다.</summary>
    public string? KeyWaiting;
    private bool _keysOpen;

    /// <summary>단축키 등록 — 줄을 누르고 새 글쇠를 누른다. 고른 것은 설정에 남는다.</summary>
    private void KeyWindow()
    {
        const float w = 380;
        float h = 96 + KeyActions.Length * 32 + 46;
        float x = (canvas.Width - w) / 2, y = 34;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text("단축키 등록", x + 16, y + 10, w - 32, 26, 18, Canvas.Gold, 0, true);
        canvas.Text(KeyWaiting == null ? "바꿀 줄을 누르고 새 글쇠를 누른다." : "새 글쇠를 누른다. (Esc 그만두기)", x + 16, y + 40, w - 32, 20, 13, Canvas.Dim);
        var keys = voyage.Data.Settings.Keys;
        float row = y + 68;
        foreach (var (action, label, _) in KeyActions)
        {
            bool waiting = KeyWaiting == action;
            canvas.Text(label, x + 16, row + 4, 200, 22, 15, Canvas.White);
            if (canvas.Button(waiting ? "…" : KeyName(KeyOf(keys, action)), x + w - 136, row, 120, 28, true, 14)) KeyWaiting = waiting ? null : action;
            row += 32;
        }
        canvas.Text("걷기 W A S D · 돛 W S · 입항 F · 퀵슬롯 1 ~ 8 은 고정이다.", x + 16, row + 4, w - 32, 20, 12, Canvas.Dim);
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
        if (TitleButton("—", canvas.Width - TitleButtonsWidth, 44, 13)) Minimize?.Invoke();
        if (TitleButton("✕", canvas.Width - 44, 44, 14)) Quit?.Invoke();
        if (canvas.Hover(0, -bar, canvas.Width, bar)) canvas.Pointer.Consumed = true;

        if (_menuOpen)
        {
            (string Label, Action Run)[] menu =
            [
                ("환경설정 (해상도 · 소리)", () => { if (voyage.Created) voyage.Dialog = Dialog.None; _displayOpen = true; }),
                ("단축키 등록", () => _keysOpen = true),
                ("모드", () => { if (voyage.Created) voyage.Dialog = Dialog.None; _modOpen = true; }),
                ("개발 메뉴", () => { if (voyage.Created) _devOpen = true; }),
                ("소지품 (I)", () => { if (voyage.Created) voyage.Dialog = Dialog.Items; }),
                ("캐릭터 정보 (C)", () => { if (voyage.Created) voyage.Dialog = Dialog.Character; }),
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

        const float w = 300;
        float h = 74 + (Sizes.Length + 3) * 30 + 62 + 68 + 68;
        float x = (canvas.Width - w) / 2, y = 34;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text("설정 — 화면과 소리", x + 16, y + 10, w - 32, 26, 18, Canvas.Gold, 0, true);
        canvas.Text($"지금 {Pixels.Width} × {Pixels.Height}   (F11 전체 화면)", x + 16, y + 40, w - 32, 20, 13, Canvas.Dim);
        var settings = voyage.Data.Settings;
        float row = y + 68;
        foreach (var (width, height) in Sizes)
        {
            bool current = !settings.Fullscreen && Pixels.Width == width && Pixels.Height == height;
            bool fits = width <= Screen.Width && height <= Screen.Height;
            if (canvas.Button((current ? "● " : "") + $"{width} × {height}" + (fits ? "" : "  (화면보다 큼)"), x + 16, row, w - 32, 26, fits, 14)) SetDisplay?.Invoke(width, height, false);
            row += 30;
        }
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
        // 소지품 창의 격자 줄 수(다섯 칸 × n 줄)
        row += 34;
        canvas.Text($"소지품 5 × {settings.ItemRows}", x + 16, row + 3, 130, 22, 14, Canvas.White);
        if (canvas.Button("−", x + 150, row, 36, 26, settings.ItemRows > 4, 16)) { settings.ItemRows--; voyage.Data.SaveSettings(); }
        if (canvas.Button("+", x + 190, row, 36, 26, settings.ItemRows < 8, 16)) { settings.ItemRows++; voyage.Data.SaveSettings(); }
        row += 34;
        if (canvas.Button("효과음 고르기", x + 16, row, 170, 26, true, 13)) _soundOpen = true;
        if (canvas.Button("닫기", x + w - 116, y + h - 40, 100, 30)) (_displayOpen, _soundOpen) = (false, false);
    }

    /// <summary>
    /// 효과음 고르기 — 원본 소리 1,397개에 이름표가 없어서, 들어 보고 메모를 달고 일(선회 · 돛 조종 · 스킬)에 매는 창.
    /// 왼쪽에서 묶음을 고르면 오른쪽에 그 묶음의 소리가 늘어선다. 줄을 누르면 들린다.
    /// </summary>
    private void SoundWindow()
    {
        const float sw = 700, sh = 470;
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
            string uses = string.Join(" ", new[] { ("Turn", "선회"), ("Sail", "돛"), ("Skill", "스킬"), ("Eat", "음식"), ("Door", "입구"), ("Click", "누름"), ("Quest", "퀘스트"), ("Error", "오류"), ("Mastery", "숙련도"), ("Warn", "경고"), ("SkillUp", "레벨업"), ("Done", "완료"), ("StudyDone", "연구 완료"), ("Bank", "저금"), ("Part", "부품"), ("Buy", "구매"), ("Drunk", "술"), ("University", "대학") }.Where(c => settings.Sounds.GetValueOrDefault(c.Item1) == key).Select(c => $"[{c.Item2}]"));
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
        (string Cue, string Label)[] cues = [("Turn", "선회"), ("Sail", "돛 조종"), ("Skill", "그 밖의 스킬")];
        for (int i = 0; i < cues.Length; i++)
        {
            float cy = my + 36 + i * 30;
            canvas.Text($"{cues[i].Label}: {(settings.Sounds.GetValueOrDefault(cues[i].Cue) is { Length: > 0 } now ? now : "없음")}", lx, cy + 3, 170, 22, 13, Canvas.White);
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
            canvas.Text($"{voyage.Money:N0} Ð", 30, 34, 300, 24, 17, Canvas.White);
            canvas.Text(voyage.PlayerName, 44, 60, 300, 24, 17, Canvas.White);
            Bars(44, 90);
        }
        else
        {
            // 원본처럼: 타륜 안에 항해일수, 옆에 소지금, 아래에 이름과 막대 셋(배 · 선원 · 식량 그림)
            const float wx = 40, wy = 40;
            canvas.Circle(wx, wy, 27, new Color4(0.08f, 0.10f, 0.24f, 0.95f));
            for (int k = 0; k < 8; k++)
            {
                float a = k * MathF.PI / 4;
                canvas.Line(wx + MathF.Cos(a) * 16, wy + MathF.Sin(a) * 16, wx + MathF.Cos(a) * 33, wy + MathF.Sin(a) * 33, new Color4(0.62f, 0.45f, 0.22f, 1), 4);
                canvas.Circle(wx + MathF.Cos(a) * 33, wy + MathF.Sin(a) * 33, 3, new Color4(0.72f, 0.55f, 0.28f, 1));
            }
            canvas.Circle(wx, wy, 27, new Color4(0.72f, 0.55f, 0.28f, 1), false, 4);
            canvas.Circle(wx, wy, 17, new Color4(0.04f, 0.06f, 0.18f, 1));
            canvas.Circle(wx, wy, 17, new Color4(0.85f, 0.85f, 0.9f, 1), false, 1.5f);
            canvas.Text($"{voyage.DaysAtSea}", wx - 20, wy - 13, 40, 26, 18, Canvas.White, 1, true);
            canvas.Image("gm0:311", () => (_markParts ??= new UiParts(0)).Pixels(311), 84, 40, 22, 22);
            canvas.Text($"{voyage.Money:N0} Ð", 110, 38, 300, 26, 18, Canvas.White, 0, true);
            canvas.Text(voyage.PlayerName, 44, 76, 260, 24, 17, Canvas.White);
            bool storm = voyage.Weather == Weather.Storm;
            canvas.Text($"{voyage.SeaName}  " + (voyage.Weather == Weather.Clear ? "☀" : storm ? "⚡" : "☁") + voyage.WeatherName, 44, 166, 300, 22, 14,
                        storm ? new Color4(1f, 0.45f, 0.4f, 1) : Canvas.Gold);
            int[] icons = [301, 330, 232];
            for (int k = 0; k < 3; k++)
            {
                int icon = icons[k];
                canvas.Image($"gm0:{icon}", () => (_markParts ??= new UiParts(0)).Pixels(icon), 44, 100 + k * 20, 18, 18);
            }
            Bars(68, 106);
            Disasters();
        }
    }

    /// <summary>막대 셋 — 내구(빨강), 선원과 피로(초록 위에 노랑), 물·식량 가운데 적은 쪽(보라).</summary>
    private void Bars(float x, float y)
    {
        var rules = voyage.Data.Settings.Voyage;
        Bar(x, y, (float)(voyage.Durability / voyage.Stats.Durability), new Color4(0.90f, 0.35f, 0.40f, 1));
        Bar(x, y + 20, (float)(voyage.Crew / voyage.Stats.MaxCrew), new Color4(0.45f, 0.80f, 0.35f, 1));
        canvas.Fill(x, y + 24, 122 * (float)(voyage.Fatigue / 100), 2, new Color4(1f, 0.85f, 0.2f, 1));
        Bar(x, y + 40, (float)Math.Min(voyage.Water / rules.MaxWater, voyage.Food / rules.MaxFood), new Color4(0.80f, 0.40f, 0.85f, 1));
        canvas.Text($"내구 {voyage.Durability:0}", x + 128, y - 7, 200, 18, 12, Canvas.Dim);
        canvas.Text($"선원 {voyage.Crew:0}  피로 {voyage.Fatigue:0}", x + 128, y + 13, 200, 18, 12, Canvas.Dim);
        canvas.Text($"물 {voyage.Water:0}  식량 {voyage.Food:0}", x + 128, y + 33, 200, 18, 12, Canvas.Dim);
    }

    private void Bar(float x, float y, float fill, Color4 color)
    {
        canvas.Fill(x, y, 122, 6, new Color4(0.1f, 0.1f, 0.15f, 0.8f));
        canvas.Fill(x, y, 122 * Math.Clamp(fill, 0, 1), 6, color);
    }

    /// <summary>벌어진 재해와, 실어 둔 보급품으로 대처하는 단추.</summary>
    private void Disasters()
    {
        float y = 192;
        foreach (var disaster in voyage.Disasters.ToList())
        {
            canvas.Fill(4, y, 150, 26, new Color4(0.45f, 0.08f, 0.08f, 0.85f));
            canvas.Text(voyage.DisasterName(disaster.Data), 10, y + 2, 140, 22, 15, Canvas.White);
            int supplyId = disaster.Data.CureSupply;
            if (supplyId != 0 && voyage.Data.Supplies.Find(s => s.Id == supplyId) is { } supply)
            {
                int count = voyage.SupplyCount(supplyId);
                if (canvas.Button($"{supply.Name} 쓰기 ({count})", 160, y, 190, 26, count > 0 && voyage.Dialog == Dialog.None, 13))
                    voyage.Cure(disaster);
            }
            else if (voyage.CureSkill(disaster.Data) is { } rule &&
                     canvas.Button($"{voyage.SkillName(rule.SkillId)} 쓰기 (R{voyage.Rank(rule.SkillId)})", 160, y, 190, 26, voyage.Dialog == Dialog.None, 13))
                voyage.CureWithSkill(disaster);
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
        _logArea = (x, y - 26, w, h + 26);
        _logHover = canvas.Hover(x, y - 26, w, h + 26);
        // 긴 글은 창 너비에 맞춰 여러 줄로 나눠 둔다(아래 도움말 줄로 넘치지 않게) — 기록이 늘 때만 다시 나눈다
        if (voyage.LogSerial != _logCount || (_logLines.Count == 0 && voyage.Log.Count > 0))
        {
            (_logCount, _logBack) = (voyage.LogSerial, 0);          // 새 줄이 오면 맨 아래로
            _logLines.Clear();
            foreach (string entry in voyage.Log.Skip(Math.Max(0, voyage.Log.Count - 200))) _logLines.AddRange(Wrapped(entry, w - 34, 16));
        }
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

    private (string Text, float X, float Y)? _tip;

    /// <summary>가리킨 단추의 이름 — 다 그린 뒤 맨 위에 띄운다.</summary>
    private void Tip()
    {
        if (_tip is not { } tip) return;
        _tip = null;
        float w = tip.Text.Length * 15 + 20, x = Math.Clamp(tip.X - w / 2, 4, canvas.Width - w - 4);
        canvas.Fill(x, tip.Y - 28, w, 24, new Color4(0.02f, 0.04f, 0.14f, 0.92f));
        canvas.Text(tip.Text, x, tip.Y - 27, w, 22, 15, Canvas.White, 1);
    }

    private void PortPanel()
    {
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
            (13, "의뢰 내용", voyage.Quest != null, () => voyage.Dialog = Dialog.QuestDetail),
            (154, "스킬 (X)", true, () => voyage.Dialog = Dialog.Skills),
            (43, "주점 (부관)", true, () => voyage.Dialog = voyage.HasTavern ? Dialog.Tavern : Dialog.Aides),
            (208, "왕궁 (칙명)", voyage.AtCourt || voyage.Order != null, () => voyage.Dialog = Dialog.Court),
        ];
        float icon = 1.1f * IconScale;             // 그림 단추는 본디 비율(40 × 24)대로
        float w = Math.Max(212, 18 + 4 * 46 * icon), h = 30 + (buttons.Length + 3) / 4 * 28 * icon + 24;
        float x = canvas.Width - w - 6, y = canvas.Height - h - 6;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text("항구", x + 8, y + 4, 100, 20, 14, Canvas.White);
        canvas.Text($"{voyage.Ship.Name}   모험 {voyage.AdventureExp} · 명성 {voyage.AdventureFame}   교역 {voyage.TradeExp}", x + 8, y + h - 20, w - 12, 18, 10, Canvas.Dim);
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
        const double roundReach = 180;                // 둥근 지도가 보이는 반지름(세계 좌표) — 측량과 상관없이 늘 뜬다
        float cx = canvas.Width - radius - 26, cy = canvas.Height - radius - 50;

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
                if (round ? dx * dx + dy * dy > 0.94 : Math.Max(Math.Abs(dx), Math.Abs(dy)) > 0.9) return;
                canvas.Circle(mx + (float)dx * half, my + (float)dy * half, dot + 1, new Color4(0, 0, 0, 0.7f));
                canvas.Circle(mx + (float)dx * half, my + (float)dy * half, dot, color);
            }
            foreach (var city in voyage.Data.Cities) Mark(city.SeaX, city.SeaY, round ? new Color4(1f, 0.35f, 0.3f, 1) : Canvas.White, round ? 3 : 2);
            if (voyage.QuestStage == QuestStage.Accepted && voyage.QuestLanding is { X: not 0 } site)
                Mark(site.X, site.Y, Canvas.Gold, 4);
            float heading = (float)voyage.Heading;
            if (Part(ship) != null) PartImage(ship, mx - 8, my - 8, 16, 16, heading);
            else canvas.Line(mx, my, mx + MathF.Sin(heading) * 12, my - MathF.Cos(heading) * 12, Canvas.White, 2);
        }

        // 둥근 지도 — 늘 뜬다
        Paint(_seaMapPixels, Part(mask), roundReach, true);
        canvas.Circle(cx, cy, radius + 5, new Color4(0.04f, 0.06f, 0.18f, 0.9f));
        canvas.Image("seamap", () => (MapPixels, MapPixels, (byte[])_seaMapPixels.Clone()), cx - radius, cy - radius, size, size, 0, true);
        canvas.Circle(cx, cy, radius + 1, Canvas.PanelEdge, false, 2.5f);
        canvas.Circle(cx, cy, radius + 5, new Color4(0.45f, 0.38f, 0.2f, 1), false, 1);
        Marks(cx, cy, radius, roundReach, true);

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
            if (!canvas.Image($"sb{item}", () => (_itemIcons ??= new ImageSet(@"0010\0001\sb")).Pixels(15, item), x + (size - side) / 2, y + (size - side) / 2, side, side))
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
        float y = canvas.Height * 0.38f;             // 원본의 자리 — 아래의 측량 지도와 겹치지 않는다
        for (int i = 0; i < on.Count; i++)
        {
            float x = canvas.Width - 56 - (on.Count - 1 - i) * 40;
            canvas.Fill(x - 2, y - 2, 36, 42, new Color4(0.25f, 0.75f, 0.45f, 0.9f));
            SkillIcon(on[i].Rule.SkillId, x + 2, y + 1, 1);
            canvas.Text($"{i + 1}", x - 1, y - 3, 14, 16, 12, Canvas.White, 0, true);
            canvas.Fill(x, y + 35, 32 * (float)on[i].Left, 3, Canvas.Gold);
            if (canvas.Hover(x - 2, y - 2, 36, 42))
            {
                _tip = (voyage.SkillName(on[i].Rule.SkillId), x + 16, y - 4);
                if (canvas.Pointer.Clicked && voyage.Dialog == Dialog.None) { canvas.Pointer.Consumed = true; voyage.UseSkill(on[i].Rule); }
            }
        }
    }

    private void QuickBar()
    {
        const float cell = 36, gap = 3;             // 글자 없는 칸이라 작게 — 아이콘 본디 크기(24 × 28)에 가깝게
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

        float w = cell * 2 + gap * 3, h = cell * 4 + gap * 5 + 24;
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
            if (canvas.Pointer.Clicked && voyage.Dialog == Dialog.None) { canvas.Pointer.Consumed = true; voyage.UseQuickSlot(slot); }
        }
        float py = y + h - 23;
        if (canvas.Button("◀", x + gap, py, 22, 20, true, 10)) voyage.TurnQuickPage(-1);
        canvas.Text($"{voyage.QuickPage + 1}/{Voyage.QuickPages}", x, py + 2, w, 18, 12, Canvas.White, 1);
        if (canvas.Button("▶", x + w - gap - 22, py, 22, 20, true, 10)) voyage.TurnQuickPage(1);
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

        var choices = voyage.UsableSkills().Select(r => r.SkillId).Concat(voyage.UsableItems().Select(i => -i.Id)).ToList();
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
    /// 스킬 사용 창(F2) — 눌러 쓰는 스킬과 소비 아이템의 목록. 여기서 바로 쓰거나 퀵슬롯에 올린다.
    /// 창이 열려 있는 동안 퀵슬롯의 칸을 누르면 그 칸이 비워진다.
    /// </summary>
    private void UseSkillWindow()
    {
        const float w = 560;
        var skills = voyage.UsableSkills();
        var items = voyage.UsableItems();
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
            canvas.Text(blocker ?? "", x + 226, row + 6, 150, 20, 12, Canvas.Dim);
            if (canvas.Button("사용", x + w - 180, row, 70, 28, blocker == null, 13)) voyage.UseSkill(rule);
            if (canvas.Button(voyage.InQuickSlot(id) ? "올림" : "슬롯", x + w - 104, row, 84, 28, !voyage.InQuickSlot(id), 13)) voyage.AddQuickSlot(id);
            row += 34;
        }
        foreach (var item in items)
        {
            canvas.Text($"{item.Name} × {voyage.Items.GetValueOrDefault(item.Id)}", x + 56, row + 4, 200, 22, 15, Canvas.White);
            canvas.Text(voyage.ItemNote(item.Id), x + 226, row + 6, 150, 20, 11, Canvas.Dim);
            if (canvas.Button("사용", x + w - 180, row, 70, 28, true, 13)) voyage.UseItem(item.Id);
            if (canvas.Button(voyage.InQuickSlot(-item.Id) ? "올림" : "슬롯", x + w - 104, row, 84, 28, !voyage.InQuickSlot(-item.Id), 13)) voyage.AddQuickSlot(-item.Id);
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
        if (voyage.SiteInReach()) text = $"F : {voyage.QuestLanding!.Name}에 상륙한다";
        else if (voyage.PortInReach() is { } port) text = $"F : {port.Name}에 입항한다";
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
        if (voyage.Dialog != Dialog.Sail && _sailWas is { } was) { voyage.ShowSail(was.Pattern, was.Tint); _sailWas = null; }
        if (voyage.Dialog == Dialog.None) return;
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
        if (voyage.Dialog == Dialog.ShipyardMenu) { ShipyardMenu(); return; }
        if (voyage.Dialog == Dialog.SpecialBuild) { SpecialBuildWindow(); return; }
        if (voyage.Dialog == Dialog.HullBuild) { HullBuildWindow(); return; }
        if (voyage.Dialog == Dialog.Equip) { EquipWindow(); return; }
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
                if (voyage.Dialog == Dialog.Guild && canvas.Button("스킬을 배운다", x + w - 170, y + 12, 150, 30, true, 14)) { voyage.LearnFrom(0); break; }
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
                if (Close()) voyage.Dialog = Dialog.None;
                break;

            case Dialog.Wreck:
                Title("난파");
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

    /// <summary>항구의 보급 — 물·식량·대처 물품을 사고, 배를 고치고, 선원을 채운다.</summary>
    private void Supply(float x, float y, float w, float h, Action<string> title)
    {
        var rules = voyage.Data.Settings.Voyage;
        title($"보급     소지금 {voyage.Money:N0} Ð");

        float row = y + 50;
        void Row(string label, string state, string button, bool enabled, Action buy)
        {
            canvas.Text(label, x + 20, row + 4, 150, 24, 15, Canvas.White);
            canvas.Text(state, x + 130, row + 4, 230, 24, 14, Canvas.Dim);
            if (canvas.Button(button, x + w - 190, row, 170, 23, enabled, 13)) buy();
            row += 26;
        }

        Row("물", $"{voyage.Water:0} / {rules.MaxWater}", $"10통 싣기 ({voyage.WaterPrice * 10:N0})", voyage.Water < rules.MaxWater, () => voyage.BuyWater(10));
        Row("식량", $"{voyage.Food:0} / {rules.MaxFood}", $"10통 싣기 ({voyage.FoodPrice * 10:N0})", voyage.Food < rules.MaxFood, () => voyage.BuyFood(10));
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
        if (_displayOpen && _soundOpen)
        {
            if (_soundLeft) _bankTop = Math.Max(0, _bankTop - notches * 2);
            else _soundTop = Math.Max(0, _soundTop - notches * 2);
            return true;
        }
        if (voyage.Dialog == Dialog.None && _logHover)
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
            case Dialog.ShipSwap: _swapTop = Math.Max(0, _swapTop + step); return true;
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
                canvas.Text(top ? "※최대 랭크" : $"{voyage.Skills[skill.Id].Exp:0}/{voyage.ExpToNext(rank)}", x + 340, row + 7, 170, 24, 16, Canvas.White, 2);
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
            var skill = list[_skillChosen];
            int rank = voyage.Rank(skill.Id);
            var rule = voyage.Data.SkillRules.Find(r => r.SkillId == skill.Id);
            SkillIcon(skill.Id, rx, y + 16, 1);
            canvas.Text(skill.Name, rx + 40, y + 22, rw - 40, 26, 18, Canvas.White, 0, true);
            canvas.Fill(rx, y + 60, rw, 96, new Color4(0.06f, 0.10f, 0.26f, 0.9f));
            canvas.Text(skill.Description, rx + 8, y + 66, rw - 16, 88, 15, Canvas.White);
            bool active = rule != null && voyage.SeaSkills().Contains(rule);
            canvas.Text(rule == null ? "(이 게임에서는 아직 하는 일이 없다)" : active || rule.Effect is "Procure" or "Fish" or "Repair" or "Rest" ? "사용 스킬 — 바다에서 눌러 쓴다" : "자동효과",
                        rx, y + 162, rw, 22, 14, rule == null ? Canvas.Dim : Canvas.Gold, 2);
            canvas.Text("상태", rx, y + 196, 100, 22, 15, Canvas.Gold, 0, true);
            canvas.Text(rank > 0 ? $"Rank {rank}" + (rank >= voyage.Data.Settings.MaxSkillRank ? "   ※최대 랭크" : $"   {voyage.Skills[skill.Id].Exp:0}/{voyage.ExpToNext(rank)}") : "익히지 않았다",
                        rx + 10, y + 222, rw - 10, 22, 15, Canvas.White);
            canvas.Text("습득", rx, y + 258, 100, 22, 15, Canvas.Gold, 0, true);
            canvas.Text($"{skill.Cost:N0} 두캇 — {Voyage.TeacherName(skill.Group)} 마스터에게 배운다", rx + 10, y + 284, rw - 10, 22, 15, Canvas.White);
            // 배우기 단추는 그 갈래의 조합 마스터 앞에서만 나온다
            if (rank == 0 && rule != null && voyage.Teacher == skill.Group &&
                canvas.Button($"배우기 ({skill.Cost:N0})", rx, y + 320, 200, 32, voyage.Mode == Mode.Port && voyage.Money >= skill.Cost, 14))
                voyage.Learn(skill);
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
                int have = voyage.Cargo.TryGetValue(good, out var item) ? item.Count : 0;
                GoodIcon(good, x + 20, row);
                canvas.Text($"{voyage.Good(good)?.Name} × {count}", x + 52, row + 3, 260, 22, 15, Canvas.White);
                canvas.Text($"실은 수 {have}", x + 320, row + 4, 200, 22, 13, have >= count ? Canvas.Dim : new Color4(1f, 0.5f, 0.45f, 1));
                row += 30;
            }
            row += 8;
            canvas.Text("만들어지는 것", x + 20, row, 160, 22, 15, Canvas.Gold, 0, true);
            row += 26;
            if (open.OutputItem > 0)
            {
                int made = open.OutputItem;
                canvas.Image($"sb{made / 100000}:{made}", () => (_itemIcons ??= new ImageSet(@"0010\0001\sb")).Pixels(made / 100000, made), x + 20, row, 28, 28);
                canvas.Text($"{voyage.ItemName(made)} × {open.OutputCount}  (아이템)", x + 52, row + 3, 300, 22, 15, Canvas.White);
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
            string needs = voyage.RecipeSkill(open) is { } need ? $"필요 스킬: {voyage.SkillName(need.SkillId)} 랭크 {need.Rank} (지금 {voyage.Rank(need.SkillId)})   " : "";
            canvas.Text($"{needs}지금 {can}번 만들 수 있다 · 창고 {voyage.CargoCount}/{voyage.Stats.Hold}", x + 20, row + 36, w - 40, 22, 13, Canvas.Dim);
            if (voyage.ProduceBlocker(open, 1) is { } why) canvas.Text(why, x + 20, row + 60, w - 40, 22, 13, new Color4(1f, 0.5f, 0.45f, 1));
            if (canvas.Button("1번 생산", x + 20, y + h - 50, 100, 34, voyage.ProduceBlocker(open, 1) == null, 14)) voyage.Produce(open, 1);
            if (canvas.Button("10번", x + 126, y + h - 50, 70, 34, can >= 10 && voyage.ProduceBlocker(open, 10) == null, 14)) voyage.Produce(open, 10);
            if (canvas.Button("전부", x + 202, y + h - 50, 70, 34, can > 0 && voyage.ProduceBlocker(open, can) == null, 14)) voyage.Produce(open, can);
            if (canvas.Button("목록으로", x + 290, y + h - 50, 100, 34, true, 14)) _recipeOpen = null;
            return;
        }

        // 원본의 짜임: 왼쪽에 책 그림, 이름, 그 밑에 필요 스킬(그림과 랭크), 오른쪽에 필요 재료(그림과 수). 줄을 누르면 연다
        var owned = voyage.Recipes.OrderBy(id => id).ToList();
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
                    SkillIcon(need.SkillId, x + 72, row + 22, voyage.Rank(need.SkillId) >= need.Rank ? 1 : 0.45f);
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
        if (owned.Count == 0) canvas.Text("가진 레시피가 없다. 「아이템 추가」의 「레시피 보기」에서 넣는다.", x + 20, row, w - 40, 24, 15, Canvas.Dim);
        pages(count2);
    }
    private int _itemTab, _itemPage;
    /// <summary>대본용: 소지품 창의 쪽지.</summary>
    public int ItemTab { set => (_itemTab, _itemPage) = (value, 0); }
    private bool _addRecipes, _addMaterials;
    private RecipeRule? _recipeOpen;

    /// <summary>소지품 창(I) — 가진 것을 쓰는 쪽지와, 전직증을 마음대로 넣는 「아이템 추가」 쪽지.</summary>
    private void ItemWindow()
    {
        string[] tabs = ["소지품", "레시피", "도구점", "아이템 추가"];
        if (voyage.ItemShopOpen) (voyage.ItemShopOpen, _itemTab, _itemPage) = (false, 2, 0);
        const float w = 640;
        int gridRows = Math.Clamp(voyage.Data.Settings.ItemRows, 4, 8);
        float h = _itemTab == 0 ? Math.Max(440, 136 + gridRows * 68) : 440;
        float x = (canvas.Width - w) / 2, y = Math.Max(34, (canvas.Height - h) / 2);
        canvas.Block(x, y - 30, w, h + 30);
        for (int i = 0; i < tabs.Length; i++)
        {
            bool on = _itemTab == i;
            canvas.Fill(x + i * 142, y - 30, 138, 30, on ? new Color4(0.16f, 0.62f, 0.72f, 0.98f) : new Color4(0.35f, 0.55f, 0.65f, 0.9f));
            canvas.Frame(x + i * 142, y - 30, 138, 30, Canvas.PanelEdge, 1);
            canvas.Text(tabs[i], x + i * 142, y - 27, 138, 24, 16, on ? Canvas.White : new Color4(0.05f, 0.1f, 0.2f, 1), 1, on, on);
            if (canvas.Hover(x + i * 142, y - 30, 138, 30) && canvas.Pointer.Clicked) (_itemTab, _itemPage) = (i, 0);
        }
        canvas.Panel(x, y, w, h);
        canvas.Text(_itemTab == 3 ? "아이템 추가" : $"직업: {voyage.JobName}", x + 20, y + 12, 130, 24, 16, Canvas.Gold, 0, true);

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
            canvas.Text($"{total} / 50", x + 180, y + 46, 100, 22, 14, Canvas.Dim);
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
                    canvas.Text(voyage.ItemName(id), px + 8, y + 132, pw - 16, 62, 14, Canvas.White);
                    canvas.Fill(px, y + 208, 130, 22, new Color4(0.72f, 0.76f, 0.84f, 0.92f));
                    canvas.Text("사용시 효과", px, y + 209, 130, 20, 14, new Color4(0.05f, 0.08f, 0.2f, 1), 1, true, false);
                    string note = voyage.ItemNote(id);
                    canvas.Text(note == "" ? "없음" : note, px + 4, y + 236, pw - 8, 60, 14, Canvas.White);
                    if (shown == _itemChosen && canvas.Button("사용", px, y + 304, 110, 30, true, 14)) voyage.UseItem(id);
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
            int shopPages = Math.Max(1, (voyage.Data.Items.Count(i => i.Price > 0) + perPage - 1) / perPage);
            _itemPage = Math.Clamp(_itemPage, 0, shopPages - 1);
            Pages(shopPages);
            foreach (var item in voyage.Data.Items.Where(i => i.Price > 0).Skip(_itemPage * perPage).Take(open ? perPage : perPage - 1))
            {
                canvas.Text(item.Name, x + 20, row + 3, 200, 24, 15, Canvas.White);
                canvas.Text($"{voyage.ItemNote(item.Id)}   가진 수 {voyage.Items.GetValueOrDefault(item.Id)}", x + 200, row + 5, 300, 22, 12, Canvas.Dim);
                if (canvas.Button($"사기 ({item.Price:N0})", x + w - 140, row, 120, 26, open && voyage.Money >= item.Price, 13)) voyage.BuyItem(item);
                row += 32;
            }
            canvas.Text($"소지금 {voyage.Money:N0} Ð", x + 200, y + h - 43, 250, 22, 14, Canvas.Dim);
        }
        if (_itemTab == 3)
        {
            // 「아이템 추가」: 갈래(전직증 · 레시피 · 재질)와 그 안의 거르기, 이름으로 찾기
            string[] kinds = ["전직", "레시피", "재질", "도료", "증서", "교환", "조빌", "의상", "연금"];
            int kind = _addLab ? 8 : _addGear ? 7 : _addShipItems ? 6 : _addTickets ? 5 : _addPapers ? 4 : _addDyes ? 3 : _addMaterials ? 2 : _addRecipes ? 1 : 0;
            bool Chip(string text, float cx, float cy, float cw, bool on)
            {
                bool over = canvas.Hover(cx, cy, cw, 22);
                canvas.Fill(cx, cy, cw, 22, on ? new Color4(0.16f, 0.62f, 0.72f, 0.98f) : over ? new Color4(0.22f, 0.32f, 0.66f, 0.95f) : new Color4(0.06f, 0.10f, 0.28f, 0.9f));
                canvas.Text(text, cx, cy + 1, cw, 20, 13, Canvas.White, 1);
                return over && canvas.Pointer.Clicked;
            }
            for (int i = 0; i < kinds.Length; i++)
                if (Chip(kinds[i], x + 146 + i * 37 + (i > 1 ? 12 : 0), y + 13, kinds[i].Length > 2 ? 47 : 35, kind == i)) (_addRecipes, _addMaterials, _addDyes, _addPapers, _addTickets, _addShipItems, _addGear, _addLab, _itemPage, _addGroup) = (i == 1, i == 2, i == 3, i == 4, i == 5, i == 6, i == 7, i == 8, 0, 0);
            float sx = x + 502, sw = w - 522;
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
            string[] groups = kind == 0 ? ["전체", "모험", "교역", "전투"] : kind == 2 ? ["전체", "나라", "의식용", "특수 도장", "제독", "그 밖"] : kind == 3 ? ["전체", "돛 도료", "특수"] : kind == 5 ? ["전체", "배를 찾은 것"] : kind == 6 ? ["전체", "선체", "돛", "선박재료", "그 밖"] : kind == 7 ? ["전체", "옷", "모자", "신발", "장갑", "무기", "장신구"] : ["전체"];
            _addGroup = Math.Clamp(_addGroup, 0, groups.Length - 1);
            for (int i = 0; i < groups.Length && groups.Length > 1; i++)
                if (Chip(groups[i], x + 20 + i * 84, y + 42, 80, _addGroup == i)) (_addGroup, _itemPage) = (i, 0);
            row = y + 72;
        }
        bool Found(string name) => _addSearch == "" || name.Contains(_addSearch, StringComparison.OrdinalIgnoreCase);
        const int addPage = 9;
        if (_itemTab != 3) { }
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
                canvas.Image($"sb{id / 100000}:{id}", () => (_itemIcons ??= new ImageSet(@"0010\0001\sb")).Pixels(id / 100000, id), x + 20, row - 1, 28, 28);
                canvas.Text(thing.Name, x + 54, row + 3, 190, 24, thing.Name.Length > 12 ? 12 : 15, Canvas.White);
                canvas.Text(thing.Note, x + 246, row + 5, 230, 22, 11, Canvas.Dim);
                canvas.Text($"가진 수 {voyage.Items.GetValueOrDefault(id)}", x + 476, row + 5, 70, 22, 12, Canvas.Dim);
                if (canvas.Button("추가", x + w - 110, row, 90, 26, true, 13)) voyage.AddItem(id);
                row += 32;
            }
            // 연금술 레시피를 한꺼번에 — 만들 것이 정해진 것만
            var labRecipes = voyage.Data.RecipeRules.Where(r => r.Facility != "").ToList();
            if (canvas.Button($"연금술 레시피 {labRecipes.Count}가지 넣기", x + 180, y + h - 50, 220, 34, labRecipes.Exists(r => !voyage.Recipes.Contains(r.RecipeId)), 13))
                foreach (var rule in labRecipes)
                    if (voyage.Data.Recipes.Find(r => r.Id == rule.RecipeId) is { } recipe) voyage.AddRecipe(recipe);
            Pages(pages);
        }
        else if (_addGear)
        {
            // 의상 · 장비 — 장비 표(15). 번호의 십만 자리가 갈래이자 그림 무리(0 옷 · 1 모자 · 2 신발 · 3 장갑 · 4 무기 · 5 장신구)
            var gear = voyage.Data.Gear.Where(g => Found(g.Name) && (_addGroup == 0 || g.Id / 100000 == _addGroup - 1)).ToList();
            int pages = Math.Max(1, (gear.Count + addPage - 1) / addPage);
            _itemPage = Math.Clamp(_itemPage, 0, pages - 1);
            foreach (var worn in gear.Skip(_itemPage * addPage).Take(addPage))
            {
                int id = worn.Id;
                canvas.Image($"sb{id / 100000}:{id}", () => (_itemIcons ??= new ImageSet(@"0010\0001\sb")).Pixels(id / 100000, id), x + 20, row - 1, 28, 28);
                canvas.Text(worn.Name, x + 54, row + 3, 190, 24, worn.Name.Length > 12 ? 12 : 15, Canvas.White);
                canvas.Text(worn.Description.Replace("\n", " "), x + 246, row + 5, 230, 22, 11, Canvas.Dim);
                canvas.Text($"가진 수 {voyage.Items.GetValueOrDefault(id)}", x + 476, row + 5, 70, 22, 12, Canvas.Dim);
                if (canvas.Button("추가", x + w - 110, row, 90, 26, true, 13)) voyage.AddItem(id);
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
                canvas.Image($"sb22:{id}", () => (_itemIcons ??= new ImageSet(@"0010\0001\sb")).Pixels(22, id), x + 20, row - 1, 28, 28);
                canvas.Text(part.Name, x + 54, row + 3, 190, 24, part.Name.Length > 12 ? 12 : 15, Canvas.White);
                canvas.Text(part.Description.Replace("\n", " "), x + 246, row + 5, 230, 22, 11, Canvas.Dim);
                canvas.Text($"가진 수 {voyage.Items.GetValueOrDefault(id)}", x + 476, row + 5, 70, 22, 12, Canvas.Dim);
                if (canvas.Button("추가", x + w - 110, row, 90, 26, true, 13)) voyage.AddItem(id);
                row += 32;
            }
            if (parts.Count == 0) canvas.Text(voyage.Data.Papers.Count == 0 ? "조빌 아이템 목록(data\\extracted\\paper-items.json)이 없다." : "여기에 맞는 것이 없다.", x + 20, row, w - 40, 24, 14, Canvas.Dim);
            Pages(pages);
        }
        else if (_addTickets)
        {
            // 선박 교환권 — 아이템 표(14)의 원본 번호와 이름. 쓰면 그 배가 부두에 들어온다
            var tickets = voyage.ShipTickets().Where(t => Found(t.Name + voyage.TicketShip(t)?.Name) && (_addGroup == 0 || voyage.TicketShip(t) != null)).ToList();
            int pages = Math.Max(1, (tickets.Count + addPage - 1) / addPage);
            _itemPage = Math.Clamp(_itemPage, 0, pages - 1);
            foreach (var ticket in tickets.Skip(_itemPage * addPage).Take(addPage))
            {
                int id = ticket.Id;
                canvas.Image($"sb{id}", () => (_itemIcons ??= new ImageSet(@"0010\0001\sb")).Pixels(15, id), x + 20, row - 1, 28, 28);
                canvas.Text(ticket.Name, x + 54, row + 3, 230, 24, ticket.Name.Length > 14 ? 12 : 15, Canvas.White);
                var gives = voyage.TicketShip(ticket);
                canvas.Text(gives != null ? $"→ {gives.Name}" : "바꿀 배를 못 찾았다", x + 290, row + 5, 180, 22, 12, gives != null ? Canvas.Gold : Canvas.Dim);
                canvas.Text($"가진 수 {voyage.Items.GetValueOrDefault(id)}", x + 470, row + 5, 80, 22, 12, Canvas.Dim);
                if (canvas.Button("추가", x + w - 110, row, 90, 26, true, 13)) voyage.AddItem(id);
                row += 32;
            }
            if (tickets.Count == 0) canvas.Text(voyage.Data.Papers.Count == 0 ? "교환권 목록(data\\extracted\\paper-items.json)이 없다." : "여기에 맞는 교환권이 없다.", x + 20, row, w - 40, 24, 14, Canvas.Dim);
            Pages(pages);
        }
        else if (_addPapers)
        {
            // 증서 — 국가공헌 훈장증서 · 전용함 건조 허가증, 그리고 아이템 표의 조선 쪽 책(해체 기법서 …)
            int[] books = [Voyage.DismantleBook, 1510029, 1510061, 1510062, 1510030, Voyage.RedesignBook, 1510035, 1510016];
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
                var shapes = Voyage.DyePatterns((int)dye.Amount);
                for (int k = 0; k < shapes.Length; k++) SailThumb(shapes[k], 5, x + 170 + k * 30, row - 1, 28);
                canvas.Text($"가진 수 {voyage.Items.GetValueOrDefault(dye.Id)}", x + 420, row + 5, 100, 22, 12, Canvas.Dim);
                if (canvas.Button("추가", x + w - 110, row, 90, 26, true, 13)) voyage.AddItem(dye.Id);
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
                canvas.Text($"내구 {wood.Durability * 100:0}% · 돛 {wood.Sail * 100:0}%   가진 수 {voyage.Items.GetValueOrDefault(id)}", x + 300, row + 5, 220, 22, 12, Canvas.Dim);
                if (canvas.Button("추가", x + w - 110, row, 90, 26, true, 13)) voyage.AddItem(id);
                row += 32;
            }
            if (woods.Count == 0) canvas.Text("맞는 재질이 없다.", x + 20, row, w - 40, 24, 14, Canvas.Dim);
            Pages(pages);
        }
        else if (_addRecipes)
        {
            // 만들 것이 정해진 레시피만 늘어놓는다(나머지 3,300개는 재료 자료가 없다)
            var rules = voyage.Data.RecipeRules.Where(r => Found(voyage.Data.Recipes.Find(x => x.Id == r.RecipeId)?.Name ?? r.Name) || Found(voyage.Good(r.Output)?.Name ?? "")).ToList();
            int pages = Math.Max(1, (rules.Count + addPage - 1) / addPage);
            _itemPage = Math.Clamp(_itemPage, 0, pages - 1);
            foreach (var rule in rules.Skip(_itemPage * addPage).Take(addPage))
            {
                var recipe = voyage.Data.Recipes.Find(r => r.Id == rule.RecipeId);
                bool have = voyage.Recipes.Contains(rule.RecipeId);
                canvas.Text(recipe?.Name ?? rule.Name, x + 20, row + 3, 250, 24, 15, have ? Canvas.Dim : Canvas.White);
                canvas.Text($"→ {voyage.Good(rule.Output)?.Name} × {rule.OutputCount}", x + 270, row + 5, 230, 22, 12, Canvas.Dim);
                if (canvas.Button(have ? "있음" : "추가", x + w - 110, row, 90, 26, !have && recipe != null, 13)) voyage.AddRecipe(recipe!);
                row += 32;
            }
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
                canvas.Text(job.Group switch { 0 => "모험 계열", 1 => "교역 계열", 2 => "전투 계열", _ => "" } + $"   가진 수 {voyage.Items.GetValueOrDefault(id)}", x + 250, row + 5, 250, 22, 12, Canvas.Dim);
                if (canvas.Button("추가", x + w - 110, row, 90, 26, true, 13)) voyage.AddItem(id);
                row += 32;
            }
            Pages(pages);
        }
        if (canvas.Button("이전", x + w - 130, y + h - 50, 110, 34)) voyage.Dialog = Dialog.None;

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
    private void ShipyardWindow()
    {
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
            canvas.Text($"Lv 모험 {s.Levels.Adventure} · 교역 {s.Levels.Trade} · 전투 {s.Levels.Battle}     속도 {s.Knots:0.0}노트" + (s.Real ? "" : "   (능력치는 지어낸 값)"), rx + 4, y + 288, rw, 22, 14, Canvas.White);
            string? blocker = voyage.ShipBlocker(ship);
            if (blocker != null) canvas.Text(blocker, rx + 4, y + 312, rw, 22, 14, new Color4(1f, 0.5f, 0.45f, 1));

            Cell("구입 가격", $"{voyage.ShipCost(ship):N0} Ð", rx + 150, y + h - 118, 300);
            Cell("소지금", $"{voyage.Money:N0} Ð", rx + 150, y + h - 88, 300);
            if (canvas.Button("확인", rx + 300, y + h - 50, 72, 34, blocker == null)) voyage.BuyShip(ship);
        }
        canvas.Text($"타고 있는 배: {voyage.Ship.Name} · 부두 {voyage.Dock.Count}/{Voyage.DockSlots}", rx, y + h - 148, rw, 20, 13, Canvas.Gold);
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
    private void StrengthenWindow()
    {
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
        canvas.Text($"값 {voyage.WorkCost(_workPicked):N0} 두캇" + (_workPicked.Count(voyage.OwnsPart) is > 0 and var owned ? $"   (가진 조빌 아이템 {owned}가지는 값 대신 든다)" : ""), rx + 6, ry + 4, rw, 20, 14, Canvas.White);
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
        var built = voyage.StatsOf(ship, material.Id, _buildLoad);

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
        if (canvas.Button("−5", x + 140, row, 44, 28, canLoad && _buildLoad > -25, 14)) _buildLoad -= 5;
        canvas.Text($"{_buildLoad:+0;-0;0}%", x + 190, row + 3, 150, 24, 16, Math.Abs(_buildLoad) > 20 ? new Color4(1f, 0.6f, 0.4f, 1) : Canvas.White, 1);
        if (canvas.Button("+5", x + 346, row, 44, 28, canLoad && _buildLoad < 25, 14)) _buildLoad += 5;
        canvas.Text(!canLoad ? $"조선 랭크 {Voyage.LoadRank} 부터 바꿀 수 있다" : Math.Abs(_buildLoad) > 20 ? "20% 를 넘으면 돛과 내파가 깎인다" : "+ 는 창고를 늘리고 선실 · 포실을 줄인다. 20% 까지는 손해가 없다",
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
        if (canvas.Button("건조를 맡긴다", x + 20, y + h - 50, 160, 34, blocker == null))
        {
            // 맡겼으면 창을 닫고 조선소 주인 차림으로 돌아간다
            voyage.OrderShip(ship, material.Id, _buildLoad);
            if (voyage.Ordered != null) voyage.Dialog = Dialog.ShipyardMenu;
        }
        if (canvas.Button("이전", x + w - 140, y + h - 50, 110, 34)) voyage.Dialog = Dialog.Shipyard;
    }

    /// <summary>왕궁 — 작위와 공적, 받은 칙명의 진행, 새로 받을 칙명.</summary>
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
    private void AideWindow()
    {
        const float w = 800, h = 470, listWidth = 400;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
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
            canvas.Text($"급여 {voyage.AidePay(aide)}", x + 276, row + 4, 70, 20, 12, Canvas.Dim);
            if (canvas.Button("해고", x + 340, row, 50, 24, voyage.Mode == Mode.Port, 12)) voyage.DismissAide(aide);
            row += 28;
        }
        if (voyage.Aides.Count == 0) { canvas.Text("없음", x + 20, row + 2, 100, 20, 13, Canvas.Dim); row += 26; }
        row += 6;
        canvas.Fill(x + 20, row, listWidth - 20, 24, new Color4(0.72f, 0.76f, 0.84f, 0.92f));
        canvas.Text("부관 후보", x + 20, row + 1, listWidth - 20, 22, 15, new Color4(0.05f, 0.08f, 0.2f, 1), 1, true, false);
        row += 30;
        bool open = voyage.Mode == Mode.Port && voyage.HasTavern;
        var candidates = open ? voyage.AidesToHire() : [];
        _aideChosen = Math.Clamp(_aideChosen, 0, Math.Max(0, candidates.Count - 1));
        for (int i = 0; i < candidates.Count && row + 50 < y + h - 56; i++, row += 52)
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
        // 붙은 선박 스킬까지 보이게 넓게 — 화면이 좁으면 화면에 맞춘다
        const float h = 420;
        float w = Math.Min(1200, canvas.Width - 16);
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text($"선박교환     부두 {voyage.Dock.Count}/{Voyage.DockSlots}", x + 20, y + 12, w - 40, 30, 20, Canvas.Gold, 0, true);
        // 한 줄에 배 하나: 이름, 그림 붙은 능력치, 단추
        void Line(int ship, string name, Dho.Data.ShipStats s, double durability, float row, bool mine, ShipWork work, string form)
        {
            canvas.Fill(x + 16, row, w - 32, 50, mine ? new Color4(0.12f, 0.45f, 0.45f, 0.7f) : new Color4(0.04f, 0.07f, 0.2f, 0.85f));
            // 왼쪽에 배의 작은 그림(커스텀설정 조선의 목록과 같은 것)
            canvas.Fill(x + 20, row + 3, 44, 44, new Color4(0.02f, 0.03f, 0.08f, 0.95f));
            ShipIcon(ship, x + 20, row + 3, 44);
            canvas.Frame(x + 20, row + 3, 44, 44, new Color4(0.75f, 0.75f, 0.8f, 0.9f), 1);
            canvas.Text(name, x + 74, row + 4, 300, 24, 17, Canvas.White, 0, true);
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
            float room = x + w - 290 - kx;
            for (int k = 0; k < fitted.Count && (k + 1) * 32 <= room; k++)
            {
                SkillIcon(fitted[k], kx + k * 32, row + 8, 1);
                if (canvas.Hover(kx + k * 32, row + 8, 30, 33)) _tip = (voyage.OptionName(fitted[k]) + (fitted[k] == work.Dedicated ? " (전용함 스킬)" : ""), kx + k * 32 + 15, row + 6);
            }
        }
        Line(voyage.Ship.Id, "타고 있는 배 — " + voyage.Ship.Name, voyage.Stats, voyage.Durability, y + 52, true, voyage.Work, voyage.FormName(voyage.Ship, voyage.Work));
        float row = y + 110;
        const int shown = 4;
        _swapTop = Math.Clamp(_swapTop, 0, Math.Max(0, voyage.Dock.Count - shown));
        ScrollBar(x + w - 13, y + 110, shown * 54 - 4, shown, voyage.Dock.Count, ref _swapTop);        // 누르거나 끌어도 굴러간다
        if (voyage.Dock.Count > shown)
            canvas.Text($"{_swapTop + 1} ~ {Math.Min(voyage.Dock.Count, _swapTop + shown)} / {voyage.Dock.Count}척 · 휠로 굴린다", x + 20, y + h - 44, 300, 22, 14, Canvas.Dim);
        foreach (var docked in voyage.Dock.Skip(_swapTop).Take(shown).ToList())
        {
            Line(docked.Ship.Id, docked.Ship.Name + (docked.Material != 0 ? $" ({voyage.MaterialOf(docked.Material)?.Name})" : ""), voyage.StatsOf(docked), docked.Durability, row, false, docked.Work, voyage.FormName(docked.Ship, docked.Work));
            string? blocker = voyage.SwapBlocker(docked);
            if (blocker != null) canvas.Text(blocker, x + w - 470, row + 30, 200, 20, 12, new Color4(1f, 0.5f, 0.45f, 1), 2);
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
        if (canvas.Button(_swapFrom == Dialog.None ? "닫기" : "이전", x + w - 140, y + h - 50, 120, 34)) (voyage.Dialog, _swapFrom) = (_swapFrom, Dialog.None);
    }
    /// <summary>선박교환 창을 연 곳 — 닫으면 그리로 돌아간다(조선소 · 선박 정보).</summary>
    private Dialog _swapFrom;
    private bool _workView, _infoDocked;
    private int _combineMain = -1, _combineMaterial = -1, _combineTop;

    /// <summary>선박부품의 그림 — 아이템 그림 묶음의 무리 6(돛) · 8(장갑) · 9(선수상). 그 번호의 그림이 없으면 무리의 첫 그림.</summary>
    private void PartIcon(ShipPart part, float x, float y, float size)
    {
        var (group, first) = part.Slot switch { 0 => (6, 600100), 1 => (8, 800100), _ => (9, 900001) };
        if (!canvas.Image($"sb{group}:{part.Id}", () => (_itemIcons ??= new ImageSet(@"0010\0001\sb")).Pixels(group, part.Id), x, y, size, size))
            canvas.Image($"sb{group}:{first}", () => (_itemIcons ??= new ImageSet(@"0010\0001\sb")).Pixels(group, first), x, y, size, size);
    }

    /// <summary>
    /// 선박부품 탈착(B) — 원본의 짜임: 왼쪽 「소유 선박부품」 격자, 오른쪽에 그 배의 칸들(보조돛 · 장갑 · 선수상).
    /// 왼쪽을 누르면 빈 칸에 달리고, 단 것(초록 E)을 누르면 떼어진다. 오른쪽 끝에 내 배가 돈다(원본은 칸 뒤에 비친다). 대포 칸은 없다(전투가 없다).
    /// 위의 탭은 원본의 셋 — 선박 데코 · 선원 장비는 아직 없어 꺼 두었다.
    /// </summary>
    private void FittingWindow()
    {
        const float w = 900, h = 432, tile = 52;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2 - 10;
        string[] tabs = ["선박부품", "선박 데코", "선원 장비"];
        for (int k = 0; k < tabs.Length; k++)
        {
            canvas.Fill(x + 4 + k * 132, y - 28, 128, 28, k == 0 ? new Color4(0.25f, 0.72f, 0.85f, 0.95f) : new Color4(0.16f, 0.30f, 0.48f, 0.9f));
            canvas.Text(tabs[k], x + 4 + k * 132, y - 25, 128, 22, 15, k == 0 ? new Color4(0.03f, 0.06f, 0.18f, 1) : Canvas.Dim, 1, k == 0, false);
        }
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y - 28, w, h + 28);
        ShipPreview = (x + 636, y + 14, w - 652, h - 110);
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
        int[] icons = [306, 333, 245];
        for (int slot = 0; slot < 3; slot++)
        {
            float ry = y + 50 + slot * (tile + 14);
            int icon = icons[slot];
            canvas.Image($"gm0:{icon}", () => (_markParts ??= new UiParts(0)).Pixels(icon), rx, ry + 14, 22, 22);
            canvas.Text(Voyage.SlotName[slot], rx - 4, ry + tile - 14, 60, 16, 10, Canvas.Dim);
            var fitted = voyage.Parts.Where(p => p.Slot == slot).ToList();
            for (int k = 0; k < voyage.SlotsOf(slot); k++)
            {
                float tx = rx + 60 + k * (tile + 6);
                canvas.Fill(tx, ry, tile, tile, new Color4(0.02f, 0.04f, 0.12f, 0.95f));
                canvas.Frame(tx, ry, tile, tile, new Color4(0.75f, 0.75f, 0.8f, 0.9f), 1);
                if (k >= fitted.Count) continue;
                PartIcon(fitted[k], tx + 3, ry + 3, tile - 6);
                if (!canvas.Hover(tx, ry, tile, tile)) continue;
                pointed = fitted[k];
                canvas.Frame(tx, ry, tile, tile, Canvas.Gold, 2);
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
        canvas.Text("조합할 선박과 재료가 될 선박을 고른다. 탑승 중인 선박은 고를 수 없고, 재료로 고른 선박은 사라진다.", x + 150, y + 15, w - 170, 22, 13, Canvas.White);
        void Header(string text, float hx, float hw)
        {
            canvas.Fill(hx, y + 46, hw, 24, new Color4(0.72f, 0.76f, 0.84f, 0.92f));
            canvas.Text(text, hx, y + 47, hw, 22, 15, new Color4(0.05f, 0.08f, 0.2f, 1), 1, true, false);
        }
        var dock = voyage.Dock;
        const int shown = 7;
        _combineTop = Math.Clamp(_combineTop, 0, Math.Max(0, dock.Count - shown));
        if (_combineMain >= dock.Count) _combineMain = -1;
        if (_combineMaterial >= dock.Count) _combineMaterial = -1;
        void List(string title, float lx, bool material)
        {
            Header(title, lx, listWidth);
            float row = y + 78;
            for (int i = _combineTop; i < Math.Min(dock.Count, _combineTop + shown); i++, row += 46)
            {
                bool chosen = (material ? _combineMaterial : _combineMain) == i, other = (material ? _combineMain : _combineMaterial) == i;
                bool hover = !other && canvas.Hover(lx, row, listWidth, 44);
                if (chosen) canvas.Fill(lx, row, listWidth, 44, new Color4(0.12f, 0.62f, 0.55f, 0.95f));
                else if (hover) canvas.Fill(lx, row, listWidth, 44, new Color4(0.2f, 0.3f, 0.6f, 0.6f));
                // 이름 뒤에 크기 — 크기가 같은 배만 재료가 된다. 재료 쪽에서는 강화 선박과 크기가 다른 배를 흐리게
                string[] sizeNames = ["소형", "소형", "중형", "대형", "대형"];
                string size = sizeNames[Math.Clamp(dock[i].Ship.SizeClass, 0, 4)];
                bool fits = !material || _combineMain < 0 || _combineMain >= dock.Count || Voyage.SizeGroup(dock[_combineMain].Ship) == Voyage.SizeGroup(dock[i].Ship);
                var sizeColor = size switch { "대형" => new Color4(1f, 0.72f, 0.45f, 1), "중형" => new Color4(0.65f, 1f, 0.7f, 1), _ => new Color4(0.6f, 0.85f, 1f, 1) };
                canvas.Text(dock[i].Ship.Name, lx + 8, row + 2, listWidth - 70, 22, 15, other || !fits ? Canvas.Dim : Canvas.White);
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
            canvas.Text("성공하면 강화는 초기화된다." + (picked > 0 ? $"\n보너스: {Voyage.BonusName(picked)}" + (inherit > 0 ? $" — {voyage.OptionName(inherit)}" : "") : "") + (main.Work.Grade >= 3 ? "\n4 부터는 대실패(강등)가 있다." : ""), rx + 4, y + 216, rw - 8, 80, 12, Canvas.Dim);
            if (blocker != null) canvas.Text(blocker, rx + 4, y + 300, rw - 8, 40, 13, new Color4(1f, 0.5f, 0.45f, 1));
            if (canvas.Button("확인", x + w - 232, y + h - 50, 100, 34, blocker == null))
            {
                voyage.Combine(main, material, picked, inherit);
                (_combineMain, _combineMaterial) = (dock.IndexOf(main), -1);
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
        if (canvas.Hover(x - model + 12, y + h - 48, 170, 34)) _tip = ($"조타 숙련도 — 강화가 {Voyage.WorkShare(voyage.Ship, voyage.Work) * 100:0}% 듣는다", x - model + 150, y + h - 50);
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
        canvas.Text(voyage.PlayerName, x + 20, y + 12, w - 40, 24, 16, Canvas.White);
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
        canvas.Text($"강화 {voyage.Work.Times} / {voyage.MaxTimesOf(voyage.Ship)}   부품 {voyage.Parts.Count}   {s.Knots:0.0}노트", x + 22, y + 192, 220, 20, 13, Canvas.Dim);
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
        Cell(305, $"0 / {s.Guns}", x + 165, y + 250, 140);
        Cell(323, $"{voyage.CargoCount} / {s.Hold}", x + 310, y + 250, 140);
        Bar(x + 310, y + 278, 140, voyage.CargoCount / (double)Math.Max(1, s.Hold), new Color4(0.85f, 0.7f, 0.3f, 1));

        Header("상세 선원 상황", x + 20, y + 296, 170);
        canvas.Text($"필요 선원 {s.MinCrew}     피로 {voyage.Fatigue:0}     물 {voyage.Water:0} · 식량 {voyage.Food:0}", x + 22, y + 326, w - 44, 22, 14, Canvas.White);

        if (_workView)
        {
            // 강화성능 — 능력치마다 지금 값(+강화와 그레이드로 붙은 몫), 선실 · 포실 · 창고, 그레이드 보너스
            var plain = voyage.StatsOf(ship, voyage.ShipMaterialId, voyage.ShipLoad);
            canvas.Fill(x + 4, y + 96, w - 12, h - 150, new Color4(0.03f, 0.06f, 0.20f, 0.98f));
            Header("기본성능", x + 20, y + 100, 200);
            Header("선실 강화", x + 250, y + 100, 200);
            // 원본의 짜임: 줄마다 「값( +붙은 몫)」, 그 밑에 초록 막대 — 붙은 몫이 그 배의 강화 상한에 얼마나 찼는가
            var caps = voyage.StrengthCaps(ship, plain);
            void Line(int icon, string label, int now, int was, float lx, float ly, int cap)
            {
                canvas.Image($"gm0:{icon}", () => (_markParts ??= new UiParts(0)).Pixels(icon), lx, ly + 1, 18, 18);
                canvas.Text(label, lx + 22, ly, 90, 20, 13, Canvas.White);
                canvas.Text($"{now}( {(now - was >= 0 ? "+" : "")}{now - was})", lx, ly, 196, 20, 13, now > was ? new Color4(0.55f, 1f, 0.6f, 1) : Canvas.White, 2);
                float part = cap > 0 ? Math.Clamp((now - was) / (float)cap, 0, 1) : 0;
                canvas.Fill(lx, ly + 20, 196, 3, new Color4(0.30f, 0.32f, 0.40f, 0.9f));
                if (part > 0) canvas.Fill(lx, ly + 20, 196 * part, 3, new Color4(0.55f, 0.85f, 0.35f, 1));
                if (canvas.Hover(lx, ly, 196, 23)) _tip = (cap > 0 ? $"{label} 강화 {Math.Max(0, now - was)} / 상한 {cap} ({part * 100:0}%)" : $"{label} — 강화할 수 없다", lx + 98, ly - 2);
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
            ("커스텀설정 조선", true, () => (voyage.Dialog, _specialChosen, _specialTitle) = (Dialog.SpecialBuild, 0, "커스텀설정 조선")),
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
            _tip = ($"{gear.Name} ({Voyage.GearSlots[Math.Min(gear.Slot, 5)]}){(worn ? " — 장비 중" : "")}", tx + tile / 2, ty - 2);
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
        // 수치 — 장비 표의 칸들을 더한 것(뜻은 못 밝혀 차례대로 적는다)
        var totals = voyage.GearTotals();
        string[] names = ["수치 1", "수치 2", "수치 3", "수치 4"];
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
            if (fittedSkills.Count == 0) canvas.Text("없음", rx + 130, skillY, 100, 20, 13, Canvas.Dim);
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
        if (canvas.Button("선박부품", x + 20, y + h - 50, 110, 34, true, 14)) (voyage.Dialog, _partPage) = (Dialog.ShipParts, 0);
        int books = voyage.Items.GetValueOrDefault(Voyage.DismantleBook);
        bool worked = _specialChosen > 1 ? (voyage.Dock[_specialChosen - 2].Work?.Times ?? 0) > 0 : _specialChosen == 1 && onBoard && voyage.Work.Times > 0;
        if (canvas.Button("성능초기화", x + 136, y + h - 50, 120, 34, worked && books > 0, 14)) _resetAsk = true;
        if (canvas.Hover(x + 136, y + h - 50, 120, 34)) _tip = ($"특수조선 해체 기법서가 한 권 든다 (가진 수 {books})", x + 196, y + h - 52);
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
        canvas.Image("gm0:323", () => (_markParts ??= new UiParts(0)).Pixels(323), x + w - 250, y + h - 42, 20, 20);
        canvas.Text($"{voyage.CargoCount} / {voyage.Stats.Hold}", x + w - 226, y + h - 43, 100, 22, 15, Canvas.White, 2);
        canvas.Fill(x + w - 250, y + h - 18, 124, 4, new Color4(0.03f, 0.05f, 0.16f, 0.9f));
        canvas.Fill(x + w - 250, y + h - 18, 124 * Math.Clamp(voyage.CargoCount / (float)Math.Max(1, voyage.Stats.Hold), 0, 1), 4, new Color4(0.3f, 0.85f, 0.9f, 1));
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
            canvas.Text(under, rx + 52, ry + 26, rw - 60, 20, 13, Canvas.Dim);
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
        string? Original(string name) => detail?.Skills.Find(s => s.Name == name) is { Parts.Count: > 0 } real ? string.Join(" + ", real.Parts) : null;
        int count = _methodChosen == 0 ? plain.Length : options.Count;
        _contentRows = (count, 7);
        _contentTop = Math.Clamp(_contentTop, 0, Math.Max(0, count - 7));
        if (count > 7) canvas.Text($"{_contentTop + 1} ~ {Math.Min(count, _contentTop + 7)} / {count} · 휠 · ↑↓", rx0, y + h - 78, rw0, 20, 12, Canvas.Dim, 2);
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
        }
        if (canvas.Button("돌아가기", x + w - 330, y + h - 50, 100, 34, true, 14)) voyage.Dialog = Dialog.SpecialBuild;
        if (_methodChosen == 2)
        {
            // 전용함 스킬은 재료 고르기 없이 여기서 바로 붙인다
            string? blocker = count > 0 ? voyage.DedicatedBlocker(options[_contentChosen]) : "붙일 수 있는 전용함 스킬이 없다";
            canvas.Text(blocker ?? $"가진 전용함 건조 허가증 {voyage.Items.GetValueOrDefault(Voyage.ShipPermit)}장", x + 20, y + h - 70, listWidth - 20, 20, 13, blocker != null ? new Color4(1f, 0.5f, 0.45f, 1) : Canvas.Dim);
            if (canvas.Button("부여", x + w - 224, y + h - 50, 100, 34, blocker == null, 14)) voyage.GiveDedicated(options[_contentChosen]);
        }
        else if (canvas.Button("다음 ▶", x + w - 224, y + h - 50, 100, 34, count > 0, 14))
        {
            _workPicked.Clear();
            // 고른 내용의 재료를 미리 골라 둔다 — 보통 강화는 그 능력치의 재료 둘, 옵션 스킬은 그 조합
            if (_methodChosen == 0) _workPicked.AddRange(book.Parts.Where(p => p.Stat == plain[_contentChosen].Stat).Take(2).Select(p => p.Id));
            else _workPicked.AddRange([options[_contentChosen].PartA, options[_contentChosen].PartB]);
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
            canvas.Text("✦ 우대 스킬 : 습득조건면제", rx + 10, y + 208, rw - 20, 22, 15, Canvas.White);
            if (voyage.Data.SkillRules.Find(r => r.SkillId == skill.Id) == null)
                canvas.Text("(이 게임에서는 아직 하는 일이 없다)", rx + 10, y + 234, rw - 20, 20, 13, Canvas.Dim);
            canvas.Text("비용", rx + rw - 230, y + h - 122, 80, 22, 15, Canvas.White);
            canvas.Text(known ? "익혔다" : $"{skill.Cost:N0} Ð", rx, y + h - 122, rw - 8, 22, 16, Canvas.White, 2);
            canvas.Image("gm0:311", () => (_markParts ??= new UiParts(0)).Pixels(311), rx + rw - 230, y + h - 96, 20, 20);
            canvas.Text($"{voyage.Money:N0} Ð", rx, y + h - 96, rw - 8, 22, 16, Canvas.White, 2);
            if (canvas.Button("확인", x + w - 224, y + h - 46, 100, 32, !known && voyage.Money >= skill.Cost)) voyage.Learn(skill);
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
        canvas.Text(voyage.NationName, x + 20, y + 14, w - 40, 24, 15, Canvas.Gold, 2);
        canvas.Text(voyage.JobName == "" ? "직업 없음" : voyage.JobName, x + 22, y + 44, w - 44, 22, 15, Canvas.White);
        canvas.Text($"작위  {voyage.TitleName}     공적 {voyage.Merit} / {voyage.MeritToNext}", x + 22, y + 68, w - 44, 22, 14, Canvas.White);
        canvas.Text(voyage.Major == "" ? "전공 없음" : $"전공  {voyage.Major}     학점 {voyage.Credits:N0}", x + 22, y + 90, w - 44, 22, 14, Canvas.Dim);

        Header("상태", y + 122);
        (string Name, int Icon, int Exp, int Fame)[] kinds = [("모험", 268, voyage.AdventureExp, voyage.AdventureFame), ("교역", 269, voyage.TradeExp, 0), ("전투", 270, 0, 0)];
        for (int i = 0; i < kinds.Length; i++)
        {
            var (level, next) = Voyage.LevelOf(kinds[i].Exp);
            float cy = y + 152 + i * 30;
            Cell(kinds[i].Icon, $"Lv {kinds[i].Name}", $"{level}", x + 20, cy, 130);
            Cell(-1, "Next", $"{next:N0}", x + 154, cy, 140);
            Cell(245, "", $"{kinds[i].Fame:N0}", x + 298, cy, 162);
        }
        Cell(232, "피로", $"{voyage.Fatigue:0} / 100", x + 20, y + 246, 200);
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
            canvas.Text($"얻는 스킬: {research.Skill}", rx + 10, row + 56, rw - 190, 22, 13, Canvas.Dim);
            if (done) canvas.Text("마쳤다", rx + rw - 170, row + 56, 160, 22, 14, new Color4(0.55f, 1f, 0.6f, 1), 2);
            else if (now) canvas.Text("연구 중", rx + rw - 170, row + 56, 160, 22, 14, Canvas.Gold, 2);
            else if (!can) canvas.Text("이 게임에 없는 행동이 든다", rx + rw - 230, row + 56, 220, 22, 13, Canvas.Dim, 2);
            else if (canvas.Button("연구 시작", rx + rw - 130, row + 52, 120, 28, major == voyage.Major, 13)) voyage.StartResearch(research);
            row += 92;
        }
        canvas.Text("연구가 바라는 행동을 하면 진행된다. 얻은 스킬의 효과는 아직 없다(이름만 남는다).", x + 20, y + h - 44, w - 180, 22, 13, Canvas.Dim);
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
        const float w = 620, h = 360;
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
        if (canvas.Button("닫기", x + w - 140, y + h - 50, 120, 34)) voyage.Dialog = Dialog.None;
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
        if (canvas.Button("구입", x + w - 200, y + 10, 86, 30, _selling, 15)) { (_selling, _tradePage, _tradeChosen) = (false, 0, 0); _basket.Clear(); }
        if (canvas.Button("매각", x + w - 108, y + 10, 86, 30, !_selling, 15)) { (_selling, _tradePage, _tradeChosen) = (true, 0, 0); _basket.Clear(); }

        // 왼쪽: 품목
        var goods = _selling ? voyage.Cargo.Keys.OrderBy(k => k).Select(voyage.Good).OfType<GoodData>().ToList() : voyage.GoodsHere();
        Header(_selling ? "실은 교역품" : "진열품", x + 20, listWidth);
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
            canvas.Text(goods[i].Name + (_selling ? $" × {have}" : ""), x + 84, row + 4, listWidth - 90, 24, 17, Canvas.White);
            canvas.Text($"{Price(goods[i]):N0} Ð", x + 84, row + 28, listWidth - 96, 24, 17, Canvas.White, 2);
            if (!_selling && voyage.CanSeeMarket) canvas.Text($"{voyage.MarketPercent(goods[i])}%", x + 84, row + 31, 80, 20, 13, Canvas.Dim);
        }
        if (goods.Count == 0) canvas.Text(_selling ? "실은 교역품이 없다." : "이 도시의 교역소는 팔 물건이 없다.", x + 30, y + 96, listWidth, 24, 15, Canvas.Dim);
        if (pages > 1) canvas.Text($"{_tradePage + 1} / {pages} · 휠", x + 20, y + h - 44, 120, 22, 13, Canvas.Dim);

        // 가운데: 수량 — 누르면 담긴다
        float mx = x + 20 + listWidth + 14;
        Header(_selling ? "매각량" : "구입량", mx, midWidth);
        int used = voyage.CargoCount + (_selling ? -1 : 1) * _basket.Values.Sum();
        long sum = _basket.Sum(b => voyage.Good(b.Key) is { } g ? (long)Price(g) * b.Value : 0);
        if (goods.Count > 0)
        {
            var good = goods[_tradeChosen];
            int inBasket = _basket.GetValueOrDefault(good.Id), price = Price(good);
            int room = _selling
                ? (voyage.Cargo.TryGetValue(good.Id, out var owned) ? owned.Count : 0) - inBasket
                : (int)Math.Max(0, Math.Min(Math.Min(voyage.Stock(good) - inBasket, voyage.Stats.Hold - used), (voyage.Money - sum) / Math.Max(1, price)));
            canvas.Text(_selling ? "매각 가능량" : "구입 가능량", mx, y + 82, midWidth, 20, 13, Canvas.White, 1);
            canvas.Text($"{Math.Max(0, room)}", mx, y + 100, midWidth, 22, 16, Canvas.White, 1, true);
            int[] amounts = [1, 10, 50, int.MaxValue];
            for (int k = 0; k < amounts.Length; k++)
            {
                float ty = y + 130 + k * 78;
                int take = Math.Min(amounts[k], room);
                bool can = take > 0 && (amounts[k] == int.MaxValue || room >= amounts[k]);
                bool over = can && canvas.Hover(mx + 27, ty, 56, 56);
                canvas.Fill(mx + 27, ty, 56, 56, over ? new Color4(0.22f, 0.32f, 0.66f, 0.95f) : new Color4(0.04f, 0.07f, 0.2f, 0.85f));
                canvas.Frame(mx + 27, ty, 56, 56, over ? Canvas.Gold : Canvas.PanelEdge, 1.2f);
                GoodIcon(good.Id, mx + 32, ty + 6, 46);
                canvas.Text(amounts[k] == int.MaxValue ? "가득" : $"{amounts[k]}", mx + 27, ty + 36, 52, 20, 14, can ? Canvas.White : Canvas.Dim, 2, true);
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
        if (canvas.Button("전부취소", rx + rw - 104, y + 50, 104, 28, _basket.Count > 0, 14)) _basket.Clear();
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
        canvas.Text($"{used} / {voyage.Stats.Hold}", rx + rw - 126, y + h - 142, 120, 22, 15, Canvas.White, 2);
        canvas.Fill(rx, y + h - 118, rw, 4, new Color4(0.03f, 0.05f, 0.16f, 0.9f));
        canvas.Fill(rx, y + h - 118, rw * Math.Clamp(used / (float)Math.Max(1, voyage.Stats.Hold), 0, 1), 4, new Color4(0.85f, 0.4f, 0.9f, 1));
        canvas.Text(_selling ? "매각 가격" : "구입 가격", rx + 60, y + h - 108, 120, 24, 16, Canvas.White);
        canvas.Text($"{sum:N0} Ð", rx, y + h - 108, rw - 8, 24, 16, Canvas.White, 2);
        canvas.Text("소지금", rx + 60, y + h - 82, 120, 24, 16, Canvas.Dim);
        canvas.Text($"{voyage.Money:N0} Ð", rx, y + h - 82, rw - 8, 24, 16, Canvas.White, 2);
        if (canvas.Button("확인", x + w - 232, y + h - 46, 100, 34, _basket.Count > 0))
        {
            foreach (var (id, count) in _basket.ToList())
                if (voyage.Good(id) is { } good) { if (_selling) voyage.SellGood(good, count); else voyage.BuyGood(good, count); }
            _basket.Clear();
        }
        if (canvas.Button("이전", x + w - 124, y + h - 46, 100, 34)) { voyage.Dialog = Dialog.None; _basket.Clear(); }
    }
    private (int Count, int Visible) _tradeRows;

    /// <summary>교역품 아이콘 — <c>0010\0001\sc</c> 의 무리 0, id 가 교역품 번호다(48 × 48).</summary>
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
    private bool _addSearching, _addDyes, _addPapers, _addTickets, _addShipItems, _addGear, _addLab;
    private int _addGroup;
    private bool AddTyping => voyage.Dialog == Dialog.Items && _itemTab == 3 && _addSearching;
    private bool JobTyping => voyage.Dialog == Dialog.Jobs && _jobSearching;
    private bool ContentTyping => voyage.Dialog == Dialog.WorkMethod && _contentSearching;
    public bool Typing => MemoTyping || (voyage.Created && ((voyage.Dialog == Dialog.Shipyard && _shipSearching) || AddTyping || JobTyping || ContentTyping));
    public void StopTyping() => (_shipSearching, _addSearching, _memoEditing, _jobSearching, _contentSearching) = (false, false, false, false, false);

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
