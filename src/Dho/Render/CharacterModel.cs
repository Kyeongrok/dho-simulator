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
/// 사람 모형 — 몸 묶음 <c>0001\md000N.bin</c> 의 부위를 겹쳐 한 사람을 세우고, 뼈대로 자세를 잡는다.
/// </summary>
/// <remarks>
/// 묶음 안에서 부위는 「모형(XKMD), 텍스처(MFTF)」 짝으로 잇달아 있고, 텍스처 이름이 <c>NNN_face####</c> · <c>hair##</c> ·
/// <c>body###_at</c> · <c>leg##_at</c> · <c>hand##</c> · <c>cap##_m</c> 꼴이라 이름으로 부위를 가린다. NNN 이 몸 틀이다(짝수 남성형, 홀수 여성형).
/// 몸 · 다리 · 손의 정점은 팔을 벌린 자세의 모델 좌표다. 얼굴 · 머리 · 모자는 머리 뼈 기준 좌표(앞이 +x, 위가 −y)라서
/// 모형 머리의 경계 상자(+0x14)에 맞게 돌려 올리고 머리 뼈에 붙인다.
/// <b>뼈대</b>는 묶음의 0번 항목이다: 마디 20개(0x2C 바이트 — u16 번호, i16 부모, 배율 × 3, 사원수 × 4, 자리 × 3)이고
/// 이름은 Hips · L_UpLeg · L_Leg · L_Foot · R_… · Spine1 · Head · L_Shoulder · L_Arm · L_ForeArm · L_Hand · L_Weapon · R_….
/// 행렬 39개 가운데 20 ~ 38 이 살갗용이고, 행렬-마디 짝 표(8바이트: u16, u16 마디, …)가 어느 마디인지 일러 준다.
/// <b>가중치</b>: 그리기 조각(부분) 레코드 +0x20 에 행렬 번호 넷(i16, −1 은 없음)이 있고, 정점의 가중치 n 개가 앞의 n 개 몫, 마지막 몫은 1 − 합이다.
/// 움직임 자료(<c>hm*</c>)는 못 풀어서 걷는 자세는 지어낸 것이다(다리와 팔을 번갈아 흔든다).
/// </remarks>
internal sealed class CharacterModel : IDisposable
{
    private const int Head = 9, LeftArm = 11, LeftForeArm = 12, RightArm = 16, RightForeArm = 17;
    private const int LeftUpLeg = 2, LeftLeg = 3, RightUpLeg = 5, RightLeg = 6, Spine = 8;

    private sealed class Part
    {
        public required Vector3[] Positions, Normals;
        public required Vector2[] Uvs;
        /// <summary>정점마다 마디 셋과 그 몫.</summary>
        public required int[] Bones;
        public required float[] Weights;
        public required List<uint> Indices;
        public ID3D11ShaderResourceView? Texture;
        public Mesh? Mesh;
    }

    private readonly Gfx _gfx;
    private readonly List<Part> _parts = [];
    private readonly List<ID3D11ShaderResourceView> _textures = [];
    private int[] _parents = [];
    private Vector3[] _joints = [];
    // 선 자세(T 꼴)에서의 마디 — 제 자리 값과, 세계 행렬의 역
    private Quaternion[] _bindTurn = [];
    private Vector3[] _bindSpot = [];
    private Matrix4x4[] _bindInverse = [];
    private Matrix4x4[] _bindWorld = [];
    private Motion? _idle, _walk, _run;

    /// <summary>
    /// 움직임 하나 — <c>0001\hm000N.bin</c> 의 항목(FCVD0022). 16바이트 레코드의 줄이다.
    /// 머리: "FCVD0022", u32 레코드 수, u16 161, u16 곡선 수, u32 × 곡선 수(곡선 머리 레코드의 차례).
    /// 곡선 머리: u32 레코드 수, u8 3, u8 갈래, u16 마디, u32 키 수, u32 0. 갈래 6 · 7 · 8 = 자리 x y z, 0x22 ~ 0x25 = 사원수 x y z w.
    /// 키: u16 4, u16 끝 프레임, f32 c0, c1, c2 — 그 프레임까지 값 = c0 + c1·t + c2·t²(t 는 프레임).
    /// </summary>
    private sealed class Motion
    {
        public int Length;
        private readonly Dictionary<(int Node, int Kind), (int End, float C0, float C1, float C2)[]> _curves = new();

        public static Motion? Read(byte[] data)
        {
            if (data.Length < 32 || !data.AsSpan(0, 8).SequenceEqual("FCVD0022"u8)) return null;
            var motion = new Motion();
            int count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(14));
            for (int c = 0; c < count; c++)
            {
                int at = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(16 + c * 4)) * 16;
                if (at < 0 || at + 16 > data.Length || data[at + 4] != 3) continue;
                int kind = data[at + 5], node = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at + 6));
                int keys = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at + 8));
                if (keys <= 0 || at + 16 + keys * 16L > data.Length) continue;
                var curve = new (int, float, float, float)[keys];
                for (int k = 0; k < keys; k++)
                {
                    int key = at + 16 + k * 16;
                    curve[k] = (BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(key + 2)), BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(key + 4)),
                                BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(key + 8)), BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(key + 12)));
                    motion.Length = Math.Max(motion.Length, curve[k].Item1);
                }
                motion._curves[(node, kind)] = curve;
            }
            return motion.Length > 0 ? motion : null;
        }

        private float Value(int node, int kind, float t, float fallback)
        {
            if (!_curves.TryGetValue((node, kind), out var curve)) return fallback;
            foreach (var (end, c0, c1, c2) in curve)
                if (t <= end) return c0 + c1 * t + c2 * t * t;
            var last = curve[^1];
            return last.C0 + last.C1 * last.End + last.C2 * last.End * last.End;
        }

        /// <summary>그 마디의 제 자리 회전과 자리. 곡선이 없는 값은 선 자세의 것.</summary>
        public (Quaternion Turn, Vector3 Spot) At(int node, float t, Quaternion turn, Vector3 spot) =>
            (Quaternion.Normalize(new Quaternion(Value(node, 0x22, t, turn.X), Value(node, 0x23, t, turn.Y), Value(node, 0x24, t, turn.Z), Value(node, 0x25, t, turn.W))),
             new Vector3(Value(node, 6, t, spot.X), Value(node, 7, t, spot.Y), Value(node, 8, t, spot.Z)));
    }
    private Dictionary<int, int> _matrixNode = new();
    private (float, float) _posedAs = (float.NaN, float.NaN);

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
        _gfx = gfx;
        var parts = PartsOf(looks.Frame);
        if (!parts.TryGetValue("body", out var bodies) || bodies.Count == 0) return;
        Pack pack;
        try
        {
            pack = new Pack($@"0001\md{looks.Frame:D4}.bin");
            ReadSkeleton(pack.Entry(0));
        }
        catch (Exception) { return; }
        try
        {
            // 움직임: 몸 틀과 같은 번호의 묶음에서 서 있기(0) · 걷기(5) · 달리기(9)
            var motions = new Pack($@"0001\hm{looks.Frame:D4}.bin");
            (_idle, _walk, _run) = (Motion.Read(motions.Entry(0)), Motion.Read(motions.Entry(5)), Motion.Read(motions.Entry(9)));
        }
        catch (Exception) { }                      // 없으면 지어낸 자세로 선다

        foreach (var (part, index) in looks.Parts())
        {
            if (index < 0 || !parts.TryGetValue(part, out var list) || list.Count == 0) continue;
            var (modelEntry, textureEntry) = list[Math.Clamp(index, 0, list.Count - 1)];
            try
            {
                var texture = GameTexture.FromMftf(gfx, pack.Entry(textureEntry));
                _textures.Add(texture);
                foreach (var loaded in Load(pack.Entry(modelEntry)))
                {
                    loaded.Texture = texture;
                    _parts.Add(loaded);
                }
            }
            catch (Exception) { }                  // 못 읽는 부위는 건너뛴다
        }
    }

    /// <summary>뼈대(묶음의 0번 항목): 마디의 부모와 선 자세에서의 자리, 살갗 행렬이 가리키는 마디.</summary>
    private void ReadSkeleton(byte[] data)
    {
        int U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at));
        int I16(int at) => BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(at));
        int I32(int at) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at));
        float F32(int at) => BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(at));

        int matrices = U16(0x2C + 2), nodes = U16(0x2C + 4);
        int mapAt = I32(0x4C + 2 * 4), nodesAt = I32(0x4C + 3 * 4);
        _parents = new int[nodes];
        _joints = new Vector3[nodes];
        _bindTurn = new Quaternion[nodes];
        _bindSpot = new Vector3[nodes];
        _bindInverse = new Matrix4x4[nodes];
        var world = new Matrix4x4[nodes];
        for (int i = 0; i < nodes; i++)
        {
            int at = nodesAt + i * 0x2C;
            _parents[i] = I16(at + 2);
            var local = Matrix4x4.CreateFromQuaternion(new Quaternion(F32(at + 16), F32(at + 20), F32(at + 24), F32(at + 28)))
                        * Matrix4x4.CreateTranslation(F32(at + 32), F32(at + 36), F32(at + 40));
            world[i] = _parents[i] >= 0 && _parents[i] < i ? local * world[_parents[i]] : local;
            _joints[i] = world[i].Translation;
            _bindTurn[i] = new Quaternion(F32(at + 16), F32(at + 20), F32(at + 24), F32(at + 28));
            _bindSpot[i] = new Vector3(F32(at + 32), F32(at + 36), F32(at + 40));
            Matrix4x4.Invert(world[i], out _bindInverse[i]);
        }
        _bindWorld = world;
        for (int m = 0; m < matrices; m++) _matrixNode[m] = U16(mapAt + m * 8 + 2);
    }

    private IEnumerable<Part> Load(byte[] data)
    {
        int U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at));
        int I16(int at) => BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(at));
        int I32(int at) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at));
        float F32(int at) => BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(at));

        Vector3 low = new(F32(0x14), F32(0x18), F32(0x1C)), high = new(F32(0x20), F32(0x24), F32(0x28));
        int bufferCount = U16(0x2C);
        int buffersAt = I32(0x4C), indexAt = I32(0x4C + 10 * 4), vertexAt = I32(0x4C + 11 * 4);
        var parts = new Part?[bufferCount];
        var weightCounts = new int[bufferCount];
        var weightData = new (int At, int Stride)[bufferCount];
        for (int b = 0; b < bufferCount; b++)
        {
            int description = buffersAt + b * 0x34;
            int fvf = I32(description), stride = I32(description + 4), count = I32(description + 8);
            int at = vertexAt + I32(description + 12);
            if ((fvf & 0x10) == 0 || (fvf >> 8 & 0xF) == 0 || count == 0) continue;
            int weights = weightCounts[b] = (fvf & 0xE) switch { 0x6 => 1, 0x8 => 2, 0xA => 3, 0xC => 4, 0xE => 5, _ => 0 };
            weightData[b] = (at + 12, stride);
            int normalAt = 12 + 4 * weights;
            int uvAt = normalAt + 12 + ((fvf & 0x40) != 0 ? 4 : 0);

            var part = parts[b] = new Part
            {
                Positions = new Vector3[count], Normals = new Vector3[count], Uvs = new Vector2[count],
                Bones = new int[count * 4], Weights = new float[count * 4], Indices = [],
            };
            Vector3 min = new(float.MaxValue), max = new(float.MinValue);
            for (int v = 0; v < count; v++)
            {
                int p = at + v * stride;
                part.Positions[v] = new Vector3(F32(p), F32(p + 4), F32(p + 8));
                part.Normals[v] = new Vector3(F32(p + normalAt), F32(p + normalAt + 4), F32(p + normalAt + 8));
                part.Uvs[v] = new Vector2(F32(p + uvAt), F32(p + uvAt + 4));
                (part.Bones[v * 4], part.Weights[v * 4]) = (-1, 1);
                min = Vector3.Min(min, part.Positions[v]);
                max = Vector3.Max(max, part.Positions[v]);
            }
            // 머리 쪽 부위: 정점이 경계 상자와 딴 데 있다 — 머리 뼈 기준 좌표를 돌려 올리고 머리 뼈에 붙인다
            if (MathF.Abs(max.Y - high.Y) > 5)
            {
                Vector3 Turn(Vector3 p) => new(p.Z, -p.Y, p.X);
                Vector3 turnedMin = new(float.MaxValue), turnedMax = new(float.MinValue);
                for (int v = 0; v < count; v++)
                {
                    part.Positions[v] = Turn(part.Positions[v]);
                    part.Normals[v] = Turn(part.Normals[v]);
                    turnedMin = Vector3.Min(turnedMin, part.Positions[v]);
                    turnedMax = Vector3.Max(turnedMax, part.Positions[v]);
                }
                var shift = (low + high) / 2 - (turnedMin + turnedMax) / 2;
                for (int v = 0; v < count; v++)
                {
                    part.Positions[v] += shift;
                    part.Bones[v * 4] = Head;
                }
                weightCounts[b] = -1;              // 조각의 행렬 번호는 안 쓴다
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
                int firstVertex = I32(record + 0x30), vertexCount = I32(record + 0x34);
                int first = I32(record + 0x38), primitives = I32(record + 0x3C);
                var part = buffer < bufferCount ? parts[buffer] : null;
                if (part == null) continue;

                // 행렬 번호가 20 아래인 조각은 뼈대가 아니라 그 옷에 딸린 마디(늘어진 천 · 장식)에 붙는다.
                // 그 마디의 자리를 못 풀어서 그리지 않는다 — 그대로 그리면 발밑에 조각이 떨어져 보인다
                // 다만 레코드 첫 바이트에 0x40 이 없는 조각은 몫 없이 뼈대의 마디 하나(레코드 +4)에 통째로 붙는 딱딱한 조각이다
                // (무릎 보호대 · 팔 장식 · 가슴 장식). 정점이 그 마디 기준 좌표라서 선 자세의 마디 행렬로 몸 좌표로 옮겨 둔다
                if (weightCounts[buffer] >= 0 && (data[record] & 0x40) == 0 && U16(record + 4) is var rigid && rigid < _bindWorld.Length)
                {
                    for (int v = firstVertex; v < Math.Min(firstVertex + vertexCount, part.Positions.Length); v++)
                    {
                        part.Positions[v] = Vector3.Transform(part.Positions[v], _bindWorld[rigid]);
                        part.Normals[v] = Vector3.TransformNormal(part.Normals[v], _bindWorld[rigid]);
                        for (int k = 0; k < 4; k++) (part.Bones[v * 4 + k], part.Weights[v * 4 + k]) = (k == 0 ? rigid : -1, k == 0 ? 1 : 0);
                    }
                }
                else if (weightCounts[buffer] >= 0 && I16(record + 0x20) is >= 0 and < 20) continue;
                else

                // 이 조각의 정점들에 마디와 몫을 적는다
                if (weightCounts[buffer] >= 0)
                {
                    int given = Math.Min(weightCounts[buffer], 3);          // 몫이 셋이면 마디는 넷(마지막은 나머지)
                    for (int v = firstVertex; v < Math.Min(firstVertex + vertexCount, part.Positions.Length); v++)
                    {
                        float rest = 1;
                        for (int k = 0; k < 4; k++)
                        {
                            int matrix = I16(record + 0x20 + k * 2);
                            int node = matrix >= 0 && _matrixNode.TryGetValue(matrix, out int mapped) ? mapped : -1;
                            float weight = k < given ? F32(weightData[buffer].At + v * weightData[buffer].Stride + k * 4) : rest;
                            if (node < 0) weight = 0;
                            rest -= weight;
                            (part.Bones[v * 4 + k], part.Weights[v * 4 + k]) = (node, weight);
                            if (k >= given) break;
                        }
                    }
                }

                int indices = indexAt + I32(buffersAt + buffer * 0x34 + 0x24);
                uint Index(int i) => (uint)U16(indices + (first + i) * 2);
                void Triangle(uint a, uint b, uint c) { part.Indices.Add(a); part.Indices.Add(b); part.Indices.Add(c); }
                if (primitive == 5)
                    for (int i = 0; i < primitives; i++)
                    {
                        uint a = Index(i), b = Index(i + 1), c = Index(i + 2);
                        if (a == b || b == c || a == c) continue;
                        if ((i & 1) == 0) Triangle(a, b, c);
                        else Triangle(b, a, c);
                    }
                else
                    for (int i = 0; i < primitives; i++) Triangle(Index(i * 3), Index(i * 3 + 1), Index(i * 3 + 2));
            }
        }
        foreach (var part in parts)
            if (part != null && part.Indices.Count > 0) yield return part;
    }

    /// <summary>
    /// 마디마다 「선 자세에서 얼마나 움직였나」를 낸다. 자세는 지어낸 것이다 — 팔은 내리고,
    /// <paramref name="stride"/>(걸음의 위상, 라디안. 한 바퀴가 두 걸음)에 따라 다리와 팔을 번갈아 흔든다.
    /// <paramref name="amount"/> 는 걸음의 크기(0 서 있음 ~ 1 걷기, 그 위는 달리기)라서 0 으로 줄이면 선 자세로 부드럽게 돌아간다.
    /// </summary>
    private Matrix4x4[] Pose(float stride, float amount, float time)
    {
        if (_idle != null && _walk != null) return Animated(stride, amount, time);
        float swing = MathF.Sin(stride), run = Math.Clamp(amount - 1, 0, 1);
        float size = MathF.Min(amount, 1) * (1 + run * 0.5f);
        var turn = new Matrix4x4[_joints.Length];
        Array.Fill(turn, Matrix4x4.Identity);
        void Set(int node, Matrix4x4 rotation) { if (node < turn.Length) turn[node] = rotation; }
        // 0 ~ 1 사이를 부드럽게 오르내리는 굽힘 — 다리가 앞으로 넘어오는 동안(뒤 → 앞) 무릎이 접힌다
        static float Fold(float phase) { float c = MathF.Max(0, MathF.Cos(phase)); return c * c; }

        // 모델 좌표: +x 가 왼팔 쪽, +z 가 앞. 팔은 z 축으로 돌려 내리고, 흔들기는 x 축으로
        float arm = 0.32f * size, elbow = 0.14f + 0.5f * run;
        Set(LeftArm, Matrix4x4.CreateRotationZ(-1.34f) * Matrix4x4.CreateRotationX(swing * arm));
        Set(RightArm, Matrix4x4.CreateRotationZ(1.34f) * Matrix4x4.CreateRotationX(-swing * arm));
        Set(LeftForeArm, Matrix4x4.CreateRotationX(-elbow - MathF.Max(0, -swing) * 0.3f * size));
        Set(RightForeArm, Matrix4x4.CreateRotationX(-elbow - MathF.Max(0, swing) * 0.3f * size));

        float hip = 0.36f * size, knee = (0.55f + 0.45f * run) * size;
        Set(LeftUpLeg, Matrix4x4.CreateRotationX(-swing * hip));
        Set(RightUpLeg, Matrix4x4.CreateRotationX(swing * hip));
        Set(LeftLeg, Matrix4x4.CreateRotationX(0.04f * size + Fold(stride) * knee));
        Set(RightLeg, Matrix4x4.CreateRotationX(0.04f * size + Fold(stride + MathF.PI) * knee));
        // 윗몸: 팔과 같은 쪽으로 조금 틀고, 달릴수록 앞으로 숙인다
        Set(Spine, Matrix4x4.CreateRotationY(swing * 0.05f * size) * Matrix4x4.CreateRotationX(0.03f * size + 0.12f * run));
        Set(Head, Matrix4x4.CreateRotationY(-swing * 0.03f * size));

        var moved = new Matrix4x4[_joints.Length];
        for (int i = 0; i < moved.Length; i++)
        {
            var own = Matrix4x4.CreateTranslation(-_joints[i]) * turn[i] * Matrix4x4.CreateTranslation(_joints[i]);
            moved[i] = _parents[i] >= 0 && _parents[i] < i ? own * moved[_parents[i]] : own;
        }
        return moved;
    }

    /// <summary>
    /// 원본 움직임으로 잡은 자세 — 서 있기 · 걷기 · 달리기를 걸음의 크기에 따라 섞는다.
    /// 걷기와 달리기는 한 바퀴가 두 걸음이라 걸음의 위상을 그대로 프레임으로 옮긴다.
    /// </summary>
    private Matrix4x4[] Animated(float stride, float amount, float time)
    {
        float turn = stride / MathF.Tau;
        turn -= MathF.Floor(turn);
        var run = _run ?? _walk!;
        float idleAt = time * 30 % _idle!.Length, walkAt = turn * _walk!.Length, runAt = turn * run.Length;
        float toWalk = Math.Clamp(amount, 0, 1), toRun = Math.Clamp(amount - 1, 0, 1);

        var moved = new Matrix4x4[_joints.Length];
        var world = new Matrix4x4[_joints.Length];
        for (int i = 0; i < moved.Length; i++)
        {
            var (q, p) = _idle.At(i, idleAt, _bindTurn[i], _bindSpot[i]);
            if (toWalk > 0)
            {
                var (wq, wp) = _walk.At(i, walkAt, _bindTurn[i], _bindSpot[i]);
                if (toRun > 0)
                {
                    var (rq, rp) = run.At(i, runAt, _bindTurn[i], _bindSpot[i]);
                    (wq, wp) = (Quaternion.Slerp(wq, rq, toRun), Vector3.Lerp(wp, rp, toRun));
                }
                (q, p) = (Quaternion.Slerp(q, wq, toWalk), Vector3.Lerp(p, wp, toWalk));
            }
            // 엉덩이(뿌리 바로 밑 마디)가 제자리에서 벗어나지 않게 — 나아가는 것은 게임이 한다
            if (i == 1) p = new Vector3(_bindSpot[i].X, p.Y, _bindSpot[i].Z);
            var local = Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(p);
            world[i] = _parents[i] >= 0 && _parents[i] < i ? local * world[_parents[i]] : local;
            moved[i] = _bindInverse[i] * world[i];
        }
        return moved;
    }

    /// <param name="stride">걸음의 위상(라디안).</param>
    /// <param name="amount">걸음의 크기 — 0 서 있음, 1 걷기, 2 달리기.</param>
    /// <param name="time">흐른 시간(초) — 서 있을 때의 숨쉬기 따위에 쓴다.</param>
    public void Draw(SceneRenderer scene, in Matrix4x4 world, float stride = 0, float amount = 0, float time = 0)
    {
        // 자세가 바뀌었을 때만 살갗을 다시 입힌다(원본 움직임이 있으면 서 있을 때도 움직이므로 늘 다시 입힌다)
        var pose = amount < 0.005f ? (0f, _idle != null ? -1f - time : 0f) : (stride, amount);
        if (pose != _posedAs || _parts.Exists(p => p.Mesh == null))
        {
            var moved = Pose(stride, MathF.Max(0, amount < 0.005f ? 0 : amount), time);
            foreach (var part in _parts)
            {
                var builder = new MeshBuilder();
                for (int v = 0; v < part.Positions.Length; v++)
                {
                    Vector3 position = Vector3.Zero, normal = Vector3.Zero;
                    float total = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        int bone = part.Bones[v * 4 + k];
                        float weight = part.Weights[v * 4 + k];
                        if (weight <= 0 || bone < 0 || bone >= moved.Length) continue;
                        position += Vector3.Transform(part.Positions[v], moved[bone]) * weight;
                        normal += Vector3.TransformNormal(part.Normals[v], moved[bone]) * weight;
                        total += weight;
                    }
                    if (total < 0.01f) (position, normal) = (part.Positions[v], part.Normals[v]);
                    else if (total < 0.99f) (position, normal) = (position / total, normal / total);      // 못 푼 마디의 몫은 나머지에 나눠 준다
                    builder.Add(position, normal, Vector4.One, part.Uvs[v]);
                }
                builder.Indices.AddRange(part.Indices);
                part.Mesh?.Dispose();
                part.Mesh = builder.Build(_gfx);
            }
            _posedAs = pose;
        }
        foreach (var part in _parts)
            if (part.Mesh != null) scene.Draw(part.Mesh, world, null, part.Texture, figure: true);
    }

    public void Dispose()
    {
        foreach (var part in _parts) part.Mesh?.Dispose();
        foreach (var texture in _textures) texture.Dispose();
    }
}
