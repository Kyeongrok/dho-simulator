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
    private PortScene? _port;
    private BerthData? _berth;
    private int _portCity;
    private Canvas _canvas = null!;
    private Hud _hud = null!;
    private Voyage _voyage = null!;

    // 시점: 배를 가운데 두고 도는 카메라
    private float _yaw = 2.75f, _pitch = 0.50f, _distance = 30000f;
    private bool _orbiting, _leftDown;
    private int _mouseX, _mouseY, _dragX, _dragY;
    private bool _clicked;
    private readonly HashSet<int> _keys = [];

    private Matrix4x4 _viewProjection;
    private Vector3 _eye;

    private readonly Queue<string> _script = new();
    private double _scriptWait;
    private string? _shotPath;

    public GameWindow(string? script)
    {
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
        _voyage = new Voyage(GameData.Load());
        _ship = new ShipModel(_gfx, _voyage.Data.Settings.ShipEntry);
        _canvas = new Canvas(_gfx);
        _hud = new Hud(_canvas, _voyage);

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

            RunScript(dt);
            Tick(dt);
            Render();
            _clicked = false;
        }
    }

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
        Win32.AdjustWindowRect(ref rect, Win32.WS_OVERLAPPEDWINDOW, false);

        _active = this;
        _hwnd = Win32.CreateWindowExW(0, ClassName, "대항해시대 온라인 — 항해 (개인용)",
            Win32.WS_OVERLAPPEDWINDOW, Win32.CW_USEDEFAULT, Win32.CW_USEDEFAULT,
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
            case Win32.WM_ERASEBKGND:
                return 1;
            case Win32.WM_SIZE:
                _gfx?.Resize(Win32.LowWord(lParam) & 0xFFFF, Win32.HighWord(lParam) & 0xFFFF);
                return IntPtr.Zero;

            case Win32.WM_KEYDOWN:
                if (_keys.Add((int)wParam)) KeyPressed((int)wParam);
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
                _distance = Math.Clamp(_distance * MathF.Pow(0.88f, wheel / 120f), 9000f, 160000f);
                return IntPtr.Zero;
        }
        return Win32.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void KeyPressed(int key)
    {
        switch (key)
        {
            case Win32.VK_ESCAPE:
                if (_voyage.Dialog != Dialog.None) _voyage.Dialog = Dialog.None;
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
    }

    private void Render()
    {
        // 배가 장면의 원점이다
        float sway = (float)Math.Sin(_voyage.Clock * 1.1);
        var target = new Vector3(0, 3500, 0);
        _eye = target + _distance * new Vector3(MathF.Cos(_pitch) * MathF.Sin(_yaw), MathF.Sin(_pitch), MathF.Cos(_pitch) * MathF.Cos(_yaw));
        var view = Matrix4x4.CreateLookAt(_eye, target, Vector3.UnitY);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(0.85f, _gfx.Width / (float)_gfx.Height, 400f, 3000000f);
        _viewProjection = view * projection;
        Matrix4x4.Invert(_viewProjection, out var inverse);

        var sky = Sky.At(_voyage.SkyPhase);
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
            FogDensity = 0.0000032f,
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
        else DrawPort();

        bool inPort = _voyage.Mode == Mode.Port && _port != null;
        float roll = sway * 0.025f + (float)Math.Clamp(_voyage.Knots / 14, 0, 1) * 0.04f;
        var shipWorld = Matrix4x4.CreateRotationZ(roll)
                        * Matrix4x4.CreateRotationY(inPort ? _berth!.Yaw : MathF.PI - (float)_voyage.Heading)
                        * Matrix4x4.CreateTranslation(0, sway * 60f, 0);
        _ship.Draw(_scene, shipWorld);

        // 화면 글과 창
        _canvas.Pointer = new Pointer { X = _mouseX, Y = _mouseY, Clicked = _clicked };
        _canvas.Begin();
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
        }
    }

    public void Dispose()
    {
        if (_active == this) _active = null;
        _canvas?.Dispose();
        _port?.Dispose();
        _ship?.Dispose();
        _terrain?.Dispose();
        _scene?.Dispose();
        _gfx?.Dispose();
        if (_hwnd != IntPtr.Zero) { Win32.DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
    }
}
