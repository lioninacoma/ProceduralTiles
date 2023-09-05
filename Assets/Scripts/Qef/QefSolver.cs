using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

public static class QefSolver
{
    public static readonly int SVD_NUM_SWEEPS = 5;
    public static readonly float PSUEDO_INVERSE_THRESHOLD = 0.1f;

    private static void SvdVmulSym(out float4 result, NativeArray<float> A, float4 v)
    {
        float4 A_row_x = new float4(A[0], A[1], A[2], 0f);
        result = float4.zero;
        result.x = math.dot(A_row_x, v);
        result.y = A[1] * v.x + A[3] * v.y + A[4] * v.z;
        result.z = A[2] * v.x + A[4] * v.y + A[5] * v.z;
    }

    public static void GivensCoeffsSym(float a_pp, float a_pq, float a_qq, out float c, out float s)
    {
        if (a_pq == 0f)
        {
            c = 1f;
            s = 0f;
            return;
        }
        float tau = (a_qq - a_pp) / (2f * a_pq);
        float stt = math.sqrt(1f + tau * tau);
        float tan = 1f / ((tau >= 0f) ? (tau + stt) : (tau - stt));
        c = math.rsqrt(1f + tan * tan);
        s = tan * c;
    }

    public static void SvdRotateXY(ref float x, ref float y, float c, float s)
    {
        float u = x; float v = y;
        x = c * u - s * v;
        y = s * u + c * v;
    }

    public static void SvdRotateQXY(ref float x, ref float y, float a, float c, float s)
    {
        float cc = c * c; float ss = s * s;
        float mx = 2f * c * s * a;
        float u = x; float v = y;
        x = cc * u - mx + ss * v;
        y = ss * u + mx + cc * v;
    }

    public static void SvdRotate(ref float3x3 vtav, ref float3x3 v, int a, int b)
    {
        if (vtav[a][b] == 0f) return;

        float c, s;
        GivensCoeffsSym(vtav[a][a], vtav[a][b], vtav[b][b], out c, out s);

        float x, y, z;
        x = vtav[a][a]; y = vtav[b][b]; z = vtav[a][b];
        SvdRotateQXY(ref x, ref y, z, c, s);
        vtav[a][a] = x; vtav[b][b] = y; vtav[a][b] = z;

        x = vtav[0][3 - b]; y = vtav[1 - a][2];
        SvdRotateXY(ref x, ref y, c, s);
        vtav[0][3 - b] = x; vtav[1 - a][2] = y;

        vtav[a][b] = 0f;

        x = v[0][a]; y = v[0][b];
        SvdRotateXY(ref x, ref y, c, s);
        v[0][a] = x; v[0][b] = y;

        x = v[1][a]; y = v[1][b];
        SvdRotateXY(ref x, ref y, c, s);
        v[1][a] = x; v[1][b] = y;

        x = v[2][a]; y = v[2][b];
        SvdRotateXY(ref x, ref y, c, s);
        v[2][a] = x; v[2][b] = y;
    }

    public static void SvdSolveSym(NativeArray<float> a, out float4 sigma, ref float3x3 v)
    {
        // assuming that A is symmetric: can optimize all operations for 
        // the upper right triagonal
        float3x3 vtav = float3x3.zero;
        vtav[0][0] = a[0]; vtav[0][1] = a[1]; vtav[0][2] = a[2];
        vtav[1][0] = 0f; vtav[1][1] = a[3]; vtav[1][2] = a[4];
        vtav[2][0] = 0f; vtav[2][1] = 0f; vtav[2][2] = a[5];

        // assuming V is identity: you can also pass a matrix the rotations
        // should be applied to. (U is not computed)
        for (int i = 0; i < SVD_NUM_SWEEPS; ++i)
        {
            SvdRotate(ref vtav, ref v, 0, 1);
            SvdRotate(ref vtav, ref v, 0, 2);
            SvdRotate(ref vtav, ref v, 1, 2);
        }

        sigma = new float4(vtav[0][0], vtav[1][1], vtav[2][2], 0f);
    }

    public static float SvdInvDet(float x, float tol)
    {
        return (math.abs(x) < tol || math.abs(1f / x) < tol) ? 0f : (1f / x);
    }

    public static void SvdPseudoInverse(out float3x3 o, float4 sigma, float3x3 v)
    {
        float d0 = SvdInvDet(sigma.x, PSUEDO_INVERSE_THRESHOLD);
        float d1 = SvdInvDet(sigma.y, PSUEDO_INVERSE_THRESHOLD);
        float d2 = SvdInvDet(sigma.z, PSUEDO_INVERSE_THRESHOLD);

        o = float3x3.zero;
        o[0][0] = v[0][0] * d0 * v[0][0] + v[0][1] * d1 * v[0][1] + v[0][2] * d2 * v[0][2];
        o[0][1] = v[0][0] * d0 * v[1][0] + v[0][1] * d1 * v[1][1] + v[0][2] * d2 * v[1][2];
        o[0][2] = v[0][0] * d0 * v[2][0] + v[0][1] * d1 * v[2][1] + v[0][2] * d2 * v[2][2];
        o[1][0] = v[1][0] * d0 * v[0][0] + v[1][1] * d1 * v[0][1] + v[1][2] * d2 * v[0][2];
        o[1][1] = v[1][0] * d0 * v[1][0] + v[1][1] * d1 * v[1][1] + v[1][2] * d2 * v[1][2];
        o[1][2] = v[1][0] * d0 * v[2][0] + v[1][1] * d1 * v[2][1] + v[1][2] * d2 * v[2][2];
        o[2][0] = v[2][0] * d0 * v[0][0] + v[2][1] * d1 * v[0][1] + v[2][2] * d2 * v[0][2];
        o[2][1] = v[2][0] * d0 * v[1][0] + v[2][1] * d1 * v[1][1] + v[2][2] * d2 * v[1][2];
        o[2][2] = v[2][0] * d0 * v[2][0] + v[2][1] * d1 * v[2][1] + v[2][2] * d2 * v[2][2];
    }

    public static void SvdMulMatrixVec(out float4 result, float3x3 a, float4 b)
    {
        result = float4.zero;
        result.x = math.dot(new float4(a[0][0], a[0][1], a[0][2], 0f), b);
        result.y = math.dot(new float4(a[1][0], a[1][1], a[1][2], 0f), b);
        result.z = math.dot(new float4(a[2][0], a[2][1], a[2][2], 0f), b);
        result.w = 0f;
    }

    public static void SvdSolveATAATb(NativeArray<float> ATA, float4 ATb, out float4 x)
    {
        float3x3 V = float3x3.zero;
        V[0][0] = 1f; V[0][1] = 0f; V[0][2] = 0f;
        V[1][0] = 0f; V[1][1] = 1f; V[1][2] = 0f;
        V[2][0] = 0f; V[2][1] = 0f; V[2][2] = 1f;

        SvdSolveSym(ATA, out float4 sigma, ref V);

        // A = UEV^T; U = A / (E*V^T)
        SvdPseudoInverse(out float3x3 Vinv, sigma, V);
        SvdMulMatrixVec(out x, Vinv, ATb);
    }

    public static float CalcError(NativeArray<float> A, float4 x, float4 b)
    {
        SvdVmulSym(out float4 tmp, A, x);
        tmp = b - tmp;
        return math.dot(tmp, tmp);
    }

    public static void Add(float3 n, float3 p, ref NativeArray<float> ATA, ref float4 ATb, ref float4 pointaccum)
    {
        ATA[0] += n.x * n.x;
        ATA[1] += n.x * n.y;
        ATA[2] += n.x * n.z;
        ATA[3] += n.y * n.y;
        ATA[4] += n.y * n.z;
        ATA[5] += n.z * n.z;

        float b = math.dot(p, n);
        ATb.x += n.x * b;
        ATb.y += n.y * b;
        ATb.z += n.z * b;

        pointaccum.x += p.x;
        pointaccum.y += p.y;
        pointaccum.z += p.z;
        pointaccum.w += 1f;
    }

    public static float Solve(NativeArray<float> ATA, float4 ATb, float4 pointaccum, out float3 point)
    {
        float4 masspoint = pointaccum / pointaccum.w;
        SvdVmulSym(out float4 A_mp, ATA, masspoint);
        A_mp = ATb - A_mp;

        SvdSolveATAATb(ATA, A_mp, out float4 x);

        float error = CalcError(ATA, x, ATb);
        x += masspoint;

        point = x.xyz;
        return error;
    }

    public static void ClearMatTri(ref NativeArray<float> ATA)
    {
        for (int i = 0; i < 6; i++) ATA[i] = 0;
    }
}
