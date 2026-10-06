using System.Buffers.Binary;
using System.Numerics;
using Dho.Data;
using Vortice.Direct3D11;

namespace Dho.Render;

/// <summary>
/// 배 모형 — <c>0001\sh0000.bin</c> 의 <c>XKMD</c> 항목 한 벌(선체 하나와 뒤따르는 돛 셋).
/// </summary>
/// <remarks>
/// XKMD 머리(0x80): +0x2C u16×10 개수 [정점버퍼, 행렬, 마디, 재질, 텍스처단, 0, 부분, 0, 부분B, 0],
/// +0x4C u32×13 구역 자리 [0 정점버퍼 설명, …, 8 부분, 9 부분B, 10 색인, 11 정점].
/// 정점버퍼 설명(0x34): u32 FVF, u32 한 정점 바이트, u32 정점 수, u32 정점 구역 안 자리, …, +0x20 u32 색인 수, u32 색인 구역 안 자리.
/// 부분: 구역 처음에 u32 자리 × 개수, 레코드 +6 u16 정점버퍼 번호, +0x2C u32 도형(5 = 삼각형 띠),
/// +0x38 u32 색인 시작, u32 도형 수. 색인은 정점버퍼 처음부터 센 번호다.
/// 정점은 D3D9 FVF 차례 — 0x112 = 자리·법선·UV, 0x316 = 자리·가중치·법선·UV×3(돛 천).
/// 좌표: y 가 위, 뱃머리가 +z, 물에 잠기는 선이 y ≈ 0. 길이는 12000 남짓이다.
/// </remarks>
internal sealed class ShipModel : IDisposable
{

    private const int FvfSail = 0x316;

    private readonly List<(Mesh Mesh, ID3D11ShaderResourceView? Texture, Vector4 Tint)> _parts = [];
    private ID3D11ShaderResourceView _hullTexture;
    private int _hullBase;

    /// <summary>
    /// 선체 그림을 바꾼다 — <c>sh0004.bin</c> 의 0 ~ 2 번(<c>SHIP_BASE_001 · 101 · 201</c>). 셋은 같은 자리의 그림인데 널빤지 쪽만 다르다:
    /// 001 은 잿빛 나무(재질 빛깔을 곱해 쓴다), 101 은 짙게 칠한 널, 201 은 밝게 칠한 널.
    /// </summary>
    public void SetHullBase(int which)
    {
        which = Math.Clamp(which, 0, 2);
        if (which == _hullBase) return;
        var next = GameTexture.FromMftf(_device, new Pack(@"0001\sh0004.bin").Entry(which));
        for (int i = 0; i < _parts.Count; i++)
            if (_parts[i].Texture == _hullTexture) _parts[i] = (_parts[i].Mesh, next, _parts[i].Tint);
        _hullTexture.Dispose();
        (_hullTexture, _hullBase) = (next, which);
    }
    private ID3D11ShaderResourceView _sailTexture;
    private ID3D11ShaderResourceView? _decoTexture;
    private readonly Gfx _device;
    private Vector3 _min = new(float.MaxValue), _max = new(float.MinValue);
    /// <summary>모형의 가운데와 반지름 — 창 안에 맞춰 보여 줄 때 쓴다.</summary>
    public Vector3 Center => (_min + _max) / 2;
    public float Radius => Vector3.Distance(_min, _max) / 2;

    /// <summary>돛 무늬 묶음(<c>0001\sa0000.bin</c>) — 무늬 열여덟 가지(TEX_BASE000 ~ 017), 무늬마다 색 벌 열 가지.</summary>
    public const int SailPatterns = 104, SailColors = 10;      // 무늬는 sa0000 ~ sa0005 에 18장씩(TEX_BASE000 ~ 103)

    /// <summary>돛의 무늬와 색을 바꾼다.</summary>
    public void SetSail(int pattern, int color)
    {
        var next = GameTexture.FromMftf(_device, new Pack($@"0001\sa{Math.Clamp(pattern, 0, SailPatterns - 1) / 18:D4}.bin").Entry(Math.Clamp(pattern, 0, SailPatterns - 1) % 18), 0, Math.Clamp(color, 0, SailColors - 1));
        for (int i = 0; i < _parts.Count; i++)
            if (_parts[i].Texture == _sailTexture) _parts[i] = (_parts[i].Mesh, next, _parts[i].Tint);
        _sailTexture.Dispose();
        _sailTexture = next;
    }

    /// <summary>자원 색인 — (갈래, 자원 번호) → (묶음 파일, 항목). 갈래 0x300 이 모형(XKMD), 0x301 이 텍스처다.</summary>
    private static Dictionary<(int Group, int Resource), (string Pack, int Entry)>? _index;
    private static byte[]? _modelTable;

    /// <summary>
    /// <c>0001\0001.tbl</c> 을 읽는다. 파일 전체가 57바이트 열쇠(「File Data Manager : (C) 2003 KOEI Co.,Ltd. Made in Japan.」)로 XOR 돼 있고,
    /// 풀면 「u8 길이 + 그만큼」 레코드의 줄이다. 묶음 머리: u32 ?, 항목 수, u8 글 길이, 글(「0001/sh0000.bin」).
    /// 항목(머리 뒤로 항목 수만큼, 묶음 안 차례 그대로): u32 ?, u16 갈래, u16 자원 번호, 묶음 안 번호. 수는 u8 인데 0xFF 면 뒤의 u16 이다.
    /// </summary>
    private static Dictionary<(int, int), (string, int)> ReadIndex()
    {
        var index = new Dictionary<(int, int), (string, int)>();
        var x = Dho.Data.GvoFiles.Read(@"0001\0001.tbl");
        var key = "File Data Manager : (C) 2003 KOEI Co.,Ltd. Made in Japan."u8;
        for (int i = 0; i < x.Length; i++) x[i] ^= key[i % key.Length];
        int left = 0, entry = 0;
        string path = "";
        for (int o = 0; o < x.Length;)
        {
            int p = o + 1, end = p + x[o];
            if (left == 0)
            {
                p += 4;
                left = x[p++];
                if (left == 0xFF) { left = BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(p)); p += 2; }
                int length = x[p++];
                (path, entry) = (System.Text.Encoding.ASCII.GetString(x, p, length).Replace('/', '\\'), 0);
            }
            else
            {
                index[(BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(p + 4)), BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(p + 6)))] = (path, entry++);
                left--;
            }
            o = end;
        }
        return index;
    }

    /// <summary>
    /// 배 모형 표 <c>0001\0002.bin</c> 에서 모형 번호의 줄 자리. 머리: u32 범위 수, (u16 첫 모형 번호, u16 첫 줄) × 범위 수 — (1, 0) · (91, 71), u32 × 2, u32 줄 수.
    /// 모형 번호는 줄의 차례로 정해진다(72 ~ 90 은 없다). 줄 64바이트: u16 선체 자원, u16 일련번호, u16 NL 자원, 6바이트, f32 크기 비, u32,
    /// u16 × 5 텍스처(갈래 0x301), u16 × 5 돛대 갈래, u16 × 5 뼈대 자원, u16 × 5 돛 자원(빈 칸 0xFFFF). 없으면 −1.
    /// </summary>
    private static int RowOf(byte[] table, int model)
    {
        int ranges = BinaryPrimitives.ReadInt32LittleEndian(table), start = 4 + ranges * 4 + 12;
        int rows = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(start - 4));
        for (int r = ranges - 1; r >= 0; r--)
        {
            int first = BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(4 + r * 4)), row0 = BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(6 + r * 4));
            if (model < first) continue;
            int k = row0 + model - first;
            int limit = r + 1 < ranges ? BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(6 + (r + 1) * 4)) : rows;
            return k < limit ? start + 64 * k : -1;
        }
        return -1;
    }

    /// <summary>
    /// 배 표의 모형 번호로 모형을 세운다 — 모형 표의 줄에 적힌 자원 번호(선체, 뼈대 · 돛 다섯 벌)를 색인으로 묶음 항목에 잇는다.
    /// 남의 돛을 빌려 쓰는 배도 줄에 그 자원 번호가 그대로 적혀 있다. 줄이 없으면 <paramref name="fallbackModel"/> 의 것을 쓴다.
    /// </summary>
    public ShipModel(Gfx gfx, int model, int fallbackModel = 16)
    {
        _device = gfx;
        _hullTexture = GameTexture.FromMftf(gfx, new Pack(@"0001\sh0004.bin").Entry(0));
        _sailTexture = GameTexture.FromMftf(gfx, new Pack(@"0001\sa0000.bin").Entry(0));

        _index ??= ReadIndex();
        var table = _modelTable ??= Dho.Data.GvoFiles.Read(@"0001\0002.bin");
        int row = RowOf(table, model);
        if (row < 0) row = RowOf(table, fallbackModel);
        int U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(at));
        void Part(int resource, bool isHull)
        {
            if (resource == 0xFFFF || !_index.TryGetValue((0x300, resource), out var found)) return;
            var data = new Pack(found.Pack).Entry(found.Entry);
            if (data.Length >= 0x80 && data.AsSpan(0, 4).SequenceEqual("XKMD"u8)) Load(gfx, data, isHull);
        }
        // 선체의 조각은 텍스처 번호가 둘이다: 0 = 바탕 판(SHIP_BASE_…), 1 = 그 배의 장식 판(줄의 넷째 텍스처 칸 — SHIP_DECO_001 ~ 004: 창 · 난간 · 금빛 띠)
        try
        {
            if (U16(row + 26) is var deco && deco != 0xFFFF && _index.TryGetValue((0x301, deco), out var decoAt))
                _decoTexture = GameTexture.FromMftf(gfx, new Pack(decoAt.Pack).Entry(decoAt.Entry));
        }
        catch (Exception) { }                      // 못 읽으면 장식도 바탕 판으로 그린다
        Part(U16(row), true);
        for (int mast = 0; mast < 5; mast++)
        {
            Part(U16(row + 40 + mast * 2), false);
            Part(U16(row + 50 + mast * 2), false);
        }
    }

    private void Load(Gfx gfx, byte[] data, bool isHull)
    {
        int U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at));
        int I32(int at) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at));
        float F32(int at) => BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(at));

        int bufferCount = U16(0x2C);
        int buffersAt = I32(0x4C), indexAt = I32(0x4C + 10 * 4), vertexAt = I32(0x4C + 11 * 4);

        // 텍스처단 → 텍스처 번호(구역 6, 0x20 바이트씩, +4 가 i16 번호)
        int stageCount = U16(0x2C + 4 * 2), stagesAt = I32(0x4C + 6 * 4);
        var decoBuilders = new MeshBuilder?[bufferCount];
        // 정점버퍼마다 삼각형을 모은다
        var builders = new MeshBuilder?[bufferCount];
        var formats = new int[bufferCount];
        for (int b = 0; b < bufferCount; b++)
        {
            int description = buffersAt + b * 0x34;
            int fvf = formats[b] = I32(description), stride = I32(description + 4), count = I32(description + 8);
            int at = vertexAt + I32(description + 12);
            if ((fvf & 0x10) == 0 || (fvf >> 8 & 0xF) == 0) continue;      // 법선이나 UV 가 없는 것은 그리지 않는다

            int normalAt = 12 + 4 * ((fvf & 0xE) switch { 0x6 => 1, 0x8 => 2, 0xA => 3, 0xC => 4, 0xE => 5, _ => 0 });
            int uvAt = normalAt + 12 + ((fvf & 0x40) != 0 ? 4 : 0);
            var builder = builders[b] = new MeshBuilder();
            var decoBuilder = isHull && _decoTexture != null ? decoBuilders[b] = new MeshBuilder() : null;
            for (int v = 0; v < count; v++)
            {
                int p = at + v * stride;
                _min = Vector3.Min(_min, new Vector3(F32(p), F32(p + 4), F32(p + 8)));
                _max = Vector3.Max(_max, new Vector3(F32(p), F32(p + 4), F32(p + 8)));
                builder.Add(new Vector3(F32(p), F32(p + 4), F32(p + 8)),
                            new Vector3(F32(p + normalAt), F32(p + normalAt + 4), F32(p + normalAt + 8)),
                            Vector4.One, new Vector2(F32(p + uvAt), F32(p + uvAt + 4)));
                decoBuilder?.Add(new Vector3(F32(p), F32(p + 4), F32(p + 8)),
                            new Vector3(F32(p + normalAt), F32(p + normalAt + 4), F32(p + normalAt + 8)),
                            Vector4.One, new Vector2(F32(p + uvAt), F32(p + uvAt + 4)));
            }
        }

        foreach (int section in (int[])[8, 9])
        {
            int subsetsAt = I32(0x4C + section * 4), subsetCount = U16(0x2C + (section == 8 ? 6 : 8) * 2);
            if (subsetsAt == 0) continue;
            for (int s = 0; s < subsetCount; s++)
            {
                int record = subsetsAt + I32(subsetsAt + s * 4);
                int buffer = U16(record + 6), primitive = I32(record + 0x2C);
                int first = I32(record + 0x38), primitives = I32(record + 0x3C);
                var builder = buffer < bufferCount ? builders[buffer] : null;
                if (builder == null) continue;
                // 이 조각의 첫 텍스처단이 가리키는 번호가 1 이면 장식 판 쪽에 모은다
                if (decoBuilders[buffer] is { } decorated && I32(record + 0x0C) > 0 && U16(record + 0x10) is var stage && stage < stageCount
                    && BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(stagesAt + stage * 0x20 + 4)) == 1) builder = decorated;

                int indices = indexAt + I32(buffersAt + buffer * 0x34 + 0x24);
                uint Index(int i) => (uint)U16(indices + (first + i) * 2);
                if (primitive == 5)
                {
                    for (int i = 0; i < primitives; i++)
                    {
                        uint a = Index(i), b = Index(i + 1), c = Index(i + 2);
                        if (a == b || b == c || a == c) continue;
                        if ((i & 1) == 0) builder.Triangle(a, b, c);
                        else builder.Triangle(b, a, c);
                    }
                }
                else
                {
                    for (int i = 0; i < primitives; i++) builder.Triangle(Index(i * 3), Index(i * 3 + 1), Index(i * 3 + 2));
                }
            }
        }

        for (int b = 0; b < bufferCount; b++)
        {
            var builder = builders[b];
            if (builder == null || (builder.Indices.Count == 0 && decoBuilders[b] is not { Indices.Count: > 0 })) continue;
            // 선체는 선체 그림, 돛 천은 돛 그림. 돛 파트의 나머지(활대·줄)는 그림을 못 찾아 나무 빛으로 칠한다.
            if (isHull)
            {
                _parts.Add((builder.Build(gfx), _hullTexture, Vector4.One));
                if (decoBuilders[b] is { Indices.Count: > 0 } decorated) _parts.Add((decorated.Build(gfx), _decoTexture, Vector4.One));      // 장식은 재질 빛깔로 물들이지 않는다
                continue;
            }
            else if (formats[b] == FvfSail) _parts.Add((builder.Build(gfx), _sailTexture, Vector4.One));
            else _parts.Add((builder.Build(gfx), null, new Vector4(0.42f, 0.33f, 0.22f, 1)));
        }
    }

    /// <param name="sail">돛 천에 입히는 빛깔(돛 도료).</param>
    /// <param name="hull">선체에 입히는 빛깔(재질).</param>
    public void Draw(SceneRenderer scene, in Matrix4x4 world, Vector4? sail = null, Vector4? hull = null)
    {
        foreach (var (mesh, texture, tint) in _parts)
            scene.Draw(mesh, world, texture == _sailTexture && sail is { } paint ? paint : texture == _hullTexture && hull is { } wood ? wood : tint, texture, cloth: texture == _sailTexture);
    }

    public void Dispose()
    {
        foreach (var (mesh, _, _) in _parts) mesh.Dispose();
        _hullTexture.Dispose();
        _sailTexture.Dispose();
        _decoTexture?.Dispose();
    }
}
