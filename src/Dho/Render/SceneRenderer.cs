using System.Numerics;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Dho.Render;

/// <summary>하늘·바다·물체를 그리는 셰이더 세 벌과, 바다 격자.</summary>
internal sealed class SceneRenderer : IDisposable
{
    // 셰이더 글 안에는 한글 주석을 넣지 않는다 — 컴파일러가 멀티바이트 글자 뒤의 줄바꿈을 먹는다.
    private const string Common = """
        cbuffer Frame : register(b0)
        {
            row_major float4x4 ViewProjection;
            row_major float4x4 InverseViewProjection;
            float3 CameraPosition; float Time;
            float3 SunDirection; float Night;
            float3 SunColor; float FogDensity;
            float3 Ambient; float Pad0;
            float3 HorizonColor; float Pad1;
            float3 ZenithColor; float Pad2;
            float3 WaterColor; float Pad3;
            float2 WorldOffset; float2 ShipPosition;
            float2 ShipDirection; float ShipSpeed; float Pad4;
        };
        cbuffer Object : register(b1)
        {
            row_major float4x4 World;
            float4 Tint;
            float4 Params;
        };

        float3 SkyColor(float3 dir)
        {
            float t = pow(saturate(dir.y), 0.45);
            return lerp(HorizonColor, ZenithColor, t);
        }
        float3 ApplyFog(float3 color, float3 position)
        {
            float d = length(position - CameraPosition);
            float f = 1.0 - exp(-d * FogDensity);
            return lerp(color, HorizonColor, saturate(f));
        }
        float Hash(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }
        float Noise(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x),
                        lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
        }
        """;

    private const string MeshShader = Common + """
        Texture2D Diffuse : register(t0);
        SamplerState Wrap : register(s0);
        struct VSIn { float3 pos : POSITION; float3 normal : NORMAL; float4 color : COLOR; float2 uv : TEXCOORD; };
        struct VSOut { float4 pos : SV_Position; float3 world : TEXCOORD0; float3 normal : TEXCOORD1; float4 color : COLOR; float2 uv : TEXCOORD2; };
        VSOut VS(VSIn i)
        {
            VSOut o;
            float4 world = mul(float4(i.pos, 1), World);
            o.world = world.xyz;
            o.pos = mul(world, ViewProjection);
            o.normal = mul(float4(i.normal, 0), World).xyz;
            o.color = i.color * Tint;
            o.uv = i.uv;
            return o;
        }
        float4 PS(VSOut i) : SV_Target
        {
            float4 base = i.color * Diffuse.Sample(Wrap, i.uv);
            if (Tint.a < 1.5) clip(base.a - (Params.y > 0.5 ? 0.02 : 0.35));
            float3 n = normalize(i.normal);
            float3 v = normalize(CameraPosition - i.world);
            if (dot(n, v) < 0) n = -n;
            float diffuse = saturate(dot(n, SunDirection)) * 0.85 + 0.15 * saturate(n.y * 0.5 + 0.5);
            float3 lit = base.rgb * (Ambient + SunColor * diffuse);
            if (Params.x > 0.5)
            {
                float3 x = base.rgb * 1.7;
                float3 over = max(x - 0.6, 0.0);
                lit = (min(x, 0.6) + over / (1.0 + over * 2.5)) * min(1.0, Ambient.g + SunColor.g * 0.55);
            }
            // figure: brightness only, no sky tint
            if (Params.z > 0.5)
                lit = base.rgb * (0.62 + 0.55 * diffuse) * min(1.0, Ambient.g + SunColor.g * 0.55);
            return float4(ApplyFog(lit, i.world), base.a);
        }
        """;

    private const string SkyShader = Common + """
        struct VSOut { float4 pos : SV_Position; float2 ndc : TEXCOORD0; };
        VSOut VS(uint id : SV_VertexID)
        {
            VSOut o;
            float2 uv = float2((id << 1) & 2, id & 2);
            o.ndc = uv * float2(2, -2) + float2(-1, 1);
            o.pos = float4(o.ndc, 1, 1);
            return o;
        }
        float4 PS(VSOut i) : SV_Target
        {
            float4 farPoint = mul(float4(i.ndc, 1, 1), InverseViewProjection);
            float3 dir = normalize(farPoint.xyz / farPoint.w - CameraPosition);
            float3 color = SkyColor(dir);

            float sun = saturate(dot(dir, SunDirection));
            color += SunColor * (pow(sun, 400.0) * 2.0 + pow(sun, 12.0) * 0.12);

            float2 sphere = float2(atan2(dir.z, dir.x), asin(clamp(dir.y, -1, 1))) * 110.0;
            float2 cell = floor(sphere);
            float star = Hash(cell);
            float2 offset = frac(sphere) - 0.5 - (float2(Hash(cell + 7.1), Hash(cell + 3.7)) - 0.5) * 0.6;
            float twinkle = 0.75 + 0.25 * sin(Time * 2.0 + star * 60.0);
            float point_ = smoothstep(0.12, 0.0, length(offset)) * step(0.965, star) * twinkle;
            color += point_ * Night * saturate(dir.y * 4.0 + 0.2);

            if (dir.y > 0.02)
            {
                float2 uv = dir.xz / (dir.y + 0.15) * 1.4 + Time * 0.004;
                float cloud = Noise(uv) * 0.6 + Noise(uv * 2.3) * 0.3 + Noise(uv * 5.1) * 0.1;
                cloud = smoothstep(0.55, 0.85, cloud) * saturate(dir.y * 3.0);
                color = lerp(color, color + (Ambient + SunColor * 0.6) * 0.5, cloud * 0.35);
            }
            return float4(color, 1);
        }
        """;

    private const string OceanShader = Common + """
        Texture3D Waves : register(t1);
        SamplerState Wrap : register(s0);
        struct VSOut { float4 pos : SV_Position; float3 world : TEXCOORD0; };
        static const float K = 95.0;

        float2 WaveSlope(float2 uv, float frame)
        {
            float3 s = Waves.Sample(Wrap, float3(uv, frame)).rgb;
            return (s.rb - 0.502) * 2.0;
        }

        float Height(float2 p, float t)
        {
            float h = 0;
            h += sin(dot(p, float2(0.031, 0.017)) + t * 0.9) * 0.9;
            h += sin(dot(p, float2(-0.019, 0.043)) + t * 1.3) * 0.5;
            h += sin(dot(p, float2(0.071, -0.052)) + t * 1.9) * 0.22;
            return h;
        }
        VSOut VS(float3 pos : POSITION)
        {
            VSOut o;
            float3 world = float3(pos.x + CameraPosition.x, 0, pos.z + CameraPosition.z);
            float fade = saturate(1.0 - length(pos.xz) / (2500.0 * K));
            world.y = Height((world.xz + WorldOffset) / K, Time) * fade * K * 0.4;
            o.world = world;
            o.pos = mul(float4(world, 1), ViewProjection);
            return o;
        }
        float4 PS(VSOut i) : SV_Target
        {
            float2 p = (i.world.xz + WorldOffset) / K;
            float dist = length(i.world - CameraPosition) / K;

            float e = 0.6;
            float h0 = Height(p, Time);
            float hx = Height(p + float2(e, 0), Time);
            float hz = Height(p + float2(0, e), Time);
            float2 slope = float2(hx - h0, hz - h0) / e;
            float frame = Time / 5.0;
            float2 ripple = WaveSlope(p / 46.0, frame) * 2.2;
            ripple += WaveSlope(float2(p.y, -p.x) / 131.0 + 0.37, frame * 0.61 + 0.5) * 2.6;
            float near = saturate(1.0 - dist / 420.0);
            ripple += WaveSlope(p / 13.0 + 0.11, frame * 1.7) * 1.1 * near;
            float calm = saturate(1.0 - dist / 3500.0);
            float3 n = normalize(float3(-(slope.x * 0.6 + ripple.x) * calm, 1.0, -(slope.y * 0.6 + ripple.y) * calm));

            float3 v = normalize(CameraPosition - i.world);
            float fresnel = 0.03 + 0.97 * pow(1.0 - saturate(dot(n, v)), 5.0);
            float3 reflected = SkyColor(reflect(-v, n));
            float facing = saturate(dot(n, SunDirection));
            float crest = saturate((ripple.x + ripple.y) * 0.9 + 0.1) * calm;
            float3 body = WaterColor * (0.62 + 0.38 * facing) + (WaterColor * 0.9 + HorizonColor * 0.12) * crest * 0.55;
            float3 color = lerp(body, reflected, fresnel * 0.7);

            float3 halfway = normalize(v + SunDirection);
            color += SunColor * pow(saturate(dot(n, halfway)), 90.0) * 0.22;

            float2 rel = (i.world.xz - ShipPosition) / K;
            float along = dot(rel, ShipDirection);
            float across = dot(rel, float2(-ShipDirection.y, ShipDirection.x));
            float hull = saturate(1.0 - length(float2(across / 16.0, along / 44.0)));
            float behind = saturate(-along / 260.0);
            float spread = 9.0 + behind * 60.0;
            float wake = saturate(1.0 - abs(across) / spread) * step(along, 20.0) * (1.0 - behind);
            float foamNoise = Noise(p * 0.22 + Time * 0.4) * 0.6 + Noise(p * 0.7 - Time * 0.8) * 0.4;
            float foam = saturate((hull * 1.4 + wake * 0.9) * (0.35 + ShipSpeed)) * smoothstep(0.25, 0.75, foamNoise + hull * 0.4);
            color = lerp(color, (Ambient + SunColor) * 0.9, saturate(foam) * 0.8);

            return float4(ApplyFog(color, i.world), 1);
        }
        """;

    private readonly Gfx _gfx;
    private readonly ID3D11VertexShader _meshVs, _skyVs, _oceanVs;
    private readonly ID3D11PixelShader _meshPs, _skyPs, _oceanPs;
    private readonly ID3D11InputLayout _meshLayout, _oceanLayout;
    private readonly ID3D11Buffer _oceanVertices, _oceanIndices;
    private readonly int _oceanIndexCount;
    private readonly ID3D11ShaderResourceView _white;
    private readonly ID3D11ShaderResourceView _waves;

    public SceneRenderer(Gfx gfx)
    {
        _gfx = gfx;
        ReadOnlyMemory<byte> code;
        (_meshVs, _meshPs, code) = gfx.Compile(MeshShader, "mesh.hlsl");
        _meshLayout = gfx.Device.CreateInputLayout(Vertex.Layout, code.Span);
        (_skyVs, _skyPs, _) = gfx.Compile(SkyShader, "sky.hlsl");
        (_oceanVs, _oceanPs, code) = gfx.Compile(OceanShader, "ocean.hlsl");
        _oceanLayout = gfx.Device.CreateInputLayout(
            [new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0)], code.Span);

        // 바다 격자 — 가운데는 촘촘하고 수평선 쪽은 성기다
        const int n = 160;
        const float reach = 2600000f;
        var vertices = new Vector3[(n + 1) * (n + 1)];
        for (int z = 0; z <= n; z++)
        for (int x = 0; x <= n; x++)
        {
            float u = x / (float)n * 2 - 1, v = z / (float)n * 2 - 1;
            vertices[z * (n + 1) + x] = new Vector3(Spread(u) * reach, 0, Spread(v) * reach);
        }
        var indices = new uint[n * n * 6];
        int at = 0;
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            uint a = (uint)(z * (n + 1) + x), b = a + 1, c = a + (uint)n + 1, d = c + 1;
            indices[at++] = a; indices[at++] = c; indices[at++] = b;
            indices[at++] = b; indices[at++] = c; indices[at++] = d;
        }
        _oceanVertices = gfx.Device.CreateBuffer<Vector3>(vertices, BindFlags.VertexBuffer);
        _oceanIndices = gfx.Device.CreateBuffer<uint>(indices, BindFlags.IndexBuffer);
        _oceanIndexCount = indices.Length;

        _white = Texture.Solid(gfx, 0xFFFFFFFF);
        _waves = Texture.Waves(gfx);

        static float Spread(float t) => MathF.Sign(t) * MathF.Pow(MathF.Abs(t), 3f);
    }

    public void DrawSky()
    {
        var ctx = _gfx.Context;
        _gfx.Background();
        ctx.IASetInputLayout(null);
        ctx.VSSetShader(_skyVs);
        ctx.PSSetShader(_skyPs);
        ctx.Draw(3, 0);
    }

    public void DrawOcean()
    {
        var ctx = _gfx.Context;
        _gfx.Opaque();
        ctx.IASetInputLayout(_oceanLayout);
        ctx.VSSetShader(_oceanVs);
        ctx.PSSetShader(_oceanPs);
        ctx.PSSetShaderResource(1, _waves);
        ctx.IASetVertexBuffer(0, _oceanVertices, 12);
        ctx.IASetIndexBuffer(_oceanIndices, Format.R32_UInt, 0);
        ctx.DrawIndexed((uint)_oceanIndexCount, 0, 0);
    }

    /// <summary>이 뒤로 <see cref="Draw"/> 로 물체를 그린다.</summary>
    public void BeginMeshes()
    {
        var ctx = _gfx.Context;
        _gfx.Opaque();
        ctx.IASetInputLayout(_meshLayout);
        ctx.VSSetShader(_meshVs);
        ctx.PSSetShader(_meshPs);
    }

    public void Draw(Mesh mesh, in Matrix4x4 world, Vector4? tint = null, ID3D11ShaderResourceView? texture = null, bool baked = false, bool soft = false, bool figure = false)
    {
        _gfx.SetObject(world, tint ?? Vector4.One, baked, soft, figure);
        _gfx.Context.PSSetShaderResource(0, texture ?? _white);
        mesh.Draw(_gfx);
    }

    public void Dispose()
    {
        _white.Dispose();
        _waves.Dispose();
        _oceanIndices.Dispose();
        _oceanVertices.Dispose();
        _oceanLayout.Dispose();
        _meshLayout.Dispose();
        _oceanPs.Dispose(); _oceanVs.Dispose();
        _skyPs.Dispose(); _skyVs.Dispose();
        _meshPs.Dispose(); _meshVs.Dispose();
    }
}

internal static unsafe class Texture
{
    /// <summary>한 가지 색(0xAARRGGBB)의 1x1 텍스처.</summary>
    public static ID3D11ShaderResourceView Solid(Gfx gfx, uint argb) => FromBgra(gfx, 1, 1, [argb]);

    /// <summary>
    /// 원본 물결 — <c>0001\oc0000.bin</c>(MFTF 안 XFTX)의 64 × 64 그림 150장. 빨강·파랑에 물결의 기울기(128 이 평평)가 들어 있다.
    /// 장을 깊이로 쌓은 3D 텍스처로 올려, 깊이를 따라가며 읽으면 장 사이가 저절로 이어진다. 파일이 없으면 평평한 한 장.
    /// </summary>
    public static ID3D11ShaderResourceView Waves(Gfx gfx)
    {
        int size = 1, frames = 1;
        byte[] pixels = [128, 255, 128, 255];
        try
        {
            var data = Dho.Data.GvoFiles.Read(@"0001\oc0000.bin");
            int I32(int at) => System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at));
            int xftx = I32(24), count = I32(xftx + 16);
            int first = xftx + I32(xftx + 20);
            int width = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(first));
            if (I32(first + 4) == 21 && width > 0 && count > 0)
            {
                var all = new byte[width * width * 4 * count];
                for (int i = 0; i < count; i++)
                {
                    int record = xftx + I32(xftx + 20 + i * 4);
                    Array.Copy(data, record + I32(record + 12), all, i * width * width * 4, width * width * 4);
                }
                (size, frames, pixels) = (width, count, all);
            }
        }
        catch (Exception) { }
        fixed (byte* bytes = pixels)
        {
            using var texture = gfx.Device.CreateTexture3D(new Texture3DDescription
            {
                Width = (uint)size,
                Height = (uint)size,
                Depth = (uint)frames,
                MipLevels = 1,
                Format = Format.B8G8R8A8_UNorm,
                BindFlags = BindFlags.ShaderResource,
            }, [new SubresourceData(bytes, (uint)(size * 4), (uint)(size * size * 4))]);
            return gfx.Device.CreateShaderResourceView(texture);
        }
    }

    /// <summary>BGRA 화소(0xAARRGGBB)로 텍스처를 만든다.</summary>
    public static ID3D11ShaderResourceView FromBgra(Gfx gfx, int width, int height, ReadOnlySpan<uint> pixels)
    {
        fixed (uint* data = pixels)
        {
            using var texture = gfx.Device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                BindFlags = BindFlags.ShaderResource,
            }, new SubresourceData(data, (uint)(width * 4)));
            return gfx.Device.CreateShaderResourceView(texture);
        }
    }
}
