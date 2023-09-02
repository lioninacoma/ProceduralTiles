using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class Noise
{
    // Noise functions by Inigo Quilez
    private static float hash1(float n)
    {
        return math.frac(n * 17.0f * math.frac(n * 0.3183099f));
    }

    private static float noise(float3 x)
    {
        float3 p = math.floor(x);
        float3 w = math.frac(x);
        float3 u = w * w * w * (w * (w * 6.0f - 15.0f) + 10.0f);

        float n = p.x + 317.0f * p.y + 157.0f * p.z;

        float a = hash1(n + 0.0f);
        float b = hash1(n + 1.0f);
        float c = hash1(n + 317.0f);
        float d = hash1(n + 318.0f);
        float e = hash1(n + 157.0f);
        float f = hash1(n + 158.0f);
        float g = hash1(n + 474.0f);
        float h = hash1(n + 475.0f);

        float k0 = a;
        float k1 = b - a;
        float k2 = c - a;
        float k3 = e - a;
        float k4 = a - b - c + d;
        float k5 = a - c - e + g;
        float k6 = a - b - e + f;
        float k7 = -a + b + c - d + e - f - g + h;

        return -1.0f + 2.0f * (k0 + k1 * u.x + k2 * u.y + k3 * u.z + k4 * u.x * u.y + k5 * u.y * u.z + k6 * u.z * u.x + k7 * u.x * u.y * u.z);
    }

    private static readonly float3x3 m3 = new float3x3(
         0.0f,  0.8f,   0.6f,
        -0.8f,  0.36f, -0.48f,
        -0.6f, -0.48f,  0.64f
    );

    public static float FBM_4(float3 x)
    {
        float f = 2.0f;
        float s = 0.5f;
        float a = 0.0f;
        float b = 0.5f;
        for (int i = 0; i < 4; i++)
        {
            float n = noise(x);
            a += b * n;
            b *= s;
            x = f * math.mul(m3, x);
        }
        return a;
    }
}
