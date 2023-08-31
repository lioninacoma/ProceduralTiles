using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

using static IsoMeshStructs;

public class IsoSurface
{
    private float[] SignedDistanceField;
    private float[] SurfaceHeightField;
    private int BufferSize;
    public int DataSize;

    private int3 RootMin;
    private int RootSize;
    private int CellSize;

    public IsoSurface(int3 rootMin, int rootSize, int cellSize)
    {
        RootMin = rootMin;
        RootSize = rootSize;
        CellSize = cellSize;

        DataSize = (RootSize / CellSize) + 1;
        BufferSize = DataSize * DataSize * DataSize;

        SignedDistanceField = new float[BufferSize];
        SurfaceHeightField = new float[DataSize * DataSize];
    }

    public void BuildVolume()
    {
        int x, y, z;
        int3 maxIt = RootSize / CellSize;

        for (x = 0; x < maxIt.x + 1; x++)
            for (z = 0; z < maxIt.z + 1; z++)
            {
                var idxPos = new int3(x, 0, z);
                float3 pos = idxPos * CellSize + RootMin;
                SetSurfaceHeightField(idxPos, SurfaceHeightSDF(pos));
            }

        UpdateVolumeData();
    }

    public void UpdateVolumeData()
    {
        int x, y, z;
        int3 maxIt = RootSize / CellSize;

        for (x = 0; x < maxIt.x + 1; x++)
            for (y = 0; y < maxIt.y + 1; y++)
                for (z = 0; z < maxIt.z + 1; z++)
                {
                    var idxPos = new int3(x, y, z);
                    float d = SurfaceDistance(idxPos);
                    SetVolumeData(idxPos, d);
                }
    }

    public void SetSurfaceHeightField(int3 p, float distance)
    {
        int index = Utils.I2(p.x, p.z, DataSize);
        if (index < 0 || index >= DataSize * DataSize) return;
        SurfaceHeightField[index] = distance;
    }

    public float GetSurfaceHeightField(int3 p)
    {
        int index = Utils.I2(p.x, p.z, DataSize);
        if (index < 0 || index >= DataSize * DataSize) return 0f;
        return SurfaceHeightField[index];
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

    private static float SurfaceHeightSDF(float3 p)
    {
        p.y = 0;
        return Noise.FBM_4(p * 0.04f) * 10f + 5f;
        //return 10f;
    }

    public float SurfaceDistance(int3 p)
    {
        int index = Utils.I2(p.x, p.z, DataSize);
        if (index < 0 || index >= DataSize * DataSize) return 0f;
        float d = SurfaceHeightField[index];
        return CellSize * p.y - d;
    }

    public float3 SurfaceNormal(int3 p)
    {
        int2 h = new int2(1, 0);
        return math.normalize(new float3(
            SurfaceDistance(p + h.xyy) - SurfaceDistance(p - h.xyy),
            SurfaceDistance(p + h.yxy) - SurfaceDistance(p - h.yxy),
            SurfaceDistance(p + h.yyx) - SurfaceDistance(p - h.yyx)));
    }

    public void Triangulate(ref Counts counts, NativeArray<int> indexBuffer, NativeArray<Vertex> vertexBuffer, NativeArray<int> indexCache)
    {
        int a, b, i, j, k, m, iu, iv, du, dv;
        int mask, edgeMask, edgeCount, bufNo, cellIndex;
        int v0, v1, v2, v3;
        float s, g0, g1, t;
        float d, p0, p1;
        int3 nodeMin, cellPos;
        float3 position, normal;
        float2 gridInfo;
        var vi = int3.zero;
        var R = new int[3];
        var x = new int[3];
        var e = new int[2];
        var grid = new float[8];
        var idxPos = int3.zero;

        var cellDims = new int3(RootSize / CellSize);
        var cellMin = RootMin / CellSize;

        R[0] = 1;
        R[1] = DataSize + 1;

        for (x[2] = 0; x[2] < cellDims[2]; ++x[2])
        {
            for (x[1] = 0; x[1] < cellDims[1]; ++x[1])
            {
                for (x[0] = 0; x[0] < cellDims[0]; ++x[0])
                {
                    idxPos.x = x[0]; idxPos.y = x[1]; idxPos.z = x[2];

                    bufNo = (x[2] % 2 == 1) ? 0 : 1;
                    R[2] = R[1] * R[1] * (bufNo * 2 - 1);
                    m = 1 + R[1] * (1 + bufNo * R[1]);
                    m += (x[0] + x[1] * (DataSize - 1) + 2 * x[1]);

                    mask = 0;

                    for (i = 0; i < 8; i++)
                    {
                        vi[0] = SurfaceNets.cubeVerts[i, 0] + idxPos[0];
                        vi[1] = SurfaceNets.cubeVerts[i, 1] + idxPos[1];
                        vi[2] = SurfaceNets.cubeVerts[i, 2] + idxPos[2];

                        d = GetVolumeData(vi);

                        grid[i] = d;
                        mask |= (d > 0) ? (1 << i) : 0;
                    }

                    if (mask == 0 || mask == 0xff)
                        continue;

                    edgeMask = SurfaceNets.edgeTable[mask];
                    edgeCount = 0;

                    position = float3.zero;
                    normal = float3.zero;
                    var v = float3.zero;
                                        
                    nodeMin = idxPos * CellSize + RootMin; 
                    //cellPos = idxPos + cellMin;

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
                                v[j] = a > 0 ? 1f - t : t;
                            else
                                v[j] = a > 0 ? 1f : 0;
                        }

                        position += (nodeMin + v);
                        edgeCount++;
                    }

                    if (edgeCount == 0) continue;

                    s = 1f / edgeCount;

                    //position = nodeMin;
                    position *= s;
                    normal = math.normalize(s * normal); 
                    cellIndex = Utils.I3(x[0], x[1], x[2], DataSize, DataSize);
                    gridInfo = new float2(cellIndex, 0);

                    indexCache[m] = counts.VertexCount;
                    vertexBuffer[counts.VertexCount++] = new Vertex(position, normal, gridInfo);

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
                            indexBuffer[counts.IndexCount++] = v0;
                            indexBuffer[counts.IndexCount++] = v3;
                            indexBuffer[counts.IndexCount++] = v1;

                            indexBuffer[counts.IndexCount++] = v0;
                            indexBuffer[counts.IndexCount++] = v2;
                            indexBuffer[counts.IndexCount++] = v3;
                        }
                        else
                        {
                            indexBuffer[counts.IndexCount++] = v0;
                            indexBuffer[counts.IndexCount++] = v3;
                            indexBuffer[counts.IndexCount++] = v2;

                            indexBuffer[counts.IndexCount++] = v0;
                            indexBuffer[counts.IndexCount++] = v1;
                            indexBuffer[counts.IndexCount++] = v3;
                        }
                    }
                }
            }
        }
    }

}
