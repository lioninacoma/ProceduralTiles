using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class Utils
{
    // Sourced from: https://www.shadertoy.com/view/tl3XRN
    // By BrunoLevy 
    public static bool RayTriangleIntersection(
        float3 orig, float3 dir,
        float3 A, float3 B, float3 C,
        out float t, out float u, out float v, out float3 N
    )
    {
        var E1 = B - A;
        var E2 = C - A;
        N = math.cross(E1, E2);
        float det = -math.dot(dir, N);
        float invdet = 1f / det;
        var AO = orig - A;
        var DAO = math.cross(AO, dir);
        u = math.dot(E2, DAO) * invdet;
        v = -math.dot(E1, DAO) * invdet;
        t = math.dot(AO, N) * invdet;
        return (det >= 1e-6f && t >= 0f && u >= 0f && v >= 0f && (u + v) <= 1f);
    }
}
