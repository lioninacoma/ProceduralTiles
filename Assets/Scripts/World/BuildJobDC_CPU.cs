using System;
using System.Collections;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;

namespace ChunkBuilder
{
    [BurstCompile(FloatPrecision.High, FloatMode.Default)]
    public struct BuildJobDC_CPU : IJob
    {
        [WriteOnly] public NativeArray<float3> TempVerticesArray;
        [WriteOnly] public NativeArray<int> TempIndicesArray;

        public NativeArray<int> IndexCacheArray;
        public NativeArray<int> MeshCountsArray;

        [ReadOnly] public int BufferSize;
        [ReadOnly] public int DataSize;

        [ReadOnly] public int3 ChunkMin;
        [ReadOnly] public int ChunkSize;
        [ReadOnly] public int CellSize;
        [ReadOnly] public int ChunkIndex;

        public void Execute()
        {
            Triangulate(MeshCountsArray, TempIndicesArray, TempVerticesArray, IndexCacheArray);
        }

        private static float OpUnion(float d1, float d2) { return math.min(d1, d2); }

        private static float OpSubtraction(float d1, float d2) { return math.max(-d1, d2); }

        private static float OpIntersection(float d1, float d2) { return math.max(d1, d2); }

        private static float SdSphere(float3 p, float s)
        {
            return math.length(p) - s;
        }

        private static float SdBox(float3 p, float3 b)
        {
            float3 q = math.abs(p) - b;
            return math.length(math.max(q, 0.0f)) + math.min(math.max(q.x, math.max(q.y, q.z)), 0.0f);
        }

        private static float Surface(float3 x)
        {
            return x.y - (Noise.FBM_4(new float3(x.x, 0, x.z) * 0.01f) * 30.0f + 10.0f);
        }

        private static float Map(float3 x)
        {
            float d0 = SdSphere(x - new float3(100f, 24f, 120f), 9.999f);
            float d1 = SdSphere(x - new float3(108f, 24f, 120f), 4.999f);
            float d2 = SdSphere(x - new float3(116f, 24f, 120f), 4.999f);
            float d3 = SdSphere(x - new float3(124f, 24f, 120f), 3.999f);
            float s = Surface(x);
            s = OpSubtraction(d0, s);
            s = OpUnion(d1, s);
            s = OpSubtraction(d2, s);
            s = OpSubtraction(d3, s);
            return s;
        }

        private float3 CalcNormal(float3 x)
        {
            const float eps = 0.01f;
            float2 h = new float2(eps, 0);
            return math.normalize(new float3(Map(x + h.xyy) - Map(x - h.xyy),
                                             Map(x + h.yxy) - Map(x - h.yxy),
                                             Map(x + h.yyx) - Map(x - h.yyx)));
        }
        
        private float3 Raycast(float3 p0, float3 p1, float g0)
        {
            const int linearSeachSteps = 4;
            const int binarySeachSteps = 8;
            const float targetDist = .001f;
            float step = 1f / linearSeachSteps;

            float3 p = float3.zero;
            float3 ro = (g0 < 0) ? p1 : p0;
            float3 rt = (g0 < 0) ? p0 : p1;
            float3 rd = rt - ro;
            float d;
            float t = 0f;

            // linear search
            for (int i = 0; i < linearSeachSteps; i++)
            {
                p = ro + rd * t;
                d = Map(p);

                if (d < 0)
                {
                    break;
                }

                t += step;
            }

            // binary search
            for (int i = 0; i < binarySeachSteps; i++)
            {
                p = ro + rd * t;
                d = Map(p);

                if (math.abs(d) < targetDist)
                {
                    return p;
                }

                step *= 0.5f;
                t += step * ((d > 0) ? 1 : 0);
                t -= step * ((d < 0) ? 1 : 0);
            }

            return p;
        }

        private void Triangulate(NativeArray<int> meshCounts, NativeArray<int> indexBuffer, NativeArray<float3> vertexBuffer, NativeArray<int> indexCache)
        {
            int i, m, iu, iv, du, dv;
            int mask, edgeMask, edgeCount, bufNo;
            int v0, v1, v2, v3;
            float d, s;
            float3 position, normal, p, n, p0, p1, vi;
            var cellPos = int3.zero;
            int3 cellDims = ChunkSize;
            var R = int3.zero;
            var x = int3.zero;
            var e = int2.zero;
            var grid = new NativeArray<float>(8, Allocator.Temp);
            var ATA = new NativeArray<float>(6, Allocator.Temp);

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
                            vi = SurfaceNets.CUBE_VERTS[i] + cellPos;
                            d = Map(vi * CellSize + ChunkMin);
                            grid[i] = d;
                            mask |= (d > 0) ? (1 << i) : 0;
                        }

                        if (mask == 0 || mask == 0xff)
                            continue;

                        edgeMask = SurfaceNets.EDGE_TABLE[mask];
                        edgeCount = 0;

                        //position = float3.zero;
                        //normal = float3.zero;

                        QefSolver.ClearMatTri(ref ATA);
                        var ATb = float4.zero;
                        var pointaccum = float4.zero;

                        for (i = 0; i < 12; ++i)
                        {
                            if ((edgeMask & (1 << i)) == 0)
                                continue;

                            e[0] = SurfaceNets.CUBE_EDGES[i << 1];
                            e[1] = SurfaceNets.CUBE_EDGES[(i << 1) + 1];

                            p0 = SurfaceNets.CUBE_VERTS[e[0]] + cellPos;
                            p1 = SurfaceNets.CUBE_VERTS[e[1]] + cellPos;

                            p0 = p0 * CellSize + ChunkMin;
                            p1 = p1 * CellSize + ChunkMin;

                            p = Raycast(p0, p1, grid[e[0]]);
                            n = CalcNormal(p);

                            QefSolver.Add(n, p, ref ATA, ref ATb, ref pointaccum);

                            //position += p;
                            //normal += n;

                            edgeCount++;
                        }

                        if (edgeCount == 0) continue;

                        //s = 1f / edgeCount;
                        //position *= s;
                        //normal *= s;

                        QefSolver.Solve(ATA, ATb, pointaccum, out float3 positionQef);
                        //positionQef = ((positionQef + cellPos) * CellSize) + ChunkMin;

                        //const float tl = .5f;
                        //float3 min = (cellPos * CellSize) + ChunkMin;
                        //float3 max = min + CellSize;
                        //if (positionQef.x < min.x - tl || positionQef.x > max.x + tl ||
                        //    positionQef.y < min.y - tl || positionQef.y > max.y + tl ||
                        //    positionQef.z < min.z - tl || positionQef.z > max.z + tl)
                        //{
                        //    // NOP
                        //}
                        //else
                        {
                            position = positionQef;
                        }

                        indexCache[m] = meshCounts[0];
                        vertexBuffer[meshCounts[0]++] = position - ChunkMin;

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
}
