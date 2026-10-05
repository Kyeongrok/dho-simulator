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
        if (voyage.Mode == Mode.Port && voyage.TownView) TownPanel();
        else if (voyage.Mode == Mode.Port) PortPanel();
        else
        {
            SeaMap();
            SeaSkillBar();
            Prompt();
        }
        Dialogs();
        Tip();
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
    public void OpenMenu(int which) => (_menuOpen, _displayOpen) = (which == 0, which == 1);

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
                ("설정 (해상도 · 소리)", () => _displayOpen = true),
                ("스킬 (X)", () => { if (voyage.Created) voyage.Dialog = Dialog.Skills; }),
                ("소지품 (I)", () => { if (voyage.Created) voyage.Dialog = Dialog.Items; }),
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
        if (!_displayOpen) return;
        if (voyage.Created && voyage.Dialog != Dialog.None) { _displayOpen = false; return; }

        const float w = 300;
        float h = 74 + (Sizes.Length + 3) * 30 + 62;
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
        canvas.Text($"글자 크기 {canvas.Scale * 100:0}%" + (settings.UiScale > 0 ? "" : " (자동)"), x + 16, row + 3, 130, 22, 14, Canvas.White);
        if (canvas.Button("−", x + 150, row, 36, 26, canvas.Scale > 0.75f, 16)) { settings.UiScale = Math.Max(0.75, Math.Round(canvas.Scale * 4 - 1) / 4); voyage.Data.SaveSettings(); }
        if (canvas.Button("+", x + 190, row, 36, 26, canvas.Scale < 3, 16)) { settings.UiScale = Math.Min(3, Math.Round(canvas.Scale * 4 + 1) / 4); voyage.Data.SaveSettings(); }
        if (canvas.Button("자동", x + 230, row, 54, 26, settings.UiScale > 0, 13)) { settings.UiScale = 0; voyage.Data.SaveSettings(); }
        // 배경음 크기
        row += 34;
        canvas.Text($"배경음 {settings.MusicVolume * 100:0}%", x + 16, row + 3, 120, 22, 14, Canvas.White);
        if (canvas.Button("−", x + 140, row, 40, 26, settings.MusicVolume > 0, 16)) { settings.MusicVolume = Math.Max(0, Math.Round(settings.MusicVolume - 0.1, 1)); voyage.Data.SaveSettings(); }
        if (canvas.Button("+", x + 186, row, 40, 26, settings.MusicVolume < 1, 16)) { settings.MusicVolume = Math.Min(1, Math.Round(settings.MusicVolume + 0.1, 1)); voyage.Data.SaveSettings(); }
        if (canvas.Button("닫기", x + w - 116, y + h - 40, 100, 30)) _displayOpen = false;
    }

    // ── 왼쪽 위 ──────────────────────────────────────────────────────────────

    private void Status()
    {
        if (voyage.Mode == Mode.Port)
        {
            canvas.Text(voyage.TownView ? $"{voyage.City.Name} 시내" : $"{voyage.City.Name} 항구", 10, 6, 400, 28, 19, Canvas.White);
            canvas.Text($"{voyage.Money:N0} Ð", 30, 34, 300, 24, 17, Canvas.White);
            canvas.Text(voyage.PlayerName, 44, 60, 300, 24, 17, Canvas.White);
            Bars(44, 90);
        }
        else
        {
            canvas.Text($"{voyage.Money:N0} Ð", 10, 36, 300, 24, 17, Canvas.White);
            canvas.Fill(4, 66, 150, 82, new Color4(0.03f, 0.05f, 0.20f, 0.75f));
            canvas.Frame(4, 66, 150, 82, new Color4(0.4f, 0.45f, 0.7f, 1), 1);
            canvas.Text(voyage.SeaName, 10, 68, 140, 24, 17, Canvas.White);
            canvas.Text($"항해일수:{voyage.DaysAtSea}일", 10, 94, 140, 24, 16, Canvas.White);
            bool storm = voyage.Weather == Weather.Storm;
            canvas.Text((voyage.Weather == Weather.Clear ? "☀" : storm ? "⚡" : "☁") + voyage.WeatherName, 10, 120, 140, 24, 16,
                        storm ? new Color4(1f, 0.45f, 0.4f, 1) : Canvas.Gold);
            Bars(162, 74);
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
        float y = 156;
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

    private ImageSet? _commandIcons;

    /// <summary>
    /// 원본 명령 단추 — <c>0010\0001\sg</c>(64 × 28). 세 장이 한 벌이다: 꺼짐(잿빛) · 켜짐 · 가리킴.
    /// <paramref name="icon"/> 은 꺼짐 장의 id.
    /// </summary>
    private bool Command(int icon, float x, float y, bool enabled, string label)
    {
        const float w = 64, h = 28;
        bool hover = enabled && canvas.Hover(x, y, w, h);
        int id = icon + (hover ? 2 : enabled ? 1 : 0);
        if (!canvas.Image($"sg{id}", () => (_commandIcons ??= new ImageSet(@"0010\0001\sg")).Pixels(0, id), x, y, w, h))
            return canvas.Button(label, x, y, w, h, enabled, 11);
        if (canvas.Hover(x, y, w, h)) _tip = (label, x + w / 2, y);
        if (!hover || !canvas.Pointer.Clicked) return false;
        canvas.Pointer.Consumed = true;
        return true;
    }

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
            (265, "조선소", voyage.HasShipyard, () => voyage.Dialog = Dialog.Shipyard),
            (13, "의뢰 내용", voyage.Quest != null, () => voyage.Dialog = Dialog.QuestDetail),
            (154, "스킬 (X)", true, () => voyage.Dialog = Dialog.Skills),
            (43, "부관", true, () => voyage.Dialog = Dialog.Aides),
            (208, "왕궁 (칙명)", voyage.AtCourt || voyage.Order != null, () => voyage.Dialog = Dialog.Court),
        ];
        const float w = 300;
        float h = 44 + (buttons.Length + 3) / 4 * 34 + 28;
        float x = canvas.Width - w - 6, y = canvas.Height - h - 6;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text("항구", x + 10, y + 6, 100, 24, 17, Canvas.White);
        canvas.Text($"{voyage.Ship.Name}   모험 {voyage.AdventureExp} · 명성 {voyage.AdventureFame}   교역 {voyage.TradeExp}", x + 12, y + h - 26, w - 24, 22, 13, Canvas.Dim);
        if (voyage.Dialog != Dialog.None) return;

        for (int i = 0; i < buttons.Length; i++)
            if (Command(buttons[i].Icon, x + 12 + i % 4 * 70, y + 38 + i / 4 * 34, buttons[i].Enabled, buttons[i].Label))
                buttons[i].Run();
    }

    /// <summary>시내의 걷는 면과 지금 선 자리 — 창이 프레임마다 넣는다. 없으면 내려다보기만 한다.</summary>
    public TownGrid? TownGrid;
    public System.Numerics.Vector2 TownSpot;

    /// <summary>시내 — 도시가 어떤 곳인지, 들를 수 있는 곳, 걷는 면으로 그린 작은 지도.</summary>
    private void TownPanel()
    {
        var city = voyage.City;
        var buildings = city.Buildings.Split(", ", StringSplitOptions.RemoveEmptyEntries);
        // 원본에서는 시내에 선 사람에게 말을 건다. 사람은 못 세워서 단추로 대신한다(지도의 표식을 눌러 걸어가도 열린다).
        (string Label, bool Enabled, Action Run)[] buttons =
        [
            (voyage.GuildName, true, () => voyage.Dialog = Dialog.Guild),
            ("교역소", true, () => voyage.Dialog = Dialog.Trade),
            ("조선소", voyage.HasShipyard, () => voyage.Dialog = Dialog.Shipyard),
            ("항구로", true, () => voyage.TownView = false),
        ];
        const float w = 300, bw = 136, bh = 30;
        float h = 116 + Math.Max(1, buildings.Length) * 20 + 10 + 2 * (bh + 6) + 6;
        float x = canvas.Width - w - 6, y = canvas.Height - h - 6;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text(city.Name, x + 10, y + 6, w - 20, 26, 18, Canvas.Gold, 0, true);
        canvas.Text(voyage.CityFacts(city), x + 10, y + 34, w - 20, 56, 13, Canvas.Dim);
        canvas.Text("건물", x + 10, y + 94, 100, 20, 13, Canvas.Gold);
        float row = y + 114;
        if (buildings.Length == 0) canvas.Text("들어갈 수 있는 건물이 없다.", x + 10, row, w - 20, 20, 13, Canvas.Dim);
        foreach (string building in buildings)
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
    /// <summary>시내 지도를 펼쳤는가(Ctrl+5).</summary>
    public bool TownMapOpen = true;
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
        if (TownGrid is not { } grid) return;
        var city = voyage.City;
        if (voyage.TownMap is not { } map)
        {
            // 걷는 면 그림 위에 선 자리
            float mapWidth = 250, mapHeight = mapWidth * grid.Height / grid.Width;
            float gx = canvas.Width - mapWidth - 10, gy = 10;
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
        float mx = canvas.Width - w - 10, my = TownMapOpen ? 34 : 8;
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
        canvas.Text(TownMapOpen ? "Ctrl+5 지도 접기 · 표식을 누르면 그리로 간다" : "Ctrl+5 지도 펴기", mx, my + h + 4, w, 18, 12, Canvas.Dim, 2);
    }

    private UiParts? _parts;
    private ImageSet? _skillIcons, _goodIcons;
    private readonly Dictionary<int, (int Width, int Height, byte[] Bgra)?> _partPixels = new();
    private readonly byte[] _seaMapPixels = new byte[MapPixels * MapPixels * 4];
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
    /// 나침반 바늘(북), 초록 세모(내 배), 바람 화살. 북이 위다.
    /// </summary>
    private void SeaMap()
    {
        const float size = 208, radius = size / 2;
        const int seaTile = 154, cloud = 158, mask = 159, needle = 162, ship = 163;
        double reach = voyage.SurveyReach;            // 세계 좌표 반지름 — 측량 랭크만큼 넓어진다
        float cx = canvas.Width - radius - 26, cy = canvas.Height - radius - 50;

        // 그림 한 장으로 짓는다: 가림판의 알파 안쪽에 바다 무늬·뭍·구름
        var round = Part(mask);
        var waves = Part(seaTile + (int)(voyage.Clock * 2.5) % 4);
        var clouds = Part(cloud);
        double step = reach * 2 / MapPixels;
        int drift = (int)(voyage.Clock * 3), slideX = (int)(voyage.ShipX / step), slideY = (int)(voyage.ShipY / step);
        for (int j = 0; j < MapPixels; j++)
        for (int i = 0; i < MapPixels; i++)
        {
            int at = (j * MapPixels + i) * 4;
            float dx = (i + 0.5f - MapPixels / 2f) / (MapPixels / 2f), dy = (j + 0.5f - MapPixels / 2f) / (MapPixels / 2f);
            byte alpha = round is { } m ? m.Bgra[(j * m.Height / MapPixels * m.Width + i * m.Width / MapPixels) * 4 + 3]
                       : (byte)(dx * dx + dy * dy <= 1 ? 255 : 0);
            if (alpha == 0) { _seaMapPixels[at + 3] = 0; continue; }

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
            else (b, g, r) = (150f, 90f, 40f);
            if (clouds is { } c)
            {
                // 구름은 날씨가 궂을수록 짙다
                float cover = voyage.Weather switch { Weather.Clear => 0.25f, Weather.Cloudy => 0.6f, _ => 0.85f };
                float veil = c.Bgra[(((j + slideY + drift / 2) % c.Height + c.Height) % c.Height * c.Width + ((i + slideX + drift) % c.Width + c.Width) % c.Width) * 4 + 3] / 255f * cover;
                (b, g, r) = (b + (235 - b) * veil, g + (235 - g) * veil, r + (235 - r) * veil);
            }
            (_seaMapPixels[at], _seaMapPixels[at + 1], _seaMapPixels[at + 2], _seaMapPixels[at + 3]) = ((byte)b, (byte)g, (byte)r, alpha);
        }
        canvas.Circle(cx, cy, radius + 5, new Color4(0.04f, 0.06f, 0.18f, 0.9f));
        canvas.Image("seamap", () => (MapPixels, MapPixels, (byte[])_seaMapPixels.Clone()), cx - radius, cy - radius, size, size, 0, true);
        canvas.Circle(cx, cy, radius + 1, Canvas.PanelEdge, false, 2.5f);
        canvas.Circle(cx, cy, radius + 5, new Color4(0.45f, 0.38f, 0.2f, 1), false, 1);

        void Mark(double wx, double wy, Color4 color, float dot)
        {
            double dx = WorldMap.DeltaX(voyage.ShipX, wx) / reach, dy = (wy - voyage.ShipY) / reach;
            if (dx * dx + dy * dy > 0.94) return;
            canvas.Circle(cx + (float)dx * radius, cy + (float)dy * radius, dot + 1, new Color4(0, 0, 0, 0.7f));
            canvas.Circle(cx + (float)dx * radius, cy + (float)dy * radius, dot, color);
        }

        foreach (var city in voyage.Data.Cities) Mark(city.SeaX, city.SeaY, new Color4(1f, 0.35f, 0.3f, 1), 3);
        if (voyage.QuestStage == QuestStage.Accepted && voyage.QuestLanding is { X: not 0 } site)
            Mark(site.X, site.Y, Canvas.Gold, 4);

        // 내 배: 초록 세모가 뱃머리 쪽을 가리킨다(조각은 위를 본다)
        float heading = (float)voyage.Heading;
        if (Part(ship) != null) PartImage(ship, cx - 8, cy - 8, 16, 16, heading);
        else canvas.Line(cx, cy, cx + MathF.Sin(heading) * 12, cy - MathF.Cos(heading) * 12, Canvas.White, 2);

        // 나침반: 지도 안 왼쪽 위의 바늘 — 긴 쪽이 북
        float nx = cx - radius * 0.56f, ny = cy - radius * 0.56f;
        canvas.Circle(nx, ny, 25, new Color4(0.04f, 0.06f, 0.18f, 0.6f));
        canvas.Circle(nx, ny, 25, Canvas.PanelEdge, false, 1.5f);
        PartImage(needle, nx - 24, ny - 24, 48, 48);
        canvas.Text("N", nx - 10, ny - 41, 20, 16, 11, Canvas.Gold, 1, true);

        // 바람: 테두리 바깥, 불어오는 쪽에서 안쪽을 가리키는 화살촉(조각은 오른쪽을 본다)
        float wind = (float)voyage.WindDirection;
        float sx = MathF.Sin(wind), sy = -MathF.Cos(wind);
        float ax = cx - sx * (radius + 17), ay = cy - sy * (radius + 17);
        var breeze = new Color4(0.75f, 0.92f, 1f, 1);
        canvas.Line(ax - sx * 13, ay - sy * 13, ax + sx * 9, ay + sy * 9, new Color4(0, 0, 0, 0.8f), 5);
        canvas.Line(ax - sx * 13, ay - sy * 13, ax + sx * 9, ay + sy * 9, breeze, 2.5f);
        canvas.Line(ax + sx * 10, ay + sy * 10, ax + sx * 2 - sy * 6, ay + sy * 2 + sx * 6, breeze, 2.5f);
        canvas.Line(ax + sx * 10, ay + sy * 10, ax + sx * 2 + sy * 6, ay + sy * 2 - sx * 6, breeze, 2.5f);

        // 지도 밑: 속도와 돛
        canvas.Text($"{voyage.Knots:0.0} 노트", cx - radius, cy + radius + 10, size * 0.5f, 22, 16, Canvas.White, 0, true);
        canvas.Text(SailNames[voyage.Sail], cx, cy + radius + 12, radius, 20, 14, Canvas.Dim, 2);
        if (voyage.CanSurvey) canvas.Text($"{voyage.ShipX:0}, {voyage.ShipY:0}", cx - 10, cy - radius - 28, radius + 10, 20, 13, Canvas.Dim, 2);
        canvas.Block(cx - radius - 6, cy - radius - 6, size + 12, size + 12);
    }

    /// <summary>바다에서 눌러 쓰는 스킬들 — 기록 창 오른쪽에 아이콘(<c>0010\0001\sa</c>, id = 스킬 번호)으로 늘어놓는다.</summary>
    private void SeaSkillBar()
    {
        var skills = voyage.SeaSkills().ToList();
        const float w = 54, h = 62;
        float x = 636, y = canvas.Height - h - 6;
        for (int i = 0; i < skills.Count; i++, x += w + 4)
        {
            var rule = skills[i];
            string? blocker = voyage.SkillBlocker(rule);
            bool hover = canvas.Hover(x, y, w, h);
            canvas.Fill(x, y, w, h, hover ? new Color4(0.22f, 0.32f, 0.66f, 0.95f) : Canvas.PanelFill);
            canvas.Frame(x, y, w, h, hover ? Canvas.Gold : Canvas.PanelEdge, 1.2f);
            int id = rule.SkillId;
            canvas.Image($"sa{id}", () => (_skillIcons ??= new ImageSet(@"0010\0001\sa")).Pixels(0, id), x + 9, y + 3, 36, 42, 0, false, blocker == null ? 1 : 0.45f);
            canvas.Text(voyage.SkillName(id), x - 4, y + h - 17, w + 8, 16, 10.5f, blocker == null ? Canvas.White : Canvas.Dim, 1);
            double share = voyage.SkillWaitShare(rule);
            if (share > 0) canvas.Fill(x + 1, y + 1 + (h - 2) * (float)(1 - share), w - 2, (h - 2) * (float)share, new Color4(0, 0, 0, 0.55f));
            canvas.Block(x, y, w, h);
            if (hover)
            {
                canvas.Fill(x - 40, y - 28, 250, 24, Canvas.PanelFill);
                canvas.Text($"{voyage.SkillName(id)} R{voyage.Rank(id)}" + (blocker == null ? "" : $" — {blocker}"), x - 34, y - 26, 240, 20, 13, Canvas.White);
                if (canvas.Pointer.Clicked && voyage.Dialog == Dialog.None) voyage.UseSkill(rule);
            }
        }
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
        if (voyage.Dialog == Dialog.None) return;
        if (voyage.Dialog == Dialog.Skills) { SkillWindow(); return; }
        if (voyage.Dialog == Dialog.Shipyard) { ShipyardWindow(); return; }
        if (voyage.Dialog == Dialog.Items) { ItemWindow(); return; }
        if (voyage.Dialog == Dialog.ShipParts) { ShipPartWindow(); return; }
        if (voyage.Dialog == Dialog.Aides) { AideWindow(); return; }
        if (voyage.Dialog == Dialog.Court) { CourtWindow(); return; }
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
                ShipSwap(x, y, w, h, Title);
                if (Close()) voyage.Dialog = Dialog.None;
                break;

            case Dialog.Trade:
                Trade(x, y, w, h, Title);
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
            if (canvas.Button(button, x + w - 190, row, 170, 25, enabled, 13)) buy();
            row += 28;
        }

        Row("물", $"{voyage.Water:0} / {rules.MaxWater}", $"10통 싣기 ({voyage.WaterPrice * 10:N0})", voyage.Water < rules.MaxWater, () => voyage.BuyWater(10));
        Row("식량", $"{voyage.Food:0} / {rules.MaxFood}", $"10통 싣기 ({voyage.FoodPrice * 10:N0})", voyage.Food < rules.MaxFood, () => voyage.BuyFood(10));
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
    private int _shipPage, _tradePage;
    private bool _selling;

    /// <summary>
    /// 스킬 창(X) — 원본의 짜임을 따른다: 위에 쪽지(습득 · 모험 · 교역 · 전투 · 언어), 왼쪽에 아이콘·이름·랭크·숙련도,
    /// 오른쪽에 고른 스킬의 설명과 상태. 아이콘은 <c>0010\0001\sa</c>(id = 스킬 번호).
    /// </summary>
    private void SkillWindow()
    {
        string[] tabs = ["습득", "모험", "교역", "전투", "언어"];
        const float w = 900, h = 440, listWidth = 520;
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
        const int perPage = 9;
        int pages = Math.Max(1, (list.Count + perPage - 1) / perPage);
        _skillPage = Math.Clamp(_skillPage, 0, pages - 1);
        _skillChosen = Math.Clamp(_skillChosen, 0, Math.Max(0, list.Count - 1));

        float row = y + 16;
        for (int i = _skillPage * perPage; i < Math.Min(list.Count, (_skillPage + 1) * perPage); i++, row += 38)
        {
            var skill = list[i];
            int rank = voyage.Rank(skill.Id);
            bool chosen = i == _skillChosen;
            if (chosen) canvas.Fill(x + 14, row, listWidth - 8, 36, new Color4(0.12f, 0.55f, 0.52f, 0.95f));
            else if (canvas.Hover(x + 14, row, listWidth - 8, 36)) canvas.Fill(x + 14, row, listWidth - 8, 36, new Color4(0.2f, 0.3f, 0.6f, 0.6f));
            if (canvas.Hover(x + 14, row, listWidth - 8, 36) && canvas.Pointer.Clicked) _skillChosen = i;
            SkillIcon(skill.Id, x + 18, row + 2, rank > 0 ? 1 : 0.4f);
            canvas.Text(skill.Name, x + 56, row + 7, 190, 24, 16, rank > 0 ? Canvas.White : Canvas.Dim);
            if (rank > 0)
            {
                canvas.Text($"Rank {rank,2}", x + 250, row + 7, 90, 24, 16, Canvas.White);
                bool top = rank >= voyage.Data.Settings.MaxSkillRank;
                canvas.Text(top ? "※최대 랭크" : $"{voyage.Skills[skill.Id].Exp:0}/{voyage.ExpToNext(rank)}", x + 340, row + 7, 170, 24, 16, Canvas.White, 2);
            }
            else canvas.Text($"{skill.Cost:N0} Ð", x + 340, row + 7, 170, 24, 15, Canvas.Dim, 2);
        }
        if (list.Count == 0) canvas.Text("익힌 스킬이 없다.", x + 24, y + 24, 300, 24, 16, Canvas.Dim);
        canvas.Text($"습득수 {voyage.Skills.Count}   ({_skillPage + 1}/{pages})", x + 250, y + h - 78, 270, 22, 15, Canvas.White, 2);
        if (canvas.Button("◀", x + 20, y + h - 50, 40, 34, _skillPage > 0)) (_skillPage, _skillChosen) = (_skillPage - 1, (_skillPage - 1) * perPage);
        if (canvas.Button("▶", x + 66, y + h - 50, 40, 34, _skillPage < pages - 1)) (_skillPage, _skillChosen) = (_skillPage + 1, (_skillPage + 1) * perPage);
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
            canvas.Text($"{skill.Cost:N0} 두캇 — 항구에서 배운다", rx + 10, y + 284, rw - 10, 22, 15, Canvas.White);
            if (rank == 0 && rule != null &&
                canvas.Button($"배우기 ({skill.Cost:N0})", rx, y + 320, 200, 32, voyage.Mode == Mode.Port && voyage.Money >= skill.Cost, 14))
                voyage.Learn(skill);
        }
        canvas.Text($"소지금 {voyage.Money:N0} Ð", rx, y + h - 78, rw, 22, 14, Canvas.Dim, 2);
        if (canvas.Button("이전", x + w - 130, y + h - 50, 110, 34)) voyage.Dialog = Dialog.None;
    }

    private void SkillIcon(int id, float x, float y, float opacity) =>
        canvas.Image($"sa{id}", () => (_skillIcons ??= new ImageSet(@"0010\0001\sa")).Pixels(0, id), x, y, 28, 33, 0, false, opacity);

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
    private int _itemTab, _itemPage;

    /// <summary>소지품 창(I) — 가진 것을 쓰는 쪽지와, 전직증을 마음대로 넣는 「아이템 추가」 쪽지.</summary>
    private void ItemWindow()
    {
        string[] tabs = ["소지품", "도구점", "아이템 추가"];
        if (voyage.ItemShopOpen) (voyage.ItemShopOpen, _itemTab, _itemPage) = (false, 1, 0);
        const float w = 640, h = 440;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
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
        canvas.Text($"직업: {voyage.JobName}", x + 20, y + 12, w - 40, 24, 16, Canvas.Gold, 0, true);

        const int perPage = 10;
        float row = y + 46;
        if (_itemTab == 0)
        {
            var items = voyage.Items.OrderBy(i => i.Key).ToList();
            int pages = Math.Max(1, (items.Count + perPage - 1) / perPage);
            _itemPage = Math.Clamp(_itemPage, 0, pages - 1);
            foreach (var (id, count) in items.Skip(_itemPage * perPage).Take(perPage))
            {
                canvas.Text($"{voyage.ItemName(id)} × {count}", x + 20, row + 3, 250, 24, 15, Canvas.White);
                canvas.Text(voyage.ItemNote(id), x + 250, row + 5, 250, 22, 12, Canvas.Dim);
                if (canvas.Button("사용", x + w - 110, row, 90, 26, true, 13)) voyage.UseItem(id);
                row += 32;
            }
            if (items.Count == 0) canvas.Text("가진 것이 없다. 「도구점」에서 사거나 「아이템 추가」에서 전직증을 넣는다.", x + 20, row, w - 40, 24, 15, Canvas.Dim);
            Pages(pages);
        }
        else if (_itemTab == 1)
        {
            bool open = voyage.Mode == Mode.Port && voyage.HasItemShop;
            if (!open) { canvas.Text(voyage.Mode == Mode.Port ? "이 도시에는 도구점이 없다." : "도구점은 항구에서 연다.", x + 20, row, w - 40, 24, 15, Canvas.Dim); row += 30; }
            foreach (var item in voyage.Data.Items)
            {
                canvas.Text(item.Name, x + 20, row + 3, 200, 24, 15, Canvas.White);
                canvas.Text($"{voyage.ItemNote(item.Id)}   가진 수 {voyage.Items.GetValueOrDefault(item.Id)}", x + 200, row + 5, 300, 22, 12, Canvas.Dim);
                if (canvas.Button($"사기 ({item.Price:N0})", x + w - 140, row, 120, 26, open && voyage.Money >= item.Price, 13)) voyage.BuyItem(item);
                row += 32;
            }
            canvas.Text($"소지금 {voyage.Money:N0} Ð", x + 200, y + h - 43, 250, 22, 14, Canvas.Dim);
        }
        else
        {
            var jobs = voyage.JobsToTake().ToList();
            int pages = Math.Max(1, (jobs.Count + perPage - 1) / perPage);
            _itemPage = Math.Clamp(_itemPage, 0, pages - 1);
            foreach (var job in jobs.Skip(_itemPage * perPage).Take(perPage))
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
        void Cell(string label, string value, float cx, float cy, float cw = 122)
        {
            canvas.Fill(cx, cy, cw, 26, new Color4(0.04f, 0.07f, 0.2f, 0.85f));
            canvas.Text(label, cx + 6, cy + 3, cw, 20, 13, Canvas.Dim);
            canvas.Text(value, cx, cy + 2, cw - 6, 22, 15, Canvas.White, 2);
        }

        Header("판매선박", x + 20, y + 16, listWidth - 20);
        var ships = voyage.ShipsForSale();
        const int perPage = 7;
        int pages = Math.Max(1, (ships.Count + perPage - 1) / perPage);
        _shipPage = Math.Clamp(_shipPage, 0, pages - 1);
        _shipChosen = Math.Clamp(_shipChosen, 0, Math.Max(0, ships.Count - 1));
        float row = y + 50;
        for (int i = _shipPage * perPage; i < Math.Min(ships.Count, (_shipPage + 1) * perPage); i++, row += 48)
        {
            bool chosen = i == _shipChosen, hover = canvas.Hover(x + 20, row, listWidth - 20, 46);
            if (chosen) canvas.Fill(x + 20, row, listWidth - 20, 46, new Color4(0.12f, 0.55f, 0.52f, 0.95f));
            else if (hover) canvas.Fill(x + 20, row, listWidth - 20, 46, new Color4(0.2f, 0.3f, 0.6f, 0.6f));
            if (hover && canvas.Pointer.Clicked) _shipChosen = i;
            canvas.Text(ships[i].Name, x + 34, row + 3, listWidth - 50, 22, 16, Canvas.White);
            canvas.Text($"{voyage.ShipCost(ships[i]):N0} Ð", x + 34, row + 22, listWidth - 50, 22, 16, Canvas.White, 2);
        }
        if (ships.Count == 0) canvas.Text("이 조선소는 팔 배가 없다.", x + 30, y + 60, 300, 24, 15, Canvas.Dim);
        if (canvas.Button("◀", x + 20, y + h - 50, 40, 34, _shipPage > 0)) (_shipPage, _shipChosen) = (_shipPage - 1, (_shipPage - 1) * perPage);
        canvas.Text($"{_shipPage + 1} / {pages}", x + 64, y + h - 43, 60, 24, 15, Canvas.White, 1);
        if (canvas.Button("▶", x + 128, y + h - 50, 40, 34, _shipPage < pages - 1)) (_shipPage, _shipChosen) = (_shipPage + 1, (_shipPage + 1) * perPage);
        canvas.Line(x + listWidth + 14, y + 14, x + listWidth + 14, y + h - 14, new Color4(0.5f, 0.55f, 0.75f, 0.8f), 1);

        float rx = x + listWidth + 30, rw = w - listWidth - 50;
        if (ships.Count > 0)
        {
            var ship = ships[_shipChosen];
            var s = Dho.Data.ShipStats.Of(ship, voyage.Data.Settings.Ships);
            string[] sizes = ["소형", "소형", "중형", "대형", "초대형"];
            canvas.Text($"{ship.Name}({sizes[Math.Clamp(ship.SizeClass, 0, 4)]})", rx, y + 14, rw, 26, 18, Canvas.White, 0, true);
            Cell("Grade", ship.Kind switch { 2 => "갤리", 4 => "증기선", _ => "범용함" }, rx, y + 46, 300);

            Header("기본성능", rx, y + 84);
            Header("내구력", rx + 310, y + 84, 140);
            Cell("세로돛", $"{s.VerticalSail}", rx, y + 114, 96);
            Cell("가로돛", $"{s.HorizontalSail}", rx + 100, y + 114, 96);
            Cell("조력", $"{s.Rowing}", rx + 200, y + 114, 96);
            Cell("선회", $"{s.Turn}", rx, y + 144, 96);
            Cell("내파", $"{s.WaveResist}", rx + 100, y + 144, 96);
            Cell("장갑", $"{s.Armor}", rx + 200, y + 144, 96);
            Cell("", $"{s.Durability} / {s.Durability}", rx + 310, y + 114, 140);
            canvas.Fill(rx + 310, y + 142, 140, 4, new Color4(0.90f, 0.35f, 0.40f, 1));

            Header("선창 정보", rx, y + 186);
            Cell("선원", $"{s.MinCrew} / {s.MaxCrew}", rx, y + 216, 146);
            Cell("대포", $"0 / {s.Guns}", rx + 152, y + 216, 146);
            Cell("창고", $"0 / {s.Hold}", rx + 304, y + 216, 146);

            Header("운항", rx, y + 258);
            canvas.Text($"속도 {s.Knots:0.0}노트 · 돛대 {ship.Masts} · 길이 {ship.Length} 폭 {ship.Width}", rx + 4, y + 288, rw, 22, 14, Canvas.White);
            string? blocker = voyage.ShipBlocker(ship);
            if (blocker != null) canvas.Text(blocker, rx + 4, y + 312, rw, 22, 14, new Color4(1f, 0.5f, 0.45f, 1));

            Cell("구입 가격", $"{voyage.ShipCost(ship):N0} Ð", rx + 150, y + h - 118, 300);
            Cell("소지금", $"{voyage.Money:N0} Ð", rx + 150, y + h - 88, 300);
            if (canvas.Button("확인", x + w - 260, y + h - 50, 110, 34, blocker == null)) voyage.BuyShip(ship);
        }
        canvas.Text($"타고 있는 배: {voyage.Ship.Name} · 부두 {voyage.Dock.Count}/{Voyage.DockSlots}", rx, y + h - 148, rw, 20, 13, Canvas.Gold);
        if (canvas.Button("선박부품", rx, y + h - 50, 120, 34)) (voyage.Dialog, _partPage) = (Dialog.ShipParts, 0);
        if (canvas.Button("이전", x + w - 140, y + h - 50, 110, 34)) voyage.Dialog = Dialog.None;
    }

    private int _partPage;

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
        if (canvas.Button("이전", x + w - 130, y + h - 50, 110, 34)) voyage.Dialog = Dialog.None;
    }

    /// <summary>부관 — 고용한 사람들의 담당 · 레벨과, 이 도시의 주점에서 만날 수 있는 후보.</summary>
    private void AideWindow()
    {
        const float w = 700, h = 400;
        float x = (canvas.Width - w) / 2, y = (canvas.Height - h) / 2;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text($"부관     {voyage.Aides.Count}/{Voyage.AideSlots}     소지금 {voyage.Money:N0} Ð", x + 20, y + 12, w - 40, 26, 18, Canvas.Gold, 0, true);
        float row = y + 52;
        foreach (var aide in voyage.Aides.ToList())
        {
            canvas.Text(aide.Who.Name, x + 20, row + 3, 130, 24, 16, Canvas.White);
            canvas.Text($"Lv {aide.Level}", x + 150, row + 4, 60, 22, 14, Canvas.Gold);
            if (canvas.Button(voyage.DutyName(aide.Duty), x + 210, row, 110, 26, true, 13)) voyage.NextDuty(aide);
            canvas.Text($"{Voyage.DutyNote(aide.Duty)} · 급여 하루 {voyage.AidePay(aide)}", x + 330, row + 5, 240, 20, 12, Canvas.Dim);
            if (canvas.Button("해고", x + w - 90, row, 70, 26, voyage.Mode == Mode.Port, 13)) voyage.DismissAide(aide);
            row += 32;
        }
        if (voyage.Aides.Count == 0) { canvas.Text("고용한 부관이 없다.", x + 20, row + 3, w - 40, 24, 15, Canvas.Dim); row += 32; }
        canvas.Text("담당 단추를 누르면 다음 담당으로 바뀐다.", x + 20, row, w - 40, 20, 12, Canvas.Dim);
        row += 26;
        canvas.Line(x + 16, row, x + w - 16, row, new Color4(0.5f, 0.55f, 0.75f, 0.8f), 1);
        row += 10;
        canvas.Text("주점에서 만난 사람", x + 20, row, 300, 22, 15, Canvas.Gold, 0, true);
        row += 28;
        if (voyage.Mode != Mode.Port) canvas.Text("항구에서만 고용한다.", x + 20, row, w - 40, 22, 14, Canvas.Dim);
        else if (!voyage.HasTavern) canvas.Text("이 도시에는 주점이 없다.", x + 20, row, w - 40, 22, 14, Canvas.Dim);
        else
            foreach (var who in voyage.AidesToHire())
            {
                canvas.Text(who.Name, x + 20, row + 3, 200, 24, 16, Canvas.White);
                if (canvas.Button($"고용 ({voyage.AideCost(who):N0})", x + w - 190, row, 170, 26, voyage.AideBlocker(who) == null, 13)) voyage.HireAide(who);
                row += 32;
            }
        if (canvas.Button("이전", x + w - 130, y + h - 50, 110, 34)) voyage.Dialog = Dialog.None;
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
        foreach (var part in voyage.Parts.ToList())
        {
            canvas.Text($"[{Voyage.SlotName[part.Slot]}] {part.Name}", x + 20, row + 3, 300, 22, 14, Canvas.White);
            canvas.Text(voyage.PartNote(part), x + 330, row + 4, 220, 20, 12, Canvas.Dim);
            if (canvas.Button($"떼어 팔기 ({voyage.PartPrice(part) / 2:N0})", x + w - 190, row, 170, 24, true, 12)) voyage.SellPart(part);
            row += 27;
        }
        if (voyage.Parts.Count == 0) { canvas.Text("단 부품이 없다. (보조돛 둘 · 장갑 하나 · 선수상 하나)", x + 20, row + 3, w - 40, 22, 14, Canvas.Dim); row += 27; }
        canvas.Line(x + 16, row + 4, x + w - 16, row + 4, new Color4(0.5f, 0.55f, 0.75f, 0.8f), 1);
        row += 12;

        var sale = voyage.PartsForSale();
        int perPage = Math.Max(1, (int)((y + h - 60 - row) / 27));
        int pages = Math.Max(1, (sale.Count + perPage - 1) / perPage);
        _partPage = Math.Clamp(_partPage, 0, pages - 1);
        foreach (var part in sale.Skip(_partPage * perPage).Take(perPage))
        {
            canvas.Text($"[{Voyage.SlotName[part.Slot]}] {part.Name}", x + 20, row + 3, 300, 22, 14, Canvas.White);
            canvas.Text(voyage.PartNote(part), x + 330, row + 4, 220, 20, 12, Canvas.Dim);
            if (canvas.Button($"달기 ({voyage.PartPrice(part):N0})", x + w - 190, row, 170, 24, voyage.PartBlocker(part) == null, 12)) voyage.BuyPart(part);
            row += 27;
        }
        if (canvas.Button("◀", x + 20, y + h - 50, 40, 34, _partPage > 0)) _partPage--;
        canvas.Text($"{_partPage + 1} / {pages}", x + 64, y + h - 43, 60, 24, 15, Canvas.White, 1);
        if (canvas.Button("▶", x + 128, y + h - 50, 40, 34, _partPage < pages - 1)) _partPage++;
        if (canvas.Button("이전", x + w - 140, y + h - 50, 110, 34)) voyage.Dialog = Dialog.Shipyard;
    }

    /// <summary>선박교환 — 부두에 매어 둔 배로 갈아타거나 판다.</summary>
    private void ShipSwap(float x, float y, float w, float h, Action<string> title)
    {
        title("선박교환");
        var stats = voyage.Stats;
        canvas.Text($"타고 있는 배: {voyage.Ship.Name}   내구 {voyage.Durability:0}/{stats.Durability} · 창고 {stats.Hold} · 선원 {stats.MinCrew}~{stats.MaxCrew} · {stats.Knots:0.0}노트",
                    x + 20, y + 48, w - 40, 22, 13, Canvas.Gold);
        float row = y + 80;
        foreach (var docked in voyage.Dock.ToList())
        {
            var s = Dho.Data.ShipStats.Of(docked.Ship, voyage.Data.Settings.Ships);
            canvas.Text(docked.Ship.Name, x + 20, row + 3, 150, 24, 15, Canvas.White);
            canvas.Text($"내구 {docked.Durability:0}/{s.Durability} · 창고 {s.Hold}", x + 160, row + 5, 150, 22, 12, Canvas.Dim);
            if (canvas.Button("갈아타기", x + w - 250, row, 90, 25, voyage.SwapBlocker(docked) == null, 12)) voyage.SwapShip(docked);
            if (canvas.Button($"팔기 ({voyage.DockedPrice(docked):N0})", x + w - 154, row, 134, 25, true, 12)) voyage.SellDocked(docked);
            row += 30;
        }
        if (voyage.Dock.Count == 0)
            canvas.Text("부두에 매어 둔 배가 없다.\n조선소에서 배를 사면 타던 배가 여기에 남는다.", x + 20, row, w - 40, 60, 15, Canvas.Dim);
    }

    /// <summary>교역소 — 사기와 팔기.</summary>
    private void Trade(float x, float y, float w, float h, Action<string> title)
    {
        title($"교역소     소지금 {voyage.Money:N0} Ð");
        canvas.Text($"창고 {voyage.CargoCount} / {voyage.Stats.Hold}", x + w - 180, y + 20, 160, 22, 14, Canvas.Dim, 2);
        if (canvas.Button("사기", x + 20, y + 46, 80, 26, _selling, 13)) (_selling, _tradePage) = (false, 0);
        if (canvas.Button("팔기", x + 106, y + 46, 80, 26, !_selling, 13)) (_selling, _tradePage) = (true, 0);

        const int perPage = 7;
        float row = y + 80;
        if (!_selling)
        {
            var goods = voyage.GoodsHere();
            _tradePage = Pager(x, y, h, _tradePage, Math.Max(1, (goods.Count + perPage - 1) / perPage));
            foreach (var good in goods.Skip(_tradePage * perPage).Take(perPage))
            {
                int price = voyage.BuyPrice(good);
                GoodIcon(good.Id, x + 16, row);
                canvas.Text(good.Name, x + 46, row + 3, 110, 24, 15, Canvas.White);
                canvas.Text($"{price:N0} Ð", x + 140, row + 3, 80, 24, 15, Canvas.Gold, 2);
                int stock = voyage.Stock(good);
                string note = stock > 0 ? $"재고 {stock}" : "품절";
                if (voyage.CanSeeMarket) note += $" · {voyage.MarketPercent(good)}%";
                canvas.Text(note, x + 230, row + 5, 96, 22, 12, Canvas.Dim);
                bool can = stock > 0 && voyage.HoldFree > 0 && voyage.Money >= price;
                if (canvas.Button("10개", x + w - 230, row, 64, 25, can, 12)) voyage.BuyGood(good, 10);
                if (canvas.Button("100개", x + w - 160, row, 64, 25, can, 12)) voyage.BuyGood(good, 100);
                if (canvas.Button("가득", x + w - 90, row, 70, 25, can, 12)) voyage.BuyGood(good, int.MaxValue);
                row += 28;
            }
            if (goods.Count == 0) canvas.Text("이 도시의 교역소는 팔 물건이 없다.", x + 20, row, w - 40, 24, 15, Canvas.Dim);
        }
        else
        {
            var cargo = voyage.Cargo.OrderBy(c => c.Key).ToList();
            _tradePage = Pager(x, y, h, _tradePage, Math.Max(1, (cargo.Count + perPage - 1) / perPage));
            foreach (var (id, item) in cargo.Skip(_tradePage * perPage).Take(perPage))
            {
                if (voyage.Good(id) is not { } good) continue;
                int price = voyage.SellPrice(good);
                long bought = item.Cost / Math.Max(1, item.Count);
                GoodIcon(good.Id, x + 16, row);
                canvas.Text($"{good.Name} × {item.Count}", x + 46, row + 3, 174, 24, 15, Canvas.White);
                canvas.Text($"{price:N0} Ð", x + 210, row + 3, 90, 24, 15, price >= bought ? Canvas.Gold : new Color4(1f, 0.5f, 0.45f, 1), 2);
                string note = $"산 값 {bought:N0}";
                if (voyage.BestNearby(good) is { } best) note += $"   {best.City.Name} {best.Price:N0}";
                canvas.Text(note, x + 306, row + 5, 230, 22, 12, Canvas.Dim);
                if (canvas.Button("10개", x + w - 160, row, 64, 25, true, 12)) voyage.SellGood(good, 10);
                if (canvas.Button("전부", x + w - 90, row, 70, 25, true, 12)) voyage.SellGood(good, int.MaxValue);
                row += 28;
            }
            if (cargo.Count == 0) canvas.Text("실은 교역품이 없다.", x + 20, row, w - 40, 24, 15, Canvas.Dim);
        }
    }

    /// <summary>교역품 아이콘 — <c>0010\0001\sc</c> 의 무리 0, id 가 교역품 번호다(48 × 48).</summary>
    private void GoodIcon(int goodId, float x, float y) =>
        canvas.Image($"sc{goodId}", () => (_goodIcons ??= new ImageSet(@"0010\0001\sc")).Pixels(0, goodId), x, y - 1, 26, 26);

    // ── 캐릭터 만들기 ────────────────────────────────────────────────────────

    private int _step, _nation;
    private string _name = "";
    private StartLineData? _line;
    private bool _male = true;

    /// <summary>이름 칸에 글자를 넣는다(백스페이스는 지운다).</summary>
    public void Type(char c)
    {
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
                canvas.Text("얼굴·머리·체형 고르기는 아직 없다(몸 모형을 그리지 않는다).", x + 20, y + 200, w - 40, 22, 13, Canvas.Dim);
                next = true;
                break;

            case 4:
                var nation = voyage.Data.Nations.Find(n => n.Id == _nation);
                canvas.Text($"이름: {(_name.Trim().Length > 0 ? _name.Trim() : voyage.Data.Settings.PlayerName)}\n나라: {nation?.Name}\n" +
                            $"직업: {voyage.Data.Jobs.Find(j => j.Id == _line!.JobId)?.Name}\n성별: {(_male ? "남자" : "여자")}\n" +
                            $"시작 도시: {voyage.StartCityOf(_nation).Name}",
                            x + 20, y + 90, w - 40, 160, 17, Canvas.White);
                if (canvas.Button("이대로 시작한다", x + w - 200, y + h - 54, 180, 38)) voyage.Create(_name, _nation, _line!, _male);
                break;
        }

        if (_step > 0 && canvas.Button("이전", x + 20, y + h - 54, 100, 38)) _step--;
        if (_step < 4 && canvas.Button("다음", x + w - 120, y + h - 54, 100, 38, next)) _step++;
    }
}