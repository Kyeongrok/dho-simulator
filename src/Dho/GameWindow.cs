using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Dho.Data;
using Dho.Game;
using Dho.Native;
using Dho.Render;
using Dho.Ui;

namespace Dho;

/// <summary>
/// 창 하나와 게임 루프. 입력을 <see cref="Voyage"/> 에 넘기고, 장면(하늘·바다·뭍·배)과 화면 글을 그린다.
/// </summary>
internal sealed class GameWindow : IDisposable
{
    private const int StartWidth = 1280, StartHeight = 800;
    private const string ClassName = "DhoWindow";

    private IntPtr _hwnd;
    private bool? _imeOn;
    private bool _musicAtSea;

    // 입력기(한글)는 글자를 받는 칸에 입력할 때만 붙인다 — 그 밖에는 한/영 글쇠를 눌러도 입력기 표시가 뜨지 않고 단축키가 그대로 듣는다
    private void SyncIme()
    {
        bool want = _hud != null && (_hud.Typing || !_voyage.Created);
        if (_imeOn == want || _hwnd == IntPtr.Zero) return;
        _imeOn = want;
        Win32.ImmAssociateContextEx(_hwnd, IntPtr.Zero, want ? 0x10u : 0u);
    }
    private static readonly Win32.WndProc StaticWndProcDelegate = StaticWndProcTrampoline;
    private static GameWindow? _active;
    private static ushort _classAtom;
    private bool _running;

    private Gfx _gfx = null!;
    private SceneRenderer _scene = null!;
    private Terrain _terrain = null!;
    private ShipModel _ship = null!;
    private int _shipModel;
    private (ShipModel? Ship, int Pattern, int Tint) _sailShown;
    private PortScene? _port;
    private PortScene? _town;
    private int _townCity;
    private bool _townView;
    private TownGrid? _grid;
    private Vector2 _walk;
    private float _walkYaw;
    private Mesh? _figure;
    private CharacterModel? _character;
    private string _characterLooks = "";
    private float _stride;
    private Vector2 _strideFrom;
    private double _strideUntil;
    private float _strideAmount;
    private bool _sprinting, _routeRuns;
    private float _sight = 1, _viewPitch;
    private float _portDistance, _portPitch;
    private BerthData? _berth;
    private int _portCity;
    private Canvas _canvas = null!;
    private Hud _hud = null!;
    private Voyage _voyage = null!;
    private Dho.Audio.Music? _music;
    private bool _musicOn;

    // 시점: 배를 가운데 두고 도는 카메라
    private float _yaw = 2.75f, _pitch = 0.50f, _distance = 30000f;
    private bool _orbiting, _leftDown;
    private int _orbitMoved;
    private (float X, float Y, float W, float H)? _previewBox;
    private CharacterModel? _maidModel, _previewFigure;
    private (int, int, int, int, int, int, int) _previewLooks;
    private string _previewWear = "";
    private ShipModel? _previewShip;
    private int _previewShipModel;
    private float _previewYaw = 2.3f, _previewPitch = 0.28f, _previewZoom = 1f;
    private bool OverPreview => _previewBox is { } b && _mouseX >= b.X && _mouseX < b.X + b.W && _mouseY >= b.Y && _mouseY < b.Y + b.H;
    private float? _yawGoal;
    private int _mouseX, _mouseY, _dragX, _dragY;
    private bool _clicked;
    private readonly HashSet<int> _keys = [];

    private float _overcast;
    private Dho.Data.SeaColors? _seaColors;
    private Matrix4x4 _viewProjection;
    private Vector3 _eye;

    private readonly Queue<string> _script = new();
    private double _scriptWait;
    private string? _shotPath;

    private readonly bool _scripted, _newGame;

    /// <param name="newGame">이어 하기를 지우고 캐릭터 만들기부터.</param>
    public GameWindow(string? script, bool newGame = false)
    {
        _scripted = script != null;
        _newGame = newGame;
        foreach (var step in (script ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            _script.Enqueue(step);
    }

    // ── 창 열기 · 메시지 펌프 ─────────────────────────────────────────────────

    public void Run()
    {
        RegisterClassOnce();
        CreateNativeWindow();
        Win32.GetClientRect(_hwnd, out var client);

        _gfx = new Gfx(_hwnd, client.Width, client.Height);
        _scene = new SceneRenderer(_gfx);
        _terrain = new Terrain(_gfx);
        var data = GameData.Load();
        // 대본으로 돌릴 때는 이어 하기를 지우지도 적지도 않는다(--new 와 같이 줘도 만들기 화면만 본다)
        if (_newGame && !_scripted) data.DeleteSave();
        _voyage = new Voyage(data, developer: _scripted && !_newGame, scratch: _scripted);
        _shipModel = _voyage.Ship.Model;
        _ship = new ShipModel(_gfx, _shipModel);
        _canvas = new Canvas(_gfx);
        _hud = new Hud(_canvas, _voyage)
        {
            SetDisplay = SetDisplay, WalkTo = WalkTo, JumpTo = JumpTo,
            PlaySound = which => (_sounds ??= new Dho.Audio.SoundEffects()).Play(which),
            SoundCount = bank => (_sounds ??= new Dho.Audio.SoundEffects()).Count(bank),
            Minimize = () => Win32.ShowWindow(_hwnd, Win32.SW_MINIMIZE),
            Quit = () => Win32.PostMessageW(_hwnd, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero),
            PartCount = (frame, part) => CharacterModel.PartsOf(frame).TryGetValue(part, out var list) ? list.Count : 0,
        };
        // 대본으로 돌릴 때는 조용히 — 대본의 music 명령으로 켠다
        _musicOn = !_scripted;
        _music = new Dho.Audio.Music();
        // 대본으로 돌릴 때는 화면 크기를 건드리지 않는다(찍은 그림의 크기가 같아야 견줄 수 있다)
        var settings = data.Settings;
        if (!_scripted && (settings.Fullscreen || settings.WindowWidth != StartWidth || settings.WindowHeight != StartHeight))
            SetDisplay(settings.WindowWidth, settings.WindowHeight, settings.Fullscreen, false);

        Win32.ShowWindow(_hwnd, 5);
        Win32.UpdateWindow(_hwnd);

        _running = true;
        var clock = Stopwatch.StartNew();
        double last = 0;

        while (_running)
        {
            while (Win32.PeekMessageW(out var msg, IntPtr.Zero, 0, 0, Win32.PM_REMOVE))
            {
                if (msg.Message == Win32.WM_QUIT) { _running = false; break; }
                Win32.TranslateMessage(ref msg);
                Win32.DispatchMessageW(ref msg);
            }
            if (!_running) break;

            double now = clock.Elapsed.TotalSeconds;
            double dt = Math.Min(now - last, 0.1);
            last = now;

            // 그리는 크기는 늘 실제 창 안쪽 크기 — 어긋나면 그림이 늘어나 글씨가 뭉개지고 마우스 자리도 안 맞는다
            if (Win32.GetClientRect(_hwnd, out var inside)) _gfx.Resize(inside.Width, inside.Height);
            _hud.Screen = (Win32.GetSystemMetrics(Win32.SM_CXSCREEN), Win32.GetSystemMetrics(Win32.SM_CYSCREEN));
            _hud.Pixels = (_gfx.Width, _gfx.Height);

            RunScript(dt);
            Tick(dt);
            Render();
            _clicked = false;
        }
        _voyage.Save();
    }

    /// <summary>해상도를 바꾼다 — 창 안쪽 크기를 맞추거나, 테두리 없는 전체 화면으로. 고른 것은 설정에 적는다.</summary>
    private void SetDisplay(int width, int height, bool fullscreen) => SetDisplay(width, height, fullscreen, true);

    private void SetDisplay(int width, int height, bool fullscreen, bool remember)
    {
        int screenWidth = Win32.GetSystemMetrics(Win32.SM_CXSCREEN), screenHeight = Win32.GetSystemMetrics(Win32.SM_CYSCREEN);
        // 화면보다 큰 창은 못 만든다(윈도가 창을 화면 크기로 막는다)
        (width, height) = (Math.Min(width, screenWidth), Math.Min(height, screenHeight));
        // 창으로 띄울 때는 작업 표시줄을 뺀 자리 안에 들어가게 줄이고 그 가운데에 둔다 — 넘치면 아래쪽 화면 글이 가려진다
        var work = new Win32.Rect { Left = 0, Top = 0, Right = screenWidth, Bottom = screenHeight };
        if (!fullscreen && Win32.SystemParametersInfoW(0x0030, 0, ref work, 0))
            (width, height) = (Math.Min(width, work.Width), Math.Min(height, work.Height));
        if (fullscreen)
        {
            Win32.SetWindowLongPtrW(_hwnd, Win32.GWL_STYLE, (IntPtr)unchecked((uint)(Win32.WS_POPUP | Win32.WS_VISIBLE)));
            Win32.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, screenWidth, screenHeight, Win32.SWP_NOZORDER | Win32.SWP_FRAMECHANGED | Win32.SWP_SHOWWINDOW);
        }
        else
        {
            var rect = new Win32.Rect { Left = 0, Top = 0, Right = width, Bottom = height };
            Win32.SetWindowLongPtrW(_hwnd, Win32.GWL_STYLE, (IntPtr)unchecked((uint)(Win32.WS_OWNTITLE | Win32.WS_VISIBLE)));
            Win32.SetWindowPos(_hwnd, IntPtr.Zero, work.Left + Math.Max(0, (work.Width - rect.Width) / 2), work.Top + Math.Max(0, (work.Height - rect.Height) / 2),
                rect.Width, rect.Height, Win32.SWP_NOZORDER | Win32.SWP_FRAMECHANGED | Win32.SWP_SHOWWINDOW);
        }
        if (!remember) return;
        var settings = _voyage.Data.Settings;
        (settings.WindowWidth, settings.WindowHeight, settings.Fullscreen) = (width, height, fullscreen);
        settings.UiScale = 0;                       // 해상도를 바꾸면 화면 글 배율은 자동(해상도에 맞춤)으로 돌아간다
        _voyage.Data.SaveSettings();
    }

    /// <summary>화면 글의 배율 — 설정값, 없으면 윈도의 배율. 대본으로 돌릴 때는 1(찍은 그림을 견주려고).</summary>
    private float UiScale
    {
        get
        {
            if (_scripted && !_scaleInScript) return 1;
            double set = _voyage?.Data.Settings.UiScale ?? 0;
            // 자동: 창의 높이에 맞춘다 — 1080 줄에서 1 배(1440 → 1.33, 1800 → 1.67, 2160 → 2). 윈도 배율을 따르면 큰 해상도에서 화면 글이 너무 커진다
            return (float)Math.Clamp(set > 0 ? set : Math.Max(1, (_gfx?.Height ?? 1080) / 1080.0), 0.75, 3);
        }
    }

    private bool _scaleInScript;

    private static void RegisterClassOnce()
    {
        if (_classAtom != 0) return;
        var wc = new Win32.WndClassEx
        {
            Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32.WndClassEx>(),
            Style = Win32.CS_HREDRAW | Win32.CS_VREDRAW,
            WindowProc = StaticWndProcDelegate,
            Instance = Win32.GetModuleHandleW(null),
            Cursor = Win32.LoadCursorW(IntPtr.Zero, (IntPtr)Win32.IDC_ARROW),
            ClassName = ClassName,
        };
        _classAtom = Win32.RegisterClassExW(ref wc);
    }

    private static IntPtr StaticWndProcTrampoline(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam) =>
        _active != null ? _active.WndProc(hWnd, msg, wParam, lParam)
                        : Win32.DefWindowProcW(hWnd, msg, wParam, lParam);

    private void CreateNativeWindow()
    {
        var rect = new Win32.Rect { Left = 0, Top = 0, Right = StartWidth, Bottom = StartHeight };

        _active = this;
        _hwnd = Win32.CreateWindowExW(0, ClassName, "대항해시대 온라인 — 항해 (개인용)",
            Win32.WS_OWNTITLE, 80, 60,
            rect.Width, rect.Height,
            IntPtr.Zero, IntPtr.Zero, Win32.GetModuleHandleW(null), IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("창을 만들지 못했습니다.");

        // 창 · 작업 표시줄 아이콘 — 게임 폴더의 원본 아이콘을 그 자리에서 읽어 쓴다(없으면 기본 아이콘)
        string icon = GvoFiles.PathOf("GVOnline.ico");
        if (File.Exists(icon))
            foreach (var (which, size) in new[] { (1, 48), (0, 16) })
            {
                var handle = Win32.LoadImageW(IntPtr.Zero, icon, Win32.IMAGE_ICON, size, size, Win32.LR_LOADFROMFILE);
                if (handle != IntPtr.Zero) Win32.SendMessageW(_hwnd, Win32.WM_SETICON, which, handle);
            }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case Win32.WM_DESTROY:
                Win32.PostQuitMessage(0);
                return IntPtr.Zero;
            case Win32.WM_NCCALCSIZE when wParam != IntPtr.Zero:
                return IntPtr.Zero;                    // 창 전체가 그리는 곳 — 제목 줄은 우리가 그린다
            case Win32.WM_NCHITTEST:
            {
                Win32.GetWindowRect(hWnd, out var frame);
                int px = (short)Win32.LowWord(lParam) - frame.Left, py = (short)Win32.HighWord(lParam) - frame.Top;
                const int edge = 6;
                bool sizable = !(_voyage?.Data.Settings.Fullscreen ?? false);
                if (sizable)
                {
                    bool left = px < edge, right = px >= frame.Width - edge, top = py < edge, bottom = py >= frame.Height - edge;
                    if (top && left) return Win32.HTTOPLEFT;
                    if (top && right) return Win32.HTTOPRIGHT;
                    if (bottom && left) return Win32.HTBOTTOMLEFT;
                    if (bottom && right) return Win32.HTBOTTOMRIGHT;
                    if (left) return Win32.HTLEFT;
                    if (right) return Win32.HTRIGHT;
                    if (top) return Win32.HTTOP;
                    if (bottom) return Win32.HTBOTTOM;
                }
                // 제목 줄: 햄버거(왼쪽)와 내리기·닫기(오른쪽) 단추는 우리가 받고, 나머지는 잡고 끄는 자리
                float scale = UiScale;
                if (py < Hud.TitleHeight * scale && px >= Hud.TitleMenuWidth * scale && px < frame.Width - Hud.TitleButtonsWidth * scale) return Win32.HTCAPTION;
                return Win32.HTCLIENT;
            }
            case 0x0020 when (Win32.LowWord(lParam) & 0xFFFF) == Win32.HTCLIENT:        // WM_SETCURSOR
                // 원본 커서: 보통은 노란 화살표. 단추 위에서는 손, 사람 달린 화살표는 눌러 둔 곳으로 걸어가는 동안만
                _cursors ??= new GameCursors(UiScale);
                int which = _overUi ? GameCursors.Hand : Walking && _route.Count > 0 && _voyage.Dialog == Dialog.None ? GameCursors.Walk : GameCursors.Arrow;
                if (_cursors.Show(which)) return 1;
                break;
            case Win32.WM_ERASEBKGND:
                return 1;
            case Win32.WM_SIZE:
                _gfx?.Resize(Win32.LowWord(lParam) & 0xFFFF, Win32.HighWord(lParam) & 0xFFFF);
                return IntPtr.Zero;

            case Win32.WM_KEYDOWN:
                if ((int)wParam == 0xE5) return IntPtr.Zero;        // 입력기가 먹은 글쇠(VK_PROCESSKEY) — 단축키로 치지 않는다
                if (_keys.Add((int)wParam)) KeyPressed((int)wParam);
                return IntPtr.Zero;
            case Win32.WM_CHAR:
                _hud?.Type((char)(long)wParam);
                return IntPtr.Zero;
            case Win32.WM_KEYUP:
                _keys.Remove((int)wParam);
                return IntPtr.Zero;

            case Win32.WM_MOUSEMOVE:
                int x = Win32.LowWord(lParam), y = Win32.HighWord(lParam);
                if (_leftDown && OverPreview)
                {
                    _previewYaw += (x - _mouseX) * 0.01f;
                    _previewPitch = Math.Clamp(_previewPitch + (y - _mouseY) * 0.006f, -0.2f, 1.2f);
                }
                if (_orbiting)
                {
                    _orbitMoved += Math.Abs(x - _mouseX) + Math.Abs(y - _mouseY);
                    _yawGoal = null;
                    _yaw -= (x - _mouseX) * 0.006f;
                    _pitch = Math.Clamp(_pitch + (y - _mouseY) * 0.005f, 0.06f, 1.45f);
                }
                _mouseX = x;
                _mouseY = y;
                return IntPtr.Zero;
            case Win32.WM_RBUTTONDOWN:
                (_orbiting, _orbitMoved) = (true, 0);
                Win32.SetCapture(hWnd);
                return IntPtr.Zero;
            case Win32.WM_RBUTTONUP:
                _orbiting = false;
                Win32.ReleaseCapture();
                // 바다에서 끌지 않고 오른쪽 단추만 누르면 카메라가 뱃머리 쪽을 보게 배 뒤로 돈다
                if (_orbitMoved < 6 && _voyage.Mode == Mode.Sea) _yawGoal = -(float)_voyage.Heading;
                return IntPtr.Zero;
            case Win32.WM_LBUTTONDOWN:
                _leftDown = true;
                _dragX = _mouseX;
                _dragY = _mouseY;
                return IntPtr.Zero;
            case Win32.WM_LBUTTONUP:
                if (_leftDown && Math.Abs(_mouseX - _dragX) + Math.Abs(_mouseY - _dragY) < 8) _clicked = true;
                _leftDown = false;
                return IntPtr.Zero;
            case Win32.WM_MOUSEWHEEL:
                int wheel = (short)((long)wParam >> 16);
                if (OverPreview) { _previewZoom = Math.Clamp(_previewZoom * MathF.Pow(0.88f, wheel / 120f), 0.08f, 2.5f); return IntPtr.Zero; }
                if (_hud != null && _hud.Wheel(wheel / 120)) return IntPtr.Zero;      // 창이 떠 있으면 목록을 굴린다
                _distance = Math.Clamp(_distance * MathF.Pow(0.88f, wheel / 120f), Walking ? 500f : 9000f, 160000f);
                return IntPtr.Zero;
        }
        return Win32.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private Dho.Audio.SoundEffects? _sounds;
    private double _turnSounded = -10;

    /// <summary>설정에 적힌 효과음을 튼다. 대본으로 돌릴 때는 조용히.</summary>

    /// <summary>
    /// 선체에 입히는 빛깔 — 선체 그림(잿빛 도는 나무)에 곱한다. 흰 재질(하얀 달)은 곱해서는 희어지지 않으니 더 세게 곱해 바랜 흰빛으로 올린다.
    /// 0xFFFFFF 는 「재질 빛깔 없음」이라 그대로 둔다.
    /// </summary>
    private static Vector4 HullTint(int color)
    {
        int kind = color >> 24 & 255;             // 위 바이트: 바탕 판(0 이면 옛 방식 — 잿빛 나무에 곱한다)
        color &= 0xFFFFFF;
        var tint = new Vector3((color >> 16 & 255) / 255f, (color >> 8 & 255) / 255f, (color & 255) / 255f);
        if (kind == 1) return new Vector4(tint * 1.15f, 1);                 // 제 빛의 나무 판 — 나무 빛은 옅게 물들일 뿐
        if (kind >= 2) return new Vector4(tint * (MathF.Min(tint.X, MathF.Min(tint.Y, tint.Z)) > 0.8f ? 1.9f : 1.35f), 1);      // 잿빛 널에 칠 — 판이 어두워 밝혀 곱한다
        if (color != 0xFFFFFF && MathF.Min(tint.X, MathF.Min(tint.Y, tint.Z)) > 0.82f) tint *= 2.2f;
        return new Vector4(tint, 1);
    }

    // 선체의 바탕 판을 그 빛깔에 딸린 것으로 맞추고 빛깔을 낸다
    private static Vector4 Hull(ShipModel ship, int color)
    {
        ship.SetHullBase(color >> 24 & 255);
        return HullTint(color);
    }

    private float _heel, _sunk;
    private readonly ShipModel?[] _compareShips = new ShipModel?[2];
    private readonly int[] _compareModels = new int[2];

    // 지금 입은 장비를 그 몸 틀의 모형으로
    private List<Worn> WornNow(int frame) =>
        _voyage.WornModels(frame).Select(w => new Worn(w.Part, w.Entry[0], w.Entry[1], w.Entry[2], w.Entry[3], w.Colors)).ToList();

    private void PlayCue(string cue)
    {
        var sounds = _voyage.Data.Settings.Sounds;
        // 재해 · 폭풍의 소리를 따로 안 매어 두었으면 경고 소리로
        if ((cue.StartsWith("Disaster") || cue == "Storm") && !(sounds.TryGetValue(cue, out string? own) && own.Length > 0)) cue = "Warn";
        // 설정 파일에 없는 소리는 원본의 것을 기본으로 쓴다 — 사용자가 효과음 고르기에서 들어 보고 메모해 준 번호(묶음 0)
        if (!sounds.ContainsKey(cue) && cue switch
            {
                "Click" => "0:0", "Quest" => "0:2", "Error" => "0:3", "Mastery" => "0:4", "Warn" => "0:5",
                "SkillUp" => "0:7", "Done" => "0:8", "StudyDone" => "0:10", "Eat" => "0:11", "Bank" => "0:14", "Door" => "0:15", "Open" => "24:5",
                "Part" => "0:20", "Buy" => "0:23", "Drunk" => "0:24", "University" => "0:30", "Sail" => "14:15", _ => null,
            } is { } original) sounds[cue] = original;
        // 선회 소리의 옛 기본값(9:0)도 지은 것이었다 — 원본은 0:12(바다에서 배를 돌릴 때)
        if (cue == "Turn" && sounds.GetValueOrDefault("Turn") is null or "9:0") sounds["Turn"] = "0:12";
        // 스킬 소리의 옛 기본값(5:0)은 지은 것이었다 — 원본은 0:6
        if (cue is "Skill" or "Sail" && sounds.GetValueOrDefault("Skill") == "5:0") sounds["Skill"] = "0:6";
        // 돛 조종 소리를 따로 안 정했으면 스킬 소리를 쓴다
        if (_scripted || !(sounds.TryGetValue(cue, out string? which) || (cue == "Sail" && sounds.TryGetValue("Skill", out which))) || string.IsNullOrEmpty(which)) return;
        (_sounds ??= new Dho.Audio.SoundEffects()).Play(which);
    }

    private void KeyPressed(int key)
    {
        // 바다에서 키를 꺾을 때 — 잇달아 눌러도 소리는 띄엄띄엄
        if (_voyage.Created && _voyage.Mode == Mode.Sea && _voyage.Dialog == Dialog.None && key is 'A' or 'D' or Win32.VK_LEFT or Win32.VK_RIGHT && _voyage.Clock - _turnSounded > 1.2)
        {
            _turnSounded = _voyage.Clock;
            PlayCue("Turn");
        }
        // 찾기 칸에 글자를 치는 동안은 단축키가 듣지 않는다(위아래 글쇠와 Esc 는 빼고)
        if (_hud.Typing && key is not (Win32.VK_UP or Win32.VK_DOWN))
        {
            if (key == Win32.VK_ESCAPE) _hud.StopTyping();
            return;
        }
        // 단축키 등록 창이 글쇠를 기다리는 중이면 그 글쇠를 그 일에 맨다(Esc 는 그만두기)
        if (_hud.KeyWaiting is { } waiting)
        {
            _hud.KeyWaiting = null;
            if (key != Win32.VK_ESCAPE && key != Win32.VK_CONTROL)
            {
                var keys = _voyage.Data.Settings.Keys;
                foreach (string other in Hud.KeyActions.Select(a => a.Action).Where(a => a != waiting && Hud.KeyOf(keys, a) == key).ToList())
                    keys[other] = 0;                       // 같은 글쇠를 쓰던 일은 비운다
                keys[waiting] = key;
                _voyage.Data.SaveSettings();
            }
            return;
        }
        if (key is Win32.VK_UP or Win32.VK_DOWN && _hud.ListKey(key == Win32.VK_UP ? -1 : 1)) return;
        string action = Hud.KeyActions.Select(a => a.Action).FirstOrDefault(a => Hud.KeyOf(_voyage.Data.Settings.Keys, a) == key) ?? "";
        if (action == "Fullscreen")
        {
            var settings = _voyage.Data.Settings;
            SetDisplay(settings.WindowWidth, settings.WindowHeight, !settings.Fullscreen);
            return;
        }
        if (!_voyage.Created) return;
        // 조선소 주인 차림에서 C 는 「커스텀설정 조선」(캐릭터 정보보다 먼저)
        if (key == 'C' && _voyage.Dialog == Dialog.ShipyardMenu) { _hud.OpenCustomSetup(); return; }
        void Toggle(Dialog dialog)
        {
            if (_voyage.Dialog == dialog) _voyage.Dialog = Dialog.None;
            else if (_voyage.Dialog == Dialog.None) _voyage.Dialog = dialog;
        }
        // 퀵슬롯을 펴 두었으면 1 ~ 8 은 퀵슬롯이 먼저다(같은 글쇠를 다른 일에 매어 두었어도)
        if (_hud.QuickOpen && key is >= '1' and <= '8' && !_keys.Contains(Win32.VK_CONTROL) && _voyage.Dialog is Dialog.None or Dialog.UseSkills)
        {
            _voyage.UsePageSlot(key - '1');
            return;
        }
        switch (action)
        {
            case "Map": _hud.TownMapOpen = !_hud.TownMapOpen; return;
            case "Items": Toggle(Dialog.Items); return;
            case "TownMenu" when _voyage.Mode == Mode.Port && _voyage.TownView && _voyage.Dialog == Dialog.None:
                _hud.TownMenuOpen = !_hud.TownMenuOpen;
                return;
            case "UseSkills": Toggle(Dialog.UseSkills); return;
            case "Outfit": Toggle(Dialog.Character); return;
            case "Skills": Toggle(Dialog.Skills); return;
            case "ShipInfo": Toggle(Dialog.ShipInfo); return;
            case "Pause": _voyage.Paused = !_voyage.Paused; return;
            case "Cargo": Toggle(Dialog.Cargo); return;
            case "Fitting": Toggle(Dialog.Fitting); return;
            case "Quick": _hud.QuickOpen = !_hud.QuickOpen; return;
        }
        switch (key)
        {
            case Win32.VK_ESCAPE:
                if (_voyage.Dialog != Dialog.None) _voyage.Dialog = Dialog.None;
                break;
            case '5' when _keys.Contains(Win32.VK_CONTROL):
                _hud.TownMapOpen = !_hud.TownMapOpen;      // 원본의 지도 단축키도 그대로 둔다
                break;
            case >= '1' and <= '8' when !_keys.Contains(Win32.VK_CONTROL) && _voyage.Dialog is Dialog.None or Dialog.UseSkills:
                _voyage.UsePageSlot(key - '1');
                break;
            case 'W' or Win32.VK_UP: _voyage.ChangeSail(+1); break;
            case 'S' or Win32.VK_DOWN: _voyage.ChangeSail(-1); break;
            case 'F' or Win32.VK_RETURN:
                if (_voyage.Dialog != Dialog.None) break;
                if (Walking) { if (KeeperNear() is { } keeper) _voyage.Visit(keeper.Mark); break; }
                if (_voyage.SiteInReach()) _voyage.Land();
                else _voyage.EnterPort();
                break;
        }
    }

    // ── 한 틱 ────────────────────────────────────────────────────────────────

    private void Tick(double dt)
    {
        double steer = 0;
        if (_keys.Contains('A') || _keys.Contains(Win32.VK_LEFT)) steer -= 1;
        if (_keys.Contains('D') || _keys.Contains(Win32.VK_RIGHT)) steer += 1;
        _voyage.Update(dt, steer);
        if (_music != null)
        {
            _music.Volume = (float)_voyage.Data.Settings.MusicVolume;
            // 바다에서 해역이 바뀌어 곡이 달라질 때는 듣던 곡을 끝까지 틀고 바꾼다. 입항 · 출항 때는 바로 바꾼다
        bool atSea = _voyage.Mode == Mode.Sea;
        _music.Play(_musicOn && _voyage.Created && _voyage.Data.Settings.MusicVolume > 0 ? _voyage.MusicNumber : 0, atSea && _musicAtSea);
        _musicAtSea = atSea;
        }
        if (Walking && _voyage.Dialog == Dialog.None) Walk((float)dt);
    }

    private bool Walking => _townView && _grid != null;

    /// <summary>시내 걷기 — W·S 는 보는 쪽으로 앞뒤, A·D 는 옆. 벽에 걸리면 벽을 따라 미끄러진다.</summary>
    private List<Vector2> _route = [];
    private TownMark? _bound;
    private bool _boundDoor;                   // 가는 곳이 사람이 아니라 건물 입구다 — 닿으면 문 여는 소리

    /// <summary>도시 메뉴에서 고른 시설 앞으로 바로 옮겨 가서 그 시설의 일을 연다.</summary>
    private void JumpTo(TownMark mark)
    {
        if (!Walking) return;
        (_route, _bound) = ([], null);
        var keeper = _keepers.Find(k => k.Mark.Place == mark.Place);
        _walk = _grid!.Nearest(keeper.Name != null ? keeper.Spot + new Vector2(MathF.Sin(keeper.Facing), MathF.Cos(keeper.Facing)) * 180f : mark.Scene);
        if (keeper.Name != null) _walkYaw = MathF.Atan2(keeper.Spot.X - _walk.X, keeper.Spot.Y - _walk.Y);
        _voyage.Visit(mark);
    }

    /// <summary>지도에서 시설을 누르면 그리로 걸어간다.</summary>
    private void WalkTo(TownMark mark)
    {
        if (!Walking) return;
        // 사람이 서 있는 시설이면 그 사람 자리가 아니라 한 걸음 앞(내가 오는 쪽)에 가서 선다 — 자리까지 가면 둘이 겹친다
        var goal = mark.Scene;
        if (_keepers.Find(k => k.Mark.Place == mark.Place) is { Name: not null } keeper)
        {
            var toward = _walk - keeper.Spot;
            float apart = toward.Length();
            if (apart is > 110f and < 230f) { (_route, _bound) = ([], null); FaceKeeper(mark); _voyage.Visit(mark); return; }      // 이미 앞에 서 있다
            var front = new Vector2(MathF.Sin(keeper.Facing), MathF.Cos(keeper.Facing));
            goal = _grid!.Nearest(keeper.Spot + (apart > 1f ? toward / apart : front) * 160f);
            if (Vector2.Distance(goal, keeper.Spot) < 90f) goal = _grid.Nearest(keeper.Spot + front * 160f);      // 그쪽이 막혔으면 그 사람이 보는 쪽에
        }
        _route = _grid!.Path(_walk, goal);
        _bound = _route.Count > 0 ? mark : null;
        _boundDoor = _route.Count > 0 && goal == mark.Scene;
        if (_boundDoor) PlayCue("Door");      // 사람이 아니라 건물 입구를 골랐다
        _routeRuns = true;                         // 지도에서 고른 곳으로는 달려간다
        _voyage.Say(_route.Count > 0 ? (mark.Place == Voyage.InsideMaster ? $"{_voyage.PlaceName(mark.Place)}에게 간다." : $"{_voyage.PlaceName(mark.Place)}(으)로 간다.") : $"{_voyage.PlaceName(mark.Place)}까지 가는 길을 못 찾았다.");
    }

    /// <summary>그 시설에 선 사람 쪽으로 돌아선다.</summary>
    private void FaceKeeper(TownMark mark)
    {
        if (_keepers.Find(k => k.Mark.Place == mark.Place) is { Name: not null } keeper && Vector2.Distance(keeper.Spot, _walk) > 1f)
            _walkYaw = MathF.Atan2(keeper.Spot.X - _walk.X, keeper.Spot.Y - _walk.Y);
    }

    private void Walk(float dt)
    {
        // 사람 키가 170 이니 장면 단위가 1cm 쯤이다. 걷기는 초속 3m 남짓(시내가 넓어 조금 빠르게), Shift 를 누르면 달린다
        _sprinting = _keys.Contains(Win32.VK_SHIFT) || (_route.Count > 0 && _routeRuns);
        float speed = _sprinting ? 620f : 310f;
        bool Down(int a, int b) => _keys.Contains(a) || _keys.Contains(b);
        bool keyed = Down('W', Win32.VK_UP) || Down('S', Win32.VK_DOWN) || Down('A', Win32.VK_LEFT) || Down('D', Win32.VK_RIGHT);
        if (keyed) (_route, _bound) = ([], null);              // 손으로 걸으면 자동 이동을 그만둔다
        else if (_route.Count > 0)
        {
            var toward = _route[0] - _walk;
            float left = toward.Length(), stride = speed * dt;
            if (left <= stride)
            {
                _walk = _route[0];
                _route.RemoveAt(0);
                if (_route.Count == 0 && _bound is { } reached) { _bound = null; if (_boundDoor) PlayCue("Open"); FaceKeeper(reached); _voyage.Visit(reached); }
            }
            else
            {
                _walk += toward / left * stride;
                _walkYaw = MathF.Atan2(toward.X, toward.Y);
                // 가는 쪽을 등 뒤에서 보게 카메라가 천천히 따라 돈다
                float want = MathF.Atan2(-toward.X, -toward.Y), turn = MathF.IEEERemainder(want - _yaw, MathF.Tau);
                if (!_orbiting) _yaw += turn * MathF.Min(1, dt * 1.5f);
            }
            return;
        }
        var forward = new Vector2(-MathF.Sin(_yaw), -MathF.Cos(_yaw));
        var right = new Vector2(MathF.Cos(_yaw), -MathF.Sin(_yaw));
        var direction = forward * ((Down('W', Win32.VK_UP) ? 1 : 0) - (Down('S', Win32.VK_DOWN) ? 1 : 0))
                      + right * ((Down('D', Win32.VK_RIGHT) ? 1 : 0) - (Down('A', Win32.VK_LEFT) ? 1 : 0));
        if (direction == Vector2.Zero) return;
        direction = Vector2.Normalize(direction);
        _walkYaw = MathF.Atan2(direction.X, direction.Y);

        var step = direction * speed * dt;
        foreach (var move in new[] { step, new Vector2(step.X, 0), new Vector2(0, step.Y) })
        {
            if (move == Vector2.Zero) continue;
            var next = _walk + move;
            var probe = next + Vector2.Normalize(move) * 45f;
            float climb = MathF.Abs(_grid!.HeightAt(next.X, next.Y) - _grid.HeightAt(_walk.X, _walk.Y));
            if (!_grid.Walkable(next.X, next.Y) || !_grid.Walkable(probe.X, probe.Y) || climb > move.Length() * 0.9f + 3f) continue;
            _walk = next;
            break;
        }
    }

    private void Render()
    {
        SyncIme();
        // 배가 장면의 원점이다
        while (_voyage.Cues.TryDequeue(out string? cue)) PlayCue(cue);
        float sway = (float)Math.Sin(_voyage.Clock * 1.1);
        if (_yawGoal is { } yawGoal)
        {
            float turn = MathF.IEEERemainder(yawGoal - _yaw, MathF.Tau);
            _yaw += turn * 0.15f;
            if (MathF.Abs(turn) < 0.01f) _yawGoal = null;
        }
        bool town = _voyage.Mode == Mode.Port && _voyage.TownView && _voyage.City.TownScene != 0;
        if (town) LoadTown();
        if (town != _townView)
        {
            // 시내로 들어가면 사람 뒤로 다가서고(걷는 면이 없는 장면은 도시가 한눈에 들어오게 물러난다), 나오면 보던 거리로 돌아간다
            if (town) (_portDistance, _portPitch, _distance, _pitch) = (_distance, _pitch, _grid != null ? 2600f : Math.Clamp(_town!.Radius * 1.1f, 9000f, 160000f), _grid != null ? 0.32f : 0.75f);
            else (_distance, _pitch) = (_portDistance, _portPitch);
            _townView = town;
        }
        float foot = Walking ? _grid!.HeightAt(_walk.X, _walk.Y) : 0;
        var target = Walking ? new Vector3(0, foot + 150, 0) : new Vector3(0, 3500, 0);
        Vector3 Back(float pitch) => new(MathF.Cos(pitch) * MathF.Sin(_yaw), MathF.Sin(pitch), MathF.Cos(pitch) * MathF.Cos(_yaw));
        if (Walking && _voyage.Interior != 0 && _town!.HasWalls)
        {
            // 방 안: 벽이나 천장에 닿는 데 바로 앞까지만 물러난다
            var head = new Vector3(_walk.X, 0, _walk.Y) + target;
            float clear = MathF.Max(0, _town.Reach(head, head + Back(_pitch) * _distance) - 60f / _distance);
            _viewPitch = _pitch;
            _sight = clear < _sight ? clear : _sight + (clear - _sight) * 0.08f;
        }
        else if (Walking)
        {
            // 집에 가리면 먼저 카메라를 들어 올려 지붕 너머로 보고, 그래도 가리면 가리는 데까지 다가선다
            var head = new Vector3(_walk.X, 0, _walk.Y) + target;
            float want = MathF.Min(_distance, 1400f), best = -1, bestPitch = _pitch;
            for (float pitch = _pitch; pitch <= 1.46f; pitch += 0.1f)
            {
                float reach = _grid!.Sight(head, head + Back(pitch) * _distance) * _distance;
                if (reach > best) (best, bestPitch) = (reach, pitch);
                if (reach >= want) break;
            }
            // 위가 다 막힌 곳(성문 아치 밑)에서는 들어 올리지 않고 길 위에서 뒤따른다
            bool street = best < 500f;
            if (street)
            {
                bestPitch = _pitch;
                // 길이 좁아 뒤가 바로 벽이면 길이 트인 쪽으로 카메라를 돌린다
                Vector3 Along(float yaw) => new(MathF.Cos(_pitch) * MathF.Sin(yaw), MathF.Sin(_pitch), MathF.Cos(_pitch) * MathF.Cos(yaw));
                if (_grid.Sight(head, head + Along(_yaw) * _distance, true) * _distance < 400f)
                    for (int k = 1; k <= 18; k++)
                    {
                        float turn = (k + 1) / 2 * 0.35f * (k % 2 == 0 ? -1 : 1);
                        if (_grid.Sight(head, head + Along(_yaw + turn) * _distance, true) * _distance < 500f) continue;
                        _yaw += turn * 0.2f;
                        break;
                    }
            }
            _viewPitch += (bestPitch - _viewPitch) * 0.12f;
            float clear = _grid!.Sight(head, head + Back(_viewPitch) * _distance, street);
            _sight = clear < _sight ? clear : _sight + (clear - _sight) * 0.08f;
        }
        else (_sight, _viewPitch) = (1, _pitch);
        // 아치 밑처럼 어디로도 못 물러나는 곳에서는 머리 자리에서 본다(그때는 사람을 안 그린다)
        float eyeDistance = _distance * _sight;
        bool firstPerson = Walking && eyeDistance <= 170f;
        _eye = firstPerson ? target + new Vector3(0, 15, 0) : target + eyeDistance * Back(_viewPitch);
        var view = Matrix4x4.CreateLookAt(_eye, firstPerson ? _eye - Back(0.05f) : target, Vector3.UnitY);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(0.85f, _gfx.Width / (float)_gfx.Height, Walking ? 60f : 400f, Walking ? 800000f : 3000000f);
        _viewProjection = view * projection;
        Matrix4x4.Invert(_viewProjection, out var inverse);

        // 날씨가 궂을수록 하늘이 가라앉는다. 먼 데만 조금 더 흐려지고 내 배와 둘레 바다는 또렷해야 한다
        float overcast = _voyage.Mode != Mode.Sea ? 0 : _voyage.Weather switch { Weather.Storm => 1f, Weather.Rain => 0.7f, Weather.Cloudy => 0.35f, _ => 0f };
        _overcast += (overcast - _overcast) * 0.02f;
        // 방 안은 바깥이 밤이어도 등불 밑이다 — 늘 낮의 밝기로 그린다
        var sky = town && _voyage.Interior != 0 ? Sky.At(0.5) : Sky.At(_voyage.SkyPhase);
        // 바다마다 다른 하늘빛 · 물빛(원본의 바다 빛깔 벌) — 방 안에서는 안 쓴다
        if (!(town && _voyage.Interior != 0))
        {
            try
            {
                _seaColors ??= new Dho.Data.SeaColors();
                var (tintSky, tintWater) = _seaColors.At(_voyage.ShipX, _voyage.ShipY);
                sky = sky.Tinted(tintSky, tintWater, _seaColors.HomeSky, _seaColors.HomeWater).Overcast(_overcast);
            }
            catch (Exception) { sky = sky.Overcast(_overcast); }
        }
        var heading = new Vector2(MathF.Sin((float)_voyage.Heading), -MathF.Cos((float)_voyage.Heading));
        // 선체 특수효과 도료의 빛 — 15 번(무지개)은 빛깔이 천천히 돈다
        var aura = Vector3.Zero;
        if (_voyage.Created && !town && _voyage.Work.HullEffect is > 0 and <= 15)
        {
            int c = Voyage.HullEffects[_voyage.Work.HullEffect - 1].Color;
            aura = new Vector3((c >> 16 & 255) / 255f, (c >> 8 & 255) / 255f, (c & 255) / 255f);
            if (_voyage.Work.HullEffect == 15)
            {
                float h = (float)(_voyage.Clock * 0.25);
                aura = new Vector3(0.5f + 0.5f * MathF.Sin(h), 0.5f + 0.5f * MathF.Sin(h + 2.09f), 0.5f + 0.5f * MathF.Sin(h + 4.19f));
            }
        }
        var frame = new FrameConstants
        {
            ViewProjection = _viewProjection,
            InverseViewProjection = inverse,
            CameraPosition = _eye,
            Time = (float)_voyage.Clock,
            SunDirection = sky.LightDirection,
            Night = sky.Night,
            SunColor = sky.LightColor,
            FogDensity = 0.0000032f * (1 + _overcast * 0.5f),
            Ambient = sky.Ambient,
            HorizonColor = sky.Horizon,
            ZenithColor = sky.Zenith,
            WaterColor = sky.Water,
            WorldOffset = new Vector2((float)(_voyage.ShipX * Terrain.Unit % 1048576), (float)(_voyage.ShipY * Terrain.Unit % 1048576)),
            ShipPosition = Vector2.Zero,
            ShipDirection = heading,
            ShipSpeed = (float)Math.Clamp(_voyage.Knots / 12, 0, 1),
            WaveScale = _voyage.Mode == Mode.Sea ? (float)Math.Clamp(_voyage.WaveScale, 0.3, 4) : 0.8f,
            Pad1 = aura.X, Pad2 = aura.Y, Pad3 = aura.Z,
        };

        _gfx.Begin(frame);
        // 건물 안에서는 하늘과 바다를 그리지 않는다(벽은 안쪽 면뿐이라 밖에서 보면 비쳐 보인다)
        if (!(town && _voyage.Interior != 0))
        {
            _scene.DrawSky();
            _scene.DrawOcean();
        }
        _scene.BeginMeshes();
        if (_voyage.Mode == Mode.Sea) { _terrain.Draw(_scene, _voyage.ShipX, _voyage.ShipY); DrawCityAtSea(); }
        else if (town) DrawTown(foot, eyeDistance > 170f);
        else DrawPort();

        bool inPort = _voyage.Mode == Mode.Port && _port != null;
        if (_shipModel != _voyage.Ship.Model)
        {
            // 배를 갈아탔다
            _ship.Dispose();
            _shipModel = _voyage.Ship.Model;
            _ship = new ShipModel(_gfx, _shipModel);
        }
        // 돌 때는 바깥쪽으로 기운다
        _heel += ((float)(_voyage.TurnShare * Math.Clamp(_voyage.Knots / 10, 0, 1)) * 0.09f - _heel) * 0.05f;
        float roll = sway * 0.025f + (float)Math.Clamp(_voyage.Knots / 14, 0, 1) * 0.04f + (inPort ? 0 : _heel);
        // 재해에 따라 배가 움직인다: 침수면 가라앉고 한쪽으로 기울고, 암초 · 반란 · 폭풍이면 흔들린다
        float sunk = 0;
        if (_voyage.Mode == Mode.Sea)
        {
            bool Has(int id) => _voyage.Disasters.Exists(d => d.Data.Id == id);
            float clock = (float)_voyage.Clock;
            if (Has(2)) { sunk = 330; roll += 0.07f; }
            if (Has(6)) roll += MathF.Sin(clock * 23) * 0.018f;
            if (Has(9)) roll += MathF.Sin(clock * 9) * 0.03f;
            if (_voyage.Weather == Weather.Storm) roll += MathF.Sin(clock * 1.7f) * 0.09f;
        }
        _sunk += (sunk - _sunk) * 0.02f;
        var shipWorld = Matrix4x4.CreateRotationZ(roll)
                        * Matrix4x4.CreateRotationY(inPort ? _berth!.Yaw : MathF.PI - (float)_voyage.Heading)
                        * Matrix4x4.CreateTranslation(0, sway * 60f - _sunk, 0);
        // 재해의 모습을 그릴 자리 — 배의 가운데 · 뱃머리 쪽 · 돛대 꼭대기 쪽이 화면의 어디인가
        _hud.ShipOnScreen = null;
        if (_voyage.Mode == Mode.Sea && !town)
        {
            (float X, float Y)? Spot(Vector3 p)
            {
                var clip = Vector4.Transform(new Vector4(p, 1), _viewProjection);
                return clip.W > 1 ? ((clip.X / clip.W * 0.5f + 0.5f) * _gfx.Width / UiScale, (0.5f - clip.Y / clip.W * 0.5f) * _gfx.Height / UiScale - Hud.TitleHeight) : null;
            }
            float half = _ship.Radius * 0.75f;
            var bow = new Vector3(MathF.Sin((float)_voyage.Heading), 0, -MathF.Cos((float)_voyage.Heading)) * half;
            if (Spot(new Vector3(0, 500, 0)) is { } mid && Spot(new Vector3(0, 500, 0) + bow) is { } fore && Spot(new Vector3(0, 500 + half * 0.9f, 0)) is { } top)
                _hud.ShipOnScreen = (mid.X, mid.Y, fore.X - mid.X, fore.Y - mid.Y, top.X - mid.X, top.Y - mid.Y);
        }
        // 마스트 톱에 깃발 데코를 달았으면 그 깃발(표의 나라 차례), 아니면 제 나라의 깃발. 현의 데코 넷은 모형으로 단다
        var mastTop = _voyage.DecoOf(_voyage.DecoOn[0]);
        _ship.SetFlag(mastTop is { Kind: 0, Model: 4 } ? mastTop.Extra : _voyage.NationId);
        _ship.SetDecos([.. Enumerable.Range(1, 4).Select(k => _voyage.DecoOf(_voyage.DecoOn[k])?.Model ?? 0)]);
        // 단 문장을 돛에 그린다
        _ship.SetEmblem(_voyage.Parts.Find(p => p.Slot == 3) is { } crest ? crest.Id - 1_100_000 : 0);
        if (_sailShown != (_ship, _voyage.SailPattern, _voyage.SailTint))
        {
            try { _ship.SetSail(_voyage.SailPattern, _voyage.SailTint); } catch (Exception) { }
            _sailShown = (_ship, _voyage.SailPattern, _voyage.SailTint);
        }
        int wood = _voyage.HullColorOf(_voyage.Ship, _voyage.ShipMaterialId);
        _ship.Furl = _voyage.Mode == Mode.Sea ? _voyage.Sail / (float)Voyage.SailSteps : 1;      // 바다에서는 돛을 편 만큼만 보인다
        if (!town) _ship.Draw(_scene, shipWorld, null, Hull(_ship, wood));

        // 화면 글과 창
        _canvas.Scale = UiScale;
        _canvas.Pointer = new Pointer { X = _mouseX / UiScale, Y = _mouseY / UiScale - Hud.TitleHeight, Clicked = _clicked, Down = _leftDown };
        _canvas.Top = Hud.TitleHeight;
        _canvas.Begin();
        (_hud.TownGrid, _hud.TownSpot, _hud.TownFacing) = (Walking ? _grid : null, _walk, _walkYaw);
        // 사람들의 이름표 — 머리 위 자리를 화면 자리로 옮겨 넘긴다
        _hud.Labels.Clear();
        _hud.Target = null;
        _hud.TalkTo = null;
        if (Walking)
        {
            foreach (var keeper in _keepers.Concat(_bystanders))
            {
                var head = new Vector3(keeper.Spot.X - _walk.X, _grid!.HeightAt(keeper.Spot.X, keeper.Spot.Y) + 195, keeper.Spot.Y - _walk.Y);
                var clip = Vector4.Transform(new Vector4(head, 1), _viewProjection);
                if (clip.W <= 1 || clip.W > 6000) continue;
                _hud.Labels.Add(((clip.X / clip.W * 0.5f + 0.5f) * _gfx.Width / UiScale, (0.5f - clip.Y / clip.W * 0.5f) * _gfx.Height / UiScale - Hud.TitleHeight, keeper.Name));
            }
            _hud.TalkTo = KeeperNear()?.Name;
            // 눌러 가는 곳의 표식(사람이나 시설로 가는 길에는 안 찍는다)
            _hud.Target = null;
            if (_route.Count > 0 && _bound == null)
            {
                var goal = _route[^1];
                var clip = Vector4.Transform(new Vector4(goal.X - _walk.X, _grid!.HeightAt(goal.X, goal.Y) + 4, goal.Y - _walk.Y, 1), _viewProjection);
                if (clip.W > 1) _hud.Target = ((clip.X / clip.W * 0.5f + 0.5f) * _gfx.Width / UiScale, (0.5f - clip.Y / clip.W * 0.5f) * _gfx.Height / UiScale - Hud.TitleHeight);
            }
        }
        _hud.ShipPreview = null;
        _hud.PreviewShip = null;
        _hud.FigurePreview = null;
        _hud.FigureView = null;
        _hud.ComparePreviews.Clear();
        _hud.FigureMine = false;
        _previewBox = null;
        _canvas.Pressed = false;
        _hud.Draw();
        _canvas.End();
        if (_canvas.Pressed) PlayCue("Click");
        // 항구 차림에서 시설을 눌렀다 — 시내가 다 올라오면 그 시설 앞으로 옮겨 간다
        if (_hud.PendingPlace != 0 && Walking && _grid != null)
        {
            if (_voyage.TownMap?.Marks.Find(m => m.Place == _hud.PendingPlace) is { } bound) JumpTo(bound);
            _hud.PendingPlace = 0;
        }
        else if (_hud.PendingPlace != 0 && !_voyage.TownView) _hud.PendingPlace = 0;

        // 부관고용 창의 오른쪽 칸: 고른 후보의 모습
        if (_hud.FigurePreview is { } who)
        {
            // 캐릭터 정보 창은 내 모습이라 손 · 모자까지 입히고, 돌리거나 얼굴로 다가선다
            var pose = _hud.FigureView ?? (0.35f, false, 0, -1);
            var wanted = (who.Frame, who.Face, who.Hair, who.Body, who.Leg, pose.Hand, pose.Cap);
            string wearing = _hud.FigureMine ? _voyage.WornKey : "";      // 내 모습이면 입은 장비까지
            if (_previewFigure == null || _previewLooks != wanted || _previewWear != wearing)
            {
                _previewFigure?.Dispose();
                (_previewFigure, _previewLooks, _previewWear) = (new CharacterModel(_gfx, new Looks(who.Frame, who.Face, who.Hair, who.Body, who.Leg, pose.Hand, pose.Cap) { Wear = _hud.FigureMine ? WornNow(who.Frame) : null }), wanted, wearing);
            }
            var (eye, aim) = pose.Face ? (new Vector3(0, 168, 130), new Vector3(0, 162, 0)) : (new Vector3(0, 110, 470), new Vector3(0, 92, 0));
            var look = Matrix4x4.CreateLookAt(eye, aim, Vector3.UnitY) * Matrix4x4.CreatePerspectiveFieldOfView(0.5f, who.W / who.H, 20f, 4000f);
            Matrix4x4.Invert(look, out var lookInverse);
            var noon = Sky.At(0.5);
            _gfx.BeginInset(frame with
            {
                ViewProjection = look, InverseViewProjection = lookInverse, CameraPosition = eye, FogDensity = 0,
                SunDirection = noon.LightDirection, SunColor = noon.LightColor, Ambient = noon.Ambient, Night = 0,
            }, who.X * UiScale, (who.Y + Hud.TitleHeight) * UiScale, who.W * UiScale, who.H * UiScale);
            _scene.BeginMeshes();
            if (_previewFigure.Loaded) _previewFigure.Draw(_scene, Matrix4x4.CreateRotationY(pose.Yaw), 0, 0, (float)_voyage.Clock);
            _gfx.EndInset();
        }

        // 선박 정보 창의 왼쪽 칸: 타고 있는 배를 천천히 돌려 보인다
        if (_hud.ShipPreview is { } box)
        {
            // 모형의 가운데를 칸 가운데에 두고 칸에 꽉 차게 — 끌면 돌고 휠로 다가선다(저절로 돌지 않는다)
            _previewBox = (box.X * UiScale, (box.Y + Hud.TitleHeight) * UiScale, box.W * UiScale, box.H * UiScale);
            float spin = _previewYaw;
            // 창이 다른 배를 보이라고 했으면(커스텀설정 조선의 지을 배) 그 모형을 따로 들고 있는다
            var shown = _ship;
            int timber = _voyage.HullColorOf(_voyage.Ship, _voyage.ShipMaterialId);
            if (_hud.PreviewShip is { } other)
            {
                if (_previewShip == null || _previewShipModel != other.Model)
                {
                    _previewShip?.Dispose();
                    (_previewShip, _previewShipModel) = (new ShipModel(_gfx, other.Model), other.Model);
                }
                (shown, timber) = (_previewShip, other.Color);
            }
            var centre = shown.Center;
            float reach = shown.Radius / MathF.Tan(0.31f) * 0.82f * _previewZoom;
            var eye = centre + new Vector3(0, MathF.Sin(_previewPitch), MathF.Cos(_previewPitch)) * reach;
            var look = Matrix4x4.CreateLookAt(eye, centre, Vector3.UnitY)
                       * Matrix4x4.CreatePerspectiveFieldOfView(0.62f, box.W / box.H, 200f, 400000f);
            Matrix4x4.Invert(look, out var lookInverse);
            var noon = Sky.At(0.5);
            _gfx.BeginInset(frame with
            {
                ViewProjection = look, InverseViewProjection = lookInverse, CameraPosition = eye, FogDensity = 0,
                SunDirection = noon.LightDirection, SunColor = noon.LightColor, Ambient = noon.Ambient, Night = 0,
            }, box.X * UiScale, (box.Y + Hud.TitleHeight) * UiScale, box.W * UiScale, box.H * UiScale);
            _scene.BeginMeshes();
            shown.Draw(_scene, Matrix4x4.CreateTranslation(-centre) * Matrix4x4.CreateRotationY(spin) * Matrix4x4.CreateTranslation(centre), null, Hull(shown, timber));
            _gfx.EndInset();
        }

        // 선박 비교 창의 두 배 — 같은 각도로 나란히
        for (int k = 0; k < Math.Min(2, _hud.ComparePreviews.Count); k++)
        {
            var side = _hud.ComparePreviews[k];
            if (_compareShips[k] == null || _compareModels[k] != side.Model)
            {
                _compareShips[k]?.Dispose();
                _compareShips[k] = null;
                try { (_compareShips[k], _compareModels[k]) = (new ShipModel(_gfx, side.Model), side.Model); }
                catch (Exception) { _compareModels[k] = side.Model; }
            }
            if (_compareShips[k] is not { } hull) continue;
            // 두 칸을 한 덩이로 쳐서 어디를 끌어도 함께 돈다
            var first = _hud.ComparePreviews[0];
            var last = _hud.ComparePreviews[^1];
            _previewBox = (first.X * UiScale, (first.Y + Hud.TitleHeight) * UiScale, (last.X + last.W - first.X) * UiScale, first.H * UiScale);
            var centre = hull.Center;
            float reach = hull.Radius / MathF.Tan(0.31f) * 0.82f * _previewZoom;
            var eye = centre + new Vector3(0, MathF.Sin(_previewPitch), MathF.Cos(_previewPitch)) * reach;
            var look = Matrix4x4.CreateLookAt(eye, centre, Vector3.UnitY) * Matrix4x4.CreatePerspectiveFieldOfView(0.62f, side.W / side.H, 200f, 400000f);
            Matrix4x4.Invert(look, out var lookInverse);
            var noon = Sky.At(0.5);
            _gfx.BeginInset(frame with
            {
                ViewProjection = look, InverseViewProjection = lookInverse, CameraPosition = eye, FogDensity = 0,
                SunDirection = noon.LightDirection, SunColor = noon.LightColor, Ambient = noon.Ambient, Night = 0,
            }, side.X * UiScale, (side.Y + Hud.TitleHeight) * UiScale, side.W * UiScale, side.H * UiScale);
            _scene.BeginMeshes();
            hull.Draw(_scene, Matrix4x4.CreateTranslation(-centre) * Matrix4x4.CreateRotationY(_previewYaw) * Matrix4x4.CreateTranslation(centre), null, Hull(hull, side.Color));
            _gfx.EndInset();
        }

        _overUi = _canvas.Pointer.Consumed;
        if (_clicked && !_canvas.Pointer.Consumed) SteerToPointer();

        if (_shotPath != null)
        {
            _gfx.Capture(_shotPath);
            _shotPath = null;
        }
        _gfx.Present();
    }

    private PortScene? _seaCity;
    private int _seaCityId;
    private Vector3 _seaCityAnchor;

    /// <summary>
    /// 바다에서 가까운 도시의 항구 장면을 그 도시 자리에 세운다 — 뭍에 도시가 보이게.
    /// 장면의 배 대는 자리(없으면 장면 가운데)를 도시의 바다 자리에 맞춘다. 방향은 장면 그대로다(세계지도와 맞는지는 못 가렸다).
    /// </summary>
    private void DrawCityAtSea()
    {
        const double range = 60;                    // 세계 좌표 — 이보다 멀면 안 그린다(전에는 14 라 코앞에서야 나타났다)
        CityData? near = null;
        double best = range * range;
        foreach (var city in _voyage.Data.Cities)
        {
            if (city.PortScene == 0 || (city.SeaX == 0 && city.SeaY == 0)) continue;
            double dx = WorldMap.DeltaX(_voyage.ShipX, city.SeaX), dy = city.SeaY - _voyage.ShipY;
            if (dx * dx + dy * dy < best) (near, best) = (city, dx * dx + dy * dy);
        }
        if (near == null) return;
        if (_seaCityId != near.Id)
        {
            _seaCity?.Dispose();
            (_seaCityId, _seaCity) = (near.Id, null);
            try
            {
                _seaCity = new PortScene(_gfx, near.PortScene);
                var berth = _voyage.Data.Settings.Berths.Find(b => b.Scene == near.PortScene);
                _seaCityAnchor = berth != null ? new Vector3(berth.X, 0, berth.Z) : new Vector3(_seaCity.Center.X, 0, _seaCity.Center.Z);
            }
            catch (Exception) { }                  // 장면을 못 읽는 도시는 그냥 둔다
        }
        if (_seaCity == null) return;
        float x = (float)(WorldMap.DeltaX(_voyage.ShipX, near.SeaX) * Terrain.Unit), z = (float)((near.SeaY - _voyage.ShipY) * Terrain.Unit);
        _seaCity.Draw(_scene, Matrix4x4.CreateTranslation(x - _seaCityAnchor.X, 0, z - _seaCityAnchor.Z));
    }

    /// <summary>항구 장면. 배가 원점이니 장면을 배가 뜬 자리만큼 되민다.</summary>
    private void DrawPort()
    {
        if (_portCity != _voyage.City.Id)
        {
            // 배 댈 자리를 정해 둔 장면만 쓴다. 나머지 항구는 세계지도 지형으로 대신한다.
            _port?.Dispose();
            _portCity = _voyage.City.Id;
            _berth = _voyage.Data.Settings.Berths.Find(b => b.Scene == _voyage.City.PortScene);
            _port = _berth != null ? new PortScene(_gfx, _berth.Scene) : null;
        }
        if (_port == null) { _terrain.Draw(_scene, _voyage.ShipX, _voyage.ShipY); return; }
        _port.Draw(_scene, Matrix4x4.CreateTranslation(-_berth!.X, 0, -_berth.Z));
    }

    /// <summary>시내 장면(<c>2000 + 도시 id</c>)과 그 걷는 면을 읽는다. 들어서는 자리는 사람이 모이는 거리.</summary>
    private void LoadTown()
    {
        int scene = _voyage.WalkScene;
        if (_townCity == scene && _town != null) return;
        bool inside = _voyage.Interior != 0 && scene == _voyage.Interior;
        if (inside) _outside = _walk;                       // 나올 때 문 앞으로 돌아오게 적어 둔다
        _town?.Dispose();
        _townCity = scene;
        _grid = TownGrid.Read(scene);
        _town = new PortScene(_gfx, scene, _grid, inside);
        _keepers.Clear();
        _bystanders.Clear();
        (_route, _bound) = ([], null);
        if (_grid == null)
        {
            // 걷는 면을 못 읽는 방이면 들어가지 않고 창만 연다
            if (inside) { var opens = _voyage.InteriorDialog; _voyage.LeaveInterior(); _voyage.Dialog = opens; }
            return;
        }
        _walk = _grid.Entry();
        if (inside)
        {
            // 방 밖으로는 못 나가게 하고, 들어서는 자리가 방 밖이면 방 가운데로 옮긴다
            var (min, max) = _town.Bounds;
            _grid.Bound(min + new Vector3(60, 0, 60), max - new Vector3(60, 0, 60));
            if (_walk.X < min.X + 200 || _walk.X > max.X - 200 || _walk.Y < min.Z + 200 || _walk.Y > max.Z - 200)
                _walk = new Vector2(_town.Center.X, _town.Center.Z);
            _walk = _grid.Nearest(_walk);
            // 문짝을 찾았으면 문 안쪽에서 들어선다 — 닿는 자리를 거기서부터 센다(곳간 같은 막힌 칸에서 시작하지 않게)
            if (_town.Door is { } entry)
            {
                var middle = Vector2.Normalize(new Vector2(_town.Center.X, _town.Center.Z) - entry + new Vector2(0.01f, 0));
                var step = MathF.Abs(middle.X) > MathF.Abs(middle.Y) ? new Vector2(MathF.Sign(middle.X), 0) : new Vector2(0, MathF.Sign(middle.Y));
                _walk = _grid.Nearest(entry + step * 260f);
            }
        }
        if (!inside && _voyage.TownMap is { Marks.Count: > 0 } townMap)
        {
            // 시내: 항구 앞에서 들어선다. 집들에 둘러싸인 안뜰 같은 데서 시작하지 않게, 시설에 가장 많이 닿는 자리를 고른다
            var starts = townMap.Marks.Where(m => m.Place is 5 or 4).Concat(townMap.Marks.Where(m => m.Place is 6 or 7 or 8)).Select(m => _grid.Nearest(m.Scene)).Append(_walk).ToList();
            int most = -1;
            var chosen = _walk;
            foreach (var start in starts)
            {
                _grid.Seal(start);
                int reached = townMap.Marks.Count(m => _grid.Reaches(m.Scene));
                if (reached > most) (most, chosen) = (reached, start);
                if (reached * 2 > townMap.Marks.Count) break;
            }
            _walk = chosen;
        }
        _grid.Seal(_walk);
        if (inside)
        {
            // 방 안: 들어선 자리가 출구, 거기서 가장 먼 닿는 자리 쪽에 조합 마스터
            var far = _walk;
            for (int k = 0; k < 48; k++)
            {
                float angle = k * MathF.Tau / 48;
                for (float reach = 200; reach < 4000; reach += 100)
                {
                    var p = _walk + reach * new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                    if (!_grid.Walkable(p.X, p.Y)) break;
                    if (_grid.Path(_walk, p).Count > 0 && Vector2.Distance(p, _walk) > Vector2.Distance(far, _walk)) far = p;
                }
            }
            // 걷는 면을 방 테두리로 막았으니, 들어선 자리에서 가장 먼 벽 앞(계산대 · 제단 쪽으로 짐작)에 세운다
            var master = _grid.Nearest(_walk + Vector2.Normalize(far - _walk + new Vector2(0.01f, 0)) * MathF.Max(200f, Vector2.Distance(far, _walk) - 220f));
            // 계산대 안쪽처럼 걸어서는 못 닿는 바닥이 있으면 주인은 거기 서고, 말은 그 앞에서 건다
            var front = master;
            if (_grid.Pocket() is { } pocket) (master, front) = (pocket.Inside, pocket.Front);
            // 방의 선분 자료 가운데 긴 것은 계산대 안쪽에서 사람이 서는 줄이다(짧은 것들은 의자) — 있으면 주인을 그 가운데에 세우고,
            // 말은 거기서 가장 가까운 닿는 자리(계산대 앞)에서 건다. 문은 계산대에서 가장 먼 벽 쪽으로 친다(문 자리는 자료에 없다)
            // 긴 선분이 여럿이면(주점의 긴 의자 여섯) 그것은 계산대 줄이 아니다 — 그런 방은 닿지 못하는 바닥(계산대 안쪽)을 쓴다
            var longLines = _grid.Lines.Where(l => Vector3.Distance(l.A, l.B) is > 150f and < 900f).OrderByDescending(l => Vector3.Distance(l.A, l.B)).ToList();
            var stand = longLines.Count is > 0 and <= 2 ? longLines[0] : default;
            var along = _grid.Pocket()?.Axis ?? Vector2.UnitX;
            var door = _walk;
            if (stand != default)
            {
                master = new Vector2((stand.A.X + stand.B.X) / 2, (stand.A.Z + stand.B.Z) / 2);
                front = _grid.Reached(master) ?? front;
                door = _grid.Reached(master, farthest: true) ?? door;
            }
            // 손으로 적어 둔 자리가 있는 방은 그대로 세운다(계산대 안쪽) — 말은 계산대 건너편에서 건다
            if ((_voyage.Data.Settings.RoomSpots.Find(r => r.Scene == scene) ?? Array.Find(SettingsData.KnownRoomSpots, r => r.Scene == scene)) is { } spot)
            {
                var face = new Vector2(spot.FaceX, spot.FaceZ);
                master = new Vector2(spot.HostX, spot.HostZ);
                front = _grid.Nearest(master + face * 420f);
                along = (new Vector2(spot.MaidX, spot.MaidZ) - master) / 320f;
            }
            if (_voyage.InteriorHost != "")
                _keepers.Add((new TownMark(Voyage.InsideMaster, 0, 0, 0, front, 0), _voyage.InteriorHost, master, MathF.Atan2(front.X - master.X, front.Y - master.Y)));
            // 문짝을 메시에서 찾았으면 출구는 그 문 앞이고, 들어설 때도 거기서 몇 걸음 안쪽에 선다
            Vector2? inward = null;
            if (_town.Door is { } leaf)
            {
                var toMiddle = Vector2.Normalize(new Vector2(_town.Center.X, _town.Center.Z) - leaf + new Vector2(0.01f, 0));
                // 문은 벽에 붙어 있다 — 벽에 곧게 안쪽으로
                inward = MathF.Abs(toMiddle.X) > MathF.Abs(toMiddle.Y) ? new Vector2(MathF.Sign(toMiddle.X), 0) : new Vector2(0, MathF.Sign(toMiddle.Y));
                door = _grid.Reached(leaf + inward.Value * 140f) ?? _grid.Nearest(leaf + inward.Value * 140f);
            }
            _keepers.Add((new TownMark(Voyage.InsideExit, 0, 0, 0, door, 0), "출구", door, 0));
            // 주점의 여급 — 계산대 앞에서 손님 쪽을 본다
            if (_voyage.InteriorDialog == Dialog.Tavern && _voyage.HasMaid)
            {
                // 주인 옆, 계산대 안쪽에 나란히 — 말은 그 앞(계산대 건너편)에서 건다
                var maid = master + along * 320f;
                var before = _grid.Nearest(front + along * 320f);
                _keepers.Add((new TownMark(Voyage.InsideMaid, 0, 0, 0, before, 0), "여급", maid, MathF.Atan2(before.X - maid.X, before.Y - maid.Y)));
            }
            // 주점의 뱃사람 — 문 가까이에 서 있고, 말을 걸면 선원을 모집한다
            if (_voyage.InteriorDialog == Dialog.Tavern)
            {
                var sailor = _grid.Reached(door + (inward ?? Vector2.Normalize(master - door + new Vector2(0.01f, 0))) * 420f + new Vector2(260, 120)) ?? door;
                _keepers.Add((new TownMark(Voyage.InsideSailor, 0, 0, 0, sailor, 0), "뱃사람", sailor, MathF.Atan2(door.X - sailor.X, door.Y - sailor.Y)));
            }
            if (inward is { } into)
            {
                _walk = _grid.Reached(door + into * 220f) ?? door;
                _walkYaw = MathF.Atan2(into.X, into.Y);
                return;
            }
            _walk = stand != default ? _grid.Nearest(door + Vector2.Normalize(master - door + new Vector2(0.01f, 0)) * 250f)
                                     : _grid.Nearest(_walk + Vector2.Normalize(far - _walk + new Vector2(0.01f, 0)) * 300f);
            return;
        }
        if (_outside is { } back && _grid.Walkable(back.X, back.Y)) _walk = back;
        _outside = null;
        // 시설 앞에 서 있는 사람들 — 원본 시내 지도의 표식 자리에 세운다
        foreach (var mark in _voyage.TownMap?.Marks ?? [])
            if (Voyage.KeeperName(mark.Place) is { } name && !_keepers.Exists(k => k.Name == name))
            {
                var spot = _grid.Nearest(mark.Scene);
                // 길 쪽(들어선 자리 쪽)을 보고 선다
                _keepers.Add((mark, name, spot, MathF.Atan2(_walk.X - spot.X, _walk.Y - spot.Y)));
            }
        // 조선소 주인 곁의 사람들 — 원본(세비야)처럼 한 줄로 나란히 선다. 말은 못 건다(서 있기만 한다)
        if (_keepers.Find(k => k.Mark.Place == 9) is { Name: not null } owner)
        {
            var side = new Vector2(MathF.Cos(owner.Facing), -MathF.Sin(owner.Facing));
            (string Name, int Step)[] row = [("조선공", -1), ("돛 제작자", 1), ("무기 장인", 2), ("제재소장인", 3)];
            foreach (var (name, step) in row)
            {
                var wanted = owner.Spot + side * (step * 125f);
                var spot = _grid.Nearest(wanted);
                if (Vector2.Distance(spot, wanted) < 70f) _bystanders.Add((owner.Mark, name, spot, owner.Facing));      // 설 자리가 없으면 뺀다
            }
        }
    }

    private readonly List<(TownMark Mark, string Name, Vector2 Spot, float Facing)> _keepers = [], _bystanders = [];
    // 시설의 사람들 차림 — 온전히 그려지는 옷 몇 벌을 이름에 따라 나눠 입힌다
    private static readonly Looks[] KeeperLooks =
    [
        new(0, 5, 6, 4, 2, 0, -1), new(0, 2, 3, 19, 7, 0, -1), new(0, 7, 9, 10, 4, 0, -1),
        new(0, 3, 1, 28, 10, 0, -1), new(0, 9, 4, 7, 3, 0, -1), new(0, 4, 8, 31, 11, 0, -1),
    ];
    private readonly CharacterModel?[] _keeperModels = new CharacterModel?[KeeperLooks.Length];
    private GameCursors? _cursors;
    private bool _overUi;
    private Vector2? _outside;

    /// <summary>말을 걸 만큼 가까운 사람(없으면 null).</summary>
    private (TownMark Mark, string Name, Vector2 Spot, float Facing)? KeeperNear()
    {
        foreach (var keeper in _keepers)
            if (Vector2.Distance(keeper.Spot, _walk) < 220f || (keeper.Mark.Place is Voyage.InsideMaster or Voyage.InsideMaid && Vector2.Distance(keeper.Mark.Scene, _walk) < 160f)) return keeper;
        return null;
    }

    /// <summary>시내 장면을 걷는 사람이 원점에 오게 그린다(걷는 면이 없으면 장면 가운데가 원점).</summary>
    private void DrawTown(float foot, bool figure)
    {
        var origin = Walking ? new Vector3(_walk.X, 0, _walk.Y) : _town!.Center;
        _town!.Draw(_scene, Matrix4x4.CreateTranslation(-origin.X, 0, -origin.Z));
        if (!Walking) return;
        // 시설의 사람들 — 모두 같은 차림의 한 모형을 자리마다 그린다
        if (_keepers.Count > 0)
        {
            foreach (var keeper in _keepers.Concat(_bystanders))
            {
                int wears = keeper.Name.Sum(c => c) % KeeperLooks.Length;
                var _keeperModel = keeper.Name == "여급" ? _maidModel ??= new CharacterModel(_gfx, new Looks(1, 3, 2, 25, 3, 0, -1))
                                                        : _keeperModels[wears] ??= new CharacterModel(_gfx, KeeperLooks[wears]);
                var at = new Vector3(keeper.Spot.X - _walk.X, _grid!.HeightAt(keeper.Spot.X, keeper.Spot.Y), keeper.Spot.Y - _walk.Y);
                if (at.LengthSquared() > 9000f * 9000f || keeper.Name == "출구") continue;
                if (_keeperModel.Loaded) _keeperModel.Draw(_scene, Matrix4x4.CreateRotationY(keeper.Facing) * Matrix4x4.CreateTranslation(at), 0, 0, (float)_voyage.Clock);
                else _scene.Draw(_figure ??= Figure(), Matrix4x4.CreateRotationY(keeper.Facing) * Matrix4x4.CreateTranslation(at));
            }
        }
        if (!figure) return;
        // 겉모습이 바뀌면 사람 모형을 다시 맞춘다. 몸 묶음을 못 읽으면 인형으로 대신한다
        string looks = string.Join(",", _voyage.Looks) + "|" + _voyage.WornKey;      // 입은 장비가 바뀌어도 다시 맞춘다
        if (looks != _characterLooks)
        {
            _character?.Dispose();
            var l = _voyage.Looks;
            _character = new CharacterModel(_gfx, new Looks(l[0], l[1], l[2], l[3], l[4], l[5], l[6]) { Wear = WornNow(l[0]) });
            _characterLooks = looks;
        }
        var stand = Matrix4x4.CreateRotationY(_walkYaw) * Matrix4x4.CreateTranslation(0, foot, 0);
        // 움직인 거리만큼 걸음이 나아간다(한 걸음 주기가 장면 150쯤). 멈추면 선 자세로
        float gone = Vector2.Distance(_walk, _strideFrom);
        // 한 바퀴(두 걸음)에 걷기는 170, 달리기는 290 을 간다
        if (gone > 0.5f && gone < 400f) { _stride += gone / (_sprinting ? 290f : 170f) * MathF.Tau; _strideUntil = _voyage.Clock + 0.12; }
        _strideFrom = _walk;
        // 걸음은 서서히 커지고 서서히 잦아든다
        float want = _voyage.Clock < _strideUntil ? (_sprinting ? 2f : 1f) : 0f;
        _strideAmount += (want - _strideAmount) * 0.14f;
        if (_character is { Loaded: true })
        {
            // 발을 디딜 때마다(한 바퀴에 두 번) 몸이 조금 오르내린다
            _character.Draw(_scene, stand, _stride, _strideAmount, (float)_voyage.Clock);
        }
        else _scene.Draw(_figure ??= Figure(), stand);
    }

    /// <summary>걷는 사람 — 몸 모형(뼈대가 있어야 선다)을 못 그려서 대신 세우는 인형. 키 170, +Z 가 앞.</summary>
    private Mesh Figure()
    {
        var builder = new MeshBuilder();
        Vector4 coat = new(0.16f, 0.22f, 0.42f, 1), hose = new(0.25f, 0.2f, 0.16f, 1), skin = new(0.86f, 0.68f, 0.55f, 1), hat = new(0.12f, 0.1f, 0.1f, 1);
        foreach (float side in new[] { -11f, 11f })
        {
            builder.Cylinder(new Vector3(side, 0, 0), new Vector3(side, 82, 0), 9, 11, hose);
            builder.Cylinder(new Vector3(side * 2.6f, 84, 0), new Vector3(side * 2.2f, 140, 0), 6, 8, coat);
        }
        builder.Cylinder(new Vector3(0, 70, 0), new Vector3(0, 110, 0), 27, 21, coat);
        builder.Cylinder(new Vector3(0, 110, 0), new Vector3(0, 146, 0), 21, 25, coat);
        builder.Cylinder(new Vector3(0, 146, 0), new Vector3(0, 150, 0), 25, 8, coat);
        builder.Cylinder(new Vector3(0, 148, 0), new Vector3(0, 170, 0), 11, 12, skin);
        builder.Cylinder(new Vector3(0, 166, 0), new Vector3(0, 169, 0), 25, 25, hat);
        builder.Cylinder(new Vector3(0, 169, 0), new Vector3(0, 169.5f, 0), 25, 13, hat);
        builder.Cylinder(new Vector3(0, 169, 0), new Vector3(0, 182, 0), 13, 11, hat);
        builder.Cylinder(new Vector3(0, 182, 0), new Vector3(0, 182.5f, 0), 11, 0, hat);
        builder.Box(new Vector3(-3, 154, 10), new Vector3(3, 160, 15), skin);   // 코 — 앞이 어느 쪽인지 보이게
        return builder.Build(_gfx);
    }

    /// <summary>바다를 누르면 그쪽으로 뱃머리를 돌린다.</summary>
    private void SteerToPointer()
    {
        // 조선소 주인 차림(구석의 작은 창)은 바깥을 누르면 닫히고 그 자리로 걸어간다
        if (_voyage.Dialog == Dialog.ShipyardMenu && Walking) _voyage.Dialog = Dialog.None;
        if (_voyage.Dialog != Dialog.None) return;
        Matrix4x4.Invert(_viewProjection, out var inverse);
        float nx = _mouseX / (float)_gfx.Width * 2 - 1, ny = 1 - _mouseY / (float)_gfx.Height * 2;
        var far4 = Vector4.Transform(new Vector4(nx, ny, 1, 1), inverse);
        var direction = new Vector3(far4.X, far4.Y, far4.Z) / far4.W - _eye;
        if (Walking)
        {
            // 시내: 누른 곳까지 걸어간다. 사람을 눌렀으면 그 사람에게 가서 말을 건다
            var ray = Vector3.Normalize(direction);
            var origin = new Vector3(_walk.X, 0, _walk.Y);
            foreach (var keeper in _keepers)
            {
                var chest = new Vector3(keeper.Spot.X, _grid!.HeightAt(keeper.Spot.X, keeper.Spot.Y) + 100, keeper.Spot.Y) - origin - _eye;
                float along = Vector3.Dot(chest, ray);
                if (along > 0 && (chest - ray * along).Length() < 90f) { WalkTo(keeper.Mark); return; }
            }
            for (float reach = 50; reach < 30000; reach += 25)
            {
                var p = _eye + ray * reach + origin;
                if (!_grid!.Inside(p.X, p.Z) || p.Y > _grid.HeightAt(p.X, p.Z)) continue;
                _route = _grid.Path(_walk, new Vector2(p.X, p.Z));
                (_bound, _routeRuns) = (null, true);          // 눌러 가는 곳으로는 달려간다
                return;
            }
            return;
        }
        if (_voyage.Mode != Mode.Sea) return;
        if (direction.Y >= -1e-4f) return;                 // 수평선 위를 눌렀다
        var hit = _eye + direction * (-_eye.Y / direction.Y);
        _voyage.SteerTo(Math.Atan2(hit.X, -hit.Z));
    }

    // ── 확인용 대본 ──────────────────────────────────────────────────────────

    private void RunScript(double dt)
    {
        if (_script.Count == 0) return;
        if (_scriptWait > 0) { _scriptWait -= dt; return; }
        if (_shotPath != null) return;

        var parts = _script.Dequeue().Split(':', 2);
        string argument = parts.Length > 1 ? parts[1] : "";
        double Number() => double.Parse(argument, CultureInfo.InvariantCulture);
        switch (parts[0])
        {
            case "wait": _scriptWait = Number(); break;
            case "shot": _shotPath = argument; break;
            case "quit": _running = false; break;
            case "depart": _voyage.Depart(); break;
            case "display":
                var size = argument.Split('x');
                SetDisplay(int.Parse(size[0]), int.Parse(size[1]), false, false);
                break;
            case "town": _voyage.TownView = !_voyage.TownView; break;
            case "hold": _keys.Add(argument[0]); break;
            case "release": _keys.Remove(argument[0]); break;
            case "walk":
                var spot = argument.Split(',');
                _walk = new Vector2(float.Parse(spot[0], CultureInfo.InvariantCulture), float.Parse(spot[1], CultureInfo.InvariantCulture));
                break;
            case "where":
                var at = new Vector3(_walk.X, _grid!.HeightAt(_walk.X, _walk.Y) + 150, _walk.Y);
                string report = $"walk {_walk} foot {at.Y - 150} yaw {_yaw} pitch {_pitch} view {_viewPitch} sight {_sight} dist {_distance}";
                for (float pitch = 0.1f; pitch < 1.5f; pitch += 0.2f)
                    report += $" | {pitch:F1}:{_grid.Sight(at, at + _distance * new Vector3(MathF.Cos(pitch) * MathF.Sin(_yaw), MathF.Sin(pitch), MathF.Cos(pitch) * MathF.Cos(_yaw))):F2}";
                File.AppendAllText(argument, report + $" door {_town?.Door} centre {_town?.Center}\n");
                break;
            case "walkgrid":
                if (_grid?.Picture() is { } picture) Png.Write(argument, picture.Width, picture.Height, picture.Rows);
                break;
            case "city": _voyage.GoTo((int)Number()); break;
            case "wreck": _voyage.Sink(); break;
            case "guild": _voyage.Dialog = Dialog.Guild; break;
            case "offer": _voyage.Offered = _voyage.QuestsHere().ElementAtOrDefault((int)Number()); break;
            case "accept": _voyage.AcceptQuest(); break;
            case "report": _voyage.Report(); break;
            case "close": _voyage.Dialog = Dialog.None; break;
            case "sail": _voyage.ChangeSail((int)Number()); break;
            case "steer": _voyage.SteerTo(Number() * Math.PI / 180); break;
            case "key": KeyPressed(argument[0]); break;
            case "search": _voyage.Search(); break;
            case "yaw": _yaw = (float)Number(); break;
            case "behind": _yawGoal = -(float)_voyage.Heading; break;
            case "pitch": _pitch = (float)Number(); break;
            case "zoom": _distance = (float)Number(); break;
            case "phase": _voyage.SetSkyPhase(Number()); break;
            case "speed": _voyage.TimeScale = Number(); break;
            case "disaster": _voyage.StartDisaster((int)Number()); break;
            case "storm": _voyage.StartStorm(); break;
            case "skills": _voyage.Dialog = Dialog.Skills; break;
            case "wizard": _hud.Fill(argument); break;
            case "create": _hud.Finish(); break;
            case "aides": _voyage.Dialog = Dialog.Aides; break;
            case "day": _voyage.PassDay(); break;
            case "mastery": _voyage.AddMastery((int)Number()); break;
            case "jobs": _voyage.Dialog = Dialog.Jobs; break;
            case "combine": if (_voyage.Dock.Count >= 2) _voyage.Combine(_voyage.Dock[0], _voyage.Dock[1]); break;
            case "combinewindow": _voyage.Dialog = Dialog.Combine; break;
            case "combinewith":
                var chosen = argument.Split(',');
                if (_voyage.Dock.Count >= 2) _voyage.Combine(_voyage.Dock[0], _voyage.Dock[1], int.Parse(chosen[0]), chosen.Length > 1 ? int.Parse(chosen[1]) : 0);
                break;
            case "learnfrom": _voyage.LearnFrom((int)Number()); break;
            case "cargo": _voyage.Dialog = Dialog.Cargo; break;
            case "sail-look":
                var sailLook = argument.Split(',');
                _voyage.ShowSail(int.Parse(sailLook[0]), int.Parse(sailLook[1]));
                break;
            case "type": foreach (char c in argument) _hud.Type(c); break;
            case "university": _voyage.Dialog = Dialog.University; break;
            case "hire": if (_voyage.AidesToHire().ElementAtOrDefault((int)Number()) is { } who) _voyage.HireAide(who); break;
            case "court": _voyage.Dialog = Dialog.Court; break;
            case "order": if (_voyage.OrdersOffered().ElementAtOrDefault((int)Number()) is { } royal) _voyage.AcceptOrder(royal); break;
            case "fulfil": _voyage.CompleteOrder(); break;
            case "buyitem": if (_voyage.ItemOf((int)Number()) is { } wares) _voyage.BuyItem(wares); break;
            case "addrecipe": if (_voyage.Data.Recipes.Find(r => r.Id == (int)Number()) is { } learned) _voyage.AddRecipe(learned); break;
            case "produce": if (_voyage.RuleOf((int)Number()) is { } make) _voyage.Produce(make, 1); break;
            case "addgood":
                var given = argument.Split(',');
                if (!_voyage.Cargo.TryGetValue(int.Parse(given[0]), out var held)) _voyage.Cargo[int.Parse(given[0])] = held = new CargoItem();
                held.Count += int.Parse(given[1]);
                break;
            case "itemtab": _hud.ItemTab = (int)Number(); break;
            case "custom": _hud.OpenCustomBuild((int)Number()); break;
            case "build":
                var plan = argument.Split(',');
                if (_voyage.Data.Ships.Find(s => s.Id == int.Parse(plan[0])) is { } hull) _voyage.OrderShip(hull, int.Parse(plan[1]), int.Parse(plan[2]));
                break;
            case "receive": _voyage.ReceiveShip(); break;
            case "townmenu": _hud.TownMenuOpen = !_hud.TownMenuOpen; break;
            case "useskills": _voyage.Dialog = Dialog.UseSkills; break;
            case "slot": _voyage.UsePageSlot((int)Number() - 1); break;
            case "quick": _hud.QuickOpen = !_hud.QuickOpen; break;
            case "quicksetup": _voyage.Dialog = Dialog.QuickSetup; break;
            case "setslot":
                var put = argument.Split(',');
                _voyage.SetQuickSlot(int.Parse(put[0]), int.Parse(put[1]));
                break;
            case "strengthen":
                if (argument.Length == 0) _voyage.Dialog = Dialog.Strengthen;
                else _voyage.Strengthen(argument.Split(',').Select(int.Parse).ToList());
                break;
            case "workmethod": _voyage.Dialog = Dialog.WorkMethod; break;
            case "keys": _hud.OpenMenu(2); break;
            case "wheel": _hud.Wheel((int)Number()); break;
            case "dev": _hud.OpenMenu(3); break;
            case "money": _voyage.AddMoney((int)Number()); break;
            case "click":
                var where = argument.Split(',');
                (_mouseX, _mouseY) = (int.Parse(where[0]), int.Parse(where[1]));
                _clicked = true;                    // 진짜 클릭처럼 화면 창을 먼저 거친다
                break;
            case "outfit": _voyage.Dialog = Dialog.Outfit; break;
            case "character": _voyage.Dialog = Dialog.Character; break;
            case "topmenu": _hud.OpenTopMenu((int)Number()); break;
            case "look":
                var look = argument.Split(',');
                _voyage.SetLook(int.Parse(look[0]), int.Parse(look[1]));
                break;
            case "parts": _voyage.Dialog = Dialog.ShipParts; break;
            case "buypart": if (_voyage.PartsForSale().ElementAtOrDefault((int)Number()) is { } part) _voyage.BuyPart(part); break;
            case "items": _voyage.Dialog = Dialog.Items; break;
            case "additem": _voyage.AddItem((int)Number()); break;
            case "useitem": _voyage.UseItem((int)Number()); break;
            case "uiscale": _scaleInScript = true; _voyage.Data.Settings.UiScale = Number(); break;
            case "iconscale": _voyage.Data.Settings.IconScale = Number(); break;
            case "givepart": if (_voyage.Data.ShipParts.Find(p => p.Id == (int)Number()) is { } handed) { _voyage.GivePart(handed); _voyage.Fit(handed); } break;
            case "deco": _voyage.AddItem((int)Number()); _voyage.FitDeco((int)Number()); break;
            case "nation": _voyage.SetNationForTest((int)Number()); break;
            case "hullmat": _voyage.SetMaterialForTest((int)Number()); break;
            case "warp": _voyage.WarpToSea((int)Number()); break;
            case "warpmenu": _hud.OpenWarp(argument == "city"); break;
            case "sea":                             // 바다 위의 자리로 옮긴다: sea:x,y
                var seaAt = argument.Split(',');
                _voyage.Teleport(double.Parse(seaAt[0], CultureInfo.InvariantCulture), double.Parse(seaAt[1], CultureInfo.InvariantCulture));
                break;
            case "hullbase": _ship.SetHullBase((int)Number()); break;
            case "menu": _hud.OpenMenu((int)Number()); break;
            case "music": _musicOn = !_musicOn; break;
            case "goto":
                if (_keepers.Find(k => k.Mark.Place == (int)Number()) is { Name: not null } person) WalkTo(person.Mark);
                else if (_voyage.TownMap?.Marks.Find(m => m.Place == (int)Number()) is { } there) WalkTo(there);
                break;
            case "swap": _voyage.Dialog = Dialog.ShipSwap; break;
            case "specialbuild": _voyage.Dialog = Dialog.SpecialBuild; break;
            case "hull": _hud.OpenHullBuild(); break;
            case "equip": _voyage.Dialog = Dialog.Equip; break;
            case "bonus": _voyage.Work.Bonuses.Add((int)Number()); _voyage.Work.Grade++; _voyage.AddMastery(1, true); break;
            case "hullbuild": _voyage.Dialog = Dialog.HullBuild; break;
            case "yardmenu": _voyage.Dialog = Dialog.ShipyardMenu; break;
            case "shipinfo": _voyage.Dialog = Dialog.ShipInfo; break;
            case "exp": { var two = argument.Split(','); _voyage.GainExp(int.Parse(two[0]), int.Parse(two[1])); break; }
            case "dyedebug": ShipModel.DyeDebug = [new(1, 1, 1, 1), new(1, 0.1f, 0.1f, 1), new(0.1f, 1, 0.1f, 1), new(0.2f, 0.3f, 1, 1), new(1, 1, 0.1f, 1), new(1, 0, 1, 1), new(0, 1, 1, 1), new(0, 0, 0, 1)]; break;
            case "combinepick": { var two = argument.Split(','); _hud.PickCombine(int.Parse(two[0]), int.Parse(two[1])); break; }
            case "combineaboard": (_voyage.Data.Settings.ModCombineOnBoard, _voyage.Dialog) = (true, Dialog.Combine); break;
            case "board": if (_voyage.Dock.ElementAtOrDefault((int)Number()) is { } docked) _voyage.SwapShip(docked); break;
            case "skilltab": _hud.SkillTab = (int)Number(); break;
            case "shipyard": _voyage.Dialog = Dialog.Shipyard; break;
            case "trade": _voyage.Dialog = Dialog.Trade; break;
            case "ship": if (_voyage.Data.Ships.Find(s => s.Id == (int)Number()) is { } ship) _voyage.BuyShip(ship); break;
            case "buygood": if (_voyage.GoodsHere().ElementAtOrDefault((int)Number()) is { } good) _voyage.BuyGood(good, 50); break;
            case "sellall": foreach (int id in _voyage.Cargo.Keys.ToList()) if (_voyage.Good(id) is { } g) _voyage.SellGood(g, int.MaxValue); break;
            case "use": if (_voyage.Data.SkillRules.Find(r => r.SkillId == (int)Number()) is { } used) _voyage.UseSkill(used); break;
            case "learn": if (_voyage.Data.Skills.Find(s => s.Id == (int)Number()) is { } skill) _voyage.Learn(skill, true); break;
            case "supply": _voyage.Dialog = Dialog.Supply; break;
            case "buy": if (_voyage.Data.Supplies.Find(s => s.Id == (int)Number()) is { } item) _voyage.BuySupply(item); break;
            case "cure": if (_voyage.Disasters.FirstOrDefault() is { } first) _voyage.Cure(first); break;
        }
    }

    public void Dispose()
    {
        if (_active == this) _active = null;
        _music?.Dispose();
        _canvas?.Dispose();
        _port?.Dispose();
        _town?.Dispose();
        _figure?.Dispose();
        _character?.Dispose();
        foreach (var model in _keeperModels) model?.Dispose();
        _cursors?.Dispose();
        _ship?.Dispose();
        _terrain?.Dispose();
        _scene?.Dispose();
        _gfx?.Dispose();
        if (_hwnd != IntPtr.Zero) { Win32.DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
    }
}
