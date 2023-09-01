using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

using static IsoMeshStructs;

public struct ChunkBuilder : IJob
{
    public NativeArray<float> SignedDistanceField;
    public NativeArray<Vertex> TempVerticesArray;
    public NativeArray<int> TempIndicesArray;
    public NativeArray<int> IndexCacheArray;
    public NativeArray<int> MeshCountsArray;

    public int BufferSize;
    public int DataSize;

    public int3 ChunkMin;
    public int ChunkSize;
    public int CellSize;

    public void Execute()
    {
        BuildVolume();
        Triangulate(MeshCountsArray, TempIndicesArray, TempVerticesArray, IndexCacheArray);
    }

    private void BuildVolume()
    {
        int x, y, z;
        int3 maxIt = ChunkSize / CellSize;

        for (x = 0; x < maxIt.x + 1; x++)
            for (y = 0; y < maxIt.y + 1; y++)
                for (z = 0; z < maxIt.z + 1; z++)
                {
                    var idxPos = new int3(x, y, z);
                    float3 pos = idxPos * CellSize + ChunkMin;
                    float d = SurfaceSDF(pos);
                    SetVolumeData(idxPos, d);
                }
    }

    private float SurfaceSDF(float3 p)
    {
        return Noise.FBM_4(p * 0.01f) * 100f;
    }

    public void SetVolumeData(int3 p, float density)
    {
        int index = Utils.I3(p.x, p.y, p.z, DataSize, DataSize);
        if (index < 0 || index >= BufferSize) return;
        SignedDistanceField[index] = density;
    }

    public float GetVolumeData(int3 p)
    {
        int index = Utils.I3(p.x, p.y, p.z, DataSize, DataSize);
        if (index < 0 || index >= BufferSize) return default;
        return SignedDistanceField[index];
    }

    public void Triangulate(NativeArray<int> meshCounts, NativeArray<int> indexBuffer, NativeArray<Vertex> vertexBuffer, NativeArray<int> indexCache)
    {
        int a, b, i, j, k, m, iu, iv, du, dv;
        int mask, edgeMask, edgeCount, bufNo, cellIndex;
        int v0, v1, v2, v3;
        float d, s, g0, g1, t;
        float3 position, pos;
        float2 gridInfo;
        var vi = int3.zero;
        var v = float3.zero;
        var R = new int[3];
        var x = new int[3];
        var e = new int[2];
        var grid = new float[8];
        var cellPos = int3.zero;

        var cellDims = new int3(ChunkSize / CellSize);

        R[0] = 1;
        R[1] = DataSize + 1;
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
                    m += (x[0] + x[1] * (DataSize - 1) + 2 * x[1]);

                    mask = 0;

                    for (i = 0; i < 8; i++)
                    {
                        vi[0] = SurfaceNets.cubeVerts[i, 0] + cellPos[0];
                        vi[1] = SurfaceNets.cubeVerts[i, 1] + cellPos[1];
                        vi[2] = SurfaceNets.cubeVerts[i, 2] + cellPos[2];

                        d = GetVolumeData(vi);

                        grid[i] = d;
                        mask |= (d > 0) ? (1 << i) : 0;
                    }

                    if (mask == 0 || mask == 0xff)
                        continue;

                    edgeMask = SurfaceNets.edgeTable[mask];
                    edgeCount = 0;

                    position = float3.zero;

                    for (i = 0; i < 12; ++i)
                    {
                        if ((edgeMask & (1 << i)) == 0)
                            continue;

                        e[0] = SurfaceNets.cubeEdges[i << 1];
                        e[1] = SurfaceNets.cubeEdges[(i << 1) + 1];

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

                        pos = cellPos + v;
                        position += pos;
                        edgeCount++;
                    }

                    if (edgeCount == 0) continue;

                    s = 1f / edgeCount;
                    position = (position * s) * CellSize;
                    cellIndex = Utils.I3(x[0], x[1], x[2], DataSize, DataSize);
                    gridInfo = new float2(cellIndex, 0);

                    indexCache[m] = meshCounts[0];
                    vertexBuffer[meshCounts[0]++] = new Vertex(position, 0, gridInfo);

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
