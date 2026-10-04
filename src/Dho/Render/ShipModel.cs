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
    private readonly ID3D11ShaderResourceView _hullTexture, _sailTexture;

    private static Dictionary<string, (string Pack, int Entry)>? _names;

    /// <summary>
    /// 배 표의 모형 번호 nn 으로 모형을 찾는다 — <c>sh0000</c>~<c>sh0003</c> 에서 이름이 <c>SHIPnn_01</c> 인 선체 항목.
    /// 선체 다음부터 <c>NL_SHIPnn</c> 앞까지가 (뼈대, 돛) 쌍이다. 못 찾으면 <paramref name="fallbackModel"/> 의 것을 쓴다.
    /// </summary>
    public ShipModel(Gfx gfx, int model, int fallbackModel = 16)
    {
        _hullTexture = GameTexture.FromMftf(gfx, new Pack(@"0001\sh0004.bin").Entry(0));
        _sailTexture = GameTexture.FromMftf(gfx, new Pack(@"0001\sa0000.bin").Entry(0));

        _names ??= IndexNames();
        if (!_names.TryGetValue($"SHIP{model:D2}_01", out var found)) found = _names[$"SHIP{fallbackModel:D2}_01"];

        var pack = new Pack(found.Pack);
        for (int entry = found.Entry; entry < Math.Min(pack.Count, found.Entry + 14); entry++)
        {
            if (entry > found.Entry && NameOf(pack, entry).StartsWith("NL_")) break;
            var data = pack.Entry(entry);
            if (data.Length < 0x80 || !data.AsSpan(0, 4).SequenceEqual("XKMD"u8)) continue;
            Load(gfx, data, isHull: entry == found.Entry);
        }
    }

    private static Dictionary<string, (string, int)> IndexNames()
    {
        var names = new Dictionary<string, (string, int)>();
        foreach (string file in (string[])[@"0001\sh0000.bin", @"0001\sh0001.bin", @"0001\sh0002.bin", @"0001\sh0003.bin"])
        {
            var pack = new Pack(file);
            for (int entry = 0; entry < pack.Count; entry++)
            {
                string name = NameOf(pack, entry);
                if (name.StartsWith("SHIP")) names.TryAdd(name, (file, entry));
            }
        }
        return names;
    }

    /// <summary>XKMD 항목의 이름 — 머리 +0x5C 가 이름 구역 자리, 그 +4 부터 32바이트.</summary>
    private static string NameOf(Pack pack, int entry)
    {
        var head = pack.Slice(entry, 0, 0x80);
        if (head.Length < 0x80 || !head.AsSpan(0, 4).SequenceEqual("XKMD"u8)) return "";
        int at = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(0x4C + 4 * 4));
        if (at == 0) return "";
        var name = pack.Slice(entry, at + 4, 32);
        int end = Array.IndexOf(name, (byte)0);
        return System.Text.Encoding.ASCII.GetString(name, 0, end < 0 ? name.Length : end);
    }

    private void Load(Gfx gfx, byte[] data, bool isHull)
    {
        int U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at));
        int I32(int at) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at));
        float F32(int at) => BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(at));

        int bufferCount = U16(0x2C);
        int buffersAt = I32(0x4C), indexAt = I32(0x4C + 10 * 4), vertexAt = I32(0x4C + 11 * 4);

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
            for (int v = 0; v < count; v++)
            {
                int p = at + v * stride;
                builder.Add(new Vector3(F32(p), F32(p + 4), F32(p + 8)),
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
            if (builder == null || builder.Indices.Count == 0) continue;
            // 선체는 선체 그림, 돛 천은 돛 그림. 돛 파트의 나머지(활대·줄)는 그림을 못 찾아 나무 빛으로 칠한다.
            if (isHull) _parts.Add((builder.Build(gfx), _hullTexture, Vector4.One));
            else if (formats[b] == FvfSail) _parts.Add((builder.Build(gfx), _sailTexture, Vector4.One));
            else _parts.Add((builder.Build(gfx), null, new Vector4(0.42f, 0.33f, 0.22f, 1)));
        }
    }

    public void Draw(SceneRenderer scene, in Matrix4x4 world)
    {
        foreach (var (mesh, texture, tint) in _parts) scene.Draw(mesh, world, tint, texture);
    }

    public void Dispose()
    {
        foreach (var (mesh, _, _) in _parts) mesh.Dispose();
        _hullTexture.Dispose();
        _sailTexture.Dispose();
    }
}
