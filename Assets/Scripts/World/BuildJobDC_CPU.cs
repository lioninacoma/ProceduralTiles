using System;
using System.Collections;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;
using UnityEngine;

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

        private static float OpSmoothUnion(float d1, float d2, float k)
        {
            float h = math.clamp(0.5f + 0.5f * (d2 - d1) / k, 0f, 1f);
            return math.lerp(d2, d1, h) - k * h * (1f - h);
        }

        private static float OpSmoothSubtraction(float d1, float d2, float k)
        {
            float h = math.clamp(0.5f - 0.5f * (d2 + d1) / k, 0f, 1f);
            return math.lerp(d2, -d1, h) + k * h * (1f - h);
        }

        float OpSmoothIntersection(float d1, float d2, float k)
        {
            float h = math.clamp(0.5f - 0.5f * (d2 - d1) / k, 0f, 1f);
            return math.lerp(d2, d1, h) + k * h * (1f - h);
        }

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
            return x.y - (Noise.FBM_4(new float3(x.x, 0, x.z) * 0.005f) * 100.0f + 20.0f);
        }

        private static float Map(float3 x)
        {
            float r = 60f;
            float3 p = new float3(60f, 30f, 60f);
            float d = SdBox(x - (new float3(r, 0, r) + p), r);
            float s = Surface(x);
            s = OpSmoothSubtraction(d, s, 8f);
            return s;
        }

        private float3 CalcNormal(float3 x)
        {
            const float eps = 0.0001f;
            float2 h = new float2(eps, 0);
            return math.normalize(new float3(Map(x + h.xyy) - Map(x - h.xyy),
                                             Map(x + h.yxy) - Map(x - h.yxy),
                                             Map(x + h.yyx) - Map(x - h.yyx)));
        }
        
        private float3 Raycast(float3 p0, float3 p1, float g0)
        {
            const int linearSearchSteps = 8;
            const int binarySearchSteps = 8;
            const float targetDist = .0001f;
            float step = 1f / linearSearchSteps;

            float3 p = float3.zero;
            float3 ro = (g0 < 0) ? p1 : p0;
            float3 rt = (g0 < 0) ? p0 : p1;
            float3 rd = rt - ro;
            float d;
            float t = step;

            // linear search
            for (int i = 0; i < linearSearchSteps; i++)
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
            for (int i = 0; i < binarySearchSteps; i++)
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
            float d;
            float3 position, p, n, p0, p1, vi;
            var cellPos = int3.zero;
            int3 cellDims = ChunkSize;
            var R = int3.zero;
            var x = int3.zero;
            var e = int2.zero;
            var grid = new NativeArray<float>(8, Allocator.Temp);
            var ATA = new NativeArray<float>(6, Allocator.Temp);
            float g0, g1, t;
            int j, k, a, b;
            
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
                        //float3 v = float3.zero;
                        float3 normal = float3.zero;

                        QefSolver.ClearMatTri(ref ATA);
                        var ATb = float4.zero;
                        var pointaccum = float4.zero;

                        for (i = 0; i < 12 && edgeCount < 6; ++i)
                        {
                            if ((edgeMask & (1 << i)) == 0)
                                continue;

                            e[0] = SurfaceNets.CUBE_EDGES[i << 1];
                            e[1] = SurfaceNets.CUBE_EDGES[(i << 1) + 1];

                            //g0 = grid[e[0]];
                            //g1 = grid[e[1]];
                            //t = g0 - g1;

                            //if (Mathf.Abs(t) > 1e-6)
                            //    t = g0 / t;
                            //else continue;

                            //for (j = 0, k = 1; j < 3; ++j, k <<= 1)
                            //{
                            //    a = e[0] & k;
                            //    b = e[1] & k;
                            //    if (a != b)
                            //        v[j] = (a > 0) ? 1f - t : t;
                            //    else
                            //        v[j] = (a > 0) ? 1f : 0f;
                            //}

                            //v += cellPos;
                            //v = v * CellSize + ChunkMin;

                            //n = math.normalize(math.lerp(CalcNormal(p0), CalcNormal(p1), t));
                            //QefSolver.Add(n, v, ref ATA, ref ATb, ref pointaccum);

                            p0 = SurfaceNets.CUBE_VERTS[e[0]] + cellPos;
                            p1 = SurfaceNets.CUBE_VERTS[e[1]] + cellPos;

                            p0 = p0 * CellSize + ChunkMin;
                            p1 = p1 * CellSize + ChunkMin;

                            p = Raycast(p0, p1, grid[e[0]]);
                            n = CalcNormal(p);

                            QefSolver.Add(n, p, ref ATA, ref ATb, ref pointaccum);

                            normal += n;
                            edgeCount++;
                        }

                        if (edgeCount == 0) continue;

                        QefSolver.Solve(ATA, ATb, pointaccum, out position);

                        normal *= (1f / edgeCount);
                        var masspoint = pointaccum.xyz / pointaccum.w;
                        var dotWithMassPoint = math.max(0, math.dot(normal, math.normalize(position - masspoint)));

                        // gradient descent with inflated position
                        position += normal * .1f * dotWithMassPoint;
                        for (j = 0; j < 16; j++)
                        {
                            d = Map(position);
                            if (math.abs(d) < 0.0001f) break;
                            position -= CalcNormal(position) * d;
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
