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
    private static readonly Win32.WndProc StaticWndProcDelegate = StaticWndProcTrampoline;
    private static GameWindow? _active;
    private static ushort _classAtom;
    private bool _running;

    private Gfx _gfx = null!;
    private SceneRenderer _scene = null!;
    private Terrain _terrain = null!;
    private ShipModel _ship = null!;
    private int _shipModel;
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
    private int _mouseX, _mouseY, _dragX, _dragY;
    private bool _clicked;
    private readonly HashSet<int> _keys = [];

    private float _overcast;
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
        if (_newGame) data.DeleteSave();
        _voyage = new Voyage(data, developer: _scripted && !_newGame);
        _shipModel = _voyage.Ship.Model;
        _ship = new ShipModel(_gfx, _shipModel);
        _canvas = new Canvas(_gfx);
        _hud = new Hud(_canvas, _voyage)
        {
            SetDisplay = SetDisplay, WalkTo = WalkTo,
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
        if (fullscreen)
        {
            Win32.SetWindowLongPtrW(_hwnd, Win32.GWL_STYLE, (IntPtr)unchecked((uint)(Win32.WS_POPUP | Win32.WS_VISIBLE)));
            Win32.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, screenWidth, screenHeight, Win32.SWP_NOZORDER | Win32.SWP_FRAMECHANGED | Win32.SWP_SHOWWINDOW);
        }
        else
        {
            var rect = new Win32.Rect { Left = 0, Top = 0, Right = width, Bottom = height };
            Win32.SetWindowLongPtrW(_hwnd, Win32.GWL_STYLE, (IntPtr)unchecked((uint)(Win32.WS_OWNTITLE | Win32.WS_VISIBLE)));
            Win32.SetWindowPos(_hwnd, IntPtr.Zero, Math.Max(0, (screenWidth - rect.Width) / 2), Math.Max(0, (screenHeight - rect.Height) / 2),
                rect.Width, rect.Height, Win32.SWP_NOZORDER | Win32.SWP_FRAMECHANGED | Win32.SWP_SHOWWINDOW);
        }
        if (!remember) return;
        var settings = _voyage.Data.Settings;
        (settings.WindowWidth, settings.WindowHeight, settings.Fullscreen) = (width, height, fullscreen);
        _voyage.Data.SaveSettings();
    }

    /// <summary>화면 글의 배율 — 설정값, 없으면 윈도의 배율. 대본으로 돌릴 때는 1(찍은 그림을 견주려고).</summary>
    private float UiScale
    {
        get
        {
            if (_scripted && !_scaleInScript) return 1;
            double set = _voyage?.Data.Settings.UiScale ?? 0;
            return (float)Math.Clamp(set > 0 ? set : Win32.GetDpiForWindow(_hwnd) / 96.0, 0.75, 3);
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
            case Win32.WM_ERASEBKGND:
                return 1;
            case Win32.WM_SIZE:
                _gfx?.Resize(Win32.LowWord(lParam) & 0xFFFF, Win32.HighWord(lParam) & 0xFFFF);
                return IntPtr.Zero;

            case Win32.WM_KEYDOWN:
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
                if (_orbiting)
                {
                    _yaw -= (x - _mouseX) * 0.006f;
                    _pitch = Math.Clamp(_pitch + (y - _mouseY) * 0.005f, 0.06f, 1.45f);
                }
                _mouseX = x;
                _mouseY = y;
                return IntPtr.Zero;
            case Win32.WM_RBUTTONDOWN:
                _orbiting = true;
                Win32.SetCapture(hWnd);
                return IntPtr.Zero;
            case Win32.WM_RBUTTONUP:
                _orbiting = false;
                Win32.ReleaseCapture();
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
                _distance = Math.Clamp(_distance * MathF.Pow(0.88f, wheel / 120f), Walking ? 500f : 9000f, 160000f);
                return IntPtr.Zero;
        }
        return Win32.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void KeyPressed(int key)
    {
        if (key == Win32.VK_F11)
        {
            var settings = _voyage.Data.Settings;
            SetDisplay(settings.WindowWidth, settings.WindowHeight, !settings.Fullscreen);
            return;
        }
        if (!_voyage.Created) return;
        switch (key)
        {
            case Win32.VK_ESCAPE:
                if (_voyage.Dialog != Dialog.None) _voyage.Dialog = Dialog.None;
                break;
            case '5' when _keys.Contains(Win32.VK_CONTROL):
                _hud.TownMapOpen = !_hud.TownMapOpen;      // 원본의 지도 단축키
                break;
            case 'I':
                if (_voyage.Dialog == Dialog.Items) _voyage.Dialog = Dialog.None;
                else if (_voyage.Dialog == Dialog.None) _voyage.Dialog = Dialog.Items;
                break;
            case 'T' when _voyage.Mode == Mode.Port && _voyage.TownView && _voyage.Dialog == Dialog.None:
                _hud.TownMenuOpen = !_hud.TownMenuOpen;
                break;
            case Win32.VK_F2:
                // 원본처럼 F2 로 스킬 사용 창을 여닫는다
                if (_voyage.Dialog == Dialog.UseSkills) _voyage.Dialog = Dialog.None;
                else if (_voyage.Dialog == Dialog.None) _voyage.Dialog = Dialog.UseSkills;
                break;
            case >= '1' and <= '8' when !_keys.Contains(Win32.VK_CONTROL) && _voyage.Dialog is Dialog.None or Dialog.UseSkills:
                _voyage.UsePageSlot(key - '1');
                break;
            case 'C':
                if (_voyage.Dialog == Dialog.Outfit) _voyage.Dialog = Dialog.None;
                else if (_voyage.Dialog == Dialog.None) _voyage.Dialog = Dialog.Outfit;
                break;
            case 'X':
                // 원본처럼 X 로 스킬 창을 여닫는다
                if (_voyage.Dialog == Dialog.Skills) _voyage.Dialog = Dialog.None;
                else if (_voyage.Dialog == Dialog.None) _voyage.Dialog = Dialog.Skills;
                break;
            case 'W' or Win32.VK_UP: _voyage.ChangeSail(+1); break;
            case 'S' or Win32.VK_DOWN: _voyage.ChangeSail(-1); break;
            case 'F' or Win32.VK_RETURN:
                if (_voyage.Dialog != Dialog.None) break;
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
            _music.Play(_musicOn && _voyage.Created && _voyage.Data.Settings.MusicVolume > 0 ? _voyage.MusicNumber : 0);
        }
        if (Walking && _voyage.Dialog == Dialog.None) Walk((float)dt);
    }

    private bool Walking => _townView && _grid != null;

    /// <summary>시내 걷기 — W·S 는 보는 쪽으로 앞뒤, A·D 는 옆. 벽에 걸리면 벽을 따라 미끄러진다.</summary>
    private List<Vector2> _route = [];
    private TownMark? _bound;

    /// <summary>지도에서 시설을 누르면 그리로 걸어간다.</summary>
    private void WalkTo(TownMark mark)
    {
        if (!Walking) return;
        _route = _grid!.Path(_walk, mark.Scene);
        _bound = _route.Count > 0 ? mark : null;
        _voyage.Say(_route.Count > 0 ? $"{_voyage.PlaceName(mark.Place)}(으)로 간다." : $"{_voyage.PlaceName(mark.Place)}까지 가는 길을 못 찾았다.");
    }

    private void Walk(float dt)
    {
        const float speed = 900f;
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
                if (_route.Count == 0 && _bound is { } reached) { _bound = null; _voyage.Visit(reached); }
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
        // 배가 장면의 원점이다
        float sway = (float)Math.Sin(_voyage.Clock * 1.1);
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
        if (Walking)
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

        // 날씨가 궂을수록 하늘이 가라앉고 멀리 안 보인다
        float overcast = _voyage.Mode != Mode.Sea ? 0 : _voyage.Weather switch { Weather.Storm => 1f, Weather.Rain => 0.7f, Weather.Cloudy => 0.35f, _ => 0f };
        _overcast += (overcast - _overcast) * 0.02f;
        var sky = Sky.At(_voyage.SkyPhase).Overcast(_overcast);
        var heading = new Vector2(MathF.Sin((float)_voyage.Heading), -MathF.Cos((float)_voyage.Heading));
        var frame = new FrameConstants
        {
            ViewProjection = _viewProjection,
            InverseViewProjection = inverse,
            CameraPosition = _eye,
            Time = (float)_voyage.Clock,
            SunDirection = sky.LightDirection,
            Night = sky.Night,
            SunColor = sky.LightColor,
            FogDensity = 0.0000032f * (1 + _overcast * 3),
            Ambient = sky.Ambient,
            HorizonColor = sky.Horizon,
            ZenithColor = sky.Zenith,
            WaterColor = sky.Water,
            WorldOffset = new Vector2((float)(_voyage.ShipX * Terrain.Unit % 1048576), (float)(_voyage.ShipY * Terrain.Unit % 1048576)),
            ShipPosition = Vector2.Zero,
            ShipDirection = heading,
            ShipSpeed = (float)Math.Clamp(_voyage.Knots / 12, 0, 1),
        };

        _gfx.Begin(frame);
        _scene.DrawSky();
        _scene.DrawOcean();
        _scene.BeginMeshes();
        if (_voyage.Mode == Mode.Sea) _terrain.Draw(_scene, _voyage.ShipX, _voyage.ShipY);
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
        float roll = sway * 0.025f + (float)Math.Clamp(_voyage.Knots / 14, 0, 1) * 0.04f;
        var shipWorld = Matrix4x4.CreateRotationZ(roll)
                        * Matrix4x4.CreateRotationY(inPort ? _berth!.Yaw : MathF.PI - (float)_voyage.Heading)
                        * Matrix4x4.CreateTranslation(0, sway * 60f, 0);
        if (!town) _ship.Draw(_scene, shipWorld);

        // 화면 글과 창
        _canvas.Scale = UiScale;
        _canvas.Pointer = new Pointer { X = _mouseX / UiScale, Y = _mouseY / UiScale - Hud.TitleHeight, Clicked = _clicked };
        _canvas.Top = Hud.TitleHeight;
        _canvas.Begin();
        (_hud.TownGrid, _hud.TownSpot, _hud.TownFacing) = (Walking ? _grid : null, _walk, _walkYaw);
        _hud.Draw();
        _canvas.End();

        if (_clicked && !_canvas.Pointer.Consumed) SteerToPointer();

        if (_shotPath != null)
        {
            _gfx.Capture(_shotPath);
            _shotPath = null;
        }
        _gfx.Present();
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
        if (_townCity == _voyage.City.Id && _town != null) return;
        _town?.Dispose();
        _townCity = _voyage.City.Id;
        _grid = TownGrid.Read(_voyage.City.TownScene);
        _town = new PortScene(_gfx, _voyage.City.TownScene, _grid);
        if (_grid == null) return;
        _walk = _grid.Entry();
        _grid.Seal(_walk);
    }

    /// <summary>시내 장면을 걷는 사람이 원점에 오게 그린다(걷는 면이 없으면 장면 가운데가 원점).</summary>
    private void DrawTown(float foot, bool figure)
    {
        var origin = Walking ? new Vector3(_walk.X, 0, _walk.Y) : _town!.Center;
        _town!.Draw(_scene, Matrix4x4.CreateTranslation(-origin.X, 0, -origin.Z));
        if (!Walking || !figure) return;
        // 겉모습이 바뀌면 사람 모형을 다시 맞춘다. 몸 묶음을 못 읽으면 인형으로 대신한다
        string looks = string.Join(",", _voyage.Looks);
        if (looks != _characterLooks)
        {
            _character?.Dispose();
            var l = _voyage.Looks;
            _character = new CharacterModel(_gfx, new Looks(l[0], l[1], l[2], l[3], l[4], l[5], l[6]));
            _characterLooks = looks;
        }
        var stand = Matrix4x4.CreateRotationY(_walkYaw) * Matrix4x4.CreateTranslation(0, foot, 0);
        if (_character is { Loaded: true }) _character.Draw(_scene, stand);
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
        if (_voyage.Mode != Mode.Sea || _voyage.Dialog != Dialog.None) return;
        Matrix4x4.Invert(_viewProjection, out var inverse);
        float nx = _mouseX / (float)_gfx.Width * 2 - 1, ny = 1 - _mouseY / (float)_gfx.Height * 2;
        var far4 = Vector4.Transform(new Vector4(nx, ny, 1, 1), inverse);
        var direction = new Vector3(far4.X, far4.Y, far4.Z) / far4.W - _eye;
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
                File.AppendAllText(argument, report + "\n");
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
            case "hire": if (_voyage.AidesToHire().ElementAtOrDefault((int)Number()) is { } who) _voyage.HireAide(who); break;
            case "court": _voyage.Dialog = Dialog.Court; break;
            case "order": if (_voyage.OrdersOffered().ElementAtOrDefault((int)Number()) is { } royal) _voyage.AcceptOrder(royal); break;
            case "fulfil": _voyage.CompleteOrder(); break;
            case "buyitem": if (_voyage.ItemOf((int)Number()) is { } wares) _voyage.BuyItem(wares); break;
            case "addrecipe": if (_voyage.Data.Recipes.Find(r => r.Id == (int)Number()) is { } learned) _voyage.AddRecipe(learned); break;
            case "produce": if (_voyage.RuleOf((int)Number()) is { } make) _voyage.Produce(make, 1); break;
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
            case "outfit": _voyage.Dialog = Dialog.Outfit; break;
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
            case "menu": _hud.OpenMenu((int)Number()); break;
            case "music": _musicOn = !_musicOn; break;
            case "goto":
                if (_voyage.TownMap?.Marks.Find(m => m.Place == (int)Number()) is { } there) WalkTo(there);
                break;
            case "swap": _voyage.Dialog = Dialog.ShipSwap; break;
            case "board": if (_voyage.Dock.ElementAtOrDefault((int)Number()) is { } docked) _voyage.SwapShip(docked); break;
            case "skilltab": _hud.SkillTab = (int)Number(); break;
            case "shipyard": _voyage.Dialog = Dialog.Shipyard; break;
            case "trade": _voyage.Dialog = Dialog.Trade; break;
            case "ship": if (_voyage.Data.Ships.Find(s => s.Id == (int)Number()) is { } ship) _voyage.BuyShip(ship); break;
            case "buygood": if (_voyage.GoodsHere().ElementAtOrDefault((int)Number()) is { } good) _voyage.BuyGood(good, 50); break;
            case "sellall": foreach (int id in _voyage.Cargo.Keys.ToList()) if (_voyage.Good(id) is { } g) _voyage.SellGood(g, int.MaxValue); break;
            case "use": if (_voyage.Data.SkillRules.Find(r => r.SkillId == (int)Number()) is { } used) _voyage.UseSkill(used); break;
            case "learn": if (_voyage.Data.Skills.Find(s => s.Id == (int)Number()) is { } skill) _voyage.Learn(skill); break;
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
        _ship?.Dispose();
        _terrain?.Dispose();
        _scene?.Dispose();
        _gfx?.Dispose();
        if (_hwnd != IntPtr.Zero) { Win32.DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
    }
}
