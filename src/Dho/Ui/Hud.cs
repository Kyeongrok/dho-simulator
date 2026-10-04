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
            return;
        }
        Status();
        LogPanel();
        if (voyage.Mode == Mode.Port && voyage.TownView) TownPanel();
        else if (voyage.Mode == Mode.Port) PortPanel();
        else
        {
            SeaMap();
            SeaSkillBar();
            Prompt();
        }
        Dialogs();
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

    private void PortPanel()
    {
        // 단추는 두 줄로 늘어놓는다 — (이름, 켜짐, 하는 일)
        (string Label, bool Enabled, Action Run)[] buttons =
        [
            ("출항", true, voyage.Depart),
            (voyage.GuildName, true, () => voyage.Dialog = Dialog.Guild),
            ("의뢰 내용", voyage.Quest != null, () => voyage.Dialog = Dialog.QuestDetail),
            ("보급", true, () => voyage.Dialog = Dialog.Supply),
            ("스킬", true, () => voyage.Dialog = Dialog.Skills),
            ("조선소", voyage.HasShipyard, () => voyage.Dialog = Dialog.Shipyard),
            ("교역소", true, () => voyage.Dialog = Dialog.Trade),
            ("시내", voyage.City.TownScene != 0, () => voyage.TownView = true),
        ];
        const float w = 300, bw = 136, bh = 32;
        float h = 44 + (buttons.Length + 1) / 2 * (bh + 6) + 28;
        float x = canvas.Width - w - 6, y = canvas.Height - h - 6;
        canvas.Panel(x, y, w, h);
        canvas.Block(x, y, w, h);
        canvas.Text("항구", x + 10, y + 6, 100, 24, 17, Canvas.White);
        if (voyage.Dialog != Dialog.None) return;

        for (int i = 0; i < buttons.Length; i++)
            if (canvas.Button(buttons[i].Label, x + 10 + i % 2 * (bw + 8), y + 38 + i / 2 * (bh + 6), bw, bh, buttons[i].Enabled))
                buttons[i].Run();
        canvas.Text($"{voyage.Ship.Name}   모험 {voyage.AdventureExp} · 명성 {voyage.AdventureFame}   교역 {voyage.TradeExp}", x + 12, y + h - 26, w - 24, 22, 13, Canvas.Dim);
    }

    /// <summary>시내의 걷는 면과 지금 선 자리 — 창이 프레임마다 넣는다. 없으면 내려다보기만 한다.</summary>
    public TownGrid? TownGrid;
    public System.Numerics.Vector2 TownSpot;

    /// <summary>시내 — 도시가 어떤 곳인지, 들를 수 있는 곳, 걷는 면으로 그린 작은 지도.</summary>
    private void TownPanel()
    {
        var city = voyage.City;
        var buildings = city.Buildings.Split(", ", StringSplitOptions.RemoveEmptyEntries);
        // 원본에서는 시내에 선 사람에게 말을 건다. 그 배치가 클라이언트에 없어서 단추로 대신한다.
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

        if (TownGrid is not { } grid) return;
        // 작은 지도: 걷는 면 그림 위에 선 자리
        float mapWidth = 250, mapHeight = mapWidth * grid.Height / grid.Width;
        float mx = canvas.Width - mapWidth - 10, my = 10;
        canvas.Fill(mx - 3, my - 3, mapWidth + 6, mapHeight + 6, Canvas.PanelFill);
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
        }, mx, my, mapWidth, mapHeight);
        canvas.Frame(mx - 3, my - 3, mapWidth + 6, mapHeight + 6, Canvas.PanelEdge);
        float dotX = mx + TownSpot.X / (grid.Width * grid.Cell) * mapWidth, dotY = my + TownSpot.Y / (grid.Height * grid.Cell) * mapHeight;
        canvas.Circle(dotX, dotY, 4.5f, Canvas.White);
        canvas.Circle(dotX, dotY, 3f, new Color4(0.9f, 0.15f, 0.1f, 1f));
        canvas.Text("W A S D 걷기", mx, my + mapHeight + 6, mapWidth, 18, 12, Canvas.Dim, 2);
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

            case Dialog.Skills:
                SkillList(x, y, w, h, Title);
                if (Close()) voyage.Dialog = Dialog.None;
                break;

            case Dialog.Shipyard:
                Shipyard(x, y, w, h, Title);
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
    private void SkillList(float x, float y, float w, float h, Action<string> title)
    {
        title($"스킬     소지금 {voyage.Money:N0} Ð");
        var known = voyage.Skills.OrderBy(s => s.Key).ToList();
        var learnable = voyage.Learnable().ToList();

        const int perPage = 8;
        int rows = known.Count + learnable.Count, pages = Math.Max(1, (rows + perPage - 1) / perPage);
        _skillPage = Math.Clamp(_skillPage, 0, pages - 1);

        float row = y + 50;
        for (int i = _skillPage * perPage; i < Math.Min(rows, (_skillPage + 1) * perPage); i++, row += 28)
        {
            if (i < known.Count)
            {
                var (id, state) = known[i];
                canvas.Text(voyage.SkillName(id), x + 20, row + 3, 150, 24, 15, Canvas.White);
                canvas.Text($"랭크 {state.Rank}", x + 170, row + 3, 80, 24, 15, Canvas.Gold);
                int next = voyage.ExpToNext(state.Rank);
                canvas.Fill(x + 250, row + 10, 180, 6, new Color4(0.1f, 0.1f, 0.15f, 0.8f));
                canvas.Fill(x + 250, row + 10, 180 * (float)Math.Clamp(state.Exp / next, 0, 1), 6, new Color4(0.45f, 0.75f, 1f, 1));
                canvas.Text($"{state.Exp:0} / {next}", x + 440, row + 4, 110, 22, 13, Canvas.Dim);
            }
            else
            {
                var skill = learnable[i - known.Count];
                canvas.Text(skill.Name, x + 20, row + 3, 150, 24, 15, Canvas.Dim);
                string description = skill.Description.Replace('\n', ' ');
                canvas.Text(description.Length > 17 ? description[..17] + "…" : description, x + 150, row + 5, 220, 22, 12, Canvas.Dim);
                if (canvas.Button($"배우기 ({skill.Cost:N0})", x + w - 190, row, 170, 25, voyage.Mode == Mode.Port && voyage.Money >= skill.Cost, 13))
                    voyage.Learn(skill);
            }
        }

        if (pages > 1)
        {
            if (canvas.Button("◀", x + 20, y + h - 50, 40, 34, _skillPage > 0)) _skillPage--;
            canvas.Text($"{_skillPage + 1} / {pages}", x + 64, y + h - 43, 60, 24, 15, Canvas.White, 1);
            if (canvas.Button("▶", x + 128, y + h - 50, 40, 34, _skillPage < pages - 1)) _skillPage++;
        }
    }

    private int _shipPage, _tradePage;
    private bool _selling;

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

    /// <summary>조선소 — 지금 배와, 이 도시가 파는 배.</summary>
    private void Shipyard(float x, float y, float w, float h, Action<string> title)
    {
        title($"조선소     소지금 {voyage.Money:N0} Ð");
        var stats = voyage.Stats;
        canvas.Text($"지금 배: {voyage.Ship.Name}   내구 {stats.Durability} · 창고 {stats.Hold} · 선원 {stats.MinCrew}~{stats.MaxCrew} · {stats.Knots:0.0}노트   (팔면 {stats.SellPrice:N0})",
                    x + 20, y + 48, w - 40, 22, 13, Canvas.Gold);

        var ships = voyage.ShipsForSale();
        const int perPage = 7;
        _shipPage = Pager(x, y, h, _shipPage, Math.Max(1, (ships.Count + perPage - 1) / perPage));
        float row = y + 76;
        foreach (var ship in ships.Skip(_shipPage * perPage).Take(perPage))
        {
            var s = Dho.Data.ShipStats.Of(ship, voyage.Data.Settings.Ships);
            canvas.Text(ship.Name, x + 20, row + 3, 150, 24, 15, Canvas.White);
            canvas.Text($"내구 {s.Durability} · 창고 {s.Hold} · 선원 {s.MaxCrew} · {s.Knots:0.0}노트", x + 160, row + 5, 230, 22, 12, Canvas.Dim);
            int cost = voyage.ShipCost(ship);
            if (canvas.Button($"갈아타기 ({cost:N0})", x + w - 170, row, 150, 25, voyage.ShipBlocker(ship) == null, 12)) voyage.BuyShip(ship);
            row += 28;
        }
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