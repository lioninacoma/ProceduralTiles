using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Unity.Burst;

namespace ChunkBuilder
{
    [BurstCompile(FloatPrecision.High, FloatMode.Default)]
    public struct BuildJob_CPU : IJob
    {
        [WriteOnly] public NativeArray<float3> VertexBuffer;
        [WriteOnly] public NativeArray<int> IndexBuffer;

        public NativeArray<float> SignedDistanceField;
        public NativeArray<int> IndexCache;
        public NativeArray<int> MeshCounts;

        [ReadOnly] public int BufferSize;
        [ReadOnly] public int DataSize;

        [ReadOnly] public int3 ChunkMin;
        [ReadOnly] public int ChunkSize;
        [ReadOnly] public int CellSize;
        [ReadOnly] public int ChunkIndex;
        [ReadOnly] public bool InitSDF;

        public void Execute()
        {
            if (InitSDF)
            {
                InitVolume();
            }
            
            Triangulate();
        }

        private void InitVolume()
        {
            int x, y, z;
            int3 maxIt = ChunkSize;

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

        private float SurfaceSDF(float3 x)
        {
            return x.y - (Noise.FBM_4(new float3(x.x, 0, x.z) * 0.006f) * 80.0f + 20.0f);
        }

        private void SetVolumeData(int3 p, float density)
        {
            int index = Utils.I3(p.x, p.y, p.z, DataSize, DataSize);
            if (index < 0 || index >= BufferSize) return;
            SignedDistanceField[index] = density;
        }

        private float GetVolumeData(int3 p)
        {
            int index = Utils.I3(p.x, p.y, p.z, DataSize, DataSize);
            if (index < 0 || index >= BufferSize) return default;
            return SignedDistanceField[index];
        }

        private void TriangulateCell(int3 cellPos, NativeArray<float> grid, bool placeCentroid)
        {
            int a, b, i, j, k, m, iu, iv, du, dv;
            int mask, edgeMask, edgeCount, bufNo;
            int v0, v1, v2, v3;

            float d, s, g0, g1, t;

            float3 p, v = float3.zero;
            float3 position = float3.zero;

            int3 vi, cellMin, cellMax;
            int3 R = int3.zero;
            int2 e = int2.zero;

            R[0] = 1;
            R[1] = DataSize + 1;
            R[2] = R[1] * R[1];

            bufNo = cellPos[2];
            m = 1 + R[1] * (1 + bufNo * R[1]);
            m += (cellPos[0] + cellPos[1] * (DataSize - 1) + 2 * cellPos[1]);

            mask = 0;

            for (i = 0; i < 8; i++)
            {
                vi = SurfaceNets.CUBE_VERTS[i] + cellPos;
                d = GetVolumeData(vi);

                grid[i] = d;
                mask |= (d > 0) ? (1 << i) : 0;
            }

            if (mask == 0 || mask == 0xff)
                return;

            edgeMask = SurfaceNets.EDGE_TABLE[mask];
            edgeCount = 0;

            for (i = 0; i < 12 && edgeCount < 6 && !placeCentroid; ++i)
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

            if (edgeCount == 0 && !placeCentroid) 
                return;

            cellMin = cellPos * CellSize;
            cellMax = cellMin + CellSize;

            if (placeCentroid)
            {
                position = (cellMin + cellMax) / 2;
            }
            else
            {
                s = 1f / edgeCount;
                position = (position * s) * CellSize;
            }

            IndexCache[m] = MeshCounts[0];
            VertexBuffer[MeshCounts[0]++] = position;

            for (i = 0; i < 3; ++i)
            {
                if ((edgeMask & (1 << i)) == 0)
                    continue;

                iu = (i + 1) % 3;
                iv = (i + 2) % 3;

                if (cellPos[iu] == 0 || cellPos[iv] == 0)
                    continue;

                du = R[iu];
                dv = R[iv];

                v0 = IndexCache[m];
                v1 = IndexCache[m - du];
                v2 = IndexCache[m - dv];
                v3 = IndexCache[m - du - dv];

                if ((mask & 1) > 0)
                {
                    IndexBuffer[MeshCounts[1]++] = v0;
                    IndexBuffer[MeshCounts[1]++] = v3;
                    IndexBuffer[MeshCounts[1]++] = v1;

                    IndexBuffer[MeshCounts[1]++] = v0;
                    IndexBuffer[MeshCounts[1]++] = v2;
                    IndexBuffer[MeshCounts[1]++] = v3;
                }
                else
                {
                    IndexBuffer[MeshCounts[1]++] = v0;
                    IndexBuffer[MeshCounts[1]++] = v3;
                    IndexBuffer[MeshCounts[1]++] = v2;

                    IndexBuffer[MeshCounts[1]++] = v0;
                    IndexBuffer[MeshCounts[1]++] = v1;
                    IndexBuffer[MeshCounts[1]++] = v3;
                }
            }
        }

        private void Triangulate()
        {
            var grid = new NativeArray<float>(8, Allocator.Temp);

            int3 cellPos = int3.zero;
            int3 cellDims = ChunkSize;

            for (cellPos[2] = 0; cellPos[2] < cellDims[2]; ++cellPos[2])
            {
                for (cellPos[1] = 0; cellPos[1] < cellDims[1]; ++cellPos[1])
                {
                    for (cellPos[0] = 0; cellPos[0] < cellDims[0]; ++cellPos[0])
                    {
                        TriangulateCell(cellPos, grid, false);
                    }
                }
            }
        }
    }
}
