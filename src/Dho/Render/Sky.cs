using System.Numerics;

namespace Dho.Render;

/// <summary>하늘의 때(0 = 자정, 0.5 = 한낮)에 따른 빛과 빛깔.</summary>
internal readonly record struct Sky(
    Vector3 LightDirection, Vector3 LightColor, Vector3 Ambient,
    Vector3 Horizon, Vector3 Zenith, Vector3 Water, float Night)
{
    public static Sky At(double phase)
    {
        float angle = (float)((phase - 0.25) * Math.Tau);
        var sun = Vector3.Normalize(new Vector3(MathF.Cos(angle) * 0.8f, MathF.Sin(angle), 0.45f));
        float elevation = sun.Y;

        float day = Smooth(-0.08f, 0.30f, elevation);
        float dusk = Smooth(-0.25f, 0.05f, elevation) * (1 - Smooth(0.05f, 0.45f, elevation));
        float night = 1 - Smooth(-0.22f, 0.02f, elevation);

        // 밤에도 초저녁보다 어두워지지 않는다(원본의 밤은 푸르스름할 뿐 캄캄하지 않다) — 빛깔을 고르는 값에 바닥을 둔다
        day = MathF.Max(day, 0.28f);
        var zenith = Vector3.Lerp(new Vector3(0.03f, 0.05f, 0.16f), new Vector3(0.22f, 0.40f, 0.74f), day);
        var horizon = Vector3.Lerp(new Vector3(0.08f, 0.12f, 0.27f), new Vector3(0.60f, 0.74f, 0.90f), day);
        horizon = Vector3.Lerp(horizon, new Vector3(0.86f, 0.52f, 0.36f), dusk * 0.7f);
        var water = Vector3.Lerp(new Vector3(0.012f, 0.03f, 0.13f), new Vector3(0.03f, 0.13f, 0.36f), day);

        // 해가 지면 달빛(해의 맞은편)으로 비춘다
        bool moon = elevation < -0.04f;
        var direction = moon ? Vector3.Normalize(new Vector3(-sun.X, MathF.Max(0.35f, -sun.Y), -sun.Z)) : Vector3.Normalize(sun with { Y = MathF.Max(sun.Y, 0.06f) });
        var light = moon
            ? new Vector3(0.50f, 0.55f, 0.70f)
            : Vector3.Lerp(new Vector3(1.0f, 0.55f, 0.30f), new Vector3(1.0f, 0.96f, 0.88f), day) * (0.35f + 0.65f * day);
        var ambient = Vector3.Lerp(new Vector3(0.10f, 0.13f, 0.24f), new Vector3(0.38f, 0.42f, 0.50f), day);

        return new Sky(direction, light, ambient, horizon, zenith, water, night);
    }

    /// <summary>구름이 낀 만큼(0 맑음 ~ 1 폭풍) 하늘을 잿빛으로 가라앉힌다.</summary>
    public Sky Overcast(float amount)
    {
        if (amount <= 0) return this;
        float brightness = (Horizon.X + Horizon.Y + Horizon.Z) / 3;
        var gray = new Vector3(0.42f, 0.45f, 0.50f) * (0.25f + brightness);
        return this with
        {
            Horizon = Vector3.Lerp(Horizon, gray, amount),
            Zenith = Vector3.Lerp(Zenith, gray * 0.55f, amount),
            Water = Vector3.Lerp(Water, gray * 0.22f, amount * 0.8f),
            // 빛은 조금만 줄인다 — 구름 밑이어도 배와 물결은 또렷이 보인다
            LightColor = LightColor * (1 - 0.4f * amount),
            Ambient = Vector3.Lerp(Ambient, Ambient * 0.9f, amount),
            Night = Night * (1 - amount),
        };
    }

    /// <summary>
    /// 그 바다의 빛깔로 물들인다 — 원본의 바다 빛깔 벌에서 온 하늘빛 · 물빛과, 기준 바다(지중해)의 것과의 차이를
    /// 낮의 밝기만큼 더한다(밤에는 거의 안 보인다). 물빛은 우리 바다색이 원본보다 어두워 차이를 조금 줄여 더한다.
    /// </summary>
    public Sky Tinted(Vector3 sky, Vector3 water, Vector3 homeSky, Vector3 homeWater)
    {
        float day = Math.Clamp(Zenith.Z / 0.74f, 0, 1);
        return this with
        {
            Zenith = Vector3.Clamp(Zenith + (sky - homeSky) * day, Vector3.Zero, Vector3.One),
            Horizon = Vector3.Clamp(Horizon + (sky - homeSky) * day * 0.5f, Vector3.Zero, Vector3.One),
            Water = Vector3.Clamp(Water + (water - homeWater) * day * 0.75f, Vector3.Zero, Vector3.One),
        };
    }

    private static float Smooth(float from, float to, float value)
    {
        float t = Math.Clamp((value - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
