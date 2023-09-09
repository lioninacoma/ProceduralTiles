#ifndef __SURFACE_NOISE__
#define __SURFACE_NOISE__

float hash1(float n)
{
    return frac(n * 17.0 * frac(n * 0.3183099));
}

float noise(float3 x)
{
    float3 p = floor(x);
    float3 w = frac(x);
    float3 u = w * w * w * (w * (w * 6.0 - 15.0) + 10.0);

    float n = p.x + 317.0 * p.y + 157.0 * p.z;

    float a = hash1(n + 0.0);
    float b = hash1(n + 1.0);
    float c = hash1(n + 317.0);
    float d = hash1(n + 318.0);
    float e = hash1(n + 157.0);
    float f = hash1(n + 158.0);
    float g = hash1(n + 474.0);
    float h = hash1(n + 475.0);

    float k0 = a;
    float k1 = b - a;
    float k2 = c - a;
    float k3 = e - a;
    float k4 = a - b - c + d;
    float k5 = a - c - e + g;
    float k6 = a - b - e + f;
    float k7 = -a + b + c - d + e - f - g + h;

    return -1.0 + 2.0 * (k0 + k1 * u.x + k2 * u.y + k3 * u.z + k4 * u.x * u.y + k5 * u.y * u.z + k6 * u.z * u.x + k7 * u.x * u.y * u.z);
}

static const float3x3 m3 = float3x3(
     0.0,  0.8,   0.6,
    -0.8,  0.36, -0.48,
    -0.6, -0.48,  0.64
);

float FBM_4(float3 x)
{
    float f = 2.0;
    float s = 0.5;
    float a = 0.0;
    float b = 0.5;
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        float n = noise(x);
        a += b * n;
        b *= s;
        x = f * mul(m3, x);
    }
    return a;
}

float SurfaceSDF(float3 position)
{
	float3 x = position;
	return x.y - (FBM_4(float3(x.x, 0, x.z) * 0.006) * 80.0 + 20.0);
}

#define SurfaceNoise(p) SurfaceSDF(p)

#endif