using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

public static class Triangulator
{
    public static float Get(int3 p, NativeArray<float> SignedDistanceField, int dataSize, int bufferSize)
    {
        int index = Utils.I3(p.x, p.y, p.z, dataSize, dataSize);
        if (index < 0 || index >= bufferSize) return default;
        return SignedDistanceField[index];
    }

    public static void Triangulate(int chunkSize, int dataSize, int bufferSize, int cellSize, NativeArray<int> meshCounts, NativeArray<int> indexBuffer, NativeArray<float3> vertexBuffer, NativeArray<int> indexCache, NativeArray<float> SignedDistanceField)
    {
        int a, b, i, j, k, m, iu, iv, du, dv;
        int mask, edgeMask, edgeCount, bufNo;
        int v0, v1, v2, v3;
        float d, s, g0, g1, t;
        float3 position, p;
        var v = float3.zero;
        var cellPos = int3.zero;
        int3 cellDims = chunkSize;
        var vi = int3.zero;
        var R = int3.zero;
        var x = int3.zero;
        var e = int2.zero;
        var grid = new NativeArray<float>(8, Allocator.Temp);

        R[0] = 1;
        R[1] = dataSize + 1;
        R[2] = R[1] * R[1];

        for (x[2] = 0; x[2] < cellDims[2]; ++x[2])
        {
            for (x[1] = 0; x[1] < cellDims[1]; ++x[1])
            {
                for (x[0] = 0; x[0] < cellDims[0]; ++x[0])
                {
                    cellPos.x = x[0]; cellPos.y = x[1]; cellPos.z = x[2];

                    bufNo = x[2];
                    m = 1 + R[1] * (1 + bufNo * R[1]);
                    m += (x[0] + x[1] * (dataSize - 1) + 2 * x[1]);

                    mask = 0;

                    for (i = 0; i < 8; i++)
                    {
                        vi[0] = SurfaceNets.CUBE_VERTS[i][0] + cellPos[0];
                        vi[1] = SurfaceNets.CUBE_VERTS[i][1] + cellPos[1];
                        vi[2] = SurfaceNets.CUBE_VERTS[i][2] + cellPos[2];

                        d = Get(vi, SignedDistanceField, dataSize, bufferSize);

                        grid[i] = d;
                        mask |= (d > 0) ? (1 << i) : 0;
                    }

                    if (mask == 0 || mask == 0xff)
                        continue;

                    edgeMask = SurfaceNets.EDGE_TABLE[mask];
                    edgeCount = 0;

                    position = float3.zero;

                    for (i = 0; i < 12; ++i)
                    {
                        if ((edgeMask & (1 << i)) == 0)
                            continue;

                        e[0] = SurfaceNets.CUBE_EDGES[i << 1];
                        e[1] = SurfaceNets.CUBE_EDGES[(i << 1) + 1];

                        g0 = grid[e[0]];
                        g1 = grid[e[1]];
                        t = g0 - g1;

                        if (Mathf.Abs(t) > 1e-6)
                            t = g0 / t;
                        else continue;

                        for (j = 0, k = 1; j < 3; ++j, k <<= 1)
                        {
                            a = e[0] & k;
                            b = e[1] & k;
                            if (a != b)
                                v[j] = (a > 0) ? 1f - t : t;
                            else
                                v[j] = (a > 0) ? 1f : 0f;
                        }

                        p = cellPos + v;
                        position += p;
                        edgeCount++;
                    }

                    if (edgeCount == 0) continue;

                    s = 1f / edgeCount;
                    position = (position * s) * cellSize;

                    indexCache[m] = meshCounts[0];
                    vertexBuffer[meshCounts[0]++] = position;

                    for (i = 0; i < 3; ++i)
                    {
                        if ((edgeMask & (1 << i)) == 0)
                            continue;

                        iu = (i + 1) % 3;
                        iv = (i + 2) % 3;

                        if (x[iu] == 0 || x[iv] == 0)
                            continue;

                        du = R[iu];
                        dv = R[iv];

                        v0 = indexCache[m];
                        v1 = indexCache[m - du];
                        v2 = indexCache[m - dv];
                        v3 = indexCache[m - du - dv];

                        if ((mask & 1) > 0)
                        {
                            indexBuffer[meshCounts[1]++] = v0;
                            indexBuffer[meshCounts[1]++] = v3;
                            indexBuffer[meshCounts[1]++] = v1;

                            indexBuffer[meshCounts[1]++] = v0;
                            indexBuffer[meshCounts[1]++] = v2;
                            indexBuffer[meshCounts[1]++] = v3;
                        }
                        else
                        {
                            indexBuffer[meshCounts[1]++] = v0;
                            indexBuffer[meshCounts[1]++] = v3;
                            indexBuffer[meshCounts[1]++] = v2;

                            indexBuffer[meshCounts[1]++] = v0;
                            indexBuffer[meshCounts[1]++] = v1;
                            indexBuffer[meshCounts[1]++] = v3;
                        }
                    }
                }
            }
        }
    }
}
