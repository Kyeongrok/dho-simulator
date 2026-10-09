using System.Buffers.Binary;
using System.Numerics;
using Dho.Data;
using Vortice.Direct3D11;

namespace Dho.Render;

/// <summary>
/// 항구 장면 — <c>0002</c> 의 <c>GRM</c>(지형·부두·작은 배)과, <c>GRI</c> 가 늘어놓는 공용 물체 <c>GRD</c>(집).
/// 파일 이름(10진) = 0x10000(GRI) 또는 0x20000(GRM) + 장면 번호.
/// </summary>
/// <remarks>
/// GRM 머리: +0x0C u32 덩이 표 자리, u32 덩이 수, +0x1C u32 GTEX 자리,
/// +0x24 u32 정점버퍼 표 자리, u32 수(표 = 자리, 크기, FVF, 한 정점 바이트), +0x2C u32 색인버퍼 표 자리, u32 수(표 = 자리, 크기).
/// 덩이 표: (u32 자리, u32 크기). 덩이 +0xE0 부터 그리기 레코드 24바이트:
/// u16 플래그, u16 텍스처 번호, u8 갈래, u8, u8 정점버퍼, u8 색인버퍼, u8, u8, u16, u32,
/// u16 정점 시작, u16 정점 수, u16 색인 시작, u16 삼각형 수. 삼각형 목록이고 색인은 정점버퍼 처음부터 센다.
/// 정점은 자리, (가중치), (법선), 색 BGRA(빛이 구워져 있다), …, 끝 8바이트가 UV 다.
/// GRD(공용 물체): <c>nt000000.bin</c> = u32 개수, (u32 id, u32 0, u32 자리) — id &lt; 0x10000 은 <c>no000000.bin</c> 안의 자리.
/// 머리 u32×10: "GRD ", 1, 덩이 표 자리, 덩이 수, -1, 0, 정점버퍼 표 자리, 수, 색인버퍼 표 자리, 수(자리는 파일 처음부터).
/// 텍스처는 <c>no200000.bin</c> +0x10 의 GTEX.
/// GRI 배치: 머리에 (배치 자리 a, 1, 번호표 자리 = a + 16, 개수 n)이 나란히 있고, 번호표 뒤에 4x4 행렬(행 벡터식) × n.
/// y = 0 이 바다 면이고 물은 파일에 없다.
/// </remarks>
internal sealed class PortScene : IDisposable
{
    private readonly Gfx _gfx;
    private readonly List<(Mesh Mesh, ID3D11ShaderResourceView? Texture, bool Soft)> _parts = [];
    private readonly List<ID3D11ShaderResourceView> _textures = [];
    private readonly Dictionary<(int Set, int Texture, bool Soft), MeshBuilder> _builders = new();

    private const int OwnTextures = 0, SharedTextures = 1;
    private const float GroundShade = 0.68f;

    private Vector3 _min = new(float.MaxValue), _max = new(float.MinValue);
    /// <summary>장면 메시(GRM)의 가운데와 반지름 — 시내를 내려다볼 때 쓴다.</summary>
    public Vector3 Center => (_min + _max) / 2;
    public (Vector3 Min, Vector3 Max) Bounds => (_min, _max);
    public float Radius => Vector3.Distance(_min, _max) / 2;

    private readonly TownGrid? _grid;

    // 방의 벽 · 천장(갈래 2 덩이)의 삼각형 — 카메라가 벽 밖으로 나가지 않게 막는 데 쓴다
    private readonly List<Vector3> _walls = [];
    public bool HasWalls => _walls.Count > 0;
    /// <summary>방의 문 자리(장면 좌표 x, z) — 메시에서 문짝 꼴의 판을 찾은 것. 못 찾으면 null.</summary>
    public Vector2? Door
    {
        get
        {
            // 문은 방의 바깥 벽에 붙어 있다 — 방 테두리에서 먼 판(계산대 뒤의 쪽문 · 벽장)은 뺀다. 가장 큰 것, 같으면 z 가 큰 쪽
            (Vector2 At, float Area)? best = null;
            foreach (var leaf in _leaves)
            {
                float edge = MathF.Min(MathF.Min(leaf.At.X - _min.X, _max.X - leaf.At.X), MathF.Min(leaf.At.Y - _min.Z, _max.Z - leaf.At.Y));
                if (edge > 130) continue;
                if (best is not { } b || leaf.Area > b.Area + 1 || (MathF.Abs(leaf.Area - b.Area) <= 1 && leaf.At.Y > b.At.Y)) best = leaf;
            }
            return best?.At;
        }
    }
    private readonly List<(Vector2 At, float Area)> _leaves = [];

    /// <summary>from 에서 to 로 가는 길이 벽에 처음 막히는 데(0 ~ 1, 안 막히면 1).</summary>
    public float Reach(Vector3 from, Vector3 to)
    {
        var way = to - from;
        float best = 1;
        for (int i = 0; i + 2 < _walls.Count; i += 3)
        {
            Vector3 a = _walls[i], ab = _walls[i + 1] - a, ac = _walls[i + 2] - a;
            var p = Vector3.Cross(way, ac);
            float det = Vector3.Dot(ab, p);
            if (MathF.Abs(det) < 1e-4f) continue;
            var s = from - a;
            float u = Vector3.Dot(s, p) / det;
            if (u < 0 || u > 1) continue;
            var q = Vector3.Cross(s, ab);
            float v = Vector3.Dot(way, q) / det;
            if (v < 0 || u + v > 1) continue;
            float t = Vector3.Dot(ac, q) / det;
            if (t > 0 && t < best) best = t;
        }
        return best;
    }

    /// <param name="grid">시내의 걷는 면. 주면 장면 메시를 읽으면서 지붕 높이를 적는다.</param>
    private readonly bool _solid;
    // 바다에서 멀리 보는 도시 — 네모난 땅바닥의 가장자리를 투명하게 풀어 바다 · 뭍에 스미게 한다
    private readonly bool _fadeGround;
    // 오려 낸 그림(나뭇잎 · 울타리)을 입힌 면인가 — 텍스처마다 한 번만 본다. 그런 면은 지붕으로 치지 않는다(카메라 가림 — 사용자, 2026-10-09)
    private readonly Dictionary<(int Set, int Texture), bool> _leafy = new();
    private byte[]? _grmData, _sharedData;
    private bool Leafy(int set, int texture)
    {
        if (texture == 0xFFFF) return false;
        if (_leafy.TryGetValue((set, texture), out bool known)) return known;
        byte[]? source = set == OwnTextures ? _grmData : (_sharedData ??= GvoFiles.Read(@"0002\no200000.bin"));
        int gtex = set == OwnTextures && source != null ? BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(0x1C)) : 0x10;
        bool leafy = source != null && texture < GameTexture.GtexCount(source, gtex) && GameTexture.GtexHoles(source, gtex, texture) > 0.15f;      // 뚫린 데가 15% 넘으면(지은 문턱)
        return _leafy[(set, texture)] = leafy;
    }
    /// <summary>바다에서 보는 도시(바닥을 빼고 키워 그린다)에서 꼭대기가 이 높이를 넘는 면 묶음은 안 그린다 — 도시를 둘러친 높은 벽 같은 건물이 바다에서 큰 담처럼 보여서(라스팔마스 — 사용자, 2026-10-09). 장면마다 준다.</summary>
    private readonly float _seaTop;
    /// <summary>확인용(대본 sealimit): 0 이 아니면 모든 장면에 이 높이를 쓴다.</summary>
    public static float SeaBaseLimit;
    /// <summary>확인용: 바다에서 보는 장면을 읽을 때 면 묶음마다의 꼭대기 높이.</summary>
    public static readonly List<float> SeaBases = [];
    private readonly HashSet<MeshBuilder> _ground = [];
    private readonly HashSet<Mesh> _groundMeshes = [];      // 바닥 조각 — 먼바다에서 도시를 키워 그릴 때는 뺀다

    /// <param name="solid">정점색의 알파를 무시한다 — 방 장면은 벽의 알파가 0 이라 그대로면 벽이 안 그려진다.</param>
    public PortScene(Gfx gfx, int sceneNumber, TownGrid? grid = null, bool solid = false, bool fadeGround = false, float seaTop = float.MaxValue)
    {
        _gfx = gfx;
        _fadeGround = fadeGround;
        _seaTop = seaTop;
        _solid = solid;
        _grid = grid;
        var grm = GvoFiles.Read($@"0002\{0x20000 + sceneNumber:D8}.bin"); _grmData = grm;
        int I32(byte[] data, int at) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at));

        AddDraws(grm, I32(grm, 0x0C), I32(grm, 0x10), I32(grm, 0x24), I32(grm, 0x28), I32(grm, 0x2C), I32(grm, 0x30),
                 Matrix4x4.Identity, OwnTextures);

        // 집들
        var gri = GvoFiles.Read($@"0002\{0x10000 + sceneNumber:D8}.bin");
        var index = GvoFiles.Read(@"0002\nt000000.bin");
        byte[]? objects = null;
        foreach (var (id, transform) in Placements(gri))
        {
            int at = -1;
            for (int i = 0; i < I32(index, 0); i++)
                if (I32(index, 4 + i * 12) == id) { at = I32(index, 12 + i * 12); break; }
            if (at < 0 || id >= 0x10000) continue;
            objects ??= GvoFiles.Read(@"0002\no000000.bin");
            if (!objects.AsSpan(at, 4).SequenceEqual("GRD "u8)) continue;
            AddDraws(objects, I32(objects, at + 8), I32(objects, at + 12), I32(objects, at + 24), I32(objects, at + 28),
                     I32(objects, at + 32), I32(objects, at + 36), transform, SharedTextures);
        }

        // 시내 · 방 장면의 작은 물체들 — 번호에 0x10000 을 더한 것이 둘째 묶음(no100000)의 물체다
        byte[]? small = null;
        foreach (var (id, transform) in SmallPlacements(gri))
        {
            int at = -1;
            for (int i = 0; i < I32(index, 0); i++)
                if (I32(index, 4 + i * 12) == id + 0x10000) { at = I32(index, 12 + i * 12); break; }
            if (at < 0) continue;
            small ??= GvoFiles.Read(@"0002\no100000.bin");
            if (at + 40 > small.Length || !small.AsSpan(at, 4).SequenceEqual("GRD "u8)) continue;
            AddDraws(small, I32(small, at + 8), I32(small, at + 12), I32(small, at + 24), I32(small, at + 28),
                     I32(small, at + 32), I32(small, at + 36), transform, SharedTextures);
        }

        int gtex = I32(grm, 0x1C);
        byte[]? shared = null;
        foreach (var ((set, texture, soft), builder) in _builders)
        {
            if (builder.Indices.Count == 0) continue;      // 레코드를 다 건너뛴 묶음
            if (_ground.Contains(builder))
            {
                float margin = MathF.Min(_max.X - _min.X, _max.Z - _min.Z) * 0.22f;
                foreach (ref var vertex in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(builder.Vertices))
                {
                    float edge = MathF.Min(MathF.Min(vertex.Position.X - _min.X, _max.X - vertex.Position.X),
                                           MathF.Min(vertex.Position.Z - _min.Z, _max.Z - vertex.Position.Z));
                    vertex.Color.W *= Math.Clamp(edge / margin, 0, 1);
                }
            }
            ID3D11ShaderResourceView? view = null;
            if (set == OwnTextures && texture < GameTexture.GtexCount(grm, gtex))
                view = GameTexture.FromGtex(gfx, grm, gtex, texture);
            else if (set == SharedTextures)
            {
                shared ??= GvoFiles.Read(@"0002\no200000.bin");
                if (texture < GameTexture.GtexCount(shared, 0x10)) view = GameTexture.FromGtex(gfx, shared, 0x10, texture);
            }
            if (view != null) _textures.Add(view);
            _parts.Add((builder.Build(gfx), view, soft));
            if (_ground.Contains(builder)) _groundMeshes.Add(_parts[^1].Item1);
        }
        _builders.Clear();
    }

    private static IEnumerable<(int Id, Matrix4x4 Transform)> Placements(byte[] gri)
    {
        for (int at = 0x2C; at < 0x100; at += 4)
        {
            int a = BinaryPrimitives.ReadInt32LittleEndian(gri.AsSpan(at));
            int one = BinaryPrimitives.ReadInt32LittleEndian(gri.AsSpan(at + 4));
            int ids = BinaryPrimitives.ReadInt32LittleEndian(gri.AsSpan(at + 8));
            int count = BinaryPrimitives.ReadInt32LittleEndian(gri.AsSpan(at + 12));
            if (one != 1 || ids != a + 16 || count <= 0 || count > 10000 || (long)ids + count * 68L > gri.Length) continue;

            for (int i = 0; i < count; i++)
            {
                var m = new float[16];
                for (int k = 0; k < 16; k++)
                    m[k] = BinaryPrimitives.ReadSingleLittleEndian(gri.AsSpan(ids + 4 * count + 64 * i + 4 * k));
                yield return (BinaryPrimitives.ReadInt32LittleEndian(gri.AsSpan(ids + 4 * i)),
                    new Matrix4x4(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]));
            }
            yield break;
        }
    }

    /// <summary>시내 · 방 장면의 배치 — 머리 +0x70 에 (자리, 수), 번호 × 수, 그 뒤 4x4 행렬 × 수.</summary>
    private static IEnumerable<(int Id, Matrix4x4 Transform)> SmallPlacements(byte[] gri)
    {
        if (gri.Length < 0x78) yield break;
        int placed = BinaryPrimitives.ReadInt32LittleEndian(gri.AsSpan(0x70)), number = BinaryPrimitives.ReadInt32LittleEndian(gri.AsSpan(0x74));
        if (placed <= 0 || number <= 0 || number > 5000 || (long)placed + number * 68L > gri.Length) yield break;
        for (int i = 0; i < number; i++)
        {
            var m = new float[16];
            for (int k = 0; k < 16; k++)
                m[k] = BinaryPrimitives.ReadSingleLittleEndian(gri.AsSpan(placed + 4 * number + 64 * i + 4 * k));
            if (MathF.Abs(m[15] - 1) > 0.01f) yield break;      // 행렬이 아닌 것을 읽었다
            yield return (BinaryPrimitives.ReadInt32LittleEndian(gri.AsSpan(placed + 4 * i)),
                new Matrix4x4(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]));
        }
    }

    private void AddDraws(byte[] data, int chunksAt, int chunkCount, int vertexTable, int vertexBuffers,
                          int indexTable, int indexBuffers, Matrix4x4 transform, int textureSet)
    {
        int I32(int at) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at));
        int U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at));
        float F32(int at) => BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(at));

        var remap = new Dictionary<(MeshBuilder, int Buffer, int Vertex), uint>();
        for (int c = 0; c < chunkCount; c++)
        {
            int chunk = I32(chunksAt + c * 8), size = I32(chunksAt + c * 8 + 4);
            // 덩이 머리 +8 이 그리기 레코드의 수다. 방 장면은 레코드 뒤에 다른 자료가 이어져서 크기로 세면 안 된다
            int records = U16(chunk + 8);
            // 갈래 2 인 덩이(방의 벽 따위)는 머리가 16바이트 길다
            int first = chunk + (data[chunk + 2] == 2 ? 0xF0 : 0xE0);
            for (int record = first, n = 0; n < records && record + 24 <= chunk + size; record += 24, n++)
            {
                int texture = U16(record + 2), kind = data[record + 4];
                int vertexBuffer = data[record + 6], indexBuffer = data[record + 7];
                int firstIndex = U16(record + 20), triangles = U16(record + 22);
                // 텍스처 묶음 값 255 는 땅바닥 — 집·부두만큼 밝히면 하얗게 날아간다
                float shade = data[record + 9] == 0xFF ? GroundShade : 1f;
                if (kind is 0 or 0xFF || triangles == 0 || vertexBuffer >= vertexBuffers || indexBuffer >= indexBuffers) continue;
                // 텍스처가 없는(0xFFFF) 땅 겹은 시내 장면에만 있고, 그대로 그리면 광장이 흰 민무늬로 덮인다
                if (texture == 0xFFFF && data[record + 9] == 0xFF) continue;

                int vertices = I32(vertexTable + vertexBuffer * 16), fvf = I32(vertexTable + vertexBuffer * 16 + 8);
                int stride = I32(vertexTable + vertexBuffer * 16 + 12);
                int vertexCount = I32(vertexTable + vertexBuffer * 16 + 4) / stride;
                int indices = I32(indexTable + indexBuffer * 8);
                int colorAt = 12 + ((fvf & 0xE) == 0x6 ? 4 : 0) + ((fvf & 0x10) != 0 ? 12 : 0);

                // 레코드 첫 값의 둘째 비트가 선 면은 알파로 섞어 그리는 면이다(바닥의 그림자 · 불빛)
                bool ground = _fadeGround && data[record + 9] == 0xFF;
                bool soft = (U16(record) & 2) != 0 || ground;
                if (!_builders.TryGetValue((textureSet, texture, soft), out var builder))
                    _builders[(textureSet, texture, soft)] = builder = new MeshBuilder();
                if (ground) _ground.Add(builder);

                // 방 장면에는 색인이 버퍼를 넘어가는 레코드가 있다(짜임이 다른 것으로 보인다) — 건너뛴다
                if (indices < 0 || (long)indices + (firstIndex + triangles * 3) * 2L > data.Length
                    || (firstIndex + triangles * 3) * 2 > I32(indexTable + indexBuffer * 8 + 4)
                    || vertices < 0 || (long)vertices + (long)vertexCount * stride > data.Length || stride < 20) continue;

                if (_fadeGround && !ground)
                {
                    float bottom = float.MaxValue, top = float.MinValue;
                    for (int i = 0; i < triangles * 3; i++)
                    {
                        int v = U16(indices + (firstIndex + i) * 2);
                        int p = vertices + (v < vertexCount ? v : 0) * stride;
                        float tall = Vector3.Transform(new Vector3(F32(p), F32(p + 4), F32(p + 8)), transform).Y;
                        (bottom, top) = (MathF.Min(bottom, tall), MathF.Max(top, tall));
                    }
                    SeaBases.Add(top);
                    if (top > (SeaBaseLimit > 0 ? SeaBaseLimit : _seaTop)) continue;
                }

                // 땅바닥이 아닌 면의 높이(지붕)를 적어 둔다 — 카메라 가림에 쓴다
                if (_grid != null && data[record + 9] != 0xFF && !Leafy(textureSet, texture))
                    for (int i = 0; i < triangles; i++)
                    {
                        Vector3 Corner(int k)
                        {
                            int v = U16(indices + (firstIndex + i * 3 + k) * 2);
                            int p = vertices + (v < vertexCount ? v : 0) * stride;
                            return Vector3.Transform(new Vector3(F32(p), F32(p + 4), F32(p + 8)), transform);
                        }
                        _grid.Cover(Corner(0), Corner(1), Corner(2));
                    }

                if (_solid && textureSet == OwnTextures && data[chunk + 2] == 2)
                {
                    Vector3 low = new(float.MaxValue), high = new(float.MinValue);
                    Vector2 uvLow = new(float.MaxValue), uvHigh = new(float.MinValue);
                    for (int i = 0; i < triangles * 3; i++)
                    {
                        int v = U16(indices + (firstIndex + i) * 2);
                        int p = vertices + (v < vertexCount ? v : 0) * stride;
                        _walls.Add(new Vector3(F32(p), F32(p + 4), F32(p + 8)));
                        (low, high) = (Vector3.Min(low, _walls[^1]), Vector3.Max(high, _walls[^1]));
                        var uv = new Vector2(F32(p + stride - 8), F32(p + stride - 4));
                        (uvLow, uvHigh) = (Vector2.Min(uvLow, uv), Vector2.Max(uvHigh, uv));
                        if (i % 3 == 2) _grid?.Wall(_walls[^3], _walls[^2], _walls[^1]);
                    }
                    // 문짝: 벽 덩이 안에서 바닥에 닿아 서 있는 얇은 판(그림을 한 번만 입힌 것). 가장 큰 것, 같으면 z 가 큰 쪽을 문으로 친다
                    var leaf = high - low;
                    float wide = MathF.Max(leaf.X, leaf.Z), thin = MathF.Min(leaf.X, leaf.Z), area = wide * leaf.Y;
                    var middle = new Vector2((low.X + high.X) / 2, (low.Z + high.Z) / 2);
                    if (_grid != null && MathF.Abs(low.Y - _grid.HeightAt(middle.X, middle.Y)) < 40 && leaf.Y is > 180 and < 640 && wide is > 150 and < 560 && thin < 60
                        && uvHigh.X - uvLow.X <= 1.05f && uvHigh.Y - uvLow.Y <= 1.05f)
                        _leaves.Add((middle, area));
                }

                for (int i = 0; i < triangles * 3; i++)
                {
                    int v = U16(indices + (firstIndex + i) * 2);
                    if (v >= vertexCount) v = 0;
                    if (!remap.TryGetValue((builder, vertexBuffer, v), out uint index))
                    {
                        int p = vertices + v * stride;
                        var color = new Vector4(data[p + colorAt + 2], data[p + colorAt + 1], data[p + colorAt], data[p + colorAt + 3]) / 255f;
                        // 장면 안쪽 테두리의 꼭짓점은 새빨갛게 칠해져 있다(경계 표시로 보인다) — 흰빛으로 돌린다
                        if (color.X > 0.75f && color.Y < 0.5f && color.Z < 0.5f) color = new Vector4(0.9f, 0.9f, 0.9f, color.W);
                        color = new Vector4(color.X * shade, color.Y * shade, color.Z * shade, _solid ? 1 : color.W);
                        var position = Vector3.Transform(new Vector3(F32(p), F32(p + 4), F32(p + 8)), transform);
                        if (textureSet == OwnTextures)
                        {
                            _min = Vector3.Min(_min, position);
                            _max = Vector3.Max(_max, position);
                        }
                        index = builder.Add(position, Vector3.UnitY, color, new Vector2(F32(p + stride - 8), F32(p + stride - 4)));
                        remap[(builder, vertexBuffer, v)] = index;
                    }
                    builder.Indices.Add(index);
                }
            }
        }
    }

    public void Draw(SceneRenderer scene, in Matrix4x4 world, bool skipGround = false)
    {
        foreach (var (mesh, texture, soft) in _parts)
            if (!soft && !(skipGround && _groundMeshes.Contains(mesh))) scene.Draw(mesh, world, null, texture, baked: true);
        if (!_parts.Exists(p => p.Soft)) return;
        _gfx.Translucent();
        foreach (var (mesh, texture, soft) in _parts)
            if (soft && !(skipGround && _groundMeshes.Contains(mesh))) scene.Draw(mesh, world, null, texture, baked: true, soft: true);
        _gfx.Opaque();
    }

    public void Dispose()
    {
        foreach (var (mesh, _, _) in _parts) mesh.Dispose();
        foreach (var texture in _textures) texture.Dispose();
    }
}
