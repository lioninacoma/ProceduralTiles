using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public static class Csg
{
    public static float OpUnion(float d1, float d2) { return math.min(d1, d2); }
    public static float OpSubtract(float d1, float d2) { return math.max(-d1, d2); }
    public static float OpIntersect(float d1, float d2) { return math.max(d1, d2); }

    public static float OpUnionSmooth(float d1, float d2, float k)
    {
        float h = math.clamp(0.5f + 0.5f * (d2 - d1) / k, 0f, 1f);
        return math.lerp(d2, d1, h) - k * h * (1f - h);
    }

    public static float OpSubtractSmooth(float d1, float d2, float k)
    {
        float h = math.clamp(0.5f - 0.5f * (d2 + d1) / k, 0f, 1f);
        return math.lerp(d2, -d1, h) + k * h * (1f - h);
    }

    public static float OpIntersectSmooth(float d1, float d2, float k)
    {
        float h = math.clamp(0.5f - 0.5f * (d2 - d1) / k, 0f, 1f);
        return math.lerp(d2, d1, h) + k * h * (1f - h);
    }

    public static float3x3 OpRotateX(float a) { float sa = math.sin(a); float ca = math.cos(a); return new float3x3(1f, 0f, 0f, 0f, ca, sa, 0f, -sa, ca); }
    public static float3x3 OpRotateY(float a) { float sa = math.sin(a); float ca = math.cos(a); return new float3x3(ca, 0f, sa, 0f, 1f, 0f, -sa, 0f, ca); }
    public static float3x3 OpRotateZ(float a) { float sa = math.sin(a); float ca = math.cos(a); return new float3x3(ca, sa, 0f, -sa, ca, 0f, 0f, 0f, 1f); }

    public static float3 OpRep(float3 p, float s)
    {
        return p - s * math.round(p / s);
    }

    public static float3 OpRepLim(float3 p, float s, float3 l)
    {
        return p - s * math.clamp(math.round(p / s), -l, l);
    }

    public static float3 OpRepLim(float3 p, float s, float3 lima, float3 limb)
    {
        return p - s * math.clamp(math.round(p / s), lima, limb);
    }

    public static float3 OpRepLim(float3 p, float s, float2 lima, float2 limb)
    {
        var lima3 = new float3(lima, 0);
        var limb3 = new float3(limb, 0);
        return p - s * math.clamp(math.round(p / s), lima3.xzy, limb3.xzy);
    }

    public static float SdSphere(float3 p, float s)
    {
        return math.length(p) - s;
    }

    public static float SdBox(float3 p, float3 b)
    {
        float3 q = math.abs(p) - b;
        return math.length(math.max(q, 0.0f)) + math.min(math.max(q.x, math.max(q.y, q.z)), 0.0f);
    }
}
