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
    private readonly List<(Mesh Mesh, ID3D11ShaderResourceView? Texture)> _parts = [];
    private readonly List<ID3D11ShaderResourceView> _textures = [];
    private readonly Dictionary<(int Set, int Texture), MeshBuilder> _builders = new();

    private const int OwnTextures = 0, SharedTextures = 1;

    public PortScene(Gfx gfx, int sceneNumber)
    {
        _gfx = gfx;
        var grm = GvoFiles.Read($@"0002\{0x20000 + sceneNumber:D8}.bin");
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

        int gtex = I32(grm, 0x1C);
        byte[]? shared = null;
        foreach (var ((set, texture), builder) in _builders)
        {
            ID3D11ShaderResourceView? view = null;
            if (set == OwnTextures && texture < GameTexture.GtexCount(grm, gtex))
                view = GameTexture.FromGtex(gfx, grm, gtex, texture);
            else if (set == SharedTextures)
            {
                shared ??= GvoFiles.Read(@"0002\no200000.bin");
                if (texture < GameTexture.GtexCount(shared, 0x10)) view = GameTexture.FromGtex(gfx, shared, 0x10, texture);
            }
            if (view != null) _textures.Add(view);
            _parts.Add((builder.Build(gfx), view));
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
            for (int record = chunk + 0xE0; record + 24 <= chunk + size; record += 24)
            {
                int texture = U16(record + 2), kind = data[record + 4];
                int vertexBuffer = data[record + 6], indexBuffer = data[record + 7];
                int firstIndex = U16(record + 20), triangles = U16(record + 22);
                if (kind is 0 or 0xFF || triangles == 0 || vertexBuffer >= vertexBuffers || indexBuffer >= indexBuffers) continue;

                int vertices = I32(vertexTable + vertexBuffer * 16), fvf = I32(vertexTable + vertexBuffer * 16 + 8);
                int stride = I32(vertexTable + vertexBuffer * 16 + 12);
                int vertexCount = I32(vertexTable + vertexBuffer * 16 + 4) / stride;
                int indices = I32(indexTable + indexBuffer * 8);
                int colorAt = 12 + ((fvf & 0xE) == 0x6 ? 4 : 0) + ((fvf & 0x10) != 0 ? 12 : 0);

                if (!_builders.TryGetValue((textureSet, texture), out var builder))
                    _builders[(textureSet, texture)] = builder = new MeshBuilder();

                for (int i = 0; i < triangles * 3; i++)
                {
                    int v = U16(indices + (firstIndex + i) * 2);
                    if (v >= vertexCount) v = 0;
                    if (!remap.TryGetValue((builder, vertexBuffer, v), out uint index))
                    {
                        int p = vertices + v * stride;
                        var color = new Vector4(data[p + colorAt + 2], data[p + colorAt + 1], data[p + colorAt], data[p + colorAt + 3]) / 255f;
                        var position = Vector3.Transform(new Vector3(F32(p), F32(p + 4), F32(p + 8)), transform);
                        index = builder.Add(position, Vector3.UnitY, color, new Vector2(F32(p + stride - 8), F32(p + stride - 4)));
                        remap[(builder, vertexBuffer, v)] = index;
                    }
                    builder.Indices.Add(index);
                }
            }
        }
    }

    public void Draw(SceneRenderer scene, in Matrix4x4 world)
    {
        foreach (var (mesh, texture) in _parts) scene.Draw(mesh, world, null, texture, baked: true);
    }

    public void Dispose()
    {
        foreach (var (mesh, _) in _parts) mesh.Dispose();
        foreach (var texture in _textures) texture.Dispose();
    }
}
