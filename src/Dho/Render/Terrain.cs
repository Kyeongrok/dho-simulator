using System.Buffers.Binary;
using System.Numerics;
using Dho.Data;
using Vortice.Direct3D11;

namespace Dho.Render;

/// <summary>
/// 세계지도의 뭍 — 지형 조각(<c>SLAM</c>, 이름 <c>Sea{cy}x{cx}.lgm</c>)을 배 둘레만 읽어 그린다.
/// </summary>
/// <remarks>
/// 격자 <c>0003\wm000000.bin</c>(<c>LGB</c>): 머리 36바이트 뒤에 i32 조각 번호 × 2048 (-1 = 난바다).
/// 조각 번호는 <c>gm000001</c>·<c>gm000002</c> 묶음을 이은 차례다.
/// 조각: 0x18 에 u32 겹 수 n, 0x24 부터 겹 12바이트 × n, 이어서 꼭짓점 65×65 × 20바이트
/// (f32 높이, f32×3 법선, u32 색 BGRA; 북쪽 줄부터, 줄 안은 서쪽부터).
/// 꼭짓점 사이는 세계 좌표 4 이고 높이는 세계 좌표 × 100 단위다.
/// </remarks>
internal sealed class Terrain : IDisposable
{
    public const int TileSize = 256;          // 조각 한 변, 세계 좌표
    /// <summary>세계 좌표 1 이 그리는 단위로 얼마인가. 배 모형(길이 12000 남짓)을 제 크기로 두고 바다를 그만큼 키운다.</summary>
    public const float Unit = 4000f;
    /// <summary>파일의 높이(세계 좌표 × 100)에 곱하는 값. 가로만큼 키우면 산이 너무 솟는다.</summary>
    private const float HeightScale = 26f;      // 12 → 26: 원본 화면(이베리아 서쪽 끝)의 언덕은 훨씬 높다 — 제 축척은 40
    private const int Vertices = 65, TilesX = 64, TilesY = 32;
    private const int KeepTiles = 30;

    private readonly Gfx _gfx;
    private readonly int[] _index = new int[TilesX * TilesY];
    private readonly Pres _tiles;
    private readonly Pres _textureEntries;
    private readonly Dictionary<(int, int), List<(Mesh Mesh, ID3D11ShaderResourceView? Texture)>?> _meshes = new();
    // 덧칠(겹이 번지는 곳) — 조각마다 바탕 위에 알파로 섞어 그리는 메시들
    private readonly Dictionary<(int, int), List<(Mesh Mesh, ID3D11ShaderResourceView? Texture)>> _overlays = new();
    private List<(Mesh, ID3D11ShaderResourceView?)> _builtOverlays = [];
    private readonly Dictionary<int, ID3D11ShaderResourceView> _textures = new();

    public Terrain(Gfx gfx)
    {
        _gfx = gfx;
        var grid = GvoFiles.Read(@"0003\wm000000.bin");
        for (int i = 0; i < _index.Length; i++)
            _index[i] = BinaryPrimitives.ReadInt32LittleEndian(grid.AsSpan(36 + i * 4));
        _tiles = Pres.LoadSet(@"0003\gm00000");
        _textureEntries = Pres.LoadSet(@"0003\gt00000");
    }

    /// <summary>세계 좌표 (x, y) 둘레의 조각을 그린다. 원점은 <paramref name="originX"/>, <paramref name="originY"/>.</summary>
    public void Draw(SceneRenderer scene, double originX, double originY, int reach = 1)
    {
        int centerX = (int)Math.Floor(originX / TileSize), centerY = (int)Math.Floor(originY / TileSize);
        var blended = new List<(List<(Mesh Mesh, ID3D11ShaderResourceView? Texture)>, Matrix4x4)>();
        for (int dy = -reach; dy <= reach; dy++)
        for (int dx = -reach; dx <= reach; dx++)
        {
            int cx = centerX + dx, cy = centerY + dy;
            if (cy < 0 || cy >= TilesY) continue;
            var parts = Tile((cx % TilesX + TilesX) % TilesX, cy);
            if (parts == null) continue;

            var world = Matrix4x4.CreateTranslation(
                (float)((cx * (double)TileSize - originX) * Unit), 0,
                (float)((cy * (double)TileSize - originY) * Unit));
            foreach (var (mesh, texture) in parts) scene.Draw(mesh, world, null, texture);
            if (_overlays.TryGetValue(((cx % TilesX + TilesX) % TilesX, cy), out var over) && over.Count > 0) blended.Add((over, world));
        }
        // 덧칠은 바탕을 다 그린 뒤에 알파로 섞는다(깊이는 읽기만)
        if (blended.Count > 0)
        {
            scene.Translucent();
            foreach (var (over, world) in blended)
                foreach (var (mesh, texture) in over) scene.Draw(mesh, world, null, texture, soft: true);
            scene.Opaque();
        }

        if (_meshes.Count > KeepTiles)
        {
            foreach (var key in _meshes.Keys.Where(k =>
                         Math.Abs(k.Item2 - centerY) > reach + 1 ||
                         Math.Abs(WorldMap.DeltaX(k.Item1 * TileSize, centerX * TileSize)) > (reach + 1) * TileSize).ToList())
            {
                foreach (var (mesh, _) in _meshes[key] ?? []) mesh.Dispose();
                _meshes.Remove(key);
                if (_overlays.Remove(key, out var gone)) foreach (var (mesh, _) in gone) mesh.Dispose();
            }
        }
    }

    private List<(Mesh, ID3D11ShaderResourceView?)>? Tile(int cx, int cy)
    {
        if (_meshes.TryGetValue((cx, cy), out var cached)) return cached;

        int entry = _index[cy * TilesX + cx];
        _builtOverlays = [];
        var parts = entry < 0 ? null : Build(Pres.Read(_tiles.Entries[entry]));
        _meshes[(cx, cy)] = parts;
        _overlays[(cx, cy)] = _builtOverlays;
        return parts;
    }

    /// <summary>
    /// 조각 하나를 텍스처별 메시로 짓는다. 칸(64×64)마다 바탕 겹 번호가 있고, 겹 표가 그것을 텍스처 번호로 바꾼다.
    /// 겹을 섞는 덧칠(해안의 모래와 풀이 번지는 곳): 칸 자료 뒤에 「(u16 칸x, u16 칸y, u16 덧칠 수) × a」와 「(u32 겹, u32 모서리 비트) × a2」가 있다.
    /// 모서리 비트는 그 겹이 덮는 칸의 모서리 — 1 북서 · 2 남서 · 4 남동 · 8 북동(이웃 칸끼리 맞닿는 모서리가 이어지는 것으로 맞춘 것).
    /// 덮는 모서리는 알파 1, 나머지는 0 으로 그 겹의 텍스처를 한 번 더 그려 경계가 부드럽게 번진다.
    /// </summary>
    private List<(Mesh, ID3D11ShaderResourceView?)> Build(byte[] slam)
    {
        int layerCount = BinaryPrimitives.ReadInt32LittleEndian(slam.AsSpan(0x18));
        var textureOfLayer = new Dictionary<int, int>();
        for (int i = 0; i < layerCount; i++)
            textureOfLayer[BinaryPrimitives.ReadInt32LittleEndian(slam.AsSpan(0x24 + 12 * i))] =
                BinaryPrimitives.ReadInt32LittleEndian(slam.AsSpan(0x28 + 12 * i));

        int verticesAt = 0x24 + 12 * layerCount;
        int cellsAt = verticesAt + Vertices * Vertices * 20;
        float step = TileSize / (Vertices - 1f) * Unit;

        (Vector3 Position, Vector3 Normal, Vector3 Light, float Height, Vector3 RawNormal) Corner(int i, int j)
        {
            var v = slam.AsSpan(verticesAt + (j * Vertices + i) * 20);
            float height = BinaryPrimitives.ReadSingleLittleEndian(v);
            var normal = new Vector3(BinaryPrimitives.ReadSingleLittleEndian(v[4..]),
                                     BinaryPrimitives.ReadSingleLittleEndian(v[8..]),
                                     BinaryPrimitives.ReadSingleLittleEndian(v[12..]));
            // 가로와 세로를 다르게 키우므로 법선도 그만큼 눕힌다
            var scaled = Vector3.Normalize(new Vector3(normal.X * HeightScale, normal.Y * (Unit / 100f), normal.Z * HeightScale));
            return (new Vector3(i * step, height * HeightScale, j * step), scaled, new Vector3(v[18], v[17], v[16]) / 255f, height, normal);
        }

        var builders = new Dictionary<int, MeshBuilder>();       // 텍스처 번호 → 메시. -1 = 텍스처 없음
        for (int j = 0; j < Vertices - 1; j++)
        for (int i = 0; i < Vertices - 1; i++)
        {
            int layer = BinaryPrimitives.ReadInt32LittleEndian(slam.AsSpan(cellsAt + (j * (Vertices - 1) + i) * 8));
            int texture = textureOfLayer.TryGetValue(layer, out int number) && number < _textureEntries.Entries.Count ? number : -1;

            var corners = ((int, int)[])[(i, j), (i + 1, j), (i, j + 1), (i + 1, j + 1)];
            if (texture < 0 && corners.All(c => Corner(c.Item1, c.Item2).Height < -40f)) continue;    // 난바다 바닥

            if (!builders.TryGetValue(texture, out var builder)) builders[texture] = builder = new MeshBuilder();
            uint first = (uint)builder.Vertices.Count;
            foreach (var (ci, cj) in corners)
            {
                var corner = Corner(ci, cj);
                var color = texture < 0 ? Ground(corner.Height, corner.RawNormal) * corner.Light : corner.Light;
                builder.Add(corner.Position, corner.Normal, new Vector4(color, 1), new Vector2(ci, cj));
            }
            builder.Triangle(first, first + 2, first + 1);
            builder.Triangle(first + 1, first + 2, first + 3);
        }

        // 덧칠
        int mixCells = BinaryPrimitives.ReadInt32LittleEndian(slam.AsSpan(0x1C)), mixCount = BinaryPrimitives.ReadInt32LittleEndian(slam.AsSpan(0x20));
        int headsAt = cellsAt + (Vertices - 1) * (Vertices - 1) * 8, mixAt = headsAt + mixCells * 6;
        var overlays = new Dictionary<int, MeshBuilder>();
        if (mixCells > 0 && mixAt + mixCount * 8L <= slam.Length)
        {
            int at = mixAt;
            for (int k = 0; k < mixCells; k++)
            {
                int ci = BinaryPrimitives.ReadUInt16LittleEndian(slam.AsSpan(headsAt + k * 6)), cj = BinaryPrimitives.ReadUInt16LittleEndian(slam.AsSpan(headsAt + k * 6 + 2));
                int n = BinaryPrimitives.ReadUInt16LittleEndian(slam.AsSpan(headsAt + k * 6 + 4));
                for (int m = 0; m < n && at + 8 <= slam.Length; m++, at += 8)
                {
                    int layer = BinaryPrimitives.ReadInt32LittleEndian(slam.AsSpan(at)), bits = BinaryPrimitives.ReadInt32LittleEndian(slam.AsSpan(at + 4));
                    if (ci >= Vertices - 1 || cj >= Vertices - 1 || !textureOfLayer.TryGetValue(layer, out int number) || number >= _textureEntries.Entries.Count) continue;
                    if (!overlays.TryGetValue(number, out var builder)) overlays[number] = builder = new MeshBuilder();
                    uint first = (uint)builder.Vertices.Count;
                    // 바탕과 같은 차례: 북서 · 북동 · 남서 · 남동
                    ((int I, int J) At, int Bit)[] corners = [((ci, cj), 1), ((ci + 1, cj), 8), ((ci, cj + 1), 2), ((ci + 1, cj + 1), 4)];
                    foreach (var (where, bit) in corners)
                    {
                        var corner = Corner(where.I, where.J);
                        builder.Add(corner.Position + new Vector3(0, 6 + m * 2, 0), corner.Normal, new Vector4(corner.Light, (bits & bit) != 0 ? 1 : 0), new Vector2(where.I, where.J));
                    }
                    builder.Triangle(first, first + 2, first + 1);
                    builder.Triangle(first + 1, first + 2, first + 3);
                }
            }
        }
        foreach (var (texture, builder) in overlays) _builtOverlays.Add((builder.Build(_gfx), Texture(texture)));

        var parts = new List<(Mesh, ID3D11ShaderResourceView?)>();
        foreach (var (texture, builder) in builders)
            parts.Add((builder.Build(_gfx), texture < 0 ? null : Texture(texture)));
        return parts;
    }

    private ID3D11ShaderResourceView Texture(int number)
    {
        if (!_textures.TryGetValue(number, out var view))
            _textures[number] = view = GameTexture.FromMftf(_gfx, Pres.Read(_textureEntries.Entries[number]));
        return view;
    }
    /// <summary>텍스처를 입히기 전까지 쓰는 땅 빛깔 — 물가는 모래, 위는 풀과 바위.</summary>
    private static Vector3 Ground(float height, Vector3 normal)
    {
        var sand = new Vector3(0.80f, 0.72f, 0.52f);
        var grass = new Vector3(0.36f, 0.45f, 0.22f);
        var rock = new Vector3(0.50f, 0.45f, 0.38f);
        var color = Vector3.Lerp(sand, grass, Math.Clamp((height - 15f) / 60f, 0, 1));
        color = Vector3.Lerp(color, rock, Math.Clamp((height - 500f) / 900f, 0, 1));
        return Vector3.Lerp(rock, color, Math.Clamp((normal.Y - 0.55f) / 0.3f, 0, 1));
    }

    public void Dispose()
    {
        foreach (var parts in _meshes.Values)
            foreach (var (mesh, _) in parts ?? []) mesh.Dispose();
        foreach (var over in _overlays.Values)
            foreach (var (mesh, _) in over) mesh.Dispose();
        foreach (var view in _textures.Values) view.Dispose();
    }
}
