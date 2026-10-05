using System.Buffers.Binary;
using System.Numerics;
using Dho.Data;
using Vortice.Direct3D11;

namespace Dho.Render;

/// <summary>사람의 겉모습 — 몸 틀(묶음 번호)과 부위마다 고른 차례.</summary>
internal sealed record Looks(int Frame, int Face, int Hair, int Body, int Leg, int Hand, int Cap)
{
    /// <summary>부위 이름(묶음 안 텍스처 이름의 가운데 토막)과 그 차례.</summary>
    public IEnumerable<(string Part, int Index)> Parts() =>
        [("body", Body), ("leg", Leg), ("hand", Hand), ("face", Face), ("hair", Hair), ("cap", Cap)];
}

/// <summary>
/// 사람 모형 — 몸 묶음 <c>0001\md000N.bin</c> 의 부위를 겹쳐 한 사람을 세운다.
/// </summary>
/// <remarks>
/// 묶음 안에서 부위는 「모형(XKMD), 텍스처(MFTF)」 짝으로 잇달아 있고, 텍스처 이름이 <c>NNN_face####</c> · <c>hair##</c> ·
/// <c>body###_at</c> · <c>leg##_at</c> · <c>hand##</c> · <c>cap##_m</c> 꼴이라 이름으로 부위를 가린다. NNN 이 몸 틀이다(짝수 남성형, 홀수 여성형).
/// 몸 · 다리 · 손의 정점은 팔을 벌린 자세의 모델 좌표라서 그대로 겹치면 맞는다.
/// 얼굴 · 머리 · 모자는 머리 뼈 기준 좌표다(앞이 +x, 위가 −y) — 모형 머리의 경계 상자(+0x14)에 맞게 돌려 올린다.
/// 뼈대와 움직임(<c>hm*</c>)은 못 풀어서, 팔은 어깨를 축으로 팔 쪽 정점을 돌려 내리는 것으로 흉내 낸다.
/// </remarks>
internal sealed class CharacterModel : IDisposable
{
    private readonly List<(Mesh Mesh, ID3D11ShaderResourceView? Texture)> _parts = [];
    private readonly List<ID3D11ShaderResourceView> _textures = [];

    /// <summary>묶음 번호 → 부위 이름 → (모형 항목, 텍스처 항목)들. 한 번 훑어 두고 다시 쓴다.</summary>
    private static readonly Dictionary<int, Dictionary<string, List<(int Model, int Texture)>>> Index = new();

    public static Dictionary<string, List<(int Model, int Texture)>> PartsOf(int frame)
    {
        if (Index.TryGetValue(frame, out var known)) return known;
        var parts = new Dictionary<string, List<(int, int)>>();
        try
        {
            var pack = new Pack($@"0001\md{frame:D4}.bin");
            int lastModel = -1;
            for (int entry = 0; entry < pack.Count; entry++)
            {
                if (pack.Size(entry) < 0x40) continue;
                var head = pack.Slice(entry, 0, 0x40);
                if (head.AsSpan(0, 4).SequenceEqual("XKMD"u8)) lastModel = entry;
                else if (head.AsSpan(0, 4).SequenceEqual("MFTF"u8) && lastModel >= 0)
                {
                    // MFTF +24 = 첫 XFTX 자리, XFTX +20 = 첫 그림 레코드, 레코드: 12바이트 머리, u32 자리 × n, 그 뒤 이름
                    int xftx = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(24));
                    var image = pack.Slice(entry, xftx, 0x200);
                    if (image.Length < 0x40) continue;
                    int record = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(20));
                    if (record + 16 > image.Length) continue;
                    int first = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(record + 12));
                    int mips = image[record + 9], variants = Math.Max(1, (int)image[record + 11]);
                    // 팔레트 형식(41)은 자리 칸이 「밉 수 + 벌 수」, 나머지는 「밉 수 × 벌 수」
                    int slots = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(record + 4)) == 41 ? mips + image[record + 11] : mips * variants;
                    int nameAt = record + 12 + slots * 4, nameEnd = Math.Min(image.Length, record + first);
                    if (nameAt >= nameEnd) continue;
                    string name = System.Text.Encoding.ASCII.GetString(image, nameAt, nameEnd - nameAt).Split('\0')[0].ToLowerInvariant();
                    int underscore = name.IndexOf('_');
                    string part = new(name[(underscore + 1)..].TakeWhile(char.IsLetter).ToArray());
                    if (part.Length == 0) continue;
                    if (!parts.TryGetValue(part, out var list)) parts[part] = list = [];
                    list.Add((lastModel, entry));
                }
            }
        }
        catch (Exception) { }                      // 게임 폴더에 묶음이 없으면 빈 채로 — 인형으로 대신한다
        return Index[frame] = parts;
    }

    /// <summary>모형이 하나라도 섰는가.</summary>
    public bool Loaded => _parts.Count > 0;

    public CharacterModel(Gfx gfx, Looks looks)
    {
        var parts = PartsOf(looks.Frame);
        if (!parts.TryGetValue("body", out var bodies) || bodies.Count == 0) return;
        Pack pack;
        try { pack = new Pack($@"0001\md{looks.Frame:D4}.bin"); }
        catch (Exception) { return; }

        Vector2? shoulder = null;
        foreach (var (part, index) in looks.Parts())
        {
            if (index < 0 || !parts.TryGetValue(part, out var list) || list.Count == 0) continue;
            var (modelEntry, textureEntry) = list[Math.Clamp(index, 0, list.Count - 1)];
            try
            {
                var data = pack.Entry(modelEntry);
                // 어깨: 몸통 경계 상자에서 — 너비의 27% 바깥, 꼭대기에서 13 아래
                if (part == "body")
                    shoulder = new Vector2(BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(0x20)) * 0.27f,
                                           BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(0x24)) - 13f);
                var texture = GameTexture.FromMftf(gfx, pack.Entry(textureEntry));
                _textures.Add(texture);
                foreach (var mesh in Load(gfx, data, shoulder ?? new Vector2(18, 142))) _parts.Add((mesh, texture));
            }
            catch (Exception) { }                  // 못 읽는 부위는 건너뛴다
        }
    }

    private static IEnumerable<Mesh> Load(Gfx gfx, byte[] data, Vector2 shoulder)
    {
        int U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at));
        int I32(int at) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at));
        float F32(int at) => BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(at));

        Vector3 low = new(F32(0x14), F32(0x18), F32(0x1C)), high = new(F32(0x20), F32(0x24), F32(0x28));
        int bufferCount = U16(0x2C);
        int buffersAt = I32(0x4C), indexAt = I32(0x4C + 10 * 4), vertexAt = I32(0x4C + 11 * 4);
        var builders = new MeshBuilder?[bufferCount];
        for (int b = 0; b < bufferCount; b++)
        {
            int description = buffersAt + b * 0x34;
            int fvf = I32(description), stride = I32(description + 4), count = I32(description + 8);
            int at = vertexAt + I32(description + 12);
            if ((fvf & 0x10) == 0 || (fvf >> 8 & 0xF) == 0 || count == 0) continue;
            int normalAt = 12 + 4 * ((fvf & 0xE) switch { 0x6 => 1, 0x8 => 2, 0xA => 3, 0xC => 4, 0xE => 5, _ => 0 });
            int uvAt = normalAt + 12 + ((fvf & 0x40) != 0 ? 4 : 0);

            var positions = new Vector3[count];
            var normals = new Vector3[count];
            Vector3 min = new(float.MaxValue), max = new(float.MinValue);
            for (int v = 0; v < count; v++)
            {
                int p = at + v * stride;
                positions[v] = new Vector3(F32(p), F32(p + 4), F32(p + 8));
                normals[v] = new Vector3(F32(p + normalAt), F32(p + normalAt + 4), F32(p + normalAt + 8));
                min = Vector3.Min(min, positions[v]);
                max = Vector3.Max(max, positions[v]);
            }
            // 머리 쪽 부위: 정점이 경계 상자와 딴 데 있다 — 머리 뼈 기준 좌표를 돌려 올린다
            if (MathF.Abs(max.Y - high.Y) > 5)
            {
                Vector3 Turn(Vector3 p) => new(p.Z, -p.Y, p.X);
                Vector3 turnedMin = new(float.MaxValue), turnedMax = new(float.MinValue);
                for (int v = 0; v < count; v++)
                {
                    positions[v] = Turn(positions[v]);
                    normals[v] = Turn(normals[v]);
                    turnedMin = Vector3.Min(turnedMin, positions[v]);
                    turnedMax = Vector3.Max(turnedMax, positions[v]);
                }
                var shift = (low + high) / 2 - (turnedMin + turnedMax) / 2;
                for (int v = 0; v < count; v++) positions[v] += shift;
            }

            var builder = builders[b] = new MeshBuilder();
            for (int v = 0; v < count; v++)
            {
                int p = at + v * stride;
                var (position, normal) = LowerArm(positions[v], normals[v], shoulder);
                builder.Add(position, normal, Vector4.One, new Vector2(F32(p + uvAt), F32(p + uvAt + 4)));
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
                    for (int i = 0; i < primitives; i++)
                    {
                        uint a = Index(i), b = Index(i + 1), c = Index(i + 2);
                        if (a == b || b == c || a == c) continue;
                        if ((i & 1) == 0) builder.Triangle(a, b, c);
                        else builder.Triangle(b, a, c);
                    }
                else
                    for (int i = 0; i < primitives; i++) builder.Triangle(Index(i * 3), Index(i * 3 + 1), Index(i * 3 + 2));
            }
        }
        foreach (var builder in builders)
            if (builder != null && builder.Indices.Count > 0) yield return builder.Build(gfx);
    }

    /// <summary>어깨 너머의 정점을 어깨를 축으로 아래로 돌린다 — 어깨 가까이는 덜 돌려 이음매가 부드럽다.</summary>
    private static (Vector3, Vector3) LowerArm(Vector3 p, Vector3 n, Vector2 shoulder)
    {
        float side = MathF.Sign(p.X), beyond = p.X * side - shoulder.X;
        if (side == 0 || beyond <= 0) return (p, n);
        float angle = -Math.Clamp(beyond / 8f, 0, 1) * 1.25f * side;
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        float x = p.X - side * shoulder.X, y = p.Y - shoulder.Y;
        return (new Vector3(side * shoulder.X + x * c - y * s, shoulder.Y + x * s + y * c, p.Z),
                new Vector3(n.X * c - n.Y * s, n.X * s + n.Y * c, n.Z));
    }

    public void Draw(SceneRenderer scene, in Matrix4x4 world)
    {
        foreach (var (mesh, texture) in _parts) scene.Draw(mesh, world, null, texture);
    }

    public void Dispose()
    {
        foreach (var (mesh, _) in _parts) mesh.Dispose();
        foreach (var texture in _textures) texture.Dispose();
    }
}
