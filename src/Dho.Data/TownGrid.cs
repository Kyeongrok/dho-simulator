using System.Buffers.Binary;
using System.Numerics;

namespace Dho.Data;

/// <summary>
/// 시내 장면 정보(<c>GRI</c>, <c>0002\(0x10000 + 장면 번호).bin</c>)의 걷는 면과 충돌.
/// </summary>
/// <remarks>
/// 머리는 u32 들이다.
/// +0x28 (자리, 정점 수, 마디 수) — <b>잔 충돌</b>: 계단·비탈의 바닥, 분수·나무·부두 턱. 정점 셋이 삼각형 하나(색인 없음),
///   뒤에 경계 상자 나무(32바이트 × 마디 수 = 삼각형 수 × 2 − 1)와 삼각형마다 2바이트(갈래, 바닥 재질로 짐작).
/// +0x34 바닥 그림자로 보이는 구역(안 풀었다).
/// +0x38 (자리, 정점 수, 마디 수) — <b>벽</b>: 집과 도시 둘레를 따라 선 세로 띠. 같은 짜임이고 2바이트 꼬리는 없다.
/// +0x44 (자리, 삼각형 수, 마디 수) 큰 삼각형 몇 개(카메라용으로 짐작, 안 쓴다).
/// +0x50 (자리, 선분 수, 마디 수) 선분(36바이트: u32 1, f32 × 4 두 점) + 2D 경계 상자 나무(20바이트) — 좌판 앞의 막는 선으로 짐작.
/// +0x5C 격자 자리: u32 가로 칸, u32 세로 칸, f32 칸 크기 × 2(400), f32 높이 × (가로 + 1) × (세로 + 1) — z 줄 차례, 원점은 장면의 (0, 0).
///   그 뒤에 u8 × 가로 × 세로 판(땅 갈래로 짐작).
/// +0x68 (자리, 점 수) 점(16바이트: u32 갈래, f32 × 3) — 부두를 따라 고르게 늘어서 있어 등불 자리로 짐작.
/// 걷는 높이는 격자에 잔 충돌의 누운 삼각형(계단)을 얹은 것이고, 벽과 잔 충돌의 선 삼각형이 길을 막는다.
/// </remarks>
public sealed class TownGrid
{
    /// <summary>막힘 판의 한 칸(장면 단위). 사람 키가 170쯤이다.</summary>
    public const float Fine = 100f;
    /// <summary>발밑에서 이만큼 위부터 <see cref="Head"/> 까지에 걸리는 면이 길을 막는다.</summary>
    private const float Knee = 70f, Head = 260f;
    /// <summary>이보다 낮은 땅은 물이다(바다 면이 y = 0).</summary>
    private const float Shore = 20f;

    public int Width { get; }
    public int Height { get; }
    public float Cell { get; }
    public List<(Vector3 A, Vector3 B)> Lines { get; } = [];
    public List<Vector3> Lights { get; } = [];

    private readonly float[] _floor;
    private readonly bool[] _blocked;
    private readonly float[] _top;
    private readonly int _fineWidth, _fineHeight;

    private TownGrid(byte[] gri, int gridAt)
    {
        int I32(int at) => BinaryPrimitives.ReadInt32LittleEndian(gri.AsSpan(at));
        float F32(int at) => BinaryPrimitives.ReadSingleLittleEndian(gri.AsSpan(at));
        Vector3 Point(int at) => new(F32(at), F32(at + 4), F32(at + 8));

        Width = I32(gridAt);
        Height = I32(gridAt + 4);
        Cell = F32(gridAt + 8);
        float Coarse(float x, float z)
        {
            float gx = Math.Clamp(x / Cell, 0, Width - 0.001f), gz = Math.Clamp(z / Cell, 0, Height - 0.001f);
            int ix = (int)gx, iz = (int)gz, at = gridAt + 16 + (iz * (Width + 1) + ix) * 4;
            float near = F32(at) + (F32(at + 4) - F32(at)) * (gx - ix);
            float far = F32(at + (Width + 1) * 4) + (F32(at + (Width + 2) * 4) - F32(at + (Width + 1) * 4)) * (gx - ix);
            return near + (far - near) * (gz - iz);
        }

        _fineWidth = (int)(Width * Cell / Fine);
        _fineHeight = (int)(Height * Cell / Fine);
        _floor = new float[_fineWidth * _fineHeight];
        for (int z = 0; z < _fineHeight; z++)
        for (int x = 0; x < _fineWidth; x++)
            _floor[z * _fineWidth + x] = Coarse((x + 0.5f) * Fine, (z + 0.5f) * Fine);
        _blocked = new bool[_fineWidth * _fineHeight];
        _top = new float[_fineWidth * _fineHeight];
        Array.Fill(_top, float.MinValue);

        // 잔 충돌의 누운 삼각형은 바닥(계단·비탈)이다 — 격자 위에 얹는다
        var walls = new List<(Vector3, Vector3, Vector3)>();
        foreach (int head in new[] { 0x28, 0x38 })
        {
            int at = I32(head), count = I32(head + 4) / 3;
            if (at <= 0 || count <= 0 || at + count * 36L > gri.Length) continue;
            for (int i = 0; i < count; i++)
            {
                Vector3 a = Point(at + i * 36), b = Point(at + i * 36 + 12), c = Point(at + i * 36 + 24);
                var normal = Vector3.Cross(b - a, c - a);
                bool flat = normal.LengthSquared() > 0 && MathF.Abs(Vector3.Normalize(normal).Y) > 0.5f;
                if (head == 0x28 && flat) Sample(a, b, c, (cell, p) => _floor[cell] = MathF.Max(_floor[cell], p.Y));
                else walls.Add((a, b, c));
            }
        }
        // 벽은 기둥 둘 사이에 삼각형이 하나뿐이다(윗변 쪽 반만 있다) — 위에서 본 변을 따라, 높이가 사람에 걸리면 막는다
        foreach (var (a, b, c) in walls)
        {
            float low = MathF.Min(a.Y, MathF.Min(b.Y, c.Y)), high = MathF.Max(a.Y, MathF.Max(b.Y, c.Y));
            foreach (var (from, to) in new[] { (a, b), (b, c), (c, a) })
            {
                int steps = Math.Max(1, (int)(Vector2.Distance(new Vector2(from.X, from.Z), new Vector2(to.X, to.Z)) / 40f));
                for (int i = 0; i <= steps; i++)
                {
                    var p = Vector3.Lerp(from, to, i / (float)steps);
                    // 얇은 벽이라 비스듬하면 칸 사이로 샌다 — 둘레도 칠한다
                    foreach (var (dx, dz) in new[] { (0f, 0f), (35f, 0f), (-35f, 0f), (0f, 35f), (0f, -35f) })
                    {
                        if (!Inside(p.X + dx, p.Z + dz)) continue;
                        int cell = (int)((p.Z + dz) / Fine) * _fineWidth + (int)((p.X + dx) / Fine);
                        if (high > _floor[cell] + Knee && low < _floor[cell] + Head) _blocked[cell] = true;
                    }
                }
            }
        }

        int lines = I32(0x50), lineCount = I32(0x54);
        for (int i = 0; i < lineCount; i++)
        {
            var line = (Point(lines + i * 36 + 4), Point(lines + i * 36 + 20));
            Lines.Add(line);
            for (float t = 0; t <= 1; t += 0.05f)
            {
                var p = Vector3.Lerp(line.Item1, line.Item2, t);
                if (Inside(p.X, p.Z)) _blocked[(int)(p.Z / Fine) * _fineWidth + (int)(p.X / Fine)] = true;
            }
        }
        int points = I32(0x68), pointCount = I32(0x6C);
        for (int i = 0; i < pointCount && points + i * 16 + 16 <= gri.Length; i++) Lights.Add(Point(points + i * 16 + 4));
    }

    /// <summary>시내 장면의 걷는 면. 파일이 없거나 머리가 낯설면 null.</summary>
    public static TownGrid? Read(int sceneNumber)
    {
        string path = GvoFiles.PathOf($@"0002\{0x10000 + sceneNumber:D8}.bin");
        if (!File.Exists(path)) return null;
        var gri = File.ReadAllBytes(path);
        if (gri.Length < 0x80) return null;
        int lines = BinaryPrimitives.ReadInt32LittleEndian(gri.AsSpan(0x50));
        int lineCount = BinaryPrimitives.ReadInt32LittleEndian(gri.AsSpan(0x54));
        int nodes = BinaryPrimitives.ReadInt32LittleEndian(gri.AsSpan(0x58));
        int gridAt = BinaryPrimitives.ReadInt32LittleEndian(gri.AsSpan(0x5C));
        if (gridAt <= 0 || gridAt + 16 > gri.Length || lineCount < 0 || nodes < 0 || lines + 36L * lineCount + 20L * nodes != gridAt) return null;
        int width = BinaryPrimitives.ReadInt32LittleEndian(gri.AsSpan(gridAt));
        int height = BinaryPrimitives.ReadInt32LittleEndian(gri.AsSpan(gridAt + 4));
        if (width is <= 0 or > 1000 || height is <= 0 or > 1000 || gridAt + 16 + 4L * (width + 1) * (height + 1) > gri.Length) return null;
        return new TownGrid(gri, gridAt);
    }

    /// <summary>걷는 면의 높이 — 잔 칸의 가운데 값 사이를 고르게 잇는다.</summary>
    public float HeightAt(float x, float z)
    {
        float gx = Math.Clamp(x / Fine - 0.5f, 0, _fineWidth - 1.001f), gz = Math.Clamp(z / Fine - 0.5f, 0, _fineHeight - 1.001f);
        int ix = (int)gx, iz = (int)gz, at = iz * _fineWidth + ix;
        float near = _floor[at] + (_floor[at + 1] - _floor[at]) * (gx - ix);
        float far = _floor[at + _fineWidth] + (_floor[at + _fineWidth + 1] - _floor[at + _fineWidth]) * (gx - ix);
        return near + (far - near) * (gz - iz);
    }

    public bool Inside(float x, float z) => x >= 0 && z >= 0 && x < _fineWidth * Fine && z < _fineHeight * Fine;

    /// <summary>그 자리에 설 수 있는가 — 격자 안이고, 뭍이고, 벽에 안 걸린다.</summary>
    public bool Walkable(float x, float z) =>
        Inside(x, z) && !_blocked[(int)(z / Fine) * _fineWidth + (int)(x / Fine)] && HeightAt(x, z) > Shore;

    /// <summary>방의 벽 — 선 면이 무릎에서 머리 사이를 지나는 칸을 막는다(걷는 면에 벽이 안 든 방이 있다).</summary>
    public void Wall(Vector3 a, Vector3 b, Vector3 c)
    {
        var normal = Vector3.Cross(b - a, c - a);
        if (normal.LengthSquared() < 1e-3f || MathF.Abs(normal.Y) > 0.5f * normal.Length()) return;
        Sample(a, b, c, (cell, p) =>
        {
            if (p.Y > _floor[cell] + 40 && p.Y < _floor[cell] + 170) _blocked[cell] = true;
        });
    }

    /// <summary>방 메시의 테두리 밖은 못 가는 데로 친다(걷는 면은 방보다 넓게 깔려 있다).</summary>
    public void Bound(Vector3 min, Vector3 max)
    {
        for (int z = 0; z < _fineHeight; z++)
        for (int x = 0; x < _fineWidth; x++)
        {
            float px = (x + 0.5f) * Fine, pz = (z + 0.5f) * Fine;
            if (px < min.X || px > max.X || pz < min.Z || pz > max.Z) _blocked[z * _fineWidth + x] = true;
        }
    }

    /// <summary>삼각형 위에 고르게 점을 찍어 가며 그 점이 든 칸과 함께 넘긴다.</summary>
    private void Sample(Vector3 a, Vector3 b, Vector3 c, Action<int, Vector3> visit)
    {
        float reach = MathF.Max(Vector3.Distance(a, b), MathF.Max(Vector3.Distance(b, c), Vector3.Distance(c, a)));
        int steps = Math.Clamp((int)MathF.Ceiling(reach / (Fine * 0.6f)), 1, 160);
        for (int i = 0; i <= steps; i++)
        for (int j = 0; j <= steps - i; j++)
        {
            var p = a + (b - a) * (i / (float)steps) + (c - a) * (j / (float)steps);
            if (Inside(p.X, p.Z)) visit((int)(p.Z / Fine) * _fineWidth + (int)(p.X / Fine), p);
        }
    }

    /// <summary>
    /// 장면 메시(그리는 것)의 삼각형 하나 — 칸마다 가장 높은 곳(지붕)을 적어 둔다.
    /// 카메라가 집 안으로 들어가지 않게 하는 데 쓴다. 길을 막는 것은 장면 정보의 충돌이지 이것이 아니다.
    /// </summary>
    public void Cover(Vector3 a, Vector3 b, Vector3 c) =>
        Sample(a, b, c, (cell, p) =>
        {
            if (p.Y > _floor[cell] + Knee) _top[cell] = MathF.Max(_top[cell], p.Y);
        });

    /// <summary>
    /// <paramref name="from"/> 에서 <paramref name="to"/> 까지 가리는 것 없이 갈 수 있는 몫(0 ~ 1).
    /// 땅 밑으로 들어가거나 지붕보다 낮게 집 칸을 지나면 거기서 끊는다.
    /// </summary>
    /// <param name="street">
    /// 참이면 지붕 높이는 안 보고 <b>걸어서 닿는 칸 위</b>로만 간다 — 성문 아치 밑처럼 위가 다 막힌 곳에서 길 위에 머무는 데 쓴다.
    /// </param>
    public float Sight(Vector3 from, Vector3 to, bool street = false)
    {
        float length = Vector3.Distance(from, to);
        int steps = Math.Max(1, (int)(length / (Fine * 0.5f)));
        for (int i = 1; i <= steps; i++)
        {
            var p = Vector3.Lerp(from, to, i / (float)steps);
            if (!Inside(p.X, p.Z)) continue;
            int cell = (int)(p.Z / Fine) * _fineWidth + (int)(p.X / Fine);
            bool hidden = p.Y < HeightAt(p.X, p.Z) + 40;
            if (street) hidden |= _reached != null && !_reached[cell];
            // 선 자리 바로 위의 낮은 차양·간판은 치지 않는다(높은 아치는 친다)
            else if ((p.X - from.X) * (p.X - from.X) + (p.Z - from.Z) * (p.Z - from.Z) >= 160 * 160 || _top[cell] > from.Y + 300)
                hidden |= p.Y < _top[cell] + 30;
            if (hidden) return (i - 1) / (float)steps;
        }
        return 1;
    }

    /// <summary>가장 가까운 설 수 있는 자리(없으면 그대로).</summary>
    public Vector2 Nearest(Vector2 from)
    {
        for (float radius = 0; radius < Width * Cell; radius += Fine)
        for (int k = 0; k < 24; k++)
        {
            float angle = k * MathF.Tau / 24;
            var p = from + radius * new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            // 좁은 틈에 끼지 않게 둘레도 비어 있어야 한다
            if (Walkable(p.X, p.Y) && Walkable(p.X + 150, p.Y) && Walkable(p.X - 150, p.Y) && Walkable(p.X, p.Y + 150) && Walkable(p.X, p.Y - 150))
                return p;
            if (radius == 0) break;
        }
        return from;
    }

    /// <summary>
    /// <paramref name="from"/> 에서 <paramref name="to"/> 까지 걸어가는 길(꺾이는 점들). 못 가면 빈 목록.
    /// 닿는 칸을 따라 너비 먼저 뒤지고, 곧게 갈 수 있는 구간은 줄인다.
    /// </summary>
    public List<Vector2> Path(Vector2 from, Vector2 to)
    {
        to = Nearest(to);
        if (!Inside(from.X, from.Y) || !Inside(to.X, to.Y)) return [];
        int Cell(Vector2 p) => (int)(p.Y / Fine) * _fineWidth + (int)(p.X / Fine);
        Vector2 Middle(int cell) => new((cell % _fineWidth + 0.5f) * Fine, (cell / _fineWidth + 0.5f) * Fine);
        bool Open(int cell) => Walkable((cell % _fineWidth + 0.5f) * Fine, (cell / _fineWidth + 0.5f) * Fine);

        int start = Cell(from), goal = Cell(to);
        var came = new Dictionary<int, int> { [goal] = goal };
        var queue = new Queue<int>();
        queue.Enqueue(goal);
        while (queue.Count > 0 && !came.ContainsKey(start))
        {
            int cell = queue.Dequeue(), x = cell % _fineWidth, z = cell / _fineWidth;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, nz = z + dz, next = nz * _fineWidth + nx;
                if ((dx == 0 && dz == 0) || nx < 0 || nz < 0 || nx >= _fineWidth || nz >= _fineHeight || came.ContainsKey(next)) continue;
                // 모서리를 비껴 지나지 않게, 비스듬히 갈 때는 두 옆 칸도 비어 있어야 한다
                if (next != start && (!Open(next) || (dx != 0 && dz != 0 && (!Open(z * _fineWidth + nx) || !Open(nz * _fineWidth + x))))) continue;
                came[next] = cell;
                queue.Enqueue(next);
            }
        }
        if (!came.ContainsKey(start)) return [];

        var cells = new List<Vector2>();
        for (int cell = start; cell != goal; cell = came[cell]) cells.Add(Middle(cell));
        cells.Add(to);

        // 사이가 트인 점은 건너뛴다
        bool Clear(Vector2 a, Vector2 b)
        {
            int steps = Math.Max(1, (int)(Vector2.Distance(a, b) / (Fine * 0.4f)));
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)steps);
                if (!Walkable(p.X, p.Y) || !Walkable(p.X + 60, p.Y) || !Walkable(p.X - 60, p.Y) || !Walkable(p.X, p.Y + 60) || !Walkable(p.X, p.Y - 60)) return false;
            }
            return true;
        }
        var path = new List<Vector2>();
        var anchor = from;
        for (int i = 0; i < cells.Count; )
        {
            int far = i;
            for (int k = Math.Min(cells.Count - 1, i + 60); k > i; k--)
                if (Clear(anchor, cells[k])) { far = k; break; }
            path.Add(cells[far]);
            anchor = cells[far];
            i = far + 1;
        }
        return path;
    }

    /// <summary>들어설 자리 — 막는 선(좌판)들이 몰린 곳, 곧 사람이 모이는 거리.</summary>
    public Vector2 Entry()
    {
        if (Lines.Count == 0) return Nearest(new Vector2(Width * Cell / 2, Height * Cell / 2));
        var xs = Lines.Select(l => (l.A.X + l.B.X) / 2).Order().ToList();
        var zs = Lines.Select(l => (l.A.Z + l.B.Z) / 2).Order().ToList();
        return Nearest(new Vector2(xs[xs.Count / 2], zs[zs.Count / 2]));
    }

    private bool[]? _reached;

    /// <summary>
    /// <paramref name="entry"/> 에서 걸어서 닿는 칸을 가려낸다. 닿지 못하는 뭍 칸은 집 안이므로
    /// 둘레 벽의 높이를 안으로 번지게 해서 카메라가 속이 빈 집 안으로 들어가지 않게 한다.
    /// </summary>
    public void Seal(Vector2 entry)
    {
        _reached = new bool[_blocked.Length];
        var queue = new Queue<int>();
        if (Inside(entry.X, entry.Y)) queue.Enqueue((int)(entry.Y / Fine) * _fineWidth + (int)(entry.X / Fine));
        while (queue.Count > 0)
        {
            int cell = queue.Dequeue();
            int x = cell % _fineWidth, z = cell / _fineWidth;
            if (_reached[cell] || !Walkable((x + 0.5f) * Fine, (z + 0.5f) * Fine)) continue;
            _reached[cell] = true;
            if (x > 0) queue.Enqueue(cell - 1);
            if (x < _fineWidth - 1) queue.Enqueue(cell + 1);
            if (z > 0) queue.Enqueue(cell - _fineWidth);
            if (z < _fineHeight - 1) queue.Enqueue(cell + _fineWidth);
        }

        for (int pass = 0; pass < 14; pass++)
        {
            var next = (float[])_top.Clone();
            for (int z = 1; z < _fineHeight - 1; z++)
            for (int x = 1; x < _fineWidth - 1; x++)
            {
                int cell = z * _fineWidth + x;
                if (_reached[cell] || _top[cell] > float.MinValue || HeightAt((x + 0.5f) * Fine, (z + 0.5f) * Fine) <= Shore) continue;
                next[cell] = MathF.Max(MathF.Max(_top[cell - 1], _top[cell + 1]), MathF.Max(_top[cell - _fineWidth], _top[cell + _fineWidth]));
            }
            Array.Copy(next, _top, next.Length);
        }
    }

    /// <summary>작은 지도·확인용 그림: 줄마다 거르개 0 과 RGB. 물은 파랑, 벽은 검정, 걸어서 닿는 곳은 밝게, 못 닿는 뭍(집 안·성 밖)은 어둡게.</summary>
    public (int Width, int Height, byte[] Rows) Picture()
    {
        var rows = new byte[_fineHeight * (1 + _fineWidth * 3)];
        float top = MathF.Max(1, _floor.Max());
        for (int z = 0; z < _fineHeight; z++)
        for (int x = 0; x < _fineWidth; x++)
        {
            int at = z * (1 + _fineWidth * 3) + 1 + x * 3;
            float px = (x + 0.5f) * Fine, pz = (z + 0.5f) * Fine, h = HeightAt(px, pz);
            (byte r, byte g, byte b) = h <= Shore ? ((byte)40, (byte)80, (byte)150)
                : _blocked[z * _fineWidth + x] ? ((byte)20, (byte)20, (byte)20)
                : _reached != null && !_reached[z * _fineWidth + x] ? ((byte)96, (byte)104, (byte)84)
                : ((byte)(196 + 50 * h / top), (byte)(186 + 50 * h / top), (byte)(136 + 50 * h / top));
            (rows[at], rows[at + 1], rows[at + 2]) = (r, g, b);
        }
        return (_fineWidth, _fineHeight, rows);
    }
}
